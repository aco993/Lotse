using Lotse.Core.Model;

namespace Lotse.Core.Engine;

public enum StepKind
{
    /// <summary>Spaced-repetition review that is due.</summary>
    Review,
    /// <summary>Targeted practice on a currently weak node.</summary>
    Focus,
    /// <summary>Re-test of a node that used to be weak, to verify it really stuck.</summary>
    Recheck,
    /// <summary>Free writing or speaking.</summary>
    Production,
    /// <summary>Reading or listening input.</summary>
    Input,
    /// <summary>Exploration of a node we know little about.</summary>
    Explore,
}

public sealed record SessionStep(StepKind Kind, Exercise Exercise, string Reason);

public sealed record SessionPlan(IReadOnlyList<SessionStep> Steps, string Summary)
{
    public int EstimatedSeconds => Steps.Sum(s => s.Exercise.EstimatedSeconds);
}

/// <summary>Everything the planner needs to know about the learner right now. Built by the application layer from the database.</summary>
public sealed record PlannerInput
{
    public required int TimeBudgetMinutes { get; init; }
    public required DateTime NowUtc { get; init; }
    public required ContentCatalog Catalog { get; init; }
    public required IReadOnlyDictionary<string, SkillState> SkillStates { get; init; }
    /// <summary>Review states that are due (DueUtc &lt;= now), any order.</summary>
    public required IReadOnlyList<ReviewState> DueReviews { get; init; }
    /// <summary>Exercise ids seen in the last days; the planner avoids them unless they are due reviews.</summary>
    public IReadOnlySet<string> RecentExerciseIds { get; init; } = new HashSet<string>();
    /// <summary>Error events of the last 30 days, used to prioritise nodes with fresh mistakes.</summary>
    public IReadOnlyList<ErrorEvent> RecentErrors { get; init; } = [];
    public int SessionsCompleted { get; init; }
    public DateTime? LastProductionUtc { get; init; }
    public DateTime? LastInputUtc { get; init; }
    /// <summary>Optional node the learner explicitly wants to work on.</summary>
    public string? RequestedNodeId { get; init; }
    /// <summary>How far above B2 the focus ranking may reach. Defaults to B2, so existing plans are unchanged.</summary>
    public TargetLevel TargetLevel { get; init; } = TargetLevel.B2;
    /// <summary>The learner's field. Unspecified (the default) leaves every ranking and tie-break exactly as it was.</summary>
    public Occupation Occupation { get; init; } = Occupation.Unspecified;
    /// <summary>Seed for the tie-breaking randomness so plans are reproducible in tests.</summary>
    public int Seed { get; init; } = Environment.TickCount;
}

/// <summary>
/// Composes a session for a time budget. The order of business is fixed and deliberate:
/// 1) re-checks of formerly weak nodes (highest information value), 2) due reviews, 3) one production task
/// (the learner's main gap is active production), 4) focused drills on the weakest nodes, 5) input when there is time.
/// Every step carries a human-readable reason so the adaptation is transparent to the learner.
/// </summary>
public sealed class SessionPlanner
{
    private const double PriorTheta = -0.2; // a self-declared B1–B2 learner before any data

