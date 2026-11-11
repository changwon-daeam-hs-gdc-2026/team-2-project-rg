using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// 채보 에디터 (새 디자인) — 화면(HUD) 부분 (AreaChartEditor.cs 와 한 클래스, partial)
//
//  ┌──────────────────────────── 위 막대 ─────────────────────────────┐
//  │ CHART EDITOR 곡명  INSANE        ▶ 01:23.456  BAR 12 · 3.25    BPM  OFFSET  ● 저장됨 │
//  ├──────────┬──────────────────────────────────────────┬─┬──────────┤
//  │ 도구     │                                          │미│ 위치      │
//  │ (노트 ·  │            레인 · 노트                    │니│ 레인      │
//  │  옵션 ·  │                                          │맵│           │
//  │  키+클릭)│                                          │ │ 오디오 · 통계│
//  ├──────────┴──────────────────────────────────────────┴─┴──────────┤
//  │ 아래 막대: 자주 쓰는 키                      알림(토스트)           │
//  └──────────────────────────────────────────────────────────────────┘
//   · 왼쪽 도구 카드 · RED / 드래그 · 층은 마우스로 눌러도 바뀜
//   · 키 + 클릭 도구는 그 키를 누르고 있으면 그 줄이 밝아짐
//   · 미니맵: 곡 전체의 노트 밀도 · 영역(파랑) · 레인 사라짐 구간(주황) · 지금 보이는 범위. 누르거나 끌면 그 위치로 이동
//   · H : 모든 단축키 도움말 (H / ESC 로 닫기)
//   · 저장 안 된 변경이 있으면 위 막대에 '● 수정됨'
// ============================================================
public partial class AreaChartEditor
{
    // ----- 색 -----
    private static readonly Color UiPanel = new Color(0.065f, 0.075f, 0.105f, 0.94f);
    private static readonly Color UiCard = new Color(1f, 1f, 1f, 0.045f);
    private static readonly Color UiCardOn = new Color(0.36f, 0.88f, 0.90f, 0.16f);
    private static readonly Color UiLine = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color UiAccent = new Color(0.36f, 0.88f, 0.90f, 1f);    // #5ce1e6
    private static readonly Color UiText = new Color(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Color UiSub = new Color(1f, 1f, 1f, 0.56f);
    private static readonly Color UiDim = new Color(1f, 1f, 1f, 0.30f);
    private static readonly Color UiWarn = new Color(1f, 0.62f, 0.32f, 1f);
    private static readonly Color UiRed = new Color(1f, 0.38f, 0.45f, 1f);

    private const float TopH = 68f, BottomH = 52f, LeftW = 300f, RightW = 340f, MiniW = 26f, Pad = 16f;

    // ----- HUD 요소 -----
    private RectTransform hudRoot, topBar, leftPanel, rightPanel, bottomBar, miniRoot, miniView, helpRoot;
    private TextMeshProUGUI tTitle, tDiff, tTime, tPos, tBpm, tOffset, tSave, tSnap, tZoom, tToast;
    private TextMeshProUGUI tPosBig, tPosSub, tLaneState, tStats, tLongEnd, tMusicVal, tHitVal;
    private Image playIcon, pauseL, pauseR, saveDot, toastBg, laneStateFill, musicFill, hitFill;
    private CanvasGroup toastGroup;

    private class ToolCard { public RectTransform rt; public Image bg, swatch, edge; public TextMeshProUGUI name; public int kind; }
    private readonly List<ToolCard> toolCards = new List<ToolCard>();             // kind: 1~4 노트 종류
    private class ToggleBtn { public RectTransform rt; public Image bg; public TextMeshProUGUI state; public int id; }
    private readonly List<ToggleBtn> toggles = new List<ToggleBtn>();             // id: 5 RED, 6 드래그, 7 슬라이드 편집, 21 ~ 25 = 1 ~ 5층
    private class KeyRow { public Key key; public Image bg; }
    private readonly List<KeyRow> keyRows = new List<KeyRow>();

    // 미니맵
    private Image miniImage;
    private Texture2D miniTex;
    private float miniTimer;
    private int miniSig;
    private double miniLenBeat = 64.0;
    private const int MiniH = 512;

    // 저장 상태 (내용 서명으로 비교 → 어떤 편집이든 '수정됨' 이 정확히 뜸)
    private int savedSig, curSig;
    private float sigTimer;
    private bool helpOpen;
    private static Sprite triSprite;

    // =====================================================================
    //  만들기
    // =====================================================================
    private TextMeshProUGUI Txt(string name, Transform parent, float size, TextAlignmentOptions align, Color color,
                                FontStyles style = FontStyles.Normal, bool wrap = false)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt(name, parent, "", size * fontScale, align, color, style, wrap);
        if (font != null) t.font = font;
        t.richText = true;
        return t;
    }

    private Image Panel(string name, Transform parent, Color c, float radius)
    {
        Image img = SettingsUIKit.Img(name, parent, c, false);
        if (radius > 0f) SettingsUIKit.Rounded(img, radius);
        return img;
    }

