namespace Lotse.Core.Model;

/// <summary>
/// A hash that is the same on every run and every machine. <see cref="string.GetHashCode()"/> is deliberately
/// randomised per process, so anything seeded from it - a name, a shuffle, a jitter - silently changes after each
/// restart. Every place in the app that needs a stable seed goes through here.
/// </summary>
public static class StableHash
{
    /// <summary>32-bit FNV-1a over the UTF-16 code units of <paramref name="s"/>.</summary>
    public static uint Fnv1a(string s)
    {
        var hash = 2166136261u;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }

    /// <summary>The same hash folded into a non-negative <see cref="int"/>, for APIs that want one (<see cref="Random"/>).</summary>
    public static int Seed(string s) => (int)(Fnv1a(s) & int.MaxValue);
}
