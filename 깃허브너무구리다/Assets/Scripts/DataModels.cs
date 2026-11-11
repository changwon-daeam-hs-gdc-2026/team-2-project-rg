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
    public bool ShowJustPerfect = false; // ±20ms 안의 PERFECT 를 'JUST' 로 표시 (끄면 그냥 PERFECT)
    public int SongNameLanguage = 0;   // 곡명 표기: 0 = 원어 (Name_Original …), 1 = 영어 (Name_English …)
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
    // 화면에 쓰는 곡명 · 부제 (아래 원어 / 영어 칸이 있으면 설정 > 곡명 표기에 따라 그 값으로 채워짐, ApplyNameLanguage)
    public string Name;
    public string SubName; 
    // 곡명 표기 (원어 / 영어) — songs.json 이 아니라 따로 있는 songs_Name.json 에서 같은 Id 로 채워짐 (SongNames.Fill)
    //  부제가 없으면 "" (빈칸). SPECIAL 도 마찬가지 (빈칸이면 기본 곡명 그대로)
    //  고른 언어 칸이 비어 있으면 다른 언어 → 그것도 없으면 songs.json 의 Name · SubName 을 씀
    public string Name_Original;
    public string SubName_Original;
    public string SPECIAL_Name_Original;
    public string SPECIAL_SubName_Original;
    public string Name_English;
    public string SubName_English;
    public string SPECIAL_Name_English;
    public string SPECIAL_SubName_English;
    public string Difficulty_EASY;
    public string Difficulty_HARD;
    public string Difficulty_INSANE;
    // SPECIAL (4번 난이도, 있는 곡만): "Difficulty_SPECIAL": "14"  — 비어 있으면 SPECIAL 없음
    //  채보 {곡ID}_4.txt, 음원 {곡ID}_4.mp3 (없으면 {곡ID}.mp3)
    public string Difficulty_SPECIAL;
    // 숨긴 난이도: 난이도 값을 "?" 로 두면 곡 선택 · 게임 화면에서 ? 로 보이고, 실제 값은 아래 _REALVALUE 에 적음
    //  "Difficulty_SPECIAL": "?", "Difficulty_SPECIAL_REALVALUE": "17"
    //  게임 중 채보의 REVEAL 줄(에디터 G + 클릭) 시점부터 실제 값이 드러남 (없으면 끝까지 ?)
    public string Difficulty_EASY_REALVALUE;
    public string Difficulty_HARD_REALVALUE;
    public string Difficulty_INSANE_REALVALUE;
    public string Difficulty_SPECIAL_REALVALUE;
    // SPECIAL 전용 곡 정보 (비어 있으면 기본 값을 씀)
    //  "SPECIAL_Name": "東京テディベア", "SPECIAL_SubName": " (Tokino Mary Cover)", "SPECIAL_Vocal": ""
    //  그림: Sprites/squareimg/{곡ID}_4 · Sprites/blurimg/{곡ID}_4 · Sprites/loadimg-large/{곡ID}_4 · 선택 바 {곡ID}_4 (없으면 기본 그림)
    public string SPECIAL_Name;
    public string SPECIAL_SubName;
    public string SPECIAL_Vocal;
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
    public int NoteCount_SPECIAL;
    public int HasMV;
    public int ExistSongbar;
    // 난이도별 잠금: 4자리 "EASY HARD INSANE SPECIAL" (0 = 열림, 1 = 잠김). 없으면 모두 열림
    //  "LockedValue": 1010 → EASY · INSANE 잠김.  숫자 · 글자("0010") 둘 다 됨 (SongJson.Normalize 가 글자로 바꿈)
    //  4자리보다 짧으면 앞을 0 으로 채움 (10 → 0010)
    public string LockedValue;
    // MV 주의 경고: 값이 있으면 MV 를 켠 채로 플레이할 때 경고창(주의)이 뜬다
    //  "MvWarning": "default"  → 기본 문구 (폭력적인 장면, 유혈 및 신체 훼손 …)
    //  "MvWarning": "직접 쓴 문구\n두 번째 줄"  → 그 문구 그대로 (\n = 줄바꿈)
    public string MvWarning;

    // ---- 곡명 표기 적용 (원어 / 영어) ----
    [NonSerialized] private bool legacySaved;
    [NonSerialized] private string legacyName, legacySub, legacySpName, legacySpSub;

    public static bool EnglishNames { get { return SettingsStore.Current != null && SettingsStore.Current.SongNameLanguage == 1; } }

    // Name · SubName · SPECIAL_Name · SPECIAL_SubName 을 고른 언어로 채움 (몇 번 불러도 됨 — 예전 칸 값은 처음에 기억해 둠)
    public void ApplyNameLanguage(bool english)
    {
        SongNames.Fill(this);   // songs_Name.json 의 원어 / 영어 곡명 (처음 한 번)
        if (!legacySaved)
        {
            legacySaved = true;
            legacyName = Name; legacySub = SubName; legacySpName = SPECIAL_Name; legacySpSub = SPECIAL_SubName;
        }
        PickName(english, Name_Original, SubName_Original, Name_English, SubName_English, legacyName, legacySub, out Name, out SubName);
        PickName(english, SPECIAL_Name_Original, SPECIAL_SubName_Original, SPECIAL_Name_English, SPECIAL_SubName_English,
                 legacySpName, legacySpSub, out SPECIAL_Name, out SPECIAL_SubName);
    }

    // 고른 언어 곡명이 있으면 그 곡명 + 그 부제(없으면 ""), 없으면 다른 언어, 그것도 없으면 예전 칸
    private static void PickName(bool english, string nO, string sO, string nE, string sE, string legacyN, string legacyS, out string name, out string sub)
    {
        string n1 = english ? nE : nO, s1 = english ? sE : sO;
        string n2 = english ? nO : nE, s2 = english ? sO : sE;
        if (!string.IsNullOrEmpty(n1)) { name = n1; sub = s1 ?? ""; return; }
        if (!string.IsNullOrEmpty(n2)) { name = n2; sub = s2 ?? ""; return; }
        name = legacyN;
        // 곡명은 없는데 부제만 언어별로 적힌 경우 (SPECIAL 부제만 다른 경우 등)
        string sPick = !string.IsNullOrEmpty(s1) ? s1 : !string.IsNullOrEmpty(s2) ? s2 : null;
        sub = sPick ?? legacyS;
    }

    // 목록 전체에 지금 설정 언어 적용
    public static void ApplyNameLanguage(IEnumerable<SongData> songs)
    {
        if (songs == null) return;
        bool en = EnglishNames;
        foreach (var s in songs) if (s != null) s.ApplyNameLanguage(en);
    }

    // 이 난이도로 보여 줄 곡 정보: SPECIAL(4) 이면 SPECIAL_Name · SPECIAL_SubName · SPECIAL_Vocal 로 바꾼 복사본, 아니면 그대로
    //  (SPECIAL_Name 이 있으면 부제는 SPECIAL_SubName 만 씀 — 기본 부제가 섞이지 않게)
    public SongData ForLevel(int levelId)
    {
        if (levelId != DifficultyHelper.Special) return this;
        bool hasName = !string.IsNullOrEmpty(SPECIAL_Name), hasSub = !string.IsNullOrEmpty(SPECIAL_SubName), hasVocal = !string.IsNullOrEmpty(SPECIAL_Vocal);
        if (!hasName && !hasSub && !hasVocal) return this;
        SongData c = (SongData)MemberwiseClone();
        if (hasName) c.Name = SPECIAL_Name;
        if (hasName || hasSub) c.SubName = SPECIAL_SubName ?? "";
        if (hasVocal) c.Vocal = SPECIAL_Vocal;
        return c;
    }
}

