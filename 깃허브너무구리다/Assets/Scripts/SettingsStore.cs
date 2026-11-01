using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// 설정 저장 / 불러오기 / 적용
//  1) StreamingAssets/data/settings.json  : 기본값 + 개발용 값(Mode, EditorSongid)
//  2) persistentDataPath/user_settings.json : 플레이어가 설정창에서 바꾼 값 (1 위에 덮어씀)
//     예) Windows: C:/Users/<이름>/AppData/LocalLow/<회사>/<제품>/user_settings.json
//  - 값을 바꾸면 MarkDirty() → 설정창을 닫을 때 / 씬 전환 / 게임 종료 시 저장
// ============================================================
public static class SettingsStore
{
    public const string UserFileName = "user_settings.json";
    public static readonly string[] DefaultLaneKeys = { "D", "F", "J", "K" };
    public static readonly int[] FrameLimitOptions = { 30, 60, 120, 144, 165, 240, 360, 0 }; // 0 = 제한 없음

    // 레인 키로 쓸 수 없는 키 (ESC = 일시정지, SPACE = 프리키 스페이스 노트)
    public static readonly HashSet<Key> ReservedKeys = new HashSet<Key>
    {
        Key.Escape, Key.Space, Key.LeftWindows, Key.RightWindows, Key.ContextMenu, Key.None
    };

    public static event Action Changed;

    private static SettingsData standalone; // CoreManager 없이 씬을 단독 실행했을 때
    private static bool dirty;

    public static string UserFilePath { get { return Path.Combine(Application.persistentDataPath, UserFileName); } }

    public static SettingsData Current
    {
        get
        {
            if (CoreManager.Instance != null && CoreManager.Instance.CurrentSettings != null)
                return CoreManager.Instance.CurrentSettings;
            if (standalone == null)
            {
                standalone = LoadAll();
                ApplyAll();
            }
            return standalone;
        }
    }

