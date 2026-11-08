using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 게임 모드 (곡 선택창 MODE 바 / M 키 모드 설정창에서 고름, 한 판에 최대 3개)
//  점수 배율: 고른 모드의 배율을 모두 곱함 (곱연산). 최대 400% (= 4,000,000 점)
//  MODE RANDOM (MODE 바 옆 버튼): 게임을 시작할 때 모드 3개를 무작위로 고름
//   → 게임 화면에서 레인이 펼쳐지기 전에 고르는 연출(GameModeRoulette)을 보여 주고 시작 (다시 하기는 같은 조합, 연출 없음)
//   → 점수 보너스 최대 +30%p = 조합 배율 몫 최대 20%p + 특별 모드(MAX JUDGEMENT · ULTIMATE · PERFECTIONIST · COMBO BREAK · TEMPEST GAUGE) 몫 최대 10%p
//      (배율에 더함. 예: 390% + 25%p = 415%, 최대 430% = 4,300,000 점)
//
//  [JUDGE] 판정
//  EASY JUDGEMENT      75%   판정이 JUST 와 MISS 만 (BAD 범위 안이면 모두 JUST)
//  HARD JUDGEMENT     135%   JUST 를 뺀 판정 범위 1/2
//  MAX JUDGEMENT      180%   JUST 를 뺀 판정 범위 1/3
//  ULTIMATE           210%   JUST 를 뺀 판정 범위 1/4 + JUST 범위 35% 감소 (±20ms → ±13ms)
//  LAZY               120%   놓친 노트가 낮은 확률(15%)로 자동 JUST (롱노트는 끝까지 자동으로 눌림)
//  MERCY              110%   BAD · MISS 가 50% 확률로 GREAT 이상으로 바뀜 (GREAT 50% · PERFECT 35% · JUST 15%)
//  CLUTCH             110%   위험할수록 판정 범위가 넓어짐 (최대 2.5배)
//
//  [RULE] 규칙
//  PERFECTIONIST      195%   PERFECT 미만이 나오면 즉시 TRACK CRASH
//  ONE CHANCE         155%   MISS 가 한 번이라도 나오면 즉시 TRACK CRASH
//  COMBO BREAK        180%   콤보가 끊기면 3초 동안 점수를 얻지 못함
//  STRICT             140%   곡이 진행될수록 LIFE 최대 상한이 줄어듦 (100 → 60)
//  STRICT+            190%   STRICT + LIFE 하한이 점점 올라감 (0 → 40). LIFE ≤ 하한이면 즉시 TRACK CRASH (회복으로 버티는 이득을 막음)
//                             예) 곡 끝: 상한 60 · 하한 40 → LIFE 가 움직일 수 있는 범위는 41 ~ 60
//                     STRICT · STRICT+ · ONE+ 는 HARD 이상 게이지(HARD · EXTREME · TEMPEST)에서만 고를 수 있음
//
//  [CHART] 채보 · 화면
//  MIRROR             120%   좌우 반전          RANDOM   165%   레인 무작위
//  SHADOW             150%   판정선에 가까워질수록 투명 (판정 시각 20ms 전까지는 완전히 사라지지 않음)
//  UNKNOWN UI         115%   레인을 뺀 게임 화면 UI 를 모두 가림 (점수 · 콤보 · 판정 · LIFE · 곡 정보 …)
//  REVERSE            150%   노트가 아래에서 위로 (판정선이 위쪽)       FAKE     135%   치면 안 되는 가짜 노트 (치면 지금 게이지의 50% 감소)
//
//  [GAUGE] 게이지 (하나만. 아무것도 안 고르면 NORMAL)
//  EASY GAUGE          90%   0 에서 시작해 채움. 곡이 끝날 때 70 이상이면 TRACK COMPLETE, 미만이면 TRACK CRASH (잘 차고 덜 깎임)
//  NORMAL GAUGE       100%   0 에서 시작해 채움. 곡이 끝날 때 70 이상이면 TRACK COMPLETE, 미만이면 TRACK CRASH
//  HARD GAUGE         140%   100 에서 시작, 0 이 되면 즉시 TRACK CRASH
//  EXTREME GAUGE      185%   (예전 이름 HARD+) 100 에서 시작, 0 이 되면 즉시 TRACK CRASH. 완벽하게 쳐도 차지 않고 깎이기만 함
//  TEMPEST GAUGE      200%   100 에서 시작해 시간에 따라 계속 줄어듦 (모두 PERFECT 여도 조금씩). 판정은 몇 초에 걸쳐 반영, BAD · MISS 는 HARD 의 4배. 0 이면 TRACK CRASH
//  IMMORTAL            65%   절대 죽지 않음 (게이지 항상 가득)
//  ONE+               100%   TRACK CRASH 를 한 번 막아 줌 (LIFE 30 으로 부활, STRICT+ 면 하한보다 10 위)
//                             + 부활 뒤 5초 동안 LIFE 가 깎이지 않음 (판정에 따라 오르기만, LIFE 바에 유리 덮개 + 자물쇠)
//
//  함께 쓸 수 없는 모드 (같은 묶음은 하나만, 새로 고르면 이전 것과 바뀜)
//   · MIRROR / RANDOM
//   · EASY · HARD · MAX JUDGEMENT / ULTIMATE
//   · 게이지 5종 / IMMORTAL
//   · STRICT 와 STRICT+
//   · IMMORTAL 과 (PERFECTIONIST · ONE CHANCE · STRICT · STRICT+ · CLUTCH · ONE+)  ← 게이지가 줄지 않으면 의미가 없는 모드
//  고를 조건이 있는 모드 (조건이 안 맞으면 자물쇠 · 선택 불가, 조건이 깨지면 자동으로 꺼짐)
//   · STRICT · STRICT+ · ONE+ : HARD · EXTREME · TEMPEST 게이지 중 하나가 켜져 있어야 함
//  AUTO PLAY 는 모드가 아님 — AUTO PLAY 를 켜면 모드 설정창이 열리지 않고, 고른 모드도 적용되지 않음 (선택은 남아 있음)
//  GAME SETTING(5_Test) 에서는 모든 모드가 꺼진 채로 진행
// ============================================================
public enum GameMode
{
    Mirror, Random, Shadow, Easy, Hard, Maximum, Ultimate,
    Immortal, UnknownUi, Perfectionist, OneChance, ComboBreak, Strict,
    Lazy, Mercy, Clutch,
    Reverse, OnePlus, Fake,
    GaugeEasy, GaugeNormal, GaugeHard, GaugeHardPlus, GaugeTempest,
    StrictPlus      // (맨 뒤에 추가 — 저장된 기록의 번호가 바뀌지 않게)
}

