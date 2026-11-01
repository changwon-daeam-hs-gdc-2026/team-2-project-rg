using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// ALL JUST 연출 (TP 100.00% — 모든 판정이 JUST PERFECT)  ※ ALL PERFECT 보다 위 등급
//
//  순서 (초)
//   0.00  화면 전체 흰 섬광 → 어두운 막
//   0.05  가운데에서 무지개 빛줄기(12갈래)가 돌며 펼쳐짐 + 빛 고리 3개가 차례로 퍼짐
//   0.10  "ALL JUST" — 아주 넓은 글자 간격에서 모이며 커졌다 제자리 (무지개 색이 흐름 + 빛 번짐)
//         위아래 가로 빛줄이 가운데에서 양쪽으로 뻗음
//   0.35  주변에 반짝이는 별 24개가 차례로 터짐
//   0.60  아래 작은 글자: TECHNICAL POINT 100.00%  ·  MAX COMBO n
//   hold  유지 → 0.5초 동안 사라짐 → onDone
//
//  - 에셋 없이 코드로 전부 그림, 화면 맨 위(정렬 700, 곡 정보 화면 800 아래)
// ============================================================
public class AllJustEffect : MonoBehaviour
{
    private const int SortOrder = 700;
    private float hold;
    private Action onDone;
    private float t;
    private bool done;

    private CanvasGroup root;
    private Image flash, dim, rays, rays2;
    private readonly List<Image> rings = new List<Image>();
    private TextMeshProUGUI title, titleGlow, sub;
    private Image lineTop, lineBottom;
    private class Spark { public Image img; public float start, life, size, rot; public Vector2 pos; public float hue; }
    private readonly List<Spark> sparks = new List<Spark>();
    private readonly List<Texture2D> owned = new List<Texture2D>();

    public static AllJustEffect Play(int maxCombo, float holdSeconds, Action onDone)
    {
        GameObject go = new GameObject("AllJustEffect", typeof(RectTransform));
        go.layer = 5;
        AllJustEffect fx = go.AddComponent<AllJustEffect>();
        fx.hold = Mathf.Max(1.5f, holdSeconds);
        fx.onDone = onDone;
        fx.Build(maxCombo);
        return fx;
    }

    private void Build(int maxCombo)
    {
        Canvas c = gameObject.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = SortOrder;
        CanvasScaler sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        root = gameObject.AddComponent<CanvasGroup>();
        root.blocksRaycasts = false;
        RectTransform r = (RectTransform)transform;

        TMP_FontAsset bold = SettingsWindow.BoldFont != null ? SettingsWindow.BoldFont : null;
        if (bold == null) { SettingsUIKit.EnsureFont(); bold = SettingsUIKit.Font; }
        bool fakeBold = SettingsWindow.BoldFont == null;

        dim = Full("Dim", r, new Color(0f, 0f, 0f, 0f));

        Sprite raySprite = MakeRays(12);
        rays = Centered("Rays", r, raySprite, 1500f, 1500f, Color.clear);
        rays2 = Centered("Rays2", r, raySprite, 1100f, 1100f, Color.clear);

        Sprite ring = MakeRing();
        for (int i = 0; i < 3; i++) rings.Add(Centered("Ring" + i, r, ring, 100f, 100f, Color.clear));

        lineTop = Centered("LineTop", r, NoteArt.HBellSprite, 0f, 4f, Color.clear);
        lineTop.rectTransform.anchoredPosition = new Vector2(0f, 104f);
        lineBottom = Centered("LineBottom", r, NoteArt.HBellSprite, 0f, 4f, Color.clear);
        lineBottom.rectTransform.anchoredPosition = new Vector2(0f, -104f);

        // 글자 뒤 번짐 (같은 글자를 크게 · 흐리게)
        titleGlow = Text(r, "ALL JUST", bold, 150f, fakeBold);
        titleGlow.rectTransform.anchoredPosition = new Vector2(0f, 6f);
        title = Text(r, "ALL JUST", bold, 150f, fakeBold);
        title.rectTransform.anchoredPosition = new Vector2(0f, 6f);
        title.enableVertexGradient = true;

        TMP_FontAsset reg = SettingsWindow.RegularFont != null ? SettingsWindow.RegularFont : bold;
        sub = Text(r, "TECHNICAL POINT  100.00%      ·      MAX COMBO  " + maxCombo.ToString("N0"), reg, 26f, false);
        sub.rectTransform.anchoredPosition = new Vector2(0f, -150f);
        sub.characterSpacing = 8f;

        // 반짝이는 별 (글자 둘레 타원 위에 흩어짐)
        var rnd = new System.Random(7);
        for (int i = 0; i < 24; i++)
        {
            float ang = (float)(i / 24.0 * Math.PI * 2.0 + rnd.NextDouble() * 0.3);
            float rad = 1f + (float)rnd.NextDouble() * 0.35f;
            Vector2 p = new Vector2(Mathf.Cos(ang) * 560f * rad, Mathf.Sin(ang) * 210f * rad);
            Image img = Centered("Spark" + i, r, NoteArt.StarSprite, 10f, 10f, Color.clear);
            img.rectTransform.anchoredPosition = p;
            sparks.Add(new Spark { img = img, pos = p, start = 0.35f + i * 0.045f + (float)rnd.NextDouble() * 0.2f,
                                   life = 0.55f + (float)rnd.NextDouble() * 0.3f, size = 40f + (float)rnd.NextDouble() * 60f,
                                   rot = (float)rnd.NextDouble() * 90f, hue = (float)rnd.NextDouble() });
        }

        flash = Full("Flash", r, new Color(1f, 1f, 1f, 0f));
    }

