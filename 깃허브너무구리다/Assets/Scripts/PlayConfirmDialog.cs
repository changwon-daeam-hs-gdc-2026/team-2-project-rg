using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// 플레이 확인 창 (곡 선택에서 플레이를 누를 때)
//
//  · 경고창 (주의) : MV 에 주의가 필요한 장면이 있는 곡을 MV 켠 채로 플레이할 때
//        [ 돌아가기 ]  [ MV를 끈 채로 진행하기 ]  [ 그대로 진행하기 ]
//  · 알림창 (알림) : 영역 채보(SIMPLE-DIVERSE 전용)를 다른 스킨으로 플레이할 때
//        [ 돌아가기 ]  [ 진행하기 ]
//  둘 다 해당되면 경고창 → 알림창 순서로 이어서 나온다 (뒤 배경 블러는 그대로 유지)
//
//  - 창이 뜨면 뒤 화면 전체가 블러 처리된다 (그 순간의 화면을 찍어 흐리게 만든 그림을 깔아 줌, 별도 셰이더 불필요)
//  - 조작: ← → 로 버튼 고르기, ENTER 결정, ESC = 돌아가기, 마우스 올리기 / 클릭도 가능
//  - 화면 맨 위 전용 캔버스(정렬 순서 900)에 그려서 곡 선택 화면의 어떤 UI 보다도 위에 나온다
// ============================================================
public class PlayConfirmDialog : MonoBehaviour
{
    public enum Kind { Warning, Notice }

    public class Page
    {
        public Kind Kind;
        public string Title;          // "주의" / "알림"
        public string Body;           // 본문 (줄바꿈 \n)
        public string SongLabel;      // 가운데 상자 안의 곡 이름
        public string Question;       // 마지막 질문
        public string[] Buttons;      // 왼쪽부터
    }

    // 결과: proceed = 진행 여부, mvOff = 'MV를 끈 채로 진행하기' 를 골랐는지
    public delegate void Done(bool proceed, bool mvOff);

    // 곡 선택이 열려 있는 동안 / 닫힌 프레임에는 곡 선택 조작을 막는다 (같은 ENTER · ESC 가 두 번 처리되지 않게)
    public static bool IsOpen { get { return current != null || Time.frameCount == lastClosedFrame; } }
    private static PlayConfirmDialog current;
    private static int lastClosedFrame = -1;

