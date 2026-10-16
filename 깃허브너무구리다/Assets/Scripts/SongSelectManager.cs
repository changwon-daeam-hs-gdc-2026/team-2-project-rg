using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// 악곡 선택창 (1_SongSelect)
//  - 좌측: 자켓 / 곡명 / 작곡가 / 곡 길이 / 난이도 이미지 3개(ON·OFF) + 난이도 숫자 3개
//  - 우측: 악곡 정렬표 (뷰포트 밖으로 나간 줄은 RectMask2D 로 잘림)
//  - ← → : 곡 이동 (길게 누르면 연속 이동)   ↑ ↓ : 난이도 변경
//  - Enter : 게임 시작 / G + Enter : 에디터   F : MV   A : 오토플레이   F1/F2 : 배속
// ============================================================
public class SongSelectManager : MonoBehaviour
{
    [Header("배경 (blurimg)")]
    public Image selectScreenImg;
    [Tooltip("곡이 바뀔 때 배경이 위/아래로 밀려나는 시간(초)")]
    public float bgSlideDuration = 0.25f;

    [Header("난이도 스프라이트 시트")]
    [Tooltip("Resources 기준 경로. 안에 EASY_ON, EASY_OFF ... 이름의 스프라이트가 들어 있어야 함")]
    public string difficultySheetPath = "Sprites/etc/Level";

    [Header("좌측 곡 정보")]
    [Tooltip("자켓·곡명·작곡가를 묶은 부모 (선택). 곡이 바뀔 때 살짝 슬라이드+페이드 인")]
    public CanvasGroup leftInfoGroup;
    public Image selectedJacketImg;                // Sprites/squareimg/{id}
    public TextMeshProUGUI selectedSongTitleText;
    public TextMeshProUGUI selectedComposerText;
    public TextMeshProUGUI songLengthText;         // 예: 3:39
    public float leftInfoSlideDistance = 24f;

    [Header("자켓 하단 블러 (곡 정보 띠 뒤)")]
    [Tooltip("곡 정보 띠 영역(RectMask2D) 안에 둔 RawImage. 흐리게 만든 자켓이 그려진다")]
    public RawImage jacketBlurImage;
    [Tooltip("블러 해상도(가로 px). 작을수록 더 흐림. 32~96 권장")]
    [Range(16, 256)] public int jacketBlurResolution = 48;
    [Tooltip("추가로 부드럽게 만드는 횟수. 클수록 더 뭉개짐")]
    [Range(0, 6)] public int jacketBlurIterations = 3;
    public float leftInfoAnimDuration = 0.2f;

    [Header("좌측 난이도 (선택된 난이도 하나만 표시)")]
    public Image selectedDiffImg;                  // 선택된 난이도의 _ON 이미지
    public TextMeshProUGUI selectedDiffValueText;  // 선택된 난이도 숫자 (selectedDiffImg 의 자식 권장)
    public Color disabledImageTint = new Color(0.45f, 0.45f, 0.45f, 1f); // 난이도가 하나도 없는 곡일 때
    public float diffChangeDuration = 0.22f;
    public float diffSlideDistance = 36f;
    public float diffValuePunch = 1.25f;

    [Header("우측 헤더 난이도 표시 (평행사변형)")]
    [Tooltip("움직이는 부모 (Pivot 은 가운데). 자식으로 Base, Fill 두 개의 ParallelogramGraphic")]
    public RectTransform headerIndicator;
    public ParallelogramGraphic indicatorBase;     // 이전 색
    public ParallelogramGraphic indicatorFill;     // 새 색이 이동 방향으로 쓸고 지나감
    public RectTransform headerEasyLabel;
    public RectTransform headerHardLabel;
    public RectTransform headerInsaneLabel;
    public float indicatorMoveDuration = 0.25f;

    [Header("잠긴 곡 UI")]
    public RectTransform lockedStatusPanel;

    [Header("우측 악곡 정렬표")]
    [Tooltip("행들이 들어갈 영역. 이 영역 밖은 잘려서 안 보인다 (RectMask2D 자동 추가)")]
    public RectTransform songListViewport;
    [Tooltip("비워 두면 코드로 기본 행을 만든다")]
    public SongRowUI songRowPrefab;
    public TMP_FontAsset rowFont;
    public int visibleRowCount = 8;
    [Tooltip("선택된 곡이 놓이는 줄 (0부터). 3 = 위에서 4번째")]
    public int selectedSlot = 3;
    public float rowHeight = 80f;
    public float rowGap = 28f;
    [Tooltip("맨 위/맨 아래 행과 잘리는 경계 사이의 여유 (아주 살짝)")]
    public float clipPadding = 8f;
    public bool autoSizeViewportHeight = true;
    public Vector2Int clipSoftness = Vector2Int.zero;
    public bool loopList = true;
    public float scrollLerpSpeed = 14f;
    public float selectedBarDrawDuration = 0.28f;
    public Color rowTextColor = Color.white;
    [Tooltip("곡별 선택 그라데이션 이미지 폴더 (Resources 기준). 없으면 Sprites/etc/selectedbar 사용")]
    public string selectedBarFolder = "Sprites/SelectedFallback";
    [Tooltip("songs.json 에 BarColor 가 없는 곡의 색 바 색")]
    public Color defaultSongColor = Color.white;

    [Header("키 반복 (← → 길게 누르기)")]
    public float keyRepeatDelay = 0.35f;
    public float keyRepeatInterval = 0.07f;

    [Header("곡 길이")]
    [Tooltip("songs.json 에 Length 가 없으면 Audio/Full/{id} 를 잠깐 읽어 길이를 구한다")]
    public bool loadLengthFromAudio = true;

