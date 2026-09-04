namespace Lotse.Core.Model;

/// <summary>
/// The field the learner works in. Optional by design: <see cref="Occupation.Unspecified"/> must leave the planner
/// behaving exactly as it did before this setting existed.
/// </summary>
public enum Occupation
{
    Unspecified,
    IT,
    Pflege,
    Bau,
    Medizin,
    Buero,
    Handel,
}

/// <summary>
/// How an occupation steers the planner. Deliberately small and explainable: it nudges the priority of a handful of
/// vocabulary nodes and breaks ties between otherwise equally suitable exercises. It never changes difficulty, never
/// hides material, and never silently replaces the pedagogy - a nurse still gets the grammar they are weak at.
/// </summary>
public static class OccupationExtensions
{
    /// <summary>The small nudge a matching node gets in the focus ranking (see SessionPlanner.RankFocusNodes).</summary>
    public const double NodeBoost = 0.15;

    public static string Label(this Occupation o) => o switch
    {
        Occupation.IT => "IT / Software",
        Occupation.Pflege => "Pflege",
        Occupation.Bau => "Bau / Handwerk",
        Occupation.Medizin => "Medizin",
        Occupation.Buero => "Büro / Verwaltung",
        Occupation.Handel => "Handel / Verkauf",
        _ => "Keine Angabe",
    };

    /// <summary>Reads as the tail of a reason sentence: "… weil du in der Pflege arbeitest."</summary>
    public static string ReasonTail(this Occupation o) => o switch
    {
        Occupation.IT => "weil du in der IT arbeitest",
        Occupation.Pflege => "weil du in der Pflege arbeitest",
        Occupation.Bau => "weil du am Bau arbeitest",
        Occupation.Medizin => "weil du in der Medizin arbeitest",
        Occupation.Buero => "weil du im Büro arbeitest",
        Occupation.Handel => "weil du im Handel arbeitest",
        _ => "",
    };

    /// <summary>Vocabulary nodes worth a nudge for this field.</summary>
    public static IReadOnlyList<string> PreferredNodes(this Occupation o) => o switch
    {
        Occupation.IT => ["WS.IT_SOFTWARE"],
        Occupation.Pflege or Occupation.Medizin => ["WS.GESUNDHEIT_KOERPER"],
        Occupation.Bau => ["WS.WOHNEN_MOBILITAET", "WS.ARBEIT_KARRIERE"],
        Occupation.Buero => ["WS.BERUF_BUERO", "WS.ARBEIT_KARRIERE"],
        Occupation.Handel => ["WS.GELD_KONSUM", "WS.ARBEIT_KARRIERE"],
        _ => [],
    };

    /// <summary>Exercise tags worth a nudge for this field.</summary>
    public static IReadOnlyList<string> PreferredTags(this Occupation o) => o switch
    {
        Occupation.IT => ["it"],
        Occupation.Pflege or Occupation.Medizin => ["gesundheit", "pflege"],
        Occupation.Bau => ["wohnen", "bau"],
        Occupation.Buero => ["buero"],
        Occupation.Handel => ["geld", "handel"],
        _ => [],
    };

    /// <summary>
    /// How well an exercise fits, as a tie-break key (higher is better). Zero for <see cref="Occupation.Unspecified"/>,
    /// which is what keeps an unset profile on exactly the old ordering.
    /// </summary>
    public static int Fit(this Occupation o, Exercise exercise)
    {
        if (o == Occupation.Unspecified) return 0;
        var score = 0;
        if (PreferredNodes(o).Contains(exercise.NodeId)) score += 2;
        if (exercise.Tags.Any(t => PreferredTags(o).Contains(t, StringComparer.OrdinalIgnoreCase))) score += 2;
        // Everyone in this list works; a professional context beats an everyday one, but only as the last word.
        if (exercise.Context == ExerciseContext.Beruf) score += 1;
        return score;
    }
}
