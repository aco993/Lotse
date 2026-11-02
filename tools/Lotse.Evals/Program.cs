// Lotse evaluation harness.
//
//   dotnet run --project tools/Lotse.Evals                         rule-based baseline and "always B" (no key needed)
//   dotnet run --project tools/Lotse.Evals -- --preset groq        + the AI tutor; key from the preset's variable (GROQ_API_KEY)
//   ... --model openai/gpt-oss-20b --hints 40 --write
//
// --write stores each run in evals/results/ and regenerates the results section of docs/EVALS.md from the newest
// run of every rater. Without it nothing on disk changes.
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lotse.Core.Evals;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Ai;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Evals;
using Microsoft.Extensions.Logging.Abstractions;

Console.OutputEncoding = Encoding.UTF8;
var options = Args.Parse(args);
var root = FindRepoRoot();
var contentDir = Path.Combine(root, "content");
var catalog = ContentLoader.Load(contentDir);
var telc = TelcContentLoader.Load(contentDir, catalog);
var set = GoldenSet.Load(Path.Combine(root, "evals", "golden", "telc-schreiben.json"));
var problems = set.Validate(telc, catalog.ErrorTypes.Select(e => e.Code).ToHashSet(StringComparer.Ordinal));
if (problems.Count > 0)
{
    Console.Error.WriteLine("Goldener Satz ungültig:\n" + string.Join("\n", problems));
    return 2;
}

var now = DateTime.UtcNow;
var progress = new Progress<string>(line => Console.WriteLine("  " + line));
var runs = new List<RaterRun>
{
    await EvalHarness.RunAsync("Rule-based (no AI)", set, EvalHarness.Rules(telc, Lotse.Core.Engine.Lexicon.FromCatalog(catalog)), now),
    await EvalHarness.RunAsync("Always B", set, EvalHarness.AlwaysB(), now),
};
EvalHarness.HintRun? hints = null;

if (options.Preset is { } presetId)
{
    var settings = TutorSettings.ForPreset(presetId);
    if (options.Model is { } model) settings = settings with { Model = model };
    var tutorOptions = settings.ToOptions(Environment.GetEnvironmentVariable);
    if (settings.Preset.NeedsKey && string.IsNullOrWhiteSpace(tutorOptions.ApiKey))
    {
        Console.Error.WriteLine($"{settings.Preset.Name}: kein Schlüssel. Setze {settings.Preset.KeyEnvVar}.");
        return 2;
    }
    ITutor tutor = tutorOptions.Provider == TutorProvider.Anthropic
        ? new ClaudeTutor(tutorOptions, NullLogger<ClaudeTutor>.Instance)
        : new OpenAiCompatibleTutor(tutorOptions, NullLogger<OpenAiCompatibleTutor>.Instance);
    var context = new LearnerContext("Serbisch", [], [], catalog.ErrorTypes.ToList());
    var name = $"{settings.Preset.Id} · {tutorOptions.Model}";
    Console.WriteLine($"KI-Bewerter {name}:");
    runs.Insert(0, await EvalHarness.RunAsync(name, set, EvalHarness.Ai(tutor, telc, context), now, progress));
    if (options.Hints > 0)
    {
        Console.WriteLine($"Sokratische Hinweise, {options.Hints} Übungen:");
        hints = await EvalHarness.RunHintsAsync(name, tutor, EvalHarness.HintSample(catalog, options.Hints), context);
    }
    (tutor as IDisposable)?.Dispose();
}

Console.WriteLine();
Console.WriteLine(RaterRun.SummaryHeader);
foreach (var run in runs) Console.WriteLine(run.SummaryRow());
if (hints is not null)
    Console.WriteLine($"\nHinweise: {hints.Asked} gefragt, {hints.Failed} ausgefallen, {Pct(hints.LeakRate)} hätten die Lösung verraten (abgefangen), {Pct(hints.QuestionRate)} sind Fragen.");