// SPECIAL 전용 솜사탕 그라데이션 그림 (글자 말고 막대 · 밑줄 · 빛 같은 그림에 씀 — Image.color 는 흰색으로)
//  Bar      : 왼쪽 → 오른쪽 그라데이션 막대 (구분선 · 로딩 막대 등)
//  Tint(그림): 그 그림의 모양(투명도)은 그대로, 색만 왼쪽 → 오른쪽 그라데이션으로 칠한 복사본 (밑줄 · 빛 등)
public static class SpecialArt
{
    private static Sprite bar;
    private static readonly Dictionary<Sprite, Sprite> tinted = new Dictionary<Sprite, Sprite>();

    public static Sprite Bar
    {
        get
        {
            if (bar != null) return bar;
            const int W = 256, H = 4;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color32[] px = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                Color32 c = DifficultyHelper.SpecialAt(0.85f * x / (W - 1f));
                for (int y = 0; y < H; y++) px[y * W + x] = c;
            }
            tex.SetPixels32(px); tex.Apply();
            bar = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            bar.name = "SpecialBar";
            return bar;
        }
    }

    public static Sprite Tint(Sprite shape)
    {
        if (shape == null) return Bar;
        Sprite t;
        if (tinted.TryGetValue(shape, out t) && t != null) return t;
        try
        {
            Rect r = shape.rect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            Color[] src = shape.texture.GetPixels(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), w, h);
            Color[] dst = new Color[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color s0 = src[y * w + x];
                    Color g = DifficultyHelper.SpecialAt(0.85f * x / Mathf.Max(1f, w - 1f));
                    dst[y * w + x] = new Color(s0.r * g.r, s0.g * g.g, s0.b * g.b, s0.a);
                }
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(dst); tex.Apply();
            t = Sprite.Create(tex, new Rect(0, 0, w, h), shape.pivot / new Vector2(Mathf.Max(1f, w), Mathf.Max(1f, h)), shape.pixelsPerUnit, 0, SpriteMeshType.FullRect, shape.border);
            t.name = shape.name + "_Special";
        }
        catch (Exception) { t = Bar; }   // 읽을 수 없는 그림이면 그냥 그라데이션 막대
        tinted[shape] = t;
        return t;
    }
}

