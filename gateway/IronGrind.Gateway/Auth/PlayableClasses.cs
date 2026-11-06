namespace IronGrind.Gateway.Auth;

/// <summary>
/// The playable class table {0 Warrior, 1 Healer}. This duplicates <c>src/Foundation/SkillSystem/ClassType.cs</c>
/// (class-system.md, two MVP classes) and must change with it. It is neither moved nor link-compiled:
/// ADR-016 Decision 6 keeps shared code in the two shared folders (Authentication Story 005).
/// </summary>
public static class PlayableClasses
{
    private const byte WARRIOR = 0;
    private const byte HEALER = 1;

    private static readonly byte[] PLAYABLE = { WARRIOR, HEALER };

    /// <summary>True when <paramref name="classType"/> is a playable class.</summary>
    /// <param name="classType">Raw class byte from the request.</param>
    public static bool IsPlayable(byte classType)
    {
        return Array.IndexOf(PLAYABLE, classType) >= 0;
    }
}
