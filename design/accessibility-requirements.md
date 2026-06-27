# Accessibility Requirements: Iron Grind

> **Status**: Committed
> **Author**: ux-designer / producer
> **Last Updated**: 2026-06-27
> **Accessibility Tier Target**: Standard
> **Platform(s)**: iOS (primary), Android (future)
> **External Standards Targeted**:
> - Apple Human Interface Guidelines — Accessibility
> - Google Material Design — Accessibility
> - Game Accessibility Guidelines (gameaccessibilityguidelines.com)
> - WCAG 2.1 Level A (informational target; WCAG is web-origin but widely applied)
> **Accessibility Consultant**: None engaged (MVP scope)
> **Linked Documents**: `design/gdd/systems-index.md`, `design/ux/hud.md`, `design/ux/interaction-patterns.md`

---

## Accessibility Tier Definition

| Tier | Core Commitment |
|------|----------------|
| **Basic** | Critical text readable at standard resolution. No color-only gameplay signals. Independent volume controls. Completable without photosensitivity risk. |
| **Standard** | All of Basic, plus: touch target sizing to platform minimums, text size adjustment, at least one colorblind mode, hold-to-toggle alternatives for sustained inputs, motion reduction option. |
| **Comprehensive** | All of Standard, plus: screen reader support for menus, mono audio, full subtitle customization, HUD repositioning, reduced motion mode for all VFX. |
| **Exemplary** | All of Comprehensive, plus: WCAG 2.1 AA certification, third-party audit, cognitive assist modes, haptic alternatives for all audio cues. |

### This Project's Commitment

**Target Tier**: Standard

**Rationale**: Iron Grind is a mobile MMORPG with touch-only landscape controls targeting
iOS first. The primary accessibility barriers are visual (color-coded item rarity, small
stat text on mobile screens) and motor (tap precision on small targets, sustained auto-attack
rhythm requiring timed inputs). Standard tier addresses both directly. The Rhythm Mastery
pillar — timed skill activation — is a potential timing barrier for players with motor
impairments; hold-to-toggle and extended timing windows are required to keep this pillar
accessible. Comprehensive and Exemplary tiers require screen reader integration at the
game-world level, which is beyond MVP scope given Unity 6.3's UI Toolkit accessibility
primitives are still maturing.

**Features explicitly in scope (beyond tier baseline)**:
- Minimum 44×44pt touch targets on all interactive elements (Apple HIG requirement — mandatory for iOS)
- Safe area inset compliance on all screens (notched iPhone X and later)
- Chat panel text size independent of HUD text size (chat is reading-heavy; HUD is glanceable)

**Features explicitly out of scope (MVP)**:
- VoiceOver / TalkBack support for in-game world (menus only, deferred post-launch)
- Full subtitle customization (Iron Grind is not voiced at MVP; irrelevant until audio system is authored)
- Mono audio (deferred — audio system GDD not yet authored)

---

## Visual Accessibility

| Feature | Target Tier | Scope | Status | Implementation Notes |
|---------|-------------|-------|--------|---------------------|
| Minimum text size — HUD | Standard | Zone A–E HUD elements | Not Started | 14pt minimum for critical values (HP number, skill countdown). Non-critical labels (stat names) may be 12pt. Reference: Apple HIG recommends 17pt for body; 14pt is the floor for non-body. |
| Minimum text size — chat panel | Standard | Chat panel | Not Started | 14pt default, adjustable 12–20pt. Chat is reading-heavy; default should lean larger than HUD. |
| Minimum text size — menus / inventory | Standard | All menu screens | Not Started | 16pt minimum for menu body text. Item name = 16pt; item stat values = 14pt. |
| Text contrast — UI text | Standard | All UI text | Not Started | Minimum 4.5:1 for body text (WCAG AA). 3:1 for large text (18pt+ or 14pt bold). Validate with contrast analyzer on final color palette. |
| Colorblind mode — Protanopia/Deuteranopia | Standard | All color-coded gameplay | Not Started | Item rarity borders, health bars, enemy/ally indicators on minimap. Shift red signals to orange; shift green signals to teal. One combined palette covers both. Verify with Coblis simulator. |
| Colorblind mode — Tritanopia | Standard | All color-coded gameplay | Not Started | Rarer; blue→purple, yellow→orange. Include as a third mode. |
| Color-as-only-indicator audit | Basic | All UI and gameplay | Not Started | See audit table below. Every color signal needs a non-color backup before ship. |
| Text size adjustment | Standard | Chat panel; menu text | Not Started | Range: Small (12pt) / Default (16pt) / Large (20pt) for chat. Menu text: 100% / 125% / 150% scale. HUD text is fixed (layout-constrained) but default must pass 14pt floor. |
| Screen flash / strobe | Basic | All VFX, cutscenes | Not Started | (1) Pre-launch photosensitivity warning. (2) Audit all flash-heavy VFX against Harding FPA standard. (3) Optional flash reduction toggle (reduce amplitude 80%). Priority systems: skill activation VFX, death/respawn screen flash. |
| Motion reduction | Standard | UI transitions, camera effects | Not Started | Toggle in accessibility settings. Reduce: screen shake on taking damage, UI slide transitions (use fade instead), looping background animations in menus. Cannot eliminate: character movement animations (breaks gameplay readability). |
| Brightness control | Basic | Global | Not Started | Exposed in settings. Range: −30% to +30%. Include calibration reference (barely-visible grey square). |

