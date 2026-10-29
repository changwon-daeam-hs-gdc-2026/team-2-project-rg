using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 설정 값
//  - 개발용(Mode, EditorSongid)      : StreamingAssets/data/settings.json 에서만 읽는다
//  - 사용자 설정(나머지 전부)         : 설정창에서 바꾸면 persistentDataPath/user_settings.json 에 저장되고,
//                                      다음 실행 때 settings.json 값 위에 덮어써진다 (SettingsStore 참고)
// ============================================================
[Serializable]
public class SettingsData
{
    // ----- 개발용 -----
    public int Mode = 2; 
    public int EditorSongid = 1; 

    // ----- 오프셋 (초 단위) -----
    public double AudioOffset = 0.0;   // + : 음악을 노트보다 일찍 재생 (음악이 늦게 들릴 때)
    public double JudgeOffset = 0.0;   // + : 판정 시점을 늦춤 (SLOW 가 많을 때)

    // ----- 게임플레이 -----
    public double SettingSpeed = 1.0; 
    public int judgeoverrange = 10;    // FAST/SLOW 를 표시하는 최소 오차(ms)
    public bool ShowFastSlow = true;
    public int GameSkin = 0;           // 게임 화면 UI: 0 = DEFAULT, 1 = SIMPLE (GameSkins 참고)
    public float LaneOpacity = 0.85f;  // SIMPLE 레인 배경 불투명도 (0 = 완전 투명, 1 = 불투명)
    public bool ShowInfoUI = true;     // 게임 화면 Information UI (곡 정보 · 난이도 · 시간 · 점수 · 배속 · 진행 막대)
    public string[] LaneKeys = { "D", "F", "J", "K" }; // UnityEngine.InputSystem.Key 이름

    // ----- 사운드 (0 ~ 1) -----
    public float MasterVolume = 1f;
    public float MusicVolume = 1f;     // 게임 음악 + 곡 선택 미리듣기
    public float HitSoundVolume = 1f;  // 키 탭 사운드
    public bool HitSoundOn = true;

    // ----- 화면 -----
    public int FrameLimit = 0;         // 0 = 제한 없음
    public bool VSync = false;
    public bool ShowFps = false;
    public int ScreenMode = -1;        // -1 = 바꾸지 않음, 0 = 전체 화면, 1 = 테두리 없는 창, 2 = 창 모드
    public int ResolutionWidth = 0;    // 0 = 바꾸지 않음
    public int ResolutionHeight = 0;
}

[Serializable]
public class SongData
{
    public int Id;
    public string Name;
    public string SubName; 
    public string Difficulty_EASY;
    public string Difficulty_HARD;
    public string Difficulty_INSANE;
    public string Composer;
    public string illustration; 
    public string Vocal;
    // BPM 은 글자로 그대로 표시: "BPM": "132" 또는 "BPM": "132-250"
    //  (예전처럼 숫자로 적혀 있어도 SongJson.Normalize 가 글자로 바꿔서 읽음)
    public string BPM;

    // 난이도별 전체 노트 수 (= 풀콤보 시 MAX COMBO = 노트 + 롱노트 중간 틱)
    //  songs.json 예) "NoteCount_EASY": 412, "NoteCount_HARD": 806, "NoteCount_INSANE": 1234
    //  값은 게임을 한 번 플레이하면 콘솔에 정확한 수가 찍힌다 ([NoteCount] 로그). 0 이면 NoteCount 를 대신 사용.
    //  * FULL COMBO / ALL PERFECT 판정은 이 값이 아니라 채보에서 직접 센 수로 한다 (json 이 틀려도 판정은 정확).
    public int NoteCount;          // (구) 난이도 구분 없는 값 — 호환용
    public int NoteCount_EASY;
    public int NoteCount_HARD;
    public int NoteCount_INSANE;
    public int HasMV;
    public int ExistSongbar;
}

