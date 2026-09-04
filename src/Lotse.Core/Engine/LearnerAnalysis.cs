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
    SkillArea Area,
    double Mastery,
    double Confidence,
    int Errors7d,
    int Errors30d,
    IReadOnlyList<(string Code, string Title, int Count)> TopErrors,
    Trend Trend,
    string? InterferenceNote);

public sealed record ModuleReadiness(string Module, double Readiness, double Coverage, IReadOnlyList<string> Blockers);

public sealed record ReadinessReport(
    double Overall,
    IReadOnlyList<ModuleReadiness> Modules,
    /// <summary>Plain-language verdict shown on the dashboard.</summary>
    string Verdict);

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
            result.Add((new WeakArea(node.Id, node.Title, node.Area, mastery, confidence, n7, n30, top, trend, node.InterferenceNote), priority));
        }
        return result.OrderByDescending(r => r.Priority).Take(take).Select(r => r.Area).ToList();
    }

    public static IReadOnlyList<(string Code, string Title, int Count, string NodeTitle)> TopErrorCodes(ContentCatalog catalog, IReadOnlyList<ErrorEvent> errors, DateTime nowUtc, int days = 30, int take = 6)
        => errors.Where(e => (nowUtc - e.Utc).TotalDays <= days)
            .GroupBy(e => e.Code)
            .Select(g => (g.Key, catalog.Error(g.Key)?.Title ?? g.Key, g.Count(), catalog.NodeTitle(g.First().NodeId)))
            .OrderByDescending(t => t.Item3).Take(take).ToList();

    /// <summary>
    /// Estimates readiness per exam module. Grammar/vocabulary nodes feed the productive modules; receptive nodes feed
    /// Lesen/Hören. Unknown nodes are shrunk towards a B1–B2 prior instead of being ignored, so an untested learner
    /// does not look either perfect or hopeless.
    /// </summary>
    public static ReadinessReport Readiness(ContentCatalog catalog, IReadOnlyDictionary<string, SkillState> states)
    {
        const double prior = 0.45;
        double Blend(SkillState? s) => s is null ? prior : s.Confidence * s.Mastery + (1 - s.Confidence) * prior;

        var modules = new List<ModuleReadiness>();
        foreach (var (module, areas) in ModuleMap)
        {
            var nodes = catalog.Nodes.Where(n => areas.Contains(n.Area)).ToList();
            if (nodes.Count == 0) { modules.Add(new ModuleReadiness(module, prior, 0, [])); continue; }
            var wSum = nodes.Sum(n => n.Weight);
            var readiness = nodes.Sum(n => Blend(states.GetValueOrDefault(n.Id)) * n.Weight) / wSum;
            var coverage = nodes.Sum(n => (states.GetValueOrDefault(n.Id)?.Confidence ?? 0) * n.Weight) / wSum;
            var blockers = nodes.Select(n => (n, s: states.GetValueOrDefault(n.Id)))
                .Where(t => t.s is not null && t.s.Attempts >= 3 && t.s.Mastery < 0.55)
                .OrderBy(t => t.s!.Mastery).Take(3).Select(t => t.n.Title).ToList();
            modules.Add(new ModuleReadiness(module, readiness, coverage, blockers));
        }

        var overall = modules.Average(m => m.Readiness);
        var minCoverage = modules.Min(m => m.Coverage);
        var verdict = minCoverage < 0.15
            ? "Noch zu wenig Daten – mach die Einstufung und ein paar Sessions, dann wird die Prognose belastbar."
            : modules.All(m => m.Readiness >= 0.7) ? "Auf Kurs: In allen Modulen über der 60-%-Marke mit Puffer. Jetzt Prüfungssimulationen fahren."
            : modules.All(m => m.Readiness >= 0.6) ? "Knapp über der Bestehensgrenze – Schwachstellen gezielt schließen, dann Simulationen."
            : $"Noch nicht prüfungsreif: {string.Join(", ", modules.Where(m => m.Readiness < 0.6).Select(m => m.Module))} unter 60 %.";
        return new ReadinessReport(overall, modules, verdict);
    }

    /// <summary>
    /// How close a C1-bound learner is to the material above B2 - the C1 nodes and the B2.2 grammar, weighted like
    /// readiness is. Deliberately NOT called "C1 readiness": there is no C1 exam blueprint in this app, so this is a
    /// mastery figure for the upper end of the content, nothing more. Returns null when there is no such content.
    /// </summary>
    public static ModuleReadiness? C1Proximity(ContentCatalog catalog, IReadOnlyDictionary<string, SkillState> states)
    {
        const double prior = 0.45;
        double Blend(SkillState? s) => s is null ? prior : s.Confidence * s.Mastery + (1 - s.Confidence) * prior;

        var nodes = catalog.Nodes.Where(n => n.Band >= CefrBand.B2_2).ToList();
        if (nodes.Count == 0) return null;

        var wSum = nodes.Sum(n => n.Weight);
        var value = nodes.Sum(n => Blend(states.GetValueOrDefault(n.Id)) * n.Weight) / wSum;
        var coverage = nodes.Sum(n => (states.GetValueOrDefault(n.Id)?.Confidence ?? 0) * n.Weight) / wSum;
        var blockers = nodes.Select(n => (n, s: states.GetValueOrDefault(n.Id)))
            .Where(t => t.s is not null && t.s.Attempts >= 3 && t.s.Mastery < 0.55)
            .OrderBy(t => t.s!.Mastery).Take(3).Select(t => t.n.Title).ToList();
        return new ModuleReadiness("C1-Nähe", value, coverage, blockers);
    }

    private static readonly (string Module, SkillArea[] Areas)[] ModuleMap =
    [
        ("Lesen", [SkillArea.Lesen, SkillArea.Wortschatz]),
        ("Hören", [SkillArea.Hoeren, SkillArea.Wortschatz]),
        ("Schreiben", [SkillArea.Schreiben, SkillArea.Grammatik, SkillArea.Redemittel]),
        ("Sprechen", [SkillArea.Sprechen, SkillArea.Grammatik, SkillArea.Redemittel]),
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
