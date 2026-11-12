using System;
using IronGrind.Events;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Per-zone broadcast of mob spawns and deaths to Navigation and other zone systems. One bus per zone, created by
    /// Zone Instancing at T-1 (CR-MS-5) and injected. This is NOT Combat's <c>ICombatService.OnMobDied</c>: Enemy AI
    /// bridges Combat's kill event to this bus (Navigation epic decision 1).
    /// </summary>
    /// <remarks>
    /// Subscribers are kept in arrays rebuilt on add and remove (setup-time allocation only), so a raise allocates
    /// nothing and isolates each subscriber: one that throws is logged and the others still run. Removing a handler
    /// removes its last registration. A handler removed during a raise still receives that raise (the raise iterates a
    /// snapshot); a handler added during a raise is first called on the next raise. Tick thread only.
    /// </remarks>
    /// <example>
    /// <code>
    /// var bus = new MobEventBus();
    /// bus.OnMobDied += HandleMobDied;   // a named method, subscribed once
    /// bus.RaiseMobDied(new MobDiedArgs(zone, mobId));
    /// </code>
    /// </example>
    public sealed class MobEventBus
    {
        // Replaced, never mutated, on add/remove; the raise iterates a snapshot, so a subscriber may (un)subscribe.
        private Action<MobSpawnedArgs>[] _spawnedHandlers = Array.Empty<Action<MobSpawnedArgs>>();
        private Action<MobDiedArgs>[] _diedHandlers = Array.Empty<Action<MobDiedArgs>>();

        /// <summary>Raised once per mob spawn. A null handler is ignored.</summary>
        public event Action<MobSpawnedArgs> OnMobSpawned
        {
            add
            {
                if (value == null)
                {
                    return;
                }

                var next = new Action<MobSpawnedArgs>[_spawnedHandlers.Length + 1];
                Array.Copy(_spawnedHandlers, next, _spawnedHandlers.Length);
                next[_spawnedHandlers.Length] = value;
                _spawnedHandlers = next;
            }
            remove
            {
                int index = value == null ? -1 : Array.LastIndexOf(_spawnedHandlers, value);
                if (index < 0)
                {
                    return;
                }

                var next = new Action<MobSpawnedArgs>[_spawnedHandlers.Length - 1];
                Array.Copy(_spawnedHandlers, 0, next, 0, index);
                Array.Copy(_spawnedHandlers, index + 1, next, index, next.Length - index);
                _spawnedHandlers = next;
            }
        }

        /// <summary>Raised once per mob death. A null handler is ignored.</summary>
        public event Action<MobDiedArgs> OnMobDied
        {
            add
            {
                if (value == null)
                {
                    return;
                }

                var next = new Action<MobDiedArgs>[_diedHandlers.Length + 1];
                Array.Copy(_diedHandlers, next, _diedHandlers.Length);
                next[_diedHandlers.Length] = value;
                _diedHandlers = next;
            }
            remove
            {
                int index = value == null ? -1 : Array.LastIndexOf(_diedHandlers, value);
                if (index < 0)
                {
                    return;
                }

                var next = new Action<MobDiedArgs>[_diedHandlers.Length - 1];
                Array.Copy(_diedHandlers, 0, next, 0, index);
                Array.Copy(_diedHandlers, index + 1, next, index, next.Length - index);
                _diedHandlers = next;
            }
        }

        /// <summary>Raises <see cref="OnMobSpawned"/>; a throwing subscriber is logged and the rest still run.</summary>
        /// <param name="args">The spawn data.</param>
        public void RaiseMobSpawned(in MobSpawnedArgs args)
        {
            Action<MobSpawnedArgs>[] handlers = _spawnedHandlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](args);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>Raises <see cref="OnMobDied"/>; a throwing subscriber is logged and the rest still run.</summary>
        /// <param name="args">The death data.</param>
        public void RaiseMobDied(in MobDiedArgs args)
        {
            Action<MobDiedArgs>[] handlers = _diedHandlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](args);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