// songs.json 읽기 전 손질
public static class SongJson
{
    // "BPM": 132  →  "BPM": "132"  (숫자로 적힌 예전 형식도 글자로 읽히도록)
    public static string Normalize(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        return System.Text.RegularExpressions.Regex.Replace(json,
            "(\"BPM\"\\s*:\\s*)(-?[0-9]+(?:\\.[0-9]+)?)", "$1\"$2\"");
    }
}

// ============================================================
// 결과창으로 넘겨주는 한 판의 결과 (GameManager → CoreManager → ResultManager)
// ============================================================
[Serializable]
public class ResultData
{
    public int SongId;
    public string SongName;
    public string SubName;
    public string Composer;         // 작곡가
    public int LevelId;
    public string DifficultyName;   // EASY / HARD / INSANE
    public string DifficultyValue;  // 예: "13"

    public int Score;               // 0 ~ 1,000,000
    public double Accuracy;         // 0 ~ 100 (%)

    public int Excellent;
    public int Great;
    public int Good;
    public int Bad;
    public int Miss;
    public int MaxCombo;
    public int TotalNotes;          // 채보 전체 노트 수 (롱노트 중간 틱 포함)
    public bool IsFullCombo;        // MAX COMBO = 전체 노트 수
    public bool IsAllPerfect;       // + EXCELLENT 수 = 전체 노트 수 (표기: ALL PERFECT)
    public bool IsFailed;           // LIFE 가 0 이 되어 중간에 끝남 (SIMPLE 스킨)

    // 전체 판정 오차(ms). FAST = 음수(-), SLOW = 양수(+)
    public double AverageErrorMs;
    public int ErrorSampleCount;

    public bool IsAutoPlay;

    // ----- 결과창 그래프용 (GameManager 가 판정마다 기록) -----
    public int Fast;                    // PERFECT 를 뺀 판정 중 빨리 친 수
    public int Slow;                    // PERFECT 를 뺀 판정 중 늦게 친 수
    public float[] TimelineProgress;    // 판정 시점의 곡 진행도 (0 ~ 1)
    public byte[] TimelineJudge;        // 0 PERFECT  1 GREAT  2 GOOD  3 BAD  4 MISS
    public float[] LifeHistory;         // 판정 직후 LIFE (0 ~ 100)
    public float[] ErrorsMs;            // 타이밍 오차 ms (FAST -, SLOW +)
    public bool HasLife;                // LIFE 를 쓰는 판 (SIMPLE 스킨)
    public float FinalLife = 100f;
    public float FailedAtProgress = -1f; // FAILED 된 진행도 (아니면 -1)
    public float LengthSeconds;
    public bool IsMv;
    public double Speed = 1.0;

    // 화면에 보여 줄 클리어 등급 (높을수록 좋음): 0 FAILED  1 CLEAR  2 FULL COMBO  3 ALL PERFECT
    public int ClearRank { get { return IsFailed ? 0 : (IsAllPerfect ? 3 : (IsFullCombo ? 2 : 1)); } }

    // 에디터에서 결과 씬만 단독 실행해 볼 때 쓰는 샘플 (그래프까지 가짜 데이터로 채움)
    public static ResultData CreateSample()
    {
        ResultData r = new ResultData
        {
            SongId = 1, SongName = "Glow Embrace", SubName = "", Composer = "Composer Name", LevelId = 3,
            DifficultyName = "INSANE", DifficultyValue = "13",
            Score = 987654, Accuracy = 98.76,
            Excellent = 812, Great = 40, Good = 7, Bad = 2, Miss = 3,
            MaxCombo = 640, TotalNotes = 864, IsFullCombo = false, IsAllPerfect = false,
            AverageErrorMs = -3.4, ErrorSampleCount = 864, IsAutoPlay = false,
            Fast = 28, Slow = 21, HasLife = true, LengthSeconds = 128f, Speed = 4.5
        };
        System.Random rnd = new System.Random(4);
        int n = r.TotalNotes;
        r.TimelineProgress = new float[n];
        r.TimelineJudge = new byte[n];
        r.LifeHistory = new float[n];
        System.Collections.Generic.List<float> errs = new System.Collections.Generic.List<float>();
        float life = 100f;
        int[] left = { r.Excellent, r.Great, r.Good, r.Bad, r.Miss };
        for (int i = 0; i < n; i++)
        {
            r.TimelineProgress[i] = (i + 0.5f) / n;
            int k = 0;
            double roll = rnd.NextDouble() * (n - i);
            double acc = 0;
            for (int j = 0; j < 5; j++) { acc += left[j]; if (roll < acc) { k = j; break; } }
            left[k]--;
            r.TimelineJudge[i] = (byte)k;
            if (k >= 3) life = Mathf.Max(5f, life - (k == 4 ? 10f : 6f)); else life = Mathf.Min(100f, life + (100f - life) * 0.05f);
            r.LifeHistory[i] = life;
            if (k < 4)
            {
                double g = Math.Sqrt(-2.0 * Math.Log(1.0 - rnd.NextDouble())) * Math.Cos(2.0 * Math.PI * rnd.NextDouble());
                errs.Add((float)(g * (k == 0 ? 12.0 : 38.0) - 3.0));
            }
        }
        r.FinalLife = life;
        r.ErrorsMs = errs.ToArray();
        return r;
    }
}

