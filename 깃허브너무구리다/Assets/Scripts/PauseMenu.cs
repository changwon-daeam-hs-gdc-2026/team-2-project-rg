using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// 일시정지 화면 (코드로 만듦 — 씬에 배치할 것 없음)
//
//   ┌──────────────────────────────────────────────┐
//   │  PAUSED                                       │  ← 뒤 게임 화면은 블러 + 어둡게
//   │  ───                                          │
//   │  곡 제목                                      │
//   │  작곡가 · 난이도                              │
//   │                                               │
//   │  01  RESUME        ← 고른 항목: 흰 막대 + 검정 글자, 살짝 오른쪽으로
//   │  02  RETRY                                    │
//   │  03  SETTINGS                                 │
//   │  04  QUIT                                     │
//   │                                  ↑↓ ENTER ESC │
//   └──────────────────────────────────────────────┘
//
//  - GameManager 가 만들고 PauseGame / ResumeGame 때 Show / Hide 를 부른다 (예전 Pause UI 오브젝트는 쓰지 않음)
//  - 조작: ↑ ↓ 고르기, ENTER / SPACE 결정, ESC = 계속하기, 마우스 올리기 / 클릭
//  - 설정창(SETTINGS)이 열려 있는 동안에는 입력을 받지 않는다
//  - 색: 검정 + 흰색
// ============================================================
public class PauseMenu : MonoBehaviour
{
    public struct Item { public string Label; public Action Action; }

    private const int SortOrder = 450;              // 게임 화면보다 위, 설정창(500)보다 아래
    private static readonly Color Dim = new Color(0f, 0f, 0f, 0.62f);
    private static readonly Color TextIdle = new Color(1f, 1f, 1f, 0.62f);
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color TextOnWhite = new Color(0.04f, 0.04f, 0.04f, 1f);

    private TMP_FontAsset regular, bold;
    private Action onResume;
    private readonly List<Item> items = new List<Item>();
    private readonly List<Row> rows = new List<Row>();

    private Canvas canvas;
    private CanvasGroup group;
    private RawImage blurImg;
    private Texture2D blurTex;
    private TextMeshProUGUI titleText, songText, subText;
    private RectTransform menuRoot;
    private int selected;
    private int openedFrame = -1;
    private float showT;
    private bool capturing;

    public bool IsShown { get { return gameObject.activeSelf; } }

    // =====================================================================
    //  만들기
    // =====================================================================
    public static PauseMenu Create(TMP_FontAsset regular, TMP_FontAsset bold, List<Item> items, Action onResume)
    {
        GameObject go = new GameObject("PauseMenu", typeof(RectTransform));
        go.layer = 5;
        PauseMenu m = go.AddComponent<PauseMenu>();
        m.regular = regular;
        m.bold = bold != null ? bold : regular;
        m.items.AddRange(items);
        m.onResume = onResume;
        m.Build();
        go.SetActive(false);
        return m;
    }

    private void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;
        CanvasScaler sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        RectTransform root = (RectTransform)transform;

        // 뒤 배경: 블러 + 어둡게 (클릭이 게임으로 새지 않게 막음)
        RectTransform bg = NewRect("Background", root);
        Stretch(bg);
        blurImg = bg.gameObject.AddComponent<RawImage>();
        blurImg.color = new Color(0f, 0f, 0f, 0.85f);
        blurImg.raycastTarget = true;
        Image dim = NewImage("Dim", bg, Dim);
        Stretch(dim.rectTransform);

        // 왼쪽 아래로 갈수록 진해지는 그림자 (글자가 잘 보이도록)
        Image shade = NewImage("Shade", root, new Color(0f, 0f, 0f, 0.55f));
        shade.sprite = HGradient;
        shade.rectTransform.anchorMin = new Vector2(0f, 0f);
        shade.rectTransform.anchorMax = new Vector2(0f, 1f);
        shade.rectTransform.pivot = new Vector2(0f, 0.5f);
        shade.rectTransform.sizeDelta = new Vector2(1100f, 0f);
        shade.rectTransform.anchoredPosition = Vector2.zero;

        // 제목 묶음
        titleText = NewText("Title", root, "PAUSED", 104f, Color.white, true);
        TopLeft(titleText.rectTransform, 150f, 190f, 900f, 120f);
        titleText.characterSpacing = 6f;
        Image bar = NewImage("TitleBar", root, Color.white);
        TopLeft(bar.rectTransform, 156f, 318f, 120f, 4f);
        songText = NewText("Song", root, "", 34f, Color.white, true);
        TopLeft(songText.rectTransform, 154f, 348f, 1100f, 48f);
        subText = NewText("Sub", root, "", 22f, TextSub, false);
        TopLeft(subText.rectTransform, 156f, 398f, 1100f, 34f);

