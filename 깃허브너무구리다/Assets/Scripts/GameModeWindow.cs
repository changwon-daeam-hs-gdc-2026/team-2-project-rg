using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
// 모드 설정창 (곡 선택창에서 M 키 또는 MODE 바 클릭) — 코드로 생성
//
//   ┌──────────────────────────────────────────────────────────────┐
//   │ GAME MODE                                       SCORE  130%  │
//   │ [ JUDGE 1 ]        [ RULE ]        [ CHART 1 ]               │  ← 성격별 탭 3개 (켠 모드 수)
//   │ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐            │
//   │ │[◎] EASY      │ │[◎] HARD      │ │[◎] MAXIMUM   │  ← 탭의 카드 (3열 × 최대 3줄)
//   │ │ 설명…   120% │ │ 설명…   165% │ │ 설명…   175% │     켜진 카드: 모드 색 테두리 + 왼쪽 색 막대
//   │ └──────────────┘ └──────────────┘ └──────────────┘            │
//   │  …                                                           │
//   │ [1 MIRROR] [2 HARD] [3 ─ ]                 (안내 · 경고 한 줄) │
//   │          ARROWS MOVE  SPACE SELECT  TAB CATEGORY  BACKSPACE CLEAR  M ESC CLOSE │
//   └──────────────────────────────────────────────────────────────┘
//
//  조작: 방향키 이동 · SPACE / ENTER 켜고 끄기 · TAB (SHIFT+TAB) 분류 바꾸기 · BACKSPACE 모두 끄기 · M / ESC 닫기
//        마우스: 탭 · 카드 클릭, 아래 칸 클릭 = 그 모드 끄기, 창 바깥 클릭 = 닫기
//  AUTO PLAY 가 켜져 있으면 열리지 않음 (곡 선택창이 MODE 바를 깜빡여 알려 줌)
//  같이 쓸 수 없는 모드를 고르면 이전 모드와 바뀜 (예: MIRROR → RANDOM). 3개가 차 있으면 경고만.
//  모드 이름 · 안내는 영어, 모드 설명은 한국어
// ============================================================
public class GameModeWindow : MonoBehaviour
{
    public static GameModeWindow Instance { get; private set; }
    public static bool IsOpen { get { return Instance != null && !Instance.closing; } }
    private static int closedFrame = -10;
    // 열려 있거나 이번 프레임에 닫힘 → 곡 선택 조작을 막음 (닫는 ESC/M 이 곡 선택창에도 먹지 않게)
    public static bool BlocksInput { get { return Instance != null || Time.frameCount <= closedFrame + 1; } }
    public static Action OnChanged;

    private const int Cols = 3, Rows = 3;                // 탭 하나에 최대 9개 (3열 × 3줄)
    private const float PanelW = 1280f, PanelH = GridTop + Rows * (CardH + Gap) + 190f, Pad = 48f;
    private const float CardH = 124f, Gap = 14f, TabTop = 108f, TabH = 40f, GridTop = 166f;

    private static readonly Color BackColor = new Color(0f, 0f, 0f, 0.72f);   // 뒤 배경 어둡게 (이전과 같은 값, 블러 위에 덮음)
    private static readonly Color PanelColor = new Color(0.03f, 0.035f, 0.05f, 0.97f);
    // [가독성 수정] 카드 바탕은 불투명한 어두운 색 (반투명 흰색을 겹치면 선형 색 공간에서 회색 · 흰색으로 떠서 글자가 안 보였다)
    //  테두리(ring) 위에 바탕을 1.5px 안쪽으로 덮어서, 테두리 색은 가장자리 선으로만 보임
    private static readonly Color CardColor = new Color(0.065f, 0.072f, 0.092f, 1f);
    private static readonly Color CardFocus = new Color(0.105f, 0.115f, 0.145f, 1f);
    private static readonly Color RingIdle = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color RingFocus = new Color(1f, 1f, 1f, 0.50f);
    private static readonly Color TextMain = new Color(0.96f, 0.97f, 1f, 1f);
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color TextDim = new Color(1f, 1f, 1f, 0.28f);
    private static readonly Color WarnColor = new Color(1f, 0.42f, 0.45f, 1f);

