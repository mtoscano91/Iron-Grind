using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.Navigation;

namespace IronGrind.PlayModeTests
{
    /// <summary>Test fake for the PlayMode Navigation fixtures: every mob type moves at one fixed speed.</summary>
    internal sealed class FixedNavSpeedSource : INavMobSpeedSource
    {
        private readonly float _speed;

        public FixedNavSpeedSource(float speed)
        {
            _speed = speed;
        }

        public bool TryGetMoveSpeed(MobTypeID mobType, out float speed)
        {
            speed = _speed;
            return true;
        }
    }
}
