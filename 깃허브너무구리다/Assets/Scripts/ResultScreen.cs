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
    [Tooltip("버튼 · 기본 정보 글자 (아래 난이도 · 작곡가 칸을 비우면 이 글꼴)")] public TMP_FontAsset info;
    [Tooltip("왼쪽 난이도 줄 (예: INSANE 16). 비우면 Info")] public TMP_FontAsset difficulty;
    [Tooltip("왼쪽 곡 정보: 작곡가 줄. 비우면 Info")] public TMP_FontAsset composer;
    [Tooltip("숫자: 점수 · 정확도 · 콤보 · 판정 개수")] public TMP_FontAsset number;
    [Tooltip("큰 점수 숫자 (SCORE 아래). 비우면 Number")] public TMP_FontAsset score;
    [Tooltip("작은 라벨: RESULT · SCORE · ACCURACY · 판정 이름 등")] public TMP_FontAsset label;
    [Tooltip("클리어 상태: ALL PERFECT / FULL COMBO / CLEAR / FAILED")] public TMP_FontAsset status;
    [Tooltip("(지금은 안 씀 — 랭크 시스템 삭제)")] public TMP_FontAsset rank;
}

// ============================================================
// 심플 결과창 (4_Result 씬) — 에셋 없이 코드로 전부 그림
//
//  [사용법] 4_Result 씬에서 기존 결과 UI(Canvas + ResultManager)를 끄거나 지우고,
//           빈 오브젝트에 이 컴포넌트를 붙인 뒤 글꼴만 지정하면 된다.
//
//  [화면 구성 (1920 x 1080 기준)]  흰색 / 회색 위주, 색은 난이도 이름과 클리어 상태에만
//   왼쪽    자켓(squareimg, 둥근 모서리) · 난이도 · 곡명 · 작곡가
//   오른쪽  클리어 상태 · 점수 | 최고 점수 (이번 점수와의 차이 · NEW BEST)
//           TP (Technical Point, 예전 ACCURACY) / MAX COMBO
//   (랭크 시스템은 없앰)
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

    [Header("글자 세로 늘이기 (1 = 그대로, 클수록 길쭉하게)")]
    [Tooltip("작은 라벨 글자: RESULT · SCORE · BEST · TP · MAX COMBO · PERFECT~MISS · FAST/SLOW/AVG 등 (글꼴 = Fonts > Label)")]
    [Range(0.7f, 1.8f)] public float labelHeightScale = 1.15f;
    [Tooltip("숫자 글자: 점수 · 콤보 · 판정 개수 등 (글꼴 = Fonts > Number)")]
    [Range(0.7f, 1.8f)] public float numberHeightScale = 1f;
    [Tooltip("큰 점수 숫자 (글꼴 = Fonts > Score, 비어 있으면 Number 값을 씀)")]
    [Range(0.7f, 1.8f)] public float scoreHeightScale = 1f;
    [Tooltip("나머지 글자: 곡명 · 작곡가 · 난이도 · 클리어 상태 · 버튼")]
    [Range(0.7f, 1.8f)] public float otherHeightScale = 1f;


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
    private CanvasGroup statusGroup, bestGroup;

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

        // 최고 기록: 단독 실행(샘플) · 시작 시간 조정(중간부터 시작)한 판은 저장하지 않음
        bool noSave = standalone || r.IsMidStart;
        before = noSave ? ScoreRecords.Get(r.SongId, r.LevelId) : ScoreRecords.Submit(r);
        isNewRecord = !r.IsAutoPlay && !noSave && r.Score > 0 && (before == null || r.Score > before.BestScore);

        total = Mathf.Max(1, r.TotalNotes > 0 ? r.TotalNotes : r.Excellent + r.Great + r.Good + r.Bad + r.Miss);
        clearKind = r.ClearRank;
        judgeValues[0] = r.Excellent; judgeValues[1] = r.Great; judgeValues[2] = r.Good; judgeValues[3] = r.Bad; judgeValues[4] = r.Miss;

        SettingsUIKit.EnsureFont();
        groupGapEm = numberGroupGap;
        roundSprite = MakeRoundSprite();
        Build();
        Animate(0f);
    }

    void OnDestroy()
    {
        foreach (var t in ownedTextures) if (t != null) Destroy(t);
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

        // (위쪽 RESULT · SPEED · START 줄은 표시하지 않음)
        BuildLeft();
        BuildRight();
        BuildFooter();
        ApplyTextStretch();

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

        Sprite bg = BackgroundBlur.Get(SongArt.Load("Sprites/blurimg", r.SongId, r.LevelId));   // SPECIAL 이면 {곡ID}_4 (없으면 기본), 원본 → 흐리게
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
        if (r.IsMidStart) opt += "     START " + r.MidStartSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
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
        Sprite js = SongArt.Load("Sprites/squareimg", r.SongId, r.LevelId);     // SPECIAL 이면 {곡ID}_4 (없으면 기본)
        if (js != null) jacket.sprite = js;
        else jacket.color = new Color(0.12f, 0.13f, 0.17f, 1f);
        if (clearKind == 0) jacket.color = new Color(0.6f, 0.6f, 0.6f, 1f); // FAILED: 어둡게

        // 난이도 (이름만 난이도 색)  + PRACTICE 모드면 바로 옆에 [⟳ PRACTICE]
        Color dc = DifficultyHelper.GetColor(r.LevelId);
        string diff = string.IsNullOrEmpty(r.DifficultyName) ? DifficultyHelper.GetName(r.LevelId) : r.DifficultyName;
        string diffStr = DifficultyHelper.ColoredText(r.LevelId, diff) + "  " + SongRowUI.FormatDifficulty(r.DifficultyValue);   // SPECIAL 은 솜사탕 그라데이션
        TextMeshProUGUI dt = T(g, diffStr, 20, TextAlignmentOptions.MidlineLeft, TextMain, fonts.difficulty != null ? fonts.difficulty : fonts.info, FontStyles.Bold);
        dt.characterSpacing = 4f;
        Box(dt.rectTransform, 160, 712, 480, 30);
        if (r.IsMidStart)
        {
            float dw = dt.GetPreferredValues(diffStr).x;
            RectTransform pb = PracticeBadge.Practice(g, 26f, fonts.label);
            pb.anchorMin = pb.anchorMax = new Vector2(0f, 1f);
            pb.pivot = new Vector2(0f, 0.5f);
            pb.anchoredPosition = new Vector2(160f + dw + 14f, -(712f + 15f));
        }

        TextMeshProUGUI title = T(g, (r.SongName ?? "") + (r.SubName ?? ""), 40, TextAlignmentOptions.MidlineLeft, TextMain, fonts.title, FontStyles.Bold);
        Box(title.rectTransform, 160, 748, 480, 56);
        Shrink(title, 0.55f);
        TextMeshProUGUI comp = T(g, r.Composer ?? "", 22, TextAlignmentOptions.MidlineLeft, TextSub, fonts.composer != null ? fonts.composer : fonts.info, FontStyles.Normal);
        Box(comp.rectTransform, 160, 804, 480, 32);
        Shrink(comp, 0.7f);

        // 게임 모드: 화면 아래쪽 띠 (오른쪽 [CONTINUE] [RETRY] 버튼과 같은 높이, 그 왼쪽 넓은 영역 — 모드가 없으면 표시 안 함)
        //   MODE ───────────────────────────────────────────────────────────────────   ← 머리줄 (모드만, 배율은 점수 옆 [×] 아이콘)
        //   ┃[◎] HARD JUDGEMENT     ┃[≡] SHADOW            ┃[◇] PERFECTIONIST        ← 모드 칩 (넓게)
        //          135%                   150%                   195%
        if (r.Modes != null && r.Modes.Length > 0)
        {
            const float X0 = 160f, Wd = 1120f, HeadY = 884f, ChipY = 914f, ChipH = 60f, ChipGap = 12f, Icon = 40f;
            var list = new List<GameMode>();
            foreach (int m in r.Modes) if (m >= 0 && m < GameModes.All.Length) list.Add((GameMode)m);

            // 머리줄
            TextMeshProUGUI head = T(g, "MODE", 15, TextAlignmentOptions.MidlineLeft, TextDim, fonts.label, FontStyles.Bold);
            head.characterSpacing = 8f;
            Box(head.rectTransform, X0, HeadY, 100f, 20f);
            float headW = head.GetPreferredValues("MODE").x + 14f;
            Line(g, X0 + headW, HeadY + 10f, Mathf.Max(10f, Wd - headW));

            // 모드 칩 (최대 3개, 같은 폭)
            float cw = (Wd - ChipGap * (GameModes.MaxSelected - 1)) / GameModes.MaxSelected;
            for (int i = 0; i < list.Count; i++)
            {
                GameModeInfo info = GameModes.Info(list[i]);
                float cx = X0 + i * (cw + ChipGap);
                Image chip = SettingsUIKit.Img("ModeChip", g, Color.Lerp(new Color(0.06f, 0.065f, 0.085f, 1f), info.Color, 0.12f));
                chip.sprite = roundSprite; chip.type = Image.Type.Sliced; chip.pixelsPerUnitMultiplier = 32f / 10f;
                Box(chip.rectTransform, cx, ChipY, cw, ChipH);
                // 왼쪽 색 막대: 칩과 같은 높이, 칩의 둥근 모서리 모양대로 잘림 (칩 자체를 마스크로)
                chip.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                Image bar = SettingsUIKit.Img("Accent", chip.rectTransform, info.Color);
                bar.rectTransform.anchorMin = new Vector2(0f, 0f); bar.rectTransform.anchorMax = new Vector2(0f, 1f);
                bar.rectTransform.offsetMin = new Vector2(0f, 0f); bar.rectTransform.offsetMax = new Vector2(5f, 0f);

                RectTransform b = GameModes.CreateBadge(chip.rectTransform, list[i], Icon);
                b.anchorMin = b.anchorMax = new Vector2(0f, 0.5f);
                b.pivot = new Vector2(0f, 0.5f);
                b.anchoredPosition = new Vector2(14f, 0f);

                TextMeshProUGUI nm = T(chip.rectTransform, info.Name, 18, TextAlignmentOptions.BottomLeft, TextMain, fonts.label, FontStyles.Bold);
                nm.characterSpacing = 3f;
                SettingsUIKit.Place(nm.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(Icon + 26f, 1f), new Vector2(-10f, -6f));
                Shrink(nm, 0.65f);
                Color pc = info.Multiplier > 1.0001 ? info.Color : new Color(1f, 1f, 1f, 0.45f);
                TextMeshProUGUI pv = T(chip.rectTransform, GameModes.Percent(info.Multiplier), 15, TextAlignmentOptions.TopLeft, pc, fonts.label, FontStyles.Bold);
                pv.characterSpacing = 2f;
                SettingsUIKit.Place(pv.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(Icon + 26f, 6f), new Vector2(-10f, -1f));
            }
        }
    }

    private void BuildRight()
    {
        const float X = 760f, W = 1000f;

        // AUTO PLAY: 오른쪽(클리어 상태 · 점수 · 판정)은 아무것도 보여 주지 않고, 그 범위 전체를 반투명한 검은 사각형으로 덮고 안내 문구만
        if (r.IsAutoPlay)
        {
            RectTransform ga = Group("AutoPlayCover", 0.3f, 0.5f, new Vector2(30f, 0f));
            Image cover = SettingsUIKit.Img("Cover", ga, new Color(0f, 0f, 0f, 0.55f));
            cover.sprite = roundSprite; cover.type = Image.Type.Sliced; cover.pixelsPerUnitMultiplier = 32f / 18f;
            Box(cover.rectTransform, X - 40, 180, W + 80, 680);
            // 한글이 있는 글꼴 (설정창 글꼴 → Resources/Fonts/SettingsFont → 기본)
            TMP_FontAsset kf = SettingsWindow.RegularFont != null ? SettingsWindow.RegularFont : Resources.Load<TMP_FontAsset>("Fonts/SettingsFont");
            if (kf == null) kf = SettingsUIKit.Font;
            TextMeshProUGUI msg = T(cover.rectTransform, "AUTO PLAY에서는 판정 결과를 제공하지 않습니다.", 30, TextAlignmentOptions.Center, TextSub, kf, FontStyles.Normal);
            SettingsUIKit.Stretch(msg.rectTransform);
            return;
        }

        // ---- 클리어 상태 ----
        RectTransform st = SettingsUIKit.Rect("Status", stage);
        Box(st, X, 200, W, 50);
        statusGroup = st.gameObject.AddComponent<CanvasGroup>();
        string[] names = { "FAILED", "CLEAR", "FULL COMBO", "ALL PERFECT" };
        Color sc = clearKind == 0 ? FailColor : (clearKind == 1 ? TextSub : TextMain);
        // ALL JUST (TP 100%) 는 ALL PERFECT 보다 위
        statusText = T(st, r.IsAllJust ? "ALL JUST" : names[clearKind], 34, TextAlignmentOptions.MidlineLeft, sc, fonts.status, FontStyles.Bold);
        statusText.characterSpacing = 14f;
        SettingsUIKit.Stretch(statusText.rectTransform);

        // ---- 점수 | 최고 점수 ----
        const float BestW = 300f;
        RectTransform g1 = Group("Score", 0.3f, 0.5f, new Vector2(30f, 0f));
        TextMeshProUGUI sl = T(g1, "SCORE", 16, TextAlignmentOptions.MidlineLeft, TextDim, fonts.label, FontStyles.Bold);
        sl.characterSpacing = 10f;
        Box(sl.rectTransform, X, 268, 120, 24);
        scoreText = T(g1, Mono("0"), 110, TextAlignmentOptions.MidlineLeft, TextMain, fonts.score != null ? fonts.score : fonts.number, FontStyles.Bold);
        Box(scoreText.rectTransform, X - 6, 290, W - BestW - 40, 124);
        float labelEnd = X + sl.GetPreferredValues("SCORE").x + 16f;    // SCORE 글자 오른쪽 (배지들이 차례로 붙음)
        if (r.IsMidStart)
        {
            // PRACTICE 모드: 점수는 0 (GameManager 가 0 으로 넘김), SCORE 글자 옆에 [⊘ NOT SAVED]
            RectTransform ns = PracticeBadge.NotSaved(g1, 24f, fonts.label);
            ns.anchorMin = ns.anchorMax = new Vector2(0f, 1f);
            ns.pivot = new Vector2(0f, 0.5f);
            ns.anchoredPosition = new Vector2(labelEnd, -(268f + 12f));
            labelEnd += ns.sizeDelta.x + 12f;
        }
        // 게임 모드 점수 배율: 모드 아이콘과 같은 모양의 상자들
        //  흰 상자   : 모드 배율 + MODE RANDOM 의 A 몫(조합 배율 보너스)  → 최대 420% (400% + 20%p)
        //  솜사탕 상자: MODE RANDOM 의 B 몫(특별 모드 가산 0 ~ 10%p)  — MODE RANDOM 판에만
        //  예) [410%] [+7%p]  → 최종 417%
        if (r.Modes != null && r.Modes.Length > 0)
        {
            bool rnd = r.RandomBonus > 0.0001;
            var picked = new List<GameMode>();
            foreach (int m in r.Modes) if (m >= 0 && m < GameModes.All.Length) picked.Add((GameMode)m);
            double b = rnd ? Math.Min(r.RandomBonus, GameModes.RandomSpecialBonus(picked)) : 0.0;
            double whiteK = Math.Min(GameModes.MaxMultiplier + GameModes.RandomBonusComboMax, r.ScoreMultiplier - b);
            float x = labelEnd;
            x += Pill(g1, x, 266f, 28f, null, GameModes.Percent(whiteK), new Color(0.92f, 0.94f, 1f, 1f), false) + 8f;
            if (rnd)
                Pill(g1, x, 266f, 28f, null, GameModes.BonusText(b), DifficultyHelper.SpecialAt(0.15f), true);
        }

        // 최고 점수 묶음 (오른쪽): 세로 구분선 · BEST · 이전 최고 점수 · 이번 점수와의 차이 / NEW BEST
        RectTransform bs = SettingsUIKit.Rect("Best", stage);
        Box(bs, X + W - BestW, 268, BestW, 146);
        bestGroup = bs.gameObject.AddComponent<CanvasGroup>();
        Image div = SettingsUIKit.Img("Divider", bs, LineColor);
        Box(div.rectTransform, -28, 6, 1, 134);
        TextMeshProUGUI bl = T(bs, "BEST", 16, TextAlignmentOptions.MidlineLeft, TextDim, fonts.label, FontStyles.Bold);
        bl.characterSpacing = 10f;
        Box(bl.rectTransform, 0, 0, BestW, 24);

        bool hasBest = before != null && !r.IsAutoPlay;
        string bestStr = hasBest ? Num(before.BestScore) : "-";
        TextMeshProUGUI bv = T(bs, Mono(bestStr), 52, TextAlignmentOptions.MidlineLeft, hasBest ? TextMain : TextDim, fonts.number, FontStyles.Bold);
        Box(bv.rectTransform, -2, 28, BestW, 66);
        Shrink(bv, 0.6f);

        if (isNewRecord)
        {
            // NEW BEST: 흰 알약 + 검정 글자, 옆에 올라간 점수
            string up = before != null ? "+" + Num(r.Score - before.BestScore) : "FIRST PLAY";
            TextMeshProUGUI pillText = T(bs, "NEW BEST", 15, TextAlignmentOptions.Center, new Color(0.05f, 0.05f, 0.05f, 1f), fonts.label, FontStyles.Bold);
            pillText.characterSpacing = 6f;
            float pw = pillText.GetPreferredValues("NEW BEST").x + 28f;
            Image pill = SettingsUIKit.Img("NewBest", bs, Color.white);
            pill.sprite = roundSprite; pill.type = Image.Type.Sliced; pill.pixelsPerUnitMultiplier = 32f / 13f;
            Box(pill.rectTransform, 0, 104, pw, 28);
            pillText.transform.SetParent(pill.transform, false);
            SettingsUIKit.Stretch(pillText.rectTransform);
            TextMeshProUGUI upText = T(bs, Mono(up), 22, TextAlignmentOptions.MidlineLeft, TextMain, fonts.number, FontStyles.Bold);
            Box(upText.rectTransform, pw + 12, 104, BestW - pw - 12, 28);
        }
        else
        {
            string note;
            if (r.IsAutoPlay) note = "AUTO PLAY  ·  NOT SAVED";
            else if (r.IsMidStart) note = "PRACTICE  ·  NOT SAVED";
            else if (before == null) note = "";
            else
            {
                int d = r.Score - before.BestScore;
                note = (d >= 0 ? "+" : "-") + Num(Mathf.Abs(d));
            }
            TextMeshProUGUI nt = T(bs, note, 22, TextAlignmentOptions.MidlineLeft, TextSub, fonts.number, FontStyles.Bold);
            if (r.IsAutoPlay || r.IsMidStart) { nt.fontSize = 15; nt.characterSpacing = 6f; nt.font = fonts.label != null ? fonts.label : nt.font; }
            Box(nt.rectTransform, 0, 104, BestW, 28);
        }

        // ---- 구분선 ----
        RectTransform g2 = Group("Stats", 0.45f, 0.5f, new Vector2(30f, 0f));
        Line(g2, X, 440, W);

        // ---- TP / MAX COMBO ----
        //  FAILED · PRACTICE 판은 TP 를 아예 표시하지 않고, MAX COMBO 를 TP 자리(왼쪽)로 당김
        bool noTp = r.IsMidStart || r.IsFailed;
        if (!noTp)
        {
            accText = Stat(g2, "TP", X, 466);
            comboText = Stat(g2, "MAX COMBO", X + 420, 466);
        }
        else
        {
            accText = T(g2, "", 10, TextAlignmentOptions.MidlineLeft, TextMain, fonts.number, FontStyles.Bold);
            accText.gameObject.SetActive(false);                        // (숫자 올라가는 연출용 자리만, 보이지 않음)
            comboText = Stat(g2, "MAX COMBO", X, 466);
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
            if (k == 0)
            {
                // PERFECT 중 JUST PERFECT 수 (±15ms, 롱노트 중간 판정 포함) → PERFECT 670  +220 처럼 옆에 '+숫자'
                TextMeshProUGUI js = T(g3, "+" + Num(r.JustPerfect), 18, TextAlignmentOptions.MidlineLeft, TextDim, fonts.number, FontStyles.Bold);
                Box(js.rectTransform, X + 432, y, 140, 44);
            }
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

    // 키캡 + 설명 버튼 ( [ R ] RETRY ) — 마우스를 올리면 바탕이 살짝 밝아짐
    private float TextButton(RectTransform parent, string key, string label, float rightX, Action onClick)
    {
        Image bg = SettingsUIKit.Img("Btn_" + label, parent, Color.white, true);
        bg.sprite = roundSprite; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 32f / 12f;
        RectTransform hint = KeyHintUI.Build(bg.rectTransform, fonts.info, fonts.info,
            new[] { new KeyHintUI.Item(label, key) }, 18f, 0, TextMain);
        float w = hint.sizeDelta.x + 28f;
        Box(bg.rectTransform, rightX - w, 926, w, 48);
        hint.anchorMin = hint.anchorMax = new Vector2(0.5f, 0.5f);
        hint.anchoredPosition = Vector2.zero;
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
        return rightX - w - 12f;
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

        if (r.IsAutoPlay) return;   // AUTO PLAY: 오른쪽 숫자 · 상태 없음

        // 숫자 카운트 업
        float sp = EaseOut((t - 0.4f) / 1.1f);
        scoreText.text = Mono(Num(Mathf.RoundToInt(r.Score * sp)));
        float ap = EaseOut((t - 0.5f) / 1.0f);
        accText.text = Mono((r.Accuracy * ap).ToString("F2", CultureInfo.InvariantCulture)) + "<size=60%>%</size>";
        comboText.text = Mono(Num(Mathf.RoundToInt(r.MaxCombo * ap)))
                       + "<size=50%><color=#FFFFFF52>  / " + Num(total) + "</color></size>";
        for (int k = 0; k < 5; k++)
        {
            float jp = EaseOut((t - 0.6f - k * 0.05f) / 0.9f);
            judgeCounts[k].text = Mono(Num(Mathf.RoundToInt(judgeValues[k] * jp)));
            judgeCounts[k].color = judgeValues[k] == 0 ? TextDim : TextMain;
        }

        // 클리어 상태: 자간이 좁혀지며 나타남 / 최고 점수는 점수가 다 올라간 뒤 오른쪽에서 밀려 들어옴
        float s = Mathf.Clamp01((t - TStatus) / 0.5f);
        statusGroup.alpha = Smooth(s);
        statusText.characterSpacing = Mathf.Lerp(34f, 14f, EaseOut(s));
        float bp = Smooth((t - 1.3f) / 0.45f);
        bestGroup.alpha = bp;
        RectTransform brt = (RectTransform)bestGroup.transform;
        brt.anchoredPosition = new Vector2(760f + 1000f - 300f + 24f * (1f - bp), -268f);

        // ALL PERFECT 글자만 옅은 무지개 (자간을 바꾼 뒤 칠해야 유지됨)
        if (clearKind == 3 || r.IsAllJust) RainbowText.Apply(statusText, Time.unscaledTime * (r.IsAllJust ? 1.8f : 1f));
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

    // 모드 아이콘과 같은 모양의 둥근 상자 안에 [그림] 글자 — 폭은 내용에 맞춤, 폭을 돌려줌
    //  cotton = true : 연한 솜사탕 색 (테두리 · 바탕이 분홍 → 라벤더 → 하늘, 글자도 그라데이션)
    private float Pill(RectTransform parent, float x, float y, float h, Sprite glyph, string text, Color c, bool cotton)
    {
        const float Pad = 10f, Gap = 6f;
        float g = glyph != null ? h * 0.62f : 0f;
        TextMeshProUGUI t = T(parent, cotton ? DifficultyHelper.SpecialGradientText(text) : text, 17, TextAlignmentOptions.MidlineLeft, cotton ? Color.white : c, fonts.label, FontStyles.Bold);
        t.characterSpacing = 2f;
        float tw = t.GetPreferredValues(t.text).x + 2f;
        float w = Pad + (g > 0f ? g + Gap : 0f) + tw + Pad;

        Image ring = SettingsUIKit.Img("PillRing", parent, new Color(c.r, c.g, c.b, cotton ? 0.75f : 0.55f));
        ring.sprite = GameModes.BadgeSprite; ring.type = Image.Type.Sliced; ring.pixelsPerUnitMultiplier = 16f / 7f;
        if (cotton) { ring.sprite = SpecialArt.Tint(GameModes.BadgeSprite); ring.color = new Color(1f, 1f, 1f, 0.8f); }
        Box(ring.rectTransform, x, y, w, h);
        Image bg = SettingsUIKit.Img("PillBg", ring.rectTransform, cotton ? new Color(0.13f, 0.10f, 0.16f, 1f)
                                                                           : new Color(c.r * 0.12f + 0.02f, c.g * 0.12f + 0.02f, c.b * 0.12f + 0.035f, 1f));
        bg.sprite = GameModes.BadgeSprite; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 16f / 6f;
        SettingsUIKit.Stretch(bg.rectTransform);
        bg.rectTransform.offsetMin = new Vector2(1.5f, 1.5f); bg.rectTransform.offsetMax = new Vector2(-1.5f, -1.5f);
        if (glyph != null)
        {
            Image gi = SettingsUIKit.Img("Glyph", ring.rectTransform, c);
            gi.sprite = glyph; gi.preserveAspect = true;
            gi.rectTransform.anchorMin = gi.rectTransform.anchorMax = gi.rectTransform.pivot = new Vector2(0f, 0.5f);
            gi.rectTransform.anchoredPosition = new Vector2(Pad - 2f, 0f);
            gi.rectTransform.sizeDelta = new Vector2(g, g);
        }
        t.transform.SetParent(ring.transform, false);
        RectTransform tr = t.rectTransform;
        tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0f, 0.5f);
        tr.anchoredPosition = new Vector2(Pad + (g > 0f ? g + Gap : 0f), 0f);
        tr.sizeDelta = new Vector2(tw, h);
        return w;
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
        // [버그 수정] 다른 글꼴은 줄 높이(위아래 여백)가 커서 글자가 칸 높이를 넘으면 '…' 처리로 통째로 사라졌다 (점수가 안 보임)
        //  → 칸을 넘어도 그대로 그림 (Shrink 를 쓰는 곡명 · 작곡가는 그 안에서 다시 줄여 맞춤)
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    private static void Shrink(TextMeshProUGUI t, float minRatio)
    {
        float s = t.fontSize;
        t.enableAutoSizing = true;
        t.fontSizeMax = s;
        t.fontSizeMin = s * minRatio;
    }

    // 글자 세로 늘이기: 글꼴이 납작해 보일 때 인스펙터 값만큼 세로로 늘림 (글자 칸의 가운데를 기준으로)
    //  라벨(fonts.label) · 숫자(fonts.number) · 나머지 따로. (라벨 · 숫자 글꼴을 안 넣었으면 '나머지' 값을 씀)
    private void ApplyTextStretch()
    {
        foreach (var t in stage.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            float k;
            if (t == scoreText) k = fonts.score != null ? scoreHeightScale : (fonts.number != null ? numberHeightScale : otherHeightScale);
            else if (fonts.label != null && t.font == fonts.label) k = labelHeightScale;
            else if (fonts.number != null && t.font == fonts.number) k = numberHeightScale;
            else k = otherHeightScale;
            if (Mathf.Abs(k - 1f) < 0.001f) continue;
            RectTransform rt = t.rectTransform;
            float h = rt.rect.height;
            Vector3 sc = rt.localScale;
            rt.localScale = new Vector3(sc.x, sc.y * k, sc.z);
            // 피벗 기준으로 늘어나므로, 글자 칸의 가운데가 제자리에 있도록 되돌림
            float delta = (0.5f - rt.pivot.y) * h * (k - 1f);
            rt.anchoredPosition -= new Vector2(0f, delta);
        }
    }

    // 한글이 있는 글꼴 (설정창 글꼴 → Resources/Fonts/SettingsFont → 기본)
    private static TMP_FontAsset KoreanFont()
    {
        TMP_FontAsset f = SettingsWindow.RegularFont != null ? SettingsWindow.RegularFont : Resources.Load<TMP_FontAsset>("Fonts/SettingsFont");
        return f != null ? f : SettingsUIKit.Font;
    }

    // 큰 숫자 표기: 쉼표 대신 띄어쓰기 (1 000 000) — 글꼴에 쉼표가 없어도 보이도록
    //  띄어쓰기는 글자가 아니라 <space> 간격으로 → 고정 폭 숫자(mspace)에서도 숫자 한 칸만큼 벌어지지 않고 좁게
    [Tooltip("점수 등 큰 숫자의 세 자리 묶음 사이 간격 (글자 크기 비율, 0.25 = 글자 크기의 25%)")]
    [Range(0f, 0.6f)] public float numberGroupGap = 0.22f;
    private static float groupGapEm = 0.22f;
    private static string Num(long v)
    {
        string d = Math.Abs(v).ToString(CultureInfo.InvariantCulture);
        string gap = "<space=" + groupGapEm.ToString("0.###", CultureInfo.InvariantCulture) + "em>";
        var sb = new System.Text.StringBuilder(d.Length * 2 + 16);
        if (v < 0) sb.Append('-');
        for (int i = 0; i < d.Length; i++)
        {
            if (i > 0 && (d.Length - i) % 3 == 0) sb.Append(gap);
            sb.Append(d[i]);
        }
        return sb.ToString();
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