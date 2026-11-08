using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 곡 배경 흐리게 (코드에서 처리)
//  Sprites/blurimg/{곡ID} 에는 이제 흐리게 하지 않은 원본 그림을 넣음
//  게임 화면 · 곡 선택 · 결과창 · 곡 소개 · RANDOM 룰렛 · 예전 에디터에서 이 그림을 쓸 때 BackgroundBlur.Get 으로 흐리게 만든 그림을 씀
//
//  방법 (가볍게):
//   1) GPU 로 원본을 절반씩 여러 번 줄여 가로 Width(기본 320) px 정도의 작은 그림으로 (Graphics.Blit, 이 과정에서도 부드럽게 섞임)
//   2) 작은 그림을 CPU 로 가로 · 세로 상자 흐림 3번 (= 가우스 흐림과 거의 같음)
//   3) 반투명 검은 막(Dim)을 덮은 것처럼 어둡게 → 밝은 빛이 튀지 않게
//   4) 부드럽게 늘려 그리도록(Bilinear) 해서 원래 크기 · 원래 화면 크기(pixelsPerUnit 맞춤)로 씀
//  같은 그림은 한 번만 만들고 기억해 둠 (곡을 바꿨다가 돌아와도 다시 만들지 않음)
// ============================================================
public static class BackgroundBlur
{
    // 흐림 세기: 작은 그림 기준 반지름(px). 320px 기준 6 이면 1920 화면에서 약 36px 반지름
    public static float Radius = 6f;
    // 작은 그림 가로 크기 (작을수록 빠르고 더 뭉개짐)
    public static int Width = 320;
    // 흐린 그림 위에 덮는 검은 막의 진하기 (0 = 없음 ~ 1 = 완전 검정). 0.15 = 검은색 15% 막
    //  직접 조정: CoreManager 인스펙터의 Background Dim (SetLook 으로 들어옴)
    public static float Dim = 0.15f;
    // 흐림 끄기 (원본 그대로 보고 싶을 때)
    public static bool Enabled = true;

    // 값이 바뀔 때마다 1 씩 늘어남 (곡 선택 화면이 보고 지금 배경을 바로 다시 그림)
    public static int Version { get; private set; }

    private static readonly Dictionary<Sprite, Sprite> cache = new Dictionary<Sprite, Sprite>();

    // 흐린 그림 (없으면 만들어 기억). src 가 null 이면 null
    public static Sprite Get(Sprite src)
    {
        if (src == null || !Enabled) return src;
        Sprite done;
        if (cache.TryGetValue(src, out done) && done != null) return done;
        done = Make(src);
        cache[src] = done;
        return done;
    }

    // 검은 막 · 흐림 세기 바꾸기. 값이 바뀌면 기억해 둔 흐린 그림을 잊음 (지금 화면에 쓰이는 그림은 지우지 않음 → 다음 배경부터 새 값)
    public static void SetLook(float dim, float radius)
    {
        dim = Mathf.Clamp01(dim);
        radius = Mathf.Max(0f, radius);
        if (Mathf.Approximately(dim, Dim) && Mathf.Approximately(radius, Radius)) return;
        Dim = dim; Radius = radius;
        cache.Clear();
        Version++;
    }

    // 기억해 둔 흐린 그림을 모두 지움 (메모리를 비우고 싶을 때)
    public static void Clear()
    {
        foreach (var kv in cache)
        {
            if (kv.Value == null || kv.Value == kv.Key) continue;
            Texture2D t = kv.Value.texture;
            Object.Destroy(kv.Value);
            if (t != null) Object.Destroy(t);
        }
        cache.Clear();
    }

