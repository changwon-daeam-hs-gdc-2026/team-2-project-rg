using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// 결과창 글꼴 (ResultScreen 인스펙터에서 지정, 비우면 기본 폰트)
//  TMP 폰트 에셋(○○ SDF.asset)을 끌어다 넣으면 된다.
// ============================================================
[Serializable]
public class ResultFonts
{
    [Tooltip("곡 제목")] public TMP_FontAsset title;
    [Tooltip("작곡가 · 난이도 · 버튼")] public TMP_FontAsset info;
    [Tooltip("숫자: 점수 · 정확도 · 콤보 · 판정 개수")] public TMP_FontAsset number;
    [Tooltip("작은 라벨: RESULT · SCORE · ACCURACY · 판정 이름 등")] public TMP_FontAsset label;
    [Tooltip("클리어 상태: ALL PERFECT / FULL COMBO / CLEAR / FAILED")] public TMP_FontAsset status;
    [Tooltip("랭크 글자 (SSS, AA ...)")] public TMP_FontAsset rank;
}

// ============================================================
// 심플 결과창 (4_Result 씬) — 에셋 없이 코드로 전부 그림
//
//  [사용법] 4_Result 씬에서 기존 결과 UI(Canvas + ResultManager)를 끄거나 지우고,
//           빈 오브젝트에 이 컴포넌트를 붙인 뒤 글꼴만 지정하면 된다.
//
//  [화면 구성 (1920 x 1080 기준)]  흰색 / 회색 위주, 색은 난이도 이름과 클리어 상태에만
//   왼쪽    자켓(squareimg, 둥근 모서리) · 난이도 · 곡명 · 작곡가
//   오른쪽  클리어 상태 · 점수 + 랭크 (+ NEW RECORD)
//           ACCURACY / MAX COMBO / BEST
//           판정 5종 개수 · FAST / SLOW / 평균 오차
//   아래    R  RETRY      ENTER  CONTINUE
//
//  [조작]  ENTER / SPACE / ESC : 연출 중이면 건너뛰기, 끝났으면 곡 선택으로
//          R : 같은 곡 · 같은 난이도로 다시 하기
// ============================================================
public class ResultScreen : MonoBehaviour
{
    [Header("글꼴 (비워 두면 기본 폰트)")]
    public ResultFonts fonts = new ResultFonts();

    [Header("배경")]
    [Tooltip("곡 배경(blurimg) 밝기 (0 검정 ~ 1 원본)")]
    [Range(0f, 1f)] public float backgroundDim = 0.22f;

    [Header("연출")]
    [Tooltip("연출 전체 속도 배율 (2 = 두 배 빠르게)")]
    [Range(0.5f, 3f)] public float animationSpeed = 1f;
    public float fadeInSeconds = 0.6f;
    public float fadeOutSeconds = 0.5f;
    [Tooltip("숫자가 올라갈 때 글자 폭이 흔들리지 않도록 고정 폭으로 표시")]
    public bool monospaceNumbers = true;

    [Header("랭크 (위에서부터 점수 이상이면 해당 랭크, FAILED 는 F)")]
    public string[] rankNames = { "SSS", "SS", "S", "AAA", "AA", "A", "B", "C", "D" };
    public int[] rankScores = { 990000, 970000, 950000, 920000, 880000, 800000, 700000, 600000, 0 };

    // ----- 색 (흰색 단계만) -----
    private static readonly Color TextMain = new Color(0.96f, 0.97f, 1f, 1f);
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color TextDim = new Color(1f, 1f, 1f, 0.32f);
    private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color FailColor = new Color(0.85f, 0.42f, 0.45f, 1f);  // FAILED 만 차분한 빨강
    private static readonly string[] JudgeNames = { "PERFECT", "GREAT", "GOOD", "BAD", "MISS" };

    // ----- 연출 시간표 (초) -----
    private const float TStatus = 1.5f;
    private const float TIntroEnd = 2.2f;

    // ----- 데이터 -----
    private ResultData r;
    private ScoreRecord before;      // 이번 판 반영 전 기록 (첫 플레이면 null)
    private bool isNewRecord;
    private int clearKind;           // 0 FAILED 1 CLEAR 2 FC 3 AP
    private string rankText;
    private int total;
    private readonly int[] judgeValues = new int[5];

