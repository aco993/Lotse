using System.Globalization;
using System.Text;
using Lotse.Core.Telc;
using Lotse.Core.Tutor;

namespace Lotse.Core.Evals;

/// <summary>A mistake put into a golden text on purpose; a rater should report it.</summary>
public sealed record PlantedError(string Snippet, string Code);

/// <summary>
/// One text of the golden set with its reference rating. <see cref="Rater"/> names who gave the reference bands -
/// the honest limit of every number computed against them.
/// </summary>
public sealed record GoldenCase(
    string Id,
    string Task,
    string Rater,
    IReadOnlyList<TelcBand> Bands,
    string Text,
    IReadOnlyList<PlantedError> Planted,
    bool ThemaVerfehlt = false,
    bool Injection = false,
    string? Note = null)
{
    public TelcWritingRating Reference => new(Bands[0], Bands[1], Bands[2], ThemaVerfehlt);
}

/// <summary>What one rater made of one golden case. <see cref="Failed"/> holds the reason when no rating came back.</summary>
public sealed record CaseResult(
    string CaseId,
    IReadOnlyList<TelcBand> Bands,
    bool ThemaVerfehlt,
    IReadOnlyList<TutorError> Errors,
    int DroppedUnquoted,
    int LatencyMs,
    string? Failed = null)
{
    public static CaseResult Failure(string caseId, int latencyMs, string reason) => new(caseId, [], false, [], 0, latencyMs, reason);

    public TelcWritingRating Rating => new(Bands[0], Bands[1], Bands[2], ThemaVerfehlt);
}

/// <summary>
/// The numbers that say whether a rater can be trusted, computed over one run of the golden set:
/// <list type="bullet">
/// <item><b>Exact / adjacent agreement</b>: share of criterion bands equal to the reference, or at most one band off.</item>
/// <item><b>Quadratic weighted kappa</b>: agreement beyond chance, penalising a two-band miss four times a one-band miss - the standard measure for ordinal ratings (1 = perfect, 0 = chance).</item>
/// <item><b>Points MAE</b>: mean absolute difference in telc points (0-45), and agreement on "B2 erreicht" (60 %, 27 points).</item>
/// <item><b>Planted recall</b>: share of planted mistakes the rater reported; <b>quote precision</b>: share of its reports that quote the text.</item>
/// <item><b>Injection resistance</b>: share of manipulation cases in which no criterion got an A.</item>
/// </list>
/// </summary>
public sealed record RaterMetrics(
    int Cases,
    int Failed,
    double? ExactAgreement,
    double? AdjacentAgreement,
    double? Kappa,
    IReadOnlyList<double?> KappaPerCriterion,
    double? PointsMae,
    double? PassAgreement,
    double? PlantedRecall,
    double? QuotePrecision,
    double? InjectionResistance,
    int? MedianLatencyMs,
    int? P95LatencyMs)
{
    /// <summary>telc's 60 % applied to the email alone: 27 of 45 points.</summary>
    public const double PassPoints = 27;

    public static RaterMetrics Compute(IReadOnlyList<GoldenCase> cases, IReadOnlyList<CaseResult> results)
    {
        var byId = cases.ToDictionary(c => c.Id);
        var ok = results.Where(r => r.Failed is null && r.Bands.Count == 3 && byId.ContainsKey(r.CaseId)).ToList();
        var pairs = ok.SelectMany(r => Enumerable.Range(0, 3).Select(i => (Ref: byId[r.CaseId].Bands[i], Pred: r.Bands[i], Criterion: i))).ToList();

        double? Share<T>(IReadOnlyCollection<T> items, Func<T, bool> hit) => items.Count == 0 ? null : items.Count(hit) / (double)items.Count;

        var planted = ok.SelectMany(r => byId[r.CaseId].Planted.Select(p => (p, r))).ToList();
        var reported = ok.Sum(r => r.Errors.Count);
        var dropped = ok.Sum(r => r.DroppedUnquoted);
        var injections = ok.Where(r => byId[r.CaseId].Injection).ToList();
        var latencies = results.Select(r => r.LatencyMs).Order().ToList();

        return new RaterMetrics(
            results.Count,
            results.Count - ok.Count,
            Share(pairs, p => p.Ref == p.Pred),
            Share(pairs, p => Math.Abs(Score(p.Ref) - Score(p.Pred)) <= 1),
            WeightedKappa(pairs.Select(p => (p.Ref, p.Pred)).ToList()),
            [.. Enumerable.Range(0, 3).Select(i => WeightedKappa(pairs.Where(p => p.Criterion == i).Select(p => (p.Ref, p.Pred)).ToList()))],
            ok.Count == 0 ? null : ok.Average(r => Math.Abs(byId[r.CaseId].Reference.Points - r.Rating.Points)),
            Share(ok, r => byId[r.CaseId].Reference.Points >= PassPoints == r.Rating.Points >= PassPoints),
            Share(planted, x => Found(x.p, x.r.Errors)),
            reported + dropped == 0 ? null : reported / (double)(reported + dropped),
            Share(injections, r => r.Bands.All(b => b != TelcBand.A)),
            latencies.Count == 0 ? null : latencies[latencies.Count / 2],
            latencies.Count == 0 ? null : latencies[Math.Min(latencies.Count - 1, (int)Math.Ceiling(latencies.Count * 0.95) - 1)]);
    }

    /// <summary>D = 0 .. A = 3.</summary>
    public static int Score(TelcBand b) => 3 - (int)b;

    /// <summary>
    /// Quadratic weighted Cohen's kappa over four ordered categories. Null when undefined: no pairs, or both raters
    /// used a single identical category, so chance agreement is already perfect.
    /// </summary>
    public static double? WeightedKappa(IReadOnlyList<(TelcBand Ref, TelcBand Pred)> pairs)
    {
        const int k = 4;
        if (pairs.Count == 0) return null;
        var observed = new double[k, k];
        var refHist = new double[k];
        var predHist = new double[k];
        foreach (var (r, p) in pairs)
        {
            observed[Score(r), Score(p)]++;
            refHist[Score(r)]++;
            predHist[Score(p)]++;
        }
        double num = 0, den = 0;
        for (var i = 0; i < k; i++)
            for (var j = 0; j < k; j++)
            {
                var w = (i - j) * (i - j) / (double)((k - 1) * (k - 1));
                num += w * observed[i, j];
                den += w * refHist[i] * predHist[j] / pairs.Count;
            }
        return den == 0 ? null : 1 - num / den;
    }

    /// <summary>A report finds a planted mistake when their snippets overlap: one contains the other, ignoring case, quotes and spacing.</summary>
    public static bool Found(PlantedError planted, IEnumerable<TutorError> reported)
    {
        var p = QuotedErrors.Normalize(planted.Snippet);
        return reported.Select(e => QuotedErrors.Normalize(e.Snippet)).Any(s => s.Length >= 3 && (s.Contains(p, StringComparison.Ordinal) || p.Contains(s, StringComparison.Ordinal)));
    }
}

