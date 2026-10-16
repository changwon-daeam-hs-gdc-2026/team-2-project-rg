using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 우측 악곡 정렬표의 한 줄(Songbar)
//  그려지는 순서(아래 → 위):
//    Background(반투명 검정) → SelectedBar(selectedbar.png) → Jacket → 텍스트들 → LockOverlay
//  * 프리팹으로 만들 때도 Hierarchy 순서를 위와 같이 두면 된다.
//  * 프리팹을 안 넣으면 SongSelectManager 가 CreateDefault() 로 코드에서 자동 생성한다.
// ============================================================
public class SongRowUI : MonoBehaviour
{
    [Header("구성 요소 (Hierarchy 순서 = 그리는 순서)")]
    public RectTransform rect;
    public Image background;            // 반투명 검정 배경
    public Image selectedBar;           // 선택 표시 (selectedbar.png) - 배경 위, 텍스트 아래
    public Image jacket;                // squareimg/{곡ID}
    [Tooltip("곡을 상징하는 얇은 색 바 (선택). songs.json 의 BarColor 로 색이 칠해진다")]
    public Image colorBar;
    public TextMeshProUGUI titleText;   // 곡명 (+ SubName)
    public TextMeshProUGUI composerText;// 작곡가 (Composer 만)
    public TextMeshProUGUI easyText;
    public TextMeshProUGUI hardText;
    public TextMeshProUGUI insaneText;

    [Header("잠긴 곡 (선택)")]
    public GameObject lockOverlay;
    public Image lockIcon;

    [HideInInspector] public int boundVirtualIndex = int.MinValue;

    private SongData song;
    private bool isSelected;
    private float fillTimer;
    private float fillDuration;

    public SongData Song { get { return song; } }

    // 매니저가 생성 직후 한 번 호출: 행을 뷰포트 가로폭에 맞추고 선택 바를 '왼쪽→오른쪽 채우기' 이미지로 설정
    public void InitLayout(float height, Sprite selectedBarSprite)
    {
        if (rect == null) rect = (RectTransform)transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = Vector2.zero;

        if (selectedBar != null)
        {
            if (selectedBarSprite != null) selectedBar.sprite = selectedBarSprite;
            selectedBar.type = Image.Type.Filled;
            selectedBar.fillMethod = Image.FillMethod.Horizontal;
            selectedBar.fillOrigin = (int)Image.OriginHorizontal.Left;
            selectedBar.fillAmount = 1f;
            selectedBar.raycastTarget = false;
            selectedBar.enabled = false;
        }

        isSelected = false;
        boundVirtualIndex = int.MinValue;
    }

    public void Bind(int virtualIndex, SongData data, Sprite jacketSprite, string composerLine, bool locked, Sprite lockSprite,
                     Sprite selectedBarSprite, Color songColor)
    {
        boundVirtualIndex = virtualIndex;
        song = data;

        // 곡별 선택 그라데이션 (Sprites/SelectedFallback/{id}, 없으면 기본 selectedbar)
        if (selectedBar != null && selectedBarSprite != null) selectedBar.sprite = selectedBarSprite;
        if (colorBar != null) colorBar.color = songColor;

        if (jacket != null)
        {
            jacket.sprite = jacketSprite;
            jacket.color = jacketSprite != null ? Color.white : new Color(0.15f, 0.15f, 0.15f, 1f);
        }
        if (titleText != null) titleText.text = (data.Name ?? "") + (data.SubName ?? ""); // 우측: Name + SubName
        if (composerText != null) composerText.text = composerLine;

        if (easyText != null) easyText.text = FormatDifficulty(data.Difficulty_EASY);
        if (hardText != null) hardText.text = FormatDifficulty(data.Difficulty_HARD);
        if (insaneText != null) insaneText.text = FormatDifficulty(data.Difficulty_INSANE);

        if (lockOverlay != null) lockOverlay.SetActive(locked);
        if (lockIcon != null)
        {
            lockIcon.sprite = lockSprite;
            lockIcon.enabled = locked && lockSprite != null;
        }
    }

    // 선택된 난이도 열만 난이도 색으로, 나머지는 기본 색으로
    public void ApplyDifficultyColors(int selectedDiff, Color[] diffColors, Color normal)
    {
        Paint(easyText, 1);
        Paint(hardText, 2);
        Paint(insaneText, 3);

        void Paint(TextMeshProUGUI t, int d)
        {
            if (t == null) return;
            t.color = (d == selectedDiff) ? diffColors[d] : normal;
        }
    }

    // 선택 상태가 '새로' 켜질 때마다 selectedbar 가 왼쪽에서 오른쪽으로 그려진다.
    public void SetSelected(bool selected, float drawDuration)
    {
        if (selected == isSelected) return;
        isSelected = selected;
        if (selectedBar == null) return;

        selectedBar.enabled = selected;
        if (selected)
        {
            fillDuration = Mathf.Max(0.0001f, drawDuration);
            fillTimer = 0f;
            selectedBar.fillAmount = drawDuration <= 0f ? 1f : 0f;
        }
    }

