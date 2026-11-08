using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.InputSystem;
using TMPro;

public enum GameState { Loading, Playing, Paused, Rewinding, Finished } 

class RawEvent {
    public string Type = ""; public long Measure = 1; public long Num, Den; public string Value = "";
}

// 채보에서 SHOW 표시된 변속 (게임에서 SPEED UP / DOWN 글자를 띄움)
public class SpeedNotice { public double Time; public int Layer; } // Layer 0 = 두 층 모두

class GameScrollEvent { 
    public double Time; 
    public double Multiplier; 
}

public class MeasureLineObj {
    public GameObject go;
    public double hitTime;
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("상태 및 시간")]
    public GameState currentState = GameState.Loading; 
    public double currentPlayTime = -3.0; 
    public double gameStartTime = 0;

    [Header("시작 대기 시간")]
    [Tooltip("게임 시작 후 곡이 나오기 전 기본 대기(초). 곡 오프셋이 이보다 길면 오프셋 시각부터 바로 시작한다.")]
    public double maxOffsetWaitTime = 3.0;
    private double playStartTime = -3.0; // 실제 시작 시각 (min(-maxOffsetWaitTime, 오디오 시작 시각))
    
    private double dspStartTime = 0; 
    private bool isFullMusicStarted = false;   // 음악이 실제로 소리 나기 시작했는가 (차트 시각 >= TriggerTime)
    private bool musicScheduled = false;       // 음악 재생을 dspTime 기준으로 예약했는가

    // 음악 재생 예약 여유 시간 (DSP 버퍼 크기에 맞춰 Start 에서 계산)
    //  [버그 수정] 예전에는 bgmSource.Play() 를 부른 그 순간을 음악 시작으로 봤는데, 실제 소리는 다음 오디오 버퍼부터 나서
    //   판마다 0 ~ 수십 ms 씩 무작위로 늦게 시작했다 → '시작부터 오프셋이 틀어지는' 느낌의 주원인.
    //   이제는 PlayScheduled(정확한 dspTime) 로 예약하므로 시작 시각이 샘플 단위로 정확하다.
    private double minScheduleLead = 0.12;
    private double musicScheduleLead = 0.22;
    private double dspBufferSec = 0.02;

    [Header("결과창 전환")]
    [Tooltip("MV · 채보 · 노래가 모두 끝난 뒤 암전되기까지 기다리는 시간(초)")]
    public float resultDelaySeconds = 3.0f;
    [Tooltip("결과창으로 넘어갈 때 암막이 화면을 완전히 덮기까지의 시간(초). 부드럽게 시작해서 부드럽게 끝남")]
    public float resultBlackoutTime = 1.4f; // (이름 변경: 예전 이름의 값이 씬에 저장돼 있어도 새 기본값이 적용되도록)
    [Tooltip("결과창이 열린 뒤 암막이 걷히는 시간(초)")]
    public float resultRevealTime = 0.8f;
    [Tooltip("결과창으로 넘어가기 직전 화면을 어둡게 덮는 암막 (CanvasGroup). 비워 두면 blackOverlayGroup 을 대신 사용")]
    public CanvasGroup resultTransitionOverlay;
    [Tooltip("true: MISS 도 전체 판정 오차 평균의 분모(전체 노트 수)에 포함 / false: 타이밍이 있는 판정만 평균")]
    public bool missCountsInErrorAverage = true;
    public const int MaxScore = 1000000;

    [Header("FULL COMBO / ALL PERFECT 연출")]
    public bool showClearEffect = true;
    [Tooltip("오토플레이에서도 연출을 보여 줄지 (테스트용)")]
    public bool showClearEffectInAutoPlay = true;
    [Tooltip("연출 글자가 화면에 머무는 시간(초)")]
    public float clearEffectHoldSeconds = 2.2f;
    private bool clearChecked = false;
    private SimpleGameSkin simpleSkin;  // 설정 > GAME UI 가 SIMPLE 일 때만 생성

    [Header("Information UI / SIMPLE 스킨 폰트 (비워 두면 기본 폰트)")]
    [Tooltip("title·info·number·label 은 Information UI(모든 GAME UI 공통)에도 쓰임")]
    public SimpleSkinFonts simpleSkinFonts = new SimpleSkinFonts();
    private GameInfoHud infoHud;
    private RedHitEffects redFx;

    [Header("Information UI 가 켜져 있을 때 숨길 기본 화면 오브젝트")]
    [Tooltip("기본(DEFAULT) 화면에 원래 있던 곡명 · 점수 · 시간 표시처럼 Information UI 와 겹치는 것들")]
    public GameObject[] hideWhenInfoUi;

    [Header("SIMPLE 스킨 MV 레인 블러 (Resources/Shaders/LaneBlur*.shader 필요)")]
    [Tooltip("MV 가 나올 때 레인 안쪽에 비치는 영상을 흐리게 처리")]
    public bool simpleLaneBlur = true;
    [Tooltip("흐림 정도 (클수록 더 흐림)")]
    [Range(0.5f, 4f)] public float simpleLaneBlurStrength = 1.6f;

    // ----- 점수: 판정 점수 850,000 + 콤보 점수 150,000 -----
    public const int JudgeScoreMax = 900000;   // 판정 점수 90%
    public const int ComboScoreMax = 100000;   // 콤보 점수 10% (예전 15%)
    private long comboSum = 0; // Σ c(k), c(k) = (k번째 판정 직후 콤보) - 1, 콤보가 0이면 0

    [Header("곡 종료 시 UI 사라짐 (모든 스킨)")]
    [Tooltip("마지막 노트가 처리된 뒤 레인·노트·게임 UI 가 사라지는 시간(초). 배경은 그대로")]
    public float endUiFadeDuration = 0.75f;
    [Tooltip("자동으로 찾지 못한 게임 화면 오브젝트가 있으면 추가 (CanvasGroup 또는 SpriteRenderer 로 사라짐)")]
    public GameObject[] fadeOutAtEnd;
    [Tooltip("사라지면 안 되는 배경용 오브젝트 (bgRenderer 는 자동 제외)")]
    public GameObject[] keepAtEnd;
    private float endUiAlpha = 1f;
    private bool endFadeCollected = false;
    private readonly Dictionary<SpriteRenderer, Color> endFadeSprites = new Dictionary<SpriteRenderer, Color>();
    private readonly Dictionary<CanvasGroup, float> endFadeGroups = new Dictionary<CanvasGroup, float>();
    public float EndUiAlpha { get { return endUiAlpha; } }

    [Header("LIFE / FAILED (SIMPLE 스킨 전용)")]
    [Tooltip("라이프 0 → 채보가 멈추기까지 걸리는 시간(초)")]
    public float failSlowDownSeconds = 1.8f;
    [Tooltip("FAILED 글자가 머무는 시간(초)")]
    public float failEffectHoldSeconds = 1.4f;
    private double life = 100.0;

    // ----- 결과창 그래프용 기록 (판정마다 한 칸) -----
    private readonly List<float> recProgress = new List<float>();  // 곡 진행도 0 ~ 1
    private readonly List<byte> recJudge = new List<byte>();       // 0 PERFECT(EXCELLENT) 1 GREAT 2 GOOD 3 BAD 4 MISS
    private readonly List<float> recLife = new List<float>();      // 그 판정 직후 LIFE
    private readonly List<float> recErrors = new List<float>();    // 타이밍 오차 ms (FAST -, SLOW +), MISS·틱 제외
    private int fastCount, slowCount;                             // PERFECT 를 뺀 판정 중 빠름 / 느림
    private float failProgress = -1f;
    private bool isFailed = false;
    private float failSpeed = 1f;
    public bool LaneIntroStarted { get; private set; }   // 레인이 나타나기 시작했는가 (SIMPLE 레인 펼침)
    public List<NoteData> RedNoteStarts { get; private set; } = new List<NoteData>(); // 빨간 노트(단노트·롱노트 머리) 시각순

    // 해당 층의 그 시각 스크롤 배속 (SIMPLE: SPEED UP / DOWN 표시용)
    public double GetSpeedMultiplier(int layer, double time)
    {
        var list = speedChangesByLayer[Mathf.Clamp(layer, 1, 2)];
        double m = list.Count > 0 ? list[0].Multiplier : 1.0;
        for (int i = 0; i < list.Count; i++) { if (list[i].Time <= time + 1e-6) m = list[i].Multiplier; else break; }
        return m;
    }

    public bool LifeEnabled { get { return simpleSkin != null || SimpleGameSkin.Active != null; } }
    public bool IsFailed { get { return isFailed; } }
    // 게이지 표시용: 소수점 두 자리까지만 반영
    public double LifeGauge { get { return Math.Round(life, 2); } }
    private bool clearEffectPlaying = false;

    [Header("Pause UI 연출")]
    [Tooltip("Pause UI 가 투명 → 불투명으로 나타나는 시간(초). ESC 를 누르는 즉시 시작한다")]
    public float pauseUIFadeInDuration = 0.08f;

    // 한 판 통계 (결과창용)
    private int cntExcellent, cntGreat, cntGood, cntBad, cntMiss;
    private double errorSumMs = 0;      // FAST = 음수, SLOW = 양수 (표시되는 정수 ms 기준)
    private int timedCount = 0;         // 타이밍이 기록된 판정 수 (롱노트 중간 틱 제외)
    private int chartJudgeCount = 0;    // 채보 전체의 판정 대상 수 = 노트 + 롱노트 중간 틱 (점수 / FC / AP 기준)
    private int chartNoteCount = 0;     // 그중 노트(머리·꼬리·일반·프리키) 수
    private int chartTickCount = 0;     // 그중 롱노트 중간 틱 수
    private double chartLastTime = 0;   // 마지막 노트 시각
    private double chartJudgeEndTime = 0; // 판정이 있는 마지막 노트(롱노트 꼬리 포함) 시각 → 게임 종료 기준
    private int chartEndCursor = 0;

    // 종료 조건 / 전환 상태
    private bool mvActive = false;
    private bool mvFinished = true;
    private bool mvEventsHooked = false;
    private float finishHoldTimer = 0f;
    private bool finishRoutineStarted = false;
    private bool isLeaving = false;     // 씬을 떠나는 중이면 Update 정지

    private CanvasGroup pauseCanvasGroup;
    private Coroutine pauseShowRoutine;
    public double currentScrollDist = 0.0; // 2층(Top) 기준 스크롤 거리 (프리키/마디선 등 기존 호환용)
    private double[] currentScrollDistByLayer = new double[3]; // 인덱스: 1 = 1층, 2 = 2층

    [Header("컴포넌트 연결")]
    public AudioSource bgmSource;      
    public AudioSource sfxSource;      
    public VideoPlayer mvPlayer;       
    public GameObject notePrefab;      

    [Header("배경 연결")]
    public SpriteRenderer bgRenderer;    

    [Header("로딩 화면 UI")]
    public CanvasGroup loadingCanvasGroup; 
    public UnityEngine.UI.Image selectScreenImg;          
    public UnityEngine.UI.Image darkOverlay; 
    public UnityEngine.UI.Image loadImgLarge; 
    public CanvasGroup titleGroup; 
    public TextMeshProUGUI loadingTitleText;
    public TextMeshProUGUI loadingDiffText;
    public CanvasGroup detailGroup; 
    public TextMeshProUGUI loadingComposerValueText; 
    public TextMeshProUGUI loadingIllustValueText;
    public TextMeshProUGUI loadingVocalValueText;

    [Header("게임 UI 및 레인 (Basic UI)")]
    public GameObject basicUIContainer;
    public CanvasGroup laneGroup; 
    public UnityEngine.UI.Image comboTitleImage; 
    public TextMeshProUGUI accuracyText;
    public UnityEngine.UI.Image judgmentImage;
    [Range(0.1f, 2.0f)] public float judgmentImageScale = 0.3f; 
    
    [Header("일시정지 화면 (코드로 만듦)")]
    [Tooltip("일시정지 화면 일반 글자 글꼴. 비우면 설정창 글꼴")]
    public TMP_FontAsset pauseFont;
    [Tooltip("일시정지 화면 강조 글자(PAUSED · 메뉴 · 곡 제목) 글꼴. 비우면 설정창 강조 글꼴 → 일반 글꼴을 굵게")]
    public TMP_FontAsset pauseBoldFont;
    private PauseMenu pauseMenu;
    private HitErrorMeter hitErrorMeter;      // 화면 아래 판정 오차 그래프 (osu! 처럼)

    [Header("Pause UI 연결 (예전 UI — 지금은 코드로 만든 일시정지 화면을 씀)")]
    public GameObject pauseUIContainer;
    public UnityEngine.UI.Button btnResume;
    public UnityEngine.UI.Button btnRetry;
    public UnityEngine.UI.Button btnBackToMenu;
    public CanvasGroup blackOverlayGroup; 

    [Header("FAST / SLOW UI")]
    public TextMeshProUGUI fastSlowDirectionText; 
    public TextMeshProUGUI fastSlowValueText;    
    private float fastSlowAnimTimer = 0f;

    [Header("콤보 이미지 UI")] 
    public RectTransform comboImageContainer;    
    public float comboDigitWidth = 65f;          
    public float comboDigitHeight = 90f;         
    public float comboDigitSpacing = 2f;         

    private Dictionary<char, Sprite> comboSpriteDict = new Dictionary<char, Sprite>();
    private List<UnityEngine.UI.Image> basicComboDigits = new List<UnityEngine.UI.Image>();

    [Header("판정 스프라이트 연결")]
    public Sprite sprExcellent; public Sprite sprGreat; public Sprite sprGood; public Sprite sprBad; public Sprite sprMiss;

    [Header("히트 이펙트")] 
    public GameObject blueHitEffectPrefab;
    public GameObject yellowHitEffectPrefab;
    public float hitEffectDuration = 0.3f; 
    public float hitEffectOffsetY = 0.0f; 

    private Queue<GameObject> blueEffectPool = new Queue<GameObject>();
    private Queue<GameObject> yellowEffectPool = new Queue<GameObject>();
    private GameObject[] holdEffects = new GameObject[4];


    [Header("게임 설정")]
    public float[] laneXPositions = { -1.2f, -0.4f, 0.4f, 1.2f }; 
    public float hitLineY = -2.85f; 
    public const double baseScrollSpeed = 600.0;

    [Header("키빔 & 라이트")]
    public SpriteRenderer[] beamRenderers; 
    public GameObject[] whiteLights;       
    [Tooltip("설정 > GAME UI 가 SIMPLE 일 때 꺼 둘 기본 화면 오브젝트 (레인 배경 스프라이트, 장식 등). 레인 그룹·키빔·판정/콤보 UI 는 자동으로 숨겨짐")]
    public GameObject[] hideInSimpleSkin;
    public Sprite[] offSprites;            
    public Sprite[] onSprites;             

    private int totalNotes = 0, processedNotes = 0, currentCombo = 0, maxCombo = 0;
    private double currentAccSum = 0;       // 점수용 판정 가중치 합 (롱노트 틱 포함)
    // ===== TP (Technical Point) — 예전 '정확도' =====
    //  JUST (PERFECT 중 ±20ms 안)       = 100%
    //  PERFECT (15 ~ 65ms)                      = 99% → 88% 로 점점 낮아짐
    //  GREAT 50%,  GOOD 20%,  BAD 0%,  MISS 0%
    //  롱노트 중간 판정(틱)은 항상 JUST (100%),  끝까지 누른 롱노트 끝도 JUST
    //  TP 100.00% (모든 판정이 JUST) = ALL JUST
    //  표시는 버림 (99.999% 를 100.00% 로 올려 보이지 않게)
    public static double JustWindowMs = 20.0;   // (모드로 판정 범위가 좁아지면 PERFECT 범위를 넘지 않게 줄어듦)
    public const double PerfectTopValue = 0.99, PerfectEdgeValue = 0.88;
    private double accPoints = 0;
    private int accNotes = 0;
    private int cntJust = 0;
    public double StrictAccuracy { get { return accNotes == 0 ? 100.0 : Math.Floor(accPoints / accNotes * 10000.0) / 100.0; } }
    public double TechnicalPoint { get { return StrictAccuracy; } }
    public int JustCount { get { return cntJust; } }

    public static bool IsJust(string judge, double signedErrorMs, bool hasTiming)
    {
        return judge == "EXCELLENT" && (!hasTiming || Math.Abs(signedErrorMs) <= JustWindowMs);
    }

    private static double AccValue(string judge, double signedErrorMs, bool hasTiming)
    {
        switch (judge)
        {
            case "EXCELLENT":
                if (IsJust(judge, signedErrorMs, hasTiming)) return 1.0;
                double edge = W_EXCELLENT * 1000.0;
                double k = Math.Max(0.0, Math.Min(1.0, (Math.Abs(signedErrorMs) - JustWindowMs) / (edge - JustWindowMs)));
                return PerfectTopValue - (PerfectTopValue - PerfectEdgeValue) * k;
            case "GREAT": return 0.5;
            case "GOOD": return 0.2;
            default: return 0.0;
        }
    }

    // ALL JUST: 풀콤보 + 모든 판정이 JUST
    public bool IsAllJust { get { return !isFailed && chartJudgeCount > 0 && maxCombo >= chartJudgeCount && cntJust >= chartJudgeCount; } }

    private NoteData[] activeLongNote = new NoteData[8]; // 슬롯: 0~3 = 2층(Top), 4~7 = 1층(Bottom) 
    private double[] nextLongNoteTickTime = new double[8]; 
    private double[] lnReleaseTime = new double[8]; 

    private List<NoteData> notes = new List<NoteData>();
    // 층별 변속 (인덱스 1 = 1층, 2 = 2층). BPM 변화는 층과 무관하게 bpmChanges 하나를 공유한다.
    private List<SpeedChange>[] speedChangesByLayer = new List<SpeedChange>[] { null, new List<SpeedChange>(), new List<SpeedChange>() };
    private List<BpmChange> bpmChanges = new List<BpmChange>();
    private List<UIEventData> uiEvents = new List<UIEventData>();
    private List<GameScrollEvent>[] scrollEventsByLayer = new List<GameScrollEvent>[] { null, new List<GameScrollEvent>(), new List<GameScrollEvent>() };


    // 차트 시계에서 음악이 재생되기 시작하는 시각(초) — 채보의 MUSICOFFSET(또는 예전 OFFSET)에서 읽음
    public double songStartOffset = 0;
    public double chartAudioOffset = 0.0;

    // 설정의 AUDIO OFFSET 까지 반영한 '음악 시작 시각' (모든 싱크 계산은 이 값 하나만 쓴다)
    public double TriggerTime { get { return songStartOffset - SettingsStore.MusicLead; } }
    // 판정 기준 시각 (설정의 JUDGE OFFSET 반영). NoteObject 의 자동 MISS 도 이 시각으로 판단해야 판정 범위와 일치한다
    public double JudgeTime { get { return currentPlayTime - SettingsStore.JudgeDelay; } }
    // 판정 범위 (초). 게임 모드 HARD 1/2 · MAXIMUM 1/3 · ULTIMATE 1/4 로 줄어듦 → ApplyJudgeWindows (판 시작 때마다 다시 계산)
    public const double BaseExcellent = 0.065, BaseGreat = 0.085, BaseGood = 0.100, BaseBad = 0.120;
    public static double W_EXCELLENT = BaseExcellent, W_GREAT = BaseGreat, W_GOOD = BaseGood, W_BAD = BaseBad;

    private static void ApplyJudgeWindows(double extra = 1.0)
    {
        double k = GameModes.Play.JudgeScale * extra;     // extra: CLUTCH (위험할수록 넓어짐)
        W_EXCELLENT = BaseExcellent * k; W_GREAT = BaseGreat * k; W_GOOD = BaseGood * k; W_BAD = BaseBad * k;
        // JUST 는 ±20ms (HARD · MAX JUDGEMENT 에서도 그대로, ULTIMATE 만 35% 감소 → ±13ms). 단, 좁아진 PERFECT 범위보다는 작게
        JustWindowMs = Math.Min(GameModes.JustWindowBaseMs * GameModes.Play.JustScale, W_EXCELLENT * 1000.0 - 1.0);
    }
    
    private Key[] laneKeys = { Key.D, Key.F, Key.J, Key.K };
    private bool[] prevKeyState = new bool[4];
    
    private SongData currentSong;
    private Camera mainCam;
    private float deltaTime = 0.0f; 

    private AudioClip hitSoundClip; 
    private Dictionary<NoteData, NoteObject> noteObjectDict = new Dictionary<NoteData, NoteObject>(); 
    private List<NoteData>[] notesByLane = new List<NoteData>[4]; 
    private int[] laneStartIndex = new int[4]; 
    private float judgmentAnimTimer = 0f; 
    private float comboAnimTimer = 0f;

    private Queue<NoteObject> notePool = new Queue<NoteObject>();
    // 층마다 스크롤 속도가 다르므로 생성(스폰) 순서도 층별로 관리한다.
    private List<NoteData>[] notesByLayer = new List<NoteData>[] { null, new List<NoteData>(), new List<NoteData>() };
    private int[] layerSpawnIndex = new int[3];
    private const double SPAWN_LOOKAHEAD = 3.5; 

    private Queue<GameObject> measureLinePool = new Queue<GameObject>();
    private List<MeasureLineObj> activeMeasureLines = new List<MeasureLineObj>();
    private List<double> allMeasureTimes = new List<double>();
    private int measureSpawnIndex = 0;

    private Vector3 initialComboScale = Vector3.one;
    private Vector3 initialComboTitleScale = Vector3.one;
    private Vector2 initialComboPos;
    private Vector2 initialComboTitlePos;

    void Awake() { 
        Instance = this; 
        SettingsStore.ApplyFrameRate(); // 설정 > DISPLAY 의 프레임 제한 / VSync

        for (int i = 0; i < 4; i++) {
            notesByLane[i] = new List<NoteData>();
            laneStartIndex[i] = 0;
        }

        TMP_FontAsset moveSans = Resources.Load<TMP_FontAsset>("Fonts/MoveSans-Light");

        if (accuracyText != null) {
            accuracyText.alignment = TextAlignmentOptions.Center;
            if (moveSans != null) accuracyText.font = moveSans;
        }
        if (fastSlowDirectionText != null) {
            fastSlowDirectionText.alignment = TextAlignmentOptions.Center;
            if (moveSans != null) fastSlowDirectionText.font = moveSans;
            fastSlowDirectionText.gameObject.SetActive(false);
        }
        if (fastSlowValueText != null) {
            fastSlowValueText.alignment = TextAlignmentOptions.Center;
            if (moveSans != null) fastSlowValueText.font = moveSans;
            fastSlowValueText.gameObject.SetActive(false);
        }
        if (loadingTitleText != null) {
            loadingTitleText.alignment = TextAlignmentOptions.Center;
        }

        Sprite[] comboSprites = Resources.LoadAll<Sprite>("Sprites/NoteAsset/COMBONumbers");
        foreach (var s in comboSprites) {
            foreach (char c in s.name) {
                if (char.IsDigit(c)) { comboSpriteDict[c] = s; break; }
            }
        }

        if (comboImageContainer != null) {
            initialComboScale = comboImageContainer.localScale;
            initialComboPos = comboImageContainer.anchoredPosition;
        }
        if (comboTitleImage != null) {
            initialComboTitleScale = comboTitleImage.rectTransform.localScale;
            initialComboTitlePos = comboTitleImage.rectTransform.anchoredPosition;
        }

        if (pauseUIContainer != null) {
            pauseCanvasGroup = pauseUIContainer.GetComponent<CanvasGroup>();
            if (pauseCanvasGroup == null) pauseCanvasGroup = pauseUIContainer.AddComponent<CanvasGroup>();
            pauseCanvasGroup.alpha = 0f;
            pauseUIContainer.SetActive(false);
        }
        if (blackOverlayGroup != null) {
            blackOverlayGroup.alpha = 0f;
            blackOverlayGroup.gameObject.SetActive(false);
        }
        
        if(btnResume != null) btnResume.onClick.AddListener(ResumeGame);

        if (resultTransitionOverlay != null) {
            resultTransitionOverlay.alpha = 0f;
            resultTransitionOverlay.blocksRaycasts = false;
            resultTransitionOverlay.gameObject.SetActive(false);
        }
        if(btnRetry != null) btnRetry.onClick.AddListener(RetryGame);
        if(btnBackToMenu != null) btnBackToMenu.onClick.AddListener(BackToMenu);
    }

    void Start() 
    { 
        mainCam = FindSceneCamera();
        // GAME SETTING (5_Test 씬): 실제 채보로 맞추는 연습 공간 — 무한 반복 · FAILED/결과창 없음 · 조정 패널
        IsTestMode = GameSettingSession.Active;
        // 게임 모드: 이번 판에 쓸 모드를 확정 (GAME SETTING 은 모드 없음) → 판정 범위 계산
        GameModes.BeginPlay(IsTestMode);
        ApplyJudgeWindows();
        if (GameModes.Play.Modes.Length > 0) Debug.Log("[Mode] " + string.Join(", ", GameModes.Play.Modes.Select(m => GameModes.Info(m).Name)) + "  (점수 " + GameModes.Percent(GameModes.Play.ScoreMultiplier) + ")");
        currentSong = CoreManager.Instance.CurrentSongData;
        if (SongIntroOverlay.PreferredTitleFont == null && loadingTitleText != null) SongIntroOverlay.PreferredTitleFont = loadingTitleText.font;
        hitSoundClip = Resources.Load<AudioClip>("Audio/etc/hitsound"); 
        // [버그 수정] 탭음 데이터를 미리 올려 둠 (첫 PlayOneShot 때 아직 안 올라와 있으면 첫 소리가 나지 않음)
        if (hitSoundClip != null && hitSoundClip.loadState == AudioDataLoadState.Unloaded) hitSoundClip.LoadAudioData();
        laneKeys = SettingsStore.GetLaneKeys();                        // 설정 > CONTROLS
        InputTiming.Watch(laneKeys);                                     // 키를 실제로 누른 시각 기록 (프레임 단위 오차 제거)
        InputTiming.Watch(new[] { Key.Space });
        SetupAudioTiming();
        CreatePauseMenu();
        if (SettingsStore.Current.ShowHitErrorBar)
        {
            hitErrorMeter = HitErrorMeter.Create(this, W_EXCELLENT, W_GREAT, W_GOOD, W_BAD);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(hitErrorMeter.gameObject, gameObject.scene);
        }
        int skinId = SettingsStore.Current.GameSkin;                     // 설정 > GAME UI
        if (GameSkins.UsesSimpleSkin(skinId)) simpleSkin = SimpleGameSkin.Create(this, skinId == GameSkins.Diverse);
        if (GameModes.Play.Reverse) ApplyReverseView();
        redFx = RedHitEffects.Create(this);  // RED 노트 전용 탭 / 롱노트 홀드 이펙트 (일반 이펙트 아래)
        infoHud = GameInfoHud.Create(this); // 곡 정보 · 난이도 · 시간 · 점수 · 배속 · 진행 막대 (모든 GAME UI 공통, 설정 > INFORMATION UI)
        if (IsTestMode)
        {
            GameSettingPanel panel = GameSettingPanel.Create(this);   // 왼쪽 · 오른쪽 조정 패널 (마우스 조작)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(panel.gameObject, gameObject.scene);
        }
        if (bgmSource != null) bgmSource.volume = SettingsStore.MusicVolume; // 설정 > SOUND

        StartCoroutine(StartGameSetupRoutine());
    }

    private IEnumerator StartGameSetupRoutine() 
    {
        int songId = currentSong.Id;
        int levelId = CoreManager.Instance.CurrentLevelId; // 추가됨

        currentState = GameState.Loading;

        loadingTitleText.text = currentSong.Name + currentSong.SubName;
        loadingComposerValueText.text = currentSong.Composer;
        loadingIllustValueText.text = currentSong.illustration;
        loadingVocalValueText.text = currentSong.Vocal;

        // 중앙정렬된 난이도 텍스트 렌더링
        string diffName = "HARD";
        string diffVal = currentSong.Difficulty_HARD;
        Color diffColor = Color.white;

        if (levelId == 1) {
            diffName = "EASY"; diffVal = currentSong.Difficulty_EASY;
            ColorUtility.TryParseHtmlString("#5ce1e6", out diffColor);
        } else if (levelId == 2) {
            diffName = "HARD"; diffVal = currentSong.Difficulty_HARD;
            ColorUtility.TryParseHtmlString("#ff751f", out diffColor);
        } else if (levelId == 3) {
            diffName = "INSANE"; diffVal = currentSong.Difficulty_INSANE;
            ColorUtility.TryParseHtmlString("#ff3131", out diffColor);
        } else if (levelId == DifficultyHelper.Special) {
            diffName = "SPECIAL"; diffVal = currentSong.Difficulty_SPECIAL;
            diffColor = DifficultyHelper.GetColor(DifficultyHelper.Special);
        }

        if (loadingDiffText != null) {
            // 난이도 이름은 난이도 색 (SPECIAL 은 솜사탕 그라데이션), 숫자는 같은 색
            loadingDiffText.richText = true;
            loadingDiffText.text = DifficultyHelper.ColoredText(levelId, diffName) + " " + DifficultyHelper.ColoredText(levelId, diffVal ?? "");
            loadingDiffText.color = Color.white;
            loadingDiffText.alignment = TextAlignmentOptions.Center; 
        }

        if(laneGroup != null) laneGroup.alpha = 0f;
        bool introScreen = SongIntroOverlay.IsActive; // 곡 정보 화면이 따로 떠 있으면 게임 씬의 로딩 화면은 쓰지 않음
        loadingCanvasGroup.alpha = introScreen ? 0f : 1f;
        loadingCanvasGroup.gameObject.SetActive(!introScreen);
        if (selectScreenImg != null) selectScreenImg.gameObject.SetActive(true);
        if (darkOverlay != null) darkOverlay.gameObject.SetActive(true);

        if (loadImgLarge != null) loadImgLarge.color = new Color(1, 1, 1, 0); 
        if (titleGroup != null) titleGroup.alpha = 0f;
        if (detailGroup != null) detailGroup.alpha = 0f;

        yield return null;

        // 음원: 난이도 전용 음원(Audio/Full/{곡ID}_{난이도}, 예: 8_3)이 있으면 그것, 없으면 기존 음원(Audio/Full/{곡ID})
        ResourceRequest audioReq = Resources.LoadAsync<AudioClip>($"Audio/Full/{songId}_{levelId}");
        // 그림: SPECIAL 이면 {곡ID}_4 (없으면 기본 그림)
        ResourceRequest bgReq = Resources.LoadAsync<Sprite>(SongArt.Path("Sprites/blurimg", songId, levelId));
        ResourceRequest jacketReq = Resources.LoadAsync<Sprite>(SongArt.Path("Sprites/loadimg-large", songId, levelId));

        LoadChart(songId, levelId); // 레벨 ID를 추가해 로딩
        totalNotes = chartJudgeCount;
        int jsonCount = DifficultyHelper.GetNoteCount(currentSong, levelId);
        Debug.Log($"[NoteCount] {currentSong.Id} {DifficultyHelper.GetName(levelId)} = {chartJudgeCount} (notes {chartNoteCount} + long ticks {chartTickCount})" +
                  (jsonCount != chartJudgeCount ? $"  ※ songs.json 값 {jsonCount} 과 다름 (에디터에서 이 채보를 다시 저장하면 갱신됨)" : ""));
        yield return null;

        CreateNoteClipMask();

        notePool.Clear();
        noteObjectDict.Clear();
        layerSpawnIndex[1] = 0; layerSpawnIndex[2] = 0;
        for (int i = 0; i < 200; i++) {
            GameObject obj = InstantiateInMyScene(notePrefab);
            obj.SetActive(false); 
            notePool.Enqueue(obj.GetComponent<NoteObject>());
        }
        yield return null;

        measureLinePool.Clear();
        activeMeasureLines.Clear();
        // [버그 수정] 마디선 50개가 각자 텍스처를 만들어서 SIMPLE-DIVERSE 재질이 50개나 생겼다
        //  → 재질 한도를 넘겨 그 뒤의 노트들이 공용 재질(레인과 같이 접힘)을 받아, 레인을 접으면 영역 노트도 사라졌다.
        //  지금: 마디선은 텍스처 · 스프라이트 하나를 같이 씀
        Texture2D mlTex = new Texture2D(1, 1); mlTex.SetPixel(0, 0, new Color(128, 128, 128, 0.2f)); mlTex.Apply();
        Sprite mlSprite = Sprite.Create(mlTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        for (int i = 0; i < 50; i++) {
            GameObject ml = new GameObject("MeasureLine");
            SpriteRenderer sr = ml.AddComponent<SpriteRenderer>();
            sr.sprite = mlSprite;
            sr.sortingOrder = 1; 
            sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask; // 판정선 아래로는 안 그려짐
            ml.transform.SetParent(this.transform);
            ml.transform.localScale = new Vector3(3.2f, 0.015f, 1f); 
            ml.SetActive(false);
            measureLinePool.Enqueue(ml);
            if (simpleSkin != null) simpleSkin.AdoptToPlayfield(ml); // SIMPLE-DIVERSE: 원근 재질 (판정선 아래 자름)
        }
        yield return null;

        blueEffectPool.Clear();
        yellowEffectPool.Clear();
        if (blueHitEffectPrefab != null && yellowHitEffectPrefab != null) {
            for (int i = 0; i < 20; i++) {
                GameObject b = Instantiate(blueHitEffectPrefab, transform); b.SetActive(false); blueEffectPool.Enqueue(b);
                GameObject y = Instantiate(yellowHitEffectPrefab, transform); y.SetActive(false); yellowEffectPool.Enqueue(y);
            }
        }
        yield return null;

        for (int i = 0; i < 4; i++) {
            if (holdEffects[i] != null) Destroy(holdEffects[i]);
            bool isBlue = (i == 0 || i == 3);
            GameObject prefab = isBlue ? blueHitEffectPrefab : yellowHitEffectPrefab;
            if (prefab != null) {
                holdEffects[i] = Instantiate(prefab, transform);
                holdEffects[i].transform.position = new Vector3(laneXPositions[i], hitLineY + hitEffectOffsetY, -4.9f);
                ParticleSystemRenderer[] renderers = holdEffects[i].GetComponentsInChildren<ParticleSystemRenderer>(true);
                foreach (var r in renderers) { r.sortingLayerName = "Default"; r.sortingOrder = 99; }
                ParticleSystem[] pss = holdEffects[i].GetComponentsInChildren<ParticleSystem>(true);
                foreach(var ps in pss) {
                    var main = ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpeed = 4.0f; 
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                holdEffects[i].SetActive(true); 
            }
        }
        yield return null;

        while (!audioReq.isDone) yield return null;
        if (audioReq.asset == null) audioReq = Resources.LoadAsync<AudioClip>($"Audio/Full/{songId}");   // 난이도 전용 음원이 없음 → 기존 음원
        else Debug.Log($"[Audio] 난이도 전용 음원 사용: Audio/Full/{songId}_{levelId}");
        while (!audioReq.isDone || !bgReq.isDone || !jacketReq.isDone) {
            yield return null;
        }

        Sprite bgSprite = BackgroundBlur.Get(bgReq.asset as Sprite);   // 원본 배경 → 흐리게 (BackgroundBlur)
        if (bgRenderer != null) {
            bgRenderer.enabled = true;
            if (bgSprite != null) {
                bgRenderer.sprite = bgSprite;
                bgRenderer.color = CoreManager.Instance.IsMvOn ? Color.black : Color.white;
                
                float cameraHeight = mainCam.orthographic ? mainCam.orthographicSize * 2f : 10f; 
                float cameraWidth = cameraHeight * mainCam.aspect;
                float spriteWidth = bgSprite.bounds.size.x;
                float spriteHeight = bgSprite.bounds.size.y;
                float scaleX = cameraWidth / spriteWidth;
                float scaleY = cameraHeight / spriteHeight;
                float finalScale = Mathf.Max(scaleX, scaleY);
                
                bgRenderer.transform.localScale = new Vector3(finalScale, finalScale, 1f);
                bgRenderer.transform.position = new Vector3(mainCam.transform.position.x, mainCam.transform.position.y, 10f);
            } else bgRenderer.color = Color.black;
        }

        if (selectScreenImg != null && bgSprite != null) selectScreenImg.sprite = bgSprite;
        if (loadImgLarge != null && jacketReq.asset != null) loadImgLarge.sprite = jacketReq.asset as Sprite;

        // MV: SPECIAL 이면 전용 영상 MV/{곡ID}_4.avi · .mp4 를 먼저, 없으면 기본 영상
        string finalMvPath = "";
        var mvNames = new List<string>();
        if (levelId == DifficultyHelper.Special) mvNames.Add($"{songId}_{DifficultyHelper.Special}");
        mvNames.Add($"{songId}");
        foreach (string mvName in mvNames)
        {
            string mvPathAvi = Path.Combine(Application.streamingAssetsPath, "MV", mvName + ".avi");
            string mvPathMp4 = Path.Combine(Application.streamingAssetsPath, "MV", mvName + ".mp4");
            if (File.Exists(mvPathAvi)) { finalMvPath = mvPathAvi; break; }
            if (File.Exists(mvPathMp4)) { finalMvPath = mvPathMp4; break; }
        }

        if (mvPlayer != null && !string.IsNullOrEmpty(finalMvPath) && CoreManager.Instance.IsMvOn) {
            mvPlayer.url = finalMvPath;
            mvPlayer.renderMode = VideoRenderMode.MaterialOverride;
            mvPlayer.targetMaterialRenderer = bgRenderer;
            mvPlayer.isLooping = false;
            mvPlayer.skipOnDrop = true;       // 디코딩이 밀리면 프레임을 건너뛰어 음악을 따라감
            mvPlayer.playbackSpeed = 1f;
            mvStallTimer = 0f; mvLastTime = -1.0; mvSyncTimer = 0f;
            mvActive = true;
            mvFinished = false;
            HookMvEvents();
        } else if (mvPlayer != null) {
            mvPlayer.url = ""; 
        }

        if (bgmSource != null && audioReq.asset != null) bgmSource.clip = audioReq.asset as AudioClip;

        judgmentImage.gameObject.SetActive(false);
        if (comboTitleImage != null) comboTitleImage.gameObject.SetActive(false);
        if (comboImageContainer != null) comboImageContainer.gameObject.SetActive(false);
        if (basicUIContainer != null) basicUIContainer.SetActive(true);
        
        StartCoroutine(LoadingSequenceRoutine());
    }

    // =====================================================================
    //  노트 · 마디선이 판정선 아래로 그려지지 않도록: 판정선 위쪽만 덮는 SpriteMask
    //  (노트/마디선 SpriteRenderer 는 maskInteraction = VisibleInsideMask, 프리키 노트는 제외)
    // =====================================================================
    [Header("판정선 아래 가리기")]
    [Tooltip("노트가 사라지는 경계 = 판정선 + 이 값 (유닛). 0 이면 판정선에서 바로 잘림, 음수면 조금 더 아래까지 보임")]
    public float noteClipOffset = 0f;
    private GameObject noteClipMask;

    private void CreateNoteClipMask()
    {
        if (noteClipMask != null) return;
        noteClipMask = new GameObject("NoteClipMask");
        noteClipMask.transform.SetParent(transform, false);
        SpriteMask mask = noteClipMask.AddComponent<SpriteMask>();
        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[16];
        for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px); tex.Apply();
        mask.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0f), 4f); // 1x1 유닛, 피벗 아래 가운데
        float clipY = hitLineY + noteClipOffset;
        float camX = mainCam != null ? mainCam.transform.position.x : 0f;
        noteClipMask.transform.position = new Vector3(camX, clipY, 0f);
        Vector3 ps = transform.lossyScale;
        noteClipMask.transform.localScale = new Vector3(200f / Mathf.Max(0.0001f, Mathf.Abs(ps.x)), 200f / Mathf.Max(0.0001f, Mathf.Abs(ps.y)), 1f); // 판정선 위로 넓게
    }

    public NoteObject GetNoteFromPool()
    {
        while (notePool.Count > 0) {
            NoteObject pooled = notePool.Dequeue();
            if (pooled != null) return pooled; // 파괴된 오브젝트가 섞여 있으면 건너뜀
        }
        GameObject obj = InstantiateInMyScene(notePrefab);
        obj.SetActive(false); 
        return obj.GetComponent<NoteObject>();
    }

    public void ReturnNoteToPool(NoteObject obj)
    {
        if (!obj.gameObject.activeSelf) return; 
        obj.gameObject.SetActive(false); 
        noteObjectDict.Remove(obj.myData); 
        notePool.Enqueue(obj); 
    }

    // 노트를 알 때: SIMPLE 스킨은 노트 색(화살표 · RED) · 종류(롱노트 끝은 작게)에 맞춘 히트 이펙트
    public void SpawnHitEffect(int lane, NoteData n)
    {
        if (lane < 0 || lane > 3) return;
        if (simpleSkin != null) { simpleSkin.Hit(lane, n); return; }
        SpawnHitEffect(lane);
    }

    public void SpawnHitEffect(int lane)
    {
        if (lane < 0 || lane > 3) return;
        if (simpleSkin != null) { simpleSkin.Hit(lane); return; }

        bool isBlue = (lane == 0 || lane == 3);
        if (isBlue && blueHitEffectPrefab == null) return;
        if (!isBlue && yellowHitEffectPrefab == null) return;

        Queue<GameObject> pool = isBlue ? blueEffectPool : yellowEffectPool;
        GameObject effect = null;

        if (pool.Count > 0) effect = pool.Dequeue();
        else effect = Instantiate(isBlue ? blueHitEffectPrefab : yellowHitEffectPrefab, transform);

        effect.transform.position = new Vector3(laneXPositions[lane], hitLineY + hitEffectOffsetY, -5.0f);
        
        ParticleSystemRenderer[] renderers = effect.GetComponentsInChildren<ParticleSystemRenderer>(true);
        foreach (var r in renderers) {
            r.sortingLayerName = "Default"; 
            r.sortingOrder = 100;
        }

        effect.SetActive(true);

        ParticleSystem ps = effect.GetComponent<ParticleSystem>();
        if (ps != null) ps.Play();

        StartCoroutine(ReturnEffectToPool(effect, pool, hitEffectDuration));
    }

    private IEnumerator ReturnEffectToPool(GameObject effect, Queue<GameObject> pool, float delay)
    {
        yield return new WaitForSeconds(delay);
        effect.SetActive(false);
        pool.Enqueue(effect);
    }

    public void StartHoldEffect(int lane)
    {
        if (simpleSkin != null) { simpleSkin.SetHold(lane, true); return; }
        if (holdEffects[lane] == null) return;
        holdEffects[lane].transform.position = new Vector3(laneXPositions[lane], hitLineY + hitEffectOffsetY, -4.9f);
        ParticleSystem[] pss = holdEffects[lane].GetComponentsInChildren<ParticleSystem>();
        foreach(var ps in pss) ps.Play();
    }

    public void StopHoldEffect(int lane)
    {
        if (simpleSkin != null) { simpleSkin.SetHold(lane, false); return; }
        if (holdEffects[lane] == null) return;
        ParticleSystem[] pss = holdEffects[lane].GetComponentsInChildren<ParticleSystem>();
        foreach(var ps in pss) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    // 곡 정보 화면이 분리된 경우(SongIntroOverlay)에는 게임 씬 자체의 로딩 화면을 쓰지 않는다.
    //  - 준비가 끝나면 NotifyGameReady → 곡 정보 화면이 걷히기 시작하면(IsRevealing) 레인을 띄우고 시작
    //  - 게임 씬을 바로 실행한 경우(settings.json Mode = 2 등)에는 예전 로딩 화면 그대로
    private IEnumerator LoadingSequenceRoutine()
    {
        bool useIntro = SongIntroOverlay.IsActive;
        float t = 0;

        if (!useIntro)
        {
            Vector3 titleBasePos = titleGroup.transform.localPosition;
            titleGroup.transform.localPosition = titleBasePos + new Vector3(0, 100f, 0); 

            while (t < 0.5f) {
                t += Time.deltaTime;
                float p = t / 0.5f;
                loadImgLarge.color = new Color(1, 1, 1, p);
                titleGroup.alpha = p;
                titleGroup.transform.localPosition = Vector3.Lerp(titleBasePos + new Vector3(0, 100f, 0), titleBasePos, EaseOut(p));
                yield return null;
            }
            yield return new WaitForSeconds(1.5f); 

            t = 0;
            Vector3 titleCurrentPos = titleGroup.transform.localPosition;
            Vector3 detailStartPos = titleBasePos + new Vector3(-200f, 0, 0); 
            Vector3 detailEndPos = titleBasePos;

            while (t < 0.5f) {
                t += Time.deltaTime;
                float p = t / 0.5f;
                titleGroup.alpha = 1f - p;
                titleGroup.transform.localPosition = Vector3.Lerp(titleCurrentPos, titleCurrentPos + new Vector3(200f, 0, 0), EaseOut(p));
                detailGroup.alpha = p;
                detailGroup.transform.localPosition = Vector3.Lerp(detailStartPos, detailEndPos, EaseOut(p));
                yield return null;
            }
            
            yield return new WaitForSeconds(0.2f); 
        }

        if (mvPlayer != null && CoreManager.Instance.IsMvOn && !string.IsNullOrEmpty(mvPlayer.url)) {
            mvPlayer.Prepare();
            yield return new WaitUntil(() => mvPlayer.isPrepared);
            mvPlayer.Play();
            mvPlayer.Pause(); 
        }
        if (bgmSource != null && bgmSource.clip != null) {
            // 음악 데이터를 미리 메모리에 올려 둔다
            //  [버그 수정] 예전에는 Play → Pause 로만 깨워 두어서, Load In Background / Streaming 설정인 곡은
            //   첫 재생 때 디코딩이 늦어져 소리가 늦게 나고(시작 싱크 어긋남) 이후 보정이 그 어긋남을 '정상'으로 굳혔다.
            AudioClip clip = bgmSource.clip;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            float waited = 0f;
            while (clip.loadState == AudioDataLoadState.Loading && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
            if (clip.loadState == AudioDataLoadState.Failed) Debug.LogWarning($"[Sync] 음악 데이터를 불러오지 못했습니다: {clip.name}");
            if (clip.loadType == AudioClipLoadType.Streaming)
                Debug.LogWarning($"[Sync] {clip.name}: Load Type 이 Streaming 입니다. 재생 위치가 흔들릴 수 있으니 임포트 설정에서 Decompress On Load (또는 Compressed In Memory) 를 권장합니다.");
        }

        if (!useIntro)
        {
            yield return new WaitForSeconds(2.5f); 

            t = 0;
            while (t < 0.3f) {
                t += Time.deltaTime;
                loadingCanvasGroup.alpha = 1f - (t / 0.3f);
                yield return null;
            }
            loadingCanvasGroup.gameObject.SetActive(false);
        }
        else
        {
            // 곡 정보 화면에 준비 완료를 알리고, 걷히기 시작할 때까지 대기
            SongIntroOverlay.NotifyGameReady();
            yield return new WaitUntil(() => !SongIntroOverlay.IsActive || SongIntroOverlay.IsRevealing);
            yield return new WaitForSeconds(0.15f);
        }

        // MODE RANDOM: 레인이 펼쳐지기 전에 모드 3개를 고르는 연출 (REVERSE 등 화면이 바뀌는 모드가 있으므로 먼저 보여 줌)
        //  모드는 이미 정해져 적용된 상태, 연출은 보여 주기만 → 창이 사라지면 레인이 펼쳐지고 곡 시작
        if (GameModes.Play.RandomFresh && !IsTestMode)
        {
            bool rouletteDone = false;
            GameModeRoulette.Show(gameObject.scene, null, () => rouletteDone = true, null, true);
            while (!rouletteDone) yield return null;
            yield return new WaitForSecondsRealtime(0.25f);
        }

        LaneIntroStarted = true; // SIMPLE 스킨: 레인이 좌우로 펼쳐지기 시작
        t = 0;
        while (t < 0.5f) {
            t += Time.deltaTime;
            if (laneGroup != null) laneGroup.alpha = t / 0.5f;
            yield return null;
        }
        if (laneGroup != null) laneGroup.alpha = 1f;

        isFullMusicStarted = false;
        musicScheduled = false;
        // 오디오가 시작되는 시각(triggerTime)이 기본 대기 시간보다 더 이전이면(예: -12.889s),
        // 그 시각부터 게임을 시작해서 곡 앞부분이 잘리지 않게 한다. (재생 예약 여유만큼 조금 더 일찍)
        double audioTriggerAtStart = TriggerTime;
        playStartTime = Math.Min(-maxOffsetWaitTime, audioTriggerAtStart - musicScheduleLead - 0.05);
        // 시작 타임 조정: 그 음악 위치에서 바로 시작 (예약 여유만큼만 앞에서). 첫 프레임에 바로 그 위치로 예약됨
        if (IsMidStart) playStartTime = MidStartChartTime - musicScheduleLead;
        currentPlayTime = playStartTime;
        gameStartTime = (double)Time.time;
        // [GC 끊김 줄이기] 로딩 중 생긴 쓰레기를 시작 직전에 한 번 치워서 플레이 중 메모리 정리(GC)가 덜 일어나게.
        //  증분 GC 가 켜져 있으면(Player Settings > Use incremental GC) 한 프레임에 1ms 씩만 나눠서 정리
        System.GC.Collect();
        UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = 1000000UL;
        currentState = GameState.Playing; 
        // 곡 중간부터 틀 때는 소리가 툭 끊겨 들어오지 않게 짧게 페이드 인
        if (IsMidStart && bgmSource != null) { bgmSource.volume = 0f; StartCoroutine(FadeInAudioRoutine(0.6f)); }
    }

    private float EaseOut(float x) { return 1f - Mathf.Pow(1f - x, 3f); }

    public void LoadChart(int songId, int levelId) 
    {
        ResetStats();
        notes.Clear(); bpmChanges.Clear(); 
        uiEvents.Clear();
        laneFoldEvents.Clear(); longHeadOf.Clear(); slideParts.Clear();
        difficultyRevealTime = double.PositiveInfinity;
        for (int L = 1; L <= 2; L++) { speedChangesByLayer[L].Clear(); scrollEventsByLayer[L].Clear(); notesByLayer[L].Clear(); }
        speedNotices.Clear();

        songStartOffset = 0; chartAudioOffset = 0;
        bool hasMusicOffset = false;
        BuildLaneMap();
        for(int i=0; i<4; i++) notesByLane[i].Clear();

        string path = Path.Combine(Application.streamingAssetsPath, "chart", $"{songId}_{levelId}.txt");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"채보 파일이 없습니다: {path}");
            return;
        }

        string[] lines = File.ReadAllLines(path);
        double initialBpm = 120.0;
        double initialSpeed = 1.0;
        List<RawEvent> rawEvents = new List<RawEvent>();

        foreach (string rawLine in lines) 
        {
            int commentIndex = rawLine.IndexOf('#');
            string line = commentIndex >= 0 ? rawLine.Substring(0, commentIndex).Trim() : rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] data = line.Split(',');
            string type = data[0].Trim().ToUpper();

            if (type == ChartOffset.Key || type == "OFFSET" || type == "SONGSTARTOFFSET") { 
                // MUSICOFFSET(ms, osu! 와 같은 부호)이 있으면 그 값이 우선, 없으면 예전 OFFSET(초, 반대 부호)
                double start; bool isNew;
                if (ChartOffset.TryParse(type, data, out start, out isNew) && (isNew || !hasMusicOffset)) {
                    songStartOffset = start;
                    if (isNew) hasMusicOffset = true;
                }
                continue; 
            }
            else if (type == "DEATHAREA") {
                // DEATHAREA, 시작초, 끝초 : 에디터 전용 표시(노트 배치 금지 구간). 시간 보정은 BPM 이벤트로 이미 반영됨.
                continue;
            }
            else if (type == "AUDIOOFFSET") {
                if (data.Length >= 2) chartAudioOffset = double.Parse(data[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            else if (type == "SPEED" && data.Length == 2) {
                initialSpeed = double.Parse(data[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            else if (line.StartsWith("NoteSpeed:", StringComparison.OrdinalIgnoreCase)) {
                initialSpeed = double.Parse(line.Split(':')[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            else if (type == "BPM" && data.Length == 2) {
                initialBpm = double.Parse(data[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            else if (type == "UI") {
                // 프리키 구간에서 기본 UI 를 숨기던 줄(BasicUI / FREEKEYUI)은 프리키 삭제와 함께 무시
                string targetUi = data.Length > 1 ? data[1].Trim() : "";
                if (targetUi == "BasicUI" || targetUi == "FREEKEYUI") continue;
                uiEvents.Add(new UIEventData {
                    TargetUI = data[1].Trim(), Action = data[2].Trim(),
                    StartTime = double.Parse(data[3].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                    Duration = double.Parse(data[4].Trim(), System.Globalization.CultureInfo.InvariantCulture)
                });
                continue;
            }
            else if (type == "FREEKEY") {
                continue; // 프리키(FREE-KEY) 기능은 삭제됨 — 예전 채보에 남아 있어도 무시
            }

            if (data.Length >= 5) {
                {
                    string val = string.Join(",", data.Skip(4).Select(s => s.Trim()));
                    rawEvents.Add(new RawEvent { Type = type, Measure = long.Parse(data[1].Trim()), Num = long.Parse(data[2].Trim()), Den = long.Parse(data[3].Trim()), Value = val });
                }
            }
            else if (data.Length >= 4) rawEvents.Add(new RawEvent { Type = type, Measure = long.Parse(data[1].Trim()), Num = long.Parse(data[2].Trim()), Den = 4, Value = data[3].Trim() });
        }

        List<BpmChange> tempBpms = new List<BpmChange>();
        tempBpms.Add(new BpmChange { Beats = 0, Time = 0.0, Bpm = initialBpm }); 

        // BPM   , 마디, 분자, 분모, BPM          : 시각 = 앞에서부터 박 × BPM 누적
        // TIMING, 마디, 분자, 분모, BPM, 시각ms  : 이 박은 반드시 '시각ms'(0박 기준)에 온다 (osu! 빨간 줄과 같음, 누적 오차 없음)
        var bpmEvts = rawEvents.Where(e => e.Type == "BPM" || e.Type == "TIMING").OrderBy(e => ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0);
        foreach (var b in bpmEvts) {
            double beats = ((b.Measure - 1) * 4.0) + ((double)b.Num / Math.Max(1, b.Den)) * 4.0; 
            string[] vals = b.Value.Split(',');
            var change = new BpmChange { Beats = beats, Bpm = double.Parse(vals[0].Trim(), System.Globalization.CultureInfo.InvariantCulture) };
            if (b.Type == "TIMING" && vals.Length >= 2)
                change.AnchorTime = double.Parse(vals[1].Trim(), System.Globalization.CultureInfo.InvariantCulture) / 1000.0;
            if (beats <= 1e-9) { tempBpms[0].Bpm = change.Bpm; continue; } // 0박의 BPM 은 첫 BPM 으로
            tempBpms.Add(change); 
        }

        double currentTime = 0.0, currentBeats = 0, currentBpmVal = tempBpms[0].Bpm;
        for (int i = 1; i < tempBpms.Count; i++) {
            if (tempBpms[i].HasAnchor) tempBpms[i].Time = tempBpms[i].AnchorTime;
            else tempBpms[i].Time = currentTime + (tempBpms[i].Beats - currentBeats) * (60.0 / currentBpmVal);
            currentTime = tempBpms[i].Time; currentBeats = tempBpms[i].Beats; currentBpmVal = tempBpms[i].Bpm;
        }
        // 박 순서로 정렬 (GetExactTime 은 박으로 찾는다)
        bpmChanges = tempBpms.OrderBy(b => b.Beats).ToList();

        double GetExactTime(double beats) {
            var lastBpm = bpmChanges.LastOrDefault(b => b.Beats <= beats + 0.000001) ?? bpmChanges[0];
            return lastBpm.Time + (beats - lastBpm.Beats) * (60.0 / lastBpm.Bpm);
        }

        double[] currentSpeedByLayer = new double[] { 0.0, initialSpeed, initialSpeed };
        for (int L = 1; L <= 2; L++) speedChangesByLayer[L].Add(new SpeedChange { Time = -999999.0, Multiplier = initialSpeed });

        foreach (var e in rawEvents.Where(e => e.Type != "BPM" && e.Type != "TIMING")) {
            double absoluteBeats = ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0;
            double exactTime = GetExactTime(absoluteBeats);

            if (e.Type == "SPEED") {
                // 형식1(구형): SPEED, 마디, 분자, 분모, 속도           → 1층/2층 모두
                // 형식2(신형): SPEED, 마디, 분자, 분모, 층, 속도       → 해당 층만
                ParseSpeedEventValue(e.Value, out int spdLayer, out double spdValue);
                if (IsSpeedShowFlag(e.Value)) speedNotices.Add(new SpeedNotice { Time = exactTime, Layer = spdLayer });
                for (int L = 1; L <= 2; L++) {
                    if (spdLayer != 0 && spdLayer != L) continue;
                    currentSpeedByLayer[L] = spdValue;
                    speedChangesByLayer[L].Add(new SpeedChange { Time = exactTime, Multiplier = spdValue });
                    // 층을 명시한 변속이 채보 시작 지점(0초)이면 그 층의 초기 속도로도 사용 (카운트다운 구간 포함)
                    if (spdLayer != 0 && exactTime <= 1e-9) speedChangesByLayer[L][0].Multiplier = spdValue;
                }
            }
            else if (e.Type == "STOP") { for (int L = 1; L <= 2; L++) speedChangesByLayer[L].Add(new SpeedChange { Time = exactTime, Multiplier = 0.0 }); }
            else if (e.Type == "START") { for (int L = 1; L <= 2; L++) speedChangesByLayer[L].Add(new SpeedChange { Time = exactTime, Multiplier = currentSpeedByLayer[L] }); }
            // (AREA 줄은 영역 기능이 삭제되어 무시)
            else if (e.Type == "FOLD" || e.Type == "UNFOLD" || e.Type == "HIDE" || e.Type == "SHOW") {
                // 레인 사라짐 / 드러남: FOLD(=HIDE) / UNFOLD(=SHOW), 마디, 분자, 분모, 초 (다 사라지는/드러나는 데 걸리는 시간)
                //  (예전 이름 '접기 / 펼치기' 의 FOLD · UNFOLD 를 그대로 씀. HIDE · SHOW 로 써도 같음)
                double dur;
                if (!double.TryParse(e.Value.Split(',')[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dur)) dur = 0.5;
                laneFoldEvents.Add(new LaneFoldEvent { Time = exactTime, Fold = e.Type == "FOLD" || e.Type == "HIDE", Duration = Math.Max(0.0, dur) });
            }
            else if (e.Type == "REVEAL") {
                // 숨긴 난이도(?) 공개: REVEAL, 마디, 분자, 분모, LEVEL — 이 시점부터 게임 화면 난이도가 실제 값으로 바뀜 (여러 개면 가장 앞)
                difficultyRevealTime = Math.Min(difficultyRevealTime, exactTime);
            }
            else if (e.Type == "NOTE") {
                string[] lanesData = e.Value.Split(',');
                
                // 구형 압축 포맷 (예: 1000) - 층 개념이 없으므로 전부 2층(Top)
                if (lanesData.Length == 1 && !lanesData[0].Contains("_") && !lanesData[0].Contains("(")) {
                    string val = lanesData[0].Trim();
                    for (int lane = 0; lane < val.Length && lane < 4; lane++) {
                        if (val[lane] != '0') {
                            int noteType = NoteTypes.Normalize(int.Parse(val[lane].ToString()));   // 예전 화살표(7 · 8) → 탭
                            if (noteType == 6 || noteType == 5) continue;
                            int ml = MapLaneStart(lane, 1);   // MIRROR / RANDOM
                            var newNote = new NoteData { HitTime = exactTime, Lane = ml, Type = noteType, IsHit = false, isTimeLogged = false, Layer = NoteLayer.Top };
                            notes.Add(newNote); notesByLane[ml].Add(newNote); 
                        }
                    }
                } else {
                    // 레인 칸 형식: "2층노트" 또는 "2층노트(1층노트)"  예) 11_3(12)
                    for (int lane = 0; lane < lanesData.Length && lane < 4; lane++) {
                        NoteCellParser.SplitCell(lanesData[lane], out string topToken, out string bottomToken);
                        AddNoteFromToken(topToken, lane, exactTime, NoteLayer.Top);
                        AddNoteFromToken(bottomToken, lane, exactTime, NoteLayer.Bottom);
                    }
                }
            }
        }

        for (int L = 1; L <= 2; L++) speedChangesByLayer[L] = speedChangesByLayer[L].OrderBy(s => s.Time).ToList();
        laneFoldEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        if (laneFoldEvents.Count > 0)
            Debug.Log($"[Chart] 레인 사라짐/드러남 {laneFoldEvents.Count}개 (첫 이벤트 {laneFoldEvents[0].Time:0.000}s, {(laneFoldEvents[0].Fold ? "사라짐" : "드러남")})");
        BuildSlides();   // 슬라이드: 같은 번호의 점들을 이음
        notes = notes.OrderBy(n => n.HitTime).ToList();
        for (int i = 0; i < 4; i++) notesByLane[i] = notesByLane[i].OrderBy(n => n.HitTime).ThenByDescending(n => n.Layer).ToList();

        // 층별 스폰 리스트
        foreach (var n in notes) notesByLayer[Mathf.Clamp(n.Layer, 1, 2)].Add(n);

        // 층별 누적 스크롤 거리 테이블
        for (int L = 1; L <= 2; L++)
        {
            var layerSpeeds = speedChangesByLayer[L];
            var allTimes = layerSpeeds.Select(s => s.Time).Distinct().ToList();
            if (!allTimes.Contains(0.0)) allTimes.Add(0.0);
            if (!allTimes.Contains(-999999.0)) allTimes.Add(-999999.0);
            allTimes.Sort();

            double currentPos = 0.0; double curSpeedTemp = layerSpeeds[0].Multiplier; double lastTime = allTimes[0];

            foreach (var tm in allTimes) {
                double dt = tm - lastTime;
                if (dt > 0) currentPos += dt * curSpeedTemp; 
                var s = layerSpeeds.LastOrDefault(x => x.Time <= tm + 1e-6);
                if (s != null) curSpeedTemp = s.Multiplier;
                scrollEventsByLayer[L].Add(new GameScrollEvent { Time = tm, Multiplier = currentPos }); 
                lastTime = tm;
            }
        }

        // 롱노트 연결: (레인, 층) 단위로 분리해서 연결한다.
        //  → 2층의 시작 판정과 1층의 끝 판정은 절대 서로 연결되지 않는다.
        //  → 빨간 노트는 기준 레인(Lane)에서만 연결해 에디터의 몸통 연결 규칙과 일치시킨다.
        for (int lane = 0; lane < 4; lane++) {
            NoteData[] lastLinkNode = new NoteData[3]; // 인덱스 = 층(1, 2)
            foreach (var note in notesByLane[lane]) {
                if (note.Lane != lane) continue;   // 넓은 노트(RED · 넓은 일반)는 기준 레인에서만 연결
                if (note.Slide != null) continue;   // 슬라이드는 BuildSlides 에서 이미 이음
                int ly = Mathf.Clamp(note.Layer, 1, 2);

                if (NoteTypes.IsLongHead(note.Type)) lastLinkNode[ly] = note;
                else if ((note.Type == 3 || note.Type == 13) && lastLinkNode[ly] != null) {
                    if (lastLinkNode[ly].IsRedNote == note.IsRedNote) {
                        lastLinkNode[ly].NextLongNote = note; longHeadOf[note] = lastLinkNode[ly];
                        note.DragTail = lastLinkNode[ly].Type == NoteTypes.DragLongHead;   // 드래그로 시작한 롱노트의 끝 (표시용 정보, 판정 · 색은 일반 끝과 같음)
                    }
                    lastLinkNode[ly] = null;
                }
            }
        }

        ApplyMidStart();
        GenerateFakeNotes();   // 게임 모드 FAKE: 가짜 노트 (판정 · 노트 수에는 들어가지 않음)

        // 채보 전체 노트 수: 완벽하게 쳤을 때 판정이 나오는 횟수 (= 풀콤보 시 MAX COMBO)
        //  노트(탭·화살표·롱노트 머리/꼬리) + 롱노트 중간 틱. 틱은 실제 플레이와 똑같은 규칙으로 미리 세어 둔다.
        chartJudgeCount = 0; chartNoteCount = 0; chartTickCount = 0; chartLastTime = 0; chartEndCursor = 0;
        chartJudgeEndTime = 0;
        foreach (var n in notes) {
            if (n.HitTime > chartLastTime) chartLastTime = n.HitTime;
            if (IsJudgableNote(n) && n.HitTime > chartJudgeEndTime) chartJudgeEndTime = n.HitTime; // 판정이 있는 마지막 노트 (꼬리 포함)
            if (NoteTypes.IsTapLike(n.Type)) chartNoteCount++;
            else if (NoteTypes.IsLongHead(n.Type)) {
                // 슬라이드: 머리 + 중계점들 + 끝 (점 수만큼)
                chartNoteCount += n.Slide != null ? n.Slide.Points.Count : (n.NextLongNote != null) ? 2 : 1;
                if (n.NextLongNote != null) chartTickCount += CountLongNoteTicks(n);
            }
        }
        chartJudgeCount = chartNoteCount + chartTickCount;
        ResetTempest();   // TEMPEST 게이지: 노트 수 · 채보 길이로 자동 감소율 계산
        RedNoteStarts = notes.Where(n => n.IsRedNote && (n.Type == 11 || n.Type == 12)).OrderBy(n => n.HitTime).ToList();

        double maxBeat = 0;
        if (rawEvents.Count > 0) {
            maxBeat = rawEvents.Max(e => ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0);
        }
        speedNotices.Sort((x, y) => x.Time.CompareTo(y.Time));

        allMeasureTimes.Clear();
        // 같은 박자에 채보에서 직접 추가한 마디선(Type 4)이 있으면 원래 마디선은 만들지 않음
        var customLineTimes = notes.Where(n => n.Type == 4).Select(n => n.HitTime).ToList();
        for (double b = 0; b <= maxBeat + 4.0; b += 4.0) {
            double t = GetExactTime(b);
            if (customLineTimes.Any(ct => Math.Abs(ct - t) < 0.0005)) continue;
            allMeasureTimes.Add(t);
        }
        allMeasureTimes.Sort();
        measureSpawnIndex = 0;
    }

    // =====================================================================
    //  게임 모드: FAKE — 치면 안 되는 가짜 노트
    //   노트가 있는 시각마다 FakeChance 확률로, 그 시각에 비어 있는 레인 하나에 가짜 노트를 하나 놓음
    //   (같은 레인의 진짜 노트 · 롱노트 구간에서 FakeGap 초 이상 떨어진 곳만)
    //   가짜 노트는 판정 · 노트 수 · 결과에 들어가지 않고, 그냥 지나가면 아무 일도 없음
    //   쳤을 때(그 레인에 칠 진짜 노트가 없고 ±GOOD 범위 안): 지금 LIFE 의 50% 감소
    // =====================================================================
    private readonly List<NoteData>[] fakeByLane = { new List<NoteData>(), new List<NoteData>(), new List<NoteData>(), new List<NoteData>() };

    private void GenerateFakeNotes()
    {
        for (int i = 0; i < 4; i++) fakeByLane[i].Clear();
        if (!GameModes.Play.Fake || IsTestMode) return;
        System.Random rng = new System.Random(Guid.NewGuid().GetHashCode());

        var busy = new List<KeyValuePair<double, double>>[4];
        for (int i = 0; i < 4; i++) busy[i] = new List<KeyValuePair<double, double>>();
        foreach (var n in notes)
        {
            if (n.Type == 4) continue;
            if (n.Slide != null)
            {
                // 슬라이드: 레인을 넘나들며 다른 키를 누르게 되므로 그 시간 동안은 모든 레인에 가짜 노트를 두지 않음
                for (int l = 0; l < 4; l++) busy[l].Add(new KeyValuePair<double, double>(n.HitTime - GameModes.FakeGap, n.Slide.EndTime + GameModes.FakeGap));
                continue;
            }
            double a = n.HitTime - GameModes.FakeGap;
            double b = (NoteTypes.IsLongHead(n.Type) && n.NextLongNote != null ? n.NextLongNote.HitTime : n.HitTime) + GameModes.FakeGap;
            for (int w = 0; w < Mathf.Max(1, n.Width); w++)
                if (n.Lane + w < 4) busy[n.Lane + w].Add(new KeyValuePair<double, double>(a, b));
        }

        var times = notes.Where(n => n.Type != 4 && n.Type != 3 && n.Type != 13).Select(n => n.HitTime).Distinct().OrderBy(t => t).ToList();
        int made = 0;
        int[] order = { 0, 1, 2, 3 };
        foreach (double t in times)
        {
            if (rng.NextDouble() >= GameModes.FakeChance) continue;
            for (int i = 3; i > 0; i--) { int j = rng.Next(i + 1); int tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
            foreach (int lane in order)
            {
                bool free = true;
                foreach (var r in busy[lane]) if (t >= r.Key && t <= r.Value) { free = false; break; }
                if (!free) continue;
                var fake = new NoteData { HitTime = t, Lane = lane, Type = NoteTypes.Tap, IsHit = false, isTimeLogged = false, Layer = NoteLayer.Top, IsFake = true };
                fakeByLane[lane].Add(fake);
                notesByLayer[NoteLayer.Top].Add(fake);
                busy[lane].Add(new KeyValuePair<double, double>(t - GameModes.FakeGap, t + GameModes.FakeGap));
                made++;
                break;
            }
        }
        if (made > 0)
        {
            var sorted = notesByLayer[NoteLayer.Top].OrderBy(n => n.HitTime).ToList();
            notesByLayer[NoteLayer.Top].Clear();
            notesByLayer[NoteLayer.Top].AddRange(sorted);
        }
        Debug.Log($"[Mode] FAKE 가짜 노트 {made}개");
    }

    private void CheckFakeHit(int lane, double judgeT)
    {
        if (!GameModes.Play.Fake || lane < 0 || lane >= 4) return;
        foreach (var f in fakeByLane[lane])
        {
            if (f.IsHit) continue;
            if (f.HitTime - judgeT > W_GOOD) break;
            if (Math.Abs(f.HitTime - judgeT) > W_GOOD) continue;
            f.IsHit = true;
            HideNoteObject(f);
            if (LifeEnabled && !LifeInfinite && !isFailed && !ShieldActive)   // ONE+ 보호 중에는 깎이지 않음
            {
                double loss = life * GameModes.FakePenalty;
                life -= loss;
                if (Gauge == GaugeKind.Tempest) tempestSettled -= loss;   // TEMPEST 는 매 프레임 다시 계산하므로 반영분에서 뺌
                if (recLife.Count > 0) recLife[recLife.Count - 1] = (float)life;
            }
            GameModePopup.Show(gameObject.scene, "FAKE", new Color(1f, 0.38f, 0.45f), 1f);
            Debug.Log($"[Mode] FAKE 노트를 침 → LIFE {life:0.0}");
            return;
        }
    }

    // 토큰("11_3", "2" ...) 하나를 노트로 만들어 등록한다.
    private void AddNoteFromToken(string token, int lane, double hitTime, int layer)
    {
        if (lane < 0 || lane >= 4) return;
        string core = NoteCellParser.SplitSlide(token, out int slideId, out int curve);   // 슬라이드 표시 "~번호[곡선]"
        if (!NoteCellParser.TryParseToken(core, out int type, out int width, out bool isRed)) return;
        type = NoteTypes.Normalize(type);   // 예전 화살표 노트(7 · 8) → 일반 탭

        // 폭(Width): RED 노트 · 넓은 일반 노트("1_3" 처럼) 모두 1~4칸. 덮는 레인 모두에서 칠 수 있음
        int w0 = Mathf.Clamp(width, 1, 4 - lane);
        int ml = MapLaneStart(lane, w0);   // MIRROR / RANDOM
        var newNote = new NoteData { HitTime = hitTime, Lane = ml, Type = type, IsHit = false, isTimeLogged = false, Layer = layer };
        newNote.IsRedNote = isRed;
        newNote.Width = w0;
        newNote.SlideId = slideId;
        newNote.Curve = curve;
        notes.Add(newNote);
        if (slideId > 0) { slideParts.Add(newNote); return; }   // 슬라이드의 점: BuildSlides 에서 잇고, 레인 목록에는 머리만
        for (int w = 0; w < newNote.Width; w++) notesByLane[ml + w].Add(newNote);
    }

    // =====================================================================
    //  슬라이드 (레인을 넘나드는 롱노트) 잇기
    //   같은 번호 · 같은 층의 점들을 시간 순으로: 처음 = 머리(롱노트 시작), 마지막 = 끝(롱노트 끝), 가운데 = 중계점(14)
    //   머리.NextLongNote = 끝 (일반 롱노트와 같은 흐름으로 처리되는 부분은 그대로), 머리.Slide = 경로
    //   레인별 노트 목록에는 머리만 넣음 (머리는 일반 롱노트처럼 그 레인 키로 침, 그 뒤는 UpdateSlides)
    // =====================================================================
    private readonly List<NoteData> slideParts = new List<NoteData>();

    private void BuildSlides()
    {
        foreach (var g in slideParts.GroupBy(n => n.Layer * 100000 + n.SlideId))
        {
            var pts = g.OrderBy(n => n.HitTime).ToList();
            if (pts.Count < 2)
            {
                // 점이 하나뿐: 그냥 노트 하나로 (중계점 · 끝 하나만 있으면 탭으로)
                NoteData one = pts[0];
                one.SlideId = 0;
                if (!NoteTypes.IsTapLike(one.Type) && !NoteTypes.IsLongHead(one.Type)) one.Type = one.IsRedNote ? 11 : NoteTypes.Tap;
                AddToLaneLists(one);
                continue;
            }
            NoteData head = pts[0], tail = pts[pts.Count - 1];
            if (!NoteTypes.IsLongHead(head.Type)) head.Type = head.IsRedNote ? 12 : NoteTypes.LongHead;
            tail.IsRedNote = head.IsRedNote;
            tail.Type = head.IsRedNote ? 13 : NoteTypes.LongTail;
            tail.DragTail = head.Type == NoteTypes.DragLongHead;
            var path = new SlidePath();
            foreach (var p in pts)
            {
                if (p != head && p != tail) { p.Type = NoteTypes.SlideRelay; p.IsRedNote = head.IsRedNote; }
                if (p != head) { p.SlideHead = head; longHeadOf[p] = head; }
                path.Points.Add(new SlidePoint { Time = p.HitTime, Lane = p.Lane, Width = Mathf.Max(1, p.Width), Curve = p.Curve, Note = p });
            }
            head.Slide = path;
            head.NextLongNote = tail;
            AddToLaneLists(head);
        }
        slideParts.Clear();
    }

    private void AddToLaneLists(NoteData n)
    {
        for (int w = 0; w < Mathf.Max(1, n.Width); w++) if (n.Lane + w < 4) notesByLane[n.Lane + w].Add(n);
    }

    // =====================================================================
    //  게임 모드: MIRROR / RANDOM — 채보를 읽을 때 레인 번호를 바꿈
    //   MIRROR : 0 1 2 3 → 3 2 1 0
    //   RANDOM : 판마다 무작위로 섞음 (원래와 같은 배치는 나오지 않음). 한 판 동안은 같은 섞임이라 롱노트 · 동시치기가 그대로 유지됨
    //            넓은 노트(2 ~ 3칸)는 섞인 뒤에도 붙어 있어야 하므로, 섞임이 그 노트의 레인 순서를 뒤집으면 좌우 반전 자리, 아니면 제자리
    // =====================================================================
    private int[] laneMap;   // null = 그대로

    private void BuildLaneMap()
    {
        laneMap = null;
        if (GameModes.Play.Mirror) laneMap = new[] { 3, 2, 1, 0 };
        else if (GameModes.Play.Random)
        {
            System.Random rng = new System.Random(Guid.NewGuid().GetHashCode());
            int[] m = { 0, 1, 2, 3 };
            do
            {
                for (int i = 3; i > 0; i--) { int j = rng.Next(i + 1); int t = m[i]; m[i] = m[j]; m[j] = t; }
            } while (m[0] == 0 && m[1] == 1 && m[2] == 2 && m[3] == 3);
            laneMap = m;
            Debug.Log($"[Mode] RANDOM 배치: {m[0]}{m[1]}{m[2]}{m[3]}");
        }
    }

    // =====================================================================
    //  게임 모드: REVERSE — 노트가 내려오는 방향을 반대로 (판정선 · 키 빔 등 판정 부분 전체가 위쪽, 노트는 아래 → 위)
    //   화면을 돌리지 않고 위아래만 거울처럼 바꿈 → 레인 순서 · 키는 그대로 (왼쪽 레인 = 왼쪽 키)
    //   SIMPLE: 게임 카메라가 위아래를 바꿔 그림 (ReverseView). 이 카메라가 그리는 화면 UI 는 한 번 더 뒤집어 글자가 바로 보임
    //   SIMPLE-DIVERSE: 원근 레인 쪽에서 위아래만 바꿈 (DiversePlayfield)
    //   배경(MV 포함)은 뒤집지 않음
    // =====================================================================
    public Key[] LaneKeys { get { return laneKeys; } }

    private void ApplyReverseView()
    {
        bool diverse = simpleSkin != null && simpleSkin.IsDiverse;
        if (diverse || mainCam == null) return;
        ReverseView.Attach(mainCam, bgRenderer);
    }

    private int MapLaneStart(int lane, int width)
    {
        if (laneMap == null || lane < 0 || lane > 3) return lane;
        if (width >= 4) return 0;
        if (width <= 1) return laneMap[lane];
        int last = Mathf.Min(3, lane + width - 1);
        int lo = 4, hi = -1;
        for (int i = lane; i <= last; i++) { lo = Mathf.Min(lo, laneMap[i]); hi = Mathf.Max(hi, laneMap[i]); }
        if (hi - lo + 1 == width) return lo;                        // 섞인 뒤에도 붙어 있음
        return laneMap[lane] > laneMap[last] ? 4 - lane - width : lane;   // 붙어 있지 않으면 반전 / 제자리
    }

    // =====================================================================
    //  레인 사라짐 / 드러남 (채보의 FOLD · UNFOLD = HIDE · SHOW 줄)
    // =====================================================================
    private readonly List<LaneFoldEvent> laneFoldEvents = new List<LaneFoldEvent>();

    // =====================================================================
    //  시작 타임 조정 (설정 > GAMEPLAY > START TIME)
    //   예) 109.2초 → 음악을 109.2초부터 틀고, 그 뒤 2초(노트가 내려오는 시간) 안의 노트는 아예 빼서
    //       111.2초 노트부터 내려온다. 빠진 노트는 전체 노트 수 · 점수 · 콤보 계산에도 들어가지 않는다.
    //   롱노트는 머리가 빠지면 꼬리도 같이 뺀다 (몸통만 내려오는 일 없음)
    // =====================================================================
    public const double MidStartSkipSeconds = 2.0;
    public bool IsMidStart { get; private set; }
    public double MidStartSeconds { get; private set; }          // 음악 위치 (초)
    public double MidStartChartTime { get { return TriggerTime + MidStartSeconds; } }   // 그 음악 위치의 차트 시각

    private void ApplyMidStart()
    {
        IsMidStart = false;
        MidStartSeconds = 0.0;
        double s = SettingsStore.Current.StartTime;
        if (s <= 0.0 || IsTestMode) return;     // GAME SETTING 에서는 시작 시간 설정을 쓰지 않음 (처음부터 반복)
        AudioClip clip = bgmSource != null ? bgmSource.clip : null;
        if (clip != null && s >= clip.length - 0.5)
        {
            Debug.LogWarning($"[StartTime] 시작 시각 {s:0.0}s 가 곡 길이({clip.length:0.0}s)를 넘어 처음부터 시작합니다.");
            return;
        }
        IsMidStart = true;
        MidStartSeconds = s;

        double startT = MidStartChartTime;
        double cut = startT + MidStartSkipSeconds;
        var removed = new HashSet<NoteData>();
        foreach (var n in notes)
        {
            if (n.Type == 4) { if (n.HitTime < startT) removed.Add(n); continue; }   // 마디선은 시작 전 것만
            if (longHeadOf.ContainsKey(n)) continue;                                   // 꼬리는 머리를 따라감
            if (n.HitTime < cut)
            {
                removed.Add(n);
                if (n.NextLongNote != null) removed.Add(n.NextLongNote);
                if (n.Slide != null) foreach (var sp in n.Slide.Points) removed.Add(sp.Note);   // 슬라이드의 중계점도
            }
        }
        // 짝 없는 꼬리(머리가 없는 롱노트 끝)는 그 시각 기준으로 뺌. 머리가 남은 꼬리는 항상 머리보다 늦어서 남음
        foreach (var n in notes)
            if ((n.Type == 3 || n.Type == 13) && !longHeadOf.ContainsKey(n) && n.HitTime < cut) removed.Add(n);
        if (removed.Count == 0) return;

        notes.RemoveAll(n => removed.Contains(n));
        for (int i = 0; i < 4; i++) notesByLane[i].RemoveAll(n => removed.Contains(n));
        for (int L = 1; L <= 2; L++) notesByLayer[L].RemoveAll(n => removed.Contains(n));
        Debug.Log($"[StartTime] {s:0.0}s 부터 시작 · 앞 {MidStartSkipSeconds:0}초 안의 노트 {removed.Count}개 제외 (첫 노트 ≥ {s + MidStartSkipSeconds:0.0}s)");
    }

    private readonly Dictionary<NoteData, NoteData> longHeadOf = new Dictionary<NoteData, NoteData>();   // 롱노트 꼬리 → 머리

    public bool HasLaneFolds { get { return laneFoldEvents.Count > 0; } }

    // ---- 숨긴 난이도 (? → 실제 값) ----
    private double difficultyRevealTime = double.PositiveInfinity;   // 채보 REVEAL 줄의 시각 (없으면 공개 안 함)
    public bool DifficultyHidden
    {
        get { return currentSong != null && CoreManager.Instance != null && DifficultyHelper.IsHidden(currentSong, CoreManager.Instance.CurrentLevelId); }
    }
    // 지금 실제 난이도가 드러났는가 (숨긴 난이도 + REVEAL 시점을 지남)
    public bool DifficultyRevealed { get { return DifficultyHidden && currentPlayTime >= difficultyRevealTime; } }

    // 레인 사라짐 / 드러남 진행도 (선이 빨려 들어가는 / 올라오는 모션용)
    //  반환: 0 ~ 1 (마지막 이벤트 시작 ~ 끝), hiding = 마지막 이벤트가 '사라짐'(FOLD) 인가
    //  이벤트가 아직 없으면 1, false (= 다 보임)
    public float LaneHideProgress(double t, out bool hiding)
    {
        LaneFoldEvent last = null;
        for (int i = 0; i < laneFoldEvents.Count && laneFoldEvents[i].Time <= t + 1e-9; i++) last = laneFoldEvents[i];
        hiding = last != null && last.Fold;
        if (last == null) return 1f;
        double p = last.Duration <= 1e-6 ? 1.0 : (t - last.Time) / last.Duration;
        return Mathf.Clamp01((float)p);
    }

    // 레인 보임 정도 (1 = 다 보임, 0 = 다 사라짐) — 예전 이름은 '펼침 정도'

    public float LaneFoldAt(double t)
    {
        LaneFoldEvent last = null;
        for (int i = 0; i < laneFoldEvents.Count && laneFoldEvents[i].Time <= t + 1e-9; i++) last = laneFoldEvents[i];
        if (last == null) return 1f;
        double p = last.Duration <= 1e-6 ? 1.0 : (t - last.Time) / last.Duration;
        float e = Mathf.Clamp01((float)p);
        e = e * e * (3f - 2f * e);
        return last.Fold ? 1f - e : e;
    }

    // "층, 속도, SHOW" 처럼 끝에 SHOW 가 붙은 변속 = 에디터에서 SPEED UP / DOWN 표시를 켠 변속
    private static bool IsSpeedShowFlag(string value)
    {
        string[] p = value.Split(',');
        return p.Length >= 3 && p[2].Trim().ToUpperInvariant() == "SHOW";
    }

    // SPEED UP / DOWN 을 표시할 변속 목록 (시간 순)
    private readonly List<SpeedNotice> speedNotices = new List<SpeedNotice>();
    public List<SpeedNotice> SpeedNotices { get { return speedNotices; } }

    private static void ParseSpeedEventValue(string value, out int layer, out double speed)
    {
        layer = 0;
        string[] p = value.Split(',');
        if (p.Length >= 2)
        {
            layer = int.Parse(p[0].Trim());
            if (layer != 1 && layer != 2) layer = 0; // 잘못된 층 번호는 전체 적용
            speed = double.Parse(p[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            speed = double.Parse(p[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public double GetCurrentScrollDist(int layer)
    {
        return currentScrollDistByLayer[Mathf.Clamp(layer, 1, 2)];
    }

    private void UpdateCurrentScrollDist()
    {
        currentScrollDistByLayer[1] = GetScrollDistance(currentPlayTime, NoteLayer.Bottom);
        currentScrollDistByLayer[2] = GetScrollDistance(currentPlayTime, NoteLayer.Top);
        currentScrollDist = currentScrollDistByLayer[2];
    }

    public double GetScrollDistance(double targetTime, int layer = NoteLayer.Top) 
    {
        layer = Mathf.Clamp(layer, 1, 2);
        var scrollEvents = scrollEventsByLayer[layer];
        var speedChanges = speedChangesByLayer[layer];
        double speedMultiplier = CoreManager.Instance.CurrentSettings.SettingSpeed / 4.0;
        
        if (scrollEvents.Count == 0) return targetTime * baseScrollSpeed * speedMultiplier;
        
        // [렉 수정] 예전: LastOrDefault(람다) → 매번 목록 전체를 훑고 + 쓰레기(메모리) 생성. 노트 생성 · 영역 그리기 등에서
        //  한 프레임에 수십~수백 번 불려 GC 끊김의 원인이 됐다. 지금: 시간순 목록에서 이진 탐색 (할당 없음)
        int ei = LastAtOrBefore(scrollEvents, targetTime + 1e-6);
        var ev = ei >= 0 ? scrollEvents[ei] : scrollEvents[0];
        double dt = targetTime - ev.Time;
        int si = LastAtOrBefore(speedChanges, targetTime + 1e-6);
        double spd = si >= 0 ? speedChanges[si].Multiplier : 1.0;
        
        return (ev.Multiplier + dt * spd) * baseScrollSpeed * speedMultiplier;
    }

    // 시간순 목록에서 Time <= t 인 마지막 칸 (없으면 -1)
    private static int LastAtOrBefore(List<GameScrollEvent> list, double t)
    {
        int lo = 0, hi = list.Count - 1, ans = -1;
        while (lo <= hi) { int mid = (lo + hi) >> 1; if (list[mid].Time <= t) { ans = mid; lo = mid + 1; } else hi = mid - 1; }
        return ans;
    }
    private static int LastAtOrBefore(List<SpeedChange> list, double t)
    {
        int lo = 0, hi = list.Count - 1, ans = -1;
        while (lo <= hi) { int mid = (lo + hi) >> 1; if (list[mid].Time <= t) { ans = mid; lo = mid + 1; } else hi = mid - 1; }
        return ans;
    }

    public bool IsNoteHiddenByFutureReverseScroll(double currentTime, double noteTime, int layer = NoteLayer.Top)
    {
        if (noteTime <= currentTime) return false;
        var speedChanges = speedChangesByLayer[Mathf.Clamp(layer, 1, 2)];
        
        for (int i = 0; i < speedChanges.Count; i++)
        {
            var sc = speedChanges[i];
            if (sc.Time > noteTime) break;
            
            if (sc.Time > currentTime && sc.Multiplier < 0)
            {
                double prevSpeed = i > 0 ? speedChanges[i - 1].Multiplier : 1.0;
                if (prevSpeed >= 0) 
                {
                    return true;
                }
            }
        }
        return false;
    }

    private double GetBpmAtTime(double time)
    {
        if (bpmChanges == null || bpmChanges.Count == 0) return 120.0;
        for (int i = bpmChanges.Count - 1; i >= 0; i--)          // (LINQ 없이 — 롱노트 틱마다 불림)
            if (bpmChanges[i].Time <= time + 1e-6) return bpmChanges[i].Bpm;
        return bpmChanges[0].Bpm;
    }

    // ALL PERFECT / FULL COMBO / TRACK COMPLETE / FAILED 연출 중이거나 결과창으로 넘어가는 중이면 일시정지 불가
    public bool CanPause { get { return currentState == GameState.Playing && !clearEffectPlaying && !isFailed && !finishRoutineStarted && !isLeaving; } }

    // =====================================================================
    //  GAME SETTING (5_Test) — 테스트 모드 전용
    // =====================================================================
    public bool IsTestMode { get; private set; }
    public int TestLoopCount { get; private set; }
    // 자동 플레이: 테스트 모드에서는 패널의 AUTO PLAY, 그 밖에는 곡 선택창의 오토플레이
    public bool AutoPlayNow { get { return IsTestMode ? GameSettingSession.AutoPlay : CoreManager.Instance.IsAutoPlay; } }

    // 노트 속도를 바꾸면 증가 → 이미 내려오고 있는 노트(NoteObject)가 자기 위치 기준을 다시 계산
    public static int ScrollVersion { get; private set; }
    public void NotifyScrollChanged() { ScrollVersion++; UpdateCurrentScrollDist(); }

    // 오디오 오프셋을 바꾼 즉시 음악 위치를 새 기준으로 다시 예약 (테스트 패널에서 실시간 반영)
    public void ApplyAudioOffsetLive()
    {
        if (currentState != GameState.Playing || isFailed) return;
        if (musicScheduled) ScheduleMusic(currentPlayTime);
        if (!isFullMusicStarted && currentPlayTime >= TriggerTime) OnMusicStarted(currentPlayTime - TriggerTime);
    }

    // 판정 그래프 켜고 끄기 (게임 중에도 바로)
    public void SetHitErrorMeter(bool on)
    {
        if (on && hitErrorMeter == null)
        {
            hitErrorMeter = HitErrorMeter.Create(this, W_EXCELLENT, W_GREAT, W_GOOD, W_BAD);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(hitErrorMeter.gameObject, gameObject.scene);
        }
        else if (!on && hitErrorMeter != null) { Destroy(hitErrorMeter.gameObject); hitErrorMeter = null; }
    }

    // 이번 반복의 판정 수 [PERFECT, GREAT, GOOD, BAD, MISS] · 콤보
    public void GetJudgeCounts(int[] five)
    {
        five[0] = cntExcellent; five[1] = cntGreat; five[2] = cntGood; five[3] = cntBad; five[4] = cntMiss;
    }
    public int MaxComboNow { get { return maxCombo; } }

    // 최근 n 개 판정의 평균 오차 (ms, FAST -, SLOW +)
    //  [버그 수정] 예전에는 '이번 반복의 모든 오차'로 계산해서
    //   ① 오프셋을 바꾼(반영한) 뒤에도 바꾸기 전 오차가 섞여 추천값이 계속 바뀌고 → 다시 반영하면 과하게 옮겨져 판정이 더 튀었다
    //   ② 오토 플레이 · 드래그 노트(항상 0ms) · 롱노트 끝(손 뗀 시각)까지 섞여 값이 흔들렸다
    //  지금: 직접 친 탭 · 롱노트 시작의 오차만, 마지막으로 오프셋을 바꾼 뒤부터 모음 (ResetCalibration)
    //        평균은 위아래 15% 를 뺀 평균 (한두 번 크게 빗나간 타격에 흔들리지 않게)
    private readonly List<float> calibErrors = new List<float>();
    public void ResetCalibration() { calibErrors.Clear(); }

    public float RecentAverageErrorMs(int n, out int count)
    {
        count = Mathf.Min(n, calibErrors.Count);
        if (count == 0) return 0f;
        var buf = new List<float>(count);
        for (int i = calibErrors.Count - count; i < calibErrors.Count; i++) buf.Add(calibErrors[i]);
        buf.Sort();
        int cut = count >= 10 ? Mathf.FloorToInt(count * 0.15f) : 0;
        double sum = 0; int m = 0;
        for (int i = cut; i < count - cut; i++) { sum += buf[i]; m++; }
        return m > 0 ? (float)(sum / m) : 0f;
    }

    // 채보 · 음악을 처음부터 다시 (노트 · 판정 · 콤보 · LIFE 초기화, 채보는 다시 읽지 않음)
    public void RestartTestLoop()
    {
        for (int i = 0; i < 4; i++) StopHoldEffect(i);
        if (redFx != null) redFx.StopAll();
        if (bgmSource != null) bgmSource.Stop();

        foreach (var obj in noteObjectDict.Values.ToList()) ReturnNoteToPool(obj);
        noteObjectDict.Clear();
        for (int i = 0; i < 8; i++) activeLongNote[i] = null;
        activeSlides.Clear();
        foreach (var ml in activeMeasureLines) { ml.go.SetActive(false); measureLinePool.Enqueue(ml.go); }
        activeMeasureLines.Clear();
        foreach (var n in notes) n.IsHit = false;

        layerSpawnIndex[1] = 0; layerSpawnIndex[2] = 0;
        for (int i = 0; i < 4; i++) laneStartIndex[i] = 0;
        measureSpawnIndex = 0;
        chartEndCursor = 0;

        // 판정 기록 (채보 정보 · 전체 노트 수는 그대로)
        processedNotes = 0; currentCombo = 0; maxCombo = 0; currentAccSum = 0; accPoints = 0; accNotes = 0; cntJust = 0;
        cntExcellent = cntGreat = cntGood = cntBad = cntMiss = 0;
        errorSumMs = 0; timedCount = 0; comboSum = 0; life = StartLife; fastCount = slowCount = 0;
        ResetTempest();
        scoreLockUntil = double.NegativeInfinity;
        autoHeld.Clear();
        recProgress.Clear(); recJudge.Clear(); recLife.Clear(); recErrors.Clear();
        finishHoldTimer = 0f; clearChecked = false; endUiAlpha = 1f; onePlusUsed = false;
        crashCause = null; shieldUntil = double.NegativeInfinity; capProgress = 0f; capCatchingUp = false;
        UpdateUI();

        // 시작 위치: 첫 노트가 화면 위쪽 바깥에서 나타나 자연스럽게 내려오도록 (한꺼번에 화면 중간에 툭 나타나지 않게)
        //  - 첫 노트까지의 스크롤 거리가 '보이는 거리'(노트 생성 기준)를 넘는 시각까지 거슬러 올라가서 시작
        //  - 음악 예약 여유보다도 앞이어야 함
        isFullMusicStarted = false;
        musicScheduled = false;
        double firstNote = notes.Count > 0 ? notes[0].HitTime : 0.0;
        double t0 = firstNote - 0.5;
        for (int i = 0; i < 400; i++)
        {
            int ly = notes.Count > 0 ? notes[0].Layer : NoteLayer.Top;
            if (GetScrollDistance(firstNote, ly) - GetScrollDistance(t0, ly) > 2000.0 + 50.0 && firstNote - t0 > 0.6) break;
            t0 -= 0.05;
        }
        t0 = Math.Min(t0, firstNote - SPAWN_LOOKAHEAD - 0.05);   // 시간 기준 생성 범위(SPAWN_LOOKAHEAD) 밖에서도 시작
        playStartTime = Math.Min(TriggerTime - musicScheduleLead - 0.05, t0);
        currentPlayTime = playStartTime;
        UpdateCurrentScrollDist();
        TestLoopCount++;
    }

    public void PauseGame()
    {
        if (!CanPause) return;
        currentState = GameState.Paused;
        
        // 다시 시작할 때 되감은 위치에서 새로 예약 재생하므로 여기서는 완전히 멈춤 (예약만 된 상태도 취소)
        if (bgmSource != null) bgmSource.Stop();
        if (mvPlayer != null && mvPlayer.isPlaying) mvPlayer.Pause();
        
        if (pauseShowRoutine != null) StopCoroutine(pauseShowRoutine);
        pauseShowRoutine = StartCoroutine(ShowPauseUIRoutine());
    }
    
    public void ResumeGame()
    {
        if (currentState != GameState.Paused) return;
        StartCoroutine(RewindAndResumeRoutine());
    }

    private IEnumerator RewindAndResumeRoutine()
    {
        HidePauseUIImmediate();
        currentState = GameState.Rewinding;

        var activeNotes = noteObjectDict.Values.ToList();
        foreach (var obj in activeNotes)
        {
            ReturnNoteToPool(obj);
        }
        noteObjectDict.Clear();

        for(int i = 0; i < 8; i++) activeLongNote[i] = null;
        activeSlides.Clear();
        for(int i = 0; i < 4; i++) StopHoldEffect(i);

        foreach (var ml in activeMeasureLines) {
            ml.go.SetActive(false);
            measureLinePool.Enqueue(ml.go);
        }
        activeMeasureLines.Clear();

        double targetRewindTime = Math.Max(playStartTime, currentPlayTime - 1.0);
        double startRewindTime = currentPlayTime;
        float duration = 0.5f; 
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float ease = 1f - Mathf.Pow(1f - t, 3f); 

            currentPlayTime = Mathf.Lerp((float)startRewindTime, (float)targetRewindTime, ease);
            UpdateCurrentScrollDist();
            
                yield return null;
        }

        currentPlayTime = targetRewindTime;
        
        for (int L = 1; L <= 2; L++) {
            layerSpawnIndex[L] = 0;
            while (layerSpawnIndex[L] < notesByLayer[L].Count && notesByLayer[L][layerSpawnIndex[L]].HitTime < currentPlayTime) {
                layerSpawnIndex[L]++;
            }
        }
        for(int i = 0; i < 4; i++) {
            laneStartIndex[i] = 0;
            while(laneStartIndex[i] < notesByLane[i].Count && notesByLane[i][laneStartIndex[i]].HitTime < currentPlayTime) {
                laneStartIndex[i]++;
            }
        }

        measureSpawnIndex = 0;
        while(measureSpawnIndex < allMeasureTimes.Count && allMeasureTimes[measureSpawnIndex] < currentPlayTime) {
            measureSpawnIndex++;
        }

        currentState = GameState.Playing;
        
        // 다시 시작: 되감은 위치에서 음악을 '예약 재생'
        //  [버그 수정] 예전에는 bgmSource.time 으로 위치를 옮기고 바로 Play 했는데, 압축 음원은 위치 이동이 정확하지 않고
        //   재생 시작도 다음 오디오 버퍼까지 밀려서 일시정지 후 다시 시작할 때마다 싱크가 조금씩 어긋났다.
        double triggerTime = TriggerTime;
        if (bgmSource != null) bgmSource.Stop();
        isFullMusicStarted = false;
        musicScheduled = false;
        if (currentPlayTime >= triggerTime - musicScheduleLead)
        {
            ScheduleMusic(currentPlayTime);
            if (bgmSource != null && bgmSource.clip != null)
            {
                bgmSource.volume = 0f;
                StartCoroutine(FadeInAudioRoutine(1.0f));
            }
            if (currentPlayTime >= triggerTime) OnMusicStarted(currentPlayTime - triggerTime);
        }
    }

    private IEnumerator FadeInAudioRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(0f, SettingsStore.MusicVolume, elapsed / duration);
            yield return null;
        }
        bgmSource.volume = SettingsStore.MusicVolume;
    }

    public void RetryGame()
    {
        if (isLeaving) return;
        if (IsTestMode)
        {
            // GAME SETTING: 씬을 다시 불러오지 않고 그 자리에서 처음부터
            HidePauseUIImmediate();
            RestartTestLoop();
            currentState = GameState.Playing;
            return;
        }
        StartCoroutine(RetryRoutine());
    }

    public void BackToMenu()
    {
        if (isLeaving) return;
        isLeaving = true;
        StartCoroutine(BackToMenuRoutine());
    }

    private IEnumerator BackToMenuRoutine()
    {
        if (bgmSource != null) bgmSource.Stop();
        if (mvPlayer != null) mvPlayer.Stop();

        // 게임에 들어올 때의 '검은 막'만 따라함 (곡 정보 · 로딩 표시 없이): 막이 덮은 뒤 씬 이동, 곡 선택 씬이 준비되면 걷힘
        if (!CurtainTransition.IsActive && !SongIntroOverlay.IsActive)
        {
            HidePauseUIImmediate();
            CurtainTransition.Play(() => CoreManager.Instance.LoadSongSelectScene());
            yield break;
        }
        
        if (blackOverlayGroup != null)
        {
            blackOverlayGroup.gameObject.SetActive(true);
            blackOverlayGroup.transform.SetAsLastSibling(); 
            
            float duration = 1.0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                blackOverlayGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / duration);
                yield return null;
            }
        }
        
        CoreManager.Instance.LoadSongSelectScene();
    }

    // 끊김 진단: 플레이 중 한 프레임이 25ms 넘게 걸리면 콘솔에 남김 (GC = 메모리 정리가 그 프레임에 일어났는지)
    private int lastGcCount = -1;
    private void LogHitch()
    {
        int gc = System.GC.CollectionCount(0);
        bool gcRan = lastGcCount >= 0 && gc != lastGcCount;
        lastGcCount = gc;
        float dtMs = Time.unscaledDeltaTime * 1000f;
        if (currentState == GameState.Playing && dtMs > 25f)
            Debug.Log($"[Hitch] 프레임 {dtMs:0}ms 걸림 (곡 {currentPlayTime:0.00}s){(gcRan ? "  · GC 발생" : "")}");
    }

    void Update() 
    {
        if (isLeaving) return;
        LogHitch();
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        
        var kb = Keyboard.current;
        if (kb != null && kb[Key.Escape].wasPressedThisFrame)
        {
            // GAME SETTING: 일시정지 없음 (패널의 [처음부터] · [나가기] 를 씀)
            if (currentState == GameState.Playing && !isFailed && !IsTestMode) PauseGame();
        }

        if (currentState != GameState.Playing) 
        {
            if (currentState != GameState.Rewinding) return;
        }
        
        if (currentState == GameState.Playing)
        {
            // [버그 수정] 노트 순간이동: 예전에는 한 프레임 진행을 0.05초로 잘라서, 한 번 끊기면(렉) 화면 시계가 음악보다
            //  뒤처졌다가 → 빨리 감기로 따라잡거나(노트가 빨라짐) 0.3초 넘게 벌어지면 한꺼번에 점프했다.
            //  지금은 실제 지난 시간만큼 진행(최대 0.25초)해서 끊긴 뒤에도 음악과 거의 어긋나지 않는다.
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.25f) dt = 0.25f; 

            double triggerTime = TriggerTime;

            if (isFailed)
            {
                // FAILED: 음악과 동기화하지 않고 점점 느려지며 멈춘다
                currentPlayTime += dt * failSpeed;
            }
            else if (!musicScheduled) 
            {
                // 음악 예약 전: 프레임 시간으로 흐르다가, 음악 시작 직전(musicScheduleLead 초 전)에
                // '정확히 triggerTime 에 소리가 나도록' dspTime 으로 재생을 예약한다.
                currentPlayTime += dt; 
                if (currentPlayTime >= triggerTime - musicScheduleLead) ScheduleMusic(currentPlayTime);
            }
            else 
            {
                if (bgmSource != null && bgmSource.clip != null && bgmSource.isPlaying) 
                {
                    CorrectAudioDrift();   // 실제 재생 위치(timeSamples)와 어긋나면 기준 시각을 아주 천천히 맞춤
                    // [버그 수정] dspTime 은 오디오 버퍼 단위(약 10~20ms)로 '계단식'으로만 바뀐다. 프레임이 높으면(400fps 등)
                    //  목표 시각이 몇 프레임 멈췄다 한 번에 뛰어서, 그걸 따라가는 노트가 떨리거나 순간이동했다.
                    //  → 버퍼 사이는 실제 흐른 시간으로 메워서 매끄러운 시계로 만든다 (SmoothDspTime)
                    double audioPlayedTime = SmoothDspTime() - dspStartTime;   // 예약 시각 전에는 음수
                    double targetVisualTime = triggerTime + audioPlayedTime;
                    double diff = targetVisualTime - currentPlayTime;
                    
                    // 화면 시계 = 실제 흐른 시간으로 진행 + 음악 시계와의 차이를 '시간 기준'으로 부드럽게 줄임
                    //  [순간이동 수정] 예전에는 차이가 작은 기준(0.06초)만 넘어도 바로 점프했다.
                    //   Windows 오디오 등은 dspTime 이 가끔 수십 ms 멈췄다 한꺼번에 가는데(오디오 스레드 끊김), 그때마다 노트가 순간이동했다.
                    //   지금: 0.25초 넘게 벌어진 경우(일시정지 직후 · 큰 끊김)에만 점프, 그 밖에는 약 0.12초에 걸쳐 매끄럽게 따라감
                    currentPlayTime += dt;
                    diff = targetVisualTime - currentPlayTime;
                    if (Math.Abs(diff) > 0.25) {
                        Debug.Log($"[Sync] 화면 시계를 음악에 바로 맞춤: {diff * 1000.0:+0;-0} ms");
                        currentPlayTime = targetVisualTime; 
                    } 
                    else {
                        double k = 1.0 - Math.Exp(-dt / 0.12);
                        currentPlayTime += diff * k;
                    }

                    if (CoreManager.Instance.IsMvOn && mvPlayer != null && mvPlayer.isPlaying) {
                        if (bgRenderer != null) bgRenderer.color = Color.white;
                    }
                }
                else 
                {
                    currentPlayTime += dt;
                }

                if (!isFullMusicStarted && currentPlayTime >= triggerTime) OnMusicStarted(currentPlayTime - triggerTime);
            }
        }

        SyncMv(Time.unscaledDeltaTime);

        ProcessUIEvents();

        UpdateCurrentScrollDist();
        double maxVisibleDist = 2000.0;

        // 층별로 각자의 스크롤 속도 기준으로 스폰 판단
        for (int L = 1; L <= 2; L++)
        {
            var layerNotes = notesByLayer[L];
            while (layerSpawnIndex[L] < layerNotes.Count)
            {
                NoteData targetData = layerNotes[layerSpawnIndex[L]];
                double targetDist = GetScrollDistance(targetData.HitTime, L);
                double distDiff = targetDist - currentScrollDistByLayer[L];

                if (targetData.HitTime <= currentPlayTime + SPAWN_LOOKAHEAD || distDiff <= maxVisibleDist)
                {
                    NoteObject noteObj = GetNoteFromPool();
                    noteObj.Initialize(targetData);     
                    noteObj.gameObject.SetActive(true); 
                    noteObjectDict[targetData] = noteObj;
                    layerSpawnIndex[L]++; 
                }
                else
                {
                    break;
                }
            }
        }

        while (measureSpawnIndex < allMeasureTimes.Count)
        {
            double targetTime = allMeasureTimes[measureSpawnIndex];
            double targetDist = GetScrollDistance(targetTime);
            double distDiff = targetDist - currentScrollDist;

            if (targetTime <= currentPlayTime + SPAWN_LOOKAHEAD || distDiff <= maxVisibleDist)
            {
                if (measureLinePool.Count > 0) {
                    GameObject ml = measureLinePool.Dequeue();
                    ml.SetActive(true);
                    activeMeasureLines.Add(new MeasureLineObj { go = ml, hitTime = targetTime });
                }
                measureSpawnIndex++;
            }
            else break;
        }

        for (int i = activeMeasureLines.Count - 1; i >= 0; i--)
        {
            var ml = activeMeasureLines[i];
            bool isHidden = IsNoteHiddenByFutureReverseScroll(currentPlayTime, ml.hitTime);

            if (isHidden) {
                ml.go.SetActive(false);
            } else {
                ml.go.SetActive(true);
                double mTargetDist = GetScrollDistance(ml.hitTime);
                float rawYPos = hitLineY + (float)(mTargetDist - currentScrollDist) * 0.01f;
                ml.go.transform.position = new Vector3(0f, rawYPos - 0.1f, 0f);
            }

            if (currentPlayTime - ml.hitTime > W_BAD + 0.5) {
                ml.go.SetActive(false);
                measureLinePool.Enqueue(ml.go);
                activeMeasureLines.RemoveAt(i);
            }
        }

        UpdateJudgmentAnim();

        if (currentState == GameState.Playing && !isFailed)
        {
            if (AutoPlayNow) HandleAutoPlay(currentPlayTime);
            else HandleInputs(currentPlayTime);
            // TEMPEST 게이지: 시간에 따라 줄고, 판정이 몇 초에 걸쳐 반영됨 → 매 프레임 다시 계산
            if (Gauge == GaugeKind.Tempest && !LifeInfinite) UpdateTempest();
            // STRICT · STRICT+: 상한 · 하한이 따라가는 진행도 (ONE+ 보호 중에는 멈춰 있다가, 끝나면 그 차이만큼 빠르게 따라잡음)
            UpdateCapProgress(Time.unscaledDeltaTime);
            // STRICT · STRICT+: LIFE 최대 상한이 곡 진행에 따라 내려오므로, 판정이 없어도 매 프레임 맞춰 줌
            if (HasLifeCap && !IsTestMode && life > LifeCap) { if (Gauge == GaugeKind.Tempest) tempestSettled -= life - LifeCap; life = LifeCap; }
            // STRICT+: 하한이 올라와 LIFE 와 같아지거나 넘으면 즉시 TRACK CRASH (ONE+ 가 있으면 한 번 막음)
            //  (ONE+ 보호 중에는 TRACK CRASH 없음 — 끝난 뒤 하한 아래면 그대로 TRACK CRASH)
            if (GameModes.Play.StrictPlus && !IsTestMode && !LifeInfinite && !isFailed && !ShieldActive && LifeFloor > 0.0 && life <= LifeFloor)
            {
                crashCause = GameMode.StrictPlus;   // TRACK CRASH 아래 원인 아이콘
                StartFail();
            }
            // CLUTCH: 위험할수록 판정 범위가 넓어짐 (최대 2.5배)
            if (GameModes.Play.Clutch && !IsTestMode) ApplyJudgeWindows(GameModes.ClutchScale(ClutchDanger()));
        }

        UpdateClearEffect();
        UpdateFinishCondition();

        if (comboAnimTimer > 0 && currentCombo > 0)
        {
            comboAnimTimer -= Time.deltaTime;
            float p = 1f - (comboAnimTimer / 1f); 
            
            float scale = 1f; 
            float yOffset = 0f;

            if (p < 0.1f) {
                yOffset = Mathf.Lerp(-20f, 15f, p / 0.1f);
            } 
            else if (p < 0.25f) {
                yOffset = Mathf.Lerp(15f, 0f, (p - 0.1f) / 0.15f);
            }
            
            float alpha = 1f;
            if (comboAnimTimer < 0.3f) alpha = comboAnimTimer / 0.3f;
            
            if (comboImageContainer != null) {
                comboImageContainer.localScale = initialComboScale * scale;
                comboImageContainer.anchoredPosition = initialComboPos + new Vector2(0, yOffset);
                foreach(var img in basicComboDigits) {
                    if (img.gameObject.activeSelf && img.sprite != null) 
                        img.color = new Color(1f, 1f, 1f, alpha);
                }
            }
            
            if (comboTitleImage != null) {
                comboTitleImage.rectTransform.localScale = initialComboTitleScale; 
                comboTitleImage.rectTransform.anchoredPosition = initialComboTitlePos; 
                comboTitleImage.color = new Color(1, 1, 1, alpha); 
            }
        }
        else
        {
            if (comboImageContainer != null) comboImageContainer.gameObject.SetActive(false);
            if (comboTitleImage != null) comboTitleImage.gameObject.SetActive(false);
        }
    }

    private void ProcessUIEvents()
    {
        if (uiEvents.Count == 0) return;     // [렉 수정] 매 프레임 LINQ · RemoveAll 를 돌지 않음
        bool any = false;
        for (int i = 0; i < uiEvents.Count; i++) if (currentPlayTime >= uiEvents[i].StartTime) { any = true; break; }
        if (!any) return;
        foreach (var e in uiEvents.Where(x => currentPlayTime >= x.StartTime))
        {
            if (e.TargetUI == "BasicUI" && basicUIContainer != null) {
                if (e.Action == "exposure") basicUIContainer.SetActive(true);
                CanvasGroup cg = basicUIContainer.GetComponent<CanvasGroup>();
                if (cg == null) cg = basicUIContainer.AddComponent<CanvasGroup>();
                StartCoroutine(FadeCanvasGroup(cg, e.Action == "exposure" ? 1f : 0f, (float)e.Duration, basicUIContainer));
            } 
        }
        uiEvents.RemoveAll(x => currentPlayTime >= x.StartTime);
    }

    IEnumerator FadeCanvasGroup(CanvasGroup cg, float targetAlpha, float duration, GameObject container) {
        if (!cg) yield break;
        float startAlpha = cg.alpha; float t = 0;
        while(t < duration) { t += Time.deltaTime; cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, t/duration); yield return null; }
        cg.alpha = targetAlpha;
        if (targetAlpha <= 0.01f && container != null) container.SetActive(false);
    }


    // ===== 층별 롱노트 슬롯 헬퍼 =====
    private int SlotBase(NoteData n) { return n.Layer == NoteLayer.Bottom ? 4 : 0; }
    private int CoveredLaneCount(NoteData n) { return Mathf.Max(1, n.Width); }

    // 레인별 일반 홀드 효과가 필요한가 (RED 롱노트는 RED 전용 효과를 쓰므로 제외)
    private bool LaneNeedsHoldEffect(int lane)
    {
        NoteData a = activeLongNote[lane], b = activeLongNote[lane + 4];
        bool redOnly = redFx != null;
        return (a != null && !(redOnly && a.IsRedNote)) || (b != null && !(redOnly && b.IsRedNote)) || SlideHoldsLane(lane);
    }

    private bool IsLaneHeldByLongNote(int lane)
    {
        return activeLongNote[lane] != null || activeLongNote[lane + 4] != null;
    }

    private void RegisterActiveLongNote(NoteData n, double tickInterval)
    {
        if (n.Slide != null) { RegisterSlide(n, tickInterval); return; }   // 슬라이드는 레인 슬롯 대신 따로 (UpdateSlides)
        int b = SlotBase(n);
        int cnt = CoveredLaneCount(n);
        for (int k = 0; k < cnt; k++) {
            int l = n.Lane + k;
            if (l >= 4) continue;
            activeLongNote[b + l] = n;
            nextLongNoteTickTime[b + l] = n.HitTime + tickInterval;
            if (!n.IsRedNote || redFx == null) StartHoldEffect(l); // RED 롱노트는 RED 전용 홀드 효과만
        }
        if (n.IsRedNote && redFx != null) redFx.StartHold(n);
    }

    private void ClearActiveLongNote(NoteData n)
    {
        int b = SlotBase(n);
        int cnt = CoveredLaneCount(n);
        for (int k = 0; k < cnt; k++) {
            int l = n.Lane + k;
            if (l >= 4) continue;
            if (activeLongNote[b + l] == n) activeLongNote[b + l] = null;
            if (!LaneNeedsHoldEffect(l)) StopHoldEffect(l);
        }
        if (redFx != null) redFx.StopHold(n);
    }

    private bool IsLongNoteHeld(NoteData ln)
    {
        if (autoHeld.Contains(ln)) return true;   // LAZY 로 살아난 롱노트: 끝까지 자동으로 눌림
        int cnt = CoveredLaneCount(ln);
        for (int k = 0; k < cnt; k++) {
            int l = ln.Lane + k;
            if (l < 4 && prevKeyState[l]) return true;
        }
        return false;
    }

    private void SetLongNoteReleaseTime(NoteData ln, double time)
    {
        int b = SlotBase(ln);
        int cnt = CoveredLaneCount(ln);
        for (int k = 0; k < cnt; k++) {
            int l = ln.Lane + k;
            if (l < 4) lnReleaseTime[b + l] = time;
        }
    }

    private void HandleAutoPlay(double T_sys)
    {
        for (int i = 0; i < 4; i++)
        {
            var laneNotes = notesByLane[i];
            while (laneStartIndex[i] < laneNotes.Count)
            {
                var n = laneNotes[laneStartIndex[i]];
                if (n.IsHit || n.Type == 4) { laneStartIndex[i]++; continue; }

                if (n.HitTime <= T_sys)
                {
                    n.IsHit = true;
                    
                    bool isNormalNote = NoteTypes.IsTapLike(n.Type);
                    bool isTail = (n.Type == 3 || n.Type == 13);
                    
                    // AUTO PLAY: 들쑥날쑥하지 않고 모든 노트를 정확히 0ms 로 판정
                    if (isNormalNote) ApplyJudgment(0.0);
                    else ProcessHit("EXCELLENT", 0.0, true);
                    
                    if (n.IsRedNote) {
                        // RED 노트: 레인별 일반 히트 효과 대신 노트 폭만큼의 RED 전용 효과 하나
                        if (redFx != null) redFx.Tap(n); else for (int w = 0; w < n.Width; w++) if (n.Lane + w < 4) SpawnHitEffect(n.Lane + w, n);
                        for (int w = 0; w < n.Width; w++) {
                            int l = n.Lane + w;
                            if (l < 4) StartCoroutine(AutoPlayLaneVisual(l, isTail));
                        }
                    } else {
                        // 넓은 일반 노트는 덮는 레인 모두에 효과 (1칸이면 기존과 같음)
                        for (int w = 0; w < Mathf.Max(1, n.Width); w++) {
                            int l = n.Lane + w;
                            if (l < 4) { SpawnHitEffect(l, n); StartCoroutine(AutoPlayLaneVisual(l, isTail)); }
                        }
                    }

                    if (NoteTypes.IsLongHead(n.Type))
                    {
                        RegisterActiveLongNote(n, GetTickInterval(n.HitTime));
                    }
                    laneStartIndex[i]++;
                }
                else break;
            }
        }

        // 진행 중인 롱노트(1층/2층 슬롯 8개) 처리
        for (int s = 0; s < 8; s++)
        {
            NoteData ln = activeLongNote[s];
            if (ln == null) continue;
            int lane = s % 4;

            if (ln.Lane == lane) ProcessLongNoteTick(s, T_sys);   // 넓은 롱노트는 기준 레인 슬롯에서만 틱

            if (T_sys >= GetLongNoteEndTime(ln)) FinishLongNote(ln);
        }

        UpdateSlides(T_sys, true);    // 슬라이드: AUTO PLAY 는 항상 눌린 것으로
    }

    // 탭 사운드 (설정 > SOUND 의 ON/OFF · 볼륨 반영)
    private void PlayHitSound()
    {
        SettingsStore.PlayHitSound(sfxSource, hitSoundClip);
    }

    private IEnumerator AutoPlayLaneVisual(int lane, bool skipSound = false)
    {
        if (beamRenderers != null && beamRenderers[lane] != null && onSprites != null) beamRenderers[lane].sprite = onSprites[lane];
        if (whiteLights != null && whiteLights[lane] != null) whiteLights[lane].SetActive(true);
        if (simpleSkin != null) simpleSkin.SetPressed(lane, true);
        if (!skipSound) PlayHitSound();

        yield return new WaitForSeconds(0.05f);

        if (!IsLaneHeldByLongNote(lane)) {
            if (beamRenderers != null && beamRenderers[lane] != null && offSprites != null) beamRenderers[lane].sprite = offSprites[lane];
            if (whiteLights != null && whiteLights[lane] != null) whiteLights[lane].SetActive(false);
            if (simpleSkin != null) simpleSkin.SetPressed(lane, false);
        }
    }

    private void HandleInputs(double T_sys)
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // 마지막 노트까지 처리한 뒤(클리어 확인)에는 입력을 받지 않음 — 키 빔 · 탭음 · 판정 모두 없음
        //  (그 순간 눌려 있던 키는 한 번만 뗀 상태로 되돌림)
        if (clearChecked)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!prevKeyState[i]) continue;
                prevKeyState[i] = false;
                if (beamRenderers != null && beamRenderers[i] != null && offSprites != null) beamRenderers[i].sprite = offSprites[i];
                if (whiteLights != null && whiteLights[i] != null) whiteLights[i].SetActive(false);
                if (simpleSkin != null) simpleSkin.SetPressed(i, false);
                StopHoldEffect(i);
            }
            return;
        }

        for (int i = 0; i < 4; i++) 
        {
            Key targetKey = laneKeys[i];
            bool isCurrentlyPressed = kb[targetKey].isPressed;

            bool wasPressed = isCurrentlyPressed && !prevKeyState[i];
            bool wasReleased = !isCurrentlyPressed && prevKeyState[i];
            
            prevKeyState[i] = isCurrentlyPressed;

            // 키를 실제로 누른/뗀 시각 (프레임 시각이 아니라 입력 이벤트 시각, InputTiming 참고)
            //  [버그 수정] 예전에는 항상 '이번 프레임 시각'으로 판정해서 평균 반 프레임 늦게, FPS 에 따라 흔들리게 판정됐다.

            if (wasReleased) 
            {
                double releaseT = T_sys - InputTiming.ReleaseAge(targetKey);
                if (beamRenderers != null && beamRenderers[i] != null && offSprites != null) beamRenderers[i].sprite = offSprites[i];
                if (whiteLights != null && whiteLights[i] != null) whiteLights[i].SetActive(false);
                if (simpleSkin != null) simpleSkin.SetPressed(i, false);
                if (!SlideHoldsLane(i)) StopHoldEffect(i);   // 슬라이드가 지나가는 레인이면 (다른 키로 옮겨 잡는 중) 효과 유지
                
                // 이 레인의 2층/1층 롱노트를 각각 검사
                for (int layerSlot = 0; layerSlot < 2; layerSlot++)
                {
                    NoteData ln = activeLongNote[i + layerSlot * 4];
                    if (ln == null) continue;
                    if (IsLongNoteHeld(ln)) continue; // 와이드 빨간 노트: 다른 레인 키가 아직 눌려 있음

                    double endTime = GetLongNoteEndTime(ln);
                    double releaseJudge = releaseT - SettingsStore.JudgeDelay;
                    if (endTime - releaseJudge <= 0.15) FinishLongNote(ln, SignedMs(Math.Max(0.0, endTime - releaseJudge)));
                    else SetLongNoteReleaseTime(ln, T_sys);
                }
            }

            if (wasPressed) 
            {
                if (beamRenderers != null && beamRenderers[i] != null && onSprites != null) beamRenderers[i].sprite = onSprites[i];
                if (whiteLights != null && whiteLights[i] != null) whiteLights[i].SetActive(true);
                if (simpleSkin != null) simpleSkin.SetPressed(i, true);
                PlayHitSound();
                
                EvaluateHit(i, T_sys - InputTiming.PressAge(targetKey));
                
                if (LaneNeedsHoldEffect(i)) StartHoldEffect(i);
            }
        }

        // 드래그 노트 (1): 판정선에 닿을 때 그 레인 키가 눌려 있으면 성공
        CheckDragHolds(T_sys - SettingsStore.JudgeDelay);
        
        for (int s = 0; s < 8; s++)
        {
            NoteData ln = activeLongNote[s];
            if (ln == null) continue;

            int lane = s % 4;
            double endTime = GetLongNoteEndTime(ln);
            bool isHeld = IsLongNoteHeld(ln);

            if (isHeld) 
            {
                if (ln.Lane == lane) ProcessLongNoteTick(s, T_sys);   // 넓은 롱노트는 기준 레인 슬롯에서만 틱
                SetLongNoteReleaseTime(ln, 0);
            }
            else if (T_sys - lnReleaseTime[s] > 0.2) 
            {
                FailLongNote(ln);
                continue;
            }

            if (activeLongNote[s] == ln && T_sys >= endTime) FinishLongNote(ln);
        }

        UpdateSlides(T_sys, false);   // 슬라이드 (레인을 넘나드는 롱노트)
    }

    private void FinishLongNote(NoteData head, double tailErrorMs = 0.0)
    {
        // 끝나기 직전(0.15초 이내)에 손을 떼서 완료 처리된 경우, 남은 틱을 마저 정산한다 (풀콤보가 깨지지 않도록)
        int tickSlot = SlotBase(head) + head.Lane;
        if (head.Lane >= 0 && head.Lane < 4 && activeLongNote[tickSlot] == head)
            ProcessLongNoteTick(tickSlot, GetLongNoteEndTime(head));

        NoteData tailNote = head.NextLongNote;
        if (tailNote != null && !tailNote.IsHit) {
            tailNote.IsHit = true; 
            ProcessHit("EXCELLENT", tailErrorMs, true); 
            
            int cnt = CoveredLaneCount(head);
            if (head.IsRedNote && redFx != null) redFx.Tap(head); // RED: 노트 폭만큼의 전용 효과 하나
            else
                for (int w = 0; w < cnt; w++) {
                    if (head.Lane + w < 4) SpawnHitEffect(head.Lane + w, tailNote);
                }
            HideNoteObject(tailNote);
        }
        ClearActiveLongNote(head);
    }


    private void UpdateJudgmentAnim()
    {
        if (judgmentAnimTimer > 0)
        {
            judgmentAnimTimer -= Time.deltaTime;
            if (judgmentAnimTimer <= 0) judgmentImage.gameObject.SetActive(false);
            else
            {
                float p = 1f - (judgmentAnimTimer / 0.45f); 
                float scale = judgmentImageScale; 
                
                if (p < 0.15f) scale *= Mathf.Lerp(0.4f, 1.3f, p / 0.15f); 
                else if (p < 0.3f) scale *= Mathf.Lerp(1.3f, 1.0f, (p - 0.15f) / 0.15f); 
                else scale *= 1.0f;

                judgmentImage.transform.localScale = Vector3.one * scale;

                float alpha = 1f;
                if (p > 0.7f) alpha = Mathf.Lerp(1f, 0f, (p - 0.7f) / 0.3f);
                judgmentImage.color = new Color(1, 1, 1, alpha);
            }
        }

        if (fastSlowAnimTimer > 0)
        {
            fastSlowAnimTimer -= Time.deltaTime;
            if (fastSlowAnimTimer <= 0)
            {
                if (fastSlowDirectionText != null) fastSlowDirectionText.gameObject.SetActive(false);
                if (fastSlowValueText != null) fastSlowValueText.gameObject.SetActive(false);
            }
            else
            {
                float p = fastSlowAnimTimer / 0.45f;
                if (fastSlowDirectionText != null) fastSlowDirectionText.color = new Color(fastSlowDirectionText.color.r, fastSlowDirectionText.color.g, fastSlowDirectionText.color.b, p);
                if (fastSlowValueText != null) fastSlowValueText.color = new Color(1f, 1f, 1f, p);
            }
        }
    }

    private void EvaluateHit(int lane, double hitTimestamp) 
    {
        double adjustedHitTime = hitTimestamp - SettingsStore.JudgeDelay;
        NoteData targetNote = null, dragNote = null;
        double minAbsDiff = double.MaxValue, dragAbsDiff = double.MaxValue;
        int startIndex = laneStartIndex[lane];
        var currentLaneNotes = notesByLane[lane];

        for (int i = startIndex; i < currentLaneNotes.Count; i++) 
        {
            var n = currentLaneNotes[i];
            if (n.IsHit) 
            {
                if (i == laneStartIndex[lane]) laneStartIndex[lane]++;
                continue;
            }
            if (!NoteTypes.IsTapLike(n.Type) && !NoteTypes.IsLongHead(n.Type)) continue;

            double timeDiff = n.HitTime - adjustedHitTime;
            if (timeDiff > W_BAD) break; 
            if (timeDiff < -W_BAD) continue; 

            double absDiff = Math.Abs(timeDiff);
            if (NoteTypes.IsDrag(n.Type)) {
                // 드래그 노트는 따로 모음 (일반 노트를 치려고 누른 키를 드래그 노트가 가로채지 않도록)
                if (absDiff <= W_GREAT && absDiff < dragAbsDiff) { dragAbsDiff = absDiff; dragNote = n; }
                continue;
            }
            if (absDiff < minAbsDiff) {
                minAbsDiff = absDiff;
                targetNote = n;
            }
        }

        // 드래그 노트 (2): 일반 노트처럼 판정선에 닿을 때 키를 누름 → 성공 (칠 일반 노트가 없을 때만)
        if (targetNote == null) {
            if (dragNote != null) HitDragNote(dragNote, false);
            else CheckFakeHit(lane, adjustedHitTime);   // FAKE: 가짜 노트를 쳤는가
            return;
        }
            
        targetNote.IsHit = true; 
        ApplyJudgment(targetNote.HitTime - adjustedHitTime);
        
        if (targetNote.IsRedNote) {
            // RED 노트: 레인별 일반 히트 효과 대신 노트 폭만큼의 RED 전용 효과 하나
            if (redFx != null) redFx.Tap(targetNote);
            else
                for (int w = 0; w < targetNote.Width; w++) {
                    if (targetNote.Lane + w < 4) SpawnHitEffect(targetNote.Lane + w, targetNote);
                }
        } else if (targetNote.Width > 1) {
            // 넓은 일반 노트: 덮는 레인 모두에 효과
            for (int w = 0; w < targetNote.Width; w++) if (targetNote.Lane + w < 4) SpawnHitEffect(targetNote.Lane + w, targetNote);
        } else {
            SpawnHitEffect(lane, targetNote); 
        }

        if (NoteTypes.IsLongHead(targetNote.Type))
        {
            RegisterActiveLongNote(targetNote, GetTickInterval(targetNote.HitTime));
        }
    }

    // =====================================================================
    //  드래그 노트 (9 · 10)
    //   판정은 JUST / MISS 두 가지뿐
    //   (1) 판정선에 닿을 때 그 레인(넓으면 덮는 레인 중 하나) 키가 눌려 있으면 성공
    //   (2) 일반 노트처럼 판정선 근처(±GREAT 범위)에서 키를 누르면 성공
    //   판정선을 지나 BAD 범위가 끝날 때까지 둘 다 안 되면 MISS (NoteObject 가 처리)
    //   10 (드래그로 시작하는 롱노트): 시작 판정만 드래그 규칙, 그 뒤는 일반 롱노트와 같음
    // =====================================================================
    private void CheckDragHolds(double judgeNow)
    {
        for (int lane = 0; lane < 4; lane++)
        {
            if (!prevKeyState[lane]) continue;
            var laneNotes = notesByLane[lane];
            for (int i = laneStartIndex[lane]; i < laneNotes.Count; i++)
            {
                var n = laneNotes[i];
                if (n.HitTime > judgeNow) break;                  // 아직 판정선에 닿지 않음
                if (n.IsHit || !NoteTypes.IsDrag(n.Type)) continue;
                if (judgeNow - n.HitTime > W_BAD) continue;        // 이미 놓친 노트
                HitDragNote(n, true);
            }
        }
    }

    private void HitDragNote(NoteData n, bool byHold)
    {
        if (n == null || n.IsHit) return;
        n.IsHit = true;
        ProcessHit("EXCELLENT", 0.0, true);                         // 항상 JUST (오차 0ms)
        if (hitErrorMeter != null) hitErrorMeter.Add(0.0, 0);
        if (byHold) PlayHitSound();                                 // 누르고 있다가 받은 경우에도 탭음 (누른 경우는 누를 때 이미 남)
        int cnt = Mathf.Max(1, n.Width);
        for (int w = 0; w < cnt; w++) if (n.Lane + w < 4) SpawnHitEffect(n.Lane + w, n);
        if (NoteTypes.IsLongHead(n.Type))
        {
            RegisterActiveLongNote(n, GetTickInterval(n.HitTime));
            for (int w = 0; w < cnt; w++) { int l = n.Lane + w; if (l < 4 && prevKeyState[l] && LaneNeedsHoldEffect(l)) StartHoldEffect(l); }
        }
    }

    private void ProcessLongNoteTick(int slot, double T_sys)
    {
        var head = activeLongNote[slot];
        if (head == null) return;

        double endTime = GetLongNoteEndTime(head);
        
        while (T_sys >= nextLongNoteTickTime[slot] && nextLongNoteTickTime[slot] < endTime - LongNoteTickRule.EndMargin)
        {
            RegisterTickExcellent();
            nextLongNoteTickTime[slot] += GetTickInterval(nextLongNoteTickTime[slot]);
        }
    }

    // 롱노트 중간 틱 = EXCELLENT 판정 (콤보 +1, 정확도·점수·EXCELLENT 수에 포함, 판정 이미지/오차는 표시하지 않음)
    private void RegisterTickExcellent()
    {
        if (isFailed) return;
        processedNotes++;
        bool locked = ScoreLocked;       // COMBO BREAK: 콤보가 끊긴 뒤 잠깐은 점수 없음
        if (!locked) currentAccSum += 1f;
        cntExcellent++;
        cntJust++;                       // 롱노트 중간 판정 = 항상 JUST
        accNotes++; accPoints += 1.0;    // TP 에도 100% 로 들어감
        currentCombo++;
        if (currentCombo > maxCombo) maxCombo = currentCombo;
        if (!locked) comboSum += currentCombo - 1;
        comboAnimTimer = 1f;
        UpdateUI();
        ApplyLife("EXCELLENT", true, true);
        RecordJudge("EXCELLENT");
    }

    // 틱 간격 / 틱 수: DataModels 의 LongNoteTickRule (에디터 저장 시 노트 수 계산과 같은 규칙)
    private double GetTickInterval(double time)
    {
        return LongNoteTickRule.Interval(GetBpmAtTime(time));
    }

    private int CountLongNoteTicks(NoteData head)
    {
        return LongNoteTickRule.Count(head.HitTime, GetLongNoteEndTime(head), GetBpmAtTime);
    }

    // 놓친 롱노트: 회색으로 남기지 않고, 레인을 따라 그대로 흘러가며 서서히 투명해져 사라진다
    public void DimEntireLongNoteChain(NoteData head)
    {
        if (head.Slide != null)
            foreach (var sp in head.Slide.Points)
                if (sp.Note != head && sp.Note != head.NextLongNote)
                {
                    sp.Note.IsHit = true;
                    if (noteObjectDict.TryGetValue(sp.Note, out NoteObject ro) && ro != null) ro.FadeOutMissed();
                }
        NoteData curr = head;
        while (curr != null) 
        {
            curr.IsHit = true; 
            if (noteObjectDict.TryGetValue(curr, out NoteObject obj) && obj != null) obj.FadeOutMissed();
            curr = curr.NextLongNote;
        }
    }

    private void HideNoteObject(NoteData data)
    {
        if (noteObjectDict.TryGetValue(data, out NoteObject obj) && obj != null) obj.HideNote();
    }

    // 롱노트를 중간에 놓침: 아직 지나지 않은 틱 + 끝 판정(꼬리)을 모두 MISS 로 셈
    //  (판정 수 합계 = 전체 노트 수가 되도록. 판정 글자는 꼬리 MISS 한 번만 표시)
    private void FailLongNote(NoteData ln)
    {
        int slot = SlotBase(ln) + ln.Lane;
        double from = (ln.Lane >= 0 && ln.Lane < 4 && activeLongNote[slot] == ln) ? nextLongNoteTickTime[slot] : currentPlayTime;
        int remaining = CountTicksFrom(from, GetLongNoteEndTime(ln));
        for (int i = 0; i < remaining; i++) RegisterTickMiss();
        MissSlideRelays(ln);
        if (ln.NextLongNote != null && !ln.NextLongNote.IsHit) ProcessHit("MISS"); // 꼬리
        DimEntireLongNoteChain(ln);
        ClearActiveLongNote(ln);
    }

    // 롱노트 머리를 놓침: 머리 + 모든 틱 + 꼬리를 MISS 로 셈
    public void MissLongNoteHead(NoteData headNote)
    {
        ProcessHit("MISS"); // 머리
        if (headNote.NextLongNote != null && !headNote.NextLongNote.IsHit)
        {
            int ticks = CountLongNoteTicks(headNote);
            for (int i = 0; i < ticks; i++) RegisterTickMiss();
            MissSlideRelays(headNote);   // 슬라이드: 중계점들
            ProcessHit("MISS"); // 꼬리
        }
        DimEntireLongNoteChain(headNote);
    }

    // time 부터 끝까지 남은 틱 수 (ProcessLongNoteTick 과 같은 규칙)
    private int CountTicksFrom(double time, double endTime)
    {
        int count = 0;
        double t = time;
        while (t < endTime - LongNoteTickRule.EndMargin && count < 100000)
        {
            count++;
            t += GetTickInterval(t);
        }
        return count;
    }

    // 놓친 롱노트 틱 = MISS (콤보 끊김, MISS 수 · 정확도에 포함, LIFE 감소는 일반 MISS 의 10%, 판정 글자는 표시하지 않음)
    private void RegisterTickMiss()
    {
        if (isFailed) return;
        processedNotes++;
        accNotes++;                      // 놓친 틱은 TP 0%
        cntMiss++;
        if (currentCombo > 0) OnComboBroken();
        currentCombo = 0;
        UpdateUI();
        ApplyLife("MISS", true, false);
        RecordJudge("MISS");
        CheckModeCrash("MISS", false);
    }

    private double GetLongNoteEndTime(NoteData head) { return head.NextLongNote != null ? head.NextLongNote.HitTime : head.HitTime; }

    // =====================================================================
    //  슬라이드 판정 (레인을 넘나드는 롱노트)
    //   판정선에 닿은 지점이 덮는 레인 키 중 하나를 누르고 있으면 '눌림'
    //    (옮겨 잡는 순간을 봐주려고 ±SlideGrace 초 앞뒤에 덮는 레인도 인정)
    //   눌린 동안: 틱(JUST) · 중계점(지나는 순간 JUST + 효과)
    //   0.2초 넘게 안 눌리면 끊김 → 남은 틱 · 중계점 · 끝 MISS (끝나기 0.15초 전부터는 떼도 완료)
    //   끝 시각이 되면 끝 판정 JUST
    // =====================================================================
    private const double SlideGrace = 0.08;
    private class ActiveSlide
    {
        public NoteData Head;
        public double NextTick;
        public double ReleaseTime;          // 0 = 눌림, 그 밖 = 손을 뗀 시각
        public int NextRelay = 1;           // 다음에 판정할 중계점 (Points 의 번호)
        public readonly bool[] Fx = new bool[4];   // 홀드 효과를 켠 레인
    }
    private readonly List<ActiveSlide> activeSlides = new List<ActiveSlide>();
    private readonly bool[] slideLanes = new bool[4];

    private void RegisterSlide(NoteData head, double tickInterval)
    {
        foreach (var a in activeSlides) if (a.Head == head) return;
        activeSlides.Add(new ActiveSlide { Head = head, NextTick = head.HitTime + tickInterval });
    }

    private bool SlideHoldsLane(int lane)
    {
        foreach (var a in activeSlides) if (a.Fx[lane]) return true;
        return false;
    }

    private void UpdateSlides(double T_sys, bool auto)
    {
        if (activeSlides.Count == 0) return;
        double t = T_sys - SettingsStore.JudgeDelay;                 // 판정 기준 시각
        for (int i = activeSlides.Count - 1; i >= 0; i--)
        {
            if (i >= activeSlides.Count) continue;
            ActiveSlide a = activeSlides[i];
            SlidePath path = a.Head.Slide;
            double end = path.EndTime;

            // 지금 (앞뒤 조금 포함) 덮는 레인 중 하나라도 눌려 있는가
            Array.Clear(slideLanes, 0, 4);
            path.CoveredLanes(t - SlideGrace, slideLanes);
            path.CoveredLanes(t, slideLanes);
            path.CoveredLanes(t + SlideGrace, slideLanes);
            bool held = auto || autoHeld.Contains(a.Head);
            for (int l = 0; l < 4 && !held; l++) if (slideLanes[l] && prevKeyState[l]) held = true;

            if (held)
            {
                a.ReleaseTime = 0;
                while (T_sys >= a.NextTick && a.NextTick < end - LongNoteTickRule.EndMargin)
                {
                    RegisterTickExcellent();
                    a.NextTick += GetTickInterval(a.NextTick);
                }
                while (a.NextRelay < path.Points.Count - 1 && t >= path.Points[a.NextRelay].Time)
                {
                    HitSlideRelay(path.Points[a.NextRelay].Note);
                    a.NextRelay++;
                }
            }
            else
            {
                if (a.ReleaseTime <= 0) a.ReleaseTime = T_sys;
                if (end - t <= 0.15) { FinishSlide(a); continue; }           // 끝나기 직전에 뗌 → 완료
                if (T_sys - a.ReleaseTime > 0.2) { FailSlide(a); continue; }  // 끊김
            }

            UpdateSlideFx(a, t, held);
            if (T_sys >= end) FinishSlide(a);
        }
    }

    // 홀드 효과: 지금 덮는 레인에만 (눌린 동안)
    private void UpdateSlideFx(ActiveSlide a, double t, bool held)
    {
        Array.Clear(slideLanes, 0, 4);
        if (held) a.Head.Slide.CoveredLanes(t, slideLanes);
        for (int l = 0; l < 4; l++)
        {
            if (slideLanes[l] == a.Fx[l]) continue;
            a.Fx[l] = slideLanes[l];
            if (a.Fx[l]) StartHoldEffect(l);
            else if (!LaneNeedsHoldEffect(l)) StopHoldEffect(l);
        }
    }

    private void HitSlideRelay(NoteData relay)
    {
        if (relay == null || relay.IsHit) return;
        relay.IsHit = true;
        ProcessHit("EXCELLENT", 0.0, true);                         // 중계점 = JUST
        for (int w = 0; w < Mathf.Max(1, relay.Width); w++) if (relay.Lane + w < 4) SpawnHitEffect(relay.Lane + w, relay);
        HideNoteObject(relay);
    }

    private void MissSlideRelays(NoteData head)
    {
        if (head == null || head.Slide == null) return;
        foreach (var sp in head.Slide.Points)
            if (sp.Note != head && sp.Note != head.NextLongNote && !sp.Note.IsHit) { sp.Note.IsHit = true; ProcessHit("MISS"); }
    }

    private void FinishSlide(ActiveSlide a)
    {
        if (!activeSlides.Contains(a)) return;
        NoteData head = a.Head;
        double end = head.Slide.EndTime;
        while (a.NextTick < end - LongNoteTickRule.EndMargin)       // 남은 틱 정산 (끝 직전에 뗀 경우)
        {
            RegisterTickExcellent();
            a.NextTick += GetTickInterval(a.NextTick);
        }
        for (int k = a.NextRelay; k < head.Slide.Points.Count - 1; k++) HitSlideRelay(head.Slide.Points[k].Note);
        NoteData tail = head.NextLongNote;
        if (tail != null && !tail.IsHit)
        {
            tail.IsHit = true;
            ProcessHit("EXCELLENT", 0.0, true);
            for (int w = 0; w < Mathf.Max(1, tail.Width); w++) if (tail.Lane + w < 4) SpawnHitEffect(tail.Lane + w, tail);
            HideNoteObject(tail);
        }
        RemoveSlide(a);
    }

    private void FailSlide(ActiveSlide a)
    {
        if (!activeSlides.Contains(a)) return;
        NoteData head = a.Head;
        int remaining = CountTicksFrom(a.NextTick, head.Slide.EndTime);
        for (int i = 0; i < remaining; i++) RegisterTickMiss();
        MissSlideRelays(head);
        if (head.NextLongNote != null && !head.NextLongNote.IsHit) ProcessHit("MISS");   // 끝
        DimEntireLongNoteChain(head);
        RemoveSlide(a);
    }

    private void RemoveSlide(ActiveSlide a)
    {
        activeSlides.Remove(a);
        for (int l = 0; l < 4; l++)
            if (a.Fx[l]) { a.Fx[l] = false; if (!LaneNeedsHoldEffect(l)) StopHoldEffect(l); }
    }

    private void ApplyJudgment(double diff) 
    {
        double absDiff = Math.Abs(diff);
        int msDiff = (int)(absDiff * 1000.0);

        double signedMs = SignedMs(diff);
        if (IsTestMode && !AutoPlayNow && Math.Abs(diff) <= W_BAD) calibErrors.Add((float)signedMs);   // GAME SETTING 자동 맞춤용 (직접 친 탭 · 롱노트 시작만)
        if (absDiff <= W_EXCELLENT) ProcessHit("EXCELLENT", signedMs, true);
        else if (absDiff <= W_GREAT) ProcessHit("GREAT", signedMs, true);
        else if (absDiff <= W_GOOD) ProcessHit("GOOD", signedMs, true);
        else ProcessHit("BAD", signedMs, true);
        if (hitErrorMeter != null) hitErrorMeter.Add(signedMs, absDiff <= W_EXCELLENT ? 0 : absDiff <= W_GREAT ? 1 : absDiff <= W_GOOD ? 2 : 3);

        if (CoreManager.Instance.CurrentSettings.ShowFastSlow && msDiff > 0 && msDiff >= CoreManager.Instance.CurrentSettings.judgeoverrange && absDiff <= W_BAD)   // 0ms(AUTO PLAY 등)는 표시 안 함
        {
            if (simpleSkin != null) simpleSkin.OnFastSlow(diff > 0, msDiff);
            if (fastSlowDirectionText != null && fastSlowValueText != null)
            {
                // 표기: FAST 26 → "-26ms",  SLOW 43 → "+43ms"
                bool isFast = diff > 0;
                fastSlowDirectionText.text = (isFast ? "-" : "+") + msDiff + "ms";
                fastSlowDirectionText.color = isFast ? new Color(0.53f, 0.81f, 0.92f) : Color.red; 
                fastSlowValueText.text = "";
                
                fastSlowDirectionText.gameObject.SetActive(true);
                fastSlowValueText.gameObject.SetActive(false);
                fastSlowAnimTimer = 0.45f;
            }
        }
    }

    public void ProcessHit(string judge, double signedErrorMs = 0.0, bool hasTiming = false)
    {
        if (isFailed) return; // FAILED 이후의 판정은 무시
        // 게임 모드 EASY JUDGEMENT: BAD 범위 안이면 모두 JUST (MISS 는 그대로)
        bool easyJust = false;
        if (GameModes.Play.Easy && (judge == "EXCELLENT" || judge == "GREAT" || judge == "GOOD" || judge == "BAD")) { judge = "EXCELLENT"; easyJust = true; }
        // 게임 모드 MERCY: BAD · MISS 를 MercyChance(50%) 확률로 GREAT 이상으로 바꿈
        //  바뀐 판정: GREAT 50% · PERFECT(JUST 아님) 35% · JUST 15%. 바뀐 판정의 오차는 오차 기록(평균 · FAST/SLOW)에 넣지 않음
        bool mercied = false;
        if ((judge == "MISS" || judge == "BAD") && GameModes.Play.Mercy && !IsTestMode && uiRng.NextDouble() < GameModes.MercyChance)
        {
            mercied = true;
            if (simpleSkin != null) simpleSkin.FlashFloor(SimpleGameSkin.SaveFlashColor);   // 레인 아래 빛이 초록으로 번쩍
            double roll = uiRng.NextDouble();
            if (roll < GameModes.MercyGreat) { judge = "GREAT"; signedErrorMs = 0.0; hasTiming = false; }
            else if (roll < GameModes.MercyGreat + GameModes.MercyPerfect)
            {
                // JUST 가 아닌 PERFECT: JUST 범위와 PERFECT 범위 사이의 오차로 처리 (TP 도 그만큼)
                judge = "EXCELLENT";
                double edge = W_EXCELLENT * 1000.0;
                if (edge > JustWindowMs + 1.0)
                {
                    double mag = JustWindowMs + 1.0 + uiRng.NextDouble() * (edge - JustWindowMs - 1.0);
                    signedErrorMs = uiRng.Next(2) == 0 ? -mag : mag;
                    hasTiming = true;
                }
                else { signedErrorMs = 0.0; hasTiming = false; }   // PERFECT 범위가 너무 좁으면(ULTIMATE) JUST 로
            }
            else { judge = "EXCELLENT"; signedErrorMs = 0.0; hasTiming = false; }   // JUST
        }
        processedNotes++;
        float accMultiplier = 0f;
        int comboBefore = currentCombo;

        switch (judge) {
            case "EXCELLENT": accMultiplier = 1f; currentCombo++; break;
            case "GREAT": accMultiplier = 0.5f; currentCombo++; break;
            case "GOOD": accMultiplier = 0.3f; currentCombo++; break;
            case "BAD": accMultiplier = 0.05f; currentCombo = 0; break;
            case "MISS": accMultiplier = 0f; currentCombo = 0; break;
        }
        if (currentCombo > maxCombo) maxCombo = currentCombo; 
        if (comboBefore > 0 && currentCombo == 0) OnComboBroken();
        bool locked = ScoreLocked;   // COMBO BREAK: 콤보가 끊긴 뒤 3초 동안은 판정 · 콤보 점수 모두 0
        if (currentCombo > 0 && !locked) comboSum += currentCombo - 1;

        if (!locked) currentAccSum += accMultiplier;
        accNotes++;
        accPoints += easyJust ? 1.0 : AccValue(judge, signedErrorMs, hasTiming);
        bool just = easyJust || IsJust(judge, signedErrorMs, hasTiming);
        if (just) cntJust++;

        switch (judge) {
            case "EXCELLENT": cntExcellent++; break;
            case "GREAT": cntGreat++; break;
            case "GOOD": cntGood++; break;
            case "BAD": cntBad++; break;
            case "MISS": cntMiss++; break;
        }
        if (hasTiming && judge != "MISS" && !mercied)
        {
            errorSumMs += signedErrorMs; timedCount++;
            recErrors.Add((float)signedErrorMs);
            if (judge != "EXCELLENT")
            {
                if (signedErrorMs < 0) fastCount++;
                else if (signedErrorMs > 0) slowCount++;
            }
        }

        UpdateUI();
        string shown = just && SettingsStore.Current.ShowJustPerfect ? "JUST" : judge;
        if (!HideAllUi) ShowJudgmentImage(shown == "JUST" ? "EXCELLENT" : shown);   // UNKNOWN UI: 판정 그림 숨김 (SIMPLE 스킨은 HUD 전체가 숨겨짐)
        // JUST 는 화면에 'PERFECT' 그대로 (설정 > 게임플레이 > JUST 표시 를 켜면 'JUST' — 흰 글자 + 흰 빛번짐)
        if (simpleSkin != null) simpleSkin.OnJudge(shown);
        ApplyLife(judge, false, just);
        RecordJudge(judge);
        CheckModeCrash(judge, just);
    }

    // =====================================================================
    //  게임 모드 — 판정 · LIFE · 점수 · 화면
    // =====================================================================
    // LAZY: 놓친 노트(판정 범위를 지나감)를 LazyChance 확률로 JUST 처리. NoteObject 가 MISS 내기 직전에 물어봄
    //  롱노트 머리면 그 롱노트는 끝까지 자동으로 눌린 것으로 (틱 · 끝도 JUST)
    private readonly HashSet<NoteData> autoHeld = new HashSet<NoteData>();
    public bool TryLazySave(NoteData n)
    {
        if (!GameModes.Play.Lazy || isFailed || IsTestMode || n == null || n.IsHit) return false;
        if (uiRng.NextDouble() >= GameModes.LazyChance) return false;
        n.IsHit = true;
        ProcessHit("EXCELLENT", 0.0, false);                 // 오차 기록 없이 JUST
        if (simpleSkin != null) simpleSkin.FlashFloor(SimpleGameSkin.SaveFlashColor);   // 레인 아래 빛이 초록으로 번쩍
        if (n.IsRedNote && redFx != null) redFx.Tap(n);
        else for (int w = 0; w < Mathf.Max(1, n.Width); w++) if (n.Lane + w < 4) SpawnHitEffect(n.Lane + w, n);
        if (NoteTypes.IsLongHead(n.Type) && n.NextLongNote != null)
        {
            autoHeld.Add(n);
            RegisterActiveLongNote(n, GetTickInterval(n.HitTime));
        }
        return true;
    }
    // COMBO BREAK: 콤보가 끊긴 시각부터 ComboBreakLockSeconds 동안 점수를 얻지 못함
    private double scoreLockUntil = double.NegativeInfinity;
    public bool ScoreLocked { get { return GameModes.Play.ComboBreak && currentPlayTime < scoreLockUntil; } }
    // 점수를 못 얻는 남은 시간 (초, 0 = 아님) — 화면 위쪽 COMBO BREAK 막대
    public double ScoreLockLeft { get { return ScoreLocked ? scoreLockUntil - currentPlayTime : 0.0; } }
    private void OnComboBroken()
    {
        if (GameModes.Play.ComboBreak) scoreLockUntil = currentPlayTime + GameModes.ComboBreakLockSeconds;
    }

    // PERFECTIONIST · PERFECTIONIST+ · ONE CHANCE: 조건을 어기면 즉시 TRACK CRASH (FAILED)
    private void CheckModeCrash(string judge, bool just)
    {
        if (isFailed || IsTestMode) return;
        if (ShieldActive) return;   // ONE+ 보호 중에는 TRACK CRASH 없음
        if (GameModes.Play.Modes.Length == 0) return;
        bool crash = (GameModes.Play.OneChance && judge == "MISS")
                  || (GameModes.Play.Perfectionist && judge != "EXCELLENT");
        if (!crash) return;
        Debug.Log($"[Mode] TRACK CRASH ({judge}{(judge == "EXCELLENT" ? " / not JUST" : "")})");
        crashCause = GameModes.Play.OneChance && judge == "MISS" ? GameMode.OneChance : GameMode.Perfectionist;   // TRACK CRASH 아래 원인 아이콘
        life = 0.0;
        StartFail();
    }

    // LIFE 를 쓰지 않는 판(무한): GAME SETTING · IMMORTAL
    public bool LifeInfinite { get { return IsTestMode || GameModes.Play.Immortal; } }
    // STRICT · STRICT+: 곡이 진행될수록 LIFE 최대 상한이 100 → 60 (HARD 이상 게이지에서만 쓸 수 있음)
    public bool HasLifeCap { get { return (GameModes.Play.Strict || GameModes.Play.StrictPlus) && !IsTestMode; } }
    // 상한 · 하한이 쓰는 곡 진행도 (0 ~ 1). 평소엔 곡 진행 그대로, ONE+ 보호 중엔 멈춤 → 끝나면 약 0.6초에 걸쳐 따라잡음
    private float capProgress;
    private bool capCatchingUp;
    private void UpdateCapProgress(float dt)
    {
        float target = PlayProgress();
        if (ShieldActive) { capCatchingUp = true; return; }
        if (capCatchingUp)
        {
            capProgress = Mathf.MoveTowards(capProgress, target, Mathf.Max(0.05f, Mathf.Abs(target - capProgress) / 0.6f + 0.02f) * dt);
            if (Mathf.Abs(target - capProgress) < 0.0005f) capCatchingUp = false;
        }
        else capProgress = target;
    }
    public double LifeCap
    {
        get
        {
            if (!HasLifeCap) return 100.0;
            return 100.0 - (100.0 - GameModes.StrictMinCap) * capProgress;
        }
    }
    // STRICT+: 곡이 진행될수록 LIFE 하한이 0 → 40 (LIFE ≤ 하한이면 즉시 TRACK CRASH)
    //  예) 곡 끝: 상한 60 · 하한 40 → LIFE 가 움직일 수 있는 범위는 41 ~ 60
    public double LifeFloor
    {
        get
        {
            if (!GameModes.Play.StrictPlus || IsTestMode) return 0.0;
            return GameModes.StrictPlusMaxFloor * capProgress;
        }
    }

    // UNKNOWN UI: 레인을 뺀 게임 화면 UI 를 모두 가림 (점수 · 콤보 · 판정 · LIFE · 곡 정보 · 오차 막대 · 키 이름 …)
    //  레인 · 판정선 · 노트 · 키 빔 · 타격 효과는 그대로
    private readonly System.Random uiRng = new System.Random();
    public bool HideAllUi { get { return GameModes.Play.UnknownUi && !IsTestMode; } }
    public int DisplayCombo { get { return currentCombo; } }
    public int DisplayScore { get { return ComputeScore(); } }

    // 결과창 판정 타임라인 / LIFE 그래프용 기록
    private void RecordJudge(string judge)
    {
        byte code;
        switch (judge)
        {
            case "EXCELLENT": code = 0; break;
            case "GREAT": code = 1; break;
            case "GOOD": code = 2; break;
            case "BAD": code = 3; break;
            default: code = 4; break;
        }
        recProgress.Add(PlayProgress());
        recJudge.Add(code);
        recLife.Add((float)life);
    }

    private float PlayProgress()
    {
        double s, e;
        GetPlayRange(out s, out e);
        return (float)Math.Max(0.0, Math.Min(1.0, (currentPlayTime - s) / Math.Max(0.001, e - s)));
    }

    // =====================================================================
    //  유틸 / 정리
    // =====================================================================
    // 초 단위 오차(양수 = FAST) → 부호 있는 정수 ms (FAST = -, SLOW = +). 화면의 FAST/SLOW 숫자와 같은 기준(소수점 버림)
    private static double SignedMs(double diffSeconds)
    {
        return 0.0 - Math.Truncate(diffSeconds * 1000.0);
    }

    private Camera FindSceneCamera()
    {
        foreach (var root in gameObject.scene.GetRootGameObjects()) {
            Camera cam = root.GetComponentInChildren<Camera>(true);
            if (cam != null && cam.enabled) return cam;
        }
        return Camera.main;
    }

    // Instantiate 는 '활성 씬'(보통 Core 씬)에 오브젝트를 만들기 때문에, 게임 씬이 언로드돼도 노트가 남는다.
    // → 항상 이 GameManager 가 속한 씬으로 옮겨서, 씬이 사라질 때 같이 지워지게 한다.
    private GameObject InstantiateInMyScene(GameObject prefab)
    {
        GameObject go = Instantiate(prefab);
        if (go.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        return go;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (mvPlayer != null && mvEventsHooked) {
            mvPlayer.loopPointReached -= OnMvLoopPointReached;
            mvPlayer.errorReceived -= OnMvError;
        }
        mvEventsHooked = false;

        foreach (var obj in noteObjectDict.Values) if (obj != null) Destroy(obj.gameObject);
        noteObjectDict.Clear();
        while (notePool.Count > 0) {
            NoteObject o = notePool.Dequeue();
            if (o != null) Destroy(o.gameObject);
        }
    }

    private void ResetStats()
    {
        processedNotes = 0; currentCombo = 0; maxCombo = 0; currentAccSum = 0; accPoints = 0; accNotes = 0; cntJust = 0;
        cntExcellent = cntGreat = cntGood = cntBad = cntMiss = 0;
        errorSumMs = 0; timedCount = 0;
        chartJudgeCount = 0; chartNoteCount = 0; chartTickCount = 0; chartLastTime = 0; chartEndCursor = 0;
        finishHoldTimer = 0f; finishRoutineStarted = false;
        clearChecked = false; clearEffectPlaying = false; onePlusUsed = false;
        crashCause = null; shieldUntil = double.NegativeInfinity; capProgress = 0f; capCatchingUp = false;
        comboSum = 0; life = StartLife; isFailed = false; failSpeed = 1f; endUiAlpha = 1f;
        ResetTempest();
        scoreLockUntil = double.NegativeInfinity;
        autoHeld.Clear();
        recProgress.Clear(); recJudge.Clear(); recLife.Clear(); recErrors.Clear();
        fastCount = slowCount = 0; failProgress = -1f;
    }

    // =====================================================================
    //  Pause UI (약간 늦게, 부드럽게 등장)
    // =====================================================================
    // 코드로 만든 일시정지 화면 (RESUME / RETRY / SETTINGS / QUIT)
    private void CreatePauseMenu()
    {
        TMP_FontAsset reg = pauseFont != null ? pauseFont : SettingsWindow.RegularFont;
        TMP_FontAsset bold = pauseBoldFont != null ? pauseBoldFont : SettingsWindow.BoldFont;
        if (reg == null) { SettingsUIKit.EnsureFont(); reg = SettingsUIKit.Font; }
        var items = new List<PauseMenu.Item>
        {
            new PauseMenu.Item { Label = "RESUME", Action = ResumeGame },
            new PauseMenu.Item { Label = "RETRY", Action = RetryGame },
            new PauseMenu.Item { Label = "SETTINGS", Action = SettingsWindow.Open },
            new PauseMenu.Item { Label = "QUIT", Action = BackToMenu },
        };
        // GAME SETTING(5_Test) 에서는 설정창에 들어가지 못함 (조정은 패널에서)
        if (IsTestMode) items.RemoveAll(it => it.Label == "SETTINGS");
        pauseMenu = PauseMenu.Create(reg, bold, items, ResumeGame);
        // 씬을 겹쳐 불러오는 구조(Additive)라 '활성 씬'은 바뀌지 않음 → 게임 씬에 넣어야 게임이 끝날 때 같이 사라짐
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pauseMenu.gameObject, gameObject.scene);
        if (pauseUIContainer != null) pauseUIContainer.SetActive(false);   // 예전 Pause UI 는 쓰지 않음
    }

    private IEnumerator ShowPauseUIRoutine()
    {
        if (pauseMenu != null)
        {
            string title = currentSong != null ? currentSong.Name + (string.IsNullOrWhiteSpace(currentSong.SubName) ? "" : " " + currentSong.SubName) : "";
            int lv = CoreManager.Instance != null ? CoreManager.Instance.CurrentLevelId : 2;
            string sub = (currentSong != null && !string.IsNullOrWhiteSpace(currentSong.Composer) ? currentSong.Composer + "   ·   " : "") +
                         DifficultyHelper.GetName(lv) + " " + DifficultyHelper.GetValue(currentSong, lv);
            pauseMenu.Show(title, sub);
            pauseShowRoutine = null;
            yield break;
        }
        if (pauseUIContainer == null) { pauseShowRoutine = null; yield break; }

        // 지연 없이 ESC 를 누른 그 프레임에 바로 켠다. 버튼도 처음부터 누를 수 있다.
        pauseUIContainer.SetActive(true);
        pauseUIContainer.transform.SetAsLastSibling(); // 같은 부모 안의 다른 UI 에 가려지지 않게
        if (pauseCanvasGroup != null) { pauseCanvasGroup.interactable = true; pauseCanvasGroup.blocksRaycasts = true; }

        float dur = Mathf.Max(0.01f, pauseUIFadeInDuration);
        float t = 0f;
        if (pauseCanvasGroup != null) pauseCanvasGroup.alpha = 0.35f; // 첫 프레임부터 바로 보이게 시작값을 둔다
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            if (pauseCanvasGroup != null) pauseCanvasGroup.alpha = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(t / dur));
            yield return null;
        }
        if (pauseCanvasGroup != null) pauseCanvasGroup.alpha = 1f;
        pauseShowRoutine = null;
    }

    private void HidePauseUIImmediate()
    {
        if (pauseShowRoutine != null) { StopCoroutine(pauseShowRoutine); pauseShowRoutine = null; }
        if (pauseMenu != null) pauseMenu.Hide();
        if (SettingsWindow.IsOpen) SettingsWindow.Close();
        if (pauseCanvasGroup != null) pauseCanvasGroup.alpha = 0f;
        if (pauseUIContainer != null) pauseUIContainer.SetActive(false);
    }

    private IEnumerator RetryRoutine()
    {
        isLeaving = true;
        HidePauseUIImmediate();
        for (int i = 0; i < 4; i++) StopHoldEffect(i);
        if (bgmSource != null) bgmSource.Stop();
        if (mvPlayer != null) mvPlayer.Stop();

        yield return FadeBlackOverlay(0.4f);

        CoreManager.Instance.LoadGameScene(currentSong, CoreManager.Instance.IsMvOn, CoreManager.Instance.CurrentLevelId);
    }

    // easeIn = true : 부드럽게 시작해서 부드럽게 끝남 (결과창 전환)
    private IEnumerator FadeBlackOverlay(float duration, CanvasGroup overlay = null, bool easeIn = false)
    {
        CanvasGroup g = overlay != null ? overlay : blackOverlayGroup;
        if (g == null) yield break;
        g.gameObject.SetActive(true);
        g.transform.SetAsLastSibling();
        g.blocksRaycasts = true;
        // [버그 수정] 레인(SIMPLE 스킨) · 정보 UI 캔버스가 암막보다 위에 그려져, 암막이 다 덮이기 전에 레인을 지워야 했음
        //  → 암막을 모든 게임 UI 위로 (클리어 연출 200 · MODE RANDOM 320 보다 위, 설정창 500 아래 → 900 은 FPS 1000 아래)
        Canvas oc = g.GetComponent<Canvas>();
        if (oc == null) oc = g.gameObject.AddComponent<Canvas>();
        if (!oc.isRootCanvas) oc.overrideSorting = true;
        oc.sortingOrder = Mathf.Max(oc.sortingOrder, 900);

        float elapsed = 0f;
        float start = g.alpha;
        float dur = Mathf.Max(0.01f, duration);
        while (elapsed < dur) {
            elapsed += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(elapsed / dur);
            if (easeIn) u = u * u * (3f - 2f * u);   // 부드럽게 시작해서 부드럽게 끝남
            g.alpha = Mathf.Lerp(start, 1f, u);
            yield return null;
        }
        g.alpha = 1f;
    }

    // =====================================================================
    //  결과창 진입: MV 종료 + 채보 종료 + 노래 종료 → resultDelaySeconds 후 암전 → 결과 씬
    // =====================================================================
    private void HookMvEvents()
    {
        if (mvPlayer == null || mvEventsHooked) return;
        mvPlayer.loopPointReached += OnMvLoopPointReached;
        mvPlayer.errorReceived += OnMvError;
        mvEventsHooked = true;
    }

    private void OnMvLoopPointReached(VideoPlayer vp) { mvFinished = true; }
    private void OnMvError(VideoPlayer vp, string message)
    {
        Debug.LogWarning("[MV] 재생 오류로 MV를 종료 처리합니다: " + message);
        mvFinished = true;
    }

    private static bool IsJudgableNote(NoteData n)
    {
        return NoteTypes.IsTapLike(n.Type) || NoteTypes.IsLongHead(n.Type) || n.Type == 3 || n.Type == 13 || NoteTypes.IsSlideRelay(n.Type);
    }

    private bool IsChartFinished()
    {
        if (notes.Count == 0) return true;
        if (currentPlayTime < chartLastTime + W_BAD + 0.1) return false;
        if (currentPlayTime >= chartLastTime + W_BAD + 3.0) return true; // 안전장치: 처리되지 않는 노트가 있어도 무한 대기 방지

        for (int s = 0; s < activeLongNote.Length; s++) if (activeLongNote[s] != null) return false;
        if (activeSlides.Count > 0) return false;

        while (chartEndCursor < notes.Count) {
            NoteData n = notes[chartEndCursor];
            if (n.IsHit || !IsJudgableNote(n)) chartEndCursor++;
            else return false;
        }
        return true;
    }

    // =====================================================================
    //  FULL COMBO / ALL PERFECT
    //   FULL COMBO  : MAX COMBO = 전체 노트 수
    //   ALL PERFECT : FULL COMBO 이면서 EXCELLENT 수 = 전체 노트 수 (롱노트 중간 틱 포함)
    // =====================================================================
    // ===== 게임 화면 UI(스킨)가 읽어 가는 값 =====
    public int Combo { get { return currentCombo; } }
    public double LiveAccuracy { get { return StrictAccuracy; } }
    public int LiveScore { get { return ComputeScore(); } }

    // 점수 = 판정 점수 + 콤보 점수
    //  판정 점수 = 850,000 × (판정 가중치 합) / N            (EXCELLENT 1, GREAT 0.5, GOOD 0.3, BAD 0.05, MISS 0)
    //  콤보 점수 = 150,000 × 2 / ((N - 1) N) × Σ c(k)        c(k) = n - 1 (n ≠ 0), 0 (n = 0)
    //  N = 채보 전체 노트 수(롱노트 틱 포함), n = k번째 판정 직후의 콤보.  풀콤보면 Σ c(k) = N(N-1)/2 → 150,000
    private int ComputeScore()
    {
        int n = chartJudgeCount;
        if (n <= 0) return 0;
        double judge = JudgeScoreMax * currentAccSum / n;
        double combo = n > 1 ? ComboScoreMax * 2.0 / ((double)(n - 1) * n) * comboSum : (maxCombo >= 1 ? ComboScoreMax : 0);
        int baseScore = Mathf.Clamp((int)Math.Round(judge + combo), 0, MaxScore);
        // 게임 모드 점수 배율 (고른 모드 배율의 곱, 최대 400%. MODE RANDOM 보너스는 그 위에 더함 → 최대 430% = 4,300,000)
        double k = GameModes.Play.ScoreMultiplier;
        if (Math.Abs(k - 1.0) < 1e-9) return baseScore;
        return (int)Math.Min(GameModes.MaxScoreWithModes, Math.Round(baseScore * k));
    }

    // =====================================================================
    //  LIFE (SIMPLE 스킨 전용, 0 ~ 100, 시작 100)
    //   EXCELLENT : + (100 - LIFE) × 0.05 × 100%     (롱노트 틱은 × 10%)
    //   GREAT     : + (100 - LIFE) × 0.05 × 50%
    //   GOOD      : + (100 - LIFE) × 0.05 × 35%
    //   BAD       : - 100 × (1750 / N) × 3%
    //   MISS      : - 100 × (1750 / N) × 5%          N = 전체 노트 수(틱 포함)
    //   99.3 이상이면 100. 0 이 되면 FAILED.
    // =====================================================================
    // =====================================================================
    //  게이지 (게임 모드 GAUGE 탭. 아무것도 안 고르면 NORMAL)
    //   N = 전체 판정 수 (노트 + 롱노트 틱).  틱은 노트의 10% 만큼만 오르고 깎임
    //   EASY   : 0 에서 시작.  PERFECT +200/N, GREAT 85%, GOOD 60%,  BAD -(0.3 + 100/N), MISS -(0.6 + 200/N)
    //   NORMAL : 0 에서 시작.  PERFECT +160/N, GREAT 75%, GOOD 40%,  BAD -(0.5 + 320/N), MISS -(1.0 + 640/N)
    //            → 모두 PERFECT 면 곡의 약 62% 지점에서 가득. 곡이 끝날 때 70 이상이면 TRACK COMPLETE, 미만이면 TRACK CRASH
    //   HARD   : 100 에서 시작 (예전 LIFE 와 같음). 오르는 양 = min(5, (100-게이지)×5%) × (PERFECT 1 · GREAT 0.5 · GOOD 0.35)
    //            깎이는 양 D = 100 × 1750/N 기준 BAD -3%, MISS -5%.  0 이면 즉시 TRACK CRASH
    //   HARD+  : 100 에서 시작. 오르지 않음.  GREAT -0.5%D, GOOD -1.5%D, BAD -3%D, MISS -5%D.  0 이면 즉시 TRACK CRASH
    //   TEMPEST: 100 에서 시작 (아래 UpdateTempest)
    //  한 번에 깎이는 양은 최대 20 (TEMPEST 는 판정 하나가 HARD 의 4배라 최대 60)
    //  다른 모드: IMMORTAL 은 항상 100 · STRICT 는 위로 LifeCap 까지만
    // =====================================================================
    public GaugeKind Gauge { get { return GameModes.Play.Gauge; } }
    public bool IsFillGauge { get { return !LifeInfinite && GameModes.IsFillGauge(Gauge); } }
    private double StartLife
    {
        get
        {
            if (LifeInfinite) return 100.0;
            switch (Gauge)
            {
                case GaugeKind.Easy: case GaugeKind.Normal: return 0.0;
                case GaugeKind.Tempest: return 100.0;
                default: return 100.0;
            }
        }
    }

    private void ApplyLife(string judge, bool isTick, bool just = true)
    {
        if (LifeInfinite) { life = 100.0; return; }   // GAME SETTING · IMMORTAL: 무한 (항상 가득, 줄지 않음)
        if (!LifeEnabled || isFailed || chartJudgeCount <= 0) return;
        const double MaxGain = 5.0, MaxLoss = 20.0;
        double n = chartJudgeCount;
        double w = isTick ? 0.10 : 1.0;                 // 롱노트 틱은 10%
        double D = 100.0 * (1750.0 / n);                // HARD 류 깎이는 양 기준
        double delta = 0.0;
        switch (Gauge)
        {
            case GaugeKind.Easy:
            {
                double g = 200.0 / n;
                switch (judge)
                {
                    case "EXCELLENT": delta = g * w; break;
                    case "GREAT": delta = g * 0.85; break;
                    case "GOOD": delta = g * 0.60; break;
                    case "BAD": delta = -(0.3 + 100.0 / n); break;
                    case "MISS": delta = -(0.6 + 200.0 / n) * w; break;
                }
                break;
            }
            case GaugeKind.Normal:
            {
                double g = 160.0 / n;
                switch (judge)
                {
                    case "EXCELLENT": delta = g * w; break;
                    case "GREAT": delta = g * 0.75; break;
                    case "GOOD": delta = g * 0.40; break;
                    case "BAD": delta = -(0.5 + 320.0 / n); break;
                    case "MISS": delta = -(1.0 + 640.0 / n) * w; break;
                }
                break;
            }
            case GaugeKind.Hard:
            {
                double gain = Math.Min(MaxGain, (100.0 - life) * 0.05);
                switch (judge)
                {
                    case "EXCELLENT": delta = gain * w; break;
                    case "GREAT": delta = gain * 0.5; break;
                    case "GOOD": delta = gain * 0.35; break;
                    case "BAD": delta = -D * 0.03; break;
                    case "MISS": delta = -D * 0.05 * w; break;
                }
                break;
            }
            case GaugeKind.HardPlus:
            {
                switch (judge)
                {
                    case "EXCELLENT": delta = 0.0; break;          // 완벽해도 차지 않음
                    case "GREAT": delta = -D * 0.005; break;
                    case "GOOD": delta = -D * 0.015; break;
                    case "BAD": delta = -D * 0.03; break;
                    case "MISS": delta = -D * 0.05 * w; break;
                }
                break;
            }
            case GaugeKind.Tempest:
                AddTempestEvent(judge, isTick);
                return;                                        // 값은 매 프레임 UpdateTempest 가 계산
        }
        delta = Math.Max(-MaxLoss, Math.Min(MaxGain, delta));
        if (ShieldActive && delta < 0.0) delta = 0.0;            // ONE+ 보호: 깎이지 않고 차오르기만
        life += delta;
        if (life >= 99.995) life = 100.0;
        if (HasLifeCap) life = Math.Min(life, LifeCap);
        if (life <= 0.0)
        {
            life = 0.0;
            if (!IsFillGauge) StartFail();                     // EASY · NORMAL 은 0 이 되어도 계속 (곡이 끝날 때 판정)
        }
    }

    // =====================================================================
    //  TEMPEST 게이지 (기억회상률) — 가장 어려운 게이지
    //   dR/dt = -r_d + Σ r_n · ln2 / 2^(t - t_n)
    //    r_d (자동 감소율) = (모두 PERFECT 일 때 얻는 총량) × 1.08 ÷ (채보 길이 + 0.5)  — 곡이 끝나면 0
    //       → 모두 PERFECT 여도 곡 끝까지 조금씩 줄어듦 (총량의 8% ≈ 16)
    //    r_n (노트 하나) = 처리 전 0, 처리 후 = 아래 값
    //       PERFECT +200/N · GREAT +40/N · GOOD 0 (나머지 몫만큼 순수하게 손해)
    //       BAD -12%D · MISS -20%D (HARD 게이지의 4배, D = 100 × 1750/N), 틱은 10%, 하나당 최대 -60
    //    → 판정 하나의 몫은 처리 순간부터 반감기 1초로 들어옴 (7초쯤이면 거의 다 반영)
    //   100 에서 시작 (최대 100, 넘친 몫은 버림), 0 이하면 TRACK CRASH
    //   예) 노트 1000개: MISS 하나 -35, 셋이면 TRACK CRASH.  GREAT 하나 -0.16, GOOD 하나 -0.2
    // =====================================================================
    private const double TempestDecayScale = 1.08;     // 자동 감소를 모두 PERFECT 의 몇 배로 (1 = 모두 PERFECT 면 그대로 유지)
    private const double TempestLossScale = 4.0;       // BAD · MISS 가 HARD 게이지의 몇 배
    private const double TempestMaxLoss = 60.0;
    private readonly List<KeyValuePair<double, double>> tempestEvents = new List<KeyValuePair<double, double>>();   // (t_n, r_n)
    private double tempestSettled;            // 다 반영된 몫의 합 (+ 넘친 몫 빼기)
    private double tempestRate;               // r_d
    private double tempestStart, tempestEnd;  // 감소가 일어나는 구간 (채보 시각)
    private bool tempestFinal;

    private void ResetTempest()
    {
        tempestEvents.Clear();
        tempestSettled = 0.0;
        tempestFinal = false;
        tempestRate = 0.0;
        if (Gauge != GaugeKind.Tempest || LifeInfinite || chartJudgeCount <= 0) return;
        double n = chartJudgeCount;
        double pure = 2.0 * (100.0 / n) * (chartNoteCount + 0.1 * chartTickCount);   // 모두 PERFECT 일 때 얻는 총량
        tempestStart = IsMidStart ? MidStartChartTime : 0.0;
        tempestEnd = Math.Max(tempestStart + 0.5, chartLastTime);
        tempestRate = pure * TempestDecayScale / (tempestEnd - tempestStart + 0.5);
    }

    private void AddTempestEvent(string judge, bool isTick)
    {
        double n = chartJudgeCount, w = isTick ? 0.10 : 1.0, D = 100.0 * (1750.0 / n);
        double r = 0.0;
        switch (judge)
        {
            case "EXCELLENT": r = 200.0 / n * w; break;
            case "GREAT": r = 40.0 / n; break;
            case "GOOD": r = 0.0; break;
            case "BAD": r = -D * 0.03 * TempestLossScale; break;
            case "MISS": r = -D * 0.05 * TempestLossScale * w; break;
        }
        r = Math.Max(-TempestMaxLoss, r);
        tempestEvents.Add(new KeyValuePair<double, double>(currentPlayTime, r));
    }

    private void UpdateTempest()
    {
        if (tempestRate <= 0.0 && tempestEvents.Count == 0) return;
        double t = currentPlayTime;
        // 12초가 지난 몫은 다 반영된 것으로 (2^-12 ≈ 0)
        for (int i = tempestEvents.Count - 1; i >= 0; i--)
            if (t - tempestEvents[i].Key > 12.0) { tempestSettled += tempestEvents[i].Value; tempestEvents.RemoveAt(i); }
        double sum = 0.0;
        foreach (var e in tempestEvents)
        {
            double dt = Math.Max(0.0, t - e.Key);
            sum += e.Value * (tempestFinal ? 1.0 : 1.0 - Math.Pow(2.0, -dt));
        }
        double decay = tempestFinal ? tempestRate * (tempestEnd - tempestStart)
                                    : tempestRate * Math.Max(0.0, Math.Min(t, tempestEnd) - tempestStart);
        double r = 100.0 - decay + tempestSettled + sum;
        double cap = HasLifeCap ? LifeCap : 100.0;
        if (r > cap) { tempestSettled -= r - cap; r = cap; }   // 넘친 몫은 버림
        if (ShieldActive && r < life) { tempestSettled += life - r; r = life; }   // ONE+ 보호: 줄어든 몫은 버림 (오르는 것만)
        life = Math.Max(0.0, r);
        if (r <= 0.0 && !isFailed) { life = 0.0; StartFail(); }
    }

    // CLUTCH 위험도 (0 ~ 1)
    //  HARD · HARD+ · TEMPEST: 게이지가 낮을수록 (100 → 0)
    //  EASY · NORMAL: 클리어 선(70)까지 가야 할 속도보다 뒤처진 만큼 (곡 진행 × 70 보다 얼마나 모자란가)
    private double ClutchDanger()
    {
        if (LifeInfinite) return 0.0;
        if (IsFillGauge) return Math.Max(0.0, Math.Min(1.0, (GameModes.ClearLine * PlayProgress() - life) / GameModes.ClearLine));
        return 1.0 - Math.Max(0.0, Math.Min(100.0, life)) / 100.0;
    }

    // 마지막 노트까지 처리된 순간: TEMPEST 는 남은 몫을 모두 반영, EASY · NORMAL 은 70 이상인가
    //  반환: true = TRACK CRASH
    private bool atEndGaugeCheck;
    private bool GaugeFailsAtEnd()
    {
        if (LifeInfinite || IsTestMode || !LifeEnabled) return false;
        if (Gauge == GaugeKind.Tempest)
        {
            tempestFinal = true;
            UpdateTempest();
            return isFailed || life <= 0.0;
        }
        if (IsFillGauge && !IsMidStart) return life < GameModes.ClearLine;   // PRACTICE(중간부터 시작)는 게이지로 실패하지 않음
        return false;
    }

    private bool onePlusUsed;   // ONE+: 이번 판에 이미 막았는가
    private GameMode? crashCause;   // TRACK CRASH 의 원인 모드 (PERFECTIONIST · ONE CHANCE · STRICT+), 게이지가 바닥나면 null
    // ONE+ 보호: 부활 뒤 OnePlusShieldSeconds 초 동안 LIFE 가 깎이지 않음 (오르기만)
    private double shieldUntil = double.NegativeInfinity;
    public bool ShieldActive { get { return currentPlayTime < shieldUntil && !isFailed; } }
    public double ShieldLeft { get { return ShieldActive ? shieldUntil - currentPlayTime : 0.0; } }

    private void StartFail()
    {
        if (IsTestMode) { life = 100.0; return; }   // GAME SETTING: FAILED 없음 (LIFE 가 바닥나면 다시 가득)
        if (isFailed) return;
        if (ShieldActive) { crashCause = null; return; }   // ONE+ 보호 중: TRACK CRASH 없음
        // 게임 모드 ONE+: TRACK CRASH 를 한 번 막음 → LIFE 30 으로 부활 (STRICT+ 면 하한 + 10, 상한은 넘지 않음)
        if (GameModes.Play.OnePlus && !onePlusUsed)
        {
            onePlusUsed = true;
            life = GameModes.OnePlusReviveLife;
            if (GameModes.Play.StrictPlus) life = Math.Max(life, LifeFloor + 10.0);
            if (IsFillGauge && atEndGaugeCheck) life = Math.Max(life, GameModes.ClearLine);   // 곡 끝 판정에서 부활 → 클리어
            if (Gauge == GaugeKind.Tempest)
            {
                // TEMPEST: 아직 반영 중인 몫은 버리고, 지금 값이 부활 값이 되도록 맞춤
                double decayNow = tempestFinal ? tempestRate * (tempestEnd - tempestStart)
                                               : tempestRate * Math.Max(0.0, Math.Min(currentPlayTime, tempestEnd) - tempestStart);
                tempestEvents.Clear();
                tempestSettled = life - 100.0 + decayNow;   // 시작 값 100 기준
            }
            if (HasLifeCap) life = Math.Min(life, LifeCap);
            crashCause = null;
            shieldUntil = currentPlayTime + GameModes.OnePlusShieldSeconds;   // 5초 동안 LIFE 가 깎이지 않음
            if (simpleSkin != null) simpleSkin.OnOnePlusRevive();              // 콤보 자리에 ONE+ 아이콘 · 레인 테두리 노란 빛 · LIFE 바 노란 타이머
            Debug.Log("[Mode] ONE+ 부활");
            return;
        }
        isFailed = true;
        failProgress = PlayProgress();
        finishRoutineStarted = true; // 일반 종료 흐름 대신 FAILED 흐름으로
        clearChecked = true;
        for (int i = 0; i < 4; i++) StopHoldEffect(i);
        if (redFx != null) redFx.StopAll();
        Debug.Log($"[Life] FAILED at {currentPlayTime:0.00}s");
        StartCoroutine(FailRoutine());
    }

    // 채보가 점점 느려지며 멈추고(음악도 함께 늘어지며 사라짐), 레인·노트·UI 가 투명해진 뒤 FAILED 연출 → 결과창
    private IEnumerator FailRoutine()
    {
        float slow = Mathf.Max(0.3f, failSlowDownSeconds);
        float startPitch = bgmSource != null ? bgmSource.pitch : 1f;
        float startVol = bgmSource != null ? bgmSource.volume : 1f;
        bool effectStarted = false, effectDone = false;
        float t = 0f;

        while (t < slow || !effectDone)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / slow);
            failSpeed = 0.5f * (1f + Mathf.Cos(Mathf.PI * p)); // 1 → 0 부드럽게

            if (bgmSource != null)
            {
                bgmSource.pitch = Mathf.Lerp(startPitch, 0.3f, p);
                bgmSource.volume = startVol * (1f - p);
            }
            if (mvPlayer != null && mvPlayer.isPlaying) mvPlayer.playbackSpeed = Mathf.Max(0.05f, failSpeed);

            endUiAlpha = 1f - p;
            ApplyEndFade();

            if (!effectStarted && t >= slow * 0.4f)
            {
                effectStarted = true;
                ClearEffect.FailCause = crashCause;   // 특정 모드 때문이면 TRACK CRASH 아래 그 아이콘
                ClearEffect.Play(transform, ClearEffect.ClearType.Failed, maxCombo, failEffectHoldSeconds, () => effectDone = true);
            }
            yield return null;
        }

        if (bgmSource != null) { bgmSource.Stop(); bgmSource.pitch = startPitch; }
        if (mvPlayer != null) mvPlayer.Pause();
        StartCoroutine(FinishToResultRoutine());
    }

    // =====================================================================
    //  곡 종료 UI 페이드 (모든 스킨): 마지막 노트 처리 직후 endUiFadeDuration 동안 사라짐
    //   대상: 기본 UI 컨테이너·레인 그룹·프리키 UI (CanvasGroup), 게임 씬의 모든 스프라이트(배경 제외), SIMPLE 스킨
    // =====================================================================
    private IEnumerator EndUiFadeRoutine() { return EndUiFadeRoutine(endUiFadeDuration); }

    private IEnumerator EndUiFadeRoutine(float duration)
    {
        float t = 0f, dur = Mathf.Max(0.01f, duration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            endUiAlpha = 1f - Mathf.Clamp01(t / dur);
            ApplyEndFade();
            yield return null;
        }
        endUiAlpha = 0f;
        ApplyEndFade();
    }

    private void ApplyEndFade()
    {
        if (!endFadeCollected) CollectEndFadeTargets();

        // 노트는 NoteObject 가 EndUiAlpha 를 읽어 스스로 투명해진다. 새로 나온 마디선만 추가
        foreach (var ml in activeMeasureLines)
        {
            if (ml == null || ml.go == null) continue;
            SpriteRenderer sr = ml.go.GetComponent<SpriteRenderer>();
            if (sr != null && !endFadeSprites.ContainsKey(sr)) endFadeSprites[sr] = sr.color;
        }

        foreach (var kv in endFadeSprites)
        {
            if (kv.Key == null) continue;
            Color c = kv.Value;
            kv.Key.color = new Color(c.r, c.g, c.b, c.a * endUiAlpha);
        }
        foreach (var kv in endFadeGroups)
        {
            if (kv.Key != null) kv.Key.alpha = kv.Value * endUiAlpha;
        }
    }

    private void CollectEndFadeTargets()
    {
        endFadeCollected = true;

        var keep = new HashSet<Transform>();
        if (bgRenderer != null) keep.Add(bgRenderer.transform);
        if (keepAtEnd != null) foreach (var go in keepAtEnd) if (go != null) keep.Add(go.transform);
        if (simpleSkin != null) keep.Add(simpleSkin.transform); // SIMPLE 스킨은 EndUiAlpha 를 직접 읽어 사라짐
        if (infoHud != null) keep.Add(infoHud.transform);       // Information UI 도 스스로 사라짐
        if (redFx != null) keep.Add(redFx.transform);           // RED 이펙트도 스스로 사라짐

        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (IsUnder(sr.transform, keep)) continue;
                if (IsScreenFillingSprite(sr)) continue; // 화면 전체를 덮는 배경·배경 어둡게 하는 막은 배경으로 취급
                if (sr.GetComponentInParent<NoteObject>() != null) continue; // 노트는 스스로 처리
                endFadeSprites[sr] = sr.color;
            }
        }

        ProtectBackgroundImages(basicUIContainer);
        if (laneGroup != null) ProtectBackgroundImages(laneGroup.gameObject);
        AddFadeGroup(basicUIContainer);
        if (laneGroup != null && simpleSkin == null) endFadeGroups[laneGroup] = laneGroup.alpha;
        if (fadeOutAtEnd != null)
        {
            foreach (var go in fadeOutAtEnd)
            {
                if (go == null) continue;
                if (go.transform is RectTransform) AddFadeGroup(go);
                else foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) endFadeSprites[sr] = sr.color;
            }
        }
    }

    // 카메라 화면의 80% 이상을 덮는 스프라이트 = 배경(또는 배경을 어둡게 하는 반투명 막) → 사라지지 않음
    private bool IsScreenFillingSprite(SpriteRenderer sr)
    {
        Camera cam = SceneCamera;
        if (sr == null || cam == null || !cam.orthographic) return false;
        float h = cam.orthographicSize * 2f, w = h * cam.aspect;
        Vector3 size = sr.bounds.size;
        return size.x >= w * 0.8f && size.y >= h * 0.8f;
    }

    // UI 안의 '화면 전체를 덮는 어두운 이미지'(배경 어둡게 하는 막)는 부모가 투명해져도 그대로 남도록
    // CanvasGroup.ignoreParentGroups 로 부모 투명도와 분리한다
    private void ProtectBackgroundImages(GameObject container)
    {
        if (container == null) return;
        Vector3[] a = new Vector3[4], b = new Vector3[4];
        foreach (var img in container.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            if (img.canvas == null) continue;
            Color c = img.color;
            if (c.r > 0.25f || c.g > 0.25f || c.b > 0.25f) continue; // 어두운 색만
            RectTransform canvasRt = img.canvas.rootCanvas.transform as RectTransform;
            if (canvasRt == null) continue;
            img.rectTransform.GetWorldCorners(a);
            canvasRt.GetWorldCorners(b);
            float iw = a[2].x - a[0].x, ih = a[2].y - a[0].y;
            float cw = b[2].x - b[0].x, ch = b[2].y - b[0].y;
            if (iw < cw * 0.8f || ih < ch * 0.8f) continue;     // 화면 대부분을 덮는 것만

            CanvasGroup g = img.GetComponent<CanvasGroup>();
            if (g == null) g = img.gameObject.AddComponent<CanvasGroup>();
            g.ignoreParentGroups = true;
            g.blocksRaycasts = false;
            g.interactable = false;
        }
    }

    private void AddFadeGroup(GameObject go)
    {
        if (go == null) return;
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        endFadeGroups[cg] = cg.alpha;
    }

    private static bool IsUnder(Transform t, HashSet<Transform> roots)
    {
        for (Transform p = t; p != null; p = p.parent) if (roots.Contains(p)) return true;
        return false;
    }

    // 게임 종료 시점: 마지막 노트(롱노트는 꼬리)가 판정선을 지나 판정 범위(W_BAD) 밖으로 벗어났을 때.
    //  사용자가 마지막 노트를 일찍 처리했거나 롱노트를 놓아 버려도, 이 시각이 되기 전에는 끝나지 않는다.
    //  (+0.05초: 같은 프레임에 처리되는 마지막 MISS 판정이 반영된 뒤에 결과를 정하도록)
    private bool IsAllNotesProcessed()
    {
        if (notes.Count == 0) return currentPlayTime >= 1.0;
        if (currentPlayTime < chartJudgeEndTime + W_BAD + 0.05) return false;
        if (currentPlayTime >= chartJudgeEndTime + W_BAD + 1.0) return true; // 안전장치
        for (int s = 0; s < activeLongNote.Length; s++) if (activeLongNote[s] != null) return false;
        if (activeSlides.Count > 0) return false;
        return true;
    }
    public Camera SceneCamera { get { return mainCam != null ? mainCam : Camera.main; } }

    // 진행 막대 / 남은 시간 기준: 시작 = min(0, 음악 시작), 끝 = max(마지막 노트 + 1초, 음악 끝)
    public void GetPlayRange(out double start, out double end)
    {
        double trigger = TriggerTime;
        start = Math.Min(0.0, trigger);
        double audioEnd = (bgmSource != null && bgmSource.clip != null) ? trigger + bgmSource.clip.length : 0.0;
        end = Math.Max(chartLastTime + 1.0, audioEnd);
    }

    private ClearEffect.ClearType GetClearType()
    {
        if (chartJudgeCount <= 0 || maxCombo < chartJudgeCount) return ClearEffect.ClearType.None;
        return cntExcellent >= chartJudgeCount ? ClearEffect.ClearType.AllPerfect : ClearEffect.ClearType.FullCombo;
    }

    // 마지막 노트까지 판정이 끝난 순간 한 번 검사해서 연출을 띄운다
    private void UpdateClearEffect()
    {
        if (IsTestMode) return;                    // GAME SETTING: 클리어 연출 없음
        if (clearChecked || currentState != GameState.Playing || isFailed) return;
        if (!IsAllNotesProcessed()) return;
        clearChecked = true;

        // 게이지: EASY · NORMAL 은 70 미만, TEMPEST 는 남은 몫까지 반영해서 0 이하면 TRACK CRASH (ONE+ 부활 가능)
        if (GaugeFailsAtEnd())
        {
            if (!isFailed) { atEndGaugeCheck = true; StartFail(); atEndGaugeCheck = false; }
            if (isFailed) { Debug.Log($"[Gauge] {Gauge} {life:0.0} → TRACK CRASH"); return; }
        }

        // 클리어 연출(FULL COMBO · ALL PERFECT · TRACK COMPLETE)은 게임 화면 UI 를 가리지 않고 MV 도 그대로 재생
        //  → 곡 · MV · 채보가 모두 끝나면(UpdateFinishCondition) 암막과 함께 UI 가 사라지며 결과창으로

        ClearEffect.ClearType type = GetClearType();
        Debug.Log($"[Clear] {(IsAllJust ? "ALL JUST" : type.ToString())}  (MAX COMBO {maxCombo} / EXCELLENT {cntExcellent} / JUST {cntJust} / TOTAL {chartJudgeCount} / TP {StrictAccuracy:F2}%)");
        if (!showClearEffect) return;
        if (CoreManager.Instance.IsAutoPlay && !showClearEffectInAutoPlay) return;

        // ALL JUST (TP 100%): ALL PERFECT 보다 위 등급 — 전용 연출
        //  FULL COMBO · ALL PERFECT 와 같은 연출(ClearEffect)의 한 단계 위 버전
        if (IsAllJust)
        {
            clearEffectPlaying = true;
            ClearEffect.Play(transform, ClearEffect.ClearType.AllJust, maxCombo, clearEffectHoldSeconds + 0.6f, () => clearEffectPlaying = false);
            return;
        }

        // 풀콤보가 아니면 일반 클리어 연출 (TRACK COMPLETE)
        ClearEffect.ClearType fx = type == ClearEffect.ClearType.None ? ClearEffect.ClearType.Clear : type;
        clearEffectPlaying = true;
        ClearEffect.Play(transform, fx, maxCombo, clearEffectHoldSeconds, () => clearEffectPlaying = false);
    }

    private bool IsSongFinished()
    {
        if (bgmSource == null || bgmSource.clip == null) return currentPlayTime >= chartLastTime + 3.0;
        if (!isFullMusicStarted) return false;

        double triggerTime = TriggerTime;
        return !bgmSource.isPlaying && currentPlayTime >= triggerTime + bgmSource.clip.length - 0.25;
    }

    // MV 가 끝났는가
    //  · 영상 끝 이벤트(loopPointReached) 또는 재생 오류 → 끝
    //  · 재생이 멈춘 채 영상 시각이 2초 넘게 그대로 → 끝 (끝 이벤트가 안 오는 파일 대비)
    //  · 최후 안전장치: 음악 기준으로 영상 길이 + 10초가 지남
    //  [버그 수정] 예전에는 '음악 기준 시각이 영상 길이 + 1초'를 넘으면 바로 끝으로 봐서,
    //   영상이 음악보다 조금 늦게 재생되거나 파일이 길이를 짧게 알려 주는 MV 는 끝나기 전에 잘렸다.
    private bool IsMvFinished()
    {
        if (!mvActive || mvFinished) return true;
        if (!isFullMusicStarted || mvPlayer == null) return false;
        if (!mvPlayer.isPlaying && mvStallTimer > 2f) return true;
        if (mvPlayer.length > 0.0) {
            double triggerTime = TriggerTime;
            if (currentPlayTime - triggerTime >= mvPlayer.length + 10.0) return true;
        }
        return false;
    }

    // =====================================================================
    //  MV 동기화: 영상이 음악보다 늦거나 빨라지면 재생 속도를 살짝 조절해 따라잡음
    //   (0.04초 이내는 그대로, 1초 넘게 벌어지면 그 위치로 바로 이동)
    //  [버그 수정] 영상은 음악과 따로 재생되기 때문에, 디코딩이 밀리면 곡이 진행될수록 점점 어긋났다.
    // =====================================================================
    private float mvSyncTimer, mvStallTimer;
    private double mvLastTime = -1.0;

    private void SyncMv(float dt)
    {
        if (!mvActive || mvFinished || mvPlayer == null || !isFullMusicStarted) return;

        // 영상 시각이 멈춰 있는 시간 (끝 판정용)
        if (Math.Abs(mvPlayer.time - mvLastTime) > 0.0001) { mvLastTime = mvPlayer.time; mvStallTimer = 0f; }
        else if (currentState == GameState.Playing) mvStallTimer += dt;

        if (currentState != GameState.Playing || isFailed || !mvPlayer.isPlaying) return;
        mvSyncTimer += dt;
        if (mvSyncTimer < 0.25f) return;
        mvSyncTimer = 0f;

        double triggerTime = TriggerTime;
        double expected = currentPlayTime - triggerTime;
        if (expected < 0.0 || (mvPlayer.length > 0.0 && expected > mvPlayer.length - 0.3)) { SetMvSpeed(1f); return; }

        double diff = expected - mvPlayer.time;   // + : 영상이 늦음
        if (Math.Abs(diff) > 1.0 && mvPlayer.canSetTime) { mvPlayer.time = expected; SetMvSpeed(1f); }
        else if (Math.Abs(diff) > 0.04) SetMvSpeed(Mathf.Clamp(1f + (float)diff * 0.5f, 0.85f, 1.15f));
        else SetMvSpeed(1f);
    }

    private void SetMvSpeed(float speed)
    {
        if (mvPlayer != null && mvPlayer.canSetPlaybackSpeed && Mathf.Abs(mvPlayer.playbackSpeed - speed) > 0.001f) mvPlayer.playbackSpeed = speed;
    }

    // =====================================================================
    //  음악 재생 예약 (시작 / 일시정지 후 다시 시작 공통)
    //   fromPlayTime(지금의 차트 시각)에서 lead 초 뒤의 차트 시각에 해당하는 음악 위치를,
    //   정확히 lead 초 뒤의 dspTime 에 재생하도록 예약한다. → 예약 직후에도 시계가 튀지 않는다.
    // =====================================================================
    private void SetupAudioTiming()
    {
        int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        int bufLen, bufCount;
        AudioSettings.GetDSPBufferSize(out bufLen, out bufCount);
        if (bufLen <= 0) bufLen = 1024;
        if (bufCount <= 0) bufCount = 4;
        dspBufferSec = bufLen / (double)sr;
        // 믹서가 이미 처리 중인 구간보다 뒤로 예약해야 정확히 그 시각에 소리가 난다
        minScheduleLead = Math.Max(0.1, dspBufferSec * bufCount * 1.5 + 0.02);
        musicScheduleLead = minScheduleLead + 0.1;
        Debug.Log($"[Sync] DSP buffer {bufLen} x {bufCount} @ {sr}Hz → schedule lead {minScheduleLead * 1000:0}ms");
    }

    private void ScheduleMusic(double fromPlayTime)
    {
        musicScheduled = true;
        if (bgmSource == null || bgmSource.clip == null) return;

        AudioClip clip = bgmSource.clip;
        double trigger = TriggerTime;
        double lead = Math.Max(minScheduleLead, trigger - fromPlayTime);   // 몇 초 뒤에 소리가 나기 시작하는가
        double startPos = fromPlayTime + lead - trigger;                     // 그때의 음악 위치 (초)
        if (startPos < 0.0) startPos = 0.0;
        if (startPos >= clip.length) return;                                 // 이미 음악이 끝난 위치

        double startDsp = AudioSettings.dspTime + lead;
        bgmSource.Stop();
        bgmSource.pitch = 1f;   // 시계 계산은 1배속 재생을 전제로 함
        bgmSource.timeSamples = Mathf.Clamp((int)Math.Round(startPos * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
        bgmSource.PlayScheduled(startDsp);
        dspStartTime = startDsp - startPos;   // 음악 위치 0 에 해당하는 dspTime
        ResetAudioDrift(startDsp);
    }

    // 차트 시각이 음악 시작 시각을 지난 순간 (MV 도 이때 같이 시작)
    private void OnMusicStarted(double overshoot)
    {
        isFullMusicStarted = true;
        if (mvPlayer != null && !string.IsNullOrEmpty(mvPlayer.url) && CoreManager.Instance.IsMvOn) {
            if (mvPlayer.canSetTime) mvPlayer.time = Math.Max(0.0, overshoot);
            mvPlayer.Play();
        }
    }

    // =====================================================================
    //  음악 시각 보정: 화면 시계는 AudioSettings.dspTime 기준이고, 음악은 예약 재생으로 정확히 시작한다.
    //   그래도 재생이 밀리는 경우(스트리밍 끊김, 디코딩 지연 등)를 대비해 실제 재생 위치(timeSamples)를 지켜보다가
    //   5ms 넘게 벌어지면 시계 기준(dspStartTime)을 한 프레임에 최대 1ms 씩 옮겨 맞춘다.
    //  [버그 수정] 예전에는 '처음 1.5초 동안의 차이'를 정상으로 간주했는데, 시작이 늦었던 판은 그 늦음이 기준이 되어
    //   끝까지 고쳐지지 않았다. 이제는 시작이 정확하므로 기준 차이를 오디오 버퍼 1개(보고 단위 차이) 이내로만 인정한다.
    //   또 허용 오차를 15ms → 5ms 로 줄였다.
    // =====================================================================
    private const double DriftTolerance = 0.005;
    private const int DriftBaselineSamples = 45;
    private double driftAvg, driftBase, driftStartDsp;
    private int driftSamples, driftOutliers;

    private void ResetAudioDrift(double startDsp)
    {
        driftSamples = 0; driftOutliers = 0; driftAvg = 0; driftBase = 0; driftSampleTimer = 0.0; lastDspRaw = -1.0;
        driftStartDsp = startDsp + 0.15;   // 재생이 막 시작된 직후는 측정하지 않음
    }

    // 매끄러운 dspTime: 마지막으로 dspTime 이 바뀐 뒤 흐른 실제 시간을 더함 (버퍼 2개 분량까지만)
    private double lastDspRaw = -1.0, lastDspReal;
    private double SmoothDspTime()
    {
        double dsp = AudioSettings.dspTime;
        double now = Time.realtimeSinceStartupAsDouble;
        if (dsp != lastDspRaw) { lastDspRaw = dsp; lastDspReal = now; }
        double ext = Math.Min(now - lastDspReal, 0.1);   // 오디오가 잠깐 멈춰도 0.1초까지는 매끄럽게 이어감
        return dsp + Math.Max(0.0, ext);
    }

    // [버그 수정] 드리프트 보정이 '프레임마다' 돌아서, 프레임이 높을수록(400fps 면 초당 400번) 기준 시각이 크게 흔들렸다
    //  (한 번에 최대 1ms × 초당 400번 = 초당 0.4초까지 움직일 수 있었음 → 노트 떨림 · 순간이동).
    //  지금은 0.05초마다 한 번만 재고, 한 번에 최대 0.5ms 씩만 옮긴다 (프레임 수와 무관하게 초당 최대 10ms)
    private const double DriftSampleInterval = 0.05;
    private const double DriftMaxStep = 0.0005;
    private double driftSampleTimer;

    private void CorrectAudioDrift()
    {
        if (bgmSource == null || bgmSource.clip == null || bgmSource.clip.frequency <= 0) return;
        if (AudioSettings.dspTime < driftStartDsp) return;
        driftSampleTimer += Time.unscaledDeltaTime;
        if (driftSampleTimer < DriftSampleInterval) return;
        driftSampleTimer = 0.0;
        double sampleT = bgmSource.timeSamples / (double)bgmSource.clip.frequency;
        double dspT = AudioSettings.dspTime - dspStartTime;
        double err = sampleT - dspT;          // + : 음악이 시계보다 앞섬 / - : 음악이 밀림
        if (Math.Abs(err) > 2.0) return;     // 끝난 뒤 · 이상한 값은 무시

        // 한 프레임만 튀는 값은 무시 (계속 그 값이면 진짜로 밀린 것 → 20프레임 뒤부터 반영)
        if (driftSamples > 0 && Math.Abs(err - driftAvg) > 0.1)
        {
            if (++driftOutliers < 20) return;
            driftAvg = err;
        }
        driftOutliers = 0;
        driftAvg = driftSamples == 0 ? err : driftAvg + (err - driftAvg) * 0.05;
        driftSamples++;

        if (driftSamples == DriftBaselineSamples)
        {
            // timeSamples 와 dspTime 은 갱신 단위가 버퍼 하나만큼 다를 수 있다 → 그 이내의 차이만 '정상'으로 인정
            driftBase = Math.Abs(driftAvg) <= dspBufferSec + 0.003 ? driftAvg : 0.0;
            if (driftBase == 0.0 && Math.Abs(driftAvg) > DriftTolerance)
                Debug.Log($"[Sync] 음악이 예정보다 {driftAvg * 1000:0.0}ms 어긋나 있어 천천히 맞춥니다.");
        }
        else if (driftSamples > DriftBaselineSamples)
        {
            double dev = driftAvg - driftBase;
            if (Math.Abs(dev) > DriftTolerance)
            {
                double step = Math.Max(-DriftMaxStep, Math.Min(DriftMaxStep, dev * 0.1));
                dspStartTime -= step;
                driftAvg -= step;
            }
        }
    }

    private void UpdateFinishCondition()
    {
        if (currentState != GameState.Playing || finishRoutineStarted) return;

        // GAME SETTING: 채보 · 음악이 끝나면 결과창 대신 처음부터 다시 (무한 반복)
        if (IsTestMode)
        {
            if (IsChartFinished() && IsSongFinished()) RestartTestLoop();
            return;
        }

        if (IsChartFinished() && IsSongFinished() && IsMvFinished() && !clearEffectPlaying) {
            finishHoldTimer += Time.unscaledDeltaTime;
            if (finishHoldTimer >= resultDelaySeconds) {
                finishRoutineStarted = true;
                StartCoroutine(FinishToResultRoutine());
            }
        } else {
            finishHoldTimer = 0f;
        }
    }

    private double PlayLengthSeconds()
    {
        double s, e;
        GetPlayRange(out s, out e);
        return Math.Max(0.0, e - s);
    }

    private ResultData BuildResultData()
    {
        int levelId = CoreManager.Instance.CurrentLevelId;

        double acc = isFailed ? 0.0 : StrictAccuracy;   // FAILED 판은 정확도(TP) 0%

        int score = IsMidStart ? 0 : ComputeScore();       // PRACTICE 모드(중간부터 시작한 판)는 0점

        // 전체 판정 오차 = (모든 판정의 부호 있는 ms 합) / 전체 노트 수. 롱노트 중간 틱은 포함하지 않는다.
        int denom = missCountsInErrorAverage ? (timedCount + cntMiss) : timedCount;
        double avgError = denom > 0 ? errorSumMs / denom : 0.0;

        return new ResultData {
            SongId = currentSong.Id,
            SongName = currentSong.Name,
            SubName = currentSong.SubName,
            Composer = currentSong.Composer,
            LevelId = levelId,
            DifficultyName = DifficultyHelper.GetName(levelId),
            DifficultyValue = DifficultyRevealed ? DifficultyHelper.GetRealValue(currentSong, levelId)    // 숨긴 난이도: 공개 시점을 지났으면 실제 값
                                                 : DifficultyHelper.GetValue(currentSong, levelId),
            Score = score,
            Accuracy = acc,
            Excellent = cntExcellent, Great = cntGreat, Good = cntGood, Bad = cntBad, Miss = cntMiss,
            MaxCombo = maxCombo,
            TotalNotes = chartJudgeCount,
            IsFullCombo = !isFailed && GetClearType() != ClearEffect.ClearType.None,
            IsAllPerfect = !isFailed && GetClearType() == ClearEffect.ClearType.AllPerfect,
            IsAllJust = IsAllJust,
            JustPerfect = cntJust,
            IsFailed = isFailed,
            AverageErrorMs = avgError,
            ErrorSampleCount = denom,
            IsAutoPlay = CoreManager.Instance.IsAutoPlay,

            // 결과창 그래프용
            Fast = fastCount,
            Slow = slowCount,
            TimelineProgress = recProgress.ToArray(),
            TimelineJudge = recJudge.ToArray(),
            LifeHistory = recLife.ToArray(),
            ErrorsMs = recErrors.ToArray(),
            HasLife = LifeEnabled,
            FinalLife = (float)life,
            FailedAtProgress = failProgress,
            LengthSeconds = (float)PlayLengthSeconds(),
            IsMv = CoreManager.Instance.IsMvOn,
            IsMidStart = IsMidStart,
            MidStartSeconds = MidStartSeconds,
            Speed = SettingsStore.Current.SettingSpeed,
            Modes = GameModes.Play.Modes.Select(m => (int)m).ToArray(),
            ScoreMultiplier = GameModes.Play.ScoreMultiplier,
            RandomBonus = GameModes.Play.RandomBonus
        };
    }

    // 결과창으로: 성과 연출(이 끝나고 곡 · MV · 채보가 모두 끝나면) → 암막이 생기기 시작 → 암막이 화면을 완전히 덮음
    //  → (암막 뒤에서) 게임 화면을 모두 없앰 · 음악 · MV 정지 → 결과창
    //  암막이 다 덮일 때까지 게임 화면(레인 · 노트 · 점수)은 그대로 둠 (상태도 Playing 그대로 → 레인이 먼저 사라지지 않음)
    private IEnumerator FinishToResultRoutine()
    {
        isLeaving = true;                       // 일시정지 · 입력 막힘 (마지막 노트 뒤라 입력은 이미 받지 않음)
        for (int i = 0; i < 4; i++) StopHoldEffect(i);

        ResultData result = BuildResultData();

        // 1) 암막: 천천히 덮음
        //  [버그 수정] 씬에 연결된 암막 오브젝트(resultTransitionOverlay)는 설정에 따라 한 번에 확 덮이는 경우가 있어
        //   코드로 만든 전용 암막(ScreenBlackout, 씬을 넘어가도 남음)을 서서히 진하게 → 결과창에서 걷힘
        ScreenBlackout blackout = ScreenBlackout.Create();
        yield return blackout.Cover(resultBlackoutTime);

        // 2) 다 덮인 뒤: 게임 화면을 모두 없애고 음악 · MV 정지
        currentState = GameState.Finished;
        endUiAlpha = 0f;
        ApplyEndFade();
        if (bgmSource != null) bgmSource.Stop();
        if (mvPlayer != null) mvPlayer.Stop();
        yield return new WaitForSecondsRealtime(0.15f);

        // 3) 결과창 (열리면 암막이 걷힘)
        blackout.RevealWhenLoaded("4_Result", resultRevealTime);
        CoreManager.Instance.LoadResultScene(result);
    }

    private void UpdateUI()
    {
        int shownCombo = HideAllUi ? 0 : DisplayCombo;   // UNKNOWN UI: 콤보 숨김
        if (shownCombo > 0) {
            if (comboTitleImage != null) comboTitleImage.gameObject.SetActive(true);
            
            if (comboImageContainer != null) { 
                comboImageContainer.gameObject.SetActive(true); 
                UpdateComboImages(shownCombo, comboImageContainer, basicComboDigits);
            }
            
            comboAnimTimer = 1f; 
        } else {
            if (comboTitleImage != null) comboTitleImage.gameObject.SetActive(false);
            if (comboImageContainer != null) comboImageContainer.gameObject.SetActive(false);
        }

        if (accuracyText != null)
        {
            if (IsMidStart || HideAllUi) accuracyText.SetText("");   // PRACTICE 모드: 정확도 표시 안 함
            else accuracyText.SetText("TP {0:2}%", (float)StrictAccuracy);   // SetText: 문자열을 새로 만들지 않음 (GC 줄이기)
        }
    }

    private static readonly char[] comboDigitBuf = new char[12];

    private void UpdateComboImages(int combo, RectTransform container, List<UnityEngine.UI.Image> digitList)
    {
        if (container == null) return;
        
        // [GC 줄이기] combo.ToString() 대신 자리수를 직접 계산 (판정마다 문자열이 생기지 않게)
        int digitCount = 0;
        { int v = Mathf.Max(0, combo); do { comboDigitBuf[digitCount++] = (char)('0' + v % 10); v /= 10; } while (v > 0 && digitCount < comboDigitBuf.Length); }
        System.Array.Reverse(comboDigitBuf, 0, digitCount);
        
        while (digitList.Count < digitCount) {
            GameObject go = new GameObject($"Digit_{digitList.Count}");
            go.transform.SetParent(container, false);
            UnityEngine.UI.Image img = go.AddComponent<UnityEngine.UI.Image>();
            img.raycastTarget = false; 
            digitList.Add(img);
        }
        
        for (int i = 0; i < digitList.Count; i++) {
            digitList[i].gameObject.SetActive(i < digitCount);
        }
        
        float totalWidth = (digitCount * comboDigitWidth) + ((digitCount - 1) * comboDigitSpacing);
        float startX = -totalWidth / 2f + (comboDigitWidth / 2f);
        
        for (int i = 0; i < digitCount; i++) {
            char c = comboDigitBuf[i];
            
            if (comboSpriteDict.TryGetValue(c, out Sprite spr)) {
                digitList[i].sprite = spr;
                digitList[i].color = Color.white;
            } else {
                digitList[i].sprite = null;
                digitList[i].color = new Color(1, 1, 1, 0); 
            }
            
            RectTransform rt = digitList[i].rectTransform;
            rt.sizeDelta = new Vector2(comboDigitWidth, comboDigitHeight);
            rt.anchoredPosition = new Vector2(startX + (i * (comboDigitWidth + comboDigitSpacing)), 0);
        }
    }

    private void ShowJudgmentImage(string judge)
    {
        if (judgmentImage == null) return;
        switch (judge) {
            case "EXCELLENT": judgmentImage.sprite = sprExcellent; break;
            case "GREAT": judgmentImage.sprite = sprGreat; break;
            case "GOOD": judgmentImage.sprite = sprGood; break;
            case "BAD": judgmentImage.sprite = sprBad; break;
            case "MISS": judgmentImage.sprite = sprMiss; break;
        }
        judgmentImage.SetNativeSize(); 
        judgmentImage.gameObject.SetActive(true);
        judgmentAnimTimer = 0.45f; 
    }
}