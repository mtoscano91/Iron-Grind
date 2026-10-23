namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Identifies a zone template (GDD CR-ZI-1). The <c>-zoneTemplate</c> command-line value carries the enum
    /// <b>name</b> (for example <c>StartingZone</c>), parsed case-sensitive; numeric text is rejected by
    /// <see cref="ZoneSceneRegistry.TryResolve"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// ZoneTemplateID template = ZoneTemplateID.StartingZone; // -zoneTemplate StartingZone
    /// </code>
    /// </example>
    public enum ZoneTemplateID : byte
    {
        /// <summary>The MVP starting zone.</summary>
        StartingZone = 0
    }
}
