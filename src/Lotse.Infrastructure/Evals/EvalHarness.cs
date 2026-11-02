using System.Diagnostics;
using System.Text.Json;
using Lotse.Core.Engine;
using Lotse.Core.Evals;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Services;

namespace Lotse.Infrastructure.Evals;

/// <summary>The golden set as stored in <c>evals/golden/telc-schreiben.json</c>.</summary>
public sealed record GoldenSet(int Version, IReadOnlyList<GoldenCase> Cases)
{
    private sealed record FileDto(int Version, List<CaseDto> Cases);
    private sealed record CaseDto(string Id, string Task, string Rater, List<string> Bands, string Text, List<PlantedError>? Planted, bool ThemaVerfehlt, bool Injection, string? Note);

    public static GoldenSet Load(string path)
    {
        var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), ContentLoader.Options) ?? throw new InvalidDataException($"{path} ist leer.");
        return new GoldenSet(dto.Version, [.. dto.Cases.Select(c => new GoldenCase(
            c.Id, c.Task, c.Rater, [.. c.Bands.Select(b => Enum.Parse<TelcBand>(b, ignoreCase: true))], c.Text, c.Planted ?? [], c.ThemaVerfehlt, c.Injection, c.Note))]);
    }

    /// <summary>Format problems of the set itself: unknown tasks, wrong band counts, planted snippets that are not in the text.</summary>
    public IReadOnlyList<string> Validate(TelcContent content, ISet<string> errorCodes)
    {
        var problems = new List<string>();
        foreach (var c in Cases)
        {
            if (content.FindWritingTask(c.Task) is null) problems.Add($"{c.Id}: unbekannte Aufgabe {c.Task}");
            if (c.Bands.Count != 3) problems.Add($"{c.Id}: braucht genau drei Stufen");
            foreach (var p in c.Planted)
            {
                if (!QuotedErrors.IsQuoted(p.Snippet, c.Text)) problems.Add($"{c.Id}: gepflanzter Fehler „{p.Snippet}“ steht nicht im Text");
                if (!errorCodes.Contains(p.Code)) problems.Add($"{c.Id}: unbekannter Fehlercode {p.Code}");
            }
        }
        if (Cases.Select(c => c.Id).Distinct().Count() != Cases.Count) problems.Add("Fall-Id doppelt");
        return problems;
    }
}

/// <summary>
/// Runs a rater over the golden set. Three raters share one harness: the AI tutor exactly as the app calls it
/// (same task text, same caps from <see cref="TelcWritingCheck"/>), the rule-based baseline the app falls back to
/// without a key (every criterion B, then the same caps, errors from <see cref="FreeTextAnalyzer"/>), and "always
/// B" - the trivial rater any real one must beat to be worth its cost.
/// </summary>
public static class EvalHarness
{
    public delegate Task<CaseResult> Rater(GoldenCase golden, CancellationToken ct);