    // ----- 화면 -----
    private RectTransform root, stage;
    private CanvasGroup overlay;
    private Sprite roundSprite;
    private readonly List<Texture2D> ownedTextures = new List<Texture2D>();

    private class Reveal { public CanvasGroup cg; public RectTransform rt; public Vector2 offset; public float start, dur; }
    private readonly List<Reveal> reveals = new List<Reveal>();

    private TextMeshProUGUI scoreText, accText, comboText, statusText;
    private readonly TextMeshProUGUI[] judgeCounts = new TextMeshProUGUI[5];
    private CanvasGroup statusGroup, rankGroup, newRecordGroup;

    // ----- 상태 -----
    private float clock;
    private bool leaving;
    private float leaveT;
    private Action leaveAction;

    // =====================================================================
    //  준비
    // =====================================================================
    void Awake()
    {
        r = CoreManager.Instance != null ? CoreManager.Instance.LastResult : null;
        bool standalone = r == null;
        if (r == null) r = ResultData.CreateSample(); // 결과 씬만 단독 실행했을 때 확인용

        // 최고 기록: 단독 실행(샘플)일 때는 저장하지 않음
        before = standalone ? ScoreRecords.Get(r.SongId, r.LevelId) : ScoreRecords.Submit(r);
        isNewRecord = !r.IsAutoPlay && !standalone && r.Score > 0 && (before == null || r.Score > before.BestScore);

        total = Mathf.Max(1, r.TotalNotes > 0 ? r.TotalNotes : r.Excellent + r.Great + r.Good + r.Bad + r.Miss);
        clearKind = r.ClearRank;
        rankText = GetRank();
        judgeValues[0] = r.Excellent; judgeValues[1] = r.Great; judgeValues[2] = r.Good; judgeValues[3] = r.Bad; judgeValues[4] = r.Miss;

        SettingsUIKit.EnsureFont();
        roundSprite = MakeRoundSprite();
        Build();
        Animate(0f);
    }

    void OnDestroy()
    {
        foreach (var t in ownedTextures) if (t != null) Destroy(t);
    }

    private string GetRank()
    {
        if (r.IsFailed) return "F";
        int n = Mathf.Min(rankNames.Length, rankScores.Length);
        for (int i = 0; i < n; i++)
            if (r.Score >= rankScores[i]) return rankNames[i];
        return n > 0 ? rankNames[n - 1] : "-";
    }

    // =====================================================================
    //  화면 만들기
    // =====================================================================
    private void Build()
    {
        GameObject cgo = new GameObject("ResultCanvas", typeof(RectTransform));
        cgo.layer = 5;
        cgo.transform.SetParent(transform, false);
        Canvas canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // 곡 정보 화면(800) · 설정창(500) 아래
        CanvasScaler sc = cgo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        root = (RectTransform)cgo.transform;

        BuildBackground();

        // 1920 x 1080 무대 (화면 비율이 달라도 전체가 보이도록 Update 에서 배율 조정)
        stage = SettingsUIKit.Rect("Stage", root);
        stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
        stage.sizeDelta = new Vector2(1920f, 1080f);

        BuildHeader();
        BuildLeft();
        BuildRight();
        BuildFooter();

        Image ov = SettingsUIKit.Img("BlackOverlay", root, Color.black);
        SettingsUIKit.Stretch(ov.rectTransform);
        overlay = ov.gameObject.AddComponent<CanvasGroup>();
        overlay.alpha = 1f;          // 게임 씬에서 암전된 채로 넘어오므로 첫 프레임부터 검게
        overlay.blocksRaycasts = false;
    }

    private void BuildBackground()
    {
        Image bgBase = SettingsUIKit.Img("BgBase", root, new Color(0.035f, 0.04f, 0.055f, 1f));
        SettingsUIKit.Stretch(bgBase.rectTransform);

        Sprite bg = Resources.Load<Sprite>("Sprites/blurimg/" + r.SongId);
        if (bg != null)
        {
            Image bgImg = SettingsUIKit.Img("Background", root, Color.white);
            bgImg.sprite = bg;
            float d = Mathf.Clamp01(backgroundDim) * (clearKind == 0 ? 0.7f : 1f);
            bgImg.color = new Color(d, d, d, 1f);
            SettingsUIKit.Stretch(bgImg.rectTransform);
            AspectRatioFitter fit = bgImg.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = bg.rect.width / Mathf.Max(1f, bg.rect.height);
        }
    }

