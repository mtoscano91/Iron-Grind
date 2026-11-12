using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.StandIns;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Stand-in for the Enemy AI mob speed source: every mob type moves at
    /// <see cref="NavigationConfigValues.StandInMobMoveSpeed"/>. Logs "[ZoneServer] navigation mob speed source not
    /// implemented" once. Enemy AI replaces it and removes the config field.
    /// </summary>
    /// <example>
    /// <code>
    /// var source = new StandInNavMobSpeedSource(NavigationConfigValues.Defaults);
    /// source.TryGetMoveSpeed(new MobTypeID(1u), out float speed);   // true, 3.5
    /// </code>
    /// </example>
    public sealed class StandInNavMobSpeedSource : INavMobSpeedSource
    {
        private readonly float _speed;
        private readonly StandInLogOnce _log = new StandInLogOnce("navigation mob speed source");

        /// <summary>Builds the stand-in.</summary>
        /// <param name="values">The navigation knobs; supplies the stand-in speed.</param>
        public StandInNavMobSpeedSource(NavigationConfigValues values)
        {
            _speed = values.StandInMobMoveSpeed;
        }

        /// <inheritdoc />
        public bool TryGetMoveSpeed(MobTypeID mobType, out float speed)
        {
            _log.Log();
            speed = _speed;
            return true;
        }
    }
}
