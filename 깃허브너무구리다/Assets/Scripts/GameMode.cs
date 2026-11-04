using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 게임 모드 (곡 선택창 MODE 바 / M 키 모드 설정창에서 고름, 한 판에 최대 3개)
//
//  [기본 모드]        [배율]  [내용]      (모드 설정창 탭은 성격별: JUDGE · RULE · CHART, 아래 CategoryNames)
//  MIRROR             120%   채보가 좌우로 반전
//  RANDOM             165%   레인이 무작위로 섞임 (한 판 동안 같은 섞임)
//  SHADOW             175%   판정선에 가까워질수록 노트가 투명해짐
//  EASY                75%   판정이 PERFECT 와 MISS 만 (BAD 범위 안이면 모두 PERFECT)
//  HARD               135%   판정 범위 1/2
//  MAXIMUM            180%   판정 범위 1/3, GREAT 미만(GOOD · BAD · MISS)이면 LIFE 감소
//  ULTIMATE           210%   판정 범위 1/4, PERFECT 미만(GREAT)이면 LIFE 감소, GREAT 미만이면 크게 감소
//  IMMORTAL            65%   LIFE 가 줄지 않음 (절대 TRACK CRASH 되지 않음)
//  UNKNOWN UI         115%   화면의 점수 · 콤보 · 판정 글자가 무작위
//  PERFECTIONIST      200%   PERFECT 미만이 나오면 즉시 TRACK CRASH
//  PERFECTIONIST+     230%   JUST 미만이 나오면 즉시 TRACK CRASH
//  ONE CHANCE         155%   MISS 가 한 번이라도 나오면 즉시 TRACK CRASH
//  COMBO BREAK        180%   콤보가 끊기면 3초 동안 점수를 얻지 못함
//  STRICT             140%   곡이 진행될수록 LIFE 최대치가 100 → 50 으로 줄어듦
//
//  [CASUAL] 뉴비용 예능 모드
//  LAZY               120%   놓친 노트가 15% 확률로 자동 JUST PERFECT (롱노트는 끝까지 자동으로 눌림)
//  MERCY              110%   BAD · MISS 가 50% 확률로 GREAT 이상으로 바뀜 (확률은 화면에 표시하지 않음)
//                            바뀐 판정: GREAT 50% · PERFECT 35% · JUST PERFECT 15%
//  CLUTCH             110%   LIFE 가 낮을수록 판정 범위가 넓어짐 (LIFE 0 에서 2.5배)
//
//  [GIMMICK] 기믹 모드
//  REVERSE            150%   판정선이 위쪽, 노트가 아래에서 위로 올라감
//  ONE+               100%   TRACK CRASH 때 15% 확률로 LIFE 30 으로 부활
//  FAKE               135%   치면 안 되는 가짜 노트가 섞여 나옴. 치면 지금 LIFE 의 50% 감소
//
//  점수 배율: 고른 모드의 배율을 모두 곱함 (곱연산). 최대 350% (= 3,500,000 점)
//  함께 쓸 수 없는 모드 (같은 묶음은 하나만, 새로 고르면 이전 것과 바뀜)
//   · MIRROR / RANDOM
//   · EASY / HARD / MAXIMUM / ULTIMATE
//   · PERFECTIONIST / PERFECTIONIST+
//   · IMMORTAL 과 (PERFECTIONIST · PERFECTIONIST+ · ONE CHANCE · STRICT · CLUTCH · ONE+)  ← LIFE 가 줄지 않으면 의미가 없는 모드
//  AUTO PLAY 는 모드가 아님 — AUTO PLAY 를 켜면 모드 설정창이 열리지 않고, 고른 모드도 적용되지 않음 (선택은 남아 있음)
//  GAME SETTING(5_Test) 에서는 모든 모드가 꺼진 채로 진행
// ============================================================
public enum GameMode
{
    Mirror, Random, Shadow, Easy, Hard, Maximum, Ultimate,
    Immortal, UnknownUi, Perfectionist, PerfectionistPlus, OneChance, ComboBreak, Strict,
    Lazy, Mercy, Clutch,
    Reverse, OnePlus, Fake
}

public class GameModeInfo
{
    public GameMode Mode;
    public string Name;
    public string Desc;         // 한국어 설명 (모드 설정창 카드)
    public double Multiplier;   // 점수 배율 (1 = 100%)
    public int Group;           // 0 = 묶음 없음, 같은 값끼리는 하나만
    public int Category;        // 0 JUDGE  1 RULE  2 CHART (모드 설정창 탭)
    public Color Color;
}

public static class GameModes
{
    public const int MaxSelected = 3;
    public const double MaxMultiplier = 3.5;          // 최대 350%
    public const int MaxScoreWithModes = 3500000;
    // 모드 설정창 탭 (성격별)
    //  JUDGE   판정             EASY · HARD · MAXIMUM · ULTIMATE · LAZY · MERCY · CLUTCH
    //  RULE    LIFE · 즉사 · 점수  IMMORTAL · PERFECTIONIST · PERFECTIONIST+ · ONE CHANCE · COMBO BREAK · STRICT · ONE+
    //  CHART   채보 · 화면       MIRROR · RANDOM · SHADOW · UNKNOWN UI · REVERSE · FAKE
    public static readonly string[] CategoryNames = { "JUDGE", "RULE", "CHART" };