    private class Card
    {
        public GameMode mode;
        public int category;
        public RectTransform rt;
        public Image ring, bg, accent;
        public TextMeshProUGUI name, desc, mult;
        public GameModeBadge badge;
        public CanvasGroup cg;
        public float sel, focus, pop;
    }

    private class Slot { public RectTransform rt; public GameModeBadge badge; public TextMeshProUGUI name; public Image ring; }
    private class Tab { public RectTransform rt; public Image bg, line; public TextMeshProUGUI label; public float on; }

    private readonly Tab[] tabs = new Tab[GameModes.CategoryNames.Length];
    private int currentTab;
    private readonly List<Card> view = new List<Card>();   // 지금 탭의 카드

    private readonly List<Card> cards = new List<Card>();
    private readonly Slot[] slots = new Slot[GameModes.MaxSelected];
    private RectTransform panel;
    private CanvasGroup group;
    private TextMeshProUGUI totalText, message, capNote;
    private TMP_FontAsset bold, reg;
    private float openT, msgT;
    private bool closing;
    private int openedFrame;
    private int focusIndex;
    private bool mouseFocus;
    private int lastVersion = -1;
    private Vector2 lastMouse;

    // =====================================================================
    //  열기 / 닫기
    // =====================================================================
    public static void Open(Scene scene, TMP_FontAsset nameFont)
    {
        if (Instance != null || GameModes.Locked) return;   // AUTO PLAY 중에는 모드를 고를 수 없음
        GameObject go = new GameObject("GameModeWindow", typeof(RectTransform));
        go.layer = 5;
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(go, scene);
        GameModeWindow w = go.AddComponent<GameModeWindow>();
        Instance = w;
        w.openedFrame = Time.frameCount;
        w.StartCoroutine(w.OpenRoutine(nameFont));
    }

    public void Close()
    {
        if (closing) return;
        closing = true;
        closedFrame = Time.frameCount;
    }

    void OnDestroy()
    {
        if (blurTex != null) Destroy(blurTex);
        if (Instance == this) Instance = null;
        closedFrame = Mathf.Max(closedFrame, Time.frameCount);
    }

    // =====================================================================
    //  만들기
    // =====================================================================
    // 창을 만들기 전에 지금 화면을 찍어 흐리게 (창이 같이 찍히지 않도록 프레임 끝에서)
    private Texture2D blurTex;
    private bool built;
    private System.Collections.IEnumerator OpenRoutine(TMP_FontAsset nameFont)
    {
        yield return new WaitForEndOfFrame();
        blurTex = PlayConfirmDialog.CaptureBlurred();
        Build(nameFont);
        built = true;
    }

