using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
// RED 노트 전용 이펙트 (모든 GAME UI 공통)
//
//  [탭]   RED 노트(머리 · 꼬리 포함)를 칠 때 — 레인별 일반 히트 효과 대신 이것 하나만 나옴
//         ⓪ 노트 모양 섬광 : 노트 폭 전체를 덮는 둥근 판이 흰 분홍 → 진홍으로 부풀며 사라짐
//         ① 충격파 2겹    : 노트 테두리 모양 고리가 바깥으로 퍼짐 (두 번째는 조금 늦게 · 더 크게)
//         ② 빛줄기         : 판정선을 따라 노트 폭보다 길게 뻗는 가는 흰 → 붉은 빛
//         ③ 빛무리         : 노트 뒤로 넓게 번쩍 번지는 붉은 빛
//         ④ 빛 기둥        : 노트 폭 위로 잠깐 솟는 옅은 붉은 빛
//         ⑤ 불꽃           : 부채꼴로 튀어 오르는 길쭉한 불꽃 (날아가는 방향으로 누움, 점점 느려짐)
//  [홀드] RED 롱노트를 누르고 있는 동안
//         ① 판정선 위 노트 폭만큼 맥박치는 붉은 띠
//         ② 일정 간격으로 퍼지는 작은 충격파
//         ③ 은은한 붉은 빛 기둥 + 위로 피어오르는 불꽃
//
//  - 노트 모양(둥근 판 · 고리)의 모서리 반지름은 스킨의 RED 노트 두께에 맞춤
//      CIRCLE: 납작한 알약 / SIMPLE · SIMPLE-DIVERSE: 둥근 사각형 / DEFAULT: 조금 둥근 사각형
//  - 설정 > 게임플레이 > 히트 이펙트 = 낮음 이면 두 번째 충격파 · 빛무리를 생략하고 불꽃 수를 줄임
//  - 일반 노트 이펙트보다 '아래'에 그린다.
//      SIMPLE / SIMPLE-DIVERSE / CIRCLE : 노트(2~5) 위, 일반 히트 빛(7~9) 아래 → 6
//      DEFAULT                          : 일반 히트 파티클(100) 아래 → 95
//  - SIMPLE-DIVERSE 에서는 레인과 같은 원근으로 그림
//  - 곡 종료 / FAILED 때 GameManager.EndUiAlpha 를 따라 함께 사라짐
// ============================================================
public class RedHitEffects : MonoBehaviour
{
    private static readonly Color Red = new Color(1f, 0.24f, 0.36f, 1f);
    private static readonly Color Deep = new Color(0.85f, 0.06f, 0.20f, 1f);  // 빛무리 (조금 더 진한 진홍)
    private static readonly Color Hot = new Color(1f, 0.86f, 0.89f, 1f);   // 처음 색 (거의 흰 분홍)
    private static float LaneW { get { return SimpleGameSkin.GameLaneWidth; } }   // CIRCLE 은 레인이 넓음

    // ===== 세기 · 크기 조절 =====
    private const float TapSeconds = 0.42f;      // 탭 이펙트 길이
    private const float FlashAlpha = 0.80f;      // 노트 모양 섬광 진하기
    private const float WaveAlpha = 0.90f;       // 충격파 진하기
    private const float WaveGrowX = 0.45f;       // 충격파가 좌우로 퍼지는 거리 (유닛, 양쪽 합)
    private const float WaveGrowY = 0.55f;       // 충격파가 위아래로 퍼지는 거리
    private const float StreakAlpha = 0.85f;     // 빛줄기 진하기
    private const float HaloAlpha = 0.40f;       // 빛무리 진하기
    private const float ColumnAlpha = 0.14f;     // 빛 기둥 진하기
    private const float ColumnHeight = 1.6f;     // 빛 기둥 최대 높이
    private const int TapSparks = 12;            // 칠 때 튀는 불꽃 수 (히트 이펙트 '낮음'이면 절반)
    private const float HoldBandAlpha = 0.45f;   // 홀드 중 맥박 띠
    private const float HoldColumnAlpha = 0.08f; // 홀드 중 빛 기둥
    private const float HoldWaveEvery = 0.18f;   // 홀드 중 작은 충격파 간격 (초)
    private const float HoldSparkEvery = 0.07f;  // 홀드 중 불꽃 간격 (초, 클수록 적게)

