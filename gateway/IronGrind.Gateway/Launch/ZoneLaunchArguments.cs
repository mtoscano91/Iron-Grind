using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.Launch;

/// <summary>Argument checks shared by the zone launchers (Gateway Story 008).</summary>
internal static class ZoneLaunchArguments
{
    /// <summary>
    /// Rejects an invalid ZoneID, port 0, and a template name outside <c>[A-Za-z0-9_-]</c>. The template goes into an env
    /// file line and a command line, so a newline, <c>=</c>, space or quote could inject another variable or argument.
    /// </summary>
    /// <param name="zone">ZoneID to start.</param>
    /// <param name="zoneTemplate">Template name.</param>
    /// <param name="port">UDP game port.</param>
    public static void Check(ZoneID zone, string zoneTemplate, ushort port)
    {
        if (zone == ZoneID.Invalid)
        {
            throw new ArgumentException("ZoneID 0 is not a valid zone to start", nameof(zone));
        }

        ArgumentNullException.ThrowIfNull(zoneTemplate);
        if (zoneTemplate.Length == 0 || !zoneTemplate.All(IsTemplateChar))
        {
            throw new ArgumentException($"Zone template name must be non-empty and use only A-Z, a-z, 0-9, '_' or '-' (was \"{zoneTemplate}\")", nameof(zoneTemplate));
        }

        if (port == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port 0 is not a valid zone port");
        }
    }

    private static bool IsTemplateChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-';
}
