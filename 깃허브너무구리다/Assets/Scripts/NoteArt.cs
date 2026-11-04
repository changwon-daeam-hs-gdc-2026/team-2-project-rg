using UnityEngine;

// ============================================================
// 코드로 만드는 그림 (에셋 파일 없이)
//  - 화살표 노트의 >>> 화살표 / 노트 빛
//  - 영역(Area) 배경의 사선 무늬
//  - 경고 아이콘 (⚠)
//  처음 쓸 때 한 번만 만들고 계속 재사용한다.
// ============================================================
public static class NoteArt
{
    public static readonly Color ArrowRightColor = new Color(0.55f, 0.90f, 1.00f, 1f);  // >>> 밝은 파랑
    public static readonly Color ArrowLeftColor = new Color(1.00f, 0.90f, 0.42f, 1f);   // <<< 밝은 노랑
    public static readonly Color AreaColor = new Color(0.62f, 0.86f, 1.00f, 1f);        // (예전) 영역 사선 무늬 색 — 지금은 아래 검은 계열 사용

    // 영역: 검은 판 + 회색 사선 + 밝은 흰 테두리 (어두운 레인 위에서도 경계가 또렷하게)
    public static readonly Color AreaBase = new Color(0.006f, 0.007f, 0.010f, 0.94f);    // 영역 바탕 (거의 검정)
    public static readonly Color AreaStripe = new Color(0.62f, 0.65f, 0.70f, 0.42f);     // 사선 무늬 (회색)
    public static readonly Color AreaEdge = new Color(1f, 1f, 1f, 0.92f);                // 옆 테두리 · 시작/끝선
    public static readonly Color AreaEdgeGlow = new Color(1f, 1f, 1f, 0.16f);            // 테두리 안쪽으로 번지는 빛
    public const float AreaEdgeWidth = 0.045f;
    public const float AreaEdgeGlowWidth = 0.20f;

    // ----- 노트 모양 (SIMPLE · SIMPLE-DIVERSE · 영역 에디터 공통) -----
    public const float NoteGlowPad = 0.05f;    // 노트 바깥 빛 여백 (월드 유닛, 위아래 · 좌우 각각) = 10px / PPU 200

    public static Color ArrowColor(int type) { return type == NoteTypes.ArrowLeft ? ArrowLeftColor : ArrowRightColor; }

    // 드래그 노트: 연한 노란색 (크림빛 — <<< 화살표의 진한 노랑과 구분되게 채도를 낮춤)
    public static readonly Color DragColor = new Color(1.00f, 0.95f, 0.66f, 1f);
    public const float DragHeightRatio = 0.58f;   // 드래그 노트 두께 = 일반 노트의 58% (얇은 레일 모양)

    private static Sprite dragGem, dragTrail;
    private static Sprite arrow, glow, stripe, warn, rounded, white, face, outline, longBody, hGrad, diamond, beam, star, tailCap, hBell;

