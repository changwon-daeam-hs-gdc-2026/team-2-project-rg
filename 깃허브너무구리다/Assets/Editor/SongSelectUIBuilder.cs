#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 메뉴 [Tools → 4B → Build Song Select UI] 한 번으로
//  악곡 선택창 UI 전체를 스크린샷(1920x1080) 위치대로 만들고 SongSelectManager 에 자동 연결한다.
//
//  * 반드시 Assets/Editor 폴더(이름이 Editor 인 아무 폴더)에 넣을 것
//  * 1_SongSelect 씬을 열어 둔 상태(활성 씬)에서 실행
//  * 다시 실행하면 SongSelectUI 를 지우고 새로 만든다 (Ctrl+Z 로 되돌리기 가능)
//  * Project 창에서 TMP 폰트 에셋을 선택한 채 실행하면 그 폰트를 쓴다
// ============================================================
public static class SongSelectUIBuilder
{
    private const string RootName = "SongSelectUI";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string RowPrefabPath = "Assets/Prefabs/SongRow.prefab";

    private static TMP_FontAsset font;
    private static int uiLayer;

    private static readonly Color Cyan = new Color(0.36f, 0.88f, 0.90f, 1f);
    private static readonly Color Gray = new Color(0.75f, 0.75f, 0.78f, 1f);
    private static readonly Color InsaneColor = new Color(1f, 0.192f, 0.192f, 1f); // #ff3131

    [MenuItem("Tools/4B/Build Song Select UI")]
    public static void Build()
    {
        font = Selection.activeObject as TMP_FontAsset;
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts/MoveSans-Light");
        if (font == null) font = TMP_Settings.defaultFontAsset;
        uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 0;

        var scene = EditorSceneManager.GetActiveScene();
        Undo.SetCurrentGroupName("Build Song Select UI");
        int undoGroup = Undo.GetCurrentGroup();

        // ---------- 1. 기존 SongSelectUI 제거 ----------
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == RootName) Undo.DestroyObjectImmediate(go);

        // ---------- 2. SongSelectManager 확보 (Canvas 밖 루트로 이동) ----------
        SongSelectManager manager = null;
        foreach (var go in scene.GetRootGameObjects())
        {
            manager = go.GetComponentInChildren<SongSelectManager>(true);
            if (manager != null) break;
        }
        if (manager == null)
        {
            var mgo = new GameObject("SongSelectManager");
            Undo.RegisterCreatedObjectUndo(mgo, "Create SongSelectManager");
            manager = Undo.AddComponent<SongSelectManager>(mgo);
        }
        else if (manager.transform.parent != null)
        {
            Undo.SetTransformParent(manager.transform, null, "Move SongSelectManager to root");
        }
        if (!manager.gameObject.activeSelf) { Undo.RecordObject(manager.gameObject, "Activate"); manager.gameObject.SetActive(true); }

        AudioSource previewSource = manager.GetComponent<AudioSource>();
        if (previewSource == null) previewSource = Undo.AddComponent<AudioSource>(manager.gameObject);
        previewSource.playOnAwake = false;

        // ---------- 3. 기존 Canvas 비활성화 여부 ----------
        var oldCanvases = new List<Canvas>();
        foreach (var go in scene.GetRootGameObjects())
            foreach (var c in go.GetComponentsInChildren<Canvas>(false))
                if (c.isRootCanvas) oldCanvases.Add(c);

        if (oldCanvases.Count > 0 && EditorUtility.DisplayDialog(
                "기존 Canvas",
                $"씬에 기존 Canvas가 {oldCanvases.Count}개 있습니다.\n예전 악곡 선택 UI가 겹쳐 보이지 않도록 비활성화할까요?\n(삭제가 아니라 비활성화이며 Ctrl+Z 로 되돌릴 수 있습니다)",
                "비활성화", "그대로 두기"))
        {
            foreach (var c in oldCanvases)
            {
                Undo.RecordObject(c.gameObject, "Disable old canvas");
                c.gameObject.SetActive(false);
            }
        }

        // ---------- 4. Canvas ----------
        GameObject canvasGo = new GameObject(RootName, typeof(RectTransform));
        canvasGo.layer = uiLayer;
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        Transform root = canvasGo.transform;

        // ---------- 5. 배경 ----------
        Image bg = Img("Background", root, Color.white, Resources.Load<Sprite>("Sprites/blurimg/1"));
        Stretch(bg.rectTransform);
        AspectRatioFitter fitter = bg.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = 16f / 9f;