    private void BuildHeader()
    {
        RectTransform g = Group("Header", 0.05f, 0.5f, new Vector2(0f, 10f));
        TextMeshProUGUI t = T(g, "RESULT", 22, TextAlignmentOptions.MidlineLeft, TextSub, fonts.label, FontStyles.Bold);
        t.characterSpacing = 20f;
        Box(t.rectTransform, 160, 90, 400, 36);

        // 플레이 옵션 (오른쪽, 회색 글자 한 줄)
        string opt = "SPEED " + r.Speed.ToString("0.0#", CultureInfo.InvariantCulture);
        if (r.IsMv) opt += "     MV";
        if (r.IsAutoPlay) opt += "     AUTO PLAY";
        TextMeshProUGUI o = T(g, opt, 18, TextAlignmentOptions.MidlineRight, TextDim, fonts.label, FontStyles.Bold);
        o.characterSpacing = 6f;
        Box(o.rectTransform, 1160, 90, 600, 36);
    }

    private void BuildLeft()
    {
        RectTransform g = Group("Left", 0.15f, 0.6f, new Vector2(-30f, 0f));

        // 자켓 (둥근 마스크)
        Image mask = SettingsUIKit.Img("JacketMask", g, Color.white);
        mask.sprite = roundSprite; mask.type = Image.Type.Sliced;
        mask.pixelsPerUnitMultiplier = 32f / 20f;
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        Box(mask.rectTransform, 160, 200, 480, 480);
        Image jacket = SettingsUIKit.Img("Jacket", mask.rectTransform, Color.white);
        SettingsUIKit.Stretch(jacket.rectTransform);
        Sprite js = Resources.Load<Sprite>("Sprites/squareimg/" + r.SongId);
        if (js != null) jacket.sprite = js;
        else jacket.color = new Color(0.12f, 0.13f, 0.17f, 1f);
        if (clearKind == 0) jacket.color = new Color(0.6f, 0.6f, 0.6f, 1f); // FAILED: 어둡게

        // 난이도 (이름만 난이도 색)
        Color dc = DifficultyHelper.GetColor(r.LevelId);
        string diff = string.IsNullOrEmpty(r.DifficultyName) ? DifficultyHelper.GetName(r.LevelId) : r.DifficultyName;
        TextMeshProUGUI dt = T(g, "<color=#" + ColorUtility.ToHtmlStringRGB(dc) + ">" + diff + "</color>  " + SongRowUI.FormatDifficulty(r.DifficultyValue),
                               20, TextAlignmentOptions.MidlineLeft, TextMain, fonts.info, FontStyles.Bold);
        dt.characterSpacing = 4f;
        Box(dt.rectTransform, 160, 712, 480, 30);

        TextMeshProUGUI title = T(g, (r.SongName ?? "") + (r.SubName ?? ""), 40, TextAlignmentOptions.MidlineLeft, TextMain, fonts.title, FontStyles.Bold);
        Box(title.rectTransform, 160, 748, 480, 56);
        Shrink(title, 0.55f);
        TextMeshProUGUI comp = T(g, r.Composer ?? "", 22, TextAlignmentOptions.MidlineLeft, TextSub, fonts.info, FontStyles.Normal);
        Box(comp.rectTransform, 160, 804, 480, 32);
        Shrink(comp, 0.7f);
    }

