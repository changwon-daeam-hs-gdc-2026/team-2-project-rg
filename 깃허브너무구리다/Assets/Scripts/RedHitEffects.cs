using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
// RED 노트 전용 이펙트 (모든 GAME UI 공통)
//
//  [탭]   RED 노트(머리 · 꼬리 포함)를 칠 때 — 레인별 일반 히트 효과(흰/파란 네모) 대신 이것 하나만 나옴
//         ⓪ 노트 폭 전체를 덮는 한 장짜리 터치 판 (흰색 → 붉은색으로 퍼지며 사라짐)
//         ① 노트 폭만큼 판정선에서 위아래로 퍼지는 붉은 띠
//         ② 노트 폭 전체 위로 잠깐 솟는 붉은 빛 기둥
//         ③ 노트 폭 안에서 위로 튀는 붉은 불씨
//  [홀드] RED 롱노트를 누르고 있는 동안
//         ① 판정선 위 노트 폭만큼 맥박치는 붉은 띠
//         ② 은은한 붉은 빛 기둥
//         ③ 계속 피어오르는 붉은 불씨
//
//  - RED 롱노트를 누르는 동안에도 레인별 일반 홀드 효과 대신 이 효과만 나옴
//  - 일반 노트 이펙트보다 '아래'에 그린다.
//      SIMPLE / SIMPLE-DIVERSE : 노트(2~5) 위, 일반 히트 빛(7~9) 아래 → 6
//      DEFAULT                 : 일반 히트 파티클(100) 아래 → 95
//  - SIMPLE-DIVERSE 에서는 레인과 같은 원근으로 그림
//  - 곡 종료 / FAILED 때 GameManager.EndUiAlpha 를 따라 함께 사라짐
//  - [감소] 터치 판 · 붉은 띠 · 빛 기둥을 더 옅고 작게, 불씨 수를 줄임 (아래 상수로 조절)
// ============================================================
public class RedHitEffects : MonoBehaviour
{
    private static readonly Color Red = new Color(1f, 0.24f, 0.36f, 1f);
    private static readonly Color Hot = new Color(1f, 0.82f, 0.86f, 1f);   // 불씨 처음 색 (거의 흰 분홍)
    private const float LaneW = 0.8f;

    // ===== 세기 조절 (예전 값 → 지금 값) =====
    private const float FlashAlpha = 0.55f;      // 터치 판 진하기 (0.9 → 0.55)
    private const float FlashHeight = 0.40f;     // 터치 판 최대 높이 (0.62 → 0.40)
    private const float BandAlpha = 0.50f;       // 퍼지는 붉은 띠 진하기 (0.85 → 0.50)
    private const float BandHeight = 0.70f;      // 붉은 띠 최대 높이 (1.1 → 0.70)
    private const float ColumnAlpha = 0.16f;     // 빛 기둥 진하기 (0.32 → 0.16)
    private const float ColumnHeight = 1.5f;     // 빛 기둥 최대 높이 (2.4 → 1.5)
    private const int TapEmbers = 6;             // 칠 때 튀는 불씨 수 (14 → 6)
    private const float HoldBandAlpha = 0.45f;   // 홀드 중 맥박 띠 (0.75 → 0.45)
    private const float HoldColumnAlpha = 0.08f; // 홀드 중 빛 기둥 (0.16 → 0.08)
    private const float HoldSparkEvery = 0.08f;  // 홀드 중 불씨 간격(초) (0.035 → 0.08, 클수록 적게)

    private GameManager gm;
    private int order;
    private Sprite pixel, vBell, column;

    private class Burst { public SpriteRenderer flash, band, col; public float t = 1f; public float x, w; }
    private class Ember { public SpriteRenderer sr; public float t = 1f, life = 1f, rise, size; public Vector3 start; }
    private class Hold { public NoteData note; public SpriteRenderer band, col; public float x, w, level, sparkTimer; public bool on; }

    private readonly List<Burst> bursts = new List<Burst>();
    private readonly List<Ember> embers = new List<Ember>();
    private readonly List<Hold> holds = new List<Hold>();
    private float clock;

    public static RedHitEffects Create(GameManager manager)
    {
        GameObject go = new GameObject("RedHitEffects");
        SceneManager.MoveGameObjectToScene(go, manager.gameObject.scene); // 게임 씬과 함께 정리
        RedHitEffects fx = go.AddComponent<RedHitEffects>();
        fx.gm = manager;
        fx.order = SimpleGameSkin.Active != null ? 6 + NoteLayer.EffectLift : 95;   // 5층 노트보다 위
        fx.MakeSprites();
        for (int i = 0; i < 8; i++) fx.bursts.Add(new Burst { flash = fx.Piece("RedFlash", fx.pixel), band = fx.Piece("RedBand", fx.vBell), col = fx.Piece("RedColumn", fx.column) });
        for (int i = 0; i < 70; i++) fx.embers.Add(new Ember { sr = fx.Piece("RedEmber", fx.pixel) });
        for (int i = 0; i < 4; i++) fx.holds.Add(new Hold { band = fx.Piece("RedHoldBand", fx.vBell), col = fx.Piece("RedHoldColumn", fx.column) });
        if (SimpleGameSkin.Active != null) SimpleGameSkin.Active.AdoptToPlayfield(go, false); // DIVERSE: 원근
        return fx;
    }