if (options.Write)
{
    var resultsDir = Path.Combine(root, "evals", "results");
    Directory.CreateDirectory(resultsDir);
    foreach (var run in runs)
    {
        var file = Path.Combine(resultsDir, $"{now:yyyy-MM-dd}-{Slug(run.Rater)}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new StoredRun(run, run == runs[0] ? hints : null), ContentLoader.Options).ReplaceLineEndings("\n") + "\n");
    }
    var doc = Path.Combine(root, "docs", "EVALS.md");
    await File.WriteAllTextAsync(doc, EvalsDoc.Regenerate(await File.ReadAllTextAsync(doc), resultsDir, set));
    Console.WriteLine($"\nGeschrieben: evals/results/, docs/EVALS.md");
}
return 0;

static string Pct(double? v) => v is { } x ? x.ToString("P0", CultureInfo.InvariantCulture) : "–";

static string Slug(string s) => System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "Lotse.slnx"))) return dir.FullName;
    throw new DirectoryNotFoundException("Lotse.slnx nicht gefunden - aus dem Repository starten.");
}

/// <summary>One stored run; the hint figures ride along with the AI run they belong to.</summary>
internal sealed record StoredRun(RaterRun Run, EvalHarness.HintRun? Hints);

internal sealed record Args(string? Preset, string? Model, int Hints, bool Write)
{
    public static Args Parse(string[] args)
    {
        string? Value(string name) => Array.IndexOf(args, name) is var i && i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        return new Args(Value("--preset"), Value("--model"), int.TryParse(Value("--hints"), out var h) ? h : 40, args.Contains("--write"));
    }
}

/// <summary>Rewrites the part of docs/EVALS.md between the result markers from the stored runs - newest per rater.</summary>
internal static class EvalsDoc
{
    private const string Start = "<!-- results:start -->";
    private const string End = "<!-- results:end -->";

    public static string Regenerate(string doc, string resultsDir, GoldenSet set)
    {
        var stored = Directory.EnumerateFiles(resultsDir, "*.json")
            .Select(f => JsonSerializer.Deserialize<StoredRun>(File.ReadAllText(f), ContentLoader.Options)!)
            .GroupBy(s => s.Run.Rater).Select(g => g.OrderByDescending(s => s.Run.Utc).First())
            .OrderBy(s => s.Run.Rater is "Rule-based (no AI)" or "Always B")
            .ThenByDescending(s => s.Run.Metrics.Kappa ?? -1)
            .ToList();

        var sb = new StringBuilder().AppendLine(Start)
            .AppendLine($"Golden set v{set.Version}, {set.Cases.Count} cases. Newest run per rater; generated by `tools/Lotse.Evals`.")
            .AppendLine()
            .AppendLine(RaterRun.SummaryHeader);
        foreach (var s in stored) sb.AppendLine(s.Run.SummaryRow());
        var withHints = stored.Where(s => s.Hints is not null).ToList();
        if (withHints.Count > 0)
        {
            sb.AppendLine().AppendLine("| Hints from | Asked | Failed | Would have given the answer (caught) | Phrased as a question | Median latency |").AppendLine("|---|---|---|---|---|---|");
            foreach (var s in withHints)
            {
                var h = s.Hints!;
                sb.AppendLine($"| {h.Rater} | {h.Asked} | {h.Failed} | {Pct(h.LeakRate)} | {Pct(h.QuestionRate)} | {(h.MedianLatencyMs is { } ms ? (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s" : "–")} |");
            }
        }
        foreach (var s in stored)
        {
            sb.AppendLine().AppendLine($"<details><summary>{s.Run.Rater} – per case ({s.Run.Utc:yyyy-MM-dd})</summary>").AppendLine()
              .Append(s.Run.CaseTable(set.Cases)).AppendLine().AppendLine("</details>");
        }
        sb.Append(End);

        var i = doc.IndexOf(Start, StringComparison.Ordinal);
        var j = doc.IndexOf(End, StringComparison.Ordinal);
        if (i < 0 || j < i) throw new InvalidDataException("docs/EVALS.md: Marker für die Ergebnisse fehlen.");
        return (doc[..i] + sb + doc[(j + End.Length)..]).ReplaceLineEndings("\n");
    }

    private static string Pct(double? v) => v is { } x ? x.ToString("P0", CultureInfo.InvariantCulture) : "–";
}