    private void Build(TMP_FontAsset nameFont)
    {
        SettingsUIKit.EnsureFont();
        reg = SettingsWindow.RegularFont != null ? SettingsWindow.RegularFont : Resources.Load<TMP_FontAsset>("Fonts/SettingsFont");
        if (reg == null) reg = SettingsUIKit.Font;
        bold = SettingsWindow.BoldFont != null ? SettingsWindow.BoldFont : Resources.Load<TMP_FontAsset>("Fonts/SettingsFontBold");
        if (nameFont != null) bold = nameFont;
        if (bold == null) bold = reg;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;               // 곡 선택 UI 위, 설정창(500) 아래
        CanvasScaler sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();   // 뒤쪽 곡 선택 패널 버튼이 눌리지 않도록 가림
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // 뒤 배경: 곡 선택 화면을 흐리게 찍은 그림 + 이전과 같은 어두운 막 (클릭이 뒤로 새지 않게 막음)
        RawImage blur = new GameObject("Blur", typeof(RectTransform)).AddComponent<RawImage>();
        blur.gameObject.layer = 5;
        blur.transform.SetParent(transform, false);
        blur.texture = blurTex;
        blur.color = blurTex != null ? Color.white : new Color(0f, 0f, 0f, 0.6f);
        blur.raycastTarget = true;
        SettingsUIKit.Stretch(blur.rectTransform);
        Image back = SettingsUIKit.Img("Backdrop", transform, BackColor, true);
        SettingsUIKit.Stretch(back.rectTransform);

        Image p = Round(SettingsUIKit.Img("Panel", transform, PanelColor, true), 18f);
        panel = p.rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(PanelW, PanelH);
        Image edge = Round(SettingsUIKit.Img("Edge", panel, new Color(1f, 1f, 1f, 0.07f)), 18f);   // (원래 값 — 안쪽 바탕 색이 바뀌지 않게)
        SettingsUIKit.Stretch(edge.rectTransform);
        edge.transform.SetAsFirstSibling();
        Image inner = Round(SettingsUIKit.Img("Inner", panel, PanelColor), 17f);
        SettingsUIKit.Stretch(inner.rectTransform);
        inner.rectTransform.offsetMin = new Vector2(1f, 1f); inner.rectTransform.offsetMax = new Vector2(-1f, -1f);
        inner.transform.SetSiblingIndex(1);
        // 창 겉 테두리: 얇은 흰 선 (속이 빈 둥근 사각형 그림이라 안쪽 바탕 색에는 영향 없음)
        Image border = SettingsUIKit.Img("Border", panel, new Color(1f, 1f, 1f, 0.55f));
        border.sprite = GameModes.RingSprite; border.type = Image.Type.Sliced; border.fillCenter = false;
        border.pixelsPerUnitMultiplier = 16f / 18f;
        SettingsUIKit.Stretch(border.rectTransform);
        border.transform.SetSiblingIndex(2);

        // ---- 머리줄 ----
        TextMeshProUGUI title = Txt(panel, "GAME MODE", 30, TextAlignmentOptions.MidlineLeft, TextMain, bold, FontStyles.Bold);
        title.characterSpacing = 10f;
        Box(title.rectTransform, Pad, 34, 500, 44);
        TextMeshProUGUI sub = Txt(panel, "UP TO " + GameModes.MaxSelected + " MODES", 15, TextAlignmentOptions.MidlineLeft, TextDim, bold, FontStyles.Bold);
        sub.characterSpacing = 6f;
        Box(sub.rectTransform, Pad + 2, 76, 500, 22);

        TextMeshProUGUI sl = Txt(panel, "SCORE", 15, TextAlignmentOptions.MidlineRight, TextDim, bold, FontStyles.Bold);
        sl.characterSpacing = 8f;
        Box(sl.rectTransform, PanelW - Pad - 340, 30, 160, 24);
        totalText = Txt(panel, "100%", 40, TextAlignmentOptions.MidlineRight, TextMain, bold, FontStyles.Bold);
        Box(totalText.rectTransform, PanelW - Pad - 170, 22, 170, 56);
        capNote = Txt(panel, "", 13, TextAlignmentOptions.MidlineRight, TextDim, bold, FontStyles.Bold);
        capNote.characterSpacing = 4f;
        Box(capNote.rectTransform, PanelW - Pad - 340, 76, 340, 22);

        // ---- 분류 탭 ----
        float tabW = (PanelW - Pad * 2f - 10f * (tabs.Length - 1)) / tabs.Length;
        for (int t = 0; t < tabs.Length; t++)
        {
            Tab tb = new Tab();
            tb.bg = Round(SettingsUIKit.Img("Tab" + t, panel, CardColor), 9f);
            tb.rt = tb.bg.rectTransform;
            Box(tb.rt, Pad + t * (tabW + 10f), TabTop, tabW, TabH);
            tb.label = Txt(tb.rt, GameModes.CategoryNames[t], 15, TextAlignmentOptions.Center, TextSub, bold, FontStyles.Bold);
            tb.label.characterSpacing = 6f;
            SettingsUIKit.Stretch(tb.label.rectTransform);
            tb.line = SettingsUIKit.Img("Line", tb.rt, Color.white);
            tb.line.rectTransform.anchorMin = new Vector2(0.15f, 0f);
            tb.line.rectTransform.anchorMax = new Vector2(0.85f, 0f);
            tb.line.rectTransform.offsetMin = new Vector2(0f, 0f);
            tb.line.rectTransform.offsetMax = new Vector2(0f, 2f);
            tabs[t] = tb;
        }

        // ---- 카드 (분류마다 3열, 탭을 바꾸면 그 분류만 보임) ----
        float cardW = (PanelW - Pad * 2f - Gap * (Cols - 1)) / Cols;
        int[] count = new int[tabs.Length];
        foreach (var info in GameModes.All)
        {
            int i = count[info.Category]++;
            int col = i % Cols, row = i / Cols;
            Card c = MakeCard(info, Pad + col * (cardW + Gap), GridTop + row * (CardH + Gap), cardW);
            c.category = info.Category;
            cards.Add(c);
        }
        SetTab(0);

        // ---- 아래: 고른 모드 3칸 + 안내 ----
        float fy = GridTop + Rows * (CardH + Gap) + 12f;
        Image line = SettingsUIKit.Img("Line", panel, new Color(1f, 1f, 1f, 0.06f));
        Box(line.rectTransform, Pad, fy, PanelW - Pad * 2f, 1);
        float slotW = 236f;
        for (int i = 0; i < slots.Length; i++)
        {
            Slot s = new Slot();
            Image sb = Round(SettingsUIKit.Img("Slot" + i, panel, CardColor), 10f);
            s.rt = sb.rectTransform;
            Box(s.rt, Pad + i * (slotW + 12f), fy + 18, slotW, 58);
            s.ring = sb;
            RectTransform br = GameModes.CreateBadge(s.rt, GameMode.Mirror, 40f);
            br.anchorMin = br.anchorMax = new Vector2(0f, 0.5f);
            br.pivot = new Vector2(0f, 0.5f);
            br.anchoredPosition = new Vector2(10f, 0f);
            s.badge = br.GetComponent<GameModeBadge>();
            s.name = Txt(s.rt, "", 16, TextAlignmentOptions.MidlineLeft, TextMain, bold, FontStyles.Bold);
            s.name.characterSpacing = 3f;
            s.name.enableAutoSizing = true; s.name.fontSizeMax = 16; s.name.fontSizeMin = 11;
            SettingsUIKit.Place(s.name.rectTransform, Vector2.zero, Vector2.one, new Vector2(60f, 0f), new Vector2(-10f, 0f));
            slots[i] = s;
        }
        message = Txt(panel, "", 16, TextAlignmentOptions.MidlineRight, TextSub, bold, FontStyles.Bold);
        message.characterSpacing = 4f;
        Box(message.rectTransform, PanelW - Pad - 480, fy + 18, 480, 30);
        // 키 안내: 키캡 그림 (일시정지 · 결과창과 같은 KeyHintUI)
        RectTransform hints = KeyHintUI.Build(panel, reg, bold, new[]
        {
            new KeyHintUI.Item("MOVE", "^LEFT", "^RIGHT", "^UP", "^DOWN"),
            new KeyHintUI.Item("SELECT", "SPACE"),
            new KeyHintUI.Item("CATEGORY", "TAB"),
            new KeyHintUI.Item("CLEAR", "BACKSPACE"),
            new KeyHintUI.Item("CLOSE", "M", "ESC"),
        }, 14f, 1);
        hints.anchorMin = hints.anchorMax = new Vector2(1f, 0f);
        hints.anchoredPosition = new Vector2(-Pad, 34f);

        Refresh(true);
        Update();
    }

