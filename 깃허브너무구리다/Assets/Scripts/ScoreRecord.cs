using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ============================================================
// 곡 · 난이도별 최고 기록 (persistentDataPath/records.json)
//
//  - 결과창(ResultScreen)이 열릴 때 Submit 으로 한 번 저장한다.
//  - AUTO PLAY 판은 저장하지 않는다 (이전 기록만 돌려줌).
//  - 점수 · 정확도 · 클리어 등급 · MAX COMBO 는 각각 따로 최고값을 기억한다.
//    (예: 점수는 낮아도 처음으로 FULL COMBO 를 했다면 클리어 등급만 올라감)
// ============================================================
[Serializable]
public class ScoreRecord
{
    public int SongId;
    public int LevelId;
    public int BestScore;
    public double BestAccuracy;
    public int BestClear = -1;   // -1 기록 없음, 0 FAILED, 1 CLEAR, 2 FULL COMBO, 3 ALL PERFECT
    public int BestMaxCombo;
    public int PlayCount;
    public string LastPlayed;    // yyyy-MM-dd HH:mm

    public ScoreRecord Clone() { return (ScoreRecord)MemberwiseClone(); }
}

public static class ScoreRecords
{
    [Serializable]
    private class FileData { public List<ScoreRecord> Records = new List<ScoreRecord>(); }

    private static FileData data;
    private static string FilePath { get { return Path.Combine(Application.persistentDataPath, "records.json"); } }

    private static void EnsureLoaded()
    {
        if (data != null) return;
        data = new FileData();
        try
        {
            if (File.Exists(FilePath))
            {
                FileData loaded = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath));
                if (loaded != null && loaded.Records != null) data = loaded;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ScoreRecords] 기록 파일을 읽지 못했습니다: " + e.Message);
        }
    }

    // 점수 → 랭크 (결과창 ResultScreen 의 기본 기준과 같음). FAILED 는 F
    private static readonly string[] RankNames = { "SSS", "SS", "S", "AAA", "AA", "A", "B", "C", "D" };
    private static readonly int[] RankScores = { 990000, 970000, 950000, 920000, 880000, 800000, 700000, 600000, 0 };
    public static string RankOf(int score, bool failed)
    {
        if (failed) return "F";
        for (int i = 0; i < RankNames.Length; i++) if (score >= RankScores[i]) return RankNames[i];
        return "D";
    }

    // 기록 없으면 null
    public static ScoreRecord Get(int songId, int levelId)
    {
        EnsureLoaded();
        foreach (var r in data.Records)
            if (r.SongId == songId && r.LevelId == levelId) return r;
        return null;
    }

    // 결과를 반영하고 '반영 전' 기록을 돌려준다 (처음이면 null)
    public static ScoreRecord Submit(ResultData result)
    {
        if (result == null) return null;
        ScoreRecord cur = Get(result.SongId, result.LevelId);
        ScoreRecord before = cur != null ? cur.Clone() : null;
        if (result.IsAutoPlay) return before;

        if (cur == null)
        {
            cur = new ScoreRecord { SongId = result.SongId, LevelId = result.LevelId };
            data.Records.Add(cur);
        }
        cur.PlayCount++;
        cur.LastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        // FAILED 판도 점수는 기록하지만, 클리어 등급은 FAILED(0) 이상으로만 올라감
        cur.BestScore = Math.Max(cur.BestScore, result.Score);
        cur.BestAccuracy = Math.Max(cur.BestAccuracy, result.Accuracy);
        cur.BestClear = Math.Max(cur.BestClear, result.ClearRank);
        cur.BestMaxCombo = Math.Max(cur.BestMaxCombo, result.MaxCombo);
        Save();
        return before;
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ScoreRecords] 기록을 저장하지 못했습니다: " + e.Message);
        }
    }
}