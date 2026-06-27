# Unity 6 — Current Best Practices

*Last verified: 2026-04-19*
*Focused on mobile (iOS) + URP + C# — relevant to Project Iron Grind.*

---

## URP Rendering (Unity 6 required patterns)

### Render Graph is Required for Custom Passes
URP Compatibility Mode was removed in 6.3. All `ScriptableRendererFeature` must use render graph.

```csharp
// WRONG — removed in Unity 6.3
public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData data) { }

// CORRECT — Unity 6 render graph pattern
public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { }
public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) { }
```

### Mobile URP Performance Checklist
- **SRP Batching**: Enable in URP Asset → Advanced → SRP Batcher (significant CPU win)
- **Store Actions**: Set to `Auto` or `Discard` for iOS (tile-based GPU — TBDR architecture)
- **MSAA**: Disable or set to 2x on lower-end targets (memory bandwidth cost on mobile)
- **Opaque materials**: Prefer over transparent wherever possible (no sorting overhead)
- **Shadows**: Disable shadow casting on props that don't need it (draw call reduction)
- **Render Graph native passes**: On iOS TBDR, URP merges passes into native render passes,
  keeping textures in tile memory — significant bandwidth savings vs. old Compatibility Mode

---

## Object Search — Required Unity 6 Pattern

```csharp
// WRONG (deprecated — compiler warning)
var players = FindObjectsOfType<PlayerController>();
var player  = FindObjectOfType<PlayerController>();

// CORRECT
var players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None); // unsorted = fastest
var player  = FindAnyObjectByType<PlayerController>();   // fastest single-object lookup
var player  = FindFirstObjectByType<PlayerController>(); // deterministic (sorted by InstanceID)
```

---

## SerializeField — Unity 6.3 Enforcement

```csharp
// COMPILE ERROR in 6.3+
[SerializeField] public float Speed { get; set; }

// CORRECT — field only
[SerializeField] private float _speed;

// ALSO CORRECT — backing field attribute
[field: SerializeField] public float Speed { get; private set; }
```

---

## C# Patterns (Unity 6)

### Component access
```csharp
// Prefer TryGetComponent over GetComponent + null check
if (TryGetComponent<Rigidbody>(out var rb))
    rb.AddForce(Vector3.up);
```

### Events
```csharp
// C# events for code-to-code (better performance than UnityEvent)
public event Action<int> OnHealthChanged;

// UnityEvent is fine for designer-facing Inspector connections
public UnityEvent OnDeath;
```

### Null coalescing with Unity objects
```csharp
// WARNING: C# null-coalescing (?? and ?.) does NOT work correctly with
// Unity Object subclasses — use explicit null checks instead
if (target != null) target.DoSomething(); // CORRECT
target?.DoSomething();                    // UNSAFE with Unity Objects
```

---

## iOS Build Settings (Unity 6)

| Setting | Value | Reason |
|---------|-------|--------|
| Scripting Backend | IL2CPP | Required for iOS App Store |
| Architecture | ARM64 | iPhone 5S+ (all supported devices) |
| Graphics API | Metal only | Remove OpenGL ES from the list |
| Texture format | ASTC | Best quality/size on all modern iPhones |
| Frame Pacing | Enabled | Smoother frame delivery on iOS |
| `Application.targetFrameRate` | 60 | Set in Awake — iOS defaults to 30 |

```csharp
// Set this early in game startup
void Awake()
{
    Application.targetFrameRate = 60;
    Screen.sleepTimeout = SleepTimeout.NeverSleep; // optional for active gameplay
}
```

### Safe Area (notched iPhones)
```csharp
// Always apply safe area to UI root — accounts for notch and Dynamic Island
Rect safeArea = Screen.safeArea;
Vector2 anchorMin = safeArea.position;
Vector2 anchorMax = safeArea.position + safeArea.size;
anchorMin.x /= Screen.width;  anchorMin.y /= Screen.height;
anchorMax.x /= Screen.width;  anchorMax.y /= Screen.height;
rectTransform.anchorMin = anchorMin;
rectTransform.anchorMax = anchorMax;
```

---

## Networking (Relevant for Project Iron Grind)

Unity 6.3 changed Netcode for GameObjects (NGO):
- `NetworkTransform.Update` override removed → use `NetworkTransform.OnUpdate`
- Multiplay Hosting shut down March 31, 2026 — do not use

**This project uses NGO — see `docs/architecture/ADR-004-networking-library-ngo.md`.**
Mirror was evaluated and rejected: it is battle-tested for traditional MMO patterns and better suited for persistent-world topology, but it is not officially supported on Unity 6.x, and approved CSP rules (CR-CSP-3/21) already lock in `NetworkManager.ServerTime.Tick` (NGO API). Iron Grind's instanced-zone model (10-50 players per zone) is session-shaped, which sits inside NGO's design center rather than requiring Mirror's persistent-world strengths.

---

## UI Toolkit (Unity 6)

```csharp
// WRONG — deprecated in 6.2
element.transform.position = new Vector3(10, 10, 0);

// CORRECT
element.style.translate = new StyleTranslate(new Translate(10, 10));
```

USS files: invalid syntax now **blocks import** in 6.3 (was warning). Fix all USS
syntax errors before they accumulate.

**For new projects**: UI Toolkit (UXML/USS) is the recommended UI system. UGUI
(Canvas) is still fully supported — use it if the team is more familiar with it.