### Color-as-Only-Indicator Audit

| Location | Color Signal | What It Communicates | Non-Color Backup | Status |
|----------|-------------|---------------------|-----------------|--------|
| Item rarity border | Grey/Blue/Purple/Gold | Item quality tier | Rarity name label always shown in item tooltip; future: star count icon | Not Started |
| HP bar | Green → Red gradient | Player health level | Numeric HP value displayed; bar flashes on critical (<20%) | Not Started |
| Enemy HP bar | Same | Enemy health level | Numeric or % value on focus | Not Started |
| Minimap — ally vs enemy markers | Green vs Red | Unit allegiance | Enemy markers = triangle; ally markers = circle; self = pulsing dot | Not Started |
| Status effect icons | Color-coded by type | Effect category (buff/debuff/neutral) | Icon shape differs by category; text label on long-press | Not Started |
| Enhancement result feedback | Green (success) / Red (fail) | Enhancement outcome | Outcome text label + animation (burst vs. crack) | Not Started |

---

## Motor Accessibility

Iron Grind is touch-only, landscape orientation. Controller remapping is N/A.
Touch accessibility focus: target sizing, hold-to-toggle, and timing windows.

| Feature | Target Tier | Scope | Status | Implementation Notes |
|---------|-------------|-------|--------|---------------------|
| Touch target minimum size | Standard | All interactive elements | Not Started | 44×44pt minimum per Apple HIG. Applies to: skill bar slots, HUD buttons, menu list rows, chat input, inventory item cells. If a visual element is smaller than 44pt, expand the tap target invisibly around it. |
| Touch target spacing | Standard | All interactive elements | Not Started | Minimum 8pt gap between adjacent tap targets to prevent mis-taps. Skill bar slots are 56dp wide with 4dp gap — verify gap meets 8pt floor at smallest iPhone viewport. |
| Hold-to-toggle for skill bar expand | Standard | Skill bar expand (SE2 expanded state) | Not Started | Expand is currently a tap toggle. No hold input at MVP — this row is a reminder to audit if any hold input is added. |
| Timing window adjustment | Standard | Rhythm Mastery pillar — skill timing | Not Started | Provide a timing window multiplier in accessibility settings: 1× (default) / 1.5× / 2×. At 2×, a 500ms input window becomes 1000ms. Applies to skill activation timing windows. Does NOT alter server tick rate — extends client-side acceptance window only. |
| Auto-attack toggle (no sustained hold) | Basic | Auto-attack activation | Not Started | Auto-attack is a toggle (on/off), not a sustained hold. This is already accessible by design. Confirm in HUD GDD amendment for Zone D toggle button. |
| Safe area compliance | Standard | All screens | Not Started | All interactive elements must be within safe area insets on iPhone X+ (notch, home indicator). Use `RuntimePanelUtils.ScreenToPanel` + `Screen.safeArea` as specified in ADR-005. No tap target may overlap the home indicator swipe region. |

---

## Cognitive Accessibility