    // ===== 불러오기 / 저장 =====
    public static SettingsData LoadAll()
    {
        var s = new SettingsData();

        string basePath = Path.Combine(Application.streamingAssetsPath, "data", "settings.json");
        try
        {
            if (File.Exists(basePath))
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(basePath, Encoding.UTF8), s);
                MigrateOffsetSign(s, "settings.json");
            }
        }
        catch (Exception e) { Debug.LogWarning("[Settings] settings.json 을 읽지 못했습니다: " + e.Message); }

        try
        {
            if (File.Exists(UserFilePath))
            {
                var user = new SettingsData();
                JsonUtility.FromJsonOverwrite(File.ReadAllText(UserFilePath, Encoding.UTF8), user);
                if (MigrateOffsetSign(user, UserFileName)) dirty = true; // 바뀐 부호로 다시 저장
                CopyUserFields(user, s);
            }
        }
        catch (Exception e) { Debug.LogWarning("[Settings] user_settings.json 을 읽지 못했습니다: " + e.Message); }

        s.OffsetConvention = CurrentOffsetConvention;
        Sanitize(s);
        Debug.Log($"[Settings] Audio {s.AudioOffset * 1000:0}ms, Judge {s.JudgeOffset * 1000:0}ms, Speed {s.SettingSpeed:0.0}, Mode {s.Mode}");
        return s;
    }

    // ===== 오프셋 부호 =====
    //  예전 버전: AUDIO + = 음악을 일찍 재생(노트가 늦어짐), JUDGE + = 판정을 늦춤
    //  지금(osu! 와 같음): + = 노트 · 판정이 빨라짐, - = 늦어짐  → 저장된 예전 값은 부호를 뒤집어 그대로의 효과를 유지
    public const int CurrentOffsetConvention = 1;

    private static bool MigrateOffsetSign(SettingsData d, string source)
    {
        if (d == null || d.OffsetConvention >= CurrentOffsetConvention) return false;
        d.AudioOffset = -d.AudioOffset;
        d.JudgeOffset = -d.JudgeOffset;
        d.OffsetConvention = CurrentOffsetConvention;
        Debug.Log($"[Settings] {source}: 오프셋 부호 규칙을 새 방식(osu! 방향)으로 바꿨습니다. Audio {d.AudioOffset * 1000:0}ms, Judge {d.JudgeOffset * 1000:0}ms");
        return true;
    }

    // 게임 내부에서 쓰는 값 (부호 변환은 여기 한 군데에서만)
    //  MusicLead  : 음악을 노트보다 몇 초 '먼저' 재생할지 (음악이 늦게 들리면 +)
    //  JudgeDelay : 판정 기준을 몇 초 '늦출지' (늦게 치는 사람이면 +)
    public static double MusicLead { get { return -Current.AudioOffset; } }
    public static double JudgeDelay { get { return -Current.JudgeOffset; } }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(UserFilePath, JsonUtility.ToJson(Current, true), Encoding.UTF8);
            dirty = false;
        }
        catch (Exception e) { Debug.LogWarning("[Settings] 저장 실패: " + e.Message); }
    }

    public static void MarkDirty()
    {
        dirty = true;
        if (Changed != null) Changed();
    }

    public static void SaveIfDirty()
    {
        if (dirty) Save();
    }

    // Mode / EditorSongid(개발용)를 제외한 모든 값을 복사
    public static void CopyUserFields(SettingsData from, SettingsData to)
    {
        to.AudioOffset = from.AudioOffset;
        to.JudgeOffset = from.JudgeOffset;
        to.OffsetConvention = from.OffsetConvention;
        to.SettingSpeed = from.SettingSpeed;
        to.judgeoverrange = from.judgeoverrange;
        to.ShowFastSlow = from.ShowFastSlow;
        to.GameSkin = from.GameSkin;
        to.LaneOpacity = from.LaneOpacity;
        to.ShowInfoUI = from.ShowInfoUI;
        to.LaneKeys = from.LaneKeys != null ? (string[])from.LaneKeys.Clone() : (string[])DefaultLaneKeys.Clone();
        to.MasterVolume = from.MasterVolume;
        to.MusicVolume = from.MusicVolume;
        to.HitSoundVolume = from.HitSoundVolume;
        to.HitSoundOn = from.HitSoundOn;
        to.FrameLimit = from.FrameLimit;
        to.VSync = from.VSync;
        to.ShowFps = from.ShowFps;
        to.ScreenMode = from.ScreenMode;
        to.ResolutionWidth = from.ResolutionWidth;
        to.ResolutionHeight = from.ResolutionHeight;
    }

    public static void Sanitize(SettingsData s)
    {
        s.MasterVolume = Mathf.Clamp01(s.MasterVolume);
        s.MusicVolume = Mathf.Clamp01(s.MusicVolume);
        s.HitSoundVolume = Mathf.Clamp01(s.HitSoundVolume);
        s.AudioOffset = Math.Max(-1.0, Math.Min(1.0, s.AudioOffset));
        s.JudgeOffset = Math.Max(-0.3, Math.Min(0.3, s.JudgeOffset));
        s.SettingSpeed = Math.Round(Math.Max(1.0, Math.Min(12.0, s.SettingSpeed)), 1);
        s.judgeoverrange = Mathf.Clamp(s.judgeoverrange, 0, 120);
        s.GameSkin = Mathf.Clamp(s.GameSkin, 0, GameSkins.Names.Length - 1);
        s.LaneOpacity = Mathf.Clamp01(s.LaneOpacity);
        if (Array.IndexOf(FrameLimitOptions, s.FrameLimit) < 0) s.FrameLimit = 0;
        if (s.ScreenMode < -1 || s.ScreenMode > 2) s.ScreenMode = -1;
        s.LaneKeys = ToKeyNames(ParseLaneKeys(s.LaneKeys));
    }

    // ===== 적용 =====
    public static void ApplyAll()
    {
        ApplyAudio();
        ApplyFrameRate();
        ApplyScreen();
        FpsOverlay.SetVisible(Current.ShowFps);
    }

    // 마스터 볼륨은 AudioListener 전체 볼륨으로 (모든 AudioSource 에 적용)
    public static void ApplyAudio()
    {
        AudioListener.volume = Current.MasterVolume;
    }

    public static void ApplyFrameRate()
    {
        var s = Current;
        QualitySettings.vSyncCount = s.VSync ? 1 : 0;
        Application.targetFrameRate = s.VSync ? -1 : (s.FrameLimit > 0 ? s.FrameLimit : -1);
    }

    // 에디터에서는 화면 모드 / 해상도를 바꿀 수 없으므로 빌드에서만 적용
    public static void ApplyScreen()
    {
        if (Application.isEditor) return;
        var s = Current;
        if (s.ScreenMode < 0 && s.ResolutionWidth <= 0) return;

        FullScreenMode mode = s.ScreenMode >= 0 ? ToUnityMode(s.ScreenMode) : Screen.fullScreenMode;
        int w = s.ResolutionWidth > 0 ? s.ResolutionWidth : Screen.width;
        int h = s.ResolutionHeight > 0 ? s.ResolutionHeight : Screen.height;
        if (w != Screen.width || h != Screen.height || mode != Screen.fullScreenMode)
            Screen.SetResolution(w, h, mode);
    }

    public static FullScreenMode ToUnityMode(int mode)
    {
        if (mode == 0) return FullScreenMode.ExclusiveFullScreen;
        if (mode == 1) return FullScreenMode.FullScreenWindow;
        return FullScreenMode.Windowed;
    }

    public static int FromUnityMode(FullScreenMode mode)
    {
        if (mode == FullScreenMode.ExclusiveFullScreen) return 0;
        if (mode == FullScreenMode.FullScreenWindow || mode == FullScreenMode.MaximizedWindow) return 1;
        return 2;
    }

    // ===== 게임에서 쓰는 값 =====
    public static float MusicVolume { get { return Current.MusicVolume; } }

    // 탭 사운드: ON/OFF 와 볼륨을 반영해서 재생
    public static void PlayHitSound(AudioSource source, AudioClip clip)
    {
        var s = Current;
        if (!s.HitSoundOn || source == null || clip == null || s.HitSoundVolume <= 0f) return;
        source.PlayOneShot(clip, s.HitSoundVolume);
    }

    public static Key[] GetLaneKeys()
    {
        return ParseLaneKeys(Current.LaneKeys);
    }

    public static Key[] ParseLaneKeys(string[] names)
    {
        Key[] result = new Key[4];
        var used = new HashSet<Key>();
        bool valid = names != null && names.Length == 4;
        for (int i = 0; valid && i < 4; i++)
        {
            Key k;
            if (!Enum.TryParse(names[i], true, out k) || ReservedKeys.Contains(k) || !used.Add(k)) valid = false;
            else result[i] = k;
        }
        if (!valid)
        {
            for (int i = 0; i < 4; i++) result[i] = (Key)Enum.Parse(typeof(Key), DefaultLaneKeys[i]);
        }
        return result;
    }

    public static string[] ToKeyNames(Key[] keys)
    {
        string[] names = new string[keys.Length];
        for (int i = 0; i < keys.Length; i++) names[i] = keys[i].ToString();
        return names;
    }

    // 현재 키보드 배열 기준 표시 이름 (예: Key.Semicolon → ";")
    public static string KeyDisplayName(Key key)
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            try
            {
                string n = kb[key].displayName;
                if (!string.IsNullOrEmpty(n)) return n.ToUpperInvariant();
            }
            catch (Exception) { }
        }
        return key.ToString().ToUpperInvariant();
    }
}