        // ---------- 6. 로고 "4B TUNES" ----------
        var logo4B = Txt("Logo_4B", root, "4B", 86, TextAlignmentOptions.BottomLeft, Color.white, FontStyles.Bold);
        TopLeft(logo4B.rectTransform, 132, 98, 120, 76);
        var logoTunes = Txt("Logo_TUNES", root, "TUNES", 34, TextAlignmentOptions.BottomLeft, Cyan, FontStyles.Bold);
        TopLeft(logoTunes.rectTransform, 221, 128, 200, 44);

        // ---------- 7. 좌측 곡 정보 ----------
        RectTransform leftInfo = Rect("LeftInfo", root);
        TopLeft(leftInfo, 108, 185, 480, 480);
        CanvasGroup leftGroup = leftInfo.gameObject.AddComponent<CanvasGroup>();
        leftGroup.blocksRaycasts = false;

        Image jacket = Img("Jacket", leftInfo, Color.white, Resources.Load<Sprite>("Sprites/squareimg/1"));
        Stretch(jacket.rectTransform);

        // 곡 정보 띠 뒤 블러: 띠와 같은 크기의 마스크 영역 + 그 안에 자켓 크기의 RawImage
        RectTransform blurArea = Rect("JacketBlurArea", leftInfo);
        blurArea.anchorMin = new Vector2(0f, 0f); blurArea.anchorMax = new Vector2(1f, 0f); blurArea.pivot = new Vector2(0.5f, 0f);
        blurArea.anchoredPosition = Vector2.zero; blurArea.sizeDelta = new Vector2(0f, 111f);
        blurArea.gameObject.AddComponent<RectMask2D>();
        RectTransform blurRt = Rect("JacketBlur", blurArea);
        blurRt.anchorMin = new Vector2(0f, 0f); blurRt.anchorMax = new Vector2(1f, 0f); blurRt.pivot = new Vector2(0.5f, 0f);
        blurRt.anchoredPosition = Vector2.zero; blurRt.sizeDelta = new Vector2(0f, 480f);
        RawImage jacketBlur = blurRt.gameObject.AddComponent<RawImage>();
        jacketBlur.raycastTarget = false;
        jacketBlur.texture = jacket.sprite != null ? jacket.sprite.texture : null; // 편집 화면 미리보기용 (실행 시 블러로 교체)
        jacketBlur.enabled = jacketBlur.texture != null;

        Image infoBar = Img("InfoBar", leftInfo, new Color(0f, 0f, 0f, 0.55f));
        RectTransform ib = infoBar.rectTransform;
        ib.anchorMin = new Vector2(0f, 0f); ib.anchorMax = new Vector2(1f, 0f); ib.pivot = new Vector2(0.5f, 0f);
        ib.anchoredPosition = Vector2.zero; ib.sizeDelta = new Vector2(0f, 111f);

        Image accent = Img("AccentLine", ib, Color.white);
        RectTransform ar = accent.rectTransform;
        ar.anchorMin = new Vector2(0f, 0f); ar.anchorMax = new Vector2(0f, 1f); ar.pivot = new Vector2(0f, 0.5f);
        ar.offsetMin = new Vector2(10f, 12f); ar.offsetMax = new Vector2(14f, -12f);

        var title = Txt("Title", ib, "Song Title", 36, TextAlignmentOptions.Left, Color.white);
        TopBand(title.rectTransform, 26f, 16f, 14f, 48f);
        var composer = Txt("Composer", ib, "Composer", 17, TextAlignmentOptions.Left, Color.white);
        TopBand(composer.rectTransform, 26f, 16f, 68f, 26f);

        Image lengthBadge = Img("LengthBadge", leftInfo, new Color(0f, 0f, 0f, 0.5f));
        TopLeft(lengthBadge.rectTransform, 395, 334, 76, 28);
        var lengthText = Txt("LengthText", lengthBadge.rectTransform, "3:39", 20, TextAlignmentOptions.Center, Color.white);
        Stretch(lengthText.rectTransform);

