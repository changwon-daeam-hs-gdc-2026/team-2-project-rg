using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// ============================================================
// 채보 파일의 전체 노트 수 계산 (= 풀콤보 시 MAX COMBO)
//  - 에디터에서 저장할 때 호출되어 songs.json 의 NoteCount_EASY / HARD / INSANE / SPECIAL 에 기록된다.
//  - GameManager.LoadChart 와 같은 규칙으로 읽는다:
//      노트   : 탭(1, 11) · 드래그(9) 1개, 롱노트 머리(2, 12, 10) 1개 + 연결된 꼬리 1개 (예전 화살표 7 · 8 은 탭으로 셈)
//      BPM    : BPM 줄은 누적, TIMING 줄은 그 박의 시각을 고정 (게임과 같음)
//      롱노트 : (레인, 층) 단위로 머리-꼬리를 연결 (빨간 노트는 기준 레인에서만, 빨강끼리만)
//      틱     : LongNoteTickRule (BPM 에 따라 간격이 달라짐)
//      슬라이드: 같은 번호(~번호, 같은 층)의 점들 = 머리 1 + 중계점 하나마다 1 + 끝 1 + 머리~끝 틱
// ============================================================
public static class ChartNoteCounter
{
    public struct Result
    {
        public bool Ok;
        public string Error;
        public int Notes;
        public int Ticks;
        public int Total { get { return Notes + Ticks; } }
    }

    private class Bpm { public double Beats, Time, Value; public double Anchor = double.NaN; }
    private class Raw { public string Type; public long Measure, Num, Den; public string Value; }
    private class Note { public double Time; public int Lane, Type, Layer, Width = 1, SlideId, Sub = 0, SubW = 4; public bool IsRed; public Note Next; }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static Result CountFile(string path)
    {
        if (!File.Exists(path)) return new Result { Ok = false, Error = "파일 없음: " + path };
        try { return CountLines(File.ReadAllLines(path)); }
        catch (Exception e) { return new Result { Ok = false, Error = e.Message }; }
    }

    public static Result CountLines(IEnumerable<string> lines)
    {
        double initialBpm = 120.0;
        var raws = new List<Raw>();

        // ---- 1) 줄 읽기 (GameManager.LoadChart 와 같은 분기) ----
        foreach (string rawLine in lines)
        {
            int commentIndex = rawLine.IndexOf('#');
            string line = commentIndex >= 0 ? rawLine.Substring(0, commentIndex).Trim() : rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] data = line.Split(',');
            string type = data[0].Trim().ToUpper();

            if (type == "OFFSET" || type == "SONGSTARTOFFSET" || type == "DEATHAREA" || type == "AUDIOOFFSET" || type == "UI") continue;
            if (type == "SPEED" && data.Length == 2) continue;
            if (line.StartsWith("NoteSpeed:", StringComparison.OrdinalIgnoreCase)) continue;
            if (type == "BPM" && data.Length == 2) { initialBpm = double.Parse(data[1].Trim(), Inv); continue; }
            if (type == "FREEKEY" || type == "MUSICOFFSET" || type == "PREVIEW") continue;   // 프리키는 삭제된 기능

            if (data.Length >= 5)
            {
                string val = string.Join(",", data.Skip(4).Select(s => s.Trim()));
                raws.Add(new Raw { Type = type, Measure = L(data[1]), Num = L(data[2]), Den = L(data[3]), Value = val });
            }
            else if (data.Length >= 4)
            {
                raws.Add(new Raw { Type = type, Measure = L(data[1]), Num = L(data[2]), Den = 4, Value = data[3].Trim() });
            }
        }

        // ---- 2) BPM 표 ----
        var bpms = new List<Bpm> { new Bpm { Beats = 0, Time = 0, Value = initialBpm } };
        foreach (var b in raws.Where(e => e.Type == "BPM" || e.Type == "TIMING").OrderBy(e => Beats(e)))
        {
            string[] v = b.Value.Split(',');
            var nb = new Bpm { Beats = Beats(b), Value = double.Parse(v[0].Trim(), Inv) };
            if (b.Type == "TIMING" && v.Length >= 2) nb.Anchor = double.Parse(v[1].Trim(), Inv) / 1000.0;
            if (nb.Beats <= 1e-9) { bpms[0].Value = nb.Value; continue; }
            bpms.Add(nb);
        }

        double curTime = 0, curBeats = 0, curBpm = bpms[0].Value;
        for (int i = 1; i < bpms.Count; i++)
        {
            bpms[i].Time = !double.IsNaN(bpms[i].Anchor) ? bpms[i].Anchor : curTime + (bpms[i].Beats - curBeats) * (60.0 / curBpm);
            curTime = bpms[i].Time; curBeats = bpms[i].Beats; curBpm = bpms[i].Value;
        }
        bpms = bpms.OrderBy(b => b.Beats).ToList();

        Func<double, double> exactTime = beats =>
        {
            var last = bpms.LastOrDefault(b => b.Beats <= beats + 0.000001) ?? bpms[0];
            return last.Time + (beats - last.Beats) * (60.0 / last.Value);
        };
        Func<double, double> bpmAt = t =>
        {
            var last = bpms.LastOrDefault(b => b.Time <= t + 1e-6);
            return last != null ? last.Value : bpms[0].Value;
        };

