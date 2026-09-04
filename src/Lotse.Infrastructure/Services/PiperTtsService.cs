using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Services;

/// <summary>Which of the installed voices to speak with. The male one is the default and the better model.</summary>
public enum SpeechVoice
{
    Male,
    Female,
}

/// <summary>Where piper.exe and its voices live, plus the folder the rendered WAVs are cached in.</summary>
public sealed record PiperTtsOptions(string PiperPath, string VoicePath, string CacheDirectory, string? VoiceFemalePath = null)
{
    /// <summary>Default install location written by <c>tools/install-piper.ps1</c>.</summary>
    public static PiperTtsOptions Defaults(string cacheDirectory)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotse");
        return new PiperTtsOptions(
            Path.Combine(root, "piper", "piper.exe"),
            Path.Combine(root, "voices", "de_DE-thorsten-medium.onnx"),
            cacheDirectory,
            Path.Combine(root, "voices", "de_DE-kerstin-low.onnx"));
    }
}

/// <summary>
/// Renders German speech. The browser's own voices stay as the fallback, so an implementation that reports
/// <see cref="Available"/> = false is a normal state, not an error.
/// </summary>
public interface ITextToSpeech
{
    /// <summary>True once the engine and at least the default voice are actually present on this machine.</summary>
    bool Available { get; }

    /// <summary>
    /// Renders <paramref name="text"/> and returns the cache key of the WAV, or null when nothing could be
    /// rendered (engine missing, empty text, failure). The caller then falls back to browser speech.
    /// A voice that is not installed falls back to the default one rather than failing.
    /// </summary>
    Task<string?> SynthesizeAsync(string text, double rate = 1.0, SpeechVoice voice = SpeechVoice.Male, CancellationToken ct = default);

    /// <summary>Absolute path of a previously rendered WAV, or null when <paramref name="key"/> is unknown.</summary>
    string? ResolveCached(string key);
}

/// <summary>
/// Local neural text-to-speech via Piper (https://github.com/rhasspy/piper), MIT licensed, voices de_DE-thorsten
/// and de_DE-kerstin (CC0). It runs as a short-lived child process and never talks to the network, which is why it
/// is preferred over the browser's online voices: the same near-native pronunciation without sending lesson text to
/// a third party.
///
/// Rendering the same sentence again is common (a learner replays a listening task), so every result is cached on
/// disk under a hash of voice + speed + text and reused. Cache entries are plain WAV files; deleting the folder is
/// the supported way to reset it.
/// </summary>
public sealed class PiperTtsService : ITextToSpeech
{
    // Piper reads one utterance per line from stdin, so the text has to arrive as a single line.
    private static readonly char[] LineBreaks = ['\r', '\n'];

    // One process at a time. Piper is CPU-bound and a single learner triggers at most a couple of sentences at
    // once; letting a page full of "Vorlesen" buttons spawn a process each would just thrash the machine.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PiperTtsOptions _options;
    private readonly ILogger<PiperTtsService> _log;
    private readonly Dictionary<SpeechVoice, string> _voices = [];

    public PiperTtsService(PiperTtsOptions options, ILogger<PiperTtsService> log)
    {
        _options = options;
        _log = log;

        if (File.Exists(options.VoicePath)) _voices[SpeechVoice.Male] = options.VoicePath;
        if (options.VoiceFemalePath is { Length: > 0 } f && File.Exists(f)) _voices[SpeechVoice.Female] = f;

        Available = File.Exists(options.PiperPath) && _voices.ContainsKey(SpeechVoice.Male);
        if (Available)
        {
            Directory.CreateDirectory(options.CacheDirectory);
            _log.LogInformation("Lokale Sprachausgabe aktiv: {Voices} ({Piper})",
                string.Join(", ", _voices.Values.Select(Path.GetFileNameWithoutExtension)), options.PiperPath);
            if (!_voices.ContainsKey(SpeechVoice.Female))
                _log.LogInformation("Keine Frauenstimme installiert - alle Rollen sprechen mit der Standardstimme. Nachruesten mit tools/install-piper.ps1.");
        }
        else
        {
            _log.LogInformation(
                "Lokale Sprachausgabe nicht eingerichtet (erwartet: {Piper} + {Voice}) - es werden die Browser-Stimmen benutzt. Einrichten mit tools/install-piper.ps1.",
                options.PiperPath, options.VoicePath);
        }
    }

