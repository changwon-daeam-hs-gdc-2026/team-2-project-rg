#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 메뉴 [Tools > Create Result Scene (4_Result)] 를 누르면
// Assets/Scenes/4_Result.unity 를 만들고 ResultManager 의 모든 필드를 자동으로 연결한다.
// (이 파일은 반드시 Assets/Editor 폴더 안에 두세요.)
public static class ResultSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/4_Result.unity";

    [MenuItem("Tools/Create Result Scene (4_Result)")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("결과 씬 생성", "Assets/Scenes/4_Result.unity 가 이미 있습니다. 덮어쓸까요?", "덮어쓰기", "취소"))
            return;

        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- 카메라 ----------
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.orthographic = true;
        camGo.AddComponent<AudioListener>();

        // ---------- 캔버스 ----------
        GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        Transform root = canvasGo.transform;

        // ---------- 배경 ----------
        Image bg = MakeImage(root, "Background", Vector2.zero, Vector2.zero, new Color(0.05f, 0.05f, 0.09f, 1f));
        Stretch(bg.rectTransform);

        // ---------- 자켓 ----------
        Image jacket = MakeImage(root, "Jacket", new Vector2(-480, 40), new Vector2(560, 560), Color.white);
        jacket.preserveAspect = true;

        // ---------- 곡명 / 난이도 ----------
        TextMeshProUGUI songName = MakeText(root, "SongNameText", "Song Name", 64, new Vector2(0, 440), new Vector2(1700, 90), TextAlignmentOptions.Center, Color.white);
        TextMeshProUGUI diff = MakeText(root, "DifficultyText", "HARD 10", 44, new Vector2(0, 365), new Vector2(900, 70), TextAlignmentOptions.Center, Color.white);

        // ---------- 점수 / ACC ----------
        TextMeshProUGUI score = MakeText(root, "ScoreText", "0", 120, new Vector2(380, 235), new Vector2(760, 150), TextAlignmentOptions.Center, Color.white);
        TextMeshProUGUI acc = MakeText(root, "AccuracyText", "0.00%", 54, new Vector2(380, 125), new Vector2(760, 80), TextAlignmentOptions.Center, new Color(1f, 0.9f, 0.5f));

        // ---------- 판정 수 / 콤보 / 오차 ----------
        TextMeshProUGUI exc = MakeRow(root, "Excellent", "EXCELLENT", 20, new Color(1f, 0.85f, 0.3f));
        TextMeshProUGUI great = MakeRow(root, "Great", "GREAT", -40, new Color(0.45f, 0.85f, 1f));
        TextMeshProUGUI good = MakeRow(root, "Good", "GOOD", -100, new Color(0.55f, 1f, 0.55f));
        TextMeshProUGUI bad = MakeRow(root, "Bad", "BAD", -160, new Color(0.8f, 0.6f, 1f));
        TextMeshProUGUI miss = MakeRow(root, "Miss", "MISS", -220, new Color(1f, 0.35f, 0.35f));
        TextMeshProUGUI maxCombo = MakeRow(root, "MaxCombo", "MAX COMBO", -300, Color.white);
        TextMeshProUGUI error = MakeRow(root, "Error", "TIMING ERROR", -360, Color.white);

        // 오토플레이 표시
        TextMeshProUGUI auto = MakeText(root, "AutoPlayBadge", "AUTO PLAY", 36, new Vector2(-480, -290), new Vector2(560, 60), TextAlignmentOptions.Center, new Color(1f, 0.6f, 0.2f));
        auto.gameObject.SetActive(false);

        // 안내
        MakeText(root, "Hint", "ESC : 곡 선택으로", 28, new Vector2(0, -490), new Vector2(800, 50), TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.6f));

        // ---------- 암막 (마지막 = 맨 위) ----------
        Image overlayImg = MakeImage(root, "BlackOverlay", Vector2.zero, Vector2.zero, Color.black);
        Stretch(overlayImg.rectTransform);
        overlayImg.raycastTarget = false;
        CanvasGroup overlay = overlayImg.gameObject.AddComponent<CanvasGroup>();
        overlay.alpha = 1f;
        overlay.blocksRaycasts = false;

        // ---------- ResultManager ----------
        GameObject mgrGo = new GameObject("ResultManager");
        ResultManager rm = mgrGo.AddComponent<ResultManager>();
        rm.songNameText = songName;
        rm.jacketImage = jacket;
        rm.scoreText = score;
        rm.accuracyText = acc;
        rm.excellentText = exc;
        rm.greatText = great;
        rm.goodText = good;
        rm.badText = bad;
        rm.missText = miss;
        rm.maxComboText = maxCombo;
        rm.errorText = error;
        rm.autoPlayBadge = auto.gameObject;
        rm.blackOverlay = overlay;

        // ---------- 저장 + 빌드 설정 등록 ----------
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);

        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool exists = false;
        foreach (var s in list) if (s.path == ScenePath) exists = true;
        if (!exists)
        {
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        EditorUtility.DisplayDialog("완료", "Assets/Scenes/4_Result.unity 를 만들고 Build Settings 에 등록했습니다.\n\n" +
            "레이아웃/폰트/색은 자유롭게 바꿔도 됩니다. 필드 연결만 유지하면 됩니다.", "확인");
    }

    // ---------------- 헬퍼 ----------------
    private static TextMeshProUGUI MakeRow(Transform parent, string name, string label, float y, Color labelColor)
    {
        MakeText(parent, name + "Label", label, 38, new Vector2(190, y), new Vector2(330, 56), TextAlignmentOptions.Left, labelColor);
        return MakeText(parent, name + "Text", "0", 42, new Vector2(560, y), new Vector2(260, 56), TextAlignmentOptions.Right, Color.white);
    }

    private static Image MakeImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, string text, float fontSize, Vector2 pos, Vector2 size, TextAlignmentOptions align, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.overflowMode = TextOverflowModes.Overflow;

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts/MoveSans-Light");
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
#endif