    private Card MakeCard(GameModeInfo info, float x, float y, float w)
    {
        Card c = new Card { mode = info.Mode };
        c.ring = Round(SettingsUIKit.Img("Card_" + info.Name, panel, RingIdle), 12f);
        c.rt = c.ring.rectTransform;
        Box(c.rt, x, y, w, CardH);
        c.cg = c.rt.gameObject.AddComponent<CanvasGroup>();
        c.bg = Round(SettingsUIKit.Img("Bg", c.rt, CardColor), 11f);
        SettingsUIKit.Stretch(c.bg.rectTransform);
        c.bg.rectTransform.offsetMin = new Vector2(1.5f, 1.5f); c.bg.rectTransform.offsetMax = new Vector2(-1.5f, -1.5f);

        // 켜졌을 때 왼쪽 색 막대
        // 켜졌을 때 왼쪽 색 막대: 카드와 같은 높이, 카드의 둥근 모서리 모양대로 잘림 (카드 자체를 마스크로)
        c.ring.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        c.accent = SettingsUIKit.Img("Accent", c.rt, info.Color);
        c.accent.rectTransform.anchorMin = new Vector2(0f, 0f);
        c.accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        c.accent.rectTransform.pivot = new Vector2(0f, 0.5f);
        c.accent.rectTransform.anchoredPosition = Vector2.zero;
        c.accent.rectTransform.sizeDelta = new Vector2(0f, 0f);

        RectTransform br = GameModes.CreateBadge(c.rt, info.Mode, 58f);
        br.anchorMin = br.anchorMax = new Vector2(0f, 0.5f);
        br.pivot = new Vector2(0f, 0.5f);
        br.anchoredPosition = new Vector2(20f, 0f);
        c.badge = br.GetComponent<GameModeBadge>();

        c.name = Txt(c.rt, info.Name, 20, TextAlignmentOptions.TopLeft, TextMain, bold, FontStyles.Bold);
        c.name.characterSpacing = 4f;
        c.name.enableAutoSizing = true; c.name.fontSizeMax = 20; c.name.fontSizeMin = 14;
        SettingsUIKit.Place(c.name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(94f, -44f), new Vector2(-92f, -14f));

        string m = GameModes.Percent(info.Multiplier);
        Color mc = info.Multiplier > 1.0001 ? new Color(info.Color.r, info.Color.g, info.Color.b, 0.95f)
                 : info.Multiplier < 0.9999 ? new Color(1f, 1f, 1f, 0.40f) : TextSub;
        c.mult = Txt(c.rt, m, 18, TextAlignmentOptions.TopRight, mc, bold, FontStyles.Bold);
        c.mult.characterSpacing = 2f;
        SettingsUIKit.Place(c.mult.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -42f), new Vector2(-18f, -15f));

