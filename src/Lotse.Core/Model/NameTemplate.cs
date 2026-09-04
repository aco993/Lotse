namespace Lotse.Core.Model;

/// <summary>
/// The content used to carry the author's own name; it now carries tokens, and this is where they become the
/// learner's name. Rendering happens at the boundary where content is handed to one learner, never in the shared
/// catalogue - the catalogue is a singleton and several accounts use it at once.
///
/// <see cref="Exercise.Answers"/> is deliberately left alone: the checker compares what the learner typed against
/// those strings, so a token in an answer would be unmatchable. A content test asserts none ever appears there.
/// </summary>
public static class NameTemplate
{
    public const string FirstNameToken = "{Vorname}";
    public const string LastNameToken = "{Nachname}";
    public const string FullNameToken = "{Name}";

    /// <summary>Used when the profile leaves a field empty, so the author's own experience is unchanged.</summary>
    public const string DefaultFirstName = "Aleksandar";
    public const string DefaultLastName = "Micić";

    public static readonly string[] AllTokens = [FullNameToken, FirstNameToken, LastNameToken];

    public static bool ContainsToken(string? text)
        => text is not null && AllTokens.Any(t => text.Contains(t, StringComparison.Ordinal));

    public static string Render(string text, string firstName, string lastName)
    {
        if (text.Length == 0 || text.IndexOf('{') < 0) return text;
        var first = string.IsNullOrWhiteSpace(firstName) ? DefaultFirstName : firstName.Trim();
        var last = string.IsNullOrWhiteSpace(lastName) ? DefaultLastName : lastName.Trim();
        // Full name first: "{Name}" must not be eaten by a narrower token.
        return text
            .Replace(FullNameToken, $"{first} {last}", StringComparison.Ordinal)
            .Replace(FirstNameToken, first, StringComparison.Ordinal)
            .Replace(LastNameToken, last, StringComparison.Ordinal);
    }

    public static string? RenderOptional(string? text, string firstName, string lastName)
        => text is null ? null : Render(text, firstName, lastName);

    private static IReadOnlyList<string> RenderAll(IReadOnlyList<string> items, string firstName, string lastName)
        => items.Count == 0 ? items : [.. items.Select(t => Render(t, firstName, lastName))];

    private static DialogueLine Render(DialogueLine line, string firstName, string lastName) => line with
    {
        Speaker = Render(line.Speaker, firstName, lastName),
        Text = Render(line.Text, firstName, lastName),
        Options = line.Options is null ? null : RenderAll(line.Options, firstName, lastName),
        Feedback = line.Feedback is null ? null : RenderAll(line.Feedback, firstName, lastName),
    };

    /// <summary>Everything the learner reads or hears. <see cref="Exercise.Answers"/> stays untouched by design.</summary>
    public static Exercise Render(Exercise e, string firstName, string lastName) => e with
    {
        Prompt = Render(e.Prompt, firstName, lastName),
        Instruction = RenderOptional(e.Instruction, firstName, lastName),
        Hint = RenderOptional(e.Hint, firstName, lastName),
        Options = RenderAll(e.Options, firstName, lastName),
        Explanation = RenderOptional(e.Explanation, firstName, lastName),
        SerbianNote = RenderOptional(e.SerbianNote, firstName, lastName),
        EnglishNote = RenderOptional(e.EnglishNote, firstName, lastName),
        PromptEn = RenderOptional(e.PromptEn, firstName, lastName),
        ExampleDe = RenderOptional(e.ExampleDe, firstName, lastName),
        ModelAnswer = RenderOptional(e.ModelAnswer, firstName, lastName),
        Text = RenderOptional(e.Text, firstName, lastName),
        Rubric = RenderAll(e.Rubric, firstName, lastName),
        Questions = e.Questions.Count == 0 ? e.Questions
            : [.. e.Questions.Select(q => q with { Question = Render(q.Question, firstName, lastName), Options = RenderAll(q.Options, firstName, lastName) })],
        Lines = e.Lines.Count == 0 ? e.Lines : [.. e.Lines.Select(l => Render(l, firstName, lastName))],
        Pairs = e.Pairs.Count == 0 ? e.Pairs
            : [.. e.Pairs.Select(p => p with { Left = Render(p.Left, firstName, lastName), Right = Render(p.Right, firstName, lastName) })],
    };

    /// <summary>
    /// The full boundary: the learner's name substituted and the helper language applied. For Vocab and Translate
    /// the prompt IS the helper language, so it is swapped here - one place, so no path can forget it (the speech
    /// synthesiser reads the rendered text, and a wrong-language prompt would be spoken as well as shown).
    ///
    /// The prompt falls back to Serbian when no English one was authored: a Vocab card without a prompt has no
    /// question at all. A content test makes sure that fallback never fires in practice.
    /// </summary>
    public static Exercise Render(Exercise e, LearnerView view)
    {
        var rendered = Render(e, view.FirstName, view.LastName);
        if (view.HelperLanguage != HelperLanguage.English || string.IsNullOrWhiteSpace(rendered.PromptEn)) return rendered;
        return rendered with { Prompt = rendered.PromptEn };
    }

    /// <summary>
    /// Lessons carry both helper columns to the view, which picks one per row (<see cref="ExampleRow.Helper"/>);
    /// only the name substitution happens here.
    /// </summary>
    public static Lesson Render(Lesson l, LearnerView view) => Render(l, view.FirstName, view.LastName);

    public static Lesson Render(Lesson l, string firstName, string lastName) => l with
    {
        Intro = Render(l.Intro, firstName, lastName),
        Merksatz = Render(l.Merksatz, firstName, lastName),
        Story = [.. l.Story.Select(s => Render(s, firstName, lastName))],
    };
}
