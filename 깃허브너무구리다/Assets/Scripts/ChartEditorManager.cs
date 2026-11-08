using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro; 
using System.Globalization; 
using UnityEngine.EventSystems; 

public class EditorNote 
{
    public long Measure; public long Num; public long Den; 
    public int Lane; public int Type; 
    public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0;
    public GameObject VisualObject;

    public bool IsRedNote = false;
    public int Width = 1;

    public int Layer = NoteLayer.Top;   // 1 = 1층(하단, 가려짐) / 2 = 2층(상단)
    public float BaseAlpha = 1f;        // 스프라이트 기본 알파 (층 투명도 계산용)
    public SpriteRenderer Sr;
}

// 레인 사라짐 / 드러남 (예전 이름 접기 / 펼치기)  →  FOLD (= HIDE) / UNFOLD (= SHOW), 마디, 분자, 분모, 초
public class EditorFoldEvent { public long Measure, Num, Den; public bool Fold; public double Duration; public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0; }
// DeathArea : BPM 오차 보정용 구간(차트 시간 초 단위). 격자/노트 배치 금지.
public class EditorDeathArea { public double Start; public double End; }
public class EditorBpm { public long Measure, Num, Den; public double Bpm; public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0;
    // TIMING 줄(osu! 빨간 줄): 이 박이 와야 하는 고정 시각(ms, 0박 기준). NaN = 일반 BPM 줄
    public double AnchorMs = double.NaN;
    public bool HasAnchor => !double.IsNaN(AnchorMs); }
public class EditorSpeed { public long Measure, Num, Den; public double Multiplier; public int Layer = NoteLayer.Top;
    public bool ShowChange; // 게임에서 SPEED UP / DOWN 표시 (에디터에서 SPEED 표시를 우클릭해서 켜고 끔)
    public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0; }
public class EditorStopEvent { public long Measure, Num, Den; public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0; }
public class EditorStartEvent { public long Measure, Num, Den; public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0; }


public class LongNoteBodyLink { public EditorNote Head; public EditorNote Tail; public GameObject BodyObj; public SpriteRenderer Sr; public float BaseAlpha = 1f; }

public class EditorActiveLN {
    public EditorNote tail;
    public double nextTickTime;
}

public class UIDrawEvent
{
    public double beat;
    public float y;
    public string text;
    public string shortText; 
    public Color color;
    public bool isWide;
    public List<EditorSpeed> speeds; // SPEED 표시면 해당 변속들 (우클릭으로 SPEED UP/DOWN 표시 켜고 끄기)
}

[Serializable]
public class TempSettingsData
{
    public int EditorSongid;
}

[Serializable]
public class TempSongData
{
    public int Id;
    public string Name;
}

[Serializable]
public class TempSongDataList
{
    public List<TempSongData> Items;
}

public class ChartEditorManager : MonoBehaviour
{
    private enum EditorState { Editing }
    private EditorState currentState = EditorState.Editing;

    [Header("에디터 및 카메라 설정")]
    public int editSongId = 1; 
    public int editLevelId = 2; // 추가 (EASY=1, HARD=2, INSANE=3)
    public Camera editorCamera; 
    public float beatSpacing = 3f; 
    public float hitLineScreenOffsetY = -3f; 
    public float noteScale = 0.35f; 
    public int currentKeyCount = 4; 
    private float laneWidth = 1.0f; 

    [Header("에디터 폰트 설정 (OnGUI용)")]
    public Font customGuiFont;

    [Header("오디오 설정 (볼륨)")]
    [Range(0f, 1f)] public float musicVolume = 0.4f;
    [Range(0f, 1f)] public float hitSoundVolume = 1.0f;
    public float hitSoundLatencyCorrection = 0.035f; 

    [Header("에셋 연결 (노트 및 롱노트 바디)")]
    public Sprite blueShort; public Sprite greenShort;
    public Sprite blueLongHead; public Sprite greenLongHead;
    public Sprite blueLongTail; public Sprite greenLongTail;
    public Sprite blueLongBody; public Sprite greenLongBody; 

    [Header("새 디자인 채보 에디터 (AreaChartEditor — 이제 모든 채보를 이 에디터로 엽니다)")]
    [Tooltip("켜 두면 모든 채보를 새 디자인 에디터로 엽니다. 끄면 예전 에디터 화면")]
    public bool useNewEditor = true;
    [Tooltip("새 디자인 에디터 글꼴 (한글이 들어 있는 TMP 글꼴). 비우면 Resources/Fonts/AreaEditorFont → 이 에디터의 글꼴")]
    public TMP_FontAsset areaEditorFont;
    [Range(0.5f, 2f)] public float areaEditorFontScale = 1f;
    [Tooltip("처음 열 때 한국어로 표시 (에디터 안에서 L 키로 바꿀 수 있고, 바꾼 값은 기억됨)")]
    public bool areaEditorKorean = true;
    [Tooltip("새 디자인 에디터에서 롱노트 끝 판정(꼬리)에도 탭음을 낼지 (에디터 안에서 F5 로 바꿀 수 있고, 바꾼 값은 기억됨)")]
    public bool areaEditorLongNoteHitSound = true;

    [Header("채보 에디터 설정창 UI (신규)")]
    public CanvasGroup chartUIGroup;
    public CanvasGroup settingUIGroup;
    public TextMeshProUGUI settingMainText;
    public TextMeshProUGUI settingSubText;
    public TextMeshProUGUI settingValueText;

    [Header("UI 연결 (캔버스 요소들)")]
    public GameObject selectPanelObj;       
    public TMP_InputField songIdInputField; 
    public GameObject editorPanelObj;       
    public TextMeshProUGUI selectedSnapText; 
    public TextMeshProUGUI modeText;         
    public TextMeshProUGUI timeText;         
    public TextMeshProUGUI measureBeatText;  
    public TextMeshProUGUI songNameText;     
    public TextMeshProUGUI bpmText;          
    public TextMeshProUGUI offsetText;       
    public TextMeshProUGUI offsetSignText;   
    public TextMeshProUGUI centerMessageText; 
    public Image songImg;                    
    public Image bgImageUI;

    [Header("층(Layer) 편집 UI")]
    public TextMeshProUGUI layerText;                       // 현재 편집 중인 층 표시용 텍스트
    [Range(0f, 1f)] public float inactiveLayerAlpha = 0.3f; // 편집 중이 아닌 층의 투명도 (재생 중에는 1.0으로 복구)

    [Header("콤보 표기 UI")]
    public TextMeshProUGUI editorComboText;
    public TextMeshProUGUI editorComboTitleText; 

    private Sprite fallbackSprite; 

    private List<EditorNote> editorNotes = new List<EditorNote>();
    private List<LongNoteBodyLink> longNoteBodies = new List<LongNoteBodyLink>();
    private List<EditorBpm> bpmChanges = new List<EditorBpm>();
    private List<EditorSpeed> speedChanges = new List<EditorSpeed>();
    private List<EditorStopEvent> stopEvents = new List<EditorStopEvent>();
    private List<EditorStartEvent> startEvents = new List<EditorStartEvent>();
    private List<EditorFoldEvent> foldEvents = new List<EditorFoldEvent>();
    // (레인 사라짐 / 드러남 줄은 새 디자인 에디터(AreaChartEditor)에서 편집. 여기서는 지워지지 않게 읽고 그대로 저장만 함)
    private List<EditorDeathArea> deathAreas = new List<EditorDeathArea>();
    private List<string> originalOtherEvents = new List<string>();
    
    // 차트 시계에서 음악이 시작되는 시각(초, 게임의 songStartOffset 과 같음) = -(MUSICOFFSET / 1000)
    //  파일에는 MUSICOFFSET(ms, osu! 와 같은 부호)으로 저장한다. ChartOffset 참고.
    private double songStartOffset = 0.0;
    private double chartOffset = 0.0; 

    // 재생 시계: 음악 위치 0 에 해당하는 dspTime (예약 재생 기준)
    //  차트 시각 = EditorAudioStart + (dspTime - edDspAudioZero)
    //  → 재생 중에 오프셋을 바꾸면 음악은 끊기지 않고 격자/노트만 움직인다 (osu! 에디터와 같은 방식)
    private double edDspAudioZero = 0.0;
    private bool edMusicScheduled = false;
    private bool edWaitingAudio = false;   // 음악 데이터를 불러오는 중 (다 되면 그 시점부터 예약 재생)
    // 설정의 AUDIO OFFSET 까지 반영한 음악 시작 시각 → 에디터에서도 게임과 같은 싱크로 들린다
    private double EditorAudioStart { get { return chartOffset - SettingsStore.MusicLead; } }
    private Camera mainCam;
    private double currentBeat = 0;
    private bool isPlaying = false;

    private string currentSongName = "";

    private AudioSource bgmSource;
    private AudioSource sfxSource;
    private AudioClip hitSoundClip;
    private double edHitCheckBeat;      // 탭음: 여기까지의 박은 이미 검사함
    private double edPlayFromTime;      // 재생을 시작한 차트 시각 (예약 대기 동안 커서 고정용)

    private double playStartTime = 0;
    private bool isMusicPlaying = false;
    private double currentPlaybackTime = 0;

    private int[] snapLevels = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 24, 32, 48, 64 };
    private int currentSnap = 16; 
    private int currentNoteType = 1;
    private double hoverSnapBeat = 0; 

    private float offsetHoldTimer = 0f;
    private int offsetHoldCount = 0;

    private int editorCombo = 0;
    private float comboAnimTimer = 0f;
    private List<EditorActiveLN> activeLNs = new List<EditorActiveLN>();

    private Vector3 initialComboScale = Vector3.one;
    private Vector3 initialComboTitleScale = Vector3.one;
    private int lastScreenWidth = 0;
    private int lastScreenHeight = 0;

    private bool isInputtingBpm = false;
    private bool isInputtingSpeed = false;
    private bool isInputtingDeathArea = false;      // G + 클릭 : DeathArea 시작/끝 시간 입력
    private int deathAreaInputStage = 0;            // 0: 시작 시간 입력 중, 1: 끝 시간 입력 중
    private double deathAreaPendingStart = 0;
    private double deathAreaCursorBeat = 0;
    private bool isInputtingOffset = false;          // O : MUSICOFFSET 을 ms 로 직접 입력
    private bool IsSettingInputOpen { get { return isInputtingBpm || isInputtingSpeed || isInputtingDeathArea || isInputtingOffset; } }
    private bool speedApplyBothLayers = false; // Shift+F 로 입력하면 1층/2층 모두에 적용
    private bool isRedNoteMode = false;
    private bool isDragNoteMode = false;   // 6: 드래그 노트 (1 → 드래그 노트 9, 2 → 드래그로 시작하는 롱노트 10)
    private int editLayer = NoteLayer.Top; // 현재 편집 중인 층 (기본: 2층)
    private bool isDraggingRedNote = false;
    private EditorNote draggedRedNote = null;

    private string inputBuffer = "";
    private long targetMeasure = 1, targetNum = 0, targetDen = 4;

    private Dictionary<Color, Texture2D> colorTexDict = new Dictionary<Color, Texture2D>();
    private Dictionary<Color, Texture2D> circleTexDict = new Dictionary<Color, Texture2D>();

    private Mesh quadMesh;
    private Dictionary<Color, Material> gridMatDict = new Dictionary<Color, Material>();

    private Dictionary<string, Sprite> redNoteSprites = null;
    private Coroutine settingTransitionCoroutine;

    private bool originalOrthographic;
    private float originalOrthographicSize;
    private Color originalBackgroundColor;
    private CameraClearFlags originalClearFlags;
    private Vector3 originalCamPos;
    private Quaternion originalCamRot;
    private Transform originalCamParent;

    private int escapePressCount = 0;
    private float escapePressTimer = 0f;

    void Awake()
    {
        Time.timeScale = 1f;

        if (editorComboText != null) initialComboScale = editorComboText.transform.localScale;
        if (editorComboTitleText != null) initialComboTitleScale = editorComboTitleText.transform.localScale;
    }

    void Start()
    {
        if (settingUIGroup) { settingUIGroup.alpha = 0f; settingUIGroup.gameObject.SetActive(false); }
        if (chartUIGroup) { chartUIGroup.alpha = 1f; chartUIGroup.gameObject.SetActive(true); }

#pragma warning disable CS0618
        if (editorCamera != null) mainCam = editorCamera;
        else
        {
            foreach (var cam in FindObjectsOfType<Camera>()) {
                if (cam.gameObject.scene == this.gameObject.scene) { mainCam = cam; break; }
            }
            if (mainCam == null) mainCam = Camera.main; 
        }

        if (mainCam != null)
        {
            originalCamParent = mainCam.transform.parent;
            originalCamPos = mainCam.transform.localPosition;
            originalCamRot = mainCam.transform.localRotation;
            originalOrthographic = mainCam.orthographic;
            originalOrthographicSize = mainCam.orthographicSize;
            originalBackgroundColor = mainCam.backgroundColor;
            originalClearFlags = mainCam.clearFlags;

            mainCam.transform.SetParent(null); 
            mainCam.transform.position = new Vector3(0, 0, -10f);
            mainCam.transform.rotation = Quaternion.identity;
            mainCam.transform.localScale = Vector3.one;

            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.1f, 0.1f, 0.15f); 
            mainCam.orthographic = true;
            mainCam.orthographicSize = 5f;
        }

        Canvas mainCanvas = FindObjectOfType<Canvas>();
        if (mainCanvas != null && mainCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            mainCanvas.planeDistance = 5f; 
        }

        AudioListener[] listeners = FindObjectsOfType<AudioListener>();
        for (int i = 1; i < listeners.Length; i++) { Destroy(listeners[i]); }
