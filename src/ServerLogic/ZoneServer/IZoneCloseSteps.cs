namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Zone Instancing close steps (CR-ZI-12 steps 1-8), teardown step 4. <see cref="StandInZoneCloseSteps"/>
    /// until the Zone Instancing epic ships.
    /// </summary>
    /// <example>
    /// <code>
    /// closeSteps.RunCloseSteps(); // after navigation teardown, before the outbound flush
    /// </code>
    /// </example>
    public interface IZoneCloseSteps
    {
        /// <summary>Runs CR-ZI-12 steps 1-8. May throw; the composition logs it and goes on.</summary>
        void RunCloseSteps();
    }
}
