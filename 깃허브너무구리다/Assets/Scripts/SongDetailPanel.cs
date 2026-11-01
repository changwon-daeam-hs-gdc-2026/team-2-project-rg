using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 곡 선택 화면: 좌측 자켓 아래 플레이 설정 (코드로 생성, 씬 설정 필요 없음)
//
//   ┌──────────────────────────────────────────┐
//   │    EASY    │    HARD    │   INSANE   │   ← 난이도 바: 위에 이름, 아래에 숫자 (있는 그대로)
//   │     3      │     7      │░░░  11  ░░░│      고른 칸: 아래에 난이도 색 그라데이션 밑줄 (가운데가 진하고 양끝으로 옅어짐)
//   └────────────────────────━━━━━━━━━━━━━━┘      + 밑줄 위로 은은한 색 빛, 이름이 난이도 색으로 (칸 전체를 덮지 않음)
//   ┌──────────────────────────────────────────┐
//   │  SPEED                              4.5  │   ← 배속: 왼쪽 절반 누르면 -0.1, 오른쪽 절반 +0.1
//   └──────────────────────────────────────────┘
//   ┌────────────────────┐ ┌────────────────────┐
//   │  MV            ON  │ │  AUTO PLAY    OFF  │   ← 칸을 누르면 켜고 끔
//   └━━━━━━━━━━━━━━━━━━━━┘ └────────────────────┘      켜지면 아래 선이 색으로 펼쳐짐
//
//  - 납작한 어두운 칸 + 작은 모서리, 그림자 · 빛 효과 없음. 색은 난이도 블록과 켜진 상태에만.
//  - 키보드 조작(↑↓ / F1 F2 / F / A)과 같은 기능을 마우스로도 할 수 있다 (씬에 EventSystem 필요)
//  - 위치: 자켓 · 곡명 · 작곡가 · 길이 글자 중 가장 아래 끝에서 아래로, 자켓과 같은 폭
//    (화면 아래가 모자라면 전체를 줄여서 맞춤)
// ============================================================
public class SongDetailPanel : MonoBehaviour
{
    private static readonly Color BoxColor = new Color(0.02f, 0.025f, 0.04f, 0.6f);
    private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color TextMain = new Color(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.5f);
    private static readonly Color TextDim = new Color(1f, 1f, 1f, 0.18f);
    private static readonly Color MvColor = new Color(0.36f, 0.88f, 0.90f, 1f);   // #5ce1e6
    private static readonly Color AutoColor = new Color(1f, 0.80f, 0.36f, 1f);
    private const float Gap = 20f;            // 위쪽 곡 정보와의 간격
    private const float RowGap = 8f;
    private const float DiffH = 84f, SpeedH = 54f, ToggleH = 54f;
    private const float Radius = 6f;          // 모든 칸 · 선택 블록 공통 모서리 반지름
    private const float NameValueGap = 4f;    // 난이도 이름(위) ↔ 숫자(아래) 사이 세로 간격
    private const float PadX = 18f;           // 칸 안 좌우 여백
    private const float BottomMargin = 28f;

    public Action<int> OnSelectDifficulty;   // 1 EASY 2 HARD 3 INSANE
    public Action<double> OnSpeedDelta;      // 배속 증감 (0.1 단위)
    public Action OnToggleMv;
    public Action OnToggleAuto;

    private RectTransform rt;
    private RectTransform[] refs;
    private TMP_FontAsset font;
    private Sprite round;
    private float fullHeight;

    // 난이도 바
    private RectTransform diffBox;
    private Image diffBlock;      // 고른 칸 아래 그라데이션 밑줄
    private Image diffGlow;       // 밑줄 위로 옅게 번지는 빛
    private const float GlowTop = 1.0f;   // 위로 번지는 빛이 닿는 높이 (칸 높이 비율, 1 = 칸 꼭대기까지)
    private readonly TextMeshProUGUI[] diffName = new TextMeshProUGUI[4];
    private readonly TextMeshProUGUI[] diffValue = new TextMeshProUGUI[4];
    private readonly Image[] diffSep = new Image[2];
    private readonly bool[] diffAvailable = new bool[4];
    private int selLevel;
    private float blockPos = -1f;
    private Color blockColor = Color.white, blockTarget = Color.white;

    // 배속
    private TextMeshProUGUI speedValue;

