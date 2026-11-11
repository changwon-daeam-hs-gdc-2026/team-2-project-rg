using System;
using System.IO;
using System.Linq;
using UnityEngine;

// ============================================================
// GAME SETTING (5_Test 씬) — 실제 채보로 노트 속도 · 판정 · 표시를 맞추는 연습 공간의 상태
//
//  들어가기: CoreManager.LoadTestScene()  (설정창 > 타이밍 > 'GAME SETTING 열기' 버튼, 곡 선택창 T 키)
//   - 곡 선택 → 게임과 같은 전환(곡 정보 화면). 단 제목 GAME SETTING, 난이도 회색 UNKNOWN, 정보 줄 'MUSIC : 곡명'
//   - 채보: 곡 ID 2 (SongId), 난이도 LevelId (모두 2레벨 미만의 쉬운 채보). 화면에는 'TEST LEVEL 0'
//   - 음악 · 채보는 끝나면 처음부터 무한 반복 (FAILED · 결과창 없음)
//  나가기: 일시정지(ESC) > QUIT 또는 오른쪽 패널의 '나가기' → 곡 선택창 (CoreManager.LoadSongSelectScene 이 End 를 부름)
//
//  5_Test 씬 만들기: 2_Game 씬을 그대로 복제해서 이름만 5_Test 로 바꾸고 Build Settings 에 추가하면 된다.
//   (GameManager 가 이 세션이 켜져 있으면 테스트 모드로 동작하고 조정 패널(GameSettingPanel)을 스스로 만든다)
// ============================================================
public static class GameSettingSession
{
    public const string SceneName = "5_Test";
    public const int SongId = 2;           // 테스트용 곡
    public const int LevelId = 1;          // 쓸 채보 (1 = EASY 칸의 채보 파일 2_1.txt). 없으면 2 → 3 순서로 찾음
    public const string LevelLabel = "TEST LEVEL";
    public const string LevelValue = "0";

    public static bool Active { get; private set; }
    public static bool AutoPlay;           // 패널의 AUTO PLAY (곡 선택창의 오토플레이와 따로)
    public static int ChosenLevel = LevelId;

    // 들어가기 전 상태 (나갈 때 되돌림)
    private static bool prevAuto, prevMv;
    private static int prevLevel;
    private static SongData prevSong;

    public static void Begin(CoreManager cm)
    {
        if (!Active)
        {
            prevAuto = cm.IsAutoPlay; prevMv = cm.IsMvOn; prevLevel = cm.CurrentLevelId; prevSong = cm.CurrentSongData;
        }
        Active = true;
        AutoPlay = false;
        cm.IsAutoPlay = false;
        cm.IsMvOn = false;
    }

    public static void End(CoreManager cm)
    {
        if (!Active) return;
        Active = false;
        AutoPlay = false;
        cm.IsAutoPlay = prevAuto;
        cm.IsMvOn = prevMv;
        cm.CurrentLevelId = prevLevel;
        if (prevSong != null) cm.CurrentSongData = prevSong;   // 곡 선택창이 원래 고른 곡으로 돌아가도록
    }

    // songs.json 에서 곡 하나 읽기 (곡 선택창과 같은 방식)
    public static SongData LoadSong(int id)
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "data", "songs.json");
            if (!File.Exists(path)) return null;
            string json = SongJson.Normalize(File.ReadAllText(path, System.Text.Encoding.UTF8));
            SongData[] arr = JsonHelper.FromJson<SongData>(json);
            SongData found = arr != null ? arr.FirstOrDefault(s => s != null && s.Id == id) : null;
            if (found != null) found.ApplyNameLanguage(SongData.EnglishNames);   // 곡명 표기 (원어 / 영어)
            return found;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GameSetting] songs.json 을 읽지 못했습니다: " + e.Message);
            return null;
        }
    }

    // 실제로 있는 채보 파일의 난이도 (LevelId → 2 → 3 → 1 순서)
    public static int FindLevel(int songId)
    {
        int[] order = { LevelId, 2, 3, 1 };
        foreach (int lv in order)
        {
            string p = Path.Combine(Application.streamingAssetsPath, "chart", $"{songId}_{lv}.txt");
            if (File.Exists(p)) return lv;
        }
        return LevelId;
    }
}