using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// ============================================================
// 설정창 / 오프셋 측정 화면을 코드로 만들 때 쓰는 UI 부품
//  스프라이트 없이 단색 사각형으로 그리므로 별도 에셋이 필요 없다.
// ============================================================
public static class SettingsUIKit
{
    public static TMP_FontAsset Font;

    // 색 배합: 검정 + 흰색 계열 (설정창 · 오프셋 측정 화면)
    public static readonly Color Accent = new Color(1f, 1f, 1f, 1f);                  // 강조 = 흰색 (선택된 탭 · ON · 슬라이더 채움)
    public static readonly Color AccentDark = new Color(0.20f, 0.20f, 0.20f, 1f);     // 닫기 버튼 등 진한 회색
    public static readonly Color TextMain = new Color(0.96f, 0.96f, 0.96f, 1f);
    public static readonly Color TextSub = new Color(0.60f, 0.60f, 0.60f, 1f);
    public static readonly Color TextOnAccent = new Color(0.04f, 0.04f, 0.04f, 1f);   // 흰 바탕 위 글자 = 검정
    public static readonly Color PanelColor = new Color(0.035f, 0.035f, 0.035f, 0.97f);
    public static readonly Color PanelSolid = new Color(0.03f, 0.03f, 0.03f, 1f);
    public static readonly Color RowColor = new Color(1f, 1f, 1f, 0.04f);
    public static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.08f);
    public static readonly Color FastColor = new Color(0.53f, 0.81f, 0.92f, 1f);
    public static readonly Color SlowColor = new Color(1f, 0.35f, 0.35f, 1f);

    public static void EnsureFont()
    {
        if (Font != null) return;
        Font = Resources.Load<TMP_FontAsset>("Fonts/MoveSans-Light");
        if (Font == null) Font = TMP_Settings.defaultFontAsset;
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5; // UI
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image Img(string name, Transform parent, Color color, bool raycast = false)
    {
        RectTransform r = Rect(name, parent);
        Image img = r.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    public static TextMeshProUGUI Txt(string name, Transform parent, string text, float size, TextAlignmentOptions align,
                                      Color color, FontStyles style = FontStyles.Normal, bool wrap = false)
    {
        RectTransform r = Rect(name, parent);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        EnsureFont();
        if (Font != null) t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = wrap;
#pragma warning restore CS0618
        t.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        return t;
    }

    public static void Place(RectTransform r, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        r.anchorMin = anchorMin;
        r.anchorMax = anchorMax;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = offsetMin;
        r.offsetMax = offsetMax;
    }

    public static void Stretch(RectTransform r)
    {
        Place(r, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    // 가운데 기준 고정 크기
    public static void Center(RectTransform r, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    public static Button Btn(Transform parent, string label, float fontSize, UnityAction onClick, out TextMeshProUGUI text)
    {
        Image img = Img("Btn_" + label, parent, Color.white, true);
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        SetButtonColor(b, ButtonColor);
        text = Txt("Label", img.rectTransform, label, fontSize, TextAlignmentOptions.Center, TextMain, FontStyles.Bold);
        Stretch(text.rectTransform);
        if (onClick != null) b.onClick.AddListener(onClick);
        return b;
    }

    // 평소 색 / 마우스 올림 / 누름 / 키보드 선택 색
    public static void SetButtonColor(Button b, Color baseColor)
    {
        ColorBlock cb = b.colors;
        cb.normalColor = baseColor;
        cb.highlightedColor = Brighten(baseColor, 0.12f);
        cb.pressedColor = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
        cb.selectedColor = Blend(baseColor, new Color(Accent.r, Accent.g, Accent.b, 0.30f));   // 키보드로 고른 버튼: 옅은 흰색
        cb.disabledColor = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.4f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.06f;
        b.colors = cb;
    }

    public static Slider MakeSlider(Transform parent, float min, float max, bool whole)
    {
        RectTransform root = Rect("Slider", parent);
        Slider s = root.gameObject.AddComponent<Slider>();

        Image bg = Img("Background", root, new Color(1f, 1f, 1f, 0.14f), true);
        Place(bg.rectTransform, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), Vector2.zero, Vector2.zero);

        RectTransform fillArea = Rect("Fill Area", root);
        Place(fillArea, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), Vector2.zero, Vector2.zero);
        Image fill = Img("Fill", fillArea, Accent);
        fill.rectTransform.sizeDelta = Vector2.zero;

        RectTransform handleArea = Rect("Handle Slide Area", root);
        Place(handleArea, Vector2.zero, Vector2.one, new Vector2(9f, 0f), new Vector2(-9f, 0f));
        Image handle = Img("Handle", handleArea, Color.white, true);
        handle.rectTransform.sizeDelta = new Vector2(18f, 0f);

        s.fillRect = fill.rectTransform;
        s.handleRect = handle.rectTransform;
        s.targetGraphic = handle;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min;
        s.maxValue = max;
        s.wholeNumbers = whole;

        ColorBlock cb = s.colors;
        cb.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = Color.white;
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.06f;
        s.colors = cb;
        return s;
    }

    public static LayoutElement Fixed(GameObject go, float height)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        return le;
    }

    public static string Ms(double seconds)
    {
        int ms = (int)Math.Round(seconds * 1000.0);
        return (ms > 0 ? "+" : "") + ms + " ms";
    }

    private static Color Brighten(Color c, float add)
    {
        return new Color(Mathf.Min(1f, c.r + add), Mathf.Min(1f, c.g + add), Mathf.Min(1f, c.b + add), Mathf.Min(1f, c.a + add));
    }

    private static Color Blend(Color a, Color b)
    {
        return new Color(Mathf.Max(a.r, b.r), Mathf.Max(a.g, b.g), Mathf.Max(a.b, b.b), Mathf.Max(a.a, b.a));
    }
}

// ============================================================
// UI 연출용 시간: 한 프레임이 길게 걸려도(렉) 연출이 건너뛰어지지 않도록 한 프레임 진행량을 1/30초로 제한
//  (렉이 걸린 프레임에 Time.unscaledDeltaTime 이 0.3초처럼 크게 들어오면 짧은 모션이 통째로 끝나 버린다)
// ============================================================
public static class UiTime
{
    public const float MaxStep = 1f / 30f;
    public static float Delta { get { return Mathf.Min(Time.unscaledDeltaTime, MaxStep); } }
}