using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================
// 채보 에디터 (새 디자인) — 편의 기능 (AreaChartEditor.cs 와 한 클래스, partial)
//
//  1) 지속 가속 (SPEED RAMP)
//     A + 클릭 = 시작 박,  A + 클릭 = 끝 박  →  시작 속도 · 끝 속도 입력
//     시작 박 ~ 끝 박 동안 노트 속도가 시작 속도 → 끝 속도로 부드럽게 바뀜 (시각 기준 직선)
//     편집 중인 층만 (SHIFT + A = 모든 층),  A + 우클릭 = 그 구간 삭제 (시작만 찍은 상태면 취소)
//     저장 형식: SPEEDRAMP, 시작 마디, 분자, 분모, 끝 마디, 분자, 분모, 층(0 = 모든 층), 시작 속도, 끝 속도
//       + 게임이 읽는 잘게 나눈 SPEED 줄 (1/8 박마다, 끝에 "# RAMP" 표시).
//       게임은 SPEEDRAMP 줄을 무시하고 SPEED 줄만 읽음 (게임 코드 변경 없음).
//       에디터는 "# RAMP" 줄을 읽지 않고 SPEEDRAMP 에서 다시 만듦 → 구간 하나로 편집됨
//
//  2) 복사 · 붙여넣기
//     CTRL + 드래그 = 범위 선택 (CTRL + SHIFT + 드래그 = 선택에 더하기),  CTRL + A = 전체 선택
//     CTRL + C 복사 · CTRL + X 잘라내기 · CTRL + V 마우스 박에 붙여넣기 (CTRL + SHIFT + V = 좌우 반전)
//     DELETE = 선택 삭제,  ESC = 선택 해제,  CTRL + Z 되돌리기 · CTRL + Y (CTRL + SHIFT + Z) 다시
//     롱노트 · 슬라이드는 일부만 걸쳐도 통째로 선택됨. 모든 층의 노트가 층 그대로 복사됨
//
//  3) 롱노트 아트 (8)
//     격자 칸을 칠하면 그 모양대로 롱노트가 만들어짐 (칸 높이 = 지금 박자 단위)
//     좌클릭 드래그 = 칠하기 · 우클릭 드래그 = 지우기 · [ ] = 도장 바꾸기 (숫자 0 ~ 9 · 하트 · 토끼 · 별 · 화살표 · 웃는 얼굴)
//     ENTER = 롱노트로 만들기 (편집 중인 층, RED 켜져 있으면 RED 롱노트) · BACKSPACE = 칸 모두 지우기
//     8 다시 = 만들고 끄기 · ESC = 만들지 않고 끄기
//     같은 박에 시작해서 같은 박에 끝나는 옆 칸들은 넓은 롱노트 하나로 합침
//
//  4) 점점 넓어지는 롱노트 (1칸 → 2칸 → 3칸)
//     롱노트 끝을 머리와 다른 폭으로 끌어 놓으면 자동으로 슬라이드가 되어 점점 넓어짐 / 좁아짐
//     슬라이드 편집(7) 중 점(머리 · 중계점 · 끝) 위에서 W = 폭 +1,  SHIFT + W = 폭 -1
// ============================================================
public partial class AreaChartEditor
{
    // =====================================================================
    //  공통: 마디 분수 계산 (박 위치를 정확하게 옮기기)
    // =====================================================================
    private static long Lcm(long a, long b) { return a / Gcd(a, b) * b; }

    // 마디 분수 num/den (마디 단위, 0 = 1마디 시작) → Pos
    private static Pos FromFrac(long num, long den)
    {
        if (den <= 0) den = 1;
        long m = num / den;
        long n = num - m * den;
        long g = Gcd(n, den);
        return new Pos { M = m + 1, N = n / g, D = den / g };
    }

    // p 를 from → to 만큼 옮긴 위치 (0박보다 앞이면 null)
    private static Pos ShiftPos(Pos p, Pos from, Pos to)
    {
        long pd = Math.Max(1, p.D), fd = Math.Max(1, from.D), td = Math.Max(1, to.D);
        long den = Lcm(Lcm(pd, fd), td);
        long num = (p.M - 1) * den + p.N * (den / pd)
                 - ((from.M - 1) * den + from.N * (den / fd))
                 + ((to.M - 1) * den + to.N * (den / td));
        if (num < 0) return null;
        return FromFrac(num, den);
    }

    private static ANote CloneNote(ANote n)
    {
        return new ANote { M = n.M, N = n.N, D = n.D, Lane = n.Lane, Type = n.Type, Width = n.Width, Layer = n.Layer, Red = n.Red, SlideId = n.SlideId, Curve = n.Curve };
    }

