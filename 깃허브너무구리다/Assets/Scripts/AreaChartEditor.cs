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
// 영역(Area) 채보 전용 에디터 — 기존 채보 에디터와 별개
//
//  songs.json 에 "Area": "deployed(INSANE)" 가 있는 곡 · 난이도를 에디터로 열면
//  ChartEditorManager 대신 이 에디터가 열린다 (같은 3_Editor 씬, 화면 · 조작 · 그리기 모두 따로).
//   - 노트는 SIMPLE 스킨 모양(둥근 단색 노트 · 빛나는 RED 노트 · 반투명 롱노트 몸통)으로 통일
//   - 영역(AREA), 레인 접기/펼치기(FOLD/UNFOLD), 화살표 노트(7 >>>, 8 <<<) 편집
//   - 일반 노트 · 롱노트 · RED 노트 · 층(1층/2층) · 마디선 · BPM · 변속 · STOP/START · 오프셋도 편집 가능
//   - 저장 형식은 게임과 같은 채보 txt. 이 에디터가 모르는 줄(PREVIEW, DEATHAREA 등)은 그대로 보존
//
//  조작 (화면 아래쪽에도 표시)
//   마우스 좌클릭 : 노트 놓기       우클릭 : 노트 지우기      휠 : 이동 (SHIFT = 1마디)
//   1 탭  2 롱노트 시작  3 롱노트 끝  4 마디선   5 RED 노트 켜기/끄기
//   노트 폭: 클릭한 채 오른쪽으로 끌면 1~4칸 (RED 뿐 아니라 일반 탭 · 롱노트 · 화살표 모두) → 저장 형식 "1_3" (3칸 탭)
//   Q 층 전환 (2층 ↔ 1층)
//   Z + 클릭 : >>> 화살표 노트 (밝은 파랑)     X + 클릭 : <<< 화살표 노트 (밝은 노랑)
//   A + 드래그 : 영역 그리기 (가로 = 1~4칸, 세로 = 길이 — RED 노트처럼 쭉 늘림, 여러 개 동시에 가능)
//   A + 영역 위/아래 끝 드래그 : 길이 조절      A + 우클릭 : 그 영역 삭제      C + 클릭 : 그 박에서 영역 자르기
//   V + 클릭 : 레인 접기 (시간 입력)           B + 클릭 : 레인 펼치기    (V/B + 우클릭 : 삭제)
//   D + 클릭 : BPM    F + 클릭 : 변속    E + 클릭 : STOP    R + 클릭 : START   (+ 우클릭 : 삭제)
//   SPACE 재생/정지   ← → 박자 단위   ↑ ↓ 확대/축소   PAGE UP/DOWN 1마디   HOME 처음으로
//   - / = 오프셋 1ms (SHIFT 10ms)   O 오프셋 직접 입력   S 저장   ESC 3번 저장 후 나가기
// ============================================================
public class AreaChartEditor : MonoBehaviour
{
    // ===================== 채보 데이터 =====================
    private class Pos
    {
        public long M = 1, N = 0, D = 4;
        public double Beat { get { return (M - 1) * 4.0 + (double)N / Math.Max(1, D) * 4.0; } }
        public bool Same(Pos o) { return Math.Abs(Beat - o.Beat) < 1e-6; }
    }
    private class ANote : Pos { public int Lane, Type, Width = 1, Layer = NoteLayer.Top; public bool Red; public ANote Tail, Head; }
    private class ABpm : Pos { public double Bpm; public double AnchorMs = double.NaN; public bool HasAnchor { get { return !double.IsNaN(AnchorMs); } } }
    private class ASpeed : Pos { public int Layer; public double Value; public bool Show; }
    private class AMark : Pos { public bool IsStop; }
    // 영역 = 직사각형 하나 (시작 박 ~ 끝 박, 레인 Lane ~ Lane+Width-1). 여러 개를 동시에 둘 수 있음
    //  저장할 때는 게임 형식(AREA, 마디, 분자, 분모, 1010 — 그 박부터의 '영역 레인 합집합')으로 바꿔 쓴다
    private class AArea
    {
        public Pos Start, End;             // End == null : 끝 없음 (예전 채보에서 끝이 안 정해진 영역)
        public int Lane, Width = 1;
        public double StartBeat { get { return Start.Beat; } }
        public double EndBeat { get { return End == null ? double.PositiveInfinity : End.Beat; } }
        public bool HasLane(int lane) { return lane >= Lane && lane < Lane + Width; }
        public int Mask { get { int m = 0; for (int i = Lane; i < Lane + Width && i < 4; i++) m |= 1 << i; return m; } }
    }
    private class AFold : Pos { public bool Fold; public double Duration; }

    private readonly List<ANote> notes = new List<ANote>();
    private readonly List<ABpm> bpms = new List<ABpm>();
    private readonly List<ASpeed> speeds = new List<ASpeed>();
    private readonly List<AMark> marks = new List<AMark>();
    private readonly List<AArea> areas = new List<AArea>();
    private readonly List<AFold> folds = new List<AFold>();
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
    private int editLayer = NoteLayer.Top;
    private float beatSpacing = 2.4f;         // 1박의 화면 길이 (월드 유닛)
    private ANote draggingWide;              // 폭을 끌어서 정하는 중인 노트 (RED · 일반 · 화살표 모두 1~4칸)
    // 영역 드래그: 0 = 새로 그리기, 1 = 끝(위쪽) 늘이기, 2 = 시작(아래쪽) 옮기기
    private AArea areaDrag;
    private int areaDragMode;
    private int areaDragLane0;
    private double areaDragBeat0;
    private bool draggingArea { get { return areaDrag != null; } }
    private int escCount; private float escTimer;
    private string status = ""; private float statusTimer;

    // 숫자 입력창
    private enum PromptKind { None, Bpm, Speed, Fold, Unfold, Offset }
    private PromptKind prompt = PromptKind.None;
    private string promptBuffer = "";
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
    private TextMeshProUGUI infoText, helpText, statusText, promptTitle, promptValue, promptHint, promptKeys;
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
        AudioClip clip = Resources.Load<AudioClip>($"Audio/Full/{songId}");
        if (clip != null)
        {
            bgm.clip = clip;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        }

