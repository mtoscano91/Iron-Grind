namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Supplies the current Skill System knobs. Read on every cast, so replacing the values behind the source changes
    /// the next cast without rebuilding any system (AC-SK-19; Story 006).
    /// </summary>
    /// <remarks>
    /// The production implementation (a holder over <see cref="SkillSystemConfigValues.FromAsset"/>, built once per
    /// swap so clamp warnings are not logged per cast) is wired by the composition in Story 011.
    /// </remarks>
    public interface ISkillSystemConfigSource
    {
        /// <summary>The current knobs, every value inside its safe range.</summary>
        SkillSystemConfigValues Values { get; }
    }
}