    public SessionPlan Plan(PlannerInput input)
    {
        if (input.RequestedNodeId is not null && input.Catalog.Node(input.RequestedNodeId) is not null)
            return PlanFocus(input);

        var rng = new Random(input.Seed);
        var budget = input.TimeBudgetMinutes * 60;
        var steps = new List<SessionStep>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var catalog = input.Catalog;
        var now = input.NowUtc;

        double Remaining() => budget - steps.Sum(s => s.Exercise.EstimatedSeconds);
        bool Fits(Exercise e) => Remaining() >= e.EstimatedSeconds * 0.8; // allow a slight overrun instead of an empty slot

        // ---- 1. Re-checks -------------------------------------------------------------------------
        var recheckNodes = input.SkillStates.Values
            .Where(s => s.RecheckDueUtc is not null && s.RecheckDueUtc <= now)
            .OrderBy(s => s.RecheckDueUtc)
            .Take(2);
        foreach (var node in recheckNodes)
        {
            var ex = PickDrill(catalog, node.NodeId, CefrBand.B2_1.Difficulty(), used, input.RecentExerciseIds, rng, avoidRecent: false);
            if (ex is null || !Fits(ex)) continue;
            Add(StepKind.Recheck, ex, $"Wiedervorlage: „{catalog.NodeTitle(node.NodeId)}“ war früher eine Schwachstelle – sitzt es noch?");
        }

        // ---- 2. Due reviews (max ~40 % of the budget) ---------------------------------------------
        var reviewBudget = budget * 0.4;
        var dueOrdered = input.DueReviews
            .Select(r => (State: r, Exercise: catalog.Exercise(r.ExerciseId)))
            .Where(t => t.Exercise is not null && !t.Exercise.IsProduction && t.Exercise.Type != ExerciseType.Reading)
            .OrderByDescending(t => (now - t.State.DueUtc).TotalHours + t.State.Lapses * 24)
            .ToList();
        double reviewUsed = 0;
        foreach (var (state, ex) in dueOrdered)
        {
            if (reviewUsed + ex!.EstimatedSeconds > reviewBudget) break;
            if (!used.Add(ex.Id)) continue;
            var overdueDays = Math.Max(0, (int)(now - state.DueUtc).TotalDays);
            var why = state.Lapses > 0
                ? $"Wiederholung: hier lagst du schon {state.Lapses}× daneben."
                : overdueDays > 0 ? $"Wiederholung fällig (seit {overdueDays} Tag{(overdueDays == 1 ? "" : "en")})." : "Wiederholung fällig.";
            steps.Add(new SessionStep(StepKind.Review, ex, why));
            reviewUsed += ex.EstimatedSeconds;
        }

        // ---- 3. Production ------------------------------------------------------------------------
        var daysSinceProduction = input.LastProductionUtc is null ? 99 : (now - input.LastProductionUtc.Value).TotalDays;
        var wantsProduction = input.TimeBudgetMinutes >= 10 || daysSinceProduction >= 2 || input.SessionsCompleted % 3 == 2;
        if (wantsProduction)
        {
            var preferSpeaking = input.SessionsCompleted % 2 == 1;
            var maxSeconds = input.TimeBudgetMinutes switch { <= 5 => 150, <= 10 => 240, _ => 420 };
            // The exam's own writing tasks (Teil 1: 150 words) estimate at 600 s and never fitted under 420, so the
            // single most valuable production in the bank was reachable only by hand from /pruefung. From twenty
            // minutes on, an exam-format task may take the slot - at fifteen it would, together with the reading
            // share below, leave nothing for the drills.
            var examSeconds = input.TimeBudgetMinutes >= 20 ? 600 : maxSeconds;
            var production = PickProduction(catalog, input, used, rng, preferSpeaking, maxSeconds, examSeconds);
            if (production is not null && Fits(production))
            {
                var kind = production.Type == ExerciseType.Speak ? "Sprechen" : "Schreiben";
                Add(StepKind.Production, production, $"{kind}: Aktives Produzieren ist deine größte Lücke – lieber kurz und täglich als selten und lang.");
            }
        }

        // ---- 4. Input (reading / listening) BEFORE the drills take the rest -------------------------
        // This block used to come after the focus drills, which run until less than 20 s remain - so reading and
        // listening only ever got leftovers, and a 20-minute plan for a new learner had none at all. The exam's
        // Lesen and Hören are thirty items each; one receptive item per session, if any, exhausted the bank in
        // about five weeks without building either stamina. Longer sessions now reserve two or three slots here,
        // alternating reading and listening where time allows.
        var daysSinceInput = input.LastInputUtc is null ? 99 : (now - input.LastInputUtc.Value).TotalDays;
        if (Remaining() >= 120 && (input.TimeBudgetMinutes >= 15 || daysSinceInput >= 3))
        {
            // Reading and listening get a share of the session, not the session: about a third, so a 20-minute
            // plan carries one text and two dictations (300 + 40 + 40 s) and still has its drills.
            var inputBudget = budget * 0.35;
            var inputSlots = input.TimeBudgetMinutes >= 20 ? 3 : input.TimeBudgetMinutes >= 15 ? 2 : 1;
            for (var slot = 0; slot < inputSlots && Remaining() >= 120; slot++)
            {
                var inputLeft = inputBudget - steps.Where(s => s.Kind == StepKind.Input).Sum(s => s.Exercise.EstimatedSeconds);
                var preferReading = slot == 0 && inputLeft >= 300;
                var inputEx = catalog.Exercises
                    .Where(e => e.IsReceptive && !used.Contains(e.Id) && !input.RecentExerciseIds.Contains(e.Id) && e.EstimatedSeconds <= inputLeft)
                    .OrderBy(e => (e.Type == ExerciseType.Reading) == preferReading ? 0 : 1)
                    .ThenBy(_ => rng.Next())
                    .FirstOrDefault(e => Fits(e));
                if (inputEx is null) break;
                Add(StepKind.Input, inputEx, inputEx.Type == ExerciseType.Reading
                    ? "Lesen: Prüfungsformat trainieren und Wortschatz im Kontext sehen."
                    : "Hören: Diktat schult Hörverstehen und Rechtschreibung zugleich.");
            }
        }

        // ---- 5. Focus drills on the weakest nodes -------------------------------------------------
        var focusNodes = RankFocusNodes(input).ToList();
        if (input.RequestedNodeId is not null && catalog.Node(input.RequestedNodeId) is not null)
            focusNodes.Insert(0, (input.RequestedNodeId, 99, "Dein Wunschthema."));

        var perNodeCap = input.TimeBudgetMinutes <= 5 ? 3 : 4;
        foreach (var (nodeId, _, why) in focusNodes)
        {
            if (Remaining() < 20) break;
            var state = input.SkillStates.GetValueOrDefault(nodeId);
            var theta = state?.Theta ?? PriorTheta;
            var target = Ability.TargetDifficulty(theta);
            var placed = 0;
            while (placed < perNodeCap)
            {
                var ex = PickDrill(catalog, nodeId, target, used, input.RecentExerciseIds, rng, avoidRecent: true, input.Occupation);
                if (ex is null || !Fits(ex)) break;
                var kind = state is null || state.Attempts < 3 ? StepKind.Explore : StepKind.Focus;
                Add(kind, ex, why);
                placed++;
                // one B2 item slightly above target keeps the drill from feeling flat
                target += 0.15;
            }
            if (Remaining() < 20) break;
        }

        // ---- 6. Fill leftover time with short drills, even for new learners with no history --------
        var guard = 0;
        while (Remaining() >= 25 && guard++ < 20)
        {
            var filler = catalog.Exercises
                .Where(e => !e.IsProduction && !e.IsReceptive && !used.Contains(e.Id) && !input.RecentExerciseIds.Contains(e.Id))
                .OrderBy(e => Math.Abs(e.Band.Difficulty() - Ability.TargetDifficulty(input.SkillStates.GetValueOrDefault(e.NodeId)?.Theta ?? PriorTheta)))
                .ThenByDescending(e => input.Occupation.Fit(e))
                .ThenBy(_ => rng.Next())
                .FirstOrDefault(Fits);
            if (filler is null) break;
            var st = input.SkillStates.GetValueOrDefault(filler.NodeId);
            Add(st is null || st.Attempts < 3 ? StepKind.Explore : StepKind.Focus, filler,
                st is null || st.Attempts < 3 ? $"Erkundung: Wie sicher bist du bei „{catalog.NodeTitle(filler.NodeId)}“?" : $"Übung: „{catalog.NodeTitle(filler.NodeId)}“.");
        }

        var ordered = Interleave(steps);
        return new SessionPlan(ordered, BuildSummary(ordered, catalog));

        void Add(StepKind kind, Exercise ex, string reason)
        {
            used.Add(ex.Id);
            steps.Add(new SessionStep(kind, ex, reason));
        }
    }

