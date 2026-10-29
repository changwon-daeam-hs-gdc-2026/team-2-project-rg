using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// ============================================================
// 채보 파일의 전체 노트 수 계산 (= 풀콤보 시 MAX COMBO)
//  - 에디터에서 저장할 때 호출되어 songs.json 의 NoteCount_EASY / HARD / INSANE 에 기록된다.
//  - GameManager.LoadChart 와 같은 규칙으로 읽는다:
//      노트   : 일반(1, 11) 1개, 롱노트 머리(2, 12) 1개 + 연결된 꼬리 1개, 프리키 노트 1개
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

    private class Bpm { public double Beats, Time, Value; }
    private class Raw { public string Type; public long Measure, Num, Den; public string Value; }
    private class Note { public double Time; public int Lane, Type, Layer, Width = 1; public bool IsRed, IsFreeKey; public Note Next; }

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
            if (type == "FREEKEY" && data.Length >= 4 && data[1].Trim() == "Linear") continue;

            if (data.Length >= 5)
            {
                if (type == "FREEKEY")
                {
                    if (data.Length >= 10) raws.Add(new Raw { Type = "FREEKEY", Measure = L(data[7]), Num = L(data[8]), Den = L(data[9]) });
                    else if (data.Length >= 9) raws.Add(new Raw { Type = "FREEKEY", Measure = L(data[6]), Num = L(data[7]), Den = L(data[8]) });
                }
                else
                {
                    string val = string.Join(",", data.Skip(4).Select(s => s.Trim()));
                    raws.Add(new Raw { Type = type, Measure = L(data[1]), Num = L(data[2]), Den = L(data[3]), Value = val });
                }
            }
            else if (data.Length >= 4)
            {
                raws.Add(new Raw { Type = type, Measure = L(data[1]), Num = L(data[2]), Den = 4, Value = data[3].Trim() });
            }
        }

        // ---- 2) BPM 표 ----
        var bpms = new List<Bpm> { new Bpm { Beats = 0, Time = 0, Value = initialBpm } };
        foreach (var b in raws.Where(e => e.Type == "BPM").OrderBy(e => Beats(e)))
            bpms.Add(new Bpm { Beats = Beats(b), Value = double.Parse(b.Value, Inv) });

        double curTime = 0, curBeats = 0, curBpm = initialBpm;
        for (int i = 1; i < bpms.Count; i++)
        {
            bpms[i].Time = curTime + (bpms[i].Beats - curBeats) * (60.0 / curBpm);
            curTime = bpms[i].Time; curBeats = bpms[i].Beats; curBpm = bpms[i].Value;
        }
        bpms = bpms.OrderBy(b => b.Time).ToList();

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

        foreach (var e in raws.Where(e => e.Type != "BPM"))
        {
            double t = exactTime(Beats(e));
            if (e.Type == "FREEKEY")
            {
                notes.Add(new Note { Time = t, IsFreeKey = true, Layer = NoteLayer.Top });
            }
            else if (e.Type == "NOTE")
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
                if (n.IsRed && n.Lane != lane) continue;
                int ly = Math.Max(1, Math.Min(2, n.Layer));
                if (n.Type == 2 || n.Type == 12) last[ly] = n;
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
            if (n.IsFreeKey || n.Type == 1 || n.Type == 11) r.Notes++;
            else if (n.Type == 2 || n.Type == 12)
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
        var n = new Note { Time = t, Lane = lane, Type = type, Layer = layer, IsRed = isRed, Width = isRed ? width : 1 };
        notes.Add(n);
        if (isRed) { for (int w = 0; w < width; w++) if (lane + w < 4) byLane[lane + w].Add(n); }
        else byLane[lane].Add(n);
    }

    private static double Beats(Raw e) { return ((e.Measure - 1) * 4.0) + ((double)e.Num / Math.Max(1, e.Den)) * 4.0; }
    private static long L(string s) { return long.Parse(s.Trim()); }
}