# Unity 6 — Breaking Changes

*Last verified: 2026-04-19*
*Covers: Unity 6.0 (6000.0) through Unity 6.3 LTS (6000.3)*

---

## Unity 6.0 (6000.0)

### Object Search API — BREAKING
```csharp
// OLD (deprecated — generates warning)
var objects = FindObjectsOfType<PlayerController>();
var obj = FindObjectOfType<PlayerController>();

// NEW (required)
var objects = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
var obj = FindAnyObjectByType<PlayerController>();      // faster, any instance
var obj = FindFirstObjectByType<PlayerController>();    // deterministic, slower
```

### UI Toolkit Event Handling — BREAKING
- `ExecuteDefaultAction` → `HandleEventTrickleDown`
- `ExecuteDefaultActionAtTarget` → `HandleEventBubbleUp`
- `PreventDefault()` → `StopPropagation()`

### Render Pipeline Custom Attributes — BREAKING
- `CustomEditorForRenderPipelineAttribute` → `[CustomEditor] + [SupportedOnRenderPipeline]`
- `VolumeComponentMenuForRenderPipelineAttribute` → `[VolumeComponentMenu] + [SupportedOnRenderPipeline]`

### Lighting API — BREAKING (type changed)
- `LightingSettings.filteringGaussRadiusAO` (int) → `filteringGaussianRadiusAO` (float)
- `LightingSettings.filteringGaussRadiusDirect` (int) → `filteringGaussianRadiusDirect` (float)
- `LightingSettings.filteringGaussRadiusIndirect` (int) → `filteringGaussianRadiusIndirect` (float)

### Android — BREAKING
- `UnityPlayer` no longer extends `FrameLayout`
- Replace with `UnityPlayerForActivityOrService` or `UnityPlayerForGameActivity`

### Behavior Changes (not API breaks but observable)
- Light Probe brightness: 94% → 100% of lightmap brightness (subtle visual shift)
- Runtime 2D textures: no longer mipmap-limited by default (now opt-in)
- Metal shaders: `min16float`, `half`, `real` now compile to 32-bit (was 16-bit)
- Enlighten Baked GI removed; replaced by Progressive Lightmapper automatically
- Environment lighting no longer auto-baked — must call `Lightmapping.Bake()`

---

## Unity 6.1 (6000.1)

### Shader Keywords — BREAKING
`_FORWARD_PLUS` keyword replaced by `_CLUSTER_LIGHT_LOOP`.
Custom shaders referencing `_FORWARD_PLUS` must be updated.

### Platform Default Changes
- Windows new projects: DirectX12 is the default Auto Graphics API
- Android: Default Gradle 8.11, AGP 8.7.2, NDK r27c, JDK 17

---

## Unity 6.2 (6000.2)

### URP Rendering — BEHAVIORAL CHANGE
`AfterRendering` injection point now consistently fires AFTER final blit to back buffer.
Previous behavior was inconsistent. Migration: switch to `AfterRenderingPostProcessing`
if you need the pre-6.2 timing.

---

## Unity 6.3 LTS (6000.3)

### URP Compatibility Mode — REMOVED
`RenderGraphSettings.enableRenderCompatibilityMode` is now read-only (returns false).
**All custom URP rendering must use the render graph system.**
`SetupRenderPasses` is deprecated → migrate to `AddRenderPasses` + `RecordRenderGraph`.

### SerializeField — NOW A COMPILE ERROR
`[SerializeField]` can ONLY be applied to fields. Applying to properties, methods,
or types causes a **compile-time error** (was a warning in earlier versions).

```csharp
// COMPILE ERROR in 6.3+
[SerializeField] public float Speed { get; set; }

// CORRECT
[SerializeField] private float _speed;
// or: [field: SerializeField] public float Speed { get; private set; }
```

### Accessibility Enums — BREAKING (precompiled assemblies)
- `AccessibilityRole` changed from flags enum → standard enum (bitwise ops break)
- `AccessibilityRole` and `AccessibilityState` underlying type: `int` → `byte`
- Precompiled `.dll` assemblies will throw `MissingFieldException` — require recompile

### Scene/Entities — BREAKING (precompiled assemblies)
- `Scene.handle` type: `int` → `SceneHandle`
- `UnityEngine.Experimental.GlobalIllumination.instanceID` (int) → `entityID` (EntityId)
- Precompiled assemblies require recompilation

### Netcode for GameObjects (NGO) — BREAKING
- `NetworkTransform.Update` can no longer be overridden
- Use new `NetworkTransform.OnUpdate` method instead
- Multiplay Hosting service shut down March 31, 2026

### Lightmapping API — REMOVED
- `AdditionalBakedProbes` API removed
- `CustomBake` API obsolete → use `LightTransport.IProbeIntegrator`

### USS (UI Toolkit) — STRICTER VALIDATION
Invalid USS syntax now **blocks file import** in 6.3 (was previously a warning).
Fix all USS syntax errors before upgrading.
