namespace Lotse.Core.Telc;

public enum TelcPhase
{
    /// <summary>More than six weeks out: the language itself - training, weak grammar, speaking every week.</summary>
    Aufbau,
    /// <summary>Six to two weeks out: every part in exam format, a full simulation every two weeks.</summary>
    Pruefungsformat,
    /// <summary>The last two weeks: timing, the weakest parts, the oral exam daily, one last simulation.</summary>
    Endspurt,
    /// <summary>The last two days: nothing new, only the prepared Teil-1 topics and sleep.</summary>
    Ruhe,
}

/// <summary>One week of the plan. <see cref="Simulation"/> marks the week that ends with a full simulation.</summary>
public sealed record TelcPlanWeek(DateOnly Start, DateOnly End, TelcPhase Phase, string Title, IReadOnlyList<string> Tasks, bool Simulation);

/// <summary>
/// The plan back from the exam date. Each week gets the phase its distance from the exam gives it, and the tasks of
/// that phase aimed at the current <see cref="TelcReadiness.Priorities"/>. A simulation sits every second week in
/// the exam-format phase and once more about a week before the exam - late enough to measure, early enough to
/// still fix what it shows. Without an exam date there is no plan, only the priorities.
/// </summary>
public static class TelcStudyPlan
{
    public const int FormatWeeks = 6;
    public const int FinalDays = 14;
    public const int RestDays = 2;
    /// <summary>Weeks shown at most; a date half a year away does not need 26 identical rows.</summary>
    public const int MaxWeeks = 12;

    public static TelcPhase PhaseFor(int daysToExam) => daysToExam switch
    {
        <= RestDays => TelcPhase.Ruhe,
        <= FinalDays => TelcPhase.Endspurt,
        <= FormatWeeks * 7 => TelcPhase.Pruefungsformat,
        _ => TelcPhase.Aufbau,
    };

    public static IReadOnlyList<TelcPlanWeek> Build(DateOnly today, DateOnly? examDate, TelcReadiness readiness)
    {
        if (examDate is not { } exam || exam < today) return [];

        var focus = readiness.Priorities().Take(3).Select(p => TelcBlueprint.Info(p.Part).Title).ToList();
        var focusText = focus.Count > 0 ? string.Join(", ", focus) : "alle Teile";
        var weeks = new List<TelcPlanWeek>();
        var start = today;
        var formatWeekIndex = 0;
        while (start <= exam && weeks.Count < MaxWeeks)
        {
            var end = start.AddDays(6) < exam ? start.AddDays(6) : exam;
            var daysLeft = exam.DayNumber - end.DayNumber;
            var phase = PhaseFor(exam.DayNumber - start.DayNumber);
            bool simulation;
            List<string> tasks;
            string title;
            switch (phase)
            {
                case TelcPhase.Aufbau:
                    title = "Aufbau: Sprache vor Format";
                    simulation = weeks.Count == 0; // one baseline simulation at the very start
                    tasks =
                    [
                        "3× B2-Training (Wiederholung, Schwerpunkt, Sprechen)",
                        "2× Sprachbausteine (Teil 1 und 2) - die Lücken treffen Verben mit Präposition, Konnektoren, Kollokationen",
                        "1× Mündlich Teil 1 mit 4/3/2: ein Thema dreimal, jedes Mal kürzer",
                        $"Schwerpunkt dieser Woche: {focusText}",
                    ];
                    if (simulation) tasks.Insert(0, "Einstiegssimulation: alle Teile einmal, damit die Prognose auf Daten steht");
                    break;
                case TelcPhase.Pruefungsformat:
                    title = "Prüfungsformat: jeder Teil unter Zeit";
                    simulation = formatWeekIndex++ % 2 == 1;
                    tasks =
                    [
                        "2× B2-Training",
                        "Leseverstehen und Sprachbausteine als 90-Minuten-Block (mindestens einmal)",
                        "1× E-Mail in 30 Minuten, danach die Hinweise der Prüfung abarbeiten",
                        "2× Mündlich: Teil 2 (Diskussion) und Teil 3 (Planen) mit dem Partner",
                        $"Schwerpunkt: {focusText}",
                    ];
                    if (simulation) tasks.Add("Am Wochenende: volle Simulation, danach Prognose vergleichen");
                    break;
                case TelcPhase.Endspurt:
                    title = "Endspurt: Zeit und Mündlich";
                    simulation = daysLeft is >= 5 and <= 9;
                    tasks =
                    [
                        "Täglich 15 Minuten Mündlich: abwechselnd Teil 1, 2, 3",
                        "Alle sieben Teil-1-Themen einmal laut durchsprechen",
                        "1× E-Mail in 30 Minuten",
                        $"Nur noch die schwächsten Teile: {focusText}",
                    ];
                    if (simulation) tasks.Insert(0, "Letzte volle Simulation - danach nur noch gezielt nacharbeiten");
                    break;
                default:
                    title = "Ruhe vor der Prüfung";
                    simulation = false;
                    tasks =
                    [
                        "Nichts Neues mehr lernen",
                        "Die vorbereiteten Teil-1-Themen einmal laut erzählen",
                        "Ausweis, Anmeldebestätigung, Uhr, Stifte bereitlegen - früh schlafen",
                    ];
                    break;
            }
            weeks.Add(new TelcPlanWeek(start, end, phase, title, tasks, simulation));
            start = end.AddDays(1);
        }
        return weeks;
    }
}