    // MV / AUTO PLAY
    private class Switch { public Image box, bar, baseLine; public TextMeshProUGUI label, state; public float on, target; public Color color; public bool enabled = true; }
    private Switch mv, auto;

    private string lastKey = "";

    public static SongDetailPanel Create(Transform parent, TMP_FontAsset font, params RectTransform[] refs)
    {
        SettingsUIKit.EnsureFont();
        RectTransform r = SettingsUIKit.Rect("SongPlayOptions", parent);
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0f, 1f);
        SongDetailPanel p = r.gameObject.AddComponent<SongDetailPanel>();
        p.rt = r;
        p.font = font;
        var list = new List<RectTransform>();
        foreach (var x in refs) if (x != null) list.Add(x);
        p.refs = list.ToArray();
        p.round = MakeRoundSprite();
        p.Build();
        return p;
    }

    // =====================================================================
    //  만들기
    // =====================================================================
    private void Build()
    {
        float y = 0f;

        // ---- 난이도 바 (한 상자, 3칸) ----
        //  상자 자체를 마스크로 써서 색 블록이 둥근 모서리 모양대로 잘림 → 바 안에 빈틈 없이 딱 맞게
        Image dbox = Box("Difficulty", 0f, 1f, y, DiffH, false);
        dbox.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        diffBox = dbox.rectTransform;
        // 선택 표시: 칸을 덮지 않고 아래쪽 그라데이션 밑줄 + 위로 옅게 번지는 빛
        diffGlow = SettingsUIKit.Img("SelectedGlow", diffBox, Color.clear);
        diffGlow.sprite = UpGlowSprite();
        diffGlow.rectTransform.anchorMin = new Vector2(0f, 0f);
        diffGlow.rectTransform.anchorMax = new Vector2(1f / 3f, GlowTop);
        diffGlow.rectTransform.offsetMin = diffGlow.rectTransform.offsetMax = Vector2.zero;
        diffBlock = SettingsUIKit.Img("SelectedLine", diffBox, Color.clear);
        diffBlock.sprite = NoteArt.HBellSprite;
        diffBlock.rectTransform.anchorMin = new Vector2(0f, 0f);
        diffBlock.rectTransform.anchorMax = new Vector2(1f / 3f, 0f);
        diffBlock.rectTransform.pivot = new Vector2(0.5f, 0f);
        diffBlock.rectTransform.offsetMin = new Vector2(2f, 5f);
        diffBlock.rectTransform.offsetMax = new Vector2(-2f, 10f);
        for (int i = 0; i < 2; i++)
        {
            diffSep[i] = SettingsUIKit.Img("Sep", diffBox, LineColor);
            RectTransform s = diffSep[i].rectTransform;
            s.anchorMin = new Vector2((i + 1) / 3f, 0.25f);
            s.anchorMax = new Vector2((i + 1) / 3f, 0.75f);
            s.offsetMin = new Vector2(-0.5f, 0f);
            s.offsetMax = new Vector2(0.5f, 0f);
        }
        for (int d = 1; d <= 3; d++)
        {
            int level = d;
            float x0 = (d - 1) / 3f, x1 = d / 3f;
            // 두 줄: 위에 이름(작게), 아래에 숫자(크게 · 굵게), 둘 다 칸 가운데 정렬
            diffName[d] = Txt(diffBox, "", 14, TextAlignmentOptions.Bottom, TextSub);
            diffName[d].characterSpacing = 4f;
            Span(diffName[d].rectTransform, x0, x1, 0.60f, 0.88f, 6f, 6f);
            diffName[d].rectTransform.offsetMin = new Vector2(6f, NameValueGap * 0.5f);
            diffValue[d] = Txt(diffBox, "", 30, TextAlignmentOptions.Top, TextMain);
            diffValue[d].fontStyle = FontStyles.Bold;
            Span(diffValue[d].rectTransform, x0, x1, 0.10f, 0.60f, 6f, 6f);
            diffValue[d].rectTransform.offsetMax = new Vector2(-6f, -NameValueGap * 0.5f);
            Hit(diffBox, x0, x1, () => { if (diffAvailable[level] && OnSelectDifficulty != null) OnSelectDifficulty(level); });
        }
        y += DiffH + RowGap;

        // ---- 배속 (한 상자: 왼쪽 절반 -0.1 / 오른쪽 절반 +0.1) ----
        RectTransform sp = Box("Speed", 0f, 1f, y, SpeedH, false).rectTransform;
        TextMeshProUGUI sl = Txt(sp, "SPEED", 14, TextAlignmentOptions.MidlineLeft, TextSub);
        sl.characterSpacing = 4f;
        Span(sl.rectTransform, 0f, 1f, 0f, 1f, PadX, PadX);
        speedValue = Txt(sp, "", 22, TextAlignmentOptions.MidlineRight, TextMain);
        speedValue.fontStyle = FontStyles.Bold;
        Span(speedValue.rectTransform, 0f, 1f, 0f, 1f, PadX, PadX);
        Hit(sp, 0f, 0.5f, () => Speed(-0.1));
        Hit(sp, 0.5f, 1f, () => Speed(0.1));
        y += SpeedH + RowGap;

        // ---- MV · AUTO PLAY (반반) ----
        mv = MakeSwitch("MV", 0f, 0.5f, y, MvColor, () => { if (mv.enabled && OnToggleMv != null) OnToggleMv(); });
        auto = MakeSwitch("AUTO PLAY", 0.5f, 1f, y, AutoColor, () => { if (OnToggleAuto != null) OnToggleAuto(); });
        y += ToggleH;

        fullHeight = y;
    }

    // "MV            ON" + 아래 선 (켜지면 색 선이 가운데에서 펼쳐짐)
    private Switch MakeSwitch(string label, float x0, float x1, float y, Color color, Action onClick)
    {
        Switch s = new Switch { color = color };
        s.box = Box(label, x0, x1, y, ToggleH, true);
        s.box.gameObject.AddComponent<Mask>().showMaskGraphic = true; // 아래 선이 둥근 모서리를 따라 잘리도록
        RectTransform b = s.box.rectTransform;
        b.offsetMin = new Vector2(b.offsetMin.x + (x0 > 0f ? RowGap * 0.5f : 0f), b.offsetMin.y);
        b.offsetMax = new Vector2(b.offsetMax.x - (x1 < 1f ? RowGap * 0.5f : 0f), b.offsetMax.y);
        AddButton(s.box.gameObject, onClick);

        s.label = Txt(b, label, 14, TextAlignmentOptions.MidlineLeft, TextSub);
        s.label.characterSpacing = 3f;
        Span(s.label.rectTransform, 0f, 1f, 0f, 1f, PadX, 60f);
        s.state = Txt(b, "", 16, TextAlignmentOptions.MidlineRight, TextSub);
        s.state.fontStyle = FontStyles.Bold;
        s.state.characterSpacing = 2f;
        Span(s.state.rectTransform, 0f, 1f, 0f, 1f, PadX, PadX);

        // 아래 선: 항상 보이는 가는 선 + 켜졌을 때 색 선
        s.baseLine = SettingsUIKit.Img("BaseLine", b, LineColor);
        BottomLine(s.baseLine.rectTransform, 1f);
        s.bar = SettingsUIKit.Img("Bar", b, Color.clear);
        BottomLine(s.bar.rectTransform, 2f);
        return s;
    }

    private void Speed(double d) { if (OnSpeedDelta != null) OnSpeedDelta(d); }


    // =====================================================================
    //  상태 반영 (바뀌었을 때만)
    // =====================================================================
    public void Show(SongData song, int level, bool mvOn, bool hasMv)
    {
        if (song == null) return;
        double speed = SettingsStore.Current.SettingSpeed;
        bool isAuto = CoreManager.Instance != null && CoreManager.Instance.IsAutoPlay;
        string key = song.Id + "|" + level + "|" + mvOn + "|" + hasMv + "|" + speed.ToString("0.0", CultureInfo.InvariantCulture) + "|" + isAuto;
        if (key == lastKey) return;
        lastKey = key;

        for (int d = 1; d <= 3; d++)
        {
            string val = DifficultyHelper.GetValue(song, d);
            diffAvailable[d] = SongRowUI.IsDifficultyAvailable(val);
            bool sel = d == level && diffAvailable[d];
            diffName[d].text = DifficultyHelper.GetName(d);
            diffValue[d].text = diffAvailable[d] ? SongRowUI.FormatDifficulty(val) : "-";   // 있는 그대로 (9 → 9, 두 자리로 채우지 않음)
            Color dc = DifficultyHelper.GetColor(d);
            diffName[d].color = sel ? new Color(dc.r, dc.g, dc.b, 1f) : (diffAvailable[d] ? TextSub : TextDim);
            diffValue[d].color = diffAvailable[d] ? TextMain : TextDim;
            if (sel) { selLevel = d; blockTarget = DifficultyHelper.GetColor(d); }
        }
        // 구분선은 항상 보임 (선택 표시가 칸을 덮지 않으므로)
        for (int i = 0; i < 2; i++) diffSep[i].enabled = true;

        speedValue.text = speed.ToString("0.0", CultureInfo.InvariantCulture);

        SetSwitch(mv, hasMv, hasMv && mvOn, hasMv ? null : "NONE");
        SetSwitch(auto, true, isAuto, null);
    }

    private static void SetSwitch(Switch s, bool enabled, bool on, string overrideText)
    {
        s.enabled = enabled;
        s.target = on ? 1f : 0f;
        s.state.text = overrideText ?? (on ? "ON" : "OFF");
        s.state.color = !enabled ? TextDim : (on ? s.color : TextSub);
        s.label.color = !enabled ? TextDim : (on ? TextMain : TextSub);
        s.box.raycastTarget = enabled;
    }

    // =====================================================================
    //  매 프레임 (위치 · 움직임)
    // =====================================================================
    void LateUpdate()
    {
        PlaceUnderInfo();
        float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);

        // 난이도 색 블록: 고른 칸으로 미끄러져 이동 + 색이 바뀜
        if (selLevel >= 1)
        {
            float target = (selLevel - 1) / 3f;
            blockPos = blockPos < 0f ? target : Mathf.Lerp(blockPos, target, k);
            if (Mathf.Abs(blockPos - target) < 0.0005f) blockPos = target;
            diffBlock.rectTransform.anchorMin = new Vector2(blockPos, 0f);
            diffBlock.rectTransform.anchorMax = new Vector2(blockPos + 1f / 3f, 0f);
            diffGlow.rectTransform.anchorMin = new Vector2(blockPos, 0f);
            diffGlow.rectTransform.anchorMax = new Vector2(blockPos + 1f / 3f, GlowTop);
            blockColor = Color.Lerp(blockColor, blockTarget, k);
            diffBlock.color = new Color(blockColor.r, blockColor.g, blockColor.b, 1f);
            diffGlow.color = new Color(blockColor.r, blockColor.g, blockColor.b, 0.30f);
        }
        diffBlock.enabled = diffGlow.enabled = selLevel >= 1 && diffAvailable[selLevel];

        TickSwitch(mv, k);
        TickSwitch(auto, k);
    }

    private static void TickSwitch(Switch s, float k)
    {
        if (s == null) return;
        s.on = Mathf.Lerp(s.on, s.target, k);
        if (Mathf.Abs(s.on - s.target) < 0.001f) s.on = s.target;
        RectTransform r = s.bar.rectTransform;
        r.anchorMin = new Vector2(0.5f - 0.5f * s.on, 0f);
        r.anchorMax = new Vector2(0.5f + 0.5f * s.on, 0f);
        s.bar.enabled = s.on > 0.001f;
        s.bar.color = new Color(s.color.r, s.color.g, s.color.b, s.on);
    }

    // 자켓 · 곡명 등 가장 아래 끝 바로 밑, 자켓과 같은 폭
    private readonly Vector3[] corners = new Vector3[4];
    private void PlaceUnderInfo()
    {
        Transform parent = rt.parent;
        if (parent == null || refs == null || refs.Length == 0) return;
        float minY = float.MaxValue, left = float.MaxValue, right = float.MinValue;
        for (int i = 0; i < refs.Length; i++)
        {
            if (refs[i] == null || !refs[i].gameObject.activeInHierarchy) continue;
            refs[i].GetWorldCorners(corners);
            for (int k = 0; k < 4; k++)
            {
                Vector3 l = parent.InverseTransformPoint(corners[k]);
                minY = Mathf.Min(minY, l.y);
                if (i == 0) { left = Mathf.Min(left, l.x); right = Mathf.Max(right, l.x); }
            }
        }
        if (minY == float.MaxValue || left == float.MaxValue) return;

        float top = minY - Gap;
        float scale = 1f;
        Canvas c = rt.GetComponentInParent<Canvas>();
        if (c != null)
        {
            ((RectTransform)c.rootCanvas.transform).GetWorldCorners(corners);
            float bottom = parent.InverseTransformPoint(corners[0]).y + BottomMargin;
            float avail = top - bottom;
            if (avail > 40f && avail < fullHeight) scale = avail / fullHeight;
        }
        rt.localScale = new Vector3(scale, scale, 1f);
        rt.sizeDelta = new Vector2(Mathf.Max(260f, right - left) / scale, fullHeight);
        rt.localPosition = new Vector3(left, top, 0f);
    }

    // =====================================================================
    //  도우미
    // =====================================================================
    // 패널 가로 [x0, x1] 비율, 위에서 y 만큼 아래, 높이 h 인 어두운 상자
    private Image Box(string name, float x0, float x1, float y, float h, bool raycast)
    {
        Image img = SettingsUIKit.Img(name, rt, BoxColor, raycast);
        img.sprite = round; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 32f / Radius;
        RectTransform r = img.rectTransform;
        r.anchorMin = new Vector2(x0, 1f);
        r.anchorMax = new Vector2(x1, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.offsetMin = new Vector2(0f, -y - h);
        r.offsetMax = new Vector2(0f, -y);
        return img;
    }

    // 부모 안 [x0, x1] × [y0, y1] 비율 구간 + 좌우 여백
    private static void Span(RectTransform r, float x0, float x1, float y0, float y1, float padL, float padR)
    {
        r.anchorMin = new Vector2(x0, y0);
        r.anchorMax = new Vector2(x1, y1);
        r.offsetMin = new Vector2(padL, 0f);
        r.offsetMax = new Vector2(-padR, 0f);
    }

    // 상자 맨 아래 가로선 (두께 h)
    private static void BottomLine(RectTransform r, float h)
    {
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(1f, 0f);
        r.pivot = new Vector2(0.5f, 0f);
        r.offsetMin = new Vector2(0f, 0f);
        r.offsetMax = new Vector2(0f, h);
    }

    private void Hit(RectTransform parent, float x0, float x1, Action onClick)
    {
        Image hit = SettingsUIKit.Img("Hit", parent, new Color(1f, 1f, 1f, 0f), true);
        Span(hit.rectTransform, x0, x1, 0f, 1f, 0f, 0f);
        AddButton(hit.gameObject, onClick);
    }

    private static void AddButton(GameObject go, Action onClick)
    {
        UnityEngine.UI.Button b = go.AddComponent<UnityEngine.UI.Button>();
        b.transition = Selectable.Transition.None;
        b.navigation = new Navigation { mode = Navigation.Mode.None }; // 방향키가 버튼으로 가지 않도록
        b.onClick.AddListener(() => { if (!SettingsWindow.BlocksGameInput) onClick(); });
    }

    private TextMeshProUGUI Txt(Transform parent, string text, float size, TextAlignmentOptions align, Color c)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt("T", parent, text, size, align, c, FontStyles.Normal);
        if (font != null) t.font = font;
        t.richText = true;
        t.raycastTarget = false;
        t.enableAutoSizing = true;
        t.fontSizeMax = size;
        t.fontSizeMin = size * 0.6f;
        return t;
    }

    // 아래가 진하고 위로 갈수록 사라지는 빛 (가로로도 양끝이 옅음)
    private static Sprite upGlow;
    private static Sprite UpGlowSprite()
    {
        if (upGlow != null) return upGlow;
        const int W = 64, H = 64;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            // 위로 더 멀리 번지도록 완만하게 (2.2 → 1.4), 옆으로도 칸 끝까지 넓게 (u² → u⁴)
            float v = Mathf.Pow(1f - y / (H - 1f), 2.2f);
            float u = Mathf.Abs((x + 0.5f) / W * 2f - 1f);
            float a = v * (1f - u * u * u * u);
            px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
        }
        tex.SetPixels32(px); tex.Apply();
        upGlow = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 100f);
        return upGlow;
    }

    private static Sprite MakeRoundSprite()
    {
        const int C = 64;
        Texture2D tex = new Texture2D(C, C, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[C * C];
        for (int y = 0; y < C; y++)
            for (int x = 0; x < C; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(C * 0.5f, C * 0.5f));
                px[y * C + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(C * 0.5f - d + 0.5f) * 255));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(C / 2, C / 2, C / 2, C / 2));
    }
}