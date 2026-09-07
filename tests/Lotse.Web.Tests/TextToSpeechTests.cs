using Lotse.Infrastructure.Services;
using Lotse.Web.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;

namespace Lotse.Web.Tests;

/// <summary>An engine that is simply not installed - the state on CI and on any machine without install-piper.ps1.</summary>
internal sealed class NoTextToSpeech : ITextToSpeech
{
    public bool Available => false;
    public Task<string?> SynthesizeAsync(string text, double rate = 1.0, SpeechVoice voice = SpeechVoice.Male, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
    public string? ResolveCached(string key) => null;
}

/// <summary>An engine that always renders, so the fallback logic in SpeechService can be exercised without Piper.</summary>
internal sealed class FakeTextToSpeech(string key = "abc") : ITextToSpeech
{
    public bool Available => true;
    public string? LastText { get; private set; }
    public double LastRate { get; private set; }
    public SpeechVoice LastVoice { get; private set; }

    public Task<string?> SynthesizeAsync(string text, double rate = 1.0, SpeechVoice voice = SpeechVoice.Male, CancellationToken ct = default)
    {
        LastText = text;
        LastRate = rate;
        LastVoice = voice;
        return Task.FromResult<string?>(key);
    }

    public string? ResolveCached(string k) => k == key ? "x.wav" : null;
}

/// <summary>The speaker label decides the voice; getting it wrong makes Herr Krüger sound like Sabine.</summary>
public class SpeakerVoicesTests
{
    [Theory]
    [InlineData("Herr Krüger", SpeechVoice.Male)]
    [InlineData("Herr Krüger (Mail)", SpeechVoice.Male)]        // the aside must not defeat the match
    [InlineData("Frau Kaya", SpeechVoice.Female)]
    [InlineData("Frau Ahrens (im Gespräch)", SpeechVoice.Female)]
    [InlineData("Sabine", SpeechVoice.Female)]
    [InlineData("Sabine (im Stand-up)", SpeechVoice.Female)]
    [InlineData("Sabine Berger", SpeechVoice.Female)]           // matched on the first name
    [InlineData("Lena (eine Woche später)", SpeechVoice.Female)]
    [InlineData("Ana (draußen)", SpeechVoice.Female)]
    [InlineData("Prüferin", SpeechVoice.Female)]
    [InlineData("Jonas", SpeechVoice.Male)]
    [InlineData("Tarek (Nachricht)", SpeechVoice.Male)]
    [InlineData("Du (Mail)", SpeechVoice.Male)]
    public void Known_speakers_get_their_own_voice(string speaker, SpeechVoice expected)
        => Assert.Equal(expected, SpeakerVoices.For(speaker));

    [Theory]
    [InlineData("IT-Hotline")]      // institutions have no gender to get right
    [InlineData("Radiobeitrag")]
    [InlineData("Dr. Hartmann")]    // the lessons never say, so we do not guess
    [InlineData("Wer-auch-immer")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_speakers_keep_the_default_voice(string? speaker)
        => Assert.Equal(SpeechVoice.Male, SpeakerVoices.For(speaker));
}

public class PiperTtsServiceTests
{
    private static PiperTtsService Missing() => new(
        new PiperTtsOptions(Path.Combine(Path.GetTempPath(), "gibt-es-nicht.exe"), Path.Combine(Path.GetTempPath(), "keine-stimme.onnx"), Path.GetTempPath()),
        NullLogger<PiperTtsService>.Instance);

    [Fact]
    public void Not_installed_reports_unavailable_instead_of_throwing()
        => Assert.False(Missing().Available);

    [Fact]
    public async Task Not_installed_renders_nothing_so_the_caller_falls_back()
        => Assert.Null(await Missing().SynthesizeAsync("Guten Tag."));