    // 부모 왼쪽 위 기준 (x, y 는 아래로 +)
    private static void At(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    private void BuildHud()
    {
        SettingsUIKit.EnsureFont();
        GameObject cgo = new GameObject("AreaEditorHud", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        hud = cgo.AddComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 500;
        CanvasScaler sc = cgo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        hudRoot = (RectTransform)cgo.transform;

        labelRoot = SettingsUIKit.Rect("Labels", cgo.transform);
        SettingsUIKit.Stretch(labelRoot);

        BuildTopBar();
        BuildLeftPanel();
        BuildRightPanel();
        BuildMiniMap();
        BuildBottomBar();
        BuildToast();
        BuildPrompt();
        BuildHelp();
        if (promptKeys != null) promptKeys.text = T("ENTER 적용   ·   ESC 취소", "ENTER apply   ·   ESC cancel");
        BuildBottomHints();
    }

    // ----- 위 막대 -----
    private void BuildTopBar()
    {
        topBar = Panel("TopBar", hudRoot, UiPanel, 0f).rectTransform;
        SettingsUIKit.Place(topBar, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -TopH), Vector2.zero);
        Image line = Panel("Line", topBar, UiLine, 0f);
        SettingsUIKit.Place(line.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f));

        // 왼쪽: 태그 · 곡명 · 난이도
        Image tag = Panel("Tag", topBar, new Color(UiAccent.r, UiAccent.g, UiAccent.b, 0.14f), 6f);
        At(tag.rectTransform, Pad + 4f, 20f, 112f, 28f);
        TextMeshProUGUI tagT = Txt("TagText", tag.rectTransform, 13, TextAlignmentOptions.Center, UiAccent, FontStyles.Bold);
        tagT.text = "CHART EDITOR"; tagT.characterSpacing = 3f;
        SettingsUIKit.Stretch(tagT.rectTransform);
        tTitle = Txt("Title", topBar, 24, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        At(tTitle.rectTransform, Pad + 132f, 14f, 460f, 40f);
        tDiff = Txt("Diff", topBar, 16, TextAlignmentOptions.MidlineLeft, UiSub, FontStyles.Bold);
        tDiff.characterSpacing = 4f;
        At(tDiff.rectTransform, Pad + 600f, 14f, 180f, 40f);

        // 가운데: 재생 상태 · 시간 · 위치
        RectTransform mid = SettingsUIKit.Rect("Transport", topBar);
        mid.anchorMin = mid.anchorMax = new Vector2(0.5f, 0.5f);
        mid.pivot = new Vector2(0.5f, 0.5f);
        mid.sizeDelta = new Vector2(520f, TopH);
        mid.anchoredPosition = new Vector2(40f, 0f);
        Image btn = Panel("PlayBtn", mid, new Color(1f, 1f, 1f, 0.08f), 19f);
        At(btn.rectTransform, 0f, 15f, 38f, 38f);
        playIcon = SettingsUIKit.Img("Play", btn.rectTransform, UiText);
        playIcon.sprite = TriSprite;
        playIcon.rectTransform.anchorMin = playIcon.rectTransform.anchorMax = playIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        playIcon.rectTransform.sizeDelta = new Vector2(14f, 16f);
        playIcon.rectTransform.anchoredPosition = new Vector2(2f, 0f);
        pauseL = SettingsUIKit.Img("PauseL", btn.rectTransform, UiAccent);
        pauseR = SettingsUIKit.Img("PauseR", btn.rectTransform, UiAccent);
        foreach (var p in new[] { pauseL, pauseR })
        {
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = p.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            p.rectTransform.sizeDelta = new Vector2(4f, 15f);
        }
        pauseL.rectTransform.anchoredPosition = new Vector2(-4f, 0f);
        pauseR.rectTransform.anchoredPosition = new Vector2(4f, 0f);
        tTime = Txt("Time", mid, 28, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        At(tTime.rectTransform, 54f, 10f, 200f, 48f);
        tPos = Txt("Pos", mid, 15, TextAlignmentOptions.MidlineLeft, UiSub, FontStyles.Bold);
        tPos.characterSpacing = 3f;
        At(tPos.rectTransform, 250f, 10f, 270f, 48f);

        // 오른쪽: BPM · OFFSET · 저장 상태
        tSave = Txt("Save", topBar, 15, TextAlignmentOptions.MidlineRight, UiSub, FontStyles.Bold);
        tSave.rectTransform.anchorMin = tSave.rectTransform.anchorMax = tSave.rectTransform.pivot = new Vector2(1f, 0.5f);
        tSave.rectTransform.sizeDelta = new Vector2(150f, 40f);
        tSave.rectTransform.anchoredPosition = new Vector2(-Pad - 4f, 0f);
        saveDot = Panel("Dot", topBar, UiAccent, 5f);
        saveDot.rectTransform.anchorMin = saveDot.rectTransform.anchorMax = saveDot.rectTransform.pivot = new Vector2(1f, 0.5f);
        saveDot.rectTransform.sizeDelta = new Vector2(10f, 10f);
        tOffset = Chip(topBar, -Pad - 180f, 150f);
        tBpm = Chip(topBar, -Pad - 344f, 130f);
    }

    private TextMeshProUGUI Chip(RectTransform parent, float rightX, float w)
    {
        Image bg = Panel("Chip", parent, UiCard, 8f);
        bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = bg.rectTransform.pivot = new Vector2(1f, 0.5f);
        bg.rectTransform.sizeDelta = new Vector2(w, 34f);
        bg.rectTransform.anchoredPosition = new Vector2(rightX, 0f);
        TextMeshProUGUI t = Txt("ChipText", bg.rectTransform, 15, TextAlignmentOptions.Center, UiText, FontStyles.Bold);
        SettingsUIKit.Stretch(t.rectTransform);
        return t;
    }

    // ----- 왼쪽 도구 -----
    private void BuildLeftPanel()
    {
        leftPanel = Panel("Tools", hudRoot, UiPanel, 14f).rectTransform;
        SettingsUIKit.Place(leftPanel, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(Pad, BottomH + Pad), new Vector2(Pad + LeftW, -TopH - Pad));
        float y = 16f;
        y = Section(leftPanel, T("노트", "NOTES"), y);
        string[] names = { T("탭", "TAP"), T("롱노트 시작", "LONG START"), T("롱노트 끝", "LONG END"), T("마디선", "MEASURE") };
        for (int i = 0; i < 4; i++)
        {
            int col = i % 2, row = i / 2;
            var c = new ToolCard { kind = i + 1 };
            c.bg = Panel("Card" + i, leftPanel, UiCard, 10f);
            c.rt = c.bg.rectTransform;
            At(c.rt, 16f + col * 138f, y + row * 74f, 130f, 66f);
            c.edge = Panel("Edge", c.rt, UiAccent, 10f);
            SettingsUIKit.Stretch(c.edge.rectTransform);
            c.edge.sprite = KeyCapLine; c.edge.type = Image.Type.Sliced; c.edge.pixelsPerUnitMultiplier = 1f;
            c.swatch = Panel("Swatch", c.rt, Color.white, 3f);
            At(c.swatch.rectTransform, 14f, 14f, 34f, i == 2 ? 5f : i == 3 ? 2f : 9f);
            c.name = Txt("Name", c.rt, 16, TextAlignmentOptions.BottomLeft, UiText, FontStyles.Bold);
            At(c.name.rectTransform, 14f, 28f, 110f, 28f);
            KeyCap(c.rt, (i + 1).ToString(), 98f, 10f);
            toolCards.Add(c);
        }
        y += 2 * 74f + 10f;

        y = Section(leftPanel, T("옵션", "OPTIONS"), y);
        y = ToggleRow(T("RED 노트", "RED NOTE"), "5", 5, y);
        y = ToggleRow(T("드래그 노트", "DRAG NOTE"), "6", 6, y);
        y = ToggleRow(T("슬라이드 편집", "SLIDE EDIT"), "7", 7, y);
        // 층: [5][4][3][2][1] (왼쪽이 위층 — 5층이 가장 위에 그려짐)
        TextMeshProUGUI ll = Txt("LayerLabel", leftPanel, 15, TextAlignmentOptions.MidlineLeft, UiSub, FontStyles.Bold);
        ll.text = T("층", "LAYER");
        At(ll.rectTransform, 18f, y, 60f, 34f);
        KeyCap(leftPanel, "Q", 64f, y + 4f);
        for (int i = 0; i < NoteLayer.Count; i++)
        {
            int layer = NoteLayer.Max - i;
            var tb = new ToggleBtn { id = 20 + layer };
            tb.bg = Panel("Layer" + layer, leftPanel, UiCard, 8f);
            tb.rt = tb.bg.rectTransform;
            At(tb.rt, 108f + i * 36f, y, 32f, 34f);
            tb.state = Txt("T", tb.rt, 15, TextAlignmentOptions.Center, UiText, FontStyles.Bold);
            tb.state.text = layer.ToString();
            SettingsUIKit.Stretch(tb.state.rectTransform);
            toggles.Add(tb);
        }
        y += 48f;

        y = Section(leftPanel, T("키를 누른 채 클릭", "HOLD KEY + CLICK"), y);
        KeyItem(Key.V, "V", T("레인 사라짐", "lanes disappear"), ref y);
        KeyItem(Key.B, "B", T("레인 드러남", "lanes reappear"), ref y);
        KeyItem(Key.U, "U", T("변속 SPEED UP / DOWN 표시", "speed UP / DOWN notice"), ref y);
        KeyItem(Key.G, "G", T("난이도 공개 (? → 실제 값)", "level reveal (? → real)"), ref y);
        KeyItem(Key.D, "D", "BPM", ref y);
        KeyItem(Key.F, "F", T("변속 이 층만 (SHIFT 모든 층)", "speed this layer (SHIFT all)"), ref y);
        KeyItem(Key.E, "E", T("정지 STOP", "STOP"), ref y);
        KeyItem(Key.R, "R", T("재개 START", "START"), ref y);
        TextMeshProUGUI del = Txt("DelHint", leftPanel, 13, TextAlignmentOptions.TopLeft, UiDim);
        del.text = T("키 + 우클릭 = 그 표시 삭제", "key + right-click = delete");
        At(del.rectTransform, 18f, y + 2f, 270f, 22f);
        y += 30f;

        y = Section(leftPanel, T("격자", "GRID"), y);
        tSnap = Txt("Snap", leftPanel, 15, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        At(tSnap.rectTransform, 18f, y, 160f, 30f);
        KeyCap(leftPanel, "^LEFT", 196f, y + 2f); KeyCap(leftPanel, "^RIGHT", 228f, y + 2f);
        y += 34f;
        tZoom = Txt("Zoom", leftPanel, 15, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        At(tZoom.rectTransform, 18f, y, 160f, 30f);
        KeyCap(leftPanel, "^UP", 196f, y + 2f); KeyCap(leftPanel, "^DOWN", 228f, y + 2f);
    }

    private float Section(RectTransform parent, string title, float y)
    {
        TextMeshProUGUI t = Txt("Section", parent, 12, TextAlignmentOptions.MidlineLeft, UiDim, FontStyles.Bold);
        t.text = title; t.characterSpacing = 5f;
        At(t.rectTransform, 18f, y, 260f, 20f);
        Image l = Panel("SecLine", parent, UiLine, 0f);
        float w = t.GetPreferredValues(title).x + 12f;
        At(l.rectTransform, 18f + w, y + 10f, Mathf.Max(0f, LeftW - 36f - w), 1f);
        return y + 28f;
    }

    private float ToggleRow(string label, string key, int id, float y)
    {
        TextMeshProUGUI l = Txt("ToggleLabel", leftPanel, 15, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        l.text = label;
        At(l.rectTransform, 18f, y, 150f, 34f);
        KeyCap(leftPanel, key, 156f, y + 4f);
        var tb = new ToggleBtn { id = id };
        tb.bg = Panel("Toggle" + id, leftPanel, UiCard, 8f);
        tb.rt = tb.bg.rectTransform;
        At(tb.rt, 196f, y, 88f, 34f);
        tb.state = Txt("T", tb.rt, 14, TextAlignmentOptions.Center, UiText, FontStyles.Bold);
        SettingsUIKit.Stretch(tb.state.rectTransform);
        toggles.Add(tb);
        return y + 42f;
    }

    private void KeyItem(Key key, string cap, string label, ref float y)
    {
        Image bg = Panel("KeyRow", leftPanel, new Color(1f, 1f, 1f, 0f), 6f);
        At(bg.rectTransform, 10f, y - 1f, 280f, 28f);
        KeyCap(leftPanel, cap, 18f, y + 1f);
        TextMeshProUGUI t = Txt("KeyLabel", leftPanel, 14, TextAlignmentOptions.MidlineLeft, UiSub);
        t.text = label;
        At(t.rectTransform, 56f, y - 1f, 230f, 28f);
        keyRows.Add(new KeyRow { key = key, bg = bg });
        y += 28f;
    }

    // 작은 키캡 (KeyHintUI 와 같은 모양)
    private void KeyCap(RectTransform parent, string key, float x, float y)
    {
        RectTransform r = KeyHintUI.Build(parent, font, font, new[] { new KeyHintUI.Item(null, key) }, 13f * fontScale, -1);
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
    }

    // ----- 오른쪽 정보 -----
    private void BuildRightPanel()
    {
        rightPanel = Panel("Inspector", hudRoot, UiPanel, 14f).rectTransform;
        SettingsUIKit.Place(rightPanel, new Vector2(1f, 0f), Vector2.one, new Vector2(-Pad - RightW, BottomH + Pad), new Vector2(-Pad, -TopH - Pad));
        float y = 16f;
        y = SectionR(T("위치", "POSITION"), y);
        tPosBig = Txt("PosBig", rightPanel, 30, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        At(tPosBig.rectTransform, 18f, y, 304f, 40f);
        tPosSub = Txt("PosSub", rightPanel, 15, TextAlignmentOptions.TopLeft, UiSub, FontStyles.Normal, true);
        At(tPosSub.rectTransform, 18f, y + 42f, 304f, 70f);
        y += 118f;

        y = SectionR(T("레인", "LANES"), y);
        tLaneState = Txt("LaneState", rightPanel, 16, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        At(tLaneState.rectTransform, 18f, y, 304f, 26f);
        laneStateFill = Bar(y + 30f);
        y += 52f;

        y = SectionR(T("오디오", "AUDIO"), y);
        tMusicVal = AudioRow(T("음악", "MUSIC"), "F1", "F2", y, out musicFill); y += 44f;
        tHitVal = AudioRow(T("탭음", "HIT"), "F3", "F4", y, out hitFill); y += 44f;
        TextMeshProUGUI le = Txt("LongEndLabel", rightPanel, 14, TextAlignmentOptions.MidlineLeft, UiSub, FontStyles.Bold);
        le.text = T("롱노트 끝 탭음", "LONG END HIT");
        At(le.rectTransform, 18f, y, 170f, 28f);
        KeyCap(rightPanel, "F5", 196f, y + 1f);
        tLongEnd = Txt("LongEnd", rightPanel, 14, TextAlignmentOptions.MidlineRight, UiText, FontStyles.Bold);
        At(tLongEnd.rectTransform, 240f, y, 82f, 28f);
        y += 40f;

        y = SectionR(T("채보", "CHART"), y);
        tStats = Txt("Stats", rightPanel, 14, TextAlignmentOptions.TopLeft, UiSub, FontStyles.Normal, true);
        At(tStats.rectTransform, 18f, y, 304f, 120f);
    }

    private float SectionR(string title, float y)
    {
        TextMeshProUGUI t = Txt("Section", rightPanel, 12, TextAlignmentOptions.MidlineLeft, UiDim, FontStyles.Bold);
        t.text = title; t.characterSpacing = 5f;
        At(t.rectTransform, 18f, y, 300f, 20f);
        float w = t.GetPreferredValues(title).x + 12f;
        Image l = Panel("SecLine", rightPanel, UiLine, 0f);
        At(l.rectTransform, 18f + w, y + 10f, RightW - 36f - w, 1f);
        return y + 28f;
    }

    private Image Bar(float y)
    {
        Image track = Panel("Track", rightPanel, new Color(1f, 1f, 1f, 0.08f), 3f);
        At(track.rectTransform, 18f, y, RightW - 36f, 6f);
        Image fill = Panel("Fill", track.rectTransform, UiAccent, 3f);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(1f, 1f);
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        return fill;
    }

    private TextMeshProUGUI AudioRow(string label, string k1, string k2, float y, out Image fill)
    {
        TextMeshProUGUI l = Txt("AudioLabel", rightPanel, 14, TextAlignmentOptions.MidlineLeft, UiSub, FontStyles.Bold);
        l.text = label;
        At(l.rectTransform, 18f, y, 80f, 24f);
        KeyCap(rightPanel, k1, 196f, y);
        KeyCap(rightPanel, k2, 236f, y);
        TextMeshProUGUI v = Txt("AudioValue", rightPanel, 14, TextAlignmentOptions.MidlineRight, UiText, FontStyles.Bold);
        At(v.rectTransform, 262f, y, 60f, 24f);
        fill = Bar(y + 28f);
        return v;
    }

    // ----- 미니맵 (곡 전체) -----
    private void BuildMiniMap()
    {
        Image bg = Panel("MiniMap", hudRoot, UiPanel, 8f);
        miniRoot = bg.rectTransform;
        SettingsUIKit.Place(miniRoot, new Vector2(1f, 0f), Vector2.one,
                            new Vector2(-Pad - RightW - 10f - MiniW, BottomH + Pad), new Vector2(-Pad - RightW - 10f, -TopH - Pad));
        miniTex = new Texture2D(4, MiniH, TextureFormat.RGBA32, false);
        miniTex.wrapMode = TextureWrapMode.Clamp;
        miniTex.filterMode = FilterMode.Bilinear;
        miniImage = SettingsUIKit.Img("Map", miniRoot, Color.white);
        miniImage.sprite = Sprite.Create(miniTex, new Rect(0, 0, 4, MiniH), new Vector2(0.5f, 0.5f), 100f);
        SettingsUIKit.Place(miniImage.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 6f), new Vector2(-4f, -6f));
        Image view = Panel("View", miniImage.rectTransform, new Color(1f, 1f, 1f, 0.16f), 3f);
        miniView = view.rectTransform;
        Image edge = SettingsUIKit.Img("Edge", miniView, new Color(1f, 1f, 1f, 0.75f));
        SettingsUIKit.Place(edge.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(-3f, 0f), new Vector2(3f, 2f));
    }

    // ----- 아래 막대 -----
    private void BuildBottomBar()
    {
        bottomBar = Panel("BottomBar", hudRoot, UiPanel, 0f).rectTransform;
        SettingsUIKit.Place(bottomBar, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, BottomH));
        Image line = Panel("Line", bottomBar, UiLine, 0f);
        SettingsUIKit.Place(line.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -1f), Vector2.zero);
    }

    private RectTransform bottomHints;
    private void BuildBottomHints()
    {
        if (bottomHints != null) Destroy(bottomHints.gameObject);
        bottomHints = KeyHintUI.Build(bottomBar, font, font, new[]
        {
            new KeyHintUI.Item(T("놓기 · 지우기", "place · delete"), "L-CLICK", "R-CLICK"),
            new KeyHintUI.Item(T("재생", "play"), "SPACE"),
            new KeyHintUI.Item(T("이동 (SHIFT 1마디)", "move (SHIFT bar)"), "WHEEL"),
            new KeyHintUI.Item(T("1마디", "bar"), "PGUP", "PGDN"),
            new KeyHintUI.Item(T("오프셋", "offset"), "-", "=", "O"),
            new KeyHintUI.Item(T("저장", "save"), "S"),
            new KeyHintUI.Item(T("도움말", "help"), "H"),
            new KeyHintUI.Item(T("언어", "lang"), "L"),
            new KeyHintUI.Item(T("3번 = 저장 후 나가기", "x3 save & exit"), "ESC"),
        }, 14f * fontScale, -1, UiSub);
        bottomHints.anchorMin = bottomHints.anchorMax = new Vector2(0f, 0.5f);
        bottomHints.anchoredPosition = new Vector2(Pad + 4f, 0f);
    }

    // ----- 알림 (토스트) -----
    private void BuildToast()
    {
        toastBg = Panel("Toast", hudRoot, new Color(0.10f, 0.12f, 0.16f, 0.96f), 18f);
        RectTransform r = toastBg.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
        r.pivot = new Vector2(0.5f, 0f);
        r.anchoredPosition = new Vector2(-20f, BottomH + Pad + 8f);
        r.sizeDelta = new Vector2(400f, 40f);
        toastGroup = toastBg.gameObject.AddComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        Image accent = Panel("Accent", r, UiAccent, 3f);
        accent.rectTransform.anchorMin = new Vector2(0f, 0.25f); accent.rectTransform.anchorMax = new Vector2(0f, 0.75f);
        accent.rectTransform.offsetMin = new Vector2(14f, 0f); accent.rectTransform.offsetMax = new Vector2(18f, 0f);
        tToast = Txt("ToastText", r, 16, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        SettingsUIKit.Place(tToast.rectTransform, Vector2.zero, Vector2.one, new Vector2(30f, 0f), new Vector2(-18f, 0f));
    }

    // ----- 숫자 입력창 -----
    private void BuildPrompt()
    {
        Image dim = SettingsUIKit.Img("PromptDim", hudRoot, new Color(0f, 0f, 0f, 0.55f), true);
        SettingsUIKit.Stretch(dim.rectTransform);
        promptPanel = dim.gameObject;
        Image panel = Panel("Prompt", dim.rectTransform, new Color(0.08f, 0.09f, 0.125f, 0.98f), 16f);
        SettingsUIKit.Center(panel.rectTransform, Vector2.zero, new Vector2(620f, 236f));
        Image line = Panel("Accent", panel.rectTransform, UiAccent, 2f);
        SettingsUIKit.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -4f), new Vector2(-24f, -1f));
        promptTitle = Txt("Title", panel.transform, 26, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
        SettingsUIKit.Place(promptTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -64f), new Vector2(-20f, -18f));
        promptHint = Txt("Hint", panel.transform, 16, TextAlignmentOptions.Center, UiSub);
        SettingsUIKit.Place(promptHint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -96f), new Vector2(-20f, -64f));
        Image box = Panel("ValueBox", panel.rectTransform, new Color(0f, 0f, 0f, 0.4f), 10f);
        SettingsUIKit.Place(box.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(110f, 56f), new Vector2(-110f, 122f));
        promptValue = Txt("Value", box.rectTransform, 36, TextAlignmentOptions.Center, UiAccent, FontStyles.Bold);
        SettingsUIKit.Stretch(promptValue.rectTransform);
        promptKeys = Txt("Keys", panel.transform, 14, TextAlignmentOptions.Center, UiDim);
        SettingsUIKit.Place(promptKeys.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 14f), new Vector2(-20f, 44f));
        promptPanel.SetActive(false);
    }

    // ----- 도움말 (H) -----
    private void BuildHelp()
    {
        if (helpRoot != null) Destroy(helpRoot.gameObject);
        Image dim = SettingsUIKit.Img("HelpDim", hudRoot, new Color(0f, 0f, 0f, 0.6f), true);
        SettingsUIKit.Stretch(dim.rectTransform);
        helpRoot = dim.rectTransform;
        Image panel = Panel("Help", helpRoot, new Color(0.075f, 0.085f, 0.12f, 0.98f), 18f);
        SettingsUIKit.Center(panel.rectTransform, Vector2.zero, new Vector2(1320f, 700f));
        RectTransform pr = panel.rectTransform;
        TextMeshProUGUI title = Txt("Title", pr, 28, TextAlignmentOptions.MidlineLeft, UiText, FontStyles.Bold);
        title.text = T("단축키", "SHORTCUTS");
        At(title.rectTransform, 36f, 26f, 400f, 44f);
        RectTransform close = KeyHintUI.Build(pr, font, font, new[] { new KeyHintUI.Item(T("닫기", "close"), "H"), new KeyHintUI.Item(null, "ESC") }, 15f * fontScale, 1, UiSub);
        close.anchorMin = close.anchorMax = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-36f, -48f);

        string[][] cols =
        {
            new[] { T("노트", "NOTES"),
                    "L-CLICK|" + T("노트 놓기", "place note"), "R-CLICK|" + T("노트 지우기", "delete note"),
                    "1|" + T("탭", "tap"), "2|" + T("롱노트 시작", "long start"), "3|" + T("롱노트 끝", "long end"), "4|" + T("마디선", "measure line"),
                    "5|" + T("RED 켜기 / 끄기", "RED on / off"), "6|" + T("드래그 켜기 / 끄기", "DRAG on / off"),
                    "Q|" + T("층 위로 (1 → 5, SHIFT + Q 아래로)", "layer up (1 → 5, SHIFT + Q down)"),
                    "7|" + T("슬라이드 편집 (롱노트 클릭 → 사이 클릭 = 중계점 · 점 끌기 = 레인)", "SLIDE EDIT (click long note → click between = point · drag)"),
                    "C|" + T("슬라이드 편집 중: 구간 곡선 (직선 → IN → OUT → 직각)", "slide edit: segment curve (line → IN → OUT → CORNER)"),
                    "DRAG|" + T("클릭한 채 오른쪽으로 = 폭 1~4칸", "drag right = width 1-4") },
            new[] { T("레인 · 타이밍", "LANES · TIMING"),
                    "V|" + T("+ 클릭  레인 사라짐 (시간 입력)", "+ click  lanes disappear"), "B|" + T("+ 클릭  레인 드러남 (시간 입력)", "+ click  lanes reappear"),
                    "U|" + T("+ 클릭  변속에 SPEED UP / DOWN 글자 켜고 끄기", "+ click  speed UP / DOWN notice on / off"),
                    "G|" + T("+ 클릭  난이도 공개 (? → 실제 값, 하나만)", "+ click  level reveal (? → real, one only)"),
                    "D|" + T("+ 클릭  BPM", "+ click  BPM"), "F|" + T("+ 클릭  변속 (편집 중인 층만)", "+ click  speed (this layer)"), "SHIFT|" + T("+ F + 클릭  변속 (모든 층)", "+ F + click  speed (all layers)"),
                    "E|" + T("+ 클릭  정지 STOP", "+ click  STOP"), "R|" + T("+ 클릭  재개 START", "+ click  START"),
                    "KEY|" + T("+ 우클릭  그 표시 삭제", "+ R-click  delete") },
            new[] { T("이동 · 재생 · 파일", "VIEW · PLAY · FILE"),
                    "SPACE|" + T("재생 / 정지", "play / stop"), "WHEEL|" + T("이동 (SHIFT = 1마디)", "move (SHIFT = bar)"),
                    "^LEFT|" + T("박자 단위 ← →", "snap ← →"), "^UP|" + T("확대 / 축소 ↑ ↓", "zoom ↑ ↓"),
                    "PGUP|" + T("1마디 앞 / 뒤 (PGDN)", "bar up / down (PGDN)"), "HOME|" + T("처음으로", "go to start"),
                    "-|" + T("오프셋 -1ms / = +1ms (SHIFT 10ms)", "offset ∓1ms (SHIFT 10)"), "O|" + T("오프셋 직접 입력", "type offset"),
                    "F1|" + T("음악 볼륨 (F2 올림)", "music vol (F2 up)"), "F3|" + T("탭음 볼륨 (F4 올림)", "hit vol (F4 up)"),
                    "F5|" + T("롱노트 끝 탭음", "long end hit sound"),
                    "S|" + T("저장", "save"), "ESC|" + T("3번 = 저장 후 나가기", "x3 = save & exit"), "L|" + T("한국어 / English", "한국어 / English") },
        };
        for (int c = 0; c < cols.Length; c++)
        {
            float x = 36f + c * 424f, y = 96f;
            TextMeshProUGUI h = Txt("ColTitle", pr, 13, TextAlignmentOptions.MidlineLeft, UiAccent, FontStyles.Bold);
            h.text = cols[c][0]; h.characterSpacing = 4f;
            At(h.rectTransform, x, y, 400f, 22f);
            y += 34f;
            for (int i = 1; i < cols[c].Length; i++)
            {
                string[] kv = cols[c][i].Split('|');
                RectTransform k = KeyHintUI.Build(pr, font, font, new[] { new KeyHintUI.Item(null, kv[0]) }, 13f * fontScale, -1);
                k.anchorMin = k.anchorMax = new Vector2(0f, 1f);
                k.pivot = new Vector2(0f, 0.5f);
                k.anchoredPosition = new Vector2(x, -(y + 14f));
                TextMeshProUGUI t = Txt("Desc", pr, 15, TextAlignmentOptions.MidlineLeft, UiSub);
                t.text = kv[1];
                At(t.rectTransform, x + 92f, y, 320f, 28f);
                y += 36f;
            }
        }
        helpRoot.gameObject.SetActive(helpOpen);
    }