    /// <summary>
    /// A session the learner asked for ("this topic, now - I have a meeting"): the whole budget goes to that node,
    /// climbing from just below the learner's level to just above it, with the node's own due reviews first.
    /// No re-checks, no production, no other topics - the daily session does those.
    /// </summary>
    private SessionPlan PlanFocus(PlannerInput input)
    {
        var rng = new Random(input.Seed);
        var budget = input.TimeBudgetMinutes * 60;
        var steps = new List<SessionStep>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var catalog = input.Catalog;
        var nodeId = input.RequestedNodeId!;
        var title = catalog.NodeTitle(nodeId);
        var state = input.SkillStates.GetValueOrDefault(nodeId);
        var target = Ability.TargetDifficulty(state?.Theta ?? PriorTheta) - 0.15;

        double Remaining() => budget - steps.Sum(s => s.Exercise.EstimatedSeconds);

        foreach (var r in input.DueReviews.Where(r => r.NodeId == nodeId).OrderBy(r => r.DueUtc))
        {
            var ex = catalog.Exercise(r.ExerciseId);
            if (ex is null || ex.IsProduction || ex.IsReceptive || !used.Add(ex.Id)) continue;
            if (Remaining() < ex.EstimatedSeconds * 0.8) break;
            steps.Add(new SessionStep(StepKind.Review, ex, $"Wiederholung fällig – {title}."));
        }

        var guard = 0;
        while (Remaining() >= 20 && guard++ < 40)
        {
            var ex = PickDrill(catalog, nodeId, target, used, input.RecentExerciseIds, rng, avoidRecent: steps.Count < 6);
            if (ex is null || Remaining() < ex.EstimatedSeconds * 0.8) break;
            var kind = state is null || state.Attempts < 3 ? StepKind.Explore : StepKind.Focus;
            steps.Add(new SessionStep(kind, ex, $"Dein Wunschthema: {title}."));
            used.Add(ex.Id);
            target += 0.12; // a gentle climb keeps the focus session from feeling flat
        }

        var summary = state is null || state.Attempts == 0
            ? $"Fokus: {title} – {steps.Count} Aufgaben, wir schauen erst mal, wo du stehst."
            : $"Fokus: {title} – {steps.Count} Aufgaben rund um deine aktuelle Stufe (Beherrschung {state.Mastery:P0}).";
        return new SessionPlan(steps, summary);
    }

