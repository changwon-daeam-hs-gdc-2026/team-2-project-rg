using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// PRACTICE 모드 표시 부품 (게임 화면 · 결과창 공용)
//
//  PRACTICE 모드 = 설정 > 플레이 > 시작 시간을 바꿔서 곡 중간부터 시작한 판 (기록 저장 안 함)
//
//   [ ⟳ PRACTICE ]   곡 이름 앞에 붙는 배지 (다시 하기 화살표 아이콘)
//   [ ⊘ NOT SAVED ]  점수 자리에 들어가는 배지 (저장 안 됨 아이콘: 디스크 + 사선)
//
//  - 아이콘은 코드로 그림 (따로 넣을 이미지 없음)
//  - Build(부모, 아이콘, 글자, 색, 높이, 글꼴) → 왼쪽 가운데 피벗의 배지 RectTransform (폭은 내용에 맞춤)
// ============================================================
public static class PracticeBadge
{
    public static readonly Color PracticeColor = new Color(1f, 0.80f, 0.38f, 1f);   // 연습: 따뜻한 호박색
    public static readonly Color NotSavedColor = new Color(1f, 1f, 1f, 0.78f);       // 저장 안 됨: 옅은 흰색

    private static Sprite practiceIcon, notSavedIcon, round;

    public static RectTransform Practice(Transform parent, float height, TMP_FontAsset font)
    {
        return Build(parent, PracticeIcon, "PRACTICE", PracticeColor, height, font);
    }

    public static RectTransform NotSaved(Transform parent, float height, TMP_FontAsset font)
    {
        return Build(parent, NotSavedIcon, "NOT SAVED", NotSavedColor, height, font);
    }