        c.desc = Txt(c.rt, info.Desc, 15, TextAlignmentOptions.TopLeft, TextSub, reg, FontStyles.Normal);
        c.desc.enableWordWrapping = true;
        c.desc.lineSpacing = -6f;
        c.desc.enableAutoSizing = true; c.desc.fontSizeMax = 15; c.desc.fontSizeMin = 11.5f;
        SettingsUIKit.Place(c.desc.rectTransform, Vector2.zero, Vector2.one, new Vector2(94f, 10f), new Vector2(-18f, -46f));
        return c;
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    void Update()
    {
        if (!built) return;                       // 화면을 찍고 창을 만드는 중
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        // 열림 / 닫힘 연출 (살짝 커지며 나타남)
        openT = Mathf.MoveTowards(openT, closing ? 0f : 1f, dt / (closing ? 0.14f : 0.18f));
        float e = 1f - Mathf.Pow(1f - openT, 3f);
        group.alpha = e;
        panel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
        group.blocksRaycasts = !closing;
        if (closing)
        {
            if (openT <= 0f) Destroy(gameObject);
            return;
        }

        if (!SettingsWindow.BlocksGameInput) { HandleKeys(); HandleMouse(); }

        if (lastVersion != GameModes.Version) Refresh(false);

        // 카드 상태 연출
        float k = 1f - Mathf.Exp(-dt * 18f);
        var sel = GameModes.Selected;
        bool full = sel.Count >= GameModes.MaxSelected;
        for (int i = 0; i < cards.Count; i++)
        {
            Card c = cards[i];
            bool on = sel.Contains(c.mode);
            bool blocked = !on && full && !sel.Exists(x => GameModes.Conflicts(x, c.mode));
            c.sel = Mathf.Lerp(c.sel, on ? 1f : 0f, k);
            bool focused = focusIndex >= 0 && focusIndex < view.Count && view[focusIndex] == c;
            c.focus = Mathf.Lerp(c.focus, focused ? 1f : 0f, k);
            c.pop = Mathf.MoveTowards(c.pop, 0f, dt / 0.25f);
            Color mc = GameModes.Info(c.mode).Color;
            Color ring = Color.Lerp(RingIdle, RingFocus, c.focus);
            ring = Color.Lerp(ring, new Color(mc.r, mc.g, mc.b, 0.9f), c.sel);
            c.ring.color = ring;
            // 켜짐: 모드 색이 살짝 섞인 어두운 바탕 (흰 글자가 그대로 잘 보이게)
            Color selBg = Color.Lerp(CardColor, new Color(mc.r, mc.g, mc.b, 1f), 0.16f);
            selBg = Color.Lerp(selBg, Color.Lerp(CardFocus, new Color(mc.r, mc.g, mc.b, 1f), 0.20f), c.focus);
            c.bg.color = Color.Lerp(Color.Lerp(CardColor, CardFocus, c.focus), selBg, c.sel);
            c.accent.rectTransform.sizeDelta = new Vector2(5f * c.sel, 0f);   // 켜질 때 왼쪽에서 두꺼워짐
            c.accent.enabled = c.sel > 0.01f;
            c.name.color = Color.Lerp(on ? TextMain : new Color(1f, 1f, 1f, 0.85f), TextMain, c.focus);
            c.cg.alpha = Mathf.Lerp(c.cg.alpha, blocked ? 0.38f : 1f, k);
            float sc = 1f + 0.035f * Mathf.Sin(c.pop * Mathf.PI) * c.pop;
            c.rt.localScale = new Vector3(sc, sc, 1f);
        }

        // 탭: 고른 탭은 밝은 바탕 + 아래 흰 선, 켠 모드 수는 숫자로
        for (int t = 0; t < tabs.Length; t++)
        {
            Tab tb = tabs[t];
            tb.on = Mathf.Lerp(tb.on, t == currentTab ? 1f : 0f, k);
            tb.bg.color = Color.Lerp(CardColor, CardFocus, tb.on);
            tb.line.color = new Color(1f, 1f, 1f, 0.9f * tb.on);
            tb.label.color = Color.Lerp(TextSub, TextMain, tb.on);
        }

        // 안내 · 경고 글자: 잠깐 보였다 사라짐
        msgT = Mathf.MoveTowards(msgT, 0f, dt / 2.2f);
        Color mcol = message.color;
        message.color = new Color(mcol.r, mcol.g, mcol.b, Mathf.Clamp01(msgT * 3f));
    }

