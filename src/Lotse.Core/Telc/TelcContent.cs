namespace Lotse.Core.Telc;

/// <summary>A multiple-choice question with options a, b, c.</summary>
public sealed record TelcChoice(string Question, IReadOnlyList<string> Options, int Correct, string? Explanation = null);

/// <summary>A right/false statement about a listening text.</summary>
public sealed record TelcStatement(string Text, bool True, string? Explanation = null);

/// <summary>An advertisement or info text in Leseverstehen Teil 3.</summary>
public sealed record TelcAd(string Title, string Text);

/// <summary>
/// One gap in a Sprachbausteine text. Teil 1 carries its three <see cref="Options"/> and the index of the right one;
/// Teil 2 has no options of its own - <see cref="Correct"/> is the index into the text's word box. Every gap names
/// the skill node it tests, so a wrong answer lands in the learner model like any other mistake.
/// </summary>
public sealed record TelcGap(int Correct, string NodeId, IReadOnlyList<string>? Options = null, string? ErrorCode = null, string? Explanation = null);

/// <summary>Leseverstehen Teil 1: ten headings, five texts, <see cref="Solution"/>[i] = heading index of text i.</summary>
public sealed record TelcLesen1(string Id, string Title, IReadOnlyList<string> Headings, IReadOnlyList<string> Texts, IReadOnlyList<int> Solution);

/// <summary>Leseverstehen Teil 2: one longer text and five questions.</summary>
public sealed record TelcLesen2(string Id, string Title, string Text, IReadOnlyList<TelcChoice> Questions);

/// <summary>Leseverstehen Teil 3: ten situations, twelve ads, <see cref="Solution"/>[i] = ad index or -1 for "x".</summary>
public sealed record TelcLesen3(string Id, string Title, IReadOnlyList<string> Situations, IReadOnlyList<TelcAd> Ads, IReadOnlyList<int> Solution);

/// <summary>
/// Sprachbausteine Teil 1 or 2. The text marks gap n as <c>[n]</c>, numbered from 1. Teil 2 has a <see cref="Box"/>
/// of fifteen words.
/// </summary>
public sealed record TelcSprachbausteine(string Id, int Teil, string Title, string Text, IReadOnlyList<TelcGap> Gaps, IReadOnlyList<string>? Box = null);

/// <summary>One recording: a news item, the interview, an announcement. Lines "Name: …" give each speaker a voice.</summary>
public sealed record TelcRecording(string Audio, IReadOnlyList<TelcStatement> Statements, string? Label = null);

/// <summary>Hörverstehen Teil 1 (five news items), 2 (one interview, ten statements) or 3 (five announcements).</summary>
public sealed record TelcHoeren(string Id, int Teil, string Title, IReadOnlyList<TelcRecording> Recordings)
{
    public IEnumerable<TelcStatement> Statements => Recordings.SelectMany(r => r.Statements);
}

/// <param name="Keywords">Lower-case stems; a text mentioning one of them probably addresses the point. Used for a hint, never for the score.</param>
public sealed record TelcLeitpunkt(string Text, IReadOnlyList<string> Keywords);

/// <summary>One of the two writing tasks the candidate chooses between.</summary>
/// <param name="Kind">The task's name in the exam booklet, e.g. "Beschwerde" or "Bitte um Informationen".</param>
/// <param name="Stimulus">The ad or notice the email answers, if any.</param>
public sealed record TelcWritingTask(
    string Id,
    string Kind,
    string Situation,
    string? Stimulus,
    IReadOnlyList<TelcLeitpunkt> Leitpunkte,
    bool Formal,
    string NodeId,
    string? ModelAnswer = null)
{
    /// <summary>The task in a few words: its kind and who the email goes to, from the first line of the ad.</summary>
    public string Short => Stimulus is { } s ? $"{Kind} ({s.Split('\n')[0].Split(" – ")[0].Split(" · ")[0].TrimEnd('!')})" : Kind;
}

/// <summary>Schriftlicher Ausdruck as the exam presents it: task A or task B.</summary>
public sealed record TelcWritingPair(string Id, TelcWritingTask A, TelcWritingTask B);