    public static RectTransform Build(Transform parent, Sprite icon, string label, Color c, float height, TMP_FontAsset font)
    {
        RectTransform root = SettingsUIKit.Rect("Badge_" + label, parent);
        root.anchorMin = root.anchorMax = new Vector2(0f, 0.5f);
        root.pivot = new Vector2(0f, 0.5f);

        Image bg = SettingsUIKit.Img("Bg", root, new Color(c.r, c.g, c.b, 0.14f));
        bg.sprite = Round; bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, height * 0.5f);   // 양 끝이 반원인 알약 모양
        SettingsUIKit.Stretch(bg.rectTransform);
        Image line = SettingsUIKit.Img("Line", root, new Color(c.r, c.g, c.b, 0.55f));
        line.sprite = PillLine; line.type = Image.Type.Sliced;
        line.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, height * 0.5f);
        SettingsUIKit.Stretch(line.rectTransform);

        float pad = height * 0.42f, iconS = height * 0.56f, gap = height * 0.24f;
        Image ic = SettingsUIKit.Img("Icon", root, c);
        ic.sprite = icon;
        RectTransform ir = ic.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
        ir.pivot = new Vector2(0f, 0.5f);
        ir.sizeDelta = new Vector2(iconS, iconS);
        ir.anchoredPosition = new Vector2(pad, 0f);

        TextMeshProUGUI t = SettingsUIKit.Txt("Text", root, label, height * 0.46f, TextAlignmentOptions.MidlineLeft, c, FontStyles.Bold);
        if (font != null) t.font = font;
        t.characterSpacing = 6f;
        float tw = t.GetPreferredValues(label).x + 2f;
        RectTransform tr = t.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0f, 0.5f);
        tr.pivot = new Vector2(0f, 0.5f);
        tr.sizeDelta = new Vector2(tw, height);
        tr.anchoredPosition = new Vector2(pad + iconS + gap, 0f);

        root.sizeDelta = new Vector2(pad + iconS + gap + tw + pad, height);
        return root;
    }

    // 난이도 배지:  [ ◆ INSANE  16 ]  — 난이도 색 알약 + 마름모 아이콘 + 이름(난이도 색) + 수치(흰색)
    public static RectTransform Difficulty(Transform parent, string name, string value, Color c, float height, TMP_FontAsset font)
    {
        RectTransform root = SettingsUIKit.Rect("Badge_Difficulty", parent);
        root.anchorMin = root.anchorMax = new Vector2(0f, 0.5f);
        root.pivot = new Vector2(0f, 0.5f);
        float ppu = 32f / Mathf.Max(1f, height * 0.5f);
        Image bg = SettingsUIKit.Img("Bg", root, new Color(c.r, c.g, c.b, 0.16f));
        bg.sprite = Round; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = ppu;
        SettingsUIKit.Stretch(bg.rectTransform);
        Image line = SettingsUIKit.Img("Line", root, new Color(c.r, c.g, c.b, 0.7f));
        line.sprite = PillLine; line.type = Image.Type.Sliced; line.pixelsPerUnitMultiplier = ppu;
        SettingsUIKit.Stretch(line.rectTransform);

        float pad = height * 0.42f, iconS = height * 0.5f, gap = height * 0.24f;
        Image ic = SettingsUIKit.Img("Icon", root, c);
        ic.sprite = DifficultyIcon;
        RectTransform ir = ic.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
        ir.pivot = new Vector2(0f, 0.5f);
        ir.sizeDelta = new Vector2(iconS, iconS);
        ir.anchoredPosition = new Vector2(pad, 0f);

        float x = pad + iconS + gap;
        TextMeshProUGUI n = SettingsUIKit.Txt("Name", root, name, height * 0.46f, TextAlignmentOptions.MidlineLeft, c, FontStyles.Bold);
        if (font != null) n.font = font;
        n.characterSpacing = 6f;
        float nw = n.GetPreferredValues(name).x + 2f;
        Pin(n.rectTransform, x, nw, height);
        x += nw + height * 0.3f;
        TextMeshProUGUI v = SettingsUIKit.Txt("Value", root, value, height * 0.56f, TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold);
        if (font != null) v.font = font;
        float vw = v.GetPreferredValues(value).x + 2f;
        Pin(v.rectTransform, x, vw, height);
        x += vw + pad;
        root.sizeDelta = new Vector2(x, height);
        return root;
    }

    private static void Pin(RectTransform r, float x, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, 0f);
    }

    // 난이도 아이콘: 마름모 테두리 + 안쪽 작은 마름모
    private static Sprite difficultyIcon;
    public static Sprite DifficultyIcon
    {
        get
        {
            if (difficultyIcon == null) difficultyIcon = Draw(p =>
            {
                float d = Mathf.Abs(p.x) + Mathf.Abs(p.y);
                float ring = Mathf.Abs(d - 0.78f) - 0.12f;
                float core = d - 0.34f;
                return Mathf.Min(ring, core);
            });
            return difficultyIcon;
        }
    }

    // ---------- 아이콘 ----------
    // 다시 하기(연습): 오른쪽 위가 열린 원호 + 끝의 화살촉
    public static Sprite PracticeIcon
    {
        get
        {
            if (practiceIcon == null) practiceIcon = Draw(p =>
            {
                float ang = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;           // -180 ~ 180
                float arc = Mathf.Abs(p.magnitude - 0.62f) - 0.13f;
                if (ang > 10f && ang < 75f) arc = 1f;                          // 오른쪽 위를 비움
                // 화살촉: 원호의 열린 끝(10°) 근처에서 아래를 향한 삼각형
                Vector2 tip = new Vector2(0.61f, -0.05f);
                float tri = Mathf.Max(Mathf.Max(Line(p, new Vector2(0.30f, 0.30f), new Vector2(0.95f, 0.30f)),
                                                Line(p, new Vector2(0.95f, 0.30f), tip)),
                                      Line(p, tip, new Vector2(0.30f, 0.30f)));
                return Mathf.Min(arc, tri);
            });
            return practiceIcon;
        }
    }

    // 저장 안 됨: 디스크(둥근 사각 테두리 + 위쪽 라벨 칸) + 사선
    public static Sprite NotSavedIcon
    {
        get
        {
            if (notSavedIcon == null) notSavedIcon = Draw(p =>
            {
                float frame = Mathf.Abs(RoundBox(p, new Vector2(0.66f, 0.66f), 0.16f)) - 0.09f;
                float label = Mathf.Abs(RoundBox(p - new Vector2(0f, 0.38f), new Vector2(0.32f, 0.2f), 0.04f)) - 0.07f;
                float slash = Seg(p, new Vector2(-0.82f, -0.82f), new Vector2(0.82f, 0.82f)) - 0.11f;
                // 사선 주변은 비워서 사선이 또렷하게
                float clear = Seg(p, new Vector2(-0.82f, -0.82f), new Vector2(0.82f, 0.82f)) - 0.24f;
                float body = Mathf.Min(frame, label);
                body = Mathf.Max(body, -clear);
                return Mathf.Min(body, slash);
            });
            return notSavedIcon;
        }
    }

    private delegate float Shape(Vector2 p);

    private static Sprite Draw(Shape f)
    {
        const int S = 64;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        float aa = 2.2f / S;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / S * 2f - 1f, (y + 0.5f) / S * 2f - 1f);
                float a = Mathf.Clamp01(0.5f - f(p) / aa);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite Round
    {
        get
        {
            if (round == null) round = SettingsUIKit.RoundSprite;
            return round;
        }
    }

    private static Sprite pillLine;
    private static Sprite PillLine
    {
        get
        {
            if (pillLine != null) return pillLine;
            const int C = 64;
            Texture2D tex = new Texture2D(C, C, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[C * C];
            for (int y = 0; y < C; y++)
                for (int x = 0; x < C; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(C * 0.5f, C * 0.5f)) - C * 0.5f;
                    float a = Mathf.Clamp01(1.6f - Mathf.Abs(d + 1.4f));
                    px[y * C + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            pillLine = Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(C / 2, C / 2, C / 2, C / 2));
            return pillLine;
        }
    }

    private static float Seg(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    // a → b 직선 기준 부호 거리 (진행 방향 오른쪽이 음수)
    private static float Line(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 d = (b - a).normalized;
        return Vector2.Dot(p - a, new Vector2(-d.y, d.x));
    }

    private static float RoundBox(Vector2 p, Vector2 half, float r)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(r, r);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }
}