// ============================================================
// 화면 오른쪽 위 FPS 표시 (설정 > DISPLAY > SHOW FPS)
// ============================================================
public class FpsOverlay : MonoBehaviour
{
    private static FpsOverlay instance;
    private TextMeshProUGUI label;
    private int frames;
    private float elapsed;

    public static void SetVisible(bool visible)
    {
        if (!visible)
        {
            if (instance != null) instance.gameObject.SetActive(false);
            return;
        }
        if (instance == null) Create();
        instance.gameObject.SetActive(true);
    }

    private static void Create()
    {
        GameObject go = new GameObject("FpsOverlay", typeof(RectTransform));
        DontDestroyOnLoad(go);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 1000;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        instance = go.AddComponent<FpsOverlay>();
        GameObject t = new GameObject("Fps", typeof(RectTransform));
        t.transform.SetParent(go.transform, false);
        instance.label = t.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts/MoveSans-Light");
        if (font != null) instance.label.font = font;
        instance.label.fontSize = 22;
        instance.label.alignment = TextAlignmentOptions.TopRight;
        instance.label.color = new Color(1f, 1f, 1f, 0.75f);
        instance.label.raycastTarget = false;
        RectTransform r = (RectTransform)t.transform;
        r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(1f, 1f);
        r.anchoredPosition = new Vector2(-16f, -10f);
        r.sizeDelta = new Vector2(220f, 36f);
        instance.label.text = "FPS";
    }

    void Update()
    {
        frames++;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= 0.5f)
        {
            label.text = "FPS " + Mathf.RoundToInt(frames / elapsed);
            frames = 0;
            elapsed = 0f;
        }
    }
}