#pragma warning restore CS0618

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false;
        bgmSource.ignoreListenerPause = true;
        bgmSource.volume = musicVolume; 

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.ignoreListenerPause = true;
        
        hitSoundClip = Resources.Load<AudioClip>("Audio/etc/hitsound");
        // [버그 수정] 탭음 데이터를 미리 올려 둠 (첫 PlayOneShot 때 아직 안 올라와 있으면 첫 소리가 나지 않음)
        if (hitSoundClip != null && hitSoundClip.loadState == AudioDataLoadState.Unloaded) hitSoundClip.LoadAudioData();

        if (selectPanelObj != null) selectPanelObj.SetActive(false);
        if (editorPanelObj != null) editorPanelObj.SetActive(true);

        int targetId = editSongId; 
        
        if (CoreManager.Instance != null)
        {
            if (CoreManager.Instance.CurrentSongData != null && CoreManager.Instance.CurrentSongData.Id > 0)
            {
                targetId = CoreManager.Instance.CurrentSongData.Id;
                editLevelId = CoreManager.Instance.CurrentLevelId; // 추가됨
            }
            else if (CoreManager.Instance.CurrentSettings != null && CoreManager.Instance.CurrentSettings.EditorSongid > 0)
            {
                targetId = CoreManager.Instance.CurrentSettings.EditorSongid;
            }
        }
        else
        {
            string settingPath = Path.Combine(Application.streamingAssetsPath, "data", "setting.json");
            if (File.Exists(settingPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(settingPath, System.Text.Encoding.UTF8);
                    TempSettingsData tempSettings = JsonUtility.FromJson<TempSettingsData>(jsonContent);
                    if (tempSettings != null && tempSettings.EditorSongid > 0)
                    {
                        targetId = tempSettings.EditorSongid;
                    }
                }
                catch (Exception) { }
            }
        }
        
        StartEditorWithSong(targetId);
        UpdateLayerText();
    }

    private void ApplyLetterbox()
    {
        if (mainCam == null) return;
        if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        float targetAspect = 16.0f / 9.0f;
        float windowAspect = (float)Screen.width / (float)Screen.height;
        float scaleHeight = windowAspect / targetAspect;

        if (scaleHeight < 1.0f) {
            Rect rect = mainCam.rect;
            rect.width = 1.0f; rect.height = scaleHeight;
            rect.x = 0; rect.y = (1.0f - scaleHeight) / 2.0f;
            mainCam.rect = rect;
        } else {
            float scaleWidth = 1.0f / scaleHeight;
            Rect rect = mainCam.rect;
            rect.width = scaleWidth; rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) / 2.0f; rect.y = 0;
            mainCam.rect = rect;
        }
    }

    private void EnsureGraphicsResources()
    {
        if (fallbackSprite == null)
        {
            Texture2D tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            fallbackSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        if (quadMesh == null)
        {
            quadMesh = new Mesh();
            quadMesh.vertices = new Vector3[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            quadMesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        }
    }

    private Texture2D GetColorTex(Color c)
    {
        if(!colorTexDict.ContainsKey(c)) {
            Texture2D tex = new Texture2D(1,1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            tex.SetPixel(0,0,c); tex.Apply();
            colorTexDict[c] = tex;
        }
        return colorTexDict[c];
    }

    private Texture2D GetCircleTex(Color c)
    {
        if(!circleTexDict.ContainsKey(c)) {
            int size = 64; 
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            Color transparent = new Color(c.r, c.g, c.b, 0f); 
            float radius = size / 2f;
            for(int y=0; y<size; y++) {
                for(int x=0; x<size; x++) {
                    float dx = x - (radius - 0.5f); float dy = y - (radius - 0.5f);
                    float dist = Mathf.Sqrt(dx*dx + dy*dy);
                    float alpha = Mathf.Clamp01(radius - dist); 
                    tex.SetPixel(x,y, Color.Lerp(transparent, c, alpha));
                }
            }
            tex.Apply();
            circleTexDict[c] = tex;
        }
        return circleTexDict[c];
    }

    private Material GetGridMat(Color c)
    {
        if(!gridMatDict.ContainsKey(c)) {
            Material m = new Material(Shader.Find("Sprites/Default"));
            m.color = c;
            gridMatDict[c] = m;
        }
        return gridMatDict[c];
    }

    private void DrawLine(Vector3 pos, Vector3 scale, Color c)
    {
        Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(pos, Quaternion.identity, scale), GetGridMat(c), 0, mainCam);
    }

    private void StartEditorWithSong(int songId)
    {
        // 모든 채보를 새 디자인 에디터(AreaChartEditor: 상업용 HUD · 아이콘 · 미니맵)로 연다 (useNewEditor 를 끄면 예전 화면)
        if (useNewEditor)
        {
            Debug.Log($"[Editor] {songId}_{editLevelId}: 새 디자인 에디터로 엽니다.");
            if (chartUIGroup) chartUIGroup.gameObject.SetActive(false);
            if (settingUIGroup) settingUIGroup.gameObject.SetActive(false);
            if (editorPanelObj != null) editorPanelObj.SetActive(false);
            if (selectPanelObj != null) selectPanelObj.SetActive(false);
            if (bgmSource != null) bgmSource.Stop();
            string title = (CoreManager.Instance != null && CoreManager.Instance.CurrentSongData != null && CoreManager.Instance.CurrentSongData.Id == songId)
                ? CoreManager.Instance.CurrentSongData.Name : $"Song {songId}";
            TMP_FontAsset areaFont = areaEditorFont;
            if (areaFont == null && Resources.Load<TMP_FontAsset>("Fonts/AreaEditorFont") == null)
            {
                // 지정한 글꼴이 없으면 이 에디터 화면에서 쓰던 글꼴 (한글 표시가 되는 글꼴)
                TextMeshProUGUI[] cands = { layerText, centerMessageText, modeText, settingSubText, songNameText };
                foreach (var c in cands) if (c != null && c.font != null) { areaFont = c.font; break; }
            }
            AreaChartEditor areaEd = AreaChartEditor.Launch(gameObject.scene, songId, editLevelId, mainCam, title, areaFont, areaEditorFontScale, areaEditorKorean);
            areaEd.SetAudioDefaults(musicVolume, hitSoundVolume, areaEditorLongNoteHitSound);   // 이 에디터의 음악 · 탭음 볼륨으로 시작
            currentState = EditorState.Editing;
            enabled = false;   // 이 에디터의 Update / OnGUI 를 멈춤
            return;
        }

        editSongId = songId;
        currentSongName = $"Song {songId}"; 

        if (CoreManager.Instance != null && CoreManager.Instance.CurrentSongData != null && !string.IsNullOrEmpty(CoreManager.Instance.CurrentSongData.Name))
        {
            currentSongName = CoreManager.Instance.CurrentSongData.Name;
        }
        else
        {
            string songsJsonPath = Path.Combine(Application.streamingAssetsPath, "data", "songs.json");
            if (File.Exists(songsJsonPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(songsJsonPath, System.Text.Encoding.UTF8).Trim();
                    if (jsonContent.StartsWith("["))
                    {
                        jsonContent = "{ \"Items\": " + jsonContent + "}";
                    }
                    
                    TempSongDataList songList = JsonUtility.FromJson<TempSongDataList>(jsonContent);
                    if (songList != null && songList.Items != null)
                    {
                        var songInfo = songList.Items.FirstOrDefault(s => s.Id == songId);
                        if (songInfo != null && !string.IsNullOrEmpty(songInfo.Name))
                        {
                            currentSongName = songInfo.Name;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("songs.json 파일 로드 실패: " + e.Message);
                }
            }
        }

        if (songNameText != null) songNameText.text = currentSongName;

        if (songImg != null)
        {
            Sprite img = SongArt.Load("Sprites/squareimg", songId);   // 없으면 None
            if (img != null) { songImg.sprite = img; songImg.color = Color.white; }
            else { songImg.sprite = null; songImg.color = new Color(0.1f, 0.1f, 0.1f, 1f); }
        }

        if (bgImageUI != null)
        {
            Sprite bgSprite = BackgroundBlur.Get(SongArt.Load("Sprites/blurimg", songId));   // 없으면 None, 원본 → 흐리게
            if (bgSprite != null) { bgImageUI.sprite = bgSprite; bgImageUI.color = Color.white; }
            else { bgImageUI.sprite = null; bgImageUI.color = new Color(0.1f, 0.1f, 0.1f, 1f); }
        }

        LoadChartForEditor(editSongId);
        LoadAudio(editSongId);
        currentState = EditorState.Editing;
    }

    private void LoadAudio(int songId)
    {
        // 난이도 전용 음원(Audio/Full/{곡ID}_{난이도})이 있으면 그것, 없으면 기존 음원
        AudioClip clip = Resources.Load<AudioClip>($"Audio/Full/{songId}_{editLevelId}");
        if (clip == null) clip = Resources.Load<AudioClip>($"Audio/Full/{songId}");
        if (clip != null)
        {
            bgmSource.clip = clip;
            // 음악 데이터를 미리 메모리에 올림 (Load In Background 설정이면 Resources.Load 직후엔 아직 비어 있음)
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        }
    }

    // ===== DeathArea =====
    // 경계(시작/끝 시각)는 허용, 안쪽만 금지. (변환기가 새 구간 첫 노트를 끝 시각에 두기 때문)
    private const double DEATH_EDGE_TOLERANCE = 2e-4;

    public bool IsDeathAreaTime(double time)
    {
        foreach (var a in deathAreas)
            if (time > a.Start + DEATH_EDGE_TOLERANCE && time < a.End - DEATH_EDGE_TOLERANCE) return true;
        return false;
    }

    public bool IsDeathAreaBeat(double beat)
    {
        foreach (var z in GetGapZones())
            if (beat > z[0] + 1e-5 && beat < z[1] - 1e-5) return true;   // TIMING 앞 빈 박
        if (deathAreas.Count == 0) return false;
        return IsDeathAreaTime(GetTimeFromBeat(beat));
    }

    private void OpenDeathAreaInput(double cursorBeat)
    {
        isInputtingDeathArea = true;
        deathAreaInputStage = 0;
        deathAreaCursorBeat = cursorBeat;
        inputBuffer = "";
        double cursorTime = GetTimeFromBeat(cursorBeat);
        OpenSettingUI("DEATH AREA", $"시작 시간(0.0000s)을 입력하세요. 빈 칸 Enter = 커서 위치 ({cursorTime:F4}s)");
    }

    private void ConfirmDeathAreaInput()
    {
        double cursorTime = GetTimeFromBeat(deathAreaCursorBeat);
        string buf = inputBuffer.Trim().TrimEnd('s', 'S');
        double v;

        if (string.IsNullOrEmpty(buf)) v = cursorTime;
        else if (!double.TryParse(buf, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
        {
            inputBuffer = "";
            if (settingSubText != null) settingSubText.text = "숫자 형식이 올바르지 않습니다. 다시 입력하세요. (예: 53.5518)";
            if (settingValueText != null) settingValueText.text = "";
            return;
        }
        v = Math.Round(v, 4);

        if (deathAreaInputStage == 0)
        {
            deathAreaPendingStart = v;
            deathAreaInputStage = 1;
            inputBuffer = "";
            if (settingSubText != null) settingSubText.text = $"끝 시간(0.0000s)을 입력하세요. (시작 {v:F4}s) 빈 칸 Enter = 커서 위치 ({cursorTime:F4}s)";
            if (settingValueText != null) settingValueText.text = "";
            return;
        }

        double s = Math.Min(deathAreaPendingStart, v);
        double e = Math.Max(deathAreaPendingStart, v);
        if (e - s < 0.0001)
        {
            inputBuffer = "";
            if (settingSubText != null) settingSubText.text = "끝 시간은 시작 시간보다 커야 합니다. 다시 입력하세요.";
            if (settingValueText != null) settingValueText.text = "";
            return;
        }

        deathAreas.RemoveAll(a => a.End > s && a.Start < e); // 겹치는 기존 구간은 교체
        deathAreas.Add(new EditorDeathArea { Start = s, End = e });
        deathAreas = deathAreas.OrderBy(a => a.Start).ToList();
        CloseSettingUI();
    }


    void Update()
    {
        ApplyLetterbox();
        var kb = Keyboard.current;
        if (kb == null) return; 

        if (escapePressTimer > 0)
        {
            escapePressTimer -= Time.unscaledDeltaTime;
            if (escapePressTimer <= 0) escapePressCount = 0;
        }

        if (songIdInputField != null && songIdInputField.isFocused) 
        {
            if (kb[Key.Escape].wasPressedThisFrame || kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            return; 
        }

        if (IsSettingInputOpen)
        {
            HandleSettingInput();
            return;
        }

        bool isGUIOpen = false;
        var mouse = Mouse.current;
        bool isPointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        
        if (mouse != null && mainCam != null && !isPlaying && !isPointerOverUI && !isGUIOpen)
        {
            Vector3 worldPos = mainCam.ScreenToWorldPoint(new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 10f));
            double clickOffsetBeat = (worldPos.y - hitLineScreenOffsetY) / beatSpacing;
            double targetBeat = Math.Max(0, currentBeat + clickOffsetBeat);
            double step = 4.0 / currentSnap;
            hoverSnapBeat = Math.Round(targetBeat / step) * step;
        }


        HandlePlaybackAndScroll();

        if (isPlaying)
        {
            // [버그 수정] 첫 탭음이 안 들리던 문제
            //  예전: 이번 프레임 구간을 (이전 시각 + 보정, 지금 시각 + 보정] 으로 잡아서
            //        재생 시작 위치에 있는 노트(와 시작 직후 35ms 안의 노트)가 첫 프레임에 빠졌다.
            //  지금: 재생 시작 때 '시작 박 바로 앞'부터 검사하고, 그 뒤로는 지난번 검사한 박부터 이어서 검사
            double prevBeat = edHitCheckBeat;
            double curBeat = GetBeatFromTime(currentPlaybackTime + hitSoundLatencyCorrection);

            if (curBeat > prevBeat)
            {
                edHitCheckBeat = curBeat;
                var notesToProcess = editorNotes
                    .Where(n => n.AbsoluteBeat > prevBeat && n.AbsoluteBeat <= curBeat)
                    .OrderBy(n => n.AbsoluteBeat)
                    .ToList();

                var distinctBeats = notesToProcess.Select(n => n.AbsoluteBeat).Distinct();
                foreach (var b in distinctBeats)
                {
                    if (sfxSource != null && hitSoundClip != null)
                    {
                        sfxSource.PlayOneShot(hitSoundClip, hitSoundVolume);
                    }
                }

                foreach (var n in notesToProcess)
                {
                    IncreaseCombo();

                    if (NoteTypes.IsLongHead(n.Type))
                    {
                        var tail = editorNotes
                            .Where(t => t.Lane == n.Lane && t.Layer == n.Layer && t.IsRedNote == n.IsRedNote && (t.Type == 3 || t.Type == 13) && t.AbsoluteBeat > n.AbsoluteBeat)
                            .OrderBy(t => t.AbsoluteBeat)
                            .FirstOrDefault();
                        if (tail != null) {
                            double headTime = GetTimeFromBeat(n.AbsoluteBeat);
                            activeLNs.Add(new EditorActiveLN {
                                tail = tail,
                                nextTickTime = headTime + LongNoteTickRule.Interval(GetEditorBpmAtTime(headTime))
                            });
                        }
                    }
                }
            }

            double curTimeForTick = currentPlaybackTime + hitSoundLatencyCorrection;
            for (int i = activeLNs.Count - 1; i >= 0; i--)
            {
                var ln = activeLNs[i];
                double tailTime = GetTimeFromBeat(ln.tail.AbsoluteBeat);

                // 게임과 같은 틱 규칙 (LongNoteTickRule)
                while (curTimeForTick >= ln.nextTickTime && ln.nextTickTime < tailTime - LongNoteTickRule.EndMargin)
                {
                    IncreaseCombo();
                    ln.nextTickTime += LongNoteTickRule.Interval(GetEditorBpmAtTime(ln.nextTickTime));
                }

                if (curTimeForTick >= tailTime) {
                    activeLNs.RemoveAt(i);
                }
            }
        }

        if (!isGUIOpen) HandleShortcuts();
        if (!isPointerOverUI && !isGUIOpen) HandleMouseInput(); 
        
        UpdateNoteVisuals(); 
        UpdateGridVisuals();
        UpdateUITexts(); 
    }

    // ===== 층(Layer) 편집 =====
    // UI 버튼의 OnClick에 연결해서 사용: ToggleEditLayer / SetEditLayerTop / SetEditLayerBottom
    public void ToggleEditLayer()
    {
        SetEditLayer(editLayer == NoteLayer.Top ? NoteLayer.Bottom : NoteLayer.Top);
    }

    public void SetEditLayerTop() { SetEditLayer(NoteLayer.Top); }
    public void SetEditLayerBottom() { SetEditLayer(NoteLayer.Bottom); }

    public void SetEditLayer(int layer)
    {
        editLayer = (layer == NoteLayer.Bottom) ? NoteLayer.Bottom : NoteLayer.Top;
        isDraggingRedNote = false;
        draggedRedNote = null;
        UpdateLayerText();
    }

    private void UpdateLayerText()
    {
        if (layerText == null) return;
        if (editLayer == NoteLayer.Top)
        {
            layerText.text = "LAYER 2 (상층)";
            layerText.color = new Color(1f, 0.85f, 0.3f);
        }
        else
        {
            layerText.text = "LAYER 1 (하층)";
            layerText.color = new Color(0.4f, 0.8f, 1f);
        }
    }

    private void IncreaseCombo()
    {
        editorCombo++;
        comboAnimTimer = 1f; 
    }

    public void TogglePlayButton()
    {
        if (currentState != EditorState.Editing) return;
        
        isPlaying = !isPlaying;
        if (isPlaying)
        {
            double exactTime = GetTimeFromBeat(currentBeat);
            edPlayFromTime = exactTime;
            edHitCheckBeat = currentBeat - 1e-6;          // 시작 위치의 노트도 탭음이 나도록
            isMusicPlaying = false;
            // 음악 예약에 필요한 아주 짧은 시간(약 0.1초)만큼 시계를 앞당겨 시작 → 시작 위치부터 음악이 빠짐없이 나옴
            currentPlaybackTime = ScheduleEditorMusic(exactTime, true);
            playStartTime = Time.realtimeSinceStartupAsDouble - currentPlaybackTime;
        }
        else
        {
            if (bgmSource != null) bgmSource.Stop();
            edMusicScheduled = false;
            edWaitingAudio = false;
            isMusicPlaying = false;
            SnapCursorToBeat(currentBeat);
        }
    }

    private void HandlePlaybackAndScroll()
    {
        var kb = Keyboard.current;
        bool isGUIOpen = IsSettingInputOpen;
        
        if (kb != null && kb[Key.Space].wasPressedThisFrame && !isGUIOpen) TogglePlayButton(); 

        if (isPlaying)
        {
            // [버그 수정] 예전: 프레임 시간으로 흐르다가 bgmSource.time 과 50ms 넘게 벌어질 때만 순간 이동
            //  → 에디터 화면이 음악과 최대 ±50ms 어긋난 채로 보여서, 그 상태로 맞춘 오프셋/노트가 게임에서 틀어졌다.
            //  지금: 게임과 같이 예약 재생 + dspTime 시계 (프레임마다 부드럽게 따라감)
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            double audioStartTime = EditorAudioStart;

            if (edMusicScheduled && bgmSource != null && bgmSource.clip != null)
            {
                double target = audioStartTime + (AudioSettings.dspTime - edDspAudioZero);
                double diff = target - currentPlaybackTime;
                if (Math.Abs(diff) > 0.25) currentPlaybackTime = target;
                else currentPlaybackTime += dt * Mathf.Clamp(1f + (float)(diff * 2.0), 0.8f, 1.2f);

                isMusicPlaying = currentPlaybackTime >= audioStartTime;
                if (currentPlaybackTime > audioStartTime + bgmSource.clip.length + 0.05)
                {
                    TogglePlayButton();   // 음악 끝
                    return;
                }
            }
            else
            {
                currentPlaybackTime += dt;
                if (edWaitingAudio && bgmSource != null && bgmSource.clip != null && bgmSource.clip.loadState == AudioDataLoadState.Loaded)
                    ScheduleEditorMusic(currentPlaybackTime);
            }
            // 예약 대기(시작 전 약 0.1초) 동안은 커서를 시작 위치에 둠
            currentBeat = GetBeatFromTime(Math.Max(currentPlaybackTime, edPlayFromTime));
        }
        else
        {
            var mouse = Mouse.current;
            bool isPointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            
            if (mouse != null && mouse.scroll.ReadValue().y != 0 && !isPointerOverUI && !isGUIOpen)
            {
                float scrollDelta = Mathf.Sign(mouse.scroll.ReadValue().y);
                double step = 4.0 / currentSnap;
                if (kb != null && kb[Key.LeftShift].isPressed) step *= Mathf.Max(1, currentSnap / 4);
                
                currentBeat += scrollDelta * step;
                if (currentBeat < -100) currentBeat = -100; 
                currentBeat = Math.Round(currentBeat / step) * step;
                currentPlaybackTime = GetTimeFromBeat(currentBeat);
            }
        }
    }

    private void SnapCursorToBeat(double rawBeat)
    {
        double step = 4.0 / currentSnap;
        currentBeat = Math.Round(rawBeat / step) * step;
        if (currentBeat < -100) currentBeat = -100;
        currentPlaybackTime = GetTimeFromBeat(currentBeat);
    }

    private void HandleSettingInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[Key.Escape].wasPressedThisFrame)
        {
            CloseSettingUI();
            return;
        }

        if (kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame)
        {
            if (isInputtingDeathArea) { ConfirmDeathAreaInput(); return; }

            if (double.TryParse(inputBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            {
                if (isInputtingOffset)
                {
                    // MUSICOFFSET 을 ms 로 직접 지정
                    double delta = ChartOffset.ToMusicOffsetMs(songStartOffset) - val;   // AdjustOffset 은 '늘릴 양(초)'을 받음
                    AdjustOffset(-delta / 1000.0);
                }
                else if (isInputtingBpm && val >= 0)
                {
                    // 같은 자리의 TIMING(고정 시각)은 BPM 값만 바꾸고 시각 고정은 유지
                    var old = bpmChanges.FirstOrDefault(b => b.Measure == targetMeasure && b.Num == targetNum && b.Den == targetDen);
                    double keepAnchor = old != null ? old.AnchorMs : double.NaN;
                    bpmChanges.RemoveAll(b => b.Measure == targetMeasure && b.Num == targetNum && b.Den == targetDen);
                    bpmChanges.Add(new EditorBpm { Measure = targetMeasure, Num = targetNum, Den = targetDen, Bpm = val, AnchorMs = keepAnchor });
                    bpmChanges = bpmChanges.OrderBy(b => b.AbsoluteBeat).ToList();
                }
                else if (isInputtingSpeed)
                {
                    // 변속은 현재 편집 층에만 적용 (Shift+F: 두 층 모두). BPM 변화는 층과 무관.
                    for (int L = NoteLayer.Bottom; L <= NoteLayer.Top; L++)
                    {
                        if (!speedApplyBothLayers && L != editLayer) continue;
                        int layerCopy = L;
                        bool keepShow = speedChanges.Any(s => s.Layer == layerCopy && s.Measure == targetMeasure && s.Num == targetNum && s.Den == targetDen && s.ShowChange);
                        speedChanges.RemoveAll(s => s.Layer == layerCopy && s.Measure == targetMeasure && s.Num == targetNum && s.Den == targetDen);
                        speedChanges.Add(new EditorSpeed { Measure = targetMeasure, Num = targetNum, Den = targetDen, Multiplier = val, Layer = L, ShowChange = keepShow });
                    }
                    speedChanges = speedChanges.OrderBy(s => s.AbsoluteBeat).ThenBy(s => s.Layer).ToList();
                }
            }
            CloseSettingUI();
            return;
        }

        if (kb[Key.Backspace].wasPressedThisFrame && inputBuffer.Length > 0)
        {
            inputBuffer = inputBuffer.Substring(0, inputBuffer.Length - 1);
        }
        
        if (kb[Key.Digit0].wasPressedThisFrame || kb[Key.Numpad0].wasPressedThisFrame) inputBuffer += "0";
        if (kb[Key.Digit1].wasPressedThisFrame || kb[Key.Numpad1].wasPressedThisFrame) inputBuffer += "1";
        if (kb[Key.Digit2].wasPressedThisFrame || kb[Key.Numpad2].wasPressedThisFrame) inputBuffer += "2";
        if (kb[Key.Digit3].wasPressedThisFrame || kb[Key.Numpad3].wasPressedThisFrame) inputBuffer += "3";
        if (kb[Key.Digit4].wasPressedThisFrame || kb[Key.Numpad4].wasPressedThisFrame) inputBuffer += "4";
        if (kb[Key.Digit5].wasPressedThisFrame || kb[Key.Numpad5].wasPressedThisFrame) inputBuffer += "5";
        if (kb[Key.Digit6].wasPressedThisFrame || kb[Key.Numpad6].wasPressedThisFrame) inputBuffer += "6";
        if (kb[Key.Digit7].wasPressedThisFrame || kb[Key.Numpad7].wasPressedThisFrame) inputBuffer += "7";
        if (kb[Key.Digit8].wasPressedThisFrame || kb[Key.Numpad8].wasPressedThisFrame) inputBuffer += "8";
        if (kb[Key.Digit9].wasPressedThisFrame || kb[Key.Numpad9].wasPressedThisFrame) inputBuffer += "9";
        if (kb[Key.Period].wasPressedThisFrame || kb[Key.NumpadPeriod].wasPressedThisFrame) inputBuffer += ".";
        if (kb[Key.Minus].wasPressedThisFrame || kb[Key.NumpadMinus].wasPressedThisFrame) inputBuffer += "-";

        if (settingValueText != null) settingValueText.text = inputBuffer;
    }

    private void OpenSettingUI(string type, string desc)
    {
        if (settingMainText != null) settingMainText.text = type;
        if (settingSubText != null) settingSubText.text = desc;
        if (settingValueText != null) settingValueText.text = "";

        if (settingTransitionCoroutine != null) StopCoroutine(settingTransitionCoroutine);
        settingTransitionCoroutine = StartCoroutine(FadeUI(chartUIGroup, settingUIGroup));
    }

    private void CloseSettingUI()
    {
        isInputtingBpm = false;
        isInputtingSpeed = false;
        isInputtingDeathArea = false;
        deathAreaInputStage = 0;
        isInputtingOffset = false;
        
        if (settingTransitionCoroutine != null) StopCoroutine(settingTransitionCoroutine);
        settingTransitionCoroutine = StartCoroutine(FadeUI(settingUIGroup, chartUIGroup));
    }

    private IEnumerator FadeUI(CanvasGroup fadeOut, CanvasGroup fadeIn)
    {
        float t = 0;
        float duration = 0.1f; 

        if (fadeIn != null) fadeIn.gameObject.SetActive(true);

        float startOut = fadeOut != null ? fadeOut.alpha : 1f;
        float startIn = fadeIn != null ? fadeIn.alpha : 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float p = t / duration;
            if (fadeOut != null) fadeOut.alpha = Mathf.Lerp(startOut, 0f, p);
            if (fadeIn != null) fadeIn.alpha = Mathf.Lerp(startIn, 1f, p);
            yield return null;
        }

        if (fadeOut != null) { fadeOut.alpha = 0f; fadeOut.gameObject.SetActive(false); }
        if (fadeIn != null) { fadeIn.alpha = 1f; }
    }

    private void HandleShortcuts()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[Key.F9].wasPressedThisFrame && !isPlaying)
        {
            editorCombo = 0;
            activeLNs.Clear();
            comboAnimTimer = 0f;
        }

        if (kb[Key.Digit1].wasPressedThisFrame || kb[Key.Numpad1].wasPressedThisFrame) currentNoteType = 1; 
        if (kb[Key.Digit2].wasPressedThisFrame || kb[Key.Numpad2].wasPressedThisFrame) currentNoteType = 2; 
        if (kb[Key.Digit3].wasPressedThisFrame || kb[Key.Numpad3].wasPressedThisFrame) currentNoteType = 3; 
        if (kb[Key.Digit4].wasPressedThisFrame || kb[Key.Numpad4].wasPressedThisFrame) currentNoteType = 4; 

        if (kb[Key.Digit5].wasPressedThisFrame || kb[Key.Numpad5].wasPressedThisFrame) 
        {
            isRedNoteMode = !isRedNoteMode;
            if (isRedNoteMode) isDragNoteMode = false;
        }
        // 6 : 드래그 노트 모드 켜기/끄기 (RED 와 동시에 켤 수 없음)
        if (kb[Key.Digit6].wasPressedThisFrame || kb[Key.Numpad6].wasPressedThisFrame)
        {
            isDragNoteMode = !isDragNoteMode;
            if (isDragNoteMode) isRedNoteMode = false;
        }

        // Q : 편집 층 전환 (1층 <-> 2층)
        if (kb[Key.Q].wasPressedThisFrame) ToggleEditLayer();

        bool minusPressed = kb[Key.Minus].wasPressedThisFrame || kb[Key.NumpadMinus].wasPressedThisFrame;
        bool plusPressed = kb[Key.Equals].wasPressedThisFrame || kb[Key.NumpadPlus].wasPressedThisFrame;
        bool minusHeld = kb[Key.Minus].isPressed || kb[Key.NumpadMinus].isPressed;
        bool plusHeld = kb[Key.Equals].isPressed || kb[Key.NumpadPlus].isPressed;

        // + : MUSICOFFSET 증가 = 노트(격자)가 음악보다 늦어짐 (osu! 와 같은 방향)
        //  기본 1ms 단위, SHIFT 를 누르고 있으면 10ms 단위.  O : 값을 ms 로 직접 입력
        double baseAmount = (kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed) ? 0.01 : 0.001; 
        if (kb[Key.O].wasPressedThisFrame && !isPlaying)
        {
            isInputtingOffset = true;
            inputBuffer = "";
            OpenSettingUI("MUSIC OFFSET", $"음악 파일에서 첫 박이 나오는 시각을 ms 로 입력하세요. (지금 {ChartOffset.Display(songStartOffset)}, osu! Offset 과 같은 값)");
        }

        if (minusPressed) { AdjustOffset(-baseAmount); offsetHoldTimer = 0.5f; offsetHoldCount = 0; }
        else if (plusPressed) { AdjustOffset(baseAmount); offsetHoldTimer = 0.5f; offsetHoldCount = 0; }
        else if (minusHeld) {
            offsetHoldTimer -= Time.deltaTime;
            if (offsetHoldTimer <= 0) {
                AdjustOffset(-baseAmount);
                offsetHoldCount++;
                offsetHoldTimer = Mathf.Max(0.01f, 0.1f - (offsetHoldCount * 0.005f)); 
            }
        }
        else if (plusHeld) {
            offsetHoldTimer -= Time.deltaTime;
            if (offsetHoldTimer <= 0) {
                AdjustOffset(baseAmount);
                offsetHoldCount++;
                offsetHoldTimer = Mathf.Max(0.01f, 0.1f - (offsetHoldCount * 0.005f));
            }
        }
        else offsetHoldCount = 0;

        if (kb[Key.PageUp].wasPressedThisFrame) 
        {
            int denom = currentSnap / 4;
            int idx = Array.IndexOf(snapLevels, denom);
            if (idx == -1) idx = 3;
            idx = (idx + 1) % snapLevels.Length;
            SetSnap(snapLevels[idx]);
        }
        if (kb[Key.PageDown].wasPressedThisFrame) 
        {
            int denom = currentSnap / 4;
            int idx = Array.IndexOf(snapLevels, denom);
            if (idx == -1) idx = 3;
            idx = (idx - 1 + snapLevels.Length) % snapLevels.Length;
            SetSnap(snapLevels[idx]);
        }

        if (kb[Key.F4].wasPressedThisFrame) SetKeyMode(4);
        if (kb[Key.F5].wasPressedThisFrame) SetKeyMode(5);
        if (kb[Key.F6].wasPressedThisFrame) SetKeyMode(6);
        if (kb[Key.F8].wasPressedThisFrame) SetKeyMode(8);

        {
            if (kb[Key.LeftArrow].wasPressedThisFrame) 
            {
                int denom = currentSnap / 4;
                int idx = Array.IndexOf(snapLevels, denom);
                if (idx == -1) idx = 3;
                idx = (idx - 1 + snapLevels.Length) % snapLevels.Length;
                SetSnap(snapLevels[idx]);
            }
            if (kb[Key.RightArrow].wasPressedThisFrame) 
            {
                int denom = currentSnap / 4;
                int idx = Array.IndexOf(snapLevels, denom);
                if (idx == -1) idx = 3;
                idx = (idx + 1) % snapLevels.Length;
                SetSnap(snapLevels[idx]);
            }

            if (kb[Key.UpArrow].wasPressedThisFrame) 
            {
                beatSpacing += 1f;
                if (beatSpacing > 30f) beatSpacing = 30f; 
            }
            if (kb[Key.DownArrow].wasPressedThisFrame) 
            {
                beatSpacing -= 1f;
                if (beatSpacing < 1f) beatSpacing = 1f; 
            }

            if (kb[Key.Escape].wasPressedThisFrame)
            {
                escapePressCount++;
                escapePressTimer = 0.5f; 

                if (escapePressCount >= 5)
                {
                    escapePressCount = 0;
                    SaveChartFile(editSongId); 
                    if (bgmSource != null && bgmSource.isPlaying) bgmSource.Stop();
                    if (CoreManager.Instance != null) CoreManager.Instance.LoadSongSelectScene();
                }
            }
        }

        if (kb[Key.S].wasPressedThisFrame) SaveChartFile(editSongId);
    }

    private void HandleMouseInput()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mainCam == null || isPlaying) return;

        bool isLeftDown = false; bool isRightDown = false;
        bool isLeftHeld = false; bool isLeftUp = false;

        if (mouse != null) { 
            isLeftDown = mouse.leftButton.wasPressedThisFrame; 
            isRightDown = mouse.rightButton.wasPressedThisFrame; 
            isLeftHeld = mouse.leftButton.isPressed;
            isLeftUp = mouse.leftButton.wasReleasedThisFrame;
        }

        Vector3 mousePos = mouse != null ? new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 10f) : Vector3.zero;

        // 아무 키도 누르지 않은 채 SPEED 표시 위를 우클릭: 게임에서 SPEED UP / DOWN 을 표시할지 켜고 끔
        //  다른 키(F + 우클릭 = 변속 삭제 등)를 누르고 있을 때는 원래 기능이 동작하도록 건너뜀
        if (isRightDown && (kb == null || !kb.anyKey.isPressed) && TryToggleSpeedShowAt(new Vector2(mousePos.x, mousePos.y))) return;
        Vector3 worldPos = mainCam.ScreenToWorldPoint(mousePos);
        
        float totalWidth = currentKeyCount * laneWidth;
        float leftEdge = -totalWidth / 2f;
        
        long measure = (long)Math.Floor(hoverSnapBeat / 4.0) + 1;
        double fraction = (hoverSnapBeat / 4.0) - Math.Floor(hoverSnapBeat / 4.0);
        long num = (long)Math.Round(fraction * currentSnap);
        long den = currentSnap;

        if (num >= den) { measure++; num = 0; }
        long gcd = GCD(num, den); num /= gcd; den /= gcd;
        bool inDeathArea = IsDeathAreaBeat(hoverSnapBeat); // DeathArea 안에서는 노트 신규 배치 금지

        if (isLeftDown || isRightDown)
        {
            if (kb != null)
            {
                bool shift = kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed;


                if (kb[Key.G].isPressed) {
                    // G + 좌클릭 : DeathArea 추가(시작/끝 시간 입력), G + 우클릭 : 커서 위치의 DeathArea 삭제
                    if (isRightDown) {
                        double hoverTime = GetTimeFromBeat(hoverSnapBeat);
                        deathAreas.RemoveAll(a => hoverTime >= a.Start - DEATH_EDGE_TOLERANCE && hoverTime <= a.End + DEATH_EDGE_TOLERANCE);
                    }
                    else OpenDeathAreaInput(hoverSnapBeat);
                    return;
                }

                if (kb[Key.D].isPressed) {
                    if (isRightDown) bpmChanges.RemoveAll(ev => ev.Measure == measure && ev.Num == num && ev.Den == den);
                    else { 
                        isInputtingBpm = true; 
                        inputBuffer = ""; 
                        targetMeasure = measure; targetNum = num; targetDen = den; 
                        OpenSettingUI("BPM", "변화시킬 BPM 값을 수치로 적어주세요.");
                    }
                    return;
                }
                else if (kb[Key.F].isPressed) {
                    if (isRightDown) speedChanges.RemoveAll(ev => (shift || ev.Layer == editLayer) && ev.Measure == measure && ev.Num == num && ev.Den == den);
                    else { 
                        isInputtingSpeed = true; 
                        speedApplyBothLayers = shift;
                        inputBuffer = ""; 
                        targetMeasure = measure; targetNum = num; targetDen = den; 
                        string layerLabel = shift ? "1층+2층 모두" : (editLayer == NoteLayer.Top ? "2층" : "1층");
                        OpenSettingUI("SCROLL", $"[{layerLabel}] 변화시킬 SCROLL SPEED 값을 수치로 적어주세요.");
                    }
                    return;
                }

                if (kb[Key.E].isPressed) {
                    stopEvents.RemoveAll(ev => ev.Measure == measure && ev.Num == num && ev.Den == den);
                    if (!isRightDown) stopEvents.Add(new EditorStopEvent { Measure = measure, Num = num, Den = den });
                    return;
                }
                else if (kb[Key.R].isPressed) {
                    startEvents.RemoveAll(ev => ev.Measure == measure && ev.Num == num && ev.Den == den);
                    if (!isRightDown) startEvents.Add(new EditorStartEvent { Measure = measure, Num = num, Den = den });
                    return;
                }
            }
        }

        {
            int hoverLane = Mathf.FloorToInt((worldPos.x - leftEdge) / laneWidth);

            if (isRightDown && hoverLane >= 0 && hoverLane < currentKeyCount) 
            {
                var target = editorNotes.FirstOrDefault(n => n.Layer == editLayer && n.Lane <= hoverLane && (n.Lane + (n.IsRedNote ? n.Width : 1) - 1) >= hoverLane && Math.Abs(n.AbsoluteBeat - hoverSnapBeat) < 0.02f);
                if (target != null) { Destroy(target.VisualObject); editorNotes.Remove(target); RefreshLongNoteBodies(); }
            } 
            else if (isLeftDown && !inDeathArea && hoverLane >= 0 && hoverLane < currentKeyCount) 
            {
                var exist = editorNotes.FirstOrDefault(n => n.Layer == editLayer && n.Lane <= hoverLane && (n.Lane + (n.IsRedNote ? n.Width : 1) - 1) >= hoverLane && Math.Abs(n.AbsoluteBeat - hoverSnapBeat) < 0.02f);
                if (exist != null) { Destroy(exist.VisualObject); editorNotes.Remove(exist); }
                
                if (isRedNoteMode) {
                    int rType = currentNoteType + 10; 
                    draggedRedNote = AddNoteToEditor(measure, num, den, hoverLane, rType, true, 1, editLayer);
                    isDraggingRedNote = true;
                } else {
                    int placeType = currentNoteType;
                    if (isDragNoteMode) placeType = currentNoteType == 1 ? NoteTypes.Drag : currentNoteType == 2 ? NoteTypes.DragLongHead : currentNoteType;
                    AddNoteToEditor(measure, num, den, hoverLane, placeType, false, 1, editLayer);
                }
                RefreshLongNoteBodies();
            }
            else if (isLeftHeld && isDraggingRedNote && draggedRedNote != null)
            {
                int currentLaneClamp = Mathf.Clamp(hoverLane, draggedRedNote.Lane, currentKeyCount - 1);
                int newWidth = currentLaneClamp - draggedRedNote.Lane + 1;
                if (draggedRedNote.Width != newWidth) {
                    draggedRedNote.Width = newWidth;
                    UpdateEditorNoteSprite(draggedRedNote);
                }
            }
            else if (isLeftUp)
            {
                if (isDraggingRedNote) RefreshLongNoteBodies();
                isDraggingRedNote = false;
                draggedRedNote = null;
            }
        }
    }

    private EditorNote AddNoteToEditor(long measure, long num, long den, int lane, int type, bool isRed = false, int width = 1, int layer = NoteLayer.Top)
    {
        EditorNote newNote = new EditorNote { Measure = measure, Num = num, Den = den, Lane = lane, Type = type, IsRedNote = isRed, Width = width, Layer = layer };
        GameObject obj = new GameObject($"Note_{measure}_{num}_{lane}_L{layer}");
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        newNote.Sr = sr;
        
        EnsureGraphicsResources();
        newNote.VisualObject = obj; 
        
        UpdateEditorNoteSprite(newNote); // 스프라이트/색/sortingOrder(층별) 설정
        
        obj.transform.SetParent(this.transform);
        editorNotes.Add(newNote);
        return newNote;
    }

    private void UpdateEditorNoteSprite(EditorNote note)
    {
        if (note.VisualObject == null) return;
        SpriteRenderer sr = note.VisualObject.GetComponent<SpriteRenderer>();
        if (sr == null) return;
        sr.color = Color.white;

        if (redNoteSprites == null) {
            redNoteSprites = new Dictionary<string, Sprite>();
            Sprite[] sprites = Resources.LoadAll<Sprite>("Sprites/NoteAsset/red_Note");
            foreach(var s in sprites) redNoteSprites[s.name] = s;
        }

        if (note.IsRedNote)
        {
            string prefix = $"{note.Width}x";
            string targetSpriteName = "";

            if (note.Type == 11) targetSpriteName = $"{prefix}_Basic";
            else if (note.Type == 12 || note.Type == 13) targetSpriteName = $"{prefix}_longhead";
            
            if (note.Type == 13) sr.flipY = true;

            float targetW = (note.Width - 1) * laneWidth + (laneWidth * 0.9f);

            if (redNoteSprites.TryGetValue(targetSpriteName, out Sprite spr)) {
                sr.sprite = spr;
                float currentW = spr.bounds.size.x;
                float dynamicScale = targetW / currentW;
                
                note.VisualObject.transform.localScale = new Vector3(dynamicScale, dynamicScale, 1f);
            } else {
                EnsureGraphicsResources();
                sr.sprite = fallbackSprite;
                Shader defaultShader = Shader.Find("Sprites/Default");
                if (defaultShader != null) sr.material = new Material(defaultShader);
                sr.color = new Color(1f, 0.2f, 0.2f, 0.8f); 
                note.VisualObject.transform.localScale = new Vector3(targetW, 0.3f, 1f);
            }
        }
        else if (note.Type == 4) 
        {
            EnsureGraphicsResources();
            sr.sprite = fallbackSprite;
            Shader defaultShader = Shader.Find("Sprites/Default");
            if (defaultShader != null) sr.material = new Material(defaultShader);
            
            sr.color = new Color(0.6f, 0.6f, 0.6f, 0.8f); 
            sr.sortingOrder = 1; 
            
            float totalWidth = currentKeyCount * laneWidth;
            note.VisualObject.transform.localScale = new Vector3(totalWidth, 0.02f, 1f);
        }
        else
        {
            bool isBlue = (note.Lane % 2 == 0);
            if (note.Type == 1 || note.Type == NoteTypes.Drag) sr.sprite = isBlue ? blueShort : greenShort;
            else if (note.Type == 2 || note.Type == NoteTypes.DragLongHead) sr.sprite = isBlue ? blueLongHead : greenLongHead;
            else if (note.Type == 3) sr.sprite = isBlue ? blueLongTail : greenLongTail;
            
            if (sr.sprite == null) {
                EnsureGraphicsResources();
                sr.sprite = fallbackSprite;
                Shader defaultShader = Shader.Find("Sprites/Default");
                if (defaultShader != null) sr.material = new Material(defaultShader);
                sr.color = isBlue ? new Color(0.2f, 0.6f, 1f) : new Color(0.2f, 1f, 0.5f);
                note.VisualObject.transform.localScale = new Vector3(laneWidth * 0.9f, 0.3f, 1f);
            } else {
                note.VisualObject.transform.localScale = new Vector3(noteScale, noteScale, 1f);
            }
        }

        // 층별 렌더링 순서: 2층(Top)이 항상 1층(Bottom)보다 위. 마디선(Type 4)은 맨 아래.
        sr.sortingOrder = (!note.IsRedNote && note.Type == 4) ? 1 : NoteLayer.HeadOrder(note.Layer);
        if (!note.IsRedNote && NoteTypes.IsDrag(note.Type))
        {
            // 드래그 노트: 연한 노랑으로 물들이고 얇게
            sr.color = Color.Lerp(Color.white, NoteArt.DragColor, 0.8f);
            Vector3 ls = note.VisualObject.transform.localScale;
            note.VisualObject.transform.localScale = new Vector3(ls.x, ls.y * NoteArt.DragHeightRatio, ls.z);
        }
        note.BaseAlpha = sr.color.a;
    }

    // 편집 중이 아닌 층은 흐리게, 재생 중에는 모든 층을 또렷하게
    private void ApplyLayerAlpha(SpriteRenderer sr, int layer, float baseAlpha)
    {
        if (sr == null) return;
        float mul = (isPlaying || layer == editLayer) ? 1f : inactiveLayerAlpha;
        float a = baseAlpha * mul;
        Color c = sr.color;
        if (Mathf.Abs(c.a - a) > 0.001f) { c.a = a; sr.color = c; }
    }

    private void UpdateNoteVisuals()
    {
        float totalWidth = currentKeyCount * laneWidth;
        float leftEdge = -totalWidth / 2f;
        float screenW = mainCam != null ? mainCam.orthographicSize * 2f * mainCam.aspect : 10f;
        
        bool isVisible = !IsSettingInputOpen;

        foreach (var note in editorNotes)
        {
            if (note.VisualObject != null)
            {
                float noteY = hitLineScreenOffsetY + (float)((note.AbsoluteBeat - currentBeat) * beatSpacing);
                
                if (note.IsRedNote) {
                    float startX = leftEdge + (note.Lane * laneWidth) + (laneWidth / 2f);
                    float endX = leftEdge + (Mathf.Min(note.Lane + note.Width - 1, currentKeyCount - 1) * laneWidth) + (laneWidth / 2f);
                    float centerX = (startX + endX) / 2f;
                    note.VisualObject.transform.position = new Vector3(centerX, noteY, -1f);
                } else if (note.Type == 4) { 
                    float centerX = leftEdge + (currentKeyCount * laneWidth) / 2f;
                    note.VisualObject.transform.position = new Vector3(centerX, noteY, -1f);
                } else {
                    if (note.Lane >= currentKeyCount) { note.VisualObject.SetActive(false); continue; }
                    float laneX = leftEdge + (note.Lane * laneWidth) + (laneWidth / 2f);
                    note.VisualObject.transform.position = new Vector3(laneX, noteY, -1f);
                }
                note.VisualObject.SetActive(isVisible && noteY > -1000f && noteY < 1000f);

                if (note.VisualObject.activeSelf) ApplyLayerAlpha(note.Sr, note.Layer, note.BaseAlpha);
            }
        }

        foreach (var link in longNoteBodies)
        {
            if (link.BodyObj != null && link.Sr != null)
            {
                if (link.Head.Lane >= currentKeyCount) { link.BodyObj.SetActive(false); continue; }

                float headY = hitLineScreenOffsetY + (float)((link.Head.AbsoluteBeat - currentBeat) * beatSpacing);
                float tailY = hitLineScreenOffsetY + (float)((link.Tail.AbsoluteBeat - currentBeat) * beatSpacing);
                float midY = (headY + tailY) / 2f;
                float distance = tailY - headY;

                if (link.Head.IsRedNote && link.Sr.drawMode == SpriteDrawMode.Sliced && link.Sr.sprite != null)
                {
                    float rs = leftEdge + (link.Head.Lane * laneWidth) + (laneWidth / 2f);
                    float re = leftEdge + (Mathf.Min(link.Head.Lane + link.Head.Width - 1, currentKeyCount - 1) * laneWidth) + (laneWidth / 2f);
                    float rTargetW = (link.Head.Width - 1) * laneWidth + (laneWidth * 0.9f);
                    LongBodyHelper.Fit(link.Sr, (rs + re) / 2f, headY, tailY, rTargetW, -0.5f);
                }
                else if (link.Sr.sprite != null && link.Sr.sprite.bounds.size.y > 0f)
                {
                    // [버그 수정] 스프라이트 피벗이 중앙이 아니어도 머리~꼬리 구간 밖으로 밀려 나가지 않도록 bounds.center 보정
                    Bounds sb = link.Sr.sprite.bounds;
                    float scaleY = distance / sb.size.y;
                    float scaleX;
                    float centerX;

                    if (link.Head.IsRedNote) {
                        float startX = leftEdge + (link.Head.Lane * laneWidth) + (laneWidth / 2f);
                        float endX = leftEdge + (Mathf.Min(link.Head.Lane + link.Head.Width - 1, currentKeyCount - 1) * laneWidth) + (laneWidth / 2f);
                        centerX = (startX + endX) / 2f;
                        float targetW = (link.Head.Width - 1) * laneWidth + (laneWidth * 0.9f);
                        scaleX = targetW / Mathf.Max(0.0001f, sb.size.x);
                    } else {
                        centerX = leftEdge + (link.Head.Lane * laneWidth) + (laneWidth / 2f);
                        scaleX = noteScale;
                    }

                    link.BodyObj.transform.position = new Vector3(centerX - sb.center.x * scaleX, midY - sb.center.y * scaleY, -0.5f);
                    link.BodyObj.transform.localScale = new Vector3(scaleX, scaleY, 1f);
                }

                link.BodyObj.SetActive(isVisible && tailY > -1000f && headY < 1000f);

                if (link.BodyObj.activeSelf) ApplyLayerAlpha(link.Sr, link.Head.Layer, link.BaseAlpha);
            }
        }
    }

    private void CreateBodyObject(EditorNote head, EditorNote tail)
    {
        GameObject obj = new GameObject($"LongBody_{head.Lane}_L{head.Layer}");
        obj.transform.SetParent(this.transform);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        EnsureGraphicsResources();

        if (head.IsRedNote) {
            string targetSpriteName = $"{head.Width}x_mid";
            if (redNoteSprites.TryGetValue(targetSpriteName, out Sprite midSpr)) {
                sr.sprite = LongBodyHelper.GetSlicedSprite(midSpr);
                sr.drawMode = SpriteDrawMode.Sliced; // 9-slice 몸통 (양 끝 고정, 가운데만 늘림)
            } else {
                sr.sprite = fallbackSprite;
                Shader defaultShader = Shader.Find("Sprites/Default");
                if (defaultShader != null) sr.material = new Material(defaultShader);
                sr.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            }
        } else {
            sr.sprite = (head.Lane % 2 == 0) ? blueLongBody : greenLongBody; 
            if (sr.sprite == null) {
                sr.sprite = fallbackSprite;
                Shader defaultShader = Shader.Find("Sprites/Default");
                if (defaultShader != null) sr.material = new Material(defaultShader);
                sr.color = (head.Lane % 2 == 0) ? new Color(0.2f, 0.4f, 1f, 0.6f) : new Color(0.2f, 1f, 0.4f, 0.6f);
            }
        }
        
        sr.sortingOrder = NoteLayer.BodyOrder(head.Layer);
        longNoteBodies.Add(new LongNoteBodyLink { Head = head, Tail = tail, BodyObj = obj, Sr = sr, BaseAlpha = sr.color.a });
    }

    private void RefreshLongNoteBodies()
    {
        foreach (var link in longNoteBodies) if (link.BodyObj) Destroy(link.BodyObj);
        longNoteBodies.Clear();

        if (redNoteSprites == null) {
            redNoteSprites = new Dictionary<string, Sprite>();
            Sprite[] sprites = Resources.LoadAll<Sprite>("Sprites/NoteAsset/red_Note");
            foreach(var s in sprites) redNoteSprites[s.name] = s;
        }

        // 1층/2층은 서로 연결되지 않으므로 층마다 따로 짝을 찾는다.
        for (int layer = NoteLayer.Bottom; layer <= NoteLayer.Top; layer++)
        {
            for (int lane = 0; lane < currentKeyCount; lane++) 
            {
                var regularNotes = editorNotes.Where(n => !n.IsRedNote && n.Layer == layer && n.Lane == lane).OrderBy(n => n.AbsoluteBeat).ToList();
                EditorNote head = null;
                
                foreach (var n in regularNotes)
                {
                    if (n.Type == 2 || n.Type == NoteTypes.DragLongHead) head = n; 
                    else if (n.Type == 3 && head != null) 
                    {
                        CreateBodyObject(head, n);
                        head = null;
                    }
                    else if (n.Type == 1 || n.Type == 4 || n.Type == NoteTypes.Drag) head = null;
                }

                var redNotes = editorNotes.Where(n => n.IsRedNote && n.Layer == layer && n.Lane == lane).OrderBy(n => n.AbsoluteBeat).ToList();
                EditorNote redHead = null;
                
                foreach (var n in redNotes)
                {
                    if (n.Type == 12) redHead = n; 
                    else if (n.Type == 13 && redHead != null) 
                    {
                        CreateBodyObject(redHead, n);
                        redHead = null;
                    }
                    else if (n.Type == 11) redHead = null;
                }
            }
        }
    }

    private void UpdateGridVisuals()
    {
        if (mainCam == null || currentState != EditorState.Editing) return;
        
        bool isVisible = !IsSettingInputOpen;
        if (!isVisible) return; 

        EnsureGraphicsResources();

        float screenW = mainCam.orthographicSize * 2f * mainCam.aspect;
        float totalWidth = currentKeyCount * laneWidth;
        float leftEdge = -totalWidth / 2f;
        float gridZ = 1f; 
        float thickness = 0.02f; 

        List<Vector2> fkYList = new List<Vector2>();   // 레인 선을 끊을 구간 (DeathArea · TIMING 빈 박)
        // DeathArea : 레인 선을 끊고(격자 없음) 붉은 띠로 표시
        List<Vector2> deathBands = new List<Vector2>();
        List<double[]> deathBeatRanges = new List<double[]>();
        foreach (var a in deathAreas) {
            double sb = GetBeatFromTime(a.Start), eb = GetBeatFromTime(a.End);
            deathBeatRanges.Add(new double[] { sb, eb });
            float dy1 = hitLineScreenOffsetY + (float)((sb - currentBeat) * beatSpacing);
            float dy2 = hitLineScreenOffsetY + (float)((eb - currentBeat) * beatSpacing);
            if (dy1 > 1000f || dy2 < -1000f) continue;
            Vector2 seg = new Vector2(Mathf.Max(-1000f, dy1), Mathf.Min(1000f, dy2));
            fkYList.Add(seg);
            deathBands.Add(seg);
        }
        // TIMING 앞의 빈 박도 같은 붉은 띠로 (노트 배치 금지)
        foreach (var z in GetGapZones()) {
            deathBeatRanges.Add(new double[] { z[0], z[1] });
            float gy1 = hitLineScreenOffsetY + (float)((z[0] - currentBeat) * beatSpacing);
            float gy2 = hitLineScreenOffsetY + (float)((z[1] - currentBeat) * beatSpacing);
            if (gy1 > 1000f || gy2 < -1000f) continue;
            Vector2 gseg = new Vector2(Mathf.Max(-1000f, gy1), Mathf.Min(1000f, gy2));
            fkYList.Add(gseg);
            deathBands.Add(gseg);
        }
        fkYList = fkYList.OrderBy(v => v.x).ToList();

        List<Vector2> regYList = new List<Vector2>();
        float curY = -1000f;
        foreach (var seg in fkYList) {
            if (curY < seg.x) regYList.Add(new Vector2(curY, seg.x));
            curY = Mathf.Max(curY, seg.y);
        }
        if (curY < 1000f) regYList.Add(new Vector2(curY, 1000f));

        Color gridColor = new Color(1f, 1f, 1f, 0.3f);

        for (int i = 0; i <= currentKeyCount; i++) {
            float lineX = leftEdge + (i * laneWidth);
            foreach (var seg in regYList) { 
                float h = seg.y - seg.x; 
                DrawLine(new Vector3(lineX, seg.x + h/2f, gridZ), new Vector3(thickness, h, 1f), gridColor); 
            }
        }

        Color deathColor = new Color(0.55f, 0.05f, 0.05f, 0.35f);
        foreach (var seg in deathBands) {
            float h = seg.y - seg.x;
            if (h > 0f) DrawLine(new Vector3(leftEdge + totalWidth / 2f, seg.x + h / 2f, gridZ), new Vector3(totalWidth, h, 1f), deathColor);
        }

        int minMeasure = Mathf.Max(1, (int)Math.Floor(currentBeat / 4.0) - 2);
        int maxMeasure = minMeasure + 12; 
        
        for (int m = minMeasure; m <= maxMeasure; m++) {
            for (int b = 0; b < currentSnap; b++) {
                if (b == 0) continue; 
                double beat = (m - 1) * 4.0 + ((double)b / currentSnap) * 4.0;
                bool beatInDeath = false;
                foreach (var r in deathBeatRanges) { if (beat > r[0] + 1e-5 && beat < r[1] - 1e-5) { beatInDeath = true; break; } }
                if (beatInDeath) continue; // DeathArea 안은 박자선도 그리지 않는다
                float y = hitLineScreenOffsetY + (float)((beat - currentBeat) * beatSpacing);
                
                if (y > -1000f && y < 1000f) {
                    Color c = (currentSnap >= 4 && b % (currentSnap / 4) == 0) ? new Color(0.6f, 0.6f, 0.6f, 0.8f) : new Color(0.3f, 0.3f, 0.3f, 0.4f);
                    DrawLine(new Vector3(leftEdge + totalWidth/2f, y, gridZ), new Vector3(totalWidth, thickness, 1f), c);
                }
            }
        }

        Color hitLineColor = isPlaying ? new Color(0f, 1f, 0f, 0.4f) : new Color(1f, 0f, 0f, 0.4f);
        float hHeight = 0.08f; 
        float width = totalWidth + 0.4f;
        float cx = leftEdge + totalWidth/2f;
        
        DrawLine(new Vector3(cx, hitLineScreenOffsetY, 0f), new Vector3(width, hHeight * 2f, 1f), hitLineColor);

    }

    public void SetSnap(int snapDenominator)
    {
        currentSnap = snapDenominator * 4; 
        if (!isPlaying) SnapCursorToBeat(currentBeat);
        UpdateUITexts();
    }

    public void SetKeyMode(int keys)
    {
        currentKeyCount = keys;
        RefreshLongNoteBodies();
        UpdateUITexts();
    }

    // amount: MUSICOFFSET 변화량(초). + = 노트가 늦어짐 (osu! 타이밍 Offset 을 늘리는 것과 같음)
    //  [부호 수정] 예전에는 + 를 누르면 '음악 시작 시각'이 늘어서 노트가 오히려 빨라졌다 (대중적인 리듬게임과 반대).
    public void AdjustOffset(double amount)
    {
        songStartOffset -= amount;
        songStartOffset = Math.Round(songStartOffset, 6);   // 변환기의 소수 ms(예: 1234.5ms)도 보존
        chartOffset = songStartOffset; 
        
        if (isPlaying)
        {
            if (edMusicScheduled && bgmSource != null && bgmSource.clip != null && AudioSettings.dspTime >= edDspAudioZero)
            {
                // 음악은 그대로 두고 화면(차트 시각)만 즉시 옮긴다
                currentPlaybackTime -= amount;
                edPlayFromTime = Math.Min(edPlayFromTime, currentPlaybackTime);
                currentBeat = GetBeatFromTime(currentPlaybackTime);
                edHitCheckBeat = GetBeatFromTime(currentPlaybackTime + hitSoundLatencyCorrection);   // 옮긴 만큼 탭음이 몰려 나오지 않게
            }
            else ScheduleEditorMusic(currentPlaybackTime); // 아직 음악이 시작 전이면 예약을 다시
        }
        else
        {
            currentPlaybackTime = GetTimeFromBeat(currentBeat);
        }
        UpdateUITexts();
    }

    // fromTime(지금의 차트 시각)부터 음악이 이어지도록 예약 재생
    //  preroll = false : 지금 시각이 fromTime (예약에 걸리는 약 0.1초 분량의 음악은 건너뜀 — 재생 도중 다시 예약할 때)
    //  preroll = true  : fromTime 의 음악부터 빠짐없이 나오도록, 시계를 그 시간만큼 앞당긴 값을 돌려줌 (재생 시작할 때)
    //  돌려주는 값 = 지금 이 순간의 차트 시각
    private double ScheduleEditorMusic(double fromTime) { return ScheduleEditorMusic(fromTime, false); }

    private double ScheduleEditorMusic(double fromTime, bool preroll)
    {
        edMusicScheduled = false;
        isMusicPlaying = false;
        if (bgmSource == null || bgmSource.clip == null) return fromTime;
        AudioClip clip = bgmSource.clip;
        // [버그 수정] 음악 데이터가 다 올라오기 전에 재생하면 앞부분(약 0.5초)이 소리 없이 지나가 버렸다
        //  → 다 올라올 때까지 기다렸다가 그 시점의 위치로 예약한다 (HandlePlaybackAndScroll 에서 다시 부름)
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            if (clip.loadState != AudioDataLoadState.Loading) clip.LoadAudioData();
            edWaitingAudio = true;
            return fromTime;
        }
        edWaitingAudio = false;

        int bufLen, bufCount;
        AudioSettings.GetDSPBufferSize(out bufLen, out bufCount);
        int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        double minLead = Math.Max(0.1, bufLen * (double)Math.Max(1, bufCount) / sr * 1.5 + 0.02);

        double audioStart = EditorAudioStart;
        // musicAt : 음악이 실제로 소리 나기 시작하는 차트 시각,  clockNow : 지금 이 순간의 차트 시각
        double clockNow, musicAt;
        if (preroll)
        {
            musicAt = Math.Max(audioStart, fromTime);
            clockNow = Math.Min(fromTime, musicAt - minLead);
        }
        else
        {
            clockNow = fromTime;
            musicAt = Math.Max(audioStart, fromTime + minLead);
        }
        double startPos = musicAt - audioStart;            // 음악 파일 안의 위치
        if (startPos >= clip.length) return fromTime;

        double startDsp = AudioSettings.dspTime + (musicAt - clockNow);
        bgmSource.Stop();
        bgmSource.pitch = 1f;
        bgmSource.timeSamples = Mathf.Clamp((int)Math.Round(startPos * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
        bgmSource.PlayScheduled(startDsp);
        edDspAudioZero = startDsp - startPos;
        edMusicScheduled = true;
        return clockNow;
    }

    long GCD(long a, long b) { return b == 0 ? (a == 0 ? 1 : a) : GCD(b, a % b); }

    // ===== 박 ↔ 시각 =====
    //  BPM 줄  : 앞에서부터 박 × BPM 누적
    //  TIMING 줄: 그 박의 시각을 고정 (osu! 빨간 줄과 같음) → 누적 오차 없음
    //   TIMING 앞쪽에 남는 '빈 박'(앞 구간이 시각상 이미 끝난 박)은 GapZones 로 표시하고 노트 배치를 막는다.
    private struct TimingSeg { public double Beat, Time, Bpm; }
    private List<TimingSeg> timingSegCache;
    private double timingSegKey;

    private List<TimingSeg> GetTimingSegs()
    {
        // bpmChanges 내용이 바뀌었을 때만 다시 계산 (간단한 체크섬, 할당 없음)
        double key = bpmChanges.Count;
        for (int i = 0; i < bpmChanges.Count; i++)
        {
            var b = bpmChanges[i];
            key = key * 1.000003 + b.AbsoluteBeat * 7.13 + b.Bpm * 3.71 + (b.HasAnchor ? b.AnchorMs * 1.37 + 0.5 : 0.77) + i * 0.011;
        }
        if (timingSegCache != null && key == timingSegKey) return timingSegCache;

        var sorted = bpmChanges.OrderBy(b => b.AbsoluteBeat).ToList();
        var list = new List<TimingSeg>();
        double bpm0 = sorted.Count > 0 ? sorted[0].Bpm : 120.0;
        list.Add(new TimingSeg { Beat = 0, Time = 0, Bpm = bpm0 });
        foreach (var b in sorted)
        {
            if (b.AbsoluteBeat <= 1e-9) { var f = list[0]; f.Bpm = b.Bpm; list[0] = f; continue; }
            var p = list[list.Count - 1];
            double t = b.HasAnchor ? b.AnchorMs / 1000.0 : p.Time + (b.AbsoluteBeat - p.Beat) * (60.0 / p.Bpm);
            list.Add(new TimingSeg { Beat = b.AbsoluteBeat, Time = t, Bpm = b.Bpm });
        }
        timingSegCache = list; timingSegKey = key;
        return list;
    }

    private double GetTimeFromBeat(double targetBeat) 
    {
        var segs = GetTimingSegs();
        if (targetBeat <= 0) return targetBeat * (60.0 / segs[0].Bpm);
        TimingSeg s = segs[0];
        for (int i = 1; i < segs.Count && segs[i].Beat <= targetBeat + 1e-9; i++) s = segs[i];
        return s.Time + (targetBeat - s.Beat) * (60.0 / s.Bpm);
    }

    private double GetBeatFromTime(double time) 
    {
        var segs = GetTimingSegs();
        if (time < 0) return time / (60.0 / segs[0].Bpm);
        int idx = 0;
        for (int i = 1; i < segs.Count && segs[i].Time <= time + 1e-9; i++) idx = i;
        double beat = segs[idx].Beat + (time - segs[idx].Time) / (60.0 / segs[idx].Bpm);
        // 다음 TIMING 이 시각상 조금 늦게 오는 경우(빈 시간)에는 그 박에서 기다림
        if (idx + 1 < segs.Count) beat = Math.Min(beat, segs[idx + 1].Beat);
        return beat;
    }

    // TIMING 줄 바로 앞의 '빈 박' 구간 [시작 박, 끝 박] (노트 배치 금지, 붉은 띠로 표시)
    private List<double[]> GetGapZones()
    {
        var segs = GetTimingSegs();
        var zones = new List<double[]>();
        for (int i = 1; i < segs.Count; i++)
        {
            var p = segs[i - 1];
            double realEnd = p.Beat + (segs[i].Time - p.Time) / (60.0 / p.Bpm);
            if (realEnd < segs[i].Beat - 1e-4) zones.Add(new double[] { Math.Max(p.Beat, realEnd), segs[i].Beat });
        }
        return zones;
    }

    private void LoadChartForEditor(int songId)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "chart", $"{songId}_{editLevelId}.txt");
        
        foreach (var n in editorNotes) if (n.VisualObject) Destroy(n.VisualObject); editorNotes.Clear();
        foreach (var b in longNoteBodies) if (b.BodyObj) Destroy(b.BodyObj); longNoteBodies.Clear();
        
        bpmChanges.Clear(); speedChanges.Clear(); stopEvents.Clear(); startEvents.Clear();
        foldEvents.Clear(); deathAreas.Clear(); originalOtherEvents.Clear();
        isInputtingOffset = false;
        
        songStartOffset = 0.0; chartOffset = songStartOffset; currentKeyCount = 4;
        bool hasMusicOffset = false;
        edMusicScheduled = false;
        double initialBpm = 120.0; double initialSpeed = 1.0;
        
        currentBeat = 0; currentPlaybackTime = 0;
        activeLNs.Clear();
        editorCombo = 0; comboAnimTimer = 0f;
        isPlaying = false; isMusicPlaying = false;
        isInputtingBpm = false; isInputtingSpeed = false; isInputtingDeathArea = false; deathAreaInputStage = 0; 
        isRedNoteMode = false; isDraggingRedNote = false; draggedRedNote = null;
        inputBuffer = "";
        escapePressCount = 0;
        escapePressTimer = 0f;
        
        if (settingUIGroup) { settingUIGroup.alpha = 0f; settingUIGroup.gameObject.SetActive(false); }
        if (chartUIGroup) { chartUIGroup.alpha = 1f; chartUIGroup.gameObject.SetActive(true); }
        
        if (bgmSource != null) bgmSource.Stop();

        if (!File.Exists(path)) return;

        string[] allLines = File.ReadAllLines(path, System.Text.Encoding.UTF8);

        // --- 1st Pass: BPM, OFFSET, SPEED 정보 선제 로드 ---
        foreach (string line in allLines)
        {
            string cleanLine = line.Contains("#") ? line.Substring(0, line.IndexOf('#')).Trim() : line.Trim();
            if (string.IsNullOrWhiteSpace(cleanLine)) continue;

            string[] data = cleanLine.Split(',');
            string type = data[0].Trim().ToUpper();
            
            if (type == "BPM" && data.Length == 2) { initialBpm = double.Parse(data[1].Trim(), CultureInfo.InvariantCulture); }
            else if (type == ChartOffset.Key || type == "OFFSET" || type == "SONGSTARTOFFSET") {
                // MUSICOFFSET(ms) 이 우선, 없으면 예전 OFFSET(초, 반대 부호). 저장할 때는 MUSICOFFSET 으로 바뀐다
                double start; bool isNew;
                if (ChartOffset.TryParse(type, data, out start, out isNew) && (isNew || !hasMusicOffset)) {
                    songStartOffset = start; chartOffset = songStartOffset;
                    if (isNew) hasMusicOffset = true;
                }
            }
            else if (cleanLine.StartsWith("KeyMode:")) { currentKeyCount = int.Parse(cleanLine.Split(':')[1].Trim()); }
            else if (type == "SPEED" && data.Length == 2) { initialSpeed = double.Parse(data[1].Trim(), CultureInfo.InvariantCulture); }
            else if (cleanLine.StartsWith("NoteSpeed:", StringComparison.OrdinalIgnoreCase)) { initialSpeed = double.Parse(cleanLine.Split(':')[1].Trim(), CultureInfo.InvariantCulture); }
            else if (type == "BPM" && data.Length >= 5) { bpmChanges.Add(new EditorBpm { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]), Bpm = double.Parse(data[4], CultureInfo.InvariantCulture) }); }
            else if (type == "TIMING" && data.Length >= 6) {
                // TIMING, 마디, 분자, 분모, BPM, 시각ms : 시각 고정 BPM 줄 (osu! 빨간 줄)
                bpmChanges.Add(new EditorBpm { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]),
                    Bpm = double.Parse(data[4], CultureInfo.InvariantCulture), AnchorMs = double.Parse(data[5], CultureInfo.InvariantCulture) });
            }
            else if (type == "SPEED" && data.Length >= 6) {
                // 신형: SPEED, 마디, 분자, 분모, 층, 속도
                // 7번째 칸이 SHOW 면 게임에서 SPEED UP / DOWN 표시. 층 0 = 두 층 모두
                int spdLayer = int.Parse(data[4].Trim());
                double spdVal = double.Parse(data[5].Trim(), CultureInfo.InvariantCulture);
                bool spdShow = data.Length >= 7 && data[6].Trim().ToUpperInvariant() == "SHOW";
                for (int L = NoteLayer.Bottom; L <= NoteLayer.Top; L++) {
                    if (spdLayer == 1 || spdLayer == 2) { if (L != spdLayer) continue; }
                    speedChanges.Add(new EditorSpeed { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]), Multiplier = spdVal, Layer = L, ShowChange = spdShow });
                }
            }
            else if (type == "SPEED" && data.Length >= 5) {
                // 구형: SPEED, 마디, 분자, 분모, 속도  → 두 층 모두에 적용
                for (int L = NoteLayer.Bottom; L <= NoteLayer.Top; L++)
                    speedChanges.Add(new EditorSpeed { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]), Multiplier = double.Parse(data[4], CultureInfo.InvariantCulture), Layer = L });
            }
        }

        if (!bpmChanges.Any(b => b.AbsoluteBeat == 0)) {
            bpmChanges.Add(new EditorBpm { Measure = 1, Num = 0, Den = 4, Bpm = initialBpm });
        }
        bpmChanges = bpmChanges.OrderBy(b => b.AbsoluteBeat).ToList();
        
        for (int L = NoteLayer.Bottom; L <= NoteLayer.Top; L++) {
            int layerCopy = L;
            if (!speedChanges.Any(s => s.Layer == layerCopy && s.AbsoluteBeat == 0)) {
                speedChanges.Add(new EditorSpeed { Measure = 1, Num = 0, Den = 4, Multiplier = initialSpeed, Layer = L });
            }
        }
        speedChanges = speedChanges.OrderBy(s => s.AbsoluteBeat).ThenBy(s => s.Layer).ToList();

        // --- 2nd Pass: 실제 노트 및 이벤트 로드 ---
        foreach (string line in allLines)
        {
            string cleanLine = line.Contains("#") ? line.Substring(0, line.IndexOf('#')).Trim() : line.Trim();
            if (string.IsNullOrWhiteSpace(cleanLine)) continue;

            string[] data = cleanLine.Split(',');
            string type = data[0].Trim().ToUpper();

            if (type == "BPM" || type == "TIMING" || type == ChartOffset.Key || type == "OFFSET" || type == "SONGSTARTOFFSET" || type == "SPEED" || cleanLine.StartsWith("KeyMode:") || cleanLine.StartsWith("NoteSpeed:", StringComparison.OrdinalIgnoreCase)) {
                continue; // 1st Pass에서 이미 처리함
            }
            
            if (type == "DEATHAREA" && data.Length >= 3) {
                double ds = double.Parse(data[1].Trim(), CultureInfo.InvariantCulture);
                double de = double.Parse(data[2].Trim(), CultureInfo.InvariantCulture);
                if (de > ds) deathAreas.Add(new EditorDeathArea { Start = ds, End = de });
            }
            else if (type == "STOP" && data.Length >= 4) { stopEvents.Add(new EditorStopEvent { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]) }); }
            else if (type == "START" && data.Length >= 4) { startEvents.Add(new EditorStartEvent { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]) }); }
            else if (type == "FREEKEY") {
                continue; // 프리키(FREE-KEY) 기능은 삭제됨 — 저장하면 지워진다
            }
            else if (type == "AREA") {
                continue; // 영역(AREA) 기능은 삭제됨 — 저장하면 지워진다
            }
            else if ((type == "FOLD" || type == "UNFOLD" || type == "HIDE" || type == "SHOW") && data.Length >= 5) {   // 레인 사라짐 / 드러남
                double dur;
                if (!double.TryParse(data[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out dur)) dur = 0.5;
                foldEvents.Add(new EditorFoldEvent { Measure = long.Parse(data[1].Trim()), Num = long.Parse(data[2].Trim()), Den = long.Parse(data[3].Trim()), Fold = type == "FOLD" || type == "HIDE", Duration = dur });
            }
            else if (type == "NOTE" && data.Length >= 5) {
                long m = long.Parse(data[1]), num = long.Parse(data[2]), den = long.Parse(data[3]);
                
                if (data.Length == 5 && !data[4].Contains("_") && !data[4].Contains("(") && data[4].Trim().Length >= currentKeyCount) {
                    // 구형 압축 포맷 (예: 1000) - 전부 2층
                    string val = data[4].Trim();
                    for (int i = 0; i < val.Length && i < 8; i++) {
                        if (val[i] != '0') { int noteType = NoteTypes.Normalize(int.Parse(val[i].ToString())); if (noteType != 5 && noteType != 6) AddNoteToEditor(m, num, den, i, noteType); }
                    }
                } else {
                    // 레인 칸 형식: "2층노트" 또는 "2층노트(1층노트)"  예) 11_3(12)
                    for (int i = 0; i < currentKeyCount && (i + 4) < data.Length; i++) {
                        NoteCellParser.SplitCell(data[i + 4], out string topToken, out string bottomToken);

                        // (예전 화살표 노트 7 · 8 은 일반 탭으로 읽음)
                        if (NoteCellParser.TryParseToken(topToken, out int tType, out int tWidth, out bool tRed))
                            AddNoteToEditor(m, num, den, i, NoteTypes.Normalize(tType), tRed, tWidth, NoteLayer.Top);

                        if (NoteCellParser.TryParseToken(bottomToken, out int bType, out int bWidth, out bool bRed))
                            AddNoteToEditor(m, num, den, i, NoteTypes.Normalize(bType), bRed, bWidth, NoteLayer.Bottom);
                    }
                }
            }
            else if (type == "UI" && data.Length >= 4)
            {
                string targetUI = data[1].Trim();
                // 프리키 구간용 UI 줄(FREEKEYUI / BasicUI)은 기능 삭제와 함께 버림
                if (targetUI == "FREEKEYUI" || targetUI == "BasicUI") continue; 
                originalOtherEvents.Add(cleanLine);
            }
            else { originalOtherEvents.Add(cleanLine); }
        }

        RefreshLongNoteBodies();
    }

    public void SaveChartFile(int songId)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "chart", $"{songId}_{editLevelId}.txt");
        List<string> output = new List<string>();

        double initialBpm = bpmChanges.Count > 0 ? bpmChanges[0].Bpm : 120.0;
        
        // 초기 속도 줄(SPEED, 값)은 2층 기준. 1층의 초기 속도가 다르면 아래에서 층 지정 변속으로 따로 기록한다.
        var baseSpeedEvent = speedChanges.FirstOrDefault(s => s.Layer == NoteLayer.Top && s.AbsoluteBeat == 0);
        double initialSpeedValue = baseSpeedEvent != null ? baseSpeedEvent.Multiplier : 1.0;

        output.Add($"BPM, {initialBpm.ToString("0.######", CultureInfo.InvariantCulture)}");
        output.Add(ChartOffset.Line(songStartOffset));   // MUSICOFFSET, ms (osu! 와 같은 부호)
        output.Add($"KeyMode: {currentKeyCount}");
        output.Add($"SPEED, {initialSpeedValue.ToString("0.00", CultureInfo.InvariantCulture)}");

        foreach (var b in bpmChanges.Skip(1))
        {
            if (b.HasAnchor) output.Add($"TIMING, {b.Measure}, {b.Num}, {b.Den}, {b.Bpm.ToString(CultureInfo.InvariantCulture)}, {b.AnchorMs.ToString("0.######", CultureInfo.InvariantCulture)}");
            else output.Add($"BPM, {b.Measure}, {b.Num}, {b.Den}, {b.Bpm.ToString(CultureInfo.InvariantCulture)}");
        }
        
        // 변속 저장: 같은 위치/같은 값이 두 층에 모두 있으면 구형(층 생략) 한 줄로, 아니면 층 지정 형식으로 기록
        foreach (var grp in speedChanges.GroupBy(s => new { s.Measure, s.Num, s.Den }).OrderBy(g => g.First().AbsoluteBeat))
        {
            var l1 = grp.FirstOrDefault(s => s.Layer == NoteLayer.Bottom);
            var l2 = grp.FirstOrDefault(s => s.Layer == NoteLayer.Top);
            bool isBase = grp.First().AbsoluteBeat == 0;

            if (isBase)
            {
                // 2층 기준값은 초기 속도 줄에 이미 기록됨. 1층 값이 다를 때만 별도 기록.
                if (l1 != null && l2 != null && Math.Abs(l1.Multiplier - l2.Multiplier) > 1e-9)
                    output.Add($"SPEED, {l1.Measure}, {l1.Num}, {l1.Den}, {NoteLayer.Bottom}, {l1.Multiplier.ToString("0.00", CultureInfo.InvariantCulture)}");
                else if (l2 == null && l1 != null && Math.Abs(l1.Multiplier - initialSpeedValue) > 1e-9)
                    output.Add($"SPEED, {l1.Measure}, {l1.Num}, {l1.Den}, {NoteLayer.Bottom}, {l1.Multiplier.ToString("0.00", CultureInfo.InvariantCulture)}");
                continue;
            }

            // SPEED UP / DOWN 표시가 켜진 변속은 끝에 ", SHOW" (층 0 = 두 층 모두)
            if (l1 != null && l2 != null && Math.Abs(l1.Multiplier - l2.Multiplier) < 1e-9)
            {
                if (l1.ShowChange || l2.ShowChange)
                    output.Add($"SPEED, {l2.Measure}, {l2.Num}, {l2.Den}, 0, {l2.Multiplier.ToString("0.00", CultureInfo.InvariantCulture)}, SHOW");
                else
                    output.Add($"SPEED, {l2.Measure}, {l2.Num}, {l2.Den}, {l2.Multiplier.ToString("0.00", CultureInfo.InvariantCulture)}");
            }
            else
            {
                if (l1 != null) output.Add($"SPEED, {l1.Measure}, {l1.Num}, {l1.Den}, {NoteLayer.Bottom}, {l1.Multiplier.ToString("0.00", CultureInfo.InvariantCulture)}" + (l1.ShowChange ? ", SHOW" : ""));
                if (l2 != null) output.Add($"SPEED, {l2.Measure}, {l2.Num}, {l2.Den}, {NoteLayer.Top}, {l2.Multiplier.ToString("0.00", CultureInfo.InvariantCulture)}" + (l2.ShowChange ? ", SHOW" : ""));
            }
        }
        
        foreach (var s in stopEvents) output.Add($"STOP, {s.Measure}, {s.Num}, {s.Den}");
        foreach (var s in startEvents) output.Add($"START, {s.Measure}, {s.Num}, {s.Den}");
        foreach (var a in deathAreas.OrderBy(x => x.Start))
            output.Add($"DEATHAREA, {a.Start.ToString("0.0000", CultureInfo.InvariantCulture)}, {a.End.ToString("0.0000", CultureInfo.InvariantCulture)}");
        
        // 레인 사라짐 / 드러남
        foreach (var f in foldEvents.OrderBy(x => x.AbsoluteBeat))
            output.Add($"{(f.Fold ? "FOLD" : "UNFOLD")}, {f.Measure}, {f.Num}, {f.Den}, {f.Duration.ToString("0.###", CultureInfo.InvariantCulture)}");

        output.AddRange(originalOtherEvents);

        var groupedNotes = editorNotes.GroupBy(n => new { n.Measure, n.Num, n.Den }).OrderBy(g => g.Key.Measure).ThenBy(g => (double)g.Key.Num / g.Key.Den);
        foreach (var group in groupedNotes)
        {
            string[] topStrs = new string[currentKeyCount];
            string[] bottomStrs = new string[currentKeyCount];
            
            foreach (var note in group) {
                if (note.Lane >= currentKeyCount) continue;
                string token = note.IsRedNote ? $"{note.Type}_{note.Width}" : note.Type.ToString(CultureInfo.InvariantCulture);   // 두 자리 종류(10 = 드래그 롱노트 시작)도 그대로
                if (note.Layer == NoteLayer.Bottom) bottomStrs[note.Lane] = token;
                else topStrs[note.Lane] = token;
            }

            string[] laneStrs = new string[currentKeyCount];
            for (int i = 0; i < currentKeyCount; i++) {
                string main = topStrs[i] ?? "0";
                // 1층 노트는 괄호 안에 기록: 11_3(12)
                laneStrs[i] = bottomStrs[i] != null ? $"{main}({bottomStrs[i]})" : main;
            }
            string saveStr = string.Join(", ", laneStrs); 
            output.Add($"NOTE, {group.Key.Measure}, {group.Key.Num}, {group.Key.Den}, {saveStr}");
        }


        File.WriteAllLines(path, output, System.Text.Encoding.UTF8);
        Debug.Log("채보가 성공적으로 저장되었습니다!");

        // 저장한 채보를 게임과 같은 규칙으로 다시 읽어 전체 노트 수(롱노트 틱 포함)를 세고 songs.json 에 기록
        UpdateNoteCountInSongsJson(songId, editLevelId, path);
    }

    private void UpdateNoteCountInSongsJson(int songId, int levelId, string chartPath)
    {
        ChartNoteCounter.Result r = ChartNoteCounter.CountFile(chartPath);
        if (!r.Ok)
        {
            Debug.LogWarning("[NoteCount] 채보를 읽지 못해 노트 수를 갱신하지 않았습니다: " + r.Error);
            return;
        }

        string jsonPath = Path.Combine(Application.streamingAssetsPath, "data", "songs.json");
        string key = "NoteCount_" + DifficultyHelper.GetName(levelId);
        string message;
        if (SongsJsonEditor.SetIntField(jsonPath, songId, key, r.Total, out message))
            Debug.Log($"[NoteCount] {songId} {DifficultyHelper.GetName(levelId)} = {r.Total} (notes {r.Notes} + long ticks {r.Ticks}) → songs.json \"{key}\" {message}");
        else
            Debug.LogWarning("[NoteCount] songs.json 갱신 실패: " + message);

        // 곡 길이(m:ss)도 함께 기록 → 곡 선택창이 음원을 통째로 읽지 않아도 됨
        if (bgmSource != null && bgmSource.clip != null)
        {
            int sec = Mathf.FloorToInt(bgmSource.clip.length);
            string len = (sec / 60) + ":" + (sec % 60).ToString("00");
            string lenMsg;
            if (SongsJsonEditor.SetStringField(jsonPath, songId, "Length", len, out lenMsg))
                Debug.Log($"[Length] {songId} = {len} → songs.json \"Length\" {lenMsg}");
        }

        // 메모리에 들고 있는 곡 정보도 맞춰 둔다 (에디터 → 게임으로 바로 넘어가도 같은 값)
        if (CoreManager.Instance != null && CoreManager.Instance.CurrentSongData != null && CoreManager.Instance.CurrentSongData.Id == songId)
        {
            SongData s = CoreManager.Instance.CurrentSongData;
            if (levelId == 1) s.NoteCount_EASY = r.Total;
            else if (levelId == 3) s.NoteCount_INSANE = r.Total;
            else if (levelId == DifficultyHelper.Special) s.NoteCount_SPECIAL = r.Total;
            else s.NoteCount_HARD = r.Total;
        }
    }

    // 에디터 미리보기용: 시간(초) → 그 시점의 BPM
    private double GetEditorBpmAtTime(double time)
    {
        double beat = GetBeatFromTime(time);
        var b = bpmChanges.LastOrDefault(x => x.AbsoluteBeat <= beat + 1e-6);
        return b != null ? b.Bpm : (bpmChanges.Count > 0 ? bpmChanges[0].Bpm : 120.0);
    }

    private void UpdateUITexts()
    {
        if (selectedSnapText != null) selectedSnapText.text = $"1/{(currentSnap / 4)}";
        UpdateLayerText();
        
        string baseMode = $"{currentKeyCount}B";
        if (isDragNoteMode) baseMode += "  DRAG";
        if (modeText != null) modeText.text = baseMode;
        if (modeText != null) modeText.color = isRedNoteMode ? Color.red : (isDragNoteMode ? NoteArt.DragColor : Color.white);
        
        if (timeText != null) timeText.text = $"{currentPlaybackTime:F4}s";
        
        // MUSICOFFSET (ms): 음악 파일에서 0박이 나오는 시각. osu! 타이밍 Offset 과 같은 값 · 같은 부호
        double musicOffsetMs = ChartOffset.ToMusicOffsetMs(songStartOffset);
        if (offsetText != null) offsetText.text = Math.Abs(musicOffsetMs).ToString("0.#", CultureInfo.InvariantCulture) + " ms";
        if (offsetSignText != null) offsetSignText.text = musicOffsetMs >= 0 ? "PLUS(+)" : "MINUS(-)";

        if (songNameText != null) {
            if (!string.IsNullOrEmpty(currentSongName)) songNameText.text = currentSongName;
            else songNameText.text = $"Song {editSongId}";
        }

        if (bpmChanges.Count > 0 && bpmText != null) {
            var cbpm = bpmChanges.LastOrDefault(b => b.AbsoluteBeat <= currentBeat + 0.001) ?? bpmChanges[0];
            bpmText.text = $"BPM {cbpm.Bpm:F0}";
        }

        if (measureBeatText != null) {
            int currentM = (int)Math.Floor(currentBeat / 4.0) + 1;
            double fraction = (currentBeat / 4.0) - Math.Floor(currentBeat / 4.0);
            int currentB = (int)Math.Round(fraction * currentSnap);
            measureBeatText.text = $"마디 {currentM}의 {currentB}/{currentSnap} 박자";
        }

        if (centerMessageText != null) {
            centerMessageText.text = IsDeathAreaBeat(currentBeat) ? "DEATH AREA (노트 배치 불가)" : "";
        }

        if (editorComboText != null && editorComboTitleText != null)
        {
            if (comboAnimTimer > 0 && editorCombo > 0)
            {
                editorComboText.gameObject.SetActive(true);
                editorComboTitleText.gameObject.SetActive(true);
                editorComboText.text = editorCombo.ToString();
                
                float p = 1f - comboAnimTimer; 
                float scale = 1f;

                if (p < 0.1f) scale = Mathf.Lerp(1f, 1.4f, p / 0.1f);
                else if (p < 0.3f) scale = Mathf.Lerp(1.4f, 1f, (p - 0.1f) / 0.2f);
                
                editorComboText.transform.localScale = initialComboScale * scale;
                editorComboTitleText.transform.localScale = initialComboTitleScale * scale;
                
                float alpha = 1f;
                if (comboAnimTimer < 0.3f) alpha = comboAnimTimer / 0.3f;
                
                editorComboText.color = new Color(1f, 1f, 1f, alpha);
                editorComboTitleText.color = new Color(1f, 1f, 1f, alpha);
            }
            else
            {
                editorComboText.gameObject.SetActive(false);
                editorComboTitleText.gameObject.SetActive(false);
            }
        }
    }

    private void DrawRoundedLine(float x, float y, float w, float h, Color c)
    {
        Texture2D tex = GetColorTex(c); Texture2D circle = GetCircleTex(c);
        GUI.DrawTexture(new Rect(x, y - h/2f, w, h), tex);
        GUI.DrawTexture(new Rect(x - h/2f, y - h/2f, h, h), circle);
        GUI.DrawTexture(new Rect(x + w - h/2f, y - h/2f, h, h), circle);
    }

    // 그린 알약의 전체 영역(GUI 좌표)을 돌려줌
    private Rect DrawPill(float cx, float cy, string text, Color c, Font font, int fontSize = 16, float h = 28f)
    {
        GUIStyle style = new GUIStyle();
        if (font != null) style.font = font;
        style.fontSize = fontSize; style.normal.textColor = Color.white; style.alignment = TextAnchor.MiddleCenter;

        Vector2 size = style.CalcSize(new GUIContent(text));
        float w = size.x + 24f; 
        float x = cx - w/2f; float y = cy - h/2f;

        Texture2D tex = GetColorTex(c); Texture2D circle = GetCircleTex(c);
        GUI.DrawTexture(new Rect(x, y, w, h), tex);
        GUI.DrawTexture(new Rect(x - h/2f, y, h, h), circle);
        GUI.DrawTexture(new Rect(x + w - h/2f, y, h, h), circle);
        GUI.Label(new Rect(x, y, w, h), text, style);
        return new Rect(x - h / 2f, y, w + h, h);
    }

    // ----- SPEED 표시 우클릭 → 게임에서 SPEED UP / DOWN 표시 켜고 끄기 -----
    private readonly List<KeyValuePair<Rect, List<EditorSpeed>>> speedPillHits = new List<KeyValuePair<Rect, List<EditorSpeed>>>();

    // 마우스(화면 좌표)가 SPEED 알약 위면 표시를 뒤집고 true
    private bool TryToggleSpeedShowAt(Vector2 screenPos)
    {
        float scale = (Screen.height / 1080f) * 0.85f; // OnGUI 와 같은 배율
        Vector2 gui = new Vector2(screenPos.x / scale, (Screen.height - screenPos.y) / scale);
        for (int i = speedPillHits.Count - 1; i >= 0; i--)
        {
            if (!speedPillHits[i].Key.Contains(gui)) continue;
            var list = speedPillHits[i].Value;
            bool on = !list.Any(o => o.ShowChange);
            foreach (var o in list) o.ShowChange = on;
            return true;
        }
        return false;
    }

    // 이 박자에 직접 추가한 마디선(Type 4)이 있는지
    private bool HasCustomMeasureLineAt(double beat)
    {
        foreach (var n in editorNotes)
            if (n.Type == 4 && Math.Abs(n.AbsoluteBeat - beat) < 1e-6) return true;
        return false;
    }

    void OnGUI()
    {
        float scale = (Screen.height / 1080f) * 0.85f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float invScale = 1f / scale;


        bool isVisible = !IsSettingInputOpen;
        if (Event.current.type == EventType.Repaint) speedPillHits.Clear(); // 다시 그릴 때마다 우클릭 영역 갱신
        if (currentState == EditorState.Editing && mainCam != null && isVisible)
        {
            List<UIDrawEvent> uiEvents = new List<UIDrawEvent>();
            float screenW = mainCam.orthographicSize * 2f * mainCam.aspect;
            float leftEdgeX = -(currentKeyCount * laneWidth) / 2f;
            float rightEdgeX = (currentKeyCount * laneWidth) / 2f;
            float camHalfHeight = mainCam.orthographicSize;

            int minMeasure = Mathf.Max(1, (int)Math.Floor(currentBeat / 4.0) - 2);
            int maxMeasure = minMeasure + 12;
            for (int m = minMeasure; m <= maxMeasure; m++) {
                double beat = (m - 1) * 4.0;
                float y = hitLineScreenOffsetY + (float)((beat - currentBeat) * beatSpacing);
                // 같은 박자에 직접 추가한 마디선(4번 키)이 있으면 원래 마디선은 표시하지 않음
                if (HasCustomMeasureLineAt(beat)) continue;
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = beat, y = (Screen.height - sp.y) * invScale, text = $"M: {m}", shortText = $"{m}", color = new Color(1f, 0.2f, 0.2f), isWide = false });
                }
            }

            foreach (var ev in bpmChanges) {
                if (ev.AbsoluteBeat == 0) continue; 
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale, text = ev.HasAnchor ? $"BPM: {ev.Bpm:0.##} @ {ev.AnchorMs / 1000.0:F3}s" : $"BPM: {ev.Bpm:F0}", shortText = $"{ev.Bpm:F0}", color = new Color(1f, 0.7f, 0.2f), isWide = false });
                }
            }
            foreach (var ev in speedChanges) {
                if (ev.AbsoluteBeat == 0 && Math.Abs(ev.Multiplier - 1.0) < 0.01) continue; 
                // 같은 위치·같은 값이 두 층에 모두 있으면 한 번만 표시 (2층 항목에서 합쳐서 그림)
                var twin = speedChanges.FirstOrDefault(o => o != ev && o.Layer != ev.Layer && Math.Abs(o.AbsoluteBeat - ev.AbsoluteBeat) < 1e-9 && Math.Abs(o.Multiplier - ev.Multiplier) < 1e-9);
                if (twin != null && ev.Layer == NoteLayer.Bottom) continue;
                string layerTag = twin != null ? "" : $"L{ev.Layer} ";
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    var spList = new List<EditorSpeed> { ev };
                    if (twin != null) spList.Add(twin);
                    bool show = spList.Any(o => o.ShowChange);
                    // SPEED UP / DOWN 표시가 켜진 변속: 글자 뒤에 [SHOW], 더 밝은 분홍
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale,
                        text = $"{layerTag}SPEED: {ev.Multiplier:F2}x" + (show ? "  [SHOW]" : ""), shortText = $"{layerTag}{ev.Multiplier:F2}" + (show ? "*" : ""),
                        color = show ? new Color(1f, 0.35f, 0.75f) : new Color(0.8f, 0.4f, 1f), isWide = false, speeds = spList });
                }
            }
            foreach (var ev in stopEvents) {
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale, text = "STOP", shortText = "STP", color = new Color(1f, 0.2f, 0.2f), isWide = false });
                }
            }
            foreach (var ev in startEvents) {
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale, text = "START", shortText = "STR", color = new Color(0.2f, 0.8f, 0.4f), isWide = false });
                }
            }
            
            foreach (var ev in foldEvents) {
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    string dur = ev.Duration.ToString("0.###", CultureInfo.InvariantCulture);
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale,
                        text = (ev.Fold ? "LANE HIDE " : "LANE SHOW ") + dur + "s", shortText = ev.Fold ? "HID" : "SHW",
                        color = ev.Fold ? new Color(1f, 0.6f, 0.3f) : new Color(0.5f, 1f, 0.6f), isWide = false });
                }
            }

            foreach (var a in deathAreas) {
                double sb = GetBeatFromTime(a.Start), eb = GetBeatFromTime(a.End);
                float ys = hitLineScreenOffsetY + (float)((sb - currentBeat) * beatSpacing);
                float ye = hitLineScreenOffsetY + (float)((eb - currentBeat) * beatSpacing);
                Color dc = new Color(0.85f, 0.1f, 0.1f);
                if (ys > -camHalfHeight - 2f && ys < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, ys, 0));
                    uiEvents.Add(new UIDrawEvent { beat = sb, y = (Screen.height - sp.y) * invScale, text = $"DEATH START {a.Start:F4}s", shortText = "DTH-S", color = dc, isWide = false });
                }
                if (ye > -camHalfHeight - 2f && ye < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, ye, 0));
                    uiEvents.Add(new UIDrawEvent { beat = eb, y = (Screen.height - sp.y) * invScale, text = $"DEATH END {a.End:F4}s", shortText = "DTH-E", color = dc, isWide = false });
                }
            }

            uiEvents = uiEvents.OrderBy(e => e.beat).ToList();
            List<List<UIDrawEvent>> groupedEvents = new List<List<UIDrawEvent>>();
            foreach(var e in uiEvents) {
                if(groupedEvents.Count == 0) { groupedEvents.Add(new List<UIDrawEvent>{e}); } 
                else {
                    var lastGroup = groupedEvents.Last();
                    if(Math.Abs(lastGroup[0].beat - e.beat) < 0.001) lastGroup.Add(e);
                    else groupedEvents.Add(new List<UIDrawEvent>{e});
                }
            }

            foreach(var group in groupedEvents)
            {
                float avgY = group.Average(e => e.y); 
                bool isWide = group.Any(e => e.isWide); 

                float startXWorld = isWide ? -screenW/2f + 0.5f : leftEdgeX;
                float endXWorld = isWide ? screenW/2f - 0.5f : rightEdgeX;

                Vector3 p1 = mainCam.WorldToScreenPoint(new Vector3(startXWorld, 0, 0));
                Vector3 p2 = mainCam.WorldToScreenPoint(new Vector3(endXWorld, 0, 0));

                float drawStartX = p1.x * invScale; 
                float drawWidth = (p2.x - p1.x) * invScale; 
                float centerX = drawStartX + drawWidth / 2f;

                if (group.Count == 1)
                {
                    var ev = group[0];
                    DrawRoundedLine(drawStartX, avgY, drawWidth, 6f, ev.color);
                    Rect pr = DrawPill(centerX, avgY, ev.text, ev.color, customGuiFont);
                    if (ev.speeds != null && Event.current.type == EventType.Repaint) speedPillHits.Add(new KeyValuePair<Rect, List<EditorSpeed>>(pr, ev.speeds));
                }
                else
                {
                    Color overlapCol = new Color(0.1f, 0.6f, 0.9f); 
                    DrawRoundedLine(drawStartX, avgY, drawWidth, 8f, overlapCol);
                    DrawPill(centerX, avgY, "Overlapping", overlapCol, customGuiFont);

                    if (isWide)
                    {
                        float subY = avgY + 35f;
                        foreach(var ev in group) {
                            Rect pr = DrawPill(centerX, subY, ev.text, ev.color, customGuiFont);
                            if (ev.speeds != null && Event.current.type == EventType.Repaint) speedPillHits.Add(new KeyValuePair<Rect, List<EditorSpeed>>(pr, ev.speeds));
                            subY += 30f;
                        }
                    }
                    else
                    {
                        float subY = avgY - ((group.Count - 1) * 22f) / 2f;
                        float subX = (p2.x * invScale) + 40f; 
                        foreach(var ev in group) {
                            Rect pr = DrawPill(subX, subY, ev.shortText, ev.color, customGuiFont, 12, 20f);
                            if (ev.speeds != null && Event.current.type == EventType.Repaint) speedPillHits.Add(new KeyValuePair<Rect, List<EditorSpeed>>(pr, ev.speeds));
                            subY += 22f;
                        }
                    }
                }
            }

        }
    }
    
    private void OnDestroy()
    {
        if (mainCam != null)
        {
            mainCam.transform.SetParent(originalCamParent);
            mainCam.transform.localPosition = originalCamPos;
            mainCam.transform.localRotation = originalCamRot;
            mainCam.orthographic = originalOrthographic;
            mainCam.orthographicSize = originalOrthographicSize;
            mainCam.backgroundColor = originalBackgroundColor;
            mainCam.clearFlags = originalClearFlags;
            mainCam.rect = new Rect(0, 0, 1, 1);
        }
        
        if (bgmSource != null) bgmSource.Stop();
        if (sfxSource != null) sfxSource.Stop();
    }
}