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

    public static IReadOnlyList<Exercise> Build(ContentCatalog catalog, int seed = 42)
    {
        var rng = new Random(seed);
        var result = new List<Exercise>();
        foreach (var nodeId in CoreNodeIds)
        {
            var pool = catalog.ForNode(nodeId).Where(e => !e.IsProduction && !e.IsReceptive).ToList();
            if (pool.Count == 0) continue;
            var lower = pool.Where(e => e.Band <= CefrBand.B1_2).OrderBy(_ => rng.Next()).FirstOrDefault();
            var upper = pool.Where(e => e.Band >= CefrBand.B2_1).OrderBy(_ => rng.Next()).FirstOrDefault();
            if (lower is not null) result.Add(lower);
            if (upper is not null && upper != lower) result.Add(upper);
            if (lower is null && upper is null) result.Add(pool[rng.Next(pool.Count)]);
        }
        return result;
    }
}
