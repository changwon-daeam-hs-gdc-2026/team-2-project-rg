using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
// 채보 에디터 (새 디자인) — 이제 모든 곡 · 모든 난이도(SPECIAL 포함)를 이 에디터로 연다
//  (클래스 이름은 예전 '영역 전용 에디터' 그대로. 영역(AREA) · 화살표 노트 기능은 삭제됨)
//
//  ChartEditorManager 가 이 에디터를 띄운다 (같은 3_Editor 씬, 화면 · 조작 · 그리기 모두 따로).
//   ChartEditorManager 인스펙터의 Use New Editor 를 끄면 예전 에디터 화면으로 열림
//   - 노트는 SIMPLE 스킨 모양(둥근 단색 노트 · 빛나는 RED 노트 · 반투명 롱노트 몸통)으로 통일
//   - 레인 사라짐/드러남(채보 줄 FOLD/UNFOLD = HIDE/SHOW) 편집
//   - 예전 채보의 AREA 줄은 읽지 않고(저장하면 지워짐), 화살표 노트(7 · 8)는 일반 탭으로 바뀜
//   - 화면: 위 막대(곡 · 재생 시간 · 저장 상태) · 왼쪽 도구 · 오른쪽 정보 · 미니맵 · 아래 단축키 → AreaChartEditorHud.cs
//   - 일반 노트 · 롱노트 · RED 노트 · 층(1층/2층) · 마디선 · BPM · 변속 · STOP/START · 오프셋도 편집 가능
//   - 저장 형식은 게임과 같은 채보 txt. 이 에디터가 모르는 줄(PREVIEW, DEATHAREA 등)은 그대로 보존
//
//  조작 (화면 아래쪽에도 표시)
//   마우스 좌클릭 : 노트 놓기       우클릭 : 노트 지우기      휠 : 이동 (SHIFT = 1마디)
//   1 탭  2 롱노트 시작  3 롱노트 끝  4 마디선   5 RED 노트 켜기/끄기   6 드래그 노트 켜기/끄기
//     (드래그 켜짐: 1 = 드래그 노트(9), 2 = 드래그로 시작하는 롱노트(10), 끝은 3 그대로)
//   노트 폭: 클릭한 채 오른쪽으로 끌면 1~4칸 (RED 뿐 아니라 일반 탭 · 롱노트 모두) → 저장 형식 "1_3" (3칸 탭)
//   Q 층 전환 (2층 ↔ 1층)
//   7 슬라이드 편집 켜기/끄기 (레인을 넘나드는 롱노트, 프로세카처럼 곡선)
//     롱노트(머리 · 몸통 · 끝)를 클릭 = 고름 · 점(머리 · 중계점 · 끝)을 끌면 레인 이동
//     고른 롱노트의 머리 ~ 끝 사이 빈 곳 클릭 = 중계점 추가 (바로 끌 수 있음) · 중계점 우클릭 = 삭제
//     C = 마우스가 있는 구간의 곡선 바꾸기 (직선 → 처음 천천히 IN → 끝 천천히 OUT)
//     저장 형식: 같은 번호를 붙인 점들  예) 2~1  14~1i  3~1   (14 = 중계점, i / o = 그 점 → 다음 점 곡선)
//   V + 클릭 : 레인 사라짐 (시간 입력)         B + 클릭 : 레인 드러남    (V/B + 우클릭 : 삭제)
//   U + 클릭 : 그 박의 변속에 게임 SPEED UP / DOWN 글자 표시 켜고 끄기 (채보 줄 끝 ", SHOW")
//   G + 클릭 : 난이도 공개 (숨긴 난이도 "?" 가 이 시점부터 실제 값으로, 채보에 하나만)   G + 우클릭 : 삭제
//     저장 형식: REVEAL, 마디, 분자, 분모, LEVEL
//   H : 단축키 도움말
//   D + 클릭 : BPM    F + 클릭 : 변속 (편집 중인 층만, SHIFT+F = 두 층 모두)    E + 클릭 : STOP    R + 클릭 : START   (+ 우클릭 : 삭제)
//   SPACE 재생/정지   ← → 박자 단위   ↑ ↓ 확대/축소   PAGE UP/DOWN 1마디   HOME 처음으로
//   - / = 오프셋 1ms (SHIFT 10ms)   O 오프셋 직접 입력   S 저장   ESC 3번 저장 후 나가기
// ============================================================
public partial class AreaChartEditor : MonoBehaviour
{
    // ===================== 채보 데이터 =====================
    private class Pos
    {
        public long M = 1, N = 0, D = 4;
        public double Beat { get { return (M - 1) * 4.0 + (double)N / Math.Max(1, D) * 4.0; } }
        public bool Same(Pos o) { return Math.Abs(Beat - o.Beat) < 1e-6; }
    }
    private class ANote : Pos
    {
        public int Lane, Type, Width = 1, Layer = NoteLayer.Top; public bool Red; public ANote Tail, Head;
        public int SlideId, Curve;            // 슬라이드: 번호 (0 = 아님) · 이 점 → 다음 점 곡선
        public List<ANote> SlidePts;          // 슬라이드 머리에만: 머리 · 중계점들 · 끝 (박 순서)
    }
    private class ABpm : Pos { public double Bpm; public double AnchorMs = double.NaN; public bool HasAnchor { get { return !double.IsNaN(AnchorMs); } } }
    private class ASpeed : Pos { public int Layer; public double Value; public bool Show; }
    private class AMark : Pos { public bool IsStop; }
    private class AFold : Pos { public bool Fold; public double Duration; }

    private readonly List<ANote> notes = new List<ANote>();
    private readonly List<ABpm> bpms = new List<ABpm>();
    private readonly List<ASpeed> speeds = new List<ASpeed>();
    private readonly List<AMark> marks = new List<AMark>();
    private readonly List<AFold> folds = new List<AFold>();
    private Pos reveal;                                          // 난이도 공개 시점 (없으면 null)
    private readonly List<string> others = new List<string>();   // 이 에디터가 편집하지 않는 줄 (그대로 저장)
    private double initialBpm = 120.0, initialSpeed = 1.0;
    private double audioStart = 0.0;                             // 차트 시계에서 음악이 시작되는 시각 (= -MUSICOFFSET/1000)
    private bool linksDirty = true;

