using System;
using System.Buffers.Binary;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// Codec for one <see cref="EntityState"/> entry (little-endian). Entries have no length prefix, so
    /// <see cref="TryReadBody"/> reports the bytes consumed to let a caller walk a packed payload.
    /// Zone Instancing Story 006.
    /// </summary>
    public static class EntityStateCodec
    {
        /// <summary>
        /// Writes the entry at offset 0 and returns the bytes written (player 53 to 77, mob 35). A player name is cut to
        /// 24 UTF-8 bytes at a character boundary. Throws <see cref="InvalidOperationException"/> (via
        /// <see cref="WireIdCodec"/>, CR-NET-7.3) for <see cref="EntityID.Invalid"/> and
        /// <see cref="ArgumentOutOfRangeException"/> if <paramref name="destination"/> is smaller than the maximum size
        /// of the entry's type (<see cref="EntityState.PlayerMaxBodySize"/> / <see cref="EntityState.MobBodySize"/>).
        /// </summary>
        public static int WriteBody(Span<byte> destination, in EntityState state)
        {
            bool isPlayer = state.Type == EntityType.Player;
            destination = destination.Slice(0, isPlayer ? EntityState.PlayerMaxBodySize : EntityState.MobBodySize);

            destination[0] = (byte)state.Type;
            WireIdCodec.SerializeEntityId(destination.Slice(1, WireIdCodec.EntityIdWireSize), state.EntityId, 0, nameof(state.EntityId));
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(5, 2), state.PosX);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(7, 2), state.PosY);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(9, 2), state.PosZ);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(11, 2), state.RotX);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(13, 2), state.RotY);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(15, 2), state.RotZ);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(17, 2), state.RotW);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(19, 4), state.CurrentHp);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(23, 4), state.MaxHp);
            destination[27] = state.IsInCombat ? (byte)1 : (byte)0;
            destination[28] = state.IsGhost ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(29, 2), state.CycleTimer);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(31, 2), state.FacingAngle);

            int offset = EntityState.HeaderSize;
            if (!isPlayer)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), state.MobTypeId);
                return offset + 2;
            }

            offset = WireStringCodec.WriteString(destination, offset, state.CharacterName, EntityState.MaxNameBytes);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), state.Level);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset + 4, 4), state.CurrentMp);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset + 8, 4), state.MaxMp);
            destination[offset + 12] = state.EquipmentAppearanceFlags;
            destination[offset + 13] = state.IsInDeadState ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 14, 4), state.RespawnTicksRemaining);
            return offset + EntityState.PlayerTailSize;
        }

        /// <summary>
        /// Attempts to read one entry from the start of <paramref name="source"/>. Returns <see langword="false"/>
        /// (default state, 0 bytes consumed) if the buffer is truncated, the entityType byte is unknown, or the name length
        /// exceeds 24; never throws. Bytes after the entry are ignored, so a packed payload is walked by slicing
        /// <paramref name="source"/> by <paramref name="bytesConsumed"/>. No log is written.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> source, out EntityState state, out int bytesConsumed)
        {
            state = default;
            bytesConsumed = 0;

            if (source.Length < EntityState.HeaderSize)
            {
                return false;
            }

            byte rawType = source[0];
            if (rawType != (byte)EntityType.Player && rawType != (byte)EntityType.Mob)
            {
                return false;
            }

            EntityID id = WireIdCodec.DeserializeEntityId(source.Slice(1, WireIdCodec.EntityIdWireSize));
            short posX = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(5, 2));
            short posY = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(7, 2));
            short posZ = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(9, 2));
            short rotX = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(11, 2));
            short rotY = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(13, 2));
            short rotZ = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(15, 2));
            short rotW = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(17, 2));
            int currentHp = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(19, 4));
            int maxHp = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(23, 4));
            bool inCombat = source[27] != 0;
            bool isGhost = source[28] != 0;
            ushort cycleTimer = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(29, 2));
            short facing = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(31, 2));

            if (rawType == (byte)EntityType.Mob)
            {
                if (source.Length < EntityState.MobBodySize)
                {
                    return false;
                }

                state = EntityState.CreateMob(id, posX, posY, posZ, rotX, rotY, rotZ, rotW, currentHp, maxHp, inCombat,
                    cycleTimer, facing, BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(EntityState.HeaderSize, 2)));
                bytesConsumed = EntityState.MobBodySize;
                return true;
            }

            if (!WireStringCodec.TryReadString(source, EntityState.HeaderSize, EntityState.MaxNameBytes, out string name, out int offset)
                || source.Length - offset < EntityState.PlayerTailSize)
            {
                return false;
            }

            state = EntityState.CreatePlayer(id, posX, posY, posZ, rotX, rotY, rotZ, rotW, currentHp, maxHp, inCombat, isGhost,
                cycleTimer, facing, name,
                BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset + 4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset + 8, 4)),
                source[offset + 12],
                source[offset + 13] != 0,
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset + 14, 4)));
            bytesConsumed = offset + EntityState.PlayerTailSize;
            return true;
        }
    }
}
