// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does cancelling auto-attack damage when a skill fires before the beat
//           feel rhythmically satisfying on touch input?
// Date: 2026-04-19

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Self-contained prototype. Add to an empty GameObject in a blank scene and press Play.
/// No other setup required.
/// </summary>
public class CombatTimingPrototype : MonoBehaviour
{
    // ── TUNE THESE ──────────────────────────────────────────────────────────────
    const float AUTO_CADENCE = 1.0f;    // seconds between auto-attack beats — try 0.8 / 1.0 / 1.2
    const float AUTO_DAMAGE  = 30f;

    const float ENEMY_MAX_HP = 1000f;

    // (label shown on button, damage, cooldown in seconds)
    static readonly (string label, float dmg, float cd)[] SKILLS =
    {
        ("Slash\n75 dmg | 3s",   75f,  3f),
        ("Crush\n120 dmg | 6s", 120f,  6f),
        ("Smash\n200 dmg | 10s", 200f, 10f),
    };
    // ────────────────────────────────────────────────────────────────────────────

    // Combat state
    float _autoTimer;
    bool  _skillUsedThisCycle;    // true if a skill fired before the beat in this cycle
    bool  _autoHasFiredThisCycle; // true after the auto fires — skills are free until next cycle
    float _enemyHp;

    // Stats
    int _autosLanded;
    int _autosCancelled;
    int _skillsUsed;

    // Skill cooldowns (one per skill)
    float[] _skillCooldowns;

    // UI refs
    Canvas   _canvas;
    Image    _beatRing;   // horizontal charge bar fill
    Image    _hpFill;
    Image[]  _cooldownOverlays;
    Text     _statsText;
    Renderer _enemyRenderer;

    readonly List<(RectTransform rt, Text txt, float lifetime)> _floatingTexts = new();

    // ── Unity lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        _autoTimer        = AUTO_CADENCE;
        _enemyHp          = ENEMY_MAX_HP;
        _skillCooldowns   = new float[SKILLS.Length];
        _cooldownOverlays = new Image[SKILLS.Length];

