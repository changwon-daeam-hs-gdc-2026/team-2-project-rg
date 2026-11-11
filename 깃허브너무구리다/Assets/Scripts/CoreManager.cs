using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.IO;

public class CoreManager : MonoBehaviour
{
    public static CoreManager Instance;

    public SongData CurrentSongData;
    public bool IsMvOn = false;
    public int CurrentLevelId = 2; // 1: EASY, 2: HARD, 3: INSANE (기본값 HARD)
    public SettingsData CurrentSettings = new SettingsData();

    public bool IsAutoPlay = false;

    // 가장 최근에 끝난 한 판의 결과 (게임 씬 → 결과 씬으로 전달)
    public ResultData LastResult;

    [Header("곡 배경 (원본 → 흐리게 + 검은 막)")]
    [Tooltip("흐린 배경 위 검은 막 진하기 (0 = 없음 ~ 1 = 완전 검정)\n플레이 중에 바꾸면 다음에 그려지는 배경부터 적용 (곡을 바꾸거나 화면을 옮기면 보임)")]
    [Range(0f, 1f)] public float backgroundDim = 0.15f;
    [Tooltip("흐림 세기 (작은 그림 320px 기준 반지름, 0 = 흐리지 않음)")]
    [Range(0f, 20f)] public float backgroundBlurRadius = 6f;

    private string currentActiveScene = "";
    private bool isTransitioning = false; // 전환 중 중복 호출(연타) 방지

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ApplyBackgroundLook();
            AudioKeepAlive.Ensure();   // 블루투스 등 오디오 장치가 무음 중에 꺼져서 곡 앞부분이 잘리지 않게
            LoadSettings();
            SettingsStore.ApplyAll(); // 볼륨 / 프레임 제한 / 화면 모드 / FPS 표시
            Shader.WarmupAllShaders(); // 처음 쓰는 셰이더를 그 순간 컴파일하느라 멈칫하는 것 방지
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // 인스펙터의 배경 값 → BackgroundBlur (바뀌었으면 만들어 둔 흐린 그림을 버리고 다음부터 새 값으로)
    private void ApplyBackgroundLook()
    {
        BackgroundBlur.SetLook(backgroundDim, backgroundBlurRadius);
    }

    void OnValidate()
    {
        if (Application.isPlaying && Instance == this) ApplyBackgroundLook();
    }

    // settings.json(기본값·개발용) 위에 user_settings.json(설정창에서 바꾼 값)을 덮어써서 읽는다
    private void LoadSettings()
    {
        CurrentSettings = SettingsStore.LoadAll();
    }

    void OnApplicationQuit()
    {
        SettingsStore.SaveIfDirty();
    }

    void Start()
    {
        string startSceneName = "1_SongSelect";

        if (CurrentSettings.Mode == 0) startSceneName = "3_Editor";
        else if (CurrentSettings.Mode == 1) startSceneName = "1_SongSelect";
        else if (CurrentSettings.Mode == 2) startSceneName = "2_Game";

        currentActiveScene = startSceneName;
        SceneManager.LoadSceneAsync(startSceneName, LoadSceneMode.Additive);
    }

    // 난이도(levelId)를 받아옵니다.
    public void LoadGameScene(SongData songData, bool mvState, int levelId)
    {
        CurrentSongData = songData;
        IsMvOn = mvState;
        CurrentLevelId = levelId;

        // 곡 정보 화면(SongIntroOverlay)이 화면을 덮은 뒤 게임 씬을 불러온다. 게임 → 게임(RETRY)이면 짧게.
        if (isTransitioning || SongIntroOverlay.IsActive) return;
        bool retry = currentActiveScene == "2_Game";
        SongIntroOverlay.Show(songData, levelId, retry, () => StartCoroutine(TransitionScene("2_Game")));
    }

    public void LoadSongSelectScene()
    {
        if (GameSettingSession.Active) GameSettingSession.End(this);   // GAME SETTING 에서 나옴: 오토 · MV · 곡 선택 되돌림
        StartCoroutine(TransitionScene("1_SongSelect"));
    }

    // GAME SETTING (5_Test 씬): 실제 채보(곡 ID 2)로 노트 속도 · 판정 · 표시를 맞추는 곳
    //  곡 선택 → 게임과 같은 전환(곡 정보 화면). 단 제목 GAME SETTING · 난이도 회색 UNKNOWN · 'MUSIC  곡명'
    public void LoadTestScene()
    {
        if (isTransitioning || SongIntroOverlay.IsActive || CurtainTransition.IsActive) return;
        SongData song = GameSettingSession.LoadSong(GameSettingSession.SongId);
        if (song == null) { Debug.LogWarning("[GameSetting] songs.json 에 곡 ID " + GameSettingSession.SongId + " 이 없습니다."); return; }
        GameSettingSession.Begin(this);
        CurrentSongData = song;
        CurrentLevelId = GameSettingSession.FindLevel(song.Id);
        SongIntroOverlay.GameSettingMode = true;
        SongIntroOverlay.Show(song, CurrentLevelId, false, () => StartCoroutine(TransitionScene(GameSettingSession.SceneName)));
    }

    // 에디터 씬으로 넘어갈 때 곡 데이터와 선택된 난이도를 함께 받도록 수정됨
    public void LoadEditorScene(SongData songData, int levelId)
    {
        CurrentSongData = songData;
        CurrentLevelId = levelId;
        StartCoroutine(TransitionScene("3_Editor"));
    }

    // 게임이 끝나면 한 판의 결과를 들고 결과 씬(4_Result)으로 넘어간다.
    public void LoadResultScene(ResultData result)
    {
        LastResult = result;
        StartCoroutine(TransitionScene("4_Result"));
    }

    private IEnumerator TransitionScene(string loadSceneName)
    {
        if (isTransitioning) yield break;
        isTransitioning = true;

        SettingsWindow.Close();
        SettingsStore.SaveIfDirty();

        try
        {
            // 로드하기 '전에' 지울 씬을 잡아 둔다. (같은 이름의 씬이 둘 생겨도 옛 씬을 정확히 가리키도록)
            Scene sceneToUnload = SceneManager.GetSceneByName(currentActiveScene);

            AsyncOperation loadOp = SceneManager.LoadSceneAsync(loadSceneName, LoadSceneMode.Additive);
            while (!loadOp.isDone) yield return null;

            if (sceneToUnload.IsValid() && sceneToUnload.isLoaded)
            {
                AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(sceneToUnload);
                if (unloadOp != null)
                {
                    while (!unloadOp.isDone) yield return null;
                }
            }

            currentActiveScene = loadSceneName;

            // 여러 판을 돌릴 때 이전 판에서 만든 텍스처/스프라이트가 메모리에 쌓이지 않도록 정리
            yield return Resources.UnloadUnusedAssets();
        }
        finally
        {
            isTransitioning = false;
        }
    }
}