    // 언어가 바뀔 때 다시 만드는 부분 (도구 · 정보 패널은 글자만 다시 씀)
    private void RefreshStaticTexts()
    {
        if (promptKeys != null) promptKeys.text = T("ENTER 적용   ·   ESC 취소", "ENTER apply   ·   ESC cancel");
        if (bottomBar != null) BuildBottomHints();
        if (hudRoot != null && leftPanel != null)
        {
            // 왼쪽 · 오른쪽 · 도움말은 글자가 많아 통째로 다시 만듦
            Destroy(leftPanel.gameObject); Destroy(rightPanel.gameObject);
            toolCards.Clear(); toggles.Clear(); keyRows.Clear();
            BuildLeftPanel(); BuildRightPanel();
            leftPanel.SetSiblingIndex(1); rightPanel.SetSiblingIndex(2);
            BuildHelp();
            if (toastBg != null) toastBg.transform.SetAsLastSibling();
            if (promptPanel != null) promptPanel.transform.SetAsLastSibling();
            if (helpRoot != null) helpRoot.SetAsLastSibling();
        }
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    private void DrawHud()
    {
        if (hudRoot == null) return;
        int div = SnapDivs[snapIndex];
        double viewTime = playing ? playTime : TimeOf(cursorBeat);
        double beatNow = playing ? BeatOf(playTime) : cursorBeat;
        long m = (long)Math.Floor(beatNow / 4.0) + 1;
        double inBar = beatNow - (m - 1) * 4.0;
        double bpmNow = initialBpm;
        foreach (var sg in segs) { if (sg.Beat <= beatNow + 1e-6) bpmNow = sg.Bpm; else break; }
        double speedNow = initialSpeed;
        foreach (var sp in speeds) { if (sp.Beat <= beatNow + 1e-6 && (sp.Layer == 0 || sp.Layer == editLayer)) speedNow = sp.Value; }   // 편집 중인 층 기준

        // ----- 위 막대 -----
        tTitle.text = title;
        tDiff.text = DifficultyHelper.ColoredText(levelId, DifficultyHelper.GetName(levelId));   // SPECIAL 은 솜사탕 그라데이션
        playIcon.enabled = !playing;
        pauseL.enabled = pauseR.enabled = playing;
        tTime.text = "<mspace=0.6em>" + FormatTime(viewTime) + "</mspace>";
        tPos.text = T("마디 ", "BAR ") + m + "  ·  " + T("박 ", "BEAT ") + (inBar + 1.0).ToString("0.##", CultureInfo.InvariantCulture);
        tBpm.text = "<color=#FFFFFF80>BPM</color>  " + bpmNow.ToString("0.##", CultureInfo.InvariantCulture);
        tOffset.text = "<color=#FFFFFF80>OFFSET</color>  " + ChartOffset.Display(audioStart);

        sigTimer -= Time.unscaledDeltaTime;
        if (sigTimer <= 0f) { sigTimer = 0.25f; curSig = ContentSignature(); }
        bool dirty = curSig != savedSig;
        tSave.text = dirty ? T("수정됨", "UNSAVED") : T("저장됨", "SAVED");
        tSave.color = dirty ? UiWarn : UiSub;
        saveDot.color = dirty ? UiWarn : new Color(0.4f, 1f, 0.6f, 1f);
        saveDot.rectTransform.anchoredPosition = new Vector2(-Pad - 10f - tSave.GetPreferredValues(tSave.text).x, 0f);

        // ----- 왼쪽 도구 -----
        foreach (var c in toolCards)
        {
            bool on = c.kind == noteType;
            c.bg.color = on ? UiCardOn : UiCard;
            c.edge.color = on ? UiAccent : new Color(1f, 1f, 1f, 0f);
            Color nc = c.kind == 4 ? new Color(1f, 0.85f, 0.4f) : (redMode && c.kind <= 3 ? UiRed : (dragMode && c.kind <= 2 ? NoteArt.DragColor : SimpleGameSkin.LaneColor(1)));
            c.swatch.color = nc;
            string nm = c.kind == 1 ? T("탭", "TAP") : c.kind == 2 ? T("롱노트 시작", "LONG START") : c.kind == 3 ? T("롱노트 끝", "LONG END") : T("마디선", "MEASURE");
            if (c.kind <= 2 && dragMode) nm = c.kind == 1 ? T("드래그", "DRAG") : T("드래그 롱 시작", "DRAG LONG");
            if (c.kind <= 3 && redMode) nm = "RED " + (c.kind == 1 ? T("탭", "TAP") : c.kind == 2 ? T("시작", "START") : T("끝", "END"));
            c.name.text = nm;
            c.name.color = on ? UiText : UiSub;
        }
        foreach (var tb in toggles)
        {
            bool on = tb.id == 5 ? redMode : tb.id == 6 ? dragMode : tb.id == 7 ? slideMode : editLayer == tb.id - 20;
            Color onC = tb.id == 5 ? UiRed : tb.id == 6 ? NoteArt.DragColor : tb.id == 7 ? SlideUiColor : UiAccent;
            tb.bg.color = on ? new Color(onC.r, onC.g, onC.b, 0.22f) : UiCard;
            if (tb.id == 5 || tb.id == 6 || tb.id == 7) tb.state.text = on ? "ON" : "OFF";
            tb.state.color = on ? onC : UiDim;
        }
        var kbd = Keyboard.current;
        foreach (var kr in keyRows)
        {
            bool held = kbd != null && kbd[kr.key].isPressed && !playing;
            kr.bg.color = held ? new Color(UiAccent.r, UiAccent.g, UiAccent.b, 0.16f) : new Color(1f, 1f, 1f, 0f);
        }
        tSnap.text = T("박자 단위  ", "SNAP  ") + "<color=#5ce1e6>1/" + (div * 4) + "</color>";
        tZoom.text = T("확대  ", "ZOOM  ") + "<color=#5ce1e6>" + Mathf.RoundToInt(beatSpacing / 2.4f * 100f) + "%</color>";

        // ----- 오른쪽 정보 -----
        tPosBig.text = "<mspace=0.6em>" + m + "</mspace><size=55%><color=#FFFFFF80>  " + T("마디", "BAR") + "   +" + inBar.ToString("0.###", CultureInfo.InvariantCulture) + T(" 박", " beat") + "</color></size>";
        tPosSub.text = T("시간  ", "TIME  ") + viewTime.ToString("0.000", CultureInfo.InvariantCulture) + " s\n" +
                       "BPM  " + bpmNow.ToString("0.##", CultureInfo.InvariantCulture) + "     " + T($"변속({editLayer}층)  x", $"SPEED(L{editLayer})  x") + speedNow.ToString("0.00", CultureInfo.InvariantCulture) + "\n" +
                       T("층  ", "LAYER  ") + T($"{editLayer}층", $"L{editLayer}") + (editLayer == NoteLayer.Top ? T(" (기본)", " (default)") : "");

        float shown = FoldAt(viewTime);
        tLaneState.text = shown > 0.999f ? T("보임", "VISIBLE")
                        : shown < 0.001f ? "<color=#ff9a4d>" + T("사라짐", "HIDDEN") + "</color>"
                        : "<color=#ff9a4d>" + T("바뀌는 중 ", "CHANGING ") + Mathf.RoundToInt(shown * 100f) + "%</color>";
        laneStateFill.rectTransform.anchorMax = new Vector2(shown, 1f);
        laneStateFill.color = shown > 0.999f ? UiAccent : UiWarn;

        tMusicVal.text = Mathf.RoundToInt(musicVolume * 100f) + "%";
        musicFill.rectTransform.anchorMax = new Vector2(musicVolume, 1f);
        tHitVal.text = Mathf.RoundToInt(hitVolume * 100f) + "%";
        hitFill.rectTransform.anchorMax = new Vector2(hitVolume, 1f);
        tLongEnd.text = longHitSound ? "<color=#5ce1e6>ON</color>" : "<color=#FFFFFF55>OFF</color>";

        int nTap = 0, nLong = 0, nRed = 0, nDrag = 0, nSlide = 0;
        foreach (var n in notes)
        {
            if (n.Type == 4) continue;
            if (n.Red) nRed++;
            if (NoteTypes.IsDrag(n.Type)) nDrag++;
            if (n.SlidePts != null) nSlide++;
            if (NoteTypes.IsLongHead(n.Type)) nLong++; else if (n.Type != 3 && n.Type != 13 && !NoteTypes.IsSlideRelay(n.Type)) nTap++;
        }
        tStats.text = T($"한 번 치는 노트  {nTap}\n롱노트  {nLong}  (슬라이드 {nSlide})\nRED  {nRed}   ·   드래그  {nDrag}\n레인 사라짐 / 드러남  {folds.Count}",
                        $"TAP-LIKE  {nTap}\nLONG  {nLong}  (SLIDE {nSlide})\nRED  {nRed}   ·   DRAG  {nDrag}\nLANE HIDE / SHOW  {folds.Count}");

        // ----- 미니맵 · 알림 · 입력창 -----
        UpdateMiniMap(beatNow);
        bool toast = statusTimer > 0f;
        if (toast) tToast.text = status;
        float ta = toast ? Mathf.Clamp01(statusTimer / 0.4f) : 0f;
        toastGroup.alpha = Mathf.MoveTowards(toastGroup.alpha, ta, Time.unscaledDeltaTime * 6f);
        if (toast) toastBg.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(tToast.GetPreferredValues(status).x + 56f, 200f, 1100f), 40f);
        if (promptPanel.activeSelf) promptValue.text = promptBuffer + (Mathf.PingPong(Time.unscaledTime * 2f, 1f) > 0.5f ? "|" : " ");
        if (helpRoot != null && helpRoot.gameObject.activeSelf != helpOpen) helpRoot.gameObject.SetActive(helpOpen);

        DrawWorldLabels();
    }

