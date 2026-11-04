using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = SettingsUIKit;

// ============================================================
// 곡 정보 화면 (곡 선택 → 게임 사이에 뜨는 별도 화면, 씬에 배치할 필요 없음)
//
//  흐름
//   1) 곡 선택에서 Enter → CoreManager.LoadGameScene → SongIntroOverlay.Show
//   2) 검은 막이 비스듬히 왼쪽 → 오른쪽으로 덮음 (덮이면 onCovered → 게임 씬 로드 시작)
//   3) 배경(blurimg) · 자켓 · 난이도 · 곡명 · 작곡가/일러스트/보컬 · BPM 이 차례로 등장
//   4) 게임 씬이 준비되면(GameManager → NotifyGameReady) 최소 표시 시간을 채운 뒤
//      내용이 왼쪽으로 빠지고, 검은 막이 오른쪽으로 걷히며 게임 화면이 드러남 (IsRevealing)
//   * 일시정지 → RETRY 로 다시 시작할 때는 짧게 보여 준다.
//   * 15초 안에 게임이 준비되지 않으면 그냥 걷힌다 (안전장치).
//
//  데이터: SongData(songs.json), 자켓 Resources/Sprites/squareimg/{id}, 배경 Sprites/blurimg/{id}
//  글자가 칸보다 길면 자르지 않고 칸에 들어갈 때까지 글자 크기를 줄인다 (TMP Auto Size)
//  폰트: 곡 선택창(SongSelectManager) 인스펙터 > 곡 정보 화면 폰트 에서 항목별로 지정
// ============================================================
[Serializable]
public class SongIntroFonts
{
    [Tooltip("난이도 (EASY / HARD / INSANE)")] public TMP_FontAsset difficulty;
    [Tooltip("곡명")] public TMP_FontAsset title;
    [Tooltip("부제 (SubName)")] public TMP_FontAsset subName;
    [Tooltip("정보 라벨 (MUSIC · ILLUST · VOCAL · BPM)")] public TMP_FontAsset label;
    [Tooltip("정보 값 (작곡가 · 일러스트 · 보컬 · BPM)")] public TMP_FontAsset value;
    [Tooltip("왼쪽 아래 플레이 설정 (SPEED · MV · AUTO)")] public TMP_FontAsset options;
    [Tooltip("오른쪽 아래 LOADING / READY")] public TMP_FontAsset status;
}

public class SongIntroOverlay : MonoBehaviour
{
    private static SongIntroOverlay instance;

    // 곡명 등 일본어/한자가 들어가는 글자에 쓸 폰트 (곡 선택창 / 게임 씬에서 넣어 줌). 없으면 기본 폰트
    public static TMP_FontAsset PreferredTitleFont;
    // 항목별 폰트 (곡 선택창 인스펙터에서 넣어 줌, 비우면 기본)
    public static SongIntroFonts Fonts;

    public static bool IsActive { get { return instance != null; } }

    // GAME SETTING(5_Test) 으로 들어갈 때: 난이도 회색 'UNKNOWN', 제목 'GAME SETTING', 부제, 정보 줄은 작곡가(MUSIC)만 (CoreManager.LoadTestScene 이 켬)
    public static bool GameSettingMode;
    private bool gameSetting;
    public static bool IsRevealing { get { return instance != null && instance.revealing; } }

    private const float NormalHold = 2.4f;   // 내용이 다 나온 뒤 최소 표시 시간
    private const float QuickHold = 0.5f;    // RETRY 때
    private const float SafetyTimeout = 15f;
    private const float InfoWidth = 760f;            // 오른쪽 정보 칸 폭 = 곡명 최대 폭 = 구분선 길이
    private const float JacketCornerRadius = 28f;    // 자켓 모서리 반지름 (px)

    private SongData song;
    private int level;
    private bool quick;
    private Action onCovered;
    private bool gameReady;
    private bool revealing;

