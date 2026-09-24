using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Leveling-System-side implementation of <see cref="IronGrind.CharacterStats.ILevelingService"/>
    /// (ADR-010 Tier 1 direct injection). Constructor-injected into
    /// <see cref="IronGrind.CharacterStats.CharacterStats"/>; there is no runtime
    /// <c>RegisterListener()</c> call — "only one listener" is structurally guaranteed by C#
    /// constructor injection.
    /// </summary>
    /// <remarks>
    /// <para><b>Story 001 (XP Accumulation) + Story 002 (Level-Up Sequence Core) + Story 003
    /// (Consecutive Level-Up &amp; Re-Entrancy Guards) + Story 005 (Free Point Allocation) +
    /// Story 009 (Spawn Initialization &amp; Persistence Load).</b>
    /// This class implements the three <see cref="IronGrind.CharacterStats.ILevelingService"/>
    /// methods plus the real CR-2.1–CR-2.7 level-up sequence (Story 002), the CR-2.9
    /// consecutive-level-up loop, the re-entrancy guard exposed via
    /// <see cref="IsLevelingUpInProgress"/>, the CR-2.10 <see cref="OnLevelUp"/> broadcast
    /// (Story 003), the real CR-3 <see cref="AllocateFreePoint"/> allocation semantics
    /// (Story 005: <c>heldFreePoints==0</c> guard, invalid-stat guard, decrement, write, F-3–F-9
    /// derived-stat recompute via the same <c>RecomputeDerivedStats</c> helper
    /// <see cref="ExecuteLevelUpSequence"/> uses — deliberately NOT the CR-2.7 HP/MP restore),
    /// and the CR-6 spawn/load sequence (Story 009: <see cref="InitializeAtL1"/> for CR-6.1/
    /// CR-6.2, <see cref="GetLevelingState"/>/<see cref="RestoreLevelingState"/> for CR-6.3's
    /// <c>heldFreePoints</c> persistence round-trip and its EC-LS-38/EC-LS-36 tamper-defense
    /// clamps).</para>
    /// <para><b>Placement note:</b> the Leveling System EPIC is marked "Layer: Core", but this
    /// class lives under <c>src/Foundation/</c> (single <c>IronGrind.Foundation.asmdef</c>),
    /// following the same precedent Networking Core established this session — no separate
    /// Core-layer assembly exists yet, and creating one needs its own ADR.</para>
    /// <para><b>Player registry is a forward-dependency stand-in.</b> No player/mob registry
    /// system exists yet in this codebase. <see cref="RegisterPlayerEntity"/> lets a caller
    /// (today: tests; eventually: the real player-session system) declare which
    /// <see cref="IronGrind.CharacterStats.EntityID"/>s are players — mirrors the
    /// caller-supplied test-local state idiom already established for not-yet-built subsystems
    /// (see <c>PartyDisbandCoordinator</c>, Networking Core Story 020).
    /// <see cref="RegisterPlayerClassType"/> extends the same idiom for the
    /// per-entity <c>classType</c> CR-2.4 needs — <see cref="InitializeAtL1"/> (Story 009) is
    /// the real cache point, reusing <see cref="RegisterPlayerClassType"/> rather than
    /// duplicating the caching logic. <b>This cache is in-memory and per-instance</b> — see
    /// <see cref="RestoreLevelingState"/>'s doc comment for the real caller-ordering contract
    /// this implies for a future session-restart / persistence-load caller.</para>
    /// <para><b>Class data is a forward-dependency stand-in.</b> No Class System epic
    /// implementation exists yet (confirmed via full-repo grep). <see cref="IClassRegistry"/>/
    /// <see cref="ClassDefinition"/>/<see cref="ClassRegistry"/> (this namespace) are the
    /// smallest mock this story needs for CR-2.4/CR-2.8; the constructor accepts any
    /// <see cref="IClassRegistry"/>, defaulting to an empty <see cref="ClassRegistry"/> when
    /// none is supplied so auto-alloc silently no-ops for unregistered classes rather than
    /// throwing.</para>
    /// <para><b>XP threshold table is injected, not hardcoded.</b> The real <c>XpThreshold</c>
    /// table is owned by Story 010 (currently Blocked on OQ-LS-7, mob XP rates). This story
    /// accepts any <see cref="IReadOnlyList{T}"/>&lt;int&gt; via the constructor; production
    /// wiring swaps in the real table once Story 010 unblocks.</para>
    /// <para><b>Breaking the CharacterStats &lt;-&gt; ILevelingService constructor cycle.</b>
    /// <see cref="IronGrind.CharacterStats.CharacterStats"/>'s constructor requires an
    /// <see cref="IronGrind.CharacterStats.ILevelingService"/>, but this service needs to
    /// read/write stats on that same CharacterStats instance (Level lookups for
    /// <see cref="GetExperienceThreshold"/>; the full CR-2 sequence for
    /// <see cref="NotifyExperienceCrossedThreshold"/>) — a genuine two-way dependency
    /// construction injection can't satisfy on both sides at once. Resolved with a single
    /// settable back-reference (<see cref="AttachCharacterStats"/>), assigned once at startup
    /// wiring, after both objects exist:
    /// <code>
    /// var levelingService = new LevelingService(xpThresholds, classRegistry);
    /// var stats = new CharacterStats(levelingService);
    /// levelingService.AttachCharacterStats(stats);
    /// </code>
    /// That assignment is a one-time startup-wiring step, not a persistent
    /// <c>event +=</c> subscription — it is therefore NOT the pattern ADR-010 Decision 4
    /// forbids ("lambda captures are forbidden for persistent subscriptions", which targets
    /// long-lived event subscriptions with an unsubscribe lifecycle). Do not misapply that
    /// rule to this line.</para>
    /// <para><b>Consolidated from Story 001's narrower <c>AttachLevelProvider</c>
    /// delegate.</b> Story 001 introduced a single-purpose <c>Func&lt;EntityID,int&gt;</c>
    /// delegate specifically to avoid needing a full <c>CharacterStats</c> back-reference.
    /// Story 002 needs that full back-reference anyway (<c>SetBaseStat</c>/<c>GetBaseStat</c>/
    /// <c>SetBaseStatFloat</c>/<c>SetCurrentHP</c>/<c>SetCurrentMP</c> for the CR-2 sequence),
    /// which subsumes the narrower delegate's only use (<see cref="GetExperienceThreshold"/>'s
    /// Level read). <c>AttachLevelProvider</c> has been removed in favor of
    /// <see cref="AttachCharacterStats"/> alone — keeping both would leave two parallel wiring
    /// mechanisms doing overlapping jobs. Story 001's test file was updated to match (see
    /// <c>LevelingSystem_XpAccumulation_tests.cs</c>).</para>
    /// </remarks>
    public sealed class LevelingService : IronGrind.CharacterStats.ILevelingService, ILevelingEventBroadcaster
    {
        private readonly IReadOnlyList<int> _xpThresholds;
        private readonly IClassRegistry _classRegistry;

        private readonly HashSet<IronGrind.CharacterStats.EntityID> _playerEntityIds =
            new HashSet<IronGrind.CharacterStats.EntityID>();

        private readonly Dictionary<IronGrind.CharacterStats.EntityID, byte> _classTypes =
            new Dictionary<IronGrind.CharacterStats.EntityID, byte>();

        private readonly Dictionary<IronGrind.CharacterStats.EntityID, int> _heldFreePoints =
            new Dictionary<IronGrind.CharacterStats.EntityID, int>();

        // Fixed CR-2.4 iteration order: {Strength, Dexterity, Vitality, Intelligence}.
        private static readonly IronGrind.CharacterStats.StatID[] AutoAllocOrder =
        {
            IronGrind.CharacterStats.StatID.Strength,
            IronGrind.CharacterStats.StatID.Dexterity,
            IronGrind.CharacterStats.StatID.Vitality,
            IronGrind.CharacterStats.StatID.Intelligence,
        };

        private IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>
        /// True while a CR-2.9 consecutive-level-up loop is actively executing inside
        /// <see cref="NotifyExperienceCrossedThreshold"/> — set immediately after that method's
        /// re-entrancy guard passes, cleared in a <c>finally</c> block regardless of how the loop
        /// exits (including an unexpected exception). Blocks <see cref="AllocateFreePoint"/>
        /// (AC-LS-10 / EC-LS-09) and blocks re-entrant <see cref="NotifyExperienceCrossedThreshold"/>
        /// calls (AC-LS-49 / EC-LS-10) — see that method's doc comment for the exact re-entrancy
        /// call sequence this guards against.
        /// </summary>
        private bool _levelingUpInProgress;

        /// <summary>
        /// Read-only test/observability seam for <see cref="_levelingUpInProgress"/>, per
        /// OQ-LS-5's test-seam requirement (Story 003).
        /// </summary>
        public bool IsLevelingUpInProgress => _levelingUpInProgress;

        /// <inheritdoc/>
        /// <remarks>
        /// Tier 2 broadcast event (ADR-010 Decision 3, <c>readonly struct</c> args, zero
        /// per-emit allocation). Fired once per level gained, in ascending order, only after
        /// <see cref="NotifyExperienceCrossedThreshold"/>'s full CR-2.9 loop has completed and
        /// <see cref="_levelingUpInProgress"/> has been cleared (CR-2.10, AC-LS-08) — never
        /// mid-iteration. Emitted via <see cref="FireOnLevelUp"/>, which isolates per-subscriber
        /// exceptions (CR-2.10) rather than a bare <c>?.Invoke()</c> multicast call.
        /// </remarks>
        public event Action<LevelUpEventArgs> OnLevelUp;

        /// <summary>
        /// Number of times <see cref="NotifyExperienceCrossedThreshold"/> has been called.
        /// Story 001 test-observability seam, retained across Story 002 — increments before
        /// the real level-up sequence runs, regardless of how many levels the call ultimately
        /// resolves to.
        /// </summary>
        public int NotifyExperienceCrossedThresholdCallCount { get; private set; }

        /// <summary>
        /// Entity ID passed to the most recent <see cref="NotifyExperienceCrossedThreshold"/>
        /// call, or <see cref="IronGrind.CharacterStats.EntityID.Invalid"/> if never called.
        /// </summary>
        public IronGrind.CharacterStats.EntityID LastNotifiedEntityId { get; private set; } =
            IronGrind.CharacterStats.EntityID.Invalid;

        /// <param name="xpThresholds">
        /// XP threshold lookup table, indexed by level: <c>xpThresholds[level + 1]</c> is the XP
        /// total required to cross from <c>level</c> into <c>level + 1</c>. The real table is
        /// owned by Story 010 (currently Blocked); this story's tests supply a small test-local
        /// table. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="classRegistry">
        /// Forward-dependency stand-in for the not-yet-built Class System epic (see class
        /// remarks). Optional — defaults to an empty <see cref="ClassRegistry"/> so CR-2.4
        /// auto-alloc silently no-ops (no auto-alloc, no held free points) for any classType
        /// that isn't registered, rather than throwing.
        /// </param>
        public LevelingService(IReadOnlyList<int> xpThresholds, IClassRegistry classRegistry = null)
        {
            _xpThresholds = xpThresholds ?? throw new ArgumentNullException(nameof(xpThresholds));
            _classRegistry = classRegistry ?? new ClassRegistry();
        }

        /// <summary>
        /// Wires the <see cref="IronGrind.CharacterStats.CharacterStats"/> back-reference this
        /// service reads and writes during <see cref="GetExperienceThreshold"/> and the CR-2
        /// level-up sequence. Must be called once, after both this service and the
        /// <c>CharacterStats</c> instance have been constructed — see class remarks for why
        /// this can't be constructor injection. <see cref="GetExperienceThreshold"/> and
        /// <see cref="NotifyExperienceCrossedThreshold"/> throw if called before this is set.
        /// </summary>
        public void AttachCharacterStats(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <summary>
        /// Registers <paramref name="entityId"/> as a player entity. See class remarks —
        /// forward-dependency stand-in for the not-yet-built player/session registry.
        /// </summary>
        public void RegisterPlayerEntity(IronGrind.CharacterStats.EntityID entityId)
        {
            _playerEntityIds.Add(entityId);
        }

        /// <summary>
        /// Removes <paramref name="entityId"/> from the player registry, if present. No-op if
        /// the entity was never registered.
        /// </summary>
        public void UnregisterPlayerEntity(IronGrind.CharacterStats.EntityID entityId)
        {
            _playerEntityIds.Remove(entityId);
        }

        /// <inheritdoc/>
        public bool IsPlayerEntity(IronGrind.CharacterStats.EntityID entityId)
            => _playerEntityIds.Contains(entityId);

        /// <summary>
        /// Associates <paramref name="entityId"/> with <paramref name="classType"/> for CR-2.4
        /// auto-alloc lookups. Forward-dependency stand-in for Story 009's real
        /// <c>InitializeAtL1</c> classType caching — see class remarks. An entity with no
        /// registered classType defaults to <c>0</c> for auto-alloc purposes.
        /// </summary>
        public void RegisterPlayerClassType(IronGrind.CharacterStats.EntityID entityId, byte classType)
        {
            _classTypes[entityId] = classType;
        }

        private byte GetClassType(IronGrind.CharacterStats.EntityID entityId)
            => _classTypes.TryGetValue(entityId, out byte classType) ? classType : (byte)0;

        /// <summary>
        /// Implements CR-6.1 + CR-6.2 (spawn initialization, Story 009): brings a freshly
        /// allocated <see cref="IronGrind.CharacterStats.CharacterStats"/> instance for
        /// <paramref name="entityId"/> to a valid Level-1 starting state. NOT a level-up —
        /// <see cref="OnLevelUp"/> is never fired and <see cref="NotifyExperienceCrossedThreshold"/>
        /// is never called; every write here goes through <c>SetBaseStat</c>/<c>SetCurrentHP</c>/
        /// <c>SetCurrentMP</c> directly, mirroring <see cref="ExecuteLevelUpSequence"/>'s own
        /// writes without ever touching the threshold-crossed path.
        /// </summary>
        /// <remarks>
        /// <para><b>Sequence (CR-6.2, fixed order):</b>
        /// <list type="number">
        /// <item>Cache <paramref name="classType"/> via <see cref="RegisterPlayerClassType"/> —
        /// the canonical CR-6.1 caching path; reused unmodified, not duplicated here.</item>
        /// <item><c>SetBaseStat(Level, 1)</c>.</item>
        /// <item><c>SetBaseStat</c> STR=DEX=VIT=INT=10.</item>
        /// <item><see cref="RecomputeDerivedStats"/> at a LITERAL tier of <c>1.0</c> (CR-6.2 step
        /// 3 pins "&#215;1.0" explicitly rather than deriving it from
        /// <see cref="GetLevelTierMultiplier"/> — the two are numerically equal at Level 1, but
        /// the spec's wording is followed literally).</item>
        /// <item><c>SetCurrentHP(entityId, MaxHP)</c>, <c>SetCurrentMP(entityId, MaxMP)</c> — the
        /// same GDD-vs-reality fix Story 002 already applied to
        /// <see cref="ExecuteLevelUpSequence"/>'s CR-2.7 block: the GDD's literal CR-6.2 step 4
        /// text (<c>SetBaseStat(CurrentHP, MaxHP)</c>) is wrong — <c>CurrentHP</c>/<c>CurrentMP</c>
        /// live in separate dictionaries, not the <c>SetBaseStat</c> int array.</item>
        /// <item><c>heldFreePoints = 0</c> (direct field write, matching this field's existing
        /// access pattern elsewhere in this class), <c>SetBaseStat(Experience, 0)</c>.</item>
        /// </list>
        /// </para>
        /// <para><b>Transaction-safety.</b> Every primitive this method calls (<c>SetBaseStat</c>,
        /// <c>SetBaseStatFloat</c> via <see cref="RecomputeDerivedStats"/>, <c>SetCurrentHP</c>/
        /// <c>SetCurrentMP</c>) is already transaction-aware — this method is therefore naturally
        /// safe to call inside a caller's own <c>BeginStatTransaction()</c>/<c>EndStatTransaction()</c>
        /// pair. Opening that transaction is the Class System's responsibility (CR-6.2's
        /// parenthetical, Class System SA-2), NOT this method's — no transaction handling is
        /// added here.</para>
        /// </remarks>
        /// <param name="entityId">The freshly spawned entity to initialize.</param>
        /// <param name="classType">
        /// The entity's class, cached via <see cref="RegisterPlayerClassType"/> for all
        /// subsequent auto-alloc reads (CR-6.1) — never re-queried at level-up.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// <see cref="AttachCharacterStats"/> has not been called yet.
        /// </exception>
        public void InitializeAtL1(IronGrind.CharacterStats.EntityID entityId, byte classType)
        {
            if (_stats == null)
                throw new InvalidOperationException(
                    "[LevelingService] InitializeAtL1 called before AttachCharacterStats — startup wiring is incomplete.");

            // CR-6.1 — cache classType via the canonical caching path; not duplicated here.
            RegisterPlayerClassType(entityId, classType);

            // CR-6.2 step 1.
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Level, 1);

            // CR-6.2 step 2.
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Strength, 10);
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Dexterity, 10);
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Vitality, 10);
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Intelligence, 10);

            // CR-6.2 step 3 — literal x1.0, not GetLevelTierMultiplier(1) (see remarks).
            RecomputeDerivedStats(entityId, 1.0f);

            // CR-6.2 step 4 — GDD-vs-reality fix (see remarks): SetCurrentHP/SetCurrentMP, not
            // SetBaseStat(CurrentHP/CurrentMP, ...).
            _stats.SetCurrentHP(entityId, _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.MaxHP));
            _stats.SetCurrentMP(entityId, _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.MaxMP));

            // CR-6.2 step 5.
            _heldFreePoints[entityId] = 0;
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Experience, 0);
        }

        /// <summary>
        /// Returns the number of free (player-allocatable) stat points currently held for
        /// <paramref name="entityId"/>. Not stored in <c>CharacterStats</c> — owned entirely by
        /// the Leveling System (CR-3.1).
        /// </summary>
        /// <remarks>
        /// <b>Scope note:</b> the CR-2.8 grant increment (<c>_heldFreePoints[entityId] +=
        /// def.FreePointsPerLevel</c> during CR-2.4) has been implemented since Story 002 —
        /// needed to satisfy AC-LS-05/AC-LS-07's assertion that it increments correctly per class
        /// (Warrior +1, Healer +2). <c>AllocateFreePoint</c>'s real CR-3 spend semantics (Story
        /// 005) decrement this same counter — see <see cref="AllocateFreePoint"/>. Full
        /// <c>heldFreePoints</c> ownership — the CR-6.1/CR-6.2 spawn reset to 0 and the CR-6.3
        /// persistence round-trip with its EC-LS-38/EC-LS-36 tamper-defense clamps — is Story
        /// 009's scope: see <see cref="InitializeAtL1"/>, <see cref="GetLevelingState"/>, and
        /// <see cref="RestoreLevelingState"/>.
        /// </remarks>
        public int GetHeldFreePoints(IronGrind.CharacterStats.EntityID entityId)
            => _heldFreePoints.TryGetValue(entityId, out int held) ? held : 0;

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// <see cref="AttachCharacterStats"/> has not been called yet.
        /// </exception>
        public int GetExperienceThreshold(IronGrind.CharacterStats.EntityID entityId)
        {
            if (_stats == null)
                throw new InvalidOperationException(
                    "[LevelingService] GetExperienceThreshold called before AttachCharacterStats — startup wiring is incomplete.");

            int level = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Level);
            int index = level + 1;
            if (index < 0 || index >= _xpThresholds.Count)
                throw new ArgumentOutOfRangeException(nameof(entityId),
                    $"[LevelingService] No XP threshold entry for level {level} (index {index}); xpThresholds has {_xpThresholds.Count} entries.");

            return _xpThresholds[index];
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>Implements the full CR-2 sequence: the CR-2.1–CR-2.7 single-level-up mechanics
        /// (Story 002, delegated to <see cref="ExecuteLevelUpSequence"/>, unmodified by Story 003),
        /// the CR-2.9 consecutive-level-up loop, the <see cref="_levelingUpInProgress"/>
        /// re-entrancy guard (EC-LS-09/EC-LS-10), and the deferred CR-2.10 <see cref="OnLevelUp"/>
        /// broadcast (Story 003).</para>
        /// <para><b>AC-LS-49 re-entrancy guard — exact call sequence this guards against</b>
        /// (traced against the real, already-shipped <c>CharacterStats._isFiring</c> guard
        /// (Story 005), not the GDD's literal EC-LS-10 prose — see the Story 003 completion
        /// notes for the documented GDD/implementation discrepancy this trace resolves):
        /// <list type="number">
        /// <item>Some caller invokes this method — the <b>OUTER</b> call. The guard below is
        /// <see langword="false"/>, so it passes, sets <see cref="_levelingUpInProgress"/> to
        /// <see langword="true"/>, and enters the CR-2.9 loop.</item>
        /// <item>Inside the loop, <see cref="ExecuteLevelUpSequence"/> calls
        /// <c>_stats.SetBaseStat(...)</c> repeatedly (Level, auto-alloc stats, derived stats,
        /// CurrentHP/CurrentMP). Each call synchronously fires
        /// <c>CharacterStats.OnStatChanged</c> (<c>CharacterStats._isFiring == true</c> for the
        /// duration of each individual firing).</item>
        /// <item>A subscriber's <c>OnStatChanged</c> handler — invoked from inside step 2's
        /// firing — calls <see cref="NotifyExperienceCrossedThreshold"/> <b>directly</b> (NOT
        /// via <c>AddExperience</c> — the literal GDD scenario of a handler calling
        /// <c>AddExperience</c> is a <i>different</i> case: that inner call's own
        /// <c>SetBaseStat(Experience, ...)</c> hits <c>CharacterStats._isFiring</c> — which is
        /// still <see langword="true"/> from step 2 — and throws <see cref="InvalidOperationException"/>
        /// from inside <c>CharacterStats</c> itself, before ever reaching this method; see the
        /// AC-LS-49 test file for a regression test proving that). This is the
        /// <b>RE-ENTRANT</b> call, nested synchronously inside the OUTER call's still-running
        /// loop.</item>
        /// <item>The RE-ENTRANT call reaches the guard below. <see cref="_levelingUpInProgress"/>
        /// is still <see langword="true"/> (the OUTER call has not finished its loop) — the
        /// RE-ENTRANT call returns immediately: no <see cref="ExecuteLevelUpSequence"/> call, no
        /// <c>SetBaseStat(Level, ...)</c>, no exception.</item>
        /// <item>Control returns to step 2's still-executing <c>SetBaseStat</c>/
        /// <c>FireOnStatChanged</c>, which then returns normally; the OUTER call's loop
        /// continues its own next iteration exactly as if the RE-ENTRANT call never happened.</item>
        /// <item>The OUTER call's loop keeps iterating — its own fresh
        /// <see cref="GetExperienceThreshold"/>/<c>Experience</c> reads, unaffected by the
        /// RE-ENTRANT call's early return — until no further level is gained, then falls out of
        /// the loop and clears <see cref="_levelingUpInProgress"/> in the <c>finally</c> block,
        /// then fires <see cref="OnLevelUp"/> once per level actually gained, in ascending order
        /// (AC-LS-08).</item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <see cref="AttachCharacterStats"/> has not been called yet.
        /// </exception>
        public void NotifyExperienceCrossedThreshold(IronGrind.CharacterStats.EntityID entityId)
        {
            NotifyExperienceCrossedThresholdCallCount++;
            LastNotifiedEntityId = entityId;

            if (_stats == null)
                throw new InvalidOperationException(
                    "[LevelingService] NotifyExperienceCrossedThreshold called before AttachCharacterStats — startup wiring is incomplete.");

            // EC-LS-09 / EC-LS-10 / AC-LS-49 — re-entrancy guard. See the exact call-sequence
            // trace in this method's doc comment above. A blocked re-entrant call is a silent
            // no-op: it never touches ExecuteLevelUpSequence or SetBaseStat, and never throws.
            if (_levelingUpInProgress)
                return;

            _levelingUpInProgress = true;
            List<int> levelsGained = null;
            try
            {
                // CR-2.9 — consecutive level-up loop. Each iteration re-reads Level/Experience
                // fresh (never cached across iterations) and calls the unmodified Story 002
                // single-level sequence, which itself reads fresh CR-2.5 totals from its own
                // CR-2.4 auto-alloc writes (AC-LS-09).
                while (true)
                {
                    int levelBefore = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Level);
                    if (levelBefore >= 60)
                        break; // at level cap — avoid an out-of-range GetExperienceThreshold lookup.

                    int threshold = GetExperienceThreshold(entityId); // reads the CURRENT level fresh, every iteration.
                    int currentXp = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Experience);
                    if (currentXp < threshold)
                        break; // CR-2.9 stop condition.

                    ExecuteLevelUpSequence(entityId);

                    int levelAfter = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Level);
                    if (levelAfter <= levelBefore)
                        break; // No level gained this iteration (at-cap guard fired inside
                               // ExecuteLevelUpSequence) — stop rather than looping forever.

                    (levelsGained ??= new List<int>()).Add(levelAfter);
                }
            }
            finally
            {
                _levelingUpInProgress = false;
            }

            // CR-2.10 / AC-LS-08 — broadcast only AFTER the loop has fully completed and
            // _levelingUpInProgress has been cleared, one call per level gained, ascending order.
            if (levelsGained != null)
            {
                for (int i = 0; i < levelsGained.Count; i++)
                    FireOnLevelUp(entityId, levelsGained[i]);
            }
        }

        /// <summary>
        /// Emits <see cref="OnLevelUp"/> to every current subscriber, isolating each
        /// subscriber's exceptions individually (CR-2.10) — deliberately not a bare
        /// <c>OnLevelUp?.Invoke(...)</c> multicast call, which would let one throwing subscriber
        /// prevent every subscriber after it (and every subsequent level's notification) from
        /// running.
        /// </summary>
        private void FireOnLevelUp(IronGrind.CharacterStats.EntityID entityId, int newLevel)
        {
            var handler = OnLevelUp;
            if (handler == null)
                return;

            var args = new LevelUpEventArgs(entityId, newLevel);
            Delegate[] invocationList = handler.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<LevelUpEventArgs>)invocationList[i]).Invoke(args);
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[LevelingService] OnLevelUp subscriber threw (entity={entityId}, newLevel={newLevel}): {ex}");
                }
            }
        }

        /// <summary>
        /// Implements the real CR-3 free-point spend sequence (Story 005): busy-check (Story
        /// 003, unmodified) → Guard 1 (no held points) → Guard 2 (invalid target stat) →
        /// decrement → <c>SetBaseStat</c> write → F-3–F-9 derived-stat recompute at the
        /// on-demand <see cref="GetLevelTierMultiplier"/> tier. Each call is immediately
        /// committed (CR-3.3) — there is no preview/confirm or undo path at this API layer; a
        /// client-side preview/confirm UX belongs in the Stat Screen UI (Story 013), not here.
        /// </summary>
        /// <remarks>
        /// <b>Deliberate asymmetry with <see cref="ExecuteLevelUpSequence"/>'s CR-2.7:</b> a
        /// level-up fully restores <c>CurrentHP</c>/<c>CurrentMP</c> to the freshly recomputed
        /// Max values; a free-point spend does NOT — <c>CurrentHP</c>/<c>CurrentMP</c> are left
        /// exactly as they were before this call, even when the recompute increases
        /// MaxHP/MaxMP (AC-LS-14). <c>SetCurrentHP</c>/<c>SetCurrentMP</c> are never called here.
        /// </remarks>
        /// <param name="entityId">The entity spending the point.</param>
        /// <param name="statId">
        /// The target primary stat. Only <c>Strength</c>/<c>Dexterity</c>/<c>Vitality</c>/
        /// <c>Intelligence</c> are valid — any other <see cref="IronGrind.CharacterStats.StatID"/>
        /// (including <c>Level</c>, which has separate write ownership via CR-2.2) is rejected by
        /// Guard 2 before any write occurs.
        /// </param>
        public AllocateFreePointResult AllocateFreePoint(IronGrind.CharacterStats.EntityID entityId, IronGrind.CharacterStats.StatID statId)
        {
            // EC-LS-09 / AC-LS-10 — the guard fires before any decrement; a rejected call must
            // never consume a free point. (Story 003, unmodified.)
            if (_levelingUpInProgress)
                return AllocateFreePointResult.RejectedSystemBusy;

            // Guard 1 (CR-3 / AC-LS-12) — nothing to spend. No write.
            int held = GetHeldFreePoints(entityId);
            if (held == 0)
                return AllocateFreePointResult.RejectedNoFreePoints;

            // Guard 2 (CR-3 / AC-LS-13) — only the four primary stats are allocatable targets.
            // Fires BEFORE the decrement below. Structurally guarantees StatID.Level (and every
            // derived/resource/progression StatID) can never be reached through this API.
            if (statId != IronGrind.CharacterStats.StatID.Strength &&
                statId != IronGrind.CharacterStats.StatID.Dexterity &&
                statId != IronGrind.CharacterStats.StatID.Vitality &&
                statId != IronGrind.CharacterStats.StatID.Intelligence)
                return AllocateFreePointResult.RejectedInvalidStat;

            // CR-3.3 — immediately committed: decrement, then write.
            _heldFreePoints[entityId] = held - 1;

            _stats.SetBaseStat(entityId, statId, _stats.GetBaseStat(entityId, statId) + 1);

            // F-3–F-9 recompute at the CURRENT tier, derived fresh from GetBaseStat(Level) —
            // never a stored/cached multiplier (AC-LS-16) — using the same formula set and
            // write methods as ExecuteLevelUpSequence's CR-2.6 block, via the shared helper.
            // CurrentHP/CurrentMP are deliberately left untouched — see method remarks.
            int level = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Level);
            float tier = GetLevelTierMultiplier(level);
            RecomputeDerivedStats(entityId, tier);

            return AllocateFreePointResult.Success;
        }

        /// <summary>
        /// Serializes the Leveling-System-owned per-entity state that Character Persistence
        /// cannot derive from <c>CharacterStats</c> alone (CR-6.3, Story 009) — currently just
        /// <c>heldFreePoints</c>, which has no <c>StatID</c> and is not recoverable from
        /// <see cref="IronGrind.CharacterStats.CharacterStats.GetBaseStat"/> (AC-LS-29). Pair with
        /// <see cref="RestoreLevelingState"/> on load.
        /// </summary>
        public LevelingStateSnapshot GetLevelingState(IronGrind.CharacterStats.EntityID entityId)
            => new LevelingStateSnapshot(GetHeldFreePoints(entityId));

        /// <summary>
        /// Implements CR-6.3's load path (Story 009): restores <paramref name="snapshot"/>'s
        /// <c>heldFreePoints</c> for <paramref name="entityId"/>, after Character Persistence has
        /// already restored all base stats via <c>SetBaseStat()</c> directly. Never calls
        /// <see cref="NotifyExperienceCrossedThreshold"/> and never fires <see cref="OnLevelUp"/>
        /// — load bypasses <c>AddExperience</c> entirely, the same discipline spawn uses.
        /// </summary>
        /// <remarks>
        /// <para><b>Tamper defense (EC-LS-38, EC-LS-36, and the atomicity-recovery invariant note
        /// immediately following EC-LS-36).</b> Required because this is a live MMORPG and save
        /// data can be corrupted or tampered with. Step order below is load-bearing: the
        /// <c>Level</c> clamp MUST run before the <c>heldFreePoints</c> clamp, because the
        /// <c>heldFreePoints</c> maximum is computed FROM the already-clamped <c>Level</c> — this
        /// is what makes the guard also catch the "partial-write crash recovery" case (Story
        /// 003's CR-2.8 atomicity requirement): a crash between writing <c>Level</c> and writing
        /// <c>heldFreePoints</c> can leave a pair where neither value individually exceeds its
        /// own bound but the PAIR is inconsistent (e.g. <c>Level=5</c> but
        /// <c>heldFreePoints=59</c>, a value only legal at Level 60). Re-deriving the max from
        /// the clamped Level on every load catches this automatically, with no separate
        /// detection logic needed. The inverse crash (<c>heldFreePoints</c> written, <c>Level</c>
        /// not yet advanced) is accepted as a safe-fail undercount, not corrected further.</para>
        /// <list type="number">
        /// <item>Read <c>level = GetBaseStat(Level)</c>.</item>
        /// <item>If outside <c>[1, 60]</c>: clamp to the nearest bound, log an error (EC-LS-38 /
        /// AC-LS-51). Required because an unclamped <c>Level=70</c> would cause CR-2.9 to access
        /// <c>XpThreshold[71]</c>, beyond the 62-entry array — <see cref="IndexOutOfRangeException"/>.</item>
        /// <item>If clamping changed the value: write it back via <c>SetBaseStat</c>. This may
        /// legitimately fire <c>OnStatChanged</c> outside a transaction — expected, and not
        /// forbidden by any AC (only <c>OnLevelUp</c>/<c>OnExperienceThresholdCrossed</c> are
        /// forbidden on this path).</item>
        /// <item>Look up the entity's cached classType and its <see cref="ClassDefinition"/> —
        /// unregistered defaults to a zero-valued struct (<c>FreePointsPerLevel == 0</c>), the
        /// same fallback <see cref="ExecuteLevelUpSequence"/>/<see cref="TryApplyRespec"/> already
        /// use.</item>
        /// <item>Compute <c>maxHeld = (clampedLevel - 1) * def.FreePointsPerLevel</c>.</item>
        /// <item>If <paramref name="snapshot"/>'s <c>HeldFreePoints</c> is outside
        /// <c>[0, maxHeld]</c>: clamp, log an error (EC-LS-36 / AC-LS-30).</item>
        /// <item>Write the (possibly clamped) value into the <c>heldFreePoints</c> store.</item>
        /// </list>
        /// <para>No transaction is opened here — unlike CR-6.2's spawn path, CR-6.3 does not
        /// describe a <c>BeginStatTransaction</c>/<c>EndStatTransaction</c> pair for the load
        /// path, so none is added.</para>
        /// <para><b>Precondition (code review finding, Story 009): <see cref="RegisterPlayerClassType"/>
        /// must already have been called for <paramref name="entityId"/> in this
        /// <see cref="LevelingService"/> instance's lifetime before calling this method</b> — the
        /// <c>heldFreePoints</c> clamp above reads the classType cache via <see cref="GetClassType"/>,
        /// which silently defaults to <c>0</c> for an unregistered entity. On a real session restart
        /// (e.g. a returning player logging back into a fresh server process, where this service's
        /// in-memory classType cache starts empty), calling this method before the caller has
        /// re-registered the entity's classType will resolve <c>def.FreePointsPerLevel</c> from
        /// classType <c>0</c>'s registration (or the zero-valued fallback if unregistered), silently
        /// clamping — and error-logging — the entity's legitimately-held free points down to
        /// whatever that fallback allows, most commonly <c>0</c>. This is not a bug in this method;
        /// it is a caller-ordering contract the not-yet-built Character Persistence load path must
        /// honor: re-establish <see cref="RegisterPlayerClassType"/> (or call <see cref="InitializeAtL1"/>
        /// where applicable) before <see cref="RestoreLevelingState"/> in every session.</para>
        /// </remarks>
        /// <param name="entityId">
        /// The entity being restored from persistence. <see cref="RegisterPlayerClassType"/> must
        /// already have been called for this entity in the current session — see the precondition
        /// note above.
        /// </param>
        /// <param name="snapshot">The previously saved state, from <see cref="GetLevelingState"/>.</param>
        /// <exception cref="InvalidOperationException">
        /// <see cref="AttachCharacterStats"/> has not been called yet.
        /// </exception>
        public void RestoreLevelingState(IronGrind.CharacterStats.EntityID entityId, LevelingStateSnapshot snapshot)
        {
            if (_stats == null)
                throw new InvalidOperationException(
                    "[LevelingService] RestoreLevelingState called before AttachCharacterStats — startup wiring is incomplete.");

            // EC-LS-38 / AC-LS-51 — Level clamp MUST happen first; the heldFreePoints max below
            // is derived from the CLAMPED level (see method remarks for the partial-write crash
            // recovery rationale this ordering satisfies).
            int level = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Level);
            int clampedLevel;
            if (level < 1)
            {
                clampedLevel = 1;
                Debug.LogError(
                    $"[LevelingService] RestoreLevelingState: entity {entityId} restored Level={level}, below the valid [1,60] range — clamped to 1.");
            }
            else if (level > 60)
            {
                clampedLevel = 60;
                Debug.LogError(
                    $"[LevelingService] RestoreLevelingState: entity {entityId} restored Level={level}, above the valid [1,60] range — clamped to 60.");
            }
            else
            {
                clampedLevel = level;
            }

            if (clampedLevel != level)
                _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Level, clampedLevel);

            // EC-LS-36 / AC-LS-30 — heldFreePoints clamp, using the ALREADY-CLAMPED level.
            byte classType = GetClassType(entityId);
            _classRegistry.TryGetClass(classType, out ClassDefinition def); // unregistered -> FreePointsPerLevel=0, same fallback as ExecuteLevelUpSequence/TryApplyRespec.
            int maxHeld = (clampedLevel - 1) * def.FreePointsPerLevel;

            int heldFreePoints = snapshot.HeldFreePoints;
            if (heldFreePoints < 0)
            {
                Debug.LogError(
                    $"[LevelingService] RestoreLevelingState: entity {entityId} restored heldFreePoints={heldFreePoints}, below 0 — clamped to 0.");
                heldFreePoints = 0;
            }
            else if (heldFreePoints > maxHeld)
            {
                Debug.LogError(
                    $"[LevelingService] RestoreLevelingState: entity {entityId} restored heldFreePoints={heldFreePoints}, exceeding the Level {clampedLevel} maximum of {maxHeld} — clamped to {maxHeld}.");
                heldFreePoints = maxHeld;
            }

            _heldFreePoints[entityId] = heldFreePoints;
        }

        /// <summary>
        /// Executes exactly one level-up: CR-2.1 (at-cap guard) through CR-2.7 (HP/MP restore),
        /// plus the conditional CR-2.2a XP overshoot clamp (Story 004, fires only when the
        /// level-up lands exactly on 60) and the minimal CR-2.8 <c>heldFreePoints</c>
        /// bookkeeping described on <see cref="GetHeldFreePoints"/>. Fixed, non-reorderable
        /// sequence — see the story's Implementation Notes for the full CR-2 derivation.
        /// </summary>
        private void ExecuteLevelUpSequence(IronGrind.CharacterStats.EntityID entityId)
        {
            var statId = IronGrind.CharacterStats.StatID.Level;

            // CR-2.1 — At-Cap Guard.
            int currentLevel = _stats.GetBaseStat(entityId, statId);
            if (currentLevel == 60)
                return;

            // CR-2.2 — Increment Level.
            int newLevel = currentLevel + 1;
            _stats.SetBaseStat(entityId, statId, newLevel);

            // CR-2.2a — At-Cap XP Clamp (conditional, Story 004). Fires exactly once per
            // character, on the 59->60 transition only (CR-2.1's at-cap guard above prevents
            // this method — and therefore this branch — from ever running again once Level is
            // already 60). Absorbs any XP overshoot from the AddExperience call that triggered
            // this level-up so GetBaseStat(Experience) reads XpThreshold[60] exactly, for every
            // subsequent step in THIS call (auto-alloc, derived-stat recompute, HP/MP restore)
            // and for any external reader (e.g. the HUD) immediately afterward. This is the
            // canonical clamp path for the L59->L60 transition specifically; CR-5.2 (Story 008)
            // is the separate clamp path for XP gained AFTER the character is already at L60
            // (post-cap kills) — not this transition, and not this story's scope.
            if (newLevel == 60)
            {
                _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Experience, _xpThresholds[60]);
            }

            // CR-2.3 — Determine LevelTierMultiplier, based on the NEW level.
            float tier = GetLevelTierMultiplier(newLevel);

            // CR-2.4 — Auto-Allocate Attribute Points, fixed {STR, DEX, VIT, INT} order.
            byte classType = GetClassType(entityId);
            if (_classRegistry.TryGetClass(classType, out ClassDefinition def))
            {
                for (int i = 0; i < AutoAllocOrder.Length; i++)
                {
                    var stat = AutoAllocOrder[i];
                    int increment = GetAutoAllocIncrement(def, stat);
                    if (increment > 0)
                        _stats.SetBaseStat(entityId, stat, _stats.GetBaseStat(entityId, stat) + increment);
                }

                // CR-2.8 — minimal heldFreePoints bookkeeping only; see GetHeldFreePoints remarks.
                if (def.FreePointsPerLevel > 0)
                {
                    _heldFreePoints.TryGetValue(entityId, out int held);
                    _heldFreePoints[entityId] = held + def.FreePointsPerLevel;
                }
            }

            // CR-2.5 / CR-2.6 — Read Current Totals (after CR-2.4 so auto-alloc is included) and
            // Recompute Derived Base Stats, fixed evaluation order. Shared with AllocateFreePoint
            // (Story 005) via RecomputeDerivedStats — see that method's doc comment; the formula
            // set, order, and write methods are identical to this block's pre-Story-005 form.
            RecomputeDerivedStats(entityId, tier);

            // CR-2.7 — Full HP/MP Restore, using the values just written above.
            _stats.SetCurrentHP(entityId, _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.MaxHP));
            _stats.SetCurrentMP(entityId, _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.MaxMP));
        }

        /// <summary>
        /// CR-2.6's derived-stat recompute block (F-3–F-9): reads the CURRENT Strength/
        /// Dexterity/Vitality/Intelligence totals fresh and writes MaxHP, MaxMP, AttackPower,
        /// Defense, MagicDefense (all via <c>SetBaseStat</c>, MaxMP additionally clamped to
        /// 9,999), then CritChance and AttackSpeedMultiplier (both via <c>SetBaseStatFloat</c>,
        /// unclamped) — in that fixed order. Shared by <see cref="ExecuteLevelUpSequence"/>
        /// (Story 002/004, called after its own CR-2.4 auto-alloc writes) and
        /// <see cref="AllocateFreePoint"/> (Story 005, called after its own single-stat write) so
        /// the formula set has exactly one definition. Deliberately does NOT touch
        /// CurrentHP/CurrentMP — HP/MP restore (CR-2.7) is the caller's responsibility, and
        /// <see cref="AllocateFreePoint"/> deliberately never performs it (AC-LS-14).
        /// </summary>
        private void RecomputeDerivedStats(IronGrind.CharacterStats.EntityID entityId, float tier)
        {
            int strength = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Strength);
            int dexterity = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Dexterity);
            int vitality = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Vitality);
            int intelligence = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Intelligence);

            int maxHp = Mathf.FloorToInt((200 + vitality * 20) * tier);
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.MaxHP, maxHp);

            int maxMp = Mathf.Min(Mathf.FloorToInt((100 + intelligence * 12) * tier), 9999);
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.MaxMP, maxMp);

            int attackPower = Mathf.FloorToInt((10 + strength * 2) * tier);
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.AttackPower, attackPower);

            int defense = Mathf.FloorToInt((5 + vitality * 1.5f) * tier);
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.Defense, defense);

            int magicDefense = Mathf.FloorToInt(intelligence * 0.4f * tier);
            _stats.SetBaseStat(entityId, IronGrind.CharacterStats.StatID.MagicDefense, magicDefense);

            float critChance = 0.05f + (dexterity * 0.0015f * tier);
            _stats.SetBaseStatFloat(entityId, IronGrind.CharacterStats.StatID.CritChance, critChance);

            float attackSpeedMultiplier = 1.0f + (dexterity * 0.003f * tier);
            _stats.SetBaseStatFloat(entityId, IronGrind.CharacterStats.StatID.AttackSpeedMultiplier, attackSpeedMultiplier);
        }

        /// <summary>
        /// Implements the CR-4.4 respec commit sequence (Story 006): CR-4.3 floor validation
        /// with NO transaction opened yet, then BeginStatTransaction, the 4 primary-attribute
        /// writes, a single RecomputeDerivedStats call (F-3-F-9, shared with
        /// <see cref="ExecuteLevelUpSequence"/>/<see cref="AllocateFreePoint"/>), a
        /// CurrentHP/CurrentMP re-assertion that only ever clamps DOWN (never
        /// restores/heals — see remarks), heldFreePoints left untouched (CR-4.2), then
        /// EndStatTransaction. See the story's Implementation Notes for the resolved
        /// signature (a real GDD gap: the literal GDD text has
        /// <c>TryApplyRespec(EntityID)</c> with no way to carry <c>newFreeAlloc</c>).
        /// </summary>
        /// <remarks>
        /// <para><b>CurrentHP/CurrentMP reconciliation (Story 006 fix, in coordination with
        /// the CharacterStats transaction-awareness fix on <c>SetCurrentHP</c>/
        /// <c>SetCurrentMP</c>).</b> The GDD's CR-4.4 step 4 assumed an already-implemented
        /// "EC-05" general reconciliation mechanism inside CharacterStats that in fact did not
        /// exist prior to this story — <c>SetBaseStat(MaxHP/MaxMP, ...)</c> alone never
        /// touched CurrentHP/CurrentMP. This method re-asserts the CURRENT (already-stored)
        /// HP/MP value immediately after <see cref="RecomputeDerivedStats"/>;
        /// <c>SetCurrentHP</c>/<c>SetCurrentMP</c>'s own clamp-to-ceiling logic does the real
        /// work — if the just-recomputed MaxHP/MaxMP is now below CurrentHP/CurrentMP it
        /// clamps down, otherwise the value passes through unchanged. This never
        /// heals/restores (CR-4.5 is still honored — no free healing), it only ever lowers or
        /// no-ops. Both calls happen before
        /// <see cref="IronGrind.CharacterStats.CharacterStats.EndStatTransaction"/>, so they
        /// are deferred and deduped into the same single batched pass as every other stat this
        /// method writes (AC-LS-17 / AC-LS-21 / AC-LS-52).</para>
        /// </remarks>
        /// <param name="entityId">The entity being respec'd.</param>
        /// <param name="newTotals">
        /// Caller's proposed NEW TOTAL (not delta) for each of the 4 primary attributes. Must
        /// contain exactly {Strength, Dexterity, Vitality, Intelligence} — no more, no fewer.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="newTotals"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="newTotals"/> does not contain exactly the 4 required keys, or any
        /// value is below its class-specific CR-4.3 floor. Thrown before BeginStatTransaction
        /// is ever called — no transaction opened, no writes made (AC-LS-20).
        /// </exception>
        public void TryApplyRespec(
            IronGrind.CharacterStats.EntityID entityId,
            IReadOnlyDictionary<IronGrind.CharacterStats.StatID, int> newTotals)
        {
            if (newTotals == null)
                throw new ArgumentNullException(nameof(newTotals));

            if (newTotals.Count != AutoAllocOrder.Length)
                throw new ArgumentException(
                    $"[LevelingService] TryApplyRespec: newTotals must contain exactly the 4 " +
                    $"primary attributes (Strength, Dexterity, Vitality, Intelligence) — got " +
                    $"{newTotals.Count} entries.", nameof(newTotals));

            int level = _stats.GetBaseStat(entityId, IronGrind.CharacterStats.StatID.Level);
            byte classType = GetClassType(entityId);
            _classRegistry.TryGetClass(classType, out ClassDefinition def); // unregistered -> all-zero increments -> floor=10 uniformly, same fallback as ExecuteLevelUpSequence.

            // CR-4.4 step 1 — floor validation, ALL 4 stats, before any write / before BeginStatTransaction.
            for (int i = 0; i < AutoAllocOrder.Length; i++)
            {
                var stat = AutoAllocOrder[i];
                if (!newTotals.TryGetValue(stat, out int proposed))
                    throw new ArgumentException(
                        $"[LevelingService] TryApplyRespec: newTotals is missing required key {stat}.",
                        nameof(newTotals));

                int increment = GetAutoAllocIncrement(def, stat);
                int floor = 10 + (level - 1) * increment;
                if (proposed < floor)
                    throw new ArgumentException(
                        $"[LevelingService] TryApplyRespec: {stat} total {proposed} is below its " +
                        $"CR-4.3 floor {floor} at Level {level}.", nameof(newTotals));
            }

            // CR-4.4 steps 2-6. Wrapped try/catch (Story 007): any exception here must leave no
            // partial write behind and must never reach EndStatTransaction — see EC-LS-22/
            // EC-LS-23/AC-LS-22 and the catch block below.
            try
            {
                _stats.BeginStatTransaction();

                for (int i = 0; i < AutoAllocOrder.Length; i++)
                {
                    var stat = AutoAllocOrder[i];
                    _stats.SetBaseStat(entityId, stat, newTotals[stat]);
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                    // AC-LS-22 test-only injection point — see field doc comment below. Never
                    // set in production; this project has no naturally-throwing step in this
                    // loop today, so this is this story's own seam for proving the exception
                    // path, not a permanent production hook.
                    TestOnly_ThrowDuringRespecStep2?.Invoke(entityId);
#endif
                }

                // Level is invariant across this method (never written by TryApplyRespec) —
                // reuse the value already read for the CR-4.3 floor check above rather than
                // re-reading.
                float tier = GetLevelTierMultiplier(level);
                RecomputeDerivedStats(entityId, tier);
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                // EC-LS-23 test-only injection point — broadens AC-LS-22's step-2-only literal
                // coverage to CR-4.4 step 3 (immediately after RecomputeDerivedStats, before the
                // CurrentHP/CurrentMP reassertion). Same rationale/guard as
                // TestOnly_ThrowDuringRespecStep2 above — never set in production.
                TestOnly_ThrowAfterRecomputeDerivedStats?.Invoke(entityId);
#endif

                // Re-assert the CURRENT HP/MP value — SetCurrentHP/SetCurrentMP's own clamp
                // does the real work if the recompute above just lowered MaxHP/MaxMP below it.
                // Never raises the value.
                _stats.SetCurrentHP(entityId, _stats.GetCurrentHP(entityId));
                _stats.SetCurrentMP(entityId, _stats.GetCurrentMP(entityId));

                // heldFreePoints intentionally untouched here — CR-4.2 / AC-LS-19.

                _stats.EndStatTransaction();
            }
            catch
            {
                // EC-LS-22 / EC-LS-23 / AC-LS-22 — unconditional rollback on any exception
                // during CR-4.4 steps 2-6. Safe as a no-op if no transaction is open (Character
                // Stats F-10) — do not re-guard this with an "is a transaction open" check.
                // Now that Character Stats Story 007 was revised (see CharacterStats.
                // RollbackStatTransaction's doc comment), this actually reverts every stat
                // touched by the aborted transaction, not just the deferred event queue.
                // EndStatTransaction() is never reached on this path. Re-thrown so the
                // (test-double) Inventory-System-side caller can react — CR-4.1 two-phase
                // commit: Release() on exception, Consume() on success.
                _stats.RollbackStatTransaction();
                throw;
            }
        }

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
        /// <summary>
        /// Test-only injection point for AC-LS-22 exception-safety testing (Story 007). When
        /// set, invoked once per <see cref="TryApplyRespec"/> call, immediately after each
        /// CR-4.4 step 2 primary-attribute write — i.e. once per iteration of the write loop,
        /// letting a test throw after N stats have been written (e.g. after the 2nd of 4, to
        /// prove partial writes are rolled back and untouched stats were never disturbed).
        /// Compiled out of release/Player builds (guarded by <c>UNITY_INCLUDE_TESTS</c> /
        /// <c>DEVELOPMENT_BUILD</c>, matching this project's established fault-injection seam
        /// pattern — see <c>TransportFaultInjector</c>/<c>ITransportFaultInjector</c> in the
        /// Networking Core epic). Production code must never set this. Reset to
        /// <see langword="null"/> between tests.
        /// </summary>
        internal Action<IronGrind.CharacterStats.EntityID> TestOnly_ThrowDuringRespecStep2;

        /// <summary>
        /// Test-only injection point for EC-LS-23 exception-safety testing (Story 007),
        /// broadening <see cref="TestOnly_ThrowDuringRespecStep2"/>'s step-2-only coverage to
        /// CR-4.4 step 3. When set, invoked once per <see cref="TryApplyRespec"/> call,
        /// immediately after <c>RecomputeDerivedStats</c> has run (i.e. after MaxHP/MaxMP/
        /// AttackPower/Defense/MagicDefense/CritChance/AttackSpeedMultiplier have already been
        /// written to their new values) but before the CurrentHP/CurrentMP reassertion (step 4).
        /// Same guard and "never set in production" rationale as
        /// <see cref="TestOnly_ThrowDuringRespecStep2"/> — see its doc comment. Reset to
        /// <see langword="null"/> between tests.
        /// </summary>
        internal Action<IronGrind.CharacterStats.EntityID> TestOnly_ThrowAfterRecomputeDerivedStats;