// 게이지 종류 (게이지 모드를 안 고르면 Normal)
public enum GaugeKind { Easy, Normal, Hard, HardPlus, Tempest }

public class GameModeInfo
{
    public GameMode Mode;
    public string Name;
    public string Desc;         // 한국어 설명 (모드 설정창 카드)
    public double Multiplier;   // 점수 배율 (1 = 100%)
    public int Group;           // 0 = 묶음 없음, 같은 값끼리는 하나만
    public int Category;        // 0 JUDGE  1 RULE  2 CHART  3 GAUGE (모드 설정창 탭)
    public Color Color;
}

public static class GameModes
{
    public const int MaxSelected = 3;
    public const double MaxMultiplier = 4.0;          // 최대 400%
    public const int MaxScoreWithModes = 4300000;     // 400% + MODE RANDOM 보너스 최대 30%p
    public const double RandomBonusMax = 0.30;        // MODE RANDOM 보너스 최대 +30%p (= 조합 배율 몫 20%p + 특별 모드 몫 10%p)
    public const double RandomBonusComboMax = 0.20;   //   조합 배율(A)로 받는 몫: 최대 +20%p
    public const double RandomBonusSpecialMax = 0.10; //   특별 모드(B)로 받는 몫: 최대 +10%p
    public static readonly string[] CategoryNames = { "JUDGE", "RULE", "CHART", "GAUGE" };

    // 판정 · 연출 수치
    public const double JustWindowBaseMs = 20.0;      // JUST 판정 범위 (±20ms)
    public const double UltimateJustScale = 0.65;     // ULTIMATE: JUST 범위 35% 감소 (±13ms)
    public const double ComboBreakLockSeconds = 3.0;  // COMBO BREAK: 콤보가 끊긴 뒤 점수를 못 얻는 시간
    public const double StrictMinCap = 60.0;          // STRICT · STRICT+: 곡 끝에서의 LIFE 최대 상한
    public const double StrictPlusMaxFloor = 40.0;    // STRICT+: 곡 끝에서의 LIFE 하한 (LIFE ≤ 하한이면 즉시 TRACK CRASH)
    public const double ClearLine = 70.0;             // EASY · NORMAL 게이지: 곡이 끝날 때 이 값 이상이면 TRACK COMPLETE
    // SHADOW: 판정 시각까지 남은 시간(초) 기준
    //  ShadowFadeStart 초 전부터 옅어져 ShadowFadeEnd 초 전에 ShadowFloor 까지, 그 뒤 마지막 ShadowVanish 초(20ms) 동안 사라짐
    public const float ShadowFadeStart = 0.45f;
    public const float ShadowFadeEnd = 0.06f;
    public const float ShadowFloor = 0.35f;
    public const float ShadowVanish = 0.02f;
    public const double LazyChance = 0.15;            // LAZY: 놓친 노트가 자동 JUST 될 확률
    public const double MercyChance = 0.50;           // MERCY: BAD · MISS 가 바뀔 확률 (설명에는 '일정 확률'로만 표기)
    public const double MercyGreat = 0.50;            //        바뀐 판정 확률표: GREAT 50%
    public const double MercyPerfect = 0.35;          //                         PERFECT 35% (JUST 아닌 PERFECT)
    public const double MercyJust = 0.15;             //                         JUST 15%
    public const double ClutchMaxScale = 2.5;         // CLUTCH: 가장 위험할 때 판정 범위 배율
    public const double OnePlusShieldSeconds = 5.0;   // ONE+: 부활 뒤 LIFE 가 깎이지 않는 시간 (판정에 따라 오르기만)
    public const double OnePlusReviveLife = 30.0;     // ONE+: TRACK CRASH 를 한 번 막고 부활한 뒤의 LIFE (STRICT+ 면 하한 + 10, 상한을 넘지 않음)
    public const double FakeChance = 0.15;            // FAKE: 노트가 있는 시각마다 가짜 노트가 하나 생길 확률
    public const double FakeGap = 0.25;               //       가짜 노트는 같은 레인의 진짜 노트 · 롱노트와 이 시간(초) 이상 떨어짐
    public const double FakePenalty = 0.5;            //       가짜 노트를 치면 지금 게이지의 이만큼 감소
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
    private static readonly Color CGaugeEasy = new Color(0.55f, 0.92f, 0.62f), CGaugeNormal = new Color(0.55f, 0.80f, 1.00f),
                                  CGaugeHard = new Color(1.00f, 0.55f, 0.35f), CGaugeHardPlus = new Color(1.00f, 0.32f, 0.40f),
                                  CGaugeTempest = new Color(0.78f, 0.55f, 1.00f);

