using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// GAME SETTING (5_Test) 조정 패널 — GameManager 가 테스트 모드일 때 스스로 만든다 (간소화 버전)
//
//   ┌ 왼쪽: 타이밍 ─────────────┐                ┌ 오른쪽 ──────────────┐
//   │ 노트 속도      [-]  x4.5  [+] │                │ FAST / SLOW     (●━) │
//   │ 판정 오프셋    [-] +12ms  [+] │   레인 · 채보   │ JUST            (●━) │
//   │ 오디오 오프셋  [-]   0ms  [+] │   (계속 반복)   │ 판정 그래프     (●━) │
//   │ 최근 평균 -8.3ms → [반영]     │                │ AUTO PLAY       (●━) │
//   └──────────────────────────────┘                │ [처음부터]  [나가기]  │
//                                                    └──────────────────────┘
//  - [-] [+] 한 번 = 노트 속도 0.1 / 오프셋 1ms,  SHIFT 를 누른 채 = 노트 속도 1 / 오프셋 10ms (화면 안내는 없음)
//  - 설명 글 속의 {F1} {F2} {-} {+} 는 키캡 그림으로 표시 (Desc)
//  - 이 화면에서는 일시정지(ESC)가 없다 → [처음부터] · [나가기] 로. LIFE 는 무한 (밝은 푸른색 흐르는 막대)
//  - 오른쪽 패널은 게임 화면의 점수판 자리를 덮는다 (점수는 GameInfoHud 가 숨김)
//  - 모든 값은 바로 적용: 노트 속도는 내려오던 노트까지, 오디오 오프셋은 음악을 새 기준으로 다시 예약
//  - 마우스: EventSystem 없이 패널이 직접 마우스를 읽어 처리 (씬의 입력 설정과 상관없이 클릭이 먹음)
//  - 단축키: F1 / F2 노트 속도 -0.1 / +0.1
// ============================================================
public class GameSettingPanel : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0.03f, 0.035f, 0.05f, 0.82f);
    private static readonly Color TextMain = new Color(0.96f, 0.97f, 1f, 1f);
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color TextDim = new Color(1f, 1f, 1f, 0.32f);
    private static readonly Color Accent = new Color(0.36f, 0.88f, 0.90f, 1f);

    private const float PanelW = 470f, RowH = 72f, Pad = 30f;

    private GameManager gm;
    private TMP_FontAsset reg, bold;
    private RectTransform root;
    private CanvasGroup group;
    private Canvas canvas;
    private bool prevCursorVisible;
    private CursorLockMode prevCursorLock;
    private readonly List<Action> refreshers = new List<Action>();
    private readonly List<GameSettingButtonFx> buttons = new List<GameSettingButtonFx>();
    private readonly List<RectTransform> panels = new List<RectTransform>();
    private GameSettingButtonFx pressed;
    private float refreshTimer, fade;

    private TextMeshProUGUI autoStatus, autoAvg, autoRec;
    private Image autoBarFill;
    private float autoBarTarget, autoBarShown;
    private GameSettingButtonFx recButton;

    private SettingsData S { get { return SettingsStore.Current; } }

    public static GameSettingPanel Create(GameManager manager)
    {
        GameObject go = new GameObject("GameSettingPanel", typeof(RectTransform));
        go.layer = 5;
        GameSettingPanel p = go.AddComponent<GameSettingPanel>();
        p.gm = manager;
        p.Build();
        return p;
    }

    // =====================================================================
    //  만들기
    // =====================================================================
    private void Build()
    {
        SettingsUIKit.EnsureFont();
        reg = SettingsWindow.RegularFont != null ? SettingsWindow.RegularFont : Resources.Load<TMP_FontAsset>("Fonts/SettingsFont");
        bold = SettingsWindow.BoldFont != null ? SettingsWindow.BoldFont : Resources.Load<TMP_FontAsset>("Fonts/SettingsFontBold");
        if (reg == null) reg = SettingsUIKit.Font;

        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;                 // 게임 HUD 위, 일시정지(450) · 설정창(500) 아래
        CanvasScaler sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        root = (RectTransform)transform;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        prevCursorVisible = Cursor.visible; prevCursorLock = Cursor.lockState;
        Cursor.visible = true; Cursor.lockState = CursorLockMode.None;

        // ----- 왼쪽: 타이밍 -----
        float y = Pad;
        RectTransform left = MakePanel(new Vector2(0f, 0.5f), 48f);
        y += 4f;                                           // (위쪽 제목 없음)
        y = StepRow(left, y, "노트 속도",
            "노트가 내려오는 빠르기입니다. {F1}과 {F2}를 통해 빠르기를 조절하십시오.",
            () => "x" + S.SettingSpeed.ToString("0.0", CultureInfo.InvariantCulture),
            big => { double d = big ? 1.0 : 0.1; return d; },
            v => { S.SettingSpeed = Clamp(Math.Round((S.SettingSpeed + v) * 10.0) / 10.0, 1.0, 12.0); gm.NotifyScrollChanged(); });
        y = StepRow(left, y, "판정 오프셋",
            "판정 기준선을 옮깁니다(노트·음악 위치는 그대로). SLOW가 많으면 {-}, FAST가 많으면 {+} 쪽으로 조정하십시오.",
            () => SettingsUIKit.Ms(S.JudgeOffset),
            big => big ? 0.010 : 0.001,
            v => { S.JudgeOffset = Clamp(Math.Round((S.JudgeOffset + v) * 1000.0) / 1000.0, -0.3, 0.3); gm.ResetCalibration(); });
        y = StepRow(left, y, "오디오 오프셋",
            "음악을 노트보다 먼저·늦게 틀어 소리와 화면을 맞춥니다. 소리가 늦게 들리면 {-} 쪽으로, 빠르게 들리면 {+} 쪽으로 조정하십시오.",
            () => SettingsUIKit.Ms(S.AudioOffset),
            big => big ? 0.010 : 0.001,
            v => { S.AudioOffset = Clamp(Math.Round((S.AudioOffset + v) * 1000.0) / 1000.0, -1.0, 1.0); gm.ApplyAudioOffsetLive(); gm.ResetCalibration(); });
        FitHeight(left, y + Pad - 16f);

        // ----- 왼쪽 아래: 자동 맞춤 (따로 떨어진 막대) -----
        RectTransform auto = MakePanel(new Vector2(0f, 0.5f), 48f);
        float ay = Pad;
        TextMeshProUGUI at = Txt(auto, "자동 맞춤", 21, TextAlignmentOptions.MidlineLeft, TextMain, false);
        Place(at.rectTransform, Pad, ay, 200f, 32f);
        autoStatus = Txt(auto, "", 17, TextAlignmentOptions.MidlineRight, TextSub, true);
        Place(autoStatus.rectTransform, PanelW - Pad - 200f, ay, 200f, 32f);
        ay += 40f;
        // 모인 노트 수 막대 (10노트가 모이면 꽉 참)
        Image track = SettingsUIKit.Img("Track", auto, new Color(1f, 1f, 1f, 0.10f));
        SettingsUIKit.Rounded(track, 4f);
        Place(track.rectTransform, Pad, ay, PanelW - Pad * 2f, 8f);
        autoBarFill = SettingsUIKit.Img("Fill", track.rectTransform, Accent);
        SettingsUIKit.Rounded(autoBarFill, 4f);
        autoBarFill.rectTransform.anchorMin = Vector2.zero;
        autoBarFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        autoBarFill.rectTransform.offsetMin = autoBarFill.rectTransform.offsetMax = Vector2.zero;
        ay += 22f;
        // 두 칸: 평균 오차 | 추천 판정 오프셋
        float colW = (PanelW - Pad * 2f - 16f) * 0.5f;
        TextMeshProUGUI l1 = Txt(auto, "최근 평균 오차", 16, TextAlignmentOptions.MidlineLeft, TextSub, false);
        Place(l1.rectTransform, Pad, ay, colW, 24f);
        TextMeshProUGUI l2 = Txt(auto, "추천 판정 오프셋", 16, TextAlignmentOptions.MidlineLeft, TextSub, false);
        Place(l2.rectTransform, Pad + colW + 16f, ay, colW, 24f);
        ay += 26f;
        autoAvg = Txt(auto, "", 30, TextAlignmentOptions.MidlineLeft, TextMain, true);
        Place(autoAvg.rectTransform, Pad, ay, colW, 42f);
        autoRec = Txt(auto, "", 30, TextAlignmentOptions.MidlineLeft, Accent, true);
        Place(autoRec.rectTransform, Pad + colW + 16f, ay, colW, 42f);
        ay += 52f;
        recButton = Button(auto, Pad, ay, PanelW - Pad * 2f, 52f, "추천값 반영", Accent, ApplyRecommendation);
        ay += 62f;
        ay = Desc(auto, ay, "직접 친 최근 40노트의 평균 오차로 알맞은 판정 오프셋을 계산합니다. 오프셋을 바꾸거나 [추천값 반영]을 누르면 처음부터 다시 모읍니다.");
        FitHeight(auto, ay + Pad - 16f);

        // 왼쪽 두 막대를 세로 가운데에 나란히 (위: 타이밍, 아래: 자동 맞춤)
        float gapH = 16f, lh = left.sizeDelta.y, ah = auto.sizeDelta.y, total = lh + gapH + ah;
        left.pivot = auto.pivot = new Vector2(0f, 1f);
        left.anchoredPosition = new Vector2(48f, total * 0.5f);
        auto.anchoredPosition = new Vector2(48f, total * 0.5f - lh - gapH);

        // ----- 오른쪽: 표시 · 플레이 (점수판 자리) -----
        y = Pad;
        RectTransform right = MakePanel(new Vector2(1f, 0.5f), -48f);
        y += 4f;
        y = ToggleRow(right, y, "FAST / SLOW", "판정 글자 아래에 오차를 보여 줍니다. 빨리 치면 -ms, 늦게 치면 +ms.",
            () => S.ShowFastSlow, v => S.ShowFastSlow = v);
        y = ToggleRow(right, y, "JUST", "±20ms 안에 친 PERFECT를 JUST로 따로 표시합니다.",
            () => S.ShowJustPerfect, v => S.ShowJustPerfect = v);
        y = ToggleRow(right, y, "판정 그래프", "화면 아래에 칠 때 나타난 오차를 눈금으로 표시합니다.",
            () => S.ShowHitErrorBar, v => { S.ShowHitErrorBar = v; gm.SetHitErrorMeter(v); });
        y = ToggleRow(right, y, "AUTO PLAY", "자동으로 쳐지는 플레이입니다.",
            () => GameSettingSession.AutoPlay, v => GameSettingSession.AutoPlay = v, false);
        y += 10f;
        float bw = (PanelW - Pad * 2f - 12f) * 0.5f;
        Button(right, Pad, y, bw, 56f, "처음부터", TextMain, () => gm.RestartTestLoop());
        Button(right, Pad + bw + 12f, y, bw, 56f, "나가기", new Color(1f, 0.6f, 0.5f), () => gm.BackToMenu());
        y += 56f;
        y = Desc(right, y + 4f, "[처음부터] 채보를 처음부터 다시 · [나가기] 곡 선택으로");
        FitHeight(right, y + Pad - 10f);

        RefreshAll();
    }

    private RectTransform MakePanel(Vector2 anchor, float x)
    {
        Image bg = SettingsUIKit.Img("Panel", root, PanelColor);
        SettingsUIKit.Rounded(bg, 18f);
        RectTransform r = bg.rectTransform;
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = anchor;
        r.sizeDelta = new Vector2(PanelW, 300f);
        r.anchoredPosition = new Vector2(x, 0f);
        panels.Add(r);
        return r;
    }

    private static void FitHeight(RectTransform panel, float h) { panel.sizeDelta = new Vector2(PanelW, h); }

    private float Title(RectTransform p, string text, float y)
    {
        TextMeshProUGUI t = Txt(p, text, 18, TextAlignmentOptions.MidlineLeft, new Color(1f, 1f, 1f, 0.55f), true);
        t.characterSpacing = 6f;
        Place(t.rectTransform, Pad, y, PanelW - Pad * 2f, 28f);
        Image acc = SettingsUIKit.Img("Accent", p, Accent);
        Place(acc.rectTransform, Pad, y + 34f, 32f, 3f);
        return y + 52f;
    }

    private float Hint(RectTransform p, float y, string text)
    {
        TextMeshProUGUI t = Txt(p, text, 15, TextAlignmentOptions.MidlineLeft, new Color(1f, 1f, 1f, 0.45f), false);
        Place(t.rectTransform, Pad, y, PanelW - Pad * 2f, 24f);
        return y + 30f;
    }

    // 이름 ……  [-]  값  [+]
    private float StepRow(RectTransform p, float y, string label, string desc, Func<string> get, Func<bool, double> step, Action<double> apply)
    {
        float h = RowH - 10f;
        TextMeshProUGUI l = Txt(p, label, 21, TextAlignmentOptions.MidlineLeft, TextMain, false);
        Place(l.rectTransform, Pad, y, 170f, h);
        const float B = 50f, ValW = 120f;
        float x1 = PanelW - Pad - B, x0 = x1 - ValW - B;
        Button(p, x0, y + (h - B) * 0.5f, B, B, "−", TextMain, () => { apply(-step(Shift())); SettingsStore.MarkDirty(); RefreshAll(); });
        TextMeshProUGUI v = Txt(p, "", 26, TextAlignmentOptions.Center, TextMain, true);
        Place(v.rectTransform, x0 + B, y, ValW, h);
        Button(p, x1, y + (h - B) * 0.5f, B, B, "+", TextMain, () => { apply(step(Shift())); SettingsStore.MarkDirty(); RefreshAll(); });
        refreshers.Add(() => v.text = get());
        return Desc(p, y + h + 4f, desc) + 12f;
    }

    // 세부 설명 (여러 줄, 읽기 좋은 크기) → 다음 y
    //  {F1} {F2} {-} {+} 처럼 중괄호로 쓴 키는 글 속에 키캡 그림(KeyHintUI 와 같은 모양)으로 넣는다
    private const float DescSize = 16f, CapSize = 13f;
    private float Desc(RectTransform p, float y, string text)
    {
        // 1) 키 자리를 '안 보이는 글자 + 키캡 폭만큼 빈칸'으로 바꿔서 글 배치에 자리를 잡아 둠
        var keys = new List<string>();
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('{', i);
            int close = open >= 0 ? text.IndexOf('}', open + 1) : -1;
            if (open < 0 || close < 0) { sb.Append(text, i, text.Length - i); break; }
            sb.Append(text, i, open - i);
            string key = text.Substring(open + 1, close - open - 1);
            float capW = CapWidth(key) + 6f;
            // [버그 수정] <alpha> 는 닫는 태그가 없어서 뒤 글자가 모두 투명해졌다 → 닫히는 <color> 로 안 보이는 점을 만듦
            sb.Append("<link=\"k" + keys.Count + "\"><color=#00000000>.</color><space=" + (capW / DescSize).ToString("0.###", CultureInfo.InvariantCulture) + "em></link>");
            keys.Add(key);
            i = close + 1;
        }
        string shown = sb.ToString();

        TextMeshProUGUI t = Txt(p, shown, DescSize, TextAlignmentOptions.TopLeft, new Color(1f, 1f, 1f, 0.62f), false);