    private GameManager gm;
    private int order;
    private Sprite pixel, vBell, column, fill, ring, spark, glow;
    private float noteH = 0.2f;                  // 이 스킨의 RED 노트 두께 (월드 유닛)

    private class Burst
    {
        public SpriteRenderer flash, wave, wave2, streak, halo, col;
        public float t = 1f, x, w; public bool mini;
    }
    private class Spark { public SpriteRenderer sr; public float t = 1f, life = 1f, len, thick, drag; public Vector2 pos, vel; }
    private class Hold { public NoteData note; public SpriteRenderer band, col; public float x, w, level, sparkTimer, waveTimer; public bool on; }

    private readonly List<Burst> bursts = new List<Burst>();
    private readonly List<Spark> sparks = new List<Spark>();
    private readonly List<Hold> holds = new List<Hold>();
    private float clock;

    public static RedHitEffects Create(GameManager manager)
    {
        GameObject go = new GameObject("RedHitEffects");
        SceneManager.MoveGameObjectToScene(go, manager.gameObject.scene); // 게임 씬과 함께 정리
        RedHitEffects fx = go.AddComponent<RedHitEffects>();
        fx.gm = manager;
        fx.order = SimpleGameSkin.Active != null ? 6 + NoteLayer.EffectLift : 95;   // 5층 노트보다 위
        fx.noteH = SimpleGameSkin.CircleActive ? SimpleGameSkin.CircleSize * SimpleGameSkin.CircleRedRatio
                 : (SimpleGameSkin.ThickActive ? SimpleGameSkin.ThickRedNoteHeight      // SIMPLE: 두툼한 RED 노트
                 : (SimpleGameSkin.Active != null ? SimpleGameSkin.RedNoteHeight : 0.2f));
        fx.MakeSprites();
        for (int i = 0; i < 12; i++)
        {
            Burst b = new Burst
            {
                halo = fx.Piece("RedHalo", fx.glow),
                col = fx.Piece("RedColumn", fx.column),
                flash = fx.Piece("RedFlash", fx.fill, true),
                wave = fx.Piece("RedWave", fx.ring, true),
                wave2 = fx.Piece("RedWave2", fx.ring, true),
                streak = fx.Piece("RedStreak", fx.spark),
            };
            fx.bursts.Add(b);
        }
        for (int i = 0; i < 90; i++) fx.sparks.Add(new Spark { sr = fx.Piece("RedSpark", fx.spark) });
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
        StartBurst(x, w, false);
        int count = SettingsStore.Current.EffectQuality > 0 ? TapSparks : TapSparks / 2;
        for (int i = 0; i < count; i++) SpawnSpark(x, w, true);
    }

    public void StartHold(NoteData n)
    {
        if (n == null) return;
        Hold h = holds.Find(o => o.on && o.note == n);
        if (h != null) return;
        h = holds.Find(o => !o.on && o.level <= 0.001f) ?? holds.Find(o => !o.on);
        if (h == null) return;
        Span(n, out h.x, out h.w);
        h.note = n; h.on = true; h.sparkTimer = 0f; h.waveTimer = HoldWaveEvery * 0.5f;
    }

    public void StopHold(NoteData n)
    {
        foreach (var h in holds) if (h.on && h.note == n) h.on = false;
    }

    public void StopAll()
    {
        foreach (var h in holds) h.on = false;
    }

    // 노트가 덮는 레인들의 가운데 x 와 전체 폭 (CIRCLE 1칸 RED 는 원 지름 폭)
    private void Span(NoteData n, out float x, out float w)
    {
        float[] xs = gm.laneXPositions;
        int a = Mathf.Clamp(n.Lane, 0, xs.Length - 1);
        int b = Mathf.Clamp(n.Lane + Mathf.Max(1, n.Width) - 1, 0, xs.Length - 1);
        x = (xs[a] + xs[b]) * 0.5f;
        w = SimpleGameSkin.CircleActive ? SimpleGameSkin.CircleNoteWidth(n.Width) : Mathf.Abs(xs[b] - xs[a]) + LaneW * 0.95f;
    }

