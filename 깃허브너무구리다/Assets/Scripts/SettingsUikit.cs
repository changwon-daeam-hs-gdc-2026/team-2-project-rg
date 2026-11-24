using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
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
    public static readonly Color RowSelected = new Color(1f, 1f, 1f, 0.10f);           // 방향키 · 마우스로 고른 줄
    public static readonly Color TextDim = new Color(0.38f, 0.38f, 0.38f, 1f);         // 구역 제목 등 아주 옅은 글자
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

    // 버튼 평소 색. 마우스 올림 · 누름 · 키보드 선택 · 비활성 연출은 UiPress 가 부드럽게 처리
    //  (Unity 기본 색 바꾸기는 끔 — 누르면 흰 바탕에 흰 글자가 묻히고, 클릭 뒤에도 '선택됨' 색이 남던 문제)
    public static void SetButtonColor(Button b, Color baseColor)
    {
        b.transition = Selectable.Transition.None;
        UiPress p = UiPress.For(b.gameObject, (Image)b.targetGraphic);
        p.SetBase(baseColor);
    }

    public static Slider MakeSlider(Transform parent, float min, float max, bool whole)
    {
        RectTransform root = Rect("Slider", parent);
        Slider s = root.gameObject.AddComponent<Slider>();

        // 가는 둥근 트랙 (6px)
        Image bg = Img("Background", root, new Color(1f, 1f, 1f, 0.14f), true);
        Place(bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -3f), new Vector2(0f, 3f));
        Rounded(bg, 3f);

        RectTransform fillArea = Rect("Fill Area", root);
        // 채움 끝 = 손잡이 가운데 (손잡이 영역은 양옆 11 안쪽, 채움은 폭 +11)
        Place(fillArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(5.5f, -3f), new Vector2(-16.5f, 3f));
        Image fill = Img("Fill", fillArea, Accent);
        Rounded(fill, 3f);
        fill.rectTransform.sizeDelta = new Vector2(11f, 0f);   // 손잡이 가운데까지 채움

        // 둥근 손잡이 (22px) — 올리면 살짝, 끌면 더 커지고 뒤에 옅은 빛
        RectTransform handleArea = Rect("Handle Slide Area", root);
        Place(handleArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(11f, -11f), new Vector2(-11f, 11f));
        RectTransform handleRt = Rect("Handle", handleArea);
        handleRt.sizeDelta = new Vector2(22f, 0f);
        Image halo = Img("Halo", handleRt, new Color(1f, 1f, 1f, 0f));
        Rounded(halo, 20f);
        Place(halo.rectTransform, Vector2.zero, Vector2.one, new Vector2(-9f, -9f), new Vector2(9f, 9f));
        Image handle = Img("Knob", handleRt, Color.white, true);
        Rounded(handle, 11f);
        Stretch(handle.rectTransform);

        s.fillRect = fill.rectTransform;
        s.handleRect = handleRt;
        s.targetGraphic = handle;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min;
        s.maxValue = max;
        s.wholeNumbers = whole;
        s.transition = Selectable.Transition.None;

        UiPress p = UiPress.For(root.gameObject, handle);
        p.scaleTarget = handle.rectTransform;
        p.hoverScale = 1.12f;
        p.pressScale = 1.28f;
        p.halo = halo;
        p.tintBase = false;   // 손잡이 색은 흰색 그대로 (크기 · 빛으로만 반응)
        p.SetRadius(11f);
        p.SetBase(new Color(0.92f, 0.92f, 0.92f, 1f));
        return s;
    }

    // 테두리만 있는 둥근 사각형 (키보드로 고른 버튼 둘레)
    private static Sprite ring;
    public static Sprite RingSprite
    {
        get
        {
            if (ring != null) return ring;
            const int C = 64; const float T = 8f;
            Texture2D tex = new Texture2D(C, C, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[C * C];
            for (int y = 0; y < C; y++)
                for (int x = 0; x < C; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(C * 0.5f, C * 0.5f));
                    float a = Mathf.Clamp01(C * 0.5f - d + 0.5f) * Mathf.Clamp01(d - (C * 0.5f - T) + 0.5f);
                    px[y * C + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            ring = Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(C / 2, C / 2, C / 2, C / 2));
            ring.name = "SettingsRing";
            return ring;
        }
    }

    // 둥근 모서리용 원 (지름 64, 9-slice 테두리 32) — Image.pixelsPerUnitMultiplier = 32 / 반지름(px) 로 모서리 크기 조절
    private static Sprite round;
    public static Sprite RoundSprite
    {
        get
        {
            if (round != null) return round;
            const int C = 64;
            Texture2D tex = new Texture2D(C, C, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[C * C];
            for (int y = 0; y < C; y++)
                for (int x = 0; x < C; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(C * 0.5f, C * 0.5f));
                    px[y * C + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(C * 0.5f - d + 0.5f) * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            round = Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(C / 2, C / 2, C / 2, C / 2));
            round.name = "SettingsRound";
            return round;
        }
    }

    // 이미지를 반지름 radius(px) 의 둥근 사각형으로
    public static void Rounded(Image img, float radius)
    {
        img.sprite = RoundSprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, radius);
        UiPress p = img.GetComponent<UiPress>();
        if (p != null) p.SetRadius(radius);   // 키보드 선택 테두리도 같은 둥글기로
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

}

// ============================================================
// 버튼 · 슬라이더 눌림 연출 (설정창 · 오프셋 측정 화면 공통)
//  · 마우스 올림 : 바탕이 살짝 밝아짐 (흰 바탕 버튼은 살짝 어두워짐)
//  · 누름        : 버튼이 살짝 작아지고 바탕이 조금 더 밝아짐 → 떼면 탄력 있게 제자리
//                  누른 채 버튼 밖으로 나가면 눌림이 풀려 보임 (떼도 클릭되지 않는 것과 맞춤)
//  · Enter/Space : 한 번 '톡' 눌렸다 돌아오는 연출
//  · 키보드 선택 : 버튼 둘레에 흰 테두리 (키보드로 움직일 때만 — 마우스로 클릭한 뒤 계속 남지 않음)
//  · 비활성      : 버튼 전체(글자 포함)가 옅어짐
//  · 색이 바뀔 때(탭 고름 · 키 입력 대기 등)도 부드럽게 바뀜
// ============================================================
public class UiPress : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
                                      ISelectHandler, IDeselectHandler, ISubmitHandler
{
    // 마지막 입력이 키보드(방향키 · TAB · Enter)였는지 — 마우스를 움직이거나 누르면 false
    public static bool KeyboardMode { get; private set; }
    public static UiPress Hovered { get; private set; }
    private static int inputFrame = -1;

    public Image target;               // 바탕
    public Transform scaleTarget;      // 크기 연출 대상 (없으면 자기 자신)
    public float hoverScale = 1f, pressScale = 0.955f;
    public Image halo;                 // 슬라이더 손잡이 뒤 빛 (선택)
    public bool tintBase = true;

    private Selectable selectable;
    private CanvasGroup group;
    private Image ringImg;
    private Color baseColor, shownBase;
    private bool hasBase, hover, down, selected;
    private float hoverK, downK, selK, tap = 1f, disabledK;
    private float radius = 8f;

    public static UiPress For(GameObject go, Image bg)
    {
        UiPress p = go.GetComponent<UiPress>();
        if (p == null) p = go.AddComponent<UiPress>();
        if (p.target == null) p.target = bg;
        return p;
    }

    public void SetBase(Color c)
    {
        baseColor = c;
        if (!hasBase || !isActiveAndEnabled) { shownBase = c; hasBase = true; Apply(); }
    }

    public void SetRadius(float r) { radius = r; if (ringImg != null) SettingsUIKit.Rounded(ringImg, r + 3f); }

    void Awake() { selectable = GetComponent<Selectable>(); }

    void OnDisable()
    {
        // 창을 닫거나 탭을 바꿀 때 눌린 · 올린 상태가 남지 않도록
        hover = down = selected = false;
        hoverK = downK = selK = 0f; tap = 1f;
        shownBase = baseColor;
        if (Hovered == this) Hovered = null;
        Apply();
    }

    public void OnPointerEnter(PointerEventData e) { hover = true; Hovered = this; }
    public void OnPointerExit(PointerEventData e) { hover = false; if (Hovered == this) Hovered = null; }
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left && Interactable) down = true; }
    public void OnPointerUp(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) down = false; }
    public void OnSelect(BaseEventData e) { selected = true; }
    public void OnDeselect(BaseEventData e) { selected = false; }
    public void OnSubmit(BaseEventData e) { if (Interactable) tap = 0f; }

    private bool Interactable { get { return selectable == null || selectable.IsInteractable(); } }

    private static void TrackInput()
    {
        if (inputFrame == Time.frameCount) return;
        inputFrame = Time.frameCount;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame ||
                           kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            KeyboardMode = true;
        if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame || mouse.scroll.ReadValue().sqrMagnitude > 0f))
            KeyboardMode = false;
    }

    void Update()
    {
        TrackInput();
        float dt = UiTime.Delta;
        bool usable = Interactable;
        hoverK = Approach(hoverK, hover && usable && !KeyboardMode ? 1f : 0f, dt, 0.09f);
        downK = Approach(downK, down && hover && usable ? 1f : 0f, dt, down ? 0.045f : 0.14f);
        selK = Approach(selK, selected && KeyboardMode && usable ? 1f : 0f, dt, 0.10f);
        disabledK = Approach(disabledK, usable ? 0f : 1f, dt, 0.12f);
        if (tap < 1f) tap = Mathf.Min(1f, tap + dt / 0.18f);
        shownBase = Color.Lerp(shownBase, baseColor, 1f - Mathf.Exp(-dt / 0.06f));
        Apply();
    }

    private static float Approach(float v, float to, float dt, float seconds)
    {
        return Mathf.MoveTowards(v, to, dt / Mathf.Max(0.001f, seconds));
    }

    private void Apply()
    {
        if (target == null) return;
        float h = Smooth(hoverK), d = Smooth(downK), s = Smooth(selK);
        float tapK = tap < 1f ? Mathf.Sin(tap * Mathf.PI) : 0f;            // 0 → 1 → 0

        if (tintBase)
        {
            Color c = shownBase;
            bool light = (c.r + c.g + c.b) / 3f * c.a > 0.5f;                   // 흰 바탕 버튼
            if (light)
            {
                float k = 1f - 0.07f * h - 0.16f * Mathf.Max(d, tapK);
                c = new Color(c.r * k, c.g * k, c.b * k, c.a);
            }
            else if (c.a >= 0.5f)
            {
                // 불투명한 진한 회색 버튼 (닫기 등): 흰 쪽으로 조금 밝아짐
                float k = 0.10f * h + 0.16f * Mathf.Max(d, tapK) + 0.05f * s;
                c = new Color(Mathf.Lerp(c.r, 1f, k), Mathf.Lerp(c.g, 1f, k), Mathf.Lerp(c.b, 1f, k), c.a);
            }
            else
            {
                // 반투명 흰 바탕 버튼: 진하기만 조금 올림 (글자가 묻히지 않을 만큼)
                c.a = Mathf.Min(1f, c.a + 0.06f * h + 0.08f * Mathf.Max(d, tapK) + 0.04f * s);
            }
            target.color = c;
        }
        else target.color = shownBase;

        // 크기: 올림 → hoverScale, 누름 → pressScale, Enter → 한 번 톡
        float sc = Mathf.Lerp(1f, hoverScale, h);
        sc = Mathf.Lerp(sc, pressScale, d);
        sc = Mathf.Lerp(sc, pressScale, tapK * 0.8f);
        Transform st = scaleTarget != null ? scaleTarget : transform;
        st.localScale = new Vector3(sc, sc, 1f);

        if (halo != null) halo.color = new Color(1f, 1f, 1f, 0.06f * h + 0.16f * d + 0.10f * s);

        // 키보드 선택 테두리
        if (s > 0.001f && ringImg == null) MakeRing();
        if (ringImg != null)
        {
            ringImg.color = new Color(1f, 1f, 1f, 0.75f * s);
            ringImg.enabled = s > 0.001f;
        }

        // 비활성: 글자까지 옅게
        if (disabledK > 0.001f && group == null) { group = gameObject.AddComponent<CanvasGroup>(); }
        if (group != null) group.alpha = Mathf.Lerp(1f, 0.38f, Smooth(disabledK));
    }

    private void MakeRing()
    {
        ringImg = SettingsUIKit.Img("SelectRing", scaleTarget != null ? scaleTarget : transform, Color.clear);
        SettingsUIKit.Place(ringImg.rectTransform, Vector2.zero, Vector2.one, new Vector2(-3f, -3f), new Vector2(3f, 3f));
        ringImg.sprite = SettingsUIKit.RingSprite;
        ringImg.type = Image.Type.Sliced;
        ringImg.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, radius + 3f);
    }

    private static float Smooth(float x) { return x * x * (3f - 2f * x); }
}

// 줄(Row) 위에 마우스가 있는지 (설정창 줄 강조용)
public class UiRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static Transform Current { get; private set; }
    public void OnPointerEnter(PointerEventData e) { Current = transform; }
    public void OnPointerExit(PointerEventData e) { if (Current == transform) Current = null; }
    void OnDisable() { if (Current == transform) Current = null; }
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