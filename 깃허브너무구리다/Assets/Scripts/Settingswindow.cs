using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using K = SettingsUIKit;

// ============================================================
// 설정창 (씬에 아무것도 배치할 필요 없음 — 코드로 화면을 만든다)
//  - SettingsWindow.Open() / Close() 로 열고 닫는다. 곡 선택창에서는 ESC.
//  - 탭: 사운드 / 타이밍 / 플레이 / 판정 표시 / 게임 화면 / 디스플레이 / 조작  (한국어)
//    · 탭 이름 아래 작은 글씨로 그 탭에 무엇이 있는지 표시, 탭 안은 구역(제목 + 가는 선)으로 나눔
//    · 켜고 끄는 항목은 스위치 모양 (오른쪽 = 켬), 고른 줄은 밝아지고 왼쪽에 흰 막대
//    · 오프셋은 '자동으로 맞추기'를 먼저, '직접 맞추기'를 아래에
//  - 글꼴: 강조(굵은) 글자 / 일반 글자를 따로 지정 → SetFonts(일반, 강조)
//          (곡 선택 인스펙터 Settings Font / Settings Bold Font, 또는 Resources/Fonts/SettingsFont · SettingsFontBold)
//  - 각 탭 내용은 스크롤됨 (마우스 휠 · 방향키로 고르면 고른 줄이 보이도록 자동 이동)
//  - 색: 검정 + 흰색 계열
//  - 조작: 마우스, 또는 방향키 + Enter. TAB(Shift+TAB) 탭 전환, ESC 닫기
//  - 씬이 바뀌어도 유지된다 (DontDestroyOnLoad). 처음 열 때 한 번만 만든다.
// ============================================================
public class SettingsWindow : MonoBehaviour
{
    public static SettingsWindow Instance { get; private set; }

    public static bool IsOpen { get { return Instance != null && Instance.root != null && Instance.root.activeSelf; } }
    public static bool IsCalibrating { get { return IsOpen && Instance.calibrator != null && Instance.calibrator.IsActive; } }
    // 설정창이 열려 있거나 '이번 프레임에 닫힌' 경우 게임 쪽 키 입력을 막는다 (ESC 로 닫자마자 다시 열리는 것 방지)
    public static bool BlocksGameInput { get { return IsOpen || Time.frameCount == lastClosedFrame; } }

    public static event Action Closed;

    private static int lastClosedFrame = -1;

    public static void Open() { Ensure().Show(); }

    // ===== 글꼴 (한글이 들어 있는 TMP 글꼴) =====
    //  regular: 일반 글자 (설명 · 값 · 안내),  bold: 강조 글자 (제목 · 항목 이름 · 버튼 · 탭)
    //  bold 를 따로 주면 그 글꼴로 그리고 가짜 굵게(Bold 스타일)는 끈다
    public static TMP_FontAsset RegularFont, BoldFont;
    public static void SetFonts(TMP_FontAsset regular, TMP_FontAsset bold)
    {
        if (regular != null) RegularFont = regular;
        if (bold != null) BoldFont = bold;
        if (Instance != null && Instance.root != null) Instance.ApplyFonts();
    }

