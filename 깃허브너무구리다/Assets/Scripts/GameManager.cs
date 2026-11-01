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
    [Tooltip("결과창으로 넘어갈 때 암막이 덮이는 시간(초)")]
    public float resultOverlayFadeSeconds = 1.0f; // (이름 변경: 예전 resultFadeDuration 값이 씬에 0.5 로 저장돼 있어도 새 기본값 1.0 이 적용되도록)
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
    //  JUST PERFECT (PERFECT 중 ±15ms 안)       = 100%
    //  PERFECT (15 ~ 65ms)                      = 99% → 88% 로 점점 낮아짐
    //  GREAT 50%,  GOOD 20%,  BAD 0%,  MISS 0%
    //  롱노트 중간 판정(틱)은 항상 JUST PERFECT (100%),  끝까지 누른 롱노트 끝도 JUST PERFECT
    //  TP 100.00% (모든 판정이 JUST PERFECT) = ALL JUST
    //  표시는 버림 (99.999% 를 100.00% 로 올려 보이지 않게)
    public const double JustWindowMs = 15.0;
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

    // ALL JUST: 풀콤보 + 모든 판정이 JUST PERFECT
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
    public const double W_EXCELLENT = 0.065, W_GREAT = 0.085, W_GOOD = 0.100, W_BAD = 0.120;
    
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
        // 영역 채보는 SIMPLE-DIVERSE 전용: 이 판만 강제로 바꾼다 (설정 값은 그대로 → 끝나면 원래 스킨)
        IsAreaChart = AreaHelper.IsAreaLevel(currentSong, CoreManager.Instance.CurrentLevelId);
        if (IsAreaChart && skinId != GameSkins.Diverse)
        {
            Debug.Log("[Area] 영역 채보 → 이번 판은 SIMPLE-DIVERSE 스킨으로 진행합니다.");
            skinId = GameSkins.Diverse;
        }
        if (GameSkins.UsesSimpleSkin(skinId)) simpleSkin = SimpleGameSkin.Create(this, skinId == GameSkins.Diverse);
        redFx = RedHitEffects.Create(this);  // RED 노트 전용 탭 / 롱노트 홀드 이펙트 (일반 이펙트 아래)
        infoHud = GameInfoHud.Create(this); // 곡 정보 · 난이도 · 시간 · 점수 · 배속 · 진행 막대 (모든 GAME UI 공통, 설정 > INFORMATION UI)
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
        }

        if (loadingDiffText != null) {
            loadingDiffText.text = $"{diffName} {diffVal}";
            loadingDiffText.color = diffColor;
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

        ResourceRequest audioReq = Resources.LoadAsync<AudioClip>($"Audio/Full/{songId}");
        ResourceRequest bgReq = Resources.LoadAsync<Sprite>($"Sprites/blurimg/{songId}");
        ResourceRequest jacketReq = Resources.LoadAsync<Sprite>($"Sprites/loadimg-large/{songId}");

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

        while (!audioReq.isDone || !bgReq.isDone || !jacketReq.isDone) {
            yield return null;
        }

        Sprite bgSprite = bgReq.asset as Sprite;
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

        string mvPathAvi = Path.Combine(Application.streamingAssetsPath, "MV", $"{songId}.avi");
        string mvPathMp4 = Path.Combine(Application.streamingAssetsPath, "MV", $"{songId}.mp4");
        
        string finalMvPath = "";
        if (File.Exists(mvPathAvi)) finalMvPath = mvPathAvi;
        else if (File.Exists(mvPathMp4)) finalMvPath = mvPathMp4;

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
        areaEvents.Clear(); laneFoldEvents.Clear(); longHeadOf.Clear(); areaNoteCache.Clear();
        for (int L = 1; L <= 2; L++) { speedChangesByLayer[L].Clear(); scrollEventsByLayer[L].Clear(); notesByLayer[L].Clear(); }
        speedNotices.Clear();

        songStartOffset = 0; chartAudioOffset = 0;
        bool hasMusicOffset = false;
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
            else if (e.Type == "AREA") {
                // AREA, 마디, 분자, 분모, 1100  (영역 레인 / 0000 = 영역 끝)
                areaEvents.Add(new AreaEvent { Time = exactTime, Mask = AreaMask.Parse(e.Value) });
            }
            else if (e.Type == "FOLD" || e.Type == "UNFOLD") {
                // FOLD / UNFOLD, 마디, 분자, 분모, 초 (완전히 접히는/펼쳐지는 데 걸리는 시간)
                double dur;
                if (!double.TryParse(e.Value.Split(',')[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dur)) dur = 0.5;
                laneFoldEvents.Add(new LaneFoldEvent { Time = exactTime, Fold = e.Type == "FOLD", Duration = Math.Max(0.0, dur) });
            }
            else if (e.Type == "NOTE") {
                string[] lanesData = e.Value.Split(',');
                
                // 구형 압축 포맷 (예: 1000) - 층 개념이 없으므로 전부 2층(Top)
                if (lanesData.Length == 1 && !lanesData[0].Contains("_") && !lanesData[0].Contains("(")) {
                    string val = lanesData[0].Trim();
                    for (int lane = 0; lane < val.Length && lane < 4; lane++) {
                        if (val[lane] != '0') {
                            int noteType = int.Parse(val[lane].ToString());
                            if (noteType == 6 || noteType == 5) continue;
                            var newNote = new NoteData { HitTime = exactTime, Lane = lane, Type = noteType, IsHit = false, isTimeLogged = false, Layer = NoteLayer.Top };
                            notes.Add(newNote); notesByLane[lane].Add(newNote); 
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
        areaEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        laneFoldEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        if (laneFoldEvents.Count > 0)
            Debug.Log($"[Chart] 레인 접기/펼치기 {laneFoldEvents.Count}개 (첫 이벤트 {laneFoldEvents[0].Time:0.000}s, {(laneFoldEvents[0].Fold ? "FOLD" : "UNFOLD")})");
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
                int ly = Mathf.Clamp(note.Layer, 1, 2);

                if (note.Type == 2 || note.Type == 12) lastLinkNode[ly] = note;
                else if ((note.Type == 3 || note.Type == 13) && lastLinkNode[ly] != null) {
                    if (lastLinkNode[ly].IsRedNote == note.IsRedNote) { lastLinkNode[ly].NextLongNote = note; longHeadOf[note] = lastLinkNode[ly]; }
                    lastLinkNode[ly] = null;
                }
            }
        }

        ApplyMidStart();

        // 채보 전체 노트 수: 완벽하게 쳤을 때 판정이 나오는 횟수 (= 풀콤보 시 MAX COMBO)
        //  노트(탭·화살표·롱노트 머리/꼬리) + 롱노트 중간 틱. 틱은 실제 플레이와 똑같은 규칙으로 미리 세어 둔다.
        chartJudgeCount = 0; chartNoteCount = 0; chartTickCount = 0; chartLastTime = 0; chartEndCursor = 0;
        chartJudgeEndTime = 0;
        foreach (var n in notes) {
            if (n.HitTime > chartLastTime) chartLastTime = n.HitTime;
            if (IsJudgableNote(n) && n.HitTime > chartJudgeEndTime) chartJudgeEndTime = n.HitTime; // 판정이 있는 마지막 노트 (꼬리 포함)
            if (NoteTypes.IsTapLike(n.Type)) chartNoteCount++;
            else if (n.Type == 2 || n.Type == 12) {
                chartNoteCount += (n.NextLongNote != null) ? 2 : 1;
                if (n.NextLongNote != null) chartTickCount += CountLongNoteTicks(n);
            }
        }
        chartJudgeCount = chartNoteCount + chartTickCount;
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

    // 토큰("11_3", "2" ...) 하나를 노트로 만들어 등록한다.
    private void AddNoteFromToken(string token, int lane, double hitTime, int layer)
    {
        if (lane < 0 || lane >= 4) return;
        if (!NoteCellParser.TryParseToken(token, out int type, out int width, out bool isRed)) return;

        // 폭(Width): RED 노트 · 넓은 일반 노트(영역 채보, "1_3" 처럼) 모두 1~4칸. 덮는 레인 모두에서 칠 수 있음
        var newNote = new NoteData { HitTime = hitTime, Lane = lane, Type = type, IsHit = false, isTimeLogged = false, Layer = layer };
        newNote.IsRedNote = isRed;
        newNote.Width = Mathf.Clamp(width, 1, 4 - lane);
        notes.Add(newNote);
        for (int w = 0; w < newNote.Width; w++) notesByLane[lane + w].Add(newNote);
    }

    // =====================================================================
    //  영역(Area) · 레인 접기 (SIMPLE-DIVERSE 전용 채보 기능)
    // =====================================================================
    private readonly List<AreaEvent> areaEvents = new List<AreaEvent>();
    private readonly List<LaneFoldEvent> laneFoldEvents = new List<LaneFoldEvent>();
    public List<AreaEvent> AreaEvents { get { return areaEvents; } }
    public bool HasAreas { get { return areaEvents.Count > 0; } }
    // 이 판이 영역 채보인가 (songs.json "Area": "deployed(INSANE)")
    public bool IsAreaChart { get; private set; }

    // 그 시각의 영역 레인 (0 = 영역 없음)
    public int AreaMaskAt(double t)
    {
        int mask = 0;
        for (int i = 0; i < areaEvents.Count && areaEvents[i].Time <= t + 1e-9; i++) mask = areaEvents[i].Mask;
        return mask;
    }

    // 이 레인 키로 이 노트를 칠 수 있는가
    //  노트의 판정 범위(±BAD) 동안 영역이 한 번도 없으면 → 어느 레인이든 가능 (일반 구간)
    //  판정 범위 안에 영역이 있으면 → 그 범위 안에서 한 번이라도 영역이었던 레인만 가능
    public bool AreaAllowsLane(int lane, double noteTime)
    {
        if (areaEvents.Count == 0) return true;
        double from = noteTime - W_BAD, to = noteTime + W_BAD;
        int union = AreaMaskAt(from);
        for (int i = 0; i < areaEvents.Count; i++)
        {
            double t = areaEvents[i].Time;
            if (t <= from) continue;
            if (t > to) break;
            union |= areaEvents[i].Mask;
        }
        return union == 0 || AreaMask.Has(union, lane);
    }

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
        if (s <= 0.0) return;
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

    // ===== 영역 안 노트 (레인이 접혀도 접히지 않고 그대로 보이며 내려옴) =====
    //  노트가 차지하는 레인(RED 는 폭 전체) 중 하나라도, 노트의 판정 범위(±BAD) 동안 영역이었던 레인이면 '영역 노트'
    //  예) 영역 = 1번 레인, 1~2번 레인에 걸친 2칸 RED 노트 → 영역 노트 (접히지 않음)
    //  롱노트는 머리 ~ 꼬리 전체 구간으로 판단하고, 머리 · 몸통 · 꼬리가 항상 같은 결과
    private readonly Dictionary<NoteData, NoteData> longHeadOf = new Dictionary<NoteData, NoteData>();   // 꼬리 → 머리
    private readonly Dictionary<NoteData, bool> areaNoteCache = new Dictionary<NoteData, bool>();

    public bool NoteInArea(NoteData n)
    {
        if (n == null || areaEvents.Count == 0) return false;
        NoteData head;
        if (longHeadOf.TryGetValue(n, out head)) n = head;
        bool r;
        if (areaNoteCache.TryGetValue(n, out r)) return r;
        double from = n.HitTime - W_BAD;
        double to = (n.NextLongNote != null ? n.NextLongNote.HitTime : n.HitTime) + W_BAD;
        int union = AreaUnion(from, to);
        int noteMask = 0;
        int w = Mathf.Max(1, n.Width);
        for (int i = n.Lane; i < n.Lane + w && i < 4; i++) if (i >= 0) noteMask |= 1 << i;
        r = (union & noteMask) != 0;
        areaNoteCache[n] = r;
        return r;
    }

    // [from, to] 동안 한 번이라도 영역이었던 레인 전체
    private int AreaUnion(double from, double to)
    {
        int union = AreaMaskAt(from);
        for (int i = 0; i < areaEvents.Count; i++)
        {
            double t = areaEvents[i].Time;
            if (t <= from) continue;
            if (t > to) break;
            union |= areaEvents[i].Mask;
        }
        return union;
    }

    // 지금 이 레인이 영역인가 (레인이 접혀도 이 레인의 효과는 접히지 않음)
    public bool LaneInAreaNow(int lane)
    {
        return AreaMask.Has(AreaMaskAt(currentPlayTime), lane);
    }

    // 지금 이 레인을 누를 수 있는가 (눌림 표시용)
    public bool AreaLaneOpenNow(int lane)
    {
        int m = AreaMaskAt(currentPlayTime);
        return m == 0 || AreaMask.Has(m, lane);
    }

    public bool HasLaneFolds { get { return laneFoldEvents.Count > 0; } }

    // 레인 펼침 정도 (1 = 다 펼침, 0 = 다 접힘)
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

        // 게임에 들어올 때와 같은 전환(곡 정보 화면이 덮은 뒤 씬 이동). 곡 선택 씬이 준비되면 걷힘 (SongSelectManager)
        if (currentSong != null && !SongIntroOverlay.IsActive)
        {
            int lv = CoreManager.Instance.CurrentLevelId;
            HidePauseUIImmediate();
            SongIntroOverlay.Show(currentSong, lv, true, () => CoreManager.Instance.LoadSongSelectScene());
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
            if (currentState == GameState.Playing && !isFailed) PauseGame();
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
            if (CoreManager.Instance.IsAutoPlay) HandleAutoPlay(currentPlayTime);
            else HandleInputs(currentPlayTime);
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
        return (a != null && !(redOnly && a.IsRedNote)) || (b != null && !(redOnly && b.IsRedNote));
    }

    private bool IsLaneHeldByLongNote(int lane)
    {
        return activeLongNote[lane] != null || activeLongNote[lane + 4] != null;
    }

    private void RegisterActiveLongNote(NoteData n, double tickInterval)
    {
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
                    
                    if (isNormalNote) {
                        double randomDiff = (UnityEngine.Random.Range(-30f, 30f)) / 1000.0;
                        ApplyJudgment(randomDiff);
                    } else {
                        ProcessHit("EXCELLENT", 0.0, true);
                    }
                    
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

                    if (n.Type == 2 || n.Type == 12)
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
                StopHoldEffect(i);
                
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
                // 영역 채보: 지금 영역이 아닌 레인은 눌림 표시 · 키음 없음 (판정은 노트 판정 범위 기준으로 따로 검사)
                bool open = AreaLaneOpenNow(i);
                if (open)
                {
                    if (beamRenderers != null && beamRenderers[i] != null && onSprites != null) beamRenderers[i].sprite = onSprites[i];
                    if (whiteLights != null && whiteLights[i] != null) whiteLights[i].SetActive(true);
                    if (simpleSkin != null) simpleSkin.SetPressed(i, true);
                    PlayHitSound();
                }
                
                EvaluateHit(i, T_sys - InputTiming.PressAge(targetKey));
                
                if (LaneNeedsHoldEffect(i)) StartHoldEffect(i);
            }
        }
        
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
        NoteData targetNote = null;
        double minAbsDiff = double.MaxValue;
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
            if (!NoteTypes.IsTapLike(n.Type) && n.Type != 2 && n.Type != 12) continue;
            // 영역 채보: 판정 범위 동안 영역이었던 레인의 키로만 칠 수 있음
            if (!AreaAllowsLane(lane, n.HitTime)) continue;

            double timeDiff = n.HitTime - adjustedHitTime;
            if (timeDiff > W_BAD) break; 
            if (timeDiff < -W_BAD) continue; 

            double absDiff = Math.Abs(timeDiff);
            if (absDiff < minAbsDiff) {
                minAbsDiff = absDiff;
                targetNote = n;
            }
        }

        if (targetNote == null) return;
            
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

        if (targetNote.Type == 2 || targetNote.Type == 12)
        {
            RegisterActiveLongNote(targetNote, GetTickInterval(targetNote.HitTime));
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
        currentAccSum += 1f;
        cntExcellent++;
        cntJust++;                       // 롱노트 중간 판정 = 항상 JUST PERFECT
        accNotes++; accPoints += 1.0;    // TP 에도 100% 로 들어감
        currentCombo++;
        if (currentCombo > maxCombo) maxCombo = currentCombo;
        comboSum += currentCombo - 1;
        comboAnimTimer = 1f;
        UpdateUI();
        ApplyLife("EXCELLENT", true);
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
        currentCombo = 0;
        UpdateUI();
        ApplyLife("MISS", true);
        RecordJudge("MISS");
    }

    private double GetLongNoteEndTime(NoteData head) { return head.NextLongNote != null ? head.NextLongNote.HitTime : head.HitTime; }

    private void ApplyJudgment(double diff) 
    {
        double absDiff = Math.Abs(diff);
        int msDiff = (int)(absDiff * 1000.0);

        double signedMs = SignedMs(diff);
        if (absDiff <= W_EXCELLENT) ProcessHit("EXCELLENT", signedMs, true);
        else if (absDiff <= W_GREAT) ProcessHit("GREAT", signedMs, true);
        else if (absDiff <= W_GOOD) ProcessHit("GOOD", signedMs, true);
        else ProcessHit("BAD", signedMs, true);
        if (hitErrorMeter != null) hitErrorMeter.Add(signedMs, absDiff <= W_EXCELLENT ? 0 : absDiff <= W_GREAT ? 1 : absDiff <= W_GOOD ? 2 : 3);

        if (CoreManager.Instance.CurrentSettings.ShowFastSlow && msDiff >= CoreManager.Instance.CurrentSettings.judgeoverrange && absDiff <= W_BAD)
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
        processedNotes++;
        float accMultiplier = 0f;

        switch (judge) {
            case "EXCELLENT": accMultiplier = 1f; currentCombo++; break;
            case "GREAT": accMultiplier = 0.5f; currentCombo++; break;
            case "GOOD": accMultiplier = 0.3f; currentCombo++; break;
            case "BAD": accMultiplier = 0.05f; currentCombo = 0; break;
            case "MISS": accMultiplier = 0f; currentCombo = 0; break;
        }
        if (currentCombo > maxCombo) maxCombo = currentCombo; 
        if (currentCombo > 0) comboSum += currentCombo - 1;

        currentAccSum += accMultiplier;
        accNotes++;
        accPoints += AccValue(judge, signedErrorMs, hasTiming);
        bool just = IsJust(judge, signedErrorMs, hasTiming);
        if (just) cntJust++;

        switch (judge) {
            case "EXCELLENT": cntExcellent++; break;
            case "GREAT": cntGreat++; break;
            case "GOOD": cntGood++; break;
            case "BAD": cntBad++; break;
            case "MISS": cntMiss++; break;
        }
        if (hasTiming && judge != "MISS")
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
        ShowJudgmentImage(judge);
        // JUST PERFECT 는 화면에 'PERFECT' 그대로 (설정 > 게임플레이 > JUST PERFECT 표시 를 켜면 'JUST PERFECT')
        if (simpleSkin != null) simpleSkin.OnJudge(just && SettingsStore.Current.ShowJustPerfect ? "JUST" : judge);
        ApplyLife(judge, false);
        RecordJudge(judge);
    }

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
        clearChecked = false; clearEffectPlaying = false;
        comboSum = 0; life = 100.0; isFailed = false; failSpeed = 1f; endUiAlpha = 1f;
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

    private IEnumerator FadeBlackOverlay(float duration, CanvasGroup overlay = null)
    {
        CanvasGroup g = overlay != null ? overlay : blackOverlayGroup;
        if (g == null) yield break;
        g.gameObject.SetActive(true);
        g.transform.SetAsLastSibling();
        g.blocksRaycasts = true;

        float elapsed = 0f;
        float start = g.alpha;
        float dur = Mathf.Max(0.01f, duration);
        while (elapsed < dur) {
            elapsed += Time.unscaledDeltaTime;
            g.alpha = Mathf.Lerp(start, 1f, elapsed / dur);
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
        return NoteTypes.IsTapLike(n.Type) || n.Type == 2 || n.Type == 3 || n.Type == 12 || n.Type == 13;
    }

    private bool IsChartFinished()
    {
        if (notes.Count == 0) return true;
        if (currentPlayTime < chartLastTime + W_BAD + 0.1) return false;
        if (currentPlayTime >= chartLastTime + W_BAD + 3.0) return true; // 안전장치: 처리되지 않는 노트가 있어도 무한 대기 방지

        for (int s = 0; s < activeLongNote.Length; s++) if (activeLongNote[s] != null) return false;

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
        return Mathf.Clamp((int)Math.Round(judge + combo), 0, MaxScore);
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
    private void ApplyLife(string judge, bool isTick)
    {
        if (!LifeEnabled || isFailed || chartJudgeCount <= 0) return;
        // [완화] 한 번에 오르는 양은 최대 5%, 한 노트로 깎이는 양은 최대 20%
        //  (예전에는 노트 수가 적은 채보에서 MISS 한 번에 20% 넘게 깎일 수 있었다)
        const double MaxGain = 5.0, MaxLoss = 20.0;
        double gain = Math.Min(MaxGain, (100.0 - life) * 0.05);
        double damage = 100.0 * (1750.0 / chartJudgeCount);
        double delta = 0.0;
        switch (judge)
        {
            case "EXCELLENT": delta = gain * (isTick ? 0.10 : 1.0); break;
            case "GREAT": delta = gain * 0.5; break;
            case "GOOD": delta = gain * 0.35; break;
            case "BAD": delta = -damage * 0.03; break;
            case "MISS": delta = -damage * 0.05 * (isTick ? 0.10 : 1.0); break; // 놓친 롱노트 틱은 10%
        }
        delta = Math.Max(-MaxLoss, Math.Min(MaxGain, delta));
        life += delta;
        if (life >= 99.3) life = 100.0;
        if (life <= 0.0)
        {
            life = 0.0;
            StartFail();
        }
    }

    private void StartFail()
    {
        if (isFailed) return;
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
    private IEnumerator EndUiFadeRoutine()
    {
        float t = 0f, dur = Mathf.Max(0.01f, endUiFadeDuration);
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
        if (clearChecked || currentState != GameState.Playing || isFailed) return;
        if (!IsAllNotesProcessed()) return;
        clearChecked = true;

        // 마지막 노트가 처리되자마자 게임 화면 UI 가 사라지기 시작 (FC / AP 연출은 동시에 시작)
        StartCoroutine(EndUiFadeRoutine());

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

        double acc = StrictAccuracy;

        int score = ComputeScore();

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
            DifficultyValue = DifficultyHelper.GetValue(currentSong, levelId),
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
            Speed = SettingsStore.Current.SettingSpeed
        };
    }

    private IEnumerator FinishToResultRoutine()
    {
        currentState = GameState.Finished;
        isLeaving = true;
        for (int i = 0; i < 4; i++) StopHoldEffect(i);

        ResultData result = BuildResultData();

        yield return FadeBlackOverlay(resultOverlayFadeSeconds, resultTransitionOverlay);

        if (bgmSource != null) bgmSource.Stop();
        if (mvPlayer != null) mvPlayer.Stop();

        CoreManager.Instance.LoadResultScene(result);
    }

    private void UpdateUI()
    {
        if (currentCombo > 0) {
            if (comboTitleImage != null) comboTitleImage.gameObject.SetActive(true);
            
            if (comboImageContainer != null) { 
                comboImageContainer.gameObject.SetActive(true); 
                UpdateComboImages(currentCombo, comboImageContainer, basicComboDigits);
            }
            
            comboAnimTimer = 1f; 
        } else {
            if (comboTitleImage != null) comboTitleImage.gameObject.SetActive(false);
            if (comboImageContainer != null) comboImageContainer.gameObject.SetActive(false);
        }

        if (accuracyText != null) accuracyText.SetText("TP {0:2}%", (float)StrictAccuracy);   // SetText: 문자열을 새로 만들지 않음 (GC 줄이기)
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