        BuildHud();
        LoadChart();
        SetStatus(T("영역 전용 에디터  ·  ", "AREA EDITOR  ·  ") + ChartPath());
    }

    private string ChartPath() { return Path.Combine(Application.streamingAssetsPath, "chart", $"{songId}_{levelId}.txt"); }

    // =====================================================================
    //  불러오기 / 저장
    // =====================================================================
    private static double Dbl(string s) { return double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture); }
    private static long L(string s) { return long.Parse(s.Trim()); }

    private void LoadChart()
    {
        notes.Clear(); bpms.Clear(); speeds.Clear(); marks.Clear(); areas.Clear(); folds.Clear(); others.Clear();
        initialBpm = 120.0; initialSpeed = 1.0; audioStart = 0.0;
        string path = ChartPath();
        if (!File.Exists(path)) { SetStatus(T("새 채보: ", "New chart: ") + path); return; }

        bool hasMusicOffset = false;
        var areaLines = new List<KeyValuePair<Pos, int>>();       // AREA 줄 (박, 레인 합집합) → 영역 사각형으로 바꿈
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
                else if (type == "AREA" && d.Length >= 5) areaLines.Add(new KeyValuePair<Pos, int>(new Pos { M = L(d[1]), N = L(d[2]), D = L(d[3]) }, AreaMask.Parse(d[4])));
                else if ((type == "FOLD" || type == "UNFOLD") && d.Length >= 5) folds.Add(new AFold { M = L(d[1]), N = L(d[2]), D = L(d[3]), Fold = type == "FOLD", Duration = Dbl(d[4]) });
                else if (type == "NOTE" && d.Length >= 5) ParseNoteLine(d);
                else if (type == "FREEKEY") { }                                                        // 삭제된 기능
                else if (type == "UI" && d.Length >= 2 && (d[1].Trim() == "BasicUI" || d[1].Trim() == "FREEKEYUI")) { }
                else others.Add(line);
            }
            catch (Exception e) { Debug.LogWarning("[AreaEditor] 읽지 못한 줄: " + line + " (" + e.Message + ")"); others.Add(line); }
        }
        // 0박의 BPM 줄은 첫 BPM 으로
        var zero = bpms.FirstOrDefault(b => b.Beat <= 1e-9);
        if (zero != null) { initialBpm = zero.Bpm; bpms.Remove(zero); }
        AreasFromMaskLines(areaLines);
        SortAll();
    }

    // AREA 줄들(박마다 '지금 영역인 레인 전체') → 영역 사각형들
    //  레인마다 켜짐 ~ 꺼짐 구간을 찾고, 시작 · 끝이 같은 이웃 레인끼리는 한 사각형으로 합친다
    //  예) AREA 1010 → 0000 : 1번 레인 영역 + 3번 레인 영역 (2개)
    private void AreasFromMaskLines(List<KeyValuePair<Pos, int>> lines)
    {
        areas.Clear();
        // 같은 박에 여러 줄이면 마지막 줄 (게임과 같음)
        var ev = new List<KeyValuePair<Pos, int>>();
        foreach (var e in lines.OrderBy(x => x.Key.Beat))
        {
            if (ev.Count > 0 && ev[ev.Count - 1].Key.Same(e.Key)) ev[ev.Count - 1] = e;
            else ev.Add(e);
        }
        var spans = new List<AArea>();
        Pos[] open = new Pos[4];
        int prev = 0;
        foreach (var e in ev)
        {
            for (int lane = 0; lane < 4; lane++)
            {
                bool was = AreaMask.Has(prev, lane), now = AreaMask.Has(e.Value, lane);
                if (!was && now) open[lane] = e.Key;
                else if (was && !now) { spans.Add(new AArea { Start = open[lane], End = e.Key, Lane = lane, Width = 1 }); open[lane] = null; }
            }
            prev = e.Value;
        }
        for (int lane = 0; lane < 4; lane++)
            if (open[lane] != null) spans.Add(new AArea { Start = open[lane], End = null, Lane = lane, Width = 1 });

        foreach (var sp in spans.OrderBy(x => x.StartBeat).ThenBy(x => x.Lane))
        {
            AArea join = areas.FirstOrDefault(a => a.Lane + a.Width == sp.Lane && a.Start.Same(sp.Start) && SameEnd(a.End, sp.End));
            if (join != null) join.Width++;
            else areas.Add(sp);
        }
    }

    private static bool SameEnd(Pos a, Pos b) { return (a == null && b == null) || (a != null && b != null && a.Same(b)); }

    // 이 박에서 영역인 레인 전체 (여러 영역의 합집합) — 게임이 쓰는 값과 같음
    private int AreaMaskAtBeat(double beat)
    {
        int m = 0;
        foreach (var a in areas)
            if (a.StartBeat <= beat + 1e-6 && beat < a.EndBeat - 1e-6) m |= a.Mask;
        return m;
    }

    // 영역 사각형들 → AREA 줄 (영역이 시작하거나 끝나는 박마다, 합집합이 바뀔 때만)
    private List<KeyValuePair<Pos, int>> AreaMaskLines()
    {
        var bounds = new List<Pos>();
        foreach (var a in areas) { bounds.Add(a.Start); if (a.End != null) bounds.Add(a.End); }
        var outLines = new List<KeyValuePair<Pos, int>>();
        int prev = 0;
        Pos last = null;
        foreach (var b in bounds.OrderBy(x => x.Beat))
        {
            if (last != null && last.Same(b)) continue;
            last = b;
            int m = AreaMaskAtBeat(b.Beat);
            if (m != prev) { outLines.Add(new KeyValuePair<Pos, int>(b, m)); prev = m; }
        }
        return outLines;
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
                int t = val[i] - '0';
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
        if (!NoteCellParser.TryParseToken(token, out int type, out int width, out bool red)) return;
        notes.Add(new ANote { M = m, N = n, D = den, Lane = lane, Type = type, Width = Mathf.Clamp(width, 1, 4 - lane), Red = red, Layer = layer });
    }

    private void SortAll()
    {
        bpms.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        speeds.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        marks.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        areas.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
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
        foreach (var a in AreaMaskLines()) o.Add($"AREA, {a.Key.M}, {a.Key.N}, {a.Key.D}, {AreaMask.Format(a.Value)}");
        foreach (var f in folds) o.Add($"{(f.Fold ? "FOLD" : "UNFOLD")}, {f.M}, {f.N}, {f.D}, {f.Duration.ToString("0.###", CultureInfo.InvariantCulture)}");
        o.AddRange(others);

        foreach (var g in notes.GroupBy(n => new { n.M, n.N, n.D }).OrderBy(g => g.First().Beat))
        {
            string[] top = new string[4], bottom = new string[4];
            foreach (var n in g)
            {
                if (n.Lane < 0 || n.Lane > 3) continue;
                // RED 는 항상 "종류_폭", 일반 노트는 2칸 이상일 때만 "종류_폭" (예: 1_3 = 3칸 탭)
                string token = (n.Red || n.Width > 1) ? $"{n.Type}_{n.Width}" : n.Type.ToString(CultureInfo.InvariantCulture);
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
            if (levelId == 1) s.NoteCount_EASY = r.Total; else if (levelId == 3) s.NoteCount_INSANE = r.Total; else s.NoteCount_HARD = r.Total;
        }
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
        if (kb.spaceKey.wasPressedThisFrame) TogglePlay();

        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) noteType = 1;
        if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) noteType = 2;
        if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) noteType = 3;
        if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) noteType = 4;
        if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) redMode = !redMode;
        if (kb.qKey.wasPressedThisFrame) editLayer = editLayer == NoteLayer.Top ? NoteLayer.Bottom : NoteLayer.Top;

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

        if (kb.sKey.wasPressedThisFrame && !kb.aKey.isPressed) Save();
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
        if (draggingArea)
        {
            UpdateAreaDrag(Mathf.Clamp(lane, 0, 3));
            if (lUp || !lHeld) EndAreaDrag();
            return;
        }

        if (!ld && !rd) return;
        Pos p = PosOf(hoverBeat);

        if (kb.aKey.isPressed)
        {
            if (hoverLane < 0) return;
            AArea under = AreaAt(hoverLane, hoverBeat);
            if (rd) { if (under != null) { areas.Remove(under); SetStatus(T("영역 삭제", "AREA deleted")); } return; }
            BeginAreaDrag(hoverLane, hoverBeat);
            return;
        }
        if (kb.cKey.isPressed)
        {
            if (!ld) return;
            // 커서 아래 영역을 이 박에서 자름. 영역 밖이면 이 박을 지나는 모든 영역을 자름
            AArea under = hoverLane >= 0 ? AreaAt(hoverLane, hoverBeat) : null;
            int cut = 0;
            foreach (var a in areas)
            {
                if (under != null && a != under) continue;
                if (a.StartBeat < hoverBeat - 1e-6 && hoverBeat < a.EndBeat - 1e-6) { a.End = p; cut++; }
            }
            SetStatus(cut > 0 ? T($"영역 {cut}개를 여기서 끝냄", $"{cut} area(s) ended here") : T("자를 영역이 없습니다", "No area to cut here"));
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
            if (rd) { speeds.RemoveAll(x => x.Same(p)); SortAll(); }
            else OpenPrompt(PromptKind.Speed, p);
            return;
        }
        if (kb.eKey.isPressed || kb.rKey.isPressed)
        {
            bool stop = kb.eKey.isPressed;
            marks.RemoveAll(x => x.IsStop == stop && x.Same(p));
            if (ld) marks.Add(new AMark { M = p.M, N = p.N, D = p.D, IsStop = stop });
            SortAll(); return;
        }

        if (hoverLane < 0) return;
        ANote exist = NoteAt(hoverLane, p, editLayer);
        if (rd) { if (exist != null) { notes.Remove(exist); linksDirty = true; } return; }

        if (exist != null) notes.Remove(exist);
        // 모든 노트(마디선 제외)는 RED 처럼 오른쪽으로 끌어 폭 1~4칸 (클릭만 하면 1칸)
        if (kb.zKey.isPressed || kb.xKey.isPressed)
            draggingWide = new ANote { M = p.M, N = p.N, D = p.D, Lane = hoverLane, Type = kb.zKey.isPressed ? NoteTypes.ArrowRight : NoteTypes.ArrowLeft, Width = 1, Layer = editLayer };
        else if (noteType == 4)
        {
            notes.Add(new ANote { M = p.M, N = p.N, D = p.D, Lane = hoverLane, Type = 4, Layer = editLayer });
            linksDirty = true;
            return;
        }
        else if (redMode)
            draggingWide = new ANote { M = p.M, N = p.N, D = p.D, Lane = hoverLane, Type = noteType + 10, Red = true, Width = 1, Layer = editLayer };
        else
            draggingWide = new ANote { M = p.M, N = p.N, D = p.D, Lane = hoverLane, Type = noteType, Width = 1, Layer = editLayer };
        notes.Add(draggingWide);
        linksDirty = true;
    }

    // ----- 영역 그리기 / 늘이기 -----
    // 커서 아래 영역 (레인이 겹치고, 박이 시작 ~ 끝 안). 나중에 그린 것이 우선
    private AArea AreaAt(int lane, double beat)
    {
        for (int i = areas.Count - 1; i >= 0; i--)
        {
            AArea a = areas[i];
            if (a.HasLane(lane) && beat >= a.StartBeat - 1e-6 && beat <= a.EndBeat + 1e-6) return a;
        }
        return null;
    }

    private void BeginAreaDrag(int lane, double beat)
    {
        double tol = Step * 0.5 + 1e-6;
        // 이미 있는 영역의 위 끝 / 아래 끝을 잡으면 길이 조절
        for (int i = areas.Count - 1; i >= 0; i--)
        {
            AArea a = areas[i];
            if (!a.HasLane(lane)) continue;
            if (a.End != null && Math.Abs(a.EndBeat - beat) <= tol) { areaDrag = a; areaDragMode = 1; return; }
            if (Math.Abs(a.StartBeat - beat) <= tol && (a.End == null || a.EndBeat - a.StartBeat > tol * 2)) { areaDrag = a; areaDragMode = 2; return; }
        }
        // 새 영역: 누른 칸 · 박에서 시작
        areaDragMode = 0;
        areaDragLane0 = lane;
        areaDragBeat0 = beat;
        areaDrag = new AArea { Start = PosOf(beat), End = PosOf(beat), Lane = lane, Width = 1 };
        areas.Add(areaDrag);
    }

    private void UpdateAreaDrag(int lane)
    {
        AArea a = areaDrag;
        double beat = hoverBeat;
        if (areaDragMode == 0)
        {
            // 가로: 누른 칸 ~ 지금 칸 (RED 노트 폭처럼),  세로: 누른 박 ~ 지금 박 (롱노트처럼 쭉)
            int l0 = Mathf.Min(areaDragLane0, lane), l1 = Mathf.Max(areaDragLane0, lane);
            a.Lane = l0; a.Width = l1 - l0 + 1;
            double s0 = Math.Min(areaDragBeat0, beat), s1 = Math.Max(areaDragBeat0, beat);
            a.Start = PosOf(s0); a.End = PosOf(s1);
        }
        else if (areaDragMode == 1) a.End = PosOf(Math.Max(beat, a.StartBeat + Step));
        else
        {
            double lim = a.End == null ? double.PositiveInfinity : a.EndBeat - Step;
            a.Start = PosOf(Math.Max(0.0, Math.Min(beat, lim)));
        }
    }

    private void EndAreaDrag()
    {
        AArea a = areaDrag;
        areaDrag = null;
        if (a == null) return;
        // 세로로 끌지 않고 클릭만 했으면 1마디 길이
        if (areaDragMode == 0 && a.End != null && a.EndBeat - a.StartBeat < 1e-6) a.End = PosOf(a.StartBeat + 4.0);
        SortAll();
        int lanes = a.Width;
        SetStatus(T($"영역  {a.Lane + 1}~{a.Lane + lanes}번 레인  ·  {(a.End == null ? "끝 없음" : (a.EndBeat - a.StartBeat).ToString("0.###", CultureInfo.InvariantCulture) + "박")}",
                    $"AREA  lane {a.Lane + 1}-{a.Lane + lanes}  ·  {(a.End == null ? "open" : (a.EndBeat - a.StartBeat).ToString("0.###", CultureInfo.InvariantCulture) + " beats")}"));
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
            case PromptKind.Speed: promptTitle.text = T("변속 (SCROLL SPEED)", "SCROLL SPEED"); promptHint.text = T("이 박자부터의 스크롤 속도 (두 층 모두)", "Scroll speed from this beat (both layers)"); break;
            case PromptKind.Fold: promptTitle.text = T("레인 접기", "LANE FOLD"); promptHint.text = T("레인이 완전히 접히는 데 걸리는 시간(초)  예: 0.5", "Seconds until the lanes are fully folded (e.g. 0.5)"); break;
            case PromptKind.Unfold: promptTitle.text = T("레인 펼치기", "LANE UNFOLD"); promptHint.text = T("레인이 완전히 펼쳐지는 데 걸리는 시간(초)  예: 0.5", "Seconds until the lanes are fully open (e.g. 0.5)"); break;
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
                speeds.RemoveAll(x => x.Same(p) && x.Layer == 0);
                speeds.Add(new ASpeed { M = p.M, N = p.N, D = p.D, Layer = 0, Value = v });
                break;
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
            if (!longHitSound && (n.Type == 3 || n.Type == 13)) continue;   // 롱노트 끝 판정(꼬리) 탭음 끔 — 시작은 그대로
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
        foreach (var n in notes) { n.Tail = null; n.Head = null; }
        for (int lane = 0; lane < 4; lane++)
            for (int layer = 1; layer <= 2; layer++)
            {
                ANote head = null;
                foreach (var n in notes.Where(x => x.Lane == lane && x.Layer == layer).OrderBy(x => x.Beat))
                {
                    if (n.Type == 2 || n.Type == 12) head = n;
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
        Box(0f, 0f, halfW * 2f, 14f, new Color(0.055f, 0.065f, 0.10f, 1f), -60);
        DrawFoldBar(b0, b1);     // 오른쪽 여백: 레인 접힘 정도 (접기 · 펼치기 미리보기)
        int foldFrom = pool.Used;                     // 여기부터 판정선까지 = 레인 접기 대상
        Box(0f, 0f, Right - Left + 0.06f, 12f, new Color(0.02f, 0.025f, 0.05f, 1f), -40);

        pool.Keep = true;
        DrawAreas(b0, b1);       // 영역 (레인 바탕 위) — 레인이 접혀도 접히지 않음
        pool.Keep = false;
        DrawGapZones(b0, b1);

        // 그리드 + 레인: 영역 위에도 항상 함께 그림 (가로 박자선 + 세로 레인선 · 양쪽 벽)
        DrawGrid(b0, b1);
        for (int i = 1; i < 4; i++) Box(Left + i * LaneW, 0f, 0.02f, 12f, new Color(1f, 1f, 1f, 0.30f), -28);
        Box(Left, 0f, 0.04f, 12f, new Color(1f, 1f, 1f, 0.85f), -27);
        Box(Right, 0f, 0.04f, 12f, new Color(1f, 1f, 1f, 0.85f), -27);

        // 노트
        foreach (var n in notes)
        {
            double nb = n.Beat;
            double tailB = n.Tail != null ? n.Tail.Beat : nb;
            if (tailB < b0 - 1 || nb > b1 + 1) continue;
            pool.Keep = NoteInArea(n);      // 영역 안 노트는 레인이 접혀도 그대로 (게임과 같은 규칙)
            DrawNote(n);
        }
        pool.Keep = false;

        // 판정선
        Box(0f, HitY, Right - Left, 0.05f, playing ? new Color(0.4f, 1f, 0.6f, 0.95f) : new Color(1f, 1f, 1f, 0.95f), 20);

        // 레인 접기 / 펼치기 미리보기 (재생 중): 게임처럼 레인 · 노트 · 판정선이 가운데로 모이며 투명해짐
        //  멈춰 있을 때는 편집할 수 있도록 접지 않고, 오른쪽 주황 막대와 정보창(접힘 %)으로만 보여 줌
        if (playing)
        {
            float k = FoldAt(playTime);
            if (k < 0.999f)
            {
                pool.ApplyFold(foldFrom, pool.Used, Mathf.Max(0.001f, k), k);
                // 접히는 동안: 지금 영역인 레인 위에만 판정선 (게임과 같음)
                int maskNow = AreaMaskAtBeat(BeatOf(playTime));
                for (int l = 0; l < 4; l++)
                {
                    if (!AreaMask.Has(maskNow, l)) continue;
                    int e = l;
                    while (e + 1 < 4 && AreaMask.Has(maskNow, e + 1)) e++;
                    float x0 = Left + l * LaneW, x1 = Left + (e + 1) * LaneW;
                    Box((x0 + x1) * 0.5f, HitY, x1 - x0, 0.05f, new Color(0.4f, 1f, 0.6f, 0.95f * (1f - k)), 20);
                    l = e;
                }
            }
        }

        // 마우스 위치 미리보기
        if (!playing && prompt == PromptKind.None && hoverLane >= 0 && draggingWide == null && !draggingArea)
        {
            var kb = Keyboard.current;
            bool arrow = kb != null && (kb.zKey.isPressed || kb.xKey.isPressed);
            Color c = arrow ? NoteArt.ArrowColor(kb.zKey.isPressed ? NoteTypes.ArrowRight : NoteTypes.ArrowLeft)
                    : (redMode ? new Color(1f, 0.38f, 0.45f) : SimpleGameSkin.LaneColor(hoverLane));
            Note(LaneX(hoverLane), Y(hoverBeat), 0.8f * 0.95f, SimpleGameSkin.NoteHeight, new Color(c.r, c.g, c.b, 0.35f), 15);
        }
        // 영역 편집 키(A / C)를 누르고 있으면 커서 아래 영역을 밝게 + 잡을 수 있는 끝 표시
        var kbd = Keyboard.current;
        if (!playing && kbd != null && (kbd.aKey.isPressed || kbd.cKey.isPressed) && hoverLane >= 0)
        {
            AArea h = draggingArea ? areaDrag : AreaAt(hoverLane, hoverBeat);
            float hx0 = Left + (h != null ? h.Lane : hoverLane) * LaneW, hx1 = hx0 + (h != null ? h.Width : 1) * LaneW;
            if (h != null)
            {
                float y0 = Mathf.Max(Y(h.StartBeat), -6f), y1 = double.IsInfinity(h.EndBeat) ? 6f : Mathf.Min(Y(h.EndBeat), 6f);
                if (y1 > y0) Box((hx0 + hx1) * 0.5f, (y0 + y1) * 0.5f, hx1 - hx0, y1 - y0, new Color(1f, 1f, 1f, 0.08f), -33);
            }
            Color cc = kbd.cKey.isPressed ? new Color(1f, 0.55f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.9f);
            Box((hx0 + hx1) * 0.5f, Y(hoverBeat), hx1 - hx0, 0.06f, cc, 16);      // A: 놓을 · 늘릴 위치,  C: 자를 위치
        }
        pool.End();
    }

    // 영역 그리기 (게임과 같은 방식): 영역 사각형들을 박마다 '영역 레인 합집합' 구간으로 바꿔서 그림
    //  → 옆으로 붙어 있는 영역은 한 덩어리 (사이에 테두리 없음),
    //    가로 시작선 · 끝선은 영역이 새로 시작되는 / 끝나는 레인에만
    private void DrawAreas(double b0, double b1)
    {
        var lines = AreaMaskLines();
        for (int i = 0; i < lines.Count; i++)
        {
            int mask = lines[i].Value;
            if (mask == 0) continue;
            int before = i > 0 ? lines[i - 1].Value : 0;
            int after = i + 1 < lines.Count ? lines[i + 1].Value : 0;
            double sb = lines[i].Key.Beat;
            double eb = i + 1 < lines.Count ? lines[i + 1].Key.Beat : double.PositiveInfinity;
            if (eb < b0 || sb > b1) continue;
            float ys = Y(sb), ye = double.IsInfinity(eb) ? 99f : Y(eb);
            float y0 = Mathf.Max(ys, -6f), y1 = Mathf.Min(ye, 6f);
            if (y1 <= y0) continue;
            for (int l = 0; l < 4; l++)
            {
                if (!AreaMask.Has(mask, l)) continue;
                int e = l;
                while (e + 1 < 4 && AreaMask.Has(mask, e + 1)) e++;
                DrawAreaBlock(l, e, ys, ye, y0, y1, before, after, !double.IsInfinity(eb));
                l = e;
            }
        }
    }

    // 이어진 레인 [l0 ~ l1] 의 한 구간: 검은 바탕 · 회색 사선 · 양옆 흰 테두리 + 안쪽 빛 · 필요한 곳에만 가로선
    private void DrawAreaBlock(int l0, int l1, float ys, float ye, float y0, float y1, int before, int after, bool hasEnd)
    {
        Color ec = NoteArt.AreaEdge;
        float ew = NoteArt.AreaEdgeWidth * K;
        float x0 = Left + l0 * LaneW, x1 = Left + (l1 + 1) * LaneW;
        float my = (y0 + y1) * 0.5f, hgt = y1 - y0;
        Box((x0 + x1) * 0.5f, my, x1 - x0, hgt, NoteArt.AreaBase, -36);
        SpriteRenderer st = pool.Get(NoteArt.AreaStripeSprite, -35);
        st.drawMode = SpriteDrawMode.Tiled;
        st.size = new Vector2(x1 - x0, hgt);
        st.transform.localScale = Vector3.one;
        st.transform.position = new Vector3((x0 + x1) * 0.5f, my, 0f);
        st.color = NoteArt.AreaStripe;
        float gw = Mathf.Min(NoteArt.AreaEdgeGlowWidth * K, (x1 - x0) * 0.45f);
        Vector2 hs = NoteArt.HGradientSprite.bounds.size;
        SpriteRenderer gl = pool.Get(NoteArt.HGradientSprite, -34);
        gl.drawMode = SpriteDrawMode.Simple;
        gl.transform.localScale = new Vector3(gw / hs.x, hgt / hs.y, 1f);
        gl.transform.position = new Vector3(x0, my, 0f);
        gl.color = NoteArt.AreaEdgeGlow;
        SpriteRenderer gr = pool.Get(NoteArt.HGradientSprite, -34);
        gr.drawMode = SpriteDrawMode.Simple;
        gr.transform.localScale = new Vector3(-gw / hs.x, hgt / hs.y, 1f);
        gr.transform.position = new Vector3(x1, my, 0f);
        gr.color = NoteArt.AreaEdgeGlow;
        Box(x0 + ew * 0.5f, my, ew, hgt, ec, -34);
        Box(x1 - ew * 0.5f, my, ew, hgt, ec, -34);
        if (ys >= -6f) CapRuns(l0, l1, before, ys, ec);                 // 시작선: 새로 시작된 레인만
        if (hasEnd && ye <= 6f) CapRuns(l0, l1, after, ye, ec);         // 끝선: 여기서 끝나는 레인만
    }

    private void CapRuns(int l0, int l1, int other, float y, Color c)
    {
        for (int l = l0; l <= l1; l++)
        {
            if (AreaMask.Has(other, l)) continue;
            int e = l;
            while (e + 1 <= l1 && !AreaMask.Has(other, e + 1)) e++;
            float x0 = Left + l * LaneW, x1 = Left + (e + 1) * LaneW;
            Box((x0 + x1) * 0.5f, y, x1 - x0, 0.06f, c, -34);
            l = e;
        }
    }

    // ----- 레인 접기 미리보기 -----
    // 영역 안 노트: 노트가 차지하는 레인(RED 는 폭 전체) 중 하나라도 판정 범위(±BAD 0.12초) 동안 영역이었으면 true
    //  롱노트는 머리 ~ 꼬리 전체로 판단 (머리 · 몸통 · 꼬리 모두 같은 결과). 게임의 GameManager.NoteInArea 와 같은 규칙
    private const double AreaJudgeWindow = 0.120;
    private bool NoteInArea(ANote n)
    {
        if (areas.Count == 0 || n.Type == 4) return false;
        ANote head = n.Head ?? n;
        double t0 = TimeOf(head.Beat) - AreaJudgeWindow;
        double t1 = TimeOf(head.Tail != null ? head.Tail.Beat : head.Beat) + AreaJudgeWindow;
        int noteMask = 0;
        int w = Mathf.Max(1, head.Width);
        for (int i = head.Lane; i < head.Lane + w && i < 4; i++) if (i >= 0) noteMask |= 1 << i;
        foreach (var a in areas)
        {
            if ((a.Mask & noteMask) == 0) continue;
            double s = TimeOf(a.StartBeat), e = a.End == null ? double.PositiveInfinity : TimeOf(a.EndBeat);
            if (s <= t1 && e > t0) return true;
        }
        return false;
    }

    // 게임(GameManager.LaneFoldAt)과 같은 계산: 1 = 다 펼침, 0 = 다 접힘
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

    // 레인 오른쪽 여백의 세로 막대: 진할수록 많이 접힌 구간 (접히는 · 펼쳐지는 중간 과정도 보임)
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
        float alpha = n.Layer == editLayer ? 1f : 0.3f;
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
        bool tail = n.Type == 3 || n.Type == 13;

        // 롱노트 몸통 (머리 → 꼬리)
        if (n.Tail != null)
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
                Color lc = NoteTypes.IsArrow(n.Type) ? NoteArt.ArrowColor(n.Type) : SimpleGameSkin.LaneColor(n.Lane);
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

        Color c = NoteTypes.IsArrow(n.Type) ? NoteArt.ArrowColor(n.Type) : SimpleGameSkin.LaneColor(n.Lane);
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
            sr.size = new Vector2(wGame + pad, SimpleGameSkin.NoteHeight + pad);
            sr.transform.position = new Vector3(cx, y, 0f);
            sr.color = new Color(c.r, c.g, c.b, alpha);
        }

        if (NoteTypes.IsArrow(n.Type))
        {
            // 노트 위에 떠 있는 >>> / <<< + 은은한 빛
            SpriteRenderer g = pool.Get(NoteArt.GlowSprite, order - 1);
            g.drawMode = SpriteDrawMode.Simple;
            Vector2 gs = NoteArt.GlowSprite.bounds.size;
            g.transform.localScale = new Vector3(0.95f * K / gs.x, 0.6f * K / gs.y, 1f);
            g.transform.position = new Vector3(cx, y + 0.12f, 0f);
            g.color = new Color(c.r, c.g, c.b, 0.5f * alpha);

            SpriteRenderer a = pool.Get(NoteArt.ArrowSprite, order + 1);
            a.drawMode = SpriteDrawMode.Simple;
            Vector2 asz = NoteArt.ArrowSprite.bounds.size;
            float dir = n.Type == NoteTypes.ArrowLeft ? -1f : 1f;
            a.transform.localScale = new Vector3(dir * 0.62f * K / asz.x, 0.24f * K / asz.y, 1f);
            a.transform.position = new Vector3(cx, y + 0.30f * K, 0f);
            a.color = new Color(c.r, c.g, c.b, alpha);
        }
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
    //  HUD (글자는 영문 · 숫자만)
    // =====================================================================
    // 글자 만들기: 지정 글꼴 · 글자 크기 배율 적용
    private TextMeshProUGUI Txt(string name, Transform parent, float size, TextAlignmentOptions align, Color color,
                                FontStyles style = FontStyles.Normal, bool wrap = false)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt(name, parent, "", size * fontScale, align, color, style, wrap);
        if (font != null) t.font = font;
        return t;
    }

    private void BuildHud()
    {
        SettingsUIKit.EnsureFont();
        GameObject cgo = new GameObject("AreaEditorHud", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        hud = cgo.AddComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 500;
        CanvasScaler sc = cgo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;

        labelRoot = SettingsUIKit.Rect("Labels", cgo.transform);
        SettingsUIKit.Stretch(labelRoot);

        infoText = Txt("Info", cgo.transform, 24, TextAlignmentOptions.TopLeft, Color.white, FontStyles.Normal, true);
        SettingsUIKit.Place(infoText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -460f), new Vector2(620f, -28f));

        helpText = Txt("Help", cgo.transform, 17, TextAlignmentOptions.TopLeft, new Color(1f, 1f, 1f, 0.6f), FontStyles.Normal, true);
        SettingsUIKit.Place(helpText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-560f, -560f), new Vector2(-32f, -28f));
        RefreshStaticTexts();

        statusText = Txt("Status", cgo.transform, 22, TextAlignmentOptions.Bottom, SettingsUIKit.Accent);
        SettingsUIKit.Place(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 24f), new Vector2(0f, 64f));

        // 숫자 입력창
        Image panel = SettingsUIKit.Img("Prompt", cgo.transform, new Color(0.05f, 0.07f, 0.12f, 0.96f), true);
        SettingsUIKit.Center(panel.rectTransform, Vector2.zero, new Vector2(640f, 220f));
        promptPanel = panel.gameObject;
        Image line = SettingsUIKit.Img("Line", panel.transform, SettingsUIKit.Accent);
        SettingsUIKit.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero);
        promptTitle = Txt("Title", panel.transform, 30, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
        SettingsUIKit.Place(promptTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -70f), new Vector2(-20f, -16f));
        promptHint = Txt("Hint", panel.transform, 18, TextAlignmentOptions.Center, SettingsUIKit.TextSub);
        SettingsUIKit.Place(promptHint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -104f), new Vector2(-20f, -72f));
        promptValue = Txt("Value", panel.transform, 40, TextAlignmentOptions.Center, SettingsUIKit.Accent, FontStyles.Bold);
        SettingsUIKit.Place(promptValue.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 46f), new Vector2(-20f, 110f));
        promptKeys = Txt("Keys", panel.transform, 16, TextAlignmentOptions.Center, SettingsUIKit.TextSub);
        TextMeshProUGUI keys = promptKeys;
        SettingsUIKit.Place(keys.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 12f), new Vector2(-20f, 40f));
        promptPanel.SetActive(false);
        RefreshStaticTexts();
    }

    // 언어가 바뀔 때 다시 쓰는 글자들
    private void RefreshStaticTexts()
    {
        helpText.text = korean
            ? "<b>영역 전용 에디터</b>\n" +
              "좌클릭  노트 놓기      우클릭  노트 지우기\n" +
              "1 탭   2 롱노트 시작   3 롱노트 끝   4 마디선\n" +
              "5 RED 켜기/끄기   Q 층 전환\n" +
              "클릭한 채 오른쪽으로 끌기  노트 폭 1~4칸 (모든 노트)\n" +
              "Z + 클릭  >>> 화살표 노트   X + 클릭  <<< 화살표 노트\n" +
              "A + 드래그  영역 그리기 (가로 1~4칸 · 세로로 쭉 늘림)\n" +
              "A + 영역 끝 드래그  길이 조절   A + 우클릭  영역 삭제\n" +
              "C + 클릭  그 박에서 영역 자르기 (영역은 여러 개 가능)\n" +
              "V + 클릭  레인 접기   B + 클릭  레인 펼치기\n" +
              "  (재생하면 미리보기 · 오른쪽 주황 막대 = 접힌 구간)\n" +
              "D BPM   F 변속   E 정지(STOP)   R 재개(START)\n" +
              "(키를 누른 채 우클릭 = 삭제)\n" +
              "SPACE 재생/정지   휠 이동 (SHIFT = 1마디)\n" +
              "좌우 방향키 박자 단위   상하 방향키 확대/축소\n" +
              "PgUp / PgDn 1마디   HOME 처음으로\n" +
              "- / =  오프셋 1ms (SHIFT 10ms)   O 오프셋 직접 입력\n" +
              "F1 / F2 음악 볼륨   F3 / F4 탭음 볼륨   F5 롱노트 끝 탭음 켜기/끄기\n" +
              "S 저장   ESC 3번 저장 후 나가기   L 한국어/English"
            : "<b>AREA EDITOR</b>\n" +
              "L-Click  place note      R-Click  delete\n" +
              "1 TAP  2 LONG START  3 LONG END  4 MEASURE\n" +
              "5 RED on/off   Q layer\n" +
              "click + drag right  note width 1-4 (all notes)\n" +
              "Z + click  >>> arrow      X + click  <<< arrow\n" +
              "A + drag  draw AREA (1-4 lanes wide, drag up = length)\n" +
              "A + drag area edge  resize   A + R-Click  delete area\n" +
              "C + click  cut area at this beat (many areas allowed)\n" +
              "V + click  FOLD   B + click  UNFOLD\n" +
              "  (preview while playing · orange bar = folded)\n" +
              "D BPM   F SPEED   E STOP   R START\n" +
              "(hold key + R-Click = delete)\n" +
              "SPACE play   Wheel move (SHIFT = 1 bar)\n" +
              "Left/Right snap   Up/Down zoom   PgUp/PgDn bar\n" +
              "HOME go to start\n" +
              "- / =  offset 1ms (SHIFT 10ms)   O type offset\n" +
              "F1/F2 music volume   F3/F4 hit sound   F5 long note end sound\n" +
              "S save   ESC x3 save & exit   L 한국어/English";
        if (promptKeys != null) promptKeys.text = T("ENTER 적용   ESC 취소", "ENTER apply   ESC cancel");
    }

    private void DrawHud()
    {
        if (infoText == null) return;
        int div = SnapDivs[snapIndex];
        string typeName = noteType == 1 ? T("탭", "TAP") : noteType == 2 ? T("롱노트 시작", "LONG START") : noteType == 3 ? T("롱노트 끝", "LONG END") : T("마디선", "MEASURE LINE");
        long m = (long)Math.Floor(cursorBeat / 4.0) + 1;
        double inBar = cursorBeat - (m - 1) * 4.0;
        double bpmNow = initialBpm;
        foreach (var sg in segs) { if (sg.Beat <= cursorBeat + 1e-6) bpmNow = sg.Bpm; else break; }
        int maskNow = AreaMaskAtBeat(cursorBeat);
        int areaCountNow = areas.Count(a => a.StartBeat <= cursorBeat + 1e-6 && cursorBeat < a.EndBeat - 1e-6);
        float foldNow = FoldAt(playing ? playTime : TimeOf(cursorBeat));
        infoText.text =
            $"<b>{title}</b>   <color=#ff3131>{DifficultyHelper.GetName(levelId)}</color>\n" +
            $"<color=#5ce1e6>{T("영역 전용 에디터", "AREA EDITOR")}</color>  ({T("SIMPLE 노트", "SIMPLE notes")})\n\n" +
            $"{T("노트", "NOTE")}   {(redMode ? "<color=#ff6070>RED </color>" : "")}{typeName}\n" +
            $"{T("층", "LAYER")}   {(editLayer == NoteLayer.Top ? T("2층 (위)", "2 (top)") : T("1층 (아래)", "1 (bottom)"))}\n" +
            $"{T("박자 단위", "SNAP")}   1/{div * 4}\n" +
            $"{T("마디", "BAR")}   {m}  +{inBar.ToString("0.###", CultureInfo.InvariantCulture)} {T("박", "beat")}\n" +
            $"{T("시간", "TIME")}   {(playing ? playTime : TimeOf(cursorBeat)).ToString("0.000", CultureInfo.InvariantCulture)} s\n" +
            $"BPM   {bpmNow.ToString("0.##", CultureInfo.InvariantCulture)}\n" +
            $"{T("오프셋", "OFFSET")}   {ChartOffset.Display(audioStart)}\n" +
            $"{T("영역", "AREA")}   {(maskNow == 0 ? "-" : AreaMask.Format(maskNow) + T($"  ({areaCountNow}개)", $"  ({areaCountNow})"))}   <color=#888888>{T($"전체 {areas.Count}개", $"total {areas.Count}")}</color>\n" +
            $"{T("레인", "LANES")}   {(foldNow > 0.999f ? T("펼침", "open") : foldNow < 0.001f ? T("<color=#ff9a4d>접힘</color>", "<color=#ff9a4d>folded</color>") : T($"<color=#ff9a4d>접히는 중 {Mathf.RoundToInt(foldNow * 100f)}%</color>", $"<color=#ff9a4d>{Mathf.RoundToInt(foldNow * 100f)}%</color>"))}\n\n" +
            $"{T("음악 볼륨", "MUSIC")}   {Mathf.RoundToInt(musicVolume * 100f)}%\n" +
            $"{T("탭음 볼륨", "HIT SOUND")}   {Mathf.RoundToInt(hitVolume * 100f)}%\n" +
            $"{T("롱노트 끝 탭음", "LONG END HIT")}   {(longHitSound ? "ON" : "<color=#888888>OFF</color>")}";
        statusText.text = statusTimer > 0f ? status : "";
        if (promptPanel.activeSelf) promptValue.text = promptBuffer + (Mathf.PingPong(Time.unscaledTime * 2f, 1f) > 0.5f ? "|" : " ");

        // 박자선 옆 표시 (마디 번호 · 이벤트)
        labelUsed = 0;
        float bottom = -5.5f, top = 5.5f;
        double b0 = cursorBeat + (bottom - HitY) / beatSpacing, b1 = cursorBeat + (top - HitY) / beatSpacing;
        for (long bar = Math.Max(1, (long)Math.Floor(b0 / 4.0)); bar <= (long)Math.Ceiling(b1 / 4.0) + 1; bar++)
        {
            double bb = (bar - 1) * 4.0;
            if (bb < b0 || bb > b1) continue;
            Label(Left - 0.15f, Y(bb), bar.ToString(), new Color(1f, 1f, 1f, 0.5f), false);
        }
        var events = new List<KeyValuePair<double, KeyValuePair<string, Color>>>();
        foreach (var b in bpms) events.Add(Ev(b.Beat, (b.HasAnchor ? "TIMING " : "BPM ") + b.Bpm.ToString("0.##", CultureInfo.InvariantCulture), new Color(1f, 0.7f, 0.2f)));
        foreach (var s in speeds) events.Add(Ev(s.Beat, T("변속 x", "SPEED x") + s.Value.ToString("0.00", CultureInfo.InvariantCulture) + (s.Layer != 0 ? " L" + s.Layer : ""), new Color(0.8f, 0.45f, 1f)));
        foreach (var k in marks) events.Add(Ev(k.Beat, k.IsStop ? T("정지 STOP", "STOP") : T("재개 START", "START"), k.IsStop ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 0.9f, 0.5f)));
        foreach (var a in areas)
        {
            string lanes = a.Width == 1 ? $"{a.Lane + 1}" : $"{a.Lane + 1}~{a.Lane + a.Width}";
            events.Add(Ev(a.StartBeat, T($"영역 {lanes}번", $"AREA {lanes}"), new Color(0.62f, 0.86f, 1f)));
            if (a.End != null) events.Add(Ev(a.EndBeat, T($"영역 {lanes}번 끝", $"AREA {lanes} END"), new Color(0.62f, 0.86f, 1f, 0.7f)));
        }
        foreach (var f in folds) events.Add(Ev(f.Beat, (f.Fold ? T("접기 ", "FOLD ") : T("펼치기 ", "UNFOLD ")) + f.Duration.ToString("0.###", CultureInfo.InvariantCulture) + "s", f.Fold ? new Color(1f, 0.6f, 0.3f) : new Color(0.5f, 1f, 0.6f)));
        double lastBeat = double.NaN; int stack = 0;
        foreach (var e in events.OrderBy(x => x.Key))
        {
            if (e.Key < b0 || e.Key > b1) continue;
            stack = (!double.IsNaN(lastBeat) && Math.Abs(e.Key - lastBeat) < 1e-6) ? stack + 1 : 0;
            lastBeat = e.Key;
            Label(Right + 0.18f, Y(e.Key), e.Value.Key, e.Value.Value, true, stack);
        }
        for (int i = labelUsed; i < labelPool.Count; i++) labelPool[i].gameObject.SetActive(false);
    }

    private static KeyValuePair<double, KeyValuePair<string, Color>> Ev(double beat, string text, Color c)
    {
        return new KeyValuePair<double, KeyValuePair<string, Color>>(beat, new KeyValuePair<string, Color>(text, c));
    }

    private void Label(float wx, float wy, string text, Color c, bool leftAlign, int stack = 0)
    {
        if (cam == null) return;
        TextMeshProUGUI t;
        if (labelUsed < labelPool.Count) t = labelPool[labelUsed];
        else
        {
            t = Txt("Label", labelRoot, 18, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
            t.rectTransform.sizeDelta = new Vector2(260f, 26f);
            labelPool.Add(t);
        }
        labelUsed++;
        t.gameObject.SetActive(true);
        t.text = text;
        t.color = c;
        t.alignment = leftAlign ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
        t.rectTransform.pivot = new Vector2(leftAlign ? 0f : 1f, 0.5f);
        Vector3 sp = cam.WorldToScreenPoint(new Vector3(wx, wy, 0f));
        float scale = hud.scaleFactor > 0f ? hud.scaleFactor : 1f;
        t.rectTransform.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale + stack * 22f);
    }

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
        public bool Keep;            // true 로 둔 동안 그린 것은 레인 접기의 영향을 받지 않음 (영역 · 영역 노트)
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
            return sr;
        }
        public void End() { for (int i = used; i < items.Count; i++) if (items[i].enabled) items[i].enabled = false; }
        public int Used { get { return used; } }

        // [from, to) 번째로 그린 것들을 가운데(x = 0)로 모으고 투명하게 (레인 접기 미리보기)
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