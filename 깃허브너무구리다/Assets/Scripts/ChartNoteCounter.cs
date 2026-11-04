using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// ============================================================
// 채보 파일의 전체 노트 수 계산 (= 풀콤보 시 MAX COMBO)
//  - 에디터에서 저장할 때 호출되어 songs.json 의 NoteCount_EASY / HARD / INSANE 에 기록된다.
//  - GameManager.LoadChart 와 같은 규칙으로 읽는다:
//      노트   : 탭(1, 11) · 화살표(7, 8) 1개, 롱노트 머리(2, 12) 1개 + 연결된 꼬리 1개
//      BPM    : BPM 줄은 누적, TIMING 줄은 그 박의 시각을 고정 (게임과 같음)
//      롱노트 : (레인, 층) 단위로 머리-꼬리를 연결 (빨간 노트는 기준 레인에서만, 빨강끼리만)
//      틱     : LongNoteTickRule (BPM 에 따라 간격이 달라짐)
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
    private class Note { public double Time; public int Lane, Type, Layer, Width = 1; public bool IsRed; public Note Next; }

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
        var byLane = new List<Note>[4];
        for (int i = 0; i < 4; i++) byLane[i] = new List<Note>();

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
                    for (int lane = 0; lane < val.Length && lane < 4; lane++)
                    {
                        if (val[lane] == '0' || !char.IsDigit(val[lane])) continue;
                        int nt = val[lane] - '0';
                        if (nt == 5 || nt == 6) continue;
                        var n = new Note { Time = t, Lane = lane, Type = nt, Layer = NoteLayer.Top };
                        notes.Add(n); byLane[lane].Add(n);
                    }
                }
                else
                {
                    for (int lane = 0; lane < lanes.Length && lane < 4; lane++)
                    {
                        NoteCellParser.SplitCell(lanes[lane], out string top, out string bottom);
                        AddToken(top, lane, t, NoteLayer.Top, notes, byLane);
                        AddToken(bottom, lane, t, NoteLayer.Bottom, notes, byLane);
                    }
                }
            }
        }

        // ---- 4) 롱노트 연결 ----
        for (int lane = 0; lane < 4; lane++)
        {
            var sorted = byLane[lane].OrderBy(n => n.Time).ThenByDescending(n => n.Layer).ToList();
            Note[] last = new Note[3];
            foreach (var n in sorted)
            {
                if (n.Lane != lane) continue;          // 넓은 노트(RED · 넓은 일반)는 기준 레인에서만 연결
                int ly = Math.Max(1, Math.Min(2, n.Layer));
                if (NoteTypes.IsLongHead(n.Type)) last[ly] = n;
                else if ((n.Type == 3 || n.Type == 13) && last[ly] != null)
                {
                    if (last[ly].IsRed == n.IsRed) last[ly].Next = n;
                    last[ly] = null;
                }
            }
        }

        // ---- 5) 세기 ----
        var r = new Result { Ok = true };
        foreach (var n in notes)
        {
            if (NoteTypes.IsTapLike(n.Type)) r.Notes++;
            else if (NoteTypes.IsLongHead(n.Type))
            {
                r.Notes += n.Next != null ? 2 : 1;
                if (n.Next != null) r.Ticks += LongNoteTickRule.Count(n.Time, n.Next.Time, bpmAt);
            }
        }
        return r;
    }

    private static void AddToken(string token, int lane, double t, int layer, List<Note> notes, List<Note>[] byLane)
    {
        if (!NoteCellParser.TryParseToken(token, out int type, out int width, out bool isRed)) return;
        var n = new Note { Time = t, Lane = lane, Type = type, Layer = layer, IsRed = isRed, Width = width };
        notes.Add(n);
        for (int w = 0; w < width; w++) if (lane + w < 4) byLane[lane + w].Add(n);
    }

    private static double Beats(Raw e) { return ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0; }
    private static long L(string s) { return long.Parse(s.Trim()); }
}