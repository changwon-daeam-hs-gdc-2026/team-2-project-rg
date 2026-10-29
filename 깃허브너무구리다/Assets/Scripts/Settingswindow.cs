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
//  - 탭: SOUND / OFFSET / GAMEPLAY / DISPLAY / CONTROLS
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
    private static readonly string[] TabNames = { "SOUND", "OFFSET", "GAMEPLAY", "DISPLAY", "CONTROLS" };
    private static readonly string[] TabHints =
    {
        "Volume settings are applied immediately.",
        "Offsets: run SOUND TEST first, then VISUAL TEST if notes feel off.",
        "GAME UI and scroll speed apply from the next play. Speed: F1 / F2 in song select.",
        "Screen mode and resolution are applied in builds only (not in the editor).",
        "Click a lane, then press the key you want. ESC cancels."
    };
    private static readonly string[] ScreenModeNames = { "FULLSCREEN", "BORDERLESS", "WINDOWED" };

    private GameObject root;
    private RectTransform panel;
    private readonly List<Button> tabButtons = new List<Button>();
    private readonly List<TextMeshProUGUI> tabLabels = new List<TextMeshProUGUI>();
    private readonly List<GameObject> pages = new List<GameObject>();
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
        if (FindObjectOfType<EventSystem>() != null) return;

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
            if (resetText != null) resetText.text = "RESET TAB";
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

        TextMeshProUGUI title = K.Txt("Title", panel, "SETTINGS", 44, TextAlignmentOptions.Left, K.TextMain, FontStyles.Bold);
        K.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(40f, -90f), new Vector2(0f, -24f));

        TextMeshProUGUI keysHint = K.Txt("KeysHint", panel, "TAB  NEXT TAB      ESC  CLOSE", 18, TextAlignmentOptions.Right, K.TextSub);
        K.Place(keysHint.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(0f, -80f), new Vector2(-40f, -34f));

        // 왼쪽 탭 목록
        RectTransform tabs = K.Rect("Tabs", panel);
        K.Place(tabs, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(32f, 110f), new Vector2(272f, -110f));
        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;
            TextMeshProUGUI label;
            Button b = K.Btn(tabs, TabNames[i], 24, () => SelectTab(index), out label);
            K.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -(i * 72f + 62f)), new Vector2(0f, -(i * 72f)));
            label.alignment = TextAlignmentOptions.Left;
            label.rectTransform.offsetMin = new Vector2(24f, 0f);
            tabButtons.Add(b);
            tabLabels.Add(label);
        }

        // 오른쪽 내용
        RectTransform content = K.Rect("Content", panel);
        K.Place(content, Vector2.zero, Vector2.one, new Vector2(300f, 110f), new Vector2(-40f, -110f));
        for (int i = 0; i < TabNames.Length; i++)
        {
            RectTransform page = K.Rect("Page_" + TabNames[i], content);
            K.Stretch(page);
            VerticalLayoutGroup v = page.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12f;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            pages.Add(page.gameObject);
        }
        BuildSoundPage(pages[0].transform);
        BuildOffsetPage(pages[1].transform);
        BuildGameplayPage(pages[2].transform);
        BuildDisplayPage(pages[3].transform);
        BuildControlsPage(pages[4].transform);

        // 아래쪽
        statusText = K.Txt("Status", panel, "", 18, TextAlignmentOptions.Left, K.TextSub);
        K.Place(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(300f, 32f), new Vector2(-420f, 86f));

        Button reset = K.Btn(panel, "RESET TAB", 22, OnResetClicked, out resetText);
        K.Place((RectTransform)reset.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-400f, 32f), new Vector2(-226f, 86f));
        TextMeshProUGUI closeText;
        Button close = K.Btn(panel, "CLOSE", 22, Hide, out closeText);
        K.Place((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-210f, 32f), new Vector2(-40f, 86f));
        K.SetButtonColor(close, K.AccentDark);

        // 오프셋 측정 화면 (패널 위를 덮음)
        calibrator = gameObject.AddComponent<OffsetCalibrator>();
        calibrator.Build(panel, this);

        sfxPreview = gameObject.AddComponent<AudioSource>();
        sfxPreview.playOnAwake = false;
        hitClip = Resources.Load<AudioClip>("Audio/etc/hitsound");

        root.SetActive(false);
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
            K.SetButtonColor(tabButtons[i], on ? K.Accent : new Color(1f, 1f, 1f, 0.04f));
            tabLabels[i].color = on ? K.TextOnAccent : K.TextSub;
        }
        resetArmedUntil = -1f;
        if (resetText != null) resetText.text = "RESET TAB";
        SetStatus(TabHints[currentTab]);
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
        SliderRow(page, "MASTER VOLUME", "All sounds",
            () => S.MasterVolume * 100f,
            v => { S.MasterVolume = v / 100f; SettingsStore.ApplyAudio(); SettingsStore.MarkDirty(); },
            v => Mathf.RoundToInt(v) + "%");

        SliderRow(page, "MUSIC VOLUME", "In-game music and song preview",
            () => S.MusicVolume * 100f,
            v => { S.MusicVolume = v / 100f; SettingsStore.MarkDirty(); },
            v => Mathf.RoundToInt(v) + "%");

        SwitchRow(page, "HIT SOUND", "Sound when you press a lane key",
            () => S.HitSoundOn,
            on => { S.HitSoundOn = on; Changed(); if (on) PreviewHitSound(true); });

        SliderRow(page, "HIT SOUND VOLUME", null,
            () => S.HitSoundVolume * 100f,
            v => { S.HitSoundVolume = v / 100f; SettingsStore.MarkDirty(); PreviewHitSound(false); },
            v => Mathf.RoundToInt(v) + "%");
    }

    private void BuildOffsetPage(Transform page)
    {
        StepperRow(page, "AUDIO OFFSET", "+ if the music sounds LATE (Bluetooth, TV)",
            new[] { "-10", "-1" }, new[] { -10, -1 }, new[] { "+1", "+10" }, new[] { 1, 10 },
            () => K.Ms(S.AudioOffset),
            d => { S.AudioOffset = Clamp(Math.Round(S.AudioOffset * 1000.0 + d) / 1000.0, -1.0, 1.0); Changed(); });

        StepperRow(page, "JUDGE OFFSET", "+ if you get many SLOW,  - if many FAST",
            new[] { "-10", "-1" }, new[] { -10, -1 }, new[] { "+1", "+10" }, new[] { 1, 10 },
            () => K.Ms(S.JudgeOffset),
            d => { S.JudgeOffset = Clamp(Math.Round(S.JudgeOffset * 1000.0 + d) / 1000.0, -0.3, 0.3); Changed(); });

        // 자동 측정
        RectTransform area = Row(page, "AUTO MEASURE", "Tap along to measure your offset", 84f);
        TextMeshProUGUI t;
        Button sound = K.Btn(area, "SOUND TEST", 22, () => calibrator.Begin(OffsetCalibrator.Mode.Sound), out t);
        K.Place((RectTransform)sound.transform, new Vector2(0f, 0f), new Vector2(0.49f, 1f), Vector2.zero, Vector2.zero);
        Button visual = K.Btn(area, "VISUAL TEST", 22, () => calibrator.Begin(OffsetCalibrator.Mode.Visual), out t);
        K.Place((RectTransform)visual.transform, new Vector2(0.51f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

        // 마지막 플레이 결과로 보정
        TextMeshProUGUI lastInfo = null;
        RectTransform lastArea = Row(page, "FROM LAST PLAY", null, 84f, d => lastInfo = d);
        TextMeshProUGUI applyText;
        Button apply = K.Btn(lastArea, "APPLY", 22, ApplyLastPlay, out applyText);
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
                if (lastInfo != null) lastInfo.text = "Play a song (not AUTO) to use this";
            }
            else
            {
                lastValue.text = K.Ms(r.AverageErrorMs / 1000.0) + (r.AverageErrorMs > 0 ? "  SLOW" : r.AverageErrorMs < 0 ? "  FAST" : "");
                lastValue.color = r.AverageErrorMs > 0 ? K.SlowColor : r.AverageErrorMs < 0 ? K.FastColor : K.TextMain;
                if (lastInfo != null) lastInfo.text = "Avg. error of " + r.ErrorSampleCount + " notes → adds to JUDGE OFFSET";
            }
        });

        InfoText(page,
            "AUDIO OFFSET moves the music against the notes. JUDGE OFFSET moves only the judgement.\n" +
            "SOUND TEST sets AUDIO OFFSET. VISUAL TEST then splits it so notes, music and judgement all line up.", 96f);
    }

    private void BuildGameplayPage(Transform page)
    {
        ChoiceRow(page, "GAME UI", "In-game screen design (lanes, notes, HUD)",
            () => GameSkins.Names[Mathf.Clamp(S.GameSkin, 0, GameSkins.Names.Length - 1)],
            d => { S.GameSkin = (S.GameSkin + d + GameSkins.Names.Length) % GameSkins.Names.Length; Changed(); });

        SliderRow(page, "LANE OPACITY", "SIMPLE UI lane background (0% = see-through)",
            () => S.LaneOpacity * 100f,
            v => { S.LaneOpacity = v / 100f; SettingsStore.MarkDirty(); },
            v => Mathf.RoundToInt(v) + "%");

        SwitchRow(page, "INFORMATION UI", "Song info, difficulty, time, score, speed (all GAME UI)",
            () => S.ShowInfoUI, on => { S.ShowInfoUI = on; Changed(); });

        StepperRow(page, "SCROLL SPEED", "Note speed (F1 / F2 in song select)",
            new[] { "-1", "-0.1" }, new[] { -10, -1 }, new[] { "+0.1", "+1" }, new[] { 1, 10 },
            () => S.SettingSpeed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            d => { S.SettingSpeed = Clamp(Math.Round(S.SettingSpeed * 10.0 + d) / 10.0, 1.0, 12.0); Changed(); });

        SwitchRow(page, "FAST / SLOW", "Show FAST / SLOW and ms under the judgement",
            () => S.ShowFastSlow, on => { S.ShowFastSlow = on; Changed(); });

        StepperRow(page, "FAST / SLOW THRESHOLD", "Show only when the error is at least this",
            new[] { "-5", "-1" }, new[] { -5, -1 }, new[] { "+1", "+5" }, new[] { 1, 5 },
            () => S.judgeoverrange + " ms",
            d => { S.judgeoverrange = Mathf.Clamp(S.judgeoverrange + d, 0, 120); Changed(); });
    }

    private void BuildDisplayPage(Transform page)
    {
        ChoiceRow(page, "SCREEN MODE", null,
            () => ScreenModeNames[CurrentScreenMode()],
            d =>
            {
                S.ScreenMode = (CurrentScreenMode() + d + 3) % 3;
                SettingsStore.ApplyScreen();
                Changed();
                if (Application.isEditor) SetStatus("Screen mode is applied in builds only.");
            });

        ChoiceRow(page, "RESOLUTION", null,
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
                if (Application.isEditor) SetStatus("Resolution is applied in builds only.");
            });

        SwitchRow(page, "VSYNC", "Sync to monitor refresh (ignores FRAME LIMIT)",
            () => S.VSync, on => { S.VSync = on; SettingsStore.ApplyFrameRate(); Changed(); });

        ChoiceRow(page, "FRAME LIMIT", "Higher = smoother input timing",
            () =>
            {
                string v = S.FrameLimit > 0 ? S.FrameLimit + " FPS" : "UNLIMITED";
                return S.VSync ? v + "  (VSYNC)" : v;
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

        SwitchRow(page, "SHOW FPS", "Frame rate in the top-right corner",
            () => S.ShowFps, on => { S.ShowFps = on; FpsOverlay.SetVisible(on); Changed(); });
    }

    private void BuildControlsPage(Transform page)
    {
        RectTransform area = Row(page, "LANE KEYS", "Lane 1 · 2 · 3 · 4", 104f);
        for (int i = 0; i < 4; i++)
        {
            int lane = i;
            TextMeshProUGUI label;
            Button b = K.Btn(area, "", 34, () => BeginRebind(lane), out label);
            K.Place((RectTransform)b.transform, new Vector2(i / 4f, 0f), new Vector2((i + 1) / 4f, 1f), new Vector2(5f, 0f), new Vector2(-5f, 0f));
            laneButtons[i] = b;
            refreshers.Add(() =>
            {
                Key[] keys = SettingsStore.GetLaneKeys();
                bool waiting = rebindLane == lane;
                label.text = waiting ? "PRESS..." : SettingsStore.KeyDisplayName(keys[lane]);
                label.fontSize = waiting ? 22 : 34;
                label.color = waiting ? K.TextOnAccent : K.TextMain;
                K.SetButtonColor(b, waiting ? K.Accent : K.ButtonColor);
            });
        }

        RectTransform resetArea = Row(page, "DEFAULT KEYS", null, 76f);
        TextMeshProUGUI t;
        Button reset = K.Btn(resetArea, "D  F  J  K", 24, () =>
        {
            CancelRebind();
            S.LaneKeys = (string[])SettingsStore.DefaultLaneKeys.Clone();
            Changed();
            SetStatus("Lane keys reset to D F J K.");
        }, out t);
        K.Place((RectTransform)reset.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-260f, 0f), Vector2.zero);

        InfoText(page, "SPACE (free-key space notes) and ESC (pause) are fixed and can't be used as lane keys.\n" +
                       "Choosing a key that another lane already uses swaps the two.", 80f);
    }

    // =====================================================================
    //  행(Row) 부품
    // =====================================================================
    // 왼쪽: 이름 + 설명, 오른쪽: 조작 영역(반환)
    private RectTransform Row(Transform page, string label, string desc, float height = 76f, Action<TextMeshProUGUI> descOut = null)
    {
        Image bg = K.Img("Row_" + label, page, K.RowColor);
        K.Fixed(bg.gameObject, height);
        RectTransform r = bg.rectTransform;

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

    private void SwitchRow(Transform page, string label, string desc, Func<bool> get, Action<bool> set)
    {
        RectTransform area = Row(page, label, desc);
        TextMeshProUGUI t;
        Button b = K.Btn(area, "", 24, () => set(!get()), out t);
        K.Place((RectTransform)b.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-180f, 0f), Vector2.zero);
        refreshers.Add(() =>
        {
            bool on = get();
            t.text = on ? "ON" : "OFF";
            t.color = on ? K.TextOnAccent : K.TextSub;
            K.SetButtonColor(b, on ? K.Accent : K.ButtonColor);
        });
    }

    // [-10][-1]  값  [+1][+10]
    private void StepperRow(Transform page, string label, string desc,
                            string[] leftLabels, int[] leftDeltas, string[] rightLabels, int[] rightDeltas,
                            Func<string> getText, Action<int> onDelta)
    {
        RectTransform area = Row(page, label, desc);
        const float bw = 76f, gap = 8f;
        TextMeshProUGUI t;

        float x = 0f;
        for (int i = 0; i < leftLabels.Length; i++)
        {
            int d = leftDeltas[i];
            Button b = K.Btn(area, leftLabels[i], 20, () => onDelta(d), out t);
            K.Place((RectTransform)b.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(x, 0f), new Vector2(x + bw, 0f));
            x += bw + gap;
        }
        float xr = 0f;
        for (int i = rightLabels.Length - 1; i >= 0; i--)
        {
            int d = rightDeltas[i];
            Button b = K.Btn(area, rightLabels[i], 20, () => onDelta(d), out t);
            K.Place((RectTransform)b.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-(xr + bw), 0f), new Vector2(-xr, 0f));
            xr += bw + gap;
        }

        TextMeshProUGUI value = K.Txt("Value", area, "", 28, TextAlignmentOptions.Center, K.TextMain, FontStyles.Bold);
        K.Place(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(x, 0f), new Vector2(-xr, 0f));
        refreshers.Add(() => value.text = getText());
    }

    // [<]  값  [>]
    private void ChoiceRow(Transform page, string label, string desc, Func<string> getText, Action<int> onDelta)
    {
        StepperRow(page, label, desc, new[] { "<" }, new[] { -1 }, new[] { ">" }, new[] { 1 }, getText, onDelta);
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
    // 측정 결과를 적용했을 때의 새 값
    //  - SOUND  : 소리에 맞춰 친 평균 오차 M = 음악 지연 + 입력 지연
    //             → AUDIO + JUDGE 의 합이 M 이 되도록 AUDIO 를 맞춘다
    //  - VISUAL : 화면에 맞춰 친 평균 오차 E = 화면 지연 + 입력 지연
    //             → JUDGE = E, 합(AUDIO + JUDGE)은 그대로 유지
    public void ComputeCalibration(OffsetCalibrator.Mode mode, double measured, out double audio, out double judge)
    {
        if (mode == OffsetCalibrator.Mode.Sound)
        {
            judge = S.JudgeOffset;
            audio = measured - judge;
        }
        else
        {
            double total = S.AudioOffset + S.JudgeOffset;
            judge = measured;
            audio = total - measured;
        }
        audio = Clamp(Math.Round(audio * 1000.0) / 1000.0, -1.0, 1.0);
        judge = Clamp(Math.Round(judge * 1000.0) / 1000.0, -0.3, 0.3);
    }

    public string DescribeCalibration(OffsetCalibrator.Mode mode, double measured)
    {
        double a, j;
        ComputeCalibration(mode, measured, out a, out j);
        return "AUDIO OFFSET   " + K.Ms(S.AudioOffset) + "  →  " + K.Ms(a) + "\n" +
               "JUDGE OFFSET   " + K.Ms(S.JudgeOffset) + "  →  " + K.Ms(j);
    }

    public void ApplyCalibration(OffsetCalibrator.Mode mode, double measured)
    {
        double a, j;
        ComputeCalibration(mode, measured, out a, out j);
        S.AudioOffset = a;
        S.JudgeOffset = j;
        Changed();
        SetStatus("Applied: AUDIO " + K.Ms(a) + ", JUDGE " + K.Ms(j));
        FocusCurrentTab();
    }

    private ResultData GetUsableLastResult()
    {
        if (CoreManager.Instance == null) return null;
        ResultData r = CoreManager.Instance.LastResult;
        if (r == null || r.IsAutoPlay || r.ErrorSampleCount < 30) return null;
        return r;
    }

    // 결과창의 평균 오차(+ = SLOW)만큼 판정 오프셋을 옮긴다
    private void ApplyLastPlay()
    {
        ResultData r = GetUsableLastResult();
        if (r == null) return;
        S.JudgeOffset = Clamp(Math.Round(S.JudgeOffset * 1000.0 + r.AverageErrorMs) / 1000.0, -0.3, 0.3);
        r.AverageErrorMs = 0.0; // 같은 결과를 두 번 반영하지 않도록
        r.ErrorSampleCount = 0;
        Changed();
        SetStatus("JUDGE OFFSET is now " + K.Ms(S.JudgeOffset) + ".");
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
        SetStatus("Press a key for lane " + (lane + 1) + ".  ESC to cancel.");
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
            SetStatus("Cancelled.");
            SelectNextFrame(laneButtons[lane].gameObject);
            return;
        }

        foreach (var control in kb.allKeys)
        {
            if (control == null || !control.wasPressedThisFrame) continue;
            Key key = control.keyCode;
            if (SettingsStore.ReservedKeys.Contains(key))
            {
                SetStatus(SettingsStore.KeyDisplayName(key) + " can't be used as a lane key.");
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
            SetStatus("Lane " + (lane + 1) + " = " + SettingsStore.KeyDisplayName(key) +
                      (other >= 0 && other != lane ? "  (swapped with lane " + (other + 1) + ")" : ""));
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
            resetText.text = "CONFIRM?";
            SetStatus("Press again to reset the " + TabNames[currentTab] + " tab to defaults.");
            return;
        }

        resetArmedUntil = -1f;
        resetText.text = "RESET TAB";
        var d = new SettingsData();
        switch (currentTab)
        {
            case 0:
                S.MasterVolume = d.MasterVolume; S.MusicVolume = d.MusicVolume;
                S.HitSoundOn = d.HitSoundOn; S.HitSoundVolume = d.HitSoundVolume;
                SettingsStore.ApplyAudio();
                break;
            case 1:
                S.AudioOffset = d.AudioOffset; S.JudgeOffset = d.JudgeOffset;
                break;
            case 2:
                S.SettingSpeed = d.SettingSpeed; S.ShowFastSlow = d.ShowFastSlow; S.judgeoverrange = d.judgeoverrange;
                S.GameSkin = d.GameSkin; S.LaneOpacity = d.LaneOpacity; S.ShowInfoUI = d.ShowInfoUI;
                break;
            case 3:
                S.VSync = d.VSync; S.FrameLimit = d.FrameLimit; S.ShowFps = d.ShowFps;
                SettingsStore.ApplyFrameRate();
                FpsOverlay.SetVisible(S.ShowFps);
                break;
            case 4:
                CancelRebind();
                S.LaneKeys = (string[])SettingsStore.DefaultLaneKeys.Clone();
                break;
        }
        Changed();
        SetStatus(TabNames[currentTab] + " reset to defaults.");
    }

    private static double Clamp(double v, double min, double max) { return v < min ? min : (v > max ? max : v); }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}