| Feature | Target Tier | Scope | Status | Implementation Notes |
|---------|-------------|-------|--------|---------------------|
| Pause anywhere | Basic | All gameplay states | Not Started | Mobile MMORPG caveat: full pause may disconnect the session. Acceptable alternative: tap-to-pause suspends local input and shows a "returning to game" timer; server continues. Document this caveat explicitly in UX. |
| Tutorial persistence | Standard | All tutorial prompts | Not Started | All tutorial text accessible from Settings → Help after dismissal. Do not rely on players reading prompts on first encounter. |
| Quest/objective clarity | Standard | Quest system | Not Started | Current active objective visible within 2 taps from any gameplay state. Full objective text on demand, not just map marker. |
| Visual indicators for audio | Standard | All gameplay-critical SFX | Not Started | See Auditory Accessibility section. Iron Grind is a server-authoritative game — most state is conveyed via server messages with explicit visual feedback already. Audit for any audio-only state. |
| Reading time for notifications | Standard | Toast notifications, auto-dismiss dialogs | Not Started | No auto-dismissing element with actionable text may dismiss in under 4 seconds. Preferred: tap-to-dismiss. Applies to: skill result toasts, enhancement outcome notification, loot drop notifications. |

---

## Auditory Accessibility

Iron Grind's audio system GDD is not yet authored. These are forward commitments
to honor when the audio system is designed.

| Feature | Target Tier | Scope | Status | Implementation Notes |
|---------|-------------|-------|--------|---------------------|
| Independent volume controls | Basic | Music / SFX / Voice / UI buses | Not Started | Four independent sliders. Expose in Settings and in-game pause overlay. Persist to player profile. |
| Subtitles (dialogue) | Basic | All voiced content | Not Started | Iron Grind is not voiced at MVP. Flag this if voiceover is added post-launch. |
| Visual backup for gameplay-critical SFX | Standard | All SFX that change what the player should do | Not Started | See SFX audit table below. Fill in when audio GDD is authored. |
| Hearing aid compatibility | Standard | High-frequency audio cues | Not Started | Any cue communicating critical information only through frequencies >4kHz requires a low-frequency or visual equivalent. Audit during audio implementation. |

### Gameplay-Critical SFX Audit

> Fill in when audio GDD is authored. Every entry needs a confirmed visual backup or is flagged for caption.

| Sound Effect | What It Communicates | Visual Backup | Status |
|-------------|---------------------|--------------|--------|
| [Skill ready / off cooldown] | [Skill slot available to activate] | [Cooldown arc completes; slot brightens — per ADR-008] | [Not Started] |
| [Auto-attack hit confirm] | [Damage landed on target] | [Damage number floats above target] | [Not Started] |
| [Player taking damage] | [Character took a hit] | [HP bar decreases; screen flash if critical] | [Not Started] |
| [Enemy death] | [Target eliminated] | [Death animation + loot drop indicator] | [Not Started] |
| [Enhancement success/failure] | [Enhancement outcome] | [Outcome animation (burst vs. crack) + text label] | [Not Started] |
| [Zone entry / zone loaded] | [Zone is ready] | [Loading screen removed; character spawns] | [Not Started] |

---

## Platform Accessibility API Integration

| Platform | API / Standard | Features Planned | Status |
|----------|---------------|-----------------|--------|
| iOS | UIAccessibility / Dynamic Type | VoiceOver passthrough for menus (post-launch); Dynamic Type for text size if native UIKit elements used | Not Started — Unity UI Toolkit elements do not expose UIAccessibility natively; requires custom accessibility node bridge |
| Android | AccessibilityService / TalkBack | TalkBack for menus (post-launch, Android release) | Deferred to Android release |
| Apple HIG | Touch target minimums, safe area, color contrast | 44pt touch targets, safe area compliance, contrast ratios | Not Started — see Visual and Motor sections above |

---

## Per-Feature Accessibility Matrix