    private void HandleKeys()
    {
        var kb = Keyboard.current;
        if (kb == null || Time.frameCount == openedFrame) return;   // 연 프레임의 M 키로 바로 닫히지 않게

        if (kb.escapeKey.wasPressedThisFrame || kb.mKey.wasPressedThisFrame) { Close(); return; }
        if (kb.tabKey.wasPressedThisFrame)
        {
            bool back = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            SetTab((currentTab + (back ? tabs.Length - 1 : 1)) % tabs.Length);
            mouseFocus = false;
            return;
        }
        int n = view.Count;
        if (n == 0) return;
        int move = 0;
        if (kb.leftArrowKey.wasPressedThisFrame) move = -1;
        else if (kb.rightArrowKey.wasPressedThisFrame) move = 1;
        else if (kb.upArrowKey.wasPressedThisFrame) move = -Cols;
        else if (kb.downArrowKey.wasPressedThisFrame) move = Cols;
        if (move != 0)
        {
            mouseFocus = false;
            int next = focusIndex + move;
            if (move == -1 || move == 1) next = (focusIndex / Cols) * Cols + ((focusIndex % Cols) + move + Cols) % Cols;   // 같은 줄 안에서 돌기
            else next = (next + n) % n;
            focusIndex = Mathf.Clamp(next, 0, n - 1);
        }
        if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            ToggleCard(focusIndex);
        if (kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame)
        {
            if (GameModes.Selected.Count > 0) { GameModes.Clear(); Say("ALL MODES CLEARED", TextSub); Changed(); }
        }
    }

    private void HandleMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 mp = mouse.position.ReadValue();
        bool moved = (mp - lastMouse).sqrMagnitude > 1f;
        lastMouse = mp;

        int over = -1;
        for (int i = 0; i < view.Count; i++)
            if (RectTransformUtility.RectangleContainsScreenPoint(view[i].rt, mp, null)) { over = i; break; }
        if (over >= 0 && (moved || mouseFocus)) { focusIndex = over; mouseFocus = true; }