    // ----- 모양 (1920 x 1080 기준, 첨부 디자인과 같은 배치) -----
    private const float PanelW = 1040f, PanelH = 500f;
    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.5f);   // 창 바탕: 반투명 검정 (뒤 블러 화면이 비쳐 보임)
    private static readonly Color WarnRed = new Color(0.93f, 0.20f, 0.22f, 1f);
    private static readonly Color TextColor = Color.white;
    private static readonly Color ButtonTextDark = new Color(0.32f, 0.32f, 0.32f, 1f);

    public const string DefaultMvWarning =
        "아래에 표시된 MV에는 폭력적인 장면, 유혈 및 신체 훼손을 묘사하는 장면 등\n불쾌감이나 심리적 불편함을 유발할 수 있는 요소가 포함되어 있습니다.";

    private TMP_FontAsset font;
    private Queue<Page> pages;
    private Done onDone;
    private bool mvOff;

    private Canvas canvas;
    private RectTransform root;
    private RawImage blurImg;
    private Texture2D blurTex;
    private CanvasGroup bgGroup, panelGroup, hintGroup;
    private RectTransform panel;
    private readonly List<DialogButton> buttons = new List<DialogButton>();
    private int selected;
    private bool accepting;          // 창이 다 나타난 뒤에만 입력 받음
    private Page page;

    // =====================================================================
    //  열기
    // =====================================================================
    public static bool Show(List<Page> list, TMP_FontAsset font, Done onDone, GameObject owner = null)
    {
        if (current != null || list == null || list.Count == 0) return false;
        GameObject go = new GameObject("PlayConfirmDialog");
        // [버그 원인] 씬을 겹쳐 불러오는 구조(Additive)라 새 오브젝트가 지워지지 않는 기본 씬에 생겼다
        //  → 지금 보이는 씬(곡 선택)에 넣어서 씬이 바뀌면 같이 사라지게
        if (owner != null) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, owner.scene);
        PlayConfirmDialog d = go.AddComponent<PlayConfirmDialog>();
        d.font = font;
        d.pages = new Queue<Page>(list);
        d.onDone = onDone;
        current = d;
        d.StartCoroutine(d.OpenRoutine());
        return true;
    }

    // 경고창 한 장 만들기 (warningText 가 "1", "true", "default" 등이면 기본 문구)
    public static Page WarningPage(string songLabel, string warningText)
    {
        string body = warningText == null ? "" : warningText.Trim();
        string low = body.ToLowerInvariant();
        if (body.Length == 0 || low == "1" || low == "true" || low == "on" || low == "default" || low == "yes") body = DefaultMvWarning;
        return new Page
        {
            Kind = Kind.Warning, Title = "주의", Body = body.Replace("\\n", "\n"), SongLabel = songLabel,
            Question = "그래도 MV를 켠 상태로 플레이를 진행하시겠습니까?",
            Buttons = new[] { "돌아가기", "MV를 끈 채로 진행하기", "그대로 진행하기" }
        };
    }

    // =====================================================================
    //  화면 만들기
    // =====================================================================
    private IEnumerator OpenRoutine()
    {
        // 1) 지금 화면을 찍어서 흐리게 (창을 만들기 전에 찍어야 창이 같이 찍히지 않음)
        yield return new WaitForEndOfFrame();
        blurTex = CaptureBlurred();

        // 2) 전용 캔버스
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        CanvasScaler sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        root = (RectTransform)transform;

        // 뒤 배경: 블러 그림 + 살짝 어둡게. 클릭이 뒤로 새지 않게 막음
        RectTransform bg = NewRect("Background", root);
        Stretch(bg);
        bgGroup = bg.gameObject.AddComponent<CanvasGroup>();
        blurImg = bg.gameObject.AddComponent<RawImage>();
        blurImg.texture = blurTex;
        blurImg.color = blurTex != null ? Color.white : new Color(0f, 0f, 0f, 0.8f);
        blurImg.raycastTarget = true;
        Image dim = NewImage("Dim", bg, new Color(0f, 0f, 0f, 0.62f));      // 뒤 배경을 충분히 어둡게
        Stretch(dim.rectTransform);
        bgGroup.alpha = 0f;

        // 창
        panel = NewRect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(PanelW, PanelH);
        panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        panelGroup.alpha = 0f;

        // 창 아래 조작 안내 (키캡 디자인): [◀][▶] 선택   [ENTER] 결정   [ESC] 돌아가기
        RectTransform hintRoot = NewRect("Hints", root);
        hintRoot.anchorMin = hintRoot.anchorMax = new Vector2(0.5f, 0.5f);
        hintRoot.sizeDelta = Vector2.zero;
        hintRoot.anchoredPosition = new Vector2(0f, -PanelH * 0.5f - 44f);
        hintGroup = hintRoot.gameObject.AddComponent<CanvasGroup>();
        hintGroup.alpha = 0f;
        RectTransform hints = KeyHintUI.Build(hintRoot, font, font, new[]
        {
            new KeyHintUI.Item("선택", "^LEFT", "^RIGHT"),
            new KeyHintUI.Item("결정", "ENTER"),
            new KeyHintUI.Item("돌아가기", "ESC"),
        }, 18f, 0);
        hints.anchoredPosition = Vector2.zero;

        NextPage();

        // 3) 나타나기 (배경 블러 + 창이 살짝 커지며)
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.22f);
            float e = 1f - Mathf.Pow(1f - t, 3f);
            bgGroup.alpha = e;
            panelGroup.alpha = e;
            panel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
            yield return null;
        }
        accepting = true;
    }

    private void NextPage()
    {
        page = pages.Dequeue();
        foreach (Transform c in panel) Destroy(c.gameObject);
        buttons.Clear();
        Build(page);
        selected = 0;   // 기본 선택 = 돌아가기 (실수로 ENTER 를 눌러도 안전)
        RefreshButtons();
    }

    private void Build(Page p)
    {
        Image body = NewImage("Body", panel, PanelColor);
        Stretch(body.rectTransform);
        body.raycastTarget = true;

        bool warn = p.Kind == Kind.Warning;
        float y = -22f;   // 위에서부터 내려가며 배치

        if (warn)
        {
            // 위쪽 빨간 선
            Image line = NewImage("TopLine", panel, WarnRed);
            line.rectTransform.anchorMin = new Vector2(0f, 1f);
            line.rectTransform.anchorMax = new Vector2(1f, 1f);
            line.rectTransform.pivot = new Vector2(0.5f, 1f);
            line.rectTransform.sizeDelta = new Vector2(0f, 6f);
            line.rectTransform.anchoredPosition = new Vector2(0f, 4f);

            // 경고 아이콘
            Image icon = NewImage("Icon", panel, WarnRed);
            icon.sprite = NoteArt.WarnIconSprite;
            TopCenter(icon.rectTransform, y, 46f, 46f);
            y -= 52f;
        }
        else y -= 52f;

        TextMeshProUGUI title = NewText("Title", panel, p.Title, 36f, warn ? WarnRed : TextColor, FontStyles.Bold);
        TopCenter(title.rectTransform, y, 600f, 48f);
        y -= 58f;

        TextMeshProUGUI bodyText = NewText("Text", panel, p.Body, 26f, TextColor, FontStyles.Normal);
        bodyText.lineSpacing = 10f;
        TopCenter(bodyText.rectTransform, y, PanelW - 60f, 84f);
        y -= 104f;

        // 곡 이름 상자 (흰 테두리, 글자 길이에 맞춤)
        TextMeshProUGUI song = NewText("Song", panel, p.SongLabel, 24f, TextColor, FontStyles.Normal);
        song.overflowMode = TextOverflowModes.Ellipsis;
        float sw = Mathf.Clamp(song.GetPreferredValues(p.SongLabel).x + 48f, 300f, PanelW - 120f);
        TopCenter(song.rectTransform, y, sw, 46f);
        Image box = NewImage("SongBox", panel, Color.white);
        box.sprite = OutlineSprite;
        box.type = Image.Type.Sliced;
        TopCenter(box.rectTransform, y, sw, 46f);
        y -= 74f;

        TextMeshProUGUI q = NewText("Question", panel, p.Question, 26f, TextColor, FontStyles.Normal);
        TopCenter(q.rectTransform, y, PanelW - 60f, 40f);

        // 버튼 줄 (아래 가운데)
        const float bh = 56f, gap = 16f, pad = 64f;
        float[] widths = new float[p.Buttons.Length];
        float total = 0f;
        for (int i = 0; i < p.Buttons.Length; i++)
        {
            TextMeshProUGUI probe = NewText("Probe", panel, p.Buttons[i], 24f, TextColor, FontStyles.Normal);
            widths[i] = Mathf.Max(170f, probe.GetPreferredValues(p.Buttons[i]).x + pad);
            Destroy(probe.gameObject);
            total += widths[i] + (i > 0 ? gap : 0f);
        }
        float x = -total * 0.5f;
        for (int i = 0; i < p.Buttons.Length; i++)
        {
            int index = i;
            // 경고창의 '그대로 진행하기'는 고르면 빨간색 (주의가 필요한 선택), 나머지는 흰색
            Color accent = (p.Kind == Kind.Warning && i == p.Buttons.Length - 1) ? WarnRed : Color.white;
            DialogButton b = DialogButton.Create(panel, this, p.Buttons[i], widths[i], bh, accent, () => Hover(index), () => Choose(index));
            RectTransform r = (RectTransform)b.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0.5f);                                // 가운데 기준 (고를 때 제자리에서 커짐)
            r.anchoredPosition = new Vector2(x + widths[i] * 0.5f, 38f + bh * 0.5f);
            x += widths[i] + gap;
            buttons.Add(b);
        }
    }

    // =====================================================================
    //  조작
    // =====================================================================
    void Update()
    {
        if (hintGroup != null && panelGroup != null) hintGroup.alpha = Mathf.Min(panelGroup.alpha, bgGroup != null ? bgGroup.alpha : 1f);
        if (!accepting) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.leftArrowKey.wasPressedThisFrame) { selected = (selected - 1 + buttons.Count) % buttons.Count; RefreshButtons(); }
        if (kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) { selected = (selected + 1) % buttons.Count; RefreshButtons(); }
        if (kb.escapeKey.wasPressedThisFrame) { Choose(0); return; }
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Choose(selected);
    }

    private void Hover(int index)
    {
        if (!accepting || index == selected) return;
        selected = index;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        for (int i = 0; i < buttons.Count; i++) buttons[i].SetSelected(i == selected);
    }

    // 0 = 돌아가기,  경고창 1 = MV 끄고 진행 / 2 = 그대로 진행,  알림창 1 = 진행
    private void Choose(int index)
    {
        if (!accepting) return;
        if (index <= 0) { StartCoroutine(CloseRoutine()); return; }
        if (page.Kind == Kind.Warning && index == 1) mvOff = true;

        if (pages.Count > 0)
        {
            // 다음 창 (블러는 그대로, 창만 바꿈)
            StartCoroutine(SwapRoutine());
            return;
        }
        // 모두 통과: 게임 시작을 알리고 창은 바로 사라지게 함
        //  [버그 수정] 예전에는 '씬이 바뀌면 같이 사라진다'고 보고 창을 남겨 뒀는데, 게임 씬을 불러와도 이 창이 남아
        //   (맨 위 캔버스라) 게임 화면을 가렸다 → 음악은 나오는데 화면은 멈춘 것처럼 보였다
        accepting = false;
        current = null;
        lastClosedFrame = Time.frameCount;
        Done cb = onDone;
        onDone = null;
        if (cb != null) cb(true, mvOff);
        StartCoroutine(FadeOutAndDestroy(0.2f));
    }

    private IEnumerator FadeOutAndDestroy(float seconds)
    {
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
            if (bgGroup != null) bgGroup.alpha = 1f - t;
            if (panelGroup != null) panelGroup.alpha = 1f - t;
            yield return null;
        }
        Destroy(gameObject);
    }

    // 다른 씬을 불러오면 (어떤 방식이든) 즉시 없앰
    void OnEnable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; }
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (this != null) Destroy(gameObject);
    }

    private IEnumerator SwapRoutine()
    {
        accepting = false;
        float t = 0f;
        while (t < 1f) { t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.12f); panelGroup.alpha = 1f - t; yield return null; }
        NextPage();
        t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.18f);
            float e = 1f - Mathf.Pow(1f - t, 3f);
            panelGroup.alpha = e;
            panel.localScale = Vector3.one * Mathf.Lerp(0.97f, 1f, e);
            yield return null;
        }
        accepting = true;
    }

    private IEnumerator CloseRoutine()
    {
        accepting = false;
        current = null;
        lastClosedFrame = Time.frameCount;
        Done cb = onDone;
        onDone = null;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.16f);
            bgGroup.alpha = 1f - t;
            panelGroup.alpha = 1f - t;
            yield return null;
        }
        if (cb != null) cb(false, false);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (current == this) { current = null; lastClosedFrame = Time.frameCount; }
        if (blurTex != null) Destroy(blurTex);
    }

    // =====================================================================
    //  배경 블러: 화면을 찍어 1/8 크기로 줄인 뒤 상자 블러 3번 → 늘려서 깔면 부드럽게 흐려 보임
    // =====================================================================
    public static Texture2D CaptureBlurred()
    {
        Texture2D shot = null;
        try { shot = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (Exception e) { Debug.LogWarning("[PlayConfirmDialog] 화면 캡처 실패: " + e.Message); }
        if (shot == null) return null;

        int w = Mathf.Max(16, shot.width / 8), h = Mathf.Max(16, shot.height / 8);
        RenderTexture a = RenderTexture.GetTemporary(Mathf.Max(16, shot.width / 2), Mathf.Max(16, shot.height / 2), 0);
        RenderTexture b = RenderTexture.GetTemporary(Mathf.Max(16, shot.width / 4), Mathf.Max(16, shot.height / 4), 0);
        RenderTexture c = RenderTexture.GetTemporary(w, h, 0);
        a.filterMode = b.filterMode = c.filterMode = FilterMode.Bilinear;
        Graphics.Blit(shot, a);       // 단계적으로 줄여야 깜빡이는 점 없이 고르게 섞임
        Graphics.Blit(a, b);
        Graphics.Blit(b, c);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = c;
        Texture2D small = new Texture2D(w, h, TextureFormat.RGBA32, false);
        small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        small.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(a);
        RenderTexture.ReleaseTemporary(b);
        RenderTexture.ReleaseTemporary(c);
        Destroy(shot);

        Color32[] px = small.GetPixels32();
        Color32[] tmp = new Color32[px.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlur(px, tmp, w, h, 3, true);
            BoxBlur(tmp, px, w, h, 3, false);
        }
        for (int i = 0; i < px.Length; i++) px[i].a = 255;
        small.SetPixels32(px);
        small.Apply();
        small.wrapMode = TextureWrapMode.Clamp;
        small.filterMode = FilterMode.Bilinear;
        return small;
    }

    private static void BoxBlur(Color32[] src, Color32[] dst, int w, int h, int r, bool horizontal)
    {
        int n = r * 2 + 1;
        int lines = horizontal ? h : w, len = horizontal ? w : h;
        for (int line = 0; line < lines; line++)
        {
            int sr = 0, sg = 0, sb = 0;
            for (int k = -r; k <= r; k++)
            {
                Color32 c = src[Index(line, Mathf.Clamp(k, 0, len - 1), w, horizontal)];
                sr += c.r; sg += c.g; sb += c.b;
            }
            for (int i = 0; i < len; i++)
            {
                dst[Index(line, i, w, horizontal)] = new Color32((byte)(sr / n), (byte)(sg / n), (byte)(sb / n), 255);
                Color32 add = src[Index(line, Mathf.Min(i + r + 1, len - 1), w, horizontal)];
                Color32 sub = src[Index(line, Mathf.Max(i - r, 0), w, horizontal)];
                sr += add.r - sub.r; sg += add.g - sub.g; sb += add.b - sub.b;
            }
        }
    }

    private static int Index(int line, int i, int w, bool horizontal) { return horizontal ? line * w + i : i * w + line; }

    // =====================================================================
    //  UI 도우미
    // =====================================================================
    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    private static Image NewImage(string name, Transform parent, Color c)
    {
        Image img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color c, FontStyles style)
    {
        TextMeshProUGUI t = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        return t;
    }

    // 창 위쪽 가운데 기준으로 y 만큼 내려와서 배치
    private static void TopCenter(RectTransform r, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(0f, y);
    }

    // 흰 테두리 사각형 (9-slice, 두께 3px) — 곡 이름 상자 · 버튼
    private static Sprite outline;
    public static Sprite OutlineSprite
    {
        get
        {
            if (outline != null) return outline;
            const int S = 16, B = 3;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                bool edge = x < B || y < B || x >= S - B || y >= S - B;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(edge ? 255 : 0));
            }
            tex.SetPixels32(px); tex.Apply();
            outline = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(B + 1, B + 1, B + 1, B + 1));
            return outline;
        }
    }

    // 둥근 사각형 (9-slice): 채움 / 테두리(1.5px)
    private static Sprite roundFill, roundLine;
    private static Sprite RoundSprite(bool line)
    {
        if (line ? roundLine != null : roundFill != null) return line ? roundLine : roundFill;
        const int S = 48, R = 14;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - R;   // 안쪽 음수
            float a = line ? Mathf.Clamp01(1.0f - Mathf.Abs(d + 1.0f)) : Mathf.Clamp01(0.5f - d);
            px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px); tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
        if (line) roundLine = sp; else roundFill = sp;
        return sp;
    }

    // =====================================================================
    //  버튼 (둥근 사각형)
    //   평소   : 옅은 반투명 흰 바탕 + 가는 흰 테두리 + 흰 글자
    //   고른 것: 강조색(흰색 / 경고의 '그대로 진행하기'는 빨강)으로 꽉 채움 + 뒤에 은은한 빛 + 살짝 커짐
    //            글자는 흰 바탕이면 진한 회색, 빨간 바탕이면 흰색
    //   아래쪽 가는 선이 고를 때 가운데에서 양쪽으로 그어짐
    // =====================================================================
    private class DialogButton : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        private Image glow, fill, frame, underline;
        private TextMeshProUGUI label;
        private Action onHover, onClick;
        private float lit, target;
        private Color accent;
        private float width;

        public static DialogButton Create(Transform parent, PlayConfirmDialog owner, string text, float w, float h, Color accent, Action hover, Action click)
        {
            RectTransform r = NewRect("Button_" + text, parent);
            r.sizeDelta = new Vector2(w, h);
            r.pivot = new Vector2(0.5f, 0.5f);
            DialogButton b = r.gameObject.AddComponent<DialogButton>();
            b.onHover = hover;
            b.onClick = click;
            b.accent = accent;
            b.width = w;

            b.glow = NewImage("Glow", r, new Color(1f, 1f, 1f, 0f));
            b.glow.sprite = NoteArt.GlowSprite;
            b.glow.rectTransform.anchorMin = b.glow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            b.glow.rectTransform.sizeDelta = new Vector2(w + 90f, h + 70f);

            b.fill = NewImage("Fill", r, new Color(1f, 1f, 1f, 0.08f));
            b.fill.sprite = RoundSprite(false);
            b.fill.type = Image.Type.Sliced;
            Stretch(b.fill.rectTransform);
            b.fill.raycastTarget = true;      // 이 버튼 영역에서 마우스를 받음

            b.frame = NewImage("Frame", r, new Color(1f, 1f, 1f, 0.45f));
            b.frame.sprite = RoundSprite(true);
            b.frame.type = Image.Type.Sliced;
            Stretch(b.frame.rectTransform);

            b.underline = NewImage("Underline", r, new Color(1f, 1f, 1f, 0f));
            b.underline.rectTransform.anchorMin = b.underline.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            b.underline.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            b.underline.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            b.underline.rectTransform.sizeDelta = new Vector2(0f, 2f);

            b.label = owner.NewText("Label", r, text, 24f, Color.white, FontStyles.Bold);
            Stretch(b.label.rectTransform);
            b.label.characterSpacing = 2f;
            return b;
        }

        public void SetSelected(bool on) { target = on ? 1f : 0f; }

        void Update()
        {
            lit = Mathf.MoveTowards(lit, target, Time.unscaledDeltaTime / 0.12f);
            float e = lit * lit * (3f - 2f * lit);
            bool whiteAccent = accent.r > 0.95f && accent.g > 0.95f && accent.b > 0.95f;

            Color baseFill = new Color(1f, 1f, 1f, 0.08f);
            fill.color = Color.Lerp(baseFill, new Color(accent.r, accent.g, accent.b, 1f), e);
            frame.color = Color.Lerp(new Color(1f, 1f, 1f, 0.45f), new Color(accent.r, accent.g, accent.b, 0f), e);
            label.color = Color.Lerp(Color.white, whiteAccent ? ButtonTextDark : Color.white, e);
            glow.color = new Color(accent.r, accent.g, accent.b, 0.28f * e);
            underline.color = new Color(accent.r, accent.g, accent.b, 0.9f * e);
            underline.rectTransform.sizeDelta = new Vector2(width * 0.6f * e, 2f);
            transform.localScale = Vector3.one * (1f + 0.04f * e);
        }

        public void OnPointerEnter(PointerEventData e) { if (onHover != null) onHover(); }
        public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left && onClick != null) onClick(); }
    }
}