// 곡 그림 경로: SPECIAL(4) 이면 '{폴더}/{곡ID}_4' 가 있을 때 그것, 없으면 '{폴더}/{곡ID}'
//  곡 ID 그림도 없으면 같은 폴더의 '{폴더}/None' 을 씀
//  예) SongArt.Load("Sprites/blurimg", 8, 4) → Sprites/blurimg/8_4 → (없으면) Sprites/blurimg/8 → (없으면) Sprites/blurimg/None
public static class SongArt
{
    public const string Fallback = "None";
    private static readonly Dictionary<string, bool> exists = new Dictionary<string, bool>();

    private static bool Exists(string path)
    {
        bool ok;
        if (!exists.TryGetValue(path, out ok)) { ok = Resources.Load<Sprite>(path) != null; exists[path] = ok; }
        return ok;
    }

    public static string Path(string folder, int songId, int levelId)
    {
        if (levelId == DifficultyHelper.Special)
        {
            string sp = folder + "/" + songId + "_4";
            if (Exists(sp)) return sp;
        }
        return Path(folder, songId);
    }

    // 기본 그림 경로 ('{폴더}/{곡ID}', 없으면 '{폴더}/None')
    public static string Path(string folder, int songId)
    {
        string p = folder + "/" + songId;
        if (Exists(p)) return p;
        return folder + "/" + Fallback;
    }

    public static Sprite Load(string folder, int songId, int levelId) { return Resources.Load<Sprite>(Path(folder, songId, levelId)); }
    public static Sprite Load(string folder, int songId) { return Resources.Load<Sprite>(Path(folder, songId)); }
}

// 노트 종류 번호
//  1 탭 · 2 롱노트 머리 · 3 롱노트 꼬리 · 4 마디선 · 11/12/13 RED
//  9 드래그 노트 · 10 드래그로 시작하는 롱노트 머리 (끝은 일반 롱노트 끝 3 으로 닫음 — 몸통 · 끝은 일반 롱노트 색)
//  (7 · 8 화살표 노트는 삭제됨. 예전 채보에 남아 있으면 읽을 때 일반 탭 1 로 바꿈 → NoteTypes.Normalize)
//  드래그 노트: 판정은 JUST / MISS 두 가지뿐.
//   (1) 판정선에 닿을 때 그 레인 키가 눌려 있으면 성공  또는  (2) 일반 노트처럼 판정선에 닿을 때 키를 누르면 성공
public static class NoteTypes
{
    public const int Tap = 1, LongHead = 2, LongTail = 3, Measure = 4, Drag = 9, DragLongHead = 10;
    // 슬라이드(레인을 넘나드는 롱노트)의 중계점: 꺾이는 지점, 그 순간 눌려 있으면 JUST (노트 수에 들어감)
    public const int SlideRelay = 14;
    public static bool IsSlideRelay(int type) { return type == SlideRelay; }
    // 예전 채보의 화살표 노트(7, 8)는 일반 탭으로
    public static int Normalize(int type) { return type == 7 || type == 8 ? Tap : type; }
    public static bool IsDrag(int type) { return type == Drag || type == DragLongHead; }
    // 롱노트 머리 (일반 · RED · 드래그 시작)
    public static bool IsLongHead(int type) { return type == LongHead || type == 12 || type == DragLongHead; }
    // 한 번 치고 끝나는 노트 (탭 · RED 탭 · 드래그)
    public static bool IsTapLike(int type) { return type == Tap || type == 11 || type == Drag; }
}