    // 판정 · 연출 수치
    public const double ComboBreakLockSeconds = 3.0;  // COMBO BREAK: 콤보가 끊긴 뒤 점수를 못 얻는 시간
    public const double StrictMinCap = 50.0;          // STRICT: 곡 끝에서의 LIFE 최대치
    public const float ShadowFadeStart = 4.3f;        // SHADOW: 판정선에서 이 거리(월드 단위)부터 투명해지기 시작
    public const float ShadowFadeEnd = 1.7f;          //          이 거리에서 완전히 사라짐
    public const double LazyChance = 0.15;            // LAZY: 놓친 노트가 자동 JUST PERFECT 될 확률
    public const double MercyChance = 0.50;           // MERCY: BAD · MISS 가 바뀔 확률 (설명에는 '일정 확률'로만 표기)
    public const double MercyGreat = 0.50;            //        바뀐 판정 확률표: GREAT 50%
    public const double MercyPerfect = 0.35;          //                         PERFECT 35% (JUST 아닌 PERFECT)
    public const double MercyJust = 0.15;             //                         JUST PERFECT 15%
    public const double ClutchMaxScale = 2.5;         // CLUTCH: LIFE 0 일 때 판정 범위 배율
    public const double OnePlusChance = 0.15;         // ONE+: TRACK CRASH 때 부활 확률
    public const double OnePlusReviveLife = 30.0;     //       부활 후 LIFE
    public const double FakeChance = 0.15;            // FAKE: 노트가 있는 시각마다 가짜 노트가 하나 생길 확률
    public const double FakeGap = 0.25;               //       가짜 노트는 같은 레인의 진짜 노트 · 롱노트와 이 시간(초) 이상 떨어짐
    public const double FakePenalty = 0.5;            //       가짜 노트를 치면 지금 LIFE 의 이만큼 감소
    public static readonly Color FakeNoteColor = new Color(0.36f, 0.30f, 0.44f, 0.80f);   // 가짜 노트 색 (어두운 보라 회색)

    private static readonly Color CLane = new Color(0.42f, 0.86f, 0.95f);   // 배치 (MIRROR · RANDOM)
    private static readonly Color CVisual = new Color(0.72f, 0.62f, 1.00f); // 보이는 것 (SHADOW · UNKNOWN UI)
    private static readonly Color CEasy = new Color(0.55f, 0.92f, 0.62f);
    private static readonly Color CHard = new Color(1.00f, 0.62f, 0.30f);
    private static readonly Color CMax = new Color(1.00f, 0.42f, 0.30f);
    private static readonly Color CUlt = new Color(1.00f, 0.28f, 0.38f);
    private static readonly Color CLife = new Color(0.50f, 0.85f, 1.00f);   // IMMORTAL
    private static readonly Color CRisk = new Color(1.00f, 0.45f, 0.62f);   // 즉사 · 제약
    private static readonly Color CCasual = new Color(0.45f, 0.95f, 0.80f); // 예능
    private static readonly Color CGimmick = new Color(1.00f, 0.80f, 0.36f);// 기믹