/// <summary>Mündlicher Ausdruck Teil 1: one topic from the list the candidate prepares at home.</summary>
public sealed record TelcErfahrung(string Id, string Title, IReadOnlyList<string> Stichpunkte, IReadOnlyList<string> PartnerQuestions, IReadOnlyList<string> Redemittel, string? ModelAnswer = null);

/// <summary>Mündlicher Ausdruck Teil 2: the newspaper text and what speaks for and against.</summary>
public sealed record TelcDiskussion(string Id, string Title, string Text, IReadOnlyList<string> Pro, IReadOnlyList<string> Contra, IReadOnlyList<string> Redemittel);

/// <summary>Mündlicher Ausdruck Teil 3: the planning situation and the points a good plan covers.</summary>
public sealed record TelcPlanung(string Id, string Title, string Situation, IReadOnlyList<string> Aspects, IReadOnlyList<string> Redemittel);

/// <summary>
/// Everything in <c>content/telc/</c>. Own tasks written in the exam's format - never copies of telc's protected
/// material - so the format is drilled, not the answers to one published test.
/// </summary>
public sealed record TelcContent(
    IReadOnlyList<TelcLesen1> Lesen1,
    IReadOnlyList<TelcLesen2> Lesen2,
    IReadOnlyList<TelcLesen3> Lesen3,
    IReadOnlyList<TelcSprachbausteine> Sprachbausteine,
    IReadOnlyList<TelcHoeren> Hoeren,
    IReadOnlyList<TelcWritingPair> Schreiben,
    IReadOnlyList<TelcErfahrung> Erfahrungen,
    IReadOnlyList<TelcDiskussion> Diskussionen,
    IReadOnlyList<TelcPlanung> Planungen)
{
    public static TelcContent Empty { get; } = new([], [], [], [], [], [], [], [], []);

    /// <summary>This content followed by <paramref name="extra"/>, part by part - the seed first, so its order stays.</summary>
    public TelcContent With(TelcContent extra) => new(
        [.. Lesen1, .. extra.Lesen1], [.. Lesen2, .. extra.Lesen2], [.. Lesen3, .. extra.Lesen3],
        [.. Sprachbausteine, .. extra.Sprachbausteine], [.. Hoeren, .. extra.Hoeren], [.. Schreiben, .. extra.Schreiben],
        [.. Erfahrungen, .. extra.Erfahrungen], [.. Diskussionen, .. extra.Diskussionen], [.. Planungen, .. extra.Planungen]);

    /// <summary>The parts the tutor can write new tasks for: the objective ones, whose solutions can be checked.</summary>
    public static IReadOnlyList<TelcPart> GeneratableParts { get; } = [.. Enum.GetValues<TelcPart>().Where(TelcBlueprint.IsObjective)];

    /// <summary>A task written by the tutor rather than shipped with the app.</summary>
    public static bool IsGenerated(string taskId) => taskId.StartsWith("gen.", StringComparison.Ordinal);

    /// <summary>Task ids available for one part, in content order.</summary>
    public IReadOnlyList<string> TaskIds(TelcPart part) => part switch
    {
        TelcPart.Lesen1 => Lesen1.Select(t => t.Id).ToList(),
        TelcPart.Lesen2 => Lesen2.Select(t => t.Id).ToList(),
        TelcPart.Lesen3 => Lesen3.Select(t => t.Id).ToList(),
        TelcPart.Sprachbausteine1 => Sprachbausteine.Where(t => t.Teil == 1).Select(t => t.Id).ToList(),
        TelcPart.Sprachbausteine2 => Sprachbausteine.Where(t => t.Teil == 2).Select(t => t.Id).ToList(),
        TelcPart.Hoeren1 => Hoeren.Where(t => t.Teil == 1).Select(t => t.Id).ToList(),
        TelcPart.Hoeren2 => Hoeren.Where(t => t.Teil == 2).Select(t => t.Id).ToList(),
        TelcPart.Hoeren3 => Hoeren.Where(t => t.Teil == 3).Select(t => t.Id).ToList(),
        TelcPart.Schreiben => Schreiben.Select(t => t.Id).ToList(),
        TelcPart.Sprechen1 => Erfahrungen.Select(t => t.Id).ToList(),
        TelcPart.Sprechen2 => Diskussionen.Select(t => t.Id).ToList(),
        _ => Planungen.Select(t => t.Id).ToList(),
    };

    public string? TaskTitle(TelcPart part, string id) => part switch
    {
        TelcPart.Lesen1 => Lesen1.FirstOrDefault(t => t.Id == id)?.Title,
        TelcPart.Lesen2 => Lesen2.FirstOrDefault(t => t.Id == id)?.Title,
        TelcPart.Lesen3 => Lesen3.FirstOrDefault(t => t.Id == id)?.Title,
        TelcPart.Sprachbausteine1 or TelcPart.Sprachbausteine2 => Sprachbausteine.FirstOrDefault(t => t.Id == id)?.Title,
        TelcPart.Hoeren1 or TelcPart.Hoeren2 or TelcPart.Hoeren3 => Hoeren.FirstOrDefault(t => t.Id == id)?.Title,
        TelcPart.Schreiben => Schreiben.FirstOrDefault(t => t.Id == id) is { } w ? $"A: {w.A.Short} · B: {w.B.Short}" : null,
        TelcPart.Sprechen1 => Erfahrungen.FirstOrDefault(t => t.Id == id)?.Title,
        TelcPart.Sprechen2 => Diskussionen.FirstOrDefault(t => t.Id == id)?.Title,
        _ => Planungen.FirstOrDefault(t => t.Id == id)?.Title,
    };

    public TelcLesen1? FindLesen1(string id) => Lesen1.FirstOrDefault(t => t.Id == id);
    public TelcLesen2? FindLesen2(string id) => Lesen2.FirstOrDefault(t => t.Id == id);
    public TelcLesen3? FindLesen3(string id) => Lesen3.FirstOrDefault(t => t.Id == id);
    public TelcSprachbausteine? FindSprachbausteine(string id) => Sprachbausteine.FirstOrDefault(t => t.Id == id);
    public TelcHoeren? FindHoeren(string id) => Hoeren.FirstOrDefault(t => t.Id == id);
    public TelcWritingPair? FindSchreiben(string id) => Schreiben.FirstOrDefault(t => t.Id == id);
    /// <summary>One writing task (A or B) with the pair it belongs to.</summary>
    public (TelcWritingPair Pair, TelcWritingTask Task)? FindWritingTask(string id)
        => Schreiben.Select(p => (p, p.A.Id == id ? p.A : p.B.Id == id ? p.B : null)).Where(x => x.Item2 is not null)
            .Select(x => ((TelcWritingPair, TelcWritingTask)?)(x.p, x.Item2!)).FirstOrDefault();
    public TelcErfahrung? FindErfahrung(string id) => Erfahrungen.FirstOrDefault(t => t.Id == id);
    public TelcDiskussion? FindDiskussion(string id) => Diskussionen.FirstOrDefault(t => t.Id == id);
    public TelcPlanung? FindPlanung(string id) => Planungen.FirstOrDefault(t => t.Id == id);

    /// <summary>The solution key of an objective task, one entry per item: an option index, a box index, -1 for "x", or 1/0 for richtig/falsch.</summary>
    public IReadOnlyList<int>? Key(TelcPart part, string id) => part switch
    {
        TelcPart.Lesen1 => FindLesen1(id)?.Solution,
        TelcPart.Lesen2 => FindLesen2(id)?.Questions.Select(q => q.Correct).ToList(),
        TelcPart.Lesen3 => FindLesen3(id)?.Solution,
        TelcPart.Sprachbausteine1 or TelcPart.Sprachbausteine2 => FindSprachbausteine(id)?.Gaps.Select(g => g.Correct).ToList(),
        TelcPart.Hoeren1 or TelcPart.Hoeren2 or TelcPart.Hoeren3 => FindHoeren(id)?.Statements.Select(s => s.True ? 1 : 0).ToList(),
        _ => null,
    };

    /// <summary>Checks the format rules of every task. Empty = valid.</summary>
    public IReadOnlyList<string> Validate(ISet<string>? knownNodeIds = null, ISet<string>? knownErrorCodes = null)
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) problems.Add("Aufgabe ohne Id");
            else if (!ids.Add(id)) problems.Add($"{id}: Id doppelt");
        }
        void Node(string owner, string nodeId)
        {
            if (knownNodeIds is not null && !knownNodeIds.Contains(nodeId)) problems.Add($"{owner}: unbekannter Knoten '{nodeId}'");
        }

        foreach (var t in Lesen1)
        {
            Id(t.Id);
            if (t.Headings.Count != 10) problems.Add($"{t.Id}: Teil 1 braucht 10 Überschriften, hat {t.Headings.Count}");
            if (t.Texts.Count != 5) problems.Add($"{t.Id}: Teil 1 braucht 5 Texte, hat {t.Texts.Count}");
            if (t.Solution.Count != t.Texts.Count) problems.Add($"{t.Id}: Lösung passt nicht zur Zahl der Texte");
            if (t.Solution.Any(s => s < 0 || s >= t.Headings.Count)) problems.Add($"{t.Id}: Lösung außerhalb der Überschriften");
            if (t.Solution.Distinct().Count() != t.Solution.Count) problems.Add($"{t.Id}: eine Überschrift ist zweimal Lösung");
        }
        foreach (var t in Lesen2)
        {
            Id(t.Id);
            if (t.Questions.Count != 5) problems.Add($"{t.Id}: Teil 2 braucht 5 Aufgaben, hat {t.Questions.Count}");
            foreach (var q in t.Questions.Where(q => q.Options.Count != 3 || q.Correct is < 0 or > 2))
                problems.Add($"{t.Id}: Aufgabe „{q.Question}“ braucht drei Optionen und eine gültige Lösung");
        }
        foreach (var t in Lesen3)
        {
            Id(t.Id);
            if (t.Situations.Count != 10) problems.Add($"{t.Id}: Teil 3 braucht 10 Situationen, hat {t.Situations.Count}");
            if (t.Ads.Count != 12) problems.Add($"{t.Id}: Teil 3 braucht 12 Anzeigen, hat {t.Ads.Count}");
            if (t.Solution.Count != t.Situations.Count) problems.Add($"{t.Id}: Lösung passt nicht zur Zahl der Situationen");
            if (t.Solution.Any(s => s < -1 || s >= t.Ads.Count)) problems.Add($"{t.Id}: Lösung außerhalb der Anzeigen");
            var used = t.Solution.Where(s => s >= 0).ToList();
            if (used.Distinct().Count() != used.Count) problems.Add($"{t.Id}: eine Anzeige ist zweimal Lösung");
        }
        foreach (var t in Sprachbausteine)
        {
            Id(t.Id);
            if (t.Teil is not (1 or 2)) problems.Add($"{t.Id}: Teil muss 1 oder 2 sein");
            if (t.Gaps.Count != 10) problems.Add($"{t.Id}: braucht 10 Lücken, hat {t.Gaps.Count}");
            for (var i = 1; i <= t.Gaps.Count; i++)
                if (!t.Text.Contains($"[{i}]", StringComparison.Ordinal)) problems.Add($"{t.Id}: Lücke [{i}] fehlt im Text");
            if (t.Text.Contains($"[{t.Gaps.Count + 1}]", StringComparison.Ordinal)) problems.Add($"{t.Id}: Text hat mehr Lücken als Lösungen");
            foreach (var g in t.Gaps)
            {
                Node(t.Id, g.NodeId);
                if (g.ErrorCode is not null && knownErrorCodes is not null && !knownErrorCodes.Contains(g.ErrorCode)) problems.Add($"{t.Id}: unbekannter Fehlercode '{g.ErrorCode}'");
            }
            if (t.Teil == 1)
            {
                foreach (var g in t.Gaps.Where(g => g.Options is not { Count: 3 } || g.Correct is < 0 or > 2))
                    problems.Add($"{t.Id}: Lücke mit {g.Options?.Count ?? 0} Optionen oder ungültiger Lösung");
            }
            else
            {
                if (t.Box is not { Count: 15 }) problems.Add($"{t.Id}: Teil 2 braucht einen Kasten mit 15 Wörtern");
                else
                {
                    if (t.Gaps.Any(g => g.Correct < 0 || g.Correct >= t.Box.Count)) problems.Add($"{t.Id}: Lösung außerhalb des Kastens");
                    if (t.Gaps.Select(g => g.Correct).Distinct().Count() != t.Gaps.Count) problems.Add($"{t.Id}: ein Kastenwort ist zweimal Lösung");
                    if (t.Box.Distinct(StringComparer.OrdinalIgnoreCase).Count() != t.Box.Count) problems.Add($"{t.Id}: Kastenwort doppelt");
                }
            }
        }
        foreach (var t in Hoeren)
        {
            Id(t.Id);
            var (recordings, perRecording) = t.Teil switch { 1 => (5, 1), 2 => (1, 10), 3 => (5, 1), _ => (0, 0) };
            if (recordings == 0) problems.Add($"{t.Id}: Teil muss 1, 2 oder 3 sein");
            else if (t.Recordings.Count != recordings || t.Recordings.Any(r => r.Statements.Count != perRecording))
                problems.Add($"{t.Id}: Hören Teil {t.Teil} braucht {recordings} Aufnahme(n) mit je {perRecording} Aussage(n)");
            if (t.Recordings.Any(r => string.IsNullOrWhiteSpace(r.Audio))) problems.Add($"{t.Id}: Aufnahme ohne Text");
            var statements = t.Statements.ToList();
            if (statements.Count >= 5 && (statements.All(s => s.True) || statements.All(s => !s.True)))
                problems.Add($"{t.Id}: alle Aussagen gleich - richtig und falsch müssen gemischt sein");
        }
        foreach (var p in Schreiben)
        {
            Id(p.Id);
            foreach (var w in new[] { p.A, p.B })
            {
                Id(w.Id);
                Node(w.Id, w.NodeId);
                if (w.Leitpunkte.Count != 4) problems.Add($"{w.Id}: braucht 4 Leitpunkte, hat {w.Leitpunkte.Count}");
                if (w.Leitpunkte.Any(l => l.Keywords.Count == 0)) problems.Add($"{w.Id}: Leitpunkt ohne Stichwörter");
                if (w.ModelAnswer is { } m && TelcWritingCheck.CountWords(m) < TelcBlueprint.WritingMinWords)
                    problems.Add($"{w.Id}: Musterlösung hat weniger als {TelcBlueprint.WritingMinWords} Wörter");
            }
        }
        foreach (var e in Erfahrungen)
        {
            Id(e.Id);
            if (e.Stichpunkte.Count < 3) problems.Add($"{e.Id}: mindestens 3 Stichpunkte");
            if (e.PartnerQuestions.Count < 3) problems.Add($"{e.Id}: mindestens 3 Partnerfragen");
        }
        foreach (var d in Diskussionen)
        {
            Id(d.Id);
            if (d.Pro.Count < 2 || d.Contra.Count < 2) problems.Add($"{d.Id}: je mindestens 2 Pro- und Contra-Argumente");
            if (TelcWritingCheck.CountWords(d.Text) is < 100 or > 350) problems.Add($"{d.Id}: Text sollte 100–350 Wörter haben");
        }
        foreach (var p in Planungen)
        {
            Id(p.Id);
            if (p.Aspects.Count < 4) problems.Add($"{p.Id}: mindestens 4 Planungspunkte");
        }
        return problems;
    }
}

/// <summary>Turns objective answers into points, so the practice page and the simulation count identically.</summary>
public static class TelcObjectiveScoring
{
    /// <summary>One answer per item; null = left empty. Returns which items were right.</summary>
    public static IReadOnlyList<bool> Check(IReadOnlyList<int> key, IReadOnlyList<int?> answers)
        => key.Select((k, i) => i < answers.Count && answers[i] == k).ToList();

    public static double Points(TelcPart part, IReadOnlyList<int> key, IReadOnlyList<int?> answers)
        => TelcBlueprint.ObjectivePoints(part, Check(key, answers).Count(c => c));

    /// <summary>Letter of an option or box word: 0 → a.</summary>
    public static string Letter(int index) => index < 0 ? "x" : ((char)('a' + index)).ToString();
}

