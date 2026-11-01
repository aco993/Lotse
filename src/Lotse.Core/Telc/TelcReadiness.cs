namespace Lotse.Core.Telc;

/// <summary>One scored practice or simulation result for one part.</summary>
public sealed record TelcPartResult(TelcPart Part, double Points, DateTime Utc, bool Simulation = false);

/// <summary>The estimate for one part: expected points from recent results, or null with no evidence yet.</summary>
public sealed record TelcPartEstimate(TelcPart Part, double? Points, int Evidence, DateTime? LastUtc)
{
    public double Max => TelcBlueprint.Info(Part).MaxPoints;
    public double? Fraction => Points / Max;
    /// <summary>Points this part is expected to lose - what a week of practice here could win back.</summary>
    public double? Lost => Max - Points;
}

/// <summary>
/// Where the learner stands in telc points. Each part's estimate is a recency-weighted mean of its last three
/// results (3:2:1), a simulation counting double because it was done under exam conditions. Parts without any
/// result are projected from the parts of the same exam half that have one - and flagged, because a projection
/// is not evidence. The ranking of what to practise is by points expected to be lost, not by percentage: five
/// lost points in Schreiben matter more than three lost in Sprachbausteine, even when the percentage says otherwise.
/// </summary>
public sealed record TelcReadiness(IReadOnlyList<TelcPartEstimate> Parts)
{
    public const int Window = 3;

    public static TelcReadiness From(IEnumerable<TelcPartResult> results)
    {
        var byPart = results.GroupBy(r => r.Part).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Utc).Take(Window).ToList());
        var estimates = Enum.GetValues<TelcPart>().Select(part =>
        {
            if (!byPart.TryGetValue(part, out var recent) || recent.Count == 0) return new TelcPartEstimate(part, null, 0, null);
            double sum = 0, weights = 0;
            for (var i = 0; i < recent.Count; i++)
            {
                var w = (Window - i) * (recent[i].Simulation ? 2.0 : 1.0);
                sum += w * Math.Clamp(recent[i].Points, 0, TelcBlueprint.Info(part).MaxPoints);
                weights += w;
            }
            return new TelcPartEstimate(part, sum / weights, recent.Count, recent[0].Utc);
        }).ToList();
        return new TelcReadiness(estimates);
    }

    public TelcPartEstimate Of(TelcPart part) => Parts[(int)part];

    public bool HasAnyEvidence => Parts.Any(p => p.Points is not null);

    private (double Points, bool Projected) Half(bool written)
    {
        var half = Parts.Where(p => TelcBlueprint.IsWritten(p.Part) == written).ToList();
        var known = half.Where(p => p.Points is not null).ToList();
        if (known.Count == 0) return (0, true);
        var fraction = known.Sum(p => p.Points!.Value) / known.Sum(p => p.Max);
        var projected = half.Sum(p => p.Points ?? fraction * p.Max);
        return (projected, known.Count < half.Count);
    }

    /// <summary>Expected written points (of 225); <c>Projected</c> when some parts have no result yet.</summary>
    public (double Points, bool Projected) Written => Half(written: true);

    /// <summary>Expected oral points (of 75).</summary>
    public (double Points, bool Projected) Oral => Half(written: false);

    public bool HasWrittenEvidence => Parts.Any(p => TelcBlueprint.IsWritten(p.Part) && p.Points is not null);
    public bool HasOralEvidence => Parts.Any(p => !TelcBlueprint.IsWritten(p.Part) && p.Points is not null);

    public double WrittenMargin => Written.Points - TelcBlueprint.WrittenPass;
    public double OralMargin => Oral.Points - TelcBlueprint.OralPass;

    /// <summary>The expected grade if the exam were today, or null without evidence on both halves.</summary>
    public string? ExpectedGrade => !HasWrittenEvidence || !HasOralEvidence
        ? null
        : WrittenMargin >= 0 && OralMargin >= 0 ? TelcBlueprint.Grade(Written.Points + Oral.Points) : "nicht bestanden";

    /// <summary>
    /// Parts ordered by what practising them is worth: untested parts first (nothing is known, so anything can
    /// happen there), then by expected lost points, with the oral half weighted up while its own pass mark is at
    /// risk - no surplus in Leseverstehen can make up for it.
    /// </summary>
    public IReadOnlyList<TelcPartEstimate> Priorities()
    {
        var oralAtRisk = !HasOralEvidence || OralMargin < 6;
        var writtenAtRisk = !HasWrittenEvidence || WrittenMargin < 15;
        return Parts
            .OrderBy(p => p.Points is null ? 0 : 1)
            .ThenByDescending(p => (p.Lost ?? p.Max) * (TelcBlueprint.IsWritten(p.Part) ? (writtenAtRisk ? 1.0 : 0.6) : (oralAtRisk ? 1.5 : 0.8)))
            .ThenBy(p => p.Part)
            .ToList();
    }
}