    private static string FormatTime(double t)
    {
        bool neg = t < 0; t = Math.Abs(t);
        int min = (int)(t / 60.0);
        double sec = t - min * 60.0;
        return (neg ? "-" : "") + min.ToString("00") + ":" + sec.ToString("00.000", CultureInfo.InvariantCulture);
    }

    // =====================================================================
    //  이벤트 아이콘 (BPM · TIMING · 변속 · STOP · START · 레인 사라짐 / 드러남)
    //   레인 오른쪽에 [아이콘 배지] + 글자, 레인 오른쪽 벽에는 같은 색 눈금
    // =====================================================================
    private enum EvIcon { Bpm, Timing, SpeedUp, SpeedDown, Stop, Start, Hide, Show, Reveal }
    private struct EvItem { public double Beat; public EvIcon Icon; public string Text; public Color Color; public int Layer; }   // Layer: 0 = 모든 층(표시 없음), 1 ~ 5 = 층 배지
    private readonly List<EvItem> evItems = new List<EvItem>();
    private static readonly Dictionary<EvIcon, Sprite> iconSprites = new Dictionary<EvIcon, Sprite>();
    private class IconLabel { public RectTransform rt; public Image bg, glyph, layerBg; public TextMeshProUGUI text, layerText; }
    private readonly List<IconLabel> iconPool = new List<IconLabel>();
    private int iconUsed;

