using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.HitDetection;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    // Shared recording fakes for the Auto-Attack Combat fixtures. Fixed arrays and counters: they record without allocating.
    internal static class AutoAttackTestFakes
    {
        public const int CAPACITY = 1024;
        public const float WIRE_SCALE = 100f;

        // Non-player mob ids used by every fixture: they never resolve as players in FakeZoneState.
        public const uint MOB_A_RAW = 5001u;
        public const uint MOB_B_RAW = 5002u;

        public static short Raw(float world)
        {
            return (short)Mathf.RoundToInt(world * WIRE_SCALE);
        }

        public static DamageResult Damage(int finalDamage, bool isKill)
        {
            return new DamageResult(finalDamage, 0, finalDamage, false, isKill, DamageContext.PhysicalAuto, false);
        }
    }

    // Attackers share one position; the target has its own. TargetPlayer set: that id resolves as a player, not a mob.
    // AttackerKnown / TargetKnown false make the matching lookup fail.
    internal sealed class FakeZoneState : IZoneEntityStateSource
    {
        public short AttackerX;
        public short AttackerY;
        public short AttackerZ;
        public short TargetX = AutoAttackTestFakes.Raw(1f);
        public short TargetY;
        public short TargetZ;
        public bool AttackerKnown = true;
        public bool TargetKnown = true;
        public EntityID TargetPlayer = EntityID.Invalid;
        public int PlayerLookups;
        public int MobLookups;

        private static readonly EntityID MobAId = new EntityID(AutoAttackTestFakes.MOB_A_RAW);
        private static readonly EntityID MobBId = new EntityID(AutoAttackTestFakes.MOB_B_RAW);

        public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
        {
            PlayerLookups++;
            if (id == MobAId || id == MobBId)
            {
                view = default;
                return false;
            }

            if (TargetPlayer != EntityID.Invalid && id == TargetPlayer)
            {
                view = TargetKnown ? MakePlayer(TargetX, TargetY, TargetZ) : default;
                return TargetKnown;
            }

            view = AttackerKnown ? MakePlayer(AttackerX, AttackerY, AttackerZ) : default;
            return AttackerKnown;
        }

        public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
        {
            MobLookups++;
            if (TargetPlayer != EntityID.Invalid || !TargetKnown)
            {
                view = default;
                return false;
            }

            view = new ZoneMobStateView(TargetX, TargetY, TargetZ, 0, 0, 0, 0, 0, 0, 0, false, 0);
            return true;
        }

        private static ZonePlayerStateView MakePlayer(short x, short y, short z)
        {
            return new ZonePlayerStateView(x, y, z, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, null, 0u);
        }
    }

    internal sealed class FakeHitEmitter : IHitResultEmitter
    {
        public readonly EntityID[] Attackers = new EntityID[AutoAttackTestFakes.CAPACITY];
        public readonly EntityID[] Targets = new EntityID[AutoAttackTestFakes.CAPACITY];
        public readonly HitResult[] Results = new HitResult[AutoAttackTestFakes.CAPACITY];
        public int Count;

        public void Emit(EntityID attacker, EntityID target, HitResult result)
        {
            if (Count < Results.Length)
            {
                Attackers[Count] = attacker;
                Targets[Count] = target;
                Results[Count] = result;
            }

            Count++;
        }
    }

    internal sealed class FakeStatSource : IAutoAttackStatSource
    {
        public int BaseDamage = 10;
        public float AttackRange = 3.0f;
        public int DamageReads;
        public int RangeReads;

        public int GetBaseDamage(EntityID attacker)
        {
            DamageReads++;
            return BaseDamage;
        }

        public float GetAttackRange(EntityID attacker)
        {
            RangeReads++;
            return AttackRange;
        }
    }

    // Records the arguments of the last call.
    internal sealed class FakeDamageCalc : IAutoAttackDamageCalculator
    {
        public DamageResult Result = AutoAttackTestFakes.Damage(10, false);
        public int Calls;
        public int LastBaseDamage;
        public EntityID LastAttacker;
        public EntityID LastTarget;

        public DamageResult Calculate(int baseDamage, EntityID attacker, EntityID target)
        {
            Calls++;
            LastBaseDamage = baseDamage;
            LastAttacker = attacker;
            LastTarget = target;
            return Result;
        }
    }

    // Tick is set by the test before each Run so a fire can be located in time. LastResult is the last applied result.
    internal sealed class FakeDamageApplier : IAutoAttackDamageApplier
    {
        public readonly uint[] Ticks = new uint[AutoAttackTestFakes.CAPACITY];
        public readonly EntityID[] Attackers = new EntityID[AutoAttackTestFakes.CAPACITY];
        public readonly EntityID[] Targets = new EntityID[AutoAttackTestFakes.CAPACITY];
        public uint Tick;
        public int Count;
        public EntityID LastAttacker;
        public EntityID LastTarget;
        public DamageResult LastResult;

        public void Apply(EntityID attacker, EntityID target, in DamageResult result)
        {
            if (Count < Ticks.Length)
            {
                Ticks[Count] = Tick;
                Attackers[Count] = attacker;
                Targets[Count] = target;
            }

            LastAttacker = attacker;
            LastTarget = target;
            LastResult = result;
            Count++;
        }
    }
}
