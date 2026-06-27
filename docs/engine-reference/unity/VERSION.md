# Unity Engine — Version Reference

*Last verified: 2026-04-19*

| Field | Value |
|-------|-------|
| **Engine Version** | Unity 6.3 LTS |
| **Internal Version** | 6000.4 |
| **LTS Support Until** | December 2027 |
| **Project Pinned** | 2026-04-19 |
| **Last Docs Verified** | 2026-04-19 |
| **LLM Knowledge Cutoff** | May 2025 |
| **Risk Level** | HIGH — Unity 6.x is beyond LLM training data |

## Knowledge Gap Warning

The LLM's training data likely covers Unity up to ~2023 LTS / early 6000.0.
Unity 6.0 through 6.3 introduced significant changes the model may not know about:
- Render graph system is now required for URP custom rendering (Compatibility Mode removed in 6.3)
- `Object.FindObjectsOfType` → `Object.FindObjectsByType` (new required parameter)
- `SetupRenderPasses` deprecated → use `AddRenderPasses` with render graph
- `[SerializeField]` on properties now causes compile errors (fields only)
- URP Compatibility Mode fully removed in 6.3

Always cross-reference this directory before suggesting Unity API calls.

## Post-Cutoff Version Timeline

| Version | Internal | Key Theme | Risk |
|---------|----------|-----------|------|
| Unity 6.0 | 6000.0 | Render graph, FindObjects API change, lighting overhaul | HIGH |
| Unity 6.1 | 6000.1 | Physics API changes, PVRTC deprecated, DX12 default on Windows | MEDIUM |
| Unity 6.2 | 6000.2 | SetupRenderPasses deprecated, VisualElement.transform deprecated | MEDIUM |
| Unity 6.3 LTS | 6000.4 | URP Compat Mode removed, SerializeField fields-only, NGO changes | HIGH |

## Verified Sources

- Unity 6.0 upgrade guide: https://docs.unity3d.com/6000.0/Documentation/Manual/UpgradeGuideUnity6.html
- Unity 6.1 upgrade guide: https://docs.unity3d.com/6000.1/Documentation/Manual/UpgradeGuideUnity61.html
- Unity 6.2 upgrade guide: https://docs.unity3d.com/6000.2/Documentation/Manual/UpgradeGuideUnity62.html
- Unity 6.3 upgrade guide: https://docs.unity3d.com/6000.4/Documentation/Manual/UpgradeGuideUnity63.html
- URP render graph intro: https://docs.unity3d.com/6000.1/Documentation/Manual/urp/render-graph-introduction.html
- URP mobile performance: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html
- Unity 6 releases: https://unity.com/releases/unity-6