    private static readonly Color EvBpm = new Color(1f, 0.70f, 0.22f), EvSpeed = new Color(0.80f, 0.50f, 1f),
                                  EvStop = new Color(1f, 0.34f, 0.34f), EvStart = new Color(0.34f, 0.92f, 0.52f),
                                  EvHide = new Color(1f, 0.62f, 0.30f), EvShow = new Color(0.50f, 1f, 0.62f),
                                  EvReveal = new Color(1f, 0.86f, 0.40f);

    // 화면에 보이는 박 범위의 이벤트 모으기 (시간 순)
    private void CollectEvents()
    {
        evItems.Clear();
        foreach (var b in bpms)
            evItems.Add(new EvItem { Beat = b.Beat, Icon = b.HasAnchor ? EvIcon.Timing : EvIcon.Bpm, Color = EvBpm,
                                     Text = (b.HasAnchor ? "TIMING " : "BPM ") + b.Bpm.ToString("0.##", CultureInfo.InvariantCulture) });
        // 변속: 같은 층(모든 층 = 0 포함)의 바로 앞 값보다 빠르면 ▲, 느리면 ▼
        foreach (var sp in speeds)
        {
            double prev = initialSpeed;
            foreach (var o in speeds)
            {
                if (o == sp || o.Beat >= sp.Beat - 1e-9) continue;
                if (o.Layer == 0 || sp.Layer == 0 || o.Layer == sp.Layer) prev = o.Value;
            }
            // SPEED UP / DOWN 표시가 켜진 변속: 값 뒤에 흰 [SPEED UP] / [SPEED DOWN]
            string notice = sp.Show ? "  <color=#FFFFFF><size=80%>[SPEED " + (sp.Value >= prev ? "UP" : "DOWN") + "]</size></color>" : "";
            evItems.Add(new EvItem { Beat = sp.Beat, Icon = sp.Value >= prev ? EvIcon.SpeedUp : EvIcon.SpeedDown, Color = EvSpeed,
                                     Text = sp.Value.ToString("0.00", CultureInfo.InvariantCulture) + notice, Layer = sp.Layer });
        }
        foreach (var k in marks)
            evItems.Add(new EvItem { Beat = k.Beat, Icon = k.IsStop ? EvIcon.Stop : EvIcon.Start, Color = k.IsStop ? EvStop : EvStart,
                                     Text = k.IsStop ? "STOP" : "START" });
        foreach (var f in folds)
            evItems.Add(new EvItem { Beat = f.Beat, Icon = f.Fold ? EvIcon.Hide : EvIcon.Show, Color = f.Fold ? EvHide : EvShow,
                                     Text = (f.Fold ? "HIDE " : "SHOW ") + f.Duration.ToString("0.###", CultureInfo.InvariantCulture) + "s" });
        if (reveal != null)
        {
            // 난이도 공개: "LEVEL ? → 17" (숨긴 난이도가 아니면 "LEVEL REVEAL")
            SongData sd = CoreManager.Instance != null ? CoreManager.Instance.CurrentSongData : null;
            string txt = sd != null && DifficultyHelper.IsHidden(sd, levelId)
                       ? "LEVEL ? → " + SongRowUI.FormatDifficulty(DifficultyHelper.GetRealValue(sd, levelId)) : "LEVEL REVEAL";
            evItems.Add(new EvItem { Beat = reveal.Beat, Icon = EvIcon.Reveal, Color = EvReveal, Text = txt });
        }
        evItems.Sort((x, y) => x.Beat.CompareTo(y.Beat));
    }