    // 롱노트 끝: 얇은 빛나는 막대 (흰색 → 레인 색). 위가 밝고 아래로 살짝 어두움 + 바깥 빛 (TailGlowPad 만큼 크게)
    public const float TailGlowPad = 0.04f;
    public static Sprite TailCapSprite
    {
        get
        {
            if (tailCap != null) return tailCap;
            const int G = 8, BW = 60, BH = 14, R = 6;
            int W = BW + G * 2, H = BH + G * 2;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, G + R, W - G - R), cy = Mathf.Clamp(fy, G + R, H - G - R);
                float d = Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy)) - R;
                float inside = Mathf.Clamp01(0.5f - d);
                float v = Mathf.Clamp01((fy - G) / BH);
                float bright = Mathf.Lerp(0.78f, 1f, v);
                float glowA = d > 0f ? Mathf.Exp(-d / 2.6f) * 0.5f : 0f;
                float a = Mathf.Max(inside, glowA);
                byte c = (byte)(Mathf.Clamp01(inside > 0.001f ? bright : 1f) * 255f);
                px[y * W + x] = new Color32(c, c, c, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            int bd = G + R + 1;
            tailCap = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 200f, 0, SpriteMeshType.FullRect, new Vector4(bd, bd, bd, bd));
            tailCap.name = "TailCap";
            return tailCap;
        }
    }

    // 4갈래 빛 (PERFECT 히트의 반짝임). 가운데가 밝고 십자 방향으로 길게 뻗음
    public static Sprite StarSprite
    {
        get
        {
            if (star != null) return star;
            const int S = 96;
            Texture2D tex = NewTex(S, S);
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / S * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / S * 2f - 1f);
                float armH = Mathf.Pow(Mathf.Max(0f, 1f - dx), 3f) * Mathf.Exp(-dy * 22f);
                float armV = Mathf.Pow(Mathf.Max(0f, 1f - dy), 3f) * Mathf.Exp(-dx * 22f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float core = Mathf.Exp(-r * 9f);
                float a = Mathf.Clamp01(armH + armV + core);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            star = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 96f, 0, SpriteMeshType.FullRect);
            star.name = "Star";
            return star;
        }
    }

    // 가로 종 모양 (가운데가 가장 밝고 좌우 끝에서 0) — 히트의 가로 섬광 줄기 · 판정 글자 밑줄
    public static Sprite HBellSprite
    {
        get
        {
            if (hBell != null) return hBell;
            const int W = 128, H = 8;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                float d = (x + 0.5f - W * 0.5f) / (W * 0.5f);
                float a = Mathf.Clamp01(Mathf.Exp(-d * d * 5f) * (1f - d * d));
                for (int y = 0; y < H; y++)
                {
                    float vy = Mathf.Abs((y + 0.5f) / H * 2f - 1f);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * (1f - vy * vy) * 255f));
                }
            }
            tex.SetPixels32(px); tex.Apply();
            hBell = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 128f, 0, SpriteMeshType.FullRect);
            hBell.name = "HBell";
            return hBell;
        }
    }

    // =====================================================================
    //  노트 앞면 (흰색 → SpriteRenderer.color 로 레인 색)
    //   · 바깥 은은한 빛 (NoteGlowPad 만큼 크게 그려야 함)
    //   · 밝은 테두리 + 바로 안쪽 어두운 선 → 입체감
    //   · 몸체는 위가 밝고 아래로 어두워지는 세로 그라데이션 + 위쪽 광택 띠
    //   · 9-slice: 빛 · 테두리는 그대로, 가운데만 늘어남 (1 ~ 4칸 어떤 폭이든 같은 모양)
    // =====================================================================
    public static Sprite NoteFaceSprite
    {
        get
        {
            if (face != null) return face;
            const int G = 10, BW = 76, BH = 30, R = 9;
            int W = BW + G * 2, H = BH + G * 2;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                // 둥근 사각형까지의 거리 (안쪽 음수)
                float cx = Mathf.Clamp(fx, G + R, W - G - R), cy = Mathf.Clamp(fy, G + R, H - G - R);
                float d = Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy)) - R;
                float inside = Mathf.Clamp01(0.5f - d);
                float v = (fy - G) / BH;                                   // 0 = 아래, 1 = 위
                float bright = Mathf.Lerp(0.66f, 0.97f, Mathf.Clamp01(v));  // 세로 그라데이션
                float gloss = Mathf.Clamp01(1f - Mathf.Abs(v - 0.72f) / 0.17f);  // 광택 띠 (위아래 부드럽게)
                bright = Mathf.Lerp(bright, 1f, gloss * gloss * (3f - 2f * gloss));
                float edge = -d;                                             // 테두리에서 안쪽으로 몇 px
                if (edge < 2.2f) bright = 1f;                                // 밝은 테두리
                else if (edge < 3.6f) bright *= 0.72f;                       // 안쪽 어두운 선
                float glowA = d > 0f ? Mathf.Exp(-d / 3.2f) * 0.42f : 0f;   // 바깥 빛
                float a = Mathf.Max(inside, glowA);
                float b = inside > 0.001f ? bright : 1f;
                byte c = (byte)(Mathf.Clamp01(b) * 255f);
                px[y * W + x] = new Color32(c, c, c, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            int bd = G + R + 2;
            face = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 200f, 0, SpriteMeshType.FullRect, new Vector4(bd, bd, bd, bd));
            face.name = "NoteFace";
            return face;
        }
    }

    // 판정선의 키 자리(리셉터): 둥근 사각형 테두리만 (흰색, 9-slice)
    public static Sprite NoteOutlineSprite
    {
        get
        {
            if (outline != null) return outline;
            const int W = 64, H = 32, R = 10;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, R, W - R), cy = Mathf.Clamp(y + 0.5f, R, H - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - R;   // 안쪽 음수
                float ring = Mathf.Clamp01(0.5f - Mathf.Abs(d + 1.6f) + 1.1f);                        // 두께 약 3px 선
                float fill = d < 0f ? 0.10f : 0f;                                                      // 안쪽 아주 옅게
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(ring, fill)) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            outline = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 400f, 0, SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
            outline.name = "NoteOutline";
            return outline;
        }
    }

    // 롱노트 몸통 (흰색 → 레인 색): 양옆이 밝게 빛나고 가운데는 옅은 반투명 + 가운데 가는 하이라이트
    //  Sliced 로 그림: 좌우 테두리는 그대로, 가운데만 늘어남 (가운데 하이라이트가 항상 정가운데)
    public static Sprite LongBodySprite
    {
        get
        {
            if (longBody != null) return longBody;
            // 가로 단면: [밝은 바깥선][틈][가는 안쪽선][안으로 번지는 빛] ··· [가운데 부드러운 빛 기둥] ··· (좌우 대칭)
            const int W = 64, H = 8, B = 14;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                float fe = Mathf.Min(x + 0.5f, W - x - 0.5f);                 // 가장자리에서 몇 px
                float a;
                if (fe < 2.2f) a = 1f;                                          // 바깥선
                else if (fe < 3.6f) a = 0.30f;                                  // 틈
                else if (fe < 5.0f) a = 0.75f;                                  // 안쪽 가는 선
                else a = 0.14f + 0.34f * Mathf.Exp(-(fe - 5f) / 4f);            // 안쪽으로 번지는 빛
                float center = Mathf.Abs(x + 0.5f - W * 0.5f) / 7f;
                a = Mathf.Max(a, 0.14f + 0.30f * Mathf.Exp(-center * center));  // 가운데 빛 기둥
                for (int y = 0; y < H; y++) px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            longBody = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 160f, 0, SpriteMeshType.FullRect, new Vector4(B, 0, B, 0));
            longBody.name = "LongBody";
            return longBody;
        }
    }

    // 가로 그라데이션: 왼쪽 진함 → 오른쪽 투명 (피벗 왼쪽 가운데). 영역 테두리 안쪽 빛
    public static Sprite HGradientSprite
    {
        get
        {
            if (hGrad != null) return hGrad;
            Texture2D tex = NewTex(64, 4);
            Color32[] px = new Color32[64 * 4];
            for (int x = 0; x < 64; x++)
            {
                float a = Mathf.Pow(1f - x / 63f, 2.2f);
                for (int y = 0; y < 4; y++) px[y * 64 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            hGrad = Sprite.Create(tex, new Rect(0, 0, 64, 4), new Vector2(0f, 0.5f), 64f);
            hGrad.name = "HGradient";
            return hGrad;
        }
    }

    // 히트 이펙트 입자: 부드러운 마름모 (빛나는 조각)
    public static Sprite DiamondSprite
    {
        get
        {
            if (diamond != null) return diamond;
            const int S = 32;
            Texture2D tex = NewTex(S, S);
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / S * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / S * 2f - 1f);
                float d = dx + dy;                                      // 마름모 거리
                float core = Mathf.Clamp01((1f - d) * 6f);
                float halo = Mathf.Clamp01(1f - d) * 0.5f;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(core * 0.9f, halo)) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            diamond = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect);
            diamond.name = "Diamond";
            return diamond;
        }
    }

    // 드래그 노트 보석: 바깥 마름모 테두리 + 안쪽 꽉 찬 작은 마름모 + 가운데 흰 반짝임 (이중 마름모)
    public static Sprite DragGemSprite
    {
        get
        {
            if (dragGem != null) return dragGem;
            const int S = 64;
            Texture2D tex = NewTex(S, S);
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / S * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / S * 2f - 1f);
                float d = dx + dy;                                                   // 마름모 거리 (0 가운데 ~ 1 꼭짓점)
                float outer = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.07f);      // 바깥 테두리
                float inner = Mathf.Clamp01((0.52f - d) * 14f);                      // 안쪽 꽉 찬 마름모
                float halo = Mathf.Clamp01(1f - d) * 0.28f;                          // 은은한 빛
                float a = Mathf.Max(Mathf.Max(outer, inner), halo);
                // 가운데로 갈수록 흰빛 (색은 SpriteRenderer.color 로 곱해짐 → 가운데만 하얗게 보이도록 알파는 그대로, 밝기만 유지)
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            dragGem = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect);
            dragGem.name = "DragGem";
            return dragGem;
        }
    }

    // 드래그 노트 꼬리빛: 노트 위쪽(내려오는 반대 방향)으로 길게 남는 혜성 꼬리 — 아래가 밝고 위로 가늘어지며 사라짐 (피벗 아래 가운데)
    public static Sprite DragTrailSprite
    {
        get
        {
            if (dragTrail != null) return dragTrail;
            const int W = 32, H = 96;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (H - 1f);                                      // 0 아래 ~ 1 위
                float u = Mathf.Abs((x + 0.5f) / W * 2f - 1f);
                float halfW = Mathf.Lerp(1f, 0.15f, v);                      // 위로 갈수록 가늘어짐
                float side = Mathf.Clamp01(1f - u / Mathf.Max(0.01f, halfW));
                float a = Mathf.Pow(1f - v, 1.6f) * side * side;
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            dragTrail = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 64f, 0, SpriteMeshType.FullRect);
            dragTrail.name = "DragTrail";
            return dragTrail;
        }
    }

    // 히트 빛기둥: 아래(판정선)가 가장 밝고 위로 사라짐 + 좌우 가장자리도 부드럽게 (피벗 아래 가운데)
    public static Sprite BeamSprite
    {
        get
        {
            if (beam != null) return beam;
            const int W = 32, H = 64;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (H - 1f);
                float u = Mathf.Abs((x + 0.5f) / W * 2f - 1f);
                float a = Mathf.Pow(1f - v, 2.2f) * (1f - u * u * u);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            beam = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 64f, 0, SpriteMeshType.FullRect);
            beam.name = "HitBeam";
            return beam;
        }
    }

    // SIMPLE 스킨 노트와 같은 둥근 사각형 (9-slice, 흰색 → 색은 SpriteRenderer.color 로)
    public static Sprite RoundedNoteSprite
    {
        get
        {
            if (rounded != null) return rounded;
            const int W = 64, H = 32, R = 10;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, R, W - R), cy = Mathf.Clamp(y + 0.5f, R, H - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(R - d + 0.5f) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            rounded = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 400f, 0,
                                    SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
            rounded.name = "RoundedNote";
            return rounded;
        }
    }

    // 흰 사각형 1 x 1 유닛
    public static Sprite WhiteSprite
    {
        get
        {
            if (white != null) return white;
            Texture2D tex = NewTex(4, 4);
            Color32[] px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            white.name = "White";
            return white;
        }
    }

    // >>> (오른쪽 방향, 흰색). 왼쪽은 가로 배율을 음수로 뒤집어 그린다
    public static Sprite ArrowSprite
    {
        get
        {
            if (arrow != null) return arrow;
            const int W = 192, H = 64;
            Texture2D tex = NewTex(W, H);
            Color32[] px = new Color32[W * H];
            float half = 13f;          // 화살표 꺾임의 세로 반폭 (픽셀)
            float thick = 4.2f;        // 선 굵기의 절반
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float best = 999f;
                for (int i = 0; i < 3; i++)
                {
                    float cx = 44f + i * 52f;              // 각 화살표의 꼭짓점
                    Vector2 tip = new Vector2(cx + 10f, H * 0.5f);
                    Vector2 a = new Vector2(cx - 12f, H * 0.5f + half + 8f);
                    Vector2 b = new Vector2(cx - 12f, H * 0.5f - half - 8f);
                    best = Mathf.Min(best, SegDist(new Vector2(x + 0.5f, y + 0.5f), a, tip));
                    best = Mathf.Min(best, SegDist(new Vector2(x + 0.5f, y + 0.5f), b, tip));
                }
                float core = 1f - Mathf.Clamp01((best - thick) / 1.4f);      // 선 (부드러운 가장자리)
                float halo = Mathf.Exp(-Mathf.Max(0f, best - thick) / 5f) * 0.45f; // 은은한 빛
                float aOut = Mathf.Clamp01(Mathf.Max(core, halo));
                px[y * W + x] = new Color32(255, 255, 255, (byte)(aOut * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            arrow = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 200f, 0, SpriteMeshType.FullRect);
            arrow.name = "ArrowNote";
            return arrow;
        }
    }

    // 둥글게 퍼지는 빛 (가운데가 밝고 바깥으로 사라짐)
    public static Sprite GlowSprite
    {
        get
        {
            if (glow != null) return glow;
            const int S = 64;
            Texture2D tex = NewTex(S, S);
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            glow = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect);
            glow.name = "NoteGlow";
            return glow;
        }
    }

    // 영역 배경: 45도 사선 무늬 (반복 타일, SpriteDrawMode.Tiled 로 그림). 한 칸 = 0.32 월드 유닛
    public static Sprite AreaStripeSprite
    {
        get
        {
            if (stripe != null) return stripe;
            const int S = 64;
            Texture2D tex = NewTex(S, S);
            tex.wrapMode = TextureWrapMode.Repeat;
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // 한 타일에 사선 2줄 (이어 붙였을 때 끊기지 않도록 주기 32px)
                float d = Mathf.Repeat(x + y, 32f);
                float line = Mathf.Clamp01(1f - Mathf.Abs(d - 8f) / 5.5f);     // 사선 (가장자리 부드럽게)
                float a = 0.16f + 0.42f * line;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            stripe = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 200f, 0, SpriteMeshType.FullRect);
            stripe.name = "AreaStripe";
            return stripe;
        }
    }

    // 경고 아이콘: 둥근 모서리 삼각형 테두리 + 느낌표 (흰색)
    public static Sprite WarnIconSprite
    {
        get
        {
            if (warn != null) return warn;
            const int S = 96;
            Texture2D tex = NewTex(S, S);
            Color32[] px = new Color32[S * S];
            Vector2 top = new Vector2(S * 0.5f, S * 0.90f), bl = new Vector2(S * 0.08f, S * 0.12f), br = new Vector2(S * 0.92f, S * 0.12f);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(SegDist(p, top, bl), Mathf.Min(SegDist(p, bl, br), SegDist(p, br, top)));
                float a = 1f - Mathf.Clamp01((d - 3.2f) / 1.3f);                                 // 테두리
                float bar = SegDist(p, new Vector2(S * 0.5f, S * 0.62f), new Vector2(S * 0.5f, S * 0.38f));
                a = Mathf.Max(a, 1f - Mathf.Clamp01((bar - 3.6f) / 1.3f));                       // 느낌표 막대
                float dot = Vector2.Distance(p, new Vector2(S * 0.5f, S * 0.25f));
                a = Mathf.Max(a, 1f - Mathf.Clamp01((dot - 4.2f) / 1.3f));                       // 느낌표 점
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            warn = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            warn.name = "WarnIcon";
            return warn;
        }
    }

    private static Texture2D NewTex(int w, int h)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }
}