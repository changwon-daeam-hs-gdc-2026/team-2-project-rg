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

class GameScrollEvent { 
    public double Time; 
    public double Multiplier; 
}

public class FreeKeyLineObj {
    public int id;
    public GameObject go;
    public SpriteRenderer sr;
    
    public Vector3 startPos;
    public Vector3 targetPos;
    public double posStartTime = -1;
    public double posDuration;

    public float startRot;
    public float targetRot;
    public double rotStartTime = -1;
    public double rotDuration;
    
    public float startAlpha;
    public float targetAlpha;
    public double alphaStartTime = -1;
    public double alphaDuration;
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
    private bool isFullMusicStarted = false;

    [Header("결과창 전환")]
    [Tooltip("MV · 채보 · 노래가 모두 끝난 뒤 암전되기까지 기다리는 시간(초)")]
    public float resultDelaySeconds = 3.0f;
    [Tooltip("결과창으로 넘어갈 때 암막이 덮이는 시간(초)")]
    public float resultFadeDuration = 1.0f;
    [Tooltip("결과창으로 넘어가기 직전 화면을 어둡게 덮는 암막 (CanvasGroup). 비워 두면 blackOverlayGroup 을 대신 사용")]
    public CanvasGroup resultTransitionOverlay;
    [Tooltip("true: MISS 도 전체 판정 오차 평균의 분모(전체 노트 수)에 포함 / false: 타이밍이 있는 판정만 평균")]
    public bool missCountsInErrorAverage = true;
    public const int MaxScore = 1000000;

    [Header("Pause UI 연출")]
    [Tooltip("Pause UI 가 투명 → 불투명으로 나타나는 시간(초). ESC 를 누르는 즉시 시작한다")]
    public float pauseUIFadeInDuration = 0.08f;

    // 한 판 통계 (결과창용)
    private int cntExcellent, cntGreat, cntGood, cntBad, cntMiss;
    private double errorSumMs = 0;      // FAST = 음수, SLOW = 양수 (표시되는 정수 ms 기준)
    private int timedCount = 0;         // 타이밍이 기록된 판정 수 (롱노트 중간 틱 제외)
    private int chartJudgeCount = 0;    // 채보 전체의 판정 대상 수 (점수 계산용)
    private double chartLastTime = 0;   // 마지막 노트 시각
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
    
    [Header("Pause UI 연결")]
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
    public RectTransform fkComboImageContainer;  
    public float comboDigitWidth = 65f;          
    public float comboDigitHeight = 90f;         
    public float comboDigitSpacing = 2f;         

    private Dictionary<char, Sprite> comboSpriteDict = new Dictionary<char, Sprite>();
    private List<UnityEngine.UI.Image> basicComboDigits = new List<UnityEngine.UI.Image>();
    private List<UnityEngine.UI.Image> fkComboDigits = new List<UnityEngine.UI.Image>();

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

    [Header("FREE-KEY 모드 에셋 및 UI")]
    public GameObject freeKeyUIContainer;
    public Sprite freeKeyNormalSprite;
    public Sprite freeKeySpaceSprite;
    public Sprite freeKeyDragSprite;
    public Sprite freeKeyLinearSprite; 

    public RectTransform fkInfoUI;   
    public RectTransform fkLevelUI;  
    public RectTransform fkComboUI;  

    [Header("게임 설정")]
    public float[] laneXPositions = { -1.2f, -0.4f, 0.4f, 1.2f }; 
    public float hitLineY = -2.85f; 
    public const double baseScrollSpeed = 600.0;

    [Header("키빔 & 라이트")]
    public SpriteRenderer[] beamRenderers; 
    public GameObject[] whiteLights;       
    public Sprite[] offSprites;            
    public Sprite[] onSprites;             

    private int totalNotes = 0, processedNotes = 0, currentCombo = 0, maxCombo = 0;
    private double currentAccSum = 0;

    private NoteData[] activeLongNote = new NoteData[8]; // 슬롯: 0~3 = 2층(Top), 4~7 = 1층(Bottom) 
    private double[] nextLongNoteTickTime = new double[8]; 
    private double[] lnReleaseTime = new double[8]; 

    private List<NoteData> notes = new List<NoteData>();
    // 층별 변속 (인덱스 1 = 1층, 2 = 2층). BPM 변화는 층과 무관하게 bpmChanges 하나를 공유한다.
    private List<SpeedChange>[] speedChangesByLayer = new List<SpeedChange>[] { null, new List<SpeedChange>(), new List<SpeedChange>() };
    private List<BpmChange> bpmChanges = new List<BpmChange>();
    private List<UIEventData> uiEvents = new List<UIEventData>();
    private List<LinearEventData> linearEvents = new List<LinearEventData>();
    private List<GameScrollEvent>[] scrollEventsByLayer = new List<GameScrollEvent>[] { null, new List<GameScrollEvent>(), new List<GameScrollEvent>() };

    public Dictionary<int, FreeKeyLineObj> activeLines = new Dictionary<int, FreeKeyLineObj>();

    public double songStartOffset = 0;
    public double chartAudioOffset = 0.0;
    public const double W_EXCELLENT = 0.065, W_GREAT = 0.085, W_GOOD = 0.100, W_BAD = 0.120;
    
    private Key[] laneKeys = { Key.D, Key.F, Key.J, Key.K };
    private bool[] prevKeyState = new bool[4];
    private int prevKeysPressedCount = 0;
    private bool prevSpaceState = false;
    
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
    private Vector2 initialFkComboPos;