    private void BuildRight()
    {
        const float X = 760f, W = 1000f;

        // ---- 클리어 상태 ----
        RectTransform st = SettingsUIKit.Rect("Status", stage);
        Box(st, X, 200, W, 50);
        statusGroup = st.gameObject.AddComponent<CanvasGroup>();
        string[] names = { "FAILED", "CLEAR", "FULL COMBO", "ALL PERFECT" };
        Color sc = clearKind == 0 ? FailColor : (clearKind == 1 ? TextSub : TextMain);
        statusText = T(st, names[clearKind], 34, TextAlignmentOptions.MidlineLeft, sc, fonts.status, FontStyles.Bold);
        statusText.characterSpacing = 14f;
        SettingsUIKit.Stretch(statusText.rectTransform);

        // ---- 점수 + 랭크 ----
        RectTransform g1 = Group("Score", 0.3f, 0.5f, new Vector2(30f, 0f));
        TextMeshProUGUI sl = T(g1, "SCORE", 16, TextAlignmentOptions.MidlineLeft, TextDim, fonts.label, FontStyles.Bold);
        sl.characterSpacing = 10f;
        Box(sl.rectTransform, X, 268, 120, 24);
        scoreText = T(g1, Mono("0"), 110, TextAlignmentOptions.MidlineLeft, TextMain, fonts.number, FontStyles.Bold);
        Box(scoreText.rectTransform, X - 6, 290, 760, 124);

        RectTransform nr = SettingsUIKit.Rect("NewRecord", stage);
        Box(nr, X + 120, 268, 200, 24);
        newRecordGroup = nr.gameObject.AddComponent<CanvasGroup>();
        TextMeshProUGUI nt = T(nr, "NEW RECORD", 16, TextAlignmentOptions.MidlineLeft, TextMain, fonts.label, FontStyles.Bold);
        nt.characterSpacing = 8f;
        SettingsUIKit.Stretch(nt.rectTransform);
        nr.gameObject.SetActive(isNewRecord);

        RectTransform rk = SettingsUIKit.Rect("Rank", stage);
        Box(rk, X + W - 260, 268, 260, 146);
        rankGroup = rk.gameObject.AddComponent<CanvasGroup>();
        TextMeshProUGUI rl = T(rk, "RANK", 16, TextAlignmentOptions.TopRight, TextDim, fonts.label, FontStyles.Bold);
        rl.characterSpacing = 10f;
        Box(rl.rectTransform, 0, 0, 260, 24);
        TextMeshProUGUI rt = T(rk, rankText, 110, TextAlignmentOptions.MidlineRight, clearKind == 0 ? FailColor : TextMain, fonts.rank, FontStyles.Bold);
        Box(rt.rectTransform, 0, 22, 260, 124);
        Shrink(rt, 0.6f);

        // ---- 구분선 ----
        RectTransform g2 = Group("Stats", 0.45f, 0.5f, new Vector2(30f, 0f));
        Line(g2, X, 440, W);

        // ---- ACCURACY / MAX COMBO / BEST ----
        accText = Stat(g2, "ACCURACY", X, 466);
        comboText = Stat(g2, "MAX COMBO", X + 340, 466);
        TextMeshProUGUI best = Stat(g2, "BEST", X + 680, 466);
        if (r.IsAutoPlay || before == null) best.text = "-";
        else
        {
            int d = r.Score - before.BestScore;
            best.text = Mono(before.BestScore.ToString("N0", CultureInfo.InvariantCulture))
                      + "  <size=50%><color=#FFFFFF8C>" + (d >= 0 ? "+" : "-") + Mathf.Abs(d).ToString("N0", CultureInfo.InvariantCulture) + "</color></size>";
        }

        Line(g2, X, 580, W);

        // ---- 판정 5종 (왼쪽) / 타이밍 (오른쪽) ----
        RectTransform g3 = Group("Judges", 0.6f, 0.5f, new Vector2(30f, 0f));
        for (int k = 0; k < 5; k++)
        {
            float y = 604 + k * 44;
            TextMeshProUGUI l = T(g3, JudgeNames[k], 18, TextAlignmentOptions.MidlineLeft, TextSub, fonts.label, FontStyles.Bold);
            l.characterSpacing = 8f;
            Box(l.rectTransform, X, y, 200, 44);
            judgeCounts[k] = T(g3, Mono("0"), 28, TextAlignmentOptions.MidlineRight, TextMain, fonts.number, FontStyles.Bold);
            Box(judgeCounts[k].rectTransform, X + 200, y, 220, 44);
        }

        double avg = r.AverageErrorMs;
        TimingRow(g3, "FAST", r.Fast.ToString(), X + 580, 604);
        TimingRow(g3, "SLOW", r.Slow.ToString(), X + 580, 648);
        TimingRow(g3, "AVG", (avg > 0.05 ? "+" : "") + avg.ToString("0.0", CultureInfo.InvariantCulture) + "<size=60%> ms</size>", X + 580, 692);
    }

    private TextMeshProUGUI Stat(RectTransform parent, string label, float x, float y)
    {
        TextMeshProUGUI l = T(parent, label, 16, TextAlignmentOptions.MidlineLeft, TextDim, fonts.label, FontStyles.Bold);
        l.characterSpacing = 10f;
        Box(l.rectTransform, x, y, 320, 24);
        TextMeshProUGUI v = T(parent, "", 44, TextAlignmentOptions.MidlineLeft, TextMain, fonts.number, FontStyles.Bold);
        Box(v.rectTransform, x - 2, y + 26, 320, 60);
        return v;
    }