// 레인 접기 / 펼치기. 채보: FOLD, 마디, 분자, 분모, 초  /  UNFOLD, 마디, 분자, 분모, 초
public class LaneFoldEvent { public double Time; public bool Fold; public double Duration; }

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
        // "LockedValue": 1010 / 0010  →  "1010" / "0010" (앞의 0 이 사라지지 않게 글자로)
        json = System.Text.RegularExpressions.Regex.Replace(json, "(\"LockedValue\"\\s*:\\s*)([0-9]+)", "$1\"$2\"");
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

    public int Score;               // 0 ~ 1,000,000 (게임 모드 배율을 곱하면 최대 3,000,000)
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
    public bool IsAllJust;          // + 모든 판정이 JUST (TP 100%) — ALL PERFECT 보다 위
    public int JustPerfect;         // JUST 수 (PERFECT 중 ±20ms 안, 롱노트 중간 판정 포함)
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
    // 게임 모드 (GameMode 번호) · 점수 배율 (1 = 100%, 최대 3 = 300%)
    public int[] Modes = new int[0];
    public double ScoreMultiplier = 1.0;
    public double RandomBonus = 0.0;    // MODE RANDOM 보너스 (0.25 = +25%p). ScoreMultiplier 에 이미 들어 있음

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
    // 그 난이도가 잠겼는가 (songs.json "LockedValue": 4자리, 앞부터 EASY · HARD · INSANE · SPECIAL, 1 = 잠김)
    public static bool IsLevelLocked(SongData song, int levelId)
    {
        if (song == null || string.IsNullOrWhiteSpace(song.LockedValue) || levelId < 1 || levelId > 4) return false;
        string v = song.LockedValue.Trim();
        var digits = new System.Text.StringBuilder();
        foreach (char ch in v) if (ch >= '0' && ch <= '9') digits.Append(ch);
        string d = digits.ToString();
        if (d.Length == 0) return false;
        if (d.Length < 4) d = new string('0', 4 - d.Length) + d;   // 10 → 0010
        return d[levelId - 1] == '1';
    }

    // 곡 전체 잠금(ExistSongbar 2) 또는 그 난이도 잠금
    public static bool IsPlayLocked(SongData song, int levelId)
    {
        return song != null && (song.ExistSongbar == 2 || IsLevelLocked(song, levelId));
    }

    public static string GetName(int levelId)
    {
        switch (levelId) { case 1: return "EASY"; case 3: return "INSANE"; case 4: return "SPECIAL"; default: return "HARD"; }
    }

    public const int Special = 4;   // SPECIAL 난이도 번호
    // 이 곡에 SPECIAL 이 있는가 ("Difficulty_SPECIAL" 값이 있고 "00" 이 아니면. 숫자 말고 "DIVERSITY" 같은 글자여도 됨)
    public static bool HasSpecial(SongData song)
    {
        return song != null && SongRowUI.IsDifficultyAvailable(song.Difficulty_SPECIAL);
    }

    // SPECIAL 하나만 있는 곡 (EASY · HARD · INSANE 이 모두 없음) — 기존 3난이도 목록에는 나오지 않고 SPECIAL 목록에만 나옴
    public static bool IsSpecialOnly(SongData song)
    {
        return HasSpecial(song)
            && !SongRowUI.IsDifficultyAvailable(song.Difficulty_EASY)
            && !SongRowUI.IsDifficultyAvailable(song.Difficulty_HARD)
            && !SongRowUI.IsDifficultyAvailable(song.Difficulty_INSANE);
    }
    // 이 곡에서 고를 수 있는 가장 높은 난이도 번호 (SPECIAL 이 있으면 4, 없으면 3)
    public static int MaxLevel(SongData song) { return HasSpecial(song) ? Special : 3; }

    // 게임 화면용 짧은 표기: EZ / HD / INS / SP
    public static string GetShortName(int levelId)
    {
        switch (levelId) { case 1: return "EZ"; case 3: return "INS"; case 4: return "SP"; default: return "HD"; }
    }

    public static string GetValue(SongData song, int levelId)
    {
        if (song == null) return "";
        switch (levelId) { case 1: return song.Difficulty_EASY; case 3: return song.Difficulty_INSANE; case 4: return song.Difficulty_SPECIAL; default: return song.Difficulty_HARD; }
    }

    // 숨긴 난이도인가 (값이 "?")
    public static bool IsHidden(SongData song, int levelId)
    {
        string v = GetValue(song, levelId);
        return v != null && v.Trim() == "?";
    }

    // 실제 난이도 값: 숨긴 난이도면 _REALVALUE (없으면 "?"), 아니면 보이는 값 그대로
    public static string GetRealValue(SongData song, int levelId)
    {
        if (song == null) return "";
        if (!IsHidden(song, levelId)) return GetValue(song, levelId);
        string r;
        switch (levelId)
        {
            case 1: r = song.Difficulty_EASY_REALVALUE; break;
            case 3: r = song.Difficulty_INSANE_REALVALUE; break;
            case 4: r = song.Difficulty_SPECIAL_REALVALUE; break;
            default: r = song.Difficulty_HARD_REALVALUE; break;
        }
        return SongRowUI.IsDifficultyAvailable(r) ? r.Trim() : "?";
    }

    // 난이도별 노트 수 (없으면 구 NoteCount)
    public static int GetNoteCount(SongData song, int levelId)
    {
        if (song == null) return 0;
        int v = levelId == 1 ? song.NoteCount_EASY : levelId == 3 ? song.NoteCount_INSANE : levelId == 4 ? song.NoteCount_SPECIAL : song.NoteCount_HARD;
        return v > 0 ? v : song.NoteCount;
    }

    // ----- SPECIAL 전용 색: 알록달록한 솜사탕 그라데이션 (분홍 → 라벤더 → 하늘 → 민트빛 → 분홍) -----
    public static readonly Color[] SpecialStops =
    {
        new Color(1.00f, 0.62f, 0.85f),   // 솜사탕 분홍
        new Color(0.84f, 0.66f, 1.00f),   // 라벤더
        new Color(0.60f, 0.84f, 1.00f),   // 하늘
        new Color(0.66f, 1.00f, 0.90f),   // 민트빛
        new Color(1.00f, 0.80f, 0.92f),   // 연분홍
    };

    // 그라데이션의 t(0 ~ 1) 위치 색
    public static Color SpecialAt(float t)
    {
        t = Mathf.Repeat(t, 1f) * (SpecialStops.Length - 1);
        int i = Mathf.Min((int)t, SpecialStops.Length - 2);
        return Color.Lerp(SpecialStops[i], SpecialStops[i + 1], t - i);
    }

    // 글자마다 그라데이션 색을 입힌 서식 글자 (TMP 서식). phase 로 색을 흘려 보낼 수 있음
    public static string SpecialGradientText(string text, float phase = 0f)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new System.Text.StringBuilder();
        int n = Mathf.Max(1, text.Length - 1);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ') { sb.Append(' '); continue; }
            Color c = SpecialAt(phase + 0.85f * i / n);
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>').Append(text[i]).Append("</color>");
        }
        return sb.ToString();
    }

    // 난이도 이름(또는 아무 글자)을 난이도 색으로: SPECIAL 은 솜사탕 그라데이션, 나머지는 한 가지 색
    public static string ColoredText(int levelId, string text)
    {
        if (levelId == Special) return SpecialGradientText(text);
        return "<color=#" + ColorUtility.ToHtmlStringRGB(GetColor(levelId)) + ">" + text + "</color>";
    }

    public static Color GetColor(int levelId)
    {
        // EASY 하늘색 · HARD 주황 · INSANE 빨강 · SPECIAL 솜사탕 (대표 색 = 라벤더 분홍, 글자는 ColoredText 로 그라데이션)
        if (levelId == Special) return Color.Lerp(SpecialStops[0], SpecialStops[1], 0.5f);
        string hex = levelId == 1 ? "#5ce1e6" : levelId == 3 ? "#ff3131" : "#ff751f";
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
    public bool IsFake = false;     // 게임 모드 FAKE 의 가짜 노트 (치면 LIFE 감소, 판정 · 노트 수에 안 들어감)

    // 슬라이드 (레인을 넘나드는 롱노트, 채보 칸 "종류[_폭]~번호[곡선]")
    //  머리: Slide = 경로 (머리 · 중계점 · 끝),  중계점 · 끝: SlideHead = 머리
    public int SlideId = 0;           // 채보의 슬라이드 번호 (0 = 슬라이드 아님)
    public int Curve = SlideCurve.Straight;   // 이 점 → 다음 점 구간의 곡선
    public SlidePath Slide;
    public NoteData SlideHead;

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
// 노트 층 (1 ~ 5층). 위에 그려지는 순서: 5 > 4 > 3 > 2 > 1
//  층마다 변속(SPEED · 층 지정)을 따로 가질 수 있음. 기본 층 = 2층 (예전 채보의 '위층')
// ============================================================
// 곡명 표 (StreamingAssets/data/songs_Name.json) — songs.json 과 따로, 곡명만 담음
//  [ { "Id": 8, "Name_Original": "東京テディベア", "SubName_Original": "", "SPECIAL_Name_Original": "東京テディベア",
//      "SPECIAL_SubName_Original": " (Tokino Mary Cover)", "Name_English": "Tokyo Teddy Bear", "SubName_English": "",
//      "SPECIAL_Name_English": "Tokyo Teddy Bear", "SPECIAL_SubName_English": " (Tokino Mary Cover)" }, ... ]
//  이 파일이 없거나 곡이 빠져 있으면 songs.json 의 Name · SubName 을 그대로 씀
// ============================================================
[Serializable]
public class SongNameEntry
{
    public int Id;
    public string Name_Original, SubName_Original, SPECIAL_Name_Original, SPECIAL_SubName_Original;
    public string Name_English, SubName_English, SPECIAL_Name_English, SPECIAL_SubName_English;
}

public static class SongNames
{
    private static Dictionary<int, SongNameEntry> table;

    private static Dictionary<int, SongNameEntry> Table
    {
        get
        {
            if (table != null) return table;
            table = new Dictionary<int, SongNameEntry>();
            try
            {
                string path = System.IO.Path.Combine(Application.streamingAssetsPath, "data", "songs_Name.json");
                if (!System.IO.File.Exists(path)) return table;
                SongNameEntry[] arr = JsonHelper.FromJson<SongNameEntry>(System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8));
                if (arr != null) foreach (var e in arr) if (e != null) table[e.Id] = e;
            }
            catch (Exception e) { Debug.LogWarning("[SongNames] songs_Name.json 을 읽지 못했습니다: " + e.Message); }
            return table;
        }
    }

    // 다시 읽기 (파일을 고친 뒤)
    public static void Reload() { table = null; }

    // 이 곡의 원어 / 영어 칸을 songs_Name.json 값으로 채움 (songs.json 에 적힌 값이 있으면 그대로 둠)
    public static void Fill(SongData s)
    {
        if (s == null || !Table.TryGetValue(s.Id, out SongNameEntry e)) return;
        if (s.Name_Original == null) s.Name_Original = e.Name_Original;
        if (s.SubName_Original == null) s.SubName_Original = e.SubName_Original;
        if (s.SPECIAL_Name_Original == null) s.SPECIAL_Name_Original = e.SPECIAL_Name_Original;
        if (s.SPECIAL_SubName_Original == null) s.SPECIAL_SubName_Original = e.SPECIAL_SubName_Original;
        if (s.Name_English == null) s.Name_English = e.Name_English;
        if (s.SubName_English == null) s.SubName_English = e.SubName_English;
        if (s.SPECIAL_Name_English == null) s.SPECIAL_Name_English = e.SPECIAL_Name_English;
        if (s.SPECIAL_SubName_English == null) s.SPECIAL_SubName_English = e.SPECIAL_SubName_English;
    }
}