    public static readonly GameModeInfo[] All =
    {
        new GameModeInfo { Mode = GameMode.Mirror, Name = "MIRROR", Multiplier = 1.20, Category = 2, Group = 1, Color = CLane,
            Desc = "채보가 좌우로 반전됩니다." },
        new GameModeInfo { Mode = GameMode.Random, Name = "RANDOM", Multiplier = 1.65, Category = 2, Group = 1, Color = CLane,
            Desc = "채보가 무작위로 배치되어 내려옵니다." },
        new GameModeInfo { Mode = GameMode.Shadow, Name = "SHADOW", Multiplier = 1.50, Category = 2, Group = 0, Color = CVisual,
            Desc = "채보가 판정선에 가까워질수록 투명해집니다." },
        new GameModeInfo { Mode = GameMode.Easy, Name = "EASY JUDGEMENT", Multiplier = 0.75, Category = 0, Group = 2, Color = CEasy,
            Desc = "판정이 JUST와 MISS만 존재합니다." },
        new GameModeInfo { Mode = GameMode.Hard, Name = "HARD JUDGEMENT", Multiplier = 1.35, Category = 0, Group = 2, Color = CHard,
            Desc = "JUST를 제외한 노트 판정 범위가 기존 판정 범위의 절반이 됩니다." },
        new GameModeInfo { Mode = GameMode.Maximum, Name = "MAX JUDGEMENT", Multiplier = 1.80, Category = 0, Group = 2, Color = CMax,
            Desc = "JUST를 제외한 노트 판정 범위가 기존 판정 범위의 1/3이 됩니다." },
        new GameModeInfo { Mode = GameMode.Ultimate, Name = "ULTIMATE", Multiplier = 2.10, Category = 0, Group = 2, Color = CUlt,
            Desc = "JUST를 제외한 노트 판정 범위가 기존 판정 범위의 1/4이 됩니다. JUST 판정의 범위가 35% 감소합니다." },
        new GameModeInfo { Mode = GameMode.Immortal, Name = "IMMORTAL", Multiplier = 0.65, Category = 3, Group = 4, Color = CLife,
            Desc = "절대 죽지 않습니다." },
        new GameModeInfo { Mode = GameMode.UnknownUi, Name = "UNKNOWN UI", Multiplier = 1.15, Category = 2, Group = 0, Color = CVisual,
            Desc = "게임 내 UI가 모두 가려집니다." },
        new GameModeInfo { Mode = GameMode.Perfectionist, Name = "PERFECTIONIST", Multiplier = 1.95, Category = 1, Group = 0, Color = CRisk,
            Desc = "PERFECT 미만의 판정이 발생할 시 즉시 TRACK CRASH됩니다." },
        new GameModeInfo { Mode = GameMode.OneChance, Name = "ONE CHANCE", Multiplier = 1.55, Category = 1, Group = 0, Color = CRisk,
            Desc = "MISS 발생 시 즉시 TRACK CRASH 처리됩니다." },
        new GameModeInfo { Mode = GameMode.ComboBreak, Name = "COMBO BREAK", Multiplier = 1.80, Category = 1, Group = 0, Color = CRisk,
            Desc = "콤보가 끊기면 일정 시간 동안 점수를 획득할 수 없습니다." },
        new GameModeInfo { Mode = GameMode.Strict, Name = "STRICT", Multiplier = 1.40, Category = 1, Group = 5, Color = CRisk,
            Desc = "곡이 진행될 수록 LIFE의 최대 상한이 감소합니다." },

        new GameModeInfo { Mode = GameMode.Lazy, Name = "LAZY", Multiplier = 1.20, Category = 0, Group = 0, Color = CCasual,
            Desc = "노트를 놓치면 낮은 확률로 JUST 판정으로 처리됩니다." },
        new GameModeInfo { Mode = GameMode.Mercy, Name = "MERCY", Multiplier = 1.10, Category = 0, Group = 0, Color = CCasual,
            Desc = "BAD 또는 MISS 판정이 났을 때, 일정 확률로 GREAT 이상의 판정으로 대체됩니다." },
        new GameModeInfo { Mode = GameMode.Clutch, Name = "CLUTCH", Multiplier = 1.10, Category = 0, Group = 0, Color = CCasual,
            Desc = "라이프가 낮아질수록 판정이 최대 2.5배 넓어집니다." },

        new GameModeInfo { Mode = GameMode.Reverse, Name = "REVERSE", Multiplier = 1.50, Category = 2, Group = 0, Color = CGimmick,
            Desc = "노트가 아래에서 위로 올라갑니다! 판정선이 위쪽에 있습니다." },
        new GameModeInfo { Mode = GameMode.OnePlus, Name = "ONE+", Multiplier = 1.00, Category = 3, Group = 0, Color = CGimmick,
            Desc = "TRACK CRASH를 한 번 방어합니다." },
        new GameModeInfo { Mode = GameMode.Fake, Name = "FAKE", Multiplier = 1.35, Category = 2, Group = 0, Color = CGimmick,
            Desc = "실제로 입력하면 안 되는 가짜 노트가 등장합니다. 입력할 시 현재 라이프의 50%만큼 차감됩니다." },

        new GameModeInfo { Mode = GameMode.GaugeEasy, Name = "EASY GAUGE", Multiplier = 0.90, Category = 3, Group = 4, Color = CGaugeEasy,
            Desc = "NORMAL GAUGE에 비해 LIFE 회복량이 증가합니다." },
        new GameModeInfo { Mode = GameMode.GaugeNormal, Name = "NORMAL GAUGE", Multiplier = 1.00, Category = 3, Group = 4, Color = CGaugeNormal,
            Desc = "곡이 종료될 때 LIFE가 70 이상이면 TRACK COMPLETE, 미만이면 TRACK CRASH 처리됩니다." },
        new GameModeInfo { Mode = GameMode.GaugeHard, Name = "HARD GAUGE", Multiplier = 1.40, Category = 3, Group = 4, Color = CGaugeHard,
            Desc = "LIFE가 0이 되면 TRACK CRASH 처리됩니다." },
        new GameModeInfo { Mode = GameMode.GaugeHardPlus, Name = "EXTREME GAUGE", Multiplier = 1.85, Category = 3, Group = 4, Color = CGaugeHardPlus,
            Desc = "LIFE가 0이 되면 TRACK CRASH 처리되며, LIFE가 회복되지 않습니다." },
        new GameModeInfo { Mode = GameMode.GaugeTempest, Name = "TEMPEST GAUGE", Multiplier = 2.00, Category = 3, Group = 4, Color = CGaugeTempest,
            Desc = "???" },
        new GameModeInfo { Mode = GameMode.StrictPlus, Name = "STRICT+", Multiplier = 1.90, Category = 1, Group = 5, Color = CRisk,
            Desc = "곡이 진행될 수록 LIFE의 최대 상한이 감소하며, LIFE의 하한치가 점점 상승합니다." },
    };

