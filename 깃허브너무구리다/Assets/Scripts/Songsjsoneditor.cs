using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// ============================================================
// songs.json 의 특정 곡에 숫자 값 하나만 고쳐 쓰기
//  - JsonUtility 로 통째로 다시 쓰면 SongData 에 없는 값(Length, BarColor 등)이 사라지므로,
//    파일 텍스트에서 해당 곡 { ... } 안의 그 키만 바꾸거나(있으면) 새로 넣는다(없으면).
//  - 들여쓰기·줄바꿈·다른 값은 건드리지 않는다. BOM 이 있던 파일은 BOM 을 유지한다.
// ============================================================
public static class SongsJsonEditor
{
    public static bool SetIntField(string jsonPath, int songId, string key, int value, out string message)
    {
        return SetRawField(jsonPath, songId, key, value.ToString(), out message);
    }

    // 문자열 값 (따옴표·역슬래시는 이스케이프)
    public static bool SetStringField(string jsonPath, int songId, string key, string value, out string message)
    {
        string raw = "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return SetRawField(jsonPath, songId, key, raw, out message);
    }

    // rawValue: JSON 에 그대로 들어갈 값 (숫자 1234 / 문자열 "3:39")
    private static bool SetRawField(string jsonPath, int songId, string key, string rawValue, out string message)
    {
        message = "";
        if (!File.Exists(jsonPath)) { message = "songs.json 이 없습니다: " + jsonPath; return false; }

        byte[] bytes = File.ReadAllBytes(jsonPath);
        bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));

        // 1) 배열 안의 최상위 { } 구간들 찾기 (문자열 안의 괄호는 무시)
        List<int[]> objects = FindTopLevelObjects(text);
        int[] target = null;
        var idRegex = new Regex("\"Id\"\\s*:\\s*(-?\\d+)");
        foreach (var span in objects)
        {
            Match m = idRegex.Match(text, span[0], span[1] - span[0] + 1);
            if (m.Success && int.Parse(m.Groups[1].Value) == songId) { target = span; break; }
        }
        if (target == null) { message = "Id " + songId + " 인 곡을 찾지 못했습니다."; return false; }

        int start = target[0], end = target[1]; // end = '}' 위치
        string obj = text.Substring(start, end - start + 1);
        string newObj;

        var keyRegex = new Regex("(\"" + Regex.Escape(key) + "\"\\s*:\\s*)(\"(?:\\\\.|[^\"\\\\])*\"|-?\\d+(?:\\.\\d+)?|null|true|false)");
        Match km = keyRegex.Match(obj);
        if (km.Success)
        {
            if (km.Groups[2].Value == rawValue) { message = "변경 없음 (" + rawValue + ")"; return true; }
            newObj = obj.Substring(0, km.Groups[2].Index) + rawValue + obj.Substring(km.Groups[2].Index + km.Groups[2].Length);
            message = km.Groups[2].Value + " → " + rawValue;
        }
        else
        {
            // 마지막 값 뒤에 ,"key": value 를 붙인다. 여러 줄 객체면 첫 키의 들여쓰기를 따른다.
            int close = obj.Length - 1;
            int lastContent = close - 1;
            while (lastContent > 0 && char.IsWhiteSpace(obj[lastContent])) lastContent--;
            bool empty = obj[lastContent] == '{';

            string insert;
            Match indent = Regex.Match(obj, "\\n([ \\t]*)\"");
            if (indent.Success)
            {
                string nl = obj.Contains("\r\n") ? "\r\n" : "\n";
                insert = (empty ? "" : ",") + nl + indent.Groups[1].Value + "\"" + key + "\": " + rawValue;
            }
            else
            {
                insert = (empty ? " " : ", ") + "\"" + key + "\": " + rawValue;
            }
            newObj = obj.Substring(0, lastContent + 1) + insert + obj.Substring(lastContent + 1);
            message = "새로 추가 (" + rawValue + ")";
        }

        string result = text.Substring(0, start) + newObj + text.Substring(end + 1);

        // 2) 임시 파일에 쓰고 교체 (쓰는 도중 실패해도 원본이 깨지지 않도록)
        try
        {
            string tmp = jsonPath + ".tmp";
            File.WriteAllText(tmp, result, new UTF8Encoding(hasBom));
            File.Copy(tmp, jsonPath, true);
            File.Delete(tmp);
        }
        catch (Exception e) { message = e.Message; return false; }
        return true;
    }

    private static List<int[]> FindTopLevelObjects(string text)
    {
        var list = new List<int[]>();
        int depth = 0, objStart = -1;
        bool inString = false, escape = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '{' || c == '[')
            {
                depth++;
                if (c == '{' && depth == 2) objStart = i; // [ 다음 { 가 곡 하나
            }
            else if (c == '}' || c == ']')
            {
                if (c == '}' && depth == 2 && objStart >= 0) { list.Add(new[] { objStart, i }); objStart = -1; }
                depth--;
            }
        }
        return list;
    }
}