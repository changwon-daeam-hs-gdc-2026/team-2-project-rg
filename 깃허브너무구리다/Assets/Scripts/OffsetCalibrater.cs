using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using K = SettingsUIKit;

// ============================================================
// 오프셋 자동 측정 (설정 > OFFSET > SOUND TEST / VISUAL TEST)
//
//  SOUND TEST  : 120 BPM 클릭 소리만 듣고 박자에 맞춰 탭 → 평균 오차 M (음악 지연 + 입력 지연)
//  VISUAL TEST : 소리 없이 떨어지는 막대가 선에 닿을 때 탭 → 평균 오차 E (화면 지연 + 입력 지연)
//
//  - 처음 4박은 준비(카운트인)라 세지 않는다. 16번 탭하면 끝난다.
//  - 빗나간 탭(박자에서 ±180ms 이상)은 버리고, 남은 16개 중 위아래 3개씩 잘라낸 평균을 쓴다(트림 평균).
//  - 시계는 GameManager 와 같은 방식(PlayScheduled 로 정확히 예약 재생 + 프레임마다 dspTime 에 부드럽게 맞춤)이고,
//    탭 시각도 게임과 같이 '키를 실제로 누른 시각'(InputTiming)으로 재서 측정값이 실제 플레이와 같은 기준이 되도록 했다.
//  - 결과는 + = 늦게 침. 오프셋에는 부호를 뒤집어 들어간다 (+ = 노트가 빨라짐, osu! 방향).
// ============================================================
public class OffsetCalibrator : MonoBehaviour
{
    public enum Mode { Sound, Visual }
    private enum State { Hidden, Ready, Running, Done }

    private const int RequiredTaps = 16;
    private const int CountInBeats = 4;
    private const double TargetInterval = 0.5; // 120 BPM
    private const double RejectWindow = 0.18;
    private const float NoteSpeed = 520f;      // px/s
    private const float JudgeLineY = 70f;

    public bool IsActive { get { return state != State.Hidden; } }
    public bool IsRunning { get { return state == State.Running; } }

    private SettingsWindow owner;
    private Mode mode;
    private State state = State.Hidden;

    // UI
    private GameObject overlay;
    private TextMeshProUGUI titleText, infoText, bigText, statsText, tapText;
    private RectTransform lane;
    private readonly List<Image> notes = new List<Image>();
    private Button btnStart, btnApply, btnRetry, btnBack;
    private TextMeshProUGUI retryText;

    // 측정
    private AudioSource clickSource;
    private AudioClip clickClip;
    private double interval = TargetInterval;
    private double clock;
    private bool audioStarted;
    private double dspStart;
    private double visualStart;
    private Key[] tapKeys;
    private readonly List<double> errors = new List<double>();
    private double result;
    private double spread;

    // =====================================================================
    //  화면 만들기
    // =====================================================================
    public void Build(RectTransform panel, SettingsWindow window)
    {
        owner = window;

        Image bg = K.Img("Calibration", panel, K.PanelSolid, true);
        K.Stretch(bg.rectTransform);
        overlay = bg.gameObject;

        Image topLine = K.Img("TopLine", bg.rectTransform, K.Accent);
        K.Place(topLine.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero);

        titleText = K.Txt("Title", bg.rectTransform, "", 40, TextAlignmentOptions.Left, K.TextMain, FontStyles.Bold);
        K.Place(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -86f), new Vector2(-40f, -24f));

