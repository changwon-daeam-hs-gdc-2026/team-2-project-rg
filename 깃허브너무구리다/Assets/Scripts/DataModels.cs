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