// 난이도 번호(1: EASY, 2: HARD, 3: INSANE) → 이름 / 수치 / 색
public static class DifficultyHelper
{
    public static string GetName(int levelId)
    {
        switch (levelId) { case 1: return "EASY"; case 3: return "INSANE"; default: return "HARD"; }
    }

    // 게임 화면용 짧은 표기: EZ / HD / INS
    public static string GetShortName(int levelId)
    {
        switch (levelId) { case 1: return "EZ"; case 3: return "INS"; default: return "HD"; }
    }

    public static string GetValue(SongData song, int levelId)
    {
        if (song == null) return "";
        switch (levelId) { case 1: return song.Difficulty_EASY; case 3: return song.Difficulty_INSANE; default: return song.Difficulty_HARD; }
    }

    // 난이도별 노트 수 (없으면 구 NoteCount)
    public static int GetNoteCount(SongData song, int levelId)
    {
        if (song == null) return 0;
        int v = levelId == 1 ? song.NoteCount_EASY : (levelId == 3 ? song.NoteCount_INSANE : song.NoteCount_HARD);
        return v > 0 ? v : song.NoteCount;
    }

    public static Color GetColor(int levelId)
    {
        string hex = levelId == 1 ? "#5ce1e6" : (levelId == 3 ? "#ff3131" : "#ff751f");
        Color c = Color.white;
        ColorUtility.TryParseHtmlString(hex, out c);
        return c;
    }
}

// ============================================================
// 롱노트 중간 틱 규칙 (게임 / 에디터 미리보기 / 노트 수 계산이 모두 이 규칙을 쓴다)
//  기본 간격: BPM 200 이하 1박에 3번, 300 이하 2번, 그 이상 1번  → IntervalMultiplier 배 만큼 늘림
//  IntervalMultiplier = 2 : 틱 수가 절반 (예: 8분마다 1콤보 → 4분마다 1콤보)
//  첫 틱은 머리 + 간격, 끝(꼬리)에서 EndMargin 초 이내의 틱은 만들지 않는다.
// ============================================================
public static class LongNoteTickRule
{
    public const double IntervalMultiplier = 2.0;
    public const double EndMargin = 0.05;

    public static double Interval(double bpm)
    {
        if (bpm <= 0.0) bpm = 120.0;
        double ticksPerBeat = (bpm <= 200.0) ? 3.0 : ((bpm <= 300.0) ? 2.0 : 1.0);
        return (60.0 / bpm) / ticksPerBeat * IntervalMultiplier;
    }

    // 롱노트 하나를 끝까지 눌렀을 때 나오는 틱 수
    public static int Count(double headTime, double endTime, Func<double, double> bpmAtTime)
    {
        double t = headTime + Interval(bpmAtTime(headTime));
        int count = 0;
        while (t < endTime - EndMargin && count < 100000)
        {
            count++;
            t += Interval(bpmAtTime(t));
        }
        return count;
    }
}