    private void TimingRow(RectTransform parent, string label, string value, float x, float y)
    {
        TextMeshProUGUI l = T(parent, label, 18, TextAlignmentOptions.MidlineLeft, TextSub, fonts.label, FontStyles.Bold);
        l.characterSpacing = 8f;
        Box(l.rectTransform, x, y, 160, 44);
        TextMeshProUGUI v = T(parent, value, 28, TextAlignmentOptions.MidlineRight, TextMain, fonts.number, FontStyles.Bold);
        Box(v.rectTransform, x + 160, y, 260, 44);
    }

    private void Line(RectTransform parent, float x, float y, float w)
    {
        Image l = SettingsUIKit.Img("Line", parent, LineColor);
        Box(l.rectTransform, x, y, w, 1);
    }

    private void BuildFooter()
    {
        RectTransform g = Group("Footer", 1.9f, 0.4f, Vector2.zero);
        float x = 1760f;
        x = TextButton(g, "ENTER", "CONTINUE", x, () => Leave(GoSongSelect));
        TextButton(g, "R", "RETRY", x, () => Leave(GoRetry));
    }

    // 글자만 있는 버튼 (마우스를 올리면 살짝 밝아짐)
    private float TextButton(RectTransform parent, string key, string label, float rightX, Action onClick)
    {
        string txt = "<color=#FFFFFF52>" + key + "</color>   " + label;
        TextMeshProUGUI t = T(parent, txt, 18, TextAlignmentOptions.Center, TextMain, fonts.info, FontStyles.Bold);
        t.characterSpacing = 6f;
        float w = t.GetPreferredValues(txt).x + 36f;
        Image bg = SettingsUIKit.Img("Btn_" + label, parent, Color.white, true);
        bg.sprite = roundSprite; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 32f / 20f;
        Box(bg.rectTransform, rightX - w, 930, w, 40);
        Button b = bg.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        ColorBlock cb = b.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 0f);
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
        cb.pressedColor = new Color(1f, 1f, 1f, 0.16f);
        cb.selectedColor = cb.normalColor;
        cb.fadeDuration = 0.08f;
        b.colors = cb;
        b.onClick.AddListener(() => onClick());
        t.transform.SetParent(bg.transform, false);
        SettingsUIKit.Stretch(t.rectTransform);
        return rightX - w - 16f;
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    void Update()
    {
        FitStage();
        float dt = UiTime.Delta;

        if (!leaving)
        {
            clock += dt * Mathf.Max(0.1f, animationSpeed);
            HandleInput();
        }
        Animate(clock);

        if (leaving)
        {
            leaveT += dt;
            overlay.alpha = Mathf.Max(overlay.alpha, Mathf.Clamp01(leaveT / Mathf.Max(0.01f, fadeOutSeconds)));
            if (leaveT >= fadeOutSeconds && leaveAction != null)
            {
                Action a = leaveAction;
                leaveAction = null;
                a();
            }
        }
    }

    private void FitStage()
    {
        Rect rr = root.rect;
        float k = Mathf.Min(rr.width / 1920f, rr.height / 1080f);
        if (k > 0f && Mathf.Abs(stage.localScale.x - k) > 0.0001f) stage.localScale = new Vector3(k, k, 1f);
    }

