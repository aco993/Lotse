using Lotse.Core.Engine;
using Lotse.Core.Model;

namespace Lotse.Core.Tutor;

/// <summary>
/// Asks the tutor for a Socratic hint and never lets one through that contains the answer. A hint that does is thrown
/// away and the tutor gets one more try, told why; if that one leaks too, the learner gets nothing from the AI and the
/// app's own hint ladder stays. The first local measurement (qwen2.5:7b, November 2026) found half of the first tries
/// leaking: without the retry every second request for help ended in silence.
/// The app and the evaluation both go through here, so the measured rate is the one a learner sees.
/// </summary>
public static class SocraticHint
{
    /// <param name="Hint">What the learner may see, or null.</param>
    /// <param name="FirstLeaked">The first try named the answer.</param>
    /// <param name="Tries">1 or 2.</param>
    public sealed record Result(string? Hint, bool FirstLeaked, int Tries);

    public static async Task<Result> AskAsync(ITutor tutor, Exercise exercise, string? attempt, LearnerContext context, CancellationToken ct = default)
    {
        var first = (await tutor.HintAsync(exercise, attempt, context, null, ct)).Trim();
        if (first.Length > 0 && !HintLadder.Leaks(first, exercise)) return new Result(first, false, 1);

        var leaked = first.Length > 0;
        var second = (await tutor.HintAsync(exercise, attempt, context, leaked ? first : null, ct)).Trim();
        return new Result(second.Length > 0 && !HintLadder.Leaks(second, exercise) ? second : null, leaked, 2);
    }
}