    private static bool CtrlHeld(Keyboard kb) { return kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed); }

    // "마디 12 +1.5박"
    private string BarText(Pos p)
    {
        if (p == null) return "";
        string inBar = (p.Beat - (p.M - 1) * 4.0).ToString("0.##", CultureInfo.InvariantCulture);
        return T($"마디 {p.M} +{inBar}박", $"bar {p.M} +{inBar}");
    }

    // =====================================================================
    //  되돌리기 (CTRL + Z) · 다시 (CTRL + Y)
    //   노트 · 변속 · 지속 가속을 통째로 저장해 둠 (최대 60번)
    // =====================================================================
    private class Snapshot { public List<ANote> Notes; public List<ASpeed> Speeds; public List<ARamp> Ramps; }
    private readonly List<Snapshot> undoStack = new List<Snapshot>();
    private readonly List<Snapshot> redoStack = new List<Snapshot>();
    private const int UndoMax = 60;

    private Snapshot TakeSnapshot()
    {
        return new Snapshot
        {
            Notes = notes.Select(CloneNote).ToList(),
            Speeds = speeds.Select(s => new ASpeed { M = s.M, N = s.N, D = s.D, Layer = s.Layer, Value = s.Value, Mark = s.Mark }).ToList(),
            Ramps = ramps.Select(r => r.Clone()).ToList(),
        };
    }

    private void RestoreSnapshot(Snapshot s)
    {
        notes.Clear(); notes.AddRange(s.Notes);
        speeds.Clear(); speeds.AddRange(s.Speeds);
        ramps.Clear(); ramps.AddRange(s.Ramps);
        draggingWide = null; draggingPoint = null; selSlide = null;
        SortAll();
        RebuildLinks();
    }

    // 바꾸기 바로 전에 부름
    private void PushUndo()
    {
        undoStack.Add(TakeSnapshot());
        if (undoStack.Count > UndoMax) undoStack.RemoveAt(0);
        redoStack.Clear();
    }

    private void Undo()
    {
        if (undoStack.Count == 0) { SetStatus(T("되돌릴 것이 없음", "nothing to undo")); return; }
        redoStack.Add(TakeSnapshot());
        Snapshot s = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        RestoreSnapshot(s);
        SetStatus(T($"되돌리기  (남은 {undoStack.Count})", $"UNDO  ({undoStack.Count} left)"));
    }

    private void Redo()
    {
        if (redoStack.Count == 0) { SetStatus(T("다시 할 것이 없음", "nothing to redo")); return; }
        undoStack.Add(TakeSnapshot());
        Snapshot s = redoStack[redoStack.Count - 1];
        redoStack.RemoveAt(redoStack.Count - 1);
        RestoreSnapshot(s);
        SetStatus(T("다시 하기", "REDO"));
    }

    // =====================================================================
    //  1) 지속 가속 (SPEED RAMP)
    // =====================================================================
    private class ARamp : Pos
    {
        public long EM = 1, EN = 0, ED = 4;          // 끝 위치
        public int Layer;                            // 0 = 모든 층
        public double From = 1.0, To = 1.0;
        public bool Mark;                            // 게임에서 시작 박에 변속선 (M + 클릭)
        public double EndBeat { get { return (EM - 1) * 4.0 + (double)EN / Math.Max(1, ED) * 4.0; } }
        public Pos End { get { return new Pos { M = EM, N = EN, D = ED }; } }
        public ARamp Clone() { return new ARamp { M = M, N = N, D = D, EM = EM, EN = EN, ED = ED, Layer = Layer, From = From, To = To, Mark = Mark }; }
        public bool Affects(int layer) { return Layer == 0 || layer == 0 || Layer == layer; }
    }
    private readonly List<ARamp> ramps = new List<ARamp>();
    private const int RampStepsPerBeat = 8;          // 1/8 박마다 SPEED 줄 하나 (스크롤 위치는 속도를 쌓아서 계산되므로 충분히 부드러움)
    private const string RampTag = "RAMP";           // 만든 SPEED 줄 끝의 표시 "# RAMP"
    private Pos rampStart;                           // A + 클릭으로 찍은 시작 (끝을 기다리는 중)
    private Pos rampEnd;
    private int rampLayer;
    private double rampFromValue;

    // 저장 파일 한 줄 (만든 SPEED 줄) 인지
    private static bool IsRampGeneratedLine(string raw)
    {
        int hash = raw.IndexOf('#');
        return hash >= 0 && string.Equals(raw.Substring(hash + 1).Trim(), RampTag, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryParseRamp(string type, string[] d)
    {
        if (type != "SPEEDRAMP" || d.Length < 10) return false;
        ramps.Add(new ARamp
        {
            M = L(d[1]), N = L(d[2]), D = L(d[3]), EM = L(d[4]), EN = L(d[5]), ED = L(d[6]),
            Layer = int.Parse(d[7].Trim(), CultureInfo.InvariantCulture), From = Dbl(d[8]), To = Dbl(d[9]),
            Mark = d.Length >= 11 && d[10].Trim().ToUpperInvariant() == "MARK",
        });
        return true;
    }

    private static string SpeedValue(double v) { return v.ToString("0.00####", CultureInfo.InvariantCulture); }

    private static string SpeedLine(Pos p, int layer, double v)
    {
        return layer == 0 ? $"SPEED, {p.M}, {p.N}, {p.D}, {SpeedValue(v)}" : $"SPEED, {p.M}, {p.N}, {p.D}, {layer}, {SpeedValue(v)}";
    }

    // 지속 가속 구간 안의 일반 변속인지 (저장할 때 건너뜀 — 구간이 그 층의 속도를 정함)
    private bool InsideRamp(ASpeed s)
    {
        foreach (var r in ramps)
            if ((r.Layer == 0 || r.Layer == s.Layer) && s.Beat >= r.Beat - 1e-6 && s.Beat <= r.EndBeat + 1e-6) return true;
        return false;
    }

    // 시작 ~ 끝을 잘게 나눈 (위치, 속도). 처음 = From, 마지막(끝 박) = To, 사이는 시각 비율로 직선
    private List<KeyValuePair<Pos, double>> RampSteps(ARamp r)
    {
        var list = new List<KeyValuePair<Pos, double>>();
        double len = r.EndBeat - r.Beat;
        if (len <= 1e-9) { list.Add(new KeyValuePair<Pos, double>(new Pos { M = r.M, N = r.N, D = r.D }, r.To)); return list; }
        int n = Mathf.Clamp((int)Math.Round(len * RampStepsPerBeat), 1, 4000);
        long ad = Math.Max(1, r.D), bd = Math.Max(1, r.ED);
        long den = Lcm(ad, bd);
        long aNum = (r.M - 1) * den + r.N * (den / ad), bNum = (r.EM - 1) * den + r.EN * (den / bd);
        double t0 = TimeOf(r.Beat), t1 = TimeOf(r.EndBeat);
        for (int k = 0; k <= n; k++)
        {
            Pos p = FromFrac(aNum * n + (bNum - aNum) * k, den * n);
            double u = k == n ? 1.0 : (t1 - t0 <= 1e-9 ? (double)k / n : (TimeOf(p.Beat) - t0) / (t1 - t0));
            double v = r.From + (r.To - r.From) * Math.Max(0.0, Math.Min(1.0, u));
            list.Add(new KeyValuePair<Pos, double>(p, v));
        }
        return list;
    }

    // 저장: SPEEDRAMP 정의 줄 + 게임이 읽는 SPEED 줄들
    private void SaveRamps(List<string> o)
    {
        foreach (var r in ramps.OrderBy(x => x.Beat))
        {
            o.Add($"SPEEDRAMP, {r.M}, {r.N}, {r.D}, {r.EM}, {r.EN}, {r.ED}, {r.Layer}, {SpeedValue(r.From)}, {SpeedValue(r.To)}" + (r.Mark ? ", MARK" : ""));
            foreach (var st in RampSteps(r)) o.Add(SpeedLine(st.Key, r.Layer, st.Value) + "  # " + RampTag);
        }
    }

    // 그 박 · 그 층의 노트 속도 (일반 변속 + 지속 가속)
    private double SpeedAt(double beat, int layer)
    {
        double v = initialSpeed, lastBeat = double.NegativeInfinity;
        foreach (var sp in speeds)
            if (sp.Beat <= beat + 1e-6 && (sp.Layer == 0 || sp.Layer == layer) && sp.Beat >= lastBeat) { v = sp.Value; lastBeat = sp.Beat; }
        foreach (var r in ramps)
        {
            if (!(r.Layer == 0 || r.Layer == layer) || r.Beat > beat + 1e-6 || r.Beat < lastBeat - 1e-6) continue;
            if (beat >= r.EndBeat) { v = r.To; lastBeat = r.EndBeat; continue; }
            double t0 = TimeOf(r.Beat), t1 = TimeOf(r.EndBeat);
            double u = t1 - t0 <= 1e-9 ? 1.0 : (TimeOf(beat) - t0) / (t1 - t0);
            v = r.From + (r.To - r.From) * Math.Max(0.0, Math.Min(1.0, u));
            lastBeat = r.Beat;
        }
        return v;
    }

    // A + 클릭 / 우클릭
    private void HandleRampClick(Keyboard kb, bool ld, bool rd, Pos p)
    {
        bool all = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        if (rd)
        {
            if (rampStart != null) { rampStart = null; SetStatus(T("지속 가속 취소", "speed ramp cancelled")); return; }
            var hit = ramps.Where(r => hoverBeat >= r.Beat - 1e-6 && hoverBeat <= r.EndBeat + 1e-6 && (all || r.Layer == 0 || r.Layer == editLayer)).ToList();
            if (hit.Count == 0) return;
            PushUndo();
            foreach (var r in hit) ramps.Remove(r);
            SetStatus(T("지속 가속 삭제", "speed ramp removed"));
            return;
        }
        if (!ld) return;
        if (rampStart == null)
        {
            rampStart = p;
            rampLayer = all ? 0 : editLayer;
            SetStatus(T($"지속 가속 시작: 마디 {p.M} — 끝 박에서 A + 클릭", $"SPEED RAMP start: bar {p.M} — A + click the end beat"));
            return;
        }
        Pos a = rampStart, b = p;
        if (b.Beat < a.Beat) { Pos t = a; a = b; b = t; }
        if (b.Beat - a.Beat < 1e-6) { SetStatus(T("시작과 끝이 같은 박 — 다른 박을 고르세요", "start and end are the same beat")); return; }
        rampStart = a; rampEnd = b;
        rampFromValue = SpeedAt(a.Beat - 1e-4, rampLayer == 0 ? editLayer : rampLayer);
        OpenPrompt(PromptKind.RampFrom, a);
        promptBuffer = SpeedValue(rampFromValue);
    }

    private void CommitRamp(double to)
    {
        if (rampStart == null || rampEnd == null) return;
        PushUndo();
        Pos a = rampStart, b = rampEnd;
        int ly = rampLayer;
        // 같은 층 · 같은 구간의 일반 변속 / 겹치는 지속 가속은 지움
        speeds.RemoveAll(s => (ly == 0 || s.Layer == ly) && s.Beat >= a.Beat - 1e-6 && s.Beat <= b.Beat + 1e-6);
        ramps.RemoveAll(r => (ly == 0 || r.Layer == ly || r.Layer == 0) && r.Beat < b.Beat - 1e-6 && r.EndBeat > a.Beat + 1e-6);
        ramps.Add(new ARamp { M = a.M, N = a.N, D = a.D, EM = b.M, EN = b.N, ED = b.D, Layer = ly, From = rampFromValue, To = to });
        rampStart = rampEnd = null;
        SetStatus(T($"지속 가속  x{SpeedValue(rampFromValue)} → x{SpeedValue(to)}  ({(ly == 0 ? "모든 층" : ly + "층")})",
                    $"SPEED RAMP  x{SpeedValue(rampFromValue)} → x{SpeedValue(to)}  ({(ly == 0 ? "all layers" : "L" + ly)})"));
    }

    // (월드) 구간 옆 세로 막대: 진할수록 빠름
    private void DrawRamps(double b0, double b1)
    {
        float x = Right + 0.19f, w = 0.06f;
        foreach (var r in ramps)
        {
            if (r.EndBeat < b0 || r.Beat > b1) continue;
            double lo = Math.Min(r.From, r.To), hi = Math.Max(r.From, r.To);
            const int Pieces = 24;
            for (int i = 0; i < Pieces; i++)
            {
                double s0 = r.Beat + (r.EndBeat - r.Beat) * i / Pieces, s1 = r.Beat + (r.EndBeat - r.Beat) * (i + 1) / Pieces;
                double u = (i + 0.5) / Pieces;
                double v = r.From + (r.To - r.From) * u;
                float k = hi - lo < 1e-9 ? 0.6f : (float)((v - lo) / (hi - lo));
                float y0 = Y(s0), y1 = Y(s1);
                Box(x, (y0 + y1) * 0.5f, w, y1 - y0 + 0.002f, new Color(EvSpeed.r, EvSpeed.g, EvSpeed.b, 0.25f + 0.65f * k), -30);
            }
            // 레인 위에 아주 옅은 보라 띠 (구간이 한눈에)
            float ya = Y(r.Beat), yb = Y(r.EndBeat);
            Box(0f, (ya + yb) * 0.5f, Right - Left, yb - ya, new Color(EvSpeed.r, EvSpeed.g, EvSpeed.b, 0.035f), -36);
        }
        if (rampStart != null && prompt == PromptKind.None)
        {
            Box(0f, Y(rampStart.Beat), Right - Left, 0.04f, new Color(EvSpeed.r, EvSpeed.g, EvSpeed.b, 0.95f), 21);
            if (!playing && hoverLane >= 0)
            {
                float ya = Y(Math.Min(rampStart.Beat, hoverBeat)), yb = Y(Math.Max(rampStart.Beat, hoverBeat));
                Box(0f, (ya + yb) * 0.5f, Right - Left, Mathf.Max(0.01f, yb - ya), new Color(EvSpeed.r, EvSpeed.g, EvSpeed.b, 0.08f), -36);
            }
        }
    }

    // M + 클릭: 그 박의 변속 (SHIFT 없으면 편집 중인 층 + 모든 층 변속, SHIFT = 그 박의 모든 변속), 없으면 마우스가 있는 지속 가속
    private void ToggleSpeedMark(Keyboard kb, Pos p)
    {
        bool all = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        var hitS = speeds.Where(s => s.Same(p) && !InsideRamp(s) && (all || s.Layer == 0 || s.Layer == editLayer)).ToList();
        if (hitS.Count > 0)
        {
            PushUndo();
            bool on = !hitS.Any(s => s.Mark);
            foreach (var s in hitS) s.Mark = on;
            SetStatus(T("변속선 ", "SPEED LINE ") + (on ? "ON" : "OFF") + T($"  (마디 {p.M})", $"  (bar {p.M})"));
            return;
        }
        var hitR = ramps.Where(r => hoverBeat >= r.Beat - 1e-6 && hoverBeat <= r.EndBeat + 1e-6 && (all || r.Layer == 0 || r.Layer == editLayer)).ToList();
        if (hitR.Count > 0)
        {
            PushUndo();
            bool on = !hitR.Any(r => r.Mark);
            foreach (var r in hitR) r.Mark = on;
            SetStatus(T("지속 가속 변속선 ", "RAMP SPEED LINE ") + (on ? "ON" : "OFF"));
            return;
        }
        SetStatus(T("이 박에 변속이 없음 (변속 박 또는 지속 가속 구간에서 M + 클릭)", "no speed change here (M + click a speed beat or ramp)"));
    }

    // (월드) 변속선 미리보기: 게임과 같이 변속 박에 굵은 선 + 위로 그라데이션 (가속 빨강 · 감속 초록, 지속 가속은 시작만)
    private static readonly Color MarkUp = new Color(1f, 0.27f, 0.30f), MarkDown = new Color(0.30f, 1f, 0.48f);
    private void DrawSpeedMarks(double b0, double b1)
    {
        foreach (var s in speeds)
        {
            if (!s.Mark || s.Beat < b0 - 1.0 || s.Beat > b1 || s.Beat <= 1e-6 || InsideRamp(s)) continue;
            double prev = SpeedAt(s.Beat - 1e-4, s.Layer == 0 ? NoteLayer.Top : s.Layer);
            if (Math.Abs(s.Value - prev) > 1e-6) DrawSpeedMark(s.Beat, s.Value > prev);
        }
        foreach (var r in ramps)
        {
            if (!r.Mark || r.Beat < b0 - 1.0 || r.Beat > b1 || r.Beat <= 1e-6) continue;
            double prev = SpeedAt(r.Beat - 1e-4, r.Layer == 0 ? NoteLayer.Top : r.Layer);
            int dir = Math.Abs(r.To - r.From) > 1e-6 ? Math.Sign(r.To - r.From) : Math.Abs(r.From - prev) > 1e-6 ? Math.Sign(r.From - prev) : 0;
            if (dir != 0) DrawSpeedMark(r.Beat, dir > 0);
        }
    }

    private void DrawSpeedMark(double beat, bool up)
    {
        Color c = up ? MarkUp : MarkDown;
        float y = Y(beat), w = Right - Left;
        const int Pieces = 12;
        float h = 1.1f;
        for (int i = 0; i < Pieces; i++)
        {
            float a = Mathf.Pow(1f - (i + 0.5f) / Pieces, 2.2f) * 0.42f;
            Box(0f, y + h * (i + 0.5f) / Pieces, w, h / Pieces + 0.002f, new Color(c.r, c.g, c.b, a), -31);
        }
        Box(0f, y, w, 0.075f, new Color(c.r, c.g, c.b, 0.95f), -26);
    }

    // 이벤트 아이콘에 지속 가속 추가 (CollectEvents 가 부름)
    private void CollectRampEvents()
    {
        foreach (var r in ramps)
        {
            evItems.Add(new EvItem { Beat = r.Beat, Icon = r.To >= r.From ? EvIcon.SpeedUp : EvIcon.SpeedDown, Color = EvSpeed, Layer = r.Layer,
                                     Text = SpeedValue(r.From) + " → " + SpeedValue(r.To) + T("  가속", "  RAMP") + (r.Mark ? T("  · 선", "  · LINE") : "") });
            evItems.Add(new EvItem { Beat = r.EndBeat, Icon = r.To >= r.From ? EvIcon.SpeedUp : EvIcon.SpeedDown, Color = new Color(EvSpeed.r, EvSpeed.g, EvSpeed.b, 0.6f), Layer = r.Layer,
                                     Text = SpeedValue(r.To) + T("  (가속 끝)", "  (ramp end)") });
        }
    }

    // =====================================================================
    //  2) 선택 · 복사 · 붙여넣기
    // =====================================================================
    private bool selDragging;
    private double selBeatA, selBeatB;
    private int selLaneA, selLaneB;
    private bool selAdd;
    private Pos selPosA, selPosB;                            // 범위의 두 끝 (끌기 시작 때의 박자 단위 그대로)
    private readonly List<ANote> clip = new List<ANote>();   // 복사한 노트 (미리보기용 Tail · SlidePts 연결됨)
    private Pos clipAnchor;                                  // 복사한 범위의 시작 박 (붙여넣기 기준)

    private int SelectedCount { get { int c = 0; foreach (var n in notes) if (n.Sel) c++; return c; } }
    private void ClearSelection() { foreach (var n in notes) n.Sel = false; }

    // 롱노트 · 슬라이드는 일부만 골라도 통째로
    private void ExpandSelection()
    {
        if (linksDirty) RebuildLinks();
        foreach (var n in notes.Where(x => x.Sel).ToList())
        {
            ANote h = n.Head ?? n;
            if (h.Tail == null) continue;
            h.Sel = true;
            var pts = PointsOf(h);
            if (pts != null) foreach (var p in pts) p.Sel = true;
        }
    }

    private void FinishSelectionRect()
    {
        double lo = Math.Min(selBeatA, selBeatB), hi = Math.Max(selBeatA, selBeatB);
        int l0 = Math.Min(selLaneA, selLaneB), l1 = Math.Max(selLaneA, selLaneB);
        if (!selAdd) ClearSelection();
        foreach (var n in notes)
        {
            if (n.Beat < lo - 1e-6 || n.Beat > hi + 1e-6) continue;
            if (n.Type != 4 && (n.Lane > l1 || n.Lane + Mathf.Max(1, n.Width) - 1 < l0)) continue;
            n.Sel = true;
        }
        ExpandSelection();
        int c = SelectedCount;
        SetStatus(c > 0 ? T($"{c}개 선택  ·  CTRL + C 복사 · X 잘라내기 · DELETE 삭제", $"{c} selected  ·  CTRL + C copy · X cut · DELETE delete")
                        : T("선택 해제", "selection cleared"));
    }

    private void CopySelection(bool cut)
    {
        if (linksDirty) RebuildLinks();
        var sel = notes.Where(n => n.Sel).OrderBy(n => n.Beat).ToList();
        if (sel.Count == 0) { SetStatus(T("선택한 노트가 없음 (CTRL + 드래그로 선택)", "nothing selected (CTRL + drag)")); return; }
        clip.Clear();
        var map = new Dictionary<ANote, ANote>();
        foreach (var n in sel) { var c = CloneNote(n); map[n] = c; clip.Add(c); }
        foreach (var n in sel)
        {
            if (n.Tail != null && map.TryGetValue(n.Tail, out ANote ct)) map[n].Tail = ct;
            if (n.SlidePts != null) map[n].SlidePts = n.SlidePts.Where(map.ContainsKey).Select(x => map[x]).ToList();
        }
        // 붙여넣기 기준 = 범위 시작 박 (범위보다 앞에서 시작하는 롱노트가 있으면 그 머리, CTRL + A 는 첫 노트)
        Pos first = new Pos { M = sel[0].M, N = sel[0].N, D = sel[0].D };
        clipAnchor = first;
        if (selRectValid && selPosA != null && selPosB != null)
        {
            Pos lo = selPosA.Beat <= selPosB.Beat ? selPosA : selPosB;
            if (lo.Beat < first.Beat) clipAnchor = new Pos { M = lo.M, N = lo.N, D = lo.D };
        }
        if (cut)
        {
            PushUndo();
            notes.RemoveAll(n => n.Sel);
            linksDirty = true;
            SetStatus(T($"{sel.Count}개 잘라냄  ·  CTRL + V 붙여넣기", $"cut {sel.Count}  ·  CTRL + V paste"));
        }
        else SetStatus(T($"{sel.Count}개 복사  ·  마우스를 놓을 박에 두고 CTRL + V (SHIFT = 좌우 반전)", $"copied {sel.Count}  ·  hover a beat and CTRL + V (SHIFT = mirror)"));
    }
    private bool selRectValid;

    private void Paste(bool mirror)
    {
        if (clip.Count == 0 || clipAnchor == null) { SetStatus(T("복사한 노트가 없음 (CTRL + C)", "clipboard is empty (CTRL + C)")); return; }
        Pos target = PosOf(hoverLane >= 0 ? hoverBeat : cursorBeat);
        PushUndo();
        ClearSelection();
        // 슬라이드 번호는 새로 (층마다)
        var idMap = new Dictionary<long, int>();
        int nextId = NewSlideId();
        var added = new List<ANote>();
        int skipped = 0;
        foreach (var c in clip)
        {
            Pos p = ShiftPos(c, clipAnchor, target);
            if (p == null) { skipped++; continue; }
            ANote n = CloneNote(c);
            n.M = p.M; n.N = p.N; n.D = p.D;
            if (mirror && n.Type != 4)
            {
                if (n.IsQ)
                {
                    // 1/4 칸 (레인을 넘는 폭 포함): 1/4 칸 단위로 뒤집음
                    int s0 = Mathf.RoundToInt(n.LL * Lanes.Quarters), e0 = Mathf.RoundToInt(n.RL * Lanes.Quarters), all = EdLanes * Lanes.Quarters;
                    SetCells(n, Mathf.Clamp(all - e0, 0, all - 1), Mathf.Clamp(all - s0, 1, all));
                }
                else n.Lane = Mathf.Clamp(EdLanes - n.Lane - Mathf.Max(1, n.Width), 0, EdLanes - 1);   // 6칸 가운데 기준 (= 4키 가운데 기준)
            }
            if (n.SlideId > 0)
            {
                long key = (long)n.Layer * 1000000L + n.SlideId;
                if (!idMap.TryGetValue(key, out int nid)) { nid = nextId++; idMap[key] = nid; }
                n.SlideId = nid;
            }
            n.Sel = true;
            added.Add(n);
        }
        // 같은 박 · 같은 층 · 겹치는 레인의 기존 노트는 지움 (겹치지 않게)
        foreach (var n in added)
        {
            int nl = n.Lane, nr = n.Lane + Mathf.Max(1, n.Width) - 1;
            notes.RemoveAll(x => !x.Sel && x.Layer == n.Layer && x.Same(n) && (n.Type == 4 ? x.Type == 4 : x.Type != 4 && x.Lane <= nr && x.Lane + Mathf.Max(1, x.Width) - 1 >= nl));
        }
        notes.AddRange(added);
        linksDirty = true;
        selRectValid = false;
        SetStatus(T($"{added.Count}개 붙여넣음  ·  마디 {target.M}" + (mirror ? "  (좌우 반전)" : "") + (skipped > 0 ? $"  ·  0박 앞 {skipped}개 빠짐" : ""),
                    $"pasted {added.Count}  ·  bar {target.M}" + (mirror ? "  (mirrored)" : "") + (skipped > 0 ? $"  ·  {skipped} before beat 0 skipped" : "")));
    }

    private void DeleteSelection()
    {
        int c = SelectedCount;
        if (c == 0) return;
        PushUndo();
        notes.RemoveAll(n => n.Sel);
        linksDirty = true;
        SetStatus(T($"{c}개 삭제", $"deleted {c}"));
    }

    // (월드) 선택 표시 · 끄는 중인 범위 · 붙여넣기 미리보기
    private void DrawSelection(Keyboard kb)
    {
        Color sc = new Color(1f, 1f, 1f, 0.9f);
        foreach (var n in notes)
        {
            if (!n.Sel) continue;
            float y = Y(n.Beat);
            if (y < -6f || y > 6f) continue;
            if (n.Type == 4) { Box(0f, y, Right - Left, 0.06f, new Color(1f, 1f, 1f, 0.5f), 30); continue; }
            float x0 = Left + n.LL * LaneW + 0.03f, x1 = Left + n.RL * LaneW - 0.03f;
            float h = 0.17f;
            SlideLine(x0, y - h, x1, y - h, 0.03f, sc, 30);
            SlideLine(x0, y + h, x1, y + h, 0.03f, sc, 30);
            SlideLine(x0, y - h, x0, y + h, 0.03f, sc, 30);
            SlideLine(x1, y - h, x1, y + h, 0.03f, sc, 30);
        }
        if (selDragging)
        {
            double lo = Math.Min(selBeatA, selBeatB), hi = Math.Max(selBeatA, selBeatB);
            int l0 = Math.Min(selLaneA, selLaneB), l1 = Math.Max(selLaneA, selLaneB);
            float x0 = Left + l0 * LaneW, x1 = Left + (l1 + 1) * LaneW;
            float y0 = Y(lo) - 0.12f, y1 = Y(hi) + 0.12f;
            Box((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, x1 - x0, y1 - y0, new Color(UiAccent.r, UiAccent.g, UiAccent.b, 0.10f), 29);
            Color ec = new Color(UiAccent.r, UiAccent.g, UiAccent.b, 0.9f);
            SlideLine(x0, y0, x1, y0, 0.03f, ec, 31); SlideLine(x0, y1, x1, y1, 0.03f, ec, 31);
            SlideLine(x0, y0, x0, y1, 0.03f, ec, 31); SlideLine(x1, y0, x1, y1, 0.03f, ec, 31);
        }
        // CTRL 을 누르고 있으면 붙여넣을 자리 미리보기
        if (!selDragging && CtrlHeld(kb) && clip.Count > 0 && clipAnchor != null && hoverLane >= 0 && !playing)
        {
            bool mirror = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            double d = hoverBeat - clipAnchor.Beat;
            Color gc = new Color(UiAccent.r, UiAccent.g, UiAccent.b, 0.32f);
            foreach (var c in clip)
            {
                float y = Y(c.Beat + d);
                if (c.Type == 4) { Box(0f, y, Right - Left, 0.035f, gc, 28); continue; }
                float ll = mirror ? EdLanes - c.RL : c.LL, rr = mirror ? EdLanes - c.LL : c.RL;   // 좌우 반전 (6칸 가운데 기준 = 4키 가운데 기준)
                float cx = Left + (ll + rr) * 0.5f * LaneW;
                Note(cx, y, GameWidthOf(c), SimpleGameSkin.NoteHeight * (c.Head != null ? 0.5f : 1f), gc, 28);
                // 롱노트 몸통 (점 사이를 곧게)
                var pts = c.SlidePts ?? (c.Tail != null ? new List<ANote> { c, c.Tail } : null);
                if (pts == null) continue;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    ANote a = pts[i], b = pts[i + 1];
                    float aw = a.RL - a.LL, bw = b.RL - b.LL;
                    float al = mirror ? EdLanes - a.RL : a.LL, bl = mirror ? EdLanes - b.RL : b.LL;
                    const int Steps = 6;
                    for (int k = 0; k < Steps; k++)
                    {
                        float u0 = (float)k / Steps, u1 = (float)(k + 1) / Steps;
                        float um = SlideCurve.Ease(a.Curve, (u0 + u1) * 0.5f);
                        float l = Mathf.Lerp(al, bl, um), r = Mathf.Lerp(al + aw, bl + bw, um);
                        float ya = Y(a.Beat + d + (b.Beat - a.Beat) * u0), yb = Y(a.Beat + d + (b.Beat - a.Beat) * u1);
                        Box(Left + (l + r) * 0.5f * LaneW, (ya + yb) * 0.5f, (r - l) * LaneW - 0.1f, yb - ya + 0.005f, new Color(gc.r, gc.g, gc.b, 0.16f), 27);
                    }
                }
            }
        }
    }

    // =====================================================================
    //  3) 롱노트 아트 (8)
    // =====================================================================
    private bool artMode;
    private static readonly Color ArtUiColor = new Color(1f, 0.72f, 0.88f, 1f);   // 왼쪽 도구 '롱노트 아트' 켜짐 색
    private readonly HashSet<long> artCells = new HashSet<long>();   // 키 = 칸 × 24 + 열 (칸 = 1/96 박, 열 = 레인 × 4 + 1/4 칸)
    private const int ArtCols = Lanes.Max * Lanes.Quarters;          // 열 24개 (4키 구간은 가운데 16개만)
    private const int ArtTPB = 96;                                   // 1박 = 96칸 (모든 박자 단위 1 ~ 48 이 나누어떨어짐)
    private int artPreset;                                           // 0 = 붓, 1 ~ = 도장
    private int artLastLane = -1, artLastRow = int.MinValue;

    private struct ArtStamp { public string Ko, En; public string[] Rows; }   // Rows: 위 줄 먼저, X = 칠함
    private static readonly ArtStamp[] ArtStamps =
    {
        new ArtStamp { Ko = "붓 (직접 칠하기)", En = "BRUSH (paint)", Rows = null },
        new ArtStamp { Ko = "숫자 0", En = "DIGIT 0", Rows = new[] { "XXX", "X.X", "X.X", "X.X", "XXX" } },
        new ArtStamp { Ko = "숫자 1", En = "DIGIT 1", Rows = new[] { ".X.", "XX.", ".X.", ".X.", "XXX" } },
        new ArtStamp { Ko = "숫자 2", En = "DIGIT 2", Rows = new[] { "XXX", "..X", "XXX", "X..", "XXX" } },
        new ArtStamp { Ko = "숫자 3", En = "DIGIT 3", Rows = new[] { "XXX", "..X", "XXX", "..X", "XXX" } },
        new ArtStamp { Ko = "숫자 4", En = "DIGIT 4", Rows = new[] { "X.X", "X.X", "XXX", "..X", "..X" } },
        new ArtStamp { Ko = "숫자 5", En = "DIGIT 5", Rows = new[] { "XXX", "X..", "XXX", "..X", "XXX" } },
        new ArtStamp { Ko = "숫자 6", En = "DIGIT 6", Rows = new[] { "XXX", "X..", "XXX", "X.X", "XXX" } },
        new ArtStamp { Ko = "숫자 7", En = "DIGIT 7", Rows = new[] { "XXX", "..X", "..X", "..X", "..X" } },
        new ArtStamp { Ko = "숫자 8", En = "DIGIT 8", Rows = new[] { "XXX", "X.X", "XXX", "X.X", "XXX" } },
        new ArtStamp { Ko = "숫자 9", En = "DIGIT 9", Rows = new[] { "XXX", "X.X", "XXX", "..X", "XXX" } },
        new ArtStamp { Ko = "하트", En = "HEART", Rows = new[] { "X..X", "XXXX", "XXXX", ".XX." } },
        new ArtStamp { Ko = "토끼", En = "RABBIT", Rows = new[] { "X..X", "X..X", "X..X", "XXXX", "X..X", "XXXX", ".XX.", "XXXX", "XXXX" } },
        new ArtStamp { Ko = "별", En = "STAR", Rows = new[] { ".XX.", "XXXX", ".XX.", "X..X" } },
        new ArtStamp { Ko = "화살표", En = "ARROW", Rows = new[] { ".XX.", "XXXX", ".XX.", ".XX.", ".XX." } },
        new ArtStamp { Ko = "웃는 얼굴", En = "SMILE", Rows = new[] { "X..X", "X..X", "....", "X..X", ".XX." } },
    };

    private int ArtStep { get { return ArtTPB / SnapDivs[snapIndex]; } }   // 칸 한 줄 = 지금 박자 단위
    private static long ArtKey(int col, int tick) { return (long)tick * ArtCols + col; }
    private int HoverCol { get { return hoverLane < 0 ? -1 : hoverLane * Lanes.Quarters + hoverSub; } }

    private void ToggleArtMode()
    {
        if (!artMode)
        {
            artMode = true;
            if (slideMode) ToggleSlideMode();
            ClearSelection();
            SetStatus(T("롱노트 아트: 칸을 칠하고 ENTER = 롱노트로 · [ ] 도장 · 우클릭 지우기 · ESC 취소",
                        "LONG NOTE ART: paint cells, ENTER = make long notes · [ ] stamps · R-click erase · ESC cancel"));
        }
        else
        {
            int made = artCells.Count > 0 ? CommitArt() : 0;
            artMode = false;
            if (made == 0) SetStatus(T("롱노트 아트 끔", "LONG NOTE ART OFF"));
        }
    }

    private void CancelArt()
    {
        artCells.Clear();
        artMode = false;
        SetStatus(T("롱노트 아트 취소 (만들지 않음)", "LONG NOTE ART cancelled"));
    }

    // 1/4 칸 열 하나의 한 줄 칠하기 (4키 구간의 막힌 바깥 칸은 칠하지 않음)
    private void PaintArtRow(int col, int row, bool on)
    {
        if (col < 0 || col >= ArtCols || row < 0) return;
        int s = ArtStep;
        if (on && !LaneUsable(col / Lanes.Quarters, row * Step)) return;
        for (int t = row * s; t < (row + 1) * s; t++)
        {
            if (on) artCells.Add(ArtKey(col, t)); else artCells.Remove(ArtKey(col, t));
        }
    }
    // 레인 하나(1/4 칸 4개) 칠하기 — 도장용
    private void PaintArtLane(int lane, int row, bool on)
    {
        for (int qq = 0; qq < Lanes.Quarters; qq++) PaintArtRow(lane * Lanes.Quarters + qq, row, on);
    }

    // 도장의 왼쪽 레인 · 아래 줄
    private void StampOrigin(ArtStamp st, out int lane0, out int row0)
    {
        int w = st.Rows[0].Length;
        lane0 = Mathf.Clamp(hoverLane, 0, EdLanes - w);
        row0 = (int)Math.Floor(hoverRawBeat / Step + 1e-9);
    }

    private void Stamp(ArtStamp st)
    {
        StampOrigin(st, out int lane0, out int row0);
        int h = st.Rows.Length;
        for (int i = 0; i < h; i++)
        {
            string r = st.Rows[i];
            int row = row0 + (h - 1 - i);                       // 위 줄 = 나중 박
            for (int c = 0; c < r.Length; c++) if (r[c] == 'X') PaintArtLane(lane0 + c, row, true);   // 도장 한 칸 = 레인 하나
        }
    }

    private void HandleArtMouse(Mouse mouse, bool ld)
    {
        bool lHeld = mouse.leftButton.isPressed, rHeld = mouse.rightButton.isPressed;
        if (!lHeld && !rHeld) { artLastLane = -1; artLastRow = int.MinValue; return; }
        if (hoverLane < 0) return;
        int row = (int)Math.Floor(hoverRawBeat / Step + 1e-9);
        if (row < 0) return;
        ArtStamp st = ArtStamps[artPreset];
        if (st.Rows != null && lHeld) { if (ld) Stamp(st); return; }   // 도장은 누를 때 한 번
        bool on = lHeld;
        // 빠르게 끌어도 빈칸이 안 생기게 (같은 열이면 지난 칸 ~ 지금 칸 모두)  — 열 = 1/4 칸
        int col = HoverCol;
        if (artLastLane == col && artLastRow != int.MinValue)
        {
            int a = Math.Min(artLastRow, row), b = Math.Max(artLastRow, row);
            for (int r = a; r <= b; r++) PaintArtRow(col, r, on);
        }
        else PaintArtRow(col, row, on);
        artLastLane = col; artLastRow = row;
    }

    // 칸 → 열(1/4 칸)별 이어진 구간 [시작, 끝)
    private List<KeyValuePair<int, int>>[] ArtRuns()
    {
        var ticks = new List<int>[ArtCols];
        for (int i = 0; i < ArtCols; i++) ticks[i] = new List<int>();
        foreach (long k in artCells) ticks[(int)(k % ArtCols)].Add((int)(k / ArtCols));
        var runs = new List<KeyValuePair<int, int>>[ArtCols];
        for (int lane = 0; lane < ArtCols; lane++)
        {
            runs[lane] = new List<KeyValuePair<int, int>>();
            var t = ticks[lane];
            t.Sort();
            int i = 0;
            while (i < t.Count)
            {
                int s = t[i], e = s + 1;
                i++;
                while (i < t.Count && t[i] == e) { e++; i++; }
                runs[lane].Add(new KeyValuePair<int, int>(s, e));
            }
        }
        return runs;
    }

    // 칸 → 롱노트. 돌려주는 값 = 만든 롱노트 수
    private int CommitArt()
    {
        if (artCells.Count == 0) { SetStatus(T("칠한 칸이 없음", "no cells painted")); return 0; }
        PushUndo();
        if (linksDirty) RebuildLinks();
        var runs = ArtRuns();
        var used = new HashSet<long>();
        int made = 0;
        long den = ArtTPB * 4L;   // 마디 분수 분모
        // 같은 박에 시작 · 끝나는 이웃 열(1/4 칸)들을 묶어서:
        //  레인을 꽉 채우는 부분 → 레인 전체 롱노트 (이어진 레인은 넓은 롱노트 하나), 레인 일부 → 1/4 칸 롱노트 (RED 는 레인 전체만)
        int headType = redMode ? 12 : dragMode ? NoteTypes.DragLongHead : NoteTypes.LongHead;
        for (int col = 0; col < ArtCols; col++)
        {
            foreach (var run in runs[col])
            {
                if (used.Contains(ArtKey(col, run.Key))) continue;
                used.Add(ArtKey(col, run.Key));
                int c1 = col;
                while (c1 + 1 < ArtCols && runs[c1 + 1].Exists(r => r.Key == run.Key && r.Value == run.Value) && !used.Contains(ArtKey(c1 + 1, run.Key)))
                {
                    c1++;
                    used.Add(ArtKey(c1, run.Key));
                }
                Pos ph = FromFrac(run.Key, den), pt = FromFrac(run.Value, den);
                int fullStart = -1, fullCount = 0;
                for (int lane = col / Lanes.Quarters; lane <= c1 / Lanes.Quarters; lane++)
                {
                    int a = Math.Max(col, lane * Lanes.Quarters) - lane * Lanes.Quarters;
                    int b = Math.Min(c1, lane * Lanes.Quarters + Lanes.Quarters - 1) - lane * Lanes.Quarters;
                    bool full = (a == 0 && b == Lanes.Quarters - 1) || redMode;
                    if (full)
                    {
                        if (fullStart < 0) fullStart = lane;
                        fullCount++;
                        continue;
                    }
                    if (fullCount > 0) { made += AddArtLong(fullStart, fullCount, 0, Lanes.Quarters, ph, pt, headType); fullStart = -1; fullCount = 0; }
                    made += AddArtLong(lane, 1, a, b - a + 1, ph, pt, headType);
                }
                if (fullCount > 0) made += AddArtLong(fullStart, fullCount, 0, Lanes.Quarters, ph, pt, headType);
            }
        }
        artCells.Clear();
        linksDirty = true;
        SetStatus(T($"롱노트 아트: 롱노트 {made}개 만듦 ({editLayer}층)  ·  CTRL + Z 되돌리기", $"LONG NOTE ART: {made} long notes (L{editLayer})  ·  CTRL + Z undo"));
        return made;
    }

    // 아트 롱노트 하나 (머리 + 끝). subW < 4 면 1/4 칸
    private int AddArtLong(int lane, int width, int sub, int subW, Pos ph, Pos pt, int headType)
    {
        bool q = subW < Lanes.Quarters;
        float x0 = q ? lane + sub * 0.25f : lane, x1 = q ? lane + (sub + subW) * 0.25f : lane + width;
        ClearSpan(x0, x1, editLayer, ph.Beat, pt.Beat);
        notes.Add(new ANote { M = ph.M, N = ph.N, D = ph.D, Lane = lane, Width = q ? 1 : width, Layer = editLayer, Red = redMode, Type = headType, Sub = q ? sub : 0, SubW = q ? subW : Lanes.Quarters });
        notes.Add(new ANote { M = pt.M, N = pt.N, D = pt.D, Lane = lane, Width = q ? 1 : width, Layer = editLayer, Red = redMode, Type = redMode ? 13 : NoteTypes.LongTail, Sub = q ? sub : 0, SubW = q ? subW : Lanes.Quarters });
        return 1;
    }

    // 그 층 · 레인 범위 [x0, x1) (레인 단위, 1/4 칸 가능)에서 [b0, b1] 와 겹치는 노트를 지움 (롱노트 · 슬라이드는 통째로 — 짝이 엉키지 않게)
    private void ClearSpan(int l0, int l1, int layer, double b0, double b1) { ClearSpan((float)l0, l1 + 1f, layer, b0, b1); }
    private void ClearSpan(float x0, float x1, int layer, double b0, double b1)
    {
        var remove = new HashSet<ANote>();
        foreach (var n in notes)
        {
            if (n.Layer != layer || n.Type == 4 || n.Head != null) continue;   // 끝 · 중계점은 머리에서 처리
            var pts = PointsOf(n);
            double s = n.Beat, e = n.Tail != null ? n.Tail.Beat : n.Beat;
            if (e < b0 - 1e-6 || s > b1 + 1e-6) continue;
            float nl = n.LL, nr = n.RL;
            if (pts != null) foreach (var p in pts) { nl = Math.Min(nl, p.LL); nr = Math.Max(nr, p.RL); }
            if (nr <= x0 + 1e-4f || nl >= x1 - 1e-4f) continue;
            remove.Add(n);
            if (pts != null) foreach (var p in pts) remove.Add(p);
        }
        // 짝 없는 끝 · 중계점도
        foreach (var n in notes)
            if (n.Layer == layer && n.Type != 4 && n.Head == null && !NoteTypes.IsLongHead(n.Type) && (n.Type == 3 || n.Type == 13 || NoteTypes.IsSlideRelay(n.Type))
                && n.Beat >= b0 - 1e-6 && n.Beat <= b1 + 1e-6 && n.LL < x1 - 1e-4f && n.RL > x0 + 1e-4f) remove.Add(n);
        if (remove.Count > 0) notes.RemoveAll(remove.Contains);
    }

    // (월드) 칠한 칸 · 도장 미리보기 · 붓 위치
    private void DrawArt()
    {
        if (!artMode) return;
        var runs = ArtRuns();
        float cw = LaneW / Lanes.Quarters;
        for (int col = 0; col < ArtCols; col++)
        {
            Color lc = redMode ? SimpleGameSkin.RedColor : EdLaneColor(col / Lanes.Quarters);
            float cx = Left + (col + 0.5f) * cw;
            foreach (var r in runs[col])
            {
                float y0 = Y(r.Key / (double)ArtTPB), y1 = Y(r.Value / (double)ArtTPB);
                if (y1 < -6f || y0 > 6f) continue;
                Box(cx, (y0 + y1) * 0.5f, cw - 0.03f, y1 - y0, new Color(lc.r, lc.g, lc.b, 0.55f), 24);
                Box(cx, y0, cw - 0.03f, 0.03f, new Color(1f, 1f, 1f, 0.8f), 25);
                Box(cx, y1, cw - 0.03f, 0.03f, new Color(1f, 1f, 1f, 0.8f), 25);
            }
        }
        if (playing || prompt != PromptKind.None || hoverLane < 0) return;
        int row = (int)Math.Floor(hoverRawBeat / Step + 1e-9);
        if (row < 0) return;
        ArtStamp st = ArtStamps[artPreset];
        Color gc = new Color(1f, 1f, 1f, 0.22f);
        if (st.Rows == null)
        {
            float y0 = Y(row * Step), y1 = Y((row + 1) * Step);
            Box(Left + (HoverCol + 0.5f) * cw, (y0 + y1) * 0.5f, cw - 0.03f, y1 - y0, gc, 23);   // 붓 = 1/4 칸
            return;
        }
        StampOrigin(st, out int lane0, out int row0);
        int h = st.Rows.Length;
        for (int i = 0; i < h; i++)
        {
            string r = st.Rows[i];
            int rr = row0 + (h - 1 - i);
            float y0 = Y(rr * Step), y1 = Y((rr + 1) * Step);
            for (int c = 0; c < r.Length; c++)
                if (r[c] == 'X') Box(LaneX(lane0 + c), (y0 + y1) * 0.5f, LaneW - 0.08f, y1 - y0 - 0.02f, gc, 23);
        }
    }

    // =====================================================================
    //  4) 점점 넓어지는 롱노트
    // =====================================================================
    // 일반 모드에서 롱노트 끝(또는 머리)을 다른 폭으로 끌어 놓았을 때: 슬라이드로 바꿔 폭이 점점 바뀌게
    private void AutoSlideOnWidthChange(ANote changed)
    {
        RebuildLinks();
        ANote head = changed.Head ?? (changed.Tail != null ? changed : null);
        if (head == null || head.SlideId > 0 || head.Tail == null) return;
        if (Mathf.Max(1, head.Width) == Mathf.Max(1, head.Tail.Width)) return;
        EnsureSlide(head);
        SetStatus(T($"점점 넓어지는 롱노트: {head.Width}칸 → {head.Tail.Width}칸 (슬라이드)", $"growing long note: {head.Width} → {head.Tail.Width} lanes (slide)"));
    }

    // 슬라이드 편집 중 W / SHIFT + W : 마우스 아래 점의 폭
    private void ChangePointWidth(int delta)
    {
        Vector2 mp = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        float wx = cam != null ? cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 10f)).x : 0f;
        if (linksDirty) RebuildLinks();
        ANote head = LongNoteAt(hoverBeat, wx, out ANote point);
        if (head == null || point == null) { SetStatus(T("폭을 바꿀 점(머리 · 중계점 · 끝) 위에서 W", "hover a point (head · relay · end) and press W")); return; }
        // 모든 점: 폭을 1/4 칸씩 (W = +1/4, SHIFT + W = −1/4). 4/4 → 5/4 (1 + 1/4 칸) → 6/4 … 처럼 레인을 넘어가도 됨
        //  왼쪽 끝은 그대로, 오른쪽 끝이 판 끝에 닿으면 왼쪽으로 밀림
        const int Q = Lanes.Quarters;
        int s0 = Mathf.RoundToInt(point.LL * Q), w0 = Mathf.RoundToInt(point.RL * Q) - s0;
        int qw = Mathf.Clamp(w0 + delta, 1, EdLanes * Q);
        if (qw == w0) return;
        int ns = Mathf.Clamp(s0, 0, EdLanes * Q - qw);
        for (int i = ns / Q; i <= (ns + qw - 1) / Q; i++)
            if (!LaneUsable(i, point.Beat)) { SetStatus(T("4키 구간의 막힌 칸까지는 못 늘려요", "can't extend into a closed lane (4-key section)")); return; }
        PushUndo();
        EnsureSlide(head);
        TrySetPointCells(point, ns, ns + qw);
        selSlide = head;
        RebuildLinks();
        NormalizeSlide(head);
        string frac = qw % Q == 0 ? T($"{qw / Q}칸", $"{qw / Q} lane(s)") : qw > Q ? T($"{qw / Q} + {qw % Q}/4 칸", $"{qw / Q} + {qw % Q}/4") : $"{qw}/4" + T(" 칸", "");
        SetStatus(T("점 폭 ", "point width ") + frac + T("  ·  앞뒤 점과 폭이 다르면 그 사이에서 점점 넓어짐 / 좁아짐", "  ·  the body grows / shrinks toward it"));
    }

    // =====================================================================
    //  키 · 마우스 (AreaChartEditor.UpdateKeys / UpdateMouse 가 부름)
    // =====================================================================
    // true = 이 프레임의 나머지 키 처리를 건너뜀
    private bool HandleToolKeys(Keyboard kb)
    {
        bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        if (CtrlHeld(kb))
        {
            if (!playing)
            {
                if (kb.zKey.wasPressedThisFrame) { if (shift) Redo(); else Undo(); }
                if (kb.yKey.wasPressedThisFrame) Redo();
                if (kb.cKey.wasPressedThisFrame) CopySelection(false);
                if (kb.xKey.wasPressedThisFrame) CopySelection(true);
                if (kb.vKey.wasPressedThisFrame) Paste(shift);
                if (kb.aKey.wasPressedThisFrame)
                {
                    foreach (var n in notes) n.Sel = true;
                    selRectValid = false;
                    SetStatus(T($"전체 선택 {notes.Count}개", $"selected all {notes.Count}"));
                }
            }
            if (kb.sKey.wasPressedThisFrame) Save();
            return true;
        }
        if (playing) return false;

        if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) ToggleArtMode();
        if (artMode)
        {
            if (kb.leftBracketKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame)
            {
                int n = ArtStamps.Length;
                artPreset = (artPreset + (kb.rightBracketKey.wasPressedThisFrame ? 1 : -1) + n) % n;
                SetStatus(T("도장: ", "STAMP: ") + T(ArtStamps[artPreset].Ko, ArtStamps[artPreset].En) + T("   ([ ] 바꾸기)", "   ([ ] change)"));
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { CommitArt(); return true; }
            if (kb.backspaceKey.wasPressedThisFrame) { artCells.Clear(); SetStatus(T("칠한 칸 모두 지움", "cells cleared")); return true; }
            if (kb.escapeKey.wasPressedThisFrame) { CancelArt(); return true; }
        }
        if (slideMode && kb.wKey.wasPressedThisFrame) ChangePointWidth(shift ? -1 : 1);
        if (kb.deleteKey.wasPressedThisFrame || (kb.backspaceKey.wasPressedThisFrame && !artMode)) DeleteSelection();
        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (rampStart != null) { rampStart = null; SetStatus(T("지속 가속 취소", "speed ramp cancelled")); return true; }
            if (SelectedCount > 0) { ClearSelection(); SetStatus(T("선택 해제", "selection cleared")); return true; }
        }
        return false;
    }

    // 끌기 중인 범위 선택 (HUD 위로 나가도 이어짐). true = 처리함
    private bool UpdateSelectionDrag(Mouse mouse)
    {
        if (!selDragging) return false;
        selBeatB = hoverBeat;
        selPosB = PosOf(hoverBeat);
        if (hoverLane >= 0) selLaneB = hoverLane;
        if (!mouse.leftButton.isPressed)
        {
            selDragging = false;
            selRectValid = true;
            FinishSelectionRect();
        }
        return true;
    }

    // 레인 위 클릭 (HUD 밖). true = 처리함 (노트 놓기 등은 하지 않음)
    private bool HandleToolMouse(Keyboard kb, Mouse mouse, bool ld, bool rd, Pos p)
    {
        if (CtrlHeld(kb))
        {
            if (ld)
            {
                selDragging = true;
                selAdd = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                selBeatA = selBeatB = hoverBeat;
                selPosA = selPosB = p;
                selLaneA = selLaneB = hoverLane >= 0 ? hoverLane : 0;
            }
            return true;
        }
        if (kb.aKey.isPressed) { if (ld || rd) HandleRampClick(kb, ld, rd, p); return true; }
        if (artMode) { HandleArtMouse(mouse, ld); return true; }
        return false;
    }

    // (월드) Draw 가 부름
    private void DrawTools()
    {
        var kb = Keyboard.current;
        float bottom = -5.5f, top = 5.5f;
        double b0 = cursorBeat + (bottom - HitY) / beatSpacing, b1 = cursorBeat + (top - HitY) / beatSpacing;
        DrawRamps(b0, b1);
        DrawSpeedMarks(b0, b1);
        DrawArt();
        DrawSelection(kb);
    }

    // (HUD 글자) DrawWorldLabels 가 부름
    private void DrawToolLabels()
    {
        if (rampStart != null && prompt == PromptKind.None)
            Label(Left - 0.18f, Y(rampStart.Beat) + 0.25f, T("가속 시작", "RAMP START"), EvSpeed, false);
        if (artMode && !playing && hoverLane >= 0)
        {
            ArtStamp st = ArtStamps[artPreset];
            int row = (int)Math.Floor(hoverRawBeat / Step + 1e-9);
            int h = st.Rows != null ? st.Rows.Length : 1;
            Label(Left - 0.18f, Y((row + h) * Step) + 0.15f, T(st.Ko, st.En), new Color(1f, 1f, 1f, 0.75f), false);
        }
    }
}