    // (월드) 레인 오른쪽 벽 바깥 눈금 — Draw 안에서 부름
    private void DrawEventTicks(double b0, double b1)
    {
        CollectEvents();                                                   // (눈금은 그리지 않음 — 아이콘만)
    }

    // 박자선 옆 표시 (마디 번호 · 이벤트 아이콘)
    private void DrawWorldLabels()
    {
        labelUsed = 0;
        iconUsed = 0;
        float bottom = -5.5f, top = 5.5f;
        double b0 = cursorBeat + (bottom - HitY) / beatSpacing, b1 = cursorBeat + (top - HitY) / beatSpacing;
        for (long bar = Math.Max(1, (long)Math.Floor(b0 / 4.0)); bar <= (long)Math.Ceiling(b1 / 4.0) + 1; bar++)
        {
            double bb = (bar - 1) * 4.0;
            if (bb < b0 || bb > b1) continue;
            Label(Left - 0.18f, Y(bb), "<size=70%><color=#FFFFFF60>#</color></size>" + bar, new Color(1f, 1f, 1f, 0.62f), false);
        }
        double lastBeat = double.NaN; int stack = 0;
        foreach (var e in evItems)
        {
            if (e.Beat < b0 || e.Beat > b1) continue;
            stack = (!double.IsNaN(lastBeat) && Math.Abs(e.Beat - lastBeat) < 1e-6) ? stack + 1 : 0;
            lastBeat = e.Beat;
            IconAt(Right + 0.26f, Y(e.Beat), e, stack);
        }
        DrawSlideLabels();   // 슬라이드 편집 중: 구간 곡선 이름
        DrawSpeedNoticePreview();   // 재생 중: SPEED UP / DOWN 미리보기
        // 레인 번호 (판정선 아래)
        for (int l = 0; l < 4; l++) LabelCenter(LaneX(l), HitY - 0.32f, (l + 1).ToString(), new Color(1f, 1f, 1f, 0.35f));
        for (int i = labelUsed; i < labelPool.Count; i++) labelPool[i].gameObject.SetActive(false);
        for (int i = iconUsed; i < iconPool.Count; i++) iconPool[i].rt.gameObject.SetActive(false);
    }

    // ----- 재생 중 SPEED UP / DOWN 미리보기 (게임과 같은 색 · 레인 위쪽 가운데) -----
    private double noticePrevBeat = double.NaN;
    private float noticeAge = 99f;
    private bool noticeUp;

    // 같은 층(모든 층 = 0 포함)의 바로 앞 변속 값 (없으면 처음 배속)
    private double PrevSpeedValue(ASpeed sp)
    {
        double prev = initialSpeed, prevBeat = double.NegativeInfinity;
        foreach (var o in speeds)
        {
            if (o == sp || o.Beat >= sp.Beat - 1e-9 || o.Beat < prevBeat) continue;
            if (o.Layer == 0 || sp.Layer == 0 || o.Layer == sp.Layer) { prev = o.Value; prevBeat = o.Beat; }
        }
        return prev;
    }

