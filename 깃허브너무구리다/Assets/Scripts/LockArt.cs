using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 잠긴 곡 (songs.json "ExistSongbar": 2) 표시
//  · LockArt            : 사선 줄무늬 · 원 · 원 테두리 그림 (코드로 만듦)
//  · LockedJacketOverlay: 곡 선택 왼쪽 큰 자켓 위 잠금 연출
//       어두운 막 + 천천히 흐르는 사선 줄무늬 + 가운데 작은 둥근 배지(자물쇠) + 숨 쉬는 빛 고리 (글자 없음)
//       나타날 때: 막이 덮이고 → 배지가 툭 내려앉으며 자물쇠가 '철컥' 기울었다 바로 섬 → 고리가 한 번 퍼짐
//       ENTER(잠긴 곡 플레이 시도): 배지가 좌우로 흔들리고 빨간 고리가 한 번 퍼짐
// ============================================================
public static class LockArt
{
    public static readonly Color Accent = new Color(1f, 0.42f, 0.45f, 1f);     // 잠금 강조 색 (연한 빨강)

    private static Sprite stripes, circle, ring;

    // 사선 줄무늬 (가로 · 세로로 이어 붙여도 끊기지 않는 32px 타일)
    public static Sprite Stripes
    {
        get
        {
            if (stripes != null) return stripes;
            const int N = 32;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Repeat(x + y, 16f);                  // 16px 마다 한 줄 (45도)
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 4f) / 3f); // 줄 굵기 약 6px, 가장자리 부드럽게
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            stripes = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return stripes;
        }
    }

    public static Sprite Circle { get { if (circle == null) circle = MakeDisc(false); return circle; } }
    public static Sprite Ring { get { if (ring == null) ring = MakeDisc(true); return ring; } }

    private static Sprite MakeDisc(bool ringOnly)
    {
        const int N = 128;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f, R = N * 0.5f - 2f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = ringOnly ? Mathf.Clamp01(1f - Mathf.Abs(d - (R - 2.5f)) / 1.6f + 0.5f) * Mathf.Clamp01(R - d + 0.5f)
                                   : Mathf.Clamp01(R - d + 0.5f);
                if (ringOnly && d < R - 5f) a = 0f;
                px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        t.SetPixels32(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    // 도우미: 부모를 꽉 채우는 Image
    public static Image Fill(string name, Transform parent, Color c)
    {
        Image img = Img(name, parent, c);
        RectTransform r = img.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        return img;
    }

    public static Image Img(string name, Transform parent, Color c)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    // 사선 줄무늬 막 (타일)
    public static Image StripeFill(string name, Transform parent, Color c, float tilePx)
    {
        Image img = Fill(name, parent, c);
        img.sprite = Stripes;
        img.type = Image.Type.Tiled;
        img.pixelsPerUnitMultiplier = 32f / Mathf.Max(4f, tilePx) * (100f / 100f);
        return img;
    }
}

// ---------------------------------------------------------------
//  왼쪽 큰 자켓 위 잠금 연출
// ---------------------------------------------------------------
public class LockedJacketOverlay : MonoBehaviour
{
    private CanvasGroup group;
    private RectTransform rt, badge, lockRt, stripeRt;
    private Image stripe, glow, disc, discRing, pulseRing, lockImg;
    private bool shown;
    private float showT, idleT, shakeT = -1f, alpha;

    public static LockedJacketOverlay Create(RectTransform jacket, TMP_FontAsset font)
    {
        if (jacket == null) return null;
        GameObject go = new GameObject("LockedOverlay", typeof(RectTransform));
        go.layer = jacket.gameObject.layer;
        go.transform.SetParent(jacket, false);
        LockedJacketOverlay o = go.AddComponent<LockedJacketOverlay>();
        o.Build(font);
        return o;
    }

    private void Build(TMP_FontAsset font)
    {
        rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling();
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false;
        group.alpha = 0f;

        // 어두운 막
        LockArt.Fill("Dim", rt, new Color(0.02f, 0.02f, 0.04f, 0.6f));
        // 사선 줄무늬 (천천히 흐름) — 잘라 내기 위해 마스크 안에
        RectTransform clip = (RectTransform)new GameObject("StripeClip", typeof(RectTransform)).transform;
        clip.gameObject.layer = gameObject.layer;
        clip.SetParent(rt, false);
        clip.anchorMin = Vector2.zero; clip.anchorMax = Vector2.one; clip.offsetMin = clip.offsetMax = Vector2.zero;
        clip.gameObject.AddComponent<RectMask2D>();
        stripe = LockArt.StripeFill("Stripes", clip, new Color(1f, 1f, 1f, 0.045f), 34f);
        stripeRt = stripe.rectTransform;
        stripeRt.offsetMin = new Vector2(-68f, 0f); stripeRt.offsetMax = new Vector2(0f, 0f);   // 왼쪽으로 한 칸 더 (흐를 자리)

        // 가운데 배지
        badge = (RectTransform)new GameObject("Badge", typeof(RectTransform)).transform;
        badge.gameObject.layer = gameObject.layer;
        badge.SetParent(rt, false);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0.5f, 0.5f);
        glow = LockArt.Img("Glow", badge, new Color(LockArt.Accent.r, LockArt.Accent.g, LockArt.Accent.b, 0.25f));
        glow.sprite = NoteArt.GlowSprite;
        pulseRing = LockArt.Img("Pulse", badge, new Color(1f, 1f, 1f, 0f));
        pulseRing.sprite = LockArt.Ring;
        disc = LockArt.Img("Disc", badge, new Color(0.06f, 0.06f, 0.09f, 0.82f));
        disc.sprite = LockArt.Circle;
        discRing = LockArt.Img("Ring", badge, new Color(1f, 1f, 1f, 0.55f));
        discRing.sprite = LockArt.Ring;
        lockImg = LockArt.Img("Lock", badge, Color.white);
        lockImg.sprite = GameModeIconArt.Lock();
        lockImg.preserveAspect = true;
        lockRt = lockImg.rectTransform;
        lockRt.pivot = new Vector2(0.5f, 0.3f);

    }

    // 보이기 / 숨기기 (instant: 연출 없이 바로)
    public void Show(bool on, bool instant)
    {
        if (on && (!shown || instant)) { showT = instant ? 10f : 0f; }
        shown = on;
        if (instant) { alpha = on ? 1f : 0f; group.alpha = alpha; }
    }

    // 잠긴 곡에서 플레이를 시도했을 때
    public void Shake() { if (shown) shakeT = 0f; }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        alpha = Mathf.MoveTowards(alpha, shown ? 1f : 0f, dt / 0.25f);
        group.alpha = alpha * alpha * (3f - 2f * alpha);
        if (alpha <= 0.001f && !shown) return;
        showT += dt; idleT += dt;

        // 크기는 자켓에 맞춰 (자켓 크기가 바뀌어도 따라감)
        Rect r = rt.rect;
        float s = Mathf.Max(60f, Mathf.Min(r.width, r.height));
        float bs = s * 0.17f;                                  // 배지 지름 (작게)
        badge.anchoredPosition = Vector2.zero;
        Size(disc.rectTransform, bs); Size(discRing.rectTransform, bs);
        Size(lockImg.rectTransform, bs * 0.62f);
        lockRt.anchoredPosition = new Vector2(0f, -bs * 0.12f);
        Size(glow.rectTransform, bs * 2.6f);

        // 나타남: 배지가 위에서 툭 내려앉음 (easeOutBack) → 자물쇠 '철컥'
        float k = Mathf.Clamp01(showT / 0.42f);
        float back = 1f + 2.4f * Mathf.Pow(k - 1f, 3f) + 1.4f * Mathf.Pow(k - 1f, 2f);
        float drop = (1f - Mathf.Clamp01(back)) * s * 0.05f;
        badge.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, back);
        badge.anchoredPosition += new Vector2(0f, drop);
        float clack = showT > 0.30f && showT < 0.55f ? Mathf.Sin((showT - 0.30f) / 0.25f * Mathf.PI) * -10f : 0f;

        // ENTER 흔들림
        float shake = 0f;
        if (shakeT >= 0f)
        {
            shakeT += dt;
            float sk = Mathf.Clamp01(shakeT / 0.5f);
            shake = Mathf.Sin(shakeT * 46f) * (1f - sk) * s * 0.025f;
            if (sk >= 1f) shakeT = -1f;
        }
        badge.anchoredPosition += new Vector2(shake, 0f);
        lockRt.localRotation = Quaternion.Euler(0f, 0f, clack + shake * 0.6f);

        // 빛 고리: 나타날 때 · 흔들 때 한 번 퍼짐 (흔들 때는 빨강)
        float pk = shakeT >= 0f ? Mathf.Clamp01(shakeT / 0.55f) : Mathf.Clamp01((showT - 0.30f) / 0.6f);
        bool pulsing = shakeT >= 0f || (showT > 0.30f && showT < 0.90f);
        Size(pulseRing.rectTransform, bs * Mathf.Lerp(1f, 1.9f, 1f - Mathf.Pow(1f - pk, 3f)));
        Color pc = shakeT >= 0f ? LockArt.Accent : Color.white;
        pulseRing.color = new Color(pc.r, pc.g, pc.b, pulsing ? 0.7f * (1f - pk) * (1f - pk) : 0f);

        // 평소: 테두리 · 뒤쪽 빛이 천천히 숨 쉼, 줄무늬가 천천히 흐름
        float breath = 0.5f + 0.5f * Mathf.Sin(idleT * 2.1f);
        discRing.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 0.65f, breath));
        glow.color = new Color(LockArt.Accent.r, LockArt.Accent.g, LockArt.Accent.b, Mathf.Lerp(0.10f, 0.24f, breath) + (shakeT >= 0f ? 0.25f : 0f));
        stripeRt.anchoredPosition = new Vector2(Mathf.Repeat(idleT * 12f, 34f), 0f);

    }

    private static void Size(RectTransform r, float d)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(d, d);
    }
}