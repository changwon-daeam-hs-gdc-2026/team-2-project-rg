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
    //  osu! 의 Universal offset 과 같은 방향: + = 노트(게임)가 음악보다 '빨라짐', - = '늦어짐'
    //   · 블루투스 / TV 처럼 음악이 늦게 들리면  → AUDIO OFFSET 을 - 로
    //   · SLOW 가 많으면(늦게 친다)              → JUDGE OFFSET 을 - 로,  FAST 가 많으면 + 로
    //  게임 내부 계산은 SettingsStore.MusicLead / JudgeDelay 로 읽는다 (부호 변환은 그곳 한 군데에서만).
    public double AudioOffset = 0.0;
    public double JudgeOffset = 0.0;
    // 저장 파일의 오프셋 부호 규칙 버전. 0 = 예전(반대 부호) → 불러올 때 부호를 뒤집어 1 로 바꾼다.
    //  (기본값을 0 으로 둬야 이 칸이 없는 예전 파일을 알아볼 수 있다)
    public int OffsetConvention = 0;

    // ----- 게임플레이 -----
    public double SettingSpeed = 1.0; 
    public int judgeoverrange = 10;    // FAST/SLOW 를 표시하는 최소 오차(ms)
    public bool ShowFastSlow = true;
    public int GameSkin = 0;           // 게임 화면 UI: 0 = DEFAULT, 1 = SIMPLE (GameSkins 참고)
    public float LaneOpacity = 0.85f;  // SIMPLE 레인 배경 불투명도 (0 = 완전 투명, 1 = 불투명)
    public bool ShowInfoUI = true;     // 게임 화면 Information UI (곡 정보 · 난이도 · 시간 · 점수 · 배속 · 진행 막대)
    public bool LaneLines = true;      // SIMPLE 레인 사이 구분선 3개 켜기/끄기
    public bool ShowKeyMarkers = true; // SIMPLE 판정선 위 노트 모양 키 자리 켜기/끄기
    public bool ShowHitErrorBar = true; // 화면 아래 판정 오차 그래프 (osu! Hit Error Meter 처럼)
    public int EffectQuality = 1;      // 히트 이펙트: 0 = 낮음 (가벼움), 1 = 높음
    public bool ShowJustPerfect = false; // ±15ms 안의 PERFECT 를 'JUST PERFECT' 로 표시 (끄면 그냥 PERFECT)
    public string[] LaneKeys = { "D", "F", "J", "K" }; // UnityEngine.InputSystem.Key 이름
    // 시작 타임 조정 (초, 0 = 처음부터). 예) 109.2 → 음악을 109.2초부터 틀고, 그 뒤 2초 안의 노트는 빼고 111.2초 노트부터 내려옴
    public double StartTime = 0.0;

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
    // 영역(Area) 채보: "Area": "deployed(INSANE)"  (여러 난이도면 "deployed(HARD,INSANE)")
    //  영역 채보는 SIMPLE-DIVERSE 스킨 전용 (다른 스킨이면 그 판만 강제로 바뀜) + 영역 전용 에디터로 열림
    public string Area;
    // MV 주의 경고: 값이 있으면 MV 를 켠 채로 플레이할 때 경고창(주의)이 뜬다
    //  "MvWarning": "default"  → 기본 문구 (폭력적인 장면, 유혈 및 신체 훼손 …)
    //  "MvWarning": "직접 쓴 문구\n두 번째 줄"  → 그 문구 그대로 (\n = 줄바꿈)
    public string MvWarning;
}

