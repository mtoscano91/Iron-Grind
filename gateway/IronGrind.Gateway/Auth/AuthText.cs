using System.Text;

namespace IronGrind.Gateway.Auth;

/// <summary>Text helpers shared by the credential rules (Authentication Story 005).</summary>
internal static class AuthText
{
    /// <summary>Counts Unicode code points, not UTF-16 units (a lone surrogate counts as one).</summary>
    /// <param name="text">Text to measure.</param>
    /// <returns>The number of code points.</returns>
    internal static int CodePointCount(string text)
    {
        int count = 0;
        foreach (Rune _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