    private static Sprite Make(Sprite src)
    {
        Texture tex = src.texture;
        if (tex == null) return src;
        Rect r = src.textureRect;
        if (r.width < 2f || r.height < 2f) return src;

        int w = Mathf.Clamp(Width, 16, Mathf.RoundToInt(r.width));
        int h = Mathf.Max(2, Mathf.RoundToInt(w * r.height / r.width));

        // ---- 1) GPU 로 절반씩 줄이기 (한 번에 확 줄이면 계단이 지므로) ----
        Vector2 scale = new Vector2(r.width / tex.width, r.height / tex.height);
        Vector2 offset = new Vector2(r.x / tex.width, r.y / tex.height);
        int cw = Mathf.RoundToInt(r.width), ch = Mathf.RoundToInt(r.height);
        RenderTexture cur = null;
        RenderTexture prevActive = RenderTexture.active;
        do
        {
            int nw = cw / 2, nh = ch / 2;
            if (nw <= w || nh <= h) { nw = w; nh = h; }       // 마지막 단계: 목표 크기로
            RenderTexture next = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            next.filterMode = FilterMode.Bilinear;
            if (cur == null) Graphics.Blit(tex, next, scale, offset);   // 스프라이트가 그림의 일부여도 그 부분만
            else { Graphics.Blit(cur, next); RenderTexture.ReleaseTemporary(cur); }
            cur = next;
            cw = nw; ch = nh;
        } while (cw != w || ch != h);

        // ---- 작은 그림을 CPU 로 읽기 ----
        RenderTexture.active = cur;
        Texture2D small = new Texture2D(w, h, TextureFormat.RGBA32, false);
        small.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(cur);

        // ---- 2) 상자 흐림 3번 (가로 → 세로) ----
        Color32[] px = small.GetPixels32();
        int rad = Mathf.Max(0, Mathf.RoundToInt(Radius * w / 320f));
        float[] a = new float[w * h * 3], b = new float[w * h * 3];
        for (int i = 0; i < px.Length; i++) { a[i * 3] = px[i].r; a[i * 3 + 1] = px[i].g; a[i * 3 + 2] = px[i].b; }
        for (int pass = 0; pass < (rad > 0 ? 3 : 0); pass++)
        {
            BoxH(a, b, w, h, rad);
            BoxV(b, a, w, h, rad);
        }
        // ---- 3) 검은 막 (색 × (1 - Dim)) ----
        float k = 1f - Mathf.Clamp01(Dim);
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(a[i * 3] * k), 0, 255),
                                (byte)Mathf.Clamp(Mathf.RoundToInt(a[i * 3 + 1] * k), 0, 255),
                                (byte)Mathf.Clamp(Mathf.RoundToInt(a[i * 3 + 2] * k), 0, 255), 255);
        small.SetPixels32(px);
        small.wrapMode = TextureWrapMode.Clamp;
        small.filterMode = FilterMode.Bilinear;
        small.name = src.name + " (blur)";
        small.Apply(false, true);   // 다 만들었으면 CPU 쪽 사본은 버림

        // ---- 4) 원래와 같은 화면 크기 · 기준점 ----
        Vector2 pivot = new Vector2(src.pivot.x / r.width, src.pivot.y / r.height);
        float ppu = src.pixelsPerUnit * (w / r.width);
        Sprite s = Sprite.Create(small, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
        s.name = src.name;
        return s;
    }

    // 가로 상자 흐림 (가장자리는 끝 픽셀을 늘려 씀 → 테두리가 어두워지지 않음)
    private static void BoxH(float[] src, float[] dst, int w, int h, int rad)
    {
        float inv = 1f / (rad * 2 + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int c = 0; c < 3; c++)
            {
                float sum = 0f;
                for (int k = -rad; k <= rad; k++) sum += src[(row + Mathf.Clamp(k, 0, w - 1)) * 3 + c];
                for (int x = 0; x < w; x++)
                {
                    dst[(row + x) * 3 + c] = sum * inv;
                    int add = Mathf.Min(w - 1, x + rad + 1), sub = Mathf.Max(0, x - rad);
                    sum += src[(row + add) * 3 + c] - src[(row + sub) * 3 + c];
                }
            }
        }
    }

    // 세로 상자 흐림
    private static void BoxV(float[] src, float[] dst, int w, int h, int rad)
    {
        float inv = 1f / (rad * 2 + 1);
        for (int x = 0; x < w; x++)
        {
            for (int c = 0; c < 3; c++)
            {
                float sum = 0f;
                for (int k = -rad; k <= rad; k++) sum += src[(Mathf.Clamp(k, 0, h - 1) * w + x) * 3 + c];
                for (int y = 0; y < h; y++)
                {
                    dst[(y * w + x) * 3 + c] = sum * inv;
                    int add = Mathf.Min(h - 1, y + rad + 1), sub = Mathf.Max(0, y - rad);
                    sum += src[(add * w + x) * 3 + c] - src[(sub * w + x) * 3 + c];
                }
            }
        }
    }
}