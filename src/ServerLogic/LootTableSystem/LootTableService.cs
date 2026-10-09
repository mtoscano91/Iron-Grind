using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Randomness;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Kill resolution and gold distribution (design/gdd/loot-table-system.md CR-LT-14 gold
    /// distribution, F-LT-1 gold per member, CR-LT-4 no attacker). Entry point per
    /// design/gdd/enemy-ai.md: <see cref="ResolveMobDrop"/>.
    /// </summary>
    public sealed class LootTableService : ILootTableService
    {
        private readonly LootTableRegistry _registry;
        private readonly PartyTagTracker _tagTracker;
        private readonly IPartyService _partyService;
        private readonly IMobInfoProvider _mobInfoProvider;
        private readonly ICurrencyService _currencyService;
        private readonly IRandomProvider _random;
        private readonly ILootDropSink _dropSink;

        /// <summary>Creates the loot table service.</summary>
        /// <param name="registry">Loot tables by mob type.</param>
        /// <param name="tagTracker">Damage record and tag owner lookup.</param>
        /// <param name="partyService">Supplies the winning party's members.</param>
        /// <param name="mobInfoProvider">Supplies the mob's type and position.</param>
        /// <param name="currencyService">Receives the gold awards.</param>
        /// <param name="random">Injected random provider (ADR-013); see <see cref="RandomProviderFactory"/>.</param>
        /// <param name="dropSink">Receives a non-empty drop list.</param>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public LootTableService(
            LootTableRegistry registry,
            PartyTagTracker tagTracker,
            IPartyService partyService,
            IMobInfoProvider mobInfoProvider,
            ICurrencyService currencyService,
            IRandomProvider random,
            ILootDropSink dropSink)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _tagTracker = tagTracker ?? throw new ArgumentNullException(nameof(tagTracker));
            _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
            _mobInfoProvider = mobInfoProvider ?? throw new ArgumentNullException(nameof(mobInfoProvider));
            _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _dropSink = dropSink ?? throw new ArgumentNullException(nameof(dropSink));
        }

        /// <inheritdoc/>
        public void RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage)
        {
            _tagTracker.RecordDamage(mobEntityId, attacker, finalDamage);
        }

        /// <inheritdoc/>
        public void ClearMob(EntityID mobEntityId)
        {
            _tagTracker.ClearMob(mobEntityId);
        }

        /// <summary>Drops every mob's damage record. For zone teardown.</summary>
        public void Clear()
        {
            _tagTracker.Clear();
        }

        /// <summary>
        /// Resolves a kill: look up the mob and its table, find the tag owner (none: silent return),
        /// fetch the owner's members (none: warning), clear the damage record, roll the drops, draw
        /// the gold, pay each member <c>floor(baseGold / N)</c> (F-LT-1), and hand a non-empty drop
        /// list to the sink. A non-zero <paramref name="tierShift"/> is ignored with a warning
        /// (enemy-ai.md OQ-AI-1).
        /// </summary>
        /// <remarks>
        /// The record is cleared before anything is paid, so a kill is resolved at most once: if a
        /// currency subscriber or the sink throws or calls back into this method for the same mob,
        /// the gold cannot be paid a second time. Exceptions are not caught here.
        /// </remarks>
        /// <param name="mobEntityID">The mob that died.</param>
        /// <param name="tierShift">Ignored; a non-zero value logs one warning.</param>
        public void ResolveMobDrop(EntityID mobEntityID, int tierShift)
        {
            if (!TryGetTable(mobEntityID, out MobInfo info, out LootTableDefinition table))
            {
                return;
            }

            if (!_tagTracker.TryGetTagOwner(mobEntityID, out PartyID owner))
            {
                return;
            }

            IReadOnlyList<CharacterID> members = _partyService.GetPartyMembers(owner);
            _tagTracker.ClearMob(mobEntityID);
            if (members == null || members.Count == 0)
            {
                UnityEngine.Debug.LogWarning($"[LootTableService] ResolveMobDrop: {owner} owns the tag on {mobEntityID} but has no members; nothing distributed.");
                return;
            }

            if (tierShift != 0)
            {
                UnityEngine.Debug.LogWarning($"[LootTableService] ResolveMobDrop: tierShift={tierShift} for {mobEntityID} is ignored (pending a GDD rule, enemy-ai.md OQ-AI-1).");
            }

            List<ItemID> drops = LootDropRoller.Roll(table, _random);
            int baseGold = _random.NextInt(table.GoldMin, table.GoldMax + 1);
            DistributeGold(mobEntityID, table, baseGold, members);

            if (drops.Count > 0)
            {
                _dropSink.OnDropsResolved(mobEntityID, owner, drops, info.Position);
            }
        }

        private bool TryGetTable(EntityID mobEntityID, out MobInfo info, out LootTableDefinition table)
        {
            table = null;
            if (!_mobInfoProvider.TryGetMob(mobEntityID, out info))
            {
                UnityEngine.Debug.LogError($"[LootTableService] ResolveMobDrop: {mobEntityID} is not a known mob; nothing distributed.");
                _tagTracker.ClearMob(mobEntityID);
                return false;
            }

            if (!_registry.TryGetTable(info.MobTypeId, out table))
            {
                UnityEngine.Debug.LogError($"[LootTableService] ResolveMobDrop: {mobEntityID} has {info.MobTypeId} with no loot table; nothing distributed.");
                _tagTracker.ClearMob(mobEntityID);
                return false;
            }

            return true;
        }

        private void DistributeGold(EntityID mobEntityID, LootTableDefinition table, int baseGold, IReadOnlyList<CharacterID> members)
        {
            // Read once: the divisor and the loop bound must be the same N even if a gold-sync
            // subscriber changes the party while the awards are being paid.
            int memberCount = members.Count;
            int goldPerMember = baseGold / memberCount;

            // <= 0, not == 0: a negative share cast to uint would wrap to a huge award.
            if (goldPerMember <= 0)
            {
                UnityEngine.Debug.LogError($"[LootTableService] ResolveMobDrop: gold range [{table.GoldMin}, {table.GoldMax}] of {mobEntityID} produced no payable share (baseGold={baseGold}, members={memberCount}); authoring violation: GoldMin must be >= MAX_PARTY_SIZE.");
                return;
            }

            for (int i = 0; i < memberCount; i++)
            {
                GoldMutationResult result = _currencyService.AddGold(members[i], (uint)goldPerMember, GoldTransactionReason.MonsterDrop);
                if (result.Error != GoldMutationError.None)
                {
                    UnityEngine.Debug.LogError($"[LootTableService] ResolveMobDrop: AddGold for {members[i]} failed with {result.Error}; continuing with the remaining members.");
                }
            }
        }
    }
}
