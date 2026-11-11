using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 우측 악곡 정렬표의 한 줄(Songbar)
//  그려지는 순서(아래 → 위):
//    Background(반투명 검정) → SelectedBar(selectedbar.png) → Jacket → 텍스트들 → LockOverlay
//  * 프리팹으로 만들 때도 Hierarchy 순서를 위와 같이 두면 된다.
//  * 프리팹을 안 넣으면 SongSelectManager 가 CreateDefault() 로 코드에서 자동 생성한다.
//  * SPECIAL 목록 전용 Songbar (Special Row Prefab): 난이도 글자는 하나 (Special Text, 비우면 Hard Text 를 씀).
//    HARD 자리에 두고 폭을 넓게 — "15" 같은 숫자뿐 아니라 "DIVERSITY" 같은 글자 난이도도 들어감.
//    Easy Text · Insane Text 는 비워 둬도 됨 (있으면 빈 글자로 둠)
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
    [Tooltip("SPECIAL 전용 Songbar 에서만: 난이도 하나를 넓게 표시하는 글자. 비우면 Hard Text 를 씀")]
    public TextMeshProUGUI specialText;

    // SPECIAL 목록용 줄인가 (매니저가 정함). 이때는 난이도 하나(SPECIAL)만 솜사탕 그라데이션으로 표시
    [HideInInspector] public bool isSpecialRow;
    private TextMeshProUGUI SpecialLabel { get { return specialText != null ? specialText : hardText; } }

    [Header("잠긴 곡 (선택)")]
    [Tooltip("잠긴 곡(ExistSongbar 2)일 때 켜지는 오브젝트. 비워 두면 자켓 위에 어두운 막 + 자물쇠를 코드로 만듦")]
    public GameObject lockOverlay;
    [Tooltip("잠금 그림 (Sprites/etc/Locked). 비워 두거나 그림이 없으면 코드로 그린 자물쇠")]
    public Image lockIcon;

    // 코드로 만드는 잠금 표시 (프리팹에 잠금 표시가 없을 때): 자켓 위 어두운 막 + 가운데 자물쇠
    private GameObject autoLock;
    private RectTransform autoLockIcon;
    private float lockPulse = -1f;          // 잠긴 곡에서 ENTER: 자물쇠가 흔들림 (0 → 0.45초)

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
        if (titleText != null) titleText.text = data.Name ?? "";   // 우측: 곡명만 (부제 SubName 은 표시하지 않음)
        if (composerText != null) composerText.text = composerLine;

        if (isSpecialRow)
        {
            // SPECIAL 목록: 난이도 하나만 (숫자 또는 "DIVERSITY" 같은 글자), 흰색
            if (easyText != null) easyText.text = "";
            if (insaneText != null) insaneText.text = "";
            if (hardText != null && hardText != SpecialLabel) hardText.text = "";
            TextMeshProUGUI st = SpecialLabel;
            if (st != null)
            {
                st.richText = true;
                st.text = FormatDifficulty(data.Difficulty_SPECIAL);       // 흰색 (그라데이션 없음)
            }
        }
        else
        {
            if (easyText != null) easyText.text = FormatDifficulty(data.Difficulty_EASY);
            if (hardText != null) hardText.text = FormatDifficulty(data.Difficulty_HARD);
            if (insaneText != null) insaneText.text = FormatDifficulty(data.Difficulty_INSANE);
        }

        // ---- 난이도별 잠금 (LockedValue): 잠긴 난이도는 숫자 대신 작은 자물쇠 ----
        bool anyDiff = false;
        if (isSpecialRow) anyDiff |= SetDiffLock(SpecialLabel, !locked && DifficultyHelper.IsLevelLocked(data, DifficultyHelper.Special) && IsDifficultyAvailable(data.Difficulty_SPECIAL));
        else
        {
            anyDiff |= SetDiffLock(easyText, !locked && DifficultyHelper.IsLevelLocked(data, 1) && IsDifficultyAvailable(data.Difficulty_EASY));
            anyDiff |= SetDiffLock(hardText, !locked && DifficultyHelper.IsLevelLocked(data, 2) && IsDifficultyAvailable(data.Difficulty_HARD));
            anyDiff |= SetDiffLock(insaneText, !locked && DifficultyHelper.IsLevelLocked(data, 3) && IsDifficultyAvailable(data.Difficulty_INSANE));
        }
        // (SPECIAL 줄로 쓰다 기존 줄로 바뀐 경우 등) 이번에 쓰지 않는 칸의 자물쇠는 숨김
        foreach (var kv in diffLocks)
        {
            bool used = isSpecialRow ? kv.Key == SpecialLabel : (kv.Key == easyText || kv.Key == hardText || kv.Key == insaneText);
            if (!used && kv.Value != null) kv.Value.gameObject.SetActive(false);
        }

        // ---- 잠긴 곡: 줄 전체에 어두운 막 + 난이도 자리에 자물쇠 · LOCKED 글자 ----
        if (locked)
        {
            if (easyText != null) easyText.text = "";
            if (hardText != null) hardText.text = "";
            if (insaneText != null) insaneText.text = "";
            if (specialText != null) specialText.text = "";
            if (lockTag == null) BuildLockTag();
        }
        if (rowFilm != null || locked)
        {
            if (rowFilm == null)
            {
                rowFilm = LockArt.Fill("LockedFilm", transform, new Color(0f, 0f, 0f, 0.5f));   // 줄 전체를 덮는 어두운 막
            }
            rowFilm.gameObject.SetActive(locked);
            if (locked) rowFilm.transform.SetAsLastSibling();
        }
        if (lockTag != null)
        {
            lockTag.gameObject.SetActive(locked);
            if (locked) { lockTag.SetAsLastSibling(); PlaceLockTag(); }   // 막 위에
        }

        if (lockOverlay != null) lockOverlay.SetActive(locked);
        if (lockIcon != null)
        {
            lockIcon.sprite = lockSprite;
            lockIcon.enabled = locked && lockSprite != null;
        }
        // 프리팹에 잠금 표시가 없거나 그림이 없으면 코드로 만든 잠금 표시
        bool needAuto = locked && (lockIcon == null || lockSprite == null);
        if (needAuto && autoLock == null) BuildAutoLock();
        if (autoLock != null) autoLock.SetActive(needAuto);
        if (!locked) lockPulse = -1f;
    }

    private void BuildAutoLock()
    {
        RectTransform host = jacket != null ? jacket.rectTransform : (RectTransform)transform;
        autoLock = new GameObject("AutoLock", typeof(RectTransform));
        autoLock.layer = gameObject.layer;
        RectTransform r = (RectTransform)autoLock.transform;
        r.SetParent(host, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        Image dim = autoLock.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.45f);
        dim.raycastTarget = false;

        // 가운데 자물쇠 (흔들 때 이것이 움직임)
        float size = Mathf.Min(host.rect.width, host.rect.height);
        if (size < 20f) size = 80f;   // 아직 크기가 정해지지 않았을 때
        Image li = LockArt.Img("Lock", r, new Color(1f, 1f, 1f, 0.9f));
        li.sprite = GameModeIconArt.Lock();
        li.preserveAspect = true;
        autoLockIcon = li.rectTransform;
        autoLockIcon.anchorMin = autoLockIcon.anchorMax = autoLockIcon.pivot = new Vector2(0.5f, 0.5f);
        autoLockIcon.sizeDelta = new Vector2(size * 0.42f, size * 0.42f);
    }

    // ---- 난이도별 잠금 자물쇠 (난이도 글자 칸마다 하나, 처음 필요할 때 만듦) ----
    private readonly System.Collections.Generic.Dictionary<TextMeshProUGUI, Image> diffLocks =
        new System.Collections.Generic.Dictionary<TextMeshProUGUI, Image>();

    // on 이면 그 칸 숫자를 지우고 자물쇠를 보임. 반환: on
    private bool SetDiffLock(TextMeshProUGUI t, bool on)
    {
        if (t == null) return false;
        Image img;
        diffLocks.TryGetValue(t, out img);
        if (on && img == null)
        {
            img = LockArt.Img("DiffLock", t.rectTransform, new Color(1f, 1f, 1f, 0.75f));
            img.sprite = GameModeIconArt.Lock();
            img.preserveAspect = true;
            diffLocks[t] = img;
        }
        if (img != null)
        {
            img.gameObject.SetActive(on);
            if (on)
            {
                // 글자 칸 가운데 (글자 정렬이 왼쪽 / 오른쪽이어도 칸 가운데), 크기는 글자 크기에 맞춤
                RectTransform r = img.rectTransform;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
                Vector2 c = t.rectTransform.rect.center;
                float sz = Mathf.Max(16f, t.fontSize * 0.85f);
                r.sizeDelta = new Vector2(sz, sz);
                r.anchoredPosition = c;
            }
        }
        if (on) t.text = "";
        return on;
    }

    // ---- 난이도 자리의 [자물쇠 LOCKED] 글자 (상자 없이) ----
    private RectTransform lockTag;
    private Image rowFilm;

    private void BuildLockTag()
    {
        GameObject go = new GameObject("LockedTag", typeof(RectTransform));
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        lockTag = (RectTransform)go.transform;
        lockTag.anchorMin = lockTag.anchorMax = lockTag.pivot = new Vector2(0.5f, 0.5f);
        Color c = new Color(1f, 1f, 1f, 0.85f);
        Image ic = LockArt.Img("Icon", lockTag, c);
        ic.sprite = GameModeIconArt.Lock(); ic.preserveAspect = true;
        RectTransform ir = ic.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
        ir.anchoredPosition = new Vector2(0f, 1f); ir.sizeDelta = new Vector2(22f, 22f);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.layer = gameObject.layer;
        tg.transform.SetParent(lockTag, false);
        TextMeshProUGUI t = tg.AddComponent<TextMeshProUGUI>();
        TextMeshProUGUI refT = hardText != null ? hardText : titleText;
        if (refT != null) t.font = refT.font;
        t.text = "LOCKED"; t.fontSize = 20f; t.fontStyle = FontStyles.Bold; t.characterSpacing = 8f;
        t.color = c; t.alignment = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
        RectTransform tr = t.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(30f, 0f); tr.offsetMax = Vector2.zero;
        lockTag.sizeDelta = new Vector2(30f + t.GetPreferredValues("LOCKED").x, 32f);
    }

    // 꼬리표 위치: 난이도 칸들(EZ ~ INS, SPECIAL 줄이면 그 칸)의 가운데
    private void PlaceLockTag()
    {
        if (lockTag == null) return;
        RectTransform a = isSpecialRow ? (SpecialLabel != null ? SpecialLabel.rectTransform : null) : (easyText != null ? easyText.rectTransform : null);
        RectTransform b = isSpecialRow ? a : (insaneText != null ? insaneText.rectTransform : a);
        if (a == null) { lockTag.anchoredPosition = new Vector2(((RectTransform)transform).rect.width * 0.3f, 0f); return; }
        Vector3 pa = transform.InverseTransformPoint(a.TransformPoint(a.rect.center));
        Vector3 pb = b != null ? transform.InverseTransformPoint(b.TransformPoint(b.rect.center)) : pa;
        RectTransform rt = (RectTransform)transform;
        Vector2 c = (Vector2)((pa + pb) * 0.5f);
        // 앵커가 가운데(0.5, 0.5)이므로 줄의 기준점(pivot) 차이만큼 보정
        c += new Vector2((rt.pivot.x - 0.5f) * rt.rect.width, (rt.pivot.y - 0.5f) * rt.rect.height);
        lockTag.anchoredPosition = c;
    }

    // 잠긴 곡에서 ENTER 를 누르면 자물쇠가 좌우로 흔들림
    public void PulseLock() { lockPulse = 0f; }

    // 선택된 난이도 열만 난이도 색으로, 나머지는 기본 색으로
    public void ApplyDifficultyColors(int selectedDiff, Color[] diffColors, Color normal)
    {
        if (isSpecialRow)
        {
            // 색은 글자 서식(그라데이션)에 들어 있으므로 바탕 색은 흰색
            if (SpecialLabel != null) SpecialLabel.color = Color.white;
            return;
        }
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
        if (lockTag != null && lockTag.gameObject.activeSelf) PlaceLockTag();   // 줄 크기가 늦게 정해져도 따라감
        // 잠금 흔들림
        if (lockPulse >= 0f)
        {
            lockPulse += dt;
            float k = Mathf.Clamp01(lockPulse / 0.45f);
            float shake = Mathf.Sin(lockPulse * 50f) * (1f - k) * 7f;
            float sc = 1f + 0.18f * (1f - k) * (1f - k);
            RectTransform t = autoLockIcon != null && autoLock != null && autoLock.activeSelf ? autoLockIcon
                            : lockIcon != null && lockIcon.enabled ? lockIcon.rectTransform : null;
            if (t != null)
            {
                t.localRotation = Quaternion.Euler(0f, 0f, shake);
                t.localScale = Vector3.one * sc;
                if (k >= 1f) { t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }
            }
            if (lockTag != null) lockTag.localRotation = Quaternion.Euler(0f, 0f, shake * 0.4f);
            if (k >= 1f) { lockPulse = -1f; if (lockTag != null) lockTag.localRotation = Quaternion.identity; }
        }
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
    public static SongRowUI CreateDefault(Transform parent, TMP_FontAsset font, float rowHeight, bool special = false)
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

        if (special)
        {
            // SPECIAL 전용: HARD 자리를 가운데로, EZ ~ INS 세 칸을 합친 넓이 (글자 난이도는 자동으로 작아짐)
            row.specialText = NewDiffText("Special", row.rect, font, -185f);
            row.specialText.rectTransform.sizeDelta = new Vector2(250f, 60f);
            row.specialText.enableAutoSizing = true;
            row.specialText.fontSizeMax = 44f;
            row.specialText.fontSizeMin = 22f;
            row.specialText.characterSpacing = 2f;
            row.isSpecialRow = true;
            textRight = 330f;
            Band(row.titleText.rectTransform, textLeft, textRight, 8f, 46f);
            Band(row.composerText.rectTransform, textLeft, textRight, -24f, 22f);
        }
        else
        {
            row.easyText = NewDiffText("Easy", row.rect, font, -267f);
            row.hardText = NewDiffText("Hard", row.rect, font, -185f);
            row.insaneText = NewDiffText("Insane", row.rect, font, -102f);
        }

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