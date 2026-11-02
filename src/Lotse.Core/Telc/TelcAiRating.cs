using System.Text.RegularExpressions;
using Lotse.Core.Tutor;

namespace Lotse.Core.Telc;

/// <summary>What the tutor rates: one telc production (an email, or the transcript of an oral part).</summary>
/// <param name="Task">The task as the candidate saw it, including the Leitpunkte or the planning situation.</param>
/// <param name="LearnerText">The email, or for the oral exam the learner's turns (the partner's turns are context).</param>
public sealed record TelcRatingRequest(TelcPart Part, string Task, string LearnerText, string? Conversation = null)
{
    public bool Speaking => !TelcBlueprint.IsWritten(Part);
}

/// <summary>One criterion as rated by the tutor, with the reason in one or two sentences.</summary>
public sealed record TelcCriterionRating(string Criterion, TelcBand Band, string Comment);

/// <summary>
/// The tutor's rating of a telc production. <see cref="Errors"/> only ever holds mistakes whose snippet occurs in
/// the learner's text - see <see cref="QuotedErrors"/>; <see cref="DroppedUnquoted"/> counts the reports that did
/// not, so the evaluation can measure how often the model invents a mistake.
/// </summary>
public sealed record TelcAiRating(
    IReadOnlyList<TelcCriterionRating> Criteria,
    IReadOnlyList<TutorError> Errors,
    string Feedback,
    IReadOnlyList<string> Upgrades,
    bool ThemaVerfehlt = false,
    int DroppedUnquoted = 0)
{
    public TelcBand Band(int index) => index < Criteria.Count ? Criteria[index].Band : TelcBand.D;

    public TelcWritingRating ToWriting() => new(Band(0), Band(1), Band(2), ThemaVerfehlt);

    public TelcSpeakingRating ToSpeaking() => new(Band(0), Band(1), Band(2), Band(3));
}

/// <summary>
/// Keeps only the tutor's error reports that quote the learner. Language models rate writing close to human raters
/// but now and then report a mistake the text does not contain; a learner who then "corrects" a right sentence is
/// worse off than one who got no report. A snippet counts as quoted when it occurs in the text after both are
/// lower-cased and their whitespace and quotation marks are normalised - nothing looser.
/// </summary>
public static partial class QuotedErrors
{
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    public static string Normalize(string s)
        => Whitespace().Replace(s.ToLowerInvariant().Replace('„', '"').Replace('“', '"').Replace('”', '"').Replace('’', '\'').Trim(' ', '.', ',', '"', '\''), " ");

    public static bool IsQuoted(string snippet, string text)
    {
        var n = Normalize(snippet);
        return n.Length > 0 && Normalize(text).Contains(n, StringComparison.Ordinal);
    }

    public static IReadOnlyList<TutorError> Filter(IEnumerable<TutorError> errors, string text, out int dropped)
    {
        var all = errors.ToList();
        var kept = all.Where(e => IsQuoted(e.Snippet, text)).ToList();
        dropped = all.Count - kept.Count;
        return kept;
    }
}