    void Awake() { 
        Instance = this; 
        Application.targetFrameRate = 1000; 

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
        if (fkComboImageContainer != null) {
            if (comboImageContainer == null) initialComboScale = fkComboImageContainer.localScale;
            initialFkComboPos = fkComboImageContainer.anchoredPosition;
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
        hitSoundClip = Resources.Load<AudioClip>("Audio/etc/hitsound"); 

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
        loadingCanvasGroup.alpha = 1f;
        loadingCanvasGroup.gameObject.SetActive(true);
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
        totalNotes = currentSong.NoteCount;
        yield return null;

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
        for (int i = 0; i < 50; i++) {
            GameObject ml = new GameObject("MeasureLine");
            SpriteRenderer sr = ml.AddComponent<SpriteRenderer>();
            Texture2D tex = new Texture2D(1, 1); tex.SetPixel(0, 0, new Color(128, 128, 128, 0.2f)); tex.Apply();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            sr.sortingOrder = 1; 
            ml.transform.SetParent(this.transform);
            ml.transform.localScale = new Vector3(3.2f, 0.015f, 1f); 
            ml.SetActive(false);
            measureLinePool.Enqueue(ml);
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
        if (fkComboImageContainer != null) fkComboImageContainer.gameObject.SetActive(false);
        if (fkComboUI != null) fkComboUI.gameObject.SetActive(false);
        if (freeKeyUIContainer != null) freeKeyUIContainer.SetActive(false);
        if (basicUIContainer != null) basicUIContainer.SetActive(true);
        
        StartCoroutine(LoadingSequenceRoutine());
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

    public void SpawnHitEffect(int lane)
    {
        if (lane < 0 || lane > 3) return;

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
        if (holdEffects[lane] == null) return;
        holdEffects[lane].transform.position = new Vector3(laneXPositions[lane], hitLineY + hitEffectOffsetY, -4.9f);
        ParticleSystem[] pss = holdEffects[lane].GetComponentsInChildren<ParticleSystem>();
        foreach(var ps in pss) ps.Play();
    }

    public void StopHoldEffect(int lane)
    {
        if (holdEffects[lane] == null) return;
        ParticleSystem[] pss = holdEffects[lane].GetComponentsInChildren<ParticleSystem>();
        foreach(var ps in pss) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private IEnumerator LoadingSequenceRoutine()
    {
        Vector3 titleBasePos = titleGroup.transform.localPosition;
        titleGroup.transform.localPosition = titleBasePos + new Vector3(0, 100f, 0); 

        float t = 0;
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

        if (mvPlayer != null && CoreManager.Instance.IsMvOn && !string.IsNullOrEmpty(mvPlayer.url)) {
            mvPlayer.Prepare();
            yield return new WaitUntil(() => mvPlayer.isPrepared);
            mvPlayer.Play();
            mvPlayer.Pause(); 
        }
        if (bgmSource != null && bgmSource.clip != null) {
            bgmSource.Play();
            bgmSource.Pause(); 
        }

        yield return new WaitForSeconds(2.5f); 

        t = 0;
        while (t < 0.3f) {
            t += Time.deltaTime;
            loadingCanvasGroup.alpha = 1f - (t / 0.3f);
            yield return null;
        }
        loadingCanvasGroup.gameObject.SetActive(false);

        t = 0;
        while (t < 0.5f) {
            t += Time.deltaTime;
            if (laneGroup != null) laneGroup.alpha = t / 0.5f;
            yield return null;
        }
        if (laneGroup != null) laneGroup.alpha = 1f;

        isFullMusicStarted = false;
        // 오디오가 시작되는 시각(triggerTime)이 기본 대기 시간보다 더 이전이면(예: -12.889s),
        // 그 시각부터 게임을 시작해서 곡 앞부분이 잘리지 않게 한다.
        double audioTriggerAtStart = songStartOffset - CoreManager.Instance.CurrentSettings.AudioOffset;
        playStartTime = Math.Min(-maxOffsetWaitTime, audioTriggerAtStart);
        currentPlayTime = playStartTime;
        gameStartTime = (double)Time.time;
        currentState = GameState.Playing; 
    }

    private float EaseOut(float x) { return 1f - Mathf.Pow(1f - x, 3f); }

    public void LoadChart(int songId, int levelId) 
    {
        ResetStats();
        notes.Clear(); bpmChanges.Clear(); 
        uiEvents.Clear(); linearEvents.Clear();
        for (int L = 1; L <= 2; L++) { speedChangesByLayer[L].Clear(); scrollEventsByLayer[L].Clear(); notesByLayer[L].Clear(); }
        foreach (var line in activeLines.Values) Destroy(line.go);
        activeLines.Clear();

        songStartOffset = 0; chartAudioOffset = 0;
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

            if (type == "OFFSET" || type == "SONGSTARTOFFSET") { 
                if (data.Length >= 2) songStartOffset = double.Parse(data[1].Trim(), System.Globalization.CultureInfo.InvariantCulture); 
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
                uiEvents.Add(new UIEventData {
                    TargetUI = data[1].Trim(), Action = data[2].Trim(),
                    StartTime = double.Parse(data[3].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                    Duration = double.Parse(data[4].Trim(), System.Globalization.CultureInfo.InvariantCulture)
                });
                continue;
            }
            else if (type == "FREEKEY" && data.Length >= 4 && data[1].Trim() == "Linear") {
                string actionOrId = data[2].Trim();
                if (actionOrId == "add" || actionOrId == "del") {
                    double st = data.Length >= 5 ? double.Parse(data[4].Trim(), System.Globalization.CultureInfo.InvariantCulture) : -999;
                    linearEvents.Add(new LinearEventData { Action = actionOrId, LineNumber = int.Parse(data[3].Trim()), StartTime = st });
                } else {
                    int lineId = int.Parse(actionOrId);
                    string action = data[3].Trim();
                    LinearEventData lin = new LinearEventData { LineNumber = lineId, Action = action };
                    
                    if (action == "exposure" || action == "disappearance") {
                        lin.StartTime = double.Parse(data[4].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                        lin.Duration = double.Parse(data[5].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    } else if (action == "rotation") {
                        lin.Value1 = double.Parse(data[4].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                        lin.StartTime = double.Parse(data[5].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                        lin.Duration = double.Parse(data[6].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    } else if (action == "move") {
                        lin.Value1 = double.Parse(data[4].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                        lin.Value2 = double.Parse(data[5].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                        lin.StartTime = double.Parse(data[6].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                        lin.Duration = double.Parse(data[7].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    }
                    linearEvents.Add(lin);
                }
                continue;
            }

            if (data.Length >= 5) {
                if (type == "FREEKEY" && data[1].Trim() != "Linear") {
                    if (data.Length >= 10) {
                        var fNote = new NoteData {
                            IsFreeKey = true, FreeKeyType = int.Parse(data[1].Trim()), Rotation = double.Parse(data[2].Trim(), System.Globalization.CultureInfo.InvariantCulture), Direction = data[3].Trim(), LineNumber = int.Parse(data[4].Trim()), StartPos = double.Parse(data[5].Trim(), System.Globalization.CultureInfo.InvariantCulture), TargetPos = double.Parse(data[6].Trim(), System.Globalization.CultureInfo.InvariantCulture)
                        };
                        long m = long.Parse(data[7].Trim()), num = long.Parse(data[8].Trim()), den = long.Parse(data[9].Trim());
                        rawEvents.Add(new RawEvent { Type = "FREEKEY", Measure = m, Num = num, Den = den, Value = JsonUtility.ToJson(fNote) });
                    }
                    else if (data.Length >= 9) { 
                        var fNote = new NoteData {
                            IsFreeKey = true, FreeKeyType = int.Parse(data[1].Trim()), Rotation = double.Parse(data[2].Trim(), System.Globalization.CultureInfo.InvariantCulture), Direction = data[3].Trim(), LineNumber = 1, StartPos = double.Parse(data[4].Trim(), System.Globalization.CultureInfo.InvariantCulture), TargetPos = double.Parse(data[5].Trim(), System.Globalization.CultureInfo.InvariantCulture)
                        };
                        long m = long.Parse(data[6].Trim()), num = long.Parse(data[7].Trim()), den = long.Parse(data[8].Trim());
                        rawEvents.Add(new RawEvent { Type = "FREEKEY", Measure = m, Num = num, Den = den, Value = JsonUtility.ToJson(fNote) });
                    }
                }
                else {
                    string val = string.Join(",", data.Skip(4).Select(s => s.Trim()));
                    rawEvents.Add(new RawEvent { Type = type, Measure = long.Parse(data[1].Trim()), Num = long.Parse(data[2].Trim()), Den = long.Parse(data[3].Trim()), Value = val });
                }
            }
            else if (data.Length >= 4) rawEvents.Add(new RawEvent { Type = type, Measure = long.Parse(data[1].Trim()), Num = long.Parse(data[2].Trim()), Den = 4, Value = data[3].Trim() });
        }

        List<BpmChange> tempBpms = new List<BpmChange>();
        tempBpms.Add(new BpmChange { Beats = 0, Time = 0.0, Bpm = initialBpm }); 

        var bpmEvts = rawEvents.Where(e => e.Type == "BPM").OrderBy(e => ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0);
        foreach (var b in bpmEvts) {
            double beats = ((b.Measure - 1) * 4.0) + ((double)b.Num / Math.Max(1, b.Den)) * 4.0; 
            tempBpms.Add(new BpmChange { Beats = beats, Bpm = double.Parse(b.Value, System.Globalization.CultureInfo.InvariantCulture) }); 
        }

        double currentTime = 0.0, currentBeats = 0, currentBpmVal = initialBpm;
        for (int i = 1; i < tempBpms.Count; i++) {
            double timeDiff = (tempBpms[i].Beats - currentBeats) * (60.0 / currentBpmVal);
            tempBpms[i].Time = currentTime + timeDiff;
            currentTime = tempBpms[i].Time; currentBeats = tempBpms[i].Beats; currentBpmVal = tempBpms[i].Bpm;
        }
        bpmChanges = tempBpms.OrderBy(b => b.Time).ToList();

        double GetExactTime(double beats) {
            var lastBpm = bpmChanges.LastOrDefault(b => b.Beats <= beats + 0.000001) ?? bpmChanges[0];
            return lastBpm.Time + (beats - lastBpm.Beats) * (60.0 / lastBpm.Bpm);
        }

        double[] currentSpeedByLayer = new double[] { 0.0, initialSpeed, initialSpeed };
        for (int L = 1; L <= 2; L++) speedChangesByLayer[L].Add(new SpeedChange { Time = -999999.0, Multiplier = initialSpeed });

        foreach (var e in rawEvents.Where(e => e.Type != "BPM")) {
            double absoluteBeats = ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0;
            double exactTime = GetExactTime(absoluteBeats);

            if (e.Type == "SPEED") {
                // 형식1(구형): SPEED, 마디, 분자, 분모, 속도           → 1층/2층 모두
                // 형식2(신형): SPEED, 마디, 분자, 분모, 층, 속도       → 해당 층만
                ParseSpeedEventValue(e.Value, out int spdLayer, out double spdValue);
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
            else if (e.Type == "FREEKEY") {
                NoteData fNote = JsonUtility.FromJson<NoteData>(e.Value);
                fNote.HitTime = exactTime; fNote.IsHit = false; notes.Add(fNote);
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
        notes = notes.OrderBy(n => n.HitTime).ToList();
        for (int i = 0; i < 4; i++) notesByLane[i] = notesByLane[i].OrderBy(n => n.HitTime).ThenByDescending(n => n.Layer).ToList();

        // 층별 스폰 리스트 (프리키 노트는 기본 Layer = 2)
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
                if (note.IsRedNote && note.Lane != lane) continue;
                int ly = Mathf.Clamp(note.Layer, 1, 2);

                if (note.Type == 2 || note.Type == 12) lastLinkNode[ly] = note;
                else if ((note.Type == 3 || note.Type == 13) && lastLinkNode[ly] != null) {
                    if (lastLinkNode[ly].IsRedNote == note.IsRedNote) lastLinkNode[ly].NextLongNote = note;
                    lastLinkNode[ly] = null;
                }
            }
        }

        // 점수 계산용: 완벽하게 쳤을 때 ProcessHit 이 불리는 횟수 (롱노트 중간 틱 제외)
        chartJudgeCount = 0; chartLastTime = 0; chartEndCursor = 0;
        foreach (var n in notes) {
            if (n.HitTime > chartLastTime) chartLastTime = n.HitTime;
            if (n.IsFreeKey || n.Type == 1 || n.Type == 11) chartJudgeCount++;
            else if (n.Type == 2 || n.Type == 12) chartJudgeCount += (n.NextLongNote != null) ? 2 : 1;
        }

        double maxBeat = 0;
        if (rawEvents.Count > 0) {
            maxBeat = rawEvents.Max(e => ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0);
        }
        allMeasureTimes.Clear();
        for (double b = 0; b <= maxBeat + 4.0; b += 4.0) {
            allMeasureTimes.Add(GetExactTime(b));
        }
        allMeasureTimes.Sort();
        measureSpawnIndex = 0;
    }

    // 토큰("11_3", "2" ...) 하나를 노트로 만들어 등록한다.
    private void AddNoteFromToken(string token, int lane, double hitTime, int layer)
    {
        if (lane < 0 || lane >= 4) return;
        if (!NoteCellParser.TryParseToken(token, out int type, out int width, out bool isRed)) return;

        var newNote = new NoteData { HitTime = hitTime, Lane = lane, Type = type, IsHit = false, isTimeLogged = false, Layer = layer };
        if (isRed) { newNote.IsRedNote = true; newNote.Width = width; }
        notes.Add(newNote);

        if (isRed) {
            for (int w = 0; w < width; w++) {
                if (lane + w < 4) notesByLane[lane + w].Add(newNote);
            }
        } else {
            notesByLane[lane].Add(newNote);
        }
    }

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
        
        var ev = scrollEvents.LastOrDefault(e => e.Time <= targetTime + 1e-6) ?? scrollEvents[0];
        double dt = targetTime - ev.Time;
        var spd = speedChanges.LastOrDefault(s => s.Time <= targetTime + 1e-6)?.Multiplier ?? 1.0;
        
        return (ev.Multiplier + dt * spd) * baseScrollSpeed * speedMultiplier;
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
        var lastBpm = bpmChanges.LastOrDefault(b => b.Time <= time + 1e-6);
        return lastBpm != null ? lastBpm.Bpm : bpmChanges[0].Bpm;
    }

    public void PauseGame()
    {
        if (currentState != GameState.Playing) return;
        currentState = GameState.Paused;
        
        if (bgmSource != null && bgmSource.isPlaying) bgmSource.Pause();
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
            
            EvaluateLineStates();
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
        
        double triggerTime = songStartOffset - CoreManager.Instance.CurrentSettings.AudioOffset;
        if (currentPlayTime >= triggerTime)
        {
            if (bgmSource != null && bgmSource.clip != null)
            {
                bgmSource.time = (float)(currentPlayTime - triggerTime);
                bgmSource.volume = 0f;
                bgmSource.Play();
                
                dspStartTime = AudioSettings.dspTime - (currentPlayTime - triggerTime);
                StartCoroutine(FadeInAudioRoutine(1.0f));
            }
            if (mvPlayer != null && CoreManager.Instance.IsMvOn)
            {
                mvPlayer.time = currentPlayTime - triggerTime;
                mvPlayer.Play();
            }
            isFullMusicStarted = true; 
        }
        else 
        {
            isFullMusicStarted = false; 
        }
    }

    private IEnumerator FadeInAudioRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(0f, 1f, elapsed / duration);
            yield return null;
        }
        bgmSource.volume = 1f;
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

    void Update() 
    {
        if (isLeaving) return;
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        
        var kb = Keyboard.current;
        if (kb != null && kb[Key.Escape].wasPressedThisFrame)
        {
            if (currentState == GameState.Playing) PauseGame();
        }

        if (currentState != GameState.Playing) 
        {
            if (currentState != GameState.Rewinding) return;
        }
        
        if (currentState == GameState.Playing)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.05f) dt = 0.05f; 

            double triggerTime = songStartOffset - CoreManager.Instance.CurrentSettings.AudioOffset;

            if (!isFullMusicStarted) 
            {
                currentPlayTime += dt; 
                if (currentPlayTime >= triggerTime) 
                {
                    double overshoot = currentPlayTime - triggerTime;
                    if (bgmSource != null && bgmSource.clip != null) {
                        if (overshoot > 0) bgmSource.time = (float)overshoot;
                        bgmSource.Play();
                        dspStartTime = AudioSettings.dspTime - overshoot; 
                    }
                    if (mvPlayer != null && !string.IsNullOrEmpty(mvPlayer.url) && CoreManager.Instance.IsMvOn) {
                        if (overshoot > 0) mvPlayer.time = overshoot;
                        mvPlayer.Play();
                    }
                    isFullMusicStarted = true;
                }
            }
            else 
            {
                if (bgmSource != null && bgmSource.isPlaying) 
                {
                    double audioPlayedTime = AudioSettings.dspTime - dspStartTime;
                    double targetVisualTime = triggerTime + audioPlayedTime;
                    double diff = targetVisualTime - currentPlayTime;
                    
                    if (Math.Abs(diff) > 0.1) {
                        currentPlayTime = targetVisualTime; 
                    } 
                    else {
                        float timeScale = 1.0f + (float)(diff * 2.0); 
                        timeScale = Mathf.Clamp(timeScale, 0.5f, 1.5f);
                        currentPlayTime += dt * timeScale; 
                    }

                    if (CoreManager.Instance.IsMvOn && mvPlayer != null && mvPlayer.isPlaying) {
                        if (bgRenderer != null) bgRenderer.color = Color.white;
                    }
                }
                else 
                {
                    currentPlayTime += dt;
                }
            }
        }

        ProcessUIAndLinearEvents();
        EvaluateLineStates();

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

        if (currentState == GameState.Playing)
        {
            if (CoreManager.Instance.IsAutoPlay) HandleAutoPlay(currentPlayTime);
            else HandleInputs(currentPlayTime);
        }

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
            
            if (fkComboImageContainer != null) {
                fkComboImageContainer.localScale = initialComboScale * scale;
                fkComboImageContainer.anchoredPosition = initialFkComboPos + new Vector2(0, yOffset);
                foreach(var img in fkComboDigits) {
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
            if (fkComboImageContainer != null) fkComboImageContainer.gameObject.SetActive(false);
        }
    }

    private void EvaluateLineStates()
    {
        foreach (var line in activeLines.Values) 
        {
            if (line.posStartTime >= 0) {
                double elapsed = currentPlayTime - line.posStartTime;
                if (elapsed >= line.posDuration) { line.go.transform.position = line.targetPos; line.posStartTime = -1; }
                else if (elapsed >= 0) { line.go.transform.position = Vector3.LerpUnclamped(line.startPos, line.targetPos, (float)(elapsed / line.posDuration)); }
            }
            if (line.rotStartTime >= 0) {
                double elapsed = currentPlayTime - line.rotStartTime;
                if (elapsed >= line.rotDuration) { line.go.transform.rotation = Quaternion.Euler(0, 0, line.targetRot); line.rotStartTime = -1; }
                else if (elapsed >= 0) { 
                    float z = Mathf.LerpUnclamped(line.startRot, line.targetRot, (float)(elapsed / line.rotDuration));
                    line.go.transform.rotation = Quaternion.Euler(0, 0, z); 
                }
            }
            if (line.alphaStartTime >= 0) {
                double elapsed = currentPlayTime - line.alphaStartTime;
                if (elapsed >= line.alphaDuration) { 
                    Color c = line.sr.color; c.a = line.targetAlpha; line.sr.color = c; line.alphaStartTime = -1; 
                }
                else if (elapsed >= 0) { 
                    Color c = line.sr.color; c.a = Mathf.LerpUnclamped(line.startAlpha, line.targetAlpha, (float)(elapsed / line.alphaDuration)); line.sr.color = c; 
                }
            }
        }
    }

    private void ProcessUIAndLinearEvents()
    {
        foreach (var e in uiEvents.Where(x => currentPlayTime >= x.StartTime))
        {
            if (e.TargetUI == "BasicUI" && basicUIContainer != null) {
                if (e.Action == "exposure") basicUIContainer.SetActive(true);
                CanvasGroup cg = basicUIContainer.GetComponent<CanvasGroup>();
                if (cg == null) cg = basicUIContainer.AddComponent<CanvasGroup>();
                StartCoroutine(FadeCanvasGroup(cg, e.Action == "exposure" ? 1f : 0f, (float)e.Duration, basicUIContainer));
            } 
            else if (e.TargetUI == "FREEKEYUI" && freeKeyUIContainer != null) {
                freeKeyUIContainer.SetActive(true);
                bool isExposure = (e.Action == "exposure");
                StartCoroutine(AnimateFreeKeyUI(isExposure, (float)e.Duration));
            }
        }
        uiEvents.RemoveAll(x => currentPlayTime >= x.StartTime);

        var activeLineEvents = linearEvents.Where(x => currentPlayTime >= x.StartTime || x.StartTime == -999).ToList();
        foreach (var e in activeLineEvents)
        {
            if (e.Action == "add") {
                if (!activeLines.ContainsKey(e.LineNumber)) {
                    GameObject go = new GameObject($"Linear_{e.LineNumber}");
                    SceneManager.MoveGameObjectToScene(go, gameObject.scene);
                    SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = freeKeyLinearSprite;
                    sr.sortingOrder = 3;
                    sr.color = new Color(1,1,1,0); 
                    go.transform.position = Vector3.zero; 

                    float screenW = mainCam.orthographicSize * 2f * mainCam.aspect;
                    float screenH = mainCam.orthographicSize * 2f;
                    float maxLength = Mathf.Max(screenW, screenH) * 2.5f; 
                    go.transform.localScale = new Vector3(maxLength, 0.5f, 1f); 

                    activeLines[e.LineNumber] = new FreeKeyLineObj { id = e.LineNumber, go = go, sr = sr, startPos = Vector3.zero, targetPos = Vector3.zero };
                }
            }
            else if (e.Action == "del" && activeLines.ContainsKey(e.LineNumber)) {
                Destroy(activeLines[e.LineNumber].go);
                activeLines.Remove(e.LineNumber);
            }
            else if (activeLines.ContainsKey(e.LineNumber)) {
                var line = activeLines[e.LineNumber];
                if (e.Action == "exposure") {
                    line.startAlpha = line.sr.color.a; line.targetAlpha = 1f;
                    line.alphaStartTime = e.StartTime; line.alphaDuration = e.Duration;
                }
                else if (e.Action == "disappearance") {
                    line.startAlpha = line.sr.color.a; line.targetAlpha = 0f;
                    line.alphaStartTime = e.StartTime; line.alphaDuration = e.Duration;
                }
                else if (e.Action == "rotation") {
                    line.startRot = line.go.transform.rotation.eulerAngles.z; 
                    line.targetRot = (float)e.Value1;
                    line.rotStartTime = e.StartTime; line.rotDuration = e.Duration;
                }
                else if (e.Action == "move") {
                    line.startPos = line.go.transform.position;
                    float screenW = mainCam.orthographicSize * 2f * mainCam.aspect;
                    float screenH = mainCam.orthographicSize * 2f;
                    float targetX = ((float)e.Value1 / 100f) * (screenW / 2f);
                    float targetY = ((float)e.Value2 / 50f) * (screenH / 2f);
                    line.targetPos = new Vector3(targetX, targetY, 0);
                    line.posStartTime = e.StartTime; line.posDuration = e.Duration;
                }
            }
        }
        linearEvents.RemoveAll(x => currentPlayTime >= x.StartTime || x.StartTime == -999);
    }

    IEnumerator FadeCanvasGroup(CanvasGroup cg, float targetAlpha, float duration, GameObject container) {
        if (!cg) yield break;
        float startAlpha = cg.alpha; float t = 0;
        while(t < duration) { t += Time.deltaTime; cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, t/duration); yield return null; }
        cg.alpha = targetAlpha;
        if (targetAlpha <= 0.01f && container != null) container.SetActive(false);
    }

    IEnumerator AnimateFreeKeyUI(bool isExposure, float duration) {
        float t = 0;
        Vector2 infoOff = new Vector2(-500, 0);
        Vector2 levelOff = new Vector2(500, 0); Vector2 comboOff = new Vector2(0, 500);
        
        while (t < duration) {
            t += Time.deltaTime;
            float progress = isExposure ? (t / duration) : (1f - (t / duration));
            float curve = 1f - Mathf.Pow(1f - progress, 3f); 
            
            if (fkInfoUI) fkInfoUI.anchoredPosition = Vector2.Lerp(infoOff, Vector2.zero, curve);
            if (fkLevelUI) fkLevelUI.anchoredPosition = Vector2.Lerp(levelOff, Vector2.zero, curve);
            if (fkComboUI) fkComboUI.anchoredPosition = Vector2.Lerp(comboOff, Vector2.zero, curve);
            yield return null;
        }
        if (!isExposure && freeKeyUIContainer != null) freeKeyUIContainer.SetActive(false);
    }

    // ===== 층별 롱노트 슬롯 헬퍼 =====
    private int SlotBase(NoteData n) { return n.Layer == NoteLayer.Bottom ? 4 : 0; }
    private int CoveredLaneCount(NoteData n) { return n.IsRedNote ? n.Width : 1; }

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
            StartHoldEffect(l);
        }
    }

    private void ClearActiveLongNote(NoteData n)
    {
        int b = SlotBase(n);
        int cnt = CoveredLaneCount(n);
        for (int k = 0; k < cnt; k++) {
            int l = n.Lane + k;
            if (l >= 4) continue;
            if (activeLongNote[b + l] == n) activeLongNote[b + l] = null;
            if (!IsLaneHeldByLongNote(l)) StopHoldEffect(l);
        }
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
                    
                    bool isNormalNote = (n.Type == 1 || n.Type == 11);
                    bool isTail = (n.Type == 3 || n.Type == 13);
                    
                    if (isNormalNote) {
                        double randomDiff = (UnityEngine.Random.Range(-30f, 30f)) / 1000.0;
                        ApplyJudgment(randomDiff);
                    } else {
                        ProcessHit("EXCELLENT", 0.0, true);
                    }
                    
                    if (n.IsRedNote) {
                        for (int w = 0; w < n.Width; w++) {
                            int l = n.Lane + w;
                            if (l < 4) {
                                SpawnHitEffect(l);
                                StartCoroutine(AutoPlayLaneVisual(l, isTail));
                            }
                        }
                    } else {
                        SpawnHitEffect(i);
                        StartCoroutine(AutoPlayLaneVisual(i, isTail));
                    }

                    if (n.Type == 2 || n.Type == 12)
                    {
                        double currentBpm = GetBpmAtTime(n.HitTime);
                        double ticksPerBeat = (currentBpm <= 200.0) ? 3.0 : ((currentBpm <= 300.0) ? 2.0 : 1.0);
                        double tickInterval = (60.0 / currentBpm) / ticksPerBeat;
                        RegisterActiveLongNote(n, tickInterval);
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

            if (!ln.IsRedNote || ln.Lane == lane) ProcessLongNoteTick(s, T_sys);

            if (T_sys >= GetLongNoteEndTime(ln)) FinishLongNote(ln);
        }

        var activeFreeNotes = notes.Where(n => n.IsFreeKey && !n.IsHit && n.HitTime <= T_sys).ToList();
        foreach (var n in activeFreeNotes)
        {
            n.IsHit = true;
            ProcessHit("EXCELLENT", 0.0, true);
            if (sfxSource != null && hitSoundClip != null) sfxSource.PlayOneShot(hitSoundClip);
            HideNoteObject(n);
        }
    }

    private IEnumerator AutoPlayLaneVisual(int lane, bool skipSound = false)
    {
        if (beamRenderers != null && beamRenderers[lane] != null && onSprites != null) beamRenderers[lane].sprite = onSprites[lane];
        if (whiteLights != null && whiteLights[lane] != null) whiteLights[lane].SetActive(true);
        if (!skipSound && sfxSource != null && hitSoundClip != null) sfxSource.PlayOneShot(hitSoundClip);

        yield return new WaitForSeconds(0.05f);

        if (!IsLaneHeldByLongNote(lane)) {
            if (beamRenderers != null && beamRenderers[lane] != null && offSprites != null) beamRenderers[lane].sprite = offSprites[lane];
            if (whiteLights != null && whiteLights[lane] != null) whiteLights[lane].SetActive(false);
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

            if (wasReleased) 
            {
                if (beamRenderers != null && beamRenderers[i] != null && offSprites != null) beamRenderers[i].sprite = offSprites[i];
                if (whiteLights != null && whiteLights[i] != null) whiteLights[i].SetActive(false);
                StopHoldEffect(i);
                
                // 이 레인의 2층/1층 롱노트를 각각 검사
                for (int layerSlot = 0; layerSlot < 2; layerSlot++)
                {
                    NoteData ln = activeLongNote[i + layerSlot * 4];
                    if (ln == null) continue;
                    if (IsLongNoteHeld(ln)) continue; // 와이드 빨간 노트: 다른 레인 키가 아직 눌려 있음

                    double endTime = GetLongNoteEndTime(ln);
                    if (endTime - T_sys <= 0.15) FinishLongNote(ln, SignedMs(Math.Max(0.0, endTime - (T_sys - CoreManager.Instance.CurrentSettings.JudgeOffset))));
                    else SetLongNoteReleaseTime(ln, T_sys);
                }
            }

            if (wasPressed) 
            {
                if (beamRenderers != null && beamRenderers[i] != null && onSprites != null) beamRenderers[i].sprite = onSprites[i];
                if (whiteLights != null && whiteLights[i] != null) whiteLights[i].SetActive(true);
                if (sfxSource != null && hitSoundClip != null) sfxSource.PlayOneShot(hitSoundClip);
                
                EvaluateHit(i, T_sys);
                
                if (IsLaneHeldByLongNote(i)) StartHoldEffect(i);
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
                if (!ln.IsRedNote || ln.Lane == lane) ProcessLongNoteTick(s, T_sys);
                SetLongNoteReleaseTime(ln, 0);
            }
            else if (T_sys - lnReleaseTime[s] > 0.2) 
            {
                FailLongNote(ln);
                continue;
            }

            if (activeLongNote[s] == ln && T_sys >= endTime) FinishLongNote(ln);
        }

        int currentKeysPressedCount = 0;
        foreach (var keyControl in kb.allKeys)
        {
            switch (keyControl.keyCode) {
                case Key.Tab: case Key.CapsLock: case Key.LeftCtrl: case Key.RightCtrl:
                case Key.LeftAlt: case Key.RightAlt: case Key.LeftShift: case Key.RightShift:
                case Key.Enter: case Key.NumpadEnter: case Key.Escape: 
                case Key.LeftWindows: case Key.RightWindows: 
                    continue;
            }

            if (keyControl.isPressed) currentKeysPressedCount++;
        }

        bool isValidFreeKey = currentKeysPressedCount > prevKeysPressedCount;
        bool isAnyKeyCurrentlyHeld = currentKeysPressedCount > 0;
        prevKeysPressedCount = currentKeysPressedCount;

        bool isSpaceCurrentlyPressed = kb.spaceKey.isPressed;
        bool isSpaceKey = isSpaceCurrentlyPressed && !prevSpaceState;
        prevSpaceState = isSpaceCurrentlyPressed;

        if (isValidFreeKey || isAnyKeyCurrentlyHeld) 
            EvaluateFreeKey(T_sys, isValidFreeKey, isSpaceKey, isAnyKeyCurrentlyHeld);
    }

    private void FinishLongNote(NoteData head, double tailErrorMs = 0.0)
    {
        NoteData tailNote = head.NextLongNote;
        if (tailNote != null && !tailNote.IsHit) {
            tailNote.IsHit = true; 
            ProcessHit("EXCELLENT", tailErrorMs, true); 
            
            int cnt = CoveredLaneCount(head);
            for (int w = 0; w < cnt; w++) {
                if (head.Lane + w < 4) SpawnHitEffect(head.Lane + w);
            }
            HideNoteObject(tailNote);
        }
        ClearActiveLongNote(head);
    }

    private void EvaluateFreeKey(double hitTimestamp, bool pressedThisFrame, bool spacePressed, bool isHeld)
    {
        double adjustedHitTime = hitTimestamp - CoreManager.Instance.CurrentSettings.JudgeOffset;
        var activeFreeNotes = notes.Where(n => n.IsFreeKey && !n.IsHit && Math.Abs(n.HitTime - adjustedHitTime) <= W_BAD).ToList();

        foreach (var n in activeFreeNotes)
        {
            bool hitConditionMet = false;
            if (n.FreeKeyType == 0 && pressedThisFrame) hitConditionMet = true;
            else if (n.FreeKeyType == 1 && spacePressed) hitConditionMet = true;
            else if (n.FreeKeyType == 2) 
            {
                if (pressedThisFrame) hitConditionMet = true; 
                else if (isHeld && (n.HitTime - adjustedHitTime) <= 0) hitConditionMet = true; 
            }

            if (hitConditionMet)
            {
                n.IsHit = true;
                ApplyJudgment(n.HitTime - adjustedHitTime);
                if (sfxSource != null && hitSoundClip != null) sfxSource.PlayOneShot(hitSoundClip);
                HideNoteObject(n);
                break; 
            }
        }
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
        double adjustedHitTime = hitTimestamp - CoreManager.Instance.CurrentSettings.JudgeOffset;
        NoteData targetNote = null;
        double minAbsDiff = double.MaxValue;
        int startIndex = laneStartIndex[lane];
        var currentLaneNotes = notesByLane[lane];

        for (int i = startIndex; i < currentLaneNotes.Count; i++) 
        {
            var n = currentLaneNotes[i];
            if (n.IsHit || n.IsFreeKey) 
            {
                if (i == laneStartIndex[lane]) laneStartIndex[lane]++;
                continue;
            }
            if (n.Type != 1 && n.Type != 2 && n.Type != 11 && n.Type != 12) continue;

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
            for (int w = 0; w < targetNote.Width; w++) {
                if (targetNote.Lane + w < 4) SpawnHitEffect(targetNote.Lane + w);
            }
        } else {
            SpawnHitEffect(lane); 
        }

        if (targetNote.Type == 2 || targetNote.Type == 12)
        {
            double currentBpm = GetBpmAtTime(targetNote.HitTime);
            double ticksPerBeat = (currentBpm <= 200.0) ? 3.0 : ((currentBpm <= 300.0) ? 2.0 : 1.0);
            double tickInterval = (60.0 / currentBpm) / ticksPerBeat;

            RegisterActiveLongNote(targetNote, tickInterval);
        }
    }

    private void ProcessLongNoteTick(int slot, double T_sys)
    {
        var head = activeLongNote[slot];
        if (head == null) return;

        double endTime = GetLongNoteEndTime(head);
        
        while (T_sys >= nextLongNoteTickTime[slot] && nextLongNoteTickTime[slot] < endTime - 0.05)
        {
            currentCombo++;
            if (currentCombo > maxCombo) maxCombo = currentCombo;
            comboAnimTimer = 1f; 
            
            UpdateUI();

            double currentBpm = GetBpmAtTime(nextLongNoteTickTime[slot]);
            double ticksPerBeat = (currentBpm <= 200.0) ? 3.0 : ((currentBpm <= 300.0) ? 2.0 : 1.0);
            double tickInterval = (60.0 / currentBpm) / ticksPerBeat;
            
            nextLongNoteTickTime[slot] += tickInterval;
        }
    }

    public void DimEntireLongNoteChain(NoteData head)
    {
        NoteData curr = head;
        while (curr != null) 
        {
            curr.IsHit = true; 
            if (noteObjectDict.TryGetValue(curr, out NoteObject obj) && obj != null) obj.DimNote();
            curr = curr.NextLongNote;
        }
    }

    private void HideNoteObject(NoteData data)
    {
        if (noteObjectDict.TryGetValue(data, out NoteObject obj) && obj != null) obj.HideNote();
    }

    private void FailLongNote(NoteData ln)
    {
        ProcessHit("MISS"); 
        DimEntireLongNoteChain(ln);
        ClearActiveLongNote(ln);
    }

    public void MissLongNoteHead(NoteData headNote)
    {
        ProcessHit("MISS"); 
        DimEntireLongNoteChain(headNote);
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

        if (msDiff >= CoreManager.Instance.CurrentSettings.judgeoverrange && absDiff <= W_BAD)
        {
            if (fastSlowDirectionText != null && fastSlowValueText != null)
            {
                bool isFast = diff > 0;
                fastSlowDirectionText.text = isFast ? "FAST" : "SLOW";
                fastSlowDirectionText.color = isFast ? new Color(0.53f, 0.81f, 0.92f) : Color.red; 
                fastSlowValueText.text = msDiff.ToString();
                fastSlowValueText.color = Color.white;
                
                fastSlowDirectionText.gameObject.SetActive(true);
                fastSlowValueText.gameObject.SetActive(true);
                fastSlowAnimTimer = 0.45f;
            }
        }
    }

    public void ProcessHit(string judge, double signedErrorMs = 0.0, bool hasTiming = false)
    {
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

        currentAccSum += accMultiplier;

        switch (judge) {
            case "EXCELLENT": cntExcellent++; break;
            case "GREAT": cntGreat++; break;
            case "GOOD": cntGood++; break;
            case "BAD": cntBad++; break;
            case "MISS": cntMiss++; break;
        }
        if (hasTiming && judge != "MISS") { errorSumMs += signedErrorMs; timedCount++; }

        UpdateUI();
        ShowJudgmentImage(judge);
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
        foreach (var line in activeLines.Values) if (line != null && line.go != null) Destroy(line.go);
        activeLines.Clear();
    }

    private void ResetStats()
    {
        processedNotes = 0; currentCombo = 0; maxCombo = 0; currentAccSum = 0;
        cntExcellent = cntGreat = cntGood = cntBad = cntMiss = 0;
        errorSumMs = 0; timedCount = 0;
        chartJudgeCount = 0; chartLastTime = 0; chartEndCursor = 0;
        finishHoldTimer = 0f; finishRoutineStarted = false;
    }

    // =====================================================================
    //  Pause UI (약간 늦게, 부드럽게 등장)
    // =====================================================================
    private IEnumerator ShowPauseUIRoutine()
    {
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
        return n.IsFreeKey || n.Type == 1 || n.Type == 2 || n.Type == 3 || n.Type == 11 || n.Type == 12 || n.Type == 13;
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

    private bool IsSongFinished()
    {
        if (bgmSource == null || bgmSource.clip == null) return currentPlayTime >= chartLastTime + 3.0;
        if (!isFullMusicStarted) return false;

        double triggerTime = songStartOffset - CoreManager.Instance.CurrentSettings.AudioOffset;
        return !bgmSource.isPlaying && currentPlayTime >= triggerTime + bgmSource.clip.length - 0.25;
    }

    private bool IsMvFinished()
    {
        if (!mvActive || mvFinished) return true;

        // 안전장치: 종료 이벤트가 오지 않아도 영상 길이를 넘으면 종료로 본다
        if (isFullMusicStarted && mvPlayer != null && mvPlayer.length > 0.0) {
            double triggerTime = songStartOffset - CoreManager.Instance.CurrentSettings.AudioOffset;
            if (currentPlayTime - triggerTime >= mvPlayer.length + 1.0) return true;
        }
        return false;
    }

    private void UpdateFinishCondition()
    {
        if (currentState != GameState.Playing || finishRoutineStarted) return;

        if (IsChartFinished() && IsSongFinished() && IsMvFinished()) {
            finishHoldTimer += Time.unscaledDeltaTime;
            if (finishHoldTimer >= resultDelaySeconds) {
                finishRoutineStarted = true;
                StartCoroutine(FinishToResultRoutine());
            }
        } else {
            finishHoldTimer = 0f;
        }
    }

    private ResultData BuildResultData()
    {
        int levelId = CoreManager.Instance.CurrentLevelId;

        double acc = processedNotes == 0 ? 100.0 : (currentAccSum / processedNotes) * 100.0;

        int score = 0;
        if (chartJudgeCount > 0) score = Mathf.Clamp((int)Math.Round(MaxScore * currentAccSum / chartJudgeCount), 0, MaxScore);

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
            AverageErrorMs = avgError,
            ErrorSampleCount = denom,
            IsAutoPlay = CoreManager.Instance.IsAutoPlay
        };
    }

    private IEnumerator FinishToResultRoutine()
    {
        currentState = GameState.Finished;
        isLeaving = true;
        for (int i = 0; i < 4; i++) StopHoldEffect(i);

        ResultData result = BuildResultData();

        yield return FadeBlackOverlay(resultFadeDuration, resultTransitionOverlay);

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
            if (fkComboImageContainer != null) { 
                fkComboImageContainer.gameObject.SetActive(true); 
                UpdateComboImages(currentCombo, fkComboImageContainer, fkComboDigits);
            }
            
            comboAnimTimer = 1f; 
        } else {
            if (comboTitleImage != null) comboTitleImage.gameObject.SetActive(false);
            if (comboImageContainer != null) comboImageContainer.gameObject.SetActive(false);
            if (fkComboImageContainer != null) fkComboImageContainer.gameObject.SetActive(false);
        }

        if (accuracyText != null) accuracyText.text = $"{(processedNotes == 0 ? 100.0 : (currentAccSum / processedNotes) * 100.0):F2}%";
    }

    private void UpdateComboImages(int combo, RectTransform container, List<UnityEngine.UI.Image> digitList)
    {
        if (container == null) return;
        
        string comboStr = combo.ToString();
        int digitCount = comboStr.Length;
        
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
            char c = comboStr[i];
            
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