        // 잠긴 곡 표시 (자켓 위를 덮음, 평소에는 투명)
        Image lockedPanel = Img("LockedStatusPanel", root, new Color(0f, 0f, 0f, 0.6f));
        TopLeft(lockedPanel.rectTransform, 108, 185, 480, 480);
        CanvasGroup lockedGroup = lockedPanel.gameObject.AddComponent<CanvasGroup>();
        lockedGroup.alpha = 0f; lockedGroup.blocksRaycasts = false;
        var lockedText = Txt("LockedText", lockedPanel.rectTransform, "LOCKED", 48, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
        Stretch(lockedText.rectTransform);

        // ---------- 8. 좌측 난이도 ----------
        RectTransform diffRoot = Rect("Difficulty", root);
        Stretch(diffRoot);
        Image diffImg = DiffImage("SelectedDiffImg", diffRoot, "INSANE", 688, out TextMeshProUGUI diffVal, "13");

        // ---------- 9. 우측 헤더 ----------
        Image header = Img("HeaderBar", root, new Color(0.10f, 0.20f, 0.40f, 0.55f));
        TopLeft(header.rectTransform, 652, 102, 1268, 61);

        // 평행사변형 표시 (라벨보다 먼저 만들어야 라벨 뒤에 그려진다)
        RectTransform indicator = Rect("DifficultyIndicator", header.rectTransform);
        indicator.anchorMin = indicator.anchorMax = new Vector2(0f, 1f);
        indicator.pivot = new Vector2(0.5f, 0.5f);
        indicator.anchoredPosition = new Vector2(1818 - 652, -31f);
        indicator.sizeDelta = new Vector2(98f, 46f);
        ParallelogramGraphic indBase = Para("Base", indicator, InsaneColor, 1f);
        ParallelogramGraphic indFill = Para("Fill", indicator, InsaneColor, 0f);

        RectTransform ezLabel = HeaderLabel("EZ", header.rectTransform, 1653 - 652);
        RectTransform hdLabel = HeaderLabel("HD", header.rectTransform, 1735 - 652);
        RectTransform insLabel = HeaderLabel("INS", header.rectTransform, 1818 - 652);

        // ---------- 10. 악곡 정렬표 뷰포트 ----------
        RectTransform viewport = Rect("SongListViewport", root);
        TopLeft(viewport, 720, 173, 1200, 852);
        viewport.gameObject.AddComponent<RectMask2D>();

        // ---------- 11. 배속 / MV 표시 ----------
        TopLeft(Txt("SpeedLabel", root, "SPEED", 22, TextAlignmentOptions.Left, Gray).rectTransform, 108, 950, 100, 36);
        var speedText = Txt("SpeedText", root, "1.0", 28, TextAlignmentOptions.Left, Color.white);
        TopLeft(speedText.rectTransform, 200, 948, 90, 40);
        TopLeft(Txt("MvLabel", root, "MV", 22, TextAlignmentOptions.Left, Gray).rectTransform, 320, 950, 60, 36);
        var mvText = Txt("MvStatusText", root, "None", 28, TextAlignmentOptions.Left, Color.white);
        TopLeft(mvText.rectTransform, 375, 948, 120, 40);

        // ---------- 12. 행 프리팹 ----------
        SongRowUI rowPrefab = GetOrCreateRowPrefab();

        // ---------- 13. 매니저 연결 ----------
        Undo.RecordObject(manager, "Wire SongSelectManager");
        manager.selectScreenImg = bg;
        manager.leftInfoGroup = leftGroup;
        manager.selectedJacketImg = jacket;
        manager.jacketBlurImage = jacketBlur;
        manager.selectedSongTitleText = title;
        manager.selectedComposerText = composer;
        manager.songLengthText = lengthText;
        manager.selectedDiffImg = diffImg;
        manager.selectedDiffValueText = diffVal;
        manager.headerIndicator = indicator;
        manager.indicatorBase = indBase;
        manager.indicatorFill = indFill;
        manager.headerEasyLabel = ezLabel;
        manager.headerHardLabel = hdLabel;
        manager.headerInsaneLabel = insLabel;
        manager.lockedStatusPanel = lockedPanel.rectTransform;
        manager.songListViewport = viewport;
        manager.songRowPrefab = rowPrefab;
        manager.rowFont = font;
        manager.visibleRowCount = 8;
        manager.selectedSlot = 3;
        manager.rowHeight = 80f;
        manager.rowGap = 28f;
        manager.clipPadding = 8f;
        manager.autoSizeViewportHeight = true;
        manager.speedText = speedText;
        manager.mvStatusText = mvText;
        manager.mvIconImg = null;
        manager.previewAudioSource = previewSource;
        EditorUtility.SetDirty(manager);

        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Song Select UI");
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = canvasGo;

        Debug.Log($"[SongSelectUIBuilder] 완료. 폰트: {(font != null ? font.name : "없음")} / 행 프리팹: {RowPrefabPath}. 씬을 저장(Ctrl+S)하세요.");
    }