    private void HandleInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        bool ok = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame;
        if (ok)
        {
            if (clock < TIntroEnd) clock = TIntroEnd;   // 연출 건너뛰기
            else Leave(GoSongSelect);
        }
        else if (kb.rKey.wasPressedThisFrame && clock > 0.3f)
        {
            Leave(GoRetry);
        }
    }

    private void Leave(Action action)
    {
        if (leaving) return;
        if (clock < TIntroEnd) clock = TIntroEnd;
        leaving = true;
        leaveT = 0f;
        leaveAction = action;
        overlay.blocksRaycasts = true;
    }

    private void GoSongSelect()
    {
        if (CoreManager.Instance != null) CoreManager.Instance.LoadSongSelectScene();
        else Debug.Log("[Result] CoreManager 가 없어 곡 선택 씬으로 돌아가지 못했습니다. (결과 씬 단독 실행 중)");
    }

    private void GoRetry()
    {
        CoreManager cm = CoreManager.Instance;
        if (cm != null && cm.CurrentSongData != null) cm.LoadGameScene(cm.CurrentSongData, cm.IsMvOn, cm.CurrentLevelId);
        else GoSongSelect();
    }

    // t 시각의 화면 상태를 계산해서 그림 (건너뛰기 = t 를 끝으로)
    private void Animate(float t)
    {
        if (!leaving) overlay.alpha = 1f - Smooth(t / Mathf.Max(0.01f, fadeInSeconds * Mathf.Max(0.1f, animationSpeed)));

        foreach (var rv in reveals)
        {
            float p = EaseOut((t - rv.start) / rv.dur);
            rv.cg.alpha = p;
            rv.rt.anchoredPosition = rv.offset * (1f - p);
        }

        // 숫자 카운트 업
        float sp = EaseOut((t - 0.4f) / 1.1f);
        scoreText.text = Mono(Mathf.RoundToInt(r.Score * sp).ToString("N0", CultureInfo.InvariantCulture));
        float ap = EaseOut((t - 0.5f) / 1.0f);
        accText.text = Mono((r.Accuracy * ap).ToString("F2", CultureInfo.InvariantCulture)) + "<size=60%>%</size>";
        comboText.text = Mono(Mathf.RoundToInt(r.MaxCombo * ap).ToString("N0", CultureInfo.InvariantCulture))
                       + "<size=50%><color=#FFFFFF52>  / " + total.ToString("N0", CultureInfo.InvariantCulture) + "</color></size>";
        for (int k = 0; k < 5; k++)
        {
            float jp = EaseOut((t - 0.6f - k * 0.05f) / 0.9f);
            judgeCounts[k].text = Mono(Mathf.RoundToInt(judgeValues[k] * jp).ToString("N0", CultureInfo.InvariantCulture));
            judgeCounts[k].color = judgeValues[k] == 0 ? TextDim : TextMain;
        }

        // 클리어 상태: 자간이 좁혀지며 나타남 / 랭크 · NEW RECORD 는 그 뒤에 페이드
        float s = Mathf.Clamp01((t - TStatus) / 0.5f);
        statusGroup.alpha = Smooth(s);
        statusText.characterSpacing = Mathf.Lerp(34f, 14f, EaseOut(s));
        rankGroup.alpha = Smooth((t - TStatus - 0.15f) / 0.4f);
        newRecordGroup.alpha = Smooth((t - TStatus - 0.3f) / 0.4f);

        // ALL PERFECT 글자만 옅은 무지개 (자간을 바꾼 뒤 칠해야 유지됨)
        if (clearKind == 3) RainbowText.Apply(statusText, Time.unscaledTime);
    }

    // =====================================================================
    //  도우미
    // =====================================================================
    // 순서대로 나타나는 묶음 (start 초에 시작, dur 동안 offset 만큼 밀려 있다가 제자리로)
    private RectTransform Group(string name, float start, float dur, Vector2 offset)
    {
        RectTransform g = SettingsUIKit.Rect(name, stage);
        g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
        g.sizeDelta = new Vector2(1920f, 1080f);
        CanvasGroup cg = g.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        reveals.Add(new Reveal { cg = cg, rt = g, offset = offset, start = start, dur = dur });
        return g;
    }

    // 무대 왼쪽 위 기준 (x, y 는 아래로 +)
    private static void Box(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    private TextMeshProUGUI T(Transform parent, string text, float size, TextAlignmentOptions align, Color color, TMP_FontAsset font, FontStyles style)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt("T", parent, text, size, align, color, style);
        if (font != null) t.font = font;
        t.richText = true;
        return t;
    }

    private static void Shrink(TextMeshProUGUI t, float minRatio)
    {
        float s = t.fontSize;
        t.enableAutoSizing = true;
        t.fontSizeMax = s;
        t.fontSizeMin = s * minRatio;
    }

    private string Mono(string s)
    {
        return monospaceNumbers ? "<mspace=0.62em>" + s + "</mspace>" : s;
    }

    // 원 (지름 64, 9-slice 테두리 32) → pixelsPerUnitMultiplier 로 모서리 반지름 조절
    private Sprite MakeRoundSprite()
    {
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
        ownedTextures.Add(tex);
        return Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(C / 2, C / 2, C / 2, C / 2));
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - Mathf.Pow(1f - x, 3f); }
}