    private void ApplyFonts()
    {
        if (RegularFont == null) RegularFont = Resources.Load<TMP_FontAsset>("Fonts/SettingsFont");
        if (BoldFont == null) BoldFont = Resources.Load<TMP_FontAsset>("Fonts/SettingsFontBold");
        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            bool bold = (t.fontStyle & FontStyles.Bold) != 0 || t.name == "BoldText";
            if (bold && BoldFont != null)
            {
                t.font = BoldFont;
                t.fontStyle &= ~FontStyles.Bold;
                t.name = "BoldText";            // 다시 적용할 때도 강조 글자로 알아보도록
            }
            else if (!bold && RegularFont != null) t.font = RegularFont;
            else if (bold && RegularFont != null) t.font = RegularFont;   // 강조 글꼴이 없으면 일반 글꼴 + 굵게
        }
    }

    // 처음 ESC 를 누를 때 화면을 만드느라 멈칫하지 않도록 미리 만들어 둔다 (숨긴 채)
    public static void Prewarm() { Ensure(); }
    public static void Close() { if (Instance != null) Instance.Hide(); }

    private static SettingsWindow Ensure()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("SettingsWindow");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SettingsWindow>();
            Instance.Build();
        }
        return Instance;
    }

    // ===== 내부 =====
    // 탭 순서 (인덱스는 아래 Build · 탭 초기화와 같음)
    private const int TabSound = 0, TabTiming = 1, TabPlay = 2, TabJudge = 3, TabVisual = 4, TabDisplay = 5, TabControls = 6;
    private static readonly string[] TabNames = { "사운드", "타이밍", "플레이", "판정 표시", "게임 화면", "디스플레이", "조작" };
    private static readonly string[] TabSubs = { "볼륨 · 탭음", "오프셋 맞추기", "배속 · 연습", "판정 글자 · 그래프", "레인 · 노트 · 이펙트", "해상도 · 프레임", "레인 키" };
    private static readonly string[] TabHints =
    {
        "볼륨은 바로 적용됩니다.",
        "노트가 음악과 안 맞으면 먼저 '소리 테스트'를 해 보세요. + = 노트가 빨라짐, - = 늦어짐 (osu! 와 같은 방향).",
        "곡 선택에서 F1 / F2 로도 배속을 바꿀 수 있습니다.",
        "판정 글자 · 그래프 표시 방법입니다. 판정 그래프는 다음 플레이부터 적용됩니다.",
        "게임 화면 디자인은 다음 플레이부터 적용됩니다.",
        "화면 모드와 해상도는 빌드된 게임에서만 적용됩니다 (에디터에서는 적용 안 됨).",
        "레인을 누른 뒤 원하는 키를 누르세요. ESC 는 취소."
    };
    private static readonly string[] ScreenModeNames = { "전체 화면", "테두리 없는 창", "창 모드" };

    private GameObject root;
    private RectTransform panel;
    private readonly List<Button> tabButtons = new List<Button>();
    private readonly List<TextMeshProUGUI> tabLabels = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> tabSubLabels = new List<TextMeshProUGUI>();
    private readonly List<Image> tabBars = new List<Image>();                  // 고른 탭 왼쪽의 흰 막대
    // 줄(Row) 강조: 고른 버튼 · 슬라이더가 들어 있는 줄을 밝게 + 왼쪽 흰 막대
    private readonly Dictionary<Transform, Image> rowBgs = new Dictionary<Transform, Image>();
    private readonly Dictionary<Transform, Image> rowMarks = new Dictionary<Transform, Image>();
    private Transform highlightedRow;
    private readonly List<GameObject> pages = new List<GameObject>();
    private readonly List<ScrollRect> pageScrolls = new List<ScrollRect>();      // 탭마다 스크롤
    private readonly List<RectTransform> pageContents = new List<RectTransform>();
    private GameObject lastAutoScrollTarget;
    private readonly List<Action> refreshers = new List<Action>();
    private int currentTab;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI resetText;
    private float resetArmedUntil = -1f;
    private OffsetCalibrator calibrator;
    private GameObject createdEventSystem;
    private int openedFrame;
    private bool prevCursorVisible;
    private CursorLockMode prevCursorLock;

    private AudioSource sfxPreview;
    private AudioClip hitClip;
    private float lastHitPreview;

    private GameObject pendingSelect;
    private int pendingSelectFrame = -1;

    private int rebindLane = -1;
    private int rebindArmFrame;
    private readonly Button[] laneButtons = new Button[4];

    private List<Vector2Int> resolutions;

    private SettingsData S { get { return SettingsStore.Current; } }

    // =====================================================================
    //  열기 / 닫기
    // =====================================================================
    private void Show()
    {
        if (IsOpen) return;
        EnsureEventSystem();
        root.SetActive(true);
        openedFrame = Time.frameCount;

        prevCursorVisible = Cursor.visible;
        prevCursorLock = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        resolutions = null;
        RefreshAll();
        SelectTab(currentTab);
        Select(tabButtons[currentTab].gameObject);
    }

    private void Hide()
    {
        if (!IsOpen) return;
        CancelRebind();
        if (calibrator != null) calibrator.Cancel();
        root.SetActive(false);
        lastClosedFrame = Time.frameCount;

        Cursor.visible = prevCursorVisible;
        Cursor.lockState = prevCursorLock;

        SettingsStore.SaveIfDirty();
        if (createdEventSystem != null) { Destroy(createdEventSystem); createdEventSystem = null; }
        if (Closed != null) Closed();
    }

    // 씬에 EventSystem 이 없으면(곡 선택 씬 등) 잠깐 만들어 쓰고 닫을 때 지운다
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindFirstObjectByType<EventSystem>() != null) return;

        createdEventSystem = new GameObject("SettingsEventSystem");
        createdEventSystem.transform.SetParent(transform, false);
        createdEventSystem.AddComponent<EventSystem>();
        InputSystemUIInputModule module = createdEventSystem.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    private void Select(GameObject go)
    {
        pendingSelect = null;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(go);
    }

    public void FocusCurrentTab()
    {
        if (tabButtons.Count > 0) SelectNextFrame(tabButtons[currentTab].gameObject);
    }

    // Enter/Space 로 버튼을 누른 같은 프레임에 다른 버튼을 선택하면 그 버튼까지 눌리므로 다음 프레임에 선택
    public void SelectNextFrame(GameObject go)
    {
        pendingSelect = go;
        pendingSelectFrame = Time.frameCount + 1;
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    void Update()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (pendingSelect != null && Time.frameCount >= pendingSelectFrame) Select(pendingSelect);

        if (resetArmedUntil > 0f && Time.unscaledTime > resetArmedUntil)
        {
            resetArmedUntil = -1f;
            if (resetText != null) resetText.text = "탭 초기화";
        }

        if (rebindLane >= 0) { UpdateRebind(kb); return; }
        if (Time.frameCount == openedFrame) return; // 여는 데 쓴 ESC 를 무시

        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (calibrator != null && calibrator.HandleEscape()) return;
            Hide();
            return;
        }

        if (calibrator != null && calibrator.IsActive) return;

        AutoScrollToSelected();
        UpdateRowHighlight();

        if (kb.tabKey.wasPressedThisFrame)
        {
            bool back = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            SelectTab((currentTab + (back ? -1 : 1) + TabNames.Length) % TabNames.Length);
            Select(tabButtons[currentTab].gameObject);
        }
    }

    // =====================================================================
    //  화면 만들기
    // =====================================================================
    private void Build()
    {
        K.EnsureFont();

        GameObject canvasGo = new GameObject("SettingsCanvas", typeof(RectTransform));
        canvasGo.layer = 5;
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        root = canvasGo;

        Image dim = K.Img("Dim", canvasGo.transform, new Color(0f, 0f, 0f, 0.62f), true); // 뒤쪽 클릭 차단
        K.Stretch(dim.rectTransform);

        panel = K.Img("Panel", canvasGo.transform, K.PanelColor, true).rectTransform;
        K.Center(panel, Vector2.zero, new Vector2(1320f, 840f));

        Image topLine = K.Img("TopLine", panel, K.Accent);
        K.Place(topLine.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero);

        TextMeshProUGUI title = K.Txt("Title", panel, "설정", 44, TextAlignmentOptions.Left, K.TextMain, FontStyles.Bold);
        K.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(40f, -90f), new Vector2(0f, -24f));

        // 조작 안내 (오른쪽 위, 키캡 디자인) — 글꼴은 아래 ApplyFonts 에서 설정창 글꼴로 바뀜
        RectTransform keysHint = KeyHintUI.Build(panel, K.Font, K.Font, new[]
        {
            new KeyHintUI.Item("다음 탭", "TAB"),
            new KeyHintUI.Item("선택", "^UP", "^DOWN"),
            new KeyHintUI.Item("닫기", "ESC"),
        }, 17f, 1);
        keysHint.anchorMin = keysHint.anchorMax = new Vector2(1f, 1f);
        keysHint.anchoredPosition = new Vector2(-40f, -57f);

        // 왼쪽 탭 목록: 이름(굵게) + 아래 작은 설명, 고른 탭은 옅게 밝아지고 왼쪽에 흰 막대
        RectTransform tabs = K.Rect("Tabs", panel);
        K.Place(tabs, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(32f, 110f), new Vector2(272f, -110f));
        Image tabLine = K.Img("TabDivider", panel, new Color(1f, 1f, 1f, 0.08f));   // 탭 목록과 내용 사이 세로선
        K.Place(tabLine.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(285f, 110f), new Vector2(286f, -110f));
        const float TabH = 70f, TabGap = 6f;
        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;
            TextMeshProUGUI label;
            Button b = K.Btn(tabs, TabNames[i], 23, () => SelectTab(index), out label);
            K.Rounded((Image)b.targetGraphic, 10f);
            K.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -(i * (TabH + TabGap) + TabH)), new Vector2(0f, -(i * (TabH + TabGap))));
            label.alignment = TextAlignmentOptions.BottomLeft;
            K.Place(label.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(26f, 0f), new Vector2(-10f, -6f));
            TextMeshProUGUI sub = K.Txt("Sub", b.transform, TabSubs[i], 14, TextAlignmentOptions.TopLeft, K.TextSub);
            K.Place(sub.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(26f, 6f), new Vector2(-10f, -3f));
            Image bar = K.Img("Bar", b.transform, Color.white);
            K.Place(bar.rectTransform, new Vector2(0f, 0.22f), new Vector2(0f, 0.78f), new Vector2(8f, 0f), new Vector2(12f, 0f));
            tabButtons.Add(b);
            tabLabels.Add(label);
            tabSubLabels.Add(sub);
            tabBars.Add(bar);
        }

        // 오른쪽 내용
        RectTransform content = K.Rect("Content", panel);
        K.Place(content, Vector2.zero, Vector2.one, new Vector2(310f, 110f), new Vector2(-40f, -110f));
        for (int i = 0; i < TabNames.Length; i++) BuildScrollPage(content, i);
        BuildSoundPage(pageContents[TabSound]);
        BuildOffsetPage(pageContents[TabTiming]);
        BuildPlayPage(pageContents[TabPlay]);
        BuildJudgePage(pageContents[TabJudge]);
        BuildVisualPage(pageContents[TabVisual]);
        BuildDisplayPage(pageContents[TabDisplay]);
        BuildControlsPage(pageContents[TabControls]);

        // 아래쪽
        statusText = K.Txt("Status", panel, "", 17, TextAlignmentOptions.Left, K.TextSub, FontStyles.Normal, true);
        K.Place(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(310f, 28f), new Vector2(-420f, 90f));

        Button reset = K.Btn(panel, "탭 초기화", 22, OnResetClicked, out resetText);
        K.Rounded((Image)reset.targetGraphic, 10f);
        K.Place((RectTransform)reset.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-400f, 32f), new Vector2(-226f, 86f));
        TextMeshProUGUI closeText;
        Button close = K.Btn(panel, "닫기", 22, Hide, out closeText);
        K.Place((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-210f, 32f), new Vector2(-40f, 86f));
        K.Rounded((Image)close.targetGraphic, 10f);
        K.SetButtonColor(close, K.AccentDark);

        // 오프셋 측정 화면 (패널 위를 덮음)
        calibrator = gameObject.AddComponent<OffsetCalibrator>();
        calibrator.Build(panel, this);

        sfxPreview = gameObject.AddComponent<AudioSource>();
        sfxPreview.playOnAwake = false;
        hitClip = Resources.Load<AudioClip>("Audio/etc/hitsound");

        ApplyFonts();
        root.SetActive(false);
    }

    // =====================================================================
    //  스크롤 되는 탭 내용
    //   [페이지] ─ [보이는 영역(잘라내기)] ─ [내용(세로로 쌓임, 길이 자동)]   + 오른쪽 가는 스크롤 막대
    // =====================================================================
    private void BuildScrollPage(RectTransform content, int index)
    {
        RectTransform page = K.Rect("Page_" + index, content);
        K.Stretch(page);
        ScrollRect sr = page.gameObject.AddComponent<ScrollRect>();

        RectTransform viewport = K.Rect("Viewport", page);
        K.Place(viewport, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-18f, 0f));
        viewport.gameObject.AddComponent<RectMask2D>();                        // 영역 밖은 가려짐
        Image hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);                                 // 빈 곳에서도 휠이 먹도록
        hit.raycastTarget = true;

        RectTransform inner = K.Rect("Inner", viewport);
        inner.anchorMin = new Vector2(0f, 1f);
        inner.anchorMax = new Vector2(1f, 1f);
        inner.pivot = new Vector2(0.5f, 1f);
        inner.offsetMin = Vector2.zero;
        inner.offsetMax = Vector2.zero;
        VerticalLayoutGroup v = inner.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 12f;
        v.padding = new RectOffset(0, 0, 0, 12);
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        ContentSizeFitter fit = inner.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 스크롤 막대 (내용이 넘칠 때만 보임)
        RectTransform barRt = K.Rect("Scrollbar", page);
        K.Place(barRt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-6f, 0f), Vector2.zero);
        Image barBg = barRt.gameObject.AddComponent<Image>();
        barBg.color = new Color(1f, 1f, 1f, 0.06f);
        Scrollbar bar = barRt.gameObject.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        RectTransform slide = K.Rect("Sliding Area", barRt);
        K.Stretch(slide);
        Image handle = K.Img("Handle", slide, new Color(1f, 1f, 1f, 0.55f), true);
        handle.rectTransform.sizeDelta = Vector2.zero;
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        Navigation nav = bar.navigation; nav.mode = Navigation.Mode.None; bar.navigation = nav;   // 방향키 선택에서 제외

        sr.viewport = viewport;
        sr.content = inner;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.inertia = true;
        sr.decelerationRate = 0.12f;
        sr.scrollSensitivity = 45f;
        sr.verticalScrollbar = bar;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        pages.Add(page.gameObject);
        pageScrolls.Add(sr);
        pageContents.Add(inner);
    }

    // 방향키로 고른 버튼 · 슬라이더가 보이는 영역 밖이면 그 줄이 보이도록 스크롤
    private void AutoScrollToSelected()
    {
        if (EventSystem.current == null || currentTab >= pageScrolls.Count) return;
        GameObject sel = EventSystem.current.currentSelectedGameObject;
        if (sel == lastAutoScrollTarget) return;
        lastAutoScrollTarget = sel;
        if (sel == null) return;
        RectTransform inner = pageContents[currentTab];
        if (!sel.transform.IsChildOf(inner)) return;

        // 고른 것을 품고 있는 줄(내용의 바로 아래 자식)
        Transform row = sel.transform;
        while (row.parent != null && row.parent != inner) row = row.parent;
        RectTransform target = row as RectTransform;
        if (target == null) return;

        ScrollRect sr = pageScrolls[currentTab];
        RectTransform viewport = sr.viewport;
        Canvas.ForceUpdateCanvases();
        Vector3[] c = new Vector3[4];
        target.GetWorldCorners(c);
        Vector3 min = viewport.InverseTransformPoint(c[0]), max = viewport.InverseTransformPoint(c[2]);
        Rect vr = viewport.rect;
        float dy = 0f;
        if (max.y > vr.yMax) dy = max.y - vr.yMax + 8f;          // 위로 벗어남 → 내용을 아래로
        else if (min.y < vr.yMin) dy = min.y - vr.yMin - 8f;     // 아래로 벗어남 → 내용을 위로
        if (Mathf.Abs(dy) < 0.5f) return;
        float maxY = Mathf.Max(0f, inner.rect.height - vr.height);
        Vector2 p = inner.anchoredPosition;
        p.y = Mathf.Clamp(p.y - dy, 0f, maxY);
        inner.anchoredPosition = p;
        sr.velocity = Vector2.zero;
    }

    private void SelectTab(int index)
    {
        if (calibrator != null && calibrator.IsActive) return;
        CancelRebind();
        currentTab = Mathf.Clamp(index, 0, TabNames.Length - 1);
        for (int i = 0; i < pages.Count; i++)
        {
            bool on = i == currentTab;
            pages[i].SetActive(on);
            K.SetButtonColor(tabButtons[i], on ? new Color(1f, 1f, 1f, 0.11f) : new Color(1f, 1f, 1f, 0f));
            tabLabels[i].color = on ? K.TextMain : K.TextSub;
            if (i < tabSubLabels.Count) tabSubLabels[i].color = on ? new Color(1f, 1f, 1f, 0.62f) : K.TextDim;
            if (i < tabBars.Count) tabBars[i].enabled = on;
        }
        resetArmedUntil = -1f;
        if (resetText != null) resetText.text = "탭 초기화";
        SetStatus(TabHints[currentTab]);
        if (currentTab < pageScrolls.Count) { pageScrolls[currentTab].verticalNormalizedPosition = 1f; pageScrolls[currentTab].velocity = Vector2.zero; }
    }

    private void SetStatus(string msg) { if (statusText != null) statusText.text = msg; }

    private void RefreshAll()
    {
        foreach (var r in refreshers) r();
    }

    private void Changed()
    {
        SettingsStore.MarkDirty();
        RefreshAll();
    }

    // =====================================================================
    //  탭별 내용
    // =====================================================================
    private void BuildSoundPage(Transform page)
    {
        Section(page, "볼륨");
        SliderRow(page, "전체 볼륨", "모든 소리",
            () => S.MasterVolume * 100f,
            v => { S.MasterVolume = v / 100f; SettingsStore.ApplyAudio(); SettingsStore.MarkDirty(); },
            v => Mathf.RoundToInt(v) + "%");

        SliderRow(page, "음악 볼륨", "게임 음악 · 곡 선택 미리듣기",
            () => S.MusicVolume * 100f,
            v => { S.MusicVolume = v / 100f; SettingsStore.MarkDirty(); },
            v => Mathf.RoundToInt(v) + "%");

        Section(page, "탭음");
        SwitchRow(page, "탭음", "레인 키를 누를 때 나는 소리",
            () => S.HitSoundOn,
            on => { S.HitSoundOn = on; Changed(); if (on) PreviewHitSound(true); });

        SliderRow(page, "탭음 볼륨", "바꾸면 미리 들려줌",
            () => S.HitSoundVolume * 100f,
            v => { S.HitSoundVolume = v / 100f; SettingsStore.MarkDirty(); PreviewHitSound(false); },
            v => Mathf.RoundToInt(v) + "%");
    }

    private void BuildOffsetPage(Transform page)
    {
        // 처음 맞추는 사람이 위에서부터 하면 되도록: 자동 측정 → 마지막 플레이로 보정 → (필요하면) 직접 맞추기
        Section(page, "실제 채보로 맞추기");
        {
            // GAME SETTING (5_Test): 실제 채보가 계속 반복되는 곳에서 노트 속도 · 판정 · 표시를 바로 바꿔 보며 맞춤
            RectTransform ga = Row(page, "GAME SETTING", "곡 선택창에서만 · 곡 선택창에서 T 키로도 열림", 84f);
            TextMeshProUGUI gt;
            Button open = K.Btn(ga, "열기", 22, () =>
            {
                if (CoreManager.Instance == null || GameManager.Instance != null) { SetStatus("곡 선택창에서만 열 수 있습니다."); return; }
                Hide();
                SongSelectManager ssm = FindFirstObjectByType<SongSelectManager>();
                if (ssm != null) ssm.OpenGameSetting(); else CoreManager.Instance.LoadTestScene();
            }, out gt);
            K.Rounded((Image)open.targetGraphic, 8f);
            K.Place((RectTransform)open.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-180f, 0f), Vector2.zero);
            refreshers.Add(() => open.interactable = GameManager.Instance == null);
        }

        Section(page, "자동으로 맞추기");
        RectTransform area = Row(page, "박자 테스트", "① 소리 테스트 → ② 화면 테스트 순서로", 84f);
        TextMeshProUGUI t;
        Button sound = K.Btn(area, "① 소리 테스트", 22, () => calibrator.Begin(OffsetCalibrator.Mode.Sound), out t);
        K.Rounded((Image)sound.targetGraphic, 8f);
        K.Place((RectTransform)sound.transform, new Vector2(0f, 0f), new Vector2(0.49f, 1f), Vector2.zero, Vector2.zero);
        Button visual = K.Btn(area, "② 화면 테스트", 22, () => calibrator.Begin(OffsetCalibrator.Mode.Visual), out t);
        K.Rounded((Image)visual.targetGraphic, 8f);
        K.Place((RectTransform)visual.transform, new Vector2(0.51f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

        // 마지막 플레이 결과로 보정
        TextMeshProUGUI lastInfo = null;
        RectTransform lastArea = Row(page, "마지막 플레이로 보정", null, 84f, d => lastInfo = d);
        TextMeshProUGUI applyText;
        Button apply = K.Btn(lastArea, "적용", 22, ApplyLastPlay, out applyText);
        K.Rounded((Image)apply.targetGraphic, 8f);
        K.Place((RectTransform)apply.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-180f, 0f), Vector2.zero);
        TextMeshProUGUI lastValue = K.Txt("Value", lastArea, "", 24, TextAlignmentOptions.Left, K.TextMain, FontStyles.Bold);
        K.Place(lastValue.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-196f, 0f));
        refreshers.Add(() =>
        {
            ResultData r = GetUsableLastResult();
            apply.interactable = r != null;
            if (r == null)
            {
                lastValue.text = "-";
                if (lastInfo != null) lastInfo.text = "곡을 한 번 플레이하면 (AUTO 제외) 쓸 수 있습니다";
            }
            else
            {
                lastValue.text = K.Ms(r.AverageErrorMs / 1000.0) + (r.AverageErrorMs > 0 ? "  SLOW" : r.AverageErrorMs < 0 ? "  FAST" : "");
                lastValue.color = r.AverageErrorMs > 0 ? K.SlowColor : r.AverageErrorMs < 0 ? K.FastColor : K.TextMain;
                if (lastInfo != null) lastInfo.text = "노트 " + r.ErrorSampleCount + "개 평균 오차 → 판정 오프셋 " + K.Ms(-r.AverageErrorMs / 1000.0) + " 만큼 바꿈";
            }
        });

        Section(page, "직접 맞추기");
        StepperRow(page, "오디오 오프셋", "음악이 늦게 들리면 - (블루투스 · TV)",
            new[] { "-10", "-1" }, new[] { -10, -1 }, new[] { "+1", "+10" }, new[] { 1, 10 },
            () => K.Ms(S.AudioOffset),
            d => { S.AudioOffset = Clamp(Math.Round(S.AudioOffset * 1000.0 + d) / 1000.0, -1.0, 1.0); Changed(); });

        StepperRow(page, "판정 오프셋", "SLOW 가 많으면 -,  FAST 가 많으면 +",
            new[] { "-10", "-1" }, new[] { -10, -1 }, new[] { "+1", "+10" }, new[] { 1, 10 },
            () => K.Ms(S.JudgeOffset),
            d => { S.JudgeOffset = Clamp(Math.Round(S.JudgeOffset * 1000.0 + d) / 1000.0, -0.3, 0.3); Changed(); });

        InfoText(page,
            "+ 는 노트(와 판정)를 빠르게, - 는 늦게 합니다 — osu! 와 같은 방향입니다.\n" +
            "오디오 오프셋은 음악에 대해 노트 + 판정을 함께 옮기고, 판정 오프셋은 판정만 옮깁니다.", 70f);
    }

    private void BuildPlayPage(Transform page)
    {
        Section(page, "노트 속도");
        StepperRow(page, "배속", "노트가 내려오는 속도 (곡 선택에서 F1 / F2)",
            new[] { "-1", "-0.1" }, new[] { -10, -1 }, new[] { "+0.1", "+1" }, new[] { 1, 10 },
            () => S.SettingSpeed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            d => { S.SettingSpeed = Clamp(Math.Round(S.SettingSpeed * 10.0 + d) / 10.0, 1.0, 12.0); Changed(); });

        Section(page, "연습");
        // 시작 타임 조정: 0.1초 단위. 값 = 음악 위치(초). 0 = OFF (처음부터)
        StepperRow(page, "시작 시간", "곡을 이 시간부터 시작 (기록은 저장되지 않음)",
            new[] { "-10", "-1", "-0.1" }, new[] { -100, -10, -1 }, new[] { "+0.1", "+1", "+10" }, new[] { 1, 10, 100 },
            () => S.StartTime <= 0.0 ? "끔" : S.StartTime.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " 초",
            d => { S.StartTime = Clamp(Math.Round(S.StartTime * 10.0 + d) / 10.0, 0.0, 3600.0); Changed(); },
            56f);   // 버튼이 양쪽 3개씩이라 좁게 (가운데 값 칸이 보이도록)
    }

    private void BuildJudgePage(Transform page)
    {
        Section(page, "판정 글자");
        SwitchRow(page, "JUST 표시", "±20ms 안의 PERFECT 를 JUST 로 표시 (흰 글자 + 빛번짐, 끄면 PERFECT)",
            () => S.ShowJustPerfect, on => { S.ShowJustPerfect = on; Changed(); });

        SwitchRow(page, "FAST / SLOW 표시", "판정 아래 오차 표시 (빠르면 -ms, 늦으면 +ms)",
            () => S.ShowFastSlow, on => { S.ShowFastSlow = on; Changed(); });

        StepperRow(page, "FAST / SLOW 기준", "오차가 이 값 이상일 때만 표시",
            new[] { "-5", "-1" }, new[] { -5, -1 }, new[] { "+1", "+5" }, new[] { 1, 5 },
            () => S.judgeoverrange + " ms",
            d => { S.judgeoverrange = Mathf.Clamp(S.judgeoverrange + d, 0, 120); Changed(); });

        Section(page, "그래프");
        SwitchRow(page, "판정 그래프", "화면 아래에 칠 때마다 오차를 눈금으로 표시 (osu! 처럼)",
            () => S.ShowHitErrorBar, on => { S.ShowHitErrorBar = on; Changed(); });
    }

    private void BuildVisualPage(Transform page)
    {
        Section(page, "디자인");
        ChoiceRow(page, "게임 화면", "레인 · 노트 · HUD 디자인",
            () => GameSkins.Names[Mathf.Clamp(S.GameSkin, 0, GameSkins.Names.Length - 1)],
            d => { S.GameSkin = (S.GameSkin + d + GameSkins.Names.Length) % GameSkins.Names.Length; Changed(); });

        ChoiceRow(page, "히트 이펙트", "낮음 = 가벼운 이펙트 (렉이 있으면 낮음으로)",
            () => S.EffectQuality > 0 ? "높음" : "낮음",
            d => { S.EffectQuality = S.EffectQuality > 0 ? 0 : 1; Changed(); });

        Section(page, "레인  (SIMPLE)");
        SliderRow(page, "레인 불투명도", "레인 배경 (0% = 완전 투명)",
            () => S.LaneOpacity * 100f,
            v => { S.LaneOpacity = v / 100f; SettingsStore.MarkDirty(); },
            v => Mathf.RoundToInt(v) + "%");

        SwitchRow(page, "레인 구분선", "레인 사이 구분선 3개",
            () => S.LaneLines, on => { S.LaneLines = on; Changed(); });

        SwitchRow(page, "키 자리 표시", "판정선 위 노트 모양 표시",
            () => S.ShowKeyMarkers, on => { S.ShowKeyMarkers = on; Changed(); });

        Section(page, "정보");
        SwitchRow(page, "정보 표시", "곡 정보 · 난이도 · 시간 · 점수 · 배속 (모든 게임 화면)",
            () => S.ShowInfoUI, on => { S.ShowInfoUI = on; Changed(); });
    }

    private void BuildDisplayPage(Transform page)
    {
        Section(page, "창");
        ChoiceRow(page, "화면 모드", "전체 화면 · 테두리 없는 창 · 창 모드",
            () => ScreenModeNames[CurrentScreenMode()],
            d =>
            {
                S.ScreenMode = (CurrentScreenMode() + d + 3) % 3;
                SettingsStore.ApplyScreen();
                Changed();
                if (Application.isEditor) SetStatus("화면 모드는 빌드된 게임에서만 적용됩니다.");
            });

        ChoiceRow(page, "해상도", null,
            () => { Vector2Int r = CurrentResolution(); return r.x + " x " + r.y; },
            d =>
            {
                var list = GetResolutions();
                if (list.Count == 0) return;
                int idx = list.IndexOf(CurrentResolution());
                if (idx < 0) idx = list.Count - 1;
                idx = Mathf.Clamp(idx + d, 0, list.Count - 1);
                S.ResolutionWidth = list[idx].x;
                S.ResolutionHeight = list[idx].y;
                SettingsStore.ApplyScreen();
                Changed();
                if (Application.isEditor) SetStatus("해상도는 빌드된 게임에서만 적용됩니다.");
            });

        Section(page, "프레임");
        SwitchRow(page, "수직 동기화", "모니터 주사율에 맞춤 (프레임 제한 무시)",
            () => S.VSync, on => { S.VSync = on; SettingsStore.ApplyFrameRate(); Changed(); });

        ChoiceRow(page, "프레임 제한", "높을수록 입력 타이밍이 정확",
            () =>
            {
                string v = S.FrameLimit > 0 ? S.FrameLimit + " FPS" : "제한 없음";
                return S.VSync ? v + "  (수직 동기화)" : v;
            },
            d =>
            {
                int[] opts = SettingsStore.FrameLimitOptions;
                int idx = Array.IndexOf(opts, S.FrameLimit);
                if (idx < 0) idx = opts.Length - 1;
                S.FrameLimit = opts[Mathf.Clamp(idx + d, 0, opts.Length - 1)];
                SettingsStore.ApplyFrameRate();
                Changed();
            });

        SwitchRow(page, "FPS 표시", "오른쪽 위에 프레임 수 표시",
            () => S.ShowFps, on => { S.ShowFps = on; FpsOverlay.SetVisible(on); Changed(); });
    }

    private void BuildControlsPage(Transform page)
    {
        Section(page, "레인 키");
        RectTransform area = Row(page, "레인 키", "누르고 바꿀 키 입력 (1 · 2 · 3 · 4 번 레인)", 104f);
        for (int i = 0; i < 4; i++)
        {
            int lane = i;
            TextMeshProUGUI label;
            Button b = K.Btn(area, "", 34, () => BeginRebind(lane), out label);
            K.Rounded((Image)b.targetGraphic, 10f);
            K.Place((RectTransform)b.transform, new Vector2(i / 4f, 0f), new Vector2((i + 1) / 4f, 1f), new Vector2(5f, 0f), new Vector2(-5f, 0f));
            laneButtons[i] = b;
            refreshers.Add(() =>
            {
                Key[] keys = SettingsStore.GetLaneKeys();
                bool waiting = rebindLane == lane;
                label.text = waiting ? "키 입력..." : SettingsStore.KeyDisplayName(keys[lane]);
                label.fontSize = waiting ? 22 : 34;
                label.color = waiting ? K.TextOnAccent : K.TextMain;
                K.SetButtonColor(b, waiting ? K.Accent : K.ButtonColor);
            });
        }

        RectTransform resetArea = Row(page, "기본 키", null, 76f);
        TextMeshProUGUI t;
        Button reset = K.Btn(resetArea, "D  F  J  K 로 되돌리기", 20, () =>
        {
            CancelRebind();
            S.LaneKeys = (string[])SettingsStore.DefaultLaneKeys.Clone();
            Changed();
            SetStatus("레인 키를 D F J K 로 되돌렸습니다.");
        }, out t);
        K.Rounded((Image)reset.targetGraphic, 8f);
        K.Place((RectTransform)reset.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-260f, 0f), Vector2.zero);

        InfoText(page, "SPACE 와 ESC(일시정지)는 고정 키라 레인 키로 쓸 수 없습니다.\n" +
                       "다른 레인이 이미 쓰는 키를 고르면 두 레인의 키가 서로 바뀝니다.", 80f);
    }

    // =====================================================================
    //  행(Row) 부품
    // =====================================================================
    // 왼쪽: 이름 + 설명, 오른쪽: 조작 영역(반환)
    private RectTransform Row(Transform page, string label, string desc, float height = 76f, Action<TextMeshProUGUI> descOut = null)
    {
        Image bg = K.Img("Row_" + label, page, K.RowColor);
        K.Rounded(bg, 10f);
        K.Fixed(bg.gameObject, height);
        RectTransform r = bg.rectTransform;
        // 고른 줄 표시: 왼쪽 흰 막대 (UpdateRowHighlight 에서 켜고 끔)
        Image mark = K.Img("Mark", r, Color.white);
        K.Place(mark.rectTransform, new Vector2(0f, 0.2f), new Vector2(0f, 0.8f), new Vector2(0f, 0f), new Vector2(3f, 0f));
        mark.enabled = false;
        rowBgs[r] = bg;
        rowMarks[r] = mark;

        bool hasDesc = desc != null || descOut != null;
        TextMeshProUGUI l = K.Txt("Label", r, label, 24, hasDesc ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.Left, K.TextMain, FontStyles.Bold);
        if (hasDesc)
        {
            K.Place(l.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.42f, 1f), new Vector2(24f, 0f), new Vector2(0f, -8f));
            TextMeshProUGUI d = K.Txt("Desc", r, desc ?? "", 16, TextAlignmentOptions.TopLeft, K.TextSub, FontStyles.Normal, true);
            K.Place(d.rectTransform, new Vector2(0f, 0f), new Vector2(0.42f, 0.5f), new Vector2(24f, 4f), new Vector2(0f, -4f));
            if (descOut != null) descOut(d);
        }
        else
        {
            K.Place(l.rectTransform, Vector2.zero, new Vector2(0.42f, 1f), new Vector2(24f, 0f), Vector2.zero);
        }

        RectTransform control = K.Rect("Control", r);
        K.Place(control, new Vector2(0.44f, 0f), Vector2.one, new Vector2(0f, 12f), new Vector2(-20f, -12f));
        return control;
    }

    private void SliderRow(Transform page, string label, string desc, Func<float> get, Action<float> set, Func<float, string> fmt)
    {
        RectTransform area = Row(page, label, desc);
        Slider s = K.MakeSlider(area, 0f, 100f, true);
        K.Place((RectTransform)s.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-110f, 0f));
        TextMeshProUGUI value = K.Txt("Value", area, "", 26, TextAlignmentOptions.Right, K.TextMain, FontStyles.Bold);
        K.Place(value.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-100f, 0f), Vector2.zero);

        s.onValueChanged.AddListener(v => { set(v); value.text = fmt(v); });
        refreshers.Add(() => { s.SetValueWithoutNotify(get()); value.text = fmt(s.value); });
    }

    // 스위치:  [ 켬  (●━━) ]  — 켜지면 흰 트랙 + 오른쪽 검은 손잡이, 꺼지면 어두운 트랙 + 왼쪽 회색 손잡이
    private void SwitchRow(Transform page, string label, string desc, Func<bool> get, Action<bool> set)
    {
        RectTransform area = Row(page, label, desc);
        TextMeshProUGUI t;
        Button b = K.Btn(area, "", 22, () => set(!get()), out t);
        K.Rounded((Image)b.targetGraphic, 8f);
        K.SetButtonColor(b, new Color(1f, 1f, 1f, 0f));          // 버튼 바탕은 투명 (고르면 옅게 밝아짐)
        K.Place((RectTransform)b.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-180f, 0f), Vector2.zero);
        t.alignment = TextAlignmentOptions.Right;
        K.Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(-92f, 0f));

        const float TW = 64f, TH = 32f, KN = 24f;
        Image track = K.Img("Track", b.transform, Color.white);
        K.Rounded(track, TH * 0.5f);
        RectTransform tr = track.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(1f, 0.5f);
        tr.pivot = new Vector2(1f, 0.5f);
        tr.sizeDelta = new Vector2(TW, TH);
        tr.anchoredPosition = new Vector2(-12f, 0f);
        Image knob = K.Img("Knob", tr, Color.white);
        K.Rounded(knob, KN * 0.5f);
        RectTransform kr = knob.rectTransform;
        kr.anchorMin = kr.anchorMax = new Vector2(0f, 0.5f);
        kr.pivot = new Vector2(0.5f, 0.5f);
        kr.sizeDelta = new Vector2(KN, KN);

        refreshers.Add(() =>
        {
            bool on = get();
            t.text = on ? "켬" : "끔";
            t.color = on ? K.TextMain : K.TextSub;
            track.color = on ? Color.white : new Color(1f, 1f, 1f, 0.16f);
            knob.color = on ? new Color(0.06f, 0.06f, 0.06f, 1f) : new Color(0.62f, 0.62f, 0.62f, 1f);
            float pad = (TH - KN) * 0.5f;
            kr.anchoredPosition = new Vector2(on ? TW - pad - KN * 0.5f : pad + KN * 0.5f, 0f);
        });
    }

    // [-10][-1]  값  [+1][+10]
    private void StepperRow(Transform page, string label, string desc,
                            string[] leftLabels, int[] leftDeltas, string[] rightLabels, int[] rightDeltas,
                            Func<string> getText, Action<int> onDelta, float bw = 76f)
    {
        RectTransform area = Row(page, label, desc);
        float gap = bw < 76f ? 6f : 8f;
        TextMeshProUGUI t;

        float x = 0f;
        for (int i = 0; i < leftLabels.Length; i++)
        {
            int d = leftDeltas[i];
            Button b = K.Btn(area, leftLabels[i], 20, () => onDelta(d), out t);
            K.Rounded((Image)b.targetGraphic, 8f);
            K.Place((RectTransform)b.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(x, 0f), new Vector2(x + bw, 0f));
            x += bw + gap;
        }
        float xr = 0f;
        for (int i = rightLabels.Length - 1; i >= 0; i--)
        {
            int d = rightDeltas[i];
            Button b = K.Btn(area, rightLabels[i], 20, () => onDelta(d), out t);
            K.Rounded((Image)b.targetGraphic, 8f);
            K.Place((RectTransform)b.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-(xr + bw), 0f), new Vector2(-xr, 0f));
            xr += bw + gap;
        }

        // 가운데 값 칸: 어두운 바탕 위에 값 (버튼과 구분되어 '지금 값'이 한눈에 보이게)
        Image box = K.Img("ValueBox", area, new Color(0f, 0f, 0f, 0.35f));
        K.Rounded(box, 8f);
        K.Place(box.rectTransform, Vector2.zero, Vector2.one, new Vector2(x, 0f), new Vector2(-xr, 0f));
        TextMeshProUGUI value = K.Txt("Value", box.rectTransform, "", 26, TextAlignmentOptions.Center, K.TextMain, FontStyles.Bold);
        K.Stretch(value.rectTransform);
        refreshers.Add(() => value.text = getText());
    }

    // [<]  값  [>]
    private void ChoiceRow(Transform page, string label, string desc, Func<string> getText, Action<int> onDelta)
    {
        StepperRow(page, label, desc, new[] { "<" }, new[] { -1 }, new[] { ">" }, new[] { 1 }, getText, onDelta);
    }

    // 구역 제목: 작은 굵은 글자 + 오른쪽으로 이어지는 가는 선 (첫 구역은 위 여백 없이)
    private void Section(Transform page, string title)
    {
        bool first = page.childCount == 0;
        RectTransform r = K.Rect("Section_" + title, page);
        K.Fixed(r.gameObject, first ? 30f : 46f);
        TextMeshProUGUI t = K.Txt("Title", r, title, 15, TextAlignmentOptions.BottomLeft, new Color(1f, 1f, 1f, 0.5f), FontStyles.Bold);
        t.characterSpacing = 4f;
        K.Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(6f, 4f), Vector2.zero);
        float w = t.GetPreferredValues(title).x;
        Image line = K.Img("Line", r, new Color(1f, 1f, 1f, 0.08f));
        K.Place(line.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(w + 22f, 12f), new Vector2(0f, 13f));
    }

    // 고른 버튼 · 슬라이더가 들어 있는 줄을 밝게 + 왼쪽 흰 막대 (어디를 고르고 있는지 한눈에)
    private void UpdateRowHighlight()
    {
        Transform row = null;
        GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel != null && currentTab < pageContents.Count && sel.transform.IsChildOf(pageContents[currentTab]))
        {
            Transform x = sel.transform;
            while (x != null && !rowBgs.ContainsKey(x)) x = x.parent;
            row = x;
        }
        if (row == highlightedRow) return;
        if (highlightedRow != null) { rowBgs[highlightedRow].color = K.RowColor; rowMarks[highlightedRow].enabled = false; }
        highlightedRow = row;
        if (row != null) { rowBgs[row].color = K.RowSelected; rowMarks[row].enabled = true; }
    }

    private void InfoText(Transform page, string text, float height)
    {
        TextMeshProUGUI t = K.Txt("Info", page, text, 17, TextAlignmentOptions.TopLeft, K.TextSub, FontStyles.Normal, true);
        K.Fixed(t.gameObject, height);
        t.margin = new Vector4(8f, 6f, 8f, 0f);
    }

    // =====================================================================
    //  사운드
    // =====================================================================
    private void PreviewHitSound(bool force)
    {
        if (hitClip == null || sfxPreview == null) return;
        if (!force && Time.unscaledTime - lastHitPreview < 0.12f) return;
        lastHitPreview = Time.unscaledTime;
        sfxPreview.PlayOneShot(hitClip, S.HitSoundVolume);
    }

    // =====================================================================
    //  오프셋
    // =====================================================================
    // 측정 결과를 적용했을 때의 새 값 (measured: + = 늦게 침)
    //  오프셋 부호: + = 노트·판정이 빨라짐 (osu! 방향) → 늦게 치는 만큼 '-' 로 맞춘다
    //  - SOUND  : 소리에 맞춰 친 평균 오차 M = 음악 지연 + 입력 지연
    //             → AUDIO + JUDGE 의 합이 -M 이 되도록 AUDIO 를 맞춘다
    //  - VISUAL : 화면에 맞춰 친 평균 오차 E = 화면 지연 + 입력 지연
    //             → JUDGE = -E, 합(AUDIO + JUDGE)은 그대로 유지
    public void ComputeCalibration(OffsetCalibrator.Mode mode, double measured, out double audio, out double judge)
    {
        if (mode == OffsetCalibrator.Mode.Sound)
        {
            judge = S.JudgeOffset;
            audio = -measured - judge;
        }
        else
        {
            double total = S.AudioOffset + S.JudgeOffset;
            judge = -measured;
            audio = total + measured;
        }
        audio = Clamp(Math.Round(audio * 1000.0) / 1000.0, -1.0, 1.0);
        judge = Clamp(Math.Round(judge * 1000.0) / 1000.0, -0.3, 0.3);
    }

    public string DescribeCalibration(OffsetCalibrator.Mode mode, double measured)
    {
        double a, j;
        ComputeCalibration(mode, measured, out a, out j);
        return "오디오 오프셋   " + K.Ms(S.AudioOffset) + "  →  " + K.Ms(a) + "\n" +
               "판정 오프셋   " + K.Ms(S.JudgeOffset) + "  →  " + K.Ms(j);
    }

    public void ApplyCalibration(OffsetCalibrator.Mode mode, double measured)
    {
        double a, j;
        ComputeCalibration(mode, measured, out a, out j);
        S.AudioOffset = a;
        S.JudgeOffset = j;
        Changed();
        SetStatus("적용됨: 오디오 " + K.Ms(a) + ", 판정 " + K.Ms(j));
        FocusCurrentTab();
    }

    private ResultData GetUsableLastResult()
    {
        if (CoreManager.Instance == null) return null;
        ResultData r = CoreManager.Instance.LastResult;
        if (r == null || r.IsAutoPlay || r.ErrorSampleCount < 30) return null;
        return r;
    }

    // 결과창의 평균 오차(+ = SLOW)만큼 판정 오프셋을 옮긴다 (SLOW 면 - 방향)
    private void ApplyLastPlay()
    {
        ResultData r = GetUsableLastResult();
        if (r == null) return;
        S.JudgeOffset = Clamp(Math.Round(S.JudgeOffset * 1000.0 - r.AverageErrorMs) / 1000.0, -0.3, 0.3);
        r.AverageErrorMs = 0.0; // 같은 결과를 두 번 반영하지 않도록
        r.ErrorSampleCount = 0;
        Changed();
        SetStatus("판정 오프셋이 " + K.Ms(S.JudgeOffset) + " 이 되었습니다.");
    }

    // =====================================================================
    //  화면
    // =====================================================================
    private int CurrentScreenMode()
    {
        return S.ScreenMode >= 0 ? S.ScreenMode : SettingsStore.FromUnityMode(Screen.fullScreenMode);
    }

    private Vector2Int CurrentResolution()
    {
        if (S.ResolutionWidth > 0 && S.ResolutionHeight > 0) return new Vector2Int(S.ResolutionWidth, S.ResolutionHeight);
        return new Vector2Int(Screen.width, Screen.height);
    }

    private List<Vector2Int> GetResolutions()
    {
        if (resolutions != null) return resolutions;
        resolutions = Screen.resolutions
            .Select(r => new Vector2Int(r.width, r.height))
            .Distinct()
            .OrderBy(v => v.x).ThenBy(v => v.y)
            .ToList();
        Vector2Int cur = CurrentResolution();
        if (!resolutions.Contains(cur)) { resolutions.Add(cur); resolutions = resolutions.OrderBy(v => v.x).ThenBy(v => v.y).ToList(); }
        return resolutions;
    }

    // =====================================================================
    //  키 변경
    // =====================================================================
    private void BeginRebind(int lane)
    {
        rebindLane = lane;
        rebindArmFrame = Time.frameCount; // 버튼을 누른 그 프레임의 키 입력은 무시
        Select(null);                      // Enter/Space 가 버튼을 다시 누르지 않도록 선택 해제
        RefreshAll();
        SetStatus((lane + 1) + "번 레인에 쓸 키를 누르세요.  ESC = 취소");
    }

    private void CancelRebind()
    {
        if (rebindLane < 0) return;
        rebindLane = -1;
        RefreshAll();
    }

    private void UpdateRebind(Keyboard kb)
    {
        if (Time.frameCount <= rebindArmFrame) return;

        if (kb.escapeKey.wasPressedThisFrame)
        {
            int lane = rebindLane;
            CancelRebind();
            SetStatus("취소했습니다.");
            SelectNextFrame(laneButtons[lane].gameObject);
            return;
        }

        foreach (var control in kb.allKeys)
        {
            if (control == null || !control.wasPressedThisFrame) continue;
            Key key = control.keyCode;
            if (SettingsStore.ReservedKeys.Contains(key))
            {
                SetStatus(SettingsStore.KeyDisplayName(key) + " 는 레인 키로 쓸 수 없습니다.");
                return;
            }

            int lane = rebindLane;
            Key[] keys = SettingsStore.GetLaneKeys();
            int other = Array.IndexOf(keys, key);
            if (other >= 0 && other != lane) keys[other] = keys[lane]; // 이미 쓰는 키면 서로 바꾼다
            keys[lane] = key;
            S.LaneKeys = SettingsStore.ToKeyNames(keys);

            rebindLane = -1;
            Changed();
            SetStatus((lane + 1) + "번 레인 = " + SettingsStore.KeyDisplayName(key) +
                      (other >= 0 && other != lane ? "  (" + (other + 1) + "번 레인과 서로 바뀜)" : ""));
            SelectNextFrame(laneButtons[lane].gameObject);
            return;
        }
    }

    // =====================================================================
    //  탭 초기화 (두 번 눌러 확인)
    // =====================================================================
    private void OnResetClicked()
    {
        if (resetArmedUntil < 0f)
        {
            resetArmedUntil = Time.unscaledTime + 3f;
            resetText.text = "한 번 더";
            SetStatus("한 번 더 누르면 [" + TabNames[currentTab] + "] 탭을 기본값으로 되돌립니다.");
            return;
        }

        resetArmedUntil = -1f;
        resetText.text = "탭 초기화";
        var d = new SettingsData();
        switch (currentTab)
        {
            case TabSound:
                S.MasterVolume = d.MasterVolume; S.MusicVolume = d.MusicVolume;
                S.HitSoundOn = d.HitSoundOn; S.HitSoundVolume = d.HitSoundVolume;
                SettingsStore.ApplyAudio();
                break;
            case TabTiming:
                S.AudioOffset = d.AudioOffset; S.JudgeOffset = d.JudgeOffset;
                break;
            case TabPlay:
                S.SettingSpeed = d.SettingSpeed; S.StartTime = d.StartTime;
                break;
            case TabJudge:
                S.ShowJustPerfect = d.ShowJustPerfect; S.ShowFastSlow = d.ShowFastSlow; S.judgeoverrange = d.judgeoverrange;
                S.ShowHitErrorBar = d.ShowHitErrorBar;
                break;
            case TabVisual:
                S.GameSkin = d.GameSkin; S.EffectQuality = d.EffectQuality; S.LaneOpacity = d.LaneOpacity;
                S.LaneLines = d.LaneLines; S.ShowKeyMarkers = d.ShowKeyMarkers; S.ShowInfoUI = d.ShowInfoUI;
                break;
            case TabDisplay:
                S.VSync = d.VSync; S.FrameLimit = d.FrameLimit; S.ShowFps = d.ShowFps;
                SettingsStore.ApplyFrameRate();
                FpsOverlay.SetVisible(S.ShowFps);
                break;
            case TabControls:
                CancelRebind();
                S.LaneKeys = (string[])SettingsStore.DefaultLaneKeys.Clone();
                break;
        }
        Changed();
        SetStatus("[" + TabNames[currentTab] + "] 탭을 기본값으로 되돌렸습니다.");
    }

    private static double Clamp(double v, double min, double max) { return v < min ? min : (v > max ? max : v); }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}