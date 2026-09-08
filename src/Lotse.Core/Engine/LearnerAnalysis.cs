using Lotse.Core.Model;

namespace Lotse.Core.Engine;

public enum Trend
{
    Unknown,
    Improving,
    Stable,
    Worsening,
}

public sealed record WeakArea(
    string NodeId,
    string Title,
    /// <summary>The jargon-free name for Heute; equals <paramref name="Title"/> where the node has none.</summary>
    string PlainTitle,
    SkillArea Area,
    double Mastery,
    double Confidence,
    int Errors7d,
    int Errors30d,
    IReadOnlyList<(string Code, string Title, int Count)> TopErrors,
    Trend Trend,
    string? InterferenceNote);

/// <param name="Readiness">The number shown for the module. Meaningful only when <paramref name="HasEvidence"/> is true.</param>
/// <param name="Coverage">How much of the module's OWN skill nodes has been practised (confidence-weighted) - the
/// grammar and vocabulary behind it do not count here, which is what makes "too little data" honest.</param>
/// <param name="Foundation">Mastery of the supporting grammar/vocabulary/Redemittel - the "Sprachliches Fundament".
/// Always computable, never the exam number by itself.</param>
/// <param name="HasEvidence">Whether the learner has actually done enough of this module's own tasks for the number
/// to mean anything. Without it the UI shows no percentage at all.</param>
public sealed record ModuleReadiness(
    string Module,
    double Readiness,
    double Coverage,
    IReadOnlyList<string> Blockers,
    double Foundation = 0,
    bool HasEvidence = true);

public sealed record ReadinessReport(
    /// <summary>Average over the modules that have evidence; 0 when none has - check <see cref="HasEvidence"/> first.</summary>
    double Overall,
    IReadOnlyList<ModuleReadiness> Modules,
    /// <summary>Plain-language verdict shown on the dashboard.</summary>
    string Verdict,
    /// <summary>True once at least one exam module has been practised enough to be measured.</summary>
    bool HasEvidence = true)
{
    public int EvidenceModules => Modules.Count(m => m.HasEvidence);
}

/// <summary>Turns raw skill states and error events into the two things the learner (and the planner) care about: weak areas and exam readiness.</summary>
public static class LearnerAnalysis
{
    public static IReadOnlyList<WeakArea> WeakAreas(ContentCatalog catalog, IReadOnlyDictionary<string, SkillState> states, IReadOnlyList<ErrorEvent> errors, DateTime nowUtc, int take = 8)
    {
        var e7 = errors.Where(e => (nowUtc - e.Utc).TotalDays <= 7).ToList();
        var e30 = errors.Where(e => (nowUtc - e.Utc).TotalDays <= 30).ToList();
        var ePrev = errors.Where(e => (nowUtc - e.Utc).TotalDays is > 7 and <= 14).ToList();

        var result = new List<(WeakArea Area, double Priority)>();
        foreach (var node in catalog.Nodes)
        {
            var st = states.GetValueOrDefault(node.Id);
            var n7 = e7.Count(e => e.NodeId == node.Id);
            var n30 = e30.Count(e => e.NodeId == node.Id);
            var nPrev = ePrev.Count(e => e.NodeId == node.Id);
            if (st is null && n30 == 0) continue;

            var mastery = st?.Mastery ?? 0.5;
            var confidence = st?.Confidence ?? 0;
            var top = e30.Where(e => e.NodeId == node.Id).GroupBy(e => e.Code)
                .Select(g => (g.Key, catalog.Error(g.Key)?.Title ?? g.Key, g.Count()))
                .OrderByDescending(t => t.Item3).Take(3).ToList();

            var trend = (n7, nPrev) switch
            {
                (0, 0) when st is null || st.Attempts < 3 => Trend.Unknown,
                var (a, b) when a < b => Trend.Improving,
                var (a, b) when a > b => Trend.Worsening,
                _ => Trend.Stable,
            };

            var priority = (1 - mastery) * (0.4 + 0.6 * confidence) + n7 * 0.08 + n30 * 0.02;
            result.Add((new WeakArea(node.Id, node.Title, node.PlainTitle ?? node.Title, node.Area, mastery, confidence, n7, n30, top, trend, node.InterferenceNote), priority));
        }
        return result.OrderByDescending(r => r.Priority).Take(take).Select(r => r.Area).ToList();
    }

    public static IReadOnlyList<(string Code, string Title, int Count, string NodeTitle)> TopErrorCodes(ContentCatalog catalog, IReadOnlyList<ErrorEvent> errors, DateTime nowUtc, int days = 30, int take = 6)
        => errors.Where(e => (nowUtc - e.Utc).TotalDays <= days)
            .GroupBy(e => e.Code)
            .Select(g => (g.Key, catalog.Error(g.Key)?.Title ?? g.Key, g.Count(), catalog.NodeTitle(g.First().NodeId)))
            .OrderByDescending(t => t.Item3).Take(take).ToList();