public class SpeedChange { public double Time; public double Multiplier; }
public class BpmChange { public double Time; public double Bpm; public double Beats; }
public class StopEventData { public double Time; }
public class StartEventData { public double Time; }

public class UIEventData 
{ 
    public string TargetUI; 
    public string Action; 
    public double StartTime; 
    public double Duration; 
}

public class LinearEventData 
{
    public string Action; 
    public int LineNumber; 
    public double StartTime;
    public double Duration;
    public double Value1; 
    public double Value2; 
}

public class NoteData
{
    public double HitTime;    
    public int Lane;           
    public int Type;          
    public bool IsHit;         
    public NoteData NextLongNote = null; 
    public bool isTimeLogged = true;

    public bool IsFreeKey = false;
    public int FreeKeyType = 0; 
    public double Rotation = 0;
    public string Direction = "Top"; 
    public int LineNumber = 1; 
    public double StartPos = 0;
    public double TargetPos = 0;

    public bool IsRedNote = false;
    public int Width = 1; 

    // 층 정보 (NoteLayer.Bottom = 1층: 괄호 안 / NoteLayer.Top = 2층: 괄호 밖)
    // 기존 채보(괄호 없음)는 전부 Top(2층)으로 취급되어 동작이 바뀌지 않는다.
    public int Layer = 2;
}

// ============================================================
// 노트 층(Layer) 정의
//   채보 예) NOTE, 2, 5, 8, 11_3(12), 0, 0, 0
//   - 괄호 밖  "11_3" : 2층 (위에 보임)
//   - 괄호 안  "12"   : 1층 (2층에 가려짐)
//   1층/2층 노트는 서로 롱노트 연결(시작-끝)이 되지 않는다.
// ============================================================
public static class NoteLayer
{
    public const int Bottom = 1;
    public const int Top = 2;

    // SpriteRenderer.sortingOrder : 2층의 모든 요소(몸통 포함)가 1층의 모든 요소보다 위
    //   1층 몸통 2 < 1층 머리 3 < 2층 몸통 4 < 2층 머리 5
    public static int HeadOrder(int layer) { return layer == Bottom ? 3 : 5; }
    public static int BodyOrder(int layer) { return layer == Bottom ? 2 : 4; }
}

public static class NoteCellParser
{
    // "11_3(12)" -> top = "11_3", bottom = "12"
    // "1"        -> top = "1",    bottom = null
    // "0(2)"     -> top = "0",    bottom = "2"
    public static void SplitCell(string cell, out string top, out string bottom)
    {
        cell = cell == null ? "" : cell.Trim();
        top = cell;
        bottom = null;

        int open = cell.IndexOf('(');
        if (open < 0) return;

        int close = cell.IndexOf(')', open + 1);
        top = cell.Substring(0, open).Trim();
        bottom = (close > open ? cell.Substring(open + 1, close - open - 1) : cell.Substring(open + 1)).Trim();

        if (string.IsNullOrEmpty(top)) top = "0";
        if (bottom.Length == 0) bottom = null;
    }

    // "11_3" -> type 11, width 3, red / "2" -> type 2, width 1, not red / "0" 또는 빈 값 -> false
    public static bool TryParseToken(string token, out int type, out int width, out bool isRed)
    {
        type = 0; width = 1; isRed = false;
        if (string.IsNullOrWhiteSpace(token)) return false;
        token = token.Trim();
        if (token == "0") return false;

        if (token.Contains("_"))
        {
            string[] parts = token.Split('_');
            if (parts.Length < 2) return false;
            if (!int.TryParse(parts[0].Trim(), out type)) return false;
            if (!int.TryParse(parts[1].Trim(), out width)) return false;
            isRed = true;
            return type != 0;
        }

        if (!int.TryParse(token, out type)) return false;
        return type != 0;
    }
}

