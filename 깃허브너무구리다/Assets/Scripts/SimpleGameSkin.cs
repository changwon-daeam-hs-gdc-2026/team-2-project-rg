using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
// 게임 화면 UI 종류 (설정 > GAMEPLAY > GAME UI)
//  새 스킨을 만들면 여기에 이름을 추가하고 GameManager.Start 에서 생성하면 된다.
// ============================================================
public static class GameSkins
{
    public const int Default = 0;
    public const int Simple = 1;
    public const int Diverse = 2;   // SIMPLE-DIVERSE: SIMPLE 디자인 + 원근(기울어진) 레인
    public static readonly string[] Names = { "DEFAULT", "SIMPLE", "SIMPLE-DIVERSE" };
    public static bool UsesSimpleSkin(int skin) { return skin == Simple || skin == Diverse; }
}

// ============================================================
// SIMPLE 스킨 글꼴 (GameManager 인스펙터 > SIMPLE 스킨 폰트 에서 지정, 비우면 기본 폰트)
//  TMP 폰트 에셋(○○ SDF.asset)을 끌어다 넣으면 된다. Resources 폴더에 있을 필요 없음.
// ============================================================
[Serializable]
public class SimpleSkinFonts
{
    [Tooltip("곡 제목")] public TMP_FontAsset title;
    [Tooltip("작곡가 · 난이도 · 경과 시간 · 배속 · AUTO")] public TMP_FontAsset info;
    [Tooltip("숫자: 남은 시간 · 점수 · 정확도 · 콤보")] public TMP_FontAsset number;
    [Tooltip("판정 글자 · FAST/SLOW")] public TMP_FontAsset judgement;
    [Tooltip("작은 라벨: REMAINING · SCORE · COMBO · LIFE · 키 이름")] public TMP_FontAsset label;
}

// ============================================================
// SIMPLE 게임 화면 (에셋 없이 코드로 전부 그림)
//
//  [월드]  (MV 블러) · 어두운 레인 배경 · 양쪽 벽 · 판정선 · 키 누름 빛 · 히트 플래시 · 단색 둥근 노트
//  [HUD ]  레인 위 콤보·판정·FAST/SLOW / 판정선 아래 키 이름 / LIFE / SPEED UP·DOWN / RED 예고
//          (곡 정보 · 난이도 · 시간 · 점수 · 배속 · 진행 막대는 GameInfoHud 로 옮김 — 모든 GAME UI 공통)
//
//  - 기본 화면의 레인·키빔·판정 이미지·콤보 숫자·정확도 텍스트는 숨기고 이 화면으로 대신한다.
//  - 프리키 구간(BasicUI 가 숨겨지는 구간)에서는 레인과 레인 위 HUD 도 같이 사라지고,
//    프리키 전용 UI·노트는 기본 디자인 그대로 나온다.
//  - 게임 씬에 붙어 있어서 씬이 바뀌면 함께 사라진다.
// ============================================================
public class SimpleGameSkin : MonoBehaviour
{
    public static SimpleGameSkin Active { get; private set; }

    // ----- 노트 모양 -----
    public const float NoteHeight = 0.16f;
    public const float TailHeight = 0.07f;
    public const float RedNoteHeight = 0.24f;  // 빨간(와이드) 노트는 조금 더 두껍게
    public const float RedTailHeight = 0.11f;
    public const float BodyAlpha = 0.30f;