    // ===================== 편집 상태 =====================
    private int songId, levelId;
    private string title = "";
    private Camera cam;
    private double cursorBeat = 0.0;          // 판정선 위치의 박
    private double hoverBeat = 0.0;
    private int hoverLane = -1;
    private static readonly int[] SnapDivs = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48 };
    private int snapIndex = 3;                // 박당 4칸 (= 1/16 음표)
    private int noteType = 1;
    private bool redMode = false;
    private bool dragMode = false;   // 6: 드래그 노트 (RED 와 동시에 켤 수 없음)
    private int editLayer = NoteLayer.Top;
    private float beatSpacing = 2.4f;         // 1박의 화면 길이 (월드 유닛)
    private ANote draggingWide;              // 폭을 끌어서 정하는 중인 노트 (RED · 일반 모두 1~4칸)
    // 7: 슬라이드 편집 (레인을 넘나드는 롱노트)
    private bool slideMode = false;
    private ANote selSlide;                  // 고른 롱노트의 머리
    private ANote draggingPoint;             // 레인을 옮기는 중인 점 (머리 · 중계점 · 끝)
    private static readonly Color SlideUiColor = new Color(0.62f, 0.86f, 1f, 1f);
    private int escCount; private float escTimer;
    private string status = ""; private float statusTimer;

    // 숫자 입력창
    private enum PromptKind { None, Bpm, Speed, Fold, Unfold, Offset }
    private PromptKind prompt = PromptKind.None;
    private string promptBuffer = "";
    private int promptLayer;                  // 변속 입력: 0 = 두 층 모두, 1 · 2 = 그 층만
    private Pos promptPos;

    // ===================== 재생 =====================
    private AudioSource bgm, sfx;
    private AudioClip hitClip;
    private bool playing;
    private double playTime;                  // 차트 시각 (초)
    private bool scheduled, waitingAudio;
    private double dspAudioZero;

    // ===================== 그리기 =====================
    private const float LaneW = 1.0f;         // 레인 1칸 폭 (게임 0.8 의 1.25 배)
    private const float K = LaneW / 0.8f;     // 게임 크기 → 에디터 크기 배율
    private const float HitY = -3.4f;
    private float Left { get { return -2f * LaneW; } }
    private float Right { get { return 2f * LaneW; } }

    private Transform worldRoot;
    private SpritePool pool;
    private Sprite redNoteSprite, redBodySprite;

    private Canvas hud;
    private TextMeshProUGUI promptTitle, promptValue, promptHint, promptKeys;
    private GameObject promptPanel;
    private readonly List<TextMeshProUGUI> labelPool = new List<TextMeshProUGUI>();
    private int labelUsed;
    private RectTransform labelRoot;

    // =====================================================================
    //  시작
    // =====================================================================
    // ===================== 글꼴 · 언어 =====================
    //  font  : 인스펙터(ChartEditorManager > Area Editor Font)에서 지정. 비우면 Resources/Fonts/AreaEditorFont → 기존 에디터 글꼴 순
    //  korean: 한국어 / 영어 표시 (L 키로 바꿈, 다음에 열 때도 기억)
    private TMP_FontAsset font;
    private float fontScale = 1f;
    private bool korean = true;
    private const string PrefKorean = "AreaEditor.Korean";
    private string T(string ko, string en) { return korean ? ko : en; }

    // ===================== 음량 (기존 에디터처럼) =====================
    //  musicVolume : 음악 볼륨     hitVolume : 노트 탭음 볼륨     longHitSound : 롱노트 끝 판정(꼬리) 탭음 켜기/끄기
    //  처음 값은 ChartEditorManager 인스펙터 값, 에디터 안에서 바꾸면 다음에도 기억
    private float musicVolume = 0.4f, hitVolume = 1f;
    private bool longHitSound = true;
    private const string PrefMusic = "AreaEditor.MusicVolume", PrefHit = "AreaEditor.HitVolume", PrefLong = "AreaEditor.LongHitSound";

    // 기존 에디터 인스펙터 값으로 시작 (Launch 직후 ChartEditorManager 가 부름)
    public void SetAudioDefaults(float music, float hit, bool longHit)
    {
        musicVolume = PlayerPrefs.GetFloat(PrefMusic, Mathf.Clamp01(music));
        hitVolume = PlayerPrefs.GetFloat(PrefHit, Mathf.Clamp01(hit));
        longHitSound = PlayerPrefs.GetInt(PrefLong, longHit ? 1 : 0) == 1;
        if (bgm != null) bgm.volume = musicVolume;
    }

    private void SaveAudioPrefs()
    {
        PlayerPrefs.SetFloat(PrefMusic, musicVolume);
        PlayerPrefs.SetFloat(PrefHit, hitVolume);
        PlayerPrefs.SetInt(PrefLong, longHitSound ? 1 : 0);
    }

    public static AreaChartEditor Launch(Scene scene, int songId, int levelId, Camera cam, string title)
    {
        return Launch(scene, songId, levelId, cam, title, null, 1f, true);
    }

    public static AreaChartEditor Launch(Scene scene, int songId, int levelId, Camera cam, string title, TMP_FontAsset font, float fontScale, bool korean)
    {
        GameObject go = new GameObject("AreaChartEditor");
        if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
        AreaChartEditor ed = go.AddComponent<AreaChartEditor>();
        ed.songId = songId;
        ed.levelId = levelId;
        ed.title = string.IsNullOrEmpty(title) ? $"Song {songId}" : title;
        ed.cam = cam != null ? cam : Camera.main;
        ed.font = font != null ? font : Resources.Load<TMP_FontAsset>("Fonts/AreaEditorFont");
        ed.fontScale = Mathf.Clamp(fontScale, 0.5f, 2f);
        ed.korean = PlayerPrefs.GetInt(PrefKorean, korean ? 1 : 0) == 1;
        ed.Init();
        return ed;
    }

    private void Init()
    {
        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.065f, 0.10f);
        }
        worldRoot = new GameObject("AreaEditorWorld").transform;
        worldRoot.SetParent(transform, false);
        pool = new SpritePool(worldRoot);
        redNoteSprite = SimpleGameSkin.CreateRedNoteSprite();
        redBodySprite = SimpleGameSkin.CreateRedBodySprite();

        bgm = gameObject.AddComponent<AudioSource>();
        bgm.playOnAwake = false;
        musicVolume = PlayerPrefs.GetFloat(PrefMusic, musicVolume);
        hitVolume = PlayerPrefs.GetFloat(PrefHit, hitVolume);
        longHitSound = PlayerPrefs.GetInt(PrefLong, longHitSound ? 1 : 0) == 1;
        bgm.volume = musicVolume;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        hitClip = Resources.Load<AudioClip>("Audio/etc/hitsound");
        // [버그 수정] 탭음 데이터를 미리 올려 둠 (첫 PlayOneShot 때 아직 안 올라와 있으면 첫 소리가 나지 않음)
        if (hitClip != null && hitClip.loadState == AudioDataLoadState.Unloaded) hitClip.LoadAudioData();
        // 난이도 전용 음원(Audio/Full/{곡ID}_{난이도})이 있으면 그것, 없으면 기존 음원
        AudioClip clip = Resources.Load<AudioClip>($"Audio/Full/{songId}_{levelId}");
        if (clip == null) clip = Resources.Load<AudioClip>($"Audio/Full/{songId}");
        if (clip != null)
        {
            bgm.clip = clip;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        }

        BuildHud();
        LoadChart();
        MarkSaved();
        SetStatus(T("채보 에디터  ·  ", "CHART EDITOR  ·  ") + ChartPath());
    }

    private string ChartPath() { return Path.Combine(Application.streamingAssetsPath, "chart", $"{songId}_{levelId}.txt"); }

    // =====================================================================
    //  불러오기 / 저장
    // =====================================================================
    private static double Dbl(string s) { return double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture); }
    private static long L(string s) { return long.Parse(s.Trim()); }

    private void LoadChart()
    {
        notes.Clear(); bpms.Clear(); speeds.Clear(); marks.Clear(); folds.Clear(); others.Clear();
        reveal = null;
        initialBpm = 120.0; initialSpeed = 1.0; audioStart = 0.0;
        string path = ChartPath();
        if (!File.Exists(path)) { SetStatus(T("새 채보: ", "New chart: ") + path); return; }

        bool hasMusicOffset = false;
        foreach (string raw in File.ReadAllLines(path, System.Text.Encoding.UTF8))
        {
            int hash = raw.IndexOf('#');
            string line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
            if (line.Length == 0) continue;
            string[] d = line.Split(',');
            string type = d[0].Trim().ToUpperInvariant();
            try
            {
                double start; bool isNew;
                if (ChartOffset.TryParse(type, d, out start, out isNew))
                {
                    if (isNew || !hasMusicOffset) { audioStart = start; if (isNew) hasMusicOffset = true; }
                }
                else if (line.StartsWith("KeyMode:", StringComparison.OrdinalIgnoreCase)) { }
                else if (line.StartsWith("NoteSpeed:", StringComparison.OrdinalIgnoreCase)) initialSpeed = Dbl(line.Split(':')[1]);
                else if (type == "BPM" && d.Length == 2) initialBpm = Dbl(d[1]);
                else if (type == "BPM" && d.Length >= 5) bpms.Add(new ABpm { M = L(d[1]), N = L(d[2]), D = L(d[3]), Bpm = Dbl(d[4]) });
                else if (type == "TIMING" && d.Length >= 6) bpms.Add(new ABpm { M = L(d[1]), N = L(d[2]), D = L(d[3]), Bpm = Dbl(d[4]), AnchorMs = Dbl(d[5]) });
                else if (type == "SPEED" && d.Length == 2) initialSpeed = Dbl(d[1]);
                else if (type == "SPEED" && d.Length >= 6)
                    speeds.Add(new ASpeed { M = L(d[1]), N = L(d[2]), D = L(d[3]), Layer = int.Parse(d[4].Trim()), Value = Dbl(d[5]),
                                            Show = d.Length >= 7 && d[6].Trim().ToUpperInvariant() == "SHOW" });
                else if (type == "SPEED" && d.Length >= 5) speeds.Add(new ASpeed { M = L(d[1]), N = L(d[2]), D = L(d[3]), Layer = 0, Value = Dbl(d[4]) });
                else if ((type == "STOP" || type == "START") && d.Length >= 4) marks.Add(new AMark { M = L(d[1]), N = L(d[2]), D = L(d[3]), IsStop = type == "STOP" });
                else if (type == "AREA") { }                                                           // 삭제된 기능 (영역) — 저장하면 지워짐
                else if ((type == "FOLD" || type == "UNFOLD" || type == "HIDE" || type == "SHOW") && d.Length >= 5)
                    folds.Add(new AFold { M = L(d[1]), N = L(d[2]), D = L(d[3]), Fold = type == "FOLD" || type == "HIDE", Duration = Dbl(d[4]) });   // 레인 사라짐 / 드러남
                else if (type == "REVEAL" && d.Length >= 4)                                            // 난이도 공개 (가장 앞 하나만)
                {
                    Pos rp = new Pos { M = L(d[1]), N = L(d[2]), D = L(d[3]) };
                    if (reveal == null || rp.Beat < reveal.Beat) reveal = rp;
                }
                else if (type == "NOTE" && d.Length >= 5) ParseNoteLine(d);
                else if (type == "FREEKEY") { }                                                        // 삭제된 기능
                else if (type == "UI" && d.Length >= 2 && (d[1].Trim() == "BasicUI" || d[1].Trim() == "FREEKEYUI")) { }
                else others.Add(line);
            }
            catch (Exception e) { Debug.LogWarning("[ChartEditor] 읽지 못한 줄: " + line + " (" + e.Message + ")"); others.Add(line); }
        }
        // 0박의 BPM 줄은 첫 BPM 으로
        var zero = bpms.FirstOrDefault(b => b.Beat <= 1e-9);
        if (zero != null) { initialBpm = zero.Bpm; bpms.Remove(zero); }
        SortAll();
    }

    private void ParseNoteLine(string[] d)
    {
        long m = L(d[1]), n = L(d[2]), den = L(d[3]);
        if (d.Length == 5 && !d[4].Contains("_") && !d[4].Contains("(") && d[4].Trim().Length >= 4)
        {
            string val = d[4].Trim();   // 구형 압축 포맷 (예: 1000)
            for (int i = 0; i < val.Length && i < 4; i++)
            {
                if (val[i] == '0' || !char.IsDigit(val[i])) continue;
                int t = NoteTypes.Normalize(val[i] - '0');   // 예전 화살표(7 · 8) → 탭
                if (t == 5 || t == 6) continue;
                notes.Add(new ANote { M = m, N = n, D = den, Lane = i, Type = t });
            }
            return;
        }
        for (int lane = 0; lane < 4 && lane + 4 < d.Length; lane++)
        {
            NoteCellParser.SplitCell(d[lane + 4], out string top, out string bottom);
            AddToken(top, m, n, den, lane, NoteLayer.Top);
            AddToken(bottom, m, n, den, lane, NoteLayer.Bottom);
        }
    }

    private void AddToken(string token, long m, long n, long den, int lane, int layer)
    {
        string core = NoteCellParser.SplitSlide(token, out int slideId, out int curve);   // 슬라이드 표시 "~번호[곡선]"
        if (!NoteCellParser.TryParseToken(core, out int type, out int width, out bool red)) return;
        notes.Add(new ANote { M = m, N = n, D = den, Lane = lane, Type = NoteTypes.Normalize(type), Width = Mathf.Clamp(width, 1, 4 - lane), Red = red, Layer = layer,
                              SlideId = slideId, Curve = curve });   // 예전 화살표(7 · 8) → 탭
    }

    private void SortAll()
    {
        bpms.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        speeds.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        marks.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        folds.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        linksDirty = true;
    }

    private static string F(double v) { return v.ToString("0.######", CultureInfo.InvariantCulture); }

    private void Save()
    {
        SortAll();
        var o = new List<string>();
        o.Add($"BPM, {F(initialBpm)}");
        o.Add(ChartOffset.Line(audioStart));
        o.Add("KeyMode: 4");
        o.Add($"SPEED, {initialSpeed.ToString("0.00", CultureInfo.InvariantCulture)}");
        foreach (var b in bpms)
            o.Add(b.HasAnchor ? $"TIMING, {b.M}, {b.N}, {b.D}, {F(b.Bpm)}, {F(b.AnchorMs)}" : $"BPM, {b.M}, {b.N}, {b.D}, {F(b.Bpm)}");
        foreach (var s in speeds)
        {
            string v = s.Value.ToString("0.00", CultureInfo.InvariantCulture);
            if (s.Layer == 0 && !s.Show) o.Add($"SPEED, {s.M}, {s.N}, {s.D}, {v}");
            else o.Add($"SPEED, {s.M}, {s.N}, {s.D}, {s.Layer}, {v}" + (s.Show ? ", SHOW" : ""));
        }
        foreach (var k in marks) o.Add($"{(k.IsStop ? "STOP" : "START")}, {k.M}, {k.N}, {k.D}");
        foreach (var f in folds) o.Add($"{(f.Fold ? "FOLD" : "UNFOLD")}, {f.M}, {f.N}, {f.D}, {f.Duration.ToString("0.###", CultureInfo.InvariantCulture)}");
        if (reveal != null) o.Add($"REVEAL, {reveal.M}, {reveal.N}, {reveal.D}, LEVEL");
        o.AddRange(others);

        foreach (var g in notes.GroupBy(n => new { n.M, n.N, n.D }).OrderBy(g => g.First().Beat))
        {
            string[] top = new string[4], bottom = new string[4];
            foreach (var n in g)
            {
                if (n.Lane < 0 || n.Lane > 3) continue;
                // RED 는 항상 "종류_폭", 일반 노트는 2칸 이상일 때만 "종류_폭" (예: 1_3 = 3칸 탭)
                string token = (n.Red || n.Width > 1) ? $"{n.Type}_{n.Width}" : n.Type.ToString(CultureInfo.InvariantCulture);
                token = NoteCellParser.SlideToken(token, n.SlideId, n.Curve);   // 슬라이드: "~번호[곡선]"
                if (n.Layer == NoteLayer.Bottom) bottom[n.Lane] = token; else top[n.Lane] = token;
            }
            string[] cells = new string[4];
            for (int i = 0; i < 4; i++) cells[i] = (top[i] ?? "0") + (bottom[i] != null ? $"({bottom[i]})" : "");
            o.Add($"NOTE, {g.Key.M}, {g.Key.N}, {g.Key.D}, {string.Join(", ", cells)}");
        }

        string path = ChartPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, o, System.Text.Encoding.UTF8);
        }
        catch (Exception e) { SetStatus(T("저장 실패: ", "SAVE FAILED: ") + e.Message); return; }

        // songs.json 노트 수 · 곡 길이 (게임과 같은 규칙)
        ChartNoteCounter.Result r = ChartNoteCounter.CountFile(path);
        string json = Path.Combine(Application.streamingAssetsPath, "data", "songs.json");
        string msg;
        if (r.Ok) SongsJsonEditor.SetIntField(json, songId, "NoteCount_" + DifficultyHelper.GetName(levelId), r.Total, out msg);
        if (bgm.clip != null)
        {
            int sec = Mathf.FloorToInt(bgm.clip.length);
            SongsJsonEditor.SetStringField(json, songId, "Length", (sec / 60) + ":" + (sec % 60).ToString("00"), out msg);
        }
        if (r.Ok && CoreManager.Instance != null && CoreManager.Instance.CurrentSongData != null && CoreManager.Instance.CurrentSongData.Id == songId)
        {
            SongData s = CoreManager.Instance.CurrentSongData;
            if (levelId == 1) s.NoteCount_EASY = r.Total; else if (levelId == 3) s.NoteCount_INSANE = r.Total;
            else if (levelId == DifficultyHelper.Special) s.NoteCount_SPECIAL = r.Total; else s.NoteCount_HARD = r.Total;
        }
        MarkSaved();
        SetStatus(T($"저장됨  ·  노트 {notes.Count}개  ·  전체 판정 수 {(r.Ok ? r.Total : 0)}", $"SAVED  ·  {notes.Count} notes  ·  total judge {(r.Ok ? r.Total : 0)}"));
    }

    // =====================================================================
    //  박 ↔ 시각 (BPM 누적 + TIMING 고정 시각, 게임과 같음)
    // =====================================================================
    private struct Seg { public double Beat, Time, Bpm; }
    private readonly List<Seg> segs = new List<Seg>();

    private void BuildSegs()
    {
        segs.Clear();
        segs.Add(new Seg { Beat = 0, Time = 0, Bpm = initialBpm });
        foreach (var b in bpms)
        {
            if (b.Beat <= 1e-9) continue;
            Seg p = segs[segs.Count - 1];
            double t = b.HasAnchor ? b.AnchorMs / 1000.0 : p.Time + (b.Beat - p.Beat) * 60.0 / p.Bpm;
            segs.Add(new Seg { Beat = b.Beat, Time = t, Bpm = b.Bpm });
        }
    }

    private double TimeOf(double beat)
    {
        if (beat <= 0) return beat * 60.0 / segs[0].Bpm;
        Seg s = segs[0];
        for (int i = 1; i < segs.Count && segs[i].Beat <= beat + 1e-9; i++) s = segs[i];
        return s.Time + (beat - s.Beat) * 60.0 / s.Bpm;
    }

    private double BeatOf(double time)
    {
        if (time < 0) return time * segs[0].Bpm / 60.0;
        int idx = 0;
        for (int i = 1; i < segs.Count && segs[i].Time <= time + 1e-9; i++) idx = i;
        double beat = segs[idx].Beat + (time - segs[idx].Time) * segs[idx].Bpm / 60.0;
        if (idx + 1 < segs.Count) beat = Math.Min(beat, segs[idx + 1].Beat);
        return beat;
    }

    // =====================================================================
    //  매 프레임
    // =====================================================================
    void Update()
    {
        BuildSegs();
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        if (statusTimer > 0f) statusTimer -= Time.unscaledDeltaTime;
        if (escTimer > 0f) { escTimer -= Time.unscaledDeltaTime; if (escTimer <= 0f) escCount = 0; }

        if (prompt != PromptKind.None) { UpdatePrompt(kb); return; }
        if (SettingsWindow.BlocksGameInput) return;

        UpdatePlayback(kb);
        UpdateKeys(kb);
        if (!playing && mouse != null) UpdateMouse(kb, mouse);
    }

    void LateUpdate()
    {
        if (segs.Count == 0) BuildSegs();
        if (linksDirty) RebuildLinks();
        Draw();
        DrawHud();
    }

    // ----- 키보드 -----
    private double Step { get { return 1.0 / SnapDivs[snapIndex]; } }

    private void UpdateKeys(Keyboard kb)
    {
        bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        // H : 단축키 도움말 (열려 있는 동안 다른 키는 받지 않음, ESC 로도 닫힘)
        if (kb.hKey.wasPressedThisFrame) helpOpen = !helpOpen;
        if (helpOpen) { if (kb.escapeKey.wasPressedThisFrame) helpOpen = false; return; }
        if (kb.spaceKey.wasPressedThisFrame) TogglePlay();

        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) noteType = 1;
        if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) noteType = 2;
        if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) noteType = 3;
        if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) noteType = 4;
        if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) { redMode = !redMode; if (redMode) dragMode = false; }
        if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) { dragMode = !dragMode; if (dragMode) redMode = false; }
        if (kb.qKey.wasPressedThisFrame) editLayer = editLayer == NoteLayer.Top ? NoteLayer.Bottom : NoteLayer.Top;
        if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) ToggleSlideMode();
        if (kb.cKey.wasPressedThisFrame && slideMode && !playing) CycleSlideCurve();

        if (kb.leftArrowKey.wasPressedThisFrame) snapIndex = Mathf.Max(0, snapIndex - 1);
        if (kb.rightArrowKey.wasPressedThisFrame) snapIndex = Mathf.Min(SnapDivs.Length - 1, snapIndex + 1);
        if (kb.upArrowKey.wasPressedThisFrame) beatSpacing = Mathf.Min(10f, beatSpacing * 1.2f);
        if (kb.downArrowKey.wasPressedThisFrame) beatSpacing = Mathf.Max(0.5f, beatSpacing / 1.2f);

        if (!playing)
        {
            if (kb.pageUpKey.wasPressedThisFrame) cursorBeat = Math.Floor(cursorBeat / 4.0 + 1e-6) * 4.0 + 4.0;
            if (kb.pageDownKey.wasPressedThisFrame) cursorBeat = Math.Max(0.0, Math.Ceiling(cursorBeat / 4.0 - 1e-6) * 4.0 - 4.0);
            if (kb.homeKey.wasPressedThisFrame) cursorBeat = 0.0;
        }

        // 오프셋: 기본 1ms, SHIFT 10ms.  + = MUSICOFFSET 증가 = 노트가 늦게 (osu! 와 같은 방향)
        double step = shift ? 0.010 : 0.001;
        if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) AdjustOffset(-step);
        if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) AdjustOffset(step);
        if (kb.oKey.wasPressedThisFrame && !playing) OpenPrompt(PromptKind.Offset, null);

        if (kb.sKey.wasPressedThisFrame) Save();
        // 음량: F1 / F2 음악,  F3 / F4 탭음 (5% 단위),  F5 롱노트 끝 탭음 켜기/끄기
        if (kb.f1Key.wasPressedThisFrame || kb.f2Key.wasPressedThisFrame)
        {
            musicVolume = Mathf.Clamp01(Mathf.Round((musicVolume + (kb.f2Key.wasPressedThisFrame ? 0.05f : -0.05f)) * 20f) / 20f);
            bgm.volume = musicVolume;
            SaveAudioPrefs();
            SetStatus(T("음악 볼륨 ", "MUSIC VOLUME ") + Mathf.RoundToInt(musicVolume * 100f) + "%");
        }
        if (kb.f3Key.wasPressedThisFrame || kb.f4Key.wasPressedThisFrame)
        {
            hitVolume = Mathf.Clamp01(Mathf.Round((hitVolume + (kb.f4Key.wasPressedThisFrame ? 0.05f : -0.05f)) * 20f) / 20f);
            SaveAudioPrefs();
            if (hitClip != null && hitVolume > 0f) sfx.PlayOneShot(hitClip, hitVolume);   // 미리 들려줌
            SetStatus(T("탭음 볼륨 ", "HIT SOUND ") + Mathf.RoundToInt(hitVolume * 100f) + "%");
        }
        if (kb.f5Key.wasPressedThisFrame)
        {
            longHitSound = !longHitSound;
            SaveAudioPrefs();
            SetStatus(T("롱노트 끝 탭음 ", "LONG NOTE END HIT SOUND ") + (longHitSound ? "ON" : "OFF"));
        }

        if (kb.lKey.wasPressedThisFrame)
        {
            korean = !korean;
            PlayerPrefs.SetInt(PrefKorean, korean ? 1 : 0);
            RefreshStaticTexts();
            SetStatus(T("표시 언어: 한국어", "Language: English"));
        }

        if (kb.escapeKey.wasPressedThisFrame)
        {
            escCount++; escTimer = 0.8f;
            if (escCount >= 3) { escCount = 0; Save(); Exit(); }
            else SetStatus(T($"ESC {escCount}/3  (3번 누르면 저장 후 나가기)", $"ESC {escCount}/3  (3 times = save & exit)"));
        }
    }

    private void AdjustOffset(double addMusicOffsetSec)
    {
        audioStart = Math.Round(audioStart - addMusicOffsetSec, 6);
        if (playing && scheduled)
        {
            playTime -= addMusicOffsetSec;                          // 음악은 그대로, 노트만 이동
            playFromTime = Math.Min(playFromTime, playTime);
            lastHitCheck = playTime + 0.035;                         // 옮긴 만큼 탭음이 몰려 나오지 않게
        }
        SetStatus(T("음악 오프셋 ", "MUSICOFFSET ") + ChartOffset.Display(audioStart));
    }

    // ----- 마우스 -----
    private void UpdateMouse(Keyboard kb, Mouse mouse)
    {
        if (cam == null) return;
        Vector2 mp = mouse.position.ReadValue();
        Vector3 w = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 10f));
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            double s = shift ? 4.0 : Step;
            cursorBeat = Math.Max(-8.0, Math.Round((cursorBeat + Math.Sign(scroll) * s) / s) * s);
        }

        double rawBeat = cursorBeat + (w.y - HitY) / beatSpacing;
        hoverBeat = Math.Max(0.0, Math.Round(rawBeat / Step) * Step);
        int lane = Mathf.FloorToInt((w.x - Left) / LaneW);
        hoverLane = (lane >= 0 && lane < 4) ? lane : -1;

        bool ld = mouse.leftButton.wasPressedThisFrame, rd = mouse.rightButton.wasPressedThisFrame;
        bool lHeld = mouse.leftButton.isPressed, lUp = mouse.leftButton.wasReleasedThisFrame;

        // 드래그 중 (RED 폭 · 영역 칸)
        if (draggingWide != null)
        {
            int end = Mathf.Clamp(lane, draggingWide.Lane, 3);
            draggingWide.Width = end - draggingWide.Lane + 1;
            if (lUp || !lHeld)
            {
                // 넓힌 칸에 원래 있던 같은 박 · 같은 층 노트는 지움 (겹치지 않게)
                ANote wide = draggingWide;
                notes.RemoveAll(x => x != wide && x.Layer == wide.Layer && x.Same(wide) && x.Type != 4 &&
                                     x.Lane <= wide.Lane + wide.Width - 1 && x.Lane + x.Width - 1 >= wide.Lane);
                draggingWide = null; linksDirty = true;
            }
            return;
        }

        // 슬라이드 점 끌기 (레인만 바뀜)
        if (draggingPoint != null)
        {
            if (w.x >= Left - LaneW && w.x <= Right + LaneW)
            {
                int pl = Mathf.Clamp(Mathf.FloorToInt((w.x - Left) / LaneW), 0, 4 - Mathf.Max(1, draggingPoint.Width));
                if (pl != draggingPoint.Lane) { draggingPoint.Lane = pl; linksDirty = true; }
            }
            if (lUp || !lHeld)
            {
                ANote h = draggingPoint.Head ?? draggingPoint;
                draggingPoint = null;
                linksDirty = true;
                RebuildLinks();
                NormalizeSlide(h);
            }
            return;
        }

        // 위 · 아래 막대, 도구 · 정보 패널, 미니맵 위: 레인 편집 대신 버튼 · 미니맵 처리
        if (HandleHudMouse(mouse)) { hoverLane = -1; return; }

        if (!ld && !rd) return;
        Pos p = PosOf(hoverBeat);

        if (kb.uKey.isPressed)
        {
            // U + 클릭 : 그 박의 변속(지금 편집 중인 층 · 두 층 모두)에 SPEED UP / DOWN 표시 켜고 끄기
            if (!ld) return;
            var hit = speeds.Where(x => x.Same(p) && (x.Layer == 0 || x.Layer == editLayer)).ToList();
            if (hit.Count == 0) hit = speeds.Where(x => x.Same(p)).ToList();
            if (hit.Count == 0) { SetStatus(T("이 박에는 변속이 없음 (F + 클릭으로 먼저 변속)", "No speed change on this beat (F + click first)")); return; }
            bool on = !hit.Any(x => x.Show);
            foreach (var sp in hit) sp.Show = on;
            SetStatus(on ? T("SPEED UP / DOWN 표시 켬", "SPEED UP / DOWN notice ON") : T("SPEED UP / DOWN 표시 끔", "SPEED UP / DOWN notice OFF"));
            return;
        }
        if (kb.gKey.isPressed)
        {
            // G + 클릭 : 난이도 공개 위치 (하나만 — 새로 찍으면 옮겨짐),  G + 우클릭 : 삭제
            if (rd) { if (reveal != null && reveal.Same(p)) { reveal = null; SetStatus(T("난이도 공개 삭제", "LEVEL REVEAL removed")); } }
            else
            {
                reveal = new Pos { M = p.M, N = p.N, D = p.D };
                SongData sd = CoreManager.Instance != null ? CoreManager.Instance.CurrentSongData : null;
                bool hidden = sd != null && DifficultyHelper.IsHidden(sd, levelId);
                SetStatus(hidden ? T("난이도 공개: ? → ", "LEVEL REVEAL: ? → ") + DifficultyHelper.GetRealValue(sd, levelId)
                                 : T("난이도 공개 위치 (이 난이도는 ? 가 아니라 게임에서는 변화 없음)", "LEVEL REVEAL (this level is not \"?\" — no effect in game)"));
            }
            return;
        }
        if (kb.vKey.isPressed || kb.bKey.isPressed)
        {
            bool fold = kb.vKey.isPressed;
            if (rd) { folds.RemoveAll(x => x.Fold == fold && x.Same(p)); SortAll(); }
            else OpenPrompt(fold ? PromptKind.Fold : PromptKind.Unfold, p);
            return;
        }
        if (kb.dKey.isPressed)
        {
            if (rd) { if (hoverBeat > 1e-9) bpms.RemoveAll(x => x.Same(p)); SortAll(); }
            else OpenPrompt(PromptKind.Bpm, p);
            return;
        }
        if (kb.fKey.isPressed)
        {
            // F + 클릭 : 지금 편집 중인 층에만 변속,  SHIFT + F + 클릭 : 두 층 모두 (기존 에디터와 같음)
            bool both = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            int layer = both ? 0 : editLayer;
            if (rd) { speeds.RemoveAll(x => x.Same(p) && (both || x.Layer == layer || x.Layer == 0)); SortAll(); }
            else { promptLayer = layer; OpenPrompt(PromptKind.Speed, p); }
            return;
        }
        if (kb.eKey.isPressed || kb.rKey.isPressed)
        {
            bool stop = kb.eKey.isPressed;
            marks.RemoveAll(x => x.IsStop == stop && x.Same(p));
            if (ld) marks.Add(new AMark { M = p.M, N = p.N, D = p.D, IsStop = stop });
            SortAll(); return;
        }

        if (slideMode) { HandleSlideClick(ld, rd, p); return; }   // 7: 슬라이드 편집

        if (hoverLane < 0) return;
        ANote exist = NoteAt(hoverLane, p, editLayer);
        if (rd)
        {
            if (exist != null)
            {
                // 슬라이드의 점을 지우면 그 슬라이드 전체 (머리 · 중계점 · 끝)
                if (exist.SlideId > 0) { int id = exist.SlideId, ly = exist.Layer; notes.RemoveAll(x => x.SlideId == id && x.Layer == ly); }
                else notes.Remove(exist);
                linksDirty = true;
            }
            return;
        }

        if (exist != null) notes.Remove(exist);
        // 모든 노트(마디선 제외)는 RED 처럼 오른쪽으로 끌어 폭 1~4칸 (클릭만 하면 1칸)
        if (noteType == 4)
        {
            notes.Add(new ANote { M = p.M, N = p.N, D = p.D, Lane = hoverLane, Type = 4, Layer = editLayer });
            linksDirty = true;
            return;
        }
        else if (redMode)
            draggingWide = new ANote { M = p.M, N = p.N, D = p.D, Lane = hoverLane, Type = noteType + 10, Red = true, Width = 1, Layer = editLayer };
        else
        {
            // 드래그 모드: 탭 → 드래그 노트(9), 롱노트 시작 → 드래그 롱노트 시작(10). 롱노트 끝은 3 그대로
            int t = dragMode ? (noteType == 1 ? NoteTypes.Drag : noteType == 2 ? NoteTypes.DragLongHead : noteType) : noteType;
            draggingWide = new ANote { M = p.M, N = p.N, D = p.D, Lane = hoverLane, Type = t, Width = 1, Layer = editLayer };
        }
        notes.Add(draggingWide);
        linksDirty = true;
    }

    // =====================================================================
    //  슬라이드 편집 (7)
    // =====================================================================
    private void ToggleSlideMode()
    {
        slideMode = !slideMode;
        draggingPoint = null;
        if (!slideMode) selSlide = null;
        SetStatus(slideMode ? T("슬라이드 편집: 롱노트를 클릭해 고른 뒤, 머리 ~ 끝 사이를 클릭해 중계점 추가 · 점을 끌어 레인 이동 · C 곡선",
                                "SLIDE EDIT: click a long note, click between its ends to add a point · drag points · C curve")
                            : T("슬라이드 편집 끔", "SLIDE EDIT OFF"));
    }

    private int NewSlideId() { int m = 0; foreach (var n in notes) if (n.SlideId > m) m = n.SlideId; return m + 1; }

    // 롱노트의 점들 (슬라이드면 머리 · 중계점 · 끝, 일반 롱노트면 머리 · 끝)
    private static List<ANote> PointsOf(ANote head)
    {
        if (head == null) return null;
        if (head.SlidePts != null) return head.SlidePts;
        if (head.Tail != null) return new List<ANote> { head, head.Tail };
        return null;
    }

    // 일반 롱노트를 슬라이드로 (머리 · 끝에 같은 번호)
    private void EnsureSlide(ANote head)
    {
        if (head == null || head.Tail == null || head.SlideId > 0) return;
        int id = NewSlideId();
        head.SlideId = id; head.Tail.SlideId = id;
        RebuildLinks();
    }

    // 중계점이 없고 머리 · 끝이 같은 레인 · 폭이면 다시 일반 롱노트로 (파일을 깔끔하게)
    private void NormalizeSlide(ANote head)
    {
        if (head == null || head.SlidePts == null) return;
        var pts = head.SlidePts;
        if (pts.Count != 2) return;
        if (pts[0].Lane != pts[1].Lane || pts[0].Width != pts[1].Width) return;
        foreach (var x in pts) { x.SlideId = 0; x.Curve = SlideCurve.Straight; }
        RebuildLinks();
    }

    // 시각 비율로 그 박의 범위 (레인 단위 left ~ right) — 게임과 같은 계산 (시간 기준 곡선)
    private void SlideAt(List<ANote> pts, double beat, out float l, out float r)
    {
        ANote a = pts[0], b = pts[pts.Count - 1];
        if (beat <= a.Beat) { l = a.Lane; r = a.Lane + a.Width; return; }
        if (beat >= b.Beat) { l = b.Lane; r = b.Lane + b.Width; return; }
        for (int i = 0; i < pts.Count - 1; i++) if (beat <= pts[i + 1].Beat) { a = pts[i]; b = pts[i + 1]; break; }
        double ta = TimeOf(a.Beat), tb = TimeOf(b.Beat), t = TimeOf(beat);
        float u = tb - ta <= 1e-9 ? 1f : (float)((t - ta) / (tb - ta));
        float e = SlideCurve.Ease(a.Curve, u);
        l = Mathf.Lerp(a.Lane, b.Lane, e);
        r = Mathf.Lerp(a.Lane + a.Width, b.Lane + b.Width, e);
    }

    // 마우스 위치의 롱노트 머리 (편집 중인 층) — 점 위 또는 몸통 위
    private ANote LongNoteAt(double beat, float wx, out ANote point)
    {
        point = null;
        float lanePos = (wx - Left) / LaneW;
        foreach (var n in notes)
        {
            if (n.Layer != editLayer || n.Tail == null || !NoteTypes.IsLongHead(n.Type)) continue;
            var pts = PointsOf(n);
            if (pts == null || beat < n.Beat - 1e-6 || beat > n.Tail.Beat + 1e-6) continue;
            foreach (var pt in pts)
                if (Math.Abs(pt.Beat - beat) < 1e-6 && lanePos >= pt.Lane && lanePos <= pt.Lane + Mathf.Max(1, pt.Width)) { point = pt; return n; }
            SlideAt(pts, beat, out float l, out float r);
            if (lanePos >= l - 0.05f && lanePos <= r + 0.05f) return n;
        }
        return null;
    }

    private void HandleSlideClick(bool ld, bool rd, Pos p)
    {
        Vector2 mp = Mouse.current.position.ReadValue();
        float wx = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 10f)).x;
        ANote head = LongNoteAt(hoverBeat, wx, out ANote point);

        if (rd)
        {
            // 중계점 우클릭 = 삭제
            if (point != null && NoteTypes.IsSlideRelay(point.Type))
            {
                ANote h = point.Head;
                notes.Remove(point);
                RebuildLinks();
                NormalizeSlide(h);
                SetStatus(T("중계점 삭제", "point removed"));
            }
            return;
        }
        if (!ld) return;

        if (point != null)
        {
            // 점을 누름: 그 롱노트를 고르고 끌기 시작 (일반 롱노트였으면 슬라이드로)
            selSlide = head;
            EnsureSlide(head);
            draggingPoint = point;
            return;
        }
        if (selSlide != null && selSlide.Tail != null && hoverLane >= 0 && hoverBeat > selSlide.Beat + 1e-6 && hoverBeat < selSlide.Tail.Beat - 1e-6
            && (head == null || head == selSlide))
        {
            // 고른 롱노트의 머리 ~ 끝 사이: 중계점 추가 (폭은 바로 앞 점과 같게) → 바로 끌 수 있음
            EnsureSlide(selSlide);
            var pts = PointsOf(selSlide);
            ANote prev = pts[0];
            foreach (var x in pts) if (x.Beat < hoverBeat - 1e-6) prev = x;
            if (pts.Exists(x => Math.Abs(x.Beat - hoverBeat) < 1e-6)) return;   // 그 박에 이미 점이 있음
            int wdt = Mathf.Max(1, prev.Width);
            var relay = new ANote { M = p.M, N = p.N, D = p.D, Lane = Mathf.Clamp(hoverLane, 0, 4 - wdt), Width = wdt, Type = NoteTypes.SlideRelay,
                                    Red = selSlide.Red, Layer = selSlide.Layer, SlideId = selSlide.SlideId, Curve = prev.Curve };
            notes.Add(relay);
            RebuildLinks();
            draggingPoint = relay;
            SetStatus(T("중계점 추가 — 끌어서 레인 이동", "point added — drag to move"));
            return;
        }
        selSlide = head;                                   // 롱노트 몸통을 누름 = 고름 (빈 곳 = 고르기 취소)
    }

    // C: 마우스가 있는 구간의 곡선 (직선 → IN → OUT)
    private void CycleSlideCurve()
    {
        Vector2 mp = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        float wx = cam != null ? cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 10f)).x : 0f;
        ANote head = LongNoteAt(hoverBeat, wx, out ANote _);
        if (head == null) head = selSlide;
        if (head == null || head.Tail == null) { SetStatus(T("곡선을 바꿀 롱노트 위에서 C", "hover a long note and press C")); return; }
        EnsureSlide(head);
        var pts = PointsOf(head);
        int seg = 0;
        for (int i = 0; i < pts.Count - 1; i++) if (hoverBeat >= pts[i].Beat - 1e-6) seg = i;
        pts[seg].Curve = SlideCurve.Next(pts[seg].Curve);
        selSlide = head;
        SetStatus(T("곡선: ", "CURVE: ") + (pts[seg].Curve == SlideCurve.EaseIn ? T("처음 천천히 (IN)", "ease in (IN)")
                                         : pts[seg].Curve == SlideCurve.EaseOut ? T("끝 천천히 (OUT)", "ease out (OUT)") : T("직선", "straight")));
        RebuildLinks();
        NormalizeSlide(head);
    }

    // 슬라이드 몸통: 가로 조각들로 채우고, 양쪽 테두리는 부드러운 선
    private void DrawSlideBody(ANote head, float alpha)
    {
        var pts = head.SlidePts;
        Color lc = SimpleGameSkin.LaneColor(head.Lane);
        if (head.Red) lc = new Color(1f, 0.38f, 0.45f);
        bool sel = slideMode && head == selSlide;
        int order = NoteLayer.BodyOrder(head.Layer);
        float inset = 0.025f;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            ANote a = pts[i], b = pts[i + 1];
            bool moves = a.Lane != b.Lane || a.Width != b.Width;
            int steps = !moves ? 1 : a.Curve == SlideCurve.Straight ? 10 : 18;
            float pl = 0f, pr = 0f, py = 0f;
            for (int k = 0; k <= steps; k++)
            {
                double beat = a.Beat + (b.Beat - a.Beat) * k / steps;
                SlideAt(pts, beat, out float l, out float r);
                float y = Y(beat);
                float xl = Left + l * LaneW + inset, xr = Left + r * LaneW - inset;
                if (k > 0)
                {
                    float midL = (pl + xl) * 0.5f, midR = (pr + xr) * 0.5f;
                    Box((midL + midR) * 0.5f, (py + y) * 0.5f, midR - midL, Mathf.Abs(y - py) + 0.01f, new Color(lc.r, lc.g, lc.b, (sel ? 0.42f : 0.30f) * alpha), order);
                    Color ec = sel ? new Color(1f, 1f, 1f, 0.95f * alpha) : new Color(lc.r, lc.g, lc.b, 0.85f * alpha);
                    SlideLine(pl, py, xl, y, 0.035f, ec, order + 1);
                    SlideLine(pr, py, xr, y, 0.035f, ec, order + 1);
                }
                pl = xl; pr = xr; py = y;
            }
        }
    }

    // 선 하나 (돌린 가는 막대)
    private void SlideLine(float x0, float y0, float x1, float y1, float thick, Color c, int order)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return;
        SpriteRenderer sr = pool.Get(NoteArt.WhiteSprite, order);
        sr.drawMode = SpriteDrawMode.Simple;
        sr.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
        sr.transform.localScale = new Vector3(len + thick * 0.5f, thick, 1f);
        sr.color = c;
    }

    // 슬라이드 편집 중: 고른 롱노트의 점 손잡이 · 고른 일반 롱노트 강조 · 중계점 놓일 자리 미리보기
    private void DrawSlideEditOverlay()
    {
        if (selSlide != null && !notes.Contains(selSlide)) selSlide = null;
        if (selSlide != null)
        {
            var pts = PointsOf(selSlide);
            if (pts != null)
            {
                if (selSlide.SlidePts == null)
                {
                    // 일반 롱노트를 고름: 몸통 위에 흰 테두리
                    float x0 = Left + selSlide.Lane * LaneW + 0.025f, x1 = Left + (selSlide.Lane + Mathf.Max(1, selSlide.Width)) * LaneW - 0.025f;
                    float y0 = Y(selSlide.Beat), y1 = Y(selSlide.Tail.Beat);
                    SlideLine(x0, y0, x0, y1, 0.035f, new Color(1f, 1f, 1f, 0.9f), 40);
                    SlideLine(x1, y0, x1, y1, 0.035f, new Color(1f, 1f, 1f, 0.9f), 40);
                }
                foreach (var pt in pts)
                {
                    float cx = Left + (pt.Lane + Mathf.Max(1, pt.Width) * 0.5f) * LaneW;
                    SpriteRenderer g = pool.Get(NoteArt.DragGemSprite, 45);
                    g.drawMode = SpriteDrawMode.Simple;
                    Vector2 gsz = NoteArt.DragGemSprite.bounds.size;
                    float s = pt == draggingPoint ? 0.36f : 0.28f;
                    g.transform.localScale = new Vector3(s / gsz.x, s / gsz.y, 1f);
                    g.transform.position = new Vector3(cx, Y(pt.Beat), 0f);
                    g.color = pt == draggingPoint ? Color.white : SlideUiColor;
                }
                // 중계점 놓일 자리
                if (draggingPoint == null && hoverLane >= 0 && hoverBeat > selSlide.Beat + 1e-6 && hoverBeat < selSlide.Tail.Beat - 1e-6)
                {
                    ANote prev = pts[0];
                    foreach (var x in pts) if (x.Beat < hoverBeat - 1e-6) prev = x;
                    int wdt = Mathf.Max(1, prev.Width);
                    int ln = Mathf.Clamp(hoverLane, 0, 4 - wdt);
                    Note(Left + (ln + wdt * 0.5f) * LaneW, Y(hoverBeat), (wdt - 1) * 0.8f + 0.8f * 0.95f, SimpleGameSkin.TailHeight * 2f, new Color(SlideUiColor.r, SlideUiColor.g, SlideUiColor.b, 0.45f), 15);
                }
            }
        }
    }

    // 슬라이드 편집 중: 고른 롱노트의 구간마다 곡선 이름 (HUD 글자)
    private void DrawSlideLabels()
    {
        if (!slideMode || selSlide == null || selSlide.SlidePts == null) return;
        var pts = selSlide.SlidePts;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            double mid = (pts[i].Beat + pts[i + 1].Beat) * 0.5;
            SlideAt(pts, mid, out float l, out float r);
            Color c = pts[i].Curve == SlideCurve.Straight ? new Color(1f, 1f, 1f, 0.5f) : SlideUiColor;
            LabelCenter(Left + r * LaneW + 0.35f, Y(mid), SlideCurve.Name(pts[i].Curve), c);
        }
    }

    private ANote NoteAt(int lane, Pos p, int layer)
    {
        return notes.FirstOrDefault(n => n.Layer == layer && n.Lane <= lane && n.Lane + n.Width - 1 >= lane && n.Same(p));
    }

    // 박 → (마디, 분자, 분모)  (지금 박자 단위 기준, 약분)
    private Pos PosOf(double beat)
    {
        long den = 4L * SnapDivs[snapIndex];
        long ticks = (long)Math.Round(beat / 4.0 * den);
        long m = (long)Math.Floor(ticks / (double)den);
        long n = ticks - m * den;
        long g = Gcd(n, den);
        return new Pos { M = m + 1, N = n / g, D = den / g };
    }
    private static long Gcd(long a, long b) { while (b != 0) { long t = b; b = a % b; a = t; } return a == 0 ? 1 : Math.Abs(a); }

    // ----- 숫자 입력창 -----
    private void OpenPrompt(PromptKind kind, Pos p)
    {
        prompt = kind; promptPos = p; promptBuffer = "";
        promptPanel.SetActive(true);
        switch (kind)
        {
            case PromptKind.Bpm: promptTitle.text = "BPM"; promptHint.text = T("이 박자부터 쓸 BPM 을 입력하세요", "New BPM at this beat"); break;
            case PromptKind.Speed:
                promptTitle.text = T("변속 (SCROLL SPEED)", "SCROLL SPEED");
                promptHint.text = promptLayer == 0 ? T("이 박자부터의 스크롤 속도 — 두 층 모두 (SHIFT + F)", "Scroll speed from this beat — both layers (SHIFT + F)")
                                : T($"이 박자부터의 스크롤 속도 — {promptLayer}층만 (두 층 모두: SHIFT + F)", $"Scroll speed from this beat — layer {promptLayer} only (both: SHIFT + F)");
                break;
            case PromptKind.Fold: promptTitle.text = T("레인 사라짐", "LANES DISAPPEAR"); promptHint.text = T("선이 위로 빨려 들어가 다 사라지는 데 걸리는 시간(초)  예: 0.5", "Seconds until the lane lines are fully gone (e.g. 0.5)"); break;
            case PromptKind.Unfold: promptTitle.text = T("레인 드러남", "LANES REAPPEAR"); promptHint.text = T("선이 아래에서 다 올라오는 데 걸리는 시간(초)  예: 0.5", "Seconds until the lane lines are fully back (e.g. 0.5)"); break;
            case PromptKind.Offset: promptTitle.text = T("음악 오프셋 (ms)", "MUSIC OFFSET (ms)"); promptHint.text = T("지금 ", "Now ") + ChartOffset.Display(audioStart) + T("  ·  osu! Offset 과 같은 값", "  ·  same value as osu! Offset"); break;
        }
    }

    private void UpdatePrompt(Keyboard kb)
    {
        if (kb.escapeKey.wasPressedThisFrame) { ClosePrompt(); return; }
        if (kb.backspaceKey.wasPressedThisFrame && promptBuffer.Length > 0) promptBuffer = promptBuffer.Substring(0, promptBuffer.Length - 1);
        Key[] digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        Key[] pads = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };
        for (int i = 0; i < 10; i++) if (kb[digits[i]].wasPressedThisFrame || kb[pads[i]].wasPressedThisFrame) promptBuffer += (char)('0' + i);
        if (kb.periodKey.wasPressedThisFrame || kb.numpadPeriodKey.wasPressedThisFrame) promptBuffer += ".";
        if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) promptBuffer += "-";

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            double v;
            if (double.TryParse(promptBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) ApplyPrompt(v);
            ClosePrompt();
        }
    }

    private void ApplyPrompt(double v)
    {
        Pos p = promptPos;
        switch (prompt)
        {
            case PromptKind.Bpm:
                if (v <= 0) return;
                if (p.Beat <= 1e-9) { initialBpm = v; break; }
                var old = bpms.FirstOrDefault(x => x.Same(p));
                double anchor = old != null ? old.AnchorMs : double.NaN;     // TIMING 이면 고정 시각 유지
                bpms.RemoveAll(x => x.Same(p));
                bpms.Add(new ABpm { M = p.M, N = p.N, D = p.D, Bpm = v, AnchorMs = anchor });
                break;
            case PromptKind.Speed:
            {
                // 두 층 모두(0)를 넣으면 그 박의 층별 변속은 지움, 층별로 넣으면 그 층 것만 바꿈
                int ly = promptLayer;
                var oldSp = speeds.FirstOrDefault(x => x.Same(p) && x.Layer == ly);
                bool show = oldSp != null && oldSp.Show;                         // SPEED UP / DOWN 표시는 유지
                speeds.RemoveAll(x => x.Same(p) && (ly == 0 || x.Layer == ly));
                speeds.Add(new ASpeed { M = p.M, N = p.N, D = p.D, Layer = ly, Value = v, Show = show });
                break;
            }
            case PromptKind.Fold:
            case PromptKind.Unfold:
                if (v < 0) return;
                bool fold = prompt == PromptKind.Fold;
                folds.RemoveAll(x => x.Fold == fold && x.Same(p));
                folds.Add(new AFold { M = p.M, N = p.N, D = p.D, Fold = fold, Duration = v });
                break;
            case PromptKind.Offset:
                audioStart = ChartOffset.ToAudioStart(v);
                SetStatus(T("음악 오프셋 ", "MUSICOFFSET ") + ChartOffset.Display(audioStart));
                break;
        }
        SortAll();
    }

    private void ClosePrompt() { prompt = PromptKind.None; promptPanel.SetActive(false); }

    // =====================================================================
    //  재생 (게임과 같은 예약 재생 + dspTime 시계)
    // =====================================================================
    private double AudioStartEff { get { return audioStart - SettingsStore.MusicLead; } }   // 설정 AUDIO OFFSET 반영

    private void TogglePlay()
    {
        playing = !playing;
        if (playing)
        {
            // [버그 수정] 첫 탭음이 안 들리던 문제
            //  예전: 검사 시작점 = 재생 시작 시각, 검사 조건 '시작점 < 노트' → 시작 위치(커서)에 있는 노트가 늘 빠졌다
            //  지금: 시작 위치의 노트까지 포함. 또 음악 예약에 걸리는 약 0.1초만큼 시계를 앞당겨 시작해서
            //        시작 위치의 음악 · 탭음이 빠짐없이 같이 나온다 (그동안 커서는 시작 위치에 멈춰 있음)
            playFromTime = TimeOf(cursorBeat);
            playTime = ScheduleMusic(playFromTime, true);
            lastHitCheck = Math.Min(playTime, playFromTime) - 1e-4;
        }
        else
        {
            bgm.Stop(); scheduled = false; waitingAudio = false;
            cursorBeat = Math.Max(0.0, Math.Round(cursorBeat / Step) * Step);
        }
    }

    // from 부터 음악이 이어지도록 예약 재생. 돌려주는 값 = 지금 이 순간의 차트 시각
    //  preroll = true  : from 의 음악부터 빠짐없이 나오도록 시계를 예약 시간만큼 앞당김 (재생 시작)
    //  preroll = false : 지금 = from, 예약 시간만큼의 음악은 건너뜀 (재생 도중 음악이 늦게 올라왔을 때)
    private double ScheduleMusic(double from, bool preroll)
    {
        scheduled = false;
        AudioClip clip = bgm.clip;
        if (clip == null) return from;
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            if (clip.loadState != AudioDataLoadState.Loading) clip.LoadAudioData();
            waitingAudio = true;      // 다 올라오면 그 시점부터 (앞부분이 소리 없이 지나가지 않게)
            return from;
        }
        waitingAudio = false;
        int len, num;
        AudioSettings.GetDSPBufferSize(out len, out num);
        int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        double lead = Math.Max(0.1, len * (double)Math.Max(1, num) / sr * 1.5 + 0.02);
        double start = AudioStartEff;
        double clockNow, musicAt;                 // musicAt: 음악이 실제로 나오기 시작하는 차트 시각
        if (preroll) { musicAt = Math.Max(start, from); clockNow = Math.Min(from, musicAt - lead); }
        else { clockNow = from; musicAt = Math.Max(start, from + lead); }
        double pos = musicAt - start;
        if (pos >= clip.length) return from;
        double dsp = AudioSettings.dspTime + (musicAt - clockNow);
        bgm.Stop();
        bgm.volume = musicVolume;
        bgm.timeSamples = Mathf.Clamp((int)Math.Round(pos * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
        bgm.PlayScheduled(dsp);
        dspAudioZero = dsp - pos;
        scheduled = true;
        return clockNow;
    }

    private double lastHitCheck;
    private double playFromTime;

    private void UpdatePlayback(Keyboard kb)
    {
        if (!playing) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (scheduled && bgm.clip != null)
        {
            double target = AudioStartEff + (AudioSettings.dspTime - dspAudioZero);
            double diff = target - playTime;
            if (Math.Abs(diff) > 0.25) playTime = target;
            else playTime += dt * Mathf.Clamp(1f + (float)(diff * 2.0), 0.8f, 1.2f);
            if (playTime > AudioStartEff + bgm.clip.length + 0.05) { TogglePlay(); return; }
        }
        else
        {
            playTime += dt;
            if (waitingAudio && bgm.clip != null && bgm.clip.loadState == AudioDataLoadState.Loaded) ScheduleMusic(playTime, false);
        }
        cursorBeat = BeatOf(Math.Max(playTime, playFromTime));     // 예약 대기 동안은 시작 위치에 멈춤

        // 판정선을 지난 노트마다 키음
        double now = playTime + 0.035;
        bool hit = false;
        foreach (var n in notes)
        {
            if (n.Type == 4) continue;
            if (!longHitSound && (n.Type == 3 || n.Type == 13 || NoteTypes.IsSlideRelay(n.Type))) continue;   // 롱노트 끝 · 슬라이드 중계점 탭음 끔 — 시작은 그대로
            double t = TimeOf(n.Beat);
            if (t < playFromTime - 1e-6) continue;                               // 시작 위치보다 앞 노트는 소리 안 냄
            if (t > lastHitCheck && t <= now) { hit = true; break; }
        }
        if (hit && hitClip != null && hitVolume > 0f) sfx.PlayOneShot(hitClip, hitVolume);
        lastHitCheck = now;
    }

    private void Exit()
    {
        if (bgm != null) bgm.Stop();
        if (CoreManager.Instance != null) CoreManager.Instance.LoadSongSelectScene();
    }

    private void SetStatus(string s) { status = s; statusTimer = 4f; }

    // =====================================================================
    //  그리기 (SIMPLE 스킨 모양)
    // =====================================================================
    private float Y(double beat) { return HitY + (float)(beat - cursorBeat) * beatSpacing; }
    private float LaneX(int lane) { return Left + (lane + 0.5f) * LaneW; }

    private void RebuildLinks()
    {
        foreach (var n in notes) { n.Tail = null; n.Head = null; n.SlidePts = null; }
        // 슬라이드: 같은 번호 · 같은 층의 점들을 박 순서로 (처음 = 머리, 마지막 = 끝, 가운데 = 중계점)
        foreach (var g in notes.Where(x => x.SlideId > 0).GroupBy(x => x.Layer * 100000 + x.SlideId))
        {
            var pts = g.OrderBy(x => x.Beat).ToList();
            if (pts.Count < 2) continue;
            ANote head = pts[0];
            head.SlidePts = pts;
            head.Tail = pts[pts.Count - 1];
            for (int i = 1; i < pts.Count; i++) pts[i].Head = head;
        }
        for (int lane = 0; lane < 4; lane++)
            for (int layer = 1; layer <= 2; layer++)
            {
                ANote head = null;
                foreach (var n in notes.Where(x => x.Lane == lane && x.Layer == layer && x.SlideId == 0).OrderBy(x => x.Beat))
                {
                    if (NoteTypes.IsLongHead(n.Type)) head = n;
                    else if ((n.Type == 3 || n.Type == 13) && head != null) { if (head.Red == n.Red) { head.Tail = n; n.Head = head; } head = null; }
                }
            }
        linksDirty = false;
    }

    private void Draw()
    {
        pool.Begin();
        float top = 5.5f, bottom = -5.5f;
        double b0 = cursorBeat + (bottom - HitY) / beatSpacing, b1 = cursorBeat + (top - HitY) / beatSpacing;

        // 화면 전체 어두운 바탕 (뒤에 있는 기존 에디터 화면을 가림) + 레인 바탕
        float halfW = cam != null ? cam.orthographicSize * cam.aspect + 1f : 20f;
        Box(0f, 0f, halfW * 2f, 14f, new Color(0.045f, 0.052f, 0.075f, 1f), -60);
        DrawFoldBar(b0, b1);     // 오른쪽 여백: 레인이 사라진 정도 (사라짐 · 드러남 미리보기)
        // 레인 밑 남색 덮개: 레인이 사라진 상태에서도 그대로 (게임과 같음) → 옅어지는 대상보다 먼저 그림
        Box(0f, 0f, Right - Left + 0.06f, 12f, new Color(0.018f, 0.022f, 0.04f, 1f), -40);
        // 가운데 두 레인을 아주 살짝 밝게 (레인 구분이 한눈에) + 판정선 근처 바닥 빛
        Box(LaneX(1), 0f, LaneW, 12f, new Color(1f, 1f, 1f, 0.016f), -39);
        Box(LaneX(2), 0f, LaneW, 12f, new Color(1f, 1f, 1f, 0.016f), -39);
        Box(0f, HitY + 0.45f, Right - Left, 0.9f, new Color(0.36f, 0.88f, 0.90f, 0.035f), -38);

        DrawGapZones(b0, b1);

        // 그리드 (가로 박자선)
        DrawGrid(b0, b1);
        // 마우스가 있는 레인 · 박: 세로 칸을 옅게 + 가로 줄 (어디에 놓일지 한눈에)
        if (!playing && prompt == PromptKind.None && hoverLane >= 0)
        {
            Box(LaneX(hoverLane), 0f, LaneW, 12f, new Color(1f, 1f, 1f, 0.035f), -37);
            Box(0f, Y(hoverBeat), Right - Left, 0.022f, new Color(0.36f, 0.88f, 0.90f, 0.55f), -26);
        }
        // 레인 선 (양쪽 벽 · 구분선): 레인 사라짐 / 드러남 때 위로 빨려 들어가고 아래에서 올라옴 (게임과 같음)
        pool.Keep = true;
        DrawLaneLines();
        pool.Keep = false;

        // 노트
        foreach (var n in notes)
        {
            double nb = n.Beat;
            double tailB = n.Tail != null ? n.Tail.Beat : nb;
            if (tailB < b0 - 1 || nb > b1 + 1) continue;
            DrawNote(n);
        }

        // 판정선 (뒤에 은은한 빛)
        Color jc = playing ? new Color(0.4f, 1f, 0.6f, 1f) : new Color(1f, 1f, 1f, 1f);
        Box(0f, HitY, Right - Left, 0.22f, new Color(jc.r, jc.g, jc.b, 0.08f), 19);
        Box(0f, HitY, Right - Left, 0.05f, new Color(jc.r, jc.g, jc.b, 0.95f), 20);

        // 마우스 위치 미리보기
        // 레인 사라짐 / 드러남 미리보기 (재생 중): 레인 선만 위의 DrawLaneLines 가 위로 빨려 들어가고 / 올라옴
        //  (게임과 같음 — 푸른 바탕 · 판정선 · 노트는 사라짐 상태에서도 그대로 보임)
        if (slideMode && !playing && prompt == PromptKind.None) DrawSlideEditOverlay();
        else if (!playing && prompt == PromptKind.None && hoverLane >= 0 && draggingWide == null)
        {
            Color c = redMode ? new Color(1f, 0.38f, 0.45f) : (dragMode && noteType <= 2 ? NoteArt.DragColor : SimpleGameSkin.LaneColor(hoverLane));
            float ph = SimpleGameSkin.NoteHeight * (dragMode && noteType <= 2 ? NoteArt.DragHeightRatio : 1f);
            Note(LaneX(hoverLane), Y(hoverBeat), 0.8f * 0.95f, ph, new Color(c.r, c.g, c.b, 0.35f), 15);
        }
        DrawEventTicks(b0, b1);   // 레인 오른쪽 벽 바깥의 이벤트 눈금 (BPM · 변속 · STOP 등, 색은 아이콘과 같음)
        pool.End();
    }

    // ----- 레인 사라짐 미리보기 -----
    // 게임(GameManager.LaneFoldAt)과 같은 계산: 1 = 다 보임, 0 = 다 사라짐
    private float FoldAt(double t)
    {
        AFold last = null;
        foreach (var f in folds) { if (TimeOf(f.Beat) <= t + 1e-9) last = f; else break; }
        if (last == null) return 1f;
        double d = last.Duration <= 1e-6 ? 1.0 : (t - TimeOf(last.Beat)) / last.Duration;
        float e = Mathf.Clamp01((float)d);
        e = e * e * (3f - 2f * e);
        return last.Fold ? 1f - e : e;
    }

    // 레인 선 (양쪽 흰 벽 + 구분선 3개)
    //  재생 중 레인 사라짐: 아래 끝이 점점 빨라지며 위로 빨려 올라감 / 드러남: 아래에서 위로 쭉 올라옴
    private void DrawLaneLines()
    {
        float lo = -6f, hi = 6f;
        if (playing && folds.Count > 0)
        {
            bool hiding;
            float p = FoldProgress(playTime, out hiding);
            if (hiding) lo = Mathf.Lerp(-6f, 6f, p * p * p);
            else hi = Mathf.Lerp(-6f, 6f, 1f - Mathf.Pow(1f - p, 3f));
        }
        float len = hi - lo;
        if (len <= 0.001f) return;
        float cy = (lo + hi) * 0.5f;
        for (int i = 1; i < 4; i++) Box(Left + i * LaneW, cy, 0.02f, len, new Color(1f, 1f, 1f, 0.26f), -28);
        Box(Left, cy, 0.045f, len, new Color(1f, 1f, 1f, 0.9f), -27);
        Box(Right, cy, 0.045f, len, new Color(1f, 1f, 1f, 0.9f), -27);
        // 벽 안쪽 은은한 빛
        Box(Left + 0.06f, cy, 0.08f, len, new Color(0.75f, 0.9f, 1f, 0.05f), -27);
        Box(Right - 0.06f, cy, 0.08f, len, new Color(0.75f, 0.9f, 1f, 0.05f), -27);
    }

    // 게임(GameManager.LaneHideProgress)과 같은 계산: 마지막 이벤트의 진행도 0 ~ 1, hiding = 사라지는 중
    private float FoldProgress(double t, out bool hiding)
    {
        AFold last = null;
        foreach (var f in folds) { if (TimeOf(f.Beat) <= t + 1e-9) last = f; else break; }
        hiding = last != null && last.Fold;
        if (last == null) return 1f;
        double d = last.Duration <= 1e-6 ? 1.0 : (t - TimeOf(last.Beat)) / last.Duration;
        return Mathf.Clamp01((float)d);
    }

    // 레인 오른쪽 여백의 세로 막대: 진할수록 레인이 많이 사라진 구간 (사라지는 · 드러나는 중간 과정도 보임)
    private void DrawFoldBar(double b0, double b1)
    {
        if (folds.Count == 0) return;
        const int Pieces = 90;
        float x = Right + 0.09f, w = 0.08f;          // 오른쪽 벽과 이벤트 글자 사이
        double db = (b1 - b0) / Pieces;
        for (int i = 0; i < Pieces; i++)
        {
            double bm = b0 + (i + 0.5) * db;
            if (bm < 0) continue;
            float folded = 1f - FoldAt(TimeOf(bm));
            if (folded < 0.01f) continue;
            float y0 = Y(b0 + i * db), y1 = Y(b0 + (i + 1) * db);
            Box(x, (y0 + y1) * 0.5f, w, y1 - y0 + 0.002f, new Color(1f, 0.6f, 0.3f, 0.15f + 0.75f * folded), -30);
        }
    }

    private void DrawGapZones(double b0, double b1)
    {
        for (int i = 1; i < segs.Count; i++)
        {
            Seg p = segs[i - 1];
            double realEnd = p.Beat + (segs[i].Time - p.Time) * p.Bpm / 60.0;
            if (realEnd >= segs[i].Beat - 1e-4 || segs[i].Beat < b0 || realEnd > b1) continue;
            float y0 = Y(realEnd), y1 = Y(segs[i].Beat);
            Box(0f, (y0 + y1) * 0.5f, Right - Left, y1 - y0, new Color(0.6f, 0.08f, 0.08f, 0.35f), -33);
        }
    }

    private void DrawGrid(double b0, double b1)
    {
        double step = Step;
        double start = Math.Floor(Math.Max(-8.0, b0) / step) * step;
        int guard = 0;
        for (double b = start; b <= b1 && guard < 600; b += step, guard++)
        {
            double r = b / 4.0;
            bool measure = Math.Abs(r - Math.Round(r)) < 1e-6;
            bool beat = Math.Abs(b - Math.Round(b)) < 1e-6;
            Color c = measure ? new Color(1f, 1f, 1f, 0.55f) : beat ? new Color(1f, 1f, 1f, 0.22f) : new Color(1f, 1f, 1f, 0.08f);
            Box(0f, Y(b), Right - Left, measure ? 0.03f : 0.015f, c, -29);
        }
    }

    private void DrawNote(ANote n)
    {
        float alpha = (playing || n.Layer == editLayer) ? 1f : 0.3f;   // 재생 중에는 두 층 모두 또렷하게 (편집 중에만 다른 층을 흐리게)
        float y = Y(n.Beat);
        if (n.Type == 4)
        {
            Box(0f, y, Right - Left, 0.035f, new Color(1f, 0.85f, 0.4f, 0.7f * alpha), 1);
            return;
        }
        int order = NoteLayer.HeadOrder(n.Layer);
        int nw = Mathf.Max(1, n.Width);            // RED · 넓은 일반 노트 모두 폭만큼
        float wGame = (nw - 1) * 0.8f + 0.8f * 0.95f;
        float cx = Left + (n.Lane + nw * 0.5f) * LaneW;
        bool relay = NoteTypes.IsSlideRelay(n.Type);
        bool tail = n.Type == 3 || n.Type == 13 || relay;      // 슬라이드 중계점도 끝처럼 얇은 막대

        // 슬라이드 몸통 (경로를 따라 휨)
        if (n.SlidePts != null) DrawSlideBody(n, alpha);
        // 롱노트 몸통 (머리 → 꼬리)
        else if (n.Tail != null)
        {
            float y1 = Y(n.Tail.Beat);
            float len = Mathf.Max(0.001f, y1 - y);
            if (n.Red)
            {
                SpriteRenderer body = pool.Get(redBodySprite, NoteLayer.BodyOrder(n.Layer));
                body.drawMode = SpriteDrawMode.Tiled;
                body.transform.localScale = new Vector3(K, K, 1f);
                body.size = new Vector2(wGame, len / K);
                body.transform.position = new Vector3(cx, y + len * 0.5f, 0f);
                body.color = new Color(1f, 1f, 1f, alpha);
            }
            else
            {
                // 게임과 같은 롱노트 몸통 (양옆이 빛나는 반투명 띠)
                Color lc = NoteTypes.IsDrag(n.Type) ? SimpleGameSkin.LaneColor(n.Lane) : NoteColorOf(n);   // 드래그로 시작해도 몸통은 일반 롱노트 색
                SpriteRenderer body = pool.Get(NoteArt.LongBodySprite, NoteLayer.BodyOrder(n.Layer));
                body.drawMode = SpriteDrawMode.Sliced;      // 가운데 하이라이트가 항상 정가운데
                body.transform.localScale = new Vector3(K, K, 1f);
                body.size = new Vector2(wGame, len / K);
                body.transform.position = new Vector3(cx, y + len * 0.5f, 0f);
                body.color = new Color(lc.r, lc.g, lc.b, alpha);
            }
        }

        if (n.Red)
        {
            float h = tail ? SimpleGameSkin.RedTailHeight : SimpleGameSkin.RedNoteHeight;
            float pad = SimpleGameSkin.RedGlowPad * 2f;
            SpriteRenderer sr = pool.Get(redNoteSprite, order);
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.transform.localScale = new Vector3(K, K, 1f);
            sr.size = new Vector2(wGame + pad, h + pad);
            sr.transform.position = new Vector3(cx, y, 0f);
            sr.color = new Color(1f, 1f, 1f, (tail ? 0.9f : 1f) * alpha);
            return;
        }

        Color c = NoteColorOf(n);
        if (relay) c = Color.Lerp(c, Color.white, 0.35f);   // 중계점은 조금 밝게
        if (tail)
        {
            // 게임과 같은 롱노트 끝 (얇게 빛나는 막대)
            float tpad = NoteArt.TailGlowPad * 2f;
            SpriteRenderer tc = pool.Get(NoteArt.TailCapSprite, order);
            tc.drawMode = SpriteDrawMode.Sliced;
            tc.transform.localScale = new Vector3(K, K, 1f);
            tc.size = new Vector2(wGame + tpad, Mathf.Max(SimpleGameSkin.TailHeight, 0.075f) + tpad);
            tc.transform.position = new Vector3(cx, y, 0f);
            tc.color = new Color(c.r, c.g, c.b, 0.95f * alpha);
        }
        else
        {
            // 게임과 같은 입체 앞면 + 바깥 빛 (빛 여백만큼 크게)
            float pad = NoteArt.NoteGlowPad * 2f;
            SpriteRenderer sr = pool.Get(NoteArt.NoteFaceSprite, order);
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.transform.localScale = new Vector3(K, K, 1f);
            float nh = SimpleGameSkin.NoteHeight * (NoteTypes.IsDrag(n.Type) ? NoteArt.DragHeightRatio : 1f);   // 드래그: 얇게
            sr.size = new Vector2(wGame + pad, nh + pad);
            sr.transform.position = new Vector3(cx, y, 0f);
            sr.color = new Color(c.r, c.g, c.b, alpha);
        }

        if (NoteTypes.IsDrag(n.Type))
        {
            // 게임과 같은 드래그 노트 장식: 위쪽 혜성 꼬리빛 + 가운데 이중 마름모 보석
            SpriteRenderer tr = pool.Get(NoteArt.DragTrailSprite, order - 1);
            tr.drawMode = SpriteDrawMode.Simple;
            Vector2 tsz = NoteArt.DragTrailSprite.bounds.size;
            tr.transform.localScale = new Vector3(wGame * 0.7f * K / tsz.x, 0.62f * K / tsz.y, 1f);
            tr.transform.position = new Vector3(cx, y, 0f);
            tr.color = new Color(c.r, c.g, c.b, 0.36f * alpha);

            SpriteRenderer gm2 = pool.Get(NoteArt.DragGemSprite, order + 1);
            gm2.drawMode = SpriteDrawMode.Simple;
            Vector2 gsz = NoteArt.DragGemSprite.bounds.size;
            gm2.transform.localScale = new Vector3(0.25f * K / gsz.x, 0.25f * K / gsz.y, 1f);
            gm2.transform.position = new Vector3(cx, y, 0f);
            Color gc = Color.Lerp(c, Color.white, 0.6f);
            gm2.color = new Color(gc.r, gc.g, gc.b, alpha);
        }
    }

    // 노트 색 (게임과 같음): 드래그 연한 노랑 · 그 밖은 레인 색
    private static Color NoteColorOf(ANote n)
    {
        if (n.Head != null && n.Head.SlidePts != null) return SimpleGameSkin.LaneColor(n.Head.Lane);   // 슬라이드의 중계점 · 끝 = 머리(몸통) 색
        if (NoteTypes.IsDrag(n.Type)) return NoteArt.DragColor;   // 드래그 노트 · 드래그 롱노트의 머리만 (끝은 일반 롱노트 끝)
        return SimpleGameSkin.LaneColor(n.Lane);
    }

    // 둥근 단색 노트 (게임 크기 w, h 를 에디터 배율로)
    private void Note(float x, float y, float wGame, float hGame, Color c, int order)
    {
        SpriteRenderer sr = pool.Get(NoteArt.RoundedNoteSprite, order);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.transform.localScale = new Vector3(K, K, 1f);
        sr.size = new Vector2(wGame, hGame);
        sr.transform.position = new Vector3(x, y, 0f);
        sr.color = c;
    }

    private void Box(float x, float y, float w, float h, Color c, int order)
    {
        SpriteRenderer sr = pool.Get(NoteArt.WhiteSprite, order);
        sr.drawMode = SpriteDrawMode.Simple;
        sr.transform.localScale = new Vector3(w, h, 1f);
        sr.transform.position = new Vector3(x, y, 0f);
        sr.color = c;
    }

    // =====================================================================
    //  HUD (위 막대 · 도구 · 정보 · 미니맵 · 도움말) → AreaChartEditorHud.cs
    // =====================================================================

    // =====================================================================
    //  SpriteRenderer 재사용 풀 (매 프레임 필요한 만큼만 켬)
    // =====================================================================
    private class SpritePool
    {
        // 기존 에디터 씬의 캔버스(배경 등)보다 항상 위에 그려지도록 정렬 순서를 크게 올림
        //  [버그 수정] 음수 순서로 그린 레인 바탕 · 그리드 · 벽이 씬의 배경 캔버스에 가려 하나도 안 보였다
        public const int OrderBase = 300;
        private readonly Transform root;
        private readonly List<SpriteRenderer> items = new List<SpriteRenderer>();
        private int used;
        private readonly List<bool> keep = new List<bool>();
        public bool Keep;            // true 로 둔 동안 그린 것은 레인 사라짐(옅어짐)의 영향을 받지 않음 (영역 · 영역 노트 · 레인 선)
        public SpritePool(Transform r) { root = r; }
        public void Begin() { used = 0; Keep = false; }
        public SpriteRenderer Get(Sprite sprite, int order)
        {
            SpriteRenderer sr;
            if (used < items.Count) sr = items[used];
            else
            {
                GameObject go = new GameObject("S");
                go.transform.SetParent(root, false);
                sr = go.AddComponent<SpriteRenderer>();
                items.Add(sr);
                keep.Add(false);
            }
            keep[used] = Keep;
            used++;
            sr.enabled = true;
            if (sr.sprite != sprite) sr.sprite = sprite;
            sr.sortingOrder = order + OrderBase;
            sr.flipX = sr.flipY = false;
            sr.transform.rotation = Quaternion.identity;   // 슬라이드 테두리 선만 돌려서 그림
            return sr;
        }
        public void End() { for (int i = used; i < items.Count; i++) if (items[i].enabled) items[i].enabled = false; }
        public int Used { get { return used; } }

        // [from, to) 번째로 그린 것들을 투명하게 (레인 사라짐 미리보기, k = 1 이면 가로로 모으지 않음)
        public void ApplyFold(int from, int to, float k, float alpha)
        {
            for (int i = from; i < to && i < items.Count; i++)
            {
                if (keep[i]) continue;
                SpriteRenderer sr = items[i];
                Transform t = sr.transform;
                Vector3 p = t.position; p.x *= k; t.position = p;
                Vector3 sc = t.localScale; sc.x *= k; t.localScale = sc;
                Color c = sr.color; c.a *= alpha; sr.color = c;
            }
        }
    }

    void OnDestroy()
    {
        if (bgm != null) bgm.Stop();
    }
}