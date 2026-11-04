using System;
using System.Globalization;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Phase-3 step that validates the stored cast requests, V-0 and V-3 to V-8 (skill-system.md validation table,
    /// CR-SK-17, CR-SK-19, EC-SK-5/8/10; ADR-014 Decision 3; Skill System Story 005). Runs after Auto-Attack Combat in
    /// the composition's explicit order (wired in Story 011).
    /// </summary>
    /// <remarks>
    /// A separate step rather than <see cref="SkillSystemService"/> implementing <see cref="IZoneSimulationStep"/>:
    /// the service is the per-entity store that Movement-style callers and Stories 006 and 007 read, and keeping the
    /// per-tick pipeline out of it leaves the store free of the five seam dependencies. Walks slots in player-slot
    /// order, uses <see cref="PendingSkillCast.Caster"/> (never the slot owner), reports the first failed check to
    /// <see cref="ISkillCastOutput"/> and hands a passing cast to <see cref="ISkillCastValidatedSink"/>. Mutates no mana
    /// and no cooldown, and never calls NotifySkillUsed.
    /// Each request is isolated (ADR-014): an exception from a collaborator is logged and the request skipped, then the
    /// next slot is processed. A faulting request gets no rejection, because the output may be what threw. The intake
    /// is cleared exactly once per run.
    /// Allocation-free except the log paths, each throttled to at most one message per run: unknown skill id (EC-SK-8),
    /// malformed RangeUnits (NaN or negative; fails closed as ServerError), a missing V-8 position (ServerError) and a
    /// faulting request.
    /// </remarks>
    /// <example>
    /// <code>
    /// var step = new SkillCastStep(intake, service, definitions, alive, new NoSilenceSource(), targets, positions, output, sink);
    /// step.Run(currentTick, deltaTime);
    /// </code>
    /// </example>
    public sealed class SkillCastStep : IZoneSimulationStep
    {
        private const string LOG_PREFIX = "[SkillCastStep] ";

        private readonly SkillCastIntake _intake;
        private readonly SkillSystemService _service;
        private readonly ISkillDefinitionSource _definitions;
        private readonly ISkillCasterAliveSource _alive;
        private readonly ISkillSilenceSource _silence;
        private readonly ISkillTargetSource _targets;
        private readonly ISkillPositionSource _positions;
        private readonly ISkillCastOutput _output;
        private readonly ISkillCastValidatedSink _next;

        // Per-run log throttles, reset at the start of Run (SkillCastIntake pattern).
        private bool _unknownSkillLogged;
        private bool _faultLogged;
        private bool _badRangeLogged;
        private bool _positionLogged;

        /// <summary>Creates the step.</summary>
        /// <param name="intake">The per-slot request store (Story 004).</param>
        /// <param name="service">The per-entity skill store (reads only).</param>
        /// <param name="definitions">Skill definitions.</param>
        /// <param name="alive">V-0 caster alive.</param>
        /// <param name="silence">V-5b silence.</param>
        /// <param name="targets">V-7 target lookup.</param>
        /// <param name="positions">V-8 positions.</param>
        /// <param name="output">Receives rejections.</param>
        /// <param name="next">Receives passing casts (Story 006).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SkillCastStep(
            SkillCastIntake intake,
            SkillSystemService service,
            ISkillDefinitionSource definitions,
            ISkillCasterAliveSource alive,
            ISkillSilenceSource silence,
            ISkillTargetSource targets,
            ISkillPositionSource positions,
            ISkillCastOutput output,
            ISkillCastValidatedSink next)
        {
            _intake = intake ?? throw new ArgumentNullException(nameof(intake));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _alive = alive ?? throw new ArgumentNullException(nameof(alive));
            _silence = silence ?? throw new ArgumentNullException(nameof(silence));
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        /// <summary>
        /// Validates every stored request, then empties the intake (also when a collaborator throws). A request whose
        /// processing throws is logged (once per run) and skipped without a rejection; the remaining slots are still
        /// processed.
        /// </summary>
        /// <param name="currentTick">The tick being run; V-6 compares cooldown expiry with it.</param>
        /// <param name="deltaTime">Unused.</param>
        public void Run(uint currentTick, float deltaTime)
        {
            _unknownSkillLogged = false;
            _faultLogged = false;
            _badRangeLogged = false;
            _positionLogged = false;
            try
            {
                int capacity = _intake.Capacity;
                for (int slot = 0; slot < capacity; slot++)
                {
                    if (_intake.TryGetPending(slot, out PendingSkillCast cast))
                    {
                        ProcessIsolated(in cast, currentTick);
                    }
                }
            }
            finally
            {
                _intake.ClearAll();
            }
        }

        private void ProcessIsolated(in PendingSkillCast cast, uint currentTick)
        {
            try
            {
                Process(in cast, currentTick);
            }
            catch (Exception ex)
            {
                if (!_faultLogged)
                {
                    _faultLogged = true;
                    Debug.LogError(LOG_PREFIX + "request from " + cast.Caster + " threw " + ex.GetType().Name
                        + "; skipped, no rejection sent (ADR-014)");
                }
            }
        }

        private void Process(in PendingSkillCast cast, uint currentTick)
        {
            EntityID caster = cast.Caster;
            SkillCastRequest request = cast.Request;

            // V-0
            if (!_alive.IsAlive(caster))
            {
                Reject(caster, in request, SkillCastRejectionCode.CasterNotAlive);
                return;
            }

            // V-3 to V-6
            if (!TryValidateSkill(caster, in request, currentTick, out SkillCastRejectionCode code,
                    out SkillDefinition definition, out int skillSlot))
            {
                Reject(caster, in request, code);
                return;
            }

            // CR-SK-19: a Self skill ignores the submitted target; only this working copy changes.
            bool isSelf = definition.TargetConstraint == TargetConstraint.Self;
            EntityID target = isSelf ? caster : new EntityID(request.TargetEntityId);

            if (!IsValidTarget(target, definition.TargetConstraint))
            {
                Reject(caster, in request, SkillCastRejectionCode.InvalidTarget);
                return;
            }

            if (!TryValidateRange(caster, target, in definition, isSelf, out code))
            {
                Reject(caster, in request, code);
                return;
            }

            var validated = new ValidatedSkillCast(caster, target, in definition, skillSlot, currentTick);
            _next.Accept(in validated);
        }

        // V-3 to V-6. Returns false with the rejection code for the first failed check.
        private bool TryValidateSkill(
            EntityID caster,
            in SkillCastRequest request,
            uint currentTick,
            out SkillCastRejectionCode code,
            out SkillDefinition definition,
            out int skillSlot)
        {
            code = SkillCastRejectionCode.InvalidSkill;
            skillSlot = -1;

            // V-3
            if (!TryResolveDefinition(caster, in request, out definition))
            {
                return false;
            }

            // V-4: the skill must be in the caster's own (class) skill list.
            if (!_service.TryFindSlot(caster, definition.SkillId, out skillSlot)
                || !_service.TryGetSkill(caster, skillSlot, out SkillInstance instance))
            {
                return false;
            }

            // V-5
            if (!instance.IsUnlocked)
            {
                code = SkillCastRejectionCode.SkillLocked;
                return false;
            }

            // V-5b
            if (_silence.IsSilenced(caster))
            {
                code = SkillCastRejectionCode.Silenced;
                return false;
            }

            // V-6
            if (instance.CooldownExpiryTick > currentTick)
            {
                code = SkillCastRejectionCode.OnCooldown;
                return false;
            }

            return true;
        }

        // V-7
        private bool IsValidTarget(EntityID target, TargetConstraint constraint)
        {
            if (target.RawValue == 0u
                || !_targets.TryGetTarget(target, out SkillTargetKind kind, out bool isAlive)
                || !isAlive)
            {
                return false;
            }

            switch (constraint)
            {
                case TargetConstraint.SingleHostile:
                    return kind == SkillTargetKind.Mob;
                case TargetConstraint.SingleFriendly:
                    return kind == SkillTargetKind.Player;
                default:
                    // Self (the target is the caster) and any constraint without a kind rule: every known, alive
                    // target is valid. V-0 reads the caster-alive source and this check reads the target source, so
                    // a Self caster missing from the target source is rejected here as InvalidTarget (Story 011 note).
                    return true;
            }
        }

        // V-8. The malformed-range check runs first, also for Self: it is a single comparison and a NaN or negative
        // range is bad data however the skill is cast (a Self skill with RangeUnits 0 still passes). Fails closed.
        private bool TryValidateRange(
            EntityID caster,
            EntityID target,
            in SkillDefinition definition,
            bool isSelf,
            out SkillCastRejectionCode code)
        {
            code = SkillCastRejectionCode.ServerError;
            float range = definition.RangeUnits;
            if (!(range >= 0f))
            {
                LogBadRange(definition.ID);
                return false;
            }

            // A Self cast is at distance 0 by definition; RangeUnits 0 means no range limit.
            if (isSelf || range == 0f)
            {
                return true;
            }

            if (!_positions.TryGetPositionXZ(caster, out float cx, out float cz)
                || !_positions.TryGetPositionXZ(target, out float tx, out float tz))
            {
                LogPositionUnavailable(caster, target);
                return false;
            }

            float dx = tx - cx;
            float dz = tz - cz;
            if (dx * dx + dz * dz > range * range)
            {
                code = SkillCastRejectionCode.OutOfRange;
                return false;
            }

            return true;
        }

        /// <summary>V-3: id 0 or an unknown id fails; an unknown non-zero id logs one warning per <see cref="Run"/> (EC-SK-8).</summary>
        private bool TryResolveDefinition(EntityID caster, in SkillCastRequest request, out SkillDefinition definition)
        {
            definition = default;
            if (request.SkillId == 0u)
            {
                return false;
            }

            if (_definitions.TryGetDefinition(new SkillID(request.SkillId), out definition))
            {
                return true;
            }

            if (!_unknownSkillLogged)
            {
                _unknownSkillLogged = true;
                Debug.LogWarning(LOG_PREFIX + "unknown skill id " + request.SkillId.ToString(CultureInfo.InvariantCulture)
                    + " from " + caster + " (EC-SK-8)");
            }

            return false;
        }

        /// <summary>Logs a NaN or negative <c>RangeUnits</c> once per <see cref="Run"/>.</summary>
        private void LogBadRange(uint skillId)
        {
            if (_badRangeLogged)
            {
                return;
            }

            _badRangeLogged = true;
            Debug.LogWarning(LOG_PREFIX + "invalid RangeUnits on skill id "
                + skillId.ToString(CultureInfo.InvariantCulture) + "; rejecting as ServerError");
        }

        /// <summary>Logs a missing V-8 position once per <see cref="Run"/>.</summary>
        private void LogPositionUnavailable(EntityID caster, EntityID target)
        {
            if (_positionLogged)
            {
                return;
            }

            _positionLogged = true;
            Debug.LogWarning(LOG_PREFIX + "position unavailable for V-8 (caster " + caster + ", target "
                + target + "); rejecting as ServerError");
        }

        private void Reject(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code)
        {
            _output.SendRejection(caster, in request, code);
        }
    }
}
