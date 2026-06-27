# Unity 6 — Deprecated APIs

*Last verified: 2026-04-19*
*"Deprecated" = still compiles with warning, removal planned. "Removed" = compile error.*

---

## Quick Reference Table

| Deprecated / Removed | Use Instead | Since | Status |
|----------------------|-------------|-------|--------|
| `Object.FindObjectsOfType<T>()` | `Object.FindObjectsByType<T>(FindObjectsSortMode.None)` | 6.0 | Deprecated |
| `Object.FindObjectOfType<T>()` | `Object.FindAnyObjectByType<T>()` | 6.0 | Deprecated |
| `ExecuteDefaultAction` | `HandleEventTrickleDown` | 6.0 | Deprecated |
| `ExecuteDefaultActionAtTarget` | `HandleEventBubbleUp` | 6.0 | Deprecated |
| `PreventDefault()` | `StopPropagation()` | 6.0 | Deprecated |
| `CustomEditorForRenderPipelineAttribute` | `[CustomEditor] + [SupportedOnRenderPipeline]` | 6.0 | Deprecated |
| `VolumeComponentMenuForRenderPipelineAttribute` | `[VolumeComponentMenu] + [SupportedOnRenderPipeline]` | 6.0 | Deprecated |
| `LightingSettings.filteringGaussRadiusAO` (int) | `filteringGaussianRadiusAO` (float) | 6.0 | Deprecated |
| `LightingSettings.filteringGaussRadiusDirect` | `filteringGaussianRadiusDirect` | 6.0 | Deprecated |
| `LightingSettings.filteringGaussRadiusIndirect` | `filteringGaussianRadiusIndirect` | 6.0 | Deprecated |
| `Rigidbody.SetDensity()` | `Rigidbody.mass` | 6.1 | Deprecated |
| `_FORWARD_PLUS` shader keyword | `_CLUSTER_LIGHT_LOOP` | 6.1 | **Changed** (breaks silently) |
| PVRTC texture compression | ASTC (iOS) or ETC2 (Android) | 6.1 | Deprecated |
| `SetupRenderPasses` (URP) | `AddRenderPasses` + render graph | 6.2 | Deprecated |
| `VisualElement.transform` (setter) | `style.translate`, `style.rotate`, `style.scale` | 6.2 | Deprecated |
| `VisualElement.transform` (getter) | `resolvedStyle.translate`, etc. | 6.2 | Deprecated |
| `RenderGraphSettings.enableRenderCompatibilityMode` | (removed — always returns false) | 6.3 | **Removed** |
| `NetworkTransform.Update` override | `NetworkTransform.OnUpdate` | 6.3 | **Removed** |
| `AdditionalBakedProbes` API | `LightTransport.IProbeIntegrator` | 6.3 | **Removed** |
| `AccessibilityNode.selected` | `AccessibilityNode.invoked` | 6.3 | Deprecated |
| Social API (`UnityEngine.SocialPlatforms`) | Unity Gaming Services or platform SDK | 6.x | Deprecated |
| `UnityEngine.Experimental.AI` functions | No direct replacement | 6.2+ | Obsolete |

---

## iOS / Mobile Specific

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| PVRTC texture format | ASTC (preferred on iOS 8+) | ASTC is now universal on all supported iPhones |
| OpenGL ES (iOS) | Metal only | Remove OpenGL ES from iOS Graphics API list in Player Settings |
| `UnityEngine.iOS.NotificationServices` | Unity Mobile Notifications package | UGS replacement |

---

## Notes

- `[Obsolete("message", true)]` — the `true` flag means it is already a **compile error**, not a warning
- Unity typically deprecates in version N and removes in N+1 or N+2
- Check release notes for the exact removal version before depending on deprecated APIs