    /// <summary>Ranks nodes by how much attention they need right now, with a reason string for the UI.</summary>
    public IEnumerable<(string NodeId, double Priority, string Reason)> RankFocusNodes(PlannerInput input)
    {
        var catalog = input.Catalog;
        var errors7d = input.RecentErrors.Where(e => (input.NowUtc - e.Utc).TotalDays <= 7).GroupBy(e => e.NodeId).ToDictionary(g => g.Key, g => g.Count());

        var ranked = new List<(string, double, string)>();
        // The cap is the one place the target level acts: a B2 learner never sees a C1 node in focus, a C1 learner
        // reaches WS.C1_GEHOBEN and the B2.2 grammar. TargetDifficulty is untouched - aiming higher must not mean
        // being handed items far above the measured ability.
        var bandCap = input.TargetLevel.FocusCap();
        foreach (var node in catalog.Nodes.Where(n => n.Area is SkillArea.Grammatik or SkillArea.Wortschatz or SkillArea.Redemittel && n.Band <= bandCap))
        {
            if (!catalog.ForNode(node.Id).Any(e => !e.IsProduction && !e.IsReceptive)) continue;
            var state = input.SkillStates.GetValueOrDefault(node.Id);
            var mastery = state?.Mastery ?? 0.5;
            var confidence = state?.Confidence ?? 0;
            var daysSince = state?.LastPracticedUtc is null ? 30 : (input.NowUtc - state.LastPracticedUtc.Value).TotalDays;
            var freshErrors = errors7d.GetValueOrDefault(node.Id);

            // A nudge, not a rewrite: 0.15 can lift a node past a near neighbour, never past a real weakness.
            var occupationFits = input.Occupation.PreferredNodes().Contains(node.Id);

            var priority = (1 - mastery) * node.Weight * (0.5 + 0.5 * confidence) // known weakness, trusted more with evidence
                         + (confidence < 0.25 ? 0.35 : 0)                        // exploration bonus for unknown nodes
                         + Math.Min(0.3, daysSince / 14.0 * 0.3)                  // spacing across topics
                         + Math.Min(0.3, freshErrors * 0.1)                       // fresh mistakes
                         + (node.SerbianInterference ? 0.05 : 0)
                         + (occupationFits ? OccupationExtensions.NodeBoost : 0);

            var reason = state is null || state.Attempts < 3
                ? $"Erkundung: Wie sicher bist du bei „{node.Title}“?"
                : freshErrors > 0
                    ? $"Schwerpunkt „{node.Title}“: {freshErrors} Fehler in den letzten 7 Tagen (Beherrschung {mastery:P0})."
                    : $"Schwerpunkt „{node.Title}“: Beherrschung {mastery:P0}.";
            // Say it when it mattered - an adaptation the learner cannot see is indistinguishable from a whim.
            if (occupationFits) reason += $" Vorgezogen, {input.Occupation.ReasonTail()}.";
            ranked.Add((node.Id, priority, reason));
        }
        return ranked.OrderByDescending(r => r.Item2);
    }

