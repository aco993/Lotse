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
    /// <summary>The author's own defaults: his name, his language. Used wherever no profile is at hand.</summary>
    public static readonly LearnerView Default = new("", "", HelperLanguage.Serbian);
}

public static class HelperLanguageExtensions
{
    /// <summary>Column header and chip label, in German like the rest of the interface.</summary>
    public static string Label(this HelperLanguage lang) => lang == HelperLanguage.English ? "Englisch" : "Serbisch";

    /// <summary>Short tag for the interference chip on Themen ("SR-Interferenz" / "EN-Interferenz").</summary>
    public static string ShortTag(this HelperLanguage lang) => lang == HelperLanguage.English ? "EN" : "SR";
}