    public static readonly GameModeInfo[] All =
    {
        new GameModeInfo { Mode = GameMode.Mirror, Name = "MIRROR", Multiplier = 1.20, Category = 2, Group = 1, Color = CLane,
            Desc = "채보가 좌우로 반전됩니다." },
        new GameModeInfo { Mode = GameMode.Random, Name = "RANDOM", Multiplier = 1.65, Category = 2, Group = 1, Color = CLane,
            Desc = "채보가 무작위로 배치되어 내려옵니다." },
        new GameModeInfo { Mode = GameMode.Shadow, Name = "SHADOW", Multiplier = 1.75, Category = 2, Group = 0, Color = CVisual,
            Desc = "채보가 판정선에 가까워질수록 투명해집니다." },
        new GameModeInfo { Mode = GameMode.Easy, Name = "EASY", Multiplier = 0.75, Category = 0, Group = 2, Color = CEasy,
            Desc = "판정이 PERFECT와 MISS만 존재합니다." },
        new GameModeInfo { Mode = GameMode.Hard, Name = "HARD", Multiplier = 1.35, Category = 0, Group = 2, Color = CHard,
            Desc = "노트 판정 범위가 기존 판정 범위의 절반이 됩니다." },
        new GameModeInfo { Mode = GameMode.Maximum, Name = "MAXIMUM", Multiplier = 1.80, Category = 0, Group = 2, Color = CMax,
            Desc = "노트 판정 범위가 기존 판정 범위의 1/3이 됩니다. GREAT 미만의 판정에서는 라이프가 감소합니다." },
        new GameModeInfo { Mode = GameMode.Ultimate, Name = "ULTIMATE", Multiplier = 2.10, Category = 0, Group = 2, Color = CUlt,
            Desc = "노트 판정 범위가 기존 판정 범위의 1/4이 됩니다. PERFECT 미만의 판정에서는 라이프가 감소하고, GREAT 미만에서는 큰 폭으로 감소합니다." },
        new GameModeInfo { Mode = GameMode.Immortal, Name = "IMMORTAL", Multiplier = 0.65, Category = 1, Group = 0, Color = CLife,
            Desc = "절대 죽지 않습니다." },
        new GameModeInfo { Mode = GameMode.UnknownUi, Name = "UNKNOWN UI", Multiplier = 1.15, Category = 2, Group = 0, Color = CVisual,
            Desc = "점수, 콤보, 판정이 랜덤하게 표시됩니다." },
        new GameModeInfo { Mode = GameMode.Perfectionist, Name = "PERFECTIONIST", Multiplier = 2.00, Category = 1, Group = 3, Color = CRisk,
            Desc = "PERFECT 미만의 판정이 발생할 시 즉시 TRACK CRASH됩니다." },
        new GameModeInfo { Mode = GameMode.PerfectionistPlus, Name = "PERFECTIONIST+", Multiplier = 2.30, Category = 1, Group = 3, Color = CRisk,
            Desc = "JUST 미만의 판정이 발생할 시 즉시 TRACK CRASH됩니다." },
        new GameModeInfo { Mode = GameMode.OneChance, Name = "ONE CHANCE", Multiplier = 1.55, Category = 1, Group = 0, Color = CRisk,
            Desc = "MISS가 한 번이라도 발생하면 즉시 게임이 종료됩니다." },
        new GameModeInfo { Mode = GameMode.ComboBreak, Name = "COMBO BREAK", Multiplier = 1.80, Category = 1, Group = 0, Color = CRisk,
            Desc = "콤보가 끊기면 일정 시간 동안 점수를 획득할 수 없습니다." },
        new GameModeInfo { Mode = GameMode.Strict, Name = "STRICT", Multiplier = 1.40, Category = 1, Group = 0, Color = CRisk,
            Desc = "곡이 진행됨에 따라 라이프의 최대 상한치가 감소합니다." },

        new GameModeInfo { Mode = GameMode.Lazy, Name = "LAZY", Multiplier = 1.20, Category = 0, Group = 0, Color = CCasual,
            Desc = "노트를 놓치면 낮은 확률(15%)로 JUST PERFECT 처리됩니다." },
        new GameModeInfo { Mode = GameMode.Mercy, Name = "MERCY", Multiplier = 1.10, Category = 0, Group = 0, Color = CCasual,
            Desc = "BAD 또는 MISS 판정이 났을 때, 일정 확률로 GREAT 이상의 판정으로 대체됩니다." },
        new GameModeInfo { Mode = GameMode.Clutch, Name = "CLUTCH", Multiplier = 1.10, Category = 0, Group = 0, Color = CCasual,
            Desc = "라이프가 낮아질수록 판정이 최대 2.5배 넓어집니다." },

        new GameModeInfo { Mode = GameMode.Reverse, Name = "REVERSE", Multiplier = 1.50, Category = 2, Group = 0, Color = CGimmick,
            Desc = "판정선이 위쪽으로 갑니다!" },
        new GameModeInfo { Mode = GameMode.OnePlus, Name = "ONE+", Multiplier = 1.00, Category = 1, Group = 0, Color = CGimmick,
            Desc = "TRACK CRASH 시 낮은 확률로 부활합니다." },
        new GameModeInfo { Mode = GameMode.Fake, Name = "FAKE", Multiplier = 1.35, Category = 2, Group = 0, Color = CGimmick,
            Desc = "실제로 입력하면 안 되는 가짜 노트가 등장합니다. 입력할 시 현재 라이프의 50%만큼 차감됩니다." },
    };

    public static GameModeInfo Info(GameMode m) { return All[(int)m]; }

    // =====================================================================
    //  선택 상태 (곡 선택창 ↔ 게임 ↔ 결과창 사이에 유지)
    // =====================================================================
    private static readonly List<GameMode> selected = new List<GameMode>();
    public static int Version { get; private set; }     // 바뀔 때마다 +1 (화면 갱신용)

    public static List<GameMode> Selected { get { return selected; } }
    public static bool IsOn(GameMode m) { return selected.Contains(m); }

    // AUTO PLAY 중에는 모드를 고를 수 없고, 고른 모드도 적용되지 않음
    public static bool Locked { get { return CoreManager.Instance != null && CoreManager.Instance.IsAutoPlay; } }

    public static bool Conflicts(GameMode a, GameMode b)
    {
        if (a == b) return false;
        GameModeInfo ia = Info(a), ib = Info(b);
        if (ia.Group != 0 && ia.Group == ib.Group) return true;
        return (a == GameMode.Immortal && NeedsLife(b)) || (b == GameMode.Immortal && NeedsLife(a));
    }
    // LIFE 가 줄어야 의미가 있는 모드 (IMMORTAL 과 같이 못 씀)
    private static bool NeedsLife(GameMode m)
    {
        return m == GameMode.Perfectionist || m == GameMode.PerfectionistPlus || m == GameMode.OneChance
            || m == GameMode.Strict || m == GameMode.Clutch || m == GameMode.OnePlus;
    }

