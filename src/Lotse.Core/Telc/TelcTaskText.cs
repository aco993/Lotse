using System.Text;

namespace Lotse.Core.Telc;

/// <summary>
/// An objective telc task as plain text with its solution key: what the critic model reads and what the review page
/// shows a human. One rendering for both, so the person approving a task sees exactly what was checked.
/// </summary>
public static class TelcTaskText
{
    public static string WithKey(TelcContent content, TelcPart part, string id)
    {
        var sb = new StringBuilder();
        switch (part)
        {
            case TelcPart.Lesen1 when content.FindLesen1(id) is { } t:
                sb.AppendLine(t.Title).AppendLine().AppendLine("Überschriften:");
                for (var i = 0; i < t.Headings.Count; i++) sb.AppendLine($"  {TelcObjectiveScoring.Letter(i)}) {t.Headings[i]}");
                for (var i = 0; i < t.Texts.Count; i++)
                    sb.AppendLine().AppendLine($"Text {i + 1}  [Lösung: {TelcObjectiveScoring.Letter(t.Solution[i])}]").AppendLine(t.Texts[i]);
                break;
            case TelcPart.Lesen2 when content.FindLesen2(id) is { } t:
                sb.AppendLine(t.Title).AppendLine().AppendLine(t.Text);
                for (var i = 0; i < t.Questions.Count; i++) Choice(sb, i + 1, t.Questions[i]);
                break;
            case TelcPart.Lesen3 when content.FindLesen3(id) is { } t:
                sb.AppendLine(t.Title).AppendLine().AppendLine("Anzeigen:");
                for (var i = 0; i < t.Ads.Count; i++) sb.AppendLine($"  {TelcObjectiveScoring.Letter(i)}) {t.Ads[i].Title}: {t.Ads[i].Text}");
                sb.AppendLine().AppendLine("Situationen:");
                for (var i = 0; i < t.Situations.Count; i++)
                    sb.AppendLine($"  {i + 1}. {t.Situations[i]}  [Lösung: {TelcObjectiveScoring.Letter(t.Solution[i])}]");
                break;
            case TelcPart.Sprachbausteine1 or TelcPart.Sprachbausteine2 when content.FindSprachbausteine(id) is { } t:
                sb.AppendLine(t.Title).AppendLine().AppendLine(t.Text).AppendLine();
                if (t.Box is { } box) sb.AppendLine("Kasten: " + string.Join(", ", box.Select((w, i) => $"{TelcObjectiveScoring.Letter(i)}) {w}"))).AppendLine();
                for (var i = 0; i < t.Gaps.Count; i++)
                {
                    var g = t.Gaps[i];
                    var options = g.Options is { } o ? string.Join(" / ", o.Select((w, j) => $"{TelcObjectiveScoring.Letter(j)}) {w}")) + " - " : "";
                    var right = (g.Options ?? t.Box) is { } list && g.Correct >= 0 && g.Correct < list.Count ? list[g.Correct] : "?";
                    sb.AppendLine($"[{i + 1}] {options}Lösung: {TelcObjectiveScoring.Letter(g.Correct)}) {right}  ({g.NodeId})");
                }
                break;
            case TelcPart.Hoeren1 or TelcPart.Hoeren2 or TelcPart.Hoeren3 when content.FindHoeren(id) is { } t:
                sb.AppendLine(t.Title);
                var n = 1;
                foreach (var r in t.Recordings)
                {
                    sb.AppendLine().AppendLine(r.Label is { } label ? $"Aufnahme ({label}):" : "Aufnahme:").AppendLine(r.Audio);
                    foreach (var st in r.Statements) sb.AppendLine($"  {n++}. {st.Text}  [Lösung: {(st.True ? "richtig" : "falsch")}]");
                }
                break;
            default:
                sb.Append(content.TaskTitle(part, id) ?? id);
                break;
        }
        return sb.ToString().TrimEnd();
    }

    private static void Choice(StringBuilder sb, int number, TelcChoice q)
    {
        sb.AppendLine().AppendLine($"{number}. {q.Question}");
        for (var j = 0; j < q.Options.Count; j++) sb.AppendLine($"  {TelcObjectiveScoring.Letter(j)}) {q.Options[j]}{(j == q.Correct ? "  [Lösung]" : "")}");
    }
}
