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
//  - Enter : 게임 시작 / G + Enter : 에디터   F : MV   A : 오토플레이   F1/F2 : 배속   ESC : 설정창   T : GAME SETTING   M : 게임 모드
//  - TAB : 기존 3난이도 목록 ↔ SPECIAL 목록 (SPECIAL 이 있는 곡만, SPECIAL 하나뿐인 곡은 SPECIAL 목록에만 나옴)
//          4난이도 곡은 ↑ ↓ 로도 SPECIAL 에 들어가고 나옴 (INSANE ↑ → SPECIAL, SPECIAL ↓ → INSANE)
//  - R : MODE RANDOM 켜고 끄기 (MODE 바 옆 RANDOM 버튼과 같음) — 켜져 있으면 시작할 때 모드 3개를 무작위로 고르는 연출 뒤 시작
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
    [Tooltip("(예전) 장면에 직접 만든 잠금 패널. 아래 'Use Code Locked Overlay' 가 켜져 있으면 쓰지 않고 숨김")]
    public RectTransform lockedStatusPanel;
    [Tooltip("왼쪽 큰 자켓 위에 코드로 만든 잠금 연출 (어두운 막 · 사선 줄무늬 · 자물쇠 배지 · LOCKED)")]
    public bool useCodeLockedOverlay = true;
    private LockedJacketOverlay lockedOverlay;

    [Header("우측 악곡 정렬표")]
    [Tooltip("행들이 들어갈 영역. 이 영역 밖은 잘려서 안 보인다 (RectMask2D 자동 추가)")]
    public RectTransform songListViewport;
    [Tooltip("비워 두면 코드로 기본 행을 만든다")]
    public SongRowUI songRowPrefab;
    [Tooltip("SPECIAL 목록 전용 Songbar 프리팹 (난이도 하나 — Special Text, 비우면 Hard Text). 비워 두면 코드로 기본 행을 만든다")]
    public SongRowUI specialRowPrefab;
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
    [Tooltip("songs.json 에 Length 가 없을 때만 Audio/Full/{id} 를 통째로 읽어 길이를 구한다 (무거움, 기본 꺼짐).\n" +
             "Length 는 에디터에서 채보를 저장하면 자동으로 기록된다.")]
    public bool loadLengthFromAudioFallback = false;

    [Header("곡 정보 화면 폰트 (비워 두면 기본 폰트)")]
    public SongIntroFonts introFonts = new SongIntroFonts();

    [Header("추가 설정 UI")]
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI mvStatusText;
    public Image mvIconImg;
    public Sprite mvOnSprite;
    public Sprite mvOffSprite;

    [Header("미리듣기 (Audio/Full/{id} 를 미리듣기 시작 시각부터 곡 끝까지 반복 재생, Audio/Preview 는 더 이상 쓰지 않음)")]
    public AudioSource previewAudioSource;
    [Range(0f, 1f)] public float previewVolume = 1f;
    [Tooltip("곡을 고른 뒤 이만큼 머무르면 재생 (빠르게 넘길 때 큰 음원을 매번 읽지 않도록)")]
    public float previewDelay = 0.25f;
    [Tooltip("시작할 때 소리가 커지는 시간(초)")]
    public float previewFadeIn = 0.8f;
    [Tooltip("끝날 때 소리가 작아지는 시간(초)")]
    public float previewFadeOut = 1.5f;
    [Tooltip("한 번 끝나고 다시 시작하기 전 쉬는 시간(초)")]
    public float previewLoopGap = 0.6f;
    [Tooltip("미리듣기 시작 정보가 없을 때: 곡 길이의 이 비율 지점부터")]
    [Range(0f, 0.9f)] public float previewFallbackStartRatio = 0.35f;

    // ===== 내부 상태 =====
    // songs.json 추가 항목 (선택): "PreviewStart": 54321 (ms, 음악 파일 기준) — 채보의 PREVIEW 줄보다 우선
    //  "PreviewEnd" 도 적으면 그 시각에서 끝냄 (없으면 곡 끝까지)
    [Serializable] private class SongExtra { public int Id; public string Length; public string BarColor; public double PreviewStart; public double PreviewEnd; }
    private readonly Dictionary<int, Vector2> previewOverride = new Dictionary<int, Vector2>(); // 초
    private readonly Dictionary<int, Vector2> previewFromChart = new Dictionary<int, Vector2>(); // 초 (x < 0 = 없음)
    private AudioClip previewClip;

    private readonly List<SongData> allSongs = new List<SongData>();       // songs.json 의 표시할 곡 전부
    private readonly List<SongData> normalSongs = new List<SongData>();    // 기존 3난이도 목록 (SPECIAL 하나뿐인 곡 제외)
    private readonly List<SongData> specialSongs = new List<SongData>();   // SPECIAL 목록 (SPECIAL 이 있는 곡만)
    private List<SongData> songList;                                       // 지금 보이는 목록 (둘 중 하나)
    private bool specialList;                                              // true = SPECIAL 목록 (난이도는 SPECIAL 고정)
    private int lastNormalDifficulty = 2;                                  // SPECIAL 목록에서 돌아올 때 되돌릴 난이도
    private readonly Dictionary<int, string> lengthOverride = new Dictionary<int, string>();
    private readonly Dictionary<int, float> lengthCache = new Dictionary<int, float>();
    private readonly Dictionary<int, Sprite> squareCache = new Dictionary<int, Sprite>();
    private readonly Dictionary<int, Sprite> blurCache = new Dictionary<int, Sprite>();
    private readonly Dictionary<int, Sprite> jacketCache = new Dictionary<int, Sprite>();
    private List<SongRowUI> rows = new List<SongRowUI>();                  // 지금 쓰는 줄 묶음 (아래 둘 중 하나)
    private readonly List<SongRowUI> normalRows = new List<SongRowUI>();
    private readonly List<SongRowUI> specialRows = new List<SongRowUI>();

    private readonly Sprite[] diffOn = new Sprite[5];      // [1] EASY [2] HARD [3] INSANE [4] SPECIAL
    private readonly Sprite[] diffOff = new Sprite[5];
    private readonly Color[] diffColors = new Color[5];
    private Color colorDisabled;
    private Sprite selectedBarSprite;
    private Sprite lockedSprite;

    private int currentVirtualIndex = 0;
    private float visualSongIndex = 0f;
    private int currentDifficulty = 2; // 1: EASY, 2: HARD, 3: INSANE, 4: SPECIAL (있는 곡만)
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
    // SPECIAL 전용 그림 ({곡ID}_4 — 없으면 기본 그림을 씀). SPECIAL 이 있는 곡만 읽음
    private readonly Dictionary<int, Sprite> spSquareCache = new Dictionary<int, Sprite>();
    private readonly Dictionary<int, Sprite> spBlurCache = new Dictionary<int, Sprite>();
    private readonly Dictionary<int, Sprite> spBarCache = new Dictionary<int, Sprite>();
    private bool IsSpecialNow { get { return specialList; } }
    private readonly Dictionary<int, Color> songColors = new Dictionary<int, Color>();
    private Coroutine diffRoutine, indicatorRoutine;
    private Vector2 diffBasePos;
    private bool diffPosInit = false;
    private int indicatorDiff = 0; // 헤더 표시가 현재 가리키는 난이도 (0 = 아직 없음)
    private RenderTexture jacketBlurRT;

    // ----- 좌측 하단 플레이 설정 패널 (난이도 선택 · 배속 · MV · AUTO PLAY, 마우스로도 조작) -----
    [Header("좌측 하단 플레이 설정 패널")]
    [Tooltip("자켓 아래에 난이도 선택바 · 배속 조정 · MV / AUTO PLAY 켜고 끄기를 표시")]
    public bool showDetailPanel = true;
    [Tooltip("패널 글꼴 (비우면 정렬표 글꼴 → 기본 글꼴)")]
    public TMP_FontAsset detailFont;
    [Header("Mode Window")]
    [Tooltip("모드 창 제목 \"MODE\" 글꼴 (비우면 기본 굵은 글꼴)")]
    public TMP_FontAsset modeTitleFont;
    [Tooltip("모드 창 제목 \"MODE\" 글자 크기")]
    public float modeTitleFontSize = 42f;
    private SongDetailPanel detailPanel;


    private void CreateDetailPanel()
    {
        if (!showDetailPanel || selectedJacketImg == null) return;
        // 좌측 정보 묶음 안에 넣어 곡이 바뀔 때 함께 움직이게. 묶음이 잘라내기(Mask)를 쓰면 캔버스 바로 아래에
        Transform parent = leftInfoGroup != null ? leftInfoGroup.transform : null;
        if (parent == null || parent.GetComponent<Mask>() != null || parent.GetComponent<RectMask2D>() != null)
            parent = selectedJacketImg.canvas != null ? selectedJacketImg.canvas.transform : selectedJacketImg.transform.parent;
        detailPanel = SongDetailPanel.Create(parent, detailFont != null ? detailFont : rowFont,
            selectedJacketImg.rectTransform,
            selectedSongTitleText != null ? selectedSongTitleText.rectTransform : null,
            selectedComposerText != null ? selectedComposerText.rectTransform : null,
            songLengthText != null ? songLengthText.rectTransform : null,
            jacketBlurImage != null ? jacketBlurImage.rectTransform : null);

        // 클릭 동작 = 키보드 조작과 같은 기능
        detailPanel.OnSelectDifficulty = SelectDifficulty;
        detailPanel.OnSpeedDelta = d =>
        {
            var st = SettingsStore.Current;
            st.SettingSpeed = Math.Round(Math.Min(12.0, Math.Max(1.0, st.SettingSpeed + d)), 1);
            SettingsStore.MarkDirty();
        };
        detailPanel.OnToggleMv = () => { if (CurrentSong.HasMV == 1) isMvOn = !isMvOn; };
        detailPanel.OnToggleAuto = ToggleAutoPlayMode;
        detailPanel.OnOpenModes = OpenModeWindow;
        detailPanel.OnToggleRandom = ToggleRandomMode;
    }

    // AUTO PLAY 는 게임 모드와 따로. 켜져 있는 동안은 모드를 고를 수 없고, 고른 모드도 적용되지 않음
    private void ToggleAutoPlayMode()
    {
        if (CoreManager.Instance == null) return;
        CoreManager.Instance.IsAutoPlay = !CoreManager.Instance.IsAutoPlay;
        Debug.Log(CoreManager.Instance.IsAutoPlay ? "AUTO PLAY 개시 (게임 모드 잠금)" : "AUTO PLAY 종료");
    }

    // R 키 / RANDOM 버튼: MODE RANDOM 켜고 끄기 (AUTO PLAY 중이면 MODE 바를 깜빡임)
    private void ToggleRandomMode()
    {
        if (GameModeWindow.BlocksInput || SettingsWindow.BlocksGameInput || isLeaving || GameModeRoulette.IsOpen) return;
        if (GameModes.Locked)
        {
            if (detailPanel != null) detailPanel.FlashModeFull();
            return;
        }
        GameModes.SetRandomPick(!GameModes.RandomPick);
    }

    // M 키 / MODE 바 클릭: 게임 모드 설정창 (AUTO PLAY 중이면 열지 않고 MODE 바를 깜빡임)
    private void OpenModeWindow()
    {
        if (GameModeWindow.BlocksInput || SettingsWindow.BlocksGameInput || isLeaving) return;
        if (GameModes.Locked)
        {
            if (detailPanel != null) detailPanel.FlashModeFull();
            return;
        }
        GameModeWindow.TitleFont = modeTitleFont;            // 제목 "MODE" 글꼴 · 크기 (인스펙터)
        GameModeWindow.TitleFontSize = modeTitleFontSize;
        GameModeWindow.Open(gameObject.scene, detailFont != null ? detailFont : rowFont);
    }

    // 난이도를 바로 고르기 (패널 클릭)
    private void SelectDifficulty(int level)
    {
        SongData song = CurrentSong;
        if (level == currentDifficulty || !SongRowUI.IsDifficultyAvailable(GetDifficultyValue(song, level))) return;
        // SPECIAL 칸 ↔ 나머지 칸: 목록 자체가 바뀜 (TAB 과 같음, 같은 곡 그대로)
        if (level == DifficultyHelper.Special) { if (!specialList) ToggleSpecialList(); return; }
        if (specialList) { lastNormalDifficulty = level; ToggleSpecialList(); return; }
        int dir = level > currentDifficulty ? 1 : -1;
        int prev = currentDifficulty;
        currentDifficulty = level;
        lastNormalDifficulty = level;
        UpdateDifficultyUI(song, dir);
        ApplyRowDifficultyColors();
        MoveHeaderIndicator(false);
        OnDifficultyChanged(prev, dir);
    }

    // SPECIAL 로 들어가거나 나올 때: 곡 정보(SPECIAL_Name 등) · 자켓 · 배경 · 선택 바 그림을 바꾸고 미리듣기를 다시 재생
    private void OnDifficultyChanged(int prev, int dir)
    {
        bool was = prev == DifficultyHelper.Special, now = IsSpecialNow;
        if (was == now) return;
        SongData song = CurrentSong;
        UpdateLeftInfo(song);
        UpdateBackground(song.Id, dir, false);
        RebindRows();
        PlayPreviewMusic();
    }

    // 보이는 곡 바들을 다시 그림 (선택된 곡의 선택 바 그림이 SPECIAL 전용으로 바뀌도록)
    private void RebindRows()
    {
        foreach (var r in rows)
            if (r.boundVirtualIndex != int.MinValue && r.gameObject.activeSelf) BindRow(r, r.boundVirtualIndex);
    }

    void LateUpdate()
    {
        if (detailPanel != null && songList != null && songList.Count > 0) detailPanel.Show(CurrentSong, currentDifficulty, isMvOn, CurrentSong.HasMV == 1);
        UpdateHeaderMode();
    }

    // =====================================================================
    //  SPECIAL 목록 (TAB)
    //   기존 목록 → SPECIAL: 지금 곡에 SPECIAL 이 있으면 그 곡, 없으면 곡 ID 가 가장 가까운 SPECIAL 곡
    //   SPECIAL → 기존 목록: 같은 곡 (SPECIAL 하나뿐인 곡이면 곡 ID 가 가장 가까운 곡), 난이도는 SPECIAL 로 오기 전 것
    // =====================================================================
    [Header("SPECIAL 목록")]
    [Tooltip("목록이 바뀔 때 곡 바들이 옆에서 들어오는 거리(px)")]
    public float listSwitchSlide = 48f;
    [Tooltip("목록이 바뀌는 연출 시간(초)")]
    public float listSwitchDuration = 0.28f;

    private float listSwitchT = 1f;                 // 0 → 1 : 목록이 바뀐 직후 연출
    private int listSwitchDir = 1;
    private CanvasGroup rowsGroup;

    private void ToggleSpecialList() { ToggleSpecialList(0); }

    // animDir: 좌측 난이도 판이 들어오는 방향 (0 이면 SPECIAL 로 갈 때 +1, 나올 때 -1)
    private void ToggleSpecialList(int animDir)
    {
        if (isLeaving || songList == null || songList.Count == 0) return;
        bool toSpecial = !specialList;
        List<SongData> target = toSpecial ? specialSongs : normalSongs;
        if (target.Count == 0) return;                                   // 옮겨 갈 곡이 없으면 그대로

        SongData cur = CurrentSong;
        int idx = target.FindIndex(x => x.Id == cur.Id);
        if (idx < 0) idx = NearestIndex(target, cur.Id);

        if (toSpecial) { if (currentDifficulty <= 3) lastNormalDifficulty = currentDifficulty; currentDifficulty = DifficultyHelper.Special; }
        else currentDifficulty = Mathf.Clamp(lastNormalDifficulty, 1, 3);

        SwitchList(toSpecial, idx);
        int dir = animDir != 0 ? animDir : (toSpecial ? 1 : -1);
        RefreshSelectedSong(dir, false);
        PlayDiffChangeAnim(dir);
    }

    // 목록 · 줄 묶음 바꾸기 (선택 곡 = idx)
    private void SwitchList(bool toSpecial, int idx)
    {
        foreach (var r in rows)
        {
            r.boundVirtualIndex = int.MinValue;
            r.SetSelected(false, 0f);
            if (r.gameObject.activeSelf) r.gameObject.SetActive(false);
        }
        bool changed = specialList != toSpecial;
        SetList(toSpecial);
        currentVirtualIndex = Mathf.Clamp(idx, 0, Mathf.Max(0, songList.Count - 1));
        visualSongIndex = currentVirtualIndex;                         // 스크롤하지 않고 바로 그 곡
        if (changed) { listSwitchT = 0f; listSwitchDir = toSpecial ? 1 : -1; }
    }

    private void SetList(bool special)
    {
        specialList = special;
        songList = special ? specialSongs : normalSongs;
        useLoop = loopList && songList.Count >= visibleRowCount;       // 곡 수가 줄 수보다 적으면 루프하지 않음
        rows = special ? specialRows : normalRows;
    }

    // 곡 ID 가 가장 가까운 곡 (같으면 ID 가 작은 쪽)
    private static int NearestIndex(List<SongData> list, int id)
    {
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            int d = Math.Abs(list[i].Id - id);
            if (d < bestD || (d == bestD && list[i].Id < list[best].Id)) { best = i; bestD = d; }
        }
        return best;
    }

    // ----- 우측 헤더: 기존 EZ · HD · INS + 평행사변형 ↔ SPECIAL 목록에서는 아무것도 없음 -----
    //  (예전의 SPECIAL 평행사변형은 지움 — SPECIAL 로 바뀌면 기존 헤더가 사라지기만 함)
    private CanvasGroup[] normalHeaderGroups;
    private float headerBlend = -1f;                // 0 = 기존, 1 = SPECIAL

    private void BuildSpecialHeader()
    {
        var groups = new List<CanvasGroup>();
        foreach (RectTransform r in new[] { headerIndicator, headerEasyLabel, headerHardLabel, headerInsaneLabel })
            if (r != null) groups.Add(GetOrAdd<CanvasGroup>(r.gameObject));
        normalHeaderGroups = groups.ToArray();
    }

    // 매 프레임: 헤더 전환 · 목록 전환 연출
    private void UpdateHeaderMode()
    {
        float target = specialList ? 1f : 0f;
        headerBlend = headerBlend < 0f ? target : Mathf.MoveTowards(headerBlend, target, UiTime.Delta / 0.22f);
        float e = headerBlend * headerBlend * (3f - 2f * headerBlend);
        if (normalHeaderGroups != null)
            foreach (var g in normalHeaderGroups) if (g != null) g.alpha = 1f - e;

        // 목록 전환: 새 곡 바들이 옆에서 미끄러져 들어오며 나타남 (위치는 UpdateRows 에서)
        if (listSwitchT < 1f) listSwitchT = Mathf.Min(1f, listSwitchT + UiTime.Delta / Mathf.Max(0.01f, listSwitchDuration));
        if (songListViewport != null)
        {
            if (rowsGroup == null) rowsGroup = GetOrAdd<CanvasGroup>(songListViewport.gameObject);
            rowsGroup.alpha = ListSwitchEase;
        }
    }

    private float ListSwitchEase { get { return 1f - Mathf.Pow(1f - Mathf.Clamp01(listSwitchT), 3f); } }

    private static void StretchFull(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    private SongData CurrentSong { get { return songList[DataIndex(currentVirtualIndex)]; } }
    private float PreviewTargetVolume { get { return SettingsWindow.IsCalibrating ? 0f : previewVolume * SettingsStore.MusicVolume; } }
    private float ViewportHeight { get { return clipPadding * 2f + visibleRowCount * rowHeight + Mathf.Max(0, visibleRowCount - 1) * rowGap; } }

    // =====================================================================
    //  초기화
    // =====================================================================
    void Start()
    {
        // 일시정지 → 곡 선택으로 나올 때 덮어 둔 곡 정보 화면(게임에 들어갈 때와 같은 전환)을 걷음
        if (SongIntroOverlay.IsActive) SongIntroOverlay.NotifyGameReady();
        if (CurtainTransition.IsActive) CurtainTransition.NotifyReady();     // 일시정지 → QUIT: 검은 막만 걷음

        ColorUtility.TryParseHtmlString("#5ce1e6", out diffColors[1]);
        ColorUtility.TryParseHtmlString("#ff751f", out diffColors[2]);
        ColorUtility.TryParseHtmlString("#ff3131", out diffColors[3]);
        diffColors[4] = DifficultyHelper.GetColor(DifficultyHelper.Special);
        ColorUtility.TryParseHtmlString("#545454", out colorDisabled);

        visibleRowCount = Mathf.Max(1, visibleRowCount);
        // 곡 정보 화면(SongIntroOverlay): 곡명 기본 폰트는 곡 선택창과 같게, 개별 폰트는 인스펙터에서
        if (selectedSongTitleText != null) SongIntroOverlay.PreferredTitleFont = selectedSongTitleText.font;
        SongIntroOverlay.Fonts = introFonts;
        selectedSlot = Mathf.Clamp(selectedSlot, 0, visibleRowCount - 1);

        LoadSongList();
        LoadSprites();
        // 두 목록: 기존 3난이도 목록 (SPECIAL 하나뿐인 곡 제외) / SPECIAL 목록 (SPECIAL 이 있는 곡)
        foreach (var x in allSongs)
        {
            if (!DifficultyHelper.IsSpecialOnly(x)) normalSongs.Add(x);
            if (DifficultyHelper.HasSpecial(x)) specialSongs.Add(x);
        }
        SetList(normalSongs.Count == 0);                                  // 기존 목록이 비었으면 SPECIAL 목록으로 시작
        if (songList.Count == 0) { Debug.LogWarning("[SongSelect] 표시할 곡이 없습니다."); return; }
        if (specialList) currentDifficulty = DifficultyHelper.Special;

        foreach (var s in allSongs)
        {
            // 곡 ID 그림이 없으면 같은 폴더의 None.png (SongArt)
            squareCache[s.Id] = SongArt.Load("Sprites/squareimg", s.Id);
            blurCache[s.Id] = SongArt.Load("Sprites/blurimg", s.Id);
            jacketCache[s.Id] = squareCache[s.Id];
            selectedBarCache[s.Id] = SongArt.Load(selectedBarFolder, s.Id);
            if (DifficultyHelper.HasSpecial(s))
            {
                spSquareCache[s.Id] = Resources.Load<Sprite>($"Sprites/squareimg/{s.Id}_4");
                spBlurCache[s.Id] = Resources.Load<Sprite>($"Sprites/blurimg/{s.Id}_4");
                spBarCache[s.Id] = Resources.Load<Sprite>($"{selectedBarFolder}/{s.Id}_4");
            }
        }

        RestoreLastSelection();
        SetupViewport();
        BuildRows();
        SetList(specialList);                                             // 줄 묶음도 고른 목록에 맞춤
        BuildSpecialHeader();
        CreateDetailPanel();

        visualSongIndex = currentVirtualIndex;
        isCurrentlyLocked = DifficultyHelper.IsPlayLocked(CurrentSong, currentDifficulty);
        if (useCodeLockedOverlay && selectedJacketImg != null)
        {
            lockedOverlay = LockedJacketOverlay.Create(selectedJacketImg.rectTransform, detailFont != null ? detailFont : rowFont);
            if (lockedOverlay != null) lockedOverlay.Show(isCurrentlyLocked, true);
            if (lockedStatusPanel != null) lockedStatusPanel.gameObject.SetActive(false);   // 예전 패널은 숨김
        }
        else if (lockedStatusPanel != null)
        {
            lockedStatusPanel.gameObject.SetActive(true);
            CanvasGroup cg = GetOrAdd<CanvasGroup>(lockedStatusPanel.gameObject);
            cg.alpha = isCurrentlyLocked ? 1f : 0f;
        }

        RefreshSelectedSong(0, true);
        UpdateRows();
        StartCoroutine(PrewarmRoutine());
    }

    // 처음 조작할 때 멈칫하지 않도록, 곡 선택창이 뜬 직후 무거운 첫 작업들을 미리 해 둔다
    private IEnumerator PrewarmRoutine()
    {
        yield return null;
        yield return null;
        // 설정창 글꼴 (한글): 인스펙터 Settings Font / Settings Bold Font. 비우면 확인 창 글꼴(Dialog Font → 곡 제목 글꼴)
        SettingsWindow.SetFonts(settingsFont != null ? settingsFont : DialogFont, settingsBoldFont);
        SettingsWindow.Prewarm(); // 설정창 UI 미리 생성

        // 글꼴: 자주 쓰는 영문·숫자·기호를 미리 글꼴 아틀라스에 올려 둔다 (첫 표시 때 글자 굽기 지연 방지)
        const string common = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?%+-/()[]&'\"#*_<>=~";
        var fonts = new HashSet<TMP_FontAsset>();
        foreach (var t in GetComponentsInChildren<TextMeshProUGUI>(true)) if (t.font != null) fonts.Add(t.font);
        foreach (var t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (t.font != null) fonts.Add(t.font);
        if (rowFont != null) fonts.Add(rowFont);
        SettingsUIKit.EnsureFont();
        if (SettingsUIKit.Font != null) fonts.Add(SettingsUIKit.Font);
        foreach (var f in fonts)
        {
            if (f.atlasPopulationMode != AtlasPopulationMode.Dynamic) continue;
            string all = common;
            foreach (var s in allSongs) all += (s.Name ?? "") + (s.SubName ?? "") + (s.Composer ?? "")
                                             + (s.SPECIAL_Name ?? "") + (s.SPECIAL_SubName ?? "") + (s.Difficulty_SPECIAL ?? ""); // 곡명·작곡가 · SPECIAL 글자도 미리
            f.TryAddCharacters(all);
            yield return null;
        }
    }

    private void LoadSongList()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "data", "songs.json");
        if (!File.Exists(path)) { Debug.LogWarning("[SongSelect] songs.json 이 없습니다: " + path); return; }

        string json = SongJson.Normalize(File.ReadAllText(path, System.Text.Encoding.UTF8)); // "BPM": 132 → "132"
        SongData[] arr = JsonHelper.FromJson<SongData>(json);
        if (arr != null) allSongs.AddRange(arr.Where(s => s.ExistSongbar == 1 || s.ExistSongbar == 2));

        // (선택) songs.json 의 "Length": "3:39" 또는 219.5 (초) — DataModels 를 고치지 않고 따로 읽는다
        try
        {
            SongExtra[] extras = JsonHelper.FromJson<SongExtra>(json);
            if (extras != null)
            {
                foreach (var e in extras)
                {
                    if (!string.IsNullOrWhiteSpace(e.Length)) lengthOverride[e.Id] = e.Length.Trim();
                    if (e.PreviewStart > 0 || e.PreviewEnd > 0)
                        previewOverride[e.Id] = new Vector2((float)(Math.Max(0.0, e.PreviewStart) / 1000.0), e.PreviewEnd > e.PreviewStart ? (float)(e.PreviewEnd / 1000.0) : -1f);

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

        string[] names = { "", "EASY", "HARD", "INSANE", "SPECIAL" };
        for (int d = 1; d <= 4; d++)
        {
            diffOn[d] = LoadDifficultySprite(names[d], "ON", sheet);
            diffOff[d] = LoadDifficultySprite(names[d], "OFF", sheet);
            if ((diffOn[d] == null || diffOff[d] == null) && d <= 3)   // SPECIAL 그림은 없어도 됨 (왼쪽 패널이 글자로 표시)
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

        // SPECIAL 로 플레이했으면 SPECIAL 목록으로 돌아옴
        int lv = Mathf.Clamp(core.CurrentLevelId, 1, DifficultyHelper.Special);
        int id = core.CurrentSongData.Id;
        if (lv == DifficultyHelper.Special && specialSongs.Count > 0) SetList(true);
        else if (lv != DifficultyHelper.Special && normalSongs.Count > 0) SetList(false);

        int idx = songList.FindIndex(s => s.Id == id);
        if (idx < 0) idx = NearestIndex(songList, id);
        currentVirtualIndex = idx;
        if (specialList) currentDifficulty = DifficultyHelper.Special;
        else { currentDifficulty = lv == DifficultyHelper.Special ? 3 : lv; lastNormalDifficulty = currentDifficulty; }
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
        //  기존 목록용 / SPECIAL 목록용 (SPECIAL 전용 Songbar) 두 묶음
        int poolSize = visibleRowCount + 4;
        for (int i = 0; i < poolSize; i++)
        {
            SongRowUI row = songRowPrefab != null
                ? Instantiate(songRowPrefab, songListViewport, false)
                : SongRowUI.CreateDefault(songListViewport, rowFont, rowHeight);
            row.name = $"SongRow_{i}";
            row.isSpecialRow = false;
            row.InitLayout(rowHeight, selectedBarSprite);
            row.gameObject.SetActive(false);
            normalRows.Add(row);
        }
        if (specialSongs.Count == 0) return;
        for (int i = 0; i < poolSize; i++)
        {
            SongRowUI row = specialRowPrefab != null
                ? Instantiate(specialRowPrefab, songListViewport, false)
                : SongRowUI.CreateDefault(songListViewport, rowFont, rowHeight, true);
            row.name = $"SpecialRow_{i}";
            row.isSpecialRow = true;
            row.InitLayout(rowHeight, selectedBarSprite);
            row.gameObject.SetActive(false);
            specialRows.Add(row);
        }
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    private int blurVersion = -1;

    void Update()
    {
        if (songList == null || songList.Count == 0 || isLeaving) return;
        // CoreManager 인스펙터에서 배경 검은 막 · 흐림 값을 바꾸면 지금 배경을 바로 새 값으로 (조정하면서 보기)
        UpdateLockedPanel(CurrentSong);   // 난이도를 바꿔도 잠금 표시가 따라가게 (값이 바뀔 때만 연출)
        if (blurVersion != BackgroundBlur.Version)
        {
            bool firstCheck = blurVersion < 0;
            blurVersion = BackgroundBlur.Version;
            if (!firstCheck && CurrentSong != null) UpdateBackground(CurrentSong.Id, 0, true);
        }
        var kb = Keyboard.current;
        if (kb == null) return;

        float dt = Time.unscaledDeltaTime;

        // 설정창이 열려 있으면(또는 이번 프레임에 닫혔으면) 곡 선택 조작은 막고 화면 연출만 계속한다
        // 설정창 또는 플레이 확인 창(경고 · 알림)이 열려 있으면 곡 선택 조작을 막음
        bool inputBlocked = SettingsWindow.BlocksGameInput || PlayConfirmDialog.IsOpen || GameModeWindow.BlocksInput   // 모드 설정창이 열려 있어도 막음
                         || GameModeRoulette.IsOpen;                                                                // MODE RANDOM 연출 중에도
        if (!inputBlocked && kb.escapeKey.wasPressedThisFrame)
        {
            SettingsWindow.Open();
            inputBlocked = true;
        }

        if (!inputBlocked)
        {
            int move = ReadHorizontalRepeat(kb, dt);
            if (move != 0) MoveSelection(move);

            if (kb.upArrowKey.wasPressedThisFrame) ChangeDifficulty(1);
            if (kb.downArrowKey.wasPressedThisFrame) ChangeDifficulty(-1);

            // TAB : 기존 3난이도 목록 ↔ SPECIAL 목록
            if (kb.tabKey.wasPressedThisFrame) ToggleSpecialList();

            if (kb.aKey.wasPressedThisFrame && CoreManager.Instance != null) ToggleAutoPlayMode();

            // R : MODE RANDOM 켜고 끄기
            if (kb.rKey.wasPressedThisFrame) ToggleRandomMode();

            // M : 게임 모드 설정창 (MIRROR · RANDOM · HARD … 최대 3개)
            if (kb.mKey.wasPressedThisFrame) { OpenModeWindow(); return; }

            // T : GAME SETTING (실제 채보로 노트 속도 · 판정 · 표시 맞추기, 5_Test 씬)
            if (kb.tKey.wasPressedThisFrame && CoreManager.Instance != null) { OpenGameSetting(); return; }

            var st = SettingsStore.Current;
            if (kb.f1Key.wasPressedThisFrame) { st.SettingSpeed = Math.Round(Math.Max(1.0, st.SettingSpeed - 0.1), 1); SettingsStore.MarkDirty(); }
            if (kb.f2Key.wasPressedThisFrame) { st.SettingSpeed = Math.Round(Math.Min(12.0, st.SettingSpeed + 0.1), 1); SettingsStore.MarkDirty(); }
        }
        else
        {
            repeatDir = 0;
        }

        SongData song = CurrentSong;
        bool hasMv = song.HasMV == 1;
        if (!inputBlocked && hasMv && kb.fKey.wasPressedThisFrame) isMvOn = !isMvOn;
        else if (!hasMv) isMvOn = false;

        // 미리듣기 볼륨: 설정의 MUSIC VOLUME 을 따라가고, 오프셋 측정 중에는 끈다
        if (previewAudioSource != null && previewRoutine == null && previewAudioSource.isPlaying)
            previewAudioSource.volume = Mathf.MoveTowards(previewAudioSource.volume, PreviewTargetVolume, dt * 3f);

        // 부드러운 스크롤 (프레임레이트와 무관한 지수 감쇠)
        float k = 1f - Mathf.Exp(-scrollLerpSpeed * dt);
        visualSongIndex = Mathf.Lerp(visualSongIndex, currentVirtualIndex, k);
        if (Mathf.Abs(visualSongIndex - currentVirtualIndex) < 0.001f) visualSongIndex = currentVirtualIndex;

        UpdateRows();
        foreach (var r in rows) r.Tick(dt);

        UpdateMvIcon(hasMv);
        if (speedText != null)
            speedText.text = SettingsStore.Current.SettingSpeed.ToString("0.0", CultureInfo.InvariantCulture);
        if (mvStatusText != null) mvStatusText.text = hasMv ? (isMvOn ? "ON" : "OFF") : "None";

        if (!inputBlocked && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
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
        if (!specialList) lastNormalDifficulty = currentDifficulty;
        UpdateLeftInfo(song);
        UpdateDifficultyUI(song, 0);
        ApplyRowDifficultyColors();
        MoveHeaderIndicator(instant); // 곡에 없는 난이도라 자동으로 바뀐 경우에도 이동
        UpdateBackground(song.Id, dir, instant);
        RebindRows();
        UpdateLockedPanel(song);
        UpdateSongLength(song);
        if (!instant) PlayLeftInfoAnim(dir);
        PlayPreviewMusic();
    }

    private void ChangeDifficulty(int direction)
    {
        SongData song = CurrentSong;
        // 4난이도 곡: ↑ ↓ 로 SPECIAL 까지 돎 (SPECIAL 에 들어가면 목록도 SPECIAL 목록으로, 나오면 기존 목록으로)
        bool four = DifficultyHelper.HasSpecial(song) && !DifficultyHelper.IsSpecialOnly(song);
        if (specialList && !four) return;                // SPECIAL 하나뿐인 곡: 바꿀 난이도가 없음 (기존 목록은 TAB)
        int prev = currentDifficulty;
        int next = currentDifficulty;
        int max = four ? DifficultyHelper.Special : 3;
        for (int i = 0; i < max; i++)
        {
            next += direction;
            if (next > max) next = 1;
            if (next < 1) next = max;
            if (SongRowUI.IsDifficultyAvailable(GetDifficultyValue(song, next)))
            {
                currentDifficulty = next;
                break;
            }
        }
        if (currentDifficulty == prev) return; // 고를 수 있는 난이도가 하나뿐이면 아무 연출 없음
        if (currentDifficulty == DifficultyHelper.Special || specialList)
        {
            // SPECIAL 로 들어가거나 SPECIAL 에서 나옴 → 같은 곡 그대로 목록 전환
            int to = currentDifficulty;
            currentDifficulty = prev;
            if (to != DifficultyHelper.Special) lastNormalDifficulty = to;
            ToggleSpecialList(direction);
            return;
        }

        lastNormalDifficulty = currentDifficulty;
        UpdateDifficultyUI(song, direction);
        ApplyRowDifficultyColors();
        MoveHeaderIndicator(false);
        OnDifficultyChanged(prev, direction);
    }

    // 현재 난이도가 이 곡에 없으면 HARD → EASY → INSANE 순으로 대체 (SPECIAL 이 없는 곡으로 넘어가면 INSANE 부터)
    private void ValidateCurrentDifficulty(SongData song)
    {
        if (specialList) { currentDifficulty = DifficultyHelper.Special; return; }   // SPECIAL 목록 = SPECIAL 고정
        if (currentDifficulty == DifficultyHelper.Special) currentDifficulty = 3;
        if (SongRowUI.IsDifficultyAvailable(GetDifficultyValue(song, currentDifficulty))) return;
        if (currentDifficulty == DifficultyHelper.Special && SongRowUI.IsDifficultyAvailable(song.Difficulty_INSANE)) { currentDifficulty = 3; return; }
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
        if (diff == 4) return song.Difficulty_SPECIAL;
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
            // 목록이 막 바뀌었으면 옆에서 미끄러져 들어옴 (아래 줄일수록 살짝 늦게)
            float slide = 0f;
            if (listSwitchT < 1f)
            {
                float lt = Mathf.Clamp01(listSwitchT * 1.3f - Mathf.Clamp(slot, 0f, visibleRowCount) * 0.035f);
                slide = listSwitchDir * listSwitchSlide * Mathf.Pow(1f - lt, 3f);
            }
            row.rect.anchoredPosition = new Vector2(slide, -(clipPadding + rowHeight * 0.5f + slot * spacing));
            row.SetSelected(v == currentVirtualIndex, selectedBarDrawDuration);
        }
    }

    private void BindRow(SongRowUI row, int virtualIndex)
    {
        SongData s = songList[DataIndex(virtualIndex)];
        squareCache.TryGetValue(s.Id, out Sprite sq);
        selectedBarCache.TryGetValue(s.Id, out Sprite bar);
        if (specialList)
        {
            // SPECIAL 목록: 곡명은 SPECIAL_Name 등, 자켓 · 선택 바는 SPECIAL 전용 그림 ({곡ID}_4) 이 있으면 그것
            if (spSquareCache.TryGetValue(s.Id, out Sprite spSq) && spSq != null) sq = spSq;
            if (spBarCache.TryGetValue(s.Id, out Sprite spBar) && spBar != null) bar = spBar;
            s = s.ForLevel(DifficultyHelper.Special);
        }
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
    private void UpdateLeftInfo(SongData baseSong)
    {
        // SPECIAL 이면 SPECIAL_Name · SPECIAL_Vocal 과 전용 자켓 ({곡ID}_4)
        SongData song = baseSong.ForLevel(currentDifficulty);
        // 좌측: "Name" 만 표시 (SubName 제외). 우측 정렬표는 SongRowUI 에서 Name + SubName 표시
        if (selectedSongTitleText != null) selectedSongTitleText.text = song.Name ?? "";
        if (selectedComposerText != null) selectedComposerText.text = BuildComposerLine(song);

        if (selectedJacketImg != null)
        {
            Sprite jacket = null;
            if (IsSpecialNow) spSquareCache.TryGetValue(song.Id, out jacket);
            if (jacket == null) jacketCache.TryGetValue(song.Id, out jacket);
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
            t += UiTime.Delta;
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
        if (specialList) return;                     // SPECIAL 목록: 기존 표시는 숨겨 둠 (돌아올 때 그 난이도로 이동)
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
            t += UiTime.Delta;
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
            t += UiTime.Delta;
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
        if (loadLengthFromAudioFallback) lengthRoutine = StartCoroutine(LoadLengthRoutine(song.Id));
    }

    private IEnumerator LoadLengthRoutine(int songId)
    {
        yield return new WaitForSecondsRealtime(0.25f); // 빠르게 넘길 때는 읽지 않음
        if (CurrentSong.Id != songId) yield break;

        ResourceRequest req = Resources.LoadAsync<AudioClip>($"Audio/Full/{songId}");
        yield return req;

        AudioClip clip = req.asset as AudioClip;
        lengthCache[songId] = clip != null ? clip.length : -1f;
        if (clip != null && clip != previewClip) Resources.UnloadAsset(clip); // 미리듣기에 쓰는 중이면 그대로

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
        Sprite s = null;
        if (IsSpecialNow) spBlurCache.TryGetValue(songId, out s);        // SPECIAL 전용 배경 ({곡ID}_4)
        if (s == null) blurCache.TryGetValue(songId, out s);
        if (s == null) return; // 이미지가 없으면 기존 배경 유지
        s = BackgroundBlur.Get(s);  // 원본 배경 → 흐리게 (처음 볼 때 한 번 만들고 기억)

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
            t += UiTime.Delta;
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
    //  곡 전체 잠금(ExistSongbar 2) 또는 지금 고른 난이도가 잠김(LockedValue) → 왼쪽 자켓 잠금 표시
    private void UpdateLockedPanel(SongData song)
    {
        bool locked = DifficultyHelper.IsPlayLocked(song, currentDifficulty);
        if (locked == isCurrentlyLocked) return;
        isCurrentlyLocked = locked;

        if (lockedOverlay != null) { lockedOverlay.Show(locked, false); return; }
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
            t += UiTime.Delta;
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
        previewRoutine = StartCoroutine(LoadAndPlayPreview(CurrentSong.Id, IsSpecialNow));
    }

    // =====================================================================
    //  미리듣기: 전체 음원(Audio/Full/{id})을 미리듣기 시작 시각부터 곡 끝까지 반복 재생 (osu! 와 같음)
    //   시작 시각 우선순위: songs.json PreviewStart → 채보 파일의 PREVIEW 줄(변환기가 osu! PreviewTime 으로 기록)
    //                      → 곡 길이의 35% 지점
    //   시작은 부드럽게 커지고(fade in) 끝은 부드럽게 작아진 뒤(fade out) 잠깐 쉬고 다시 시작한다.
    // =====================================================================
    //  SPECIAL: 전용 음원(Audio/Full/{id}_4)이 있으면 그것, 미리듣기 구간도 SPECIAL 채보의 PREVIEW 줄을 먼저 봄
    private IEnumerator LoadAndPlayPreview(int songId, bool special)
    {
        yield return new WaitForSecondsRealtime(previewDelay);
        if (CurrentSong.Id != songId) yield break;

        ResourceRequest req = null;
        if (special)
        {
            req = Resources.LoadAsync<AudioClip>($"Audio/Full/{songId}_{DifficultyHelper.Special}");
            yield return req;
            if (req.asset == null) req = null;
        }
        if (req == null)
        {
            req = Resources.LoadAsync<AudioClip>($"Audio/Full/{songId}");
            yield return req;
        }
        AudioClip clip = req.asset as AudioClip;
        if (clip == null) { Debug.LogWarning($"[SongSelect] Resources/Audio/Full/{songId} 이 없습니다."); previewRoutine = null; yield break; }
        if (CurrentSong.Id != songId) { ReleaseUnusedClip(clip); yield break; }

        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading)
        {
            if (CurrentSong.Id != songId) { ReleaseUnusedClip(clip); yield break; }
            yield return null;
        }

        SetPreviewClip(clip);
        if (!special && !lengthCache.ContainsKey(songId)) lengthCache[songId] = clip.length;

        float start, end;
        GetPreviewRange(songId, special, clip.length, out start, out end);

        while (true)
        {
            previewAudioSource.loop = false;
            previewAudioSource.time = start;
            previewAudioSource.volume = 0f;
            previewAudioSource.Play();

            float len = end - start;
            float fadeIn = Mathf.Min(previewFadeIn, len * 0.3f);
            float fadeOut = Mathf.Min(previewFadeOut, len * 0.4f);

            // 앞부분: 서서히 커짐
            float t = 0f;
            while (t < fadeIn && previewAudioSource.isPlaying)
            {
                t += UiTime.Delta;
                previewAudioSource.volume = PreviewTargetVolume * Smooth(t / Mathf.Max(0.01f, fadeIn));
                yield return null;
            }

            // 가운데: 그대로 (설정 볼륨 변화는 바로 반영)
            while (previewAudioSource.isPlaying && previewAudioSource.time < end - fadeOut)
            {
                previewAudioSource.volume = PreviewTargetVolume;
                yield return null;
            }

            // 뒷부분: 서서히 작아짐
            t = 0f;
            while (t < fadeOut && previewAudioSource.isPlaying)
            {
                t += UiTime.Delta;
                previewAudioSource.volume = PreviewTargetVolume * (1f - Smooth(t / Mathf.Max(0.01f, fadeOut)));
                yield return null;
            }

            previewAudioSource.Stop();
            yield return new WaitForSecondsRealtime(previewLoopGap);
            if (CurrentSong.Id != songId) { previewRoutine = null; yield break; }
        }
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    // 이전 곡의 전체 음원은 메모리에서 내린다 (전체 음원은 미리듣기 파일보다 훨씬 크다)
    private void SetPreviewClip(AudioClip clip)
    {
        if (previewClip != null && previewClip != clip)
        {
            previewAudioSource.Stop();
            previewAudioSource.clip = null;
            Resources.UnloadAsset(previewClip);
        }
        previewClip = clip;
        previewAudioSource.clip = clip;
    }

    private void ReleaseUnusedClip(AudioClip clip)
    {
        if (clip != null && clip != previewClip) Resources.UnloadAsset(clip);
    }

    // 미리듣기 구간(초)
    private void GetPreviewRange(int songId, bool special, float clipLength, out float start, out float end)
    {
        Vector2 r;
        if (!previewOverride.TryGetValue(songId, out r))
        {
            int key = special ? -songId - 1 : songId;          // SPECIAL 은 따로 기억
            if (!previewFromChart.TryGetValue(key, out r)) { r = ReadPreviewFromCharts(songId, special); previewFromChart[key] = r; }
        }

        start = (r.x >= 0f && r.x < clipLength - 2f) ? r.x : clipLength * previewFallbackStartRatio;
        end = (r.y > start) ? Mathf.Min(r.y, clipLength) : clipLength;   // 끝 시각이 없으면 곡 끝까지
    }

    // StreamingAssets/chart/{id}_{난이도}.txt 의 "PREVIEW, 시작ms" (끝ms 는 선택) — 현재 난이도 → INSANE → HARD → EASY 순으로 찾음
    private Vector2 ReadPreviewFromCharts(int songId, bool special)
    {
        // SPECIAL: SPECIAL 채보 → INSANE → HARD → EASY,  그 밖: 지금 난이도 → INSANE → HARD → EASY
        int[] order = special ? new[] { DifficultyHelper.Special, 3, 2, 1 } : new[] { Mathf.Min(currentDifficulty, 3), 3, 2, 1 };
        foreach (int lv in order)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "chart", $"{songId}_{lv}.txt");
            if (!File.Exists(path)) continue;
            try
            {
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("PREVIEW", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] d = line.Split(',');
                    if (d.Length < 2 || d[0].Trim().ToUpperInvariant() != "PREVIEW") continue;
                    float a, b = -1f;
                    if (!float.TryParse(d[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a)) continue;
                    if (d.Length >= 3) float.TryParse(d[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out b);
                    return new Vector2(a / 1000f, b > a ? b / 1000f : -1f);
                }
            }
            catch (Exception) { }
        }
        return new Vector2(-1f, -1f);
    }

    // 잠긴 곡에서 ENTER: 지금 곡 줄의 자물쇠를 흔듦
    private void PulseLockedRow()
    {
        foreach (var r in rows)
            if (r != null && r.boundVirtualIndex == currentVirtualIndex) r.PulseLock();
        if (lockedOverlay != null) lockedOverlay.Shake();
    }

    private void TryEnter(bool toEditor)
    {
        SongData song = CurrentSong;
        // 잠긴 곡 (ExistSongbar 2) · 잠긴 난이도 (LockedValue): 플레이 불가, 편집(G + ENTER)만 가능
        if (DifficultyHelper.IsPlayLocked(song, currentDifficulty) && !toEditor) { PulseLockedRow(); return; }
        if (CoreManager.Instance == null) return;

        bool anyAvailable = SongRowUI.IsDifficultyAvailable(song.Difficulty_EASY)
                         || SongRowUI.IsDifficultyAvailable(song.Difficulty_HARD)
                         || SongRowUI.IsDifficultyAvailable(song.Difficulty_INSANE)
                         || SongRowUI.IsDifficultyAvailable(song.Difficulty_SPECIAL);
        if (!anyAvailable) return;
        if (!toEditor && !SongRowUI.IsDifficultyAvailable(GetDifficultyValue(song, currentDifficulty))) return;

        if (toEditor) { BeginLeave(); CoreManager.Instance.LoadEditorScene(song.ForLevel(currentDifficulty), currentDifficulty); return; }

        // 플레이 전 확인 창
        //  · 경고창: MV 를 켠 채로, songs.json 에 "MvWarning" 이 있는 곡   → [돌아가기] [MV를 끈 채로 진행하기] [그대로 진행하기]
        //  없으면 바로 시작
        var pages = new List<PlayConfirmDialog.Page>();
        string label = SongLabel(song.ForLevel(currentDifficulty));
        if (isMvOn && song.HasMV == 1 && !string.IsNullOrWhiteSpace(song.MvWarning))
            pages.Add(PlayConfirmDialog.WarningPage(label, song.MvWarning));

        if (pages.Count == 0) { StartGame(song, isMvOn); return; }
        int level = currentDifficulty;
        PlayConfirmDialog.Show(pages, DialogFont, (proceed, mvOff) =>
        {
            if (!proceed || isLeaving) return;            // 돌아가기 → 곡 선택 그대로
            if (mvOff) isMvOn = false;                    // MV 를 끈 채로 진행
            currentDifficulty = level;
            StartGame(song, isMvOn);
        }, gameObject);
    }

    private void BeginLeave()
    {
        isLeaving = true;
        if (previewRoutine != null) StopCoroutine(previewRoutine);
        if (lengthRoutine != null) StopCoroutine(lengthRoutine);
        if (previewAudioSource != null) previewAudioSource.Stop();
    }

    // GAME SETTING (5_Test) 열기 — 미리듣기를 멈추고 게임에 들어갈 때와 같은 전환
    public void OpenGameSetting()
    {
        if (isLeaving || CoreManager.Instance == null) return;
        if (GameSettingSession.LoadSong(GameSettingSession.SongId) == null)
        {
            Debug.LogWarning("[GameSetting] songs.json 에 곡 ID " + GameSettingSession.SongId + " 이 없어 열 수 없습니다.");
            return;
        }
        BeginLeave();
        CoreManager.Instance.LoadTestScene();
    }

    // MODE RANDOM 이 켜져 있으면: 지난 조합을 버리고 시작 → 게임 화면에서 레인이 다 펼쳐진 뒤 새로 고르는 연출
    private void StartGame(SongData song, bool mv)
    {
        if (GameModes.RandomPick && !GameModes.Locked) GameModes.ClearRandomResult();
        LaunchGame(song, mv);
    }

    private void LaunchGame(SongData song, bool mv)
    {
        BeginLeave();
        CoreManager.Instance.LoadGameScene(song.ForLevel(currentDifficulty), mv, currentDifficulty);   // SPECIAL 이면 SPECIAL_Name 등으로 바뀐 곡 정보
    }

    // 확인 창 가운데 상자: "작곡가 - 곡 이름 (부제)"
    private static string SongLabel(SongData song)
    {
        string name = song.Name ?? "";
        if (!string.IsNullOrWhiteSpace(song.SubName)) name += " " + song.SubName.Trim();
        return string.IsNullOrWhiteSpace(song.Composer) ? name : song.Composer.Trim() + " - " + name;
    }

    [Tooltip("설정창 일반 글자 글꼴 (한글이 들어 있는 TMP 글꼴). 비우면 Dialog Font 와 같음")]
    public TMP_FontAsset settingsFont;
    [Tooltip("설정창 강조 글자(제목 · 항목 이름 · 버튼 · 탭) 글꼴. 비우면 일반 글꼴을 굵게")]
    public TMP_FontAsset settingsBoldFont;

    // 확인 창 글꼴: 인스펙터 Dialog Font → 곡 제목 글꼴 순 (한글 · 일본어가 들어 있는 글꼴이어야 함)
    [Tooltip("플레이 확인 창(경고 · 알림) 글꼴. 비우면 곡 제목 글꼴")]
    public TMP_FontAsset dialogFont;
    private TMP_FontAsset DialogFont
    {
        get
        {
            if (dialogFont != null) return dialogFont;
            return selectedSongTitleText != null ? selectedSongTitleText.font : rowFont;
        }
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}