// ============================================================
// 롱노트 몸통(RED) 렌더링 도우미
//  - 몸통 스프라이트를 통째로 세로로 늘리면, 스프라이트 양 끝의 페이드(투명) 영역과 장식도 같이 늘어나
//    길이가 길수록 양 끝이 크게 투명해지는 문제가 생긴다.
//  - 그래서 몸통을 9-slice(Sliced)로 그린다: 위/아래 끝 영역은 길이와 무관하게 '고정 비율'로 유지하고
//    가운데만 늘린다. 메쉬의 크기는 (머리 중심 ~ 꼬리 중심) 구간에 정확히 맞춘다.
//  - 런타임에 피벗을 정중앙으로 한 복사본 스프라이트를 만들어 피벗 위치와 상관없이 구간이 어긋나지 않게 한다.
// ============================================================
public static class LongBodyHelper
{
    // 스프라이트 임포터에 Border가 설정돼 있지 않을 때, 위/아래 각각 '늘리지 않고 고정할' 높이 비율.
    // (페이드 구간이 더 길다면 이 값을 키우세요. 0.0 ~ 0.49)
    public static float EdgeFraction = 0.25f;

    private static readonly Dictionary<Sprite, Sprite> slicedCache = new Dictionary<Sprite, Sprite>();

    public static Sprite GetSlicedSprite(Sprite src)
    {
        if (src == null || src.texture == null) return src;
        if (slicedCache.TryGetValue(src, out Sprite cached) && cached != null) return cached;

        Rect full = src.rect;
        if (src.packed && src.packingMode == SpritePackingMode.Rectangle) full = src.textureRect;

        float ppu = src.pixelsPerUnit;
        Vector2 pivotPx = src.pivot;
        Bounds b = src.bounds;

        // 실제로 그려지는 영역(bounds)만 잘라낸다 (Tight 메쉬의 투명 여백 제거)
        Rect crop = Rect.MinMaxRect(
            Mathf.Max(full.xMin, full.x + pivotPx.x + b.min.x * ppu),
            Mathf.Max(full.yMin, full.y + pivotPx.y + b.min.y * ppu),
            Mathf.Min(full.xMax, full.x + pivotPx.x + b.max.x * ppu),
            Mathf.Min(full.yMax, full.y + pivotPx.y + b.max.y * ppu));
        if (crop.width < 1f || crop.height < 1f) crop = full;

        // 임포터 Border(left, bottom, right, top)가 있으면 그대로 사용, 없으면 비율로 대체
        Vector4 border = src.border;
        float bottom = Mathf.Max(0f, border.y - (crop.yMin - full.yMin));
        float top = Mathf.Max(0f, border.w - (full.yMax - crop.yMax));
        if (bottom + top < 1f)
        {
            bottom = top = crop.height * Mathf.Clamp(EdgeFraction, 0f, 0.49f);
        }

        Sprite sliced = Sprite.Create(src.texture, crop, new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, new Vector4(0f, bottom, 0f, top));
        sliced.name = src.name + "_sliced";
        slicedCache[src] = sliced;
        return sliced;
    }

    // sr(Sliced 스프라이트)를 월드 기준 [bottomY, topY] 구간, 가로 targetWidth 로 정확히 맞춘다.
    public static void Fit(SpriteRenderer sr, float centerX, float bottomY, float topY, float targetWidth, float z)
    {
        Sprite s = sr.sprite;
        if (s == null) return;

        float spriteW = s.rect.width / s.pixelsPerUnit;
        float k = targetWidth / Mathf.Max(0.0001f, spriteW);   // 가로 기준 균일 배율 → 끝 장식의 가로세로 비율 유지
        float length = Mathf.Max(0.0001f, topY - bottomY);

        if (sr.drawMode != SpriteDrawMode.Sliced) sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(spriteW, length / k);

        Transform tf = sr.transform;
        Vector3 ps = tf.parent != null ? tf.parent.lossyScale : Vector3.one;
        tf.position = new Vector3(centerX, (bottomY + topY) * 0.5f, z);
        tf.localScale = new Vector3(k / Mathf.Max(0.0001f, Mathf.Abs(ps.x)), k / Mathf.Max(0.0001f, Mathf.Abs(ps.y)), 1f);
    }
}

public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        string newJson = "{ \"array\": " + json + "}";
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
        return wrapper.array;
    }

    [Serializable]
    private class Wrapper<T> { public T[] array; }
}