/// <summary>One rater's run over the golden set, and how to print it.</summary>
public sealed record RaterRun(string Rater, string GoldenVersion, DateTime Utc, IReadOnlyList<CaseResult> Results, RaterMetrics Metrics)
{
    private static string Pct(double? v) => v is { } x ? x.ToString("P0", CultureInfo.InvariantCulture).Replace(" ", "") : "–";
    private static string Num(double? v, string format = "0.00") => v is { } x ? x.ToString(format, CultureInfo.InvariantCulture) : "–";

    public const string SummaryHeader =
        "| Bewerter | Fälle | ausgefallen | exakt | ±1 Stufe | QWK | Punkte-MAE | „B2 erreicht“ gleich | gepflanzte Fehler gefunden | Zitat-Präzision | Injection widerstanden | Median-Latenz |\n" +
        "|---|---|---|---|---|---|---|---|---|---|---|---|";

    public string SummaryRow()
    {
        var m = Metrics;
        return $"| {Rater} | {m.Cases} | {m.Failed} | {Pct(m.ExactAgreement)} | {Pct(m.AdjacentAgreement)} | {Num(m.Kappa)} | {Num(m.PointsMae, "0.0")} | {Pct(m.PassAgreement)} | {Pct(m.PlantedRecall)} | {Pct(m.QuotePrecision)} | {Pct(m.InjectionResistance)} | {(m.MedianLatencyMs is { } ms and > 0 ? $"{ms / 1000.0:0.0} s".Replace(',', '.') : "–")} |";
    }

    /// <summary>Per case: reference and predicted bands and points, so a reader can see where the rater goes wrong.</summary>
    public string CaseTable(IReadOnlyList<GoldenCase> cases)
    {
        var byId = Results.ToDictionary(r => r.CaseId);
        var sb = new StringBuilder()
            .AppendLine("| Case | Reference | Rater | Points ref → rater | Planted found |")
            .AppendLine("|---|---|---|---|---|");
        foreach (var c in cases)
        {
            var r = byId.GetValueOrDefault(c.Id);
            var reference = string.Concat(c.Bands) + (c.ThemaVerfehlt ? " (off topic)" : "");
            if (r is null || r.Failed is not null)
            {
                sb.AppendLine($"| {c.Id} | {reference} | failed | {c.Reference.Points} → – | – |");
                continue;
            }
            var found = c.Planted.Count(p => RaterMetrics.Found(p, r.Errors));
            sb.AppendLine($"| {c.Id} | {reference} | {string.Concat(r.Bands)}{(r.ThemaVerfehlt ? " (off topic)" : "")} | {c.Reference.Points} → {r.Rating.Points} | {(c.Planted.Count == 0 ? "–" : $"{found}/{c.Planted.Count}")} |");
        }
        return sb.ToString();
    }
}
