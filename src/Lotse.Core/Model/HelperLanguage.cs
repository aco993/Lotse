namespace Lotse.Core.Model;

/// <summary>
/// The language the app explains German *in*. The app itself does not change: every instruction, button and
/// feedback line stays German, because the point is to live in the target language. What changes is the bridge -
/// the vocabulary prompt, the translation source sentence, the second column of a grammar table and the
/// contrastive note.
///
/// Serbian is the author's own first language and the default. English exists because the contrastive method
/// works for any first language once the notes are written for it - and because a portfolio reader is far more
/// likely to read English than Serbian.
/// </summary>
public enum HelperLanguage
{
    Serbian,
    English,
}

/// <summary>
/// What the render boundary needs to hand the shared catalogue to one learner: the name to substitute and the
/// language to explain in. The catalogue is a singleton used by several accounts at once, so nothing here may be
/// written back into it - see <see cref="NameTemplate"/>.
/// </summary>
public sealed record LearnerView(string FirstName, string LastName, HelperLanguage HelperLanguage)
{
    /// <summary>Used wherever no profile is at hand; the empty names fall back inside <see cref="NameFallback"/>.</summary>
    public static readonly LearnerView Default = new("", "", HelperLanguage.Serbian);

    /// <summary>
    /// The names actually shown, with the account's own stand-in filled in for whatever the learner left empty.
    /// Resolved here rather than in <see cref="NameTemplate"/> so that the stand-in can differ per account.
    /// </summary>
    public LearnerView WithFallbackFor(string? accountId)
    {
        if (!string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(LastName)) return this;
        var (first, last) = NameFallback.For(accountId);
        return this with
        {
            FirstName = string.IsNullOrWhiteSpace(FirstName) ? first : FirstName,
            LastName = string.IsNullOrWhiteSpace(LastName) ? last : LastName,
        };
    }
}

public static class HelperLanguageExtensions
{
    /// <summary>Column header and chip label, in German like the rest of the interface.</summary>
    public static string Label(this HelperLanguage lang) => lang == HelperLanguage.English ? "Englisch" : "Serbisch";

    /// <summary>Short tag for the interference chip on Themen ("SR-Interferenz" / "EN-Interferenz").</summary>
    public static string ShortTag(this HelperLanguage lang) => lang == HelperLanguage.English ? "EN" : "SR";
}
