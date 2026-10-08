# ADR-013: Server Random Provider

## Status
Proposed

## Date
2026-10-08

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Core / Scripting (plain C#; the only Unity API is `UnityEngine.Debug.Log` in the factory, as in `LootRandomFactory` today) |
| **Knowledge Risk** | LOW for this decision. The project-wide risk level is HIGH (Unity 6.x is past the LLM training cutoff), but nothing decided here uses an engine API: the contract is a C# interface over `System.Random`. |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`; `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` (searched for "random": no entry). Code read: `src/ServerLogic/LootTableSystem/LootRandomFactory.cs`, `LootDropRoller.cs`, `LootTableService.cs`, `src/ServerLogic/EnhancementSystem/EnhancementService.cs`, `src/ServerLogic/DamageCalculation/DamageCalculator.cs`, the six `System.Random` test doubles under `tests/EditMode/`. |
| **Post-Cutoff APIs Used** | None. |
| **Verification Required** | (1) `System.Random.Next()` on the project's scripting runtime returns a value in [0, `int.MaxValue`), as the .NET contract states — Decision 2 relies on it; the adapter tests of Migration Plan step 1 assert the resulting float range over a seeded run. (2) `SystemRandomProvider.NextFloat()` allocates nothing per call under IL2CPP (expected: one virtual call, one shift, one multiply). (3) `RandomNumberGenerator.Create()` returns entropy in the Linux IL2CPP Dedicated Server build with managed stripping on. It is the factory's seed source; the same call is in `LootRandomFactory` today and has only run in the Editor. Check: two server starts log different seeds. |
| **Specialist Validation** | `unity-specialist`, 2026-10-08. It read the draft and `LootRandomFactory.cs`; it did not consult the engine reference or the Unity manual, so its statements about runtime behaviour are from memory. One blocking finding, fixed in this text: the adapter's boundary tests needed a `System.Random` subclass, which Decision 6 forbids — Decision 2 now exposes the conversion as `SystemRandomProvider.ToUnitFloat`. It re-derived Decision 2's arithmetic and confirmed it. Its other points are under Risks and Verification Required. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-012 (server/client assembly boundary — the types live in `IronGrind.ServerLogic`); ADR-011 (asynchronous persistence and the tick loop — defines "tick code" and the game-logic thread, which Decision 4 restricts draws to). Both Accepted. |
| **Enables** | Every later server system that rolls: Enemy AI (CR-AI-12 Enraged spawn roll), Auto-Attack Combat and the Skill System through Damage Calculation. |
| **Blocks** | Damage Calculation Story 003 (Critical Strike) — AC-DC-F-07, F-09, F-09b, F-11, E-03 are blocked on OQ-DC-2 by the GDD. The Loot Table and Enhancement migration stories (Migration Plan steps 2 and 3). |
| **Ordering Note** | No server composition root exists yet: nothing constructs `LootTableService`, `EnhancementService` or `DamageCalculator` outside tests. Decision 4 says what that root must do when it is written. The inbound request dispatch decision that earlier reviews called "ADR-013" is now ADR-014. |

## Context

### Problem Statement
`damage-calculation.md` Step 8 rolls `r = serverRNG.NextFloat()`, uniform in [0.0, 1.0), and compares it with a float `CritChance` using a strict `<`. Its open question OQ-DC-2 is blocking: "the architectural pattern (interface design, injection point, test double contract) must be resolved before implementation begins", and the ADR "should also confirm the RNG is stateless per call (no sequence tracking) to survive zone migration safely". Five acceptance criteria cannot be written until then.

Two server systems already roll. `EnhancementService` and `LootTableService` / `LootDropRoller` take a `System.Random` in the constructor (or as a method parameter) and draw `NextDouble()` and `Next(min, max)`. Six test classes subclass `System.Random` to script it (`ScriptedRandom` four times, `CountingRandom`, `FixedGoldRandom`). `LootRandomFactory` seeds one `System.Random` from system entropy and logs the seed, as `loot-table-system.md` CR-LT-1 requires.

That precedent does not fit Damage Calculation, and the GDDs disagree with each other:

- `System.Random` has no float draw. `(float)NextDouble()` rounds: a double just under 1.0 becomes `1.0f` (outside the range the GDD specifies), and a double just under 0.75 becomes `0.75f`, which turns a crit into a non-crit under the strict `<`.
- A test can script a `System.Random` subclass only in the units the base class offers. AC-DC-F-09 needs the roll to be exactly `0.75f` and AC-DC-F-09b exactly `0.7499999f`.
- `enemy-ai.md` CR-AI-12 names an injected `IRandomProvider` whose production implementation "wraps `new System.Random()` seeded at mob spawn" (one generator per mob). `loot-table-system.md` CR-LT-1 specifies "a single process-level `System.Random` instance" with "no per-zone or per-mob re-seeding".

One contract is needed before a third system adds a third variant.

### Constraints
- All consumers are server-only and live in `IronGrind.ServerLogic` (ADR-012). The client never rolls a gameplay outcome.
- Game logic runs on one thread, the server tick loop (ADR-011). `System.Random` is not thread-safe.
- One server process per zone instance (ADR-007). A character that changes zone is served by a different process.
- Automated tests must be deterministic: "no random seeds, no time-dependent assertions" (coding standards). Unit tests set up their own state and do not depend on execution order.
- Damage Calculation guardrail: one roll per `Calculate` call and no heap allocation per call (Story 003).
- 2009 EditMode tests exist; nine places in four test files construct `DamageCalculator`, and six test doubles subclass `System.Random`.

### Requirements
- One interface that every server system uses to obtain random numbers.
- A float draw uniform in [0.0, 1.0) that includes `0.0f`, never returns `1.0f`, and involves no rounding step.
- A test double that returns exact, scripted values, counts draws, and fails the test on an unexpected draw.
- The seed of the production generator is written to the server log at startup (CR-LT-1).
- No random-number state is kept per character, per entity or per session.

## Decision

### Decision 1 — One interface: `IRandomProvider`
Every piece of server code that needs a random number takes an `IRandomProvider`. The name is the one `enemy-ai.md` CR-AI-12 already uses; that GDD calls its one-method definition a "minimum interface", and this ADR fixes the full one:

| Member | Returns |
|--------|---------|
| `float NextFloat()` | Uniform in [0.0f, 1.0f). `0.0f` is a possible result; `1.0f` is not. |
| `double NextDouble()` | Uniform in [0.0, 1.0). |
| `int NextInt(int minInclusive, int maxExclusive)` | Uniform in [`minInclusive`, `maxExclusive`). Returns `minInclusive` when the two are equal. Throws `ArgumentOutOfRangeException` when `minInclusive > maxExclusive`. |

The interface and its production implementation are in the namespace `IronGrind.Randomness`, folder `src/ServerLogic/Randomness/`, assembly `IronGrind.ServerLogic`. The namespace is added to `ServerOnlyNamespaces` in `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`, so the boundary test fails if one of these types appears in `Foundation` or `Client`.

A member is added to the interface only when a GDD rule needs a draw the three above cannot express without bias or rounding. Convenience members (pick from a list, shuffle, weighted choice) are written as static helpers that take an `IRandomProvider`, so test doubles stay three methods long.

### Decision 2 — How each value is produced
The production implementation is `SystemRandomProvider`, a sealed class that wraps one `System.Random` handed to its constructor.

- **`NextFloat()`** takes 24 bits from one integer draw and scales them: `ToUnitFloat(_random.Next())`. The public static `SystemRandomProvider.ToUnitFloat(int sample)` returns `(sample >> 7) * (1f / 16777216f)` and throws `ArgumentOutOfRangeException` for a negative `sample`. It is a separate pure function so that its boundaries can be tested without a `System.Random` subclass. `Next()` returns a value in [0, 2³¹ − 1), so the shifted value is an integer in [0, 2²⁴ − 1]. Every integer below 2²⁴ and every such integer times 2⁻²⁴ is exactly representable as a `float`, so there is no rounding. The largest result is `0.99999994f`. One call to the underlying generator per draw.
  - The largest shifted value has 127 source integers where every other value has 128. The resulting bias is one part in 2³¹ and is accepted.
  - With this construction the probability that `NextFloat() < c` is `ceil(c × 2²⁴) / 2²⁴` (leaving aside the 2⁻³¹ bias above): never below `c`, above it by less than 2⁻²⁴, and exactly `c` for any `c` that is a multiple of 2⁻²⁴ (0.75, 0.5, 0.25 and so on).
- **`NextDouble()`** returns `_random.NextDouble()` unchanged.
- **`NextInt(min, max)`** returns `_random.Next(min, max)` unchanged.

Rule for callers: **the result of `NextDouble()` is never cast to `float`.** A caller whose threshold is a `float` either draws `NextFloat()`, or widens the threshold to `double` and draws `NextDouble()` (what `LootDropRoller` does today with `DropChance`). Both are exact. Damage Calculation uses `NextFloat()` because its GDD names it and its acceptance criteria state the roll as a float.

### Decision 3 — Injection point
- A class that rolls takes `IRandomProvider` as a constructor parameter and stores it in a `private readonly` field. A null argument throws `ArgumentNullException`, like every other injected collaborator in the project.
- A static method that rolls takes it as a parameter (`LootDropRoller.Roll(table, random)`).
- It is not a parameter of a public gameplay method on an instance: `DamageCalculator.Calculate(baseDamage, attackerId, targetId, context)` keeps the signature the GDD specifies.
- Not allowed in `IronGrind.ServerLogic`:
  - `UnityEngine.Random` in any form (global state, not injectable).
  - A static or ambient accessor for the provider (`RandomProvider.Current`, a singleton, a service locator).
  - `new System.Random(...)` anywhere except `RandomProviderFactory` (Decision 4).
  - `System.Random` as the type of a field, constructor parameter or method parameter, except inside `SystemRandomProvider` and `RandomProviderFactory`. This holds from the end of Migration Plan step 3; until then `EnhancementService`, `LootTableService` and `LootDropRoller` are the listed exceptions.
- **Out of scope: values that must be unpredictable to an attacker** — session tokens, authentication nonces, anything in `networking-session-token.md` or `authentication.md`. Those use `System.Security.Cryptography.RandomNumberGenerator` and must not use `IRandomProvider`, which is a seeded, reproducible generator.

### Decision 4 — One generator per zone process
- The server composition root creates exactly one `IRandomProvider` at startup by calling `RandomProviderFactory.CreateSeededFromEntropy(out int seed)` and passes that same instance to every system it constructs. `RandomProviderFactory` replaces `LootRandomFactory`: it draws a 32-bit seed from `RandomNumberGenerator.Create()`, logs `[Random] PRNG seed: {seed}` once with `UnityEngine.Debug.Log` (as `LootRandomFactory` does), and returns `new SystemRandomProvider(new System.Random(seed))`.
- The provider is drawn from the game-logic thread only (tick code, in ADR-011's terms). It has no lock. A persistence continuation, a thread-pool callback or a background job must not draw; if such code needs an outcome, the tick draws it first and passes the value in.
- No system creates its own generator, re-seeds, or derives a child generator from the process one. This overrides the sentence in `enemy-ai.md` CR-AI-12 that seeds a `System.Random` at each mob spawn; the GDD is reworded in Migration Plan step 4.
- What the logged seed is for: an audit record that the generator was seeded from entropy, and a starting point for reproducing a short sequence on the same build. It is not a replay mechanism. Reproducing one roll needs every earlier draw from every system in the same order, and .NET does not guarantee the `System.Random` algorithm across runtime versions.

### Decision 5 — No random state outside the generator (answer to OQ-DC-2's "stateless per call")
The generator necessarily has internal state. What OQ-DC-2 asks for is read here as: **no caller keeps or depends on a sequence.**

- Each roll is one independent draw. No system stores a previous draw, a draw counter, a streak or a "pity" value that changes later odds. (`enhancement-system.md` Player Fantasy says the same from the design side: "no hidden pity mechanics".)
- No random-number state is stored on an entity, on a character, in a session, in the character record, or in a zone-transfer handoff. Nothing about randomness is persisted.
- Consequence for zone migration: a character that moves to another zone instance is served by that process's generator. There is no stream to carry, reset or corrupt.
- Each consumer's GDD states how many draws one operation makes, and its tests assert that count: exactly one per `DamageCalculator.Calculate` (including when `CritChance` is 0.0), none on a rejected call; one per drop-table entry and one for gold in Loot Table; one per Enhancement attempt and none on a rejected attempt. Draw counts are part of each system's contract because they are how an unintended extra roll is caught.

### Decision 6 — Test doubles
Tests implement `IRandomProvider`; they do not subclass `System.Random`. Two shared doubles live in `tests/EditMode/Randomness/`:

- **`ScriptedRandomProvider`** — three queues, filled with `EnqueueFloat`, `EnqueueDouble` and `EnqueueInt`. Each `Next…` call dequeues from its own queue and returns the value unchanged. A draw from an empty queue throws `InvalidOperationException`, so an unexpected draw fails the test. `DrawCount` is the total number of draws; `FloatDrawCount`, `DoubleDrawCount` and `IntDrawCount` count per method. It returns whatever was queued, including values a real provider never produces (AC-AI-13 scripts `1.0`).
- **`RecordingRandomProvider`** — wraps another `IRandomProvider`, forwards every call, and keeps the last value returned by each method plus the same counters. Used when a test needs real pseudo-random values and must see each one (AC-DC-E-05).

Rules for tests:
- A test that needs a pseudo-random sequence builds `new SystemRandomProvider(new System.Random(SEED))` with `SEED` as a named constant. A test never calls `RandomProviderFactory.CreateSeededFromEntropy`, except the one test of the factory itself, which asserts the log line and nothing about the values.
- "Guaranteed crit" and similar setups script the roll (`EnqueueFloat(0.0f)`); they do not rely on a stat of 1.0, because Character Stats clamps `CritChance` to 0.75.
- A test that does not care about the roll still supplies a double. For paths that must not draw, an empty `ScriptedRandomProvider` is the assertion.

### Architecture Diagram

```
 server composition root (ServerLogic, not yet written)
   │  RandomProviderFactory.CreateSeededFromEntropy(out seed)
   │     ├─ seed ← System.Security.Cryptography.RandomNumberGenerator
   │     ├─ log  "[Random] PRNG seed: {seed}"
   │     └─ new SystemRandomProvider(new System.Random(seed))
   │
   ▼  one IRandomProvider instance per zone process, tick thread only
   ├──► DamageCalculator      NextFloat()  < CritChance        (1 per Calculate)
   ├──► LootDropRoller        NextDouble() < (double)DropChance (1 per entry)
   ├──► LootTableService      NextInt(GoldMin, GoldMax + 1)     (1 per kill)
   ├──► EnhancementService    NextDouble() → ResolveOutcome     (1 per attempt)
   └──► MobController (later) NextDouble() < EnragedChance      (1 per spawn)

 tests:  ScriptedRandomProvider ──► same constructors, exact values, draw counts
         RecordingRandomProvider(SystemRandomProvider(new System.Random(SEED)))
```

### Key Interfaces

```csharp
namespace IronGrind.Randomness
{
    /// <summary>Source of gameplay random numbers on the server (ADR-013).</summary>
    public interface IRandomProvider
    {
        /// <summary>Uniform in [0.0f, 1.0f); never 1.0f.</summary>
        float NextFloat();

        /// <summary>Uniform in [0.0, 1.0).</summary>
        double NextDouble();

        /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
        /// <exception cref="System.ArgumentOutOfRangeException">minInclusive is greater than maxExclusive.</exception>
        int NextInt(int minInclusive, int maxExclusive);
    }

    public sealed class SystemRandomProvider : IRandomProvider
    {
        public SystemRandomProvider(System.Random random);   // null → ArgumentNullException

        /// <summary>Maps a sample in [0, int.MaxValue) to a float in [0.0f, 1.0f) using its top 24 bits.</summary>
        public static float ToUnitFloat(int sample);         // negative → ArgumentOutOfRangeException
    }

    public static class RandomProviderFactory
    {
        public static IRandomProvider CreateSeededFromEntropy(out int seed);
    }
}
```

Constructor change for Damage Calculation (Story 003): `DamageCalculator(stats, weapons, items, bonuses, config, random)`.

## Alternatives Considered

### Alternative 1: Keep injecting `System.Random`
- **Description**: `DamageCalculator` takes a `System.Random` like the two existing systems; a static helper turns one draw into a float.
- **Pros**: No new type. No migration of Enhancement or Loot Table. Matches CR-LT-1's wording.
- **Cons**: A test scripts the float only indirectly, by overriding `Next()` with the integer that the helper turns into `0.75f`. Subclassing a concrete framework class stays the test-double pattern, and each double silently inherits real random behaviour for the methods it does not override (a `ScriptedRandom` that overrides only `NextDouble()` returns real random integers from `Next(min, max)`). Six near-identical doubles already exist. `enemy-ai.md` already asks for an interface.
- **Rejection Reason**: The float contract and the exact-value tests are the reason this ADR exists, and this alternative makes both indirect.

### Alternative 2: Static ambient provider (`ServerRandom.Current`)
- **Description**: A static property set once at startup; code calls it wherever it needs a number.
- **Pros**: No constructor parameters; nothing to thread through a composition root.
- **Cons**: Hidden dependency. Tests must set and restore global state, which breaks test isolation and makes results depend on execution order. Coding standards require dependency injection over singletons.
- **Rejection Reason**: Contradicts the coding standards and the test rules.

### Alternative 3: Pass the roll in as a value
- **Description**: `Calculate(baseDamage, attackerId, targetId, context, float critRoll)`; the caller draws.
- **Pros**: The calculator becomes a pure function. Nothing to inject.
- **Cons**: Changes the signature the GDD specifies. Moves "one roll per call" to every caller (Auto-Attack Combat, each skill), where a caller can reuse or forget a roll. Guard paths would still consume a draw.
- **Rejection Reason**: It moves the rule to the places least able to enforce it.

### Alternative 4: One generator per system or per entity
- **Description**: Each system (or each mob, as `enemy-ai.md` CR-AI-12 is written) owns a generator with its own seed.
- **Pros**: One system's sequence does not depend on another system's draws.
- **Cons**: Several seeds to log, or none (per mob: an allocation and a seeding per spawn for a single draw). Per-entity state is exactly the "sequence tracking" OQ-DC-2 rules out. Contradicts CR-LT-1.
- **Rejection Reason**: User decision 2026-10-08: one generator per process; CR-AI-12 is reworded.

### Alternative 5: `float` by cast, or compare as `double`
- **Description**: `(float)NextDouble()` with a redraw on `1.0f`; or draw a double and compare it with `CritChance` widened to double.
- **Pros**: The second is exact and needs no bit arithmetic.
- **Cons**: The cast can still round a value up to `0.75f`, and a redraw breaks "exactly one draw per call". Comparing as double makes the roll a double, while the GDD names `NextFloat()` and states AC-DC-F-09b with a float literal.
- **Rejection Reason**: User decision 2026-10-08: 24 random bits. Comparing as double remains allowed for callers whose GDD states a double roll (Decision 2).

## Consequences

### Positive
- OQ-DC-2 is answered: interface, injection point, test-double contract and statelessness are all stated. Damage Calculation Story 003 can be made Ready on acceptance.
- Tests state the roll they mean (`EnqueueFloat(0.75f)`) and fail on a draw they did not expect.
- One contract for Damage Calculation, Loot Table, Enhancement and Enemy AI; the disagreement between CR-LT-1 and CR-AI-12 is settled.
- Six duplicated test doubles become two shared ones.
- `IRandomProvider` is server-only by assembly, so no client code can roll a gameplay outcome with it.

### Negative
- `DamageCalculator`'s constructor gains a parameter; nine construction sites in four test files change in Story 003.
- Two migration stories for code that works today (Loot Table, Enhancement), touching five test files and replacing `LootRandomFactory`. The log line changes from `[LootTable] PRNG seed:` to `[Random] PRNG seed:`; `LootTable_DropRoll_tests.cs` asserts the old text.
- Three GDDs need a wording pass (Migration Plan step 4).
- Until step 3 is done, two contracts coexist in `ServerLogic`.
- One interface call per draw instead of a direct call. Not measurable at the draw rates involved.

### Risks
- **A draw from the wrong thread.** `System.Random` corrupts its state silently under concurrent use (it can start returning 0 forever). Mitigation: Decision 4's rule; ADR-011 already keeps persistence continuations out of gameplay state. A debug-only thread check in `SystemRandomProvider` is an option for the story that writes the composition root, not required here.
- **Shared stream couples systems in tests.** A test that seeds one generator and passes it to two systems gets a sequence that depends on both. Mitigation: seeded tests are for statistical checks of one system (AC-DC-E-05); exact outcomes use `ScriptedRandomProvider`.
- **The float has 24 bits of resolution.** A probability smaller than 2⁻²⁴ (about 6 × 10⁻⁸) cannot be expressed with `NextFloat()`. No GDD has one; a future very rare drop uses `NextDouble()`.
- **`System.Random`'s algorithm differs between runtimes.** A seeded test that asserts an exact sequence would break on a runtime change. Mitigation: seeded tests assert properties (range, rate within a band, "no roll at or above the threshold is a crit"), never specific values.
- **The generator is predictable in principle (accepted for MVP, user decision 2026-10-08).** `System.Random` uses the absolute value of its seed, so it has about 2³¹ starting states. A client that observes enough outcomes could search that space, recover the seed and predict later rolls — the costly case is an Enhancement attempt, where a failure destroys the item. What stands in the way: a client sees one bit per roll (crit or not, success or not), and its draws are interleaved with every other player's and mob's draws in an order it cannot observe. How hard that makes the attack has **not** been analysed. Mitigation: every consumer depends on `IRandomProvider`, so the production implementation can be replaced by one backed by `System.Security.Cryptography.RandomNumberGenerator` in a single class, with no change to any consumer or test. That replacement would remove the logged seed, so it needs a change to `loot-table-system.md` CR-LT-1. **Gate: a `security-engineer` verdict on this risk (`/security-audit`) is required before any public release or any build in which Enhancement outcomes have real value to players.**
- **The seed source in a stripped server build.** `RandomNumberGenerator.Create()` has only run in the Editor so far (Verification Required 3).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| damage-calculation.md | OQ-DC-2 (blocking): interface design, injection point, test double contract; RNG "stateless per call (no sequence tracking)" for zone migration | Decisions 1, 3, 6 and 5. |
| damage-calculation.md | Step 8 / F-DC-3: `r = serverRNG.NextFloat()`, uniform in [0.0, 1.0); `IsCrit = r < CritChance`; one RNG roll per call | `IRandomProvider.NextFloat()` (Decisions 1 and 2); the one-draw rule and its test (Decision 5). |
| damage-calculation.md | Edge case: `NextFloat()` returning exactly `0.0` must be possible; range [0.0, 1.0) | Decision 2: 0 is a possible 24-bit value; the maximum is `0.99999994f`. |
| damage-calculation.md | AC-DC-F-07, F-09, F-09b, F-11, E-03 (blocked on OQ-DC-2), E-05, F-14, F-14c, I-04 need a mocked or seeded RNG | `ScriptedRandomProvider` returns exact floats; `RecordingRandomProvider` over a seeded provider for E-05 (Decision 6). |
| loot-table-system.md | CR-LT-1: a single process-level generator seeded from entropy at startup, seed written to the server log, no per-zone or per-mob re-seeding, tests inject a seeded instance | Decision 4. The type named in the GDD changes from `System.Random` to `IRandomProvider` (Migration Plan step 4). |
| enhancement-system.md | CR-ENH outcome roll: one uniform value `r ∈ [0, 1)`; no roll on a rejected attempt; no hidden pity mechanics | `NextDouble()` (Decision 1); draw counts and no carried state (Decision 5). |
| enemy-ai.md | CR-AI-12 / AC-AI-13: injected `IRandomProvider` with `NextDouble()`, not `UnityEngine.Random`, not an inline `System.Random`; test doubles return controlled values | Same name and method (Decision 1); `ScriptedRandomProvider` (Decision 6). The per-mob seeding sentence is overridden by Decision 4 and reworded in Migration Plan step 4. |
| coding-standards.md | Tests are deterministic; dependency injection over singletons | Decisions 3 and 6. |

Not checked for this ADR: `auto-attack-combat.md`, `hit-detection.md`, `class-system.md` and `mob-spawning.md` were searched for "random" / "RNG" only; none names a generator type or a seeding rule. They are expected to roll through Damage Calculation or this interface.

## Performance Implications
- **CPU**: One interface call plus a shift and a multiply per float draw. A zone with 50 players and 150 mobs at 20 Hz makes at most a few hundred draws per second.
- **Memory**: One `SystemRandomProvider` and one `System.Random` per process. No allocation per draw.
- **Load Time**: One entropy read and one log line at startup.
- **Network**: None.

## Migration Plan
Nothing is renamed or moved on acceptance. Each step is a story with the full EditMode suite as its gate.

1. **Damage Calculation Story 003 (Critical Strike).** Creates `src/ServerLogic/Randomness/` with `IRandomProvider` and `SystemRandomProvider`; creates `tests/EditMode/Randomness/` with `ScriptedRandomProvider`, `RecordingRandomProvider` and the adapter tests (range over a seeded run; `ToUnitFloat(0)` and `ToUnitFloat(127)` give `0.0f`, `ToUnitFloat(128)` gives 2⁻²⁴, `ToUnitFloat(int.MaxValue − 1)` gives `0.99999994f`, a negative sample throws; the other two methods return what a second `System.Random` with the same seed returns; null argument). Adds `IronGrind.Randomness` to `ServerOnlyNamespaces`. Adds the constructor parameter to `DamageCalculator` and updates the nine construction sites. The story's Implementation Notes and QA cases are completed from this ADR before `/story-readiness`.
2. **Loot Table migration story (new).** `LootTableService` and `LootDropRoller` take `IRandomProvider` (`Next(min, max)` becomes `NextInt`; the story checks that table validation keeps `GoldMax` below `int.MaxValue`, because the call passes `GoldMax + 1`). `LootRandomFactory` is replaced by `RandomProviderFactory` in `IronGrind.Randomness`, with the new log line; the factory test follows. The `System.Random` doubles in the four Loot Table test files are replaced by the shared ones.
3. **Enhancement migration story (new).** `EnhancementService` takes `IRandomProvider`; `ScriptedRandom` in `EnhancementTestDoubles.cs` is deleted in favour of `ScriptedRandomProvider`. After this step no `System.Random` type appears in `ServerLogic` outside `IronGrind.Randomness`, and the exception in Decision 3 ends.
4. **GDD wording pass (after acceptance; wording only, no rule a player can observe changes).**
   - `damage-calculation.md`: OQ-DC-2 marked resolved by ADR-013; the Acceptance Criteria prerequisite note and the `(BLOCKED:OQ-DC-2)` tag on AC-DC-F-09b updated; `serverRNG` identified as the injected `IRandomProvider`.
   - `loot-table-system.md` CR-LT-1: "`System.Random` instance … (`new System.Random()` with default seeding)" becomes the process-level `IRandomProvider`, seeded explicitly so the seed can be logged (what the code already does).
   - `enemy-ai.md` CR-AI-12: "Production implementation wraps `new System.Random()` seeded at mob spawn" becomes "the process-level `IRandomProvider` is injected (ADR-013)"; the interface block points to the ADR for the full definition.
   - Triad and propagation check as for any GDD edit.
5. **Control manifest and registry.** `/create-control-manifest update` after acceptance; registry entries as approved by the user.
6. **Renumbering.** The inbound request dispatch decision is ADR-014. `ADR-012` (three mentions) and `architecture-traceability.md` (one) are updated when this ADR is written. The dated review reports keep their text.

## Validation Criteria
- Damage Calculation Story 003 passes all its criteria, including "exactly one draw per `Calculate`" and "no draw on a guard path", using `ScriptedRandomProvider`.
- The adapter tests pass: no `NextFloat()` result is outside [0.0f, 1.0f) in a seeded run of at least 1,000,000 draws, and `ToUnitFloat` gives `0.0f` for 0 and `0.99999994f` for `int.MaxValue − 1`.
- After Migration Plan step 3: a search of `src/ServerLogic/` finds `System.Random` only under `Randomness/`, and `UnityEngine.Random` nowhere; no class under `tests/` derives from `System.Random`.
- The boundary test (ADR-012 Decision 6, check 1) passes with `IronGrind.Randomness` in `ServerOnlyNamespaces`.

## Related Decisions
- ADR-012 (server/client assembly boundary) — home assembly; lists this ADR under Enables.
- ADR-011 (asynchronous persistence and the tick loop) — tick code and the game-logic thread.
- ADR-007 (hosting backend) — one process per zone instance.
- `design/gdd/damage-calculation.md` OQ-DC-2, Step 8; `design/gdd/loot-table-system.md` CR-LT-1; `design/gdd/enhancement-system.md` outcome roll; `design/gdd/enemy-ai.md` CR-AI-12.
- ADR-014 (inbound request dispatch and tick order, not yet written).
