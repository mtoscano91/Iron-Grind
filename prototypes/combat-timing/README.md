# Prototype: Combat Timing

**Question:** Does cancelling auto-attack damage when a skill fires before the beat
feel rhythmically satisfying on touch input?

**Date:** 2026-04-19

---

## The Mechanic Being Tested

- Auto-attacks fire every **1.0 second** on a fixed cadence (never interrupted)
- If you tap a skill **before** the next auto-attack fires, the auto damage is **cancelled** for that cycle
- If you tap a skill **after** the auto fires, you get both: auto damage + skill damage
- The gold ring on screen charges clockwise → when it completes, the auto fires

**Good timing:** Let the ring complete → auto fires → immediately tap a skill
**Bad timing:** Tap a skill while the ring is still charging → auto cancelled

---

## Setup (Unity 6.3 LTS)

1. Create a new **Unity 6.3 LTS** project using the **3D (URP)** template
2. Open the default `SampleScene` (or any empty scene)
3. Copy `CombatTimingPrototype.cs` into your `Assets/` folder
4. In the Hierarchy: **right-click → Create Empty**, rename it `PrototypeManager`
5. Select `PrototypeManager` → **Add Component → Combat Timing Prototype**
6. Press **Play**

The script creates the enemy, UI, and skill buttons entirely in code — no prefabs or scene setup needed.

---

## Controls

- **Keyboard (editor):** press **1**, **2**, or **3** to cast the corresponding skill — most reliable in editor
- **Mouse click:** click the skill buttons (may not work if project uses New Input System)
- **Touch (device):** tap the skill buttons

The three skill buttons are at the bottom of the screen:
- **Slash** — 75 damage, 3s cooldown
- **Crush** — 120 damage, 6s cooldown
- **Smash** — 200 damage, 10s cooldown

---

## What To Observe and Report

Play for 5–10 minutes, then note your answers to these questions:

1. **Does the cadence feel right at 1.0s?**
   Try changing `AUTO_CADENCE` at the top of the script to `0.8f` and `1.2f`.
   Which speed feels most natural on touch?

2. **Is the ring readable?**
   Can you reliably tell *when* the auto is about to fire just by watching the ring?
   Does it need to be larger, brighter, or accompanied by a sound cue?

3. **Does cancellation feel punishing or educational?**
   When you accidentally cancel an auto, does the "CANCELLED" feedback make you
   want to wait next time, or does it feel unfair?

4. **Does "good timing" feel rewarding?**
   When you land auto + skill in sequence, does it feel satisfying or just correct?

5. **What's the natural play pattern that emerges?**
   Do you find yourself watching the ring and timing carefully, or just tapping
   skill buttons as fast as possible?

---

## Tuning Constants (top of `CombatTimingPrototype.cs`)

```csharp
const float AUTO_CADENCE = 1.0f;  // try 0.8 / 1.0 / 1.2
const float AUTO_DAMAGE  = 30f;   // relative to skill damage — affects cost of cancelling

static readonly (string label, float dmg, float cd)[] SKILLS =
{
    ("Slash\n75 dmg | 3s",   75f,  3f),   // adjust damage and cooldowns freely
    ("Crush\n120 dmg | 6s", 120f,  6f),
    ("Smash\n200 dmg | 10s", 200f, 10f),
};
```

---

## Stats Overlay (top-left of screen)

- **Autos Landed** — auto-attacks that fired normally
- **Autos Cancelled** — autos lost because a skill fired first
- **Skills Used** — total skill activations

A high cancellation rate means players are tapping too early. A low rate means
timing is well-calibrated or players are waiting. Report the ratio after your session.