public static class NoteLayer
{
    public const int Bottom = 1;
    public const int Top = 2;          // 기본 층 (층 표시 없는 노트 · 예전 채보의 위층)
    public const int Min = 1, Max = 5;
    public const int Count = 5;

    public static int Clamp(int layer) { return layer < Min ? Min : layer > Max ? Max : layer; }

    // SpriteRenderer.sortingOrder : 위층의 모든 요소(몸통 포함)가 아래층의 모든 요소보다 위
    //   층 L: 몸통 2 + (L-1)×3, 머리 = 몸통 + 1, (드래그 보석 = 머리 + 1)
    //   1층 2 · 3 / 2층 5 · 6 / 3층 8 · 9 / 4층 11 · 12 / 5층 14 · 15  (판정 이펙트는 EffectLift 만큼 올려 그 위)
    public static int BodyOrder(int layer) { return 2 + (Clamp(layer) - 1) * 3; }
    public static int HeadOrder(int layer) { return BodyOrder(layer) + 1; }
    public const int EffectLift = 12;  // 노트 위에 그려야 하는 이펙트(순서 6 이상)에 더하는 값 → 18 이상
}

public static class NoteCellParser
{
    // "11_3(12)" -> top = "11_3", bottom = "12"
    // "1"        -> top = "1",    bottom = null
    // "0(2)"     -> top = "0",    bottom = "2"
    // 한 칸 → 층별 토큰 (인덱스 1 ~ 5, 없으면 null)
    //  형식: "2층(1층)|토큰@층|토큰@층"   예) "1(2)|11_2@5|1@3"  = 2층 탭, 1층 롱노트 시작, 5층 RED 2칸, 3층 탭
    //   · 앞부분은 예전과 같은 "2층(1층)" — 예전 채보는 그대로 읽힘
    //   · 3 ~ 5층(또는 아무 층)은 '|' 뒤에 "토큰@층"
    public static string[] SplitLayers(string cell)
    {
        string[] r = new string[NoteLayer.Max + 1];
        if (string.IsNullOrWhiteSpace(cell)) return r;
        string[] parts = cell.Split('|');
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (p.Length == 0) continue;
            int at = p.LastIndexOf('@');
            if (at > 0 && int.TryParse(p.Substring(at + 1).Trim(), out int L) && L >= NoteLayer.Min && L <= NoteLayer.Max)
            {
                string tok = p.Substring(0, at).Trim();
                if (tok.Length > 0 && tok != "0") r[L] = tok;
                continue;
            }
            if (i == 0)
            {
                SplitCell(p, out string top, out string bottom);
                if (!string.IsNullOrEmpty(top) && top != "0") r[NoteLayer.Top] = top;
                if (!string.IsNullOrEmpty(bottom) && bottom != "0") r[NoteLayer.Bottom] = bottom;
            }
        }
        return r;
    }

    // 층별 토큰 → 한 칸 (SplitLayers 의 반대). 1 · 2층만 있으면 예전 형식 그대로
    public static string JoinLayers(string[] byLayer)
    {
        string top = byLayer != null && byLayer.Length > NoteLayer.Top ? byLayer[NoteLayer.Top] : null;
        string bottom = byLayer != null && byLayer.Length > NoteLayer.Bottom ? byLayer[NoteLayer.Bottom] : null;
        string s = string.IsNullOrEmpty(top) ? "0" : top;
        if (!string.IsNullOrEmpty(bottom)) s += "(" + bottom + ")";
        if (byLayer != null)
            for (int L = 3; L <= NoteLayer.Max && L < byLayer.Length; L++)
                if (!string.IsNullOrEmpty(byLayer[L])) s += "|" + byLayer[L] + "@" + L;
        return s;
    }

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
        int sl = token.IndexOf('~');                     // 슬라이드 표시("~번호")는 떼고 읽음
        if (sl >= 0) token = token.Substring(0, sl).Trim();

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

    // 슬라이드 표시 떼기: "2_3~5i" → core "2_3", id 5, curve EaseIn  /  "2" → core "2", id 0
    //  (TryParseToken 은 이 표시를 알아서 떼고 읽음 → 슬라이드를 모르는 곳에서는 그냥 일반 노트로 보임)
    public static string SplitSlide(string token, out int slideId, out int curve)
    {
        slideId = 0; curve = SlideCurve.Straight;
        if (string.IsNullOrEmpty(token)) return token;
        int k = token.IndexOf('~');
        if (k < 0) return token;
        string tail = token.Substring(k + 1).Trim();
        string core = token.Substring(0, k).Trim();
        int end = 0;
        while (end < tail.Length && char.IsDigit(tail[end])) end++;
        if (end > 0) int.TryParse(tail.Substring(0, end), out slideId);
        string c = tail.Substring(end).Trim().ToLowerInvariant();
        curve = c == "i" ? SlideCurve.EaseIn : c == "o" ? SlideCurve.EaseOut : c == "r" ? SlideCurve.Corner : SlideCurve.Straight;
        return core;
    }

    // 슬라이드 표시 붙이기: ("2_3", 5, EaseIn) → "2_3~5i"
    public static string SlideToken(string core, int slideId, int curve)
    {
        if (slideId <= 0) return core;
        return core + "~" + slideId + (curve == SlideCurve.EaseIn ? "i" : curve == SlideCurve.EaseOut ? "o" : curve == SlideCurve.Corner ? "r" : "");
    }
}

