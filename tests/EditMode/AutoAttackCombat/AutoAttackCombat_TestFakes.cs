using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.HitDetection;
using IronGrind.LootTableSystem;
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

    internal enum KillCallKind
    {
        Award,
        Add,
        Apply,
        Died
    }

    // One recorded call. Award: Target. Add: Attacker, Amount. Apply: Attacker, Target, Amount = FinalDamage.
    // Died: Attacker = killer, Target = mob.
    internal readonly struct KillCallEntry
    {
        public readonly KillCallKind Kind;
        public readonly EntityID Attacker;
        public readonly EntityID Target;
        public readonly int Amount;

        public KillCallEntry(KillCallKind kind, EntityID attacker, EntityID target, int amount)
        {
            Kind = kind;
            Attacker = attacker;
            Target = target;
            Amount = amount;
        }
    }

    // One shared, pre-sized log: the order of calls across the XP, health and event seams is asserted on it.
    internal sealed class KillCallLog
    {
        public readonly KillCallEntry[] Entries = new KillCallEntry[AutoAttackTestFakes.CAPACITY];
        public int Count;

        public void Record(KillCallKind kind, EntityID attacker, EntityID target, int amount)
        {
            if (Count < Entries.Length)
            {
                Entries[Count] = new KillCallEntry(kind, attacker, target, amount);
            }

            Count++;
        }

        public int CountOf(KillCallKind kind)
        {
            int n = 0;
            for (int i = 0; i < Count && i < Entries.Length; i++)
            {
                if (Entries[i].Kind == kind)
                {
                    n++;
                }
            }

            return n;
        }
    }

    // Two mobs (MOB_A_RAW, MOB_B_RAW). The applier writes it; the health-aware calculator reads it.
    internal sealed class FakeMobHealth
    {
        public int HpA = 1;
        public int HpB = 1;

        public int Get(EntityID mob)
        {
            return mob == new EntityID(AutoAttackTestFakes.MOB_A_RAW) ? HpA : HpB;
        }

        public void Damage(EntityID mob, int amount)
        {
            if (mob == new EntityID(AutoAttackTestFakes.MOB_A_RAW))
            {
                HpA -= amount;
            }
            else
            {
                HpB -= amount;
            }
        }
    }

    // Returns a fixed award and records "award:target". A null log records nothing (no-op use at plain call sites).
    internal sealed class FakeXpAward : IAutoAttackXpAward
    {
        private readonly KillCallLog _log;
        public int Award = 50;
        public int Calls;

        public FakeXpAward(KillCallLog log = null)
        {
            _log = log;
        }

        public int GetXPAward(EntityID target)
        {
            Calls++;
            _log?.Record(KillCallKind.Award, EntityID.Invalid, target, Award);
            return Award;
        }
    }

    // Records "add:attacker:amount". A null log records nothing.
    internal sealed class FakeExperienceSink : IAutoAttackExperienceSink
    {
        private readonly KillCallLog _log;
        public int Calls;

        public FakeExperienceSink(KillCallLog log = null)
        {
            _log = log;
        }

        public void AddExperience(EntityID attacker, int amount)
        {
            Calls++;
            _log?.Record(KillCallKind.Add, attacker, EntityID.Invalid, amount);
        }
    }

    // Records "apply:attacker:target:damage" and writes the mob health. With ForgetTypeOnApply set, the apply also makes
    // that type source forget the mob, like an applier that despawns a dead mob.
    internal sealed class LoggingDamageApplier : IAutoAttackDamageApplier
    {
        private readonly KillCallLog _log;
        private readonly FakeMobHealth _health;
        public int Count;
        public FakeMobTypeSource ForgetTypeOnApply;

        public LoggingDamageApplier(KillCallLog log, FakeMobHealth health)
        {
            _log = log;
            _health = health;
        }

        public void Apply(EntityID attacker, EntityID target, in DamageResult result)
        {
            Count++;
            _log.Record(KillCallKind.Apply, attacker, target, result.FinalDamage);
            _health.Damage(target, result.FinalDamage);
            if (ForgetTypeOnApply != null)
            {
                ForgetTypeOnApply.Known = false;
            }
        }
    }

    // A dead target is rejected (FinalDamage 0, IsKill false) by default. With DeadTargetHitsWithoutKill it returns the
    // damage with IsKill false instead, like the real DamageCalculator's dead-entity guard. Otherwise IsKill is true
    // when the damage empties the health.
    internal sealed class HealthAwareDamageCalc : IAutoAttackDamageCalculator
    {
        private readonly FakeMobHealth _health;
        public int Damage = 10;
        public int Calls;
        public bool DeadTargetHitsWithoutKill;

        public HealthAwareDamageCalc(FakeMobHealth health)
        {
            _health = health;
        }

        public DamageResult Calculate(int baseDamage, EntityID attacker, EntityID target)
        {
            Calls++;
            int hp = _health.Get(target);
            if (hp <= 0)
            {
                return DeadTargetHitsWithoutKill
                    ? AutoAttackTestFakes.Damage(Damage, false)
                    : DamageResult.Rejected(DamageContext.PhysicalAuto);
            }

            return AutoAttackTestFakes.Damage(Damage, hp <= Damage);
        }
    }

    internal sealed class FakeMobTypeSource : IAutoAttackMobTypeSource
    {
        public const uint MOB_TYPE_RAW = 77u;
        public bool Known = true;
        public MobTypeID Type = new MobTypeID(MOB_TYPE_RAW);

        public bool TryGetMobType(EntityID mob, out MobTypeID type)
        {
            type = Known ? Type : default;
            return Known;
        }
    }

    // Records each auto-face request; fixed arrays, no allocation per call.
    internal sealed class FakeFaceOutput : IAutoAttackFaceOutput
    {
        public readonly EntityID[] Attackers = new EntityID[AutoAttackTestFakes.CAPACITY];
        public readonly Vector3[] Directions = new Vector3[AutoAttackTestFakes.CAPACITY];
        public int Count;

        public void Face(EntityID attacker, Vector3 direction)
        {
            if (Count < Attackers.Length)
            {
                Attackers[Count] = attacker;
                Directions[Count] = direction;
            }

            Count++;
        }
    }
}