    public enum ToggleResult { Added, Removed, Replaced, Full, Locked }

    // 켜기 / 끄기. 같이 쓸 수 없는 모드가 켜져 있으면 그 모드와 바뀜. 3개가 차 있으면 Full (아무것도 안 바뀜)
    public static ToggleResult Toggle(GameMode m, out string replacedName)
    {
        replacedName = null;
        if (Locked) return ToggleResult.Locked;
        if (selected.Contains(m))
        {
            selected.Remove(m);
            Version++;
            return ToggleResult.Removed;
        }
        var conflicts = selected.FindAll(x => Conflicts(x, m));
        if (selected.Count - conflicts.Count >= MaxSelected) return ToggleResult.Full;
        foreach (var c in conflicts)
        {
            selected.Remove(c);
            replacedName = replacedName == null ? Info(c).Name : replacedName + ", " + Info(c).Name;
        }
        selected.Add(m);
        Version++;
        return conflicts.Count > 0 ? ToggleResult.Replaced : ToggleResult.Added;
    }

    public static void Clear()
    {
        if (Locked) return;
        selected.Clear();
        Version++;
    }

    // 고른 모드들의 점수 배율 (곱, 최대 350%)
    public static double MultiplierOf(IList<GameMode> modes)
    {
        double k = 1.0;
        if (modes != null) foreach (var m in modes) k *= Info(m).Multiplier;
        return Math.Min(MaxMultiplier, k);
    }
    public static double Multiplier { get { return MultiplierOf(Selected); } }
    public static bool CapReached(IList<GameMode> modes)
    {
        double k = 1.0;
        if (modes != null) foreach (var m in modes) k *= Info(m).Multiplier;
        return k > MaxMultiplier + 1e-9;
    }

    public static string Percent(double k) { return Math.Round(k * 100.0).ToString("0") + "%"; }

    // =====================================================================
    //  이번 판에 적용되는 모드 (GameManager.Start 에서 한 번 정함 — 게임 중 선택을 바꿔도 이번 판은 그대로)
    // =====================================================================
    public static class Play
    {
        public static bool Mirror, Random, Shadow, Easy, Hard, Maximum, Ultimate, Immortal, UnknownUi,
                           Perfectionist, PerfectionistPlus, OneChance, ComboBreak, Strict,
                           Lazy, Mercy, Clutch, Reverse, OnePlus, Fake;
        public static double JudgeScale = 1.0;          // 판정 범위 배율 (HARD · MAXIMUM · ULTIMATE)
        public static double ScoreMultiplier = 1.0;
        public static GameMode[] Modes = new GameMode[0];
    }

    // testMode(GAME SETTING) · AUTO PLAY 이면 모드 없음
    public static void BeginPlay(bool testMode)
    {
        List<GameMode> list = testMode || Locked ? new List<GameMode>() : new List<GameMode>(selected);
        Play.Modes = list.ToArray();
        Play.Mirror = list.Contains(GameMode.Mirror);
        Play.Random = list.Contains(GameMode.Random);
        Play.Shadow = list.Contains(GameMode.Shadow);
        Play.Easy = list.Contains(GameMode.Easy);
        Play.Hard = list.Contains(GameMode.Hard);
        Play.Maximum = list.Contains(GameMode.Maximum);
        Play.Ultimate = list.Contains(GameMode.Ultimate);
        Play.Immortal = list.Contains(GameMode.Immortal);
        Play.UnknownUi = list.Contains(GameMode.UnknownUi);
        Play.Perfectionist = list.Contains(GameMode.Perfectionist);
        Play.PerfectionistPlus = list.Contains(GameMode.PerfectionistPlus);
        Play.OneChance = list.Contains(GameMode.OneChance);
        Play.ComboBreak = list.Contains(GameMode.ComboBreak);
        Play.Strict = list.Contains(GameMode.Strict);
        Play.Lazy = list.Contains(GameMode.Lazy);
        Play.Mercy = list.Contains(GameMode.Mercy);
        Play.Clutch = list.Contains(GameMode.Clutch);
        Play.Reverse = list.Contains(GameMode.Reverse);
        Play.OnePlus = list.Contains(GameMode.OnePlus);
        Play.Fake = list.Contains(GameMode.Fake);
        Play.JudgeScale = Play.Ultimate ? 0.25 : Play.Maximum ? 1.0 / 3.0 : Play.Hard ? 0.5 : 1.0;
        Play.ScoreMultiplier = MultiplierOf(list);
    }

    // CLUTCH: LIFE(0 ~ 100) → 판정 범위 배율 (100 이면 1배, 0 이면 2.5배, 그 사이는 직선)
    public static double ClutchScale(double life)
    {
        double k = 1.0 - Math.Max(0.0, Math.Min(100.0, life)) / 100.0;
        return 1.0 + (ClutchMaxScale - 1.0) * k;
    }

