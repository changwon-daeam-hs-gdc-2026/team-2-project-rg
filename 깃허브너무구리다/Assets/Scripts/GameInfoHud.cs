using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
// Information UI (모든 GAME UI 공통) — 화면 네 귀퉁이 정보 + 맨 위 진행 막대
//
//   맨 위       곡 진행 막대 (흰색)
//   왼쪽 위     곡명 · 작곡가 (레인을 침범하지 않도록 폭 자동 제한)
//   오른쪽 위   난이도 (EZ / HD / INS + 수치) · 남은 시간 · 경과/전체 시간
//   왼쪽 아래   AUTO · SPEED
//   오른쪽 아래 SCORE · 점수 · 정확도
//
//  - 설정 > GAMEPLAY > INFORMATION UI 로 켜고 끔 (게임 중에 바꿔도 바로 반영)
//  - 글꼴은 GameManager 인스펙터 > Information UI / SIMPLE 스킨 폰트 (title / info / number / label)
//  - 켜져 있을 때 GameManager.hideWhenInfoUi 에 넣은 오브젝트(기본 화면의 중복 정보 UI)는 숨김
//  - 곡 종료 / FAILED 때 GameManager.EndUiAlpha 를 따라 함께 사라짐
//  - 게임 씬에 붙어 있어서 씬이 바뀌면 함께 사라진다.
//  - PRACTICE 모드(시작 시간을 바꿔 곡 중간부터 시작한 판):
//      곡명 앞에 [⟳ PRACTICE] 배지, 점수 · 정확도 대신 [⊘ NOT SAVED] 배지 (PracticeBadge.cs)
//  - GAME SETTING(5_Test): 곡명 자리에 GAME SETTING (작곡가 줄 · 난이도 · 남은 시간 없음),
//      점수판(점수 · 정확도)은 숨김 — 그 자리를 조정 패널(GameSettingPanel)이 씀
// ============================================================
public class GameInfoHud : MonoBehaviour
{
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.55f);
    private const float FontScale = 0.9f;        // SIMPLE 스킨과 같은 글자 크기 배율
    private const float Margin = 48f;            // 화면 가장자리에서 떨어진 거리
    private const float LaneGap = 40f;           // 레인(또는 LIFE 바)과 최소 간격

    private GameManager gm;
    private Canvas hostCanvas;
    private int refSortingOrder;
    private RectTransform hudRoot;
    private CanvasGroup group;
    private Image progressFill;
    private TextMeshProUGUI titleText, composerText, diffText, remainText, timeText, scoreText, accText;
    private float laneLeft, laneRight;
    private float leftW = -1f, rightW = -1f;
    private float fadeIn;
    private bool hiddenApplied;
    private bool? lastShown;

    // PRACTICE 모드 표시
    private bool practiceApplied;
    private RectTransform practiceBadge, notSavedBadge, titleTab, scoreTab;
    private float titleOffset;          // (일반 배치) 곡명이 배지만큼 오른쪽으로 밀린 거리
    private TextMeshProUGUI scoreLabelText, remainLabelText;
    private RectTransform leftTab;
    private bool testApplied;

    public static GameInfoHud Create(GameManager manager)
    {
        GameObject go = new GameObject("GameInfoHud");
        SceneManager.MoveGameObjectToScene(go, manager.gameObject.scene); // 게임 씬과 함께 정리되도록
        GameInfoHud hud = go.AddComponent<GameInfoHud>();
        hud.gm = manager;
        hud.Build();
        return hud;
    }

    // 텍스트로 쓰는 짧은 난이도 표기: "<color=#..>INS</color>  15"
    public static string DifficultyLabel(SongData song, int level)
    {
        Color dc = DifficultyHelper.GetColor(level);
        return "<color=#" + ColorUtility.ToHtmlStringRGB(dc) + ">" + DifficultyHelper.GetShortName(level) + "</color>  "
             + SongRowUI.FormatDifficulty(DifficultyHelper.GetValue(song, level));
    }

    private void Build()
    {
        SettingsUIKit.EnsureFont();

        // 레인 양 끝 (월드 좌표) — 곡명 폭 제한용
        float[] xs = gm.laneXPositions;
        float lw = xs != null && xs.Length >= 2 ? Mathf.Abs(xs[1] - xs[0]) : 0.8f;
        laneLeft = xs != null && xs.Length > 0 ? xs[0] - lw * 0.5f : -1.6f;
        laneRight = xs != null && xs.Length > 0 ? xs[xs.Length - 1] + lw * 0.5f : 1.6f;

        // 전용 캔버스: 일시정지 UI 캔버스 기준으로 정렬 (일시정지 중엔 아래, 플레이 중엔 게임 UI 위)
        Canvas refCanvas = FindSortingCanvas(gm.pauseUIContainer != null ? gm.pauseUIContainer.transform : null);
        if (refCanvas == null) refCanvas = FindSortingCanvas(gm.basicUIContainer != null ? gm.basicUIContainer.transform : null);

        GameObject cgo = new GameObject("InfoHUDCanvas", typeof(RectTransform));
        cgo.layer = 5;
        cgo.transform.SetParent(transform, false);
        hostCanvas = cgo.AddComponent<Canvas>();
        if (refCanvas != null && refCanvas.renderMode == RenderMode.ScreenSpaceCamera && refCanvas.worldCamera != null)
        {
            hostCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            hostCanvas.worldCamera = refCanvas.worldCamera;
            hostCanvas.planeDistance = refCanvas.planeDistance;
        }
        else hostCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hostCanvas.sortingLayerID = refCanvas != null ? refCanvas.sortingLayerID : 0;
        refSortingOrder = refCanvas != null ? refCanvas.sortingOrder : 0;
        hostCanvas.sortingOrder = refSortingOrder - 1;

        CanvasScaler sc = cgo.AddComponent<CanvasScaler>();
        CanvasScaler refScaler = refCanvas != null ? refCanvas.rootCanvas.GetComponent<CanvasScaler>() : null;
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = refScaler != null && refScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize ? refScaler.referenceResolution : new Vector2(1920f, 1080f);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = refScaler != null ? refScaler.matchWidthOrHeight : 0.5f;

        hudRoot = SettingsUIKit.Rect("InfoHUD", cgo.transform);
        SettingsUIKit.Stretch(hudRoot);
        group = hudRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        SimpleSkinFonts F = gm.simpleSkinFonts ?? new SimpleSkinFonts();
        SongData song = CoreManager.Instance != null ? CoreManager.Instance.CurrentSongData : null;
        int level = CoreManager.Instance != null ? CoreManager.Instance.CurrentLevelId : 2;

        diverse = SimpleGameSkin.Active != null && SimpleGameSkin.Active.IsDiverse;
        if (diverse) { BuildDiverse(F, song, level); return; }

        // ---- 맨 위: 진행 막대 (흰색) ----
        Image barBg = SettingsUIKit.Img("ProgressBg", hudRoot, new Color(1f, 1f, 1f, 0.10f));
        SettingsUIKit.Place(barBg.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -5f), Vector2.zero);
        progressFill = SettingsUIKit.Img("ProgressFill", barBg.rectTransform, Color.white);
        progressFill.rectTransform.anchorMin = Vector2.zero;
        progressFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        progressFill.rectTransform.offsetMin = Vector2.zero;
        progressFill.rectTransform.offsetMax = Vector2.zero;

        // ---- 왼쪽 위: 곡 정보 ----
        titleText = Text(song != null ? (song.Name ?? "") + (song.SubName ?? "") : "", 34, TextAlignmentOptions.TopLeft, Color.white, FontStyles.Bold, F.title);
        Place(titleText.rectTransform, new Vector2(0f, 1f), Margin, 28f, 760f, 46f);
        AutoShrink(titleText, 0.62f);
        composerText = Text(song != null ? song.Composer ?? "" : "", 19, TextAlignmentOptions.TopLeft, TextSub, FontStyles.Normal, F.info);
        Place(composerText.rectTransform, new Vector2(0f, 1f), Margin, 76f, 760f, 28f);
        AutoShrink(composerText, 0.75f);

        // ---- 오른쪽 위: 난이도 + 남은 시간 ----
        diffText = Text(DifficultyLabel(song, level), 26, TextAlignmentOptions.TopRight, Color.white, FontStyles.Bold, F.info);
        Place(diffText.rectTransform, new Vector2(1f, 1f), Margin, 28f, 520f, 44f);
        AutoShrink(diffText, 0.7f);
        remainText = Text("0:00", 40, TextAlignmentOptions.TopRight, Color.white, FontStyles.Bold, F.number);
        Place(remainText.rectTransform, new Vector2(1f, 1f), Margin, 74f, 300f, 50f);
        TextMeshProUGUI remainLabel = Text("REMAINING", 15, TextAlignmentOptions.TopRight, TextSub, FontStyles.Normal, F.label);
        remainLabelText = remainLabel;
        remainLabel.characterSpacing = 8f;
        Place(remainLabel.rectTransform, new Vector2(1f, 1f), Margin, 124f, 300f, 22f);
        timeText = Text("0:00 / 0:00", 18, TextAlignmentOptions.TopRight, TextSub, FontStyles.Normal, F.info);
        Place(timeText.rectTransform, new Vector2(1f, 1f), Margin, 148f, 300f, 26f);

        // ---- 오른쪽 아래: 점수 / 정확도 ----
        TextMeshProUGUI scoreLabel = Text("SCORE", 15, TextAlignmentOptions.BottomRight, TextSub, FontStyles.Normal, F.label);
        scoreLabel.characterSpacing = 8f;
        scoreLabelText = scoreLabel;
        Place(scoreLabel.rectTransform, new Vector2(1f, 0f), Margin, 122f, 400f, 22f);
        scoreText = Text("0", 44, TextAlignmentOptions.BottomRight, Color.white, FontStyles.Bold, F.number);
        Place(scoreText.rectTransform, new Vector2(1f, 0f), Margin, 62f, 500f, 60f);
        accText = Text("100.00%", 24, TextAlignmentOptions.BottomRight, TextSub, FontStyles.Normal, F.number);
        Place(accText.rectTransform, new Vector2(1f, 0f), Margin, 30f, 400f, 32f);

        // ---- 왼쪽 아래: AUTO / SPEED ----
        TextMeshProUGUI speedText = Text("SPEED  x" + SettingsStore.Current.SettingSpeed.ToString("0.0", CultureInfo.InvariantCulture),
                                         20, TextAlignmentOptions.BottomLeft, TextSub, FontStyles.Normal, F.info);
        Place(speedText.rectTransform, new Vector2(0f, 0f), Margin, 30f, 400f, 30f);
        TextMeshProUGUI autoText = Text("AUTO", 20, TextAlignmentOptions.BottomLeft, new Color(1f, 0.85f, 0.4f), FontStyles.Bold, F.info);
        Place(autoText.rectTransform, new Vector2(0f, 0f), Margin, 62f, 200f, 30f);
        autoText.gameObject.SetActive(CoreManager.Instance != null && CoreManager.Instance.IsAutoPlay);
    }

    void Update()
    {
        if (gm == null) return;
        float dt = Time.unscaledDeltaTime;
        bool shown = SettingsStore.Current.ShowInfoUI;
        ApplyHiddenObjects(shown);

        bool started = gm.LaneIntroStarted || gm.currentState == GameState.Playing;
        fadeIn = Mathf.MoveTowards(fadeIn, started ? 1f : 0f, dt / 0.35f);
        group.alpha = shown ? fadeIn * gm.EndUiAlpha : 0f;

        // 일시정지 중에는 일시정지 UI 캔버스보다 아래, 플레이 중에는 게임 UI 위
        hostCanvas.sortingOrder = gm.currentState == GameState.Paused ? refSortingOrder - 1 : refSortingOrder + 1;

        if (!testApplied && gm.IsTestMode) ApplyTestMode();
        if (!practiceApplied && gm.IsMidStart) ApplyPractice();
        if (group.alpha <= 0.001f) return;

        double start, end;
        gm.GetPlayRange(out start, out end);
        double total = Math.Max(0.001, end - start);
        double elapsed = Math.Max(0.0, Math.Min(total, gm.currentPlayTime - start));
        progressFill.rectTransform.anchorMax = new Vector2((float)(elapsed / total), 1f);
        if (remainText != null) remainText.text = FormatTime(total - elapsed);
        if (timeText != null) timeText.text = (diverse ? "-" + FormatTime(total - elapsed) + "<color=#FFFFFF59>   /   " + FormatTime(total) + "</color>"
                                                       : FormatTime(elapsed) + " / " + FormatTime(total));
        if (!practiceApplied && !testApplied)
        {
            scoreText.text = gm.DisplayScore.ToString("N0", CultureInfo.InvariantCulture);   // UNKNOWN UI 면 무작위 값
            scoreText.alpha = gm.ScoreLocked ? 0.35f : 1f;                                       // COMBO BREAK: 점수를 못 얻는 동안 흐리게
            accText.text = gm.LiveAccuracy.ToString("F2", CultureInfo.InvariantCulture) + "%";
        }

        if (!diverse) LimitHeaderWidth();
    }

    // =====================================================================
    //  SIMPLE-DIVERSE 전용 배치: 화면 위쪽 헤더 띠
    //
    //   ┌────────────┐ ┌──────────────────────┐ ┌──────────────────┐
    //   │ INS 15  x4.5│ │      곡 제목          │ │ SCORE   987,654  │   ← 맨 위 띠 (어두운 반투명)
    //   └──────────╲  ╲━━━━━━ 진행 막대 ━━━━━━╱  ╱─────────────────┘   (선은 진행 막대 하나뿐)
    //     -1:23 / 2:08      작곡가                         98.76%
    // =====================================================================
    private bool diverse;
    private Sprite tabCenter, tabLeft, tabRight, vGrad;

    private void BuildDiverse(SimpleSkinFonts F, SongData song, int level)
    {
        const float BandH = 58f;       // 맨 위 띠 높이
        tabCenter = MakeTabSprite(true, true);
        tabLeft = MakeTabSprite(false, true);
        tabRight = MakeTabSprite(true, false);
        vGrad = MakeVerticalGradient();
        Color dark = new Color(0.02f, 0.025f, 0.04f, 0.88f);

        // 위쪽 그늘 (글자가 MV 위에서도 잘 보이도록)
        Image shade = SettingsUIKit.Img("TopShade", hudRoot, new Color(0f, 0f, 0f, 0.55f));
        shade.sprite = vGrad;
        SettingsUIKit.Place(shade.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -170f), Vector2.zero);


        // ---- 왼쪽 탭: 난이도 + 배속 ----
        Image lt = Tab(tabLeft, dark, new Vector2(0f, 1f), 0f, 440f, BandH);
        leftTab = lt.rectTransform;
        diffText = Text(DifficultyLabel(song, level), 26, TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold, F.info);
        diffText.transform.SetParent(lt.transform, false);
        Place(diffText.rectTransform, new Vector2(0f, 1f), 44f, 0f, 220f, BandH);
        AutoShrink(diffText, 0.7f);
        TextMeshProUGUI sp = Text("x" + SettingsStore.Current.SettingSpeed.ToString("0.0", CultureInfo.InvariantCulture), 20, TextAlignmentOptions.MidlineRight, TextSub, FontStyles.Bold, F.number);
        sp.transform.SetParent(lt.transform, false);
        Place(sp.rectTransform, new Vector2(0f, 1f), 250f, 0f, 120f, BandH);

        // 왼쪽 탭 아래: 남은 시간 / 전체 시간 (+ AUTO)
        timeText = Text("", 20, TextAlignmentOptions.TopLeft, Color.white, FontStyles.Bold, F.number);
        Place(timeText.rectTransform, new Vector2(0f, 1f), 44f, BandH + 12f, 360f, 30f);
        TextMeshProUGUI auto = Text("AUTO", 18, TextAlignmentOptions.TopLeft, new Color(1f, 0.85f, 0.4f), FontStyles.Bold, F.label);
        auto.characterSpacing = 8f;
        Place(auto.rectTransform, new Vector2(0f, 1f), 44f, BandH + 44f, 200f, 26f);
        auto.gameObject.SetActive(CoreManager.Instance != null && CoreManager.Instance.IsAutoPlay);

        // ---- 가운데 탭: 곡 제목 + 진행 막대 ----
        Image ct = Tab(tabCenter, dark, new Vector2(0.5f, 1f), 0f, 760f, BandH + 10f);
        titleTab = ct.rectTransform;
        titleText = Text(song != null ? (song.Name ?? "") + (song.SubName ?? "") : "", 28, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, F.title);
        titleText.transform.SetParent(ct.transform, false);
        Place(titleText.rectTransform, new Vector2(0.5f, 1f), 0f, 6f, 600f, 40f);
        AutoShrink(titleText, 0.6f);
        Image barBg = SettingsUIKit.Img("ProgressBg", ct.transform, new Color(1f, 1f, 1f, 0.14f));
        Place(barBg.rectTransform, new Vector2(0.5f, 1f), 0f, 52f, 600f, 3f);
        progressFill = SettingsUIKit.Img("ProgressFill", barBg.rectTransform, Color.white);
        progressFill.rectTransform.anchorMin = Vector2.zero;
        progressFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        progressFill.rectTransform.offsetMin = progressFill.rectTransform.offsetMax = Vector2.zero;
        composerText = Text(song != null ? song.Composer ?? "" : "", 18, TextAlignmentOptions.Top, TextSub, FontStyles.Normal, F.info);
        Place(composerText.rectTransform, new Vector2(0.5f, 1f), 0f, BandH + 18f, 600f, 26f);
        AutoShrink(composerText, 0.75f);

        // ---- 오른쪽 탭: SCORE + 점수 ----
        Image rt = Tab(tabRight, dark, new Vector2(1f, 1f), 0f, 520f, BandH);
        scoreTab = rt.rectTransform;
        TextMeshProUGUI sl = Text("SCORE", 15, TextAlignmentOptions.MidlineLeft, TextSub, FontStyles.Bold, F.label);
        sl.characterSpacing = 10f;
        sl.transform.SetParent(rt.transform, false);
        Place(sl.rectTransform, new Vector2(0f, 1f), 70f, 0f, 120f, BandH);
        scoreText = Text("0", 34, TextAlignmentOptions.MidlineRight, Color.white, FontStyles.Bold, F.number);
        scoreText.transform.SetParent(rt.transform, false);
        Place(scoreText.rectTransform, new Vector2(1f, 1f), 44f, 0f, 300f, BandH);
        accText = Text("100.00%", 20, TextAlignmentOptions.TopRight, Color.white, FontStyles.Bold, F.number);
        Place(accText.rectTransform, new Vector2(1f, 1f), 44f, BandH + 12f, 300f, 30f);
    }

    // =====================================================================
    //  GAME SETTING (5_Test): 제목 · 난이도 · 곡 이름 표기, 점수판 숨김
    // =====================================================================
    private void ApplyTestMode()
    {
        testApplied = true;
        SongData song = CoreManager.Instance != null ? CoreManager.Instance.CurrentSongData : null;
        titleText.text = "GAME SETTING";
        if (composerText != null) composerText.gameObject.SetActive(false);   // 작곡가 줄은 표시하지 않음
        // 난이도 · 남은 시간 표시는 없앰 (왼쪽 난이도 탭 · 시간 줄 · REMAINING)
        if (diffText != null) diffText.gameObject.SetActive(false);
        if (leftTab != null) leftTab.gameObject.SetActive(false);
        if (timeText != null) timeText.gameObject.SetActive(false);
        if (remainText != null) remainText.gameObject.SetActive(false);
        if (remainLabelText != null) remainLabelText.gameObject.SetActive(false);
        scoreText.gameObject.SetActive(false);
        accText.gameObject.SetActive(false);
        if (scoreLabelText != null) scoreLabelText.gameObject.SetActive(false);
        if (scoreTab != null) scoreTab.gameObject.SetActive(false);
    }

    // =====================================================================
    //  PRACTICE 모드: 곡명 앞 [PRACTICE] 배지, 점수 · 정확도 대신 [NOT SAVED] 배지
    // =====================================================================
    private void ApplyPractice()
    {
        practiceApplied = true;
        SimpleSkinFonts F = gm.simpleSkinFonts ?? new SimpleSkinFonts();
        TMP_FontAsset lf = F.label;

        // 점수 · 정확도 숨기고 NOT SAVED
        scoreText.gameObject.SetActive(false);
        accText.gameObject.SetActive(false);
        if (diverse)
        {
            notSavedBadge = PracticeBadge.NotSaved(scoreTab, 32f, lf);
            notSavedBadge.anchorMin = notSavedBadge.anchorMax = new Vector2(1f, 0.5f);
            notSavedBadge.pivot = new Vector2(1f, 0.5f);
            notSavedBadge.anchoredPosition = new Vector2(-44f, 0f);
        }
        else
        {
            notSavedBadge = PracticeBadge.NotSaved(hudRoot, 38f, lf);
            notSavedBadge.anchorMin = notSavedBadge.anchorMax = new Vector2(1f, 0f);
            notSavedBadge.pivot = new Vector2(1f, 0f);
            notSavedBadge.anchoredPosition = new Vector2(-Margin, 70f);
        }

        // 곡명 앞 PRACTICE
        string title = titleText.text;
        if (diverse)
        {
            // 가운데 탭: [배지 + 곡명] 묶음이 가운데 오도록
            practiceBadge = PracticeBadge.Practice(titleTab, 30f, lf);
            float bw = practiceBadge.sizeDelta.x, gap = 12f;
            float tw = Mathf.Min(titleText.GetPreferredValues(title).x + 4f, 600f - bw - gap);
            float total = bw + gap + tw;
            practiceBadge.anchorMin = practiceBadge.anchorMax = new Vector2(0.5f, 1f);
            practiceBadge.pivot = new Vector2(0f, 0.5f);
            practiceBadge.anchoredPosition = new Vector2(-total * 0.5f, -(6f + 20f));
            RectTransform tr = titleText.rectTransform;
            tr.pivot = new Vector2(0f, 1f);
            tr.anchoredPosition = new Vector2(-total * 0.5f + bw + gap, -6f);
            tr.sizeDelta = new Vector2(tw, tr.sizeDelta.y);
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
        }
        else
        {
            practiceBadge = PracticeBadge.Practice(hudRoot, 34f, lf);
            practiceBadge.anchorMin = practiceBadge.anchorMax = new Vector2(0f, 1f);
            practiceBadge.pivot = new Vector2(0f, 0.5f);
            practiceBadge.anchoredPosition = new Vector2(Margin, -(28f + 23f));
            titleOffset = practiceBadge.sizeDelta.x + 14f;
            RectTransform tr = titleText.rectTransform;
            tr.anchoredPosition = new Vector2(Margin + titleOffset, tr.anchoredPosition.y);
            tr.sizeDelta = new Vector2(Mathf.Max(80f, tr.sizeDelta.x - titleOffset), tr.sizeDelta.y);
            leftW = -1f;     // 폭 제한 다시 계산
        }
    }

    // 맨 위에 붙는 탭 판 (Sliced, 비스듬한 옆면은 늘어나지 않는 테두리 안에 있음)
    private Image Tab(Sprite sprite, Color c, Vector2 corner, float x, float w, float h)
    {
        Image img = SettingsUIKit.Img("Tab", hudRoot, c);
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 64f / h; // 텍스처 높이 64px → 탭 높이 (옆면 기울기 45°)
        Place(img.rectTransform, corner, x, 0f, w, h);
        return img;
    }

    // 탭 모양: 위가 넓고 아래로 갈수록 좁아짐 (slantL / slantR 쪽만 비스듬)
    private static Sprite MakeTabSprite(bool slantL, bool slantR)
    {
        const int W = 192, H = 64, S = 64; // 옆면 가로 = 높이 → 45°
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float inset = S * (1f - (y + 0.5f) / H);   // 아래(y=0)에서 S, 위에서 0
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f;
                float a = 1f;
                if (slantL) a = Mathf.Min(a, Mathf.Clamp01((fx - inset) * 0.7071f + 0.5f));
                if (slantR) a = Mathf.Min(a, Mathf.Clamp01(((W - fx) - inset) * 0.7071f + 0.5f));
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(px); tex.Apply();
        int bl = slantL ? S + 2 : 2, br = slantR ? S + 2 : 2;
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(bl, 0, br, 0));
    }

    // 위가 진하고 아래로 사라지는 세로 그라데이션
    private static Sprite MakeVerticalGradient()
    {
        Texture2D tex = new Texture2D(4, 64, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[4 * 64];
        for (int y = 0; y < 64; y++)
        {
            byte a = (byte)(Mathf.Pow(y / 63f, 1.6f) * 255);
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 64), new Vector2(0.5f, 0.5f), 100f);
    }

    // INFORMATION UI 가 켜져 있으면 기본 화면의 중복 정보 UI 를 숨김 (꺼지면 되돌림)
    private void ApplyHiddenObjects(bool shown)
    {
        if (lastShown.HasValue && lastShown.Value == shown) return;
        lastShown = shown;
        if (gm.hideWhenInfoUi == null) return;
        foreach (var go in gm.hideWhenInfoUi)
        {
            if (go == null) continue;
            if (go.transform is RectTransform)
            {
                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = shown ? 0f : 1f;   // SetActive 를 건드리지 않아 다른 코드와 충돌하지 않음
            }
            else
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = !shown;
            }
        }
    }

    // 곡명·작곡가는 '화면 왼쪽 ~ 레인 왼쪽 벽', 난이도는 '레인(+ LIFE 바) ~ 화면 오른쪽' 안에서만
    private void LimitHeaderWidth()
    {
        Camera cam = gm.SceneCamera;
        if (cam == null) return;
        float y = gm.hitLineY;
        float half = hudRoot.rect.width * 0.5f;
        Vector2 l, r;
        if (WorldToLocal(cam, new Vector3(laneLeft, y, 0f), out l))
        {
            float w = Mathf.Max(160f, (l.x + half) - Margin - LaneGap);
            if (Mathf.Abs(w - leftW) > 0.5f)
            {
                leftW = w;
                titleText.rectTransform.sizeDelta = new Vector2(Mathf.Max(80f, w - titleOffset), titleText.rectTransform.sizeDelta.y);
                composerText.rectTransform.sizeDelta = new Vector2(w, composerText.rectTransform.sizeDelta.y);
            }
        }
        bool life = SimpleGameSkin.Active != null;
        float rightWorld = laneRight + (life ? SimpleGameSkin.LifeBarGap : 0f);
        if (WorldToLocal(cam, new Vector3(rightWorld, y, 0f), out r))
        {
            float w = Mathf.Clamp((half - r.x) - Margin - LaneGap - (life ? SimpleGameSkin.LifeBarWidth : 0f), 160f, 520f);
            if (Mathf.Abs(w - rightW) > 0.5f)
            {
                rightW = w;
                diffText.rectTransform.sizeDelta = new Vector2(w, diffText.rectTransform.sizeDelta.y);
            }
        }
    }

    private bool WorldToLocal(Camera worldCam, Vector3 world, out Vector2 local)
    {
        Vector3 screen = worldCam.WorldToScreenPoint(world);
        Camera uiCam = hostCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hostCanvas.worldCamera;
        bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRoot, screen, uiCam, out local);
        local -= hudRoot.rect.center;
        return ok;
    }

    // =====================================================================
    //  도우미
    // =====================================================================
    private TextMeshProUGUI Text(string text, float size, TextAlignmentOptions align, Color color, FontStyles style, TMP_FontAsset font)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt("T", hudRoot, text, size * FontScale, align, color, style);
        if (font != null) t.font = font;
        t.richText = true;
        return t;
    }

    private static void AutoShrink(TextMeshProUGUI t, float minRatio)
    {
        float size = t.fontSize;
        t.enableAutoSizing = true;
        t.fontSizeMax = size;
        t.fontSizeMin = size * minRatio;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.overflowMode = TextOverflowModes.Ellipsis;
    }

    // corner: (0,1) 왼쪽 위 / (1,1) 오른쪽 위 / (0,0) 왼쪽 아래 / (1,0) 오른쪽 아래. x, y 는 그 귀퉁이에서 안쪽으로의 거리
    private static void Place(RectTransform r, Vector2 corner, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = corner;
        r.anchoredPosition = new Vector2(corner.x < 0.5f ? x : -x, corner.y < 0.5f ? y : -y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static string FormatTime(double sec)
    {
        int s = Mathf.Max(0, (int)Math.Ceiling(sec - 0.0001));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    private static Canvas FindSortingCanvas(Transform t)
    {
        Canvas result = null;
        for (Transform p = t; p != null; p = p.parent)
        {
            Canvas c = p.GetComponent<Canvas>();
            if (c == null) continue;
            result = c;
            if (c.overrideSorting) break;
        }
        return result;
    }
}