    public static async Task<RaterRun> RunAsync(string raterName, GoldenSet set, Rater rate, DateTime utc, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<CaseResult>();
        foreach (var c in set.Cases)
        {
            var watch = Stopwatch.StartNew();
            CaseResult r;
            try
            {
                r = await rate(c, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                r = CaseResult.Failure(c.Id, (int)watch.ElapsedMilliseconds, e.Message);
            }
            results.Add(r);
            progress?.Report($"{c.Id}: {(r.Failed is null ? string.Concat(r.Bands) : "ausgefallen – " + r.Failed)}");
        }
        return new RaterRun(raterName, $"v{set.Version}", utc, results, RaterMetrics.Compute(set.Cases, results));
    }

    /// <summary>The app's own rating path: tutor rating, then the email's caps.</summary>
    public static Rater Ai(ITutor tutor, TelcContent content, LearnerContext context) => async (c, ct) =>
    {
        var task = content.FindWritingTask(c.Task)!.Value.Task;
        var watch = Stopwatch.StartNew();
        var ai = await tutor.RateTelcAsync(new TelcRatingRequest(TelcPart.Schreiben, TelcService.TaskText(content, TelcPart.Schreiben, c.Task), c.Text), context, ct);
        var capped = ai.ToWriting().Capped(TelcWritingCheck.Analyze(c.Text, task));
        return new CaseResult(c.Id, [capped.Aufgabe, capped.Gestaltung, capped.Richtigkeit], capped.ThemaVerfehlt, ai.Errors, ai.DroppedUnquoted, (int)watch.ElapsedMilliseconds);
    };

    /// <summary>What the app does without a tutor: B everywhere, capped by the form checks; mistakes from the rule-based analyzer.</summary>
    public static Rater Rules(TelcContent content, Lexicon lexicon) => (c, _) =>
    {
        var task = content.FindWritingTask(c.Task)!.Value.Task;
        var capped = new TelcWritingRating(TelcBand.B, TelcBand.B, TelcBand.B).Capped(TelcWritingCheck.Analyze(c.Text, task));
        var exercise = new Exercise { Id = "eval", Type = ExerciseType.FreeWrite, NodeId = task.NodeId, Band = CefrBand.B2_1, Prompt = "eval" };
        var findings = FreeTextAnalyzer.Analyze(c.Text, exercise, lexicon, spoken: false).Findings
            .Select(f => new TutorError(f.Code, f.Snippet, f.Correction, f.Explanation)).ToList();
        return Task.FromResult(new CaseResult(c.Id, [capped.Aufgabe, capped.Gestaltung, capped.Richtigkeit], false, findings, 0, 0));
    };

    public static Rater AlwaysB() => (c, _) => Task.FromResult(new CaseResult(c.Id, [TelcBand.B, TelcBand.B, TelcBand.B], false, [], 0, 0));

    // ---- Hint guard ---------------------------------------------------------------------------------------------

    /// <summary>How often the tutor's Socratic hint would have given the answer away (caught by <see cref="HintLadder.Leaks(string, Exercise)"/>), and how often it is a question at all.</summary>
    public sealed record HintRun(string Rater, int Asked, int Failed, int Leaked, int Questions, int? MedianLatencyMs)
    {
        public double? LeakRate => Asked - Failed == 0 ? null : Leaked / (double)(Asked - Failed);
        public double? QuestionRate => Asked - Failed == 0 ? null : Questions / (double)(Asked - Failed);
    }

    /// <summary>A fixed, spread sample of drills: every n-th closed item in id order, so two runs ask the same questions.</summary>
    public static IReadOnlyList<Exercise> HintSample(ContentCatalog catalog, int count)
    {
        // Only items whose answer is long enough for the leak check to mean something.
        var all = catalog.Exercises
            .Where(e => e.Source == ExerciseSource.Seed && e.Type is ExerciseType.Cloze or ExerciseType.MultipleChoice or ExerciseType.Transform)
            .Where(e => (e.Type == ExerciseType.MultipleChoice ? e.Options : e.Answers).Any(a => a.Length > 2))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        var n = Math.Min(count, all.Count);
        return [.. Enumerable.Range(0, n).Select(i => all[i * all.Count / n])];
    }

    public static async Task<HintRun> RunHintsAsync(string raterName, ITutor tutor, IReadOnlyList<Exercise> sample, LearnerContext context, CancellationToken ct = default)
    {
        int failed = 0, leaked = 0, questions = 0;
        var latencies = new List<int>();
        foreach (var e in sample)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var hint = (await tutor.HintAsync(e, null, context, ct)).Trim();
                latencies.Add((int)watch.ElapsedMilliseconds);
                if (HintLadder.Leaks(hint, e)) leaked++;
                if (hint.EndsWith('?')) questions++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
            }
        }
        latencies.Sort();
        return new HintRun(raterName, sample.Count, failed, leaked, questions, latencies.Count == 0 ? null : latencies[latencies.Count / 2]);
    }
}