#endif

        private static int GetAutoAllocIncrement(ClassDefinition def, IronGrind.CharacterStats.StatID stat)
        {
            switch (stat)
            {
                case IronGrind.CharacterStats.StatID.Strength:     return def.StrengthAutoAlloc;
                case IronGrind.CharacterStats.StatID.Dexterity:    return def.DexterityAutoAlloc;
                case IronGrind.CharacterStats.StatID.Vitality:     return def.VitalityAutoAlloc;
                case IronGrind.CharacterStats.StatID.Intelligence: return def.IntelligenceAutoAlloc;
                default:                                            return 0;
            }
        }

        /// <summary>
        /// CR-2.3 tier lookup: L1–19 → ×1.0, L20–39 → ×1.2, L40–59 → ×1.5, L60 → ×2.0, based on
        /// the NEW level. Pure and stateless — every call reads only <paramref name="level"/> and
        /// returns immediately; there is no field this value could be cached in, satisfying
        /// AC-LS-16/AC-LS-34's "never a stored/cached multiplier" requirement structurally, not
        /// just by convention.
        /// </summary>
        /// <remarks>
        /// <c>internal</c> rather than <c>private</c> (Story 011 test-observability seam,
        /// mirroring the <see cref="IsLevelingUpInProgress"/>/<c>TestOnly_...</c> idiom elsewhere
        /// in this class) — lets
        /// <c>LevelingSystem_TierAutoAllocFormulaVerification_tests.cs</c> (AC-LS-34) verify this
        /// lookup table's boundary values directly, in isolation from any level-up sequence,
        /// without adding any new public API surface. Unlike the <c>TestOnly_...</c> fields, this
        /// method is a permanently-present, unguarded (no <c>#if</c>) seam — acceptable because it
        /// is side-effect-free and stateless, so there is nothing for other code in this assembly
        /// to misuse even in a release build. Logic unchanged by the visibility widening.
        /// </remarks>
        internal static float GetLevelTierMultiplier(int level)
        {
            if (level >= 60) return 2.0f;
            if (level >= 40) return 1.5f;
            if (level >= 20) return 1.2f;
            return 1.0f;
        }
    }
}
