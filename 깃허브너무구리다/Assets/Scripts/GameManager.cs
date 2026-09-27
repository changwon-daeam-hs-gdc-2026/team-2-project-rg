using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.InputSystem;
using TMPro;

public enum GameState { Loading, Playing, Paused, Rewinding } 

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
    
    private double dspStartTime = 0; 
    private bool isFullMusicStarted = false;
    public double currentScrollDist = 0.0;

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

    private NoteData[] activeLongNote = new NoteData[4]; 
    private double[] nextLongNoteTickTime = new double[4]; 
    private double[] lnReleaseTime = new double[4]; 

    private List<NoteData> notes = new List<NoteData>();
    private List<SpeedChange> speedChanges = new List<SpeedChange>();
    private List<BpmChange> bpmChanges = new List<BpmChange>();
    private List<UIEventData> uiEvents = new List<UIEventData>();
    private List<LinearEventData> linearEvents = new List<LinearEventData>();
    private List<GameScrollEvent> scrollEvents = new List<GameScrollEvent>();

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
    private int spawnIndex = 0; 
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

        if (pauseUIContainer != null) pauseUIContainer.SetActive(false);
        if (blackOverlayGroup != null) {
            blackOverlayGroup.alpha = 0f;
            blackOverlayGroup.gameObject.SetActive(false);
        }
        
        if(btnResume != null) btnResume.onClick.AddListener(ResumeGame);
        if(btnRetry != null) btnRetry.onClick.AddListener(RetryGame);
        if(btnBackToMenu != null) btnBackToMenu.onClick.AddListener(BackToMenu);
    }

    void Start() 
    { 
        mainCam = Camera.main;
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
        spawnIndex = 0;
        for (int i = 0; i < 200; i++) {
            GameObject obj = Instantiate(notePrefab);
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
        if (notePool.Count > 0) return notePool.Dequeue();
        GameObject obj = Instantiate(notePrefab);
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
        currentPlayTime = -3.0; 
        gameStartTime = (double)Time.time;
        currentState = GameState.Playing; 
    }

    private float EaseOut(float x) { return 1f - Mathf.Pow(1f - x, 3f); }

    public void LoadChart(int songId, int levelId) 
    {
        notes.Clear(); speedChanges.Clear(); bpmChanges.Clear(); 
        uiEvents.Clear(); linearEvents.Clear(); scrollEvents.Clear();
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

        speedChanges.Add(new SpeedChange { Time = -999999.0, Multiplier = initialSpeed });
        double currentSpeed = initialSpeed;

        foreach (var e in rawEvents.Where(e => e.Type != "BPM")) {
            double absoluteBeats = ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0;
            double exactTime = GetExactTime(absoluteBeats);

            if (e.Type == "SPEED") {
                currentSpeed = double.Parse(e.Value, System.Globalization.CultureInfo.InvariantCulture);
                speedChanges.Add(new SpeedChange { Time = exactTime, Multiplier = currentSpeed });
            }
            else if (e.Type == "STOP") { speedChanges.Add(new SpeedChange { Time = exactTime, Multiplier = 0.0 }); }
            else if (e.Type == "START") { speedChanges.Add(new SpeedChange { Time = exactTime, Multiplier = currentSpeed }); }
            else if (e.Type == "FREEKEY") {
                NoteData fNote = JsonUtility.FromJson<NoteData>(e.Value);
                fNote.HitTime = exactTime; fNote.IsHit = false; notes.Add(fNote);
            }
            else if (e.Type == "NOTE") {
                string[] lanesData = e.Value.Split(',');
                
                if (lanesData.Length == 1 && !lanesData[0].Contains("_")) {
                    string val = lanesData[0].Trim();
                    for (int lane = 0; lane < val.Length && lane < 4; lane++) {
                        if (val[lane] != '0') {
                            int noteType = int.Parse(val[lane].ToString());
                            if (noteType == 6 || noteType == 5) continue;
                            var newNote = new NoteData { HitTime = exactTime, Lane = lane, Type = noteType, IsHit = false, isTimeLogged = false };
                            notes.Add(newNote); notesByLane[lane].Add(newNote); 
                        }
                    }
                } else {
                    for (int lane = 0; lane < lanesData.Length && lane < 4; lane++) {
                        string val = lanesData[lane].Trim();
                        if (val == "0") continue;
                        
                        if (val.Contains("_")) {
                            string[] parts = val.Split('_');
                            int type = int.Parse(parts[0]); int width = int.Parse(parts[1]);
                            var newNote = new NoteData { HitTime = exactTime, Lane = lane, Type = type, IsHit = false, isTimeLogged = false, IsRedNote = true, Width = width };
                            notes.Add(newNote);
                            
                            for(int w = 0; w < width; w++) {
                                if (lane + w < 4) notesByLane[lane + w].Add(newNote);
                            }
                        } else {
                            int type = int.Parse(val);
                            var newNote = new NoteData { HitTime = exactTime, Lane = lane, Type = type, IsHit = false, isTimeLogged = false };
                            notes.Add(newNote); notesByLane[lane].Add(newNote);
                        }
                    }
                }
            }
        }

        speedChanges = speedChanges.OrderBy(s => s.Time).ToList();
        notes = notes.OrderBy(n => n.HitTime).ToList();
        for (int i = 0; i < 4; i++) notesByLane[i] = notesByLane[i].OrderBy(n => n.HitTime).ToList();

        var allTimes = speedChanges.Select(s => s.Time).Distinct().ToList();
        if (!allTimes.Contains(0.0)) allTimes.Add(0.0);
        if (!allTimes.Contains(-999999.0)) allTimes.Add(-999999.0);
        allTimes.Sort();
        
        double currentPos = 0.0; double curSpeedTemp = initialSpeed; double lastTime = allTimes[0];

        foreach (var t in allTimes) {
            double dt = t - lastTime;
            if (dt > 0) currentPos += dt * curSpeedTemp; 
            var s = speedChanges.LastOrDefault(x => x.Time <= t + 1e-6);
            if (s != null) curSpeedTemp = s.Multiplier;
            scrollEvents.Add(new GameScrollEvent { Time = t, Multiplier = currentPos }); 
            lastTime = t;
        }

        for (int lane = 0; lane < 4; lane++) {
            NoteData lastLinkNode = null;
            foreach (var note in notesByLane[lane]) {
                if (note.Type == 2 || note.Type == 12) lastLinkNode = note;
                else if ((note.Type == 3 || note.Type == 13) && lastLinkNode != null) {
                    lastLinkNode.NextLongNote = note; lastLinkNode = null;
                }
            }
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

    public double GetScrollDistance(double targetTime) 
    {
        double speedMultiplier = CoreManager.Instance.CurrentSettings.SettingSpeed / 4.0;
        
        if (scrollEvents.Count == 0) return targetTime * baseScrollSpeed * speedMultiplier;
        
        var ev = scrollEvents.LastOrDefault(e => e.Time <= targetTime + 1e-6) ?? scrollEvents[0];
        double dt = targetTime - ev.Time;
        var spd = speedChanges.LastOrDefault(s => s.Time <= targetTime + 1e-6)?.Multiplier ?? 1.0;
        
        return (ev.Multiplier + dt * spd) * baseScrollSpeed * speedMultiplier;
    }

    public bool IsNoteHiddenByFutureReverseScroll(double currentTime, double noteTime)
    {
        if (noteTime <= currentTime) return false;
        
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
        
        pauseUIContainer.SetActive(true);
    }
    
    public void ResumeGame()
    {
        if (currentState != GameState.Paused) return;
        StartCoroutine(RewindAndResumeRoutine());
    }

    private IEnumerator RewindAndResumeRoutine()
    {
        pauseUIContainer.SetActive(false);
        currentState = GameState.Rewinding;

        var activeNotes = noteObjectDict.Values.ToList();
        foreach (var obj in activeNotes)
        {
            ReturnNoteToPool(obj);
        }
        noteObjectDict.Clear();

        for(int i = 0; i < 4; i++) {
            activeLongNote[i] = null;
            StopHoldEffect(i);
        }

        foreach (var ml in activeMeasureLines) {
            ml.go.SetActive(false);
            measureLinePool.Enqueue(ml.go);
        }
        activeMeasureLines.Clear();

        double targetRewindTime = Math.Max(-3.0, currentPlayTime - 1.0);
        double startRewindTime = currentPlayTime;
        float duration = 0.5f; 
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float ease = 1f - Mathf.Pow(1f - t, 3f); 

            currentPlayTime = Mathf.Lerp((float)startRewindTime, (float)targetRewindTime, ease);
            currentScrollDist = GetScrollDistance(currentPlayTime);
            
            EvaluateLineStates();
            yield return null;
        }

        currentPlayTime = targetRewindTime;
        
        spawnIndex = 0;
        while(spawnIndex < notes.Count && notes[spawnIndex].HitTime < currentPlayTime) {
            spawnIndex++;
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
        CoreManager.Instance.LoadGameScene(currentSong, CoreManager.Instance.IsMvOn, CoreManager.Instance.CurrentLevelId);
    }

    public void BackToMenu()
    {
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

        currentScrollDist = GetScrollDistance(currentPlayTime);
        double maxVisibleDist = 2000.0;

        while (spawnIndex < notes.Count)
        {
            NoteData targetData = notes[spawnIndex];
            double targetDist = GetScrollDistance(targetData.HitTime);
            double distDiff = targetDist - currentScrollDist;

            if (targetData.HitTime <= currentPlayTime + SPAWN_LOOKAHEAD || distDiff <= maxVisibleDist)
            {
                NoteObject noteObj = GetNoteFromPool();
                noteObj.Initialize(targetData);     
                noteObj.gameObject.SetActive(true); 
                noteObjectDict[targetData] = noteObj;
                spawnIndex++; 
            }
            else
            {
                break;
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
                        ProcessHit("EXCELLENT");
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
                        
                        if (n.IsRedNote) {
                            for (int w = 0; w < n.Width; w++) {
                                int l = n.Lane + w;
                                if (l < 4) {
                                    activeLongNote[l] = n;
                                    StartHoldEffect(l);
                                    nextLongNoteTickTime[l] = n.HitTime + tickInterval;
                                }
                            }
                        } else {
                            activeLongNote[i] = n;
                            StartHoldEffect(i);
                            nextLongNoteTickTime[i] = n.HitTime + tickInterval;
                        }
                    }
                    laneStartIndex[i]++;
                }
                else break;
            }

            if (activeLongNote[i] != null)
            {
                if (!activeLongNote[i].IsRedNote || activeLongNote[i].Lane == i) {
                    ProcessLongNoteTick(i, T_sys);
                }
                
                if (T_sys >= GetLongNoteEndTime(activeLongNote[i]))
                {
                    NoteData ln = activeLongNote[i];
                    FinishLongNote(i, ln);
                    
                    if (ln.IsRedNote) {
                        for (int w = 0; w < ln.Width; w++) {
                            int l = ln.Lane + w;
                            if (l < 4) {
                                activeLongNote[l] = null;
                                StopHoldEffect(l);
                            }
                        }
                    }
                }
            }
        }

        var activeFreeNotes = notes.Where(n => n.IsFreeKey && !n.IsHit && n.HitTime <= T_sys).ToList();
        foreach (var n in activeFreeNotes)
        {
            n.IsHit = true;
            ProcessHit("EXCELLENT");
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

        if (activeLongNote[lane] == null) {
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
                
                if (activeLongNote[i] != null)
                {
                    bool stillHeld = false;
                    
                    if (activeLongNote[i].IsRedNote) {
                        for (int w = 0; w < activeLongNote[i].Width; w++) {
                            int checkLane = activeLongNote[i].Lane + w;
                            if (checkLane < 4 && prevKeyState[checkLane]) {
                                stillHeld = true;
                                break;
                            }
                        }
                    }

                    if (!stillHeld) {
                        double endTime = GetLongNoteEndTime(activeLongNote[i]);
                        if (endTime - T_sys <= 0.15) 
                        {
                            NoteData ln = activeLongNote[i];
                            FinishLongNote(i, ln);
                            
                            if (ln.IsRedNote) {
                                for (int w = 0; w < ln.Width; w++) {
                                    int l = ln.Lane + w;
                                    if (l < 4) {
                                        activeLongNote[l] = null;
                                        StopHoldEffect(l);
                                    }
                                }
                            }
                        }
                        else 
                        {
                            if (activeLongNote[i].IsRedNote) {
                                for (int w = 0; w < activeLongNote[i].Width; w++) {
                                    int l = activeLongNote[i].Lane + w;
                                    if (l < 4) lnReleaseTime[l] = T_sys;
                                }
                            } else {
                                lnReleaseTime[i] = T_sys;
                            }
                        }
                    }
                }
            }

            if (wasPressed) 
            {
                if (beamRenderers != null && beamRenderers[i] != null && onSprites != null) beamRenderers[i].sprite = onSprites[i];
                if (whiteLights != null && whiteLights[i] != null) whiteLights[i].SetActive(true);
                if (sfxSource != null && hitSoundClip != null) sfxSource.PlayOneShot(hitSoundClip);
                
                EvaluateHit(i, T_sys);
                
                if (activeLongNote[i] != null)
                {
                    StartHoldEffect(i);
                }
            }
        }
        
        for (int i = 0; i < 4; i++)
        {
            if (activeLongNote[i] != null)
            {
                NoteData currentLN = activeLongNote[i];
                double endTime = GetLongNoteEndTime(currentLN);
                bool isHeld = prevKeyState[i]; 
                
                if (currentLN.IsRedNote) {
                    isHeld = false;
                    for (int w = 0; w < currentLN.Width; w++) {
                        int checkLane = currentLN.Lane + w;
                        if (checkLane < 4 && prevKeyState[checkLane]) {
                            isHeld = true;
                            break;
                        }
                    }
                }
                
                if (isHeld) 
                {
                    if (!currentLN.IsRedNote || currentLN.Lane == i) {
                        ProcessLongNoteTick(i, T_sys);
                    }
                    
                    if (currentLN.IsRedNote) {
                        for (int w = 0; w < currentLN.Width; w++) {
                            int l = currentLN.Lane + w;
                            if (l < 4) lnReleaseTime[l] = 0;
                        }
                    } else {
                        lnReleaseTime[i] = 0;
                    }
                }
                else 
                {
                    if (T_sys - lnReleaseTime[i] > 0.2) 
                    {
                        FailLongNote(i);
                        if (currentLN.IsRedNote) {
                            for (int w = 0; w < currentLN.Width; w++) {
                                int l = currentLN.Lane + w;
                                if (l < 4 && activeLongNote[l] == currentLN) {
                                    activeLongNote[l] = null;
                                    StopHoldEffect(l);
                                }
                            }
                        }
                    }
                }
                
                if (activeLongNote[i] != null && T_sys >= endTime)
                {
                    FinishLongNote(i, currentLN);
                    if (currentLN.IsRedNote) {
                        for (int w = 0; w < currentLN.Width; w++) {
                            int l = currentLN.Lane + w;
                            if (l < 4 && activeLongNote[l] == currentLN) {
                                activeLongNote[l] = null;
                                StopHoldEffect(l);
                            }
                        }
                    }
                }
            }
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

    private void FinishLongNote(int lane, NoteData head)
    {
        NoteData tailNote = head.NextLongNote;
        if (tailNote != null && !tailNote.IsHit) {
            tailNote.IsHit = true; 
            ProcessHit("EXCELLENT"); 
            
            if (head.IsRedNote) {
                for (int w = 0; w < head.Width; w++) {
                    if (head.Lane + w < 4) SpawnHitEffect(head.Lane + w);
                }
            } else {
                SpawnHitEffect(lane);
            }
            HideNoteObject(tailNote);
        }
        StopHoldEffect(lane); 
        activeLongNote[lane] = null; 
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

            if (targetNote.IsRedNote) {
                for (int w = 0; w < targetNote.Width; w++) {
                    int l = targetNote.Lane + w;
                    if (l < 4) {
                        activeLongNote[l] = targetNote;
                        StartHoldEffect(l);
                        nextLongNoteTickTime[l] = targetNote.HitTime + tickInterval;
                    }
                }
            } else {
                activeLongNote[lane] = targetNote;
                StartHoldEffect(lane); 
                nextLongNoteTickTime[lane] = targetNote.HitTime + tickInterval;
            }
        }
    }

    private void ProcessLongNoteTick(int lane, double T_sys)
    {
        var head = activeLongNote[lane];
        if (head == null) return;

        double endTime = GetLongNoteEndTime(head);
        
        while (T_sys >= nextLongNoteTickTime[lane] && nextLongNoteTickTime[lane] < endTime - 0.05)
        {
            currentCombo++;
            if (currentCombo > maxCombo) maxCombo = currentCombo;
            comboAnimTimer = 1f; 
            
            UpdateUI();

            double currentBpm = GetBpmAtTime(nextLongNoteTickTime[lane]);
            double ticksPerBeat = (currentBpm <= 200.0) ? 3.0 : ((currentBpm <= 300.0) ? 2.0 : 1.0);
            double tickInterval = (60.0 / currentBpm) / ticksPerBeat;
            
            nextLongNoteTickTime[lane] += tickInterval;
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

    private void FailLongNote(int lane)
    {
        ProcessHit("MISS"); 
        StopHoldEffect(lane); 
        DimEntireLongNoteChain(activeLongNote[lane]);
        activeLongNote[lane] = null;
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

        if (absDiff <= W_EXCELLENT) ProcessHit("EXCELLENT");
        else if (absDiff <= W_GREAT) ProcessHit("GREAT");
        else if (absDiff <= W_GOOD) ProcessHit("GOOD");
        else ProcessHit("BAD");

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

    public void ProcessHit(string judge)
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

        UpdateUI();
        ShowJudgmentImage(judge);
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