        // 메뉴
        menuRoot = NewRect("Menu", root);
        TopLeft(menuRoot, 150f, 500f, 600f, 400f);
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            Row r = Row.Create(menuRoot, this, (i + 1).ToString("00"), items[i].Label, i, () => Hover(index), () => Choose(index));
            rows.Add(r);
        }

        // 조작 안내 (오른쪽 아래, 키캡 디자인)
        RectTransform hint = KeyHintUI.Build(root, regular, bold, new[]
        {
            new KeyHintUI.Item("SELECT", "^UP", "^DOWN"),
            new KeyHintUI.Item("CONFIRM", "ENTER"),
            new KeyHintUI.Item("RESUME", "ESC"),
        }, 19f, 1);
        hint.anchorMin = hint.anchorMax = new Vector2(1f, 0f);
        hint.anchoredPosition = new Vector2(-70f, 70f);
    }

    // =====================================================================
    //  보이기 / 숨기기
    // =====================================================================
    public void Show(string song, string sub)
    {
        songText.text = song ?? "";
        subText.text = sub ?? "";
        selected = 0;
        openedFrame = Time.frameCount;
        showT = 0f;
        group.alpha = 0f;
        blurImg.texture = null;
        blurImg.color = new Color(0f, 0f, 0f, 0.85f);
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        for (int i = 0; i < rows.Count; i++) rows[i].ResetIntro();
        Refresh();
        if (!capturing) StartCoroutine(CaptureRoutine());
    }

    public void Hide()
    {
        if (!gameObject.activeSelf) return;
        StopAllCoroutines();
        capturing = false;
        gameObject.SetActive(false);
        if (blurTex != null) { Destroy(blurTex); blurTex = null; }
    }

    // 지금 화면(멈춘 게임)을 찍어서 흐린 배경으로 (찍는 동안 이 창은 투명)
    private System.Collections.IEnumerator CaptureRoutine()
    {
        capturing = true;
        float a = group.alpha;
        group.alpha = 0f;
        yield return new WaitForEndOfFrame();
        Texture2D tex = PlayConfirmDialog.CaptureBlurred();
        capturing = false;
        if (tex != null)
        {
            if (blurTex != null) Destroy(blurTex);
            blurTex = tex;
            blurImg.texture = blurTex;
            blurImg.color = Color.white;
        }
        group.alpha = a;
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        if (!capturing)
        {
            showT = Mathf.Min(1f, showT + dt / 0.18f);
            group.alpha = 1f - Mathf.Pow(1f - showT, 3f);
        }
        for (int i = 0; i < rows.Count; i++) rows[i].Tick(dt, i == selected);

        if (Time.frameCount == openedFrame) return;           // 일시정지에 쓴 ESC 는 무시
        if (SettingsWindow.BlocksGameInput) return;            // 설정창이 열려 있음
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.upArrowKey.wasPressedThisFrame) { selected = (selected - 1 + rows.Count) % rows.Count; Refresh(); }
        if (kb.downArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) { selected = (selected + 1) % rows.Count; Refresh(); }
        if (kb.escapeKey.wasPressedThisFrame) { if (onResume != null) onResume(); return; }
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Choose(selected);
    }

    private void Hover(int i)
    {
        if (SettingsWindow.BlocksGameInput || i == selected) return;
        selected = i;
        Refresh();
    }

    private void Choose(int i)
    {
        if (SettingsWindow.BlocksGameInput || Time.frameCount == openedFrame) return;
        if (i < 0 || i >= items.Count) return;
        selected = i;
        Refresh();
        if (items[i].Action != null) items[i].Action();
    }

    private void Refresh() { }   // 모양은 Row.Tick 에서 부드럽게 따라감

    void OnDestroy() { if (blurTex != null) Destroy(blurTex); }

    // =====================================================================
    //  메뉴 한 줄: [번호] [이름]  — 고르면 흰 막대가 왼쪽에서 차오르고 글자가 검정으로, 오른쪽으로 살짝 밀림
    // =====================================================================
    private class Row : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        private const float W = 560f, H = 68f, Gap = 10f;
        private RectTransform rt, fillRt;
        private Image fill;
        private TextMeshProUGUI num, label;
        private Action onHover, onClick;
        private float lit, intro;
        private int index;

        public static Row Create(RectTransform parent, PauseMenu owner, string number, string text, int index, Action hover, Action click)
        {
            RectTransform r = NewRect("Item_" + text, parent);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(W, H);
            r.anchoredPosition = new Vector2(0f, -index * (H + Gap));
            Row row = r.gameObject.AddComponent<Row>();
            row.rt = r;
            row.index = index;
            row.onHover = hover;
            row.onClick = click;

            Image hit = r.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);                   // 마우스를 받는 투명 판
            hit.raycastTarget = true;

            row.fill = NewImage("Fill", r, Color.white);
            row.fillRt = row.fill.rectTransform;
            row.fillRt.anchorMin = new Vector2(0f, 0f);
            row.fillRt.anchorMax = new Vector2(0f, 1f);
            row.fillRt.pivot = new Vector2(0f, 0.5f);
            row.fillRt.sizeDelta = new Vector2(0f, 0f);

            row.num = owner.NewText("Num", r, number, 22f, TextSub, false);
            row.num.alignment = TextAlignmentOptions.MidlineLeft;
            Place(row.num.rectTransform, 24f, 70f);
            row.label = owner.NewText("Label", r, text, 36f, TextIdle, true);
            row.label.alignment = TextAlignmentOptions.MidlineLeft;
            row.label.characterSpacing = 4f;
            Place(row.label.rectTransform, 82f, W - 90f);
            return row;
        }

        private static void Place(RectTransform t, float x, float w)
        {
            t.anchorMin = new Vector2(0f, 0f);
            t.anchorMax = new Vector2(0f, 1f);
            t.pivot = new Vector2(0f, 0.5f);
            t.sizeDelta = new Vector2(w, 0f);
            t.anchoredPosition = new Vector2(x, 0f);
        }

        public void ResetIntro() { intro = 0f; }

        public void Tick(float dt, bool on)
        {
            // 나타날 때: 위에서부터 차례로 왼쪽에서 밀려 들어옴
            intro = Mathf.Min(1f, intro + dt / 0.22f);
            float delay = index * 0.12f;
            float ti = Mathf.Clamp01((intro - delay) / Mathf.Max(0.01f, 1f - delay));
            float ei = 1f - Mathf.Pow(1f - ti, 3f);

            lit = Mathf.MoveTowards(lit, on ? 1f : 0f, dt / 0.12f);
            float e = lit * lit * (3f - 2f * lit);
            fillRt.sizeDelta = new Vector2(W * e, 0f);
            fill.color = new Color(1f, 1f, 1f, ei);
            label.color = Color.Lerp(new Color(TextIdle.r, TextIdle.g, TextIdle.b, TextIdle.a * ei), new Color(TextOnWhite.r, TextOnWhite.g, TextOnWhite.b, ei), e);
            num.color = Color.Lerp(new Color(TextSub.r, TextSub.g, TextSub.b, TextSub.a * ei), new Color(0.35f, 0.35f, 0.35f, ei), e);
            rt.anchoredPosition = new Vector2(-40f * (1f - ei) + 14f * e, -index * (H + Gap));
        }

        public void OnPointerEnter(PointerEventData e) { if (onHover != null) onHover(); }
        public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left && onClick != null) onClick(); }
    }

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

    private TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color c, bool isBold)
    {
        TextMeshProUGUI t = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = isBold ? bold : regular;
        if (f != null) t.font = f;
        t.text = text;
        t.fontSize = size;
        // 강조 글꼴을 따로 받았으면 그 글꼴 그대로, 아니면 굵게
        t.fontStyle = isBold && (bold == null || bold == regular) ? FontStyles.Bold : FontStyles.Normal;
        t.color = c;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    private static void TopLeft(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, -y);
    }

    // 왼쪽 진함 → 오른쪽 투명 그라데이션
    private static Sprite hGrad;
    private static Sprite HGradient
    {
        get
        {
            if (hGrad != null) return hGrad;
            Texture2D tex = new Texture2D(128, 4, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[128 * 4];
            for (int x = 0; x < 128; x++)
            {
                float a = Mathf.Pow(1f - x / 127f, 1.6f);
                for (int y = 0; y < 4; y++) px[y * 128 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            hGrad = Sprite.Create(tex, new Rect(0, 0, 128, 4), new Vector2(0f, 0.5f), 100f);
            return hGrad;
        }
    }
}