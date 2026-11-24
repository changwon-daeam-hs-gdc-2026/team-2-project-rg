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
    public const int Circle = 3;    // CIRCLE: SIMPLE 디자인 + 원 모양 노트 · 넓은 레인
    public static readonly string[] Names = { "DEFAULT", "SIMPLE", "SIMPLE-DIVERSE", "CIRCLE" };
    // DEFAULT 는 비활성화 — 설정창에서 고를 수 있는 스킨 (번호는 저장된 설정과 맞추기 위해 그대로 둠)
    public static readonly int[] Selectable = { Simple, Diverse, Circle };
    // 저장된 값이 DEFAULT(또는 잘못된 값)이면 SIMPLE 로
    public static int Normalize(int skin) { return Array.IndexOf(Selectable, skin) >= 0 ? skin : Simple; }
    // 설정창 좌우 버튼: 고를 수 있는 스킨 안에서 d 칸 이동
    public static int Step(int skin, int d)
    {
        int i = Array.IndexOf(Selectable, Normalize(skin));
        int n = Selectable.Length;
        return Selectable[((i + d) % n + n) % n];
    }
    public static bool UsesSimpleSkin(int skin) { return skin == Simple || skin == Diverse || skin == Circle; }
    public static bool UsesPerspective(int skin) { return skin == Diverse; }
    public static bool UsesCircle(int skin) { return skin == Circle; }
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
//  [HUD ]  레인 위 콤보·판정·FAST/SLOW / 판정선 아래 키 이름 / LIFE / RED 예고
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

    // SIMPLE (평면) 전용: 살짝 넓은 레인 + 두툼한 노트 (SIMPLE-DIVERSE · CIRCLE 은 그대로)
    public const float SimpleLaneWidth = 0.92f;      // 레인 한 칸 폭 (기본 0.8)
    public const float ThickNoteHeight = 0.26f;      // 일반 노트 두께 (기본 0.16)
    public const float ThickTailHeight = 0.10f;      // 롱노트 끝 두께
    public const float ThickRedNoteHeight = 0.32f;   // RED 노트 두께 (일반보다 조금 더 두껍게)
    public const float ThickRedTailHeight = 0.13f;
    // 두툼한 노트를 쓰는가 (SIMPLE 평면 스킨)
    public bool IsThick { get { return !IsDiverse && !IsCircle; } }
    public static bool ThickActive { get { return Active != null && Active.IsThick; } }
    // 지금 스킨의 노트 두께 (이펙트 · 키 자리 크기 기준)
    public float CurNoteHeight { get { return IsThick ? ThickNoteHeight : NoteHeight; } }

    private static readonly Color OuterLane = new Color(0.95f, 0.97f, 1f, 1f);     // 1, 4번 레인
    private static readonly Color InnerLane = new Color(0.36f, 0.88f, 0.90f, 1f);  // 2, 3번 레인 (#5ce1e6)
    private static readonly Color RedNote = new Color(1f, 0.38f, 0.45f, 1f);
    public static Color RedColor { get { return RedNote; } }
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.55f);

    // CIRCLE: 바깥 레인 흰색 · 안쪽 레인 회색
    private static readonly Color CircleOuter = new Color(0.96f, 0.97f, 1f, 1f);
    private static readonly Color CircleInner = new Color(0.60f, 0.63f, 0.68f, 1f);
    public static Color LaneColor(int lane)
    {
        // (CIRCLE 도 처음 디자인대로 SIMPLE 과 같은 흰색 · 청록 — 흰/회색으로 바꾸려면 CircleOuter / CircleInner 를 쓰면 됨)
        if (GameManager.Instance != null && GameManager.Instance.SixLane) return LaneColor(lane, true);
        return (lane == 0 || lane == 3) ? OuterLane : InnerLane;
    }
    // six: 6키 확장 채보의 레인 번호 (0 ~ 5). 가운데 1 ~ 4번은 4키 ABCD 와 같은 색, 바깥 0 · 5번은 청록
    public static Color LaneColor(int lane, bool six)
    {
        if (!six) return (lane == 0 || lane == 3) ? OuterLane : InnerLane;
        int i = lane - Lanes.FourOffset;
        if (i < 0 || i > 3) return InnerLane;
        return (i == 0 || i == 3) ? OuterLane : InnerLane;
    }
    public static Color NoteColor(NoteData n)
    {
        if (n.IsRedNote) return RedNote;
        if (NoteTypes.IsDrag(n.Type)) return NoteArt.DragColor;              // 드래그 노트 (드래그로 시작하는 롱노트의 머리만 — 몸통 · 끝은 일반 롱노트 색)
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

    // ===== 레인 값 (외부에서 읽음) =====
    public SpriteRenderer LaneBgRenderer { get; private set; }   // 반투명 검은 레인 배경
    public float LaneLeft { get { return laneLeft; } }
    public float LaneRight { get { return laneRight; } }
    public float LaneWidth { get { return laneWidth; } }
    public float LaneVisibility { get { return lastLaneVis; } }   // 등장 · 곡 종료 페이드 (0 ~ 1)
    public float LaneExpandNow { get { return Mathf.Max(0.001f, laneExpandApplied); } }
    public Transform WorldRoot { get { return worldRoot; } }
    public Sprite NoteSprite { get; private set; }
    private Sprite glowSprite;

    private GameManager gm;
    private float laneWidth = 0.8f;
    private float laneLeft, laneRight;

    // 월드 요소
    private Transform worldRoot;
    private readonly List<KeyValuePair<SpriteRenderer, Color>> worldSprites = new List<KeyValuePair<SpriteRenderer, Color>>();
    private SpriteRenderer[] glows = new SpriteRenderer[Lanes.Max];
    private readonly float[] glowLevel = new float[Lanes.Max];
    private readonly bool[] pressed = new bool[Lanes.Max];
    private readonly bool[] held = new bool[Lanes.Max];
    private float worldAlpha = -1f;

    // 히트 이펙트 (노트 하나 칠 때 레인마다 1개)
    //  · core : 노트 모양 섬광 — 흰색에서 노트 색으로 바뀌며 위아래로 부풀었다 사라짐
    //  · ring : 둥근 빛 — 판정선을 중심으로 넓게 퍼짐
    //  · wave : 노트 테두리 모양 충격파 — 노트 자리에서 바깥으로 퍼지며 옅어짐
    //  (위로 솟는 빛기둥은 없앰)
    private class Burst
    {
        public SpriteRenderer core, ring, wave, wave2, flare, star;
        public SpriteRenderer hot, hotCore;                                  // 판정선 위 밝은 광원 (넓은 빛 + 가운데 흰 심)
        public bool perfect;                                                 // PERFECT(EXCELLENT) 판정 → 4갈래 반짝임
        public bool mini;                                                    // 롱노트 홀드 중 맥박 (충격파 · 둥근 빛만 작게)
        public float t = 1f; public int lane; public Color c; public float power = 1f; public float x;
        public float wf = 1f;                                                // 폭 배율 (1/4 칸 노트는 0.25 ~ 0.75)
        // 드래그 노트 전용 (심플 · 고급): 판정선을 따라 양옆으로 뻗는 빛 칼날 + 납작하게 번지는 연노랑 빛무리
        //  + 노트 모양 잔상 2개가 위로 미끄러지며 사라짐 (드래그로 '훑고 지나가는' 느낌)
        //  gem = 잔상 1,  shards[0] = 빛무리,  shards[1] = 잔상 2,  zip = 빛 칼날
        public bool drag;
        public SpriteRenderer gem, zip;
        public readonly SpriteRenderer[] shards = new SpriteRenderer[DragShards];
        public readonly Vector2[] shardVel = new Vector2[DragShards];
        public readonly float[] shardSpin = new float[DragShards];
        // 화려한 빛 (입자 없이 빛만):
        //  halo     : 레인 밖까지 넓게 번지는 노트 색 빛무리
        //  streak   : 판정선을 따라 아주 길고 가늘게 뻗는 흰 빛줄기 (렌즈 플레어 느낌)
        public SpriteRenderer halo, streak;
    }
    private const int DragShards = 3;
    private float burstLW = 0.8f;                                            // 지금 그리는 이펙트의 기준 폭 (레인 폭 × 폭 배율)
    private readonly List<Burst> bursts = new List<Burst>();
    private const float BurstSeconds = 0.40f;

    // 판정선의 키 자리(리셉터): 둥근 테두리, 누르면 레인 색으로 밝아짐
    private readonly SpriteRenderer[] receptors = new SpriteRenderer[Lanes.Max];
    private readonly float[] receptorPop = new float[Lanes.Max];
    private readonly List<SpriteRenderer> laneLines = new List<SpriteRenderer>();   // 레인 구분선 3개
    private string lastJudge = "";                     // 바로 전 판정 (히트 이펙트가 PERFECT 인지 알기 위해)
    private readonly float[] holdPulseTimer = new float[Lanes.Max];


    // 롱노트 홀드 효과: 판정선 위에서 맥박처럼 빛나는 막대 + 위로 올라가는 작은 입자
    private readonly SpriteRenderer[] holdFlares = new SpriteRenderer[Lanes.Max];
    private readonly float[] holdLevel = new float[Lanes.Max];
    // 판정선 노트 모양을 롱노트 폭에 맞춤: 그 레인의 처음 x · 지금 x 차이 · 폭 (놓은 뒤 사라지는 동안에도 마지막 값 유지)
    private readonly float[] holdBaseX = new float[Lanes.Max];
    private readonly bool[] holdBaseSet = new bool[Lanes.Max];
    private readonly float[] holdOffX = new float[Lanes.Max];
    private readonly float[] holdSpan = new float[Lanes.Max];          // 그릴 폭 (월드)
    private readonly float[] holdWidthLanes = new float[Lanes.Max];    // 덮는 폭 (레인 단위, 1 = 한 칸)
    private readonly float[] holdCenterX = new float[Lanes.Max];       // 가운데 x (월드)
    private readonly float[] holdL = new float[Lanes.Max], holdR = new float[Lanes.Max];
    private readonly object[] holdKey = new object[Lanes.Max];         // 같은 롱노트를 여러 레인이 겹쳐 그리지 않게
    private readonly bool[] holdHidden = new bool[Lanes.Max];    // 넓은 롱노트의 첫 칸이 아닌 레인 (첫 칸 레인 하나로 전체 폭을 그림)
    private readonly float[] holdSparkTimer = new float[Lanes.Max];
    private class HoldSpark { public SpriteRenderer sr; public float t = 1f; public float life; public Vector3 start; public float rise; public float size; public int lane; }
    private readonly List<HoldSpark> holdSparks = new List<HoldSpark>();
    private float holdClock;

    // LIFE 게이지 (레인 오른쪽, 둥근 막대)
    private RectTransform lifeRoot, lifeFillRt;
    private Image lifeFill, lifeBg, lifeFlow, lifeGrad;
    private Image lifeCapShade, lifeCapLine;      // 게임 모드 STRICT · STRICT+: 줄어든 LIFE 최대 상한 위쪽을 빨간 사선으로 막음 + 상한선
    private Image lifeFloorShade, lifeFloorLine;  // 게임 모드 STRICT+: 올라온 LIFE 하한 아래쪽을 빨간 사선으로 막음 + 하한선
    private Image lifeShieldGrad;                 // 게임 모드 ONE+: 보호 중 LIFE 바 (연노랑 그라데이션 타이머)
    private bool shieldShown;                     // 지난 프레임에 ONE+ 타이머를 그렸는지
    private float shieldStartLife;                // ONE+ 발동 순간 보이던 LIFE (여기서 100 까지 차오름)
    private const float ShieldRiseSeconds = 0.35f;
    // (게이지 70% 클리어 선은 없앰 — 모든 게이지가 100 → 0 으로 내려가는 HARD 방식)
    private Image lifeOutline, lifeGlowImg, lifeHeadLine;   // SIMPLE · CIRCLE LIFE 막대 꾸밈 (가는 테두리 · 뒤쪽 빛 · 끝선)
    private static readonly Color StrictShade = new Color(1f, 0.30f, 0.38f, 0.85f);   // 빨간 사선 (그림의 진한 줄 · 옅은 바탕에 곱해짐)
    private static readonly Color StrictLine = new Color(1f, 0.42f, 0.50f, 0.95f);

    // 무한 LIFE 그라데이션: 아래 진한 파랑 → 가운데 하늘색 → 위 거의 흰 하늘색 (세로)
    private static Sprite infiniteGradient;
    private static Sprite InfiniteGradient
    {
        get
        {
            if (infiniteGradient != null) return infiniteGradient;
            const int H = 128;
            Texture2D tex = new Texture2D(4, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[4 * H];
            Color bottom = new Color(0.16f, 0.42f, 1.00f), mid = new Color(0.30f, 0.78f, 1.00f), top = new Color(0.82f, 0.97f, 1.00f);
            for (int y = 0; y < H; y++)
            {
                float t = y / (H - 1f);
                Color c = t < 0.55f ? Color.Lerp(bottom, mid, t / 0.55f) : Color.Lerp(mid, top, (t - 0.55f) / 0.45f);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            tex.SetPixels32(px); tex.Apply();
            infiniteGradient = Sprite.Create(tex, new Rect(0, 0, 4, H), new Vector2(0.5f, 0.5f), 4f);
            return infiniteGradient;
        }
    }

    // ONE+ 보호 그라데이션: 아래 진한 금빛 노랑 → 위 거의 흰 연노랑 (세로)
    private static Sprite onePlusGradient;
    private static Sprite OnePlusGradient
    {
        get
        {
            if (onePlusGradient != null) return onePlusGradient;
            const int H = 128;
            Texture2D tex = new Texture2D(4, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[4 * H];
            Color bottom = new Color(1.00f, 0.74f, 0.26f), mid = new Color(1.00f, 0.88f, 0.48f), top = new Color(1.00f, 0.98f, 0.84f);
            for (int y = 0; y < H; y++)
            {
                float t = y / (H - 1f);
                Color c = t < 0.5f ? Color.Lerp(bottom, mid, t / 0.5f) : Color.Lerp(mid, top, (t - 0.5f) / 0.5f);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            tex.SetPixels32(px); tex.Apply();
            onePlusGradient = Sprite.Create(tex, new Rect(0, 0, 4, H), new Vector2(0.5f, 0.5f), 4f);
            return onePlusGradient;
        }
    }

    // ONE+ 보호 중 LIFE 바에 보일 값 (0 ~ 100): 발동 순간 값에서 100 까지 빠르게 차오른 뒤, 보호가 끝날 때 0 이 되도록 일정하게 줄어듦
    //  보호 중이 아니면 -1
    private float ShieldDisplayLife()
    {
        if (gm == null || !gm.ShieldActive || gm.LifeInfinite) return -1f;
        float total = (float)GameModes.OnePlusShieldSeconds;
        float left = (float)gm.ShieldLeft;
        float e = total - left;                                                  // 발동 후 지난 시간
        float drain = 100f * Mathf.Clamp01(left / Mathf.Max(0.01f, total - ShieldRiseSeconds));   // 100 → 0
        float k = Mathf.Clamp01(e / ShieldRiseSeconds);
        k = 1f - Mathf.Pow(1f - k, 3f);
        return Mathf.Lerp(shieldStartLife, drain, k);
    }
    private Sprite uiRoundSprite;
    private Sprite pillSprite;   // LIFE 바: 원 텍스처를 9-slice 로 늘려 양 끝이 반원인 알약 모양
    private Sprite pillShadeSprite, pillRingSprite, pillGlowSprite;   // LIFE 바: 원통 유리 질감 채움 / 가는 테두리 / 부드러운 빛

    // 고급 RED 노트: 진홍 그라데이션 + 위쪽 광택 + 안쪽 밝은 테두리 + 바깥 은은한 빛 (빛 여백만큼 크게 그림)
    public Sprite RedNoteSprite { get; private set; }
    public const float RedGlowPad = 0.05f;   // 빛 여백 (월드 유닛, 위아래·좌우 각각) = 스프라이트 G(10px) / PPU(200)
    // 고급 RED 롱노트 몸통: 양옆이 밝게 빛나고 가운데는 어두운 반투명 진홍 + 가운데 가는 하이라이트
    public Sprite RedBodySprite { get; private set; }

    // 레인 펼침 (게임 시작 시 좌우로 펼쳐짐)
    // kind: 0 = 레인 전체 판(배경 · 판정선 등, 폭이 레인 수를 따라감) / 1 = 양쪽 벽(위치만 따라감, 두께 그대로) / 2 = 레인별(위치 그대로)
    private struct LanePart { public Transform t; public Vector3 scale; public Vector3 pos; public int kind; }

    // ---- 6키 확장 (채보의 LANES): 4키 구간에서는 바깥 레인 0 · 5 번을 숨기고 가운데 4레인만 ----
    //  레인은 처음부터 6개 자리에 만들어 두고, 레인 전체 판은 가운데 기준으로 4/6 ~ 1 배로 줄였다 늘림
    private float sixRatio = 1f;          // 보이는 레인 폭 / 6레인 폭 (6키 채보가 아니면 1)
    private float sixExpand = 1f;         // 0 = 4키 · 1 = 6키
    private int lastKeyMode = -1;         // 키 이름을 다시 쓸지 (4키 ↔ 6키)
    private readonly List<SpriteRenderer> outerDivs = new List<SpriteRenderer>();   // 바깥 레인 구분선 (4키 구간에서 숨김)
    private readonly List<Color> outerDivColors = new List<Color>();
    private float VisLeft { get { return laneCenterX + (laneLeft - laneCenterX) * sixRatio; } }
    private float VisRight { get { return laneCenterX + (laneRight - laneCenterX) * sixRatio; } }
    // 레인 i 가 지금 보이는 정도 (6키 채보의 바깥 레인만 0 ~ 1)
    private float LaneShow(int i)
    {
        if (!gm.SixLane) return 1f;
        return (i == 0 || i == Lanes.Max - 1) ? sixExpand : 1f;
    }
    private readonly List<LanePart> laneParts = new List<LanePart>();
    private float laneCenterX;
    private float laneExpand = 0f, laneExpandApplied = -1f;
    private const float LaneExpandSeconds = 0.6f;

    // RED 노트 예고
    private const double RedWarnLead = 1.5;    // 빨간 노트 판정 시각 몇 초 전에 예고할지
    private const double RedSectionGap = 2.5;  // 이 시간 이상 빨간 노트가 없다가 다시 나올 때만 예고
    private const float RedWarnDuration = 1.2f;
    private static readonly Color RedWarnColor = new Color(1f, 0.22f, 0.32f, 1f);
    private readonly SpriteRenderer[] redWarnGlows = new SpriteRenderer[Lanes.Max];
    private readonly float[] redWarnTime = { 99f, 99f, 99f, 99f, 99f, 99f };
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
    public const float LifeBarWidth = 12f;        // 바 두께 (UI 픽셀)
    private const float LifeFillInset = 2.5f;     // 트랙 안쪽 채움 여백
    private const float LifeGlowPad = 7f;         // 채움 뒤쪽 빛이 번지는 거리
    private static readonly Color LifeTrackColor = new Color(0.02f, 0.03f, 0.05f, 0.62f);
    private static readonly Color LifeOutlineColor = new Color(1f, 1f, 1f, 0.26f);
    private static readonly Color LifeHigh = new Color(0.95f, 0.97f, 1f);
    private static readonly Color LifeMid = new Color(1f, 0.84f, 0.42f);
    private static readonly Color LifeLow = new Color(1f, 0.38f, 0.45f);
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
    private readonly TextMeshProUGUI[] keyTexts = new TextMeshProUGUI[Lanes.Max];
    private float judgeTimer, comboPunch;
    // ONE+ 발동 알림: 콤보 자리에 잠깐 뜨는 ONE+ 아이콘 + 퍼지는 빛 고리 2개 + 뒤쪽 은은한 빛
    private RectTransform onePlusRoot, onePlusBadge;
    private Image onePlusGlow, onePlusRing1, onePlusRing2;
    private CanvasGroup onePlusGroup;
    private float onePlusT = -1f;                 // 0 → OnePlusIconSeconds, -1 = 없음
    private const float OnePlusIconSeconds = 1.8f;
    private float fadeIn;
    private int lastCombo = -1;

    // =====================================================================
    //  생성
    // =====================================================================
    public static SimpleGameSkin Create(GameManager manager, bool diverse = false, bool circle = false)
    {
        GameObject go = new GameObject("SimpleGameSkin");
        SceneManager.MoveGameObjectToScene(go, manager.gameObject.scene); // 게임 씬과 함께 정리되도록
        SimpleGameSkin skin = go.AddComponent<SimpleGameSkin>();
        skin.gm = manager;
        // 원근 셰이더가 없으면 평면 SIMPLE 로 대신 그림
        skin.IsDiverse = diverse && DiversePlayfield.IsSupported();
        skin.IsCircle = circle && !skin.IsDiverse;
        if (skin.IsCircle)
        {
            WidenLanes(manager, CircleLaneWidth);   // 노트 · 판정 · 이펙트가 모두 이 레인 위치를 씀
            manager.hitLineY -= CircleHitLineDrop;  // 판정선을 조금 아래로 (실행 중에만 — 씬에 저장된 값은 그대로)
        }
        else if (!skin.IsDiverse)
        {
            WidenLanes(manager, SimpleLaneWidth);   // SIMPLE: 레인 폭을 살짝 넓힘 (두툼한 노트)
        }
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

    // =====================================================================
    //  CIRCLE (원 모양 노트 · 넓은 레인)
    //   · 레인 한 칸 폭 = CircleLaneWidth (SIMPLE 0.8 → 1.25, 레인 전체 5.0), 레인 위치(gm.laneXPositions)를 가운데 기준으로 넓힘
    //   · 판정선은 실선 없이 원 모양 키 자리만, 높이는 CircleHitLineDrop 만큼 아래로
    //   · 노트 색: 바깥 레인 흰색 · 안쪽 레인 회색
    //   · 1칸 노트 = 원 (지름 CircleSize), 여러 칸(RED · 영역) 노트 = 양 끝이 반원인 알약
    //   · RED 노트 = 진홍 원 + 안쪽 고리 + 진한 붉은 빛 (전용 그림)
    //   · 드래그 노트 = SIMPLE 과 같은 얇은 막대 + 보석 + 꼬리빛 (폭만 원 지름에 맞춤)
    //   · 롱노트: 몸통 폭 = 노트 폭, 무늬 없는 반투명 단색 띠 / 끝 = 같은 크기의 원
    // =====================================================================
    public const float CircleLaneWidth = 1.25f;
    public const float CircleHitLineDrop = 0.35f;     // 판정선을 이만큼 아래로 (월드 유닛)
    private const float CircleNoteRatio = 0.78f;      // 원 지름 = 레인 폭의 78%
    public const float CircleSize = CircleLaneWidth * CircleNoteRatio;
    public const float CircleRelayRatio = 0.6f;       // 슬라이드 중계점 원 = 머리의 60%
    public const float CircleDragRatio = 0.86f;       // 드래그 빛 고리 = 원 지름의 86%
    public const float CircleDragGemScale = 1.45f;    // 드래그 고리 가운데 보석 크기 배율 (SIMPLE 보석 대비)
    public const float CirclePillRatio = 0.56f;       // 여러 칸 노트(알약) 두께 = 원 지름의 56%
    public const float CircleRedRatio = 0.44f;        // RED 노트(알약) 두께 = 원 지름의 44% (1칸 RED 도 납작한 알약)
    public float CircleRedPillDrawHeight { get; private set; }
    public const float CircleBodyAlpha = 0.34f;       // 롱노트 몸통 투명도
    private const int CircleTex = 128, CircleGlowPx = 12;
    // 원 그림은 바깥 빛 여백까지 포함 → 그릴 크기 = 원 지름 × 이 값
    public const float CirclePad = CircleTex / (CircleTex - 2f * CircleGlowPx);

    public bool IsCircle { get; private set; }
    public static bool CircleActive { get { return Active != null && Active.IsCircle; } }
    public Sprite CircleNoteSprite { get; private set; }      // 입체 원 (흰색 → 색 입힘, 바깥 빛 포함)
    public Sprite CircleRedSprite { get; private set; }       // RED 원 (색이 그림에 들어 있음)
    public Sprite CircleRingSprite { get; private set; }      // 원 테두리 (키 자리 · 충격파)
    public Sprite CirclePillSprite { get; private set; }      // 알약 (여러 칸 노트, Sliced 로 가로만 늘어남)
    public Sprite CircleRedPillSprite { get; private set; }   // RED 알약
    public Sprite CircleBodySprite { get; private set; }      // 롱노트 몸통 (무늬 없는 반투명 흰색)
    public float CirclePillDrawHeight { get; private set; }

    // 지금 게임 화면의 레인 한 칸 폭 (스킨이 없으면 기본 0.8)
    public static float GameLaneWidth { get { return Active != null ? Active.laneWidth : 0.8f; } }
    // width 칸 노트의 폭 (기존 규칙: 마지막 칸만 95%)
    public static float NoteSpan(int width) { float lw = GameLaneWidth; return (Mathf.Max(1, width) - 1) * lw + lw * 0.95f; }
    // 1/4 칸 노트의 폭 (subW 칸, 레인 전체 노트처럼 끝에 작은 틈)
    public static float QuarterSpan(int subW) { float lw = GameLaneWidth; return Mathf.Max(lw * 0.04f, lw * Mathf.Clamp(subW, 0, Lanes.Max * Lanes.Quarters) * 0.25f - lw * 0.04f); }   // 폭 0 점 = 아주 가는 노트
    // 노트 하나의 가로 폭 (1/4 칸이면 그 폭, 아니면 Width 칸)
    public static float SpanOf(NoteData n) { return n.IsQuarter ? QuarterSpan(n.SubW) : NoteSpan(n.Width); }
    // 노트 가운데 x (laneX = 레인 가운데 x 들) — 1/4 칸이면 그 칸들의 가운데, 넓은 노트면 덮는 레인들의 가운데
    public static float CenterXOf(NoteData n, float[] laneX)
    {
        int last = laneX.Length - 1;
        int lane = Mathf.Clamp(n.Lane, 0, last);
        if (n.IsQuarter) return laneX[lane] + ((n.Sub + n.SubW * 0.5f) * 0.25f - 0.5f) * GameLaneWidth;
        int end = Mathf.Clamp(n.Lane + Mathf.Max(1, n.Width) - 1, 0, last);
        return (laneX[lane] + laneX[end]) * 0.5f;
    }

    // CIRCLE 노트의 가로 폭: 1칸 = 원 지름, 여러 칸 = 알약 폭
    public static float CircleNoteWidth(int width) { return width <= 1 ? CircleSize : NoteSpan(width); }
    // 롱노트 몸통 폭 = 노트 폭 (CIRCLE 도 머리 원과 같은 폭)
    public static float BodySpan(int width) { return CircleActive ? CircleNoteWidth(width) : NoteSpan(width); }
    public static float BodySpanOf(NoteData n) { return n.IsQuarter ? QuarterSpan(n.SubW) : BodySpan(n.Width); }
    // 노트를 판정선 아래 얼마까지 보여 줄지 (원은 판정선에 중심이 닿을 때 아래 절반이 잘리지 않도록)
    //  CIRCLE: 노트가 판정선을 지나 화면 바닥까지 그대로 내려감 (자르지 않음)
    public float NoteClipExtra { get { return IsCircle ? 30f : 0f; } }

    // 레인 위치를 가운데 기준으로 넓힘 (새 배열 — 씬에 저장된 값은 그대로)
    private static void WidenLanes(GameManager manager, float laneW)
    {
        float[] xs = manager.laneXPositions;
        if (xs == null || xs.Length < 2) return;
        float old = Mathf.Abs(xs[1] - xs[0]);
        if (old < 0.0001f) return;
        float center = (xs[0] + xs[xs.Length - 1]) * 0.5f;
        float k = laneW / old;
        float[] nx = new float[xs.Length];
        for (int i = 0; i < xs.Length; i++) nx[i] = center + (xs[i] - center) * k;
        manager.laneXPositions = nx;
    }

    // 입체 원 · 알약 한 점의 색 (흰색 — 색은 SpriteRenderer.color 로)
    //  sdf: 모양 테두리까지 거리 px (안쪽 음수) / v: 0 아래 ~ 1 위
    //  SIMPLE · SIMPLE-DIVERSE 노트 앞면(NoteArt.NoteFaceSprite)과 같은 규칙 — 모양만 원
    //   · 세로 그라데이션 0.66 → 0.97 + 위쪽(v 0.72) 광택 띠
    //   · 밝은 테두리 2.2px + 바로 안쪽 어두운 선(×0.72) → 입체감
    //   · 바깥 은은한 빛 exp(-d / 3.2) × 0.42
    private static Color32 CircleShade(float sdf, float v)
    {
        float inside = Mathf.Clamp01(0.5f - sdf);
        float bright = Mathf.Lerp(0.66f, 0.97f, Mathf.Clamp01(v));                    // 세로 그라데이션
        float gloss = Mathf.Clamp01(1f - Mathf.Abs(v - 0.72f) / 0.17f);                // 광택 띠 (위아래 부드럽게)
        bright = Mathf.Lerp(bright, 1f, gloss * gloss * (3f - 2f * gloss));
        float edge = -sdf;
        if (edge < 2.2f) bright = 1f;                                                  // 밝은 테두리
        else if (edge < 3.6f) bright *= 0.72f;                                         // 안쪽 어두운 선
        float glowA = sdf > 0f ? Mathf.Exp(-sdf / 3.2f) * 0.42f : 0f;                  // 바깥 빛
        float a = Mathf.Max(inside, glowA);
        byte c = (byte)(Mathf.Clamp01(inside > 0.001f ? bright : 1f) * 255f);
        return new Color32(c, c, c, (byte)(Mathf.Clamp01(a) * 255f));
    }

    // RED 원 · 알약 한 점의 색 (색이 그림에 들어 있음)
    //  · 진홍 → 짙은 와인 세로 그라데이션 + 위쪽 유리 광택
    //  · 밝은 분홍 바깥 테두리 + 안쪽 가는 밝은 고리 (과녁 느낌) + 넓고 진한 붉은 바깥 빛
    //  depth: 테두리에서 안쪽으로 들어온 비율 (0 테두리 ~ 1 가운데)
    private static Color32 CircleShadeRed(float sdf, float v, float depth)
    {
        Color top = new Color(1.00f, 0.32f, 0.42f), bottom = new Color(0.86f, 0.12f, 0.24f);   // 그라데이션 약하게
        Color rim = new Color(1.00f, 0.80f, 0.86f), glow = new Color(1.00f, 0.16f, 0.30f);
        float inside = Mathf.Clamp01(0.5f - sdf);
        Color body = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(v)));
        float gloss = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.95f, v));
        body = Color.Lerp(body, new Color(1f, 0.74f, 0.80f), 0.15f * gloss);
        float rimT = Mathf.Clamp01(1f - Mathf.Abs(sdf + 1.8f) / 1.8f);                // 바깥 밝은 테두리
        float darkT = Mathf.Clamp01(1f - Mathf.Abs(sdf + 5f) / 1.4f);                 // 그 안쪽 어두운 선
        float ringT = Mathf.Clamp01(1f - Mathf.Abs(depth - 0.42f) / 0.05f);           // 안쪽 밝은 고리
        body = Color.Lerp(body, new Color(0.28f, 0f, 0.06f), 0.3f * darkT);
        body = Color.Lerp(body, rim, 0.6f * ringT);
        body = Color.Lerp(body, rim, 0.9f * rimT);
        float glowA = Mathf.Exp(-Mathf.Max(sdf, 0f) / 1.8f) * 0.25f * (1f - inside);   // 바깥 빛 (짧고 옅게)
        float a = inside + glowA;
        Color c = a > 0.0001f ? (body * inside + glow * glowA) / a : glow;
        return new Color32((byte)(Mathf.Clamp01(c.r) * 255), (byte)(Mathf.Clamp01(c.g) * 255), (byte)(Mathf.Clamp01(c.b) * 255), (byte)(Mathf.Clamp01(a) * 255));
    }

    private static Sprite CircleTexture(string name, bool red)
    {
        const int S = CircleTex, G = CircleGlowPx;
        float R = S * 0.5f - G;
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S * 0.5f, S * 0.5f)) - R;
                float v = (y + 0.5f - G) / (2f * R);
                px[y * S + x] = red ? CircleShadeRed(d, v, Mathf.Clamp01(-d / R)) : CircleShade(d, v);
            }
        t.SetPixels32(px); t.Apply();
        // 테두리 0 · FullRect: Sliced 로 그려도 size 대로 그대로 늘어남 (노트 코드가 size 로 크기를 정함)
        Sprite s = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S, 0, SpriteMeshType.FullRect, Vector4.zero);
        s.name = name;
        return s;
    }

    // 알약 (여러 칸 노트): 높이 S, 폭 2S, 양 끝 반원 — 좌우 S/2 는 고정, 가운데만 가로로 늘어남
    private Sprite PillTexture(string name, bool red, float drawHeight)
    {
        const int S = CircleTex, G = CircleGlowPx, PW = S * 2, PH = S;
        float R = S * 0.5f - G;
        Texture2D pt = new Texture2D(PW, PH, TextureFormat.RGBA32, false);
        pt.wrapMode = TextureWrapMode.Clamp;
        Color32[] pp = new Color32[PW * PH];
        for (int y = 0; y < PH; y++)
            for (int x = 0; x < PW; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, PH * 0.5f, PW - PH * 0.5f);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, PH * 0.5f)) - R;
                float v = (y + 0.5f - G) / (2f * R);
                pp[y * PW + x] = red ? CircleShadeRed(d, v, Mathf.Clamp01(-d / R)) : CircleShade(d, v);
            }
        pt.SetPixels32(pp); pt.Apply();
        Sprite s = Sprite.Create(pt, new Rect(0, 0, PW, PH), new Vector2(0.5f, 0.5f), PH / drawHeight, 0,
                                 SpriteMeshType.FullRect, new Vector4(PH / 2, 0, PH / 2, 0));
        s.name = name;
        return s;
    }

    private void MakeCircleSprites()
    {
        const int S = CircleTex;
        CircleNoteSprite = CircleTexture("CircleNote", false);
        CircleRedSprite = CircleTexture("CircleRed", true);

        // 원 테두리 (바깥 가장자리에 붙은 선 + 안쪽 아주 옅게)
        Texture2D rt = new Texture2D(S, S, TextureFormat.RGBA32, false);
        rt.wrapMode = TextureWrapMode.Clamp;
        Color32[] rp = new Color32[S * S];
        float RR = S * 0.5f - 1.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S * 0.5f, S * 0.5f)) - RR;
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d + 2.5f) / 3f);                // 두께 약 5px
                float fill = d < 0f ? 0.08f : 0f;
                rp[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(ring, fill)) * 255f));
            }
        rt.SetPixels32(rp); rt.Apply();
        CircleRingSprite = Sprite.Create(rt, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S, 0, SpriteMeshType.FullRect, Vector4.zero);
        CircleRingSprite.name = "CircleRing";

        CirclePillDrawHeight = CircleSize * CirclePillRatio * CirclePad;
        CircleRedPillDrawHeight = CircleSize * CircleRedRatio * CirclePad;
        CirclePillSprite = PillTexture("CirclePill", false, CirclePillDrawHeight);
        CircleRedPillSprite = PillTexture("CircleRedPill", true, CircleRedPillDrawHeight);

        // 롱노트 몸통: 무늬 없는 반투명 흰색 (색 · 길이는 노트 코드가 정함)
        Texture2D bt = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        bt.wrapMode = TextureWrapMode.Clamp;
        Color32[] bp = new Color32[16];
        for (int i = 0; i < 16; i++) bp[i] = new Color32(255, 255, 255, (byte)(CircleBodyAlpha * 255f));
        bt.SetPixels32(bp); bt.Apply();
        CircleBodySprite = Sprite.Create(bt, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect, Vector4.zero);
        CircleBodySprite.name = "CircleBody";

        CircleDragSprite = MakeCircleDragSprite();
        CircleBeamSprite = MakeCircleBeamSprite();
    }

    // 롱노트 끝 마감: 아래 변이 평평하고 위가 둥근 판 (1칸 = 정확한 반원)
    //  · 몸통 그림(NoteArt.LongBodySprite / RED 는 RedBodySprite)과 같은 단면을 둥근 가장자리를 따라 그림
    //    → 몸통 양옆의 빛나는 선이 끝에서 둥글게 이어져 매끄럽게 닫힘
    //  · 몸통과 같은 픽셀 밀도(160 px/유닛)로 폭 · 높이에 딱 맞게 만들어 둠 (늘이지 않음, 폭 · RED 별로 한 번만)
    //  · 피벗 = 아래 변 가운데 (롱노트 끝 판정 위치에서 위로 둥글게)
    private const float CapPPU = 160f;                // 몸통 스프라이트와 같은 밀도 (선 두께가 같아 보이게)
    private readonly Dictionary<int, Sprite> capCache = new Dictionary<int, Sprite>();

    // width 칸 롱노트 끝 (red: RED 몸통 무늬). 크기 = (CircleNoteWidth, CapHeight)
    public Sprite CapSprite(int width, bool red)
    {
        width = Mathf.Clamp(width, 1, 4);
        int key = width * 2 + (red ? 1 : 0);
        Sprite s;
        if (capCache.TryGetValue(key, out s) && s != null) return s;
        s = MakeCapSprite(CircleNoteWidth(width), CapHeight(width, red), red);
        capCache[key] = s;
        return s;
    }

    // 롱노트 끝 둥근 부분 높이: 1칸 = 원 반지름(반원) / 여러 칸 = 알약 두께의 절반 / RED = RED 알약 두께의 절반
    public static float CapHeight(int width, bool red)
    {
        if (red) return CircleSize * CircleRedRatio * 0.5f;
        return width > 1 ? CircleSize * CirclePillRatio * 0.5f : CircleSize * 0.5f;
    }

    private static Sprite MakeCapSprite(float w, float capH, bool red)
    {
        const float k = CapPPU;
        int W = Mathf.Max(4, Mathf.CeilToInt(w * k)), H = Mathf.Max(4, Mathf.CeilToInt(capH * k));
        float bodyW = W;                                  // 몸통 폭 (px)
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        float R = H;                                      // 둥근 모서리 반지름 (px) = 높이
        Color edgeC = new Color(1.00f, 0.45f, 0.55f), baseC = new Color(0.36f, 0.02f, 0.09f), stripeC = new Color(0.88f, 0.12f, 0.27f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, R, W - R);
                float fe = R - Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, 0f));   // 둥근 가장자리에서 안쪽으로 몇 px
                float inside = Mathf.Clamp01(fe + 0.5f);
                if (inside <= 0f) { px[y * W + x] = new Color32(255, 255, 255, 0); continue; }
                Color c; float a;
                if (!red)
                {
                    // NoteArt.LongBodySprite 단면: [밝은 바깥선][틈][가는 안쪽선][안으로 번지는 빛] + 가운데 빛 기둥
                    if (fe < 2.2f) a = 1f;
                    else if (fe < 3.6f) a = 0.30f;
                    else if (fe < 5.0f) a = 0.75f;
                    else a = 0.14f + 0.34f * Mathf.Exp(-(fe - 5f) / 4f);
                    // 가운데 빛 기둥: 몸통(Sliced, 테두리 14px · 가운데 36px 가 늘어남)의 같은 가로 위치 값
                    float u = fx < 14f ? fx : (fx > bodyW - 14f ? 64f - (bodyW - fx) : 14f + (fx - 14f) / Mathf.Max(1f, bodyW - 28f) * 36f);
                    float center = Mathf.Abs(u - 32f) / 7f;
                    a = Mathf.Max(a, 0.14f + 0.30f * Mathf.Exp(-center * center));
                    c = Color.white;
                }
                else
                {
                    // RedBodySprite 단면: 바깥 밝은 진홍 선 + 안으로 번지는 빛, 안쪽은 어두운 반투명 와인색
                    if (fe < 10f)
                    {
                        float line = Mathf.Clamp01(1f - Mathf.Abs(fe - 2.2f) / 1.6f);
                        float bleed = Mathf.Exp(-(fe - 2.2f) / 3.0f);
                        c = Color.Lerp(baseC, edgeC, Mathf.Clamp01(Mathf.Max(line, bleed * 0.75f)));
                        a = Mathf.Lerp(0.42f, 0.95f, Mathf.Max(line, bleed * 0.6f));
                        if (fe < 1f) a *= 0.7f;
                    }
                    else { c = Color.Lerp(baseC, stripeC, 0.3f); a = 0.45f; }
                }
                a *= inside;
                px[y * W + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255), (byte)(Mathf.Clamp01(c.g) * 255), (byte)(Mathf.Clamp01(c.b) * 255), (byte)(Mathf.Clamp01(a) * 255f));
            }
        t.SetPixels32(px); t.Apply();
        Sprite s = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), k, 0, SpriteMeshType.FullRect, Vector4.zero);
        s.name = red ? "CircleRedCap" : "CircleCap";
        return s;
    }

    // CIRCLE 드래그 노트: 굵은 빛 고리 (가운데는 비어 보이게 아주 옅게) — 색은 SpriteRenderer.color (연노랑)
    //  · 고리 바깥 · 안쪽 가장자리가 밝고 고리 가운데는 조금 어두움 → 입체적인 링
    //  · 고리 안쪽 공간에 은은한 빛 (가운데 보석이 떠 보이게) + 바깥 빛
    public Sprite CircleDragSprite { get; private set; }
    private static Sprite MakeCircleDragSprite()
    {
        const int S = CircleTex, G = CircleGlowPx;
        float R = S * 0.5f - G;           // 바깥 반지름
        float T = R * 0.26f;              // 고리 두께
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S * 0.5f, S * 0.5f));
                float outer = r - R;                       // 바깥 테두리까지 (안쪽 음수)
                float inner = (R - T) - r;                 // 안쪽 테두리까지 (고리 안쪽 공간은 양수)
                float ring = Mathf.Clamp01(0.5f - Mathf.Max(outer, inner));   // 고리 안 = 1
                float edgeD = Mathf.Min(-outer, -inner);   // 고리 안에서 가장자리까지 px
                float bright = edgeD < 2.6f ? 1f : Mathf.Lerp(0.9f, 0.78f, Mathf.Clamp01((edgeD - 2.6f) / (T * 0.5f)));
                float glowOut = outer > 0f ? Mathf.Exp(-outer / 4.5f) * 0.5f : 0f;
                float glowIn = inner > 0f ? 0.16f + 0.22f * Mathf.Exp(-inner / 5f) : 0f;   // 안쪽 공간 은은한 빛
                float a = Mathf.Max(ring, Mathf.Max(glowOut, glowIn));
                byte c = (byte)(Mathf.Clamp01(ring > 0.001f ? bright : 1f) * 255f);
                px[y * S + x] = new Color32(c, c, c, (byte)(Mathf.Clamp01(a) * 255f));
            }
        t.SetPixels32(px); t.Apply();
        Sprite s = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S, 0, SpriteMeshType.FullRect, Vector4.zero);
        s.name = "CircleDrag";
        return s;
    }

    // CIRCLE 키 빔: 아래 끝이 원 모양(키 자리 원을 감쌈)이고 위로 갈수록 사라지는 빛기둥, 좌우 가장자리는 부드럽게
    //  피벗 = 아래 원의 중심 (판정선 높이에 두면 키 자리 원과 겹침). 크기 1 x 4 유닛 (PPU 64)
    public Sprite CircleBeamSprite { get; private set; }
    private static Sprite MakeCircleBeamSprite()
    {
        const int W = 64, H = 256, C = 32;   // C: 아래 원 반지름 (= 폭의 절반)
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dist = fy < C ? Vector2.Distance(new Vector2(fx, fy), new Vector2(C, C)) / C   // 아래: 원
                                    : Mathf.Abs(fx - C) / C;                                          // 위: 기둥
                float edge = Mathf.Clamp01((1f - dist) / 0.35f);
                float prof = edge * edge * (3f - 2f * edge) * 0.6f + Mathf.Pow(Mathf.Clamp01(1f - dist), 2f) * 0.4f;   // 가운데가 밝음
                float up = fy < C ? 1f : Mathf.Pow(1f - (fy - C) / (H - C), 1.7f);                    // 위로 사라짐
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(prof * up) * 255f));
            }
        t.SetPixels32(px); t.Apply();
        Sprite s = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, (float)C / H), 64f, 0, SpriteMeshType.FullRect);
        s.name = "CircleBeam";
        return s;
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
        RedNoteSprite = CreateRedNoteSprite(IsThick ? ThickRedNoteHeight : RedNoteHeight);
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
        pillShadeSprite = MakePillSprite(C, (d, u) =>
        {
            // 원통 유리: 왼쪽 1/3 쯤이 가장 밝은 세로 광택, 가장자리로 갈수록 살짝 어두움 + 바깥 밝은 테두리
            float a = Mathf.Clamp01(-d + 0.5f);
            float sheen = Mathf.Exp(-Mathf.Pow((u - 0.34f) / 0.13f, 2f));
            float b = Mathf.Lerp(0.80f, 0.93f, Mathf.Clamp01(1f - Mathf.Abs(u - 0.45f) * 1.4f));
            b = Mathf.Lerp(b, 1f, sheen * 0.85f);
            if (-d < 1.6f) b = Mathf.Max(b, 0.97f);
            return new Color(b, b, b, a);
        });
        pillRingSprite = MakePillSprite(C, (d, u) =>
        {
            float ring = Mathf.Clamp01(1.3f - Mathf.Abs(d + 1.6f));   // 바깥 가장자리 안쪽 약 2px 선
            return new Color(1f, 1f, 1f, ring);
        });
        pillGlowSprite = MakePillSprite(C, (d, u) =>
        {
            // 가운데(채움 자리)는 진하고 바깥쪽(반지름 32px 중 바깥 14px)으로 부드럽게 사라지는 빛
            float g = 1f - Mathf.Clamp01((d + 14f) / 14f);
            return new Color(1f, 1f, 1f, g * g * (3f - 2f * g));
        });

        // 6) CIRCLE: 원 노트 · 원 테두리 · 알약
        if (IsCircle) MakeCircleSprites();
    }

    // 알약용 원 텍스처 (지름 C, 테두리 C/2 → 어떤 길이로 늘려도 양 끝이 반원)
    //  shade(d, u): d = 원 테두리까지 거리 px (안쪽 음수), u = 가로 위치 0 ~ 1
    private static Sprite MakePillSprite(int C, Func<float, float, Color> shade)
    {
        Texture2D t = new Texture2D(C, C, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] p = new Color32[C * C];
        for (int y = 0; y < C; y++)
            for (int x = 0; x < C; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(C * 0.5f, C * 0.5f)) - C * 0.5f;
                p[y * C + x] = shade(d, (x + 0.5f) / C);
            }
        t.SetPixels32(p); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(C / 2, C / 2, C / 2, C / 2));
    }

    // CIRCLE 이면 원 그림, 아니면 원래 그림
    private Sprite ShapeSprite(Sprite normal) { return IsCircle ? CircleNoteSprite : normal; }
    private Sprite OutlineSprite(Sprite normal) { return IsCircle ? CircleRingSprite : normal; }

    // =====================================================================
    //  RED 노트 (4차 디자인) — 무늬 없이 '빛나는 붉은 유리판'
    //   · 진홍 → 짙은 와인 세로 그라데이션 몸체
    //   · 위쪽 절반에 부드러운 유리 광택 (위로 갈수록 밝게)
    //   · 바깥 밝은 분홍빛 테두리 + 바로 안쪽 어두운 선 → 입체감
    //   · 넓고 진한 붉은 바깥 빛 (일반 노트와는 색 · 빛으로만 구분)
    //   · 9-slice: 빛 여백·테두리는 늘어나지 않고 가운데만 늘어남 (가로로 무늬가 없어 어떤 폭이든 같은 모양)
    // =====================================================================
    //  noteHeight: 노트 두께 (월드 유닛) — 그림 높이를 여기에 맞춰 세로로 늘어나지 않게 함 (SIMPLE 두툼한 노트는 더 두껍게)
    public static Sprite CreateRedNoteSprite(float noteHeight = RedNoteHeight)
    {
        const int W = 160, G = 10, R = 10;   // G: 빛 여백(px) = RedGlowPad * PPU
        const float PPU = 200f;
        int H = Mathf.RoundToInt(noteHeight * PPU) + 2 * G;   // 기본 0.24 → 68px
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
        // 레인이 판정선 아래로 내려가는 길이 (CIRCLE: 화면 바닥까지 넉넉히 — 노트도 바닥까지 내려감)
        float under = IsCircle ? 7f : 3f;
        float laneMidY = y + (30f - under) * 0.5f, laneSpanH = 30f + under;

        // 배경 (판정선 아래 under, 위로 30 유닛). 불투명도는 설정 > GAMEPLAY > LANE OPACITY
        float opacity = Mathf.Clamp01(SettingsStore.Current.LaneOpacity);
        Color bgc = IsDiverse ? new Color(0.03f, 0.04f, 0.075f, opacity) : new Color(0.035f, 0.045f, 0.07f, opacity);
        SpriteRenderer laneBg = AddRect(worldRoot, "LaneBg", centerX, laneMidY, fullW + 0.06f, laneSpanH, bgc, -20);
        LaneBgRenderer = laneBg;

        // MV 레인 블러: 레인 어두운 판 바로 아래에 '흐리게 만든 MV' 를 깐다 (MV 가 나올 때만 보임)
        if (gm.simpleLaneBlur)
        {
            int blurOrder = -21;
            if (gm.bgRenderer != null && gm.bgRenderer.sortingLayerID == laneBg.sortingLayerID && gm.bgRenderer.sortingOrder >= blurOrder)
                blurOrder = gm.bgRenderer.sortingOrder; // 배경과 같은 순서면 배경보다 카메라에 가까운 블러가 위에 그려짐
            laneBlur = SimpleLaneBlur.Create(worldRoot, gm, laneLeft - 0.03f, laneRight + 0.03f, y - under, y + 30f,
                                             laneBg.sortingLayerID, blurOrder, gm.simpleLaneBlurStrength);
            if (laneBlur != null) laneBlur.Diverse = IsDiverse;
        }

        // 판정선 아래쪽을 조금 더 어둡게 (레인 불투명도에 비례)
        AddRect(worldRoot, "UnderLine", centerX, y - under * 0.5f, fullW + 0.06f, under, new Color(0f, 0f, 0f, 0.4f * opacity), -19);

        // 양쪽 벽 (흰 선). 레인 사이 구분선은 없음.
        //  LANE OPACITY 와 관계없이 항상 같은 진하기 (등장·곡 종료 페이드만 따라감)
        float edgeW = IsDiverse ? 0.04f : EdgeWidth;
        AddSuckLine(AddRect(worldRoot, "EdgeL", laneLeft, laneMidY, edgeW, laneSpanH, EdgeColor, -19));
        AddSuckLine(AddRect(worldRoot, "EdgeR", laneRight, laneMidY, edgeW, laneSpanH, EdgeColor, -19));
        if (IsDiverse)
        {
            // 벽 안쪽으로 은은하게 번지는 빛 (빛나는 레일 느낌, 폭 좁게)
            AddSuckLine(AddSprite(worldRoot, "EdgeGlowL", hGradientSprite, laneLeft, y + 13.5f, 0.22f, 33f, EdgeGlowC, -19, false));
            AddSuckLine(AddSprite(worldRoot, "EdgeGlowR", hGradientSprite, laneRight, y + 13.5f, 0.22f, 33f, EdgeGlowC, -19, true));

            // 레일 바깥쪽 가는 보조선 (이중 레일)
            AddSuckLine(AddRect(worldRoot, "RailL", laneLeft - 0.08f, y + 13.5f, 0.012f, 33f, new Color(1f, 1f, 1f, 0.28f), -19));
            AddSuckLine(AddRect(worldRoot, "RailR", laneRight + 0.08f, y + 13.5f, 0.012f, 33f, new Color(1f, 1f, 1f, 0.28f), -19));

            // 판정선 근처 바닥이 은은하게 밝음 (위로 갈수록 사라짐)
            floorGlow = AddSprite(worldRoot, "FloorGlow", glowSprite, centerX, y, fullW, 2.2f, FloorGlowC, -19, false);

        }

        // 게임 모드 알림 빛 (평소엔 안 보임)
        //  · MERCY · LAZY 가 발동하면 판정선 근처 바닥 빛이 초록으로 번쩍 (FlashFloor)
        //  · ONE+ 부활: 바닥 빛이 연한 노랑으로 (보호 시간 동안) + 양쪽 레인 테두리를 노란 빛이 아래 → 위로 훑고 지나감
        accentGlow = AddSprite(worldRoot, "AccentGlow", glowSprite, centerX, y, fullW, 2.6f, Color.clear, -18, false);
        sweepL = AddSprite(worldRoot, "SweepL", vBellSprite, laneLeft, y, 0.20f, 3.2f, Color.clear, -14, false);
        sweepR = AddSprite(worldRoot, "SweepR", vBellSprite, laneRight, y, 0.20f, 3.2f, Color.clear, -14, false);

        // 레인 구분선: 판정선에서 위로 갈수록 사라지는 아주 옅은 선 (레인이 또렷하게, 화면은 깔끔하게)
        //  설정 > GAMEPLAY > LANE LINES 로 켜고 끔 (게임 중 설정을 바꿔도 바로 반영)
        //  CIRCLE: 화면 바닥부터 맨 위까지 같은 진하기의 가는 실선
        for (int i = 1; i < xs.Length; i++)
        {
            SpriteRenderer div = IsCircle
                ? AddRect(worldRoot, "LaneDiv" + i, laneLeft + i * laneWidth, laneMidY, 0.012f, laneSpanH, new Color(1f, 1f, 1f, 0.10f), -19)
                : AddSprite(worldRoot, "LaneDiv" + i, glowSprite, laneLeft + i * laneWidth, y, IsDiverse ? 0.018f : 0.012f, IsDiverse ? 10f : 7f,
                      new Color(1f, 1f, 1f, IsDiverse ? 0.11f : 0.09f), -19, false);
            laneLines.Add(div);
            AddSuckLine(div);
            if (gm.SixLane && (i == 1 || i == xs.Length - 1)) { outerDivs.Add(div); outerDivColors.Add(div.color); }
        }

        // 키 누름 빛
        for (int i = 0; i < xs.Length; i++)
        {
            GameObject go = new GameObject("Glow" + i);
            go.transform.SetParent(worldRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -18;
            Color c = LaneColor(i);
            sr.color = new Color(c.r, c.g, c.b, 0f);
            go.transform.position = new Vector3(xs[i], y, 0f);
            if (IsCircle)
            {
                // CIRCLE: 키 자리 원을 감싸는 둥근 아래 끝 + 원 폭의 빛기둥 (스프라이트 1 x 4 유닛)
                float bw = CircleSize * 1.12f;
                sr.sprite = CircleBeamSprite;
                go.transform.localScale = new Vector3(bw / CircleBeamSprite.bounds.size.x, 3.4f / (CircleBeamSprite.bounds.size.y - 0.5f), 1f);
            }
            else
            {
                sr.sprite = glowSprite;
                go.transform.localScale = new Vector3(laneWidth / glowSprite.bounds.size.x, IsDiverse ? 3f : 3.2f, 1f);
            }
            glows[i] = sr;
        }

        // 판정선의 키 자리(리셉터)
        for (int i = 0; i < xs.Length; i++)
        {
            GameObject go = new GameObject("Receptor" + i);
            go.transform.SetParent(worldRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = OutlineSprite(NoteArt.NoteOutlineSprite);
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = ReceptorSize(1f);
            sr.sortingOrder = -16;
            sr.color = new Color(1f, 1f, 1f, 0f);
            go.transform.position = new Vector3(xs[i], y, 0f);
            go.transform.localScale = new Vector3(1f, NoteScaleY, 1f);
            receptors[i] = sr;
        }

        // 판정선 (노트 아래에 그려짐) — CIRCLE 은 실선 없이 원 모양 키 자리가 판정선 역할
        if (!IsCircle)
            AddRect(worldRoot, "JudgeLine", centerX, y, fullW, IsDiverse ? 0.06f : 0.035f, new Color(1f, 1f, 1f, 0.95f), 0);
        if (IsCircle) { }
        else if (IsDiverse)
            // 판정선 빛: 판정선이 가장 밝고 위아래(바깥쪽)로 갈수록 옅어짐
            AddSprite(worldRoot, "JudgeGlow", vBellSprite, centerX, y, fullW, 0.42f, new Color(1f, 1f, 1f, 0.20f), -17, false);
        else
            AddRect(worldRoot, "JudgeGlow", centerX, y, fullW, 0.16f, new Color(1f, 1f, 1f, 0.08f), -17);

        // 롱노트 홀드: 판정선 위 빛나는 막대 + 입자
        for (int i = 0; i < xs.Length; i++)
        {
            GameObject go = new GameObject("HoldFlare" + i);
            go.transform.SetParent(worldRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ShapeSprite(NoteArt.NoteFaceSprite);       // 입체 노트 앞면 + 바깥 빛 (CIRCLE: 원)
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(laneWidth * 0.98f + NoteArt.NoteGlowPad * 2f, 0.22f + NoteArt.NoteGlowPad * 2f);
            sr.sortingOrder = 8 + NoteLayer.EffectLift;   // 모든 층 노트 위
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
            sr.sortingOrder = 9 + NoteLayer.EffectLift;
            sr.enabled = false;
            holdSparks.Add(new HoldSpark { sr = sr, t = 1f, life = 1f });  // 비어 있는 입자
        }

        // 히트 이펙트 풀
        for (int i = 0; i < 32; i++)
        {
            Burst b = new Burst();
            // (순서 6 이상은 EffectRenderer 가 NoteLayer.EffectLift 만큼 올림 → 5층 노트보다 위)
            b.ring = EffectRenderer("HitRing" + i, NoteArt.GlowSprite, 6);
            b.core = EffectRenderer("HitCore" + i, ShapeSprite(NoteSprite), 7);
            b.core.drawMode = SpriteDrawMode.Sliced;
            b.wave = EffectRenderer("HitWave" + i, OutlineSprite(NoteArt.NoteOutlineSprite), 8);
            b.wave.drawMode = SpriteDrawMode.Sliced;
            b.wave2 = EffectRenderer("HitWave2_" + i, OutlineSprite(NoteArt.NoteOutlineSprite), 8);
            b.wave2.drawMode = SpriteDrawMode.Sliced;
            b.flare = EffectRenderer("HitFlare" + i, NoteArt.HBellSprite, 10);
            b.star = EffectRenderer("HitStar" + i, NoteArt.StarSprite, 11);
            b.hot = EffectRenderer("HitHot" + i, NoteArt.GlowSprite, 10);
            b.hotCore = EffectRenderer("HitHotCore" + i, NoteArt.HBellSprite, 12);
            b.gem = EffectRenderer("HitDragGhost" + i, IsCircle ? CircleDragSprite : NoteArt.NoteFaceSprite, 13);   // 드래그 잔상 (CIRCLE: 빛 고리)
            b.gem.drawMode = SpriteDrawMode.Sliced;
            b.zip = EffectRenderer("HitDragZip" + i, NoteArt.HBellSprite, 12);
            b.shards[0] = EffectRenderer("HitDragHalo" + i, NoteArt.GlowSprite, 11);
            b.shards[1] = EffectRenderer("HitDragGhostB" + i, IsCircle ? CircleDragSprite : NoteArt.NoteFaceSprite, 12);
            b.shards[1].drawMode = SpriteDrawMode.Sliced;
            b.shards[2] = EffectRenderer("HitDragUnused" + i, NoteArt.GlowSprite, 11);
            b.halo = EffectRenderer("HitHalo" + i, NoteArt.GlowSprite, 6);
            b.streak = EffectRenderer("HitStreak" + i, NoteArt.HBellSprite, 12);
            bursts.Add(b);
        }

        // RED 노트 예고: 화면 위쪽에서 아래로 번지는 붉은 빛 (레인별)
        Camera cam = gm.SceneCamera;
        float topY = (cam != null && cam.orthographic) ? cam.transform.position.y + cam.orthographicSize : y + 8f;
        if (IsDiverse) topY -= 0.3f; // 먼 쪽 끝 페이드 구간 바로 아래부터
        for (int i = 0; i < xs.Length; i++)
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
        {
            string nm = kv.Key.name;
            int kind = nm.StartsWith("LaneDiv") ? 2
                     : (nm.StartsWith("Edge") || nm.StartsWith("Rail") || nm.StartsWith("Sweep")) ? 1 : 0;
            laneParts.Add(new LanePart { t = kv.Key.transform, scale = kv.Key.transform.localScale, pos = kv.Key.transform.position, kind = kind });
        }
        foreach (var r in receptors)
            if (r != null) laneParts.Add(new LanePart { t = r.transform, scale = r.transform.localScale, pos = r.transform.position, kind = 2 });
        ApplyLaneExpand(0f);

        SetWorldAlpha(0f);

        // SIMPLE-DIVERSE: 레인 전체를 원근 레이어로 옮기고 원근 카메라 생성
        if (IsDiverse) BuildWorldLife(y);

        if (IsDiverse)
        {
            // 6키 확장 채보: 원근 카메라는 가운데 4레인 폭에 맞춤 (4키 구간은 보통 4키 채보와 같은 크기, 6키로 펼치면 바깥으로 넓어지기만)
            float pfHalf = (laneRight - laneLeft) * 0.5f * (gm.SixLane ? 4f / Lanes.Max : 1f);
            float pfCenter = (laneLeft + laneRight) * 0.5f;
            playfield = DiversePlayfield.Create(transform, gm, pfCenter - pfHalf, pfCenter + pfHalf);
            if (playfield == null) IsDiverse = false;
            else
            {
                playfield.ApplyAll(worldRoot, false);
                PrewarmNoteMaterials();
                if (laneBlur != null) playfield.Register(laneBlur.ShowMaterial);
                // LIFE 바는 레인 접기(FOLD)의 영향을 받지 않음
                foreach (var sr in new[] { wLifeTrack, wLifeFrameL, wLifeFrameR, wLifeFill, wLifeGlow, wLifeHead, wLifeFoot, wLifeTop, wLifeFlow, wLifeCapShade, wLifeCapLine, wLifeFloorShade, wLifeFloorLine })
                    if (sr != null) playfield.Apply(sr, false, false);
                // 레인 사라짐/드러남: 양쪽 실선 · 구분선은 투명해지지 않고 '위로 빨려 올라감 / 아래에서 올라옴' 으로 움직임 (UpdateLaneHide)
                foreach (var ln in suckLines) if (ln.sr != null) playfield.Apply(ln.sr, false, false);
                // 레인 밑에 깔린 남색 덮개(레인 배경)는 레인이 사라진 상태에서도 그대로 남음
                if (LaneBgRenderer != null) playfield.Apply(LaneBgRenderer, false, false);
            }
        }

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
    private SpriteRenderer wLifeTrack, wLifeFrameL, wLifeFrameR, wLifeFill, wLifeGlow, wLifeHead, wLifeFoot, wLifeFlow;
    private SpriteRenderer wLifeCapShade, wLifeCapLine;   // STRICT · STRICT+ 상한
    private SpriteRenderer wLifeFloorShade, wLifeFloorLine;   // STRICT+ 하한
    private SpriteRenderer wLifeTop;                      // 위쪽 끝 표시 (고정 — 채워 나가는 게이지도 막대 끝이 보이게)

    // ----- 게임 모드 알림 빛 -----
    public static readonly Color SaveFlashColor = new Color(0.45f, 1f, 0.55f, 1f);     // MERCY · LAZY 발동 (초록)
    public static readonly Color OnePlusColor = new Color(1f, 0.90f, 0.50f, 1f);       // ONE+ 부활 · 보호 (연한 노랑)
    private static readonly Color FloorGlowColor = new Color(0.55f, 0.80f, 1f, 0.07f);
    private Color FloorGlowC { get { return FloorGlowColor; } }
    private static readonly Color EdgeGlowC = new Color(0.75f, 0.9f, 1f, 0.08f);
    private SpriteRenderer floorGlow, accentGlow, sweepL, sweepR;
    private Color flashColor;
    private float flashLevel;                 // 1 → 0 (MERCY · LAZY 번쩍)
    private float sweepT = -1f;               // 0 → 1 (ONE+ 테두리 훑기), -1 = 없음
    private float shieldTint;                 // 0 ~ 1 (ONE+ 보호 중 노란 바닥 빛)

    // MERCY · LAZY 가 발동했을 때: 바닥 빛이 그 색으로 번쩍였다가 사라짐
    public void FlashFloor(Color c) { flashColor = c; flashLevel = 1f; }
    // ONE+ 부활: 양쪽 테두리 훑기 + 콤보 자리에 ONE+ 아이콘 (바닥 노란 빛 · LIFE 바 노란 타이머는 gm.ShieldLeft 를 따라감)
    public void OnOnePlusRevive()
    {
        sweepT = 0f; flashColor = OnePlusColor; flashLevel = 1f;
        onePlusT = 0f;
        shieldStartLife = shownLife;
    }

    private void UpdateAccents(float dt, float vis)
    {
        if (accentGlow == null) return;
        flashLevel = Mathf.MoveTowards(flashLevel, 0f, dt / 0.7f);
        float shield = (float)gm.ShieldLeft;
        float targetTint = shield > 0f ? Mathf.Clamp01(shield / 0.5f) : 0f;          // 끝나기 0.5초 전부터 서서히 꺼짐
        shieldTint = Mathf.MoveTowards(shieldTint, targetTint, dt / 0.25f);

        // 바닥 알림 빛: 번쩍(flash) + 보호 중 노랑(tint) 중 더 센 쪽
        float fa = flashLevel * flashLevel * 0.42f, ta = shieldTint * 0.26f;
        Color c = fa >= ta ? flashColor : OnePlusColor;
        float a = Mathf.Max(fa, ta) * vis;
        accentGlow.enabled = a > 0.002f;
        accentGlow.color = new Color(c.r, c.g, c.b, a);
        // 원래 푸른 바닥 빛은 알림 빛이 켜진 만큼 옅게 (푸른색 → 알림 색으로 바뀌는 느낌)
        if (floorGlow != null)
        {
            float k = 1f - Mathf.Clamp01(Mathf.Max(flashLevel, shieldTint));
            Color fg = FloorGlowC;
            floorGlow.color = new Color(fg.r, fg.g, fg.b, fg.a * k);
        }

        // ONE+ 테두리 훑기: 판정선 아래에서 위로 0.8초 동안 올라가며 사라짐
        if (sweepL != null)
        {
            bool on = sweepT >= 0f && sweepT < 1f;
            sweepL.enabled = sweepR.enabled = on && vis > 0.001f;
            if (on)
            {
                sweepT += dt / 0.8f;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(sweepT), 2f);
                float yy = gm.hitLineY - 1f + e * 16f;
                float al = Mathf.Sin(Mathf.Clamp01(sweepT) * Mathf.PI) * 0.95f * vis;
                Color sc = new Color(OnePlusColor.r, OnePlusColor.g, OnePlusColor.b, al);
                sweepL.transform.position = new Vector3(VisLeft, yy, 0f);
                sweepR.transform.position = new Vector3(VisRight, yy, 0f);
                sweepL.color = sweepR.color = sc;
                if (sweepT >= 1f) sweepT = -1f;
            }
        }
    }

    // 막힌 구간 표시용 빨간 사선 그림 (흰색 — 색은 곱해서). 진한 사선 + 옅은 바탕, 바둑판처럼 이어 붙임
    private static Sprite stripeUi, stripeWorld;
    private static Texture2D stripeTex;
    private static Texture2D StripeTex
    {
        get
        {
            if (stripeTex != null) return stripeTex;
            const int S = 32;
            stripeTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            stripeTex.wrapMode = TextureWrapMode.Repeat;
            stripeTex.filterMode = FilterMode.Bilinear;
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Repeat(x + y, S);                         // ↗ 방향 사선
                    float band = Mathf.Clamp01(Mathf.Min(d - 0.5f, 10.5f - d) + 0.5f);   // 폭 약 10px, 끝은 부드럽게
                    float a = Mathf.Lerp(0.22f, 1f, band);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            stripeTex.SetPixels32(px); stripeTex.Apply();
            return stripeTex;
        }
    }
    private static Sprite StripeUi { get { if (stripeUi == null) stripeUi = Sprite.Create(StripeTex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect); return stripeUi; } }
    private static Sprite StripeWorld { get { if (stripeWorld == null) stripeWorld = Sprite.Create(StripeTex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 260f, 0, SpriteMeshType.FullRect); return stripeWorld; } }
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
        wLifeTop = WorldPiece("LifeTop", PixelSprite, x, y + WLifeLen, WLifeW + 0.05f, 0.035f, -14); // 위쪽 끝 표시 (고정)
        wLifeFlow = WorldPiece("LifeInfiniteFlow", vBellSprite, x, y, WLifeW, 0.01f, -15);  // GAME SETTING 무한 LIFE: 흐르는 빛
        wLifeCapShade = WorldPiece("LifeStrictShade", StripeWorld, x, y, WLifeW, 0.01f, -15);     // STRICT: 막힌 위쪽 (빨간 사선)
        wLifeCapShade.drawMode = SpriteDrawMode.Tiled; wLifeCapShade.transform.localScale = Vector3.one;
        wLifeCapLine = WorldPiece("LifeStrictLine", PixelSprite, x, y, WLifeW + 0.05f, 0.03f, -14); // STRICT: 상한선
        wLifeFloorShade = WorldPiece("LifeFloorShade", StripeWorld, x, y, WLifeW, 0.01f, -15);    // STRICT+: 막힌 아래쪽 (빨간 사선)
        wLifeFloorShade.drawMode = SpriteDrawMode.Tiled; wLifeFloorShade.transform.localScale = Vector3.one;
        wLifeFloorLine = WorldPiece("LifeFloorLine", PixelSprite, x, y, WLifeW + 0.05f, 0.03f, -14); // STRICT+: 하한선
    }

    // SIMPLE-DIVERSE LIFE 바 조각들을 가로로 옮김 (만들 때의 위치 기준)
    private readonly List<KeyValuePair<Transform, float>> wLifeBaseX = new List<KeyValuePair<Transform, float>>();
    private float wLifeShift = 0f;
    private void ShiftWorldLife(float x)
    {
        float dx = x - (laneRight + WLifeGap);
        if (Mathf.Abs(dx - wLifeShift) < 0.0001f) return;
        wLifeShift = dx;
        foreach (var kv in wLifeBaseX)
        {
            if (kv.Key == null) continue;
            Vector3 p = kv.Key.position;
            kv.Key.position = new Vector3(kv.Value + dx, p.y, p.z);
        }
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
        wLifeBaseX.Add(new KeyValuePair<Transform, float>(go.transform, x));
        return sr;
    }

    private void UpdateWorldLife(float shown, Color c, float blink)
    {
        if (wLifeFill == null) return;
        float vis = lastLaneVis;
        bool on = vis > 0.001f && !gm.HideAllUi;     // UNKNOWN UI: LIFE 막대도 숨김
        float y = gm.hitLineY, x = VisRight + WLifeGap;
        ShiftWorldLife(x);   // 6키 확장: 레인 끝을 따라 옮김
        float f = Mathf.Clamp01(shown / 100f);
        float h = Mathf.Max(0.0001f, WLifeLen * f);
        bool has = on && f > 0.001f;

        wLifeTrack.enabled = on;
        wLifeTrack.color = new Color(0.02f, 0.03f, 0.05f, 0.65f * vis);
        wLifeFrameL.enabled = wLifeFrameR.enabled = on;
        wLifeFrameL.color = wLifeFrameR.color = new Color(1f, 1f, 1f, 0.35f * vis);

        bool infinite = gm.LifeInfinite;   // GAME SETTING · IMMORTAL
        bool shield = shieldShown;         // ONE+ 보호: 연노랑 그라데이션 타이머
        Sprite fillSprite = infinite ? InfiniteGradient : shield ? OnePlusGradient : PixelSprite;
        if (wLifeFill.sprite != fillSprite)
        {
            wLifeFill.sprite = fillSprite;
            if (playfield != null) playfield.Apply(wLifeFill, false, false);   // 원근 재질도 새 그림에 맞춤 (LIFE 바는 사라짐 영향 없음)
        }
        Vector2 ps = fillSprite.bounds.size;
        wLifeFill.enabled = has;
        wLifeFill.transform.position = new Vector3(x, y + h * 0.5f, 0f);
        wLifeFill.transform.localScale = new Vector3(WLifeW / ps.x, h / ps.y, 1f);
        // 무한 LIFE: 채움 색만 그라데이션 (아래 진한 파랑 → 위 밝은 하늘색, 색은 그림에 들어 있음)
        wLifeFill.color = (infinite || shield) ? new Color(1f, 1f, 1f, 0.9f * vis) : new Color(c.r, c.g, c.b, 0.9f * blink * vis);

        Vector2 gs = hBellSprite.bounds.size;
        wLifeGlow.enabled = has;
        wLifeGlow.transform.position = new Vector3(x, y + h * 0.5f, 0f);
        wLifeGlow.transform.localScale = new Vector3(0.42f / gs.x, h / gs.y, 1f);
        wLifeGlow.color = new Color(c.r, c.g, c.b, 0.22f * blink * vis);

        wLifeFoot.enabled = on;
        wLifeFoot.color = new Color(1f, 1f, 1f, 0.95f * vis);
        if (wLifeTop != null)
        {
            wLifeTop.enabled = on;
            wLifeTop.color = new Color(1f, 1f, 1f, 0.55f * vis);   // 위쪽 끝: 아래보다 조금 옅게 (가득 차면 위에 움직이는 끝선이 겹침)
        }
        wLifeHead.enabled = has;
        wLifeHead.transform.position = new Vector3(x, y + h, 0f);
        wLifeHead.color = new Color(1f, 1f, 1f, 0.95f * vis);

        // STRICT · STRICT+: LIFE 최대 상한 위쪽을 빨간 사선으로 막고 상한 위치에 빨간 선
        float cap = Mathf.Clamp01((float)gm.LifeCap / 100f);
        bool strict = on && cap < 0.999f && !infinite;
        if (wLifeCapShade != null)
        {
            wLifeCapShade.enabled = wLifeCapLine.enabled = strict;
            if (strict)
            {
                float yc = y + WLifeLen * cap, hs = Mathf.Max(0.0001f, WLifeLen * (1f - cap));
                wLifeCapShade.transform.position = new Vector3(x, yc + hs * 0.5f, 0f);
                wLifeCapShade.size = new Vector2(WLifeW, hs);
                wLifeCapShade.color = new Color(StrictShade.r, StrictShade.g, StrictShade.b, StrictShade.a * vis);
                wLifeCapLine.transform.position = new Vector3(x, yc, 0f);
                wLifeCapLine.color = new Color(StrictLine.r, StrictLine.g, StrictLine.b, StrictLine.a * vis);
            }
        }
        // STRICT+: LIFE 하한 아래쪽을 빨간 사선으로 막고 하한 위치에 빨간 선
        float floor = Mathf.Clamp01((float)gm.LifeFloor / 100f);
        bool floored = on && floor > 0.001f && !infinite;
        if (wLifeFloorShade != null)
        {
            wLifeFloorShade.enabled = wLifeFloorLine.enabled = floored;
            if (floored)
            {
                float yf = y + WLifeLen * floor, hf = Mathf.Max(0.0001f, WLifeLen * floor);
                wLifeFloorShade.transform.position = new Vector3(x, y + hf * 0.5f, 0f);
                wLifeFloorShade.size = new Vector2(WLifeW, hf);
                wLifeFloorShade.color = new Color(StrictShade.r, StrictShade.g, StrictShade.b, StrictShade.a * vis);
                wLifeFloorLine.transform.position = new Vector3(x, yf, 0f);
                wLifeFloorLine.color = new Color(StrictLine.r, StrictLine.g, StrictLine.b, StrictLine.a * vis);
            }
        }

        foreach (var t in wLifeTicks)
        {
            t.enabled = on;
            t.color = new Color(1f, 1f, 1f, 0.35f * vis);
        }

        // GAME SETTING 무한 LIFE: 막대 안을 아래 → 위로 흐르는 밝은 빛 두 줄기 (막대 끝에서는 줄어들어 밖으로 안 나감)
        if (wLifeFlow != null)
        {
            bool inf = false;   // (흐르는 빛 없음 — 무한 LIFE 는 채움 그라데이션만)
            wLifeFlow.enabled = inf;
            if (inf)
            {
                float len = WLifeLen * 0.22f;
                float k = Mathf.Repeat(Time.unscaledTime * 0.35f, 1f);
                float cy = y + Mathf.Lerp(-len * 0.5f, WLifeLen + len * 0.5f, k);
                float y0 = Mathf.Max(y, cy - len * 0.5f), y1 = Mathf.Min(y + WLifeLen, cy + len * 0.5f);
                float fh = Mathf.Max(0.0001f, y1 - y0);
                Vector2 vs = vBellSprite.bounds.size;
                wLifeFlow.transform.position = new Vector3(x, (y0 + y1) * 0.5f, 0f);
                wLifeFlow.transform.localScale = new Vector3(WLifeW / vs.x, fh / vs.y, 1f);
                wLifeFlow.color = new Color(0.9f, 0.98f, 1f, 0.55f * vis * Mathf.Clamp01(fh / (len * 0.5f)));
            }
        }
    }

    // 레인이 가운데에서 좌우로 펼쳐짐 (w: 0 ~ 1)
    // =====================================================================
    //  레인 사라짐 / 드러남 — 선이 위로 빨려 들어감 / 아래에서 올라옴
    //   각 선의 원래 아래 끝(bottom) ~ 위 끝(top) 중 보이는 구간만 남기도록 세로 위치 · 배율만 바꿈 (가로는 그대로)
    // =====================================================================
    private class SuckLine { public SpriteRenderer sr; public float bottom, top, pivotY, spriteH; }
    private readonly List<SuckLine> suckLines = new List<SuckLine>();

    private void AddSuckLine(SpriteRenderer sr)
    {
        if (sr == null || sr.sprite == null) return;
        Bounds sb = sr.sprite.bounds;
        float hs = sb.size.y;
        float py = (0f - sb.min.y) / Mathf.Max(0.0001f, hs);             // 피벗이 스프라이트 높이의 어디쯤인지 (0 아래 ~ 1 위)
        float sy = sr.transform.localScale.y;
        float bottom = sr.transform.position.y - py * sy * hs;
        suckLines.Add(new SuckLine { sr = sr, bottom = bottom, top = bottom + sy * hs, pivotY = py, spriteH = hs });
    }

    private void UpdateLaneHide()
    {
        bool hiding;
        float p = gm.LaneHideProgress(gm.currentPlayTime, out hiding);   // 0 ~ 1 (이벤트 시작 ~ 끝)
        // (선 9개뿐이라 매 프레임 계산 — 레인 구분선 켜고 끄기와 순서가 꼬이지 않도록)

        // 화면에서 보이는 위쪽 끝 (원근 화면은 판정선 위 12 유닛 정도, 정면 화면은 카메라 위 끝)
        Camera cam = gm.SceneCamera;
        float viewTop = IsDiverse ? gm.hitLineY + 12f
                      : (cam != null && cam.orthographic ? cam.transform.position.y + cam.orthographicSize + 0.2f : gm.hitLineY + 10f);
        foreach (var ln in suckLines)
        {
            if (ln.sr == null) continue;
            float top = Mathf.Min(ln.top, Mathf.Max(viewTop, ln.bottom + 0.01f));
            float b2, t2;
            if (hiding)
            {
                // 사라짐: 아래 끝이 점점 빨라지며 위로 빨려 올라감 (ease-in)
                float e = p * p * p;
                b2 = Mathf.Lerp(ln.bottom, top, e);
                t2 = p >= 1f ? b2 : ln.top;
            }
            else
            {
                // 드러남: 아래(판정선 쪽)에서 위로 쭉 올라옴 (ease-out), 다 올라오면 원래 길이로
                float e = 1f - Mathf.Pow(1f - p, 3f);
                b2 = ln.bottom;
                t2 = p >= 1f ? ln.top : Mathf.Lerp(ln.bottom, top, e);
            }
            float len = Mathf.Max(0f, t2 - b2);
            bool vis = len > 0.002f;
            if (!vis) { ln.sr.enabled = false; continue; }
            if (!laneLines.Contains(ln.sr)) ln.sr.enabled = true;          // 레인 구분선은 설정(레인 구분선 켜고 끄기)이 정함
            Transform tr = ln.sr.transform;
            Vector3 sc = tr.localScale;
            tr.localScale = new Vector3(sc.x, len / Mathf.Max(0.0001f, ln.spriteH), sc.z);
            Vector3 pos = tr.position;
            tr.position = new Vector3(pos.x, b2 + ln.pivotY * len, pos.z);
        }
    }

    private float sixRatioApplied = -1f;
    private void ApplyLaneExpand(float w)
    {
        if (Mathf.Abs(w - laneExpandApplied) < 0.0001f && Mathf.Abs(sixRatio - sixRatioApplied) < 0.0001f) return;
        laneExpandApplied = w;
        sixRatioApplied = sixRatio;
        float k = Mathf.Max(0.001f, w);
        foreach (var p in laneParts)
        {
            if (p.t == null) continue;
            float kp = p.kind == 2 ? k : k * sixRatio;            // 위치: 레인별은 그대로, 판 · 벽은 보이는 폭을 따라감
            float ks = p.kind == 0 ? k * sixRatio : k;           // 폭: 레인 전체 판만 보이는 폭을 따라감
            Vector3 cs = p.t.localScale, cp = p.t.position;
            p.t.localScale = new Vector3(p.scale.x * ks, cs.y, cs.z);
            p.t.position = new Vector3(laneCenterX + (p.pos.x - laneCenterX) * kp, cp.y, cp.z);
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
        sr.sortingOrder = order >= 6 ? order + NoteLayer.EffectLift : order;   // 노트(5층까지) 위에 그려야 하는 이펙트
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

    // GAME SETTING: 레인 불투명도를 게임 중에 바로 바꿈 (레인 바탕 · 판정선 아래 어둡게)
    public void SetLaneOpacity(float opacity)
    {
        opacity = Mathf.Clamp01(opacity);
        for (int i = 0; i < worldSprites.Count; i++)
        {
            var kv = worldSprites[i];
            if (kv.Key == null) continue;
            Color c = kv.Value;
            if (kv.Key == LaneBgRenderer) worldSprites[i] = new KeyValuePair<SpriteRenderer, Color>(kv.Key, new Color(c.r, c.g, c.b, opacity));
            else if (kv.Key.name == "UnderLine") worldSprites[i] = new KeyValuePair<SpriteRenderer, Color>(kv.Key, new Color(c.r, c.g, c.b, 0.4f * opacity));
        }
        float a = worldAlpha;
        worldAlpha = -1f;          // 다음 SetWorldAlpha 에서 다시 칠하도록
        SetWorldAlpha(a < 0f ? 1f : a);
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
        BuildOnePlusIcon(laneHud, IsDiverse ? 82f : 96f);

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
        //  · 어두운 반투명 트랙 + 가는 밝은 테두리 → MV 위에서도 빈 부분이 또렷함
        //  · 채움: 원통처럼 왼쪽이 밝은 유리 질감 (색은 Image.color) + 뒤쪽 은은한 빛
        //  · 채움 맨 위에 밝은 끝선 (지금 LIFE 위치)
        //  · 양 끝 반지름 = 바 두께의 절반이 되도록 9-slice 배율 조정 (테두리 32px)
        lifeRoot = SettingsUIKit.Rect("LifeGauge", laneHud);
        lifeBg = SettingsUIKit.Img("Track", lifeRoot, LifeTrackColor);
        lifeBg.sprite = pillSprite; lifeBg.type = Image.Type.Sliced; lifeBg.fillCenter = true;
        lifeBg.pixelsPerUnitMultiplier = 32f / (LifeBarWidth * 0.5f);
        SettingsUIKit.Stretch(lifeBg.rectTransform);
        lifeGlowImg = SettingsUIKit.Img("Glow", lifeRoot, new Color(1f, 1f, 1f, 0f));
        lifeGlowImg.sprite = pillGlowSprite; lifeGlowImg.type = Image.Type.Sliced; lifeGlowImg.fillCenter = true;
        lifeGlowImg.pixelsPerUnitMultiplier = 32f / ((LifeBarWidth - 2f * LifeFillInset) * 0.5f + LifeGlowPad);
        lifeGlowImg.rectTransform.anchorMin = Vector2.zero;
        lifeGlowImg.rectTransform.anchorMax = Vector2.one;
        lifeGlowImg.rectTransform.offsetMin = new Vector2(LifeFillInset - LifeGlowPad, LifeFillInset - LifeGlowPad);
        lifeGlowImg.rectTransform.offsetMax = new Vector2(-LifeFillInset + LifeGlowPad, -LifeFillInset + LifeGlowPad);
        lifeFill = SettingsUIKit.Img("Fill", lifeRoot, Color.white);
        lifeFill.sprite = pillShadeSprite; lifeFill.type = Image.Type.Sliced; lifeFill.fillCenter = true;
        lifeFill.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, (LifeBarWidth - 2f * LifeFillInset) * 0.5f);
        lifeFillRt = lifeFill.rectTransform;
        lifeFillRt.anchorMin = Vector2.zero;
        lifeFillRt.anchorMax = Vector2.one;
        lifeFillRt.offsetMin = new Vector2(LifeFillInset, LifeFillInset);
        lifeFillRt.offsetMax = new Vector2(-LifeFillInset, -LifeFillInset);
        lifeOutline = SettingsUIKit.Img("Outline", lifeRoot, LifeOutlineColor);
        lifeOutline.sprite = pillRingSprite; lifeOutline.type = Image.Type.Sliced; lifeOutline.fillCenter = true;
        lifeOutline.pixelsPerUnitMultiplier = 32f / (LifeBarWidth * 0.5f);
        SettingsUIKit.Stretch(lifeOutline.rectTransform);
        lifeHeadLine = SettingsUIKit.Img("Head", lifeRoot, Color.white);
        lifeHeadLine.sprite = NoteArt.HBellSprite;
        lifeHeadLine.enabled = false;
        // GAME SETTING 무한 LIFE: 채움 위를 아래 → 위로 흐르는 밝은 빛 (가득 찬 막대 안에서만 보이게 마스크)
        lifeFill.gameObject.AddComponent<RectMask2D>();
        lifeGrad = SettingsUIKit.Img("InfiniteGradient", lifeFillRt, Color.white);   // 아래 진한 파랑 → 위 밝은 하늘색
        lifeGrad.sprite = InfiniteGradient;
        SettingsUIKit.Stretch(lifeGrad.rectTransform);
        lifeGrad.enabled = false;
        lifeFlow = SettingsUIKit.Img("InfiniteFlow", lifeFillRt, new Color(1f, 1f, 1f, 0f));
        lifeFlow.sprite = vBellSprite;
        lifeFlow.rectTransform.anchorMin = new Vector2(0f, 0f);
        lifeFlow.rectTransform.anchorMax = new Vector2(1f, 0f);
        lifeFlow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        lifeFlow.enabled = false;
        // STRICT · STRICT+: 상한 위쪽 빨간 사선 + 상한선 / STRICT+: 하한 아래쪽 빨간 사선 + 하한선 (UpdateLife 에서 위치 갱신)
        lifeCapShade = SettingsUIKit.Img("StrictCap", lifeRoot, StrictShade);
        lifeCapShade.sprite = StripeUi; lifeCapShade.type = Image.Type.Tiled; lifeCapShade.pixelsPerUnitMultiplier = 2.3f;
        lifeCapShade.enabled = false;
        lifeCapLine = SettingsUIKit.Img("StrictCapLine", lifeRoot, StrictLine);
        lifeCapLine.enabled = false;
        lifeFloorShade = SettingsUIKit.Img("StrictFloor", lifeRoot, StrictShade);
        lifeFloorShade.sprite = StripeUi; lifeFloorShade.type = Image.Type.Tiled; lifeFloorShade.pixelsPerUnitMultiplier = 2.3f;
        lifeFloorShade.enabled = false;
        lifeFloorLine = SettingsUIKit.Img("StrictFloorLine", lifeRoot, StrictLine);
        lifeFloorLine.enabled = false;
        // ONE+ 보호: 막대 전체를 덮는 유리 + 자물쇠 (보호 시간 동안)
        lifeShieldGrad = SettingsUIKit.Img("OnePlusGradient", lifeFillRt, Color.white);   // ONE+ 보호: 아래 금빛 → 위 연노랑
        lifeShieldGrad.sprite = OnePlusGradient;
        SettingsUIKit.Stretch(lifeShieldGrad.rectTransform);
        lifeShieldGrad.enabled = false;
        // SIMPLE 스킨이면 항상 LIFE 사용 (이 시점엔 아직 GameManager 에 스킨이 등록되기 전이라 gm.LifeEnabled 로 판단하면 안 됨)
        lifeRoot.gameObject.SetActive(!IsDiverse); // DIVERSE 는 레인 옆 원근 LIFE 바(UpdateWorldLife)를 씀

        // 키 이름: 레인마다 (6키 확장 채보는 그때그때 4키 / 6키 키 — UpdateKeyTexts)
        int laneN = gm.laneXPositions != null ? Mathf.Min(Lanes.Max, gm.laneXPositions.Length) : 4;
        for (int i = 0; i < laneN; i++)
        {
            keyTexts[i] = Text(laneHud, SettingsStore.KeyDisplayName(gm.DisplayKeyForLane(i)), 20, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.35f), FontStyles.Bold, F.label);
            keyTexts[i].rectTransform.sizeDelta = new Vector2(120f, 30f);
        }
    }

    // =====================================================================
    //  게임에서 부르는 함수
    // =====================================================================
    public void SetPressed(int lane, bool on)
    {
        if (lane < 0 || lane >= Lanes.Max) return;
        pressed[lane] = on;
        if (on) glowLevel[lane] = 1f;
    }

    public void SetHold(int lane, bool on)
    {
        if (lane < 0 || lane >= Lanes.Max) return;
        held[lane] = on;
    }

    public void Hit(int lane) { Hit(lane, LaneColor(lane), 1f); }

    // 노트를 알 때: 노트 색(RED · 드래그) 그대로, 롱노트 끝은 조금 작게
    public void Hit(int lane, NoteData n)
    {
        if (n == null) { Hit(lane); return; }
        bool tail = n.Type == 3 || n.Type == 13 || NoteTypes.IsSlideRelay(n.Type);
        // 드래그 노트: 일반 노트와 똑같은 이펙트, 색만 연한 노란색 (NoteColor 가 드래그 색을 줌)
        //  슬라이드의 중계점 · 끝은 머리(몸통)와 같은 색
        float x = n.IsQuarter && gm.laneXPositions != null ? CenterXOf(n, gm.laneXPositions) : float.NaN;
        Hit(lane, NoteColor(n.SlideHead != null ? n.SlideHead : n), tail ? 0.7f : 1f, false, false, x, n.IsQuarter ? n.SubW * 0.25f : 1f);
    }

    public void Hit(int lane, Color c, float power) { Hit(lane, c, power, false); }

    private void Hit(int lane, Color c, float power, bool mini, bool drag = false, float atX = float.NaN, float widthFrac = 1f)
    {
        if (lane < 0 || lane >= gm.laneXPositions.Length) return;
        Burst b = bursts.Find(x => x.t >= 1f);
        if (b == null) { b = bursts[0]; foreach (var o in bursts) if (o.t > b.t) b = o; }   // 다 쓰는 중이면 가장 오래된 것
        b.t = 0f;
        b.lane = lane;
        b.c = c;
        b.power = power;
        b.x = float.IsNaN(atX) ? gm.laneXPositions[lane] : atX;
        b.wf = Mathf.Clamp(widthFrac, 0.25f, Lanes.Max);   // 홀드 맥박은 넓은 롱노트 폭까지
        b.mini = mini;
        b.perfect = !mini && (lastJudge == "EXCELLENT" || lastJudge == "JUST");
        b.ring.enabled = b.wave.enabled = true;
        // 설정 > 게임플레이 > 히트 이펙트 = 낮음 이면 두 번째 충격파 · 가로 섬광 · 광원 · 반짝임은 생략 (가벼움)
        bool full = !mini && SettingsStore.Current.EffectQuality > 0;
        b.core.enabled = !mini;
        b.wave2.enabled = b.flare.enabled = b.hot.enabled = b.hotCore.enabled = full;
        if (!full) b.perfect = false;
        b.star.enabled = b.perfect;
        Vector3 at = new Vector3(b.x, gm.hitLineY, 0f);
        b.ring.transform.position = at; b.core.transform.position = at;     // (배열을 만들지 않음 — 칠 때마다 쓰레기 생김 방지)
        b.wave.transform.position = at; b.wave2.transform.position = at; b.flare.transform.position = at;
        b.star.transform.position = at; b.hot.transform.position = at; b.hotCore.transform.position = at;
        b.star.transform.rotation = Quaternion.identity;
        // 드래그 노트 효과 (히트 이펙트 '낮음'이면 파편은 생략, 보석 · 번개 줄기는 그대로)
        b.drag = drag && !mini;
        b.gem.enabled = b.zip.enabled = b.drag;
        if (b.drag)
        {
            // 심플하게: 두 번째 충격파 · 가로 섬광 · 반짝임은 끄고 드래그 전용 효과로
            b.wave2.enabled = b.flare.enabled = b.star.enabled = false;
            b.gem.transform.position = at; b.zip.transform.position = at;
            b.shards[0].enabled = true;                                      // 빛무리
            b.shards[1].enabled = full;                                      // 두 번째 잔상 (히트 이펙트 '낮음'이면 생략)
            b.shards[2].enabled = false;
            for (int k = 0; k < DragShards; k++) { b.shards[k].transform.position = at; b.shards[k].transform.rotation = Quaternion.identity; }
        }
        else for (int k = 0; k < DragShards; k++) b.shards[k].enabled = false;

        // 화려한 빛: 빛무리 · 긴 빛줄기 (드래그 · 홀드 맥박 · 히트 이펙트 '낮음'은 제외)
        bool fancy = !mini && !b.drag;
        b.halo.enabled = b.streak.enabled = fancy && full;
        b.halo.transform.position = at; b.streak.transform.position = at;
        if (!mini) receptorPop[lane] = 1f;
    }

    public void OnJudge(string judge)
    {
        // 게임 내부 판정 이름은 EXCELLENT 그대로, 화면에는 PERFECT 로 표시 (무지개 그라데이션)
        //  "JUST" = PERFECT 중 ±20ms 안 (설정 > JUST 표시를 켰을 때만 이 이름으로 옴) → 흰 글자 + 흰 빛번짐
        bool isJust = judge == "JUST";
        string shown = judge == "EXCELLENT" ? "PERFECT" : judge;
        judgeRainbow = judge == "EXCELLENT";
        lastJudge = judge;
        judgeText.text = shown;
        judgeText.color = judgeRainbow || isJust ? Color.white : JudgeColor(judge);
        SetJudgeGlow(isJust);
        judgeTimer = 0f;
        fastSlowText.text = "";
    }

    // JUST 글자의 흰 빛번짐: 글꼴 재질의 Underlay(바깥으로 부드럽게 번지는 흰 그림자)를 켜고 끔
    //  (재질은 이 판정 글자 전용 복사본 — 다른 글자에는 영향 없음)
    private Material judgeGlowMat;
    private bool judgeGlowOn;
    private void SetJudgeGlow(bool on)
    {
        if (judgeText == null || on == judgeGlowOn) return;
        judgeGlowOn = on;
        if (judgeGlowMat == null)
        {
            judgeGlowMat = judgeText.fontMaterial;      // 인스턴스 재질
            judgeGlowMat.SetColor("_UnderlayColor", new Color(1f, 1f, 1f, 0.9f));
            judgeGlowMat.SetFloat("_UnderlayOffsetX", 0f);
            judgeGlowMat.SetFloat("_UnderlayOffsetY", 0f);
            judgeGlowMat.SetFloat("_UnderlayDilate", 0.55f);
            judgeGlowMat.SetFloat("_UnderlaySoftness", 1f);
            judgeText.extraPadding = true;
        }
        if (on) judgeGlowMat.EnableKeyword("UNDERLAY_ON");
        else judgeGlowMat.DisableKeyword("UNDERLAY_ON");
        judgeText.UpdateMeshPadding();
        judgeText.SetMaterialDirty();
    }

    public void OnFastSlow(bool isFast, int ms)
    {
        fastSlowText.SetText(isFast ? "-{0}ms" : "+{0}ms", ms);       // FAST 26 → -26ms,  SLOW 43 → +43ms (SetText: GC 없음)
        fastSlowText.color = isFast ? new Color(0.53f, 0.81f, 0.92f) : new Color(1f, 0.42f, 0.42f);
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================

    // [렉 수정] 노트 그림(코드로 만드는 텍스처)과 원근 재질을 플레이 전에 미리 만들어 둠
    //  예전에는 첫 노트 · 첫 롱노트가 나오는 순간에 만들어져서 그 프레임이 끊겼다
    private void PrewarmNoteMaterials()
    {
        if (playfield == null) return;
        Sprite[] sprites = { NoteArt.NoteFaceSprite, NoteArt.TailCapSprite, NoteArt.LongBodySprite, NoteArt.GlowSprite,
                             NoteArt.DragGemSprite, NoteArt.DragTrailSprite,
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

    void Update()
    {
        if (gm == null) return;
        float dt = Time.unscaledDeltaTime;

        // 레인이 나타나기 시작하면: 빠르게 보이면서 가운데에서 좌우로 펼쳐짐
        bool show = gm.LaneIntroStarted || gm.currentState == GameState.Playing;
        fadeIn = Mathf.MoveTowards(fadeIn, show ? 1f : 0f, dt / 0.35f);
        if (show) laneExpand = Mathf.MoveTowards(laneExpand, 1f, dt / LaneExpandSeconds);
        // 6키 확장 채보: 4키 구간은 가운데 4레인 폭, 펼쳐지면 6레인 폭
        sixExpand = gm.SixLane ? gm.LaneExpand : 1f;
        sixRatio = gm.SixLane ? (4f + 2f * sixExpand) / Lanes.Max : 1f;
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
        bool lines = SettingsStore.Current.LaneLines && !IsCircle && laneVis > 0.001f;   // CIRCLE: 설정과 관계없이 레인 구분선 없음
        foreach (var l in laneLines) if (l != null && l.enabled != lines) l.enabled = lines;
        hudGroup.alpha = gm.HideAllUi ? 0f : fadeIn * endA * gm.LaneUiAlpha;   // 레인 테두리가 사라질 때 함께 사라짐   // UNKNOWN UI: 레인을 뺀 HUD (점수 · 콤보 · 판정 · LIFE · 키 이름 …) 숨김
        laneHudGroup.alpha = basicVis;

        lastLaneVis = laneVis;
        if (laneBlur != null) laneBlur.Tick(laneVis, laneExpandApplied * sixRatio);
        // 바깥 레인 구분선: 4키 구간에서는 숨김 (펼쳐지는 만큼 보임)
        for (int i = 0; i < outerDivs.Count; i++)
        {
            SpriteRenderer d = outerDivs[i];
            if (d == null) continue;
            Color oc = outerDivColors[i];
            d.color = new Color(oc.r, oc.g, oc.b, IsCircle ? 0f : oc.a * laneVis * sixExpand);   // CIRCLE: 바깥 구분선도 없음
        }

        // 레인 사라짐 / 드러남 (채보의 FOLD · UNFOLD = HIDE · SHOW 줄)
        //  · 양쪽 흰 실선 · 레일 · 레인 구분선: 사라질 때 위쪽으로 빨려 들어가고, 드러날 때 아래(판정선)에서 다시 올라옴
        //  · 키 이름 (레인 HUD): 같은 박자로 옅어졌다 다시 보임
        //  · 그 밖(푸른 그라데이션 배경 · 판정선 · 레인 바탕 · 노트 · 효과)은 사라짐 상태에서도 그대로 보임
        if (gm.HasLaneFolds)
        {
            float shown = gm.LaneFoldAt(gm.currentPlayTime);
            laneHudGroup.alpha = basicVis * shown;
            UpdateLaneHide();
        }
        UpdateGlows(dt, laneVis);
        UpdateAccents(dt, laneVis);
        UpdateHold(dt, laneVis);
        UpdateBursts(dt, laneVis);
        UpdateLife(dt);
        UpdateRedWarning(dt, laneVis);
        UpdateHudValues(dt);
        PlaceLaneHud();
        UpdateOnePlusIcon(dt);
    }

    // ---- ONE+ 발동 아이콘 (콤보 자리) ----
    private void BuildOnePlusIcon(RectTransform parent, float size)
    {
        onePlusRoot = SettingsUIKit.Rect("OnePlusIcon", parent);
        onePlusRoot.sizeDelta = new Vector2(size, size);
        onePlusGroup = onePlusRoot.gameObject.AddComponent<CanvasGroup>();
        onePlusGroup.blocksRaycasts = false; onePlusGroup.interactable = false;
        onePlusGlow = SettingsUIKit.Img("Glow", onePlusRoot, OnePlusColor);            // 뒤쪽 은은한 노란 빛
        onePlusGlow.sprite = NoteArt.GlowSprite;
        onePlusGlow.rectTransform.sizeDelta = new Vector2(size * 3.2f, size * 3.2f);
        onePlusRing1 = MakeOnePlusRing(size);
        onePlusRing2 = MakeOnePlusRing(size);
        onePlusBadge = GameModes.CreateBadge(onePlusRoot, GameMode.OnePlus, size);
        onePlusRoot.gameObject.SetActive(false);
    }

    private Image MakeOnePlusRing(float size)
    {
        Image r = SettingsUIKit.Img("Ring", onePlusRoot, OnePlusColor);
        r.sprite = GameModes.RingSprite; r.type = Image.Type.Sliced;
        r.pixelsPerUnitMultiplier = 16f / Mathf.Max(4f, size * 0.22f);
        r.rectTransform.sizeDelta = new Vector2(size, size);
        return r;
    }

    private void UpdateOnePlusIcon(float dt)
    {
        if (onePlusRoot == null) return;
        bool on = onePlusT >= 0f && onePlusT < OnePlusIconSeconds;
        if (onePlusRoot.gameObject.activeSelf != on) onePlusRoot.gameObject.SetActive(on);
        // 콤보 숫자 · COMBO 글자는 아이콘이 떠 있는 동안 옅게
        float hideCombo = 0f;
        if (on)
        {
            onePlusT += dt;
            float t = onePlusT;
            // 콤보 숫자와 같은 자리
            RectTransform cr = comboText.rectTransform;
            onePlusRoot.anchorMin = cr.anchorMin; onePlusRoot.anchorMax = cr.anchorMax; onePlusRoot.pivot = new Vector2(0.5f, 0.5f);
            onePlusRoot.anchoredPosition = cr.anchoredPosition + (cr.pivot - new Vector2(0.5f, 0.5f)) * -cr.rect.size;
            // 나타남(0.25초, 살짝 커졌다 제자리) → 유지 → 사라짐(마지막 0.35초, 위로 살짝 떠오르며)
            float pin = Mathf.Clamp01(t / 0.25f);
            float back = 1f + 2.2f * Mathf.Pow(pin - 1f, 3f) + 1.2f * Mathf.Pow(pin - 1f, 2f);   // easeOutBack
            float pout = Mathf.Clamp01((t - (OnePlusIconSeconds - 0.35f)) / 0.35f);
            float alpha = Mathf.Clamp01(t / 0.12f) * (1f - pout * pout);
            onePlusGroup.alpha = alpha;
            onePlusBadge.localScale = Vector3.one * Mathf.Max(0f, back) * (1f - 0.1f * pout);
            onePlusBadge.anchoredPosition = new Vector2(0f, 14f * pout);
            onePlusBadge.localRotation = Quaternion.Euler(0f, 0f, 8f * (1f - pin) * Mathf.Sin(pin * Mathf.PI * 2f));
            // 빛 고리 2개: 아이콘 크기에서 바깥으로 퍼지며 사라짐 (두 번째는 0.15초 늦게)
            RingStep(onePlusRing1, t, 0.00f);
            RingStep(onePlusRing2, t, 0.15f);
            // 뒤쪽 빛: 처음 번쩍 → 은은하게 숨쉬기
            float glow = Mathf.Lerp(0.85f, 0.30f + 0.08f * Mathf.Sin(t * 6f), Mathf.Clamp01(t / 0.4f));
            onePlusGlow.color = new Color(OnePlusColor.r, OnePlusColor.g, OnePlusColor.b, glow);
            onePlusGlow.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, pin);
            hideCombo = alpha;
        }
        else if (onePlusT >= OnePlusIconSeconds) onePlusT = -1f;
        float ca = 1f - hideCombo;
        comboText.alpha = ca;
        comboLabel.alpha = ca;
    }

    private void RingStep(Image ring, float t, float delay)
    {
        float k = Mathf.Clamp01((t - delay) / 0.6f);
        bool show = t >= delay && k < 1f;
        ring.enabled = show;
        if (!show) return;
        float e = 1f - Mathf.Pow(1f - k, 3f);
        ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 2.3f, e);
        ring.color = new Color(OnePlusColor.r, OnePlusColor.g, OnePlusColor.b, 0.9f * (1f - k) * (1f - k));
    }

    // 기본 레인 그룹은 GameManager 가 알파를 올리므로 매 프레임 마지막에 다시 0 으로
    void LateUpdate()
    {
        if (gm != null && gm.laneGroup != null) gm.laneGroup.alpha = 0f;
    }

    private void UpdateGlows(float dt, float vis)
    {
        for (int i = 0; i < Lanes.Max; i++)
        {
            if (glows[i] == null) continue;
            float target = held[i] ? 0.85f : (pressed[i] ? 0.6f : 0f);
            glowLevel[i] = target > glowLevel[i] ? target : Mathf.MoveTowards(glowLevel[i], target, dt / 0.15f);
            Color c = LaneColor(i);
            glows[i].color = new Color(c.r, c.g, c.b, (IsCircle ? 0.42f : 0.28f) * glowLevel[i] * vis);
            glows[i].enabled = glowLevel[i] > 0.001f && vis > 0.001f;

            // 키 자리: 평소엔 옅은 흰 테두리, 누르면 레인 색으로 밝아지고 칠 때 살짝 커졌다 돌아옴
            SpriteRenderer r = receptors[i];
            if (r == null) continue;
            receptorPop[i] = Mathf.MoveTowards(receptorPop[i], 0f, dt / 0.12f);
            float lit = Mathf.Max(glowLevel[i], receptorPop[i]);
            Color rc = Color.Lerp(new Color(1f, 1f, 1f, IsCircle ? 0.5f : 0.16f), new Color(c.r, c.g, c.b, 0.85f), lit);   // CIRCLE: 판정선 대신이라 더 또렷하게
            r.color = new Color(rc.r, rc.g, rc.b, rc.a * vis * LaneShow(i));
            float pop = 1f + 0.06f * receptorPop[i];
            Vector2 want = ReceptorSize(pop);
            if (r.size != want) r.size = want;                                        // 바뀔 때만 (메시 다시 만들기 줄임)
            // 설정 > GAMEPLAY > KEY MARKERS (CIRCLE 은 키 자리가 곧 판정선이라 항상 보임, SIMPLE 은 설정과 관계없이 숨김)
            bool simpleOnly = !IsCircle && !IsDiverse;
            r.enabled = vis > 0.001f && (IsCircle || (!simpleOnly && SettingsStore.Current.ShowKeyMarkers));
        }
    }

    // 판정선의 키 자리 크기 (CIRCLE: 원 노트보다 살짝 큰 원)
    private Vector2 ReceptorSize(float pop)
    {
        if (IsCircle) { float d = CircleSize * 1.04f * pop; return new Vector2(d, d); }
        return new Vector2(laneWidth * 0.90f * pop, CurNoteHeight * 1.15f * pop);
    }

    // 노트 모양 이펙트 크기: 보통은 (레인 폭 × wMul, 노트 두께 × hMul), CIRCLE 은 원 지름 × cMul 의 정사각형
    private Vector2 Shape(float wMul, float hMul, float cMul)
    {
        if (IsCircle) { float d = CircleSize * cMul; return new Vector2(d, d); }
        return new Vector2(burstLW * wMul, CurNoteHeight * hMul);
    }

    private void UpdateBursts(float dt, float vis)
    {
        float sy = NoteScaleY;
        float flat = IsCircle ? 2.0f : 1f;   // 둥근 빛 세로 배율 (CIRCLE: 납작하지 않고 둥글게)
        foreach (var b in bursts)
        {
            if (b.t >= 1f) continue;
            burstLW = laneWidth * b.wf;   // 1/4 칸 노트는 그 폭만큼 작은 이펙트
            b.t = Mathf.Min(1f, b.t + dt / (b.mini ? 0.28f : BurstSeconds));
            if (b.mini)
            {
                // 홀드 맥박: 노트 테두리 모양이 살짝 퍼지고 둥근 빛이 옅게
                float tm = b.t, em = 1f - (1f - tm) * (1f - tm);
                Color mc = Color.Lerp(Color.white, b.c, 0.5f);
                b.wave.size = Shape(Mathf.Lerp(0.95f, 1.05f, em), Mathf.Lerp(1.2f, 1.9f, em), Mathf.Lerp(1.0f, 1.3f, em));
                b.wave.transform.localScale = new Vector3(1f, sy, 1f);
                b.wave.color = new Color(mc.r, mc.g, mc.b, 0.55f * (1f - tm) * vis);
                Vector2 gsm = NoteArt.GlowSprite.bounds.size;
                float rwm = burstLW * Mathf.Lerp(0.6f, 0.95f, em);
                b.ring.transform.localScale = new Vector3(rwm / gsm.x, rwm * 0.4f * flat * sy / gsm.y, 1f);
                b.ring.color = new Color(b.c.r, b.c.g, b.c.b, 0.35f * (1f - tm) * vis);
                if (b.t >= 1f) b.ring.enabled = b.wave.enabled = false;
                continue;
            }
            float t = b.t, e = 1f - (1f - t) * (1f - t);           // 빠르게 시작해서 천천히 끝남
            Color c = b.c;
            Color w2c = Color.Lerp(Color.white, c, Mathf.Clamp01(t * 2.2f));

            // 노트 모양 섬광 (앞쪽 55% 동안)
            float tc = Mathf.Clamp01(t / 0.55f), ec = 1f - (1f - tc) * (1f - tc);
            b.core.size = IsCircle ? Shape(0f, 0f, CirclePad * Mathf.Lerp(1.0f, 1.25f, ec) * (0.75f + 0.25f * b.power))
                                   : new Vector2(burstLW * Mathf.Lerp(0.95f, 1.03f, ec), CurNoteHeight * Mathf.Lerp(1.0f, 2.1f, ec) * b.power);
            b.core.transform.localScale = new Vector3(1f, sy, 1f);
            b.core.color = new Color(w2c.r, w2c.g, w2c.b, 0.95f * (1f - tc) * vis);

            // 둥근 빛
            Vector2 gs = NoteArt.GlowSprite.bounds.size;
            float rw = burstLW * Mathf.Lerp(0.55f, 1.12f, e) * (0.8f + 0.2f * b.power);
            b.ring.transform.localScale = new Vector3(rw / gs.x, rw * 0.42f * flat * sy / gs.y, 1f);
            Color rc2 = Color.Lerp(Color.white, c, Mathf.Clamp01(t * 1.5f));
            b.ring.color = new Color(rc2.r, rc2.g, rc2.b, 0.95f * (1f - t) * (1f - t) * vis);

            // 긴 빛줄기: 판정선을 따라 레인 여러 칸 너비로 아주 가늘게 뻗음 (렌즈 플레어)
            if (b.streak.enabled)
            {
                Vector2 ksz = NoteArt.HBellSprite.bounds.size;
                float tk = Mathf.Clamp01(t / 0.55f), ek = 1f - Mathf.Pow(1f - tk, 3f);
                float kw = burstLW * Mathf.Lerp(1.4f, b.perfect ? 5.0f : 4.0f, ek) * b.power;
                b.streak.transform.localScale = new Vector3(kw / ksz.x, 0.05f * sy * (1f - 0.5f * tk) / ksz.y, 1f);
                Color kc = Color.Lerp(Color.white, c, tk * 0.6f);
                b.streak.color = new Color(kc.r, kc.g, kc.b, 0.9f * Mathf.Pow(1f - tk, 1.4f) * vis);
            }

            // 넓은 빛무리: 레인 밖까지 크게 번쩍 번졌다가 빠르게 사라짐
            if (b.halo.enabled)
            {
                float hw2 = burstLW * Mathf.Lerp(1.2f, b.perfect ? 3.2f : 2.6f, e) * (0.8f + 0.2f * b.power);
                b.halo.transform.localScale = new Vector3(hw2 / gs.x, hw2 * (IsCircle ? 0.85f : 0.55f) * sy / gs.y, 1f);
                b.halo.color = new Color(c.r, c.g, c.b, 0.65f * (1f - t) * (1f - t) * vis);
            }

            // 판정선 위 광원 (강화): 레인 폭 안에서 강하게 번쩍하고 조금 오래 남음
            float th = Mathf.Clamp01(t / 0.65f);
            float hw = burstLW * Mathf.Lerp(1.15f, 0.9f, th);
            b.hot.transform.localScale = new Vector3(hw / gs.x, hw * (IsCircle ? 0.95f : 0.62f) * sy / gs.y, 1f);
            Color hc = Color.Lerp(Color.white, c, th * 0.55f);
            b.hot.color = new Color(hc.r, hc.g, hc.b, Mathf.Pow(1f - th, 1.4f) * vis);
            //  가운데 흰 심: 판정선 위에 아주 밝은 가로 빛 (레인 폭 정도)
            Vector2 hsz = NoteArt.HBellSprite.bounds.size;
            float tcw = Mathf.Clamp01(t / 0.5f);
            b.hotCore.transform.localScale = new Vector3(burstLW * Mathf.Lerp(1.25f, 1.0f, tcw) / hsz.x, 0.16f * sy * (1f - 0.4f * tcw) / hsz.y, 1f);
            b.hotCore.color = new Color(1f, 1f, 1f, (1f - tcw) * (1f - tcw) * vis);

            // 충격파: 노트 테두리 모양이 바깥으로 퍼짐
            b.wave.size = IsCircle ? Shape(0f, 0f, Mathf.Lerp(1.0f, 1.6f, e) * (0.8f + 0.2f * b.power))
                                   : new Vector2(burstLW * Mathf.Lerp(0.92f, 1.10f, e), CurNoteHeight * Mathf.Lerp(1.1f, 2.4f, e) * b.power);
            b.wave.transform.localScale = new Vector3(1f, sy, 1f);
            b.wave.color = new Color(w2c.r, w2c.g, w2c.b, 0.85f * Mathf.Pow(1f - t, 1.5f) * vis);

            // 두 번째 충격파: 조금 늦게, 더 크게 퍼짐
            float t2 = Mathf.Clamp01((t - 0.18f) / 0.82f), e2 = 1f - (1f - t2) * (1f - t2);
            b.wave2.size = IsCircle ? Shape(0f, 0f, Mathf.Lerp(1.0f, 2.1f, e2) * (0.8f + 0.2f * b.power))
                                    : new Vector2(burstLW * Mathf.Lerp(0.95f, 1.20f, e2), CurNoteHeight * Mathf.Lerp(1.2f, 3.0f, e2) * b.power);
            b.wave2.transform.localScale = new Vector3(1f, sy, 1f);
            b.wave2.color = new Color(c.r, c.g, c.b, (t < 0.18f ? 0f : 0.55f * (1f - t2)) * vis);

            // 가로 섬광 줄기: 판정선을 따라 좌우로 길게 번쩍 (빠르게 사라짐)
            Vector2 fsz = NoteArt.HBellSprite.bounds.size;
            float tf = Mathf.Clamp01(t / 0.75f);
            float fw = burstLW * Mathf.Lerp(1.1f, 1.7f, 1f - (1f - tf) * (1f - tf)) * b.power;
            b.flare.transform.localScale = new Vector3(fw / fsz.x, 0.13f * sy * (1f - 0.4f * tf) / fsz.y, 1f);
            b.flare.color = new Color(w2c.r, w2c.g, w2c.b, Mathf.Pow(1f - tf, 1.5f) * vis);

            // PERFECT: 가운데 4갈래 반짝임 (돌면서 커졌다 작아짐)
            if (b.perfect)
            {
                Vector2 ssz = NoteArt.StarSprite.bounds.size;
                float ts = Mathf.Clamp01(t / 0.7f);
                float grow = Mathf.Sin(ts * Mathf.PI);                       // 0 → 1 → 0
                float sw = burstLW * (0.55f + 0.85f * grow);                // 더 크게 빛남
                b.star.transform.localScale = new Vector3(sw / ssz.x, sw * sy / ssz.y, 1f);
                b.star.transform.rotation = Quaternion.Euler(0f, 0f, 45f * ts);
                b.star.color = new Color(1f, 1f, 1f, 0.95f * (1f - ts) * vis);
            }

            if (b.drag) UpdateDragBurst(b, t, sy, vis);

            if (b.t >= 1f)
            {
                b.core.enabled = b.ring.enabled = b.wave.enabled = false;
                b.wave2.enabled = b.flare.enabled = b.star.enabled = b.hot.enabled = b.hotCore.enabled = false;
                b.gem.enabled = b.zip.enabled = b.halo.enabled = b.streak.enabled = false;
                for (int k = 0; k < DragShards; k++) b.shards[k].enabled = false;
                b.drag = false;
            }
        }
    }

    // 드래그 노트 히트 (심플 · 고급)
    //  ① 빛 칼날: 판정선 위 아주 가는 흰 선이 가운데에서 양옆으로 빠르게 뻗고, 연노랑으로 물들며 가늘어짐
    //  ② 빛무리: 판정선을 따라 납작하게 번지는 연노랑 빛 (부드럽게 숨 쉬듯 사라짐)
    //  ③ 잔상: 얇은 드래그 노트 모양이 위로 미끄러지며 길어지고 옅어짐 — 두 번째 잔상은 조금 늦게, 더 옅게
    private void UpdateDragBurst(Burst b, float t, float sy, float vis)
    {
        Color c = NoteArt.DragColor;
        float y0 = gm.hitLineY;

        // ① 빛 칼날 (앞쪽 40%)
        float tz = Mathf.Clamp01(t / 0.4f), ez = 1f - Mathf.Pow(1f - tz, 4f);
        Vector2 zsz = NoteArt.HBellSprite.bounds.size;
        b.zip.transform.position = new Vector3(b.x, y0, 0f);
        b.zip.transform.localScale = new Vector3(burstLW * Mathf.Lerp(0.7f, 2.3f, ez) / zsz.x, 0.034f * sy * (1f - 0.6f * tz) / zsz.y, 1f);
        Color zc = Color.Lerp(Color.white, c, tz);
        b.zip.color = new Color(zc.r, zc.g, zc.b, Mathf.Pow(1f - tz, 1.3f) * vis);

        // ② 빛무리 (전체 시간, 납작하게)
        SpriteRenderer halo = b.shards[0];
        if (halo.enabled)
        {
            Vector2 gsz = NoteArt.GlowSprite.bounds.size;
            float th = t, eh = 1f - (1f - th) * (1f - th);
            float hw = burstLW * Mathf.Lerp(0.9f, 1.7f, eh);
            halo.transform.position = new Vector3(b.x, y0, 0f);
            halo.transform.localScale = new Vector3(hw / gsz.x, hw * 0.28f * sy / gsz.y, 1f);
            halo.color = new Color(c.r, c.g, c.b, 0.55f * (1f - th) * (1f - th) * vis);
        }

        // ③ 잔상 2개
        DragGhost(b.gem, b.x, y0, t, 0f, 1f, sy, vis, c);
        if (b.shards[1].enabled) DragGhost(b.shards[1], b.x, y0, t, 0.12f, 0.55f, sy, vis, c);
    }

    // 얇은 노트 모양 잔상: delay 만큼 늦게 출발, 위로 0.55 유닛 미끄러지며 세로로 늘어나고 옅어짐
    private void DragGhost(SpriteRenderer sr, float x, float y0, float t, float delay, float strength, float sy, float vis, Color c)
    {
        float tk = Mathf.Clamp01((t - delay) / (1f - delay));
        float ek = 1f - Mathf.Pow(1f - tk, 3f);
        float h = NoteHeight * NoteArt.DragHeightRatio;
        float pad = NoteArt.NoteGlowPad * 2f;
        sr.transform.position = new Vector3(x, y0 + 0.55f * ek * sy, 0f);
        sr.transform.localScale = new Vector3(1f, sy, 1f);
        if (IsCircle)
        {
            // CIRCLE: 빛 고리가 위로 미끄러지며 커지고 옅어짐
            float dd = CircleSize * CircleDragRatio * CirclePad * Mathf.Lerp(1f, 1.45f, ek);
            sr.size = new Vector2(dd, dd);
        }
        else sr.size = new Vector2(burstLW * 0.95f * Mathf.Lerp(1f, 0.82f, ek) + pad, h * Mathf.Lerp(1f, 1.9f, ek) + pad);
        Color gc = Color.Lerp(Color.white, c, 0.35f + 0.65f * tk);
        sr.color = new Color(gc.r, gc.g, gc.b, (t < delay ? 0f : 0.85f * strength) * Mathf.Pow(1f - tk, 1.8f) * vis);
    }

    private void UpdateHudValues(float dt)
    {
        // 콤보
        int combo = gm.DisplayCombo;   // UNKNOWN UI 면 무작위 값
        if (combo != lastCombo)
        {
            if (combo > lastCombo && combo > 0) comboPunch = 1f;
            lastCombo = combo;
            if (combo > 0) comboText.SetText("{0}", combo); else comboText.text = "";   // SetText: 콤보마다 문자열을 만들지 않음
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
        int keyMode = gm.KeysAt(gm.currentPlayTime);
        bool rewrite = keyMode != lastKeyMode;
        lastKeyMode = keyMode;
        for (int i = 0; i < gm.laneXPositions.Length && i < Lanes.Max; i++)
        {
            if (keyTexts[i] == null) continue;
            if (rewrite) keyTexts[i].text = SettingsStore.KeyDisplayName(gm.DisplayKeyForLane(i));
            keyTexts[i].alpha = 0.35f * LaneShow(i);
        }
        for (int i = 0; i < gm.laneXPositions.Length && i < Lanes.Max; i++)
            if (keyTexts[i] != null)
            SetAtWorld(keyTexts[i].rectTransform, cam, new Vector3(gm.laneXPositions[i], y - (IsCircle ? CircleSize * 0.5f + 0.3f : 0.45f), 0f));


        // LIFE 바: 판정선 높이부터 화면 위쪽(여백 LifeBarTopMargin)까지 길게, 레인 오른쪽 바깥
        if (lifeRoot != null && lifeRoot.gameObject.activeSelf)
        {
            float gx = VisRight + LifeBarGap;   // 6키 확장: 보이는 레인 끝
            float topY = cam.orthographic ? cam.transform.position.y + cam.orthographicSize - LifeBarTopMargin : y + 7f;
            if (IsDiverse) topY = y + 3.2f; // 원근: 먼 쪽은 화면에서 짧아지므로 가까운 구간까지만
            topY = Mathf.Max(y + 1f, topY);
            Vector2 bottom, top;
            if (WorldToLocal(cam, new Vector3(gx, y, 0f), out bottom) && WorldToLocal(cam, new Vector3(gx, topY, 0f), out top))
            {
                lifeRoot.anchorMin = lifeRoot.anchorMax = new Vector2(0.5f, 0.5f);
                lifeRoot.pivot = new Vector2(0.5f, 0f);
                lifeRoot.anchoredPosition = bottom;
                // REVERSE(위아래 반전): 판정선이 위라서 막대가 아래로 뻗음 → 막대를 뒤집어 판정선 쪽에서부터 채워지게
                float span = top.y - bottom.y;
                lifeRoot.localRotation = span < 0f ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
                lifeRoot.sizeDelta = new Vector2(LifeBarWidth, Mathf.Max(40f, Mathf.Abs(span)));
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
    // 레인 단위 위치 (0 = 0번 레인 왼쪽 끝, 1칸 = 1) → 월드 x
    private float LaneUnitToX(float u)
    {
        float[] xs = gm.laneXPositions;
        int k = Mathf.Clamp(Mathf.FloorToInt(u), 0, xs.Length - 1);
        return xs[k] + (u - k - 0.5f) * laneWidth;
    }

    private void UpdateHold(float dt, float vis)
    {
        holdClock += dt;

        // [개선] 진행 중인 롱노트 · 슬라이드의 '지금' 폭과 가운데에 맞춤 (1/4 칸 ~ 4칸)
        //  여러 레인을 덮으면 맨 왼쪽 눌린 레인 하나가 전체 폭으로 그림 (겹쳐 그리지 않게)
        for (int i = 0; i < Lanes.Max; i++)
        {
            holdKey[i] = null;
            if (!held[i] || gm.laneXPositions == null || i >= gm.laneXPositions.Length) continue;
            holdKey[i] = gm.HoldSpanOnLane(i, out float l, out float r);
            if (holdKey[i] == null) { l = i; r = i + 1f; }     // 못 찾으면 레인 한 칸
            holdL[i] = l; holdR[i] = r;
        }
        for (int i = 0; i < Lanes.Max; i++)
        {
            if (!held[i]) continue;
            bool dup = false;
            if (holdKey[i] != null) for (int j = 0; j < i; j++) if (held[j] && holdKey[j] == holdKey[i]) { dup = true; break; }
            holdHidden[i] = dup;
            if (dup || gm.laneXPositions == null || i >= gm.laneXPositions.Length) continue;
            float w = Mathf.Max(0.02f, holdR[i] - holdL[i]);   // 끝이 한 점으로 모이는 롱노트도 그대로 줄어듦
            holdWidthLanes[i] = w;
            holdCenterX[i] = LaneUnitToX((holdL[i] + holdR[i]) * 0.5f);
            holdOffX[i] = holdCenterX[i] - gm.laneXPositions[i];
            // 몸통 폭과 같게 (Slidebody 의 안쪽 여백 규칙과 같음)
            float ins = w < 0.99f ? laneWidth * 0.02f : (IsCircle ? (laneWidth - BodySpan(1)) * 0.5f : laneWidth * 0.02f);
            holdSpan[i] = Mathf.Max(laneWidth * 0.02f, w * laneWidth - 2f * Mathf.Min(ins, w * laneWidth * 0.5f));
        }

        for (int i = 0; i < Lanes.Max; i++)
        {
            SpriteRenderer f = holdFlares[i];
            if (f == null) continue;
            if (!holdBaseSet[i]) { holdBaseX[i] = f.transform.position.x; holdBaseSet[i] = true; }
            holdLevel[i] = Mathf.MoveTowards(holdLevel[i], held[i] ? 1f : 0f, dt / 0.08f);

            bool on = holdLevel[i] > 0.001f && vis > 0.001f && !holdHidden[i];
            f.enabled = on;
            if (on)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(holdClock * 18f + i * 1.3f);
                Color c = Color.Lerp(LaneColor(i), Color.white, 0.45f);
                f.color = new Color(c.r, c.g, c.b, 0.75f * holdLevel[i] * pulse * vis);
                float pad = NoteArt.NoteGlowPad * 2f;
                float span = holdSpan[i];
                bool oneLane = Mathf.Abs(holdWidthLanes[i] - 1f) < 0.01f;
                if (IsCircle)
                {
                    float d = CircleSize * CirclePad * (1.02f + 0.08f * pulse);
                    f.size = oneLane ? new Vector2(d, d) : new Vector2(Mathf.Max(d * 0.5f, span + (d - CircleSize)), d);
                }
                else f.size = new Vector2((oneLane ? laneWidth * 0.98f : span + laneWidth * 0.02f) + pad, 0.16f + 0.08f * pulse + pad);
                f.transform.localScale = new Vector3(1f, NoteScaleY, 1f);
                Vector3 p = f.transform.position;
                f.transform.position = new Vector3(holdBaseX[i] + holdOffX[i], p.y, p.z);
            }

            // 누르고 있는 동안 일정 간격으로 노트 테두리 모양 맥박이 퍼짐 — 판정선 노트 모양과 같은 폭 · 위치
            if (held[i] && !holdHidden[i] && vis > 0.001f && SettingsStore.Current.EffectQuality > 0)
            {
                holdPulseTimer[i] -= dt;
                if (holdPulseTimer[i] <= 0f)
                {
                    holdPulseTimer[i] = 0.16f;
                    Hit(i, LaneColor(i), 0.5f, true, false, holdCenterX[i], holdWidthLanes[i]);
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

        for (int i = 0; i < Lanes.Max; i++)
        {
            SpriteRenderer sr = redWarnGlows[i];
            if (sr == null) continue;
            redWarnTime[i] += dt;
            float a = WarnPulse(redWarnTime[i]) * vis * LaneShow(i);
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
        for (int l = 0; l < Lanes.Max; l++) redWarnTime[l] = 0f;
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
        // ONE+ 보호 중: 실제 LIFE 대신 노란 타이머 (100 까지 차오름 → 5초에 걸쳐 0)
        //  끝나면 0 에서 실제 LIFE 까지 다시 차오르며 원래 표시로 돌아감
        float shieldLife = ShieldDisplayLife();
        shieldShown = shieldLife >= 0f;
        if (shieldShown) shownLife = shieldLife;
        else shownLife = Mathf.MoveTowards(shownLife, target, dt * 120f);
        float frac = Mathf.Clamp01(shownLife / 100f);
        lifeFillRt.anchorMax = new Vector2(1f, frac);

        // 게이지 색 (모든 스킨 공통 HARD 게이지: 100 에서 시작해 0 으로 내려감)
        //  흰색 → 50 아래 노랑 → 25 아래 빨강 (경계에서 부드럽게 섞임), 20 아래 깜빡임
        //  TEMPEST: 보라 → 25 아래 빨강
        Color c; float a = 1f;
        if (gm.Gauge == GaugeKind.Tempest)
            c = target > 25f ? new Color(0.80f, 0.62f, 1f) : LifeLow;
        else
            c = target > 50f ? Color.Lerp(LifeMid, LifeHigh, Mathf.Clamp01((target - 50f) / 6f))
              : Color.Lerp(LifeLow, LifeMid, Mathf.Clamp01((target - 25f) / 6f));
        if (target <= 20f && target > 0f) a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)); // 위험할 때 깜빡임
        // GAME SETTING 무한 LIFE: 밝은 푸른색 계열이 천천히 오가는 색 + 흐르는 빛
        bool infinite = gm.LifeInfinite;   // GAME SETTING · IMMORTAL
        if (infinite)
        {
            // 채움 색만 그라데이션 (lifeGrad 그림) — 그 밑 기본 채움과 바깥 빛은 하늘색
            c = new Color(0.35f, 0.75f, 1f);
            a = 1f;
        }
        lifeFill.color = new Color(c.r, c.g, c.b, a);
        lifeFill.enabled = shownLife > 0.05f;
        if (lifeCapShade != null)
        {
            float cap = Mathf.Clamp01((float)gm.LifeCap / 100f);
            bool strict = cap < 0.999f && !infinite;
            lifeCapShade.enabled = lifeCapLine.enabled = strict;
            if (strict)
            {
                RectTransform sr = lifeCapShade.rectTransform;
                sr.anchorMin = new Vector2(0f, cap); sr.anchorMax = new Vector2(1f, 1f);
                sr.offsetMin = new Vector2(3f, 0f); sr.offsetMax = new Vector2(-3f, -3f);
                RectTransform lr = lifeCapLine.rectTransform;
                lr.anchorMin = new Vector2(0f, cap); lr.anchorMax = new Vector2(1f, cap);
                lr.offsetMin = new Vector2(-2f, -1f); lr.offsetMax = new Vector2(2f, 1f);
            }
        }
        if (lifeFloorShade != null)
        {
            float floor = Mathf.Clamp01((float)gm.LifeFloor / 100f);
            bool floored = floor > 0.001f && !infinite;
            lifeFloorShade.enabled = lifeFloorLine.enabled = floored;
            if (floored)
            {
                RectTransform fr = lifeFloorShade.rectTransform;
                fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(1f, floor);
                fr.offsetMin = new Vector2(3f, 3f); fr.offsetMax = new Vector2(-3f, 0f);
                RectTransform fl = lifeFloorLine.rectTransform;
                fl.anchorMin = new Vector2(0f, floor); fl.anchorMax = new Vector2(1f, floor);
                fl.offsetMin = new Vector2(-2f, -1f); fl.offsetMax = new Vector2(2f, 1f);
            }
        }
        if (shieldShown) { c = OnePlusColor; a = 1f; lifeFill.color = c; }   // 바깥 빛 · 끝선도 노랑
        if (lifeShieldGrad != null) lifeShieldGrad.enabled = shieldShown && lifeFill.enabled;
        if (lifeGrad != null) lifeGrad.enabled = infinite && lifeFill.enabled;
        // 뒤쪽 빛 · 끝선: 채움 높이를 따라감 (색은 채움과 같은 계열)
        if (lifeGlowImg != null)
        {
            lifeGlowImg.rectTransform.anchorMax = new Vector2(1f, frac);
            lifeGlowImg.enabled = lifeFill.enabled;
            lifeGlowImg.color = new Color(c.r, c.g, c.b, 0.38f * a);
        }
        if (lifeHeadLine != null)
        {
            RectTransform hr = lifeHeadLine.rectTransform;
            float inner = Mathf.Max(0f, lifeRoot.rect.height - 2f * LifeFillInset);
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f);
            hr.pivot = new Vector2(0.5f, 0.5f);
            hr.sizeDelta = new Vector2(LifeBarWidth + 8f, 2f);
            hr.anchoredPosition = new Vector2(0f, LifeFillInset + inner * frac - 1f);
            lifeHeadLine.enabled = lifeFill.enabled && frac < 0.995f;   // 가득 찼을 때는 끝선 없음
            lifeHeadLine.color = new Color(1f, 1f, 1f, 0.9f * a);
        }
        if (lifeFlow != null)
        {
            lifeFlow.enabled = false;   // (흐르는 빛 없음)
            if (lifeFlow.enabled)
            {
                float h = lifeFillRt.rect.height;
                float k = Mathf.Repeat(Time.unscaledTime * 0.45f, 1f);
                lifeFlow.rectTransform.sizeDelta = new Vector2(0f, h * 0.25f);
                lifeFlow.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(-h * 0.15f, h * 1.15f, k));
                lifeFlow.color = new Color(0.92f, 0.99f, 1f, 0.5f);
            }
        }
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