    /// <summary>
    /// Estimates readiness per exam module - and refuses to put a number on a module the learner has not practised.
    ///
    /// The first version blended each module's own two or three skill nodes with the whole grammar/vocabulary pool
    /// behind it, at node weight. Lesen therefore carried 9 % of its own weight and 91 % vocabulary: a learner who
    /// drilled cards to 85 % and never read a single text was shown "Lesen 81 %" and told to start exam
    /// simulations. That is the one number on the front page a candidate uses to book the exam, so it has to be
    /// built from the module's own evidence:
    ///
    /// - <c>Evidence</c> is the confidence-weighted mastery of the module's OWN nodes (Lesen, Hören, Schreiben,
    ///   Sprechen). Below <see cref="MinEvidenceCoverage"/> there is no number at all.
    /// - <c>Foundation</c> is the supporting grammar/vocabulary/Redemittel - kept, shown as what it is, and
    ///   allowed to lift the module number by at most <see cref="FoundationBonus"/> above the evidence.
    ///
    /// Unknown nodes still shrink towards a B1–B2 prior inside each blend, so a single attempt neither looks perfect
    /// nor hopeless - but the prior alone is never displayed as a percentage any more.
    /// </summary>
    public static ReadinessReport Readiness(ContentCatalog catalog, IReadOnlyDictionary<string, SkillState> states)
    {
        double Blend(SkillState? s) => s is null ? Prior : s.Confidence * s.Mastery + (1 - s.Confidence) * Prior;
        (double Value, double Coverage) Weighted(List<SkillNode> nodes)
        {
            if (nodes.Count == 0) return (Prior, 0);
            var wSum = nodes.Sum(n => n.Weight);
            return (nodes.Sum(n => Blend(states.GetValueOrDefault(n.Id)) * n.Weight) / wSum,
                    nodes.Sum(n => (states.GetValueOrDefault(n.Id)?.Confidence ?? 0) * n.Weight) / wSum);
        }

        var modules = new List<ModuleReadiness>();
        foreach (var (module, own, support) in ModuleMap)
        {
            var ownNodes = catalog.Nodes.Where(n => n.Area == own).ToList();
            var supportNodes = catalog.Nodes.Where(n => support.Contains(n.Area)).ToList();
            var (evidence, evidenceCoverage) = Weighted(ownNodes);
            var (foundation, _) = Weighted(supportNodes);
            var hasEvidence = evidenceCoverage >= MinEvidenceCoverage;

            // The module's own tasks dominate; the foundation contributes, but can never carry the number more than
            // a step above what the learner has actually shown in that module.
            var readiness = hasEvidence
                ? Math.Min(0.7 * evidence + 0.3 * foundation, evidence + FoundationBonus)
                : 0;

            var blockers = ownNodes.Concat(supportNodes).Select(n => (n, s: states.GetValueOrDefault(n.Id)))
                .Where(t => t.s is not null && t.s.Attempts >= 3 && t.s.Mastery < 0.55)
                .OrderBy(t => t.s!.Mastery).Take(3).Select(t => t.n.Title).ToList();
            modules.Add(new ModuleReadiness(module, readiness, evidenceCoverage, blockers, foundation, hasEvidence));
        }

        var measured = modules.Where(m => m.HasEvidence).ToList();
        var overall = measured.Count == 0 ? 0 : measured.Average(m => m.Readiness);
        var verdict = measured.Count == 0
            ? "Noch keine Prüfungsaufgaben bearbeitet. Lesen, Hören, Schreiben und Sprechen zählen erst, wenn du sie geübt hast – Vokabeln und Grammatik allein sagen nichts über die Prüfung."
            : measured.Count < modules.Count
                ? $"Gemessen in {measured.Count} von {modules.Count} Modulen; {string.Join(", ", modules.Where(m => !m.HasEvidence).Select(m => m.Module))} noch ohne Daten. "
                  + (measured.All(m => m.Readiness >= 0.6) ? "Die gemessenen liegen über 60 %." : $"Unter 60 %: {string.Join(", ", measured.Where(m => m.Readiness < 0.6).Select(m => m.Module))}.")
            : modules.All(m => m.Readiness >= 0.7) ? "Auf Kurs: In allen Modulen über der 60-%-Marke mit Puffer. Jetzt Prüfungssimulationen fahren."
            : modules.All(m => m.Readiness >= 0.6) ? "Knapp über der Bestehensgrenze – Schwachstellen gezielt schließen, dann Simulationen."
            : $"Noch nicht prüfungsreif: {string.Join(", ", modules.Where(m => m.Readiness < 0.6).Select(m => m.Module))} unter 60 %.";
        return new ReadinessReport(overall, modules, verdict, measured.Count > 0);
    }

