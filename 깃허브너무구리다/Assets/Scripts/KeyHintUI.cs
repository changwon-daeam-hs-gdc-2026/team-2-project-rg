using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 키 안내 (키캡 디자인) — 결과창 · 일시정지 · 확인 창 · 설정창 공용
//
//   ╭─────╮              ╭───╮ ╭───╮
//   │ ESC │  돌아가기     │ ▲ │ │ ▼ │  선택
//   ╰─────╯              ╰───╯ ╰───╯
//
//  - 키캡: 둥근 사각형, 옅은 흰 바탕 + 가는 흰 테두리 + 아래쪽이 살짝 두꺼운 그림자 선(눌리는 키 느낌)
//  - 키 이름은 굵게, 설명은 옅은 흰색
//  - 특수 키: "^UP" "^DOWN" "^LEFT" "^RIGHT" 는 글꼴과 상관없이 그린 삼각형으로 표시
//  - Build(부모, 글꼴, 항목들, 글자 크기, 정렬) → 한 줄(가로) 묶음 RectTransform 을 돌려줌 (크기 = 내용에 맞춤)
// ============================================================
public static class KeyHintUI
{
    public struct Item
    {
        public string[] Keys;    // 한 항목에 키 여러 개 (예: ^UP, ^DOWN)
        public string Label;
        public Item(string label, params string[] keys) { Label = label; Keys = keys; }
    }

    private static Sprite capFill, capLine, tri;

    // align: -1 왼쪽부터, 0 가운데, 1 오른쪽 끝 기준
    public static RectTransform Build(Transform parent, TMP_FontAsset regular, TMP_FontAsset bold, IList<Item> items,
                                      float size = 18f, int align = 1, Color? labelColor = null)
    {
        Color lc = labelColor ?? new Color(1f, 1f, 1f, 0.62f);
        float capH = Mathf.Round(size * 1.65f);
        float gapKeyLabel = size * 0.55f, gapKeys = size * 0.3f, gapItems = size * 1.6f;

        RectTransform row = NewRect("KeyHints", parent);
        float x = 0f;
        var parts = new List<KeyValuePair<RectTransform, float>>();   // (조각, 폭)

        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) x += gapItems;
            Item it = items[i];
            for (int k = 0; it.Keys != null && k < it.Keys.Length; k++)
            {
                if (k > 0) x += gapKeys;
                RectTransform cap = Cap(row, it.Keys[k], bold != null ? bold : regular, size, capH, out float w);
                cap.anchoredPosition = new Vector2(x, 0f);
                x += w;
            }
            if (!string.IsNullOrEmpty(it.Label))
            {
                x += gapKeyLabel;
                TextMeshProUGUI t = Text(row, it.Label, regular, size, lc, false);
                float w = t.GetPreferredValues(it.Label).x + 2f;
                t.rectTransform.sizeDelta = new Vector2(w, capH);
                t.rectTransform.anchoredPosition = new Vector2(x, 0f);
                x += w;
            }
        }

        row.sizeDelta = new Vector2(x, capH);
        row.pivot = new Vector2(align < 0 ? 0f : (align == 0 ? 0.5f : 1f), 0.5f);
        return row;
    }

    // 키캡 하나
    private static RectTransform Cap(RectTransform parent, string key, TMP_FontAsset font, float size, float h, out float width)
    {
        bool arrow = key.StartsWith("^");
        float textSize = size * 0.82f;
        float w;
        RectTransform r = NewRect("Key_" + key, parent);
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);

        TextMeshProUGUI label = null;
        if (!arrow)
        {
            label = Text(r, key, font, textSize, Color.white, true);
            label.characterSpacing = 2f;
            w = Mathf.Max(h, label.GetPreferredValues(key).x + size * 1.1f);
        }
        else w = h;
        r.sizeDelta = new Vector2(w, h);

        // 아래 그림자 (키가 살짝 떠 있는 느낌)
        Image shadow = Img(r, CapFill, new Color(1f, 1f, 1f, 0.10f));
        Stretch(shadow.rectTransform, 0f, -3f, 0f, 3f);
        Image fill = Img(r, CapFill, new Color(1f, 1f, 1f, 0.07f));
        Stretch(fill.rectTransform, 0f, 0f, 0f, 0f);
        Image line = Img(r, CapLine, new Color(1f, 1f, 1f, 0.55f));
        Stretch(line.rectTransform, 0f, 0f, 0f, 0f);

        if (label != null)
        {
            label.transform.SetAsLastSibling();
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.offsetMin = new Vector2(0f, 1f);
            label.rectTransform.offsetMax = Vector2.zero;
        }
        else
        {
            Image t = Img(r, Triangle, Color.white);
            float s = h * 0.36f;
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(s, s);
            float rot = key == "^DOWN" ? 180f : key == "^LEFT" ? 90f : key == "^RIGHT" ? -90f : 0f;
            t.rectTransform.localEulerAngles = new Vector3(0f, 0f, rot);
        }
        width = w;
        return r;
    }

    // ---------- 그림 ----------
    private static Sprite CapFill { get { if (capFill == null) capFill = Round(false); return capFill; } }
    private static Sprite CapLine { get { if (capLine == null) capLine = Round(true); return capLine; } }

    private static Sprite Round(bool line)
    {
        const int S = 32, R = 8;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - R;
            float a = line ? Mathf.Clamp01(1.0f - Mathf.Abs(d + 0.9f)) : Mathf.Clamp01(0.5f - d);
            px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R + 1, R + 1, R + 1, R + 1));
    }

    private static Sprite Triangle
    {
        get
        {
            if (tri != null) return tri;
            const int S = 32;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // 위를 향한 삼각형 (가장자리 부드럽게)
                float fy = (y + 0.5f) / S, fx = Mathf.Abs((x + 0.5f) / S - 0.5f) * 2f;
                float edge = (1f - fy) - fx;                     // 안쪽 +
                float a = Mathf.Clamp01(edge * S * 0.5f + 0.5f) * (fy > 0.08f ? 1f : Mathf.Clamp01(fy * S));
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            tri = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return tri;
        }
    }

    // ---------- UI 도우미 ----------
    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image Img(RectTransform parent, Sprite sprite, Color c)
    {
        Image img = NewRect("Img", parent).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private static void Stretch(RectTransform r, float l, float b, float rr, float t)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rr, -t);
    }

    private static TextMeshProUGUI Text(RectTransform parent, string text, TMP_FontAsset font, float size, Color c, bool bold)
    {
        RectTransform r = NewRect("T", parent);
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        SettingsUIKit.EnsureFont();
        t.font = font != null ? font : SettingsUIKit.Font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.color = c;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }
}