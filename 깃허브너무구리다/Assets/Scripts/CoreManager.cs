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

    private string currentActiveScene = "";
    private bool isTransitioning = false; // 전환 중 중복 호출(연타) 방지

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadSettings();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void LoadSettings()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "data", "settings.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            CurrentSettings = JsonUtility.FromJson<SettingsData>(json);
            Debug.Log($"[Settings Loaded] Audio: {CurrentSettings.AudioOffset}, Judge: {CurrentSettings.JudgeOffset}, Mode: {CurrentSettings.Mode}");
        }
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
        StartCoroutine(TransitionScene("2_Game"));
    }

    public void LoadSongSelectScene()
    {
        StartCoroutine(TransitionScene("1_SongSelect"));
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