    // SHADOW: 판정선에서의 거리(월드 단위) → 투명도
    public static float ShadowAlpha(float distanceFromLine)
    {
        float k = Mathf.Clamp01((distanceFromLine - ShadowFadeEnd) / (ShadowFadeStart - ShadowFadeEnd));
        return k * k * (3f - 2f * k);
    }

    // =====================================================================
    //  아이콘 (코드로 그림, 흰색 그림 → Image.color 로 색칠)
    // =====================================================================
    private static readonly Dictionary<GameMode, Sprite> icons = new Dictionary<GameMode, Sprite>();
    private static Sprite badgeSprite;

    public static Sprite Icon(GameMode m)
    {
        Sprite s;
        if (icons.TryGetValue(m, out s) && s != null) return s;
        s = GameModeIconArt.Draw(m);
        icons[m] = s;
        return s;
    }

    // 둥근 사각형 바탕 (9-slice)
    public static Sprite BadgeSprite
    {
        get
        {
            if (badgeSprite == null) badgeSprite = GameModeIconArt.RoundRect(64, 16);
            return badgeSprite;
        }
    }

    // 속이 빈 둥근 사각형 (테두리 선만, 9-slice) — 모드 선택창 겉 테두리
    private static Sprite ringSprite;
    public static Sprite RingSprite
    {
        get
        {
            if (ringSprite == null) ringSprite = GameModeIconArt.RoundRing(64, 16, 1f);
            return ringSprite;
        }
    }

    // 아이콘 배지 하나: 어두운 둥근 바탕 + 모드 색 테두리 + 모드 색 그림
    public static RectTransform CreateBadge(Transform parent, GameMode m, float size)
    {
        RectTransform r = SettingsUIKit.Rect("Mode_" + m, parent);
        r.sizeDelta = new Vector2(size, size);
        GameModeBadge b = r.gameObject.AddComponent<GameModeBadge>();
        b.Build(size);
        b.Set(m);
        return r;
    }
}

// 아이콘 배지 (모드를 바꿔 끼울 수 있음 — 곡 선택창 MODE 바의 칸에서 사용)
public class GameModeBadge : MonoBehaviour
{
    public Image Bg, Ring, Glyph;
    public GameMode Mode { get; private set; }
    private bool hasMode;

    public void Build(float size)
    {
        RectTransform r = (RectTransform)transform;
        float rad = Mathf.Max(4f, size * 0.22f);
        Ring = SettingsUIKit.Img("Ring", r, Color.white);
        Ring.sprite = GameModes.BadgeSprite; Ring.type = Image.Type.Sliced; Ring.pixelsPerUnitMultiplier = 16f / rad;
        SettingsUIKit.Stretch(Ring.rectTransform);
        Bg = SettingsUIKit.Img("Bg", r, Color.black);
        Bg.sprite = GameModes.BadgeSprite; Bg.type = Image.Type.Sliced; Bg.pixelsPerUnitMultiplier = 16f / Mathf.Max(1f, rad - 1.5f);
        SettingsUIKit.Stretch(Bg.rectTransform);
        Bg.rectTransform.offsetMin = new Vector2(1.5f, 1.5f);
        Bg.rectTransform.offsetMax = new Vector2(-1.5f, -1.5f);
        Glyph = SettingsUIKit.Img("Glyph", r, Color.white);
        SettingsUIKit.Stretch(Glyph.rectTransform);
        float pad = size * 0.14f;
        Glyph.rectTransform.offsetMin = new Vector2(pad, pad);
        Glyph.rectTransform.offsetMax = new Vector2(-pad, -pad);
        Glyph.preserveAspect = true;
    }

    public void Set(GameMode m)
    {
        Mode = m; hasMode = true;
        Color c = GameModes.Info(m).Color;
        Glyph.enabled = true;
        Glyph.sprite = GameModes.Icon(m);
        Glyph.color = c;
        Ring.color = new Color(c.r, c.g, c.b, 0.55f);
        Bg.color = new Color(c.r * 0.12f + 0.02f, c.g * 0.12f + 0.02f, c.b * 0.12f + 0.035f, 1f);   // 불투명 (테두리가 비쳐 떠 보이지 않게)
    }

    // AUTO PLAY 중: 아이콘을 흐리게
    public void SetDim(bool dim)
    {
        CanvasGroup g = GetComponent<CanvasGroup>();
        if (g == null) g = gameObject.AddComponent<CanvasGroup>();
        g.alpha = dim ? 0.3f : 1f;
    }

    // 빈 칸 (점선 느낌의 옅은 테두리)
    public void SetEmpty()
    {
        hasMode = false;
        Glyph.enabled = false;
        Ring.color = new Color(1f, 1f, 1f, 0.12f);
        Bg.color = new Color(0.035f, 0.04f, 0.055f, 1f);
    }

    public bool HasMode { get { return hasMode; } }
}