    public static GameModeInfo Info(GameMode m) { return All[(int)m]; }

    // 모드 설정창에 놓는 순서 (탭 안에서 이 순서대로)
    public static readonly GameMode[] DisplayOrder =
    {
        GameMode.Easy, GameMode.Hard, GameMode.Maximum, GameMode.Ultimate, GameMode.Lazy, GameMode.Mercy, GameMode.Clutch,
        GameMode.Perfectionist, GameMode.OneChance, GameMode.ComboBreak, GameMode.Strict, GameMode.StrictPlus,
        GameMode.Mirror, GameMode.Random, GameMode.Shadow, GameMode.UnknownUi, GameMode.Reverse, GameMode.Fake,
        GameMode.GaugeEasy, GameMode.GaugeNormal, GameMode.GaugeHard, GameMode.GaugeHardPlus, GameMode.GaugeTempest,
        GameMode.Immortal, GameMode.OnePlus,
    };

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
    // 게이지가 줄어야 의미가 있는 모드 (IMMORTAL 과 같이 못 씀)
    private static bool NeedsLife(GameMode m)
    {
        return m == GameMode.Perfectionist || m == GameMode.OneChance
            || m == GameMode.Strict || m == GameMode.StrictPlus || m == GameMode.Clutch || m == GameMode.OnePlus;
    }

    public enum ToggleResult { Added, Removed, Replaced, Full, Locked, Requires }

    // ----- 고를 조건 -----
    public const string StrictRequireText = "해당 모드는 HARD 이상의 게이지를 필요로 합니다.";
    public static bool IsHardGauge(GameMode m) { return m == GameMode.GaugeHard || m == GameMode.GaugeHardPlus || m == GameMode.GaugeTempest; }
    // 이 모드를 고를 조건이 modes(함께 켜진 모드들)로 맞는가
    public static bool NeedsHardGauge(GameMode m) { return m == GameMode.Strict || m == GameMode.StrictPlus || m == GameMode.OnePlus; }
    public static bool RequirementMet(GameMode m, IList<GameMode> modes)
    {
        if (!NeedsHardGauge(m)) return true;
        if (modes != null) foreach (var x in modes) if (IsHardGauge(x)) return true;
        return false;
    }
    // 조건 안내 글자 (조건이 없으면 null)
    public static string RequirementText(GameMode m) { return NeedsHardGauge(m) ? StrictRequireText : null; }
    // 마지막 Toggle 에서 조건이 깨져 자동으로 꺼진 모드 이름 (없으면 null)
    public static string LastDropped { get; private set; }

    // 조건이 깨진 모드를 끔
    private static void DropUnmet()
    {
        for (int i = selected.Count - 1; i >= 0; i--)
            if (!RequirementMet(selected[i], selected))
            {
                LastDropped = LastDropped == null ? Info(selected[i]).Name : LastDropped + ", " + Info(selected[i]).Name;
                selected.RemoveAt(i);
            }
    }

