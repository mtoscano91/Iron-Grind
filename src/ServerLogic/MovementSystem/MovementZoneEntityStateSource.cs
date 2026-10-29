using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// Decorates an <see cref="IZoneEntityStateSource"/> so the zone snapshot carries the position and facing
    /// Movement holds (Movement Story 008; zone-instancing.md CR-ZI-8 step 6). All other fields are the inner
    /// source's. Main thread only; allocation-free.
    /// </summary>
    /// <remarks>
    /// Movement not attached, or not holding the player, leaves the inner view unchanged. Mob state passes through.
    /// Composition wiring is Story 009.
    /// </remarks>
    public sealed class MovementZoneEntityStateSource : IZoneEntityStateSource
    {
        private const float ROTATION_SCALE = 32767f;

        private readonly IZoneEntityStateSource _inner;
        private MovementService _movement;

        /// <summary>Creates the decorator.</summary>
        /// <param name="inner">The wrapped source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
        public MovementZoneEntityStateSource(IZoneEntityStateSource inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>Attaches the movement service. Set once.</summary>
        /// <param name="movement">The service.</param>
        /// <exception cref="ArgumentNullException"><paramref name="movement"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Already attached.</exception>
        public void AttachMovement(MovementService movement)
        {
            if (movement == null)
                throw new ArgumentNullException(nameof(movement));
            if (_movement != null)
                throw new InvalidOperationException("[MovementZoneEntityStateSource] movement is already attached.");

            _movement = movement;
        }

        /// <inheritdoc/>
        public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
        {
            if (!_inner.TryGetPlayerState(id, out view))
                return false;

            if (_movement == null
                || !_movement.TryGetPosition(id, out Vector3 position)
                || !_movement.TryGetFacing(id, out short facing))
                return true;

            Quaternion yaw = Quaternion.Euler(0f, facing / 10f, 0f);
            view = new ZonePlayerStateView(
                ToWire(position.x), ToWire(position.y), ToWire(position.z),
                (short)Mathf.RoundToInt(yaw.x * ROTATION_SCALE),
                (short)Mathf.RoundToInt(yaw.y * ROTATION_SCALE),
                (short)Mathf.RoundToInt(yaw.z * ROTATION_SCALE),
                (short)Mathf.RoundToInt(yaw.w * ROTATION_SCALE),
                facing,
                view.CurrentHp, view.MaxHp, view.CurrentMp, view.MaxMp, view.Level, view.IsInCombat,
                view.CycleTimer, view.EquipmentAppearanceFlags, view.CharacterName, view.RespawnTick);
            return true;
        }

        // Same clamp as WireFixedPointCodec.EncodePosition; silent (Movement already logs an unencodable respawn point).
        // Y is not covered by MovementPositionGuard, so this prevents a short wrap.
        private static short ToWire(float meters)
        {
            float clamped = Mathf.Clamp(meters, -WireFixedPointCodec.MaxPositionMeters, WireFixedPointCodec.MaxPositionMeters);
            return (short)Mathf.RoundToInt(clamped * 100f);
        }

        /// <inheritdoc/>
        public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
        {
            return _inner.TryGetMobState(id, out view);
        }
    }
}
