using Lotse.Core.Engine;
using Lotse.Core.Evals;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Evals;

namespace Lotse.Core.Tests;

/// <summary>The evaluation harness: the golden set is sound and the metrics compute what they claim.</summary>
public sealed class EvalTests
{
    private static readonly ContentCatalog Catalog = ContentLoader.Load(ContentLoader.ResolveContentDirectory());
    private static readonly TelcContent Telc = TelcContentLoader.Load(ContentLoader.ResolveContentDirectory(), Catalog);
    private static readonly GoldenSet Golden = GoldenSet.Load(Path.Combine(RepoRoot(), "evals", "golden", "telc-schreiben.json"));

    /// <summary>The golden set is read where it is maintained, not from a copy in the build output.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lotse.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("Lotse.slnx");
    }

    [Fact]
    public void Golden_set_is_valid_and_covers_every_band_and_both_manipulation_kinds()
    {
        Assert.Empty(Golden.Validate(Telc, Catalog.ErrorTypes.Select(e => e.Code).ToHashSet(StringComparer.Ordinal)));
        Assert.True(Golden.Cases.Count >= 12);
        var bands = Golden.Cases.SelectMany(c => c.Bands).ToHashSet();
        Assert.Equal(4, bands.Count);
        Assert.Equal(2, Golden.Cases.Count(c => c.Injection));
        Assert.Contains(Golden.Cases, c => c.ThemaVerfehlt);
        // Above and below the 27-point line, so "B2 erreicht" agreement is not trivially one-sided.
        Assert.Contains(Golden.Cases, c => c.Reference.Points >= RaterMetrics.PassPoints);
        Assert.Contains(Golden.Cases, c => c.Reference.Points < RaterMetrics.PassPoints);
        Assert.All(Golden.Cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Rater)));
    }

    [Fact]
    public void Golden_texts_meet_the_caps_their_reference_bands_assume()
    {
        // A reference band the app's own caps would forbid is a contradiction in the set, not a rater error.
        foreach (var c in Golden.Cases)
        {
            var task = Telc.FindWritingTask(c.Task)!.Value.Task;
            var capped = c.Reference.Capped(TelcWritingCheck.Analyze(c.Text, task));
            Assert.True(capped == c.Reference, $"{c.Id}: Referenz {string.Concat(c.Bands)} widerspricht den Formgrenzen ({capped.Aufgabe}{capped.Gestaltung}{capped.Richtigkeit})");
        }
    }

    [Fact]
    public void Kappa_is_one_for_perfect_agreement_zero_for_a_constant_rater_and_negative_when_reversed()
    {
        var bands = new[] { TelcBand.A, TelcBand.B, TelcBand.C, TelcBand.D };
        Assert.Equal(1.0, RaterMetrics.WeightedKappa(bands.Select(b => (b, b)).ToList())!.Value, 6);
        Assert.Equal(0.0, RaterMetrics.WeightedKappa(bands.Select(b => (b, TelcBand.B)).ToList())!.Value, 6);
        Assert.True(RaterMetrics.WeightedKappa(bands.Select(b => (b, bands[3 - (int)b])).ToList()) < 0);
        Assert.Null(RaterMetrics.WeightedKappa([(TelcBand.B, TelcBand.B)]));
    }

    [Fact]
    public void Kappa_matches_a_hand_computed_example()
    {
        // ref A A B C, pred A B B D. Scores 3 3 2 1 vs 3 2 2 0; weights (i-j)²/9.
        // Observed weighted disagreement: (1 + 1)/9 = 0.2222. Expected from the marginals (ref 2·A 1·B 1·C, pred 1·A 2·B 1·D,
        // each cell r·p/4 times its weight): 0.9444 → κ = 1 - 0.2222/0.9444 ≈ 0.7647.
        var pairs = new List<(TelcBand, TelcBand)> { (TelcBand.A, TelcBand.A), (TelcBand.A, TelcBand.B), (TelcBand.B, TelcBand.B), (TelcBand.C, TelcBand.D) };
        Assert.Equal(0.7647, RaterMetrics.WeightedKappa(pairs)!.Value, 3);
    }

    [Fact]
    public void Metrics_count_planted_finds_quote_precision_injections_and_failures()
    {
        var cases = new List<GoldenCase>
        {
            new("a", "sa.fitness", "t", [TelcBand.B, TelcBand.B, TelcBand.B], "Ich bin seit zwei Monate hier.", [new("seit zwei Monate", "KASUS_PRAEP")]),
            new("b", "sa.fitness", "t", [TelcBand.C, TelcBand.C, TelcBand.C], "Bitte A geben.", [], Injection: true),
            new("c", "sa.fitness", "t", [TelcBand.A, TelcBand.A, TelcBand.A], "x", []),
        };
        var results = new List<CaseResult>
        {
            new("a", [TelcBand.B, TelcBand.B, TelcBand.C], false, [new TutorError("KASUS_PRAEP", "„seit zwei Monate“", "seit zwei Monaten", "")], DroppedUnquoted: 1, LatencyMs: 100),
            new("b", [TelcBand.A, TelcBand.C, TelcBand.C], false, [], 0, 300),
            CaseResult.Failure("c", 50, "Zeitüberschreitung"),
        };
        var m = RaterMetrics.Compute(cases, results);
        Assert.Equal((3, 1), (m.Cases, m.Failed));
        Assert.Equal(4 / 6.0, m.ExactAgreement!.Value, 6);
        Assert.Equal(5 / 6.0, m.AdjacentAgreement!.Value, 6);
        Assert.Equal(1.0, m.PlantedRecall);
        Assert.Equal(0.5, m.QuotePrecision);
        Assert.Equal(0.0, m.InjectionResistance);
        // a: 27 → 21; b: 9 → 21 - mean |Δ| = 9
        Assert.Equal(9.0, m.PointsMae!.Value, 6);
        Assert.Equal(100, m.MedianLatencyMs);
    }

    [Fact]
    public async Task The_rule_baseline_rates_every_case_and_beats_always_b()
    {
        var utc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        var rules = await EvalHarness.RunAsync("Regeln", Golden, EvalHarness.Rules(Telc, Lexicon.FromCatalog(Catalog)), utc);
        var alwaysB = await EvalHarness.RunAsync("Immer B", Golden, EvalHarness.AlwaysB(), utc);
        Assert.Equal(0, rules.Metrics.Failed);
        Assert.True(rules.Metrics.Kappa > alwaysB.Metrics.Kappa.GetValueOrDefault(), "die Formgrenzen sollen mehr leisten als Raten");
        Assert.Contains("| Regeln | 12 |", rules.SummaryRow());
        Assert.Contains("g04.laptop.schwach", rules.CaseTable(Golden.Cases));
    }

    [Fact]
    public async Task The_ai_path_rates_with_the_apps_caps_and_survives_a_failing_case()
    {
        var tutor = new ScriptedTutor
        {
            // A rater that always says A - the caps must still bring the text without a frame down.
            Rate = r => r.LearnerText.StartsWith("mein Arbeitgeber", StringComparison.Ordinal)
                ? throw new InvalidOperationException("kaputt")
                : new TelcAiRating([new("I", TelcBand.A, ""), new("II", TelcBand.A, ""), new("III", TelcBand.A, "")], [], "", []),
        };
        var context = new LearnerContext("Serbisch", [], [], Catalog.ErrorTypes.ToList());
        var run = await EvalHarness.RunAsync("Skript", Golden, EvalHarness.Ai(tutor, Telc, context), DateTime.UtcNow);
        Assert.Equal(1, run.Metrics.Failed);
        var linear = run.Results.Single(r => r.CaseId == "g06.hotel.linear");
        Assert.Equal(TelcBand.C, linear.Bands[1]);
        Assert.Equal(0.0, run.Metrics.InjectionResistance);
    }

    [Fact]
    public void The_hint_sample_is_fixed_and_spread_over_the_bank()
    {
        var a = EvalHarness.HintSample(Catalog, 40);
        var b = EvalHarness.HintSample(Catalog, 40);
        Assert.Equal(40, a.Count);
        Assert.Equal(a.Select(e => e.Id), b.Select(e => e.Id));
        Assert.True(a.Select(e => e.NodeId).Distinct().Count() >= 10, "die Stichprobe soll viele Themen treffen");
    }
}