    // =====================================================================
    //  행 프리팹 (이미 있으면 사용자가 꾸민 것을 보존하고 그대로 사용)
    // =====================================================================
    private static SongRowUI GetOrCreateRowPrefab()
    {
        SongRowUI existing = AssetDatabase.LoadAssetAtPath<SongRowUI>(RowPrefabPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets", "Prefabs");

        GameObject temp = new GameObject("TempRowParent", typeof(RectTransform));
        SongRowUI row = SongRowUI.CreateDefault(temp.transform, font, 80f);
        row.name = "SongRow";
        row.rect.sizeDelta = new Vector2(1200f, 80f);

        Sprite bar = Resources.Load<Sprite>("Sprites/etc/selectedbar");
        if (bar != null) row.selectedBar.sprite = bar;
        row.selectedBar.type = Image.Type.Filled;
        row.selectedBar.fillMethod = Image.FillMethod.Horizontal;
        row.selectedBar.fillOrigin = (int)Image.OriginHorizontal.Left;

        // 프리팹을 열었을 때 모양을 볼 수 있도록 샘플 값 (실행 시 실제 데이터로 바뀜)
        row.titleText.text = "Song Title";
        row.composerText.text = "Composer";
        row.easyText.text = "3"; row.hardText.text = "8"; row.insaneText.text = "13";

        SetLayerRecursive(row.gameObject, uiLayer);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(row.gameObject, RowPrefabPath);
        Object.DestroyImmediate(temp);
        return saved != null ? saved.GetComponent<SongRowUI>() : null;
    }

    // =====================================================================
    //  생성 도우미
    // =====================================================================
    private static Image DiffImage(string name, Transform parent, string diff, float y, out TextMeshProUGUI value, string sample)
    {
        Image img = Img(name, parent, Color.white, LoadDifficultySprite(diff, "ON"));
        TopLeft(img.rectTransform, 108, y, 480, 65);

        value = Txt(name.Replace("Img", "Value"), img.rectTransform, sample, 44, TextAlignmentOptions.Center, Color.white);
        RectTransform r = value.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(-52f, 0f);
        r.sizeDelta = new Vector2(80f, 60f);
        return img;
    }

    private static RectTransform HeaderLabel(string text, RectTransform parent, float centerX)
    {
        var t = Txt(text, parent, text, 22, TextAlignmentOptions.Center, Color.white);
        RectTransform r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(centerX, -31f);
        r.sizeDelta = new Vector2(80f, 40f);
        return r;
    }

    private static ParallelogramGraphic Para(string name, Transform parent, Color color, float fill)
    {
        RectTransform r = Rect(name, parent);
        Stretch(r);
        ParallelogramGraphic g = r.gameObject.AddComponent<ParallelogramGraphic>();
        g.color = color;
        g.Skew = 14f;
        g.FillAmount = fill;
        g.raycastTarget = false;
        return g;
    }

    private static Sprite LoadDifficultySprite(string upper, string state)
    {
        // 1순위: Sprites/etc/Level.png 안의 스프라이트 (EASY_ON, EASY_OFF ...)
        Sprite[] sheet = Resources.LoadAll<Sprite>("Sprites/etc/Level");
        if (sheet != null)
            foreach (var sp in sheet)
                if (string.Equals(sp.name, $"{upper}_{state}", System.StringComparison.OrdinalIgnoreCase)) return sp;

        // 2순위: 개별 파일
        string title = upper.Substring(0, 1) + upper.Substring(1).ToLowerInvariant();
        string stateTitle = state.Substring(0, 1) + state.Substring(1).ToLowerInvariant();
        foreach (var b in new[] { upper, title, upper.ToLowerInvariant() })
            foreach (var s in new[] { state, stateTitle, state.ToLowerInvariant() })
            {
                Sprite spr = Resources.Load<Sprite>($"Sprites/etc/{b}_{s}");
                if (spr != null) return spr;
            }
        return null;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = uiLayer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image Img(string name, Transform parent, Color color, Sprite sprite = null)
    {
        RectTransform r = Rect(name, parent);
        Image img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI Txt(string name, Transform parent, string text, float size, TextAlignmentOptions align, Color color, FontStyles style = FontStyles.Normal)
    {
        RectTransform r = Rect(name, parent);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    // 스크린샷 픽셀 좌표 (x, y = 위에서부터) → 왼쪽 위 기준 배치
    private static void TopLeft(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    // 부모 위쪽에 붙고 가로로 늘어나는 띠 (top = 부모 위에서 떨어진 거리)
    private static void TopBand(RectTransform r, float left, float right, float top, float height)
    {
        r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.offsetMin = new Vector2(left, -(top + height));
        r.offsetMax = new Vector2(-right, -top);
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }
}
#endif