    [Header("추가 설정 UI")]
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI mvStatusText;
    public Image mvIconImg;
    public Sprite mvOnSprite;
    public Sprite mvOffSprite;

    [Header("미리듣기")]
    public AudioSource previewAudioSource;
    [Range(0f, 1f)] public float previewVolume = 1f;
    public float previewDelay = 0.15f;
    public float previewFadeIn = 0.4f;

    // ===== 내부 상태 =====
    [Serializable] private class SongExtra { public int Id; public string Length; public string BarColor; }

    private readonly List<SongData> songList = new List<SongData>();
    private readonly Dictionary<int, string> lengthOverride = new Dictionary<int, string>();
    private readonly Dictionary<int, float> lengthCache = new Dictionary<int, float>();
    private readonly Dictionary<int, Sprite> squareCache = new Dictionary<int, Sprite>();
    private readonly Dictionary<int, Sprite> blurCache = new Dictionary<int, Sprite>();
    private readonly Dictionary<int, Sprite> jacketCache = new Dictionary<int, Sprite>();
    private readonly List<SongRowUI> rows = new List<SongRowUI>();

    private readonly Sprite[] diffOn = new Sprite[4];
    private readonly Sprite[] diffOff = new Sprite[4];
    private readonly Color[] diffColors = new Color[4];
    private Color colorDisabled;
    private Sprite selectedBarSprite;
    private Sprite lockedSprite;

    private int currentVirtualIndex = 0;
    private float visualSongIndex = 0f;
    private int currentDifficulty = 2; // 1: EASY, 2: HARD, 3: INSANE
    private bool isMvOn = false;
    private bool useLoop = false;
    private bool isCurrentlyLocked = false;
    private bool isLeaving = false;

    private int repeatDir = 0;
    private float repeatTimer = 0f;

    private Coroutine bgSlideRoutine, leftInfoRoutine, previewRoutine, lengthRoutine, lockedPanelRoutine;
    private GameObject oldBgObj;
    private AspectRatioFitter bgFitter;
    private Vector2 bgOriginalPos;
    private bool bgPosInit = false;
    private Vector2 leftInfoBasePos;
    private bool leftInfoPosInit = false;

    private readonly Dictionary<int, Sprite> selectedBarCache = new Dictionary<int, Sprite>();
    private readonly Dictionary<int, Color> songColors = new Dictionary<int, Color>();
    private Coroutine diffRoutine, indicatorRoutine;
    private Vector2 diffBasePos;
    private bool diffPosInit = false;
    private int indicatorDiff = 0; // 헤더 표시가 현재 가리키는 난이도 (0 = 아직 없음)
    private RenderTexture jacketBlurRT;

    private SongData CurrentSong { get { return songList[DataIndex(currentVirtualIndex)]; } }
    private float ViewportHeight { get { return clipPadding * 2f + visibleRowCount * rowHeight + Mathf.Max(0, visibleRowCount - 1) * rowGap; } }

    // =====================================================================
    //  초기화
    // =====================================================================
    void Start()
    {
        ColorUtility.TryParseHtmlString("#5ce1e6", out diffColors[1]);
        ColorUtility.TryParseHtmlString("#ff751f", out diffColors[2]);
        ColorUtility.TryParseHtmlString("#ff3131", out diffColors[3]);
        ColorUtility.TryParseHtmlString("#545454", out colorDisabled);

        visibleRowCount = Mathf.Max(1, visibleRowCount);
        selectedSlot = Mathf.Clamp(selectedSlot, 0, visibleRowCount - 1);

        LoadSongList();
        LoadSprites();
        if (songList.Count == 0) { Debug.LogWarning("[SongSelect] 표시할 곡이 없습니다."); return; }

        // 곡 수가 화면 줄 수보다 적으면 같은 곡이 반복돼 보이므로 루프하지 않는다
        useLoop = loopList && songList.Count >= visibleRowCount;

        foreach (var s in songList)
        {
            squareCache[s.Id] = Resources.Load<Sprite>($"Sprites/squareimg/{s.Id}");
            blurCache[s.Id] = Resources.Load<Sprite>($"Sprites/blurimg/{s.Id}");
            jacketCache[s.Id] = squareCache[s.Id];
            selectedBarCache[s.Id] = Resources.Load<Sprite>($"{selectedBarFolder}/{s.Id}");
        }

        RestoreLastSelection();
        SetupViewport();
        BuildRows();

        visualSongIndex = currentVirtualIndex;
        isCurrentlyLocked = CurrentSong.ExistSongbar == 2;
        if (lockedStatusPanel != null)
        {
            lockedStatusPanel.gameObject.SetActive(true);
            CanvasGroup cg = GetOrAdd<CanvasGroup>(lockedStatusPanel.gameObject);
            cg.alpha = isCurrentlyLocked ? 1f : 0f;
        }

        RefreshSelectedSong(0, true);
        UpdateRows();
    }

