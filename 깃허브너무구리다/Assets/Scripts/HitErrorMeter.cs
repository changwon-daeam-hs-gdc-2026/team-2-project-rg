using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 판정 오차 그래프 (osu! 의 Hit Error Meter 처럼) — 화면 아래 가운데
//
//         FAST                 |                 SLOW
//   [ BAD | GOOD | GREAT |  PERFECT  | GREAT | GOOD | BAD ]   ← 판정 범위 띠 (가운데가 PERFECT)
//            ▏  ▏▏     ▏▏▏ ▏ ▏▏  ▏                           ← 칠 때마다 그 오차 위치에 판정 색 눈금 (몇 초 뒤 사라짐)
//                         ▼                                  ← 최근 평균 오차
//
//  - 왼쪽 = 빨리 침(FAST, -ms), 오른쪽 = 늦게 침(SLOW, +ms)
//  - GameManager 가 만들고 판정마다 Add(오차ms, 판정) 을 부른다. 설정 > 게임플레이 > 판정 그래프 로 켜고 끔
//  - 모든 게임 화면(GAME UI)에서 나옴, 곡 종료 / FAILED 페이드를 따라 사라짐
// ============================================================
public class HitErrorMeter : MonoBehaviour
{
    // ----- 모양 (1920 x 1080 기준) -----
    private const float Width = 380f;       // 띠 전체 폭 (= ±BAD)
    private const float BottomY = 34f;      // 화면 아래에서 띠 중심까지
    private const float BandH = 6f;
    private const float TickW = 3f, TickH = 22f;
    private const float TickLife = 3.5f;    // 눈금이 사라지는 시간
    private const int TickCount = 80;

    private static readonly Color[] JudgeColors =
    {
        new Color(0.55f, 0.95f, 1f, 1f),     // PERFECT
        new Color(0.62f, 0.92f, 0.55f, 1f),  // GREAT
        new Color(0.96f, 0.84f, 0.40f, 1f),  // GOOD
        new Color(0.95f, 0.60f, 0.32f, 1f),  // BAD
    };

    private GameManager gm;
    private double wPerfect, wGreat, wGood, wBad;   // 초
    private CanvasGroup group;
    private RectTransform bar;

    private class Tick { public Image img; public float age = 99f; }
    private readonly List<Tick> ticks = new List<Tick>();
    private int next;
    private RectTransform avgMarker;
    private float avgMs, avgShown;
    private bool hasAvg;

    public static HitErrorMeter Create(GameManager gm, double perfect, double great, double good, double bad)
    {
        GameObject go = new GameObject("HitErrorMeter", typeof(RectTransform));
        go.layer = 5;
        HitErrorMeter m = go.AddComponent<HitErrorMeter>();
        m.gm = gm;
        m.wPerfect = perfect; m.wGreat = great; m.wGood = good; m.wBad = bad;
        m.Build();
        return m;
    }

    private void Build()
    {
        Canvas c = gameObject.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 60;                         // 게임 화면 위, 일시정지(450) · 설정창(500) 아래
        CanvasScaler sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        bar = NewRect("Bar", transform);
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0.5f);
        bar.sizeDelta = new Vector2(Width, TickH + 8f);
        bar.anchoredPosition = new Vector2(0f, BottomY);

        // 어두운 바탕 + 판정 범위 띠 (바깥부터 그려서 안쪽이 위에 오도록)
        Img("Back", bar, new Color(0f, 0f, 0f, 0.35f), 0f, Width + 8f, BandH + 6f);
        Band(wBad, JudgeColors[3]);
        Band(wGood, JudgeColors[2]);
        Band(wGreat, JudgeColors[1]);
        Band(wPerfect, JudgeColors[0]);
        Band(GameManager.JustWindowMs / 1000.0, new Color(1f, 1f, 1f, 1f));   // JUST (±20ms, ULTIMATE ±13ms) — 가장 안쪽 흰 띠

        // 눈금 풀
        for (int i = 0; i < TickCount; i++)
        {
            Image t = Img("Tick", bar, Color.clear, 0f, TickW, TickH);
            t.enabled = false;
            ticks.Add(new Tick { img = t });
        }