    private void DrawSpeedNoticePreview()
    {
        noticeAge += Time.unscaledDeltaTime;
        if (!playing || double.IsNaN(noticePrevBeat) || cursorBeat < noticePrevBeat - 1e-6) noticePrevBeat = cursorBeat;   // 멈춤 · 되감기: 띄우지 않음
        else
        {
            foreach (var sp in speeds)
            {
                if (!sp.Show || sp.Beat <= noticePrevBeat || sp.Beat > cursorBeat) continue;
                double from = PrevSpeedValue(sp);
                if (from <= 0 || sp.Value <= 0 || Math.Abs(sp.Value - from) < 1e-6) continue;   // 0 이 끼는 변화(정지)는 표시 안 함 (게임과 같음)
                noticeUp = sp.Value > from;
                noticeAge = 0f;
            }
            noticePrevBeat = cursorBeat;
        }
        const float In = 0.22f, Hold = 1.1f, Out = 0.3f;
        float t = noticeAge;
        if (t >= Hold + Out) return;
        float a = t < In ? t / In : t < Hold ? 1f : Mathf.Clamp01(1f - (t - Hold) / Out);
        float slide = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / In), 3f);
        Color c = noticeUp ? new Color(1f, 0.74f, 0.38f, a) : new Color(0.48f, 0.80f, 1f, a);
        float cx = (LaneX(1) + LaneX(2)) * 0.5f;
        float y = 4.6f + (noticeUp ? -0.12f : 0.12f) * (1f - slide);   // UP: 아래에서 올라옴 / DOWN: 위에서 내려옴
        LabelCenter(cx, y, "<size=170%><cspace=0.35em>" + (noticeUp ? "SPEED UP" : "SPEED DOWN") + "</cspace></size>", c);
    }

    private void IconAt(float wx, float wy, EvItem e, int stack)
    {
        if (cam == null) return;
        IconLabel il;
        if (iconUsed < iconPool.Count) il = iconPool[iconUsed];
        else
        {
            il = new IconLabel();
            il.rt = SettingsUIKit.Rect("EventIcon", labelRoot);
            il.rt.anchorMin = il.rt.anchorMax = Vector2.zero;
            il.rt.pivot = new Vector2(0f, 0.5f);
            il.rt.sizeDelta = new Vector2(280f, 26f);
            il.bg = Panel("Badge", il.rt, Color.white, 7f);
            il.bg.rectTransform.anchorMin = il.bg.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            il.bg.rectTransform.pivot = new Vector2(0f, 0.5f);
            il.bg.rectTransform.sizeDelta = new Vector2(24f, 24f);
            il.bg.rectTransform.anchoredPosition = Vector2.zero;
            il.glyph = SettingsUIKit.Img("Glyph", il.bg.rectTransform, Color.white);
            il.glyph.rectTransform.anchorMin = il.glyph.rectTransform.anchorMax = il.glyph.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            il.glyph.rectTransform.sizeDelta = new Vector2(16f, 16f);
            il.text = Txt("Text", il.rt, 16, TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold);
            il.text.rectTransform.anchorMin = new Vector2(0f, 0f); il.text.rectTransform.anchorMax = new Vector2(1f, 1f);
            il.text.rectTransform.offsetMin = new Vector2(31f, 0f); il.text.rectTransform.offsetMax = Vector2.zero;
            // 층 배지 [L1] [L2] (층별 변속일 때만)
            il.layerBg = Panel("LayerBadge", il.rt, Color.white, 6f);
            il.layerBg.rectTransform.anchorMin = il.layerBg.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            il.layerBg.rectTransform.pivot = new Vector2(0f, 0.5f);
            il.layerBg.rectTransform.sizeDelta = new Vector2(30f, 20f);
            il.layerText = Txt("LayerText", il.layerBg.rectTransform, 13, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            SettingsUIKit.Stretch(il.layerText.rectTransform);
            iconPool.Add(il);
        }
        iconUsed++;
        il.rt.gameObject.SetActive(true);
        Color c = e.Color;
        il.bg.color = new Color(c.r, c.g, c.b, 0.20f);
        bool down = e.Icon == EvIcon.SpeedDown;
        il.glyph.sprite = IconSprite(down ? EvIcon.SpeedUp : e.Icon);
        il.glyph.rectTransform.localEulerAngles = new Vector3(0f, 0f, down ? 180f : 0f);
        il.glyph.color = new Color(c.r, c.g, c.b, Mathf.Max(0.75f, c.a));
        il.text.text = e.Text;
        il.text.color = c;
        bool hasLayer = e.Layer >= NoteLayer.Min && e.Layer <= NoteLayer.Max;
        il.layerBg.gameObject.SetActive(hasLayer);
        if (hasLayer)
        {
            il.layerText.text = "L" + e.Layer;
            Color lc = LayerBadgeColor(e.Layer);
            il.layerText.color = lc;
            il.layerBg.color = new Color(lc.r, lc.g, lc.b, 0.18f);
            il.layerBg.rectTransform.anchoredPosition = new Vector2(31f + il.text.GetPreferredValues(e.Text).x + 8f, 0f);
        }
        Vector3 sp = cam.WorldToScreenPoint(new Vector3(wx, wy, 0f));
        float scale = hud.scaleFactor > 0f ? hud.scaleFactor : 1f;
        il.rt.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale + stack * 26f);
    }

    // 아이콘 그림 (코드로 그림, 흰색 → 색은 Image.color). 좌표 u, v: -1 ~ 1 (위가 +)
    private static Sprite IconSprite(EvIcon kind)
    {
        Sprite sp;
        if (iconSprites.TryGetValue(kind, out sp) && sp != null) return sp;
        const int S = 48;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        float aa = 2.2f / S;                                              // 가장자리 부드럽게 (약 1px)
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float d = IconDist(kind, new Vector2(u, v));           // 모양 안쪽 = 음수
                float a = Mathf.Clamp01(0.5f - d / aa);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        sp = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        iconSprites[kind] = sp;
        return sp;
    }

    // 모양별 거리 (음수 = 안쪽)
    private static float IconDist(EvIcon k, Vector2 p)
    {
        const float W = 0.11f;                                            // 선 두께 (반)
        switch (k)
        {
            case EvIcon.Bpm:
            {
                // 4분음표: 기울어진 타원 머리 + 기둥
                Vector2 q = p - new Vector2(-0.22f, -0.48f);
                float c = Mathf.Cos(0.45f), s = Mathf.Sin(0.45f);
                Vector2 r = new Vector2(c * q.x + s * q.y, -s * q.x + c * q.y);
                float head = (new Vector2(r.x / 0.36f, r.y / 0.25f)).magnitude - 1f;
                head *= 0.25f;
                float stem = SegDist(p, new Vector2(0.11f, -0.40f), new Vector2(0.11f, 0.78f)) - 0.075f;
                float flag = SegDist(p, new Vector2(0.11f, 0.78f), new Vector2(0.48f, 0.42f)) - 0.075f;
                return Mathf.Min(head, Mathf.Min(stem, flag));
            }
            case EvIcon.Timing:
            {
                // 시계: 원 테두리 + 두 바늘
                float ring = Mathf.Abs(p.magnitude - 0.72f) - 0.09f;
                float h1 = SegDist(p, Vector2.zero, new Vector2(0f, 0.48f)) - 0.075f;
                float h2 = SegDist(p, Vector2.zero, new Vector2(0.36f, 0f)) - 0.075f;
                return Mathf.Min(ring, Mathf.Min(h1, h2));
            }
            case EvIcon.SpeedUp:
            case EvIcon.SpeedDown:
            {
                // 위쪽 겹화살표 (∧∧)
                float a1 = Mathf.Min(SegDist(p, new Vector2(-0.6f, -0.05f), new Vector2(0f, 0.55f)), SegDist(p, new Vector2(0f, 0.55f), new Vector2(0.6f, -0.05f))) - W;
                float a2 = Mathf.Min(SegDist(p, new Vector2(-0.6f, -0.6f), new Vector2(0f, 0f)), SegDist(p, new Vector2(0f, 0f), new Vector2(0.6f, -0.6f))) - W;
                return Mathf.Min(a1, a2);
            }
            case EvIcon.Stop:
                return RoundBox(p, new Vector2(0.52f, 0.52f), 0.12f);
            case EvIcon.Start:
            {
                // 오른쪽 삼각형 (재생)
                float e1 = Line(p, new Vector2(-0.45f, 0.62f), new Vector2(0.65f, 0f));
                float e2 = Line(p, new Vector2(0.65f, 0f), new Vector2(-0.45f, -0.62f));
                float e3 = -(p.x + 0.45f);
                return Mathf.Max(e1, Mathf.Max(e2, e3)) - 0.02f;
            }
            case EvIcon.Hide:
            {
                // 위쪽 막대 + 그 막대로 올라가는 화살표 (빨려 들어감)
                float bar = RoundBox(p - new Vector2(0f, 0.68f), new Vector2(0.66f, 0.07f), 0.05f);
                float shaft = SegDist(p, new Vector2(0f, -0.66f), new Vector2(0f, 0.28f)) - W;
                float head = Mathf.Min(SegDist(p, new Vector2(-0.36f, 0f), new Vector2(0f, 0.36f)), SegDist(p, new Vector2(0f, 0.36f), new Vector2(0.36f, 0f))) - W;
                return Mathf.Min(bar, Mathf.Min(shaft, head));
            }
            case EvIcon.Reveal:
            {
                // 눈: 아몬드 모양 테두리 + 가운데 눈동자 (드러남)
                float lens = Mathf.Max((p - new Vector2(0f, -0.62f)).magnitude - 1.0f, (p - new Vector2(0f, 0.62f)).magnitude - 1.0f);
                float outline = Mathf.Abs(lens) - 0.09f;
                float pupil = p.magnitude - 0.21f;
                return Mathf.Min(outline, pupil);
            }
            case EvIcon.Show:
            {
                // 아래쪽 막대 + 거기서 솟아오르는 화살표 (아래에서 올라옴)
                float bar = RoundBox(p - new Vector2(0f, -0.68f), new Vector2(0.66f, 0.07f), 0.05f);
                float shaft = SegDist(p, new Vector2(0f, -0.42f), new Vector2(0f, 0.62f)) - W;
                float head = Mathf.Min(SegDist(p, new Vector2(-0.36f, 0.3f), new Vector2(0f, 0.66f)), SegDist(p, new Vector2(0f, 0.66f), new Vector2(0.36f, 0.3f))) - W;
                return Mathf.Min(bar, Mathf.Min(shaft, head));
            }
        }
        return 1f;
    }

    private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    // a → b 직선의 오른쪽(시계 방향 안쪽)이 음수
    private static float Line(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 d = (b - a).normalized;
        Vector2 n = new Vector2(d.y, -d.x);
        return -Vector2.Dot(p - a, n);
    }

    private static float RoundBox(Vector2 p, Vector2 half, float r)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(r, r);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }

    private static KeyValuePair<double, KeyValuePair<string, Color>> Ev(double beat, string text, Color c)
    {
        return new KeyValuePair<double, KeyValuePair<string, Color>>(beat, new KeyValuePair<string, Color>(text, c));
    }

    private TextMeshProUGUI NextLabel()
    {
        TextMeshProUGUI t;
        if (labelUsed < labelPool.Count) t = labelPool[labelUsed];
        else
        {
            t = Txt("Label", labelRoot, 17, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
            t.rectTransform.sizeDelta = new Vector2(260f, 26f);
            labelPool.Add(t);
        }
        labelUsed++;
        t.gameObject.SetActive(true);
        return t;
    }

    private void Label(float wx, float wy, string text, Color c, bool leftAlign, int stack = 0)
    {
        if (cam == null) return;
        TextMeshProUGUI t = NextLabel();
        t.text = text;
        t.color = c;
        t.alignment = leftAlign ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
        t.rectTransform.pivot = new Vector2(leftAlign ? 0f : 1f, 0.5f);
        Vector3 sp = cam.WorldToScreenPoint(new Vector3(wx, wy, 0f));
        float scale = hud.scaleFactor > 0f ? hud.scaleFactor : 1f;
        t.rectTransform.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale + stack * 22f);
    }

    private void LabelCenter(float wx, float wy, string text, Color c)
    {
        if (cam == null) return;
        TextMeshProUGUI t = NextLabel();
        t.text = text;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        Vector3 sp = cam.WorldToScreenPoint(new Vector3(wx, wy, 0f));
        float scale = hud.scaleFactor > 0f ? hud.scaleFactor : 1f;
        t.rectTransform.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale);
    }

    // =====================================================================
    //  미니맵
    // =====================================================================
    private double SongLengthBeat()
    {
        double last = 0;
        foreach (var n in notes) { double b = n.Tail != null ? n.Tail.Beat : n.Beat; if (b > last) last = b; }
        if (bgm != null && bgm.clip != null) last = Math.Max(last, BeatOf(audioStart + bgm.clip.length));
        return Math.Max(16.0, last + 4.0);
    }

    private void UpdateMiniMap(double beatNow)
    {
        miniTimer -= Time.unscaledDeltaTime;
        if (miniTimer <= 0f)
        {
            miniTimer = 0.5f;
            int sig = curSig ^ (bgm != null && bgm.clip != null ? 7919 : 0);
            if (sig != miniSig || miniLenBeat <= 0) { miniSig = sig; RebuildMiniMap(); }
        }
        // 지금 보이는 범위
        float bottom = -5.5f, top = 5.5f;
        double b0 = cursorBeat + (bottom - HitY) / beatSpacing, b1 = cursorBeat + (top - HitY) / beatSpacing;
        float f0 = Mathf.Clamp01((float)(b0 / miniLenBeat)), f1 = Mathf.Clamp01((float)(b1 / miniLenBeat));
        float fc = Mathf.Clamp01((float)(beatNow / miniLenBeat));
        miniView.anchorMin = new Vector2(0f, f0);
        miniView.anchorMax = new Vector2(1f, Mathf.Max(f1, f0 + 0.004f));
        miniView.offsetMin = new Vector2(-3f, 0f); miniView.offsetMax = new Vector2(3f, 0f);
        // 판정선 위치 표시는 View 아래 가장자리 → 판정선 박 기준으로 맞춤
        Transform edge = miniView.Find("Edge");
        if (edge != null)
        {
            RectTransform er = (RectTransform)edge;
            float k = Mathf.InverseLerp(f0, Mathf.Max(f1, f0 + 0.004f), fc);
            er.anchorMin = new Vector2(0f, k); er.anchorMax = new Vector2(1f, k);
            er.offsetMin = new Vector2(-3f, -1f); er.offsetMax = new Vector2(3f, 1f);
        }
    }

    private void RebuildMiniMap()
    {
        miniLenBeat = SongLengthBeat();
        var dens = new float[MiniH];
        var hide = new float[MiniH];
        foreach (var n in notes)
        {
            if (n.Type == 4) continue;
            int i = Mathf.Clamp((int)(n.Beat / miniLenBeat * MiniH), 0, MiniH - 1);
            dens[i] += 1f;
        }
        for (int i = 0; i < MiniH; i++)
        {
            double b = (i + 0.5) / MiniH * miniLenBeat;
            hide[i] = folds.Count == 0 ? 0f : 1f - FoldAt(TimeOf(b));
        }
        // 밀도: 이웃과 섞어 부드럽게 + 최대값 기준
        float max = 1f;
        var sm = new float[MiniH];
        for (int i = 0; i < MiniH; i++)
        {
            float s = 0f; int c = 0;
            for (int k = -2; k <= 2; k++) { int j = i + k; if (j < 0 || j >= MiniH) continue; s += dens[j]; c++; }
            sm[i] = s / c; if (sm[i] > max) max = sm[i];
        }
        var px = new Color32[4 * MiniH];
        for (int y = 0; y < MiniH; y++)
        {
            float d = Mathf.Sqrt(sm[y] / max);
            Color baseC = Color.Lerp(new Color(1f, 1f, 1f, 0.04f), new Color(0.9f, 0.95f, 1f, 0.85f), d);
            for (int x = 0; x < 3; x++) px[y * 4 + x] = baseC;
            px[y * 4 + 3] = hide[y] > 0.01f ? (Color32)new Color(1f, 0.6f, 0.3f, 0.3f + 0.7f * hide[y]) : (Color32)new Color(0f, 0f, 0f, 0f);
        }
        miniTex.SetPixels32(px);
        miniTex.Apply();
    }

    // =====================================================================
    //  HUD 마우스 (패널 위에서는 레인 편집을 막고, 버튼 · 미니맵 처리)
    //   반환: true = 마우스가 HUD 위에 있음 (레인 편집 안 함)
    // =====================================================================
    private bool miniDragging;
    private bool HandleHudMouse(Mouse mouse)
    {
        if (hudRoot == null) return false;
        Vector2 mp = mouse.position.ReadValue();
        bool ld = mouse.leftButton.wasPressedThisFrame;
        if (miniDragging && !mouse.leftButton.isPressed) miniDragging = false;
        if (helpOpen) { if (ld) helpOpen = false; return true; }

        if (miniDragging || Over(miniRoot, mp))
        {
            if (ld) miniDragging = true;
            if (miniDragging)
            {
                Vector2 lp;
                RectTransform mr = miniImage.rectTransform;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(mr, mp, null, out lp))
                {
                    float f = Mathf.Clamp01((lp.y - mr.rect.yMin) / Mathf.Max(1f, mr.rect.height));
                    double b = f * miniLenBeat;
                    cursorBeat = Math.Max(0.0, Math.Round(b / Step) * Step);
                }
            }
            return true;
        }
        bool over = Over(topBar, mp) || Over(bottomBar, mp) || Over(leftPanel, mp) || Over(rightPanel, mp);
        if (!over) return false;
        if (ld)
        {
            foreach (var c in toolCards) if (Over(c.rt, mp)) { noteType = c.kind; return true; }
            foreach (var tb in toggles)
            {
                if (!Over(tb.rt, mp)) continue;
                if (tb.id == 5) { redMode = !redMode; if (redMode) dragMode = false; }
                else if (tb.id == 6) { dragMode = !dragMode; if (dragMode) redMode = false; }
                else if (tb.id == 7) ToggleSlideMode();
                else editLayer = NoteLayer.Clamp(tb.id - 20);
                return true;
            }
        }
        return true;
    }

    // 층 배지 색: 1층 하늘 · 2층 흰 · 3층 연두 · 4층 노랑 · 5층 분홍
    private static Color LayerBadgeColor(int layer)
    {
        switch (layer)
        {
            case 1: return new Color(0.62f, 0.86f, 1f, 1f);
            case 3: return new Color(0.62f, 1f, 0.70f, 1f);
            case 4: return new Color(1f, 0.90f, 0.55f, 1f);
            case 5: return new Color(1f, 0.66f, 0.86f, 1f);
            default: return Color.white;
        }
    }

    private static bool Over(RectTransform r, Vector2 screen)
    {
        return r != null && r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, screen, null);
    }

    // =====================================================================
    //  저장 상태 · 그림
    // =====================================================================
    private int ContentSignature()
    {
        unchecked
        {
            int h = 17;
            foreach (var n in notes) h = h * 31 + (int)(n.Beat * 9973.0) * 7 + n.Lane * 131 + n.Type * 977 + n.Width * 13 + n.Layer * 3 + (n.Red ? 5 : 0);
            foreach (var b in bpms) h = h * 31 + (int)(b.Beat * 9973.0) + (int)(b.Bpm * 1000.0) + (b.HasAnchor ? (int)b.AnchorMs : 0);
            foreach (var s in speeds) h = h * 31 + (int)(s.Beat * 9973.0) + (int)(s.Value * 1000.0) + s.Layer + (s.Show ? 17 : 0);
            foreach (var k in marks) h = h * 31 + (int)(k.Beat * 9973.0) + (k.IsStop ? 1 : 2);
            foreach (var f in folds) h = h * 31 + (int)(f.Beat * 9973.0) + (int)(f.Duration * 1000.0) + (f.Fold ? 1 : 2);
            h = h * 31 + (reveal != null ? (int)(reveal.Beat * 9973.0) + 7 : 0);
            h = h * 31 + (int)(audioStart * 100000.0) + (int)(initialBpm * 1000.0) + (int)(initialSpeed * 1000.0);
            h = h * 31 + notes.Count * 7;
            return h;
        }
    }

    private void MarkSaved() { curSig = savedSig = ContentSignature(); }

    private static Sprite TriSprite
    {
        get
        {
            if (triSprite != null) return triSprite;
            const int S = 32;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // 오른쪽을 향한 삼각형 (재생 아이콘)
                    float fx = (x + 0.5f) / S, fy = Mathf.Abs((y + 0.5f) / S - 0.5f) * 2f;
                    float a = Mathf.Clamp01(((1f - fx) - fy) * S * 0.5f + 0.5f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            triSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return triSprite;
        }
    }

    // 도구 카드 테두리 (둥근 테두리 선)
    private static Sprite keyCapLine;
    private static Sprite KeyCapLine
    {
        get
        {
            if (keyCapLine != null) return keyCapLine;
            const int S = 32, R = 10;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - R;
                    float a = Mathf.Clamp01(1.4f - Mathf.Abs(d + 1.2f));
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            keyCapLine = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R + 1, R + 1, R + 1, R + 1));
            return keyCapLine;
        }
    }
}