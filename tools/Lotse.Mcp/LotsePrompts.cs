using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Lotse.Mcp;

/// <summary>
/// How the assistant should teach with this data. The rules are the app's own: hints before answers (the PNAS 2025
/// study behind the hint ladder found that an assistant that hands out solutions lowers what learners can do alone),
/// telc points rather than vague praise, and the learner's own mistakes as the material.
/// </summary>
[McpServerPromptType]
public sealed class LotsePrompts
{
    public const string Instructions = """
        Lotse is the learner's German B2 trainer (telc or Goethe). These tools read their learning state and let you add
        practice to the app. Teach like the app does:
        - Hints before answers. When the learner is stuck, give the topic, then the rule, then the first letters - the
          full solution only after a real attempt or when they ask for it explicitly.
        - Use their own mistakes from get_error_patterns as examples.
        - Readiness and telc points that are missing or null mean "not enough evidence yet". Say that; do not estimate.
        - After you correct a text of theirs, offer to write the mistakes into the journal with record_errors.
        - Exercises you add with add_exercises are practised in the app later. Add them for the weakest topics, and only
          German you are certain of.
        Answer in the language the learner writes in.
        """;

    [McpServerPrompt(Name = "lotse_coach", Title = "Mit Lotse üben")]
    [Description("A practice conversation built on the learner's Lotse data: status, weakest area, a short exercise, then the mistakes back into the app.")]
    public static string Coach(
        [Description("Optional focus, e.g. \"Schreiben\", \"Dativ\", \"Sprachbausteine\". Empty: the weakest area.")] string? focus = null) => $"""
        Ruf zuerst get_learner_status und get_error_patterns auf.
        Sag mir in zwei, drei Sätzen, wo ich stehe: Prüfung und Datum, was schon gemessen ist und was noch nicht.
        Dann üben wir {(string.IsNullOrWhiteSpace(focus) ? "meinen schwächsten Bereich" : focus)}: fünf kurze Aufgaben, eine nach der anderen.
        Gib mir bei einem Fehler zuerst einen Hinweis, nicht die Lösung.
        Am Ende: schreib meine Fehler mit record_errors ins Journal und lege mit add_exercises bis zu fünf passende Übungen in Lotse an.
        """;
}