    // UI
    private CanvasGroup content;
    private ParallelogramGraphic curtain;
    private Image bgImage, tint, loadBar, jacket;
    private RectTransform jacketRt, infoRoot, divider;
    private TextMeshProUGUI statusText;
    private readonly List<RectTransform> staggered = new List<RectTransform>();
    private readonly List<CanvasGroup> staggeredGroups = new List<CanvasGroup>();
    private readonly List<Vector2> staggeredBase = new List<Vector2>();
    private readonly List<bool> staggeredFromLeft = new List<bool>();

    // =====================================================================
    //  외부에서 부르는 함수
    // =====================================================================
    public static void Show(SongData song, int levelId, bool quick, Action onCovered)
    {
        if (instance != null) Destroy(instance.gameObject);
        GameObject go = new GameObject("SongIntroOverlay", typeof(RectTransform));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SongIntroOverlay>();
        instance.song = song;
        instance.level = levelId;
        instance.quick = quick;
        instance.onCovered = onCovered;
        instance.gameSetting = GameSettingMode;
        GameSettingMode = false;          // 한 번 쓰고 끔 (다음 곡 진입은 평소대로)
        instance.Build();
        instance.StartCoroutine(instance.Run());
    }

    // 게임 씬의 준비(채보·음악·배경 로드)가 끝났을 때 GameManager 가 부른다
    public static void NotifyGameReady()
    {
        if (instance != null) instance.gameReady = true;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // =====================================================================
    //  화면 만들기 (1920 x 1080 기준)
    // =====================================================================
    private void Build()
    {
        K.EnsureFont();
        SongIntroFonts F = Fonts ?? new SongIntroFonts();
        TMP_FontAsset baseTitle = PreferredTitleFont != null ? PreferredTitleFont : K.Font; // 일본어 등이 나오는 칸의 기본
        TMP_FontAsset fDiff = F.difficulty != null ? F.difficulty : K.Font;
        TMP_FontAsset fTitle = F.title != null ? F.title : baseTitle;
        TMP_FontAsset fSub = F.subName != null ? F.subName : baseTitle;
        TMP_FontAsset fLabel = F.label != null ? F.label : K.Font;
        TMP_FontAsset fValue = F.value != null ? F.value : baseTitle;
        TMP_FontAsset fOptions = F.options != null ? F.options : K.Font;
        TMP_FontAsset fStatus = F.status != null ? F.status : K.Font;
        Color dc = gameSetting ? new Color(0.55f, 0.55f, 0.58f, 1f) : DifficultyHelper.GetColor(level);   // GAME SETTING: 회색

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 800; // 게임/곡 선택 UI 위, FPS 표시(1000) 아래
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>(); // 아래 화면 클릭 차단

        RectTransform root = (RectTransform)transform;

        // 검은 막 (비스듬한 평행사변형, 화면보다 넓게)
        curtain = K.Rect("Curtain", root).gameObject.AddComponent<ParallelogramGraphic>();
        curtain.color = new Color(0.02f, 0.025f, 0.04f, 1f);
        curtain.Skew = 420f;
        curtain.FillAmount = 0f;
        curtain.raycastTarget = true;
        K.Place(curtain.rectTransform, Vector2.zero, Vector2.one, new Vector2(-440f, 0f), new Vector2(440f, 0f));

        // 내용 (검은 막 위)
        RectTransform contentRt = K.Rect("Content", root);
        K.Stretch(contentRt);
        content = contentRt.gameObject.AddComponent<CanvasGroup>();
        content.alpha = 0f;
        content.blocksRaycasts = false;

        // 배경: 곡의 blurimg + 어둡게
        bgImage = K.Img("Background", contentRt, Color.white);
        K.Stretch(bgImage.rectTransform);
        Sprite bg = Resources.Load<Sprite>("Sprites/blurimg/" + song.Id);
        if (bg != null)
        {
            bgImage.sprite = bg;
            AspectRatioFitter f = bgImage.gameObject.AddComponent<AspectRatioFitter>();
            f.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            f.aspectRatio = bg.rect.width / Mathf.Max(1f, bg.rect.height);
        }
        else bgImage.color = new Color(0.06f, 0.07f, 0.1f, 1f);
        tint = K.Img("Tint", contentRt, new Color(0f, 0f, 0f, 0.55f));
        K.Stretch(tint.rectTransform);

        // 자켓
        // 자켓: 둥근 모서리 마스크 안에 넣는다 (마스크가 움직이므로 등장 연출도 마스크 기준)
        Image jacketMask = K.Img("JacketMask", contentRt, Color.white);
        jacketMask.sprite = CreateRoundedSprite();
        jacketMask.type = Image.Type.Sliced;
        jacketMask.pixelsPerUnitMultiplier = 32f / JacketCornerRadius; // 텍스처 테두리 32px → 실제 반지름 JacketCornerRadius
        jacketMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        jacketRt = jacketMask.rectTransform;
        K.Center(jacketRt, new Vector2(-420f, 20f), new Vector2(520f, 520f));
        jacket = K.Img("Jacket", jacketRt, Color.white);
        K.Stretch(jacket.rectTransform);
        Sprite js = Resources.Load<Sprite>("Sprites/squareimg/" + song.Id);
        jacket.sprite = js;
        jacket.preserveAspect = false; // 정사각형 자켓이 마스크를 꽉 채우도록
        if (js == null) jacket.color = new Color(0.12f, 0.13f, 0.17f, 1f);
        AddStagger(jacketRt, true);

        // 오른쪽 정보 (왼쪽 정렬, x = 0 부터)
        infoRoot = K.Rect("Info", contentRt);
        K.Center(infoRoot, new Vector2(390f, 20f), new Vector2(InfoWidth, 560f));

        float y = 250f;
        // 난이도 태그
        // 난이도: 이름만, 난이도 대표 색 (예: INSANE)
        TextMeshProUGUI diff = K.Txt("Difficulty", infoRoot, gameSetting ? "UNKNOWN" : DifficultyHelper.GetName(level), 32, TextAlignmentOptions.Left, dc, FontStyles.Bold);
        diff.font = fDiff;
        diff.characterSpacing = 8f;
        FitText(diff, 32f, 16f);
        LeftTop(diff.rectTransform, 0f, y, InfoWidth, 46f);
        AddStagger(diff.rectTransform, false);
        y -= 70f;

        // 곡명 / 부제
        TextMeshProUGUI title = K.Txt("Title", infoRoot, gameSetting ? "GAME SETTING" : song.Name ?? "", 64, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
        title.font = fTitle;
        FitText(title, 64f, 24f);
        LeftTop(title.rectTransform, 0f, y, InfoWidth, 80f);
        AddStagger(title.rectTransform, false);
        y -= 78f;
        if (!string.IsNullOrWhiteSpace(song.SubName))
        {
            TextMeshProUGUI sub = K.Txt("SubName", infoRoot, song.SubName.Trim(), 28, TextAlignmentOptions.Left, new Color(1f, 1f, 1f, 0.75f));
            sub.font = fSub;
            FitText(sub, 28f, 14f);
            LeftTop(sub.rectTransform, 0f, y, InfoWidth, 40f);
            AddStagger(sub.rectTransform, false);
            y -= 44f;
        }

        // 구분선
        y -= 14f;
        Image line = K.Img("Divider", infoRoot, dc);
        divider = line.rectTransform;
        LeftTop(divider, 0f, y, 0f, 3f);
        y -= 30f;

        // 정보 줄
        if (gameSetting)
        {
            // GAME SETTING: 제목(GAME SETTING) · 부제 · 작곡가만 (일러스트 · 보컬 · BPM 없음)
            y = InfoRow("MUSIC", song.Composer, y, fLabel, fValue);
        }
        else
        {
            y = InfoRow("MUSIC", song.Composer, y, fLabel, fValue);
            y = InfoRow("ILLUST", song.illustration, y, fLabel, fValue);
            y = InfoRow("VOCAL", song.Vocal, y, fLabel, fValue);
        }
        // BPM: songs.json 의 "BPM" 글자를 그대로 표시 (예: "132", "132-250")
        if (!gameSetting && !string.IsNullOrWhiteSpace(song.BPM)) y = InfoRow("BPM", song.BPM.Trim(), y, fLabel, fValue);

        // 아래: 플레이 설정 / 로딩
        var s = SettingsStore.Current;
        string opt = (gameSetting ? "GAME SETTING     " : "") + "SPEED  x" + s.SettingSpeed.ToString("0.0", CultureInfo.InvariantCulture);
        if (CoreManager.Instance != null && CoreManager.Instance.IsMvOn && song.HasMV == 1) opt += "     MV ON";
        if (CoreManager.Instance != null && CoreManager.Instance.IsAutoPlay) opt += "     AUTO PLAY";
        TextMeshProUGUI optText = K.Txt("Options", contentRt, opt, 20, TextAlignmentOptions.BottomLeft, K.TextSub);
        optText.font = fOptions;
        FitText(optText, 20f, 12f);
        K.Place(optText.rectTransform, Vector2.zero, new Vector2(0.6f, 0f), new Vector2(64f, 44f), new Vector2(0f, 80f));
        AddStagger(optText.rectTransform, false);

        statusText = K.Txt("Status", contentRt, "LOADING", 20, TextAlignmentOptions.BottomRight, K.TextSub, FontStyles.Bold);
        statusText.font = fStatus;
        statusText.characterSpacing = 10f;
        K.Place(statusText.rectTransform, new Vector2(0.6f, 0f), new Vector2(1f, 0f), new Vector2(0f, 44f), new Vector2(-64f, 80f));

        Image barBg = K.Img("LoadBarBg", contentRt, new Color(1f, 1f, 1f, 0.08f));
        K.Place(barBg.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 4f));
        loadBar = K.Img("LoadBar", barBg.rectTransform, dc);
        loadBar.rectTransform.anchorMin = Vector2.zero;
        loadBar.rectTransform.anchorMax = new Vector2(0f, 1f);
        loadBar.rectTransform.offsetMin = Vector2.zero;
        loadBar.rectTransform.offsetMax = Vector2.zero;

    }

    // 차례로 등장할 요소 등록 (fromLeft: 왼쪽에서 / 아니면 아래에서 올라옴)
    private void AddStagger(RectTransform r, bool fromLeft)
    {
        CanvasGroup g = r.gameObject.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        staggered.Add(r);
        staggeredGroups.Add(g);
        staggeredBase.Add(r.anchoredPosition);
        staggeredFromLeft.Add(fromLeft);
    }

    // 칸보다 길면 자르지 않고 글자 크기를 줄여서 한 줄에 다 들어가게
    private static void FitText(TextMeshProUGUI t, float max, float min)
    {
        t.enableAutoSizing = true;
        t.fontSizeMax = max;
        t.fontSizeMin = min;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.overflowMode = TextOverflowModes.Overflow;
    }

    // "LABEL   값" 한 줄. 값이 비어 있으면 줄을 만들지 않음
    private float InfoRow(string label, string value, float y, TMP_FontAsset labelFont, TMP_FontAsset valueFont)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "-" || value.Trim() == "0") return y;
        RectTransform row = K.Rect("Row_" + label, infoRoot);
        LeftTop(row, 0f, y, InfoWidth, 40f);
        TextMeshProUGUI l = K.Txt("Label", row, label, 17, TextAlignmentOptions.Left, K.TextSub, FontStyles.Bold);
        l.font = labelFont;
        l.characterSpacing = 8f;
        K.Place(l.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(130f, 0f));
        TextMeshProUGUI v = K.Txt("Value", row, value.Trim(), 26, TextAlignmentOptions.Left, Color.white);
        v.font = valueFont;
        FitText(v, 26f, 12f);
        K.Place(v.rectTransform, Vector2.zero, Vector2.one, new Vector2(140f, 0f), Vector2.zero);
        AddStagger(row, false);
        return y - 46f;
    }

    // =====================================================================
    //  연출
    // =====================================================================
    private IEnumerator Run()
    {
        // 1) 검은 막이 왼쪽 → 오른쪽으로 덮음
        float t = 0f, cover = quick ? 0.3f : 0.45f;
        curtain.FillFromRight = false;
        while (t < cover)
        {
            t += UiTime.Delta;
            curtain.FillAmount = EaseInOut(Mathf.Clamp01(t / cover));
            yield return null;
        }
        curtain.FillAmount = 1f;

        // 덮였으니 게임 씬 로드 시작
        if (onCovered != null) { Action a = onCovered; onCovered = null; a(); }

        // 2) 내용 등장
        float shown = 0f;
        float appear = quick ? 0.35f : 0.9f;
        float hold = quick ? QuickHold : NormalHold;
        float waited = 0f;
        while (true)
        {
            float dt = UiTime.Delta;
            shown += dt;
            waited += dt;

            content.alpha = Smooth(shown / 0.35f);
            jacketRt.localScale = Vector3.one * Mathf.Lerp(1.05f, 1f, Smooth(shown / 0.6f));
            for (int i = 0; i < staggered.Count; i++)
            {
                float start = quick ? 0f : 0.12f + i * 0.055f;
                float p = Smooth((shown - start) / 0.45f);
                staggeredGroups[i].alpha = p;
                Vector2 from = staggeredBase[i] + (staggeredFromLeft[i] ? new Vector2(-60f, 0f) : new Vector2(0f, -22f)); // 자켓은 왼쪽에서, 글자는 아래에서
                staggered[i].anchoredPosition = Vector2.Lerp(from, staggeredBase[i], p);
            }
            divider.sizeDelta = new Vector2(Mathf.Lerp(0f, InfoWidth, EaseOut(Mathf.Clamp01((shown - 0.35f) / 0.6f))), 3f); // 곡명 칸 최대 폭과 같은 길이

            // 로딩 막대: 준비 전에는 90%까지 천천히, 준비되면 꽉 참
            float target = gameReady ? 1f : 0.9f * (1f - Mathf.Exp(-waited * 1.2f));
            float cur = loadBar.rectTransform.anchorMax.x;
            loadBar.rectTransform.anchorMax = new Vector2(Mathf.MoveTowards(cur, target, dt * 2f), 1f);
            statusText.text = gameReady ? "READY" : "LOADING";

            bool minDone = shown >= appear + hold;
            if ((gameReady && minDone && loadBar.rectTransform.anchorMax.x >= 0.999f) || waited >= SafetyTimeout) break;
            yield return null;
        }

        // 3) 내용이 왼쪽으로 빠지고, 검은 막이 오른쪽으로 걷힘
        revealing = true;
        t = 0f;
        float outDur = quick ? 0.4f : 0.6f;
        Vector2 basePos = ((RectTransform)content.transform).anchoredPosition;
        curtain.FillFromRight = true;
        while (t < outDur)
        {
            t += UiTime.Delta;
            float p = Mathf.Clamp01(t / outDur);
            float pc = Smooth(p / 0.6f);
            content.alpha = 1f - pc;
            ((RectTransform)content.transform).anchoredPosition = basePos + new Vector2(-80f * EaseIn(p), 0f);
            curtain.FillAmount = 1f - EaseInOut(Mathf.Clamp01((p - 0.25f) / 0.75f));
            yield return null;
        }
        Destroy(gameObject);
    }

    // =====================================================================
    //  도우미
    // =====================================================================
    // 원 텍스처(지름 64, 9-slice 테두리 32) → Image.pixelsPerUnitMultiplier 로 모서리 반지름 조절
    private static Sprite CreateRoundedSprite()
    {
        const int C = 64;
        Texture2D tex = new Texture2D(C, C, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[C * C];
        for (int y = 0; y < C; y++)
            for (int x = 0; x < C; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(C * 0.5f, C * 0.5f));
                float a = Mathf.Clamp01(C * 0.5f - d + 0.5f);
                px[y * C + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(C / 2, C / 2, C / 2, C / 2));
    }
    private static void LeftTop(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    private static float EaseOut(float x) { return 1f - Mathf.Pow(1f - x, 3f); }
    private static float EaseIn(float x) { return x * x * x; }
    private static float EaseInOut(float x) { return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f; }
}