        EnsureEventSystem();
        BuildScene();
    }

    void Update()
    {
        TickAutoAttack();
        TickCooldowns();
        TickFloatingTexts();

        // Keyboard shortcuts for editor testing: 1 / 2 / 3
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) OnSkillPressed(0);
            if (kb.digit2Key.wasPressedThisFrame) OnSkillPressed(1);
            if (kb.digit3Key.wasPressedThisFrame) OnSkillPressed(2);
        }

        _statsText.text =
            $"Autos Landed:    <b>{_autosLanded}</b>\n" +
            $"Autos Cancelled: <b>{_autosCancelled}</b>\n" +
            $"Skills Used:     <b>{_skillsUsed}</b>\n\n" +
            $"<color=#888>Cadence: {AUTO_CADENCE}s | Auto: {AUTO_DAMAGE} dmg</color>";
    }

    // ── Auto-attack ──────────────────────────────────────────────────────────────

    void TickAutoAttack()
    {
        _autoTimer -= Time.deltaTime;

        // Beat ring fills clockwise as the next auto charges
        _beatRing.fillAmount = 1f - (_autoTimer / AUTO_CADENCE);

        if (_autoTimer > 0f) return;

        bool wasCancelled = _skillUsedThisCycle;

        // Start fresh cycle — auto has not yet fired in it
        _autoTimer += AUTO_CADENCE;
        _skillUsedThisCycle    = false;
        _autoHasFiredThisCycle = false;

        if (wasCancelled)
        {
            _autosCancelled++;
            SpawnText("CANCELLED", new Color(0.55f, 0.55f, 0.55f));
            FlashRing(new Color(0.4f, 0.4f, 0.4f, 0.5f));
        }
        else
        {
            _autosLanded++;
            _autoHasFiredThisCycle = true; // auto landed — skills are free until next cycle
            HitEnemy(AUTO_DAMAGE);
            SpawnText($"AUTO  -{AUTO_DAMAGE:0}", new Color(1f, 0.85f, 0.5f));
            FlashRing(Color.white);
        }
    }

    // ── Skills ───────────────────────────────────────────────────────────────────

    void OnSkillPressed(int idx)
    {
        if (_skillCooldowns[idx] > 0f) return;

        _skillsUsed++;
        _skillCooldowns[idx] = SKILLS[idx].cd;

        // Only cancel if the auto hasn't fired yet this cycle
        if (!_autoHasFiredThisCycle)
            _skillUsedThisCycle = true;

        HitEnemy(SKILLS[idx].dmg);
        SpawnText($"{SKILLS[idx].label.Split('\n')[0]}  -{SKILLS[idx].dmg:0}", new Color(0.5f, 0.8f, 1f));
    }

    void TickCooldowns()
    {
        for (int i = 0; i < _skillCooldowns.Length; i++)
        {
            if (_skillCooldowns[i] <= 0f) continue;
            _skillCooldowns[i] = Mathf.Max(0f, _skillCooldowns[i] - Time.deltaTime);
            _cooldownOverlays[i].fillAmount = _skillCooldowns[i] / SKILLS[i].cd;
        }
    }

    // ── Damage / HP ──────────────────────────────────────────────────────────────

    void HitEnemy(float amount)
    {
        _enemyHp = Mathf.Max(0f, _enemyHp - amount);
        _hpFill.fillAmount = _enemyHp / ENEMY_MAX_HP;

        _enemyRenderer.material.color = Color.white;
        Invoke(nameof(ResetEnemyColor), 0.06f);

        if (_enemyHp <= 0f) Invoke(nameof(RespawnEnemy), 0.5f);
    }

    void RespawnEnemy()
    {
        _enemyHp           = ENEMY_MAX_HP;
        _hpFill.fillAmount = 1f;
    }

    void ResetEnemyColor() => _enemyRenderer.material.color = new Color(0.65f, 0.18f, 0.18f);

    // ── Beat ring flash ───────────────────────────────────────────────────────────

    void FlashRing(Color c)
    {
        _beatRing.color = c;
        Invoke(nameof(ResetRingColor), 0.1f);
    }

    void ResetRingColor() => _beatRing.color = new Color(0.85f, 0.65f, 0.1f, 0.9f);

    // ── Floating damage text ──────────────────────────────────────────────────────

    void SpawnText(string msg, Color color)
    {
        var go  = new GameObject("Float");
        go.transform.SetParent(_canvas.transform, false);

        var txt       = go.AddComponent<Text>();
        txt.text      = msg;
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = 24;
        txt.fontStyle = FontStyle.Bold;
        txt.color     = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.rectTransform.sizeDelta = new Vector2(300f, 50f);

        var rt = txt.rectTransform;
        rt.anchorMin        = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(Random.Range(-60f, 60f), 90f + Random.Range(0f, 30f));

        _floatingTexts.Add((rt, txt, 1.2f));
    }

    void TickFloatingTexts()
    {
        for (int i = _floatingTexts.Count - 1; i >= 0; i--)
        {
            var (rt, txt, life) = _floatingTexts[i];
            float newLife = life - Time.deltaTime;

            if (newLife <= 0f)
            {
                Destroy(rt.gameObject);
                _floatingTexts.RemoveAt(i);
                continue;
            }

            rt.anchoredPosition += Vector2.up * 50f * Time.deltaTime;
            var c = txt.color;
            txt.color = new Color(c.r, c.g, c.b, newLife / 1.2f);
            _floatingTexts[i] = (rt, txt, newLife);
        }
    }

    // ── Scene construction ────────────────────────────────────────────────────────

    void EnsureEventSystem()
    {
        if (FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length > 0) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    void BuildScene()
    {
        // Position the camera
        var cam = Camera.main;
        cam.transform.position = new Vector3(0f, 2f, -6f);
        cam.transform.LookAt(new Vector3(0f, 0f, 3f));

        // Enemy cube (the target dummy)
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "DummyEnemy";
        cube.transform.position = new Vector3(0f, 0f, 3f);
        _enemyRenderer = cube.GetComponent<Renderer>();
        _enemyRenderer.material.color = new Color(0.65f, 0.18f, 0.18f);

        // Screen-space canvas (landscape reference: 1334 × 750)
        var canvasGO = new GameObject("Canvas");
        _canvas      = canvasGO.AddComponent<Canvas>();
        var canvas   = _canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1334f, 750f);
        scaler.matchWidthOrHeight  = 1f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // HP bar
        var hpBg = Img(canvas.transform, "HPBg", new Color(0.12f, 0.12f, 0.12f));
        SetAnchored(hpBg, new Vector2(0.5f, 0.9f), new Vector2(520f, 30f));

        _hpFill = Img(hpBg.transform, "HPFill", new Color(0.75f, 0.18f, 0.18f));
        _hpFill.type       = Image.Type.Filled;
        _hpFill.fillMethod = Image.FillMethod.Horizontal;
        _hpFill.fillAmount = 1f;
        Stretch(_hpFill.rectTransform);

        Lbl(hpBg.transform, "ENEMY HP", 16, new Color(1f, 1f, 1f, 0.7f));

        // Auto-attack charge bar (horizontal, centre screen)
        var barBg = Img(canvas.transform, "AutoBarBg", new Color(0.1f, 0.1f, 0.1f));
        SetAnchored(barBg, new Vector2(0.5f, 0.5f), new Vector2(400f, 44f), new Vector2(0f, 80f));

        _beatRing = Img(barBg.transform, "AutoBarFill", new Color(0.85f, 0.65f, 0.1f));
        _beatRing.type       = Image.Type.Filled;
        _beatRing.fillMethod = Image.FillMethod.Horizontal;
        _beatRing.fillAmount = 0f;
        Stretch(_beatRing.rectTransform);

        var barLabel = Lbl(barBg.transform, "NEXT AUTO", 18, new Color(1f, 1f, 1f, 0.8f));
        Stretch(barLabel.rectTransform);
        barLabel.alignment = TextAnchor.MiddleCenter;

        // Stats (top-left)
        _statsText = Lbl(canvas.transform, "", 20, Color.white);
        _statsText.supportRichText = true;
        _statsText.alignment       = TextAnchor.UpperLeft;
        var srt = _statsText.rectTransform;
        srt.anchorMin        = srt.anchorMax = new Vector2(0f, 1f);
        srt.pivot            = new Vector2(0f, 1f);
        srt.sizeDelta        = new Vector2(380f, 180f);
        srt.anchoredPosition = new Vector2(24f, -24f);

        // Skill buttons (bottom, centred)
        const float btnW = 168f, btnH = 110f, gap = 14f;
        float totalW = SKILLS.Length * btnW + (SKILLS.Length - 1) * gap;
        float x0     = -totalW / 2f + btnW / 2f;

        for (int i = 0; i < SKILLS.Length; i++)
        {
            int ci = i;

            var btnBg  = Img(canvas.transform, $"Skill{i}", new Color(0.15f, 0.28f, 0.50f));
            var btnImg = btnBg;
            SetAnchored(btnBg, new Vector2(0.5f, 0f), new Vector2(btnW, btnH),
                        new Vector2(x0 + i * (btnW + gap), 34f));
            btnBg.rectTransform.pivot = new Vector2(0.5f, 0f);

            var btn = btnBg.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener(() => OnSkillPressed(ci));

            // Cooldown overlay (fills top-to-bottom as cooldown drains)
            var cdImg = Img(btnBg.transform, "CD", new Color(0f, 0f, 0f, 0.65f));
            cdImg.type       = Image.Type.Filled;
            cdImg.fillMethod = Image.FillMethod.Vertical;
            cdImg.fillOrigin = (int)Image.OriginVertical.Top;
            cdImg.fillAmount = 0f;
            Stretch(cdImg.rectTransform);
            _cooldownOverlays[i] = cdImg;

            var lbl = Lbl(btnBg.transform, SKILLS[i].label, 18, Color.white);
            lbl.alignment = TextAnchor.MiddleCenter;
            Stretch(lbl.rectTransform);
        }
    }

    // ── Tiny UI helpers ───────────────────────────────────────────────────────────

    static Image Img(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static Text Lbl(Transform parent, string text, int size, Color color)
    {
        var go  = new GameObject("Lbl");
        go.transform.SetParent(parent, false);
        var t       = go.AddComponent<Text>();
        t.text      = text;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = size;
        t.color     = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.rectTransform.sizeDelta = new Vector2(300f, 160f);
        return t;
    }

    static void SetAnchored(Image img, Vector2 anchor, Vector2 size, Vector2 offset = default)
    {
        var rt = img.rectTransform;
        rt.anchorMin        = rt.anchorMax = anchor;
        rt.sizeDelta        = size;
        rt.anchoredPosition = offset;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.sizeDelta        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }
}