    // 켜기 / 끄기. 같이 쓸 수 없는 모드가 켜져 있으면 그 모드와 바뀜. 3개가 차 있으면 Full (아무것도 안 바뀜)
    public static ToggleResult Toggle(GameMode m, out string replacedName)
    {
        replacedName = null;
        LastDropped = null;
        if (Locked) return ToggleResult.Locked;
        if (selected.Contains(m))
        {
            selected.Remove(m);
            DropUnmet();                                   // 예: HARD 게이지를 끄면 STRICT 도 꺼짐
            randomPick = false;                            // 직접 고르면 MODE RANDOM 은 꺼짐
            Version++;
            return ToggleResult.Removed;
        }
        var conflicts = selected.FindAll(x => Conflicts(x, m));
        // 조건: 바뀐 뒤의 모드들로 판단 (예: STRICT 는 HARD 이상 게이지가 켜져 있어야)
        var after = new List<GameMode>(selected);
        after.RemoveAll(x => conflicts.Contains(x));
        if (!RequirementMet(m, after)) return ToggleResult.Requires;
        if (selected.Count - conflicts.Count >= MaxSelected) return ToggleResult.Full;
        foreach (var c in conflicts)
        {
            selected.Remove(c);
            replacedName = replacedName == null ? Info(c).Name : replacedName + ", " + Info(c).Name;
        }
        selected.Add(m);
        DropUnmet();                                       // 예: HARD → NORMAL 게이지로 바꾸면 STRICT 가 꺼짐
        randomPick = false;
        Version++;
        return conflicts.Count > 0 ? ToggleResult.Replaced : ToggleResult.Added;
    }

    public static void Clear()
    {
        if (Locked) return;
        selected.Clear();
        Version++;
    }

    // 이 모드를 지금 고를 수 있는가 (켜져 있지 않은 모드 기준: 조건 · 칸 수)
    public static bool CanAdd(GameMode m)
    {
        if (selected.Contains(m)) return true;
        var after = selected.FindAll(x => !Conflicts(x, m));
        return RequirementMet(m, after) && after.Count < MaxSelected;
    }

    // =====================================================================
    //  MODE RANDOM: 게임을 시작할 때 모드 3개를 무작위로 (함께 쓸 수 없는 · 조건이 안 맞는 조합은 나오지 않음)
    //   보너스 = A 몫(조합 배율, 최대 20%p) + B 몫(특별 모드, 최대 10%p) — 아래 RandomBonus 참고 (합 최대 30%p)
    //   A 몫 예) 390% → +19%p,  300% → +11%p,  200% → +4%p,  150% → +1%p
    //   NORMAL GAUGE 는 기본 게이지라 (고르나 마나) 나오지 않음
    //  밸런스
    //   · 배율 100% 미만 모드는 나오지 않음 (EASY JUDGEMENT · IMMORTAL · EASY GAUGE)
    //   · 배율 135% 미만 모드는 최대 1개. 나왔다면 나머지 2개 중 적어도 하나는 180% 이상
    public const double RandomLowLine = 1.35, RandomHighLine = 1.80;
    private static bool InRandomPool(GameModeInfo info) { return info.Mode != GameMode.GaugeNormal && info.Multiplier >= 1.0 - 1e-9; }
    // 밸런스 규칙에 맞는 조합인가
    public static bool RandomBalanced(IList<GameMode> pick)
    {
        int low = 0; bool high = false;
        foreach (var m in pick)
        {
            double k = Info(m).Multiplier;
            if (k < 1.0 - 1e-9) return false;
            if (k < RandomLowLine - 1e-9) low++;
            if (k >= RandomHighLine - 1e-9) high = true;
        }
        return low == 0 || (low == 1 && high);
    }
    // =====================================================================
    private static bool randomPick;
    public static bool RandomPick { get { return randomPick; } }
    public static void SetRandomPick(bool on)
    {
        if (Locked || randomPick == on) return;
        randomPick = on;
        Version++;
    }

    private static List<GameMode> randomResult;          // 마지막으로 고른 결과 (다시 하기 때도 같은 조합)
    private static double randomResultBonus;
    public static IList<GameMode> RandomResult { get { return randomResult; } }
    public static double RandomResultBonus { get { return randomResultBonus; } }

    // 곡 선택창에서 시작할 때: 지난 조합을 버림 → 게임 화면(BeginPlay)에서 새로 고름
    public static void ClearRandomResult() { randomResult = null; randomResultBonus = 0.0; }

    // 새로 고르기
    public static List<GameMode> RollRandom(System.Random rng = null)
    {
        if (rng == null) rng = new System.Random(Guid.NewGuid().GetHashCode());
        var pool = new List<GameMode>();
        foreach (var info in All) if (InRandomPool(info)) pool.Add(info.Mode);
        List<GameMode> best = null;
        for (int tries = 0; tries < 2000; tries++)
        {
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            var pick = new List<GameMode>();
            foreach (var m in pool)
            {
                if (pick.Count >= MaxSelected) break;
                if (pick.Exists(x => Conflicts(x, m))) continue;
                pick.Add(m);
            }
            bool ok = pick.Count == MaxSelected;
            foreach (var m in pick) if (!RequirementMet(m, pick)) ok = false;
            if (ok && !RandomBalanced(pick)) ok = false;
            if (ok) { best = pick; break; }
        }
        if (best == null) best = new List<GameMode> { GameMode.Mirror, GameMode.Shadow, GameMode.Perfectionist };   // (규칙에 맞는 기본 조합)
        randomResult = best;
        randomResultBonus = RandomBonus(best);
        return best;
    }