        infoText = K.Txt("Info", bg.rectTransform, "", 21, TextAlignmentOptions.TopLeft, K.TextSub, FontStyles.Normal, true);
        K.Place(infoText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -200f), new Vector2(-40f, -96f));

        // 화면 기준 측정용 레인
        Image laneImg = K.Img("Lane", bg.rectTransform, new Color(1f, 1f, 1f, 0.05f));
        lane = laneImg.rectTransform;
        K.Center(lane, new Vector2(0f, -20f), new Vector2(240f, 440f));
        lane.gameObject.AddComponent<RectMask2D>();
        Image line = K.Img("JudgeLine", lane, K.Accent);
        line.rectTransform.anchorMin = new Vector2(0f, 0f);
        line.rectTransform.anchorMax = new Vector2(1f, 0f);
        line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        line.rectTransform.anchoredPosition = new Vector2(0f, JudgeLineY);
        line.rectTransform.sizeDelta = new Vector2(0f, 6f);
        for (int i = 0; i < 6; i++)
        {
            Image n = K.Img("Note" + i, lane, Color.white);
            n.rectTransform.anchorMin = n.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            n.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            n.rectTransform.sizeDelta = new Vector2(200f, 24f);
            n.enabled = false;
            notes.Add(n);
        }

        bigText = K.Txt("Big", bg.rectTransform, "", 80, TextAlignmentOptions.Center, K.TextMain, FontStyles.Bold);
        K.Center(bigText.rectTransform, new Vector2(0f, -10f), new Vector2(1000f, 140f));

        tapText = K.Txt("LastTap", bg.rectTransform, "", 36, TextAlignmentOptions.Left, K.TextMain, FontStyles.Bold);
        K.Center(tapText.rectTransform, new Vector2(330f, -20f), new Vector2(300f, 60f));

        statsText = K.Txt("Stats", bg.rectTransform, "", 26, TextAlignmentOptions.Center, K.TextMain);
        K.Place(statsText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 104f), new Vector2(-40f, 190f));

        TextMeshProUGUI t;
        btnBack = K.Btn(bg.rectTransform, "뒤로", 22, Back, out t);
        PlaceBottom(btnBack, 0);
        btnRetry = K.Btn(bg.rectTransform, "다시", 22, StartRun, out retryText);
        PlaceBottom(btnRetry, 1);
        btnStart = K.Btn(bg.rectTransform, "시작", 22, StartRun, out t);
        PlaceBottom(btnStart, 2);
        K.SetButtonColor(btnStart, K.AccentDark);
        btnApply = K.Btn(bg.rectTransform, "적용", 22, Apply, out t);
        PlaceBottom(btnApply, 2);
        K.SetButtonColor(btnApply, K.AccentDark);

        clickSource = gameObject.AddComponent<AudioSource>();
        clickSource.playOnAwake = false;
        clickSource.loop = true;

        overlay.SetActive(false);
    }

    // 아래쪽 버튼 자리: slot 0 = 왼쪽(BACK) … slot 2 = 맨 오른쪽(START / APPLY)
    private static void PlaceBottom(Button b, int slot)
    {
        const float w = 170f, gap = 16f;
        float right = 40f + (2 - slot) * (w + gap);
        K.Place((RectTransform)b.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-(right + w), 32f), new Vector2(-right, 86f));
    }

    // =====================================================================
    //  흐름
    // =====================================================================
    public void Begin(Mode m)
    {
        mode = m;
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        EnterReady();
    }

    public void Cancel()
    {
        StopAudio();
        state = State.Hidden;
        if (overlay != null) overlay.SetActive(false);
    }

    // ESC: 측정 중이면 준비 화면으로, 아니면 설정창으로 돌아감
    public bool HandleEscape()
    {
        if (!IsActive) return false;
        if (state == State.Running) EnterReady();
        else Back();
        return true;
    }

    private void Back()
    {
        Cancel();
        owner.FocusCurrentTab();
    }

    private void EnterReady()
    {
        state = State.Ready;
        StopAudio();
        errors.Clear();

        bool sound = mode == Mode.Sound;
        titleText.text = sound ? "소리 테스트" : "화면 테스트";
        infoText.text = sound
            ? "박자마다 '딱' 소리가 납니다. 소리에 정확히 맞춰 레인 키나 SPACE 를 누르세요.\n" +
              "처음 4번은 준비 박자이고, 16번 눌러야 합니다. 화면은 보지 말고 귀로만 맞추세요.\n" +
              "평소 플레이할 때 쓰는 이어폰 / 스피커로 하세요."
            : "소리 없이 박자마다 막대가 내려옵니다. 막대가 선에 닿는 순간 레인 키나 SPACE 를 누르세요.\n" +
              "처음 4개(흐린 막대)는 준비 박자이고, 16번 눌러야 합니다.\n" +
              "먼저 소리 테스트를 하세요. 이 테스트는 오디오 · 판정 오프셋 사이만 나눠 맞춥니다.";
        bigText.text = sound ? "준비" : "";
        bigText.color = K.TextMain;
        statsText.text = "시작 을 누르세요";
        tapText.text = "";
        lane.gameObject.SetActive(!sound);
        HideNotes();

        btnStart.gameObject.SetActive(true);
        btnApply.gameObject.SetActive(false);
        btnRetry.gameObject.SetActive(false);
        btnBack.gameObject.SetActive(true);
        owner.SelectNextFrame(btnStart.gameObject);
    }

    private void StartRun()
    {
        tapKeys = SettingsStore.GetLaneKeys().Concat(new[] { Key.Space }).ToArray();
        InputTiming.Watch(tapKeys);
        errors.Clear();
        tapText.text = "";
        state = State.Running;

        btnStart.gameObject.SetActive(false);
        btnApply.gameObject.SetActive(false);
        btnRetry.gameObject.SetActive(true);
        retryText.text = "처음부터";
        btnBack.gameObject.SetActive(true);
        // Space 로 탭할 때 선택된 버튼이 눌리지 않도록 선택 해제
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        if (mode == Mode.Sound)
        {
            StopAudio();
            if (clickClip == null) clickClip = CreateClickClip(out interval);
            clickSource.clip = clickClip;
            clickSource.volume = 0.9f;
            // 0.6초 뒤 정확한 dspTime 에 첫 클릭이 나도록 예약 (Play() 는 다음 오디오 버퍼까지 밀려 측정값이 흔들렸다)
            clickSource.timeSamples = 0;
            dspStart = AudioSettings.dspTime + 0.6;
            clickSource.PlayScheduled(dspStart);
            clock = -0.6;
            audioStarted = true;
            bigText.text = "듣기";
            lane.gameObject.SetActive(false);
        }
        else
        {
            interval = TargetInterval;
            visualStart = Time.realtimeSinceStartupAsDouble;
            clock = -1.2;
            bigText.text = "";
            lane.gameObject.SetActive(true);
        }
        UpdateStats();
    }

    void Update()
    {
        if (state != State.Running) return;

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (mode == Mode.Sound) AdvanceAudioClock(dt);
        else
        {
            clock = Time.realtimeSinceStartupAsDouble - visualStart - 1.2;
            UpdateNotes();
        }

        if (mode == Mode.Sound) bigText.text = clock < (CountInBeats - 0.5) * interval ? "듣기" : "누르기";

        var kb = Keyboard.current;
        if (kb != null)
        {
            for (int i = 0; i < tapKeys.Length; i++)
            {
                if (kb[tapKeys[i]].wasPressedThisFrame) { RegisterTap(clock - InputTiming.PressAge(tapKeys[i])); break; }
            }
        }

        if (errors.Count >= RequiredTaps) Finish();
    }

    // GameManager 와 같은 방식: 예약한 재생 시작 시각(dspStart) 기준으로, 매 프레임 시계를 dspTime 에 부드럽게 맞춘다
    private void AdvanceAudioClock(float dt)
    {
        if (!audioStarted) { clock += dt; return; }

        double target = AudioSettings.dspTime - dspStart;   // 예약 시각 전에는 음수
        double diff = target - clock;
        if (Math.Abs(diff) > 0.1) clock = target;
        else clock += dt * Mathf.Clamp(1f + (float)(diff * 2.0), 0.5f, 1.5f);
    }

    private void RegisterTap(double t)
    {
        if (t < 0.0) return;
        int k = (int)Math.Round(t / interval);
        if (k < CountInBeats)
        {
            ShowTap("기다리세요", K.TextSub);
            return;
        }
        double err = t - k * interval; // + = 늦게 침
        if (Math.Abs(err) > RejectWindow)
        {
            ShowTap("빗나감", K.TextSub);
            return;
        }
        errors.Add(err);
        int ms = (int)Math.Round(err * 1000.0);
        ShowTap((ms > 0 ? "+" : "") + ms, ms > 0 ? K.SlowColor : (ms < 0 ? K.FastColor : K.TextMain));
        UpdateStats();
    }

    private void ShowTap(string text, Color color)
    {
        tapText.text = text;
        tapText.color = color;
        if (mode == Mode.Sound) { tapText.rectTransform.anchoredPosition = new Vector2(0f, -110f); tapText.alignment = TextAlignmentOptions.Center; }
        else { tapText.rectTransform.anchoredPosition = new Vector2(330f, -20f); tapText.alignment = TextAlignmentOptions.Left; }
    }

    private void UpdateStats()
    {
        if (errors.Count == 0) { statsText.text = "0 / " + RequiredTaps; return; }
        statsText.text = errors.Count + " / " + RequiredTaps + "      평균 " + K.Ms(errors.Average());
    }

    private void Finish()
    {
        state = State.Done;
        StopAudio();
        HideNotes();
        lane.gameObject.SetActive(false);

        // 트림 평균: 정렬 후 위아래 20%씩 버림
        List<double> sorted = errors.OrderBy(e => e).ToList();
        int trim = (int)(sorted.Count * 0.2);
        List<double> core = sorted.Skip(trim).Take(sorted.Count - trim * 2).ToList();
        result = core.Average();
        double mean = errors.Average();
        spread = Math.Sqrt(errors.Sum(e => (e - mean) * (e - mean)) / errors.Count);

        int spreadMs = (int)Math.Round(spread * 1000.0);
        string quality = spreadMs <= 15 ? "안정적" : (spreadMs <= 30 ? "보통" : "불안정 — 다시 측정을 권장");

        bigText.text = K.Ms(result);
        bigText.color = result > 0.0005 ? K.SlowColor : (result < -0.0005 ? K.FastColor : K.TextMain);
        tapText.text = "";
        statsText.text = "흔들림 ±" + spreadMs + " ms  ·  " + quality;
        infoText.text = "평균적으로 " + K.Ms(result) +
                        (result >= 0 ? " 늦게 눌렀습니다.\n" : " 빠르게 눌렀습니다.\n") +
                        owner.DescribeCalibration(mode, result);

        btnApply.gameObject.SetActive(true);
        btnRetry.gameObject.SetActive(true);
        retryText.text = "다시";
        btnBack.gameObject.SetActive(true);
        owner.SelectNextFrame(btnApply.gameObject);
    }

    private void Apply()
    {
        if (state != State.Done) return;
        owner.ApplyCalibration(mode, result);
        Cancel();
    }

    // =====================================================================
    //  화면 기준 측정: 떨어지는 막대
    // =====================================================================
    private void UpdateNotes()
    {
        float laneH = lane.rect.height;
        int firstK = Math.Max(0, (int)Math.Floor((clock - 0.3) / interval));
        for (int i = 0; i < notes.Count; i++)
        {
            int k = firstK + i;
            double t = k * interval;
            float y = JudgeLineY + (float)(t - clock) * NoteSpeed;
            bool visible = y > -20f && y < laneH + 20f;
            Image n = notes[i];
            n.enabled = visible;
            if (!visible) continue;
            n.rectTransform.anchoredPosition = new Vector2(0f, y);
            n.color = k < CountInBeats ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
        }
    }

    private void HideNotes()
    {
        foreach (var n in notes) n.enabled = false;
    }

    // =====================================================================
    //  소리
    // =====================================================================
    private void StopAudio()
    {
        if (clickSource != null) clickSource.Stop(); // 예약만 된 상태(아직 소리 전)도 취소
        audioStarted = false;
    }

    // 4박 길이의 반복 클립 (첫 박은 높은 음). 출력 샘플레이트로 만들어 리샘플링 없이 정확한 간격으로 반복된다.
    private static AudioClip CreateClickClip(out double beatInterval)
    {
        int sr = AudioSettings.outputSampleRate;
        if (sr <= 0) sr = 48000;
        int beat = Mathf.RoundToInt((float)(TargetInterval * sr));
        beatInterval = beat / (double)sr;

        float[] data = new float[beat * 4];
        int len = Mathf.Min(beat, (int)(sr * 0.06f));
        for (int b = 0; b < 4; b++)
        {
            float freq = b == 0 ? 1760f : 1320f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)sr;
                data[b * beat + i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 55f) * 0.85f;
            }
        }
        AudioClip clip = AudioClip.Create("CalibrationClick", data.Length, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }
}