// ============================================================
//  모드 아이콘 그리기 — 64 x 64 흰색 그림 (선 · 원 · 다각형을 거리 함수로 부드럽게)
// ============================================================
public static class GameModeIconArt
{
    private const int N = 64;
    private delegate float Shape(float x, float y);   // 그 점까지의 거리 (안쪽이면 음수), 픽셀 단위

    public static Sprite Draw(GameMode m)
    {
        var layers = new List<KeyValuePair<Shape, float>>();   // (모양, 진하기)
        Action<Shape> add = s => layers.Add(new KeyValuePair<Shape, float>(s, 1f));
        Action<Shape, float> addA = (s, a) => layers.Add(new KeyValuePair<Shape, float>(s, a));
        const float L = 3.2f;                                 // 기본 선 굵기 (반지름)

        switch (m)
        {
            case GameMode.Mirror:      // ◀ | ▶  (가운데 축과 양쪽으로 뒤집힌 삼각형)
                add((x, y) => Seg(x, y, 32, 8, 32, 56, 1.6f));
                add((x, y) => Tri(x, y, 6, 32, 25, 17, 25, 47));
                add((x, y) => Tri(x, y, 58, 32, 39, 17, 39, 47));
                break;
            case GameMode.Random:      // 주사위 (둥근 사각 테두리 + 점 세 개)
                add((x, y) => Mathf.Abs(RoundBox(x, y, 32, 32, 21, 21, 7)) - 2.4f);
                add((x, y) => Circle(x, y, 22, 42, 4.2f));
                add((x, y) => Circle(x, y, 32, 32, 4.2f));
                add((x, y) => Circle(x, y, 42, 22, 4.2f));
                break;
            case GameMode.Shadow:      // 아래로 갈수록 옅어지는 막대 + 판정선
                addA((x, y) => Seg(x, y, 14, 52, 50, 52, L), 1f);
                addA((x, y) => Seg(x, y, 14, 41, 50, 41, L), 0.62f);
                addA((x, y) => Seg(x, y, 14, 30, 50, 30, L), 0.32f);
                addA((x, y) => Seg(x, y, 14, 19, 50, 19, L), 0.12f);
                addA((x, y) => Seg(x, y, 6, 9, 58, 9, 1.5f), 0.9f);
                break;
            case GameMode.Lazy:        // zZ (졸고 있음)
                add((x, y) => Seg(x, y, 12, 46, 32, 46, 2.8f));
                add((x, y) => Seg(x, y, 32, 46, 12, 24, 2.8f));
                add((x, y) => Seg(x, y, 12, 24, 32, 24, 2.8f));
                add((x, y) => Seg(x, y, 38, 32, 52, 32, 2.2f));
                add((x, y) => Seg(x, y, 52, 32, 38, 16, 2.2f));
                add((x, y) => Seg(x, y, 38, 16, 52, 16, 2.2f));
                break;
            case GameMode.Mercy:       // 하트
                add((x, y) => Circle(x, y, 23.5f, 39, 10.5f));
                add((x, y) => Circle(x, y, 40.5f, 39, 10.5f));
                add((x, y) => Tri(x, y, 14.2f, 34, 49.8f, 34, 32, 13));
                break;
            case GameMode.Clutch:      // 판정 범위가 양쪽으로 넓어짐: | | + 바깥 화살표
                add((x, y) => Seg(x, y, 27, 18, 27, 46, 2f));
                add((x, y) => Seg(x, y, 37, 18, 37, 46, 2f));
                add((x, y) => Seg(x, y, 20, 32, 8, 32, 2.4f));
                add((x, y) => Seg(x, y, 8, 32, 14, 38, 2.4f));
                add((x, y) => Seg(x, y, 8, 32, 14, 26, 2.4f));
                add((x, y) => Seg(x, y, 44, 32, 56, 32, 2.4f));
                add((x, y) => Seg(x, y, 56, 32, 50, 38, 2.4f));
                add((x, y) => Seg(x, y, 56, 32, 50, 26, 2.4f));
                break;
            case GameMode.Reverse:     // 위쪽 판정선 + 위로 올라가는 화살표
                add((x, y) => Seg(x, y, 8, 54, 56, 54, 1.8f));
                add((x, y) => Seg(x, y, 32, 10, 32, 42, 3f));
                add((x, y) => Seg(x, y, 21, 32, 32, 43, 3f));
                add((x, y) => Seg(x, y, 43, 32, 32, 43, 3f));
                break;
            case GameMode.OnePlus:     // 1+
                add((x, y) => Seg(x, y, 24, 16, 24, 48, L));
                add((x, y) => Seg(x, y, 24, 48, 16, 41, L * 0.85f));
                add((x, y) => Seg(x, y, 16, 16, 32, 16, L * 0.85f));
                add((x, y) => Seg(x, y, 38, 32, 54, 32, L));
                add((x, y) => Seg(x, y, 46, 24, 46, 40, L));
                break;
            case GameMode.Fake:        // 노트 테두리 + 사선 (가짜)
                add((x, y) => Mathf.Abs(RoundBox(x, y, 32, 32, 23, 10, 4)) - 2f);
                add((x, y) => Seg(x, y, 14, 12, 50, 52, 2.4f));
                break;
            case GameMode.Easy:        // 과녁: 판정 범위가 넓음 (큰 가운데 원)
                add((x, y) => Mathf.Abs(Circle(x, y, 32, 32, 25)) - 2f);
                add((x, y) => Circle(x, y, 32, 32, 16));
                break;
            case GameMode.Hard:        // 가운데 원이 작아짐
                add((x, y) => Mathf.Abs(Circle(x, y, 32, 32, 25)) - 2f);
                add((x, y) => Circle(x, y, 32, 32, 10));
                break;
            case GameMode.Maximum:
                add((x, y) => Mathf.Abs(Circle(x, y, 32, 32, 25)) - 2f);
                add((x, y) => Mathf.Abs(Circle(x, y, 32, 32, 13)) - 1.4f);
                add((x, y) => Circle(x, y, 32, 32, 5.5f));
                break;
            case GameMode.Ultimate:    // 가장 작은 점 + 십자선
                add((x, y) => Mathf.Abs(Circle(x, y, 32, 32, 25)) - 2f);
                add((x, y) => Circle(x, y, 32, 32, 3.6f));
                add((x, y) => Seg(x, y, 32, 3, 32, 17, 1.6f));
                add((x, y) => Seg(x, y, 32, 47, 32, 61, 1.6f));
                add((x, y) => Seg(x, y, 3, 32, 17, 32, 1.6f));
                add((x, y) => Seg(x, y, 47, 32, 61, 32, 1.6f));
                break;
            case GameMode.Immortal:    // ∞
                add((x, y) => Mathf.Abs(Circle(x, y, 21, 32, 11)) - 2.6f);
                add((x, y) => Mathf.Abs(Circle(x, y, 43, 32, 11)) - 2.6f);
                break;
            case GameMode.UnknownUi:   // ?
                add((x, y) => ArcQ(x, y));
                add((x, y) => Seg(x, y, 32, 31, 32, 25, L));
                add((x, y) => Circle(x, y, 32, 14, 3.8f));
                break;
            case GameMode.Perfectionist:   // ◇
                add((x, y) => Mathf.Abs(Diamond(x, y, 32, 32, 24)) - 2.4f);
                break;
            case GameMode.PerfectionistPlus:   // ◇ + 안쪽 ◆
                add((x, y) => Mathf.Abs(Diamond(x, y, 32, 32, 24)) - 2.4f);
                add((x, y) => Diamond(x, y, 32, 32, 10));
                break;
            case GameMode.OneChance:   // 원 안의 1
                add((x, y) => Mathf.Abs(Circle(x, y, 32, 32, 25)) - 2f);
                add((x, y) => Seg(x, y, 33, 17, 33, 47, L));
                add((x, y) => Seg(x, y, 33, 47, 25, 41, L * 0.85f));
                add((x, y) => Seg(x, y, 25, 17, 41, 17, L * 0.85f));
                break;
            case GameMode.ComboBreak:  // 끊어진 막대 + 갈라진 틈
                add((x, y) => Seg(x, y, 8, 32, 24, 32, 4.2f));
                add((x, y) => Seg(x, y, 40, 32, 56, 32, 4.2f));
                add((x, y) => Seg(x, y, 30, 46, 35, 37, 1.8f));
                add((x, y) => Seg(x, y, 35, 37, 29, 27, 1.8f));
                add((x, y) => Seg(x, y, 29, 27, 34, 18, 1.8f));
                break;
            case GameMode.Strict:      // LIFE 막대 + 위에서 내려오는 상한선 화살표
                add((x, y) => Mathf.Abs(RoundBox(x, y, 32, 30, 10, 24, 4)) - 2f);
                add((x, y) => RoundBox(x, y, 32, 18, 5.5f, 9, 2));
                add((x, y) => Seg(x, y, 26, 40, 32, 34, 2f));
                add((x, y) => Seg(x, y, 32, 34, 38, 40, 2f));
                add((x, y) => Seg(x, y, 26, 48, 32, 42, 2f));
                add((x, y) => Seg(x, y, 32, 42, 38, 48, 2f));
                break;
        }

        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[N * N];
        for (int py = 0; py < N; py++)
            for (int ix = 0; ix < N; ix++)
            {
                float x = ix + 0.5f, y = py + 0.5f, a = 0f;
                foreach (var l in layers)
                {
                    float d = l.Key(x, y);
                    float cov = Mathf.Clamp01(0.5f - d);
                    a = Mathf.Max(a, cov * l.Value);
                }
                px[py * N + ix] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    // ----- 거리 함수 -----
    private static float Circle(float x, float y, float cx, float cy, float r)
    {
        float dx = x - cx, dy = y - cy;
        return Mathf.Sqrt(dx * dx + dy * dy) - r;
    }

    private static float Seg(float x, float y, float ax, float ay, float bx, float by, float r)
    {
        float px = x - ax, py = y - ay, vx = bx - ax, vy = by - ay;
        float h = Mathf.Clamp01((px * vx + py * vy) / Mathf.Max(0.0001f, vx * vx + vy * vy));
        float dx = px - vx * h, dy = py - vy * h;
        return Mathf.Sqrt(dx * dx + dy * dy) - r;
    }

    private static float RoundBox(float x, float y, float cx, float cy, float hw, float hh, float r)
    {
        float qx = Mathf.Abs(x - cx) - hw + r, qy = Mathf.Abs(y - cy) - hh + r;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    private static float Diamond(float x, float y, float cx, float cy, float r)
    {
        return (Mathf.Abs(x - cx) + Mathf.Abs(y - cy) - r) * 0.70710678f;
    }

    // 꽉 찬 삼각형 (세 변까지 거리 중 가장 큰 값 — 안쪽이면 음수)
    private static float Tri(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float s = Mathf.Sign((bx - ax) * (cy - ay) - (by - ay) * (cx - ax));
        return Mathf.Max(Edge(x, y, ax, ay, bx, by, s), Mathf.Max(Edge(x, y, bx, by, cx, cy, s), Edge(x, y, cx, cy, ax, ay, s)));
    }
    private static float Edge(float x, float y, float ax, float ay, float bx, float by, float s)
    {
        float ex = bx - ax, ey = by - ay;
        float len = Mathf.Max(0.0001f, Mathf.Sqrt(ex * ex + ey * ey));
        return -s * ((x - ax) * ey * -1f + (y - ay) * ex) / len;
    }

    // ? 의 윗부분: 원호 (아래 왼쪽 구간만 비움)
    private static float ArcQ(float x, float y)
    {
        const float cx = 32, cy = 41, R = 10.5f, W = 3.2f;
        float dx = x - cx, dy = y - cy;
        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;   // -180 ~ 180
        bool gap = ang < -90f;                               // 왼쪽 아래 (-180 ~ -90) 는 비움
        if (!gap) return Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - R) - W;
        // 끝점까지의 거리 (둥근 끝)
        float ex1 = cx - R, ey1 = cy, ex2 = cx, ey2 = cy - R;
        float d1 = Mathf.Sqrt((x - ex1) * (x - ex1) + (y - ey1) * (y - ey1));
        float d2 = Mathf.Sqrt((x - ex2) * (x - ex2) + (y - ey2) * (y - ey2));
        return Mathf.Min(d1, d2) - W;
    }

    // 속이 빈 둥근 사각형 (선 두께 thick 픽셀, 9-slice 테두리 = r 픽셀)
    public static Sprite RoundRing(int size, int r, float thick)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundBox(x + 0.5f, y + 0.5f, h, h, h, h, r);
                float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + thick + 0.5f);   // 바깥 경계 안쪽 thick 픽셀만
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }

    // 둥근 사각형 (9-slice 용, 테두리 = r 픽셀)
    public static Sprite RoundRect(int size, int r)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundBox(x + 0.5f, y + 0.5f, h, h, h, h, r);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d) * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }
}

// ============================================================
//  게임 모드 알림 글자 (REVIVE · MERCY · FAKE) — 화면 가운데 위쪽에 잠깐 떴다 사라짐
// ============================================================
public class GameModePopup : MonoBehaviour
{
    private TMPro.TextMeshProUGUI text;
    private float t, life;
    private Color color;

    public static void Show(UnityEngine.SceneManagement.Scene scene, string message, Color color, float scale)
    {
        GameObject go = new GameObject("ModePopup_" + message, typeof(RectTransform));
        go.layer = 5;
        if (scene.IsValid() && scene.isLoaded) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 95;                      // 게임 HUD 위, 일시정지 · 설정창 아래
        CanvasScaler sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        SettingsUIKit.EnsureFont();
        GameModePopup p = go.AddComponent<GameModePopup>();
        p.color = color;
        p.life = 0.9f + 0.4f * scale;
        p.text = SettingsUIKit.Txt("T", go.transform, message, 64f * scale, TMPro.TextAlignmentOptions.Center, color, TMPro.FontStyles.Bold);
        p.text.characterSpacing = 18f;
        p.text.raycastTarget = false;
        RectTransform r = p.text.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(0f, 230f);
        r.sizeDelta = new Vector2(1200f, 160f);
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(t / life);
        // 자간이 좁혀지며 나타났다가, 끝에서 위로 살짝 떠오르며 사라짐
        float a = Mathf.Clamp01(t / 0.12f) * (1f - Mathf.Clamp01((k - 0.65f) / 0.35f));
        text.color = new Color(color.r, color.g, color.b, a);
        text.characterSpacing = Mathf.Lerp(30f, 14f, 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.35f), 3f));
        text.rectTransform.anchoredPosition = new Vector2(0f, 230f + 30f * Mathf.Clamp01((k - 0.65f) / 0.35f));
        if (k >= 1f) Destroy(gameObject);
    }
}