    private void LoadSongList()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "data", "songs.json");
        if (!File.Exists(path)) { Debug.LogWarning("[SongSelect] songs.json 이 없습니다: " + path); return; }

        string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        SongData[] arr = JsonHelper.FromJson<SongData>(json);
        if (arr != null) songList.AddRange(arr.Where(s => s.ExistSongbar == 1 || s.ExistSongbar == 2));

        // (선택) songs.json 의 "Length": "3:39" 또는 219.5 (초) — DataModels 를 고치지 않고 따로 읽는다
        try
        {
            SongExtra[] extras = JsonHelper.FromJson<SongExtra>(json);
            if (extras != null)
            {
                foreach (var e in extras)
                {
                    if (!string.IsNullOrWhiteSpace(e.Length)) lengthOverride[e.Id] = e.Length.Trim();

                    // "BarColor": "#ff3131" (또는 "ff3131")
                    if (!string.IsNullOrWhiteSpace(e.BarColor))
                    {
                        string hex = e.BarColor.Trim();
                        if (!hex.StartsWith("#")) hex = "#" + hex;
                        if (ColorUtility.TryParseHtmlString(hex, out Color c)) songColors[e.Id] = c;
                    }
                }
            }
        }
        catch (Exception) { }
    }

    private void LoadSprites()
    {
        // Level.png (Sprite Mode = Multiple) 안의 스프라이트들을 이름으로 찾는다 (대소문자 무시)
        var sheet = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        Sprite[] sheetSprites = Resources.LoadAll<Sprite>(difficultySheetPath);
        if (sheetSprites != null) foreach (var s in sheetSprites) sheet[s.name] = s;
        if (sheet.Count == 0)
            Debug.LogWarning($"[SongSelect] Resources/{difficultySheetPath} 에서 스프라이트를 찾지 못했습니다. (Texture Type = Sprite, Sprite Mode = Multiple 인지 확인)");

        string[] names = { "", "EASY", "HARD", "INSANE" };
        for (int d = 1; d <= 3; d++)
        {
            diffOn[d] = LoadDifficultySprite(names[d], "ON", sheet);
            diffOff[d] = LoadDifficultySprite(names[d], "OFF", sheet);
            if (diffOn[d] == null || diffOff[d] == null)
                Debug.LogWarning($"[SongSelect] {names[d]}_ON / {names[d]}_OFF 스프라이트를 찾지 못했습니다. (Level.png 의 스프라이트 이름 확인)");
        }

        selectedBarSprite = Resources.Load<Sprite>("Sprites/etc/selectedbar");
        if (selectedBarSprite == null) Debug.LogWarning("[SongSelect] Sprites/etc/selectedbar 스프라이트가 없습니다.");
        lockedSprite = Resources.Load<Sprite>("Sprites/etc/Locked");
        if (lockedSprite == null) lockedSprite = Resources.Load<Sprite>("Sprites/Songbar/Locked");

        if (mvOnSprite == null) mvOnSprite = Resources.Load<Sprite>("Sprites/GameAsset/MV_ON");
        if (mvOffSprite == null) mvOffSprite = Resources.Load<Sprite>("Sprites/GameAsset/MV_OFF");
    }

    // 1순위: 스프라이트 시트(Level.png) 안의 "EASY_ON" 등 (대소문자 무시)
    // 2순위: 예전 방식의 개별 파일 Sprites/etc/EASY_ON.png 등
    private static Sprite LoadDifficultySprite(string upper, string state, Dictionary<string, Sprite> sheet)
    {
        if (sheet != null && sheet.TryGetValue($"{upper}_{state}", out Sprite fromSheet) && fromSheet != null)
            return fromSheet;

        string title = upper.Substring(0, 1) + upper.Substring(1).ToLowerInvariant();
        string[] bases = { upper, title, upper.ToLowerInvariant() };
        string stateTitle = state.Substring(0, 1) + state.Substring(1).ToLowerInvariant();
        string[] states = { state, stateTitle, state.ToLowerInvariant() };

        foreach (var b in bases)
            foreach (var s in states)
            {
                Sprite spr = Resources.Load<Sprite>($"Sprites/etc/{b}_{s}");
                if (spr != null) return spr;
            }
        return null;
    }

    // 게임/결과창에서 돌아왔을 때 마지막으로 고른 곡·난이도·MV 상태 유지
    private void RestoreLastSelection()
    {
        var core = CoreManager.Instance;
        if (core == null || core.CurrentSongData == null) return;

        int idx = songList.FindIndex(s => s.Id == core.CurrentSongData.Id);
        if (idx < 0) return;
        currentVirtualIndex = idx;
        currentDifficulty = Mathf.Clamp(core.CurrentLevelId, 1, 3);
        isMvOn = core.IsMvOn && songList[idx].HasMV == 1;
    }

    private void SetupViewport()
    {
        if (songListViewport == null) { Debug.LogError("[SongSelect] songListViewport 가 연결되지 않았습니다."); return; }

        if (autoSizeViewportHeight)
            songListViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ViewportHeight);

        RectMask2D mask = GetOrAdd<RectMask2D>(songListViewport.gameObject);
        mask.softness = clipSoftness;
    }

    private void BuildRows()
    {
        if (songListViewport == null) return;

        // 보이는 줄 + 위아래로 스크롤 중 잠깐 보이는 줄 2개씩
        int poolSize = visibleRowCount + 4;
        for (int i = 0; i < poolSize; i++)
        {
            SongRowUI row = songRowPrefab != null
                ? Instantiate(songRowPrefab, songListViewport, false)
                : SongRowUI.CreateDefault(songListViewport, rowFont, rowHeight);
            row.name = $"SongRow_{i}";
            row.InitLayout(rowHeight, selectedBarSprite);
            row.gameObject.SetActive(false);
            rows.Add(row);
        }
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    void Update()
    {
        if (songList.Count == 0 || isLeaving) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        float dt = Time.unscaledDeltaTime;

        int move = ReadHorizontalRepeat(kb, dt);
        if (move != 0) MoveSelection(move);

        if (kb.upArrowKey.wasPressedThisFrame) ChangeDifficulty(1);
        if (kb.downArrowKey.wasPressedThisFrame) ChangeDifficulty(-1);

        if (kb.aKey.wasPressedThisFrame && CoreManager.Instance != null)
        {
            CoreManager.Instance.IsAutoPlay = !CoreManager.Instance.IsAutoPlay;
            Debug.Log(CoreManager.Instance.IsAutoPlay ? "AUTO PLAY 개시" : "AUTO PLAY 종료");
        }

        if (CoreManager.Instance != null)
        {
            var st = CoreManager.Instance.CurrentSettings;
            if (kb.f1Key.wasPressedThisFrame) st.SettingSpeed = Math.Round(Math.Max(1.0, st.SettingSpeed - 0.1), 1);
            if (kb.f2Key.wasPressedThisFrame) st.SettingSpeed = Math.Round(Math.Min(12.0, st.SettingSpeed + 0.1), 1);
        }

        SongData song = CurrentSong;
        bool hasMv = song.HasMV == 1;
        if (hasMv && kb.fKey.wasPressedThisFrame) isMvOn = !isMvOn;
        else if (!hasMv) isMvOn = false;

        // 부드러운 스크롤 (프레임레이트와 무관한 지수 감쇠)
        float k = 1f - Mathf.Exp(-scrollLerpSpeed * dt);
        visualSongIndex = Mathf.Lerp(visualSongIndex, currentVirtualIndex, k);
        if (Mathf.Abs(visualSongIndex - currentVirtualIndex) < 0.001f) visualSongIndex = currentVirtualIndex;

        UpdateRows();
        foreach (var r in rows) r.Tick(dt);

        UpdateMvIcon(hasMv);
        if (speedText != null && CoreManager.Instance != null)
            speedText.text = CoreManager.Instance.CurrentSettings.SettingSpeed.ToString("0.0", CultureInfo.InvariantCulture);
        if (mvStatusText != null) mvStatusText.text = hasMv ? (isMvOn ? "ON" : "OFF") : "None";

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            TryEnter(kb.gKey.isPressed);
    }

    private int ReadHorizontalRepeat(Keyboard kb, float dt)
    {
        if (kb.leftArrowKey.wasPressedThisFrame) { repeatDir = -1; repeatTimer = keyRepeatDelay; return -1; }
        if (kb.rightArrowKey.wasPressedThisFrame) { repeatDir = 1; repeatTimer = keyRepeatDelay; return 1; }

        bool holding = repeatDir == -1 ? kb.leftArrowKey.isPressed : (repeatDir == 1 && kb.rightArrowKey.isPressed);
        if (!holding) { repeatDir = 0; return 0; }

        repeatTimer -= dt;
        if (repeatTimer <= 0f) { repeatTimer += keyRepeatInterval; return repeatDir; }
        return 0;
    }

    // =====================================================================
    //  곡 / 난이도 변경
    // =====================================================================
    private void MoveSelection(int dir)
    {
        int next = currentVirtualIndex + dir;
        if (!useLoop)
        {
            next = Mathf.Clamp(next, 0, songList.Count - 1);
            if (next == currentVirtualIndex) return;
        }
        currentVirtualIndex = next;
        RefreshSelectedSong(dir, false);
    }

    private void RefreshSelectedSong(int dir, bool instant)
    {
        SongData song = CurrentSong;

        ValidateCurrentDifficulty(song);
        UpdateLeftInfo(song);
        UpdateDifficultyUI(song, 0);
        ApplyRowDifficultyColors();
        MoveHeaderIndicator(instant); // 곡에 없는 난이도라 자동으로 바뀐 경우에도 이동
        UpdateBackground(song.Id, dir, instant);
        UpdateLockedPanel(song);
        UpdateSongLength(song);
        if (!instant) PlayLeftInfoAnim(dir);
        PlayPreviewMusic();
    }

    private void ChangeDifficulty(int direction)
    {
        SongData song = CurrentSong;
        int prev = currentDifficulty;
        int next = currentDifficulty;
        for (int i = 0; i < 3; i++)
        {
            next += direction;
            if (next > 3) next = 1;
            if (next < 1) next = 3;
            if (SongRowUI.IsDifficultyAvailable(GetDifficultyValue(song, next)))
            {
                currentDifficulty = next;
                break;
            }
        }
        if (currentDifficulty == prev) return; // 고를 수 있는 난이도가 하나뿐이면 아무 연출 없음

        UpdateDifficultyUI(song, direction);
        ApplyRowDifficultyColors();
        MoveHeaderIndicator(false);
    }

    // 현재 난이도가 이 곡에 없으면 HARD → EASY → INSANE 순으로 대체
    private void ValidateCurrentDifficulty(SongData song)
    {
        if (SongRowUI.IsDifficultyAvailable(GetDifficultyValue(song, currentDifficulty))) return;
        if (SongRowUI.IsDifficultyAvailable(song.Difficulty_HARD)) currentDifficulty = 2;
        else if (SongRowUI.IsDifficultyAvailable(song.Difficulty_EASY)) currentDifficulty = 1;
        else if (SongRowUI.IsDifficultyAvailable(song.Difficulty_INSANE)) currentDifficulty = 3;
        else currentDifficulty = 2;
    }

    private static string GetDifficultyValue(SongData song, int diff)
    {
        if (diff == 1) return song.Difficulty_EASY;
        if (diff == 2) return song.Difficulty_HARD;
        if (diff == 3) return song.Difficulty_INSANE;
        return "00";
    }

    // =====================================================================
    //  우측 정렬표
    // =====================================================================
    private void UpdateRows()
    {
        if (rows.Count == 0) return;

        int poolSize = rows.Count;
        float spacing = rowHeight + rowGap;
        int center = Mathf.RoundToInt(visualSongIndex);
        int first = center - selectedSlot - 2;

        for (int i = 0; i < poolSize; i++)
        {
            int v = first + i;
            SongRowUI row = rows[Wrap(v, poolSize)];

            bool valid = useLoop || (v >= 0 && v < songList.Count);
            if (!valid)
            {
                if (row.gameObject.activeSelf) row.gameObject.SetActive(false);
                row.boundVirtualIndex = int.MinValue;
                row.SetSelected(false, 0f);
                continue;
            }

            if (row.boundVirtualIndex != v) BindRow(row, v);
            if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);

            float slot = (v - visualSongIndex) + selectedSlot;
            row.rect.anchoredPosition = new Vector2(0f, -(clipPadding + rowHeight * 0.5f + slot * spacing));
            row.SetSelected(v == currentVirtualIndex, selectedBarDrawDuration);
        }
    }

    private void BindRow(SongRowUI row, int virtualIndex)
    {
        SongData s = songList[DataIndex(virtualIndex)];
        squareCache.TryGetValue(s.Id, out Sprite sq);
        selectedBarCache.TryGetValue(s.Id, out Sprite bar);
        if (bar == null) bar = selectedBarSprite;
        Color barColor = songColors.TryGetValue(s.Id, out Color c) ? c : defaultSongColor;
        row.Bind(virtualIndex, s, sq, BuildComposerLine(s), s.ExistSongbar == 2, lockedSprite, bar, barColor);
        row.ApplyDifficultyColors(currentDifficulty, diffColors, rowTextColor);
    }

    private void ApplyRowDifficultyColors()
    {
        foreach (var r in rows)
            if (r.boundVirtualIndex != int.MinValue) r.ApplyDifficultyColors(currentDifficulty, diffColors, rowTextColor);
    }

    private int DataIndex(int virtualIndex)
    {
        return useLoop ? Wrap(virtualIndex, songList.Count) : Mathf.Clamp(virtualIndex, 0, songList.Count - 1);
    }

    private static int Wrap(int index, int count) { return ((index % count) + count) % count; }

    // 좌측/우측 공통: songs.json 의 "Composer" 만 표시 (Vo. 보컬은 표기하지 않음)
    private static string BuildComposerLine(SongData s)
    {
        return s.Composer ?? "";
    }

    // =====================================================================
    //  좌측 패널
    // =====================================================================
    private void UpdateLeftInfo(SongData song)
    {
        // 좌측: "Name" 만 표시 (SubName 제외). 우측 정렬표는 SongRowUI 에서 Name + SubName 표시
        if (selectedSongTitleText != null) selectedSongTitleText.text = song.Name ?? "";
        if (selectedComposerText != null) selectedComposerText.text = BuildComposerLine(song);

        if (selectedJacketImg != null)
        {
            jacketCache.TryGetValue(song.Id, out Sprite jacket);
            selectedJacketImg.sprite = jacket;
            selectedJacketImg.color = jacket != null ? Color.white : new Color(0.1f, 0.1f, 0.1f, 1f);
            UpdateJacketBlur(jacket);
        }
    }

    // ===== 자켓 하단 블러 =====
    //  자켓을 GPU 에서 단계적으로 축소(박스 필터) → 몇 번 더 축소/확대 반복 → 작은 텍스처를 크게 늘려 표시.
    //  셰이더나 Read/Write 설정 없이 동작한다. 곡 정보 띠 영역의 RectMask2D 가 띠 부분만 남기고 잘라 낸다.
    private void UpdateJacketBlur(Sprite jacket)
    {
        if (jacketBlurImage == null) return;
        if (jacket == null || jacket.texture == null) { jacketBlurImage.enabled = false; return; }

        Texture src = jacket.texture;
        Rect r;
        try { r = jacket.textureRect; } catch (Exception) { r = jacket.rect; } // 아틀라스 Tight 패킹 대비

        int outW = Mathf.Max(4, jacketBlurResolution);
        int outH = Mathf.Max(4, Mathf.RoundToInt(outW * r.height / Mathf.Max(1f, r.width)));
        if (jacketBlurRT == null || jacketBlurRT.width != outW || jacketBlurRT.height != outH)
        {
            if (jacketBlurRT != null) jacketBlurRT.Release();
            jacketBlurRT = new RenderTexture(outW, outH, 0, RenderTextureFormat.ARGB32);
            jacketBlurRT.filterMode = FilterMode.Bilinear;
            jacketBlurRT.wrapMode = TextureWrapMode.Clamp;
            jacketBlurRT.Create();
        }

        // 1) 원본의 스프라이트 영역만 잘라 절반 크기로
        Vector2 scale = new Vector2(r.width / src.width, r.height / src.height);
        Vector2 offset = new Vector2(r.x / src.width, r.y / src.height);
        int w = Mathf.Max(outW, Mathf.RoundToInt(r.width * 0.5f));
        int h = Mathf.Max(outH, Mathf.RoundToInt(r.height * 0.5f));
        RenderTexture cur = GetTempRT(w, h);
        Graphics.Blit(src, cur, scale, offset);

        // 2) 목표 해상도까지 절반씩 축소 (한 번에 줄이면 깜빡이는 점이 생김)
        while (w / 2 >= outW)
        {
            w /= 2; h = Mathf.Max(outH, h / 2);
            RenderTexture next = GetTempRT(w, h);
            Graphics.Blit(cur, next);
            RenderTexture.ReleaseTemporary(cur);
            cur = next;
        }
        Graphics.Blit(cur, jacketBlurRT);
        RenderTexture.ReleaseTemporary(cur);

        // 3) 반 크기로 내렸다 올리기를 반복해 더 부드럽게
        for (int i = 0; i < jacketBlurIterations; i++)
        {
            RenderTexture half = GetTempRT(Mathf.Max(2, outW / 2), Mathf.Max(2, outH / 2));
            Graphics.Blit(jacketBlurRT, half);
            Graphics.Blit(half, jacketBlurRT);
            RenderTexture.ReleaseTemporary(half);
        }

        jacketBlurImage.texture = jacketBlurRT;
        jacketBlurImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        jacketBlurImage.raycastTarget = false;
        jacketBlurImage.enabled = true;
        AlignJacketBlurToJacket();
    }

    private static RenderTexture GetTempRT(int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(Mathf.Max(1, w), Mathf.Max(1, h), 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        return rt;
    }

    // 블러 이미지를 자켓과 정확히 같은 위치·크기로 겹친다 (마스크가 띠 부분만 보여 줌)
    private void AlignJacketBlurToJacket()
    {
        if (jacketBlurImage == null || selectedJacketImg == null) return;
        RectTransform b = jacketBlurImage.rectTransform;
        RectTransform j = selectedJacketImg.rectTransform;
        b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.5f);
        b.pivot = j.pivot;
        b.sizeDelta = j.rect.size;
        b.position = j.position;
    }

    void OnDestroy()
    {
        if (jacketBlurRT != null) { jacketBlurRT.Release(); Destroy(jacketBlurRT); jacketBlurRT = null; }
    }

    // 좌측: 선택된 난이도 하나만 표시. animDir: +1(↑, 어려운 쪽) / -1(↓, 쉬운 쪽) / 0(연출 없음)
    private void UpdateDifficultyUI(SongData song, int animDir)
    {
        string raw = GetDifficultyValue(song, currentDifficulty);
        bool available = SongRowUI.IsDifficultyAvailable(raw);

        if (selectedDiffImg != null)
        {
            Sprite s = available ? diffOn[currentDifficulty] : diffOff[currentDifficulty];
            if (s != null) selectedDiffImg.sprite = s;
            selectedDiffImg.color = available ? Color.white : disabledImageTint;
        }
        if (selectedDiffValueText != null)
        {
            selectedDiffValueText.text = available ? SongRowUI.FormatDifficulty(raw) : "-";
            selectedDiffValueText.color = available ? Color.white : colorDisabled;
        }

        if (animDir != 0) PlayDiffChangeAnim(animDir);
    }

    // 새 난이도 판이 누른 방향 쪽에서 미끄러져 들어오며 나타나고, 숫자가 살짝 튀어오른다
    private void PlayDiffChangeAnim(int dir)
    {
        if (selectedDiffImg == null) return;
        RectTransform rt = selectedDiffImg.rectTransform;
        if (!diffPosInit) { diffBasePos = rt.anchoredPosition; diffPosInit = true; }

        if (diffRoutine != null) StopCoroutine(diffRoutine);
        diffRoutine = StartCoroutine(DiffChangeRoutine(rt, dir));
    }

    private IEnumerator DiffChangeRoutine(RectTransform rt, int dir)
    {
        CanvasGroup cg = GetOrAdd<CanvasGroup>(rt.gameObject);
        cg.blocksRaycasts = false;
        Transform valueTf = selectedDiffValueText != null ? selectedDiffValueText.transform : null;

        Vector2 from = diffBasePos + new Vector2(dir * diffSlideDistance, 0f);
        float t = 0f, dur = Mathf.Max(0.01f, diffChangeDuration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
            rt.anchoredPosition = Vector2.Lerp(from, diffBasePos, e);
            cg.alpha = Mathf.Lerp(0f, 1f, e);
            if (valueTf != null) valueTf.localScale = Vector3.one * Mathf.Lerp(diffValuePunch, 1f, e);
            yield return null;
        }

        rt.anchoredPosition = diffBasePos;
        cg.alpha = 1f;
        if (valueTf != null) valueTf.localScale = Vector3.one;
        diffRoutine = null;
    }

    // ===== 우측 헤더 평행사변형 =====
    //  선택된 난이도 라벨(EZ/HD/INS) 위로 이동하면서, 새 난이도 색이 이동 방향대로 쓸고 지나간다.
    //  (INS → HD : 오른쪽에서 왼쪽으로 / EZ → HD : 왼쪽에서 오른쪽으로)
    private void MoveHeaderIndicator(bool instant)
    {
        if (headerIndicator == null) return;
        int to = Mathf.Clamp(currentDifficulty, 1, 3);
        if (!instant && to == indicatorDiff) return;

        // 이전 연출이 진행 중이었다면 그 연출의 목표 색으로 확정한 뒤 이어서 시작
        if (indicatorRoutine != null)
        {
            StopCoroutine(indicatorRoutine);
            indicatorRoutine = null;
            if (indicatorBase != null && indicatorFill != null) indicatorBase.color = indicatorFill.color;
            if (indicatorFill != null) indicatorFill.FillAmount = 0f;
        }

        float targetX = GetHeaderLabelWorldX(to);
        Color newColor = diffColors[to];

        if (instant || indicatorDiff == 0)
        {
            Vector3 p = headerIndicator.position;
            headerIndicator.position = new Vector3(targetX, p.y, p.z);
            if (indicatorBase != null) indicatorBase.color = newColor;
            if (indicatorFill != null) { indicatorFill.color = newColor; indicatorFill.FillAmount = 0f; }
        }
        else
        {
            indicatorRoutine = StartCoroutine(HeaderIndicatorRoutine(targetX, newColor));
        }
        indicatorDiff = to;
    }

    private IEnumerator HeaderIndicatorRoutine(float targetX, Color newColor)
    {
        Vector3 start = headerIndicator.position;
        bool movingLeft = targetX < start.x;

        if (indicatorFill != null)
        {
            indicatorFill.color = newColor;
            indicatorFill.FillFromRight = movingLeft;
            indicatorFill.FillAmount = 0f;
        }

        float t = 0f, dur = Mathf.Max(0.01f, indicatorMoveDuration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
            headerIndicator.position = new Vector3(Mathf.Lerp(start.x, targetX, e), start.y, start.z);
            if (indicatorFill != null) indicatorFill.FillAmount = e;
            else if (indicatorBase != null) indicatorBase.color = Color.Lerp(indicatorBase.color, newColor, e);
            yield return null;
        }

        headerIndicator.position = new Vector3(targetX, start.y, start.z);
        if (indicatorBase != null) indicatorBase.color = newColor;
        if (indicatorFill != null) indicatorFill.FillAmount = 0f;
        indicatorRoutine = null;
    }

    private float GetHeaderLabelWorldX(int diff)
    {
        RectTransform label = diff == 1 ? headerEasyLabel : (diff == 2 ? headerHardLabel : headerInsaneLabel);
        if (label == null) return headerIndicator.position.x;
        return label.TransformPoint(label.rect.center).x;
    }

    private void PlayLeftInfoAnim(int dir)
    {
        if (leftInfoGroup == null) return;
        RectTransform rt = (RectTransform)leftInfoGroup.transform;
        if (!leftInfoPosInit) { leftInfoBasePos = rt.anchoredPosition; leftInfoPosInit = true; }

        if (leftInfoRoutine != null) StopCoroutine(leftInfoRoutine);
        leftInfoRoutine = StartCoroutine(LeftInfoRoutine(rt));
    }

    private IEnumerator LeftInfoRoutine(RectTransform rt)
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, leftInfoAnimDuration);
        Vector2 from = leftInfoBasePos + new Vector2(-leftInfoSlideDistance, 0f);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
            leftInfoGroup.alpha = Mathf.Lerp(0.2f, 1f, e);
            rt.anchoredPosition = Vector2.Lerp(from, leftInfoBasePos, e);
            yield return null;
        }
        leftInfoGroup.alpha = 1f;
        rt.anchoredPosition = leftInfoBasePos;
        leftInfoRoutine = null;
    }

    // ===== 곡 길이 =====
    private void UpdateSongLength(SongData song)
    {
        if (songLengthText == null) return;
        if (lengthRoutine != null) { StopCoroutine(lengthRoutine); lengthRoutine = null; }

        if (lengthOverride.TryGetValue(song.Id, out string raw)) { songLengthText.text = FormatLengthString(raw); return; }
        if (lengthCache.TryGetValue(song.Id, out float sec)) { songLengthText.text = FormatSeconds(sec); return; }

        songLengthText.text = "-:--";
        if (loadLengthFromAudio) lengthRoutine = StartCoroutine(LoadLengthRoutine(song.Id));
    }

    private IEnumerator LoadLengthRoutine(int songId)
    {
        yield return new WaitForSecondsRealtime(0.25f); // 빠르게 넘길 때는 읽지 않음
        if (CurrentSong.Id != songId) yield break;

        ResourceRequest req = Resources.LoadAsync<AudioClip>($"Audio/Full/{songId}");
        yield return req;

        AudioClip clip = req.asset as AudioClip;
        lengthCache[songId] = clip != null ? clip.length : -1f;
        if (clip != null) Resources.UnloadAsset(clip);

        if (songLengthText != null && CurrentSong.Id == songId) songLengthText.text = FormatSeconds(lengthCache[songId]);
        lengthRoutine = null;
    }

    private static string FormatLengthString(string raw)
    {
        if (raw.Contains(":")) return raw;
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float sec) ? FormatSeconds(sec) : raw;
    }

    private static string FormatSeconds(float sec)
    {
        if (sec < 0f) return "-:--";
        int total = Mathf.FloorToInt(sec);
        return $"{total / 60}:{total % 60:00}";
    }

    // ===== 배경 슬라이드 (예전 연출) =====
    //  → (다음 곡) : 새 배경이 아래에서 올라오고, 이전 배경은 위로 밀려 나간다
    //  ← (이전 곡) : 새 배경이 위에서 내려오고, 이전 배경은 아래로 밀려 나간다
    private void UpdateBackground(int songId, int direction, bool instant)
    {
        if (selectScreenImg == null) return;
        blurCache.TryGetValue(songId, out Sprite s);
        if (s == null) return; // 이미지가 없으면 기존 배경 유지

        // 연타로 이전 연출이 진행 중이면 즉시 정리하고 원위치
        if (bgSlideRoutine != null) { StopCoroutine(bgSlideRoutine); bgSlideRoutine = null; }
        FinishBackgroundSlide();

        if (instant || direction == 0 || selectScreenImg.sprite == null || selectScreenImg.sprite == s)
        {
            selectScreenImg.sprite = s;
            return;
        }
        bgSlideRoutine = StartCoroutine(BackgroundSlideRoutine(s, direction));
    }

    private IEnumerator BackgroundSlideRoutine(Sprite newSprite, int direction)
    {
        RectTransform rt = selectScreenImg.rectTransform;
        if (bgFitter == null) bgFitter = selectScreenImg.GetComponent<AspectRatioFitter>();
        if (!bgPosInit) { bgOriginalPos = rt.anchoredPosition; bgPosInit = true; }

        float slideDistance = rt.rect.height;
        if (slideDistance <= 0f) slideDistance = 1080f;
        float offset = direction == 1 ? -slideDistance : slideDistance;

        // 이전 배경의 복사본 (원본 바로 뒤에 둠)
        oldBgObj = new GameObject("OldBgObj", typeof(RectTransform));
        oldBgObj.transform.SetParent(selectScreenImg.transform.parent, false);
        oldBgObj.transform.SetSiblingIndex(selectScreenImg.transform.GetSiblingIndex());
        Image oldImg = oldBgObj.AddComponent<Image>();
        oldImg.sprite = selectScreenImg.sprite;
        oldImg.color = selectScreenImg.color;
        oldImg.type = selectScreenImg.type;
        oldImg.preserveAspect = selectScreenImg.preserveAspect;
        oldImg.raycastTarget = false;
        RectTransform oldRt = (RectTransform)oldBgObj.transform;
        CopyRect(rt, oldRt);

        // AspectRatioFitter 가 위치를 고정(드라이브)하므로 슬라이드 중에는 잠시 끈다
        if (bgFitter != null) bgFitter.enabled = false;

        selectScreenImg.sprite = newSprite;
        rt.anchoredPosition = bgOriginalPos + new Vector2(0f, offset);

        float t = 0f;
        float dur = Mathf.Max(0.01f, bgSlideDuration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
            rt.anchoredPosition = Vector2.Lerp(bgOriginalPos + new Vector2(0f, offset), bgOriginalPos, ease);
            oldRt.anchoredPosition = Vector2.Lerp(bgOriginalPos, bgOriginalPos + new Vector2(0f, -offset), ease);
            yield return null;
        }

        FinishBackgroundSlide();
        bgSlideRoutine = null;
    }

    private void FinishBackgroundSlide()
    {
        if (oldBgObj != null) { Destroy(oldBgObj); oldBgObj = null; }
        if (selectScreenImg != null && bgPosInit) selectScreenImg.rectTransform.anchoredPosition = bgOriginalPos;
        if (bgFitter != null) bgFitter.enabled = true;
    }

    private static void CopyRect(RectTransform src, RectTransform dst)
    {
        dst.anchorMin = src.anchorMin;
        dst.anchorMax = src.anchorMax;
        dst.pivot = src.pivot;
        dst.sizeDelta = src.sizeDelta;
        dst.anchoredPosition = src.anchoredPosition;
        dst.localScale = src.localScale;
        dst.localRotation = src.localRotation;
    }

    // ===== 잠긴 곡 패널 =====
    private void UpdateLockedPanel(SongData song)
    {
        bool locked = song.ExistSongbar == 2;
        if (locked == isCurrentlyLocked) return;
        isCurrentlyLocked = locked;

        if (lockedStatusPanel == null) return;
        if (lockedPanelRoutine != null) StopCoroutine(lockedPanelRoutine);
        lockedPanelRoutine = StartCoroutine(AnimateLockedPanel(locked));
    }

    private IEnumerator AnimateLockedPanel(bool show)
    {
        CanvasGroup cg = GetOrAdd<CanvasGroup>(lockedStatusPanel.gameObject);
        float start = cg.alpha, target = show ? 1f : 0f;
        float t = 0f, dur = 0.3f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
            cg.alpha = Mathf.Lerp(start, target, e);
            yield return null;
        }
        cg.alpha = target;
        lockedPanelRoutine = null;
    }

    // =====================================================================
    //  MV / 미리듣기 / 진입
    // =====================================================================
    private void UpdateMvIcon(bool hasMv)
    {
        if (mvIconImg == null) return;
        mvIconImg.enabled = hasMv;
        if (hasMv) mvIconImg.sprite = isMvOn ? mvOnSprite : mvOffSprite;
    }

    private void PlayPreviewMusic()
    {
        if (previewAudioSource == null) return;
        previewAudioSource.Stop();
        if (previewRoutine != null) StopCoroutine(previewRoutine);
        previewRoutine = StartCoroutine(LoadAndPlayPreview(CurrentSong.Id));
    }

    private IEnumerator LoadAndPlayPreview(int songId)
    {
        yield return new WaitForSecondsRealtime(previewDelay);
        if (CurrentSong.Id != songId) yield break;

        ResourceRequest req = Resources.LoadAsync<AudioClip>($"Audio/Preview/{songId}");
        yield return req;
        if (CurrentSong.Id != songId || req.asset == null) yield break;

        previewAudioSource.clip = req.asset as AudioClip;
        previewAudioSource.loop = true;
        previewAudioSource.time = 0f;
        previewAudioSource.volume = 0f;
        previewAudioSource.Play();

        float t = 0f, dur = Mathf.Max(0.01f, previewFadeIn);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            previewAudioSource.volume = previewVolume * Mathf.Clamp01(t / dur);
            yield return null;
        }
        previewAudioSource.volume = previewVolume;
        previewRoutine = null;
    }

    private void TryEnter(bool toEditor)
    {
        SongData song = CurrentSong;
        if (song.ExistSongbar == 2) return;
        if (CoreManager.Instance == null) return;

        bool anyAvailable = SongRowUI.IsDifficultyAvailable(song.Difficulty_EASY)
                         || SongRowUI.IsDifficultyAvailable(song.Difficulty_HARD)
                         || SongRowUI.IsDifficultyAvailable(song.Difficulty_INSANE);
        if (!anyAvailable) return;
        if (!toEditor && !SongRowUI.IsDifficultyAvailable(GetDifficultyValue(song, currentDifficulty))) return;

        isLeaving = true;
        if (previewRoutine != null) StopCoroutine(previewRoutine);
        if (lengthRoutine != null) StopCoroutine(lengthRoutine);
        if (previewAudioSource != null) previewAudioSource.Stop();

        if (toEditor) CoreManager.Instance.LoadEditorScene(song, currentDifficulty);
        else CoreManager.Instance.LoadGameScene(song, isMvOn, currentDifficulty);
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}