using System.Collections.Generic;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Leveling System Story 010 (XP Threshold Formula &amp; Table, F-LS-1,
    /// <c>design/gdd/leveling-system.md</c>). Pre-computed, double-precision-verified,
    /// cumulative XP threshold lookup table.
    /// </summary>
    /// <remarks>
    /// <para><b>F-LS-1 formula this table is derived from</b> (per-level cost, NOT what is stored
    /// here — see next paragraph): <c>XP(L) = Mathf.RoundToInt(C * L^alpha * R^L)</c>, with
    /// <c>C=178.6</c>, <c>alpha=0.648</c>, <c>R=1.1198</c> (interdependent — never re-tune one in
    /// isolation).</para>
    /// <para><b>This array stores the CUMULATIVE baseline, not the per-level cost.</b>
    /// <c>Values[1] = 0</c> (start of L1, no XP accumulated yet); <c>Values[L] = sum of XP(i) for
    /// i = [1, L-1]</c>. Do not confuse <c>XP(L)</c> (formula output, per-level bar width) with
    /// <c>Values[L]</c> (this array's cumulative entry) — the GDD calls this out explicitly as a
    /// common implementation error.</para>
    /// <para><b>AC-LS-31 — literal pre-computed constants, never the runtime float formula.</b>
    /// Every entry below was pre-computed in double precision (outside this codebase, then
    /// independently re-verified in double precision again inside
    /// <c>LevelingSystem_XpThresholdFormulaTable_tests.cs</c> — see that file's build-time
    /// verification test) and is hardcoded here as literal <see langword="int"/> constants. This
    /// array is intentionally never populated by calling the F-LS-1 formula at runtime/startup —
    /// AC-LS-31 explicitly states "the runtime float formula is not trusted for the authoritative
    /// values".</para>
    /// <para><b>Index 0 is unused.</b> Matches this epic's established 1-based level-indexing
    /// convention: <see cref="LevelingService"/> is constructor-injected with an
    /// <c>IReadOnlyList&lt;int&gt; xpThresholds</c> and reads <c>xpThresholds[level + 1]</c> (see
    /// <see cref="LevelingService.GetExperienceThreshold"/>); every test-local XP threshold array
    /// built elsewhere in this epic (e.g.
    /// <c>LevelingSystem_TierAutoAllocFormulaVerification_tests.cs</c>'s
    /// <c>BuildAllZeroXpThresholdsToCap</c>) follows the same "index 0 unused, real data starts at
    /// index 1" shape. This table follows it too, so it is a drop-in replacement for those
    /// test-local tables at the real production wiring site.</para>
    /// <para><b>Index 61 is the CR-5.3 sentinel</b>, <see cref="int.MaxValue"/> — guards the
    /// consecutive level-up / at-cap check (<c>Experience &gt;= xpThresholds[currentLevel + 1]</c>
    /// at Level 60) without a separate bounds-check branch. Per EC-LS-35, the sentinel is
    /// separately authored, not computed from the F-LS-1 formula.</para>
    /// <para><b>Out of scope (this story).</b> Wiring this table into
    /// <see cref="LevelingService"/>'s actual constructor call site is explicitly deferred —
    /// Stories 002/003/008/011 all supply their own test-local fake tables today and are
    /// unaffected by this file's existence. See Story 010's Out of Scope section.</para>
    /// </remarks>
    public static class XpThresholdTable
    {
        /// <summary>
        /// Cumulative XP threshold, indexed by level (1-based; index 0 unused, index 61 is the
        /// <see cref="int.MaxValue"/> sentinel). See the type-level remarks for the full
        /// derivation and the AC-LS-31 "literal constants, not runtime-computed" requirement.
        /// </summary>
        public static readonly IReadOnlyList<int> Values = new int[]
        {
            0,          // index 0 — unused.
            0,          // L1
            200,        // L2
            551,        // L3
            1062,       // L4
            1752,       // L5
            2644,       // L6
            3769,       // L7
            5161,       // L8
            6860,       // L9
            8914,       // L10
            11376,      // L11
            14309,      // L12
            17783,      // L13
            21881,      // L14
            26695,      // L15
            32333,      // L16
            38916,      // L17
            46583,      // L18
            55492,      // L19
            65824,      // L20
            77785,      // L21
            91609,      // L22
            107563,     // L23
            125950,     // L24
            147116,     // L25
            171453,     // L26
            199407,     // L27
            231484,     // L28
            268261,     // L29
            310391,     // L30
            358616,     // L31
            413778,     // L32
            476832,     // L33
            548862,     // L34
            631097,     // L35
            724930,     // L36
            831939,     // L37
            953915,     // L38
            1092884,    // L39
            1251143,    // L40
            1431293,    // L41
            1636279,    // L42
            1869435,    // L43
            2134535,    // L44
            2435849,    // L45
            2778210,    // L46
            3167085,    // L47
            3608658,    // L48
            4109924,    // L49
            4678792,    // L50
            5324204,    // L51
            6056271,    // L52
            6886419,    // L53
            7827564,    // L54
            8894302,    // L55
            10103123,   // L56
            11472658,   // L57
            13023954,   // L58
            14780784,   // L59
            16769995,   // L60
            int.MaxValue, // index 61 — CR-5.3 / EC-LS-35 sentinel, not formula-derived.
        };
    }
}