    [Theory]
    [InlineData("")]
    [InlineData("nicht-hex")]
    [InlineData("../../../appsettings.json")]
    [InlineData("..\\..\\data\\lotse.db")]
    [InlineData("ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]   // uppercase is not a key we mint
    [InlineData("abcdef")]                                                             // too short
    public void ResolveCached_refuses_anything_that_is_not_one_of_our_hashes(string key)
        => Assert.Null(Missing().ResolveCached(key));

    // ---- The rest only runs where install-piper.ps1 has actually been run -------------------------------------
    private static PiperTtsOptions RealOptions(string cacheDir)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotse");
        return new PiperTtsOptions(
            Path.Combine(root, "piper", "piper.exe"),
            Path.Combine(root, "voices", "de_DE-thorsten-medium.onnx"),
            cacheDir,
            Path.Combine(root, "voices", "de_DE-kerstin-low.onnx"));
    }

    private static PiperTtsService? Installed(out string cacheDir)
    {
        cacheDir = Path.Combine(Path.GetTempPath(), "lotse-tts-tests", Guid.NewGuid().ToString("N"));
        var options = RealOptions(cacheDir);
        if (!File.Exists(options.PiperPath) || !File.Exists(options.VoicePath)) return null;
        return new PiperTtsService(options, NullLogger<PiperTtsService>.Instance);
    }

    private static bool FemaleVoiceInstalled => File.Exists(RealOptions("").VoiceFemalePath!);

    [Fact]
    public async Task Two_voices_never_share_a_cache_entry()
    {
        var piper = Installed(out var cacheDir);
        Assert.SkipWhen(piper is null, "Piper ist auf dieser Maschine nicht eingerichtet (tools/install-piper.ps1).");
        Assert.SkipWhen(!FemaleVoiceInstalled, "Die Frauenstimme de_DE-kerstin-low ist nicht installiert.");

        try
        {
            const string satz = "Guten Morgen, ich habe eine Frage zum Termin.";
            var maennlich = await piper!.SynthesizeAsync(satz, 1.0, SpeechVoice.Male);
            var weiblich = await piper.SynthesizeAsync(satz, 1.0, SpeechVoice.Female);

            // Would Sabine be served Thorsten's cached WAV, the whole per-character mapping would be pointless.
            Assert.NotNull(maennlich);
            Assert.NotNull(weiblich);
            Assert.NotEqual(maennlich, weiblich);
            Assert.Equal(2, Directory.GetFiles(cacheDir, "*.wav").Length);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task An_uninstalled_voice_falls_back_to_the_default_one_instead_of_going_silent()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "lotse-tts-tests", Guid.NewGuid().ToString("N"));
        var options = RealOptions(cacheDir) with { VoiceFemalePath = Path.Combine(Path.GetTempPath(), "gibt-es-nicht.onnx") };
        Assert.SkipWhen(!File.Exists(options.PiperPath), "Piper ist auf dieser Maschine nicht eingerichtet (tools/install-piper.ps1).");

        var piper = new PiperTtsService(options, NullLogger<PiperTtsService>.Instance);
        try
        {
            const string satz = "Guten Morgen.";
            Assert.Equal(
                await piper.SynthesizeAsync(satz, 1.0, SpeechVoice.Male),
                await piper.SynthesizeAsync(satz, 1.0, SpeechVoice.Female));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task Renders_german_with_umlauts_and_reuses_the_cache_on_the_second_call()
    {
        var piper = Installed(out var cacheDir);
        Assert.SkipWhen(piper is null, "Piper ist auf dieser Maschine nicht eingerichtet (tools/install-piper.ps1).");

        try
        {
            // Umlauts and "ć" are the point: they only survive if stdin is written as UTF-8.
            const string satz = "Sehr geehrter Herr Petrović, Donnerstag um zehn Uhr passt. Viele Grüße, Thomas Krüger.";
            var key = await piper!.SynthesizeAsync(satz);

            Assert.NotNull(key);
            var path = piper.ResolveCached(key!);
            Assert.NotNull(path);
            Assert.True(new FileInfo(path!).Length > 10_000, "Die WAV ist zu klein, um gesprochener Text zu sein.");
            Assert.Empty(Directory.GetFiles(cacheDir, "*.partial"));

            // Same text again -> same key, and no second WAV in the cache.
            Assert.Equal(key, await piper.SynthesizeAsync(satz));
            Assert.Single(Directory.GetFiles(cacheDir, "*.wav"));

            // A different speed is a different rendering, so it must not collide with the first one.
            Assert.NotEqual(key, await piper.SynthesizeAsync(satz, 0.75));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task Blank_text_is_never_sent_to_the_engine()
    {
        var piper = Installed(out var cacheDir);
        Assert.SkipWhen(piper is null, "Piper ist auf dieser Maschine nicht eingerichtet (tools/install-piper.ps1).");
        try
        {
            Assert.Null(await piper!.SynthesizeAsync("   \r\n  "));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }
}

/// <summary>
/// The three outcomes of playAudio() decide whether SpeechService may still use a browser voice. Getting
/// "stopped" wrong would make the app start talking again right after the learner silenced it.
/// </summary>
public class SpeechServiceFallbackTests : BunitContext
{
    private SpeechService Build(ITextToSpeech tts)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/speech.js").Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(tts);
        Services.AddScoped<SpeechService>();
        return Services.GetRequiredService<SpeechService>();
    }

    [Fact]
    public async Task Without_a_local_engine_it_speaks_through_the_browser()
    {
        var speech = Build(new NoTextToSpeech());
        await speech.SpeakAsync("Guten Morgen.");

        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "speak");
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "playAudio");
    }

    [Fact]
    public async Task A_finished_recording_does_not_also_trigger_the_browser_voice()
    {
        var speech = Build(new FakeTextToSpeech("k1"));
        JSInterop.SetupModule("./js/speech.js").Setup<string>("playAudio", _ => true).SetResult("ended");

        Assert.True(await speech.SpeakAsync("Guten Morgen."));
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "speak");
    }

    [Fact]
    public async Task A_stopped_recording_stays_silent_instead_of_falling_back()
    {
        var speech = Build(new FakeTextToSpeech("k1"));
        JSInterop.SetupModule("./js/speech.js").Setup<string>("playAudio", _ => true).SetResult("stopped");

        Assert.False(await speech.SpeakAsync("Guten Morgen."));
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "speak");
    }

    [Fact]
    public async Task A_failed_recording_falls_back_to_the_browser_voice()
    {
        var speech = Build(new FakeTextToSpeech("k1"));
        JSInterop.SetupModule("./js/speech.js").Setup<string>("playAudio", _ => true).SetResult("failed");

        await speech.SpeakAsync("Guten Morgen.");
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "speak");
    }
}