    // 보너스 = A 몫 + B 몫
    //  A (조합 배율 — 특별 모드 포함 세 모드 배율의 곱, 최대 400%) : 20%p × t^1.5,  t = (A - 100%) ÷ 300%  → 1%p 단위로 반올림
    //  B (특별 모드가 뽑혔으면 하나마다 더함, 합은 최대 10%p)
    //     MAX JUDGEMENT 6.5%p · ULTIMATE 8.5%p · PERFECTIONIST 7%p · COMBO BREAK 5.5%p · TEMPEST GAUGE 4%p
    //  예) ULTIMATE · COMBO BREAK · SHADOW (567% → 400%) : A 20%p + B min(10, 8.5 + 5.5) = 10%p → +30%p
    //      MIRROR · SHADOW · PERFECTIONIST (351%) : A 15%p + B 7%p → +22%p
    public static double SpecialBonusOf(GameMode m)
    {
        switch (m)
        {
            case GameMode.Maximum: return 0.065;
            case GameMode.Ultimate: return 0.085;
            case GameMode.Perfectionist: return 0.07;
            case GameMode.ComboBreak: return 0.055;
            case GameMode.GaugeTempest: return 0.04;
            default: return 0.0;
        }
    }

    public static double RandomBonus(IList<GameMode> modes)
    {
        double a = RandomComboBonus(MultiplierOf(modes));
        double b = RandomSpecialBonus(modes);
        return Math.Round((a + b) * 1000.0) / 1000.0;   // 0.1%p 단위
    }

    // B 몫 (특별 모드 가산, 0 ~ 10%p)
    public static double RandomSpecialBonus(IList<GameMode> modes)
    {
        double b = 0.0;
        if (modes != null) foreach (var m in modes) b += SpecialBonusOf(m);
        return Math.Round(Math.Min(RandomBonusSpecialMax, b) * 1000.0) / 1000.0;
    }

    // 보너스 표기: +22%p, +26.5%p
    public static string BonusText(double bonus)
    {
        return "+" + (bonus * 100.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%p";
    }

    // A 몫 (조합 배율로 받는 보너스, 최대 20%p)
    public static double RandomComboBonus(double k)
    {
        double t = Math.Max(0.0, Math.Min(1.0, (k - 1.0) / (MaxMultiplier - 1.0)));
        return Math.Round(RandomBonusComboMax * Math.Pow(t, 1.5) * 100.0) / 100.0;
    }

    // 무작위로 고를 수 있는 모드 (룰렛이 돌 때 보여 줄 후보)
    public static GameMode[] RandomPool()
    {
        var pool = new List<GameMode>();
        foreach (var info in All) if (InRandomPool(info)) pool.Add(info.Mode);
        return pool.ToArray();
    }

    // 고른 모드들의 점수 배율 (곱, 최대 400%)
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
                           Perfectionist, OneChance, ComboBreak, Strict, StrictPlus,
                           Lazy, Mercy, Clutch, Reverse, OnePlus, Fake;
        public static GaugeKind Gauge = GaugeKind.Normal;
        public static double JudgeScale = 1.0;          // JUST 를 뺀 판정 범위 배율 (HARD · MAX · ULTIMATE)
        public static double JustScale = 1.0;           // JUST 범위 배율 (ULTIMATE 0.65)
        public static double ScoreMultiplier = 1.0;     // 최종 배율 (MODE RANDOM 보너스 포함)
        public static double RandomBonus = 0.0;         // MODE RANDOM 보너스 (0.25 = +25%p, 아니면 0)
        public static bool RandomFresh;                 // MODE RANDOM 을 이번 판에 새로 골랐는가 (true 면 게임 화면에서 고르는 연출)
        public static GameMode[] Modes = new GameMode[0];
    }

    // 채워 나가는 게이지(EASY · NORMAL)인가 — 곡이 끝날 때 70 이상이면 클리어
    public static bool IsFillGauge(GaugeKind g) { return g == GaugeKind.Easy || g == GaugeKind.Normal; }

    // testMode(GAME SETTING) · AUTO PLAY 이면 모드 없음 (게이지는 NORMAL)
    public static void BeginPlay(bool testMode)
    {
        List<GameMode> list = testMode || Locked ? new List<GameMode>() : new List<GameMode>(selected);
        Play.RandomBonus = 0.0;
        Play.RandomFresh = false;
        if (!testMode && !Locked && randomPick)
        {
            // MODE RANDOM: 곡 선택창에서 시작했으면 새로 고름 (다시 하기는 지난 조합 그대로)
            if (randomResult == null) { RollRandom(); Play.RandomFresh = true; }
            list = new List<GameMode>(randomResult);
            Play.RandomBonus = randomResultBonus;
        }
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
        Play.OneChance = list.Contains(GameMode.OneChance);
        Play.ComboBreak = list.Contains(GameMode.ComboBreak);
        Play.Strict = list.Contains(GameMode.Strict);
        Play.StrictPlus = list.Contains(GameMode.StrictPlus);
        Play.Lazy = list.Contains(GameMode.Lazy);
        Play.Mercy = list.Contains(GameMode.Mercy);
        Play.Clutch = list.Contains(GameMode.Clutch);
        Play.Reverse = list.Contains(GameMode.Reverse);
        Play.OnePlus = list.Contains(GameMode.OnePlus);
        Play.Fake = list.Contains(GameMode.Fake);
        Play.Gauge = list.Contains(GameMode.GaugeEasy) ? GaugeKind.Easy
                   : list.Contains(GameMode.GaugeHard) ? GaugeKind.Hard
                   : list.Contains(GameMode.GaugeHardPlus) ? GaugeKind.HardPlus
                   : list.Contains(GameMode.GaugeTempest) ? GaugeKind.Tempest
                   : GaugeKind.Normal;
        Play.JudgeScale = Play.Ultimate ? 0.25 : Play.Maximum ? 1.0 / 3.0 : Play.Hard ? 0.5 : 1.0;
        Play.JustScale = Play.Ultimate ? UltimateJustScale : 1.0;
        Play.ScoreMultiplier = MultiplierOf(list) + Play.RandomBonus;   // 400% 까지 곱한 뒤 보너스를 더함
    }