    private static readonly Color OuterLane = new Color(0.95f, 0.97f, 1f, 1f);     // 1, 4번 레인
    private static readonly Color InnerLane = new Color(0.36f, 0.88f, 0.90f, 1f);  // 2, 3번 레인 (#5ce1e6)
    private static readonly Color RedNote = new Color(1f, 0.38f, 0.45f, 1f);
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.55f);

    public static Color LaneColor(int lane) { return (lane == 0 || lane == 3) ? OuterLane : InnerLane; }
    public static Color NoteColor(NoteData n)
    {
        if (n.IsRedNote) return RedNote;
        if (NoteTypes.IsArrow(n.Type)) return NoteArt.ArrowColor(n.Type);   // 화살표 노트: 밝은 파랑 / 밝은 노랑
        return LaneColor(n.Lane);
    }

    // ----- 판정 글자 색 -----
    private static Color JudgeColor(string judge)
    {
        switch (judge)
        {
            case "EXCELLENT":
            case "PERFECT": return new Color(0.55f, 0.95f, 1f);
            case "GREAT": return new Color(0.62f, 0.92f, 0.55f);
            case "GOOD": return new Color(0.96f, 0.84f, 0.40f);
            case "BAD": return new Color(0.95f, 0.60f, 0.32f);
            default: return new Color(0.62f, 0.64f, 0.68f);
        }
    }

    public Sprite PixelSprite { get; private set; }

    // ===== 영역(Area) · 레인 접기에서 쓰는 값 =====
    public SpriteRenderer LaneBgRenderer { get; private set; }   // 반투명 검은 레인 배경 (영역 채보에서는 AreaLaneView 가 대신 그림)
    public float LaneLeft { get { return laneLeft; } }
    public float LaneRight { get { return laneRight; } }
    public float LaneWidth { get { return laneWidth; } }
    public float LaneVisibility { get { return lastLaneVis; } }   // 등장 · 곡 종료 페이드 (0 ~ 1)
    public float LaneExpandNow { get { return Mathf.Max(0.001f, laneExpandApplied); } }
    public Transform WorldRoot { get { return worldRoot; } }
    private AreaLaneView areaView;
    public Sprite NoteSprite { get; private set; }
    private Sprite glowSprite;

    private GameManager gm;
    private float laneWidth = 0.8f;
    private float laneLeft, laneRight;

    // 월드 요소
    private Transform worldRoot;
    private readonly List<KeyValuePair<SpriteRenderer, Color>> worldSprites = new List<KeyValuePair<SpriteRenderer, Color>>();
    private SpriteRenderer[] glows = new SpriteRenderer[4];
    private readonly float[] glowLevel = new float[4];
    private readonly bool[] pressed = new bool[4];
    private readonly bool[] held = new bool[4];
    private float worldAlpha = -1f;

    // 히트 이펙트 (노트 하나 칠 때 레인마다 1개)
    //  · core : 노트 모양 섬광 — 흰색에서 노트 색으로 바뀌며 위아래로 부풀었다 사라짐
    //  · ring : 둥근 빛 — 판정선을 중심으로 넓게 퍼짐
    //  · beam : 빛기둥 — 판정선에서 레인 위쪽으로 짧게 솟았다 사라짐
    //  · wave : 노트 테두리 모양 충격파 — 노트 자리에서 바깥으로 퍼지며 옅어짐
    //  · arrow: (화살표 노트만) >>> / <<< 가 화살표 방향으로 미끄러지며 잔상 2개를 남기고 사라짐
    private class Burst
    {
        public SpriteRenderer core, ring, beam, wave, wave2, flare, star;
        public SpriteRenderer hot, hotCore;                                  // 판정선 위 밝은 광원 (넓은 빛 + 가운데 흰 심)
        public bool perfect;                                                 // PERFECT(EXCELLENT) 판정 → 4갈래 반짝임
        public bool mini;                                                    // 롱노트 홀드 중 맥박 (충격파 · 둥근 빛만 작게)
        public readonly SpriteRenderer[] arrows = new SpriteRenderer[3];   // [0] 본체, [1][2] 잔상
        public float t = 1f; public int lane; public Color c; public float power = 1f; public float x;
        public int arrowDir;                                                 // 0 = 일반, 1 = >>>, -1 = <<<
    }
    private readonly List<Burst> bursts = new List<Burst>();
    private const float BurstSeconds = 0.34f;

    // 판정선의 키 자리(리셉터): 둥근 테두리, 누르면 레인 색으로 밝아짐
    private readonly SpriteRenderer[] receptors = new SpriteRenderer[4];
    private readonly float[] receptorPop = new float[4];
    private readonly List<SpriteRenderer> laneLines = new List<SpriteRenderer>();   // 레인 구분선 3개
    private string lastJudge = "";                     // 바로 전 판정 (히트 이펙트가 PERFECT 인지 알기 위해)
    private readonly float[] holdPulseTimer = new float[4];


    // 롱노트 홀드 효과: 판정선 위에서 맥박처럼 빛나는 막대 + 위로 올라가는 작은 입자
    private readonly SpriteRenderer[] holdFlares = new SpriteRenderer[4];
    private readonly float[] holdLevel = new float[4];
    private readonly float[] holdSparkTimer = new float[4];
    private class HoldSpark { public SpriteRenderer sr; public float t = 1f; public float life; public Vector3 start; public float rise; public float size; public int lane; }
    private readonly List<HoldSpark> holdSparks = new List<HoldSpark>();
    private float holdClock;

    // LIFE 게이지 (레인 오른쪽, 둥근 막대)
    private RectTransform lifeRoot, lifeFillRt;
    private Image lifeFill, lifeBg;
    private Sprite uiRoundSprite;
    private Sprite pillSprite;   // LIFE 바: 원 텍스처를 9-slice 로 늘려 양 끝이 반원인 알약 모양

    // 고급 RED 노트: 진홍 그라데이션 + 위쪽 광택 + 안쪽 밝은 테두리 + 바깥 은은한 빛 (빛 여백만큼 크게 그림)
    public Sprite RedNoteSprite { get; private set; }
    public const float RedGlowPad = 0.05f;   // 빛 여백 (월드 유닛, 위아래·좌우 각각) = 스프라이트 G(10px) / PPU(200)
    // 고급 RED 롱노트 몸통: 양옆이 밝게 빛나고 가운데는 어두운 반투명 진홍 + 가운데 가는 하이라이트
    public Sprite RedBodySprite { get; private set; }

    // 레인 펼침 (게임 시작 시 좌우로 펼쳐짐)
    private struct LanePart { public Transform t; public Vector3 scale; public Vector3 pos; }
    private readonly List<LanePart> laneParts = new List<LanePart>();
    private float laneCenterX;
    private float laneExpand = 0f, laneExpandApplied = -1f;
    private const float LaneExpandSeconds = 0.6f;

    // SPEED UP / SPEED DOWN
    private TextMeshProUGUI speedChangeText;
    private Image speedUnderline;
    private bool speedUp;
    private int speedNoticeIndex = -1;
    private const float SpeedChangeY = 130f;   // 화면 맨 위에서 아래로 떨어진 거리 (곡 정보 줄보다 아래)
    private CanvasGroup speedChangeGroup;
    private float speedChangeTimer = 99f;
    private readonly double[] lastLayerSpeed = { 0, -1, -1 };
    private double lastCheckTime = double.MinValue;

    // RED 노트 예고
    private const double RedWarnLead = 1.5;    // 빨간 노트 판정 시각 몇 초 전에 예고할지
    private const double RedSectionGap = 2.5;  // 이 시간 이상 빨간 노트가 없다가 다시 나올 때만 예고
    private const float RedWarnDuration = 1.2f;
    private static readonly Color RedWarnColor = new Color(1f, 0.22f, 0.32f, 1f);
    private readonly SpriteRenderer[] redWarnGlows = new SpriteRenderer[4];
    private readonly float[] redWarnTime = { 99f, 99f, 99f, 99f };
    private float redEdgeTime = 99f;
    private Image redEdgeLeft, redEdgeRight;
    private Sprite hGradientSprite;
    private Sprite vBellSprite, hBellSprite;
    private int redIndex = 0;
    private double lastRedTime = -999.0;
    private bool redInit = false;
    private double lastRedCheckTime = double.MinValue;
    private float shownLife = 100f;

    // LIFE 바 모양
    public const float LifeBarWidth = 16f;        // 바 두께 (UI 픽셀)
    public const float LifeBarGap = 0.3f;         // 레인 오른쪽 끝에서 떨어진 거리 (월드 유닛)
    private const float LifeBarTopMargin = 1.4f;  // 화면 맨 위에서 띄울 거리 (월드 유닛, 곡 정보와 겹치지 않게)

    // 레인 양쪽 벽
    private const float EdgeWidth = 0.025f;
    private static readonly Color EdgeColor = new Color(1f, 1f, 1f, 0.9f);

    // MV 레인 블러
    private SimpleLaneBlur laneBlur;

    // PERFECT 판정 글자 무지개
    private bool judgeRainbow;

    // HUD
    private Canvas hostCanvas;
    private int refSortingOrder;   // 일시정지 UI 캔버스의 정렬 순서
    private RectTransform hudRoot;
    private CanvasGroup hudGroup;      // 곡 정보 / 시간 / 점수
    private CanvasGroup laneHudGroup;  // 콤보 / 판정 / 키 이름 (프리키 구간에서 숨김)
    private TextMeshProUGUI comboText, comboLabel, judgeText, fastSlowText;
    private readonly TextMeshProUGUI[] keyTexts = new TextMeshProUGUI[4];
    private float judgeTimer, comboPunch;
    private float fadeIn;
    private int lastCombo = -1;

    // =====================================================================
    //  생성
    // =====================================================================
    public static SimpleGameSkin Create(GameManager manager, bool diverse = false)
    {
        GameObject go = new GameObject("SimpleGameSkin");
        SceneManager.MoveGameObjectToScene(go, manager.gameObject.scene); // 게임 씬과 함께 정리되도록
        SimpleGameSkin skin = go.AddComponent<SimpleGameSkin>();
        skin.gm = manager;
        // 원근 셰이더가 없으면 평면 SIMPLE 로 대신 그림
        skin.IsDiverse = diverse && DiversePlayfield.IsSupported();
        Active = skin;
        skin.MakeSprites();
        skin.HideDefaultVisuals();
        skin.BuildWorld();
        skin.BuildHud();
        return skin;
    }

    // =====================================================================
    //  SIMPLE-DIVERSE (원근 레인)
    // =====================================================================
    public bool IsDiverse { get; private set; }
    private DiversePlayfield playfield;

    public DiversePlayfield Playfield { get { return playfield; } }

    // 노트 세로 배율: 기울어진 레인에서는 노트가 납작해 보이므로 조금 두껍게
    public float NoteScaleY { get { return IsDiverse ? 1.6f : 1f; } }

    // 레인 위 월드 좌표 → 화면 픽셀 (DIVERSE 는 원근 투영 기준)
    public Vector3 WorldToScreen(Vector3 world)
    {
        if (playfield != null) return playfield.WorldToScreen(world);
        Camera cam = gm.SceneCamera;
        return cam != null ? cam.WorldToScreenPoint(world) : Vector3.zero;
    }

    // 이후에 생기는 오브젝트(마디선 등)를 원근 재질로. clipBelow: 판정선 아래는 안 그림
    public void AdoptToPlayfield(GameObject go, bool clipBelow = true)
    {
        if (playfield != null && go != null) playfield.ApplyAll(go.transform, clipBelow);
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    private void MakeSprites()
    {
        // 1) 흰 사각형 (1 x 1 유닛)
        Texture2D px = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color32[] white = new Color32[16];
        for (int i = 0; i < 16; i++) white[i] = new Color32(255, 255, 255, 255);
        px.SetPixels32(white); px.Apply();
        PixelSprite = Sprite.Create(px, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);

        // 2) 둥근 사각형 (9-slice, 노트용)
        const int W = 64, H = 32, R = 10;
        Texture2D rr = new Texture2D(W, H, TextureFormat.RGBA32, false);
        rr.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, R, W - R);
                float cy = Mathf.Clamp(y + 0.5f, R, H - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(R - d + 0.5f);
                pixels[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        rr.SetPixels32(pixels); rr.Apply();
        NoteSprite = Sprite.Create(rr, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 400f, 0,
                                   SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));

        // 3) 아래가 진하고 위로 갈수록 사라지는 빛 (키 누름)
        Texture2D g = new Texture2D(4, 64, TextureFormat.RGBA32, false);
        g.wrapMode = TextureWrapMode.Clamp;
        Color32[] gp = new Color32[4 * 64];
        for (int y = 0; y < 64; y++)
        {
            float a = Mathf.Pow(1f - y / 63f, 2f);
            for (int x = 0; x < 4; x++) gp[y * 4 + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        g.SetPixels32(gp); g.Apply();
        glowSprite = Sprite.Create(g, new Rect(0, 0, 4, 64), new Vector2(0.5f, 0f), 64f); // 피벗: 아래 가운데

        // 3-0) 세로 종 모양 (가운데가 가장 밝고 위아래로 옅어짐, DIVERSE 판정선 빛)
        Texture2D vb = new Texture2D(4, 64, TextureFormat.RGBA32, false);
        vb.wrapMode = TextureWrapMode.Clamp;
        Color32[] vbp = new Color32[4 * 64];
        for (int y = 0; y < 64; y++)
        {
            float d = (y + 0.5f - 32f) / 32f;               // -1 ~ 1
            float a = Mathf.Exp(-d * d * 6f) * (1f - d * d); // 가장자리에서 정확히 0
            for (int x = 0; x < 4; x++) vbp[y * 4 + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
        }
        vb.SetPixels32(vbp); vb.Apply();
        vBellSprite = Sprite.Create(vb, new Rect(0, 0, 4, 64), new Vector2(0.5f, 0.5f), 64f);

        // 3-0b) 가로 종 모양 (가운데가 가장 밝고 좌우로 옅어짐, DIVERSE LIFE 바 빛)
        Texture2D hb = new Texture2D(64, 4, TextureFormat.RGBA32, false);
        hb.wrapMode = TextureWrapMode.Clamp;
        Color32[] hbp = new Color32[64 * 4];
        for (int x = 0; x < 64; x++)
        {
            float d = (x + 0.5f - 32f) / 32f;
            float a = Mathf.Exp(-d * d * 6f) * (1f - d * d);
            for (int yy = 0; yy < 4; yy++) hbp[yy * 64 + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
        }
        hb.SetPixels32(hbp); hb.Apply();
        hBellSprite = Sprite.Create(hb, new Rect(0, 0, 64, 4), new Vector2(0.5f, 0.5f), 64f);

        // 3-1) 가로 그라데이션 (화면 가장자리 RED 예고용, 왼쪽 진함 → 오른쪽 투명)
        Texture2D hg = new Texture2D(64, 4, TextureFormat.RGBA32, false);
        hg.wrapMode = TextureWrapMode.Clamp;
        Color32[] hp = new Color32[64 * 4];
        for (int x = 0; x < 64; x++)
        {
            float a = Mathf.Pow(1f - x / 63f, 2.2f);
            for (int y = 0; y < 4; y++) hp[y * 64 + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        hg.SetPixels32(hp); hg.Apply();
        hGradientSprite = Sprite.Create(hg, new Rect(0, 0, 64, 4), new Vector2(0f, 0.5f), 100f);

        // 3-2) 고급 RED 노트 / 롱노트 몸통
        RedNoteSprite = CreateRedNoteSprite();
        RedBodySprite = CreateRedBodySprite();

        // 4) UI 용 둥근 사각형 (LIFE 게이지)
        uiRoundSprite = Sprite.Create(rr, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
                                      SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));

        // 5) 원 (지름 64, 테두리 32 → 어떤 길이로 늘려도 양 끝이 반원)
        const int C = 64;
        Texture2D circle = new Texture2D(C, C, TextureFormat.RGBA32, false);
        circle.wrapMode = TextureWrapMode.Clamp;
        Color32[] cp = new Color32[C * C];
        for (int yy = 0; yy < C; yy++)
            for (int xx = 0; xx < C; xx++)
            {
                float d = Vector2.Distance(new Vector2(xx + 0.5f, yy + 0.5f), new Vector2(C * 0.5f, C * 0.5f));
                float a = Mathf.Clamp01(C * 0.5f - d + 0.5f);
                cp[yy * C + xx] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        circle.SetPixels32(cp); circle.Apply();
        pillSprite = Sprite.Create(circle, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, new Vector4(C / 2, C / 2, C / 2, C / 2));
    }

    // =====================================================================
    //  RED 노트 (4차 디자인) — 무늬 없이 '빛나는 붉은 유리판'
    //   · 진홍 → 짙은 와인 세로 그라데이션 몸체
    //   · 위쪽 절반에 부드러운 유리 광택 (위로 갈수록 밝게)
    //   · 바깥 밝은 분홍빛 테두리 + 바로 안쪽 어두운 선 → 입체감
    //   · 넓고 진한 붉은 바깥 빛 (일반 노트와는 색 · 빛으로만 구분)
    //   · 9-slice: 빛 여백·테두리는 늘어나지 않고 가운데만 늘어남 (가로로 무늬가 없어 어떤 폭이든 같은 모양)
    // =====================================================================
    public static Sprite CreateRedNoteSprite()
    {
        const int W = 160, H = 68, G = 10, R = 10;   // G: 빛 여백(px) = RedGlowPad * PPU
        const float PPU = 200f;
        Color top = new Color(1.00f, 0.30f, 0.40f);
        Color bottom = new Color(0.58f, 0.02f, 0.13f);
        Color rim = new Color(1.00f, 0.78f, 0.84f);
        Color glow = new Color(1.00f, 0.16f, 0.30f);

        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        float hx = W * 0.5f - G - R, hy = H * 0.5f - G - R;
        float noteH = H - 2f * G;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float qx = Mathf.Abs(fx - W * 0.5f) - hx;
                float qy = Mathf.Abs(fy - H * 0.5f) - hy;
                float sdf = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - R; // < 0 안쪽
                float inside = Mathf.Clamp01(0.5f - sdf);

                float v = Mathf.Clamp01((fy - G) / noteH);                 // 0 아래 ~ 1 위
                Color body = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, v));
                // 유리 광택: 위쪽 절반, 위로 갈수록 밝게 (경계는 부드럽게)
                float gloss = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.95f, v));
                body = Color.Lerp(body, new Color(1f, 0.72f, 0.78f), 0.38f * gloss);

                float rimT = Mathf.Clamp01(1f - Mathf.Abs(sdf + 1.3f) / 1.2f);   // 바깥 밝은 테두리 (2px)
                float darkT = Mathf.Clamp01(1f - Mathf.Abs(sdf + 3.4f) / 0.9f);  // 그 안쪽 어두운 선
                body = Color.Lerp(body, new Color(0.30f, 0.0f, 0.06f), 0.45f * darkT);
                body = Color.Lerp(body, rim, 0.85f * rimT);

                float glowA = Mathf.Exp(-Mathf.Max(sdf, 0f) / 3.6f) * 0.8f * (1f - inside);
                float a = inside + glowA;
                Color c = a > 0.0001f ? (body * inside + glow * glowA) / a : glow;
                px[y * W + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255), (byte)(Mathf.Clamp01(c.g) * 255), (byte)(Mathf.Clamp01(c.b) * 255), (byte)(Mathf.Clamp01(a) * 255));
            }
        }
        tex.SetPixels32(px); tex.Apply();
        int side = G + R + 4;           // 좌우: 빛 여백 + 모서리만 고정
        int vert = H / 2 - 1;           // 위아래: 거의 전부 고정 (세로로는 늘리지 않음)
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), PPU, 0,
                             SpriteMeshType.FullRect, new Vector4(side, vert, side, vert));
    }

    // =====================================================================
    //  RED 롱노트 몸통 (3차 디자인) — SpriteDrawMode.Tiled 로 그림
    //   · 좌우 테두리(고정): 밝은 진홍 선 + 안쪽으로 번지는 빛
    //   · 가운데(반복): 어두운 반투명 와인색 위에 45° 사선 줄무늬 (경고 테이프 느낌)
    //   · 가운데 타일이 가로·세로로 반복되므로 길이·폭이 달라져도 줄무늬 간격이 일정함
    // =====================================================================
    public const float RedBodyPPU = 160f;
    public static Sprite CreateRedBodySprite()
    {
        const int B = 10;          // 좌우 테두리 폭(px)
        const int T = 44;          // 가운데 반복 타일 크기(px, 정사각형)
        const int W = B * 2 + T, H = T;
        const float Period = 22f;  // 줄무늬 주기 (T 의 약수여야 이음새가 안 보임)
        const float StripeW = 8f;
        Color edge = new Color(1.00f, 0.45f, 0.55f);
        Color baseC = new Color(0.36f, 0.02f, 0.09f);
        Color stripeC = new Color(0.88f, 0.12f, 0.27f);

        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float e = Mathf.Min(x + 0.5f, W - x - 0.5f);           // 가장자리까지 거리
                Color c; float a;
                if (x < B || x >= W - B)
                {
                    // 테두리: 바깥 3px 밝은 선 → 안쪽으로 빛이 번짐
                    float line = Mathf.Clamp01(1f - Mathf.Abs(e - 2.2f) / 1.6f);
                    float bleed = Mathf.Exp(-(e - 2.2f) / 3.0f);
                    c = Color.Lerp(baseC, edge, Mathf.Clamp01(Mathf.Max(line, bleed * 0.75f)));
                    a = Mathf.Lerp(0.42f, 0.95f, Mathf.Max(line, bleed * 0.6f));
                    if (e < 1f) a *= 0.7f;
                }
                else
                {
                    float u = Mathf.Repeat((x - B) + y + 1f, Period);       // 45° 사선 좌표 (타일 이음새 없음)
                    float d = Mathf.Abs(u - Period * 0.5f);                     // 줄무늬 중심선까지 거리
                    float s = Mathf.Clamp01(StripeW * 0.5f - d * 0.7071f + 0.5f); // 줄무늬 안 = 1, 경계 1px 부드럽게
                    c = Color.Lerp(baseC, stripeC, s * 0.7f);
                    a = Mathf.Lerp(0.38f, 0.55f, s);
                }
                px[y * W + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(Mathf.Clamp01(a) * 255));
            }
        }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), RedBodyPPU, 0,
                             SpriteMeshType.FullRect, new Vector4(B, 0, B, 0));
    }

    // 기본 화면 요소 숨기기
    private void HideDefaultVisuals()
    {
        if (gm.hideInSimpleSkin != null)
            foreach (var go in gm.hideInSimpleSkin) if (go != null) go.SetActive(false);

        if (gm.beamRenderers != null)
        {
            foreach (var r in gm.beamRenderers) if (r != null) r.enabled = false;
            gm.beamRenderers = new SpriteRenderer[gm.beamRenderers.Length]; // 이후 켜고 끄는 코드가 아무것도 하지 않도록
        }
        if (gm.whiteLights != null)
        {
            foreach (var l in gm.whiteLights) if (l != null) l.SetActive(false);
            gm.whiteLights = new GameObject[gm.whiteLights.Length];
        }

        HideUI(gm.judgmentImage != null ? gm.judgmentImage.gameObject : null);
        HideUI(gm.comboTitleImage != null ? gm.comboTitleImage.gameObject : null);
        HideUI(gm.comboImageContainer != null ? gm.comboImageContainer.gameObject : null);
        HideUI(gm.accuracyText != null ? gm.accuracyText.gameObject : null);
        HideUI(gm.fastSlowDirectionText != null ? gm.fastSlowDirectionText.gameObject : null);
        HideUI(gm.fastSlowValueText != null ? gm.fastSlowValueText.gameObject : null);
    }

    // SetActive 로 다시 켜져도 보이지 않도록 CanvasGroup 투명도로 숨긴다
    private static void HideUI(GameObject go)
    {
        if (go == null) return;
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
    }

    // =====================================================================
    //  월드 (레인 / 판정선 / 빛)
    // =====================================================================
    private void BuildWorld()
    {
        worldRoot = new GameObject("SimpleLanes").transform;
        worldRoot.SetParent(transform, false);

        float[] xs = gm.laneXPositions;
        laneWidth = xs.Length >= 2 ? Mathf.Abs(xs[1] - xs[0]) : 0.8f;
        laneLeft = xs[0] - laneWidth * 0.5f;
        laneRight = xs[xs.Length - 1] + laneWidth * 0.5f;
        float centerX = (laneLeft + laneRight) * 0.5f;
        float fullW = laneRight - laneLeft;
        float y = gm.hitLineY;

        // 배경 (판정선 아래 3, 위로 30 유닛). 불투명도는 설정 > GAMEPLAY > LANE OPACITY
        float opacity = Mathf.Clamp01(SettingsStore.Current.LaneOpacity);
        Color bgc = IsDiverse ? new Color(0.03f, 0.04f, 0.075f, opacity) : new Color(0.035f, 0.045f, 0.07f, opacity);
        SpriteRenderer laneBg = AddRect(worldRoot, "LaneBg", centerX, y + 13.5f, fullW + 0.06f, 33f, bgc, -20);
        LaneBgRenderer = laneBg;

        // MV 레인 블러: 레인 어두운 판 바로 아래에 '흐리게 만든 MV' 를 깐다 (MV 가 나올 때만 보임)
        if (gm.simpleLaneBlur)
        {
            int blurOrder = -21;
            if (gm.bgRenderer != null && gm.bgRenderer.sortingLayerID == laneBg.sortingLayerID && gm.bgRenderer.sortingOrder >= blurOrder)
                blurOrder = gm.bgRenderer.sortingOrder; // 배경과 같은 순서면 배경보다 카메라에 가까운 블러가 위에 그려짐
            laneBlur = SimpleLaneBlur.Create(worldRoot, gm, laneLeft - 0.03f, laneRight + 0.03f, y - 3f, y + 30f,
                                             laneBg.sortingLayerID, blurOrder, gm.simpleLaneBlurStrength);
            if (laneBlur != null) laneBlur.Diverse = IsDiverse;
        }

        // 판정선 아래쪽을 조금 더 어둡게 (레인 불투명도에 비례)
        AddRect(worldRoot, "UnderLine", centerX, y - 1.5f, fullW + 0.06f, 3f, new Color(0f, 0f, 0f, 0.4f * opacity), -19);

        // 양쪽 벽 (흰 선). 레인 사이 구분선은 없음.
        //  LANE OPACITY 와 관계없이 항상 같은 진하기 (등장·곡 종료 페이드만 따라감)
        float edgeW = IsDiverse ? 0.04f : EdgeWidth;
        AddRect(worldRoot, "EdgeL", laneLeft, y + 13.5f, edgeW, 33f, EdgeColor, -19);
        AddRect(worldRoot, "EdgeR", laneRight, y + 13.5f, edgeW, 33f, EdgeColor, -19);
        if (IsDiverse)
        {
            // 벽 안쪽으로 은은하게 번지는 빛 (빛나는 레일 느낌, 폭 좁게)
            AddSprite(worldRoot, "EdgeGlowL", hGradientSprite, laneLeft, y + 13.5f, 0.22f, 33f, new Color(0.75f, 0.9f, 1f, 0.08f), -19, false);
            AddSprite(worldRoot, "EdgeGlowR", hGradientSprite, laneRight, y + 13.5f, 0.22f, 33f, new Color(0.75f, 0.9f, 1f, 0.08f), -19, true);

            // 레일 바깥쪽 가는 보조선 (이중 레일)
            AddRect(worldRoot, "RailL", laneLeft - 0.08f, y + 13.5f, 0.012f, 33f, new Color(1f, 1f, 1f, 0.28f), -19);
            AddRect(worldRoot, "RailR", laneRight + 0.08f, y + 13.5f, 0.012f, 33f, new Color(1f, 1f, 1f, 0.28f), -19);

            // 판정선 근처 바닥이 은은하게 밝음 (위로 갈수록 사라짐)
            AddSprite(worldRoot, "FloorGlow", glowSprite, centerX, y, fullW, 2.2f, new Color(0.55f, 0.80f, 1f, 0.07f), -19, false);

        }

        // 레인 구분선: 판정선에서 위로 갈수록 사라지는 아주 옅은 선 (레인이 또렷하게, 화면은 깔끔하게)
        //  설정 > GAMEPLAY > LANE LINES 로 켜고 끔 (게임 중 설정을 바꿔도 바로 반영)
        for (int i = 1; i < 4 && i < xs.Length; i++)
            laneLines.Add(AddSprite(worldRoot, "LaneDiv" + i, glowSprite, laneLeft + i * laneWidth, y, IsDiverse ? 0.018f : 0.012f, IsDiverse ? 10f : 7f,
                      new Color(1f, 1f, 1f, IsDiverse ? 0.11f : 0.09f), -19, false));

        // 키 누름 빛
        for (int i = 0; i < 4 && i < xs.Length; i++)
        {
            GameObject go = new GameObject("Glow" + i);
            go.transform.SetParent(worldRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = glowSprite;
            sr.sortingOrder = -18;
            Color c = LaneColor(i);
            sr.color = new Color(c.r, c.g, c.b, 0f);
            go.transform.position = new Vector3(xs[i], y, 0f);
            go.transform.localScale = new Vector3(laneWidth / glowSprite.bounds.size.x, IsDiverse ? 3f : 3.2f, 1f);
            glows[i] = sr;
        }

        // 판정선의 키 자리(리셉터)
        for (int i = 0; i < 4 && i < xs.Length; i++)
        {
            GameObject go = new GameObject("Receptor" + i);
            go.transform.SetParent(worldRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = NoteArt.NoteOutlineSprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(laneWidth * 0.90f, NoteHeight * 1.15f);
            sr.sortingOrder = -16;
            sr.color = new Color(1f, 1f, 1f, 0f);
            go.transform.position = new Vector3(xs[i], y, 0f);
            go.transform.localScale = new Vector3(1f, NoteScaleY, 1f);
            receptors[i] = sr;
        }

        // 판정선 (노트 아래에 그려짐)
        AddRect(worldRoot, "JudgeLine", centerX, y, fullW, IsDiverse ? 0.06f : 0.035f, new Color(1f, 1f, 1f, 0.95f), 0);
        if (IsDiverse)
            // 판정선 빛: 판정선이 가장 밝고 위아래(바깥쪽)로 갈수록 옅어짐
            AddSprite(worldRoot, "JudgeGlow", vBellSprite, centerX, y, fullW, 0.42f, new Color(1f, 1f, 1f, 0.20f), -17, false);
        else
            AddRect(worldRoot, "JudgeGlow", centerX, y, fullW, 0.16f, new Color(1f, 1f, 1f, 0.08f), -17);

        // 롱노트 홀드: 판정선 위 빛나는 막대 + 입자
        for (int i = 0; i < 4 && i < xs.Length; i++)
        {
            GameObject go = new GameObject("HoldFlare" + i);
            go.transform.SetParent(worldRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = NoteArt.NoteFaceSprite;       // 입체 노트 앞면 + 바깥 빛
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(laneWidth * 0.98f + NoteArt.NoteGlowPad * 2f, 0.22f + NoteArt.NoteGlowPad * 2f);
            sr.sortingOrder = 8;
            sr.enabled = false;
            go.transform.position = new Vector3(xs[i], y, 0f);
            holdFlares[i] = sr;
        }
        for (int i = 0; i < 40; i++)
        {
            GameObject go = new GameObject("HoldSpark" + i);
            go.transform.SetParent(worldRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PixelSprite;
            sr.sortingOrder = 9;
            sr.enabled = false;
            holdSparks.Add(new HoldSpark { sr = sr, t = 1f, life = 1f });  // 비어 있는 입자
        }

        // 히트 이펙트 풀
        for (int i = 0; i < 32; i++)
        {
            Burst b = new Burst();
            b.beam = EffectRenderer("HitBeam" + i, NoteArt.BeamSprite, -15);
            b.ring = EffectRenderer("HitRing" + i, NoteArt.GlowSprite, 6);
            b.core = EffectRenderer("HitCore" + i, NoteSprite, 7);
            b.core.drawMode = SpriteDrawMode.Sliced;
            b.wave = EffectRenderer("HitWave" + i, NoteArt.NoteOutlineSprite, 8);
            b.wave.drawMode = SpriteDrawMode.Sliced;
            b.wave2 = EffectRenderer("HitWave2_" + i, NoteArt.NoteOutlineSprite, 8);
            b.wave2.drawMode = SpriteDrawMode.Sliced;
            b.flare = EffectRenderer("HitFlare" + i, NoteArt.HBellSprite, 10);
            b.star = EffectRenderer("HitStar" + i, NoteArt.StarSprite, 11);
            b.hot = EffectRenderer("HitHot" + i, NoteArt.GlowSprite, 10);
            b.hotCore = EffectRenderer("HitHotCore" + i, NoteArt.HBellSprite, 12);
            for (int k = 0; k < 3; k++) b.arrows[k] = EffectRenderer("HitArrow" + i + "_" + k, NoteArt.ArrowSprite, 9 - k);
            bursts.Add(b);
        }

        // RED 노트 예고: 화면 위쪽에서 아래로 번지는 붉은 빛 (레인별)
        Camera cam = gm.SceneCamera;
        float topY = (cam != null && cam.orthographic) ? cam.transform.position.y + cam.orthographicSize : y + 8f;
        if (IsDiverse) topY -= 0.3f; // 먼 쪽 끝 페이드 구간 바로 아래부터
        for (int i = 0; i < 4 && i < xs.Length; i++)
        {
            GameObject go = new GameObject("RedWarn" + i);
            go.transform.SetParent(worldRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = glowSprite;
            // 피벗(위)에서 아래로 갈수록 옅어짐: flipY 대신 세로 음수 배율 (원근 셰이더에서도 동작)
            sr.sortingOrder = -17;
            sr.enabled = false;
            go.transform.position = new Vector3(xs[i], topY, 0f);
            go.transform.localScale = new Vector3(laneWidth / glowSprite.bounds.size.x, -(IsDiverse ? 5.5f : 3.4f), 1f);
            redWarnGlows[i] = sr;
        }

        // 레인 펼침용: 배경·구분선·판정선·키 자리의 원래 크기/위치 기억
        laneCenterX = centerX;
        foreach (var kv in worldSprites)
            laneParts.Add(new LanePart { t = kv.Key.transform, scale = kv.Key.transform.localScale, pos = kv.Key.transform.position });
        foreach (var r in receptors)
            if (r != null) laneParts.Add(new LanePart { t = r.transform, scale = r.transform.localScale, pos = r.transform.position });
        ApplyLaneExpand(0f);

        SetWorldAlpha(0f);

        // SIMPLE-DIVERSE: 레인 전체를 원근 레이어로 옮기고 원근 카메라 생성
        if (IsDiverse) BuildWorldLife(y);

        if (IsDiverse)
        {
            playfield = DiversePlayfield.Create(transform, gm, laneLeft, laneRight);
            if (playfield == null) IsDiverse = false;
            else
            {
                playfield.ApplyAll(worldRoot, false);
                PrewarmNoteMaterials();
                if (laneBlur != null) playfield.Register(laneBlur.ShowMaterial);
                // LIFE 바는 레인 접기(FOLD)의 영향을 받지 않음
                foreach (var sr in new[] { wLifeTrack, wLifeFrameL, wLifeFrameR, wLifeFill, wLifeGlow, wLifeHead, wLifeFoot })
                    if (sr != null) playfield.Apply(sr, false, false);
            }
        }

        // 영역 채보: 영역 배경(사선) + 영역 밖에서만 보이는 일반 레인 배경
        if (gm.IsAreaChart) areaView = AreaLaneView.Create(this, gm);

    }

    // =====================================================================
    //  SIMPLE-DIVERSE LIFE 바: 오른쪽 레일 바로 바깥, 레인과 같은 원근으로 눕혀서 그림
    //   · 어두운 트랙 + 가는 테두리 → 비어 있는 부분도 또렷하게 보임
    //   · 채움: 아래쪽이 조금 더 진한 단색 + 뒤쪽 은은한 빛
    //   · 채움 맨 위에 밝은 끝선 (현재 LIFE 위치가 잘 보이도록)
    //   · 색: 50 초과 흰색 / 25 초과 노랑 / 그 이하 빨강, 20 이하 깜빡임
    // =====================================================================
    private const float WLifeGap = 0.26f;   // 오른쪽 벽에서 떨어진 거리
    private const float WLifeLen = 5.0f;    // 판정선에서 위로 뻗는 길이
    private const float WLifeW = 0.11f;     // 두께
    private SpriteRenderer wLifeTrack, wLifeFrameL, wLifeFrameR, wLifeFill, wLifeGlow, wLifeHead, wLifeFoot;
    private readonly List<SpriteRenderer> wLifeTicks = new List<SpriteRenderer>();
    private float lastLaneVis;

    private void BuildWorldLife(float y)
    {
        float x = laneRight + WLifeGap;
        wLifeTrack = WorldPiece("LifeTrack", PixelSprite, x, y + WLifeLen * 0.5f, WLifeW, WLifeLen, -18);
        wLifeFrameL = WorldPiece("LifeFrameL", PixelSprite, x - WLifeW * 0.5f - 0.008f, y + WLifeLen * 0.5f, 0.012f, WLifeLen, -15);
        wLifeFrameR = WorldPiece("LifeFrameR", PixelSprite, x + WLifeW * 0.5f + 0.008f, y + WLifeLen * 0.5f, 0.012f, WLifeLen, -15);
        wLifeGlow = WorldPiece("LifeGlow", hBellSprite, x, y, 0.42f, 0.01f, -17);
        wLifeFill = WorldPiece("LifeFill", PixelSprite, x, y, WLifeW, 0.01f, -16);
        wLifeHead = WorldPiece("LifeHead", PixelSprite, x, y, WLifeW + 0.05f, 0.035f, -14);
        wLifeFoot = WorldPiece("LifeFoot", PixelSprite, x, y, WLifeW + 0.05f, 0.035f, -14); // 아래쪽 끝 표시 (위쪽 끝선과 같은 모양, 고정)
    }

    private SpriteRenderer WorldPiece(string name, Sprite sprite, float x, float y, float w, float h, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(worldRoot, false);
        go.transform.position = new Vector3(x, y, 0f);
        Vector2 sz = sprite.bounds.size;
        go.transform.localScale = new Vector3(w / Mathf.Max(0.0001f, sz.x), h / Mathf.Max(0.0001f, sz.y), 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    private void UpdateWorldLife(float shown, Color c, float blink)
    {
        if (wLifeFill == null) return;
        float vis = lastLaneVis;
        bool on = vis > 0.001f;
        float y = gm.hitLineY, x = laneRight + WLifeGap;
        float f = Mathf.Clamp01(shown / 100f);
        float h = Mathf.Max(0.0001f, WLifeLen * f);
        bool has = on && f > 0.001f;

        wLifeTrack.enabled = on;
        wLifeTrack.color = new Color(0.02f, 0.03f, 0.05f, 0.65f * vis);
        wLifeFrameL.enabled = wLifeFrameR.enabled = on;
        wLifeFrameL.color = wLifeFrameR.color = new Color(1f, 1f, 1f, 0.35f * vis);

        Vector2 ps = PixelSprite.bounds.size;
        wLifeFill.enabled = has;
        wLifeFill.transform.position = new Vector3(x, y + h * 0.5f, 0f);
        wLifeFill.transform.localScale = new Vector3(WLifeW / ps.x, h / ps.y, 1f);
        wLifeFill.color = new Color(c.r, c.g, c.b, 0.9f * blink * vis);

        Vector2 gs = hBellSprite.bounds.size;
        wLifeGlow.enabled = has;
        wLifeGlow.transform.position = new Vector3(x, y + h * 0.5f, 0f);
        wLifeGlow.transform.localScale = new Vector3(0.42f / gs.x, h / gs.y, 1f);
        wLifeGlow.color = new Color(c.r, c.g, c.b, 0.22f * blink * vis);

        wLifeFoot.enabled = on;
        wLifeFoot.color = new Color(1f, 1f, 1f, 0.95f * vis);
        wLifeHead.enabled = has;
        wLifeHead.transform.position = new Vector3(x, y + h, 0f);
        wLifeHead.color = new Color(1f, 1f, 1f, 0.95f * vis);

        foreach (var t in wLifeTicks)
        {
            t.enabled = on;
            t.color = new Color(1f, 1f, 1f, 0.35f * vis);
        }
    }

    // 레인이 가운데에서 좌우로 펼쳐짐 (w: 0 ~ 1)
    private void ApplyLaneExpand(float w)
    {
        if (Mathf.Abs(w - laneExpandApplied) < 0.0001f) return;
        laneExpandApplied = w;
        float k = Mathf.Max(0.001f, w);
        foreach (var p in laneParts)
        {
            if (p.t == null) continue;
            p.t.localScale = new Vector3(p.scale.x * k, p.scale.y, p.scale.z);
            p.t.position = new Vector3(laneCenterX + (p.pos.x - laneCenterX) * k, p.pos.y, p.pos.z);
        }
    }

    // 스프라이트 판 (피벗 왼쪽 가운데 그라데이션 등). flipX 면 피벗 기준 왼쪽으로 뻗음
    private SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, float x, float y, float w, float h, Color c, int order, bool flipX)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, y, 0f);
        Vector2 sz = sprite.bounds.size;
        // 뒤집기는 flipX 대신 음수 배율로 (원근 셰이더에서도 똑같이 보이도록)
        go.transform.localScale = new Vector3((flipX ? -1f : 1f) * w / Mathf.Max(0.0001f, sz.x), h / Mathf.Max(0.0001f, sz.y), 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = c;
        sr.sortingOrder = order;
        worldSprites.Add(new KeyValuePair<SpriteRenderer, Color>(sr, c));
        return sr;
    }

    // 히트 이펙트용 (처음엔 꺼 둠, 알파는 이펙트가 직접 관리)
    private SpriteRenderer EffectRenderer(string name, Sprite sprite, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(worldRoot, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    private SpriteRenderer AddRect(Transform parent, string name, float x, float y, float w, float h, Color c, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, y, 0f);
        go.transform.localScale = new Vector3(w, h, 1f); // PixelSprite = 1 x 1 유닛
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PixelSprite;
        sr.color = c;
        sr.sortingOrder = order;
        worldSprites.Add(new KeyValuePair<SpriteRenderer, Color>(sr, c));
        return sr;
    }

    private void SetWorldAlpha(float a)
    {
        if (Mathf.Abs(a - worldAlpha) < 0.001f) return;
        worldAlpha = a;
        foreach (var kv in worldSprites)
        {
            Color c = kv.Value;
            kv.Key.color = new Color(c.r, c.g, c.b, c.a * a);
            kv.Key.enabled = a > 0.001f;
        }
    }

    // =====================================================================
    //  HUD
    // =====================================================================
    private void BuildHud()
    {
        SettingsUIKit.EnsureFont();

        // HUD 전용 캔버스를 만들어 '일시정지 UI 가 들어 있는 캔버스'보다 정렬 순서를 1 낮게 둔다.
        //  → 일시정지 화면 · 로딩 화면 · 암막이 항상 HUD(곡 정보, 점수, LIFE 등) 위에 그려진다.
        Canvas refCanvas = FindSortingCanvas(gm.pauseUIContainer != null ? gm.pauseUIContainer.transform : null);
        if (refCanvas == null) refCanvas = FindSortingCanvas(gm.basicUIContainer != null ? gm.basicUIContainer.transform : null);

        GameObject cgo = new GameObject("SimpleHUDCanvas", typeof(RectTransform));
        cgo.layer = 5;
        cgo.transform.SetParent(transform, false);
        hostCanvas = cgo.AddComponent<Canvas>();
        if (refCanvas != null && refCanvas.renderMode == RenderMode.ScreenSpaceCamera && refCanvas.worldCamera != null)
        {
            hostCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            hostCanvas.worldCamera = refCanvas.worldCamera;
            hostCanvas.planeDistance = refCanvas.planeDistance;
        }
        else
        {
            hostCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        hostCanvas.sortingLayerID = refCanvas != null ? refCanvas.sortingLayerID : 0;
        refSortingOrder = refCanvas != null ? refCanvas.sortingOrder : 0;
        hostCanvas.sortingOrder = refSortingOrder - 1;

        CanvasScaler sc = cgo.AddComponent<CanvasScaler>();
        CanvasScaler refScaler = refCanvas != null ? refCanvas.rootCanvas.GetComponent<CanvasScaler>() : null;
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = refScaler != null && refScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize ? refScaler.referenceResolution : new Vector2(1920f, 1080f);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = refScaler != null ? refScaler.matchWidthOrHeight : 0.5f;
        hudRoot = SettingsUIKit.Rect("SimpleHUD", cgo.transform);
        SettingsUIKit.Stretch(hudRoot);
        hudGroup = hudRoot.gameObject.AddComponent<CanvasGroup>();
        hudGroup.blocksRaycasts = false;
        hudGroup.interactable = false;
        hudGroup.alpha = 0f;

        SimpleSkinFonts F = gm.simpleSkinFonts ?? new SimpleSkinFonts();
        // 곡 정보 · 난이도 · 시간 · 점수 · 배속 · 진행 막대는 GameInfoHud (모든 GAME UI 공통, 설정 INFORMATION UI)

        // ---- 레인 위: 콤보 / 판정 / 키 이름 ----
        RectTransform laneHud = SettingsUIKit.Rect("LaneHUD", hudRoot);
        SettingsUIKit.Stretch(laneHud);
        laneHudGroup = laneHud.gameObject.AddComponent<CanvasGroup>();
        laneHudGroup.blocksRaycasts = false;

        comboText = Text(laneHud, "", IsDiverse ? 72 : 84, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, F.number);
        if (IsDiverse) comboText.characterSpacing = 4f;
        comboText.rectTransform.sizeDelta = new Vector2(600f, 120f);
        comboLabel = Text(laneHud, "COMBO", IsDiverse ? 13 : 16, TextAlignmentOptions.Center, TextSub, FontStyles.Normal, F.label);
        comboLabel.characterSpacing = 10f;
        comboLabel.rectTransform.sizeDelta = new Vector2(300f, 24f);
        judgeText = Text(laneHud, "", IsDiverse ? 30 : 40, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, F.judgement);
        judgeText.rectTransform.sizeDelta = new Vector2(600f, 56f);
        fastSlowText = Text(laneHud, "", IsDiverse ? 15 : 18, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, F.judgement);
        fastSlowText.rectTransform.sizeDelta = new Vector2(400f, 26f);

        // ---- SPEED UP / SPEED DOWN (화면 위 가운데) ----
        RectTransform speedRt = SettingsUIKit.Rect("SpeedChange", hudRoot);
        speedRt.anchorMin = speedRt.anchorMax = new Vector2(0.5f, 1f);
        speedRt.pivot = new Vector2(0.5f, 1f);
        speedRt.anchoredPosition = new Vector2(0f, -SpeedChangeY);
        speedRt.sizeDelta = new Vector2(600f, 50f);
        speedChangeGroup = speedRt.gameObject.AddComponent<CanvasGroup>();
        speedChangeGroup.alpha = 0f;
        speedChangeGroup.blocksRaycasts = false;
        // 글자 아래 가는 밑줄 (가운데에서 양쪽으로 그어짐)
        speedUnderline = SettingsUIKit.Img("Underline", speedRt, Color.clear);
        speedUnderline.rectTransform.anchorMin = speedUnderline.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        speedUnderline.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        speedUnderline.rectTransform.anchoredPosition = new Vector2(0f, -19f);
        speedChangeText = Text(speedRt, "", 26, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, F.judgement);
        speedChangeText.characterSpacing = 8f;
        SettingsUIKit.Stretch(speedChangeText.rectTransform);

        // ---- RED 노트 예고: 화면 양쪽 가장자리 붉은 빛 ----
        redEdgeLeft = SettingsUIKit.Img("RedEdgeL", hudRoot, new Color(RedWarnColor.r, RedWarnColor.g, RedWarnColor.b, 0f));
        redEdgeLeft.sprite = hGradientSprite;
        SettingsUIKit.Place(redEdgeLeft.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(260f, 0f));
        redEdgeRight = SettingsUIKit.Img("RedEdgeR", hudRoot, new Color(RedWarnColor.r, RedWarnColor.g, RedWarnColor.b, 0f));
        redEdgeRight.sprite = hGradientSprite;
        SettingsUIKit.Place(redEdgeRight.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-260f, 0f), Vector2.zero);
        redEdgeRight.rectTransform.localScale = new Vector3(-1f, 1f, 1f); // 좌우 반전 (오른쪽이 진함)
        redEdgeLeft.enabled = redEdgeRight.enabled = false;

        // ---- LIFE 게이지 (레인 오른쪽 둥근 막대, 숫자 없음) ----
        lifeRoot = SettingsUIKit.Rect("LifeGauge", laneHud);
        lifeBg = SettingsUIKit.Img("Bg", lifeRoot, new Color(1f, 1f, 1f, 0.12f));
        // 양 끝 반지름 = 바 두께의 절반이 되도록 9-slice 배율 조정 (테두리 32px)
        lifeBg.sprite = pillSprite; lifeBg.type = Image.Type.Sliced; lifeBg.fillCenter = true;
        lifeBg.pixelsPerUnitMultiplier = 32f / (LifeBarWidth * 0.5f);
        SettingsUIKit.Stretch(lifeBg.rectTransform);
        lifeFill = SettingsUIKit.Img("Fill", lifeRoot, Color.white);
        lifeFill.sprite = pillSprite; lifeFill.type = Image.Type.Sliced; lifeFill.fillCenter = true;
        lifeFill.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, (LifeBarWidth - 6f) * 0.5f);
        lifeFillRt = lifeFill.rectTransform;
        lifeFillRt.anchorMin = Vector2.zero;
        lifeFillRt.anchorMax = Vector2.one;
        lifeFillRt.offsetMin = new Vector2(3f, 3f);
        lifeFillRt.offsetMax = new Vector2(-3f, -3f);
        // SIMPLE 스킨이면 항상 LIFE 사용 (이 시점엔 아직 GameManager 에 스킨이 등록되기 전이라 gm.LifeEnabled 로 판단하면 안 됨)
        lifeRoot.gameObject.SetActive(!IsDiverse); // DIVERSE 는 레인 옆 원근 LIFE 바(UpdateWorldLife)를 씀

        Key[] keys = SettingsStore.GetLaneKeys();
        for (int i = 0; i < 4; i++)
        {
            keyTexts[i] = Text(laneHud, SettingsStore.KeyDisplayName(keys[i]), 20, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.35f), FontStyles.Bold, F.label);
            keyTexts[i].rectTransform.sizeDelta = new Vector2(120f, 30f);
        }
    }

    // =====================================================================
    //  게임에서 부르는 함수
    // =====================================================================
    public void SetPressed(int lane, bool on)
    {
        if (lane < 0 || lane > 3) return;
        pressed[lane] = on;
        if (on) glowLevel[lane] = 1f;
    }

    public void SetHold(int lane, bool on)
    {
        if (lane < 0 || lane > 3) return;
        held[lane] = on;
    }

    public void Hit(int lane) { Hit(lane, LaneColor(lane), 1f); }

    // 노트를 알 때: 노트 색(화살표 파랑/노랑 · RED) 그대로, 롱노트 끝은 조금 작게
    public void Hit(int lane, NoteData n)
    {
        if (n == null) { Hit(lane); return; }
        bool tail = n.Type == 3 || n.Type == 13;
        int dir = n.Type == NoteTypes.ArrowRight ? 1 : n.Type == NoteTypes.ArrowLeft ? -1 : 0;
        Hit(lane, NoteColor(n), tail ? 0.7f : 1f, dir);
    }

    public void Hit(int lane, Color c, float power, int arrowDir = 0) { Hit(lane, c, power, arrowDir, false); }

    private void Hit(int lane, Color c, float power, int arrowDir, bool mini)
    {
        if (lane < 0 || lane > 3 || lane >= gm.laneXPositions.Length) return;
        Burst b = bursts.Find(x => x.t >= 1f);
        if (b == null) { b = bursts[0]; foreach (var o in bursts) if (o.t > b.t) b = o; }   // 다 쓰는 중이면 가장 오래된 것
        b.t = 0f;
        b.lane = lane;
        b.c = c;
        b.power = power;
        b.x = gm.laneXPositions[lane];
        b.arrowDir = mini ? 0 : arrowDir;
        b.mini = mini;
        b.perfect = !mini && (lastJudge == "EXCELLENT" || lastJudge == "JUST");
        b.ring.enabled = b.wave.enabled = true;
        // 설정 > 게임플레이 > 히트 이펙트 = 낮음 이면 두 번째 충격파 · 가로 섬광 · 광원 · 반짝임은 생략 (가벼움)
        bool full = !mini && SettingsStore.Current.EffectQuality > 0;
        b.core.enabled = b.beam.enabled = !mini;
        b.wave2.enabled = b.flare.enabled = b.hot.enabled = b.hotCore.enabled = full;
        if (!full) b.perfect = false;
        b.star.enabled = b.perfect;
        Vector3 at = new Vector3(b.x, gm.hitLineY, 0f);
        b.beam.transform.position = at; b.ring.transform.position = at; b.core.transform.position = at;     // (배열을 만들지 않음 — 칠 때마다 쓰레기 생김 방지)
        b.wave.transform.position = at; b.wave2.transform.position = at; b.flare.transform.position = at;
        b.star.transform.position = at; b.hot.transform.position = at; b.hotCore.transform.position = at;
        b.star.transform.rotation = Quaternion.identity;
        for (int k = 0; k < 3; k++) b.arrows[k].enabled = b.arrowDir != 0;
        if (!mini) receptorPop[lane] = 1f;
    }

    public void OnJudge(string judge)
    {
        // 게임 내부 판정 이름은 EXCELLENT 그대로, 화면에는 PERFECT 로 표시 (무지개 그라데이션)
        //  "JUST" = JUST PERFECT (±15ms, 설정에서 표시를 켰을 때만 이 이름으로 옴)
        string shown = judge == "EXCELLENT" ? "PERFECT" : (judge == "JUST" ? "JUST PERFECT" : judge);
        judgeRainbow = judge == "EXCELLENT" || judge == "JUST";
        lastJudge = judge;
        judgeText.text = shown;
        judgeText.color = judgeRainbow ? Color.white : JudgeColor(judge);
        judgeTimer = 0f;
        fastSlowText.text = "";
    }

    public void OnFastSlow(bool isFast, int ms)
    {
        fastSlowText.text = (isFast ? "-" : "+") + ms + "ms";        // FAST 26 → -26ms,  SLOW 43 → +43ms
        fastSlowText.color = isFast ? new Color(0.53f, 0.81f, 0.92f) : new Color(1f, 0.42f, 0.42f);
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    private bool foldWarned;

    // [렉 수정] 노트 그림(코드로 만드는 텍스처)과 원근 재질을 플레이 전에 미리 만들어 둠
    //  예전에는 첫 노트 · 첫 롱노트 · 영역 노트가 나오는 순간에 만들어져서 그 프레임이 끊겼다
    private void PrewarmNoteMaterials()
    {
        if (playfield == null) return;
        Sprite[] sprites = { NoteArt.NoteFaceSprite, NoteArt.TailCapSprite, NoteArt.LongBodySprite, NoteArt.ArrowSprite, NoteArt.GlowSprite,
                             NoteSprite, RedNoteSprite, RedBodySprite, PixelSprite };
        foreach (var sp in sprites)
        {
            if (sp == null) continue;
            playfield.MaterialFor(sp.texture, true, true);
            playfield.MaterialFor(sp.texture, true, false);
            playfield.MaterialFor(sp.texture, false, true);
            playfield.MaterialFor(sp.texture, false, false);
        }
    }

    // 레인 접기 중에도 '지금 영역인 레인'의 누름 빛 · 롱노트 홀드 빛 · 타격 섬광 · 불꽃은 접히지 않게 (영역 노트와 함께 보이도록)
    private readonly bool[] keepLane = new bool[4];
    private readonly Dictionary<SpriteRenderer, bool> appliedFold = new Dictionary<SpriteRenderer, bool>();

    // [렉 수정] 재질은 '접힘 여부가 바뀔 때만' 바꿈 (예전에는 매 프레임 수십 개를 다시 지정)
    private void ApplyFoldIfChanged(SpriteRenderer sr, bool fold)
    {
        if (sr == null) return;
        bool cur;
        if (appliedFold.TryGetValue(sr, out cur) && cur == fold) return;
        appliedFold[sr] = fold;
        playfield.Apply(sr, false, fold);
    }

    private void KeepAreaLaneEffects()
    {
        if (playfield == null || !gm.HasAreas || !gm.HasLaneFolds) return;   // 접기가 없는 채보는 할 일 없음
        bool[] keep = keepLane;
        for (int i = 0; i < 4; i++) keep[i] = gm.LaneInAreaNow(i);
        for (int i = 0; i < 4; i++)
        {
            ApplyFoldIfChanged(glows[i], !keep[i]);
            ApplyFoldIfChanged(holdFlares[i], !keep[i]);
            ApplyFoldIfChanged(receptors[i], !keep[i]);
        }
        foreach (var b in bursts)
        {
            if (b.t >= 1f || b.lane < 0 || b.lane > 3) continue;
            bool f = !keep[b.lane];
            ApplyFoldIfChanged(b.core, f); ApplyFoldIfChanged(b.ring, f); ApplyFoldIfChanged(b.beam, f);
            ApplyFoldIfChanged(b.wave, f); ApplyFoldIfChanged(b.wave2, f); ApplyFoldIfChanged(b.flare, f);
            ApplyFoldIfChanged(b.star, f); ApplyFoldIfChanged(b.hot, f); ApplyFoldIfChanged(b.hotCore, f);
            if (b.arrowDir != 0) for (int k = 0; k < 3; k++) ApplyFoldIfChanged(b.arrows[k], f);
        }
    }

    void Update()
    {
        if (gm == null) return;
        float dt = Time.unscaledDeltaTime;

        // 레인이 나타나기 시작하면: 빠르게 보이면서 가운데에서 좌우로 펼쳐짐
        bool show = gm.LaneIntroStarted || gm.currentState == GameState.Playing;
        fadeIn = Mathf.MoveTowards(fadeIn, show ? 1f : 0f, dt / 0.35f);
        if (show) laneExpand = Mathf.MoveTowards(laneExpand, 1f, dt / LaneExpandSeconds);
        ApplyLaneExpand(1f - Mathf.Pow(1f - laneExpand, 3f));

        // 프리키 구간: 기본 UI(BasicUI)가 숨겨지는 만큼 레인과 레인 HUD 도 숨김
        float basicVis = 1f;
        if (gm.basicUIContainer != null)
        {
            if (!gm.basicUIContainer.activeInHierarchy) basicVis = 0f;
            else
            {
                CanvasGroup cg = gm.basicUIContainer.GetComponent<CanvasGroup>();
                if (cg != null) basicVis = cg.alpha;
            }
        }

        // 일시정지 중에는 일시정지 UI 캔버스보다 아래, 플레이 중에는 게임 UI(배경 막 등)보다 위에 그린다
        hostCanvas.sortingOrder = gm.currentState == GameState.Paused ? refSortingOrder - 1 : refSortingOrder + 1;

        // 곡 종료 / FAILED 때 GameManager.EndUiAlpha 를 따라 함께 사라짐
        float endA = gm.EndUiAlpha;
        float laneVis = fadeIn * basicVis * endA;
        SetWorldAlpha(laneVis);
        bool lines = SettingsStore.Current.LaneLines && laneVis > 0.001f;
        foreach (var l in laneLines) if (l != null && l.enabled != lines) l.enabled = lines;
        hudGroup.alpha = fadeIn * endA;
        laneHudGroup.alpha = basicVis;

        lastLaneVis = laneVis;
        if (laneBlur != null) laneBlur.Tick(laneVis, laneExpandApplied);

        // 레인 접기 / 펼치기 (채보의 FOLD · UNFOLD, SIMPLE-DIVERSE 전용): 레인 · 노트 · 효과가 가운데로 모이며 사라짐
        // 레인 접기는 SIMPLE-DIVERSE 원근 셰이더가 함 → 셰이더가 없으면 접히지 않는다 (원인 확인용 경고 1번)
        if (playfield == null && !foldWarned && gm.HasLaneFolds)
        {
            foldWarned = true;
            Debug.LogWarning("[SimpleGameSkin] 채보에 FOLD/UNFOLD 가 있지만 SIMPLE-DIVERSE 원근 셰이더(Resources/Shaders/DiverseSprite.shader + DiverseProject.cginc)가 없거나 스킨이 SIMPLE-DIVERSE 가 아니라서 레인이 접히지 않습니다.");
        }
        if (playfield != null)
        {
            float fold = gm.LaneFoldAt(gm.currentPlayTime);
            playfield.SetFold(fold, fold, laneCenterX);
            laneHudGroup.alpha = basicVis * fold;
            KeepAreaLaneEffects();
        }
        UpdateGlows(dt, laneVis);
        UpdateHold(dt, laneVis);
        UpdateBursts(dt, laneVis);
        UpdateLife(dt);
        UpdateSpeedChange(dt);
        UpdateRedWarning(dt, laneVis);
        UpdateHudValues(dt);
        PlaceLaneHud();
    }

    // 기본 레인 그룹은 GameManager 가 알파를 올리므로 매 프레임 마지막에 다시 0 으로
    void LateUpdate()
    {
        if (gm != null && gm.laneGroup != null) gm.laneGroup.alpha = 0f;
    }

    private void UpdateGlows(float dt, float vis)
    {
        for (int i = 0; i < 4; i++)
        {
            if (glows[i] == null) continue;
            float target = held[i] ? 0.85f : (pressed[i] ? 0.6f : 0f);
            glowLevel[i] = target > glowLevel[i] ? target : Mathf.MoveTowards(glowLevel[i], target, dt / 0.15f);
            Color c = LaneColor(i);
            glows[i].color = new Color(c.r, c.g, c.b, 0.28f * glowLevel[i] * vis);
            glows[i].enabled = glowLevel[i] > 0.001f && vis > 0.001f;

            // 키 자리: 평소엔 옅은 흰 테두리, 누르면 레인 색으로 밝아지고 칠 때 살짝 커졌다 돌아옴
            SpriteRenderer r = receptors[i];
            if (r == null) continue;
            receptorPop[i] = Mathf.MoveTowards(receptorPop[i], 0f, dt / 0.12f);
            float lit = Mathf.Max(glowLevel[i], receptorPop[i]);
            Color rc = Color.Lerp(new Color(1f, 1f, 1f, 0.16f), new Color(c.r, c.g, c.b, 0.85f), lit);
            r.color = new Color(rc.r, rc.g, rc.b, rc.a * vis);
            float pop = 1f + 0.06f * receptorPop[i];
            Vector2 want = new Vector2(laneWidth * 0.90f * pop, NoteHeight * 1.15f * pop);
            if (r.size != want) r.size = want;                                        // 바뀔 때만 (메시 다시 만들기 줄임)
            r.enabled = vis > 0.001f && SettingsStore.Current.ShowKeyMarkers;     // 설정 > GAMEPLAY > KEY MARKERS
        }
    }

    private void UpdateBursts(float dt, float vis)
    {
        float sy = NoteScaleY;
        foreach (var b in bursts)
        {
            if (b.t >= 1f) continue;
            b.t = Mathf.Min(1f, b.t + dt / (b.mini ? 0.28f : BurstSeconds));
            if (b.mini)
            {
                // 홀드 맥박: 노트 테두리 모양이 살짝 퍼지고 둥근 빛이 옅게
                float tm = b.t, em = 1f - (1f - tm) * (1f - tm);
                Color mc = Color.Lerp(Color.white, b.c, 0.5f);
                b.wave.size = new Vector2(laneWidth * Mathf.Lerp(0.95f, 1.05f, em), NoteHeight * Mathf.Lerp(1.2f, 1.9f, em));
                b.wave.transform.localScale = new Vector3(1f, sy, 1f);
                b.wave.color = new Color(mc.r, mc.g, mc.b, 0.55f * (1f - tm) * vis);
                Vector2 gsm = NoteArt.GlowSprite.bounds.size;
                float rwm = laneWidth * Mathf.Lerp(0.6f, 0.95f, em);
                b.ring.transform.localScale = new Vector3(rwm / gsm.x, rwm * 0.4f * sy / gsm.y, 1f);
                b.ring.color = new Color(b.c.r, b.c.g, b.c.b, 0.35f * (1f - tm) * vis);
                if (b.t >= 1f) b.ring.enabled = b.wave.enabled = false;
                continue;
            }
            float t = b.t, e = 1f - (1f - t) * (1f - t);           // 빠르게 시작해서 천천히 끝남
            Color c = b.c;
            Color w2c = Color.Lerp(Color.white, c, Mathf.Clamp01(t * 2.2f));

            // 노트 모양 섬광 (앞쪽 55% 동안)
            float tc = Mathf.Clamp01(t / 0.55f), ec = 1f - (1f - tc) * (1f - tc);
            b.core.size = new Vector2(laneWidth * Mathf.Lerp(0.95f, 1.03f, ec), NoteHeight * Mathf.Lerp(1.0f, 2.1f, ec) * b.power);
            b.core.transform.localScale = new Vector3(1f, sy, 1f);
            b.core.color = new Color(w2c.r, w2c.g, w2c.b, 0.95f * (1f - tc) * vis);

            // 둥근 빛
            Vector2 gs = NoteArt.GlowSprite.bounds.size;
            float rw = laneWidth * Mathf.Lerp(0.55f, 1.12f, e) * (0.8f + 0.2f * b.power);
            b.ring.transform.localScale = new Vector3(rw / gs.x, rw * 0.42f * sy / gs.y, 1f);
            Color rc2 = Color.Lerp(Color.white, c, Mathf.Clamp01(t * 1.5f));
            b.ring.color = new Color(rc2.r, rc2.g, rc2.b, 0.95f * (1f - t) * (1f - t) * vis);

            // 빛기둥
            Vector2 bs = NoteArt.BeamSprite.bounds.size;
            float bw = laneWidth * Mathf.Lerp(0.92f, 0.62f, e);
            float bh = Mathf.Lerp(0.9f, 2.1f, e) * b.power * (IsDiverse ? 1.6f : 1f);   // 이전보다 조금 짧게
            b.beam.transform.localScale = new Vector3(bw / bs.x, bh / bs.y, 1f);
            b.beam.color = new Color(w2c.r, w2c.g, w2c.b, 0.32f * Mathf.Pow(1f - t, 1.6f) * vis);   // 이전보다 조금 약하게 (0.42 → 0.32)

            // 판정선 위 광원 (강화): 레인 폭 안에서 강하게 번쩍하고 조금 오래 남음
            float th = Mathf.Clamp01(t / 0.65f);
            float hw = laneWidth * Mathf.Lerp(1.15f, 0.9f, th);
            b.hot.transform.localScale = new Vector3(hw / gs.x, hw * 0.62f * sy / gs.y, 1f);
            Color hc = Color.Lerp(Color.white, c, th * 0.55f);
            b.hot.color = new Color(hc.r, hc.g, hc.b, Mathf.Pow(1f - th, 1.4f) * vis);
            //  가운데 흰 심: 판정선 위에 아주 밝은 가로 빛 (레인 폭 정도)
            Vector2 hsz = NoteArt.HBellSprite.bounds.size;
            float tcw = Mathf.Clamp01(t / 0.5f);
            b.hotCore.transform.localScale = new Vector3(laneWidth * Mathf.Lerp(1.25f, 1.0f, tcw) / hsz.x, 0.16f * sy * (1f - 0.4f * tcw) / hsz.y, 1f);
            b.hotCore.color = new Color(1f, 1f, 1f, (1f - tcw) * (1f - tcw) * vis);

            // 충격파: 노트 테두리 모양이 바깥으로 퍼짐
            b.wave.size = new Vector2(laneWidth * Mathf.Lerp(0.92f, 1.10f, e), NoteHeight * Mathf.Lerp(1.1f, 2.4f, e) * b.power);
            b.wave.transform.localScale = new Vector3(1f, sy, 1f);
            b.wave.color = new Color(w2c.r, w2c.g, w2c.b, 0.85f * Mathf.Pow(1f - t, 1.5f) * vis);

            // 두 번째 충격파: 조금 늦게, 더 크게 퍼짐
            float t2 = Mathf.Clamp01((t - 0.18f) / 0.82f), e2 = 1f - (1f - t2) * (1f - t2);
            b.wave2.size = new Vector2(laneWidth * Mathf.Lerp(0.95f, 1.20f, e2), NoteHeight * Mathf.Lerp(1.2f, 3.0f, e2) * b.power);
            b.wave2.transform.localScale = new Vector3(1f, sy, 1f);
            b.wave2.color = new Color(c.r, c.g, c.b, (t < 0.18f ? 0f : 0.55f * (1f - t2)) * vis);

            // 가로 섬광 줄기: 판정선을 따라 좌우로 길게 번쩍 (빠르게 사라짐)
            Vector2 fsz = NoteArt.HBellSprite.bounds.size;
            float tf = Mathf.Clamp01(t / 0.75f);
            float fw = laneWidth * Mathf.Lerp(1.1f, 1.7f, 1f - (1f - tf) * (1f - tf)) * b.power;
            b.flare.transform.localScale = new Vector3(fw / fsz.x, 0.13f * sy * (1f - 0.4f * tf) / fsz.y, 1f);
            b.flare.color = new Color(w2c.r, w2c.g, w2c.b, Mathf.Pow(1f - tf, 1.5f) * vis);

            // PERFECT: 가운데 4갈래 반짝임 (돌면서 커졌다 작아짐)
            if (b.perfect)
            {
                Vector2 ssz = NoteArt.StarSprite.bounds.size;
                float ts = Mathf.Clamp01(t / 0.7f);
                float grow = Mathf.Sin(ts * Mathf.PI);                       // 0 → 1 → 0
                float sw = laneWidth * (0.45f + 0.55f * grow);
                b.star.transform.localScale = new Vector3(sw / ssz.x, sw * sy / ssz.y, 1f);
                b.star.transform.rotation = Quaternion.Euler(0f, 0f, 45f * ts);
                b.star.color = new Color(1f, 1f, 1f, 0.95f * (1f - ts) * vis);
            }

            // 화살표 노트: >>> / <<< 가 화살표 방향으로 미끄러지며 잔상을 남김 (둥근 빛도 그쪽으로 끌려감)
            if (b.arrowDir != 0)
            {
                float dir = b.arrowDir;
                b.ring.transform.position = new Vector3(b.x + dir * laneWidth * 0.20f * e, gm.hitLineY, 0f);
                Vector2 asz = NoteArt.ArrowSprite.bounds.size;
                float lift = 0.13f * sy;
                for (int k = 0; k < 3; k++)
                {
                    float lag = k * 0.16f;                                        // 잔상은 조금씩 늦게 따라옴
                    float tk = Mathf.Clamp01((t - lag) / (1f - lag));
                    float ek = 1f - (1f - tk) * (1f - tk);
                    SpriteRenderer a = b.arrows[k];
                    float stretch = 1f + 0.35f * ek;
                    a.transform.position = new Vector3(b.x + dir * laneWidth * 0.45f * ek, gm.hitLineY + lift, 0f);
                    a.transform.localScale = new Vector3(dir * 0.62f * stretch / asz.x, 0.24f * sy * (1f - 0.3f * ek) / asz.y, 1f);
                    float ka = k == 0 ? 1f : (k == 1 ? 0.5f : 0.25f);
                    float alive = t < lag ? 0f : 1f;
                    a.color = new Color(w2c.r, w2c.g, w2c.b, ka * alive * (1f - tk) * vis);
                }
            }

            if (b.t >= 1f)
            {
                b.core.enabled = b.ring.enabled = b.beam.enabled = b.wave.enabled = false;
                b.wave2.enabled = b.flare.enabled = b.star.enabled = b.hot.enabled = b.hotCore.enabled = false;
                for (int k = 0; k < 3; k++) b.arrows[k].enabled = false;
            }
        }
    }

    private void UpdateHudValues(float dt)
    {
        // 콤보
        int combo = gm.Combo;
        if (combo != lastCombo)
        {
            if (combo > lastCombo && combo > 0) comboPunch = 1f;
            lastCombo = combo;
            comboText.text = combo > 0 ? combo.ToString() : "";
            comboLabel.gameObject.SetActive(combo > 0);
        }
        comboPunch = Mathf.MoveTowards(comboPunch, 0f, dt / 0.12f);
        comboText.rectTransform.localScale = Vector3.one * (1f + 0.12f * comboPunch);

        // 판정 글자: 커졌다 → 제자리, 0.35초 뒤 사라짐
        judgeTimer += dt;
        float pop = Mathf.Clamp01(judgeTimer / 0.08f);
        judgeText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, pop);
        float fade = judgeTimer < 0.35f ? 1f : Mathf.Clamp01(1f - (judgeTimer - 0.35f) / 0.2f);
        judgeText.alpha = fade;
        fastSlowText.alpha = fade;
        if (judgeRainbow && fade > 0.001f) RainbowText.Apply(judgeText, Time.unscaledTime); // alpha 를 바꾼 뒤에 칠해야 유지됨
    }

    // 월드 좌표(레인 위치)에 맞춰 HUD 글자 배치
    private void PlaceLaneHud()
    {
        Camera cam = gm.SceneCamera;
        if (cam == null) return;
        float cx = (laneLeft + laneRight) * 0.5f;
        float y = gm.hitLineY;
        if (IsDiverse)
        {
            // 레인 가운데 높이: COMBO 라벨 → 큰 숫자 → 판정 → FAST/SLOW 순서로 위에서 아래로
            SetAtWorld(comboText.rectTransform, cam, new Vector3(cx, y + 4.4f, 0f));
            SetAtWorld(comboLabel.rectTransform, cam, new Vector3(cx, y + 4.4f, 0f), new Vector2(0f, 46f));
            SetAtWorld(judgeText.rectTransform, cam, new Vector3(cx, y + 4.4f, 0f), new Vector2(0f, -58f));
            SetAtWorld(fastSlowText.rectTransform, cam, new Vector3(cx, y + 4.4f, 0f), new Vector2(0f, -86f));
        }
        else
        {
            SetAtWorld(comboText.rectTransform, cam, new Vector3(cx, y + 3.6f, 0f));
            SetAtWorld(comboLabel.rectTransform, cam, new Vector3(cx, y + 3.6f, 0f), new Vector2(0f, -58f));
            SetAtWorld(judgeText.rectTransform, cam, new Vector3(cx, y + 1.7f, 0f));
            SetAtWorld(fastSlowText.rectTransform, cam, new Vector3(cx, y + 1.7f, 0f), new Vector2(0f, -38f));
        }
        for (int i = 0; i < 4 && i < gm.laneXPositions.Length; i++)
            SetAtWorld(keyTexts[i].rectTransform, cam, new Vector3(gm.laneXPositions[i], y - 0.45f, 0f));


        // LIFE 바: 판정선 높이부터 화면 위쪽(여백 LifeBarTopMargin)까지 길게, 레인 오른쪽 바깥
        if (lifeRoot != null && lifeRoot.gameObject.activeSelf)
        {
            float gx = laneRight + LifeBarGap;
            float topY = cam.orthographic ? cam.transform.position.y + cam.orthographicSize - LifeBarTopMargin : y + 7f;
            if (IsDiverse) topY = y + 3.2f; // 원근: 먼 쪽은 화면에서 짧아지므로 가까운 구간까지만
            topY = Mathf.Max(y + 1f, topY);
            Vector2 bottom, top;
            if (WorldToLocal(cam, new Vector3(gx, y, 0f), out bottom) && WorldToLocal(cam, new Vector3(gx, topY, 0f), out top))
            {
                lifeRoot.anchorMin = lifeRoot.anchorMax = new Vector2(0.5f, 0.5f);
                lifeRoot.pivot = new Vector2(0.5f, 0f);
                lifeRoot.anchoredPosition = bottom;
                lifeRoot.sizeDelta = new Vector2(LifeBarWidth, Mathf.Max(40f, top.y - bottom.y));
            }
        }
    }

    private bool WorldToLocal(Camera worldCam, Vector3 world, out Vector2 local)
    {
        Vector3 screen = WorldToScreen(world);
        Camera uiCam = hostCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hostCanvas.worldCamera;
        bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRoot, screen, uiCam, out local);
        local -= hudRoot.rect.center;
        return ok;
    }

    // =====================================================================
    //  롱노트 홀드 효과
    // =====================================================================
    private void UpdateHold(float dt, float vis)
    {
        holdClock += dt;
        for (int i = 0; i < 4; i++)
        {
            SpriteRenderer f = holdFlares[i];
            if (f == null) continue;
            holdLevel[i] = Mathf.MoveTowards(holdLevel[i], held[i] ? 1f : 0f, dt / 0.08f);
            bool on = holdLevel[i] > 0.001f && vis > 0.001f;
            f.enabled = on;
            if (on)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(holdClock * 18f + i * 1.3f);
                Color c = Color.Lerp(LaneColor(i), Color.white, 0.45f);
                f.color = new Color(c.r, c.g, c.b, 0.75f * holdLevel[i] * pulse * vis);
                float pad = NoteArt.NoteGlowPad * 2f;
                f.size = new Vector2(laneWidth * 0.98f + pad, 0.16f + 0.08f * pulse + pad);
                f.transform.localScale = new Vector3(1f, NoteScaleY, 1f);
            }

            // 누르고 있는 동안 일정 간격으로 노트 테두리 모양 맥박이 퍼짐 (빛 조각 대신)
            if (held[i] && vis > 0.001f && SettingsStore.Current.EffectQuality > 0)
            {
                holdPulseTimer[i] -= dt;
                if (holdPulseTimer[i] <= 0f)
                {
                    holdPulseTimer[i] = 0.16f;
                    Hit(i, LaneColor(i), 0.5f, 0, true);
                }
            }
            else holdPulseTimer[i] = 0f;
        }

        foreach (var s in holdSparks)
        {
            if (s.t >= 1f) continue;
            s.t = Mathf.Min(1f, s.t + dt / s.life);
            float e = 1f - (1f - s.t) * (1f - s.t);
            s.sr.transform.position = s.start + new Vector3(0f, s.rise * e, 0f);
            float sz = s.size * (1f - 0.5f * s.t);
            s.sr.transform.localScale = new Vector3(sz, sz, 1f);
            Color c = Color.Lerp(Color.white, LaneColor(s.lane), s.t);
            s.sr.color = new Color(c.r, c.g, c.b, 0.9f * (1f - s.t) * vis);
            if (s.t >= 1f) s.sr.enabled = false;
        }
    }

    private void SpawnHoldSpark(int lane)
    {
        HoldSpark s = holdSparks.Find(x => x.t >= 1f);
        if (s == null || lane >= gm.laneXPositions.Length) return;
        s.t = 0f;
        s.lane = lane;
        s.life = UnityEngine.Random.Range(0.28f, 0.45f);
        s.rise = UnityEngine.Random.Range(0.9f, 1.6f);
        s.size = UnityEngine.Random.Range(0.035f, 0.065f);
        s.start = new Vector3(gm.laneXPositions[lane] + UnityEngine.Random.Range(-laneWidth * 0.4f, laneWidth * 0.4f), gm.hitLineY, 0f);
        s.sr.transform.position = s.start;
        s.sr.enabled = true;
    }

    // =====================================================================
    //  SPEED UP / SPEED DOWN: 스크롤 배속이 바뀌는 순간 화면 위에 표시
    //   (STOP / START 처럼 0 이 끼는 변화는 표시하지 않음, 일시정지 후 되감기 때도 표시하지 않음)
    // =====================================================================
    private void UpdateSpeedChange(float dt)
    {
        // 채보에서 SHOW 표시된 변속만 (에디터에서 SPEED 표시를 우클릭해서 켠 것). 일반 변속은 표시하지 않음
        var list = gm.SpeedNotices;
        double now = gm.currentPlayTime;
        bool playing = gm.currentState == GameState.Playing && !gm.IsFailed;
        bool jumped = now < lastCheckTime - 0.05; // 일시정지 후 되감기
        if (list != null)
        {
            if (jumped || speedNoticeIndex < 0)
            {
                speedNoticeIndex = 0;
                while (speedNoticeIndex < list.Count && list[speedNoticeIndex].Time <= now) speedNoticeIndex++;
            }
            while (speedNoticeIndex < list.Count && list[speedNoticeIndex].Time <= now)
            {
                SpeedNotice n = list[speedNoticeIndex++];
                if (!playing) continue;
                int L = n.Layer == 1 ? 1 : 2;   // 0(두 층 모두)은 2층 기준
                double from = gm.GetSpeedMultiplier(L, n.Time - 0.0005);
                double to = gm.GetSpeedMultiplier(L, n.Time + 0.0005);
                if (from > 0 && to > 0 && Math.Abs(to - from) > 1e-6) ShowSpeedChange(from, to);
            }
        }
        lastCheckTime = now;

        AnimateSpeedChange(dt);
    }

    // ----- SPEED UP / DOWN 연출 (차분하게) -----
    //  · 글자가 SPEED UP 은 아래에서 위로, SPEED DOWN 은 위에서 아래로 살짝 미끄러지며 나타남
    //  · 글자 아래 가는 밑줄이 가운데에서 양쪽으로 그어짐
    //  · 잠깐 머문 뒤 조용히 사라짐
    private void AnimateSpeedChange(float dt)
    {
        speedChangeTimer += dt;
        float t = speedChangeTimer;
        const float In = 0.22f, Hold = 1.1f, Out = 0.3f;
        float a = t < In ? t / In : t < Hold ? 1f : Mathf.Clamp01(1f - (t - Hold) / Out);
        speedChangeGroup.alpha = a;

        float slide = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / In), 3f);
        float dir = speedUp ? -1f : 1f;   // UP: 아래(-)에서 올라옴 / DOWN: 위(+)에서 내려옴
        speedChangeText.rectTransform.anchoredPosition = new Vector2(0f, dir * 10f * (1f - slide));
        ((RectTransform)speedChangeGroup.transform).anchoredPosition = new Vector2(0f, -SpeedChangeY);

        float lineP = 1f - Mathf.Pow(1f - Mathf.Clamp01((t - 0.08f) / 0.35f), 3f);
        float w = (speedChangeText.preferredWidth + 24f) * lineP;
        speedUnderline.rectTransform.sizeDelta = new Vector2(w, 1.5f);
    }

    private void ShowSpeedChange(double from, double to)
    {
        speedUp = to > from;
        Color c = speedUp ? new Color(1f, 0.74f, 0.38f) : new Color(0.48f, 0.80f, 1f);
        speedChangeText.text = speedUp ? "SPEED UP" : "SPEED DOWN";
        speedChangeText.color = c;
        speedChangeText.characterSpacing = 10f;
        speedUnderline.color = new Color(c.r, c.g, c.b, 0.6f);
        speedChangeTimer = 0f;
    }

    // =====================================================================
    //  RED 노트 예고: 빨간 노트 판정 RedWarnLead 초 전에,
    //   해당 레인 위쪽에서 붉은 빛이 3번 맥박치고 화면 양쪽 가장자리도 붉게 깜빡임
    //   (빨간 노트가 RedSectionGap 초 이상 없다가 다시 나올 때만 → 연속 구간에서 매번 뜨지 않음)
    // =====================================================================
    private void UpdateRedWarning(float dt, float vis)
    {
        var list = gm.RedNoteStarts;
        double now = gm.currentPlayTime;
        if (list != null && list.Count > 0)
        {
            bool jumped = now < lastRedCheckTime - 0.05; // 일시정지 후 되감기
            if (!redInit || jumped)
            {
                redInit = true;
                redIndex = 0;
                while (redIndex < list.Count && list[redIndex].HitTime - RedWarnLead <= now) redIndex++;
                lastRedTime = redIndex > 0 ? list[redIndex - 1].HitTime : -999.0;
            }
            if (gm.currentState == GameState.Playing && !gm.IsFailed)
            {
                // 빨간 노트 구간이 시작되면: 노트가 오는 레인과 관계없이 레인 4개 전체에 불이 켜짐
                while (redIndex < list.Count && list[redIndex].HitTime - RedWarnLead <= now)
                {
                    NoteData n = list[redIndex];
                    if (n.HitTime - lastRedTime >= RedSectionGap) TriggerRedWarning();
                    lastRedTime = n.HitTime;
                    redIndex++;
                }
            }
        }

        lastRedCheckTime = now;

        for (int i = 0; i < 4; i++)
        {
            SpriteRenderer sr = redWarnGlows[i];
            if (sr == null) continue;
            redWarnTime[i] += dt;
            float a = WarnPulse(redWarnTime[i]) * vis;
            sr.enabled = a > 0.001f;
            sr.color = new Color(RedWarnColor.r, RedWarnColor.g, RedWarnColor.b, 0.5f * a);
        }
        redEdgeTime += dt;
        float ea = WarnPulse(redEdgeTime) * gm.EndUiAlpha;
        redEdgeLeft.enabled = redEdgeRight.enabled = ea > 0.001f;
        redEdgeLeft.color = redEdgeRight.color = new Color(RedWarnColor.r, RedWarnColor.g, RedWarnColor.b, 0.32f * ea);
    }

    // 레인 4개 전체 + 화면 양쪽 가장자리
    private void TriggerRedWarning()
    {
        for (int l = 0; l < 4; l++) redWarnTime[l] = 0f;
        redEdgeTime = 0f;
    }

    // 1.2초 동안 3번 맥박치며 점점 약해짐
    private static float WarnPulse(float t)
    {
        if (t >= RedWarnDuration) return 0f;
        float p = t / RedWarnDuration;
        float pulse = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * 3f * p);
        return pulse * Mathf.Sqrt(1f - p);
    }

    // =====================================================================
    //  LIFE 게이지
    // =====================================================================
    private void UpdateLife(float dt)
    {
        if (lifeFill == null) return;
        float target = (float)gm.LifeGauge; // 소수점 두 자리까지만 반영된 값
        shownLife = Mathf.MoveTowards(shownLife, target, dt * 120f);
        lifeFillRt.anchorMax = new Vector2(1f, Mathf.Clamp01(shownLife / 100f));

        Color c = target > 50f ? new Color(0.95f, 0.97f, 1f)
                : target > 25f ? new Color(1f, 0.84f, 0.42f)
                : new Color(1f, 0.38f, 0.45f);
        float a = 1f;
        if (target <= 20f && target > 0f) a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)); // 위험할 때 깜빡임
        lifeFill.color = new Color(c.r, c.g, c.b, a);
        lifeFill.enabled = shownLife > 0.05f;
        UpdateWorldLife(shownLife, c, a);   // SIMPLE-DIVERSE 레인 옆 LIFE 바
    }

    private void SetAtWorld(RectTransform r, Camera worldCam, Vector3 world, Vector2 offset = default(Vector2))
    {
        Vector3 screen = WorldToScreen(world);
        Camera uiCam = hostCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hostCanvas.worldCamera;
        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRoot, screen, uiCam, out local))
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = local - hudRoot.rect.center + offset;
        }
    }

    // =====================================================================
    //  도우미
    // =====================================================================
    private static string FormatTime(double sec)
    {
        int s = Mathf.Max(0, (int)Math.Ceiling(sec - 0.0001));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    // SIMPLE 스킨 전체 글자 크기 배율
    private const float FontScale = 0.9f;

    private static TextMeshProUGUI Text(Transform parent, string text, float size, TextAlignmentOptions align, Color color, FontStyles style, TMP_FontAsset font = null)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt("T", parent, text, size * FontScale, align, color, style);
        if (font != null) t.font = font;
        t.richText = true;
        return t;
    }

    // 정렬 순서를 결정하는 캔버스 찾기: 위로 올라가며 overrideSorting 인 캔버스가 있으면 그것, 없으면 가장 위의 캔버스
    //  (비활성 오브젝트에서도 동작하도록 GetComponent 로 직접 찾는다)
    private static Canvas FindSortingCanvas(Transform t)
    {
        Canvas result = null;
        for (Transform p = t; p != null; p = p.parent)
        {
            Canvas c = p.GetComponent<Canvas>();
            if (c == null) continue;
            result = c;
            if (c.overrideSorting) break;
        }
        return result;
    }

    private static Transform TopChild(Transform t, Transform root)
    {
        while (t != null && t.parent != root) t = t.parent;
        return t;
    }

    private static void TopLeft(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static void TopRight(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 1f);
        r.anchoredPosition = new Vector2(-x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static void BottomRight(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 0f);
        r.anchoredPosition = new Vector2(-x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static void BottomLeft(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }
}