    // ---------------------------------------------------------------
    //  GameManager 에서 부르는 함수
    // ---------------------------------------------------------------
    public void Tap(NoteData n)
    {
        if (n == null) return;
        float x, w;
        Span(n, out x, out w);
        Burst b = bursts.Find(o => o.t >= 1f) ?? bursts[0];
        b.t = 0f; b.x = x; b.w = w;
        for (int i = 0; i < TapEmbers; i++) SpawnEmber(x, w, true);
    }

    public void StartHold(NoteData n)
    {
        if (n == null) return;
        Hold h = holds.Find(o => o.on && o.note == n);
        if (h != null) return;
        h = holds.Find(o => !o.on && o.level <= 0.001f) ?? holds.Find(o => !o.on);
        if (h == null) return;
        Span(n, out h.x, out h.w);
        h.note = n; h.on = true; h.sparkTimer = 0f;
    }

    public void StopHold(NoteData n)
    {
        foreach (var h in holds) if (h.on && h.note == n) h.on = false;
    }

    public void StopAll()
    {
        foreach (var h in holds) h.on = false;
    }

    // 노트가 덮는 레인들의 가운데 x 와 전체 폭
    private void Span(NoteData n, out float x, out float w)
    {
        float[] xs = gm.laneXPositions;
        int a = Mathf.Clamp(n.Lane, 0, xs.Length - 1);
        int b = Mathf.Clamp(n.Lane + Mathf.Max(1, n.Width) - 1, 0, xs.Length - 1);
        x = (xs[a] + xs[b]) * 0.5f;
        w = Mathf.Abs(xs[b] - xs[a]) + LaneW * 0.95f;
    }

    // ---------------------------------------------------------------
    //  매 프레임
    // ---------------------------------------------------------------
    void Update()
    {
        if (gm == null) return;
        float dt = Time.unscaledDeltaTime;
        if (gm.currentState == GameState.Paused) dt = 0f;   // 일시정지 중에는 멈춤
        clock += dt;
        float endA = gm.EndUiAlpha;
        float y = gm.hitLineY;

        foreach (var b in bursts)
        {
            if (b.t >= 1f) { b.flash.enabled = b.band.enabled = b.col.enabled = false; continue; }
            b.t = Mathf.Min(1f, b.t + dt / 0.32f);
            float e = 1f - (1f - b.t) * (1f - b.t);
            float fade = (1f - b.t) * endA;

            // 터치 판: 일반 노트의 네모 히트 효과처럼, 다만 노트 폭 전체를 한 장으로 (흰색 → 붉은색)
            float ft = Mathf.Clamp01(b.t * 0.32f / 0.2f);
            float fe = 1f - (1f - ft) * (1f - ft);
            Place(b.flash, b.x, y, b.w * Mathf.Lerp(0.96f, 1.06f, fe), Mathf.Lerp(0.12f, FlashHeight, fe));
            Color fc = Color.Lerp(Hot, Red, fe);
            b.flash.color = new Color(fc.r, fc.g, fc.b, FlashAlpha * (1f - ft) * endA);
            b.flash.enabled = ft < 1f && endA > 0.001f;
            Place(b.band, b.x, y, b.w * Mathf.Lerp(1f, 1.08f, e), Mathf.Lerp(0.18f, BandHeight, e));
            b.band.color = new Color(Red.r, Red.g, Red.b, BandAlpha * fade);
            PlaceBottom(b.col, b.x, y, b.w, Mathf.Lerp(0.5f, ColumnHeight, e));
            b.col.color = new Color(Red.r, Red.g, Red.b, ColumnAlpha * fade);
            b.band.enabled = b.col.enabled = fade > 0.001f;
        }

        foreach (var h in holds)
        {
            h.level = Mathf.MoveTowards(h.level, h.on ? 1f : 0f, dt / 0.1f);
            bool vis = h.level > 0.001f && endA > 0.001f;
            h.band.enabled = h.col.enabled = vis;
            if (vis)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(clock * 16f);
                Place(h.band, h.x, y, h.w, 0.28f + 0.14f * pulse);
                h.band.color = new Color(Red.r, Red.g, Red.b, HoldBandAlpha * h.level * pulse * endA);
                PlaceBottom(h.col, h.x, y, h.w, 1.8f);
                h.col.color = new Color(Red.r, Red.g, Red.b, HoldColumnAlpha * h.level * endA);
            }
            if (h.on && dt > 0f)
            {
                h.sparkTimer -= dt;
                if (h.sparkTimer <= 0f) { h.sparkTimer = HoldSparkEvery; SpawnEmber(h.x, h.w, false); }
            }
        }

