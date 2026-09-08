namespace Lotse.Core.Model;

/// <summary>
/// The stand-in name for a learner who has not filled in their own. It used to be the author's own name, which is
/// the wrong default for a public repository and wrong for anyone else who runs the app: a stranger's course would
/// greet them by a stranger's name.
///
/// The name is drawn from a small pool by a stable hash of the account id. That gives the two properties that
/// matter: one learner always sees the same name, so their story stays consistent across sessions and restarts,
/// and two accounts on the same machine rarely share one.
///
/// <b>The pool is male on purpose.</b> Nineteen places in the content address the learner as "Herr {Nachname}".
/// Handing out "Julia Berger" there would read as a bug, so widening the pool means making those salutations
/// variable first - a content change, not a change here.
/// </summary>
public static class NameFallback
{
    /// <summary>No first name of a story speaker (Jonas, Tarek, Yusuf …): the learner must not share a name with
    /// someone they talk to. A content test holds the line against every lesson.</summary>
    public static readonly IReadOnlyList<string> FirstNames =
        ["Lukas", "Felix", "Martin", "David", "Simon", "Philipp", "Andreas", "Matthias", "Tobias", "Daniel", "Christian", "Sebastian"];

    /// <summary>
    /// No surname that any character carries anywhere in the content - lessons AND exercises. The first cut of this
    /// list had "Berger" in it, and Lesson 1's team lead is Sabine Berger: a learner without a name of their own
    /// then read "Frau Berger hat mir Ihre Einarbeitung übergeben" as Herr Berger. Five of twelve collided once the
    /// exercise bank was counted too (Berger, Wagner, Bauer, Hoffmann, Neumann), so the test checks the whole bank.
    /// </summary>
    public static readonly IReadOnlyList<string> LastNames =
        ["Keller", "Brandt", "Lehmann", "Vogel", "Sommer", "Winkler", "Reuter", "Krause", "Ludwig", "Beckmann", "Franke", "Otto"];

    /// <summary>Last resort where no account is at hand at all (a preview, a test, a page rendered before login).</summary>
    public static readonly (string First, string Last) Neutral = ("Lukas", "Keller");

    /// <summary>The stand-in for one account. The same id always yields the same name.</summary>
    public static (string First, string Last) For(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return Neutral;
        var h = StableHash(accountId);
        // Two independent draws from one hash: the low half picks the first name, the high half the surname, so
        // "Lukas" is not always followed by "Berger".
        return (FirstNames[(int)(h % (uint)FirstNames.Count)],
                LastNames[(int)(h / 16 % (uint)LastNames.Count)]);
    }

    /// <summary>
    /// FNV-1a. Not <see cref="string.GetHashCode()"/> on purpose: that one is randomised per process, so the same
    /// learner would be handed a different name after every restart of the app.
    /// </summary>
    private static uint StableHash(string s)
    {
        var hash = 2166136261u;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }
}
