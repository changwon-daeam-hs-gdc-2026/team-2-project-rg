using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 곡 선택 화면 맨 위 경고 띠 (영역 채보를 SIMPLE-DIVERSE 가 아닌 스킨으로 고른 경우)
//
//   ⚠
//   이 채보(난이도)는 Simple-Diverse 스킨 전용 채보(난이도)입니다.
//   해당 스킨으로 변경 된 상태로 게임이 진행되며, 게임 종료 후 기존 스킨으로 돌아갑니다.
//
//  - 회색 사다리꼴(위가 넓음) 판이 화면 위에서 내려오고, 조건이 풀리면 다시 올라간다
//  - 글꼴은 한글이 들어 있는 글꼴이어야 함 (SongSelectManager > Area Notice Font)
// ============================================================
public class AreaNoticeBanner : MonoBehaviour
{
    public const string Line1 = "이 채보(난이도)는 Simple-Diverse 스킨 전용 채보(난이도)입니다.";
    public const string Line2 = "해당 스킨으로 변경 된 상태로 게임이 진행되며, 게임 종료 후 기존 스킨으로 돌아갑니다.";

    private const float Width = 1280f, Height = 138f, Inset = 30f;   // 1920 x 1080 기준
    private RectTransform rt;
    private CanvasGroup group;
    private float shown;          // 0 ~ 1
    private bool target;

    public static AreaNoticeBanner Create(Transform canvasRoot, TMP_FontAsset font)
    {
        GameObject go = new GameObject("AreaNoticeBanner", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(canvasRoot, false);
        AreaNoticeBanner b = go.AddComponent<AreaNoticeBanner>();
        b.Build(font);
        return b;
    }

    private void Build(TMP_FontAsset font)
    {
        rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(Width, Height);
        rt.anchoredPosition = new Vector2(0f, Height + 10f);
        transform.SetAsLastSibling();
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        Image plate = gameObject.AddComponent<Image>();
        plate.sprite = MakeTrapezoid();
        plate.color = new Color(0.5f, 0.5f, 0.5f, 0.94f);
        plate.raycastTarget = false;

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.layer = 5;
        iconGo.transform.SetParent(transform, false);
        Image icon = iconGo.AddComponent<Image>();
        icon.sprite = NoteArt.WarnIconSprite;
        icon.raycastTarget = false;
        RectTransform ir = (RectTransform)iconGo.transform;
        ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 1f);
        ir.pivot = new Vector2(0.5f, 1f);
        ir.sizeDelta = new Vector2(36f, 36f);
        ir.anchoredPosition = new Vector2(0f, -16f);

        GameObject textGo = new GameObject("Text", typeof(RectTransform));
        textGo.layer = 5;
        textGo.transform.SetParent(transform, false);
        TextMeshProUGUI t = textGo.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = Line1 + "\n" + Line2;
        t.fontSize = 25f;
        t.lineSpacing = 6f;
        t.alignment = TextAlignmentOptions.Top;
        t.color = Color.white;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        RectTransform tr = (RectTransform)textGo.transform;
        tr.anchorMin = new Vector2(0f, 0f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.offsetMin = new Vector2(Inset + 10f, 8f);
        tr.offsetMax = new Vector2(-Inset - 10f, -56f);
    }

    public void SetVisible(bool on) { target = on; }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        shown = Mathf.MoveTowards(shown, target ? 1f : 0f, dt / 0.28f);
        float e = 1f - Mathf.Pow(1f - shown, 3f);
        rt.anchoredPosition = new Vector2(0f, (1f - e) * (Height + 10f));
        group.alpha = e;
    }

    // 위가 넓은 사다리꼴 (가장자리 부드럽게). 절반 해상도로 만들어 늘려 씀
    private static Sprite trapezoid;
    private static Sprite MakeTrapezoid()
    {
        if (trapezoid != null) return trapezoid;
        int w = (int)(Width / 2f), h = (int)(Height / 2f);
        float inset = Inset / 2f;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float fromTop = (h - 1 - y + 0.5f) / h;          // 0 = 맨 위, 1 = 맨 아래
            float side = inset * fromTop;                      // 아래로 갈수록 안쪽으로
            for (int x = 0; x < w; x++)
            {
                float cx = x + 0.5f;
                float d = Mathf.Min(cx - side, (w - side) - cx);    // 비스듬한 옆선까지 거리
                float a = Mathf.Clamp01(d + 0.5f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px); tex.Apply();
        trapezoid = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), 100f);
        return trapezoid;
    }
}