        if (!mouse.leftButton.wasPressedThisFrame || Time.frameCount == openedFrame) return;
        if (over >= 0) { ToggleCard(over); return; }
        for (int t = 0; t < tabs.Length; t++)
            if (RectTransformUtility.RectangleContainsScreenPoint(tabs[t].rt, mp, null)) { SetTab(t); return; }
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].badge.HasMode && RectTransformUtility.RectangleContainsScreenPoint(slots[i].rt, mp, null))
            {
                string dummy;
                GameModes.Toggle(slots[i].badge.Mode, out dummy);
                Say(GameModes.Info(slots[i].badge.Mode).Name + " OFF", TextSub);
                Changed();
                return;
            }
        }
        if (!RectTransformUtility.RectangleContainsScreenPoint(panel, mp, null)) Close();   // 창 바깥 클릭 = 닫기
    }

    private void ToggleCard(int index)
    {
        if (index < 0 || index >= view.Count) return;
        Card c = view[index];
        string replaced;
        var res = GameModes.Toggle(c.mode, out replaced);
        string name = GameModes.Info(c.mode).Name;
        switch (res)
        {
            case GameModes.ToggleResult.Full:
                Say("MAX " + GameModes.MaxSelected + " MODES  ·  TURN ONE OFF FIRST", WarnColor);
                c.pop = 1f;
                return;
            case GameModes.ToggleResult.Replaced:
                Say(replaced + "  >  " + name, TextSub);
                break;
            case GameModes.ToggleResult.Added:
                Say(name + " ON", TextSub);
                break;
            case GameModes.ToggleResult.Removed:
                Say(name + " OFF", TextSub);
                break;
        }
        c.pop = 1f;
        Changed();
    }

    // 분류 탭 바꾸기: 그 분류의 카드만 보이고, 커서는 첫 카드로
    private void SetTab(int t)
    {
        currentTab = t;
        view.Clear();
        foreach (var c in cards)
        {
            bool show = c.category == t;
            c.rt.gameObject.SetActive(show);
            if (show) view.Add(c);
        }
        focusIndex = 0;
    }

    private void Changed()
    {
        Refresh(false);
        if (OnChanged != null) OnChanged();
    }

    private void Say(string text, Color c)
    {
        message.text = text;
        message.color = c;
        msgT = 1f;
    }

    private void Refresh(bool first)
    {
        lastVersion = GameModes.Version;
        var sel = GameModes.Selected;
        for (int i = 0; i < slots.Length; i++)
        {
            Slot s = slots[i];
            if (i < sel.Count)
            {
                GameModeInfo info = GameModes.Info(sel[i]);
                s.badge.Set(sel[i]);
                s.name.text = info.Name;
                s.name.color = TextMain;
                s.ring.color = Color.Lerp(CardColor, new Color(info.Color.r, info.Color.g, info.Color.b, 1f), 0.14f);
            }
            else
            {
                s.badge.SetEmpty();
                s.name.text = "EMPTY";
                s.name.color = TextDim;
                s.ring.color = CardColor;
            }
        }
        double k = GameModes.MultiplierOf(sel);
        totalText.text = GameModes.Percent(k);
        totalText.color = k > 1.0001 ? TextMain : k < 0.9999 ? new Color(1f, 1f, 1f, 0.6f) : TextSub;
        capNote.text = GameModes.CapReached(sel) ? "MAX " + GameModes.Percent(GameModes.MaxMultiplier) : "";

        // 탭 이름 옆에 그 분류에서 켠 모드 수
        for (int t = 0; t < tabs.Length; t++)
        {
            int n = 0;
            foreach (var m in sel) if (GameModes.Info(m).Category == t) n++;
            tabs[t].label.text = GameModes.CategoryNames[t] + (n > 0 ? "  <color=#FFFFFF>" + n + "</color>" : "");
        }
    }

    // =====================================================================
    //  도우미
    // =====================================================================
    private static Image Round(Image img, float radius)
    {
        img.sprite = GameModes.BadgeSprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 16f / Mathf.Max(1f, radius);
        return img;
    }

    // 부모 왼쪽 위 기준 (x, y) 에서 (w, h)
    private static void Box(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static TextMeshProUGUI Txt(Transform parent, string text, float size, TextAlignmentOptions align, Color color, TMP_FontAsset font, FontStyles style)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt("T", parent, text, size, align, color, style);
        if (font != null) t.font = font;
        t.richText = true;
        t.raycastTarget = false;
        t.overflowMode = TextOverflowModes.Overflow;
        t.enableWordWrapping = false;
        return t;
    }
}