        // ---- 3) 노트 ----
        var notes = new List<Note>();
        var byLane = new List<Note>[Lanes.Max];
        for (int i = 0; i < Lanes.Max; i++) byLane[i] = new List<Note>();
        // 6키 확장 채보(LANES 6 · 레인 칸 6개인 줄)면 4키 줄(ABCD)은 1 ~ 4번 레인 (게임과 같은 규칙)
        bool six = ChartLaneScan.UsesSixLanesLines(lines);
        int laneCount = six ? Lanes.Max : 4;

        foreach (var e in raws.Where(e => e.Type != "BPM" && e.Type != "TIMING"))
        {
            double t = exactTime(Beats(e));
            if (e.Type == "NOTE")
            {
                string[] lanes = e.Value.Split(',');
                if (lanes.Length == 1 && !lanes[0].Contains("_") && !lanes[0].Contains("("))
                {
                    // 구형 압축 포맷 (예: 1000)
                    string val = lanes[0].Trim();
                    int off0 = six ? Lanes.FourOffset : 0;
                    for (int lane = 0; lane < val.Length && lane < 4; lane++)
                    {
                        if (val[lane] == '0' || !char.IsDigit(val[lane])) continue;
                        int nt = NoteTypes.Normalize(val[lane] - '0');
                        if (nt == 5 || nt == 6) continue;
                        var n = new Note { Time = t, Lane = lane + off0, Type = nt, Layer = NoteLayer.Top };
                        notes.Add(n); byLane[lane + off0].Add(n);
                    }
                }
                else
                {
                    int off = six && lanes.Length < Lanes.Max ? Lanes.FourOffset : 0;
                    for (int lane = 0; lane < lanes.Length && lane + off < laneCount; lane++)
                    {
                        string[] byLayer = NoteCellParser.SplitLayers(lanes[lane]);   // 1 ~ 5층
                        for (int L = NoteLayer.Min; L <= NoteLayer.Max; L++)
                            if (byLayer[L] != null) foreach (string tk in byLayer[L].Split('+')) AddToken(tk, lane + off, t, L, notes, byLane, laneCount);
                    }
                }
            }
        }

        // ---- 4) 롱노트 연결 ----
        //  슬라이드: 같은 번호 · 같은 층의 점들을 시간 순으로 → 처음 = 머리, 마지막 = 끝, 가운데 = 중계점
        var r = new Result { Ok = true };
        foreach (var g in notes.Where(n => n.SlideId > 0).GroupBy(n => n.Layer * 100000 + n.SlideId))
        {
            var pts = g.OrderBy(n => n.Time).ToList();
            if (pts.Count < 2) { r.Notes++; continue; }   // 점이 하나뿐이면 그냥 노트 하나 (게임과 같음)
            r.Notes += pts.Count;                            // 머리 + 중계점들 + 끝
            r.Ticks += LongNoteTickRule.Count(pts[0].Time, pts[pts.Count - 1].Time, bpmAt);
        }
        for (int lane = 0; lane < laneCount; lane++)
        {
            var sorted = byLane[lane].OrderBy(n => n.Time).ThenByDescending(n => n.Layer).ToList();
            var last = new Dictionary<int, Note>();   // 층 × 100 + 1/4 칸 (시작 × 10 + 폭) — 1/4 롱노트는 같은 칸끼리만
            foreach (var n in sorted)
            {
                if (n.Lane != lane) continue;          // 넓은 노트(RED · 넓은 일반)는 기준 레인에서만 연결
                if (n.SlideId > 0) continue;            // 슬라이드는 위에서 따로 셈
                int key = NoteLayer.Clamp(n.Layer) * 10000 + (n.SubW != 4 || n.Sub != 0 ? n.Sub * 100 + n.SubW : 4);
                Note prev;
                last.TryGetValue(key, out prev);
                if (NoteTypes.IsLongHead(n.Type)) last[key] = n;
                else if ((n.Type == 3 || n.Type == 13) && prev != null)
                {
                    if (prev.IsRed == n.IsRed) prev.Next = n;
                    last.Remove(key);
                }
            }
        }

        // ---- 5) 세기 ----
        foreach (var n in notes)
        {
            if (n.SlideId > 0) continue;
            if (NoteTypes.IsTapLike(n.Type)) r.Notes++;
            else if (NoteTypes.IsLongHead(n.Type))
            {
                r.Notes += n.Next != null ? 2 : 1;
                if (n.Next != null) r.Ticks += LongNoteTickRule.Count(n.Time, n.Next.Time, bpmAt);
            }
        }
        return r;
    }

    private static void AddToken(string token, int lane, double t, int layer, List<Note> notes, List<Note>[] byLane, int laneCount)
    {
        string core = NoteCellParser.SplitSlide(token, out int slideId, out int curve);
        core = NoteCellParser.SplitQuarter(core, out int sub, out int subW);   // 1/4 칸 표시
        if (!NoteCellParser.TryParseToken(core, out int type, out int width, out bool isRed)) return;
        if (subW != 4 || sub != 0) width = 1;
        var n = new Note { Time = t, Lane = lane, Type = NoteTypes.Normalize(type), Layer = layer, IsRed = isRed, Width = width, SlideId = slideId, Sub = sub, SubW = subW };   // 예전 화살표(7 · 8) → 탭
        notes.Add(n);
        for (int w = 0; w < width; w++) if (lane + w < laneCount) byLane[lane + w].Add(n);
    }

    private static double Beats(Raw e) { return ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0; }
    private static long L(string s) { return long.Parse(s.Trim()); }
}