#pragma warning disable CS0618
        t.enableWordWrapping = true;
#pragma warning restore CS0618
        t.lineSpacing = 14f;                                 // 키캡이 들어가도 줄이 붙지 않도록 넉넉하게
        float w = PanelW - Pad * 2f;
        float hh = Mathf.Ceil(t.GetPreferredValues(shown, w, 0f).y) + 4f;
        Place(t.rectTransform, Pad, y, w, hh);

        // 2) 자리 잡은 곳에 키캡을 올림
        if (keys.Count > 0)
        {
            t.ForceMeshUpdate();
            TMP_TextInfo info = t.textInfo;
            for (int k = 0; k < info.linkCount && k < keys.Count; k++)
            {
                TMP_LinkInfo li = info.linkInfo[k];
                int ci = li.linkTextfirstCharacterIndex;
                if (ci < 0 || ci >= info.characterCount) continue;
                TMP_CharacterInfo ch = info.characterInfo[ci];
                TMP_LineInfo line = info.lineInfo[ch.lineNumber];
                float midY = (line.ascender + line.descender) * 0.5f;
                int keyIndex = 0;
                int.TryParse(li.GetLinkID().Substring(1), out keyIndex);
                RectTransform cap = KeyHintUI.Build(t.rectTransform, bold != null ? bold : reg, bold != null ? bold : reg,
                                                    new[] { new KeyHintUI.Item(null, keys[Mathf.Clamp(keyIndex, 0, keys.Count - 1)]) }, CapSize, -1);
                cap.anchorMin = cap.anchorMax = new Vector2(0f, 1f);      // 글 상자의 피벗(왼쪽 위)과 같은 기준
                cap.anchoredPosition = new Vector2(ch.bottomLeft.x + 3f, midY);
            }
        }
        return y + hh + 10f;
    }

    // KeyHintUI 키캡 폭 (같은 계산): 높이 = 글자 크기 × 1.65, 폭 = max(높이, 글자 폭 + 크기 × 1.1)
    private float CapWidth(string key)
    {
        float h = Mathf.Round(CapSize * 1.65f);
        if (key.StartsWith("^")) return h;
        float textW = key.Length * CapSize * 0.62f;                   // 대략 (글꼴마다 조금 다름)
        return Mathf.Max(h, textW + CapSize * 1.1f);
    }

    // 줄 전체가 스위치:  이름 …………  (●━━)
    private float ToggleRow(RectTransform p, float y, string label, string desc, Func<bool> get, Action<bool> set, bool save = true)
    {
        GameSettingButtonFx b = MakeButton(p, Pad - 10f, y, PanelW - Pad * 2f + 20f, RowH - 14f, new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.09f));
        TextMeshProUGUI l = Txt(b.transform, label, 20, TextAlignmentOptions.MidlineLeft, TextSub, false);
        SettingsUIKit.Place(l.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-90f, 0f));

        const float TW = 60f, TH = 30f, KN = 22f;
        Image track = SettingsUIKit.Img("Track", b.transform, Color.white);
        SettingsUIKit.Rounded(track, TH * 0.5f);
        RectTransform tr = track.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(1f, 0.5f);
        tr.pivot = new Vector2(1f, 0.5f);
        tr.sizeDelta = new Vector2(TW, TH);
        tr.anchoredPosition = new Vector2(-14f, 0f);
        Image knob = SettingsUIKit.Img("Knob", tr, Color.white);
        SettingsUIKit.Rounded(knob, KN * 0.5f);
        RectTransform kr = knob.rectTransform;
        kr.anchorMin = kr.anchorMax = new Vector2(0f, 0.5f);
        kr.pivot = new Vector2(0.5f, 0.5f);
        kr.sizeDelta = new Vector2(KN, KN);

        b.OnClick = () => { set(!get()); if (save) SettingsStore.MarkDirty(); RefreshAll(); };
        refreshers.Add(() =>
        {
            bool on = get();
            l.color = on ? TextMain : TextSub;
            track.color = on ? Accent : new Color(1f, 1f, 1f, 0.16f);
            knob.color = on ? new Color(0.04f, 0.06f, 0.08f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f);
            float pad = (TH - KN) * 0.5f;
            kr.anchoredPosition = new Vector2(on ? TW - pad - KN * 0.5f : pad + KN * 0.5f, 0f);
        });
        return Desc(p, y + RowH - 10f, desc) + 8f;
    }

    private GameSettingButtonFx Button(RectTransform p, float x, float y, float w, float h, string label, Color c, Action onClick)
    {
        bool plain = c == TextMain;
        GameSettingButtonFx b = MakeButton(p, x, y, w, h,
            plain ? new Color(1f, 1f, 1f, 0.07f) : new Color(c.r, c.g, c.b, 0.12f),
            plain ? new Color(1f, 1f, 1f, 0.16f) : new Color(c.r, c.g, c.b, 0.24f));
        TextMeshProUGUI t = Txt(b.transform, label, label.Length <= 1 ? 30f : 19f, TextAlignmentOptions.Center, c, true);
        SettingsUIKit.Stretch(t.rectTransform);
        b.OnClick = onClick;
        return b;
    }

    private GameSettingButtonFx MakeButton(RectTransform p, float x, float y, float w, float h, Color baseC, Color hoverC)
    {
        Image bg = SettingsUIKit.Img("Button", p, baseC);
        SettingsUIKit.Rounded(bg, 10f);
        RectTransform r = bg.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);                          // 가운데 기준으로 커졌다 작아지도록
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        Image flash = SettingsUIKit.Img("Flash", r, new Color(1f, 1f, 1f, 0f));
        SettingsUIKit.Rounded(flash, 10f);
        SettingsUIKit.Stretch(flash.rectTransform);
        GameSettingButtonFx fx = bg.gameObject.AddComponent<GameSettingButtonFx>();
        fx.Bg = bg; fx.Flash = flash;
        fx.BaseColor = baseC; fx.HoverColor = hoverC;
        buttons.Add(fx);
        return fx;
    }

    private TextMeshProUGUI Txt(Transform parent, string text, float size, TextAlignmentOptions align, Color c, bool isBold)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt("T", parent, text, size, align, c, FontStyles.Normal);
        if (isBold && bold != null) t.font = bold;
        else
        {
            if (reg != null) t.font = reg;
            if (isBold) t.fontStyle = FontStyles.Bold;
        }
        t.richText = true;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    // 패널 왼쪽 위 기준 (y 는 아래로 +)
    private static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static bool Shift()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    void Update()
    {
        if (gm == null) return;
        float dt = Time.unscaledDeltaTime;
        bool show = gm.LaneIntroStarted || gm.currentState == GameState.Playing || gm.currentState == GameState.Paused;
        fade = Mathf.MoveTowards(fade, show ? 1f : 0f, dt / 0.35f);
        group.alpha = fade * fade * (3f - 2f * fade);

        UpdateMouse();

        // 자동 맞춤 막대: 목표까지 부드럽게 차오름 (반복이 다시 시작되면 부드럽게 줄어듦)
        if (autoBarFill != null)
        {
            autoBarShown = Mathf.Lerp(autoBarShown, autoBarTarget, 1f - Mathf.Exp(-dt * 6f));
            if (Mathf.Abs(autoBarShown - autoBarTarget) < 0.001f) autoBarShown = autoBarTarget;
            autoBarFill.rectTransform.anchorMax = new Vector2(autoBarShown, 1f);
        }

        // 단축키: F1 / F2 노트 속도 (곡 선택창과 같음)
        var kb = Keyboard.current;
        if (kb != null && !SettingsWindow.BlocksGameInput && gm.currentState == GameState.Playing)
        {
            int d = kb.f1Key.wasPressedThisFrame ? -1 : kb.f2Key.wasPressedThisFrame ? 1 : 0;
            if (d != 0)
            {
                S.SettingSpeed = Clamp(Math.Round(S.SettingSpeed * 10.0 + d) / 10.0, 1.0, 12.0);
                SettingsStore.MarkDirty();
                gm.NotifyScrollChanged();
                RefreshAll();
            }
        }

        refreshTimer -= dt;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.1f;
            RefreshAll();                    // 설정창에서 바꾼 값도 따라감
            RefreshRecommendation();
        }
    }

    // 마우스를 직접 읽어 버튼 처리 (EventSystem 을 쓰지 않음 → 씬 입력 설정과 상관없이 동작)
    private void UpdateMouse()
    {
        Mouse mouse = Mouse.current;
        bool usable = mouse != null && fade > 0.5f && gm.currentState != GameState.Paused && !SettingsWindow.IsOpen;
        if (!usable)
        {
            foreach (var b in buttons) b.SetHover(false);
            pressed = null;
            return;
        }
        Vector2 mp = mouse.position.ReadValue();
        GameSettingButtonFx over = null;
        foreach (var b in buttons)
        {
            bool inside = b.isActiveAndEnabled && RectTransformUtility.RectangleContainsScreenPoint(b.Rect, mp, null);
            b.SetHover(inside);
            if (inside) over = b;
        }
        if (mouse.leftButton.wasPressedThisFrame && over != null) { pressed = over; over.Press(); }
        if (mouse.leftButton.wasReleasedThisFrame && pressed != null)
        {
            pressed.Release(pressed == over);
            pressed = null;
        }
    }

    private void RefreshAll() { foreach (var r in refreshers) r(); }

    private const float DeadZoneMs = 2f;

    private void RefreshRecommendation()
    {
        int n;
        float avg = gm.RecentAverageErrorMs(40, out n);
        bool ok = n >= 10;
        autoBarTarget = Mathf.Clamp01(n / 10f);           // 막대는 Update 에서 부드럽게 따라감
        if (!ok)
        {
            autoStatus.text = "";                            // (모인 노트 수 글자 없음 — 막대로만 표시)
            autoAvg.text = "<color=#FFFFFF40>—</color>";
            autoRec.text = "<color=#FFFFFF40>—</color>";
        }
        else
        {
            autoStatus.text = "";                            // (준비되면 상태 글자 없음)
            bool fine = Mathf.Abs(avg) < DeadZoneMs;           // ±2ms 안이면 손대지 않는 게 낫다 (사람 손의 흔들림 수준)
            string side = fine ? "  <size=55%>딱 맞음</size>" : avg > 0 ? "  <size=55%><color=#FF6B6B>SLOW</color></size>" : "  <size=55%><color=#87CEEB>FAST</color></size>";
            string col = fine ? "#FFFFFF" : avg > 0 ? "#FF6B6B" : "#87CEEB";
            autoAvg.text = "<color=" + col + ">" + (avg > 0 ? "+" : "") + avg.ToString("0.0", CultureInfo.InvariantCulture) + "<size=60%> ms</size></color>" + side;
            double next = fine ? S.JudgeOffset : Clamp(Math.Round(S.JudgeOffset * 1000.0 - avg) / 1000.0, -0.3, 0.3);
            ok = !fine;
            autoRec.text = SettingsUIKit.Ms(next).Replace(" ms", "<size=60%> ms</size>");
        }
        recButton.Interactable = ok;
    }

    private void ApplyRecommendation()
    {
        int n;
        float avg = gm.RecentAverageErrorMs(40, out n);
        if (n < 10 || Mathf.Abs(avg) < DeadZoneMs) return;
        S.JudgeOffset = Clamp(Math.Round(S.JudgeOffset * 1000.0 - avg) / 1000.0, -0.3, 0.3);
        SettingsStore.MarkDirty();
        gm.ResetCalibration();          // 반영 후에는 새 오프셋으로 친 노트만 다시 모음 (바꾸기 전 오차가 섞이지 않게)
        RefreshAll();
    }

    void OnDestroy()
    {
        Cursor.visible = prevCursorVisible;
        Cursor.lockState = prevCursorLock;
    }

    private static double Clamp(double v, double min, double max) { return v < min ? min : (v > max ? max : v); }
}