    private void StartBurst(float x, float w, bool mini)
    {
        Burst b = bursts.Find(o => o.t >= 1f);
        if (b == null) { b = bursts[0]; foreach (var o in bursts) if (o.t > b.t) b = o; }   // 다 쓰는 중이면 가장 오래된 것
        b.t = 0f; b.x = x; b.w = w; b.mini = mini;
        bool full = !mini && SettingsStore.Current.EffectQuality > 0;
        b.wave.enabled = true;
        b.flash.enabled = b.streak.enabled = b.col.enabled = !mini;
        b.wave2.enabled = b.halo.enabled = full;
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

        foreach (var b in bursts) UpdateBurst(b, dt, y, endA);

        foreach (var h in holds)
        {
            h.level = Mathf.MoveTowards(h.level, h.on ? 1f : 0f, dt / 0.1f);
            bool vis = h.level > 0.001f && endA > 0.001f;
            h.band.enabled = h.col.enabled = vis;
            if (vis)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(clock * 16f);
                Place(h.band, h.x, y, h.w, noteH * 1.2f + 0.14f * pulse);
                h.band.color = new Color(Red.r, Red.g, Red.b, HoldBandAlpha * h.level * pulse * endA);
                Place(h.col, h.x, y, h.w, 1.8f);
                h.col.color = new Color(Red.r, Red.g, Red.b, HoldColumnAlpha * h.level * endA);
            }
            if (h.on && dt > 0f)
            {
                h.sparkTimer -= dt;
                if (h.sparkTimer <= 0f) { h.sparkTimer = HoldSparkEvery; SpawnSpark(h.x, h.w, false); }
                h.waveTimer -= dt;
                if (h.waveTimer <= 0f) { h.waveTimer = HoldWaveEvery; StartBurst(h.x, h.w, true); }
            }
        }