        foreach (var s in embers)
        {
            if (s.t >= 1f) { if (s.sr.enabled) s.sr.enabled = false; continue; }
            s.t = Mathf.Min(1f, s.t + dt / s.life);
            float e = 1f - (1f - s.t) * (1f - s.t);
            s.sr.transform.position = s.start + new Vector3(0f, s.rise * e, 0f);
            float sz = s.size * (1f - 0.6f * s.t);
            s.sr.transform.localScale = new Vector3(sz / pixel.bounds.size.x, sz / pixel.bounds.size.y, 1f);
            Color c = Color.Lerp(Hot, Red, Mathf.Clamp01(s.t * 1.6f));
            s.sr.color = new Color(c.r, c.g, c.b, 0.95f * (1f - s.t) * endA);
            if (s.t >= 1f) s.sr.enabled = false;
        }
    }

    private void SpawnEmber(float x, float w, bool burst)
    {
        Ember s = embers.Find(o => o.t >= 1f);
        if (s == null) return;
        s.t = 0f;
        s.life = Random.Range(burst ? 0.3f : 0.35f, burst ? 0.5f : 0.6f);
        s.rise = Random.Range(burst ? 0.9f : 0.6f, burst ? 1.8f : 1.2f);  // 덜 높이 튐
        s.size = Random.Range(0.035f, burst ? 0.065f : 0.055f);   // 불씨 크기도 조금 작게
        s.start = new Vector3(x + Random.Range(-w * 0.48f, w * 0.48f), gm.hitLineY, 0f);
        s.sr.transform.position = s.start;
        s.sr.enabled = true;
    }

    // ---------------------------------------------------------------
    //  도우미
    // ---------------------------------------------------------------
    private SpriteRenderer Piece(string name, Sprite sprite)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // 가운데 기준
    private static void Place(SpriteRenderer sr, float x, float y, float w, float h)
    {
        Vector2 sz = sr.sprite.bounds.size;
        sr.transform.position = new Vector3(x, y, 0f);
        sr.transform.localScale = new Vector3(w / Mathf.Max(0.0001f, sz.x), h / Mathf.Max(0.0001f, sz.y), 1f);
    }

    // 아래 가운데 기준 (column 스프라이트는 피벗이 아래)
    private static void PlaceBottom(SpriteRenderer sr, float x, float y, float w, float h)
    {
        Place(sr, x, y, w, h);
    }

    private void MakeSprites()
    {
        Texture2D px = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color32[] white = new Color32[16];
        for (int i = 0; i < 16; i++) white[i] = new Color32(255, 255, 255, 255);
        px.SetPixels32(white); px.Apply();
        pixel = Sprite.Create(px, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);

        // 세로 종 모양 (가운데 밝고 위아래로 옅어짐)
        Texture2D vb = new Texture2D(4, 64, TextureFormat.RGBA32, false);
        vb.wrapMode = TextureWrapMode.Clamp;
        Color32[] vp = new Color32[4 * 64];
        for (int y = 0; y < 64; y++)
        {
            float d = (y + 0.5f - 32f) / 32f;
            byte a = (byte)(Mathf.Clamp01(Mathf.Exp(-d * d * 5f) * (1f - d * d)) * 255);
            for (int x = 0; x < 4; x++) vp[y * 4 + x] = new Color32(255, 255, 255, a);
        }
        vb.SetPixels32(vp); vb.Apply();
        vBell = Sprite.Create(vb, new Rect(0, 0, 4, 64), new Vector2(0.5f, 0.5f), 64f);

        // 빛 기둥: 아래가 진하고 위로 사라짐 + 좌우 가장자리도 부드럽게 (피벗 아래)
        Texture2D col = new Texture2D(32, 64, TextureFormat.RGBA32, false);
        col.wrapMode = TextureWrapMode.Clamp;
        Color32[] cp = new Color32[32 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 32; x++)
            {
                float u = Mathf.Abs(x + 0.5f - 16f) / 16f;
                float a = Mathf.Pow(1f - y / 63f, 2f) * Mathf.Clamp01((1f - u) / 0.25f);
                cp[y * 32 + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        col.SetPixels32(cp); col.Apply();
        column = Sprite.Create(col, new Rect(0, 0, 32, 64), new Vector2(0.5f, 0f), 64f);
    }
}