public static class AreaHelper
{
    // 이 곡의 이 난이도(1: EASY, 2: HARD, 3: INSANE)가 영역 채보인가
    public static bool IsAreaLevel(SongData song, int levelId)
    {
        if (song == null || string.IsNullOrWhiteSpace(song.Area)) return false;
        string a = song.Area.Trim();
        if (!a.StartsWith("deployed", StringComparison.OrdinalIgnoreCase)) return false;
        int open = a.IndexOf('('), close = a.LastIndexOf(')');
        if (open < 0 || close <= open) return false;
        string name = DifficultyHelper.GetName(levelId);
        foreach (string part in a.Substring(open + 1, close - open - 1).Split(','))
            if (string.Equals(part.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

// 노트 종류 번호
//  1 탭 · 2 롱노트 머리 · 3 롱노트 꼬리 · 4 마디선 · 7 화살표(>>> 밝은 파랑) · 8 화살표(<<< 밝은 노랑) · 11/12/13 RED
//  9 드래그 노트 · 10 드래그로 시작하는 롱노트 머리 (끝은 일반 롱노트 끝 3 으로 닫음 — 몸통 · 끝은 일반 롱노트 색)
//  화살표 노트는 판정이 탭과 똑같은 장식 노트 (주로 영역 경계에 둠)
//  드래그 노트: 판정은 JUST PERFECT / MISS 두 가지뿐.
//   (1) 판정선에 닿을 때 그 레인 키가 눌려 있으면 성공  또는  (2) 일반 노트처럼 판정선에 닿을 때 키를 누르면 성공
public static class NoteTypes
{
    public const int Tap = 1, LongHead = 2, LongTail = 3, Measure = 4, ArrowRight = 7, ArrowLeft = 8, Drag = 9, DragLongHead = 10;
    public static bool IsArrow(int type) { return type == ArrowRight || type == ArrowLeft; }
    public static bool IsDrag(int type) { return type == Drag || type == DragLongHead; }
    // 롱노트 머리 (일반 · RED · 드래그 시작)
    public static bool IsLongHead(int type) { return type == LongHead || type == 12 || type == DragLongHead; }
    // 한 번 치고 끝나는 노트 (탭 · 화살표 · RED 탭 · 드래그)
    public static bool IsTapLike(int type) { return type == Tap || type == 11 || IsArrow(type) || type == Drag; }
}

// 영역: 이 시각부터 mask 의 레인들이 영역 (mask 0 = 영역 끝). 채보: AREA, 마디, 분자, 분모, 1100
public class AreaEvent { public double Time; public int Mask; }
// 레인 접기 / 펼치기. 채보: FOLD, 마디, 분자, 분모, 초  /  UNFOLD, 마디, 분자, 분모, 초
public class LaneFoldEvent { public double Time; public bool Fold; public double Duration; }

public static class AreaMask
{
    // "1100" → 레인 0,1 (왼쪽부터). 1 = 영역
    public static int Parse(string s)
    {
        int m = 0;
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.Trim();
        for (int i = 0; i < s.Length && i < 4; i++) if (s[i] == '1') m |= 1 << i;
        return m;
    }
    public static string Format(int mask)
    {
        char[] c = new char[4];
        for (int i = 0; i < 4; i++) c[i] = (mask & (1 << i)) != 0 ? '1' : '0';
        return new string(c);
    }
    public static bool Has(int mask, int lane) { return lane >= 0 && lane < 4 && (mask & (1 << lane)) != 0; }
}

// songs.json 읽기 전 손질
public static class SongJson
{
    // "BPM": 132  →  "BPM": "132"  (숫자로 적힌 예전 형식도 글자로 읽히도록)
    public static string Normalize(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        json = System.Text.RegularExpressions.Regex.Replace(json,
            "(\"BPM\"\\s*:\\s*)(-?[0-9]+(?:\\.[0-9]+)?)", "$1\"$2\"");
        // "MvWarning": true / 1  →  "default" (기본 경고 문구),  false / 0 → "" (경고 없음)
        json = System.Text.RegularExpressions.Regex.Replace(json, "(\"MvWarning\"\\s*:\\s*)(true|1)(?=\\s*[,}])", "$1\"default\"");
        json = System.Text.RegularExpressions.Regex.Replace(json, "(\"MvWarning\"\\s*:\\s*)(false|0)(?=\\s*[,}])", "$1\"\"");
        return json;
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
    public bool IsAllJust;          // + 모든 판정이 JUST PERFECT (TP 100%) — ALL PERFECT 보다 위
    public int JustPerfect;         // JUST PERFECT 수 (PERFECT 중 ±15ms 안, 롱노트 중간 판정 포함)
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
    // 설정의 시작 시간 조정으로 곡 중간부터 시작한 판 (연습) → 최고 기록에 저장하지 않음
    public bool IsMidStart;
    public double MidStartSeconds;
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

// ============================================================
// 채보 오프셋 (게임 · 에디터 · osu! 변환기가 같은 규칙을 쓴다)
//  MUSICOFFSET, 1234      (ms) : 음악 파일 안에서 0박(1마디 시작)이 나오는 시각
//                                 = osu! 타이밍 포인트의 Offset 과 같은 값 · 같은 부호
//                                 값을 키우면(+) 노트(격자)가 음악보다 늦게 온다
//  OFFSET, -1.234         (초, 예전 형식) : 게임 시계에서 음악이 재생되기 시작하는 시각
//                                 = -(MUSICOFFSET / 1000). 부호가 반대라 헷갈려서 읽기만 지원하고,
//                                 에디터에서 저장하면 MUSICOFFSET 으로 바뀐다.
//  두 줄이 모두 있으면 MUSICOFFSET 이 우선.
//  게임 내부에서는 AudioStart(= 예전 OFFSET 값, 초)로 계산한다.
// ============================================================
public static class ChartOffset
{
    public const string Key = "MUSICOFFSET";

    public static double ToAudioStart(double musicOffsetMs) { return -musicOffsetMs / 1000.0; }
    public static double ToMusicOffsetMs(double audioStart) { return Math.Round(-audioStart * 1000.0, 3); }

    public static string Line(double audioStart)
    {
        return Key + ", " + ToMusicOffsetMs(audioStart).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    // 채보 한 줄을 읽어 오프셋 줄이면 true. isNew = MUSICOFFSET 형식인지
    public static bool TryParse(string type, string[] data, out double audioStart, out bool isNew)
    {
        audioStart = 0.0; isNew = false;
        if (data == null || data.Length < 2) return false;
        double v;
        if (!double.TryParse(data[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
        if (type == Key) { audioStart = ToAudioStart(v); isNew = true; return true; }
        if (type == "OFFSET" || type == "SONGSTARTOFFSET") { audioStart = v; return true; }
        return false;
    }

    // 화면 표시용: "+1234 ms"
    public static string Display(double audioStart)
    {
        double ms = ToMusicOffsetMs(audioStart);
        string s = Math.Abs(ms - Math.Round(ms)) < 0.0005
            ? Math.Round(ms).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : ms.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
        return (ms > 0 ? "+" : "") + s + " ms";
    }
}

public class SpeedChange { public double Time; public double Multiplier; }
public class BpmChange
{
    public double Time; public double Bpm; public double Beats;
    // TIMING 줄: 이 박이 와야 하는 고정 시각(초, 0박 기준). NaN = 일반 BPM 줄(앞에서부터 누적 계산)
    public double AnchorTime = double.NaN;
    public bool HasAnchor { get { return !double.IsNaN(AnchorTime); } }
}
public class StopEventData { public double Time; }
public class StartEventData { public double Time; }

public class UIEventData 
{ 
    public string TargetUI; 
    public string Action; 
    public double StartTime; 
    public double Duration; 
}

public class NoteData
{
    public double HitTime;    
    public int Lane;           
    public int Type;          
    public bool IsHit;         
    public NoteData NextLongNote = null; 
    public bool isTimeLogged = true;

    public bool IsRedNote = false;
    public int Width = 1; 
    public bool DragTail = false;   // 드래그로 시작한 롱노트의 끝인지 (판정 · 색은 일반 롱노트 끝과 같음)

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
    // "1_3"  -> type 1, width 3, 일반 노트 (넓은 일반 노트: 영역 채보에서 RED 처럼 1~4칸 폭)
    //  RED 여부는 종류 번호로 정함 (11 · 12 · 13 = RED). 예전 채보의 RED 는 모두 11~13 이라 그대로 읽힘
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
            width = Math.Max(1, Math.Min(4, width));
            isRed = type >= 11 && type <= 13;
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