    /// <summary>B1–B2 prior an unknown node is shrunk towards inside a blend. Never displayed on its own.</summary>
    public const double Prior = 0.45;
    /// <summary>Below this confidence-weighted share of a module's own nodes, the module has no number.</summary>
    public const double MinEvidenceCoverage = 0.15;
    /// <summary>The most the grammar/vocabulary foundation may lift a module above its own evidence.</summary>
    public const double FoundationBonus = 0.10;

    /// <summary>
    /// How close a C1-bound learner is to the material above B2 - the C1 nodes and the B2.2 grammar, weighted like
    /// readiness is. Deliberately NOT called "C1 readiness": there is no C1 exam blueprint in this app, so this is a
    /// mastery figure for the upper end of the content, nothing more. Returns null when there is no such content.
    /// </summary>
    public static ModuleReadiness? C1Proximity(ContentCatalog catalog, IReadOnlyDictionary<string, SkillState> states)
    {
        const double prior = Prior;
        double Blend(SkillState? s) => s is null ? prior : s.Confidence * s.Mastery + (1 - s.Confidence) * prior;

        var nodes = catalog.Nodes.Where(n => n.Band >= CefrBand.B2_2).ToList();
        if (nodes.Count == 0) return null;

        var wSum = nodes.Sum(n => n.Weight);
        var value = nodes.Sum(n => Blend(states.GetValueOrDefault(n.Id)) * n.Weight) / wSum;
        var coverage = nodes.Sum(n => (states.GetValueOrDefault(n.Id)?.Confidence ?? 0) * n.Weight) / wSum;
        var blockers = nodes.Select(n => (n, s: states.GetValueOrDefault(n.Id)))
            .Where(t => t.s is not null && t.s.Attempts >= 3 && t.s.Mastery < 0.55)
            .OrderBy(t => t.s!.Mastery).Take(3).Select(t => t.n.Title).ToList();
        // Same honesty as the exam modules: the prior alone is not a number worth showing.
        return new ModuleReadiness("C1-Nähe", value, coverage, blockers, value, coverage >= MinEvidenceCoverage);
    }

    /// <summary>Each exam module: the area that IS the module (its evidence) and the areas that support it.</summary>
    private static readonly (string Module, SkillArea Own, SkillArea[] Support)[] ModuleMap =
    [
        ("Lesen", SkillArea.Lesen, [SkillArea.Wortschatz]),
        ("Hören", SkillArea.Hoeren, [SkillArea.Wortschatz]),
        ("Schreiben", SkillArea.Schreiben, [SkillArea.Grammatik, SkillArea.Redemittel]),
        ("Sprechen", SkillArea.Sprechen, [SkillArea.Grammatik, SkillArea.Redemittel]),
    ];

    /// <summary>Applies an attempt to the node state and manages the weak → recovered → re-check lifecycle.</summary>
    public static void ApplyAttempt(SkillState state, Exercise exercise, double score, DateTime nowUtc)
    {
        var wasWeakBefore = state.IsWeak;
        state.Theta = Ability.Update(state.Theta, state.Attempts, exercise.Band.Difficulty(), score);
        state.Attempts++;
        state.LastPracticedUtc = nowUtc;
        if (score >= 0.7)
        {
            state.Correct++;
            state.CurrentStreak++;
        }
        else
        {
            state.CurrentStreak = 0;
            state.LastErrorUtc = nowUtc;
        }

        if (state.IsWeak) state.WasWeak = true;

        // Recovered from a weak phase: schedule verification instead of trusting the streak.
        if (state.WasWeak && !state.IsWeak && state.Mastery >= 0.7 && state.RecheckDueUtc is null && state.CurrentStreak >= 3)
            state.RecheckDueUtc = nowUtc.AddDays(RecheckIntervalDays(state.RechecksPassed));

        // A pending re-check that has just been served: pass or fail.
        if (state.RecheckDueUtc is not null && state.RecheckDueUtc <= nowUtc)
        {
            if (score >= 0.7)
            {
                state.RechecksPassed++;
                state.RecheckDueUtc = state.RechecksPassed >= 3 ? null : nowUtc.AddDays(RecheckIntervalDays(state.RechecksPassed));
                if (state.RechecksPassed >= 3) state.WasWeak = false; // verified three times: considered learned
            }
            else
            {
                state.RechecksPassed = 0;
                state.RecheckDueUtc = null; // back to normal focus handling; it will resurface as weak
            }
        }

        _ = wasWeakBefore;
    }

    public static int RecheckIntervalDays(int passed) => passed switch { 0 => 7, 1 => 21, _ => 60 };
}
