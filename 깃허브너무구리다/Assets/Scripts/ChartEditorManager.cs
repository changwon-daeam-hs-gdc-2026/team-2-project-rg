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
    
    public bool IsFreeKey = false;
    public double StartX = 0; 
    public double TargetX = 0;
    public double Rotation = 0;
    public string Direction = "Top";
    public int LineNumber = 1;

    public bool IsRedNote = false;
    public int Width = 1;
}

public class EditorFreeKeyZone { public double StartBeat; public double EndBeat = double.MaxValue; }
public class EditorBpm { public long Measure, Num, Den; public double Bpm; public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0; }
public class EditorSpeed { public long Measure, Num, Den; public double Multiplier; public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0; }
public class EditorStopEvent { public long Measure, Num, Den; public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0; }
public class EditorStartEvent { public long Measure, Num, Den; public double AbsoluteBeat => (Measure - 1) * 4.0 + ((double)Num / Den) * 4.0; }

public class EditorLinearEvent { 
    public double AbsoluteBeat;
    public int LineId;
    public string Action; 
    public double Value1, Value2, Duration;
}

public class LongNoteBodyLink { public EditorNote Head; public EditorNote Tail; public GameObject BodyObj; public SpriteRenderer Sr; }

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

    [Header("프리키 에셋 연결")]
    public Sprite freeKeyNormal; public Sprite freeKeySpace; public Sprite freeKeyDrag;   

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
    private List<EditorFreeKeyZone> freeKeyZones = new List<EditorFreeKeyZone>();
    private List<EditorLinearEvent> linearEvents = new List<EditorLinearEvent>();
    private List<string> originalOtherEvents = new List<string>();
    
    private double songStartOffset = 0.0;
    private double chartOffset = 0.0; 
    private Camera mainCam;
    private double currentBeat = 0;
    private bool isPlaying = false;

    private string currentSongName = "";

    private AudioSource bgmSource;
    private AudioSource sfxSource;
    private AudioClip hitSoundClip;

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
    private int linearInputType = 0; 
    private bool isAlternateAction = false;
    private double targetBeatForLinear = 0;
    
    private bool isInputtingFreeKeyNote = false;
    private int targetFreeKeyType = 0;

    private bool isRedNoteMode = false;
    private bool isDraggingRedNote = false;
    private EditorNote draggedRedNote = null;

    private string inputBuffer = "";
    private long targetMeasure = 1, targetNum = 0, targetDen = 4;

    private EditorNote selectedFreeKeyNote = null;

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
            Sprite img = Resources.Load<Sprite>($"Sprites/squareimg/{songId}");
            if (img != null) { songImg.sprite = img; songImg.color = Color.white; }
            else { songImg.sprite = null; songImg.color = new Color(0.1f, 0.1f, 0.1f, 1f); }
        }

        if (bgImageUI != null)
        {
            Sprite bgSprite = Resources.Load<Sprite>($"Sprites/blurimg/{songId}");
            if (bgSprite != null) { bgImageUI.sprite = bgSprite; bgImageUI.color = Color.white; }
            else { bgImageUI.sprite = null; bgImageUI.color = new Color(0.1f, 0.1f, 0.1f, 1f); }
        }

        LoadChartForEditor(editSongId);
        LoadAudio(editSongId);
        currentState = EditorState.Editing;
    }

    private void LoadAudio(int songId)
    {
        AudioClip clip = Resources.Load<AudioClip>($"Audio/Full/{songId}");
        if (clip != null) bgmSource.clip = clip;
    }

    public bool IsFreeKeyZone(double beat)
    {
        foreach (var z in freeKeyZones) {
            if (beat >= z.StartBeat && beat <= z.EndBeat) return true;
        }
        return false;
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

        if (isInputtingBpm || isInputtingSpeed)
        {
            HandleSettingInput();
            return;
        }

        bool isGUIOpen = linearInputType > 0 || isInputtingFreeKeyNote;
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

        double prevPlaybackTime = currentPlaybackTime;

        HandlePlaybackAndScroll();

        if (isPlaying)
        {
            double prevBeat = GetBeatFromTime(prevPlaybackTime + hitSoundLatencyCorrection);
            double curBeat = GetBeatFromTime(currentPlaybackTime + hitSoundLatencyCorrection);

            if (curBeat > prevBeat)
            {
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

                    if ((n.Type == 2 || n.Type == 12) && !n.IsFreeKey)
                    {
                        var tail = editorNotes.FirstOrDefault(t => t.Lane == n.Lane && (t.Type == 3 || t.Type == 13) && t.AbsoluteBeat > n.AbsoluteBeat);
                        if (tail != null) {
                            activeLNs.Add(new EditorActiveLN {
                                tail = tail,
                                nextTickTime = GetTimeFromBeat(n.AbsoluteBeat) + 0.05
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

                var currentBpm = bpmChanges.LastOrDefault(b => b.AbsoluteBeat <= GetBeatFromTime(ln.nextTickTime))?.Bpm ?? 120.0;
                double ticksPerBeat = (currentBpm < 300) ? 4.0 : 2.0; 
                double tickInterval = (60.0 / currentBpm) / ticksPerBeat;

                while (curTimeForTick >= ln.nextTickTime && ln.nextTickTime < tailTime - 0.05)
                {
                    IncreaseCombo();
                    ln.nextTickTime += tickInterval;
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
            playStartTime = Time.realtimeSinceStartupAsDouble - exactTime;
            currentPlaybackTime = exactTime;
            isMusicPlaying = false;
        }
        else
        {
            if (bgmSource != null && bgmSource.isPlaying) bgmSource.Stop();
            SnapCursorToBeat(currentBeat);
        }
    }

    private void HandlePlaybackAndScroll()
    {
        var kb = Keyboard.current;
        bool isGUIOpen = linearInputType > 0 || isInputtingFreeKeyNote || isInputtingBpm || isInputtingSpeed;
        
        if (kb != null && kb[Key.Space].wasPressedThisFrame && !isGUIOpen) TogglePlayButton(); 

        if (isPlaying)
        {
            currentPlaybackTime += Time.unscaledDeltaTime;
            double audioStartTime = chartOffset; 
            
            if (bgmSource != null && bgmSource.clip != null)
            {
                if (currentPlaybackTime >= audioStartTime && !isMusicPlaying)
                {
                    float targetTime = (float)(currentPlaybackTime - audioStartTime);
                    if (targetTime >= 0 && targetTime < bgmSource.clip.length) { 
                        bgmSource.time = targetTime; bgmSource.Play(); 
                    }
                    isMusicPlaying = true;
                }
                
                if (isMusicPlaying && bgmSource.isPlaying)
                {
                    double expectedTime = audioStartTime + bgmSource.time;
                    if (Math.Abs(currentPlaybackTime - expectedTime) > 0.05) currentPlaybackTime = expectedTime;
                }
                else if (isMusicPlaying && !bgmSource.isPlaying && currentPlaybackTime > audioStartTime + bgmSource.clip.length - 0.1f)
                {
                    TogglePlayButton();
                }
            }
            currentBeat = GetBeatFromTime(currentPlaybackTime);
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
            if (double.TryParse(inputBuffer, out double val))
            {
                if (isInputtingBpm && val >= 0)
                {
                    bpmChanges.RemoveAll(b => b.Measure == targetMeasure && b.Num == targetNum && b.Den == targetDen);
                    bpmChanges.Add(new EditorBpm { Measure = targetMeasure, Num = targetNum, Den = targetDen, Bpm = val });
                    bpmChanges = bpmChanges.OrderBy(b => b.AbsoluteBeat).ToList();
                }
                else if (isInputtingSpeed)
                {
                    speedChanges.RemoveAll(s => s.Measure == targetMeasure && s.Num == targetNum && s.Den == targetDen);
                    speedChanges.Add(new EditorSpeed { Measure = targetMeasure, Num = targetNum, Den = targetDen, Multiplier = val });
                    speedChanges = speedChanges.OrderBy(s => s.AbsoluteBeat).ToList();
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
        }

        bool minusPressed = kb[Key.Minus].wasPressedThisFrame || kb[Key.NumpadMinus].wasPressedThisFrame;
        bool plusPressed = kb[Key.Equals].wasPressedThisFrame || kb[Key.NumpadPlus].wasPressedThisFrame;
        bool minusHeld = kb[Key.Minus].isPressed || kb[Key.NumpadMinus].isPressed;
        bool plusHeld = kb[Key.Equals].isPressed || kb[Key.NumpadPlus].isPressed;

        double baseAmount = 0.01; 

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

        if (selectedFreeKeyNote != null)
        {
            if (kb[Key.LeftArrow].wasPressedThisFrame) { selectedFreeKeyNote.StartX -= 1; selectedFreeKeyNote.TargetX -= 1; }
            if (kb[Key.RightArrow].wasPressedThisFrame) { selectedFreeKeyNote.StartX += 1; selectedFreeKeyNote.TargetX += 1; }
            
            if (kb[Key.UpArrow].wasPressedThisFrame) selectedFreeKeyNote.TargetX -= 1;
            if (kb[Key.DownArrow].wasPressedThisFrame) selectedFreeKeyNote.TargetX += 1;
            
            if (kb[Key.Escape].wasPressedThisFrame) selectedFreeKeyNote = null;
        }
        else
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
        Vector3 worldPos = mainCam.ScreenToWorldPoint(mousePos);
        
        float totalWidth = currentKeyCount * laneWidth;
        float leftEdge = -totalWidth / 2f;
        
        long measure = (long)Math.Floor(hoverSnapBeat / 4.0) + 1;
        double fraction = (hoverSnapBeat / 4.0) - Math.Floor(hoverSnapBeat / 4.0);
        long num = (long)Math.Round(fraction * currentSnap);
        long den = currentSnap;

        if (num >= den) { measure++; num = 0; }
        long gcd = GCD(num, den); num /= gcd; den /= gcd;

        if (isLeftDown || isRightDown)
        {
            if (kb != null)
            {
                bool shift = kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed;

                if (kb[Key.Z].isPressed) {
                    if (isRightDown) linearEvents.RemoveAll(e => (e.Action == "add" || e.Action == "del") && Math.Abs(e.AbsoluteBeat - hoverSnapBeat) < 0.02);
                    else { linearInputType = 1; isAlternateAction = shift; targetBeatForLinear = hoverSnapBeat; inputBuffer = ""; }
                    return;
                }
                if (kb[Key.X].isPressed) {
                    if (isRightDown) linearEvents.RemoveAll(e => (e.Action == "exposure" || e.Action == "disappearance") && Math.Abs(e.AbsoluteBeat - hoverSnapBeat) < 0.02);
                    else { linearInputType = 2; isAlternateAction = shift; targetBeatForLinear = hoverSnapBeat; inputBuffer = ""; }
                    return;
                }
                if (kb[Key.V].isPressed) {
                    if (isRightDown) linearEvents.RemoveAll(e => e.Action == "move" && Math.Abs(e.AbsoluteBeat - hoverSnapBeat) < 0.02);
                    else { linearInputType = 3; isAlternateAction = false; targetBeatForLinear = hoverSnapBeat; inputBuffer = ""; }
                    return;
                }
                if (kb[Key.B].isPressed) {
                    if (isRightDown) linearEvents.RemoveAll(e => e.Action == "rotation" && Math.Abs(e.AbsoluteBeat - hoverSnapBeat) < 0.02);
                    else { linearInputType = 4; isAlternateAction = false; targetBeatForLinear = hoverSnapBeat; inputBuffer = ""; }
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
                    if (isRightDown) speedChanges.RemoveAll(ev => ev.Measure == measure && ev.Num == num && ev.Den == den);
                    else { 
                        isInputtingSpeed = true; 
                        inputBuffer = ""; 
                        targetMeasure = measure; targetNum = num; targetDen = den; 
                        OpenSettingUI("SCROLL", "변화시킬 SCROLL SPEED 값을 수치로 적어주세요.");
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
                else if (kb[Key.C].isPressed) {
                    if (!isRightDown) {
                        if (!IsFreeKeyZone(hoverSnapBeat)) {
                            freeKeyZones.Add(new EditorFreeKeyZone { StartBeat = hoverSnapBeat });
                            freeKeyZones = freeKeyZones.OrderBy(z => z.StartBeat).ToList();
                        }
                    } else {
                        var zone = freeKeyZones.LastOrDefault(z => z.StartBeat <= hoverSnapBeat && hoverSnapBeat <= z.EndBeat);
                        if (zone != null) {
                            if (Math.Abs(zone.StartBeat - hoverSnapBeat) < 0.01) freeKeyZones.Remove(zone); 
                            else zone.EndBeat = hoverSnapBeat; 
                        }
                    }
                    return;
                }
            }

            if (IsFreeKeyZone(hoverSnapBeat))
            {
                float screenW = mainCam.orthographicSize * 2f * mainCam.aspect;
                float curXNormalized = ((worldPos.x + screenW/2f) / screenW) * 200f - 100f; 
                int gridX = Mathf.Clamp(Mathf.RoundToInt(curXNormalized), -100, 100);

                if (isRightDown) {
                    var target = editorNotes.FirstOrDefault(n => n.IsFreeKey && Math.Abs(n.AbsoluteBeat - hoverSnapBeat) < 0.02f && Math.Abs(n.StartX - gridX) < 5f);
                    if (target != null) { Destroy(target.VisualObject); editorNotes.Remove(target); }
                    selectedFreeKeyNote = null; 
                } else {
                    var target = editorNotes.FirstOrDefault(n => n.IsFreeKey && Math.Abs(n.AbsoluteBeat - hoverSnapBeat) < 0.02f && Math.Abs(n.StartX - gridX) < 5f);
                    if (target != null) { selectedFreeKeyNote = target; }
                    else { 
                        selectedFreeKeyNote = null; 
                        isInputtingFreeKeyNote = true;
                        targetMeasure = measure; targetNum = num; targetDen = den;
                        targetFreeKeyType = currentNoteType - 1;
                        inputBuffer = $"0, Top, 1, {gridX}, {gridX}";
                    } 
                }
            }
        }

        if (!IsFreeKeyZone(hoverSnapBeat))
        {
            int hoverLane = Mathf.FloorToInt((worldPos.x - leftEdge) / laneWidth);

            if (isRightDown && hoverLane >= 0 && hoverLane < currentKeyCount) 
            {
                var target = editorNotes.FirstOrDefault(n => n.Lane <= hoverLane && (n.Lane + (n.IsRedNote ? n.Width : 1) - 1) >= hoverLane && Math.Abs(n.AbsoluteBeat - hoverSnapBeat) < 0.02f);
                if (target != null) { Destroy(target.VisualObject); editorNotes.Remove(target); RefreshLongNoteBodies(); }
            } 
            else if (isLeftDown && hoverLane >= 0 && hoverLane < currentKeyCount) 
            {
                var exist = editorNotes.FirstOrDefault(n => n.Lane <= hoverLane && (n.Lane + (n.IsRedNote ? n.Width : 1) - 1) >= hoverLane && Math.Abs(n.AbsoluteBeat - hoverSnapBeat) < 0.02f);
                if (exist != null) { Destroy(exist.VisualObject); editorNotes.Remove(exist); }
                
                if (isRedNoteMode) {
                    int rType = currentNoteType + 10; 
                    draggedRedNote = AddNoteToEditor(measure, num, den, hoverLane, rType, true, 1);
                    isDraggingRedNote = true;
                } else {
                    AddNoteToEditor(measure, num, den, hoverLane, currentNoteType);
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

    private void AddFreeKeyNoteToEditor(long measure, long num, long den, double startX, double targetX, int type, double rot = 0, string dir = "Top", int lineNum = 1)
    {
        EditorNote newNote = new EditorNote { 
            Measure = measure, Num = num, Den = den, IsFreeKey = true, 
            StartX = startX, TargetX = targetX, Type = type,
            Rotation = rot, Direction = dir, LineNumber = lineNum 
        };
        GameObject obj = new GameObject($"FKNote_{measure}_{num}_{startX}");
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        
        EnsureGraphicsResources();
        if (type == 0 && freeKeyNormal != null) sr.sprite = freeKeyNormal;
        else if (type == 1 && freeKeySpace != null) sr.sprite = freeKeySpace;
        else if (type == 2 && freeKeyDrag != null) sr.sprite = freeKeyDrag;

        if (sr.sprite == null) {
            sr.sprite = fallbackSprite;
            Shader defaultShader = Shader.Find("Sprites/Default");
            if (defaultShader != null) sr.material = new Material(defaultShader);
            sr.color = Color.yellow; 
            obj.transform.localScale = new Vector3(laneWidth * 0.8f, 0.3f, 1f);
        } else {
            obj.transform.localScale = new Vector3(noteScale, noteScale, 1f);
        }

        sr.sortingOrder = 5;
        obj.transform.SetParent(this.transform);
        newNote.VisualObject = obj; editorNotes.Add(newNote); selectedFreeKeyNote = newNote;
    }

    private EditorNote AddNoteToEditor(long measure, long num, long den, int lane, int type, bool isRed = false, int width = 1)
    {
        EditorNote newNote = new EditorNote { Measure = measure, Num = num, Den = den, Lane = lane, Type = type, IsRedNote = isRed, Width = width };
        GameObject obj = new GameObject($"Note_{measure}_{num}_{lane}");
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        
        EnsureGraphicsResources();
        newNote.VisualObject = obj; 
        
        UpdateEditorNoteSprite(newNote);
        
        sr.sortingOrder = 5;
        obj.transform.SetParent(this.transform);
        editorNotes.Add(newNote);
        return newNote;
    }

    private void UpdateEditorNoteSprite(EditorNote note)
    {
        if (note.VisualObject == null) return;
        SpriteRenderer sr = note.VisualObject.GetComponent<SpriteRenderer>();
        if (sr == null) return;

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
            if (note.Type == 1) sr.sprite = isBlue ? blueShort : greenShort;
            else if (note.Type == 2) sr.sprite = isBlue ? blueLongHead : greenLongHead;
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
    }

    private void UpdateNoteVisuals()
    {
        float totalWidth = currentKeyCount * laneWidth;
        float leftEdge = -totalWidth / 2f;
        float screenW = mainCam != null ? mainCam.orthographicSize * 2f * mainCam.aspect : 10f;
        
        bool isVisible = !isInputtingBpm && !isInputtingSpeed;

        foreach (var note in editorNotes)
        {
            if (note.VisualObject != null)
            {
                float noteY = hitLineScreenOffsetY + (float)((note.AbsoluteBeat - currentBeat) * beatSpacing);
                
                if (note.IsFreeKey) {
                    float objX = (float)((note.StartX + 100f) / 200f * screenW) - (screenW/2f);
                    note.VisualObject.transform.position = new Vector3(objX, noteY, -1f);
                    if (note == selectedFreeKeyNote) note.VisualObject.transform.localScale = new Vector3(noteScale * 1.5f, noteScale * 1.5f, 1f);
                    else note.VisualObject.transform.localScale = new Vector3(noteScale, noteScale, 1f);
                } else if (note.IsRedNote) {
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
                float spriteHeight = link.Sr.sprite != null ? link.Sr.sprite.bounds.size.y : 1f;

                if (link.Head.IsRedNote) {
                    float startX = leftEdge + (link.Head.Lane * laneWidth) + (laneWidth / 2f);
                    float endX = leftEdge + (Mathf.Min(link.Head.Lane + link.Head.Width - 1, currentKeyCount - 1) * laneWidth) + (laneWidth / 2f);
                    float centerX = (startX + endX) / 2f;
                    link.BodyObj.transform.position = new Vector3(centerX, midY, -0.5f);
                    
                    if (spriteHeight > 0) {
                        float targetW = (link.Head.Width - 1) * laneWidth + (laneWidth * 0.9f);
                        float bodyScaleX = targetW / link.Sr.sprite.bounds.size.x;
                        link.BodyObj.transform.localScale = new Vector3(bodyScaleX, distance / spriteHeight, 1f);
                    }
                } else {
                    float laneX = leftEdge + (link.Head.Lane * laneWidth) + (laneWidth / 2f);
                    link.BodyObj.transform.position = new Vector3(laneX, midY, -0.5f);
                    if (spriteHeight > 0) link.BodyObj.transform.localScale = new Vector3(noteScale, distance / spriteHeight, 1f);
                }

                link.BodyObj.SetActive(isVisible && tailY > -1000f && headY < 1000f);
            }
        }
    }

    private void CreateBodyObject(EditorNote head, EditorNote tail)
    {
        GameObject obj = new GameObject($"LongBody_{head.Lane}");
        obj.transform.SetParent(this.transform);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        EnsureGraphicsResources();

        if (head.IsRedNote) {
            string targetSpriteName = $"{head.Width}x_mid";
            if (redNoteSprites.TryGetValue(targetSpriteName, out Sprite midSpr)) {
                sr.sprite = midSpr;
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
        
        sr.sortingOrder = 4;
        longNoteBodies.Add(new LongNoteBodyLink { Head = head, Tail = tail, BodyObj = obj, Sr = sr });
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

        for (int lane = 0; lane < currentKeyCount; lane++) 
        {
            var regularNotes = editorNotes.Where(n => !n.IsFreeKey && !n.IsRedNote && n.Lane == lane).OrderBy(n => n.AbsoluteBeat).ToList();
            EditorNote head = null;
            
            foreach (var n in regularNotes)
            {
                if (n.Type == 2) head = n; 
                else if (n.Type == 3 && head != null) 
                {
                    CreateBodyObject(head, n);
                    head = null;
                }
                else if (n.Type == 1 || n.Type == 4) head = null;
            }

            var redNotes = editorNotes.Where(n => !n.IsFreeKey && n.IsRedNote && n.Lane == lane).OrderBy(n => n.AbsoluteBeat).ToList();
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

    private void UpdateGridVisuals()
    {
        if (mainCam == null || currentState != EditorState.Editing) return;
        
        bool isVisible = !isInputtingBpm && !isInputtingSpeed;
        if (!isVisible) return; 

        EnsureGraphicsResources();

        float screenW = mainCam.orthographicSize * 2f * mainCam.aspect;
        float totalWidth = currentKeyCount * laneWidth;
        float leftEdge = -totalWidth / 2f;
        float gridZ = 1f; 
        float thickness = 0.02f; 

        List<Vector2> fkYList = new List<Vector2>();
        foreach (var z in freeKeyZones) {
            float y1 = hitLineScreenOffsetY + (float)((z.StartBeat - currentBeat) * beatSpacing);
            float y2 = z.EndBeat >= 1000000 ? 1000f : hitLineScreenOffsetY + (float)((z.EndBeat - currentBeat) * beatSpacing);
            if (y1 > 1000f || y2 < -1000f) continue;
            fkYList.Add(new Vector2(Mathf.Max(-1000f, y1), Mathf.Min(1000f, y2)));
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

        int minMeasure = Mathf.Max(1, (int)Math.Floor(currentBeat / 4.0) - 2);
        int maxMeasure = minMeasure + 12; 
        
        for (int m = minMeasure; m <= maxMeasure; m++) {
            for (int b = 0; b < currentSnap; b++) {
                if (b == 0) continue; 
                double beat = (m - 1) * 4.0 + ((double)b / currentSnap) * 4.0;
                float y = hitLineScreenOffsetY + (float)((beat - currentBeat) * beatSpacing);
                
                if (y > -1000f && y < 1000f) {
                    Color c = (currentSnap >= 4 && b % (currentSnap / 4) == 0) ? new Color(0.6f, 0.6f, 0.6f, 0.8f) : new Color(0.3f, 0.3f, 0.3f, 0.4f);
                    if (IsFreeKeyZone(beat)) DrawLine(new Vector3(0, y, gridZ), new Vector3(screenW, thickness, 1f), c);
                    else DrawLine(new Vector3(leftEdge + totalWidth/2f, y, gridZ), new Vector3(totalWidth, thickness, 1f), c);
                }
            }
        }

        Color hitLineColor = isPlaying ? new Color(0f, 1f, 0f, 0.4f) : new Color(1f, 0f, 0f, 0.4f);
        float hHeight = 0.08f; 
        float width = IsFreeKeyZone(currentBeat) ? screenW : totalWidth + 0.4f;
        float cx = IsFreeKeyZone(currentBeat) ? 0 : leftEdge + totalWidth/2f;
        
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

    public void AdjustOffset(double amount)
    {
        songStartOffset += amount;
        songStartOffset = Math.Round(songStartOffset, 3);
        chartOffset = songStartOffset; 
        
        if (isPlaying && isMusicPlaying && bgmSource != null)
        {
            float targetTime = (float)(currentPlaybackTime - chartOffset);
            if (targetTime >= 0 && targetTime < bgmSource.clip.length) {
                bgmSource.time = targetTime;
            } else {
                bgmSource.Stop();
                isMusicPlaying = false;
            }
        }
        else if (!isPlaying)
        {
            currentPlaybackTime = GetTimeFromBeat(currentBeat);
        }
        UpdateUITexts();
    }

    long GCD(long a, long b) { return b == 0 ? (a == 0 ? 1 : a) : GCD(b, a % b); }

    private double GetTimeFromBeat(double targetBeat) 
    {
        var sorted = bpmChanges.OrderBy(b => b.AbsoluteBeat).ToList();
        double time = 0; 
        if (targetBeat <= 0) { double initBpm = sorted.Count > 0 ? sorted[0].Bpm : 120.0; return time + targetBeat * (60.0 / initBpm); }
        double curB = 0, curBpm = sorted.Count > 0 ? sorted[0].Bpm : 120.0;
        foreach (var b in sorted) {
            if (b.AbsoluteBeat > targetBeat) break;
            time += (b.AbsoluteBeat - curB) * (60.0 / curBpm);
            curB = b.AbsoluteBeat; curBpm = b.Bpm;
        }
        time += (targetBeat - curB) * (60.0 / curBpm);
        return time;
    }

    private double GetBeatFromTime(double time) 
    {
        var sorted = bpmChanges.OrderBy(b => b.AbsoluteBeat).ToList();
        if (sorted.Count == 0) return 0;
        if (time < 0) return time / (60.0 / sorted[0].Bpm);
        double t = 0, curB = 0, curBpm = sorted[0].Bpm;
        foreach (var b in sorted) {
            double nextTime = t + (b.AbsoluteBeat - curB) * (60.0 / curBpm);
            if (nextTime > time) break;
            t = nextTime; curB = b.AbsoluteBeat; curBpm = b.Bpm;
        }
        return curB + (time - t) / (60.0 / curBpm);
    }

    private void LoadChartForEditor(int songId)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "chart", $"{songId}_{editLevelId}.txt");
        
        foreach (var n in editorNotes) if (n.VisualObject) Destroy(n.VisualObject); editorNotes.Clear();
        foreach (var b in longNoteBodies) if (b.BodyObj) Destroy(b.BodyObj); longNoteBodies.Clear();
        
        bpmChanges.Clear(); speedChanges.Clear(); stopEvents.Clear(); startEvents.Clear();
        freeKeyZones.Clear(); linearEvents.Clear(); originalOtherEvents.Clear();
        
        songStartOffset = 0.0; chartOffset = songStartOffset; currentKeyCount = 4;
        double initialBpm = 120.0; double initialSpeed = 1.0;
        
        currentBeat = 0; currentPlaybackTime = 0;
        activeLNs.Clear();
        editorCombo = 0; comboAnimTimer = 0f;
        isPlaying = false; isMusicPlaying = false;
        isInputtingBpm = false; isInputtingSpeed = false; 
        linearInputType = 0; isInputtingFreeKeyNote = false;
        isRedNoteMode = false; isDraggingRedNote = false; draggedRedNote = null;
        selectedFreeKeyNote = null;
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
            else if (type == "OFFSET" || type == "SONGSTARTOFFSET") { songStartOffset = double.Parse(data[1].Trim(), CultureInfo.InvariantCulture); chartOffset = songStartOffset; }
            else if (cleanLine.StartsWith("KeyMode:")) { currentKeyCount = int.Parse(cleanLine.Split(':')[1].Trim()); }
            else if (type == "SPEED" && data.Length == 2) { initialSpeed = double.Parse(data[1].Trim(), CultureInfo.InvariantCulture); }
            else if (cleanLine.StartsWith("NoteSpeed:", StringComparison.OrdinalIgnoreCase)) { initialSpeed = double.Parse(cleanLine.Split(':')[1].Trim(), CultureInfo.InvariantCulture); }
            else if (type == "BPM" && data.Length >= 5) { bpmChanges.Add(new EditorBpm { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]), Bpm = double.Parse(data[4], CultureInfo.InvariantCulture) }); }
            else if (type == "SPEED" && data.Length >= 5) { speedChanges.Add(new EditorSpeed { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]), Multiplier = double.Parse(data[4], CultureInfo.InvariantCulture) }); }
        }

        if (!bpmChanges.Any(b => b.AbsoluteBeat == 0)) {
            bpmChanges.Add(new EditorBpm { Measure = 1, Num = 0, Den = 4, Bpm = initialBpm });
        }
        bpmChanges = bpmChanges.OrderBy(b => b.AbsoluteBeat).ToList();
        
        if (!speedChanges.Any(s => s.AbsoluteBeat == 0)) {
            speedChanges.Add(new EditorSpeed { Measure = 1, Num = 0, Den = 4, Multiplier = initialSpeed });
        }
        speedChanges = speedChanges.OrderBy(s => s.AbsoluteBeat).ToList();

        // --- 2nd Pass: 실제 노트 및 이벤트 로드 ---
        foreach (string line in allLines)
        {
            string cleanLine = line.Contains("#") ? line.Substring(0, line.IndexOf('#')).Trim() : line.Trim();
            if (string.IsNullOrWhiteSpace(cleanLine)) continue;

            string[] data = cleanLine.Split(',');
            string type = data[0].Trim().ToUpper();

            if (type == "BPM" || type == "OFFSET" || type == "SONGSTARTOFFSET" || type == "SPEED" || cleanLine.StartsWith("KeyMode:") || cleanLine.StartsWith("NoteSpeed:", StringComparison.OrdinalIgnoreCase)) {
                continue; // 1st Pass에서 이미 처리함
            }
            
            if (type == "STOP" && data.Length >= 4) { stopEvents.Add(new EditorStopEvent { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]) }); }
            else if (type == "START" && data.Length >= 4) { startEvents.Add(new EditorStartEvent { Measure = long.Parse(data[1]), Num = long.Parse(data[2]), Den = long.Parse(data[3]) }); }
            else if (type == "FREEKEY" && data.Length >= 4 && data[1].Trim() == "Linear") { 
                string actionOrId = data[2].Trim();
                if (actionOrId == "add" || actionOrId == "del") {
                    double t = data.Length >= 5 ? double.Parse(data[4].Trim(), CultureInfo.InvariantCulture) : 0;
                    linearEvents.Add(new EditorLinearEvent { Action = actionOrId, LineId = int.Parse(data[3].Trim()), AbsoluteBeat = GetBeatFromTime(t) });
                } else {
                    int lineId = int.Parse(actionOrId);
                    string action = data[3].Trim();
                    EditorLinearEvent lin = new EditorLinearEvent { LineId = lineId, Action = action };
                    if (action == "exposure" || action == "disappearance") {
                        double t = double.Parse(data[4].Trim(), CultureInfo.InvariantCulture);
                        lin.Duration = double.Parse(data[5].Trim(), CultureInfo.InvariantCulture);
                        lin.AbsoluteBeat = GetBeatFromTime(t);
                    } else if (action == "rotation") {
                        lin.Value1 = double.Parse(data[4].Trim(), CultureInfo.InvariantCulture);
                        double t = double.Parse(data[5].Trim(), CultureInfo.InvariantCulture);
                        lin.Duration = double.Parse(data[6].Trim(), CultureInfo.InvariantCulture);
                        lin.AbsoluteBeat = GetBeatFromTime(t);
                    } else if (action == "move") {
                        lin.Value1 = double.Parse(data[4].Trim(), CultureInfo.InvariantCulture);
                        lin.Value2 = double.Parse(data[5].Trim(), CultureInfo.InvariantCulture);
                        double t = double.Parse(data[6].Trim(), CultureInfo.InvariantCulture);
                        lin.Duration = double.Parse(data[7].Trim(), CultureInfo.InvariantCulture);
                        lin.AbsoluteBeat = GetBeatFromTime(t);
                    }
                    linearEvents.Add(lin);
                }
            }
            else if (type == "FREEKEY" && data.Length >= 10 && data[1].Trim() != "Linear") {
                int fkType = int.Parse(data[1].Trim());
                double rot = double.Parse(data[2].Trim(), CultureInfo.InvariantCulture);
                string dir = data[3].Trim();
                int lineNum = int.Parse(data[4].Trim());
                double startX = double.Parse(data[5].Trim(), CultureInfo.InvariantCulture);
                double targetX = double.Parse(data[6].Trim(), CultureInfo.InvariantCulture);
                long m = long.Parse(data[7].Trim());
                long num = long.Parse(data[8].Trim());
                long den = long.Parse(data[9].Trim());
                AddFreeKeyNoteToEditor(m, num, den, startX, targetX, fkType, rot, dir, lineNum);
            }
            else if (type == "FREEKEY" && data.Length >= 9 && data[1].Trim() != "Linear") {
                int fkType = int.Parse(data[1].Trim());
                double rot = double.Parse(data[2].Trim(), CultureInfo.InvariantCulture);
                string dir = data[3].Trim();
                int lineNum = 1;
                double startX = double.Parse(data[4].Trim(), CultureInfo.InvariantCulture);
                double targetX = double.Parse(data[5].Trim(), CultureInfo.InvariantCulture);
                long m = long.Parse(data[6].Trim());
                long num = long.Parse(data[7].Trim());
                long den = long.Parse(data[8].Trim());
                AddFreeKeyNoteToEditor(m, num, den, startX, targetX, fkType, rot, dir, lineNum);
            }
            else if (type == "NOTE" && data.Length >= 5) {
                long m = long.Parse(data[1]), num = long.Parse(data[2]), den = long.Parse(data[3]);
                
                if (data.Length == 5 && !data[4].Contains("_") && data[4].Trim().Length >= currentKeyCount) {
                    string val = data[4].Trim();
                    for (int i = 0; i < val.Length && i < 8; i++) {
                        if (val[i] != '0') { int noteType = int.Parse(val[i].ToString()); if (noteType != 5 && noteType != 6) AddNoteToEditor(m, num, den, i, noteType); }
                    }
                } else {
                    for (int i = 0; i < currentKeyCount && (i + 4) < data.Length; i++) {
                        string val = data[i+4].Trim();
                        if (val == "0") continue;
                        if (val.Contains("_")) {
                            string[] parts = val.Split('_');
                            int noteType = int.Parse(parts[0]);
                            int noteWidth = int.Parse(parts[1]);
                            AddNoteToEditor(m, num, den, i, noteType, true, noteWidth);
                        } else {
                            int noteType = int.Parse(val);
                            AddNoteToEditor(m, num, den, i, noteType);
                        }
                    }
                }
            }
            else if (type == "UI" && data.Length >= 4)
            {
                string targetUI = data[1].Trim();
                if (targetUI == "FREEKEYUI") {
                    double t = double.Parse(data[3].Trim(), CultureInfo.InvariantCulture);
                    double b = GetBeatFromTime(t);
                    if (data[2].Trim() == "exposure") freeKeyZones.Add(new EditorFreeKeyZone { StartBeat = b });
                    else if (data[2].Trim() == "disappearance") { var zone = freeKeyZones.LastOrDefault(z => z.EndBeat == double.MaxValue); if (zone != null) zone.EndBeat = b; }
                    continue; 
                }
                if (targetUI == "BasicUI") continue; 
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
        
        var baseSpeedEvent = speedChanges.FirstOrDefault(s => s.AbsoluteBeat == 0);
        double initialSpeedValue = baseSpeedEvent != null ? baseSpeedEvent.Multiplier : 1.0;

        output.Add($"BPM, {initialBpm.ToString("0.00", CultureInfo.InvariantCulture)}");
        output.Add($"OFFSET, {songStartOffset.ToString("0.000", CultureInfo.InvariantCulture)}");
        output.Add($"KeyMode: {currentKeyCount}");
        output.Add($"SPEED, {initialSpeedValue.ToString("0.00", CultureInfo.InvariantCulture)}");

        foreach (var b in bpmChanges.Skip(1)) output.Add($"BPM, {b.Measure}, {b.Num}, {b.Den}, {b.Bpm.ToString(CultureInfo.InvariantCulture)}");
        
        foreach (var s in speedChanges) {
            if (s.AbsoluteBeat == 0) continue;
            output.Add($"SPEED, {s.Measure}, {s.Num}, {s.Den}, {s.Multiplier.ToString("0.00", CultureInfo.InvariantCulture)}");
        }
        
        foreach (var s in stopEvents) output.Add($"STOP, {s.Measure}, {s.Num}, {s.Den}");
        foreach (var s in startEvents) output.Add($"START, {s.Measure}, {s.Num}, {s.Den}");
        
        foreach(var z in freeKeyZones.OrderBy(x => x.StartBeat))
        {
            double st = GetTimeFromBeat(z.StartBeat);
            output.Add($"UI, BasicUI, disappearance, {st.ToString("0.0000", CultureInfo.InvariantCulture)}, 0.5");
            output.Add($"UI, FREEKEYUI, exposure, {st.ToString("0.0000", CultureInfo.InvariantCulture)}, 0.5");
            if (z.EndBeat != double.MaxValue)
            {
                double et = GetTimeFromBeat(z.EndBeat);
                output.Add($"UI, FREEKEYUI, disappearance, {et.ToString("0.0000", CultureInfo.InvariantCulture)}, 0.5");
                output.Add($"UI, BasicUI, exposure, {et.ToString("0.0000", CultureInfo.InvariantCulture)}, 0.5");
            }
        }
        
        foreach (var ev in linearEvents.OrderBy(e => e.AbsoluteBeat)) {
            double st = GetTimeFromBeat(ev.AbsoluteBeat);
            if (ev.Action == "add" || ev.Action == "del") {
                output.Add($"FREEKEY, Linear, {ev.Action}, {ev.LineId}, {st.ToString("0.0000", CultureInfo.InvariantCulture)}");
            } else if (ev.Action == "exposure" || ev.Action == "disappearance") {
                output.Add($"FREEKEY, Linear, {ev.LineId}, {ev.Action}, {st.ToString("0.0000", CultureInfo.InvariantCulture)}, {ev.Duration.ToString("0.0000", CultureInfo.InvariantCulture)}");
            } else if (ev.Action == "rotation") {
                output.Add($"FREEKEY, Linear, {ev.LineId}, {ev.Action}, {ev.Value1.ToString(CultureInfo.InvariantCulture)}, {st.ToString("0.0000", CultureInfo.InvariantCulture)}, {ev.Duration.ToString("0.0000", CultureInfo.InvariantCulture)}");
            } else if (ev.Action == "move") {
                output.Add($"FREEKEY, Linear, {ev.LineId}, {ev.Action}, {ev.Value1.ToString(CultureInfo.InvariantCulture)}, {ev.Value2.ToString(CultureInfo.InvariantCulture)}, {st.ToString("0.0000", CultureInfo.InvariantCulture)}, {ev.Duration.ToString("0.0000", CultureInfo.InvariantCulture)}");
            }
        }

        output.AddRange(originalOtherEvents);

        var groupedNotes = editorNotes.Where(n => !n.IsFreeKey).GroupBy(n => new { n.Measure, n.Num, n.Den }).OrderBy(g => g.Key.Measure).ThenBy(g => (double)g.Key.Num / g.Key.Den);
        foreach (var group in groupedNotes)
        {
            string[] laneStrs = new string[currentKeyCount];
            for (int i = 0; i < currentKeyCount; i++) laneStrs[i] = "0";
            
            foreach (var note in group) {
                if (note.Lane < currentKeyCount) {
                    if (note.IsRedNote) laneStrs[note.Lane] = $"{note.Type}_{note.Width}";
                    else laneStrs[note.Lane] = note.Type.ToString()[0].ToString();
                }
            }
            string saveStr = string.Join(", ", laneStrs); 
            output.Add($"NOTE, {group.Key.Measure}, {group.Key.Num}, {group.Key.Den}, {saveStr}");
        }

        var groupedFreeKeys = editorNotes.Where(n => n.IsFreeKey).OrderBy(n => n.Measure).ThenBy(n => (double)n.Num / n.Den);
        foreach (var fk in groupedFreeKeys)
        {
            output.Add($"FREEKEY, {fk.Type}, {fk.Rotation.ToString(CultureInfo.InvariantCulture)}, {fk.Direction}, {fk.LineNumber}, {fk.StartX.ToString(CultureInfo.InvariantCulture)}, {fk.TargetX.ToString(CultureInfo.InvariantCulture)}, {fk.Measure}, {fk.Num}, {fk.Den}");
        }

        File.WriteAllLines(path, output, System.Text.Encoding.UTF8);
        Debug.Log("채보가 성공적으로 저장되었습니다!");
    }

    private void UpdateUITexts()
    {
        if (selectedSnapText != null) selectedSnapText.text = $"1/{(currentSnap / 4)}";
        
        string baseMode = IsFreeKeyZone(currentBeat) ? "FREE-KEY" : $"{currentKeyCount}B";
        if (modeText != null) modeText.text = baseMode;
        if (modeText != null) modeText.color = isRedNoteMode ? Color.red : Color.white;
        
        if (timeText != null) timeText.text = $"{currentPlaybackTime:F4}s";
        
        if (offsetText != null) offsetText.text = $"{Math.Abs(songStartOffset):F3}s";
        if (offsetSignText != null) offsetSignText.text = songStartOffset >= 0 ? "PLUS(+)" : "MINUS(-)";

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
            centerMessageText.text = selectedFreeKeyNote != null ? $"선택 노트 (시작X: {selectedFreeKeyNote.StartX}, 도착X: {selectedFreeKeyNote.TargetX})" : "";
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

    private void DrawPill(float cx, float cy, string text, Color c, Font font, int fontSize = 16, float h = 28f)
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
    }

    void OnGUI()
    {
        float scale = (Screen.height / 1080f) * 0.85f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float invScale = 1f / scale;

        if (linearInputType > 0 || isInputtingFreeKeyNote)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && inputBuffer.Length > 0) { inputBuffer = inputBuffer.Substring(0, inputBuffer.Length - 1); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) {
                    if (linearInputType > 0) {
                        try {
                            string[] parts = inputBuffer.Split(',');
                            if (linearInputType == 1 && parts.Length >= 1) { 
                                int id = int.Parse(parts[0].Trim());
                                linearEvents.Add(new EditorLinearEvent { Action = isAlternateAction ? "del" : "add", LineId = id, AbsoluteBeat = targetBeatForLinear });
                            }
                            else if (linearInputType == 2 && parts.Length >= 2) { 
                                int id = int.Parse(parts[0].Trim()); double dur = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                                linearEvents.Add(new EditorLinearEvent { Action = isAlternateAction ? "disappearance" : "exposure", LineId = id, Duration = dur, AbsoluteBeat = targetBeatForLinear });
                            }
                            else if (linearInputType == 3 && parts.Length >= 4) { 
                                int id = int.Parse(parts[0].Trim()); double tx = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture); 
                                double ty = double.Parse(parts[2].Trim(), CultureInfo.InvariantCulture); double dur = double.Parse(parts[3].Trim(), CultureInfo.InvariantCulture);
                                linearEvents.Add(new EditorLinearEvent { Action = "move", LineId = id, Value1 = tx, Value2 = ty, Duration = dur, AbsoluteBeat = targetBeatForLinear });
                            }
                            else if (linearInputType == 4 && parts.Length >= 3) { 
                                int id = int.Parse(parts[0].Trim()); double angle = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture); double dur = double.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
                                linearEvents.Add(new EditorLinearEvent { Action = "rotation", LineId = id, Value1 = angle, Duration = dur, AbsoluteBeat = targetBeatForLinear });
                            }
                        } catch { }
                    }
                    else if (isInputtingFreeKeyNote) {
                        try {
                            string[] parts = inputBuffer.Split(',');
                            if (parts.Length >= 5) {
                                double rot = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                                string dir = parts[1].Trim();
                                int line = int.Parse(parts[2].Trim());
                                double start = double.Parse(parts[3].Trim(), CultureInfo.InvariantCulture);
                                double target = double.Parse(parts[4].Trim(), CultureInfo.InvariantCulture);
                                AddFreeKeyNoteToEditor(targetMeasure, targetNum, targetDen, start, target, targetFreeKeyType, rot, dir, line);
                            }
                        } catch { }
                    }
                    linearInputType = 0; isInputtingFreeKeyNote = false; e.Use();
                }
                else if (e.keyCode == KeyCode.Escape) { linearInputType = 0; isInputtingFreeKeyNote = false; e.Use(); }
                else if (e.character != 0) { 
                    if (char.IsLetterOrDigit(e.character) || e.character == '.' || e.character == ',' || e.character == '-' || e.character == ' ') { 
                        inputBuffer += e.character; e.Use(); 
                    } 
                }
            }

            if (Event.current.type == EventType.Repaint)
            {
                float winW = 600f;
                float winH = 200f;
                float winX = (Screen.width * invScale - winW) / 2f;
                float winY = (Screen.height * invScale - winH) / 2f;

                GUI.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
                GUI.DrawTexture(new Rect(winX, winY, winW, winH), Texture2D.whiteTexture);
                
                GUI.color = new Color(0.3f, 0.5f, 0.9f, 1f);
                GUI.DrawTexture(new Rect(winX, winY, winW, 5f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(winX, winY + winH - 5f, winW, 5f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(winX, winY, 5f, winH), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(winX + winW - 5f, winY, 5f, winH), Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUIStyle titleStyle = new GUIStyle();
                if (customGuiFont != null) titleStyle.font = customGuiFont;
                titleStyle.fontSize = 24;
                titleStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);
                titleStyle.alignment = TextAnchor.MiddleCenter;

                GUIStyle guideStyle = new GUIStyle();
                if (customGuiFont != null) guideStyle.font = customGuiFont;
                guideStyle.fontSize = 16;
                guideStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                guideStyle.alignment = TextAnchor.MiddleCenter;
                
                GUIStyle inputStyle = new GUIStyle();
                if (customGuiFont != null) inputStyle.font = customGuiFont;
                inputStyle.fontSize = 20;
                inputStyle.normal.textColor = Color.white;
                inputStyle.alignment = TextAnchor.MiddleCenter;

                string title = isInputtingFreeKeyNote ? "FREE-KEY NOTE INPUT" : "LINEAR EVENT INPUT";
                
                string guide = "";
                if (isInputtingFreeKeyNote) {
                    guide = "형식: 회전각도, 방향, 판정선번호, 시작X, 도착X (예: 0, Top, 1, -50, 50)";
                } else {
                    if (linearInputType == 1) guide = "생성/제거 - 형식: 판정선번호 (예: 1)";
                    else if (linearInputType == 2) guide = "노출/숨김 - 형식: 판정선번호, 시간 (예: 1, 0.5)";
                    else if (linearInputType == 3) guide = "이동 - 형식: 판정선번호, 목표X, 목표Y, 시간 (예: 1, 0, 2.75, 1.0)";
                    else if (linearInputType == 4) guide = "회전 - 형식: 판정선번호, 목표각도, 시간 (예: 1, 90, 0.5)";
                    else guide = "콤마(,)로 구분하여 값을 입력하세요. (취소: ESC, 입력: Enter)";
                }

                GUI.Label(new Rect(winX, winY + 20, winW, 30), title, titleStyle);
                GUI.Label(new Rect(winX, winY + 60, winW, 30), guide, guideStyle);

                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(winX + 30, winY + 110, winW - 60, 50), Texture2D.whiteTexture);
                GUI.color = Color.white;

                float blinkAlpha = Mathf.PingPong(Time.time * 2f, 1f);
                Color cursorColor = new Color(1f, 1f, 1f, blinkAlpha);
                
                GUI.Label(new Rect(winX + 30, winY + 110, winW - 60, 50), inputBuffer, inputStyle);
                
                Vector2 textSize = inputStyle.CalcSize(new GUIContent(inputBuffer));
                float cursorX = winX + 30 + (winW - 60) / 2f + textSize.x / 2f + 2f;
                
                GUI.color = cursorColor;
                GUI.DrawTexture(new Rect(cursorX, winY + 120, 2f, 30f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        bool isVisible = !isInputtingBpm && !isInputtingSpeed;
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
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = beat, y = (Screen.height - sp.y) * invScale, text = $"M: {m}", shortText = $"{m}", color = new Color(1f, 0.2f, 0.2f), isWide = IsFreeKeyZone(beat) });
                }
            }

            foreach (var ev in bpmChanges) {
                if (ev.AbsoluteBeat == 0) continue; 
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale, text = $"BPM: {ev.Bpm:F0}", shortText = $"{ev.Bpm:F0}", color = new Color(1f, 0.7f, 0.2f), isWide = IsFreeKeyZone(ev.AbsoluteBeat) });
                }
            }
            foreach (var ev in speedChanges) {
                if (ev.AbsoluteBeat == 0 && Math.Abs(ev.Multiplier - 1.0) < 0.01) continue; 
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale, text = $"SPEED: {ev.Multiplier:F2}x", shortText = $"{ev.Multiplier:F2}", color = new Color(0.8f, 0.4f, 1f), isWide = IsFreeKeyZone(ev.AbsoluteBeat) });
                }
            }
            foreach (var ev in stopEvents) {
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale, text = "STOP", shortText = "STP", color = new Color(1f, 0.2f, 0.2f), isWide = IsFreeKeyZone(ev.AbsoluteBeat) });
                }
            }
            foreach (var ev in startEvents) {
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale, text = "START", shortText = "STR", color = new Color(0.2f, 0.8f, 0.4f), isWide = IsFreeKeyZone(ev.AbsoluteBeat) });
                }
            }
            
            foreach (var ev in linearEvents) {
                float y = hitLineScreenOffsetY + (float)((ev.AbsoluteBeat - currentBeat) * beatSpacing);
                if (y > -camHalfHeight - 2f && y < camHalfHeight + 2f) {
                    Vector3 sp = mainCam.WorldToScreenPoint(new Vector3(0, y, 0));
                    string txt = ""; string stxt = ""; Color c = Color.white;
                    if (ev.Action == "add") { txt = $"판정선 {ev.LineId}: 생성됨"; stxt = $"L{ev.LineId} Add"; c = new Color(0.2f, 0.8f, 0.2f); }
                    else if (ev.Action == "del") { txt = $"판정선 {ev.LineId}: 제거됨"; stxt = $"L{ev.LineId} Del"; c = new Color(0.2f, 0.8f, 0.2f); }
                    else if (ev.Action == "exposure") { txt = $"판정선 {ev.LineId}: 드러남"; stxt = $"L{ev.LineId} Exp"; c = new Color(0.6f, 0.2f, 0.8f); }
                    else if (ev.Action == "disappearance") { txt = $"판정선 {ev.LineId}: 숨겨짐"; stxt = $"L{ev.LineId} Dis"; c = new Color(0.6f, 0.2f, 0.8f); }
                    else if (ev.Action == "move") { txt = $"판정선 {ev.LineId}: ({ev.Value1},{ev.Value2})로 이동"; stxt = $"L{ev.LineId} Mov"; c = new Color(0.7f, 0.5f, 1f); }
                    else if (ev.Action == "rotation") { txt = $"판정선 {ev.LineId}: {ev.Value1}도로 회전"; stxt = $"L{ev.LineId} Rot"; c = new Color(0.9f, 0.4f, 0.8f); }
                    
                    uiEvents.Add(new UIDrawEvent { beat = ev.AbsoluteBeat, y = (Screen.height - sp.y) * invScale, text = txt, shortText = stxt, color = c, isWide = IsFreeKeyZone(ev.AbsoluteBeat) });
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
                    DrawPill(centerX, avgY, ev.text, ev.color, customGuiFont);
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
                            DrawPill(centerX, subY, ev.text, ev.color, customGuiFont);
                            subY += 30f;
                        }
                    }
                    else
                    {
                        float subY = avgY - ((group.Count - 1) * 22f) / 2f;
                        float subX = (p2.x * invScale) + 40f; 
                        foreach(var ev in group) {
                            DrawPill(subX, subY, ev.shortText, ev.color, customGuiFont, 12, 20f);
                            subY += 22f;
                        }
                    }
                }
            }

            foreach (var note in editorNotes) {
                if (note.IsFreeKey && note.VisualObject != null && note.VisualObject.activeSelf) {
                    Vector3 sp = mainCam.WorldToScreenPoint(note.VisualObject.transform.position);
                    float sx = sp.x * invScale;
                    float sy = (Screen.height - sp.y) * invScale;
                    GUIStyle ns = new GUIStyle();
                    if (customGuiFont != null) ns.font = customGuiFont;
                    ns.fontSize = 13; ns.normal.textColor = Color.white; ns.alignment = TextAnchor.MiddleCenter;
                    
                    string line1 = $"{note.Direction[0]}, R:{note.Rotation} L:{note.LineNumber}";
                    string line2 = $"{note.StartX} to {note.TargetX}";
                    
                    GUI.color = new Color(0f, 0f, 0f, 0.7f);
                    GUI.DrawTexture(new Rect(sx - 55, sy + 15, 110, 36), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(sx - 55, sy + 15, 110, 18), line1, ns);
                    GUI.Label(new Rect(sx - 55, sy + 33, 110, 18), line2, ns);
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