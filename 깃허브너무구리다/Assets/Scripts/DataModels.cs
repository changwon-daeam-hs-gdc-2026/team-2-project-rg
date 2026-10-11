using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SettingsData
{
    public double AudioOffset = 0.0;
    public double JudgeOffset = 0.0;
    public int Mode = 2; 
    public int EditorSongid = 1; 
    public double SettingSpeed = 1.0; 
    
    public int judgeoverrange = 10; 
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
    public double BPM;
    public int NoteCount;
    public int HasMV;
    public int ExistSongbar;
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

    // 전체 판정 오차(ms). FAST = 음수(-), SLOW = 양수(+)
    public double AverageErrorMs;
    public int ErrorSampleCount;

    public bool IsAutoPlay;

    // 에디터에서 결과 씬만 단독 실행해 볼 때 쓰는 샘플
    public static ResultData CreateSample()
    {
        return new ResultData
        {
            SongId = 1, SongName = "Glow Embrace", SubName = "", Composer = "Composer Name", LevelId = 3,
            DifficultyName = "INSANE", DifficultyValue = "13",
            Score = 987654, Accuracy = 98.76,
            Excellent = 812, Great = 40, Good = 7, Bad = 2, Miss = 3,
            MaxCombo = 640, AverageErrorMs = -3.4, ErrorSampleCount = 864, IsAutoPlay = false
        };
    }
}

// 난이도 번호(1: EASY, 2: HARD, 3: INSANE) → 이름 / 수치 / 색
public static class DifficultyHelper
{
    public static string GetName(int levelId)
    {
        switch (levelId) { case 1: return "EASY"; case 3: return "INSANE"; default: return "HARD"; }
    }

    public static string GetValue(SongData song, int levelId)
    {
        if (song == null) return "";
        switch (levelId) { case 1: return song.Difficulty_EASY; case 3: return song.Difficulty_INSANE; default: return song.Difficulty_HARD; }
    }

    public static Color GetColor(int levelId)
    {
        string hex = levelId == 1 ? "#5ce1e6" : (levelId == 3 ? "#ff3131" : "#ff751f");
        Color c = Color.white;
        ColorUtility.TryParseHtmlString(hex, out c);
        return c;
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