// ============================================================
// 슬라이드 (레인을 넘나드는 롱노트)
//  채보: 롱노트 머리 · 중계점(14) · 끝(3)에 같은 번호를 붙임  예) 2~1  →  14~1i  →  3~1
//   번호 뒤 글자 = 그 점에서 다음 점까지의 곡선:  (없음) 직선 · i 처음 천천히(ease-in) · o 끝 천천히(ease-out)
//                                                 · r 직각 (다음 점 시각까지 제자리로 곧게 → 그 순간 옆으로 꺾임)
//  같은 번호(같은 층)의 점들을 시간 순으로 이음: 처음 = 머리, 마지막 = 끝, 가운데 = 중계점
//  판정: 판정선에 닿은 지점이 덮는 레인 키 중 하나를 누르고 있어야 함 (옮겨 잡는 순간은 짧게 봐줌)
// ============================================================
public static class SlideCurve
{
    public const int Straight = 0, EaseIn = 1, EaseOut = 2, Corner = 3;
    public const int Count = 4;
    public static float Ease(int curve, float u)
    {
        u = u < 0f ? 0f : u > 1f ? 1f : u;
        switch (curve)
        {
            case EaseIn: return u * u;
            case EaseOut: return 1f - (1f - u) * (1f - u);
            case Corner: return u >= 1f ? 1f : 0f;       // 끝 순간까지 제자리 → 다음 점에서 직각으로 꺾임
            default: return u;
        }
    }
    public static string Name(int curve) { return curve == EaseIn ? "IN" : curve == EaseOut ? "OUT" : curve == Corner ? "CORNER" : "LINE"; }
    public static int Next(int curve) { return (curve + 1) % Count; }
}