    void Update()
    {
        if (done) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        t += dt;
        float total = 0.6f + hold + 0.5f;
        float outK = 1f - Mathf.Clamp01((t - (total - 0.5f)) / 0.5f);   // 마지막 0.5초 사라짐
        root.alpha = outK;
        float hueBase = t * 0.35f;

        // 섬광 · 어두운 막
        flash.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - t / 0.35f) * 0.85f);
        dim.color = new Color(0f, 0f, 0f, 0.55f * Ease(t / 0.3f));

        // 빛줄기: 무지개 색이 돌며 회전
        float rs = Ease((t - 0.05f) / 0.6f);
        rays.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, rs);
        rays.rectTransform.localEulerAngles = new Vector3(0f, 0f, t * 14f);
        Color rc = Color.HSVToRGB(Mathf.Repeat(hueBase, 1f), 0.45f, 1f);
        rays.color = new Color(rc.r, rc.g, rc.b, 0.32f * rs);
        rays2.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, rs);
        rays2.rectTransform.localEulerAngles = new Vector3(0f, 0f, -t * 22f + 15f);
        Color rc2 = Color.HSVToRGB(Mathf.Repeat(hueBase + 0.5f, 1f), 0.35f, 1f);
        rays2.color = new Color(rc2.r, rc2.g, rc2.b, 0.22f * rs);

        // 빛 고리 3개 (차례로 크게 퍼지며 사라짐)
        for (int i = 0; i < rings.Count; i++)
        {
            float rt = Mathf.Clamp01((t - 0.05f - i * 0.18f) / 0.9f);
            float e = 1f - Mathf.Pow(1f - rt, 3f);
            float size = Mathf.Lerp(120f, 1700f + i * 300f, e);
            rings[i].rectTransform.sizeDelta = new Vector2(size, size * 0.55f);
            Color c = Color.HSVToRGB(Mathf.Repeat(hueBase + i * 0.2f, 1f), 0.3f, 1f);
            rings[i].color = new Color(c.r, c.g, c.b, rt > 0f && rt < 1f ? 0.8f * (1f - rt) : 0f);
        }

        // 제목: 넓은 간격에서 모이며 커졌다 제자리 (살짝 튕김)
        float tt = Mathf.Clamp01((t - 0.1f) / 0.5f);
        float sp = 1f - Mathf.Pow(1f - tt, 3f);
        float over = tt < 1f ? Mathf.Sin(tt * Mathf.PI) * 0.12f : 0f;
        title.characterSpacing = Mathf.Lerp(80f, 14f, sp);
        title.rectTransform.localScale = Vector3.one * (Mathf.Lerp(1.7f, 1f, sp) + over);
        title.alpha = Ease(tt * 2.5f);
        // 무지개 세로 그라데이션이 흐름 (위는 밝게)
        Color a1 = Color.HSVToRGB(Mathf.Repeat(hueBase, 1f), 0.55f, 1f);
        Color a2 = Color.HSVToRGB(Mathf.Repeat(hueBase + 0.18f, 1f), 0.55f, 1f);
        Color b1 = Color.HSVToRGB(Mathf.Repeat(hueBase + 0.40f, 1f), 0.75f, 1f);
        Color b2 = Color.HSVToRGB(Mathf.Repeat(hueBase + 0.58f, 1f), 0.75f, 1f);
        title.colorGradient = new VertexGradient(Color.Lerp(a1, Color.white, 0.45f), Color.Lerp(a2, Color.white, 0.45f), b1, b2);
        // 번짐: 같은 글자를 크게, 맥박처럼
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 6f);
        titleGlow.characterSpacing = title.characterSpacing + 6f;
        titleGlow.rectTransform.localScale = title.rectTransform.localScale * (1.06f + 0.03f * pulse);
        Color gc = Color.HSVToRGB(Mathf.Repeat(hueBase + 0.3f, 1f), 0.4f, 1f);
        titleGlow.color = new Color(gc.r, gc.g, gc.b, (0.18f + 0.12f * pulse) * Ease(tt * 2f));

        // 위아래 가로 빛줄
        float lt = Ease((t - 0.2f) / 0.5f);
        lineTop.rectTransform.sizeDelta = new Vector2(1500f * lt, 4f);
        lineBottom.rectTransform.sizeDelta = new Vector2(1500f * lt, 4f);
        lineTop.color = lineBottom.color = new Color(1f, 1f, 1f, 0.9f * lt);

        // 별
        foreach (var s in sparks)
        {
            float k = (t - s.start) / s.life;
            if (k < 0f || k > 1f) { s.img.color = Color.clear; continue; }
            float g = Mathf.Sin(k * Mathf.PI);
            s.img.rectTransform.sizeDelta = Vector2.one * s.size * g;
            s.img.rectTransform.localEulerAngles = new Vector3(0f, 0f, s.rot + k * 90f);
            Color c = Color.HSVToRGB(Mathf.Repeat(s.hue + hueBase, 1f), 0.35f, 1f);
            s.img.color = new Color(c.r, c.g, c.b, g);
        }
        // 다 끝난 별은 다시 (유지 시간 동안 계속 반짝임)
        foreach (var s in sparks) if (t - s.start > s.life && t < total - 0.8f) s.start = t + UnityEngine.Random.Range(0f, 0.6f);

        sub.alpha = Ease((t - 0.6f) / 0.4f);

        if (t >= total)
        {
            done = true;
            if (onDone != null) onDone();
            Destroy(gameObject);
        }
    }

    void OnDestroy() { foreach (var tex in owned) if (tex != null) Destroy(tex); }

    private static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - Mathf.Pow(1f - x, 3f); }

    // ---------- 그림 ----------
    private Sprite MakeRays(int count)
    {
        const int S = 256;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            float ray = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ang * count * 0.5f)), 10f);
            float fade = Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 6f);
            float core = Mathf.Exp(-r * 6f) * 0.6f;
            px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(ray * fade + core) * 255f));
        }
        tex.SetPixels32(px); tex.Apply();
        owned.Add(tex);
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    private Sprite MakeRing()
    {
        const int S = 256;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float d = Mathf.Abs(r - 0.92f);
            float a = Mathf.Clamp01(1f - d / 0.025f) + Mathf.Exp(-d / 0.05f) * 0.35f;
            px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
        }
        tex.SetPixels32(px); tex.Apply();
        owned.Add(tex);
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    // ---------- UI 도우미 ----------
    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image Full(string name, Transform parent, Color c)
    {
        RectTransform r = NewRect(name, parent);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        Image img = r.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private static Image Centered(string name, Transform parent, Sprite sprite, float w, float h, Color c)
    {
        RectTransform r = NewRect(name, parent);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(w, h);
        Image img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI Text(Transform parent, string text, TMP_FontAsset font, float size, bool bold)
    {
        RectTransform r = NewRect("Text", parent);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(1800f, size * 1.4f);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }
}