    public void Tick(float dt)
    {
        if (!isSelected || selectedBar == null || fillTimer >= fillDuration) return;
        fillTimer += dt;
        float p = Mathf.Clamp01(fillTimer / fillDuration);
        selectedBar.fillAmount = 1f - Mathf.Pow(1f - p, 3f); // ease-out
    }

    // ===== 난이도 표기 공용 함수 =====
    public static bool IsDifficultyAvailable(string raw)
    {
        return !string.IsNullOrWhiteSpace(raw) && raw.Trim() != "00";
    }

    // "03" → "3", "00"/빈 값 → "" (사진의 I Wanna HD 칸처럼 비워 둠)
    public static string FormatDifficulty(string raw)
    {
        if (!IsDifficultyAvailable(raw)) return "";
        string t = raw.Trim();
        return int.TryParse(t, out int v) ? v.ToString() : t;
    }

    // =====================================================================
    //  프리팹이 없을 때 쓰는 기본 행 (1920x1080 기준, 사진의 레이아웃을 본뜸)
    // =====================================================================
    public static SongRowUI CreateDefault(Transform parent, TMP_FontAsset font, float rowHeight)
    {
        GameObject go = new GameObject("SongRow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        SongRowUI row = go.AddComponent<SongRowUI>();
        row.rect = (RectTransform)go.transform;

        row.background = NewImage("Background", row.rect, new Color(0f, 0f, 0f, 0.28f));
        Stretch(row.background.rectTransform);

        row.selectedBar = NewImage("SelectedBar", row.rect, Color.white);
        Stretch(row.selectedBar.rectTransform);

        row.jacket = NewImage("Jacket", row.rect, Color.white);
        RectTransform jr = row.jacket.rectTransform;
        jr.anchorMin = jr.anchorMax = new Vector2(0f, 0.5f);
        jr.pivot = new Vector2(0f, 0.5f);
        jr.anchoredPosition = Vector2.zero;
        jr.sizeDelta = new Vector2(rowHeight, rowHeight);
        row.jacket.preserveAspect = true;

        row.colorBar = NewImage("ColorBar", row.rect, Color.white);
        RectTransform cr = row.colorBar.rectTransform;
        cr.anchorMin = cr.anchorMax = new Vector2(0f, 0.5f);
        cr.pivot = new Vector2(0f, 0.5f);
        cr.anchoredPosition = new Vector2(rowHeight, 0f);
        cr.sizeDelta = new Vector2(6f, rowHeight);

        float textLeft = rowHeight + 24f;
        float textRight = 320f;
        row.titleText = NewText("Title", row.rect, font, 36f, TextAlignmentOptions.Left);
        Band(row.titleText.rectTransform, textLeft, textRight, 8f, 46f);
        row.composerText = NewText("Composer", row.rect, font, 15f, TextAlignmentOptions.Left);
        Band(row.composerText.rectTransform, textLeft, textRight, -24f, 22f);

        row.easyText = NewDiffText("Easy", row.rect, font, -267f);
        row.hardText = NewDiffText("Hard", row.rect, font, -185f);
        row.insaneText = NewDiffText("Insane", row.rect, font, -102f);

        Image lockBg = NewImage("LockOverlay", row.rect, new Color(0f, 0f, 0f, 0.55f));
        Stretch(lockBg.rectTransform);
        row.lockOverlay = lockBg.gameObject;
        row.lockIcon = NewImage("LockIcon", lockBg.rectTransform, Color.white);
        RectTransform lr = row.lockIcon.rectTransform;
        lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0.5f, 0.5f);
        lr.sizeDelta = new Vector2(rowHeight * 0.6f, rowHeight * 0.6f);
        row.lockIcon.preserveAspect = true;
        row.lockOverlay.SetActive(false);

        return row;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    private static TextMeshProUGUI NewDiffText(string name, Transform parent, TMP_FontAsset font, float xFromRight)
    {
        TextMeshProUGUI t = NewText(name, parent, font, 44f, TextAlignmentOptions.Center);
        RectTransform r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(xFromRight, 0f);
        r.sizeDelta = new Vector2(90f, 60f);
        return t;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    // 가로로는 행 폭에 맞춰 늘어나고(왼쪽 left, 오른쪽 right 만큼 여백), 세로로는 중심 centerY / 높이 height
    private static void Band(RectTransform r, float left, float right, float centerY, float height)
    {
        r.anchorMin = new Vector2(0f, 0.5f);
        r.anchorMax = new Vector2(1f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.offsetMin = new Vector2(left, centerY - height * 0.5f);
        r.offsetMax = new Vector2(-right, centerY + height * 0.5f);
    }
}