public class SlidePoint
{
    public double Time;
    public int Lane, Width = 1;
    public int Curve;          // 이 점 → 다음 점
    public NoteData Note;
}

public class SlidePath
{
    public readonly List<SlidePoint> Points = new List<SlidePoint>();
    public double StartTime { get { return Points.Count > 0 ? Points[0].Time : 0.0; } }
    public double EndTime { get { return Points.Count > 0 ? Points[Points.Count - 1].Time : 0.0; } }

    // 시각 t 에서 슬라이드가 덮는 범위 (레인 단위: 왼쪽 끝 left ~ 오른쪽 끝 right, 1칸 = 1)
    public void Evaluate(double t, out float left, out float right)
    {
        if (Points.Count == 0) { left = 0f; right = 1f; return; }
        SlidePoint a = Points[0], b = Points[Points.Count - 1];
        if (t <= a.Time) { left = a.Lane; right = a.Lane + a.Width; return; }
        if (t >= b.Time) { left = b.Lane; right = b.Lane + b.Width; return; }
        for (int i = 0; i < Points.Count - 1; i++)
        {
            if (t <= Points[i + 1].Time) { a = Points[i]; b = Points[i + 1]; break; }
        }
        double span = b.Time - a.Time;
        float u = span <= 1e-9 ? 1f : (float)((t - a.Time) / span);
        float e = SlideCurve.Ease(a.Curve, u);
        left = Mathf.Lerp(a.Lane, b.Lane, e);
        right = Mathf.Lerp(a.Lane + a.Width, b.Lane + b.Width, e);
    }

    // 시각 t 에 덮는 레인들 (조금이라도 걸치면 포함, 아주 살짝 걸친 건 제외)
    public void CoveredLanes(double t, bool[] outLanes)
    {
        Evaluate(t, out float l, out float r);
        for (int lane = 0; lane < outLanes.Length; lane++)
        {
            float overlap = Mathf.Min(r, lane + 1f) - Mathf.Max(l, lane);
            if (overlap > 0.2f) outLanes[lane] = true;
        }
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