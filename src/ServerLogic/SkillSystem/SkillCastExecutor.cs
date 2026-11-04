using System;
using System.Diagnostics;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Stage after V-0 to V-8: V-9 mana (F-SK-4), execution, cooldown write (CR-SK-8) and the accepted-cast report
    /// (skill-system.md CR-SK-2, CR-SK-8, CR-SK-11, CR-SK-13, CR-SK-16, F-SK-4, EC-SK-2, EC-SK-13; Skill System Story 006).
    /// </summary>
    /// <remarks>
    /// Order of an accepted cast: V-9 consumes mana, then the <see cref="ISkillExecutionStep"/> (Story 007
    /// NotifySkillUsed and damage, Story 008 effects), then the cooldown is written, then
    /// <see cref="ISkillCastOutput.SendAccepted"/>. The cooldown is written on every accepted cast, hit or miss.
    /// Effective cost = <c>Ceil(ManaCost * MANA_COST_MULTIPLIER)</c> computed in decimal so float multipliers such as 1.1f do not overcharge (101 x 1.5 = 152, 100 x 1.1 = 110), the multiplier
    /// read from the config source on each cast (AC-SK-19). A cost of 0 skips <c>ConsumeMana</c> and is never rejected
    /// for mana. Insufficient mana sends InsufficientMana and changes nothing: no cooldown, no execution.
    /// EC-SK-13: cooldowns are not persisted (Character Persistence does not save them), so a crash between the mana
    /// deduction and the cooldown write needs no handling. The cooldown is a <c>uint</c> tick sum; at 20 Hz the tick
    /// counter wraps after about 6.8 years of uptime and no wrap handling exists (same as the tick source).
    /// Allocation-free.
    /// </remarks>
    public sealed class SkillCastExecutor : ISkillCastValidatedSink
    {
        private readonly ISkillManaSink _mana;
        private readonly ISkillSystemConfigSource _config;
        private readonly SkillSystemService _service;
        private readonly ISkillCastOutput _output;
        private readonly ISkillExecutionStep _execution;

        /// <summary>Creates the executor.</summary>
        /// <param name="mana">V-9 mana deduction.</param>
        /// <param name="config">Supplies MANA_COST_MULTIPLIER on each cast.</param>
        /// <param name="service">The skill store; receives the cooldown write.</param>
        /// <param name="output">Receives InsufficientMana rejections and accepted-cast reports.</param>
        /// <param name="execution">Stories 007/008 execution; null = nothing to execute yet.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public SkillCastExecutor(
            ISkillManaSink mana,
            ISkillSystemConfigSource config,
            SkillSystemService service,
            ISkillCastOutput output,
            ISkillExecutionStep execution = null)
        {
            _mana = mana ?? throw new ArgumentNullException(nameof(mana));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _execution = execution;
        }

        /// <summary>F-SK-4: <c>Ceil(manaCost * multiplier)</c>, computed in decimal (no allocation).</summary>
        /// <remarks>
        /// <c>(decimal)float</c> rounds to 7 significant digits, so 1.1f is 1.1m and 100 x 1.1 is 110, not 111.
        /// A cost of 0 or less is 0. The result saturates at <see cref="int.MaxValue"/>.
        /// </remarks>
        /// <param name="manaCost">The authored cost.</param>
        /// <param name="multiplier">MANA_COST_MULTIPLIER.</param>
        /// <returns>The effective cost, 0 for a non-positive cost.</returns>
        public static int ComputeEffectiveManaCost(int manaCost, float multiplier)
        {
            if (manaCost <= 0)
            {
                return 0;
            }

            decimal c = Math.Ceiling((decimal)manaCost * (decimal)multiplier);
            if (c <= 0m)
            {
                return 0;
            }

            return c >= int.MaxValue ? int.MaxValue : (int)c;
        }

        /// <inheritdoc />
        public void Accept(in ValidatedSkillCast cast)
        {
            // V-9
            int cost = ComputeEffectiveManaCost(cast.Definition.ManaCost, _config.Values.ManaCostMultiplier);
            if (cost > 0 && !_mana.ConsumeMana(cast.Caster, cost))
            {
                _output.SendRejection(cast.Caster, in cast.Request, SkillCastRejectionCode.InsufficientMana);
                return;
            }

            // Extension point: Story 007 (NotifySkillUsed, damage) then Story 008 (effects).
            _execution?.Execute(in cast);

            // CR-SK-8: after execution, on every accepted cast. CooldownTicks 0 writes the current tick: ready next tick.
            // No data validation exists yet: a negative CooldownTicks fails safe to 0 (load-time validation belongs to
            // the skill data loader).
            int authored = cast.Definition.CooldownTicks;
            uint cd = authored > 0 ? (uint)authored : 0u;
            bool written = _service.SetCooldownExpiry(cast.Caster, cast.SkillSlot, cast.CurrentTick + cd);
            // Cannot fail in the tick that validated the slot (V-3..V-8 ran in the same call), so it is not a rejection.
            Debug.Assert(written, "Cooldown write failed for a slot validated in the same tick.");
            _output.SendAccepted(cast.Caster, in cast.Request, (int)cd);
        }
    }
}
