using Lotse.Core.Model;

namespace Lotse.Core.Engine;

/// <summary>
/// Builds the initial diagnostic ("Einstufung"): two items per core node, one just below and one just above the
/// B1/B2 boundary, all in productive formats. About 20 minutes; afterwards the learner model is no longer a guess.
/// </summary>
public static class PlacementTest
{
    /// <summary>Nodes every B2 candidate must have; the placement samples exactly these.</summary>
    public static readonly IReadOnlyList<string> CoreNodeIds =
    [
        "GR.ARTIKEL_GENUS",
        "GR.KASUS_PRAEPOSITIONEN",
        "GR.WECHSELPRAEPOSITIONEN",
        "GR.ADJEKTIVDEKLINATION",
        "GR.NEBENSATZ_WORTSTELLUNG",
        "GR.PERFEKT_PRAETERITUM",
        "GR.KONJUNKTIV2",
        "GR.PASSIV",
        "GR.VERB_PRAEPOSITION",
        "GR.KONNEKTOREN",
        "GR.RELATIVSATZ",
        "GR.NOMINALISIERUNG",
        "WS.BERUF_BUERO",
        "RM.FORMELLE_EMAIL",
    ];

    /// <param name="quick">
    /// One item per node instead of two (about eight minutes): the lower item for the first half of the nodes and
    /// the upper for the second, so the quick pass still straddles the boundary. Meant for learners who cannot sit
    /// down for twenty minutes; the model simply starts with less confidence and catches up in the daily sessions.
    /// </param>
    public static IReadOnlyList<Exercise> Build(ContentCatalog catalog, int seed = 42, bool quick = false)
    {
        var rng = new Random(seed);
        var easyPass = new List<Exercise>();
        var hardPass = new List<Exercise>();
        var index = 0;
        foreach (var nodeId in CoreNodeIds)
        {
            // Only single-answer items: the placement must be quick and comparable across nodes.
            var pool = catalog.ForNode(nodeId).Where(e => !e.IsProduction && !e.IsReceptive && !e.IsComposite).ToList();
            if (pool.Count == 0) continue;
            var lower = pool.Where(e => e.Band <= CefrBand.B1_2).OrderBy(_ => rng.Next()).FirstOrDefault();
            var upper = pool.Where(e => e.Band >= CefrBand.B2_1).OrderBy(_ => rng.Next()).FirstOrDefault();
            if (quick)
            {
                var pick = index++ % 2 == 0 ? lower ?? upper : upper ?? lower;
                easyPass.Add(pick ?? pool[rng.Next(pool.Count)]);
                continue;
            }
            if (lower is not null) easyPass.Add(lower);
            if (upper is not null && upper != lower) hardPass.Add(upper);
            if (lower is null && upper is null) easyPass.Add(pool[rng.Next(pool.Count)]);
        }
        // Two passes across all nodes (easy first, then harder) instead of two items per node in a row:
        // less fatigue on one topic, and the learner sees the breadth of the test early.
        return [.. easyPass, .. hardPass];
    }
}