    public bool Available { get; }

    public string? ResolveCached(string key)
    {
        // The key reaches this method straight from a URL segment, so anything but a plain hash is refused
        // rather than combined into a path.
        if (!IsCacheKey(key)) return null;
        var path = Path.Combine(_options.CacheDirectory, key + ".wav");
        return File.Exists(path) ? path : null;
    }

    public async Task<string?> SynthesizeAsync(string text, double rate = 1.0, SpeechVoice voice = SpeechVoice.Male, CancellationToken ct = default)
    {
        if (!Available) return null;
        var line = Flatten(text);
        if (line.Length == 0) return null;

        // An uninstalled voice speaks with the default one - a missing model is a reason to sound less varied,
        // not a reason to go silent.
        var voicePath = _voices.TryGetValue(voice, out var p) ? p : _voices[SpeechVoice.Male];

        // Piper's length_scale is duration, so it moves opposite to the browser's rate: 0.75x speed = 1.33x length.
        var lengthScale = Math.Clamp(1.0 / (rate <= 0 ? 1.0 : rate), 0.5, 2.5);
        var key = CacheKey(voicePath, line, lengthScale);
        var target = Path.Combine(_options.CacheDirectory, key + ".wav");
        if (File.Exists(target)) return key;

        await _gate.WaitAsync(ct);
        try
        {
            // Another caller may have rendered the very same sentence while we waited for the gate.
            if (File.Exists(target)) return key;
            return await RenderAsync(voicePath, line, lengthScale, key, target, ct) ? key : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> RenderAsync(string voicePath, string line, double lengthScale, string key, string target, CancellationToken ct)
    {
        // Render to a temp file first: a cancelled or crashed run must not leave a truncated WAV behind that
        // every later request would then happily serve from the cache.
        var temp = target + ".partial";
        var psi = new ProcessStartInfo(_options.PiperPath)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_options.PiperPath)!,
            // Piper expects UTF-8 on stdin; without this the default console encoding mangles umlauts and "ć".
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(voicePath);
        psi.ArgumentList.Add("--output_file");
        psi.ArgumentList.Add(temp);
        psi.ArgumentList.Add("--length_scale");
        psi.ArgumentList.Add(lengthScale.ToString("0.###", CultureInfo.InvariantCulture));

        try
        {
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start lieferte null.");
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            await proc.StandardInput.WriteLineAsync(line);
            proc.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                throw;
            }

            if (proc.ExitCode != 0 || !File.Exists(temp) || new FileInfo(temp).Length < 1024)
            {
                _log.LogWarning("Piper lieferte kein Audio (ExitCode {Code}): {Fehler}", proc.ExitCode, (await stderr).Trim());
                TryDelete(temp);
                return false;
            }

            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            throw;
        }
        catch (Exception ex)
        {
            // A broken TTS must never take a lesson down - the caller falls back to the browser voices.
            _log.LogWarning(ex, "Lokale Sprachausgabe fehlgeschlagen ({Key}); es wird auf die Browser-Stimme zurueckgefallen.", key);
            TryDelete(temp);
            return false;
        }
    }

    private static string CacheKey(string voicePath, string line, double lengthScale)
    {
        var material = $"{Path.GetFileNameWithoutExtension(voicePath)}|{lengthScale.ToString("0.###", CultureInfo.InvariantCulture)}|{line}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static bool IsCacheKey(string key)
        => key.Length == 64 && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Flatten(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var parts = text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', parts).Trim();
    }

    private static void TryKill(Process proc)
    {
        try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