    private static Exercise? PickDrill(ContentCatalog catalog, string nodeId, double targetDifficulty, ISet<string> used,
        IReadOnlySet<string> recent, Random rng, bool avoidRecent, Occupation occupation = Occupation.Unspecified)
    {
        var candidates = catalog.ForNode(nodeId)
            .Where(e => !e.IsProduction && e.Type != ExerciseType.Reading && !used.Contains(e.Id))
            .ToList();
        if (candidates.Count == 0) return null;

        var fresh = avoidRecent ? candidates.Where(e => !recent.Contains(e.Id)).ToList() : candidates;
        if (fresh.Count == 0) fresh = candidates;

        return fresh
            .OrderBy(e => Math.Abs(e.Band.Difficulty() - targetDifficulty))
            .ThenBy(e => TypePreference(e.Type))
            // Occupation is the last word before chance, never before fit or pedagogy. With Unspecified the key is
            // constant, so the ordering - and the sequence of rng draws - is bit for bit the old one.
            .ThenByDescending(e => occupation.Fit(e))
            .ThenBy(_ => rng.Next())
            .First();
    }

    /// <summary>Production-oriented types first: recognising is not this learner's bottleneck.</summary>
    private static int TypePreference(ExerciseType t) => t switch
    {
        ExerciseType.Translate => 0,
        ExerciseType.Transform => 0,
        ExerciseType.Cloze => 1,
        ExerciseType.WordOrder => 1,
        ExerciseType.Dialogue => 1,
        ExerciseType.SpotError => 1,
        ExerciseType.Dictation => 2,
        ExerciseType.Vocab => 2,
        ExerciseType.Match => 2,
        ExerciseType.MultipleChoice => 3,
        _ => 4,
    };

