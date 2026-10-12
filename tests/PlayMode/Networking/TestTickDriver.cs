using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Test-only tick driver for the Story 038 PlayMode run. Accumulates
    /// <see cref="Time.unscaledDeltaTime"/> in <c>Update</c> and calls
    /// <see cref="ServerTickLoop.AdvanceTick"/> once per
    /// <see cref="ServerTickLoop.FIXED_DELTA_TIME"/>. It is not the production tick driver
    /// (that belongs to the server composition root), so VR-1 is a statement about this driver only.
    /// </summary>
    public sealed class TestTickDriver : MonoBehaviour
    {
        // A long stall (debugger, domain pause) must not produce a burst of ticks.
        private const int MAX_PENDING_TICKS = 4;

        private ServerTickLoop _loop;
        private float _accumulator;

        /// <summary>True for exactly the duration of each <c>AdvanceTick</c> call.</summary>
        public bool IsInsideAdvanceTick { get; private set; }

        /// <summary>Number of <c>AdvanceTick</c> calls made so far.</summary>
        public int TickCount { get; private set; }

        /// <summary>Gives the driver the loop to advance. Until called, the driver does nothing.</summary>
        /// <param name="loop">The tick loop to advance.</param>
        public void Initialize(ServerTickLoop loop)
        {
            _loop = loop;
        }

        private void Update()
        {
            if (_loop == null)
            {
                return;
            }

            float step = ServerTickLoop.FIXED_DELTA_TIME;
            _accumulator += Time.unscaledDeltaTime;
            float cap = step * MAX_PENDING_TICKS;
            if (_accumulator > cap)
            {
                _accumulator = cap;
            }

            while (_accumulator >= step)
            {
                _accumulator -= step;
                IsInsideAdvanceTick = true;
                try
                {
                    _loop.AdvanceTick();
                }
                finally
                {
                    IsInsideAdvanceTick = false;
                }
                TickCount++;
            }
        }
    }
}
