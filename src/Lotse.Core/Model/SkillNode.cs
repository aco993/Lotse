namespace Lotse.Core.Model;

/// <summary>Broad competence area a skill node belongs to. Mirrors the modules of a B2 exam plus the two "building block" areas.</summary>
public enum SkillArea
{
    Grammatik,
    Wortschatz,
    Redemittel,
    Lesen,
    Hoeren,
    Schreiben,
    Sprechen,
}

/// <summary>
/// CEFR sub-band used to place both learners and exercises on one scale.
/// The numeric <see cref="CefrBandExtensions.Difficulty"/> is the "b" parameter of the logistic model in <c>AbilityUpdater</c>.
/// </summary>
public enum CefrBand
{
    B1_1,
    B1_2,
    B2_1,
    B2_2,
    C1,
}

public static class CefrBandExtensions
{
    /// <summary>Maps a band to a difficulty on the ability scale (0 = the B1/B2 boundary).</summary>
    public static double Difficulty(this CefrBand band) => band switch
    {
        CefrBand.B1_1 => -1.2,
        CefrBand.B1_2 => -0.4,
        CefrBand.B2_1 => 0.4,
        CefrBand.B2_2 => 1.2,
        CefrBand.C1 => 2.0,
        _ => 0,
    };

    public static string Label(this CefrBand band) => band switch
    {
        CefrBand.B1_1 => "B1.1",
        CefrBand.B1_2 => "B1.2",
        CefrBand.B2_1 => "B2.1",
        CefrBand.B2_2 => "B2.2",
        CefrBand.C1 => "C1",
        _ => band.ToString(),
    };

    /// <summary>Nearest band for a continuous ability/difficulty value.</summary>
    public static CefrBand FromDifficulty(double value) => value switch
    {
        < -0.8 => CefrBand.B1_1,
        < 0.0 => CefrBand.B1_2,
        < 0.8 => CefrBand.B2_1,
        < 1.6 => CefrBand.B2_2,
        _ => CefrBand.C1,
    };
}

/// <summary>
/// One node of the skill taxonomy ("Kompetenzknoten"): a grammar topic, a vocabulary field or a productive skill.
/// The learner model tracks one ability value per node; exercises and error codes point at nodes.
/// </summary>
public sealed record SkillNode(
    string Id,
    SkillArea Area,
    string Title,
    string Description,
    CefrBand Band,
    /// <summary>True when Serbian speakers are known to struggle with this topic because Serbian works differently (no articles, no V2 rule, ...).</summary>
    bool SerbianInterference,
    /// <summary>Short contrastive note (German with Serbian examples) shown when the learner makes a related mistake.</summary>
    string? InterferenceNote,
    IReadOnlyList<string> Prerequisites,
    /// <summary>Relative weight of the node when computing exam readiness (1 = normal).</summary>
    double Weight = 1.0);

/// <summary>A catalogued error type. Every mistake, whether detected deterministically or by the AI tutor, is tagged with one of these codes.</summary>
public sealed record ErrorType(
    string Code,
    string NodeId,
    string Title,
    string Description,
    /// <summary>How serious the error is for a B2 rater: 1 = cosmetic, 2 = noticeable, 3 = impairs communication.</summary>
    int Severity);
