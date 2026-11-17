using System.ComponentModel;
using Lotse.Infrastructure.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Lotse.Mcp;

/// <summary>
/// The tools a chat assistant sees. Descriptions are written for the model: what the tool returns, when to call it,
/// and what the app checks, so a rejected write comes back with a reason the model can act on.
/// </summary>
[McpServerToolType]
public sealed class LotseTools(ChatBridge bridge)
{
    [McpServerTool(Name = "get_learner_status", Title = "Lernstand", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("""
        The learner's state as the Lotse app shows it: target level, exam and exam date, streak, due reviews, minutes this
        week against the weekly goal, readiness per exam module (left out where there is not enough evidence yet - say so,
        do not estimate), the weakest topics with their typical mistakes and the Serbian interference note, and for telc
        the points per part against the pass marks. Call this first in every conversation about practising German.
        """)]
    public Task<LearnerStatus> GetLearnerStatus(CancellationToken ct) => bridge.StatusAsync(ct);

    [McpServerTool(Name = "get_error_patterns", Title = "Fehlerjournal", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("""
        The learner's error journal grouped by error type, most frequent first, with the three newest examples of each
        (wrong words and correction). Use it to pick what to practise and to explain a rule with the learner's own
        sentences instead of textbook ones.
        """)]
    public Task<IReadOnlyList<ErrorPattern>> GetErrorPatterns(
        [Description("How many days back, 1-365. Default 30.")] int days = 30,
        CancellationToken ct = default) => bridge.ErrorPatternsAsync(days, ct);

    [McpServerTool(Name = "list_topics", Title = "Themen", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("""
        The course topics: id, title, area (Grammatik, Wortschatz, Lesen, Hoeren, Schreiben, Sprechen, ...), CEFR band,
        how many exercises the app has for it, and the error codes that belong to it. The ids are what add_exercises
        needs, the error codes what record_errors needs.
        """)]
    public IReadOnlyList<TopicInfo> ListTopics(
        [Description("Only this area, e.g. \"Grammatik\". Omit for all.")] string? area = null) => bridge.Topics(area);

    [McpServerTool(Name = "add_exercises", Title = "Übungen hinzufügen", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("""
        Adds up to 10 exercises you wrote to one topic of the app, so they come up in the learner's sessions with spaced
        repetition and are marked by the app itself. Closed types only: cloze (gap as ___ and the base form in brackets,
        e.g. "mit ___ (der Bus)"), transform (instruction says what to change), translate (Serbian prompt in Latin script,
        2-4 accepted German answers), multiplechoice (options plus correctIndex), wordorder (options are the pieces,
        answers the correct sentence). Answers must be unambiguous. Every exercise is validated; rejected ones come back
        with the reason, accepted ones with their id. Write German you are sure of - the learner trusts the app.
        """)]
    public Task<ImportResult> AddExercises(
        [Description("Topic id from list_topics, e.g. \"GR.KASUS_PRAEPOSITIONEN\".")] string topicId,
        [Description("The exercises. Fields: type, prompt, answers, instruction, options, correctIndex, explanation (German, one sentence), serbianNote (optional), band (e.g. B2_1, optional).")] IReadOnlyList<ExerciseDraft> exercises,
        CancellationToken ct = default) => Checked(() => bridge.AddExercisesAsync(topicId, exercises, ct));

    [McpServerTool(Name = "record_errors", Title = "Fehler ins Journal", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("""
        Writes mistakes you found in the learner's German (in a text they wrote to you, or in a conversation) into the
        app's error journal, so the next sessions in the app aim at them. Use the error code from list_topics that fits
        best; an unknown code is rejected, not guessed. Up to 20 per call. Only real mistakes of the learner - never
        typos you made, never style preferences.
        """)]
    public Task<ImportResult> RecordErrors(
        [Description("The mistakes: code (from list_topics), wrong (the learner's words), correction.")] IReadOnlyList<ExternalError> errors,
        CancellationToken ct = default) => Checked(() => bridge.RecordErrorsAsync(errors, ct));

    /// <summary>
    /// The SDK hides an ordinary exception's message from the client ("An error occurred invoking ..."), which leaves
    /// the model guessing. An unknown topic or too many items is the model's to fix, so that message goes through.
    /// </summary>
    private static async Task<T> Checked<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ArgumentException e)
        {
            throw new McpException(e.Message.Split(" (Parameter", 2)[0]);
        }
    }
}