    private static Exercise? PickProduction(ContentCatalog catalog, PlannerInput input, ISet<string> used, Random rng, bool preferSpeaking, int maxSeconds, int examSeconds)
    {
        var writing = input.SkillStates.Values.Where(s => catalog.Node(s.NodeId)?.Area == SkillArea.Schreiben).Select(s => s.Mastery).DefaultIfEmpty(0.5).Average();
        var speaking = input.SkillStates.Values.Where(s => catalog.Node(s.NodeId)?.Area == SkillArea.Sprechen).Select(s => s.Mastery).DefaultIfEmpty(0.5).Average();
        var wantSpeak = preferSpeaking ? speaking <= writing + 0.15 : speaking < writing - 0.15;

        Exercise? Pick(ExerciseType type) => catalog.Exercises
            .Where(e => e.Type == type && !used.Contains(e.Id)
                        && e.EstimatedSeconds <= (e.Context == ExerciseContext.Pruefung ? examSeconds : maxSeconds))
            .OrderBy(e => input.RecentExerciseIds.Contains(e.Id) ? 1 : 0)
            .ThenBy(_ => rng.Next())
            .FirstOrDefault();

        return (wantSpeak ? Pick(ExerciseType.Speak) : Pick(ExerciseType.FreeWrite))
            ?? Pick(ExerciseType.FreeWrite) ?? Pick(ExerciseType.Speak);
    }

    /// <summary>Warm-up first, production in the middle, never three items of the same node in a row, end with something short.</summary>
    private static IReadOnlyList<SessionStep> Interleave(List<SessionStep> steps)
    {
        if (steps.Count <= 2) return steps;
        var production = steps.Where(s => s.Kind == StepKind.Production).ToList();
        var input = steps.Where(s => s.Kind == StepKind.Input).ToList();
        var rest = steps.Where(s => s.Kind is not (StepKind.Production or StepKind.Input)).ToList();

        // spread same-node items apart
        var spread = new List<SessionStep>();
        var pool = new List<SessionStep>(rest);
        while (pool.Count > 0)
        {
            var lastNode = spread.Count > 0 ? spread[^1].Exercise.NodeId : null;
            var next = pool.FirstOrDefault(s => s.Exercise.NodeId != lastNode) ?? pool[0];
            pool.Remove(next);
            spread.Add(next);
        }

        var result = new List<SessionStep>();
        var mid = Math.Max(1, spread.Count * 2 / 3);
        result.AddRange(spread.Take(mid));
        result.AddRange(input);
        result.AddRange(production);
        result.AddRange(spread.Skip(mid));
        return result;
    }

    private static string BuildSummary(IReadOnlyList<SessionStep> steps, ContentCatalog catalog)
    {
        if (steps.Count == 0) return "Keine passenden Aufgaben gefunden.";
        var parts = new List<string>();
        var reviews = steps.Count(s => s.Kind == StepKind.Review);
        var rechecks = steps.Count(s => s.Kind == StepKind.Recheck);
        // The summary is a suggestion to the learner ("Der Lotse schlägt vor …"), so it speaks plainly; the precise
        // term is one click away on Themen.
        var focus = steps.Where(s => s.Kind is StepKind.Focus or StepKind.Explore).Select(s => catalog.NodePlainTitle(s.Exercise.NodeId)).Distinct().Take(3).ToList();
        if (rechecks > 0) parts.Add($"{rechecks} Wiedervorlage{(rechecks == 1 ? "" : "n")}");
        if (reviews > 0) parts.Add($"{reviews} Wiederholung{(reviews == 1 ? "" : "en")}");
        if (focus.Count > 0) parts.Add($"Schwerpunkt: {string.Join(", ", focus)}");
        if (steps.Any(s => s.Kind == StepKind.Production)) parts.Add(steps.First(s => s.Kind == StepKind.Production).Exercise.Type == ExerciseType.Speak ? "eine Sprechaufgabe" : "eine Schreibaufgabe");
        if (steps.Any(s => s.Kind == StepKind.Input)) parts.Add("Lesen/Hören");
        return string.Join(" · ", parts);
    }
}