        // 가운데 기준선 (0ms)
        Img("Center", bar, Color.white, 0f, 2f, TickH + 6f);

        // 평균 오차 표시 (작은 마름모, 띠 아래)
        Image avg = Img("Average", bar, Color.white, 0f, 9f, 9f);
        avg.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
        avgMarker = avg.rectTransform;
        avgMarker.anchoredPosition = new Vector2(0f, -(TickH * 0.5f + 8f));
        avg.enabled = false;

        // 양 끝 글자
        Label("FAST", -Width * 0.5f - 10f, TextAlignmentOptions.MidlineRight, new Color(0.53f, 0.81f, 0.92f, 0.75f));
        Label("SLOW", Width * 0.5f + 10f, TextAlignmentOptions.MidlineLeft, new Color(1f, 0.42f, 0.42f, 0.75f));
    }

    // 오차(ms, FAST = -, SLOW = +) · 판정(0 PERFECT, 1 GREAT, 2 GOOD, 3 BAD)
    public void Add(double signedMs, int judge)
    {
        Tick t = ticks[next];
        next = (next + 1) % ticks.Count;
        t.age = 0f;
        t.img.enabled = true;
        Color c = JudgeColors[Mathf.Clamp(judge, 0, 3)];
        t.img.color = c;
        t.img.rectTransform.anchoredPosition = new Vector2(X(signedMs), 0f);

        // 최근 평균 (지수 이동 평균)
        if (!hasAvg) { avgMs = (float)signedMs; avgShown = avgMs; hasAvg = true; avgMarker.GetComponent<Image>().enabled = true; }
        else avgMs = Mathf.Lerp(avgMs, (float)signedMs, 0.12f);
    }

    private float tickTimer;

    void Update()
    {
        // [렉 수정] 눈금 모양은 1/20초마다만 바꿈 (UI 는 바뀔 때마다 캔버스를 다시 그리므로)
        float dt = Time.unscaledDeltaTime;
        tickTimer += dt;
        if (tickTimer >= 0.05f) { UpdateTicks(tickTimer); tickTimer = 0f; }
        if (hasAvg)
        {
            avgShown = Mathf.Lerp(avgShown, avgMs, 1f - Mathf.Exp(-dt * 10f));
            avgMarker.anchoredPosition = new Vector2(X(avgShown), avgMarker.anchoredPosition.y);
        }
        float a = gm != null ? (gm.HideAllUi ? 0f : gm.EndUiAlpha) : 1f;   // UNKNOWN UI: 숨김
        if (!Mathf.Approximately(group.alpha, a)) group.alpha = a;
    }

    private void UpdateTicks(float dt)
    {
        foreach (var t in ticks)
        {
            if (!t.img.enabled) continue;
            t.age += dt;
            float k = 1f - Mathf.Clamp01(t.age / TickLife);
            if (k <= 0f) { t.img.enabled = false; continue; }
            Color c = t.img.color;
            t.img.color = new Color(c.r, c.g, c.b, 0.15f + 0.8f * k * k);
            float h = TickH * (1f + 0.35f * Mathf.Clamp01(1f - t.age / 0.25f));   // 막 찍힌 눈금은 잠깐 더 길게
            t.img.rectTransform.sizeDelta = new Vector2(TickW, h);
        }
    }

    private float X(double ms)
    {
        double half = wBad * 1000.0;
        return (float)(System.Math.Max(-half, System.Math.Min(half, ms)) / half) * Width * 0.5f;
    }

    private void Band(double window, Color c)
    {
        float w = (float)(window / wBad) * Width;
        Img("Band", bar, new Color(c.r, c.g, c.b, 0.55f), 0f, w, BandH);
    }

    private Image Img(string name, Transform parent, Color c, float x, float w, float h)
    {
        RectTransform r = NewRect(name, parent);
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, 0f);
        Image img = r.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private void Label(string text, float x, TextAlignmentOptions align, Color c)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt(text, bar, text, 14f, align, c, FontStyles.Bold);
        t.characterSpacing = 4f;
        RectTransform r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(align == TextAlignmentOptions.MidlineRight ? 1f : 0f, 0.5f);
        r.sizeDelta = new Vector2(80f, 24f);
        r.anchoredPosition = new Vector2(x, 0f);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}