        Vector2 ssz = spark.bounds.size;
        foreach (var s in sparks)
        {
            if (s.t >= 1f) { if (s.sr.enabled) s.sr.enabled = false; continue; }
            s.t = Mathf.Min(1f, s.t + dt / s.life);
            s.vel *= Mathf.Max(0f, 1f - s.drag * dt);              // 점점 느려짐
            s.vel.y -= 1.2f * dt;                                   // 살짝 떨어지는 느낌
            s.pos += s.vel * dt;
            s.sr.transform.position = new Vector3(s.pos.x, s.pos.y, 0f);
            float speed = s.vel.magnitude;
            float len = s.len * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(speed / 3f)) * (1f - 0.5f * s.t);   // 빠를수록 길게
            s.sr.transform.localScale = new Vector3(len / ssz.x, s.thick * (1f - 0.4f * s.t) / ssz.y, 1f);
            s.sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(s.vel.y, s.vel.x) * Mathf.Rad2Deg);
            Color c = Color.Lerp(Hot, Red, Mathf.Clamp01(s.t * 1.8f));
            s.sr.color = new Color(c.r, c.g, c.b, (1f - s.t * s.t) * endA);
            if (s.t >= 1f) s.sr.enabled = false;
        }
    }

    private void UpdateBurst(Burst b, float dt, float y, float endA)
    {
        if (b.t >= 1f)
        {
            b.flash.enabled = b.wave.enabled = b.wave2.enabled = b.streak.enabled = b.halo.enabled = b.col.enabled = false;
            return;
        }
        b.t = Mathf.Min(1f, b.t + dt / (b.mini ? 0.26f : TapSeconds));
        float t = b.t, e = 1f - (1f - t) * (1f - t);
        float h0 = noteH;

        if (b.mini)
        {
            // 홀드 중 작은 충격파
            Size(b.wave, b.x, y, b.w + WaveGrowX * 0.35f * e, h0 + WaveGrowY * 0.35f * e);
            Color mc = Color.Lerp(Hot, Red, 0.6f);
            b.wave.color = new Color(mc.r, mc.g, mc.b, 0.5f * (1f - t) * endA);
            return;
        }

        // ⓪ 노트 모양 섬광 (앞쪽 45%)
        float tf = Mathf.Clamp01(t / 0.45f), ef = 1f - (1f - tf) * (1f - tf);
        Size(b.flash, b.x, y, b.w * Mathf.Lerp(1f, 1.06f, ef), h0 * Mathf.Lerp(1f, 1.7f, ef));
        Color fc = Color.Lerp(Hot, Red, ef);
        b.flash.color = new Color(fc.r, fc.g, fc.b, FlashAlpha * (1f - tf) * (1f - tf) * endA);

        // ① 충격파
        Size(b.wave, b.x, y, b.w + WaveGrowX * e, h0 + WaveGrowY * e);
        Color wc = Color.Lerp(Hot, Red, Mathf.Clamp01(t * 1.6f));
        b.wave.color = new Color(wc.r, wc.g, wc.b, WaveAlpha * Mathf.Pow(1f - t, 1.6f) * endA);
        if (b.wave2.enabled)
        {
            float t2 = Mathf.Clamp01((t - 0.16f) / 0.84f), e2 = 1f - (1f - t2) * (1f - t2);
            Size(b.wave2, b.x, y, b.w + WaveGrowX * 1.8f * e2, h0 + WaveGrowY * 1.8f * e2);
            b.wave2.color = new Color(Red.r, Red.g, Red.b, (t < 0.16f ? 0f : 0.55f * (1f - t2)) * endA);
        }

        // ② 빛줄기: 판정선을 따라 노트 폭보다 길게
        float tk = Mathf.Clamp01(t / 0.6f), ek = 1f - Mathf.Pow(1f - tk, 3f);
        Place(b.streak, b.x, y, b.w * Mathf.Lerp(1.05f, 1.9f, ek), 0.06f * (1f - 0.5f * tk));
        b.streak.transform.rotation = Quaternion.identity;
        Color kc = Color.Lerp(Color.white, Red, tk * 0.7f);
        b.streak.color = new Color(kc.r, kc.g, kc.b, StreakAlpha * Mathf.Pow(1f - tk, 1.4f) * endA);

        // ③ 빛무리
        if (b.halo.enabled)
        {
            float hw = b.w * Mathf.Lerp(1.1f, 1.6f, e) + 0.4f;
            Place(b.halo, b.x, y, hw, Mathf.Max(h0 * 3.2f, 0.7f) * Mathf.Lerp(0.9f, 1.3f, e));
            b.halo.color = new Color(Deep.r, Deep.g, Deep.b, HaloAlpha * (1f - t) * (1f - t) * endA);
        }

        // ④ 빛 기둥
        Place(b.col, b.x, y, b.w, Mathf.Lerp(0.5f, ColumnHeight, e));
        b.col.color = new Color(Red.r, Red.g, Red.b, ColumnAlpha * (1f - t) * endA);

        if (endA <= 0.001f) b.flash.enabled = b.wave.enabled = b.wave2.enabled = b.streak.enabled = b.halo.enabled = b.col.enabled = false;
    }

    // 불꽃: 탭 = 노트 폭에서 위쪽 부채꼴로 빠르게 / 홀드 = 위로 천천히 피어오름
    private void SpawnSpark(float x, float w, bool burst)
    {
        Spark s = sparks.Find(o => o.t >= 1f);
        if (s == null) return;
        s.t = 0f;
        float ang = burst ? Random.Range(25f, 155f) : Random.Range(70f, 110f);
        float speed = burst ? Random.Range(2.6f, 5.2f) : Random.Range(1.2f, 2.2f);
        s.vel = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * speed;
        s.drag = burst ? Random.Range(3.5f, 5f) : 1.5f;
        s.life = burst ? Random.Range(0.32f, 0.55f) : Random.Range(0.35f, 0.6f);
        s.len = burst ? Random.Range(0.16f, 0.30f) : Random.Range(0.10f, 0.18f);
        s.thick = burst ? Random.Range(0.030f, 0.045f) : Random.Range(0.022f, 0.032f);
        s.pos = new Vector2(x + Random.Range(-w * 0.45f, w * 0.45f), gm.hitLineY + Random.Range(-noteH * 0.3f, noteH * 0.3f));
        s.sr.transform.position = new Vector3(s.pos.x, s.pos.y, 0f);
        s.sr.enabled = true;
    }

    // ---------------------------------------------------------------
    //  도우미
    // ---------------------------------------------------------------
    private SpriteRenderer Piece(string name, Sprite sprite, bool sliced = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (sliced) sr.drawMode = SpriteDrawMode.Sliced;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // 가운데 기준, 배율로 크기 맞춤
    private static void Place(SpriteRenderer sr, float x, float y, float w, float h)
    {
        Vector2 sz = sr.sprite.bounds.size;
        sr.transform.position = new Vector3(x, y, 0f);
        sr.transform.localScale = new Vector3(w / Mathf.Max(0.0001f, sz.x), h / Mathf.Max(0.0001f, sz.y), 1f);
    }

    // 가운데 기준, 9-slice 크기로 맞춤 (둥근 모서리가 늘어나지 않음)
    private void Size(SpriteRenderer sr, float x, float y, float w, float h)
    {
        float min = cornerR * 2f;                                       // 모서리 두 개보다 작아지지 않게
        sr.transform.position = new Vector3(x, y, 0f);
        sr.transform.localScale = Vector3.one;
        sr.size = new Vector2(Mathf.Max(min, w), Mathf.Max(min, h));
    }

    private float cornerR;   // 둥근 판 · 고리의 모서리 반지름 (월드 유닛)

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

        // 둥근 빛 (빛무리) · 길쭉한 빛 (불꽃 · 빛줄기: 가로로 가운데가 밝고 양끝 · 위아래로 사라짐)
        glow = NoteArt.GlowSprite;
        Texture2D sp = new Texture2D(32, 8, TextureFormat.RGBA32, false);
        sp.wrapMode = TextureWrapMode.Clamp;
        Color32[] spp = new Color32[32 * 8];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 32; x++)
            {
                float u = (x + 0.5f) / 32f * 2f - 1f, v = (y + 0.5f) / 8f * 2f - 1f;
                float a = Mathf.Clamp01(1f - u * u) * Mathf.Clamp01(1f - v * v);
                spp[y * 32 + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * 1.3f) * 255));
            }
        sp.SetPixels32(spp); sp.Apply();
        spark = Sprite.Create(sp, new Rect(0, 0, 32, 8), new Vector2(0.5f, 0.5f), 32f);

        // 노트 모양 둥근 판 · 고리 (9-slice). 모서리 반지름 = RED 노트 두께의 절반 (CIRCLE: 알약) / 그 밖: 조금만 둥글게
        cornerR = SimpleGameSkin.CircleActive ? noteH * 0.5f : Mathf.Min(0.06f, noteH * 0.5f);
        const int S = 64, R = 32;                                       // 텍스처 64 x 64, 모서리 32px
        float ppu = R / Mathf.Max(0.01f, cornerR);
        Texture2D ft = new Texture2D(S, S, TextureFormat.RGBA32, false), rt = new Texture2D(S, S, TextureFormat.RGBA32, false);
        ft.wrapMode = rt.wrapMode = TextureWrapMode.Clamp;
        Color32[] fp = new Color32[S * S], rp = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - (R - 1f);   // 안쪽 음수
                float inside = Mathf.Clamp01(0.5f - d);
                float soft = Mathf.Clamp01(-d / 10f);                                                         // 가장자리에서 안쪽으로 밝아짐
                fp[y * S + x] = new Color32(255, 255, 255, (byte)(inside * Mathf.Lerp(0.75f, 1f, soft) * 255f));
                float ringA = Mathf.Clamp01(1f - Mathf.Abs(d + 3f) / 3f);                                    // 두께 약 6px 고리
                float inner = d < 0f ? Mathf.Exp(d / 6f) * 0.35f : 0f;                                        // 고리 안쪽으로 번지는 빛
                rp[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(ringA, inner)) * 255f));
            }
        ft.SetPixels32(fp); ft.Apply(); rt.SetPixels32(rp); rt.Apply();
        Vector4 border = new Vector4(R, R, R, R);
        fill = Sprite.Create(ft, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border);
        ring = Sprite.Create(rt, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border);
    }
}
