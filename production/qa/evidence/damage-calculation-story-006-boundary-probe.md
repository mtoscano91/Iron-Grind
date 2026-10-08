# Evidence — Damage Calculation Story 006: boundary test probe

> **Date:** 2026-10-08
> **Story:** `production/epics/damage-calculation/story-006-server-assembly-isolation.md`
> **Criterion:** "Boundary test detects a violation" (ADR-012 Validation Criteria, first bullet)
> **How run:** Unity 6000.3.10f1, batch mode, EditMode, Editor closed. Results XML read from the session scratchpad (not kept in the repository).

## 1. Suite after the move, before the probe

Full EditMode suite: **2002 total, 2002 passed, 0 failed** (1995 before the story + 7 boundary tests). No `error CS` and no `warning CS` line in the Editor log.

## 2. Probe

A throwaway file `src/Foundation/_BoundaryProbe.cs` was added:

```csharp
namespace IronGrind.DamageCalculation
{
    public sealed class _BoundaryProbe
    {
    }
}
```

Run: `-testFilter IronGrind.Tests.EditMode.Architecture` — **7 total, 5 passed, 2 failed**.

`test_every_foundation_type_is_listed` — Failed:

```
Types in IronGrind.Foundation that are on neither list (ADR-012 Decision 3: move them to ServerLogic/Client or add a shared entry with its client consumer):
IronGrind.DamageCalculation._BoundaryProbe
  Expected: <empty>
  But was:  < "IronGrind.DamageCalculation._BoundaryProbe" >
```

`test_damage_calculation_is_server_only` — Failed:

```
IronGrind.DamageCalculation types found in IronGrind.Foundation:
IronGrind.DamageCalculation._BoundaryProbe
  Expected: <empty>
  But was:  < "IronGrind.DamageCalculation._BoundaryProbe" >
```

The other five boundary tests passed, as expected: the probe changes no asmdef, is on no list, and puts no `ServerLogic` type in a signature.

## 3. After removing the probe

`_BoundaryProbe.cs` and its `.meta` were deleted. Full EditMode suite: **2002 total, 2002 passed, 0 failed**.

## Not covered here

- The two `Tools/HUD` menu commands of `Assets/Editor/HudBootstrap.cs` were not executed. The script compiles in its new assembly `IronGrind.HudEditorTools`; running the commands needs an interactive Editor.
- No player build was made. The scan of a real client build is Story 007.
