---
name: project-movement-input-decisions
description: Confirmed and recommended decisions for the Movement System touch input scheme — joystick, camera drag, auto-run placement, and release behavior
metadata:
  type: project
---

Movement System input scheme analysis conducted 2026-05-25. Recommendations given; user has not yet confirmed final decisions.

**Confirmed design decisions (user-locked):**
- Left half: floating virtual joystick zone (spawns at first touch point)
- Right half: camera rotation drag zone
- Auto-run toggle: TBD placement (analysis recommends bottom-left, left-thumb reachable)
- Skill buttons + auto-attack toggle: right side of screen
- No walk state — character always runs or stands
- Joystick direction = character runs relative to camera heading

**Recommended decisions (pending user confirmation):**
- Joystick spawn trigger: drag threshold (8–12px), NOT touch contact alone — prevents accidental activation during thumb resting
- Joystick center dead zone: 6–10% of joystick radius
- Joystick release: instant stop on input state; animation blend-out is a separate visual concern
- Auto-run placement: bottom-left corner, outside joystick spawn zone, left-thumb reachable
- Auto-run + camera drag: recommend camera-relative steering during auto-run (needs game-designer sign-off)
- Skill button layout: 2x3 grid in lower-right thumb arc (not vertical strip)
- Auto-attack toggle: far-right edge, outside primary camera drag zone
- Joystick analog sensitivity: variable speed by extension distance; full run at full extension

**Open design question flagged to game-designer:**
Does auto-run steer with camera drag (camera-relative), or lock to a world direction at activation? This affects both movement rules and input ergonomics.

**Why:** [[project-iron-grind-context]] — 2-hour grind sessions, Rhythm Mastery pillar requires precise stop behavior, Korean MMO conventions favor instant stop.

**How to apply:** When writing or reviewing movement-system UX specs, reference these decisions to avoid re-litigating settled ground. Flag open game-designer question before finalizing the Detailed Design section.