    // CLUTCH: 위험도(0 ~ 1) → 판정 범위 배율 (0 이면 1배, 1 이면 2.5배, 그 사이는 직선)
    public static double ClutchScale(double danger)
    {
        double k = Math.Max(0.0, Math.Min(1.0, danger));
        return 1.0 + (ClutchMaxScale - 1.0) * k;
    }

    // SHADOW: 판정 시각까지 남은 시간(초) → 투명도
    //  0.45초 전까지 그대로 → 0.06초 전까지 35% 로 옅어짐 → 마지막 20ms 에서 사라짐 (20ms 전까지는 거의 투명해지지 않음)
    public static float ShadowAlpha(float secondsToHit)
    {
        if (secondsToHit >= ShadowFadeStart) return 1f;
        if (secondsToHit <= 0f) return 0f;
        if (secondsToHit < ShadowVanish) return ShadowFloor * (secondsToHit / ShadowVanish);
        if (secondsToHit < ShadowFadeEnd) return ShadowFloor;
        float k = (secondsToHit - ShadowFadeEnd) / (ShadowFadeStart - ShadowFadeEnd);
        k = k * k * (3f - 2f * k);
        return Mathf.Lerp(ShadowFloor, 1f, k);
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

    // 모드가 아닌 그림을 같은 모양으로 (예: 종합 점수 배율 [×])
    public void SetGlyph(Sprite glyph, Color c)
    {
        hasMode = false;
        Glyph.enabled = true;
        Glyph.sprite = glyph;
        Glyph.color = c;
        Ring.color = new Color(c.r, c.g, c.b, 0.55f);
        Bg.color = new Color(c.r * 0.12f + 0.02f, c.g * 0.12f + 0.02f, c.b * 0.12f + 0.035f, 1f);
    }

    // MODE RANDOM: 아직 모르는 칸 (물음표)
    public void SetMystery(Color c)
    {
        hasMode = false;
        Glyph.enabled = true;
        Glyph.sprite = GameModeIconArt.Question();
        Glyph.color = c;
        Ring.color = new Color(c.r, c.g, c.b, 0.55f);
        Bg.color = new Color(c.r * 0.12f + 0.02f, c.g * 0.12f + 0.02f, c.b * 0.12f + 0.035f, 1f);
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
            case GameMode.OnePlus:     // 부활: 한 바퀴 돌아오는 화살표 고리 + 가운데 십자 (다시 살아남)
                add((x, y) => Arc(x, y, 32, 32, 21f, 112f, 412f, 2.7f));        // 위 오른쪽만 비운 고리 (반시계로 돌아옴)
                add((x, y) => Tri(x, y, 38.6f, 53.5f, 50.6f, 52.9f, 42.2f, 41.9f)); // 고리 끝 화살촉
                add((x, y) => Seg(x, y, 32, 22, 32, 42, 3.3f));                  // 십자 세로
                add((x, y) => Seg(x, y, 22, 32, 42, 32, 3.3f));                  // 십자 가로
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
            // ----- 게이지: 세로 막대 테두리 + 채움 높이 (+ 표시) -----
            case GameMode.GaugeEasy:       // 많이 찬 막대 + 70% 선
                add((x, y) => Mathf.Abs(RoundBox(x, y, 32, 32, 11, 25, 4)) - 2f);
                add((x, y) => RoundBox(x, y, 32, 28.5f, 6.5f, 19.5f, 2));   // 클리어 선(70%) 위까지 참
                add((x, y) => Seg(x, y, 16, 42, 48, 42, 1.3f));
                break;
            case GameMode.GaugeNormal:     // 반쯤 찬 막대 + 70% 선
                add((x, y) => Mathf.Abs(RoundBox(x, y, 32, 32, 11, 25, 4)) - 2f);
                add((x, y) => RoundBox(x, y, 32, 18, 6.5f, 9, 2));
                add((x, y) => Seg(x, y, 16, 42, 48, 42, 1.3f));
                break;
            case GameMode.GaugeHard:       // 가득 찬 막대
                add((x, y) => Mathf.Abs(RoundBox(x, y, 32, 32, 11, 25, 4)) - 2f);
                add((x, y) => RoundBox(x, y, 32, 32, 6.5f, 20.5f, 2));
                break;
            case GameMode.GaugeHardPlus:   // 가득 찬 막대 + 옆에 아래 화살표 (깎이기만)
                add((x, y) => Mathf.Abs(RoundBox(x, y, 25, 32, 10, 25, 4)) - 2f);
                add((x, y) => RoundBox(x, y, 25, 32, 5.5f, 20.5f, 2));
                add((x, y) => Seg(x, y, 50, 46, 50, 20, 2.4f));
                add((x, y) => Seg(x, y, 43, 27, 50, 19, 2.4f));
                add((x, y) => Seg(x, y, 57, 27, 50, 19, 2.4f));
                break;
            case GameMode.GaugeTempest:    // 폭풍 (두 갈래 소용돌이 + 가운데 눈)
                for (int arm = 0; arm < 2; arm++)
                {
                    const int K = 24;
                    for (int i = 0; i < K; i++)
                    {
                        float th0 = i / (float)K * 1.55f * Mathf.PI, th1 = (i + 1) / (float)K * 1.55f * Mathf.PI;
                        float r0 = 6f + th0 * 3.9f, r1 = 6f + th1 * 3.9f;
                        float a0 = th0 + arm * Mathf.PI + 0.6f, a1 = th1 + arm * Mathf.PI + 0.6f;
                        float sx0 = 32f + r0 * Mathf.Cos(a0), sy0 = 32f + r0 * Mathf.Sin(a0);
                        float sx1 = 32f + r1 * Mathf.Cos(a1), sy1 = 32f + r1 * Mathf.Sin(a1);
                        float thick = 1.6f + 2.2f * i / K;                // 바깥으로 갈수록 굵게
                        add((x, y) => Seg(x, y, sx0, sy0, sx1, sy1, thick));
                    }
                }
                add((x, y) => Circle(x, y, 32, 32, 5.5f));
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
            case GameMode.StrictPlus:  // STRICT 막대 + 아래에서 올라오는 하한선 + 오른쪽 위 작은 +
                add((x, y) => Mathf.Abs(RoundBox(x, y, 28, 30, 10, 24, 4)) - 2f);
                add((x, y) => RoundBox(x, y, 28, 30, 5.5f, 5, 2));
                add((x, y) => Seg(x, y, 22, 44, 28, 38, 2f));
                add((x, y) => Seg(x, y, 28, 38, 34, 44, 2f));
                add((x, y) => Seg(x, y, 22, 16, 28, 22, 2f));
                add((x, y) => Seg(x, y, 28, 22, 34, 16, 2f));
                add((x, y) => Seg(x, y, 46, 48, 58, 48, 2.4f));
                add((x, y) => Seg(x, y, 52, 42, 52, 54, 2.4f));
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

        return Raster(layers);
    }

    // 자물쇠 (고를 조건이 안 맞는 모드)
    private static Sprite lockSprite, questionSprite;
    public static Sprite Lock()
    {
        if (lockSprite != null) return lockSprite;
        var layers = new List<KeyValuePair<Shape, float>>();
        layers.Add(new KeyValuePair<Shape, float>((x, y) => RoundBox(x, y, 32, 23, 18, 15, 4.5f), 1f));
        layers.Add(new KeyValuePair<Shape, float>((x, y) => y < 36f ? Mathf.Min(Seg(x, y, 21, 30, 21, 40, 3.6f), Seg(x, y, 43, 30, 43, 40, 3.6f))
                                                                   : Mathf.Abs(Circle(x, y, 32, 40, 11)) - 3.6f, 1f));
        lockSprite = Raster(layers);
        return lockSprite;
    }
    // × (결과창 · 게임 화면의 종합 점수 배율 배지)
    private static Sprite multiplySprite;
    public static Sprite Multiply()
    {
        if (multiplySprite != null) return multiplySprite;
        var layers = new List<KeyValuePair<Shape, float>>();
        layers.Add(new KeyValuePair<Shape, float>((x, y) => Seg(x, y, 18, 18, 46, 46, 4f), 1f));
        layers.Add(new KeyValuePair<Shape, float>((x, y) => Seg(x, y, 18, 46, 46, 18, 4f), 1f));
        multiplySprite = Raster(layers);
        return multiplySprite;
    }

    // 물음표 (MODE RANDOM 이 켜져 있을 때 비어 있는 칸)
    public static Sprite Question()
    {
        if (questionSprite != null) return questionSprite;
        var layers = new List<KeyValuePair<Shape, float>>();
        layers.Add(new KeyValuePair<Shape, float>((x, y) => ArcQ(x, y), 1f));
        layers.Add(new KeyValuePair<Shape, float>((x, y) => Seg(x, y, 32, 31, 32, 25, 3.2f), 1f));
        layers.Add(new KeyValuePair<Shape, float>((x, y) => Circle(x, y, 32, 14, 3.8f), 1f));
        questionSprite = Raster(layers);
        return questionSprite;
    }

    private static Sprite Raster(List<KeyValuePair<Shape, float>> layers)
    {
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
    // 원호 (굵기 t): 각도 a0 → a1 (도, 반시계). 끝은 둥글게
    private static float Arc(float x, float y, float cx, float cy, float R, float a0, float a1, float t)
    {
        float dx = x - cx, dy = y - cy;
        float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        float rel = Mathf.Repeat(a - a0, 360f);
        if (rel <= a1 - a0) return Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - R) - t;
        float e0 = a0 * Mathf.Deg2Rad, e1 = a1 * Mathf.Deg2Rad;
        float d0 = Circle(x, y, cx + R * Mathf.Cos(e0), cy + R * Mathf.Sin(e0), t);
        float d1 = Circle(x, y, cx + R * Mathf.Cos(e1), cy + R * Mathf.Sin(e1), t);
        return Mathf.Min(d0, d1);
    }

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