| System | Visual Concerns | Motor Concerns | Cognitive Concerns | Auditory Concerns | Addressed | Notes |
|--------|----------------|---------------|-------------------|------------------|-----------|-------|
| Combat / Auto-Attack | None (toggle button, no color-only signal) | Auto-attack is a toggle — no sustained hold | Rhythm Mastery timing windows | Hit confirm SFX → visual backup required | Partial | Timing window multiplier planned |
| Skill Bar (ADR-008) | Cooldown arc is color-coded (color tint + arc) | Skill slots 56dp wide — verify 44pt floor; expand toggle is a tap | 8-slot tracking + cooldown timing | Skill ready SFX → visual backup (arc + slot brightening) | Partial | Color-only arc tint needs shape/animation backup |
| HUD (ADR-005) | HP bar color gradient; text min 14pt | All HUD elements within safe area | Simultaneous tracking: HP, MP, enemy HP, buff tray, auto-attack toggle | Damage taken SFX → HP bar update | Partial | HP numeric value present; safe area compliance required |
| Inventory / Equipment | Item rarity color border | Touch targets in item grid — must hit 44pt | Item stat comparison (multiple values) | None | Not Started | Non-color rarity backup required |
| Enhancement System | Success/fail color feedback | Confirm tap — single press, no hold | Outcome is a single discrete event | Enhancement SFX → animation backup | Not Started | Color-only feedback must add text/animation |
| NPC Shop | Item rarity color; price text size | Touch targets in shop list | Purchase confirmation flow | None | Not Started | |
| Navigation / NavMesh | None (touch-to-move tap) | Tap precision on map | Destination feedback (is tap registered?) | None | Not Started | Visual tap indicator required |
| Chat Panel | Text size (reading-heavy) | Chat input keyboard interaction | Moderate (scanning messages, @mentions) | None | Not Started | Adjustable text size required |
| Minimap | Ally/enemy color coding | None | Map orientation | None | Not Started | Shape + color markers required |
| Zone Instancing | Loading screen text | None | Zone transition clarity | Zone-ready audio → visual backup | Not Started | |

---

## Accessibility Test Plan

| Feature | Test Method | Pass Criteria | Responsible | Status |
|---------|------------|--------------|-------------|--------|
| Text contrast ratios | Automated — contrast analyzer on all UI screenshots | All body text ≥ 4.5:1; large text ≥ 3:1 | ux-designer | Not Started |
| Colorblind modes | Manual — Coblis on gameplay screenshots in all 3 modes | No essential information lost; all objectives completable without color discrimination | ux-designer | Not Started |
| Touch target sizes | Automated — measure tap target rects in UI layout | All interactive elements ≥ 44×44pt; gaps ≥ 8pt | qa-tester | Not Started |
| Timing window multiplier | Manual — enable 2× multiplier, complete skill-heavy combat encounter | All skill timing windows completable at 2× without server rejections | qa-tester | Not Started |
| Safe area compliance | Manual — run on iPhone X/11/12/15 (notch + Dynamic Island) | No interactive element obscured by notch or home indicator | qa-tester | Not Started |
| Motion reduction mode | Manual — enable mode, navigate menus and play 10 minutes | No looping menu animations; no screen shake; all transitions are fade or cut | ux-designer | Not Started |
| Touch target spacing | Manual — attempt mis-tap between adjacent targets | Tapping between two adjacent slots does not activate either | qa-tester | Not Started |

---

## Known Intentional Limitations

| Feature | Tier Required | Why Not Included | Mitigation |
|---------|--------------|-----------------|------------|
| VoiceOver / TalkBack for in-game world | Comprehensive | Unity 6.3 UI Toolkit does not expose accessibility nodes for game-world elements (only UGUI Canvas has limited UIAccessibility bridge); custom implementation is post-launch scope | All critical game state is conveyed visually; menus are primary VoiceOver target, deferred post-launch |
| Full subtitle customization | Comprehensive | Iron Grind is not voiced at MVP; subtitle system not yet authored | No voiced content at MVP — revisit when audio GDD and voice system are in scope |
| Mono audio | Comprehensive | Audio system GDD not yet authored; mono fold implementation deferred | Evaluate during audio system implementation sprint |
| Haptic alternatives for all audio cues | Exemplary | iOS Core Haptics integration requires dedicated sprint; out of MVP scope | iOS haptic feedback for critical UI events (enhance success/fail, zone entry) is a post-launch candidate |

---

## Audit History

| Date | Auditor | Type | Scope | Findings | Status |
|------|---------|------|-------|----------|--------|
| 2026-06-27 | ux-designer (internal) | Commitment review | Pre-production gate | Document created; no implementation yet — all items Not Started | Committed |

---

## Open Questions

| Question | Owner | Deadline | Resolution |
|----------|-------|----------|-----------|
| Does Unity 6.3 UI Toolkit expose accessibility nodes consumable by iOS VoiceOver? | ux-designer | Pre-Production gate | Unresolved — verify against docs/engine-reference/unity/ |
| Can the timing window multiplier be applied client-side without server changes? | lead-programmer | Before Rhythm Mastery sprint | Unresolved — server tick rate is fixed at 20Hz (ADR-004); client-side extension must not desync |
| What is the minimum iOS version that must be supported? (affects Dynamic Type support) | producer | Pre-Production gate | Unresolved |
