using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 효과음 (UI · 연출)
//  - 소리 파일 없이 코드로 합성 (44100Hz · 모노). 처음 쓸 때 한 번 만들어 둠
//  - 바꾸고 싶으면 Resources/Audio/Sfx/<이름> 에 소리 파일을 넣으면 그 파일을 대신 씀
//      예) Resources/Audio/Sfx/Move.wav  →  곡 넘김 소리가 그 파일로 바뀜 (이름은 아래 Id 와 같게)
//  - 볼륨: 설정 > 소리 > 효과음 볼륨 (SettingsData.SfxVolume) × 전체 볼륨(AudioListener)
//  - 씬이 바뀌어도 끊기지 않음 (곡 진입 소리가 게임 씬으로 넘어가는 동안에도 이어짐), 일시정지와 관계없이 재생
//
//  쓰는 곳
//   Move        곡 넘김 (← →)                    Difficulty  난이도 바꾸기 (↑ ↓ · 클릭)
//   ListSwitch  SPECIAL 목록 ↔ 기존 목록 (TAB)   ToggleOn / ToggleOff  MV · AUTO PLAY 켜고 끄기
//   Tick        배속 조절 (F1 · F2 · 클릭)       Denied      잠긴 곡 · 잠긴 난이도
//   Open / Back 창 열기 · 닫기 (설정 · 확인 창 · 일시정지)
//   Confirm     선택 (일시정지 메뉴 · 에디터 · GAME SETTING 들어가기)
//   SongStart   곡 진입 (플레이 시작)
//   Clear · FullCombo · AllPerfect · AllJust · Failed   곡 끝 연출 (ClearEffect)
//   NewBest     결과창 최고 기록 갱신
// ============================================================
public static class Sfx
{
    public enum Id
    {
        Move, Difficulty, ListSwitch, ToggleOn, ToggleOff, Tick, Denied,
        Open, Back, Confirm, SongStart,
        Clear, FullCombo, AllPerfect, AllJust, Failed, NewBest
    }

    // 소리별 기본 음량 (효과음 볼륨에 곱함)
    private static float BaseVolume(Id id)
    {
        switch (id)
        {
            case Id.Move: return 0.55f;
            case Id.Tick: return 0.35f;
            case Id.Difficulty: return 0.5f;
            case Id.ToggleOn: case Id.ToggleOff: return 0.5f;
            case Id.Denied: return 0.5f;
            case Id.Open: case Id.Back: return 0.45f;
            default: return 0.7f;
        }
    }

    private const int Rate = 44100;
    private static readonly Dictionary<Id, AudioClip> clips = new Dictionary<Id, AudioClip>();
    private static readonly Dictionary<Id, float> lastPlayed = new Dictionary<Id, float>();
    private static AudioSource source;

    public static float Volume { get { return SettingsStore.Current != null ? Mathf.Clamp01(SettingsStore.Current.SfxVolume) : 1f; } }

    public static void Play(Id id, float volume = 1f)
    {
        float v = Volume * BaseVolume(id) * volume;
        if (v <= 0.0001f) return;
        // 같은 소리가 한 프레임에 겹쳐 쌓이지 않게 (키 반복 · 클릭 중복)
        float now = Time.unscaledTime, last;
        if (lastPlayed.TryGetValue(id, out last) && now - last < 0.025f) return;
        lastPlayed[id] = now;
        AudioClip c = Clip(id);
        AudioSource s = Source();
        if (c != null && s != null) s.PlayOneShot(c, v);
    }

    public static void Preload()
    {
        foreach (Id id in Enum.GetValues(typeof(Id))) Clip(id);
    }

    private static AudioSource Source()
    {
        if (source != null) return source;
        GameObject go = new GameObject("Sfx");
        UnityEngine.Object.DontDestroyOnLoad(go);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        source.priority = 32;
        return source;
    }

    private static AudioClip Clip(Id id)
    {
        AudioClip c;
        if (clips.TryGetValue(id, out c) && c != null) return c;
        c = Resources.Load<AudioClip>("Audio/Sfx/" + id);
        if (c != null && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
        if (c == null) c = Build(id);
        clips[id] = c;
        return c;
    }

    // =====================================================================
    //  합성 — 리듬게임 UI 느낌 (멜로디 · 팡파레 없이 타격음 · 바람 소리 · 임팩트 · 반짝이는 잔향)
    // =====================================================================
    private static AudioClip Build(Id id)
    {
        float[] d;
        switch (id)
        {
            case Id.Move:
                // 탁 — 짧고 단단한 나무 타격음 (연속으로 넘기면 탁탁탁)
                d = New(0.06f);
                Click(d, 0f, 0.9f, 2600f, 1.6f, 0.006f, 11);
                Click(d, 0f, 0.35f, 5200f, 2.5f, 0.003f, 12);
                Body(d, 0f, 0.55f, 900f, 380f, 0.018f);
                break;
            case Id.Tick:
                // 더 작고 가벼운 톡
                d = New(0.04f);
                Click(d, 0f, 0.7f, 4200f, 2f, 0.004f, 21);
                Body(d, 0f, 0.25f, 1500f, 800f, 0.010f);
                break;
            case Id.Difficulty:
                // 톡-탁 (두 번, 두 번째가 조금 낮고 단단함)
                d = New(0.12f);
                Click(d, 0f, 0.6f, 3600f, 2f, 0.005f, 31);
                Body(d, 0f, 0.3f, 1300f, 700f, 0.012f);
                Click(d, 0.045f, 0.9f, 2200f, 1.6f, 0.007f, 32);
                Body(d, 0.045f, 0.55f, 700f, 260f, 0.025f);
                break;
            case Id.ListSwitch:
                // 휙 — 바람이 스쳐 지나가고 끝에 낮은 툭
                d = New(0.4f);
                NoiseSweep(d, 0f, 0.24f, 0.45f, 600f, 6500f);
                Body(d, 0.2f, 0.5f, 220f, 70f, 0.08f);
                Click(d, 0.2f, 0.4f, 1800f, 1.4f, 0.006f, 41);
                Reverb(d, 0.18f);
                break;
            case Id.ToggleOn:
                // 딸깍 + 살짝 올라가는 짧은 빛 소리
                d = New(0.14f);
                Click(d, 0f, 0.8f, 3000f, 1.8f, 0.005f, 51);
                Tone(d, 0.008f, 0.07f, 1800f, 2600f, 0.18f, 0.003f, 0.04f, Wave.Sine);
                break;
            case Id.ToggleOff:
                d = New(0.14f);
                Click(d, 0f, 0.8f, 2200f, 1.8f, 0.006f, 52);
                Tone(d, 0.008f, 0.07f, 1600f, 900f, 0.16f, 0.003f, 0.04f, Wave.Sine);
                break;
            case Id.Denied:
                // 둔하게 막히는 소리 (낮은 퉁 두 번)
                d = New(0.2f);
                Body(d, 0f, 0.7f, 180f, 120f, 0.05f);
                Click(d, 0f, 0.35f, 900f, 1.2f, 0.008f, 61);
                Body(d, 0.08f, 0.6f, 160f, 105f, 0.06f);
                Click(d, 0.08f, 0.3f, 800f, 1.2f, 0.008f, 62);
                Lowpass(d, 2500f);
                break;
            case Id.Open:
                // 부드럽게 열리는 바람 소리 + 가벼운 톡
                d = New(0.32f);
                NoiseSweep(d, 0f, 0.2f, 0.3f, 1200f, 8000f);
                Click(d, 0.12f, 0.35f, 3400f, 2f, 0.004f, 71);
                Reverb(d, 0.2f);
                break;
            case Id.Back:
                // 닫히는 바람 소리 (위에서 아래로) + 낮은 툭
                d = New(0.28f);
                NoiseSweep(d, 0f, 0.16f, 0.28f, 7000f, 900f);
                Body(d, 0.1f, 0.35f, 300f, 120f, 0.04f);
                Reverb(d, 0.15f);
                break;
            case Id.Confirm:
                // 탁 + 묵직한 저음 + 짧은 반짝임
                d = New(0.45f);
                Click(d, 0f, 0.9f, 2800f, 1.6f, 0.006f, 81);
                Body(d, 0f, 0.6f, 260f, 80f, 0.09f);
                Shimmer(d, 0.01f, 0.3f, 3520f, 4, 0.06f, 82);
                Reverb(d, 0.2f);
                break;
            case Id.SongStart:
                // 곡 진입: 점점 차오르는 상승음 → 쾅 (임팩트) → 넓게 퍼지는 잔향
                d = New(2.0f);
                Riser(d, 0f, 0.55f, 0.35f, 400f, 10000f);
                Tone(d, 0f, 0.55f, 120f, 480f, 0.12f, 0.4f, 0.5f, Wave.Sine);
                Impact(d, 0.55f, 1f, 0.9f, 91);
                Shimmer(d, 0.56f, 1.3f, 2637f, 6, 0.05f, 92);
                Reverb(d, 0.3f);
                break;
            case Id.Clear:
                // TRACK COMPLETE: 부드러운 임팩트 + 은은한 잔향
                d = New(1.6f);
                Impact(d, 0f, 0.55f, 0.6f, 101);
                Shimmer(d, 0.01f, 1.2f, 1760f, 5, 0.05f, 102);
                Reverb(d, 0.3f);
                break;
            case Id.FullCombo:
                // FULL COMBO: 짧은 상승 → 큰 임팩트 + 유리처럼 반짝이는 잔향
                d = New(2.4f);
                Riser(d, 0f, 0.18f, 0.3f, 1500f, 12000f);
                Impact(d, 0.18f, 1f, 1.1f, 111);
                Shimmer(d, 0.19f, 1.8f, 2093f, 8, 0.07f, 112);
                Reverb(d, 0.35f);
                break;
            case Id.AllPerfect:
                // ALL PERFECT: FULL COMBO 보다 더 넓고 길게, 반짝임이 더 높고 많음
                d = New(3.0f);
                Riser(d, 0f, 0.24f, 0.35f, 1200f, 14000f);
                Impact(d, 0.24f, 1f, 1.3f, 121);
                Shimmer(d, 0.25f, 2.4f, 2093f, 10, 0.07f, 122);
                Shimmer(d, 0.25f, 2.0f, 4186f, 8, 0.045f, 123);
                Reverb(d, 0.4f);
                break;
            case Id.AllJust:
                // ALL JUST: ALL PERFECT + 아주 높고 맑게 울리는 고리 소리 두 번
                d = New(3.3f);
                Riser(d, 0f, 0.28f, 0.38f, 1000f, 15000f);
                Impact(d, 0.28f, 1f, 1.4f, 131);
                Shimmer(d, 0.29f, 2.6f, 2093f, 10, 0.07f, 132);
                Shimmer(d, 0.29f, 2.2f, 4186f, 10, 0.05f, 133);
                Ring(d, 0.29f, 2.4f, 5274f, 0.08f);
                Ring(d, 0.62f, 2.0f, 6272f, 0.06f);
                Reverb(d, 0.42f);
                break;
            case Id.Failed:
                // TRACK CRASH: 둔한 충격 + 전원이 꺼지듯 아래로 가라앉는 소리
                d = New(1.6f);
                Impact(d, 0f, 0.8f, 0.7f, 141);
                Tone(d, 0f, 1.2f, 320f, 35f, 0.35f, 0.005f, 1.1f, Wave.Sine);
                Tone(d, 0f, 1.2f, 331f, 36f, 0.2f, 0.005f, 1.1f, Wave.Triangle);
                Lowpass(d, 1600f);
                Reverb(d, 0.25f);
                break;
            case Id.NewBest:
                // 반짝 — 위로 스치는 바람 + 맑은 잔향
                d = New(1.2f);
                NoiseSweep(d, 0f, 0.25f, 0.25f, 3000f, 14000f);
                Click(d, 0.18f, 0.5f, 5000f, 2.5f, 0.003f, 151);
                Shimmer(d, 0.18f, 0.9f, 3136f, 6, 0.07f, 152);
                Reverb(d, 0.3f);
                break;
            default:
                d = New(0.05f);
                break;
        }
        Normalize(d, 0.9f);
        AudioClip c = AudioClip.Create("Sfx_" + id, d.Length, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    private enum Wave { Sine, Triangle }

    private static float[] New(float seconds) { return new float[Mathf.Max(1, Mathf.CeilToInt(seconds * Rate))]; }

    private static float Osc(Wave w, double phase)
    {
        double p = phase - Math.Floor(phase);
        if (w == Wave.Triangle) return (float)(p < 0.5 ? 4.0 * p - 1.0 : 3.0 - 4.0 * p);
        return (float)Math.Sin(2.0 * Math.PI * p);
    }

    // 음 하나: start 초부터 dur 초, f0 → f1 로 미끄러짐, attack 뒤 decay 초에 걸쳐 지수적으로 줄어듦
    private static void Tone(float[] d, float start, float dur, float f0, float f1, float amp, float attack, float decay, Wave w)
    {
        int s0 = (int)(start * Rate), n = (int)(dur * Rate);
        double phase = 0.0;
        for (int i = 0; i < n && s0 + i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float k = n > 1 ? (float)i / (n - 1) : 0f;
            float f = f0 * Mathf.Pow(f1 / f0, k);
            phase += f / Rate;
            float env = t < attack ? t / Mathf.Max(1e-4f, attack) : Mathf.Exp(-(t - attack) / Mathf.Max(1e-4f, decay) * 3f);
            float tail = Mathf.Clamp01((n - i) / (Rate * 0.004f));   // 끝 4ms 부드럽게 (딸깍 방지)
            d[s0 + i] += Osc(w, phase) * amp * env * tail;
        }
    }

    // 타격음의 '몸통': 짧게 툭 떨어지는 사인 (높은 음에서 낮은 음으로 아주 빨리)
    private static void Body(float[] d, float start, float amp, float f0, float f1, float decay)
    {
        int s0 = (int)(start * Rate), n = (int)(decay * 6f * Rate);
        double phase = 0.0;
        for (int i = 0; i < n && s0 + i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float f = f1 + (f0 - f1) * Mathf.Exp(-t / (decay * 0.5f));
            phase += f / Rate;
            float env = Mathf.Min(1f, t / 0.0006f) * Mathf.Exp(-t / decay);
            d[s0 + i] += (float)Math.Sin(2.0 * Math.PI * phase) * amp * env;
        }
    }

    // 딸깍 · 탁: 대역 통과한 잡음을 아주 빨리 줄임 (freq = 소리의 높이, q 클수록 맑고 좁은 소리)
    private static void Click(float[] d, float start, float amp, float freq, float q, float decay, int seed)
    {
        var rng = new System.Random(seed);
        int s0 = (int)(start * Rate), n = (int)(decay * 7f * Rate);
        Biquad bp = Biquad.BandPass(freq, q);
        for (int i = 0; i < n && s0 + i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float x = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t / decay);
            d[s0 + i] += bp.Run(x) * amp * 3f;
        }
    }

    // 쾅: 깊은 저음 + 갈라지는 충격음 + 짧은 먼지 같은 잡음
    private static void Impact(float[] d, float start, float amp, float length, int seed)
    {
        Body(d, start, amp * 0.9f, 140f, 42f, length * 0.35f);
        Click(d, start, amp * 0.7f, 2400f, 0.9f, 0.012f, seed);
        Click(d, start, amp * 0.35f, 7000f, 1.2f, 0.02f, seed + 1);
        var rng = new System.Random(seed + 2);
        int s0 = (int)(start * Rate), n = (int)(length * 0.5f * Rate);
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * 900f / Rate), y = 0f;
        for (int i = 0; i < n && s0 + i < d.Length; i++)
        {
            float t = (float)i / Rate;
            y += a * ((float)(rng.NextDouble() * 2.0 - 1.0) - y);
            d[s0 + i] += y * amp * 0.5f * Mathf.Exp(-t / (length * 0.12f));
        }
    }

    // 상승음: 잡음이 점점 커지고 밝아지다가 끝에서 뚝 (바로 뒤에 임팩트)
    private static void Riser(float[] d, float start, float dur, float amp, float c0, float c1)
    {
        var rng = new System.Random(500 + (int)(start * 1000));
        int s0 = (int)(start * Rate), n = (int)(dur * Rate);
        float y = 0f, y2 = 0f;
        for (int i = 0; i < n && s0 + i < d.Length; i++)
        {
            float t = (float)i / n;
            float cut = c0 * Mathf.Pow(c1 / c0, t);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cut / Rate);
            y += a * ((float)(rng.NextDouble() * 2.0 - 1.0) - y);
            y2 += a * (y - y2);
            float env = t * t * Mathf.Clamp01((1f - t) * 40f + 0.0f);
            d[s0 + i] += (y - y2 * 0.4f) * amp * env * 2f;
        }
    }

    // 바람 소리: 잡음의 밝기가 c0 → c1 로 바뀌며 커졌다가 사라짐
    private static void NoiseSweep(float[] d, float start, float dur, float amp, float c0, float c1)
    {
        var rng = new System.Random(77 + (int)(start * 1000) + (int)c0);
        int s0 = (int)(start * Rate), n = (int)(dur * Rate);
        float y = 0f, y2 = 0f;
        for (int i = 0; i < n && s0 + i < d.Length; i++)
        {
            float t = (float)i / n;
            float cut = c0 * Mathf.Pow(c1 / c0, t);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cut / Rate);
            y += a * ((float)(rng.NextDouble() * 2.0 - 1.0) - y);
            y2 += a * (y - y2);
            float env = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f));
            d[s0 + i] += (y - y2 * 0.5f) * amp * env;
        }
    }

    // 반짝이는 잔향: 높은 음 여러 개를 살짝 어긋나게 겹쳐 '쉬이잉' 하고 퍼지는 빛 소리 (멜로디 없음)
    private static void Shimmer(float[] d, float start, float dur, float baseF, int count, float amp, int seed)
    {
        var rng = new System.Random(seed);
        float[] ratio = { 1f, 1.5f, 2f, 3f };
        for (int k = 0; k < count; k++)
        {
            float f = baseF * ratio[k % ratio.Length] * (1f + ((float)rng.NextDouble() - 0.5f) * 0.012f);
            float trem = 3f + (float)rng.NextDouble() * 5f;
            double phase = rng.NextDouble();
            float a = amp * (k < ratio.Length ? 1f : 0.6f) / (1f + (k % ratio.Length) * 0.5f);
            int s0 = (int)(start * Rate), n = (int)(dur * Rate);
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = (float)i / Rate;
                phase += f / Rate;
                float env = Mathf.Min(1f, t / 0.04f) * Mathf.Exp(-t / (dur * 0.35f));
                float tr = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * trem * t + k);
                d[s0 + i] += (float)Math.Sin(2.0 * Math.PI * phase) * a * env * tr;
            }
        }
    }

    // 맑게 울리는 고리 소리 (비정수배 배음, 은은하게)
    private static void Ring(float[] d, float start, float dur, float f, float amp)
    {
        Tone(d, start, dur, f, f, amp, 0.003f, dur * 0.6f, Wave.Sine);
        Tone(d, start, dur * 0.5f, f * 2.76f, f * 2.76f, amp * 0.3f, 0.003f, dur * 0.25f, Wave.Sine);
    }

    private static void Lowpass(float[] d, float cutoff)
    {
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate), y = 0f;
        for (int i = 0; i < d.Length; i++) { y += a * (d[i] - y); d[i] = y; }
    }

    // 간단한 공간감 (Schroeder 리버브: 콤 필터 4개 + 올패스 2개). mix = 젖은 소리 비율
    private static void Reverb(float[] d, float mix)
    {
        int[] combLen = { 1557, 1617, 1491, 1422 };
        const float fb = 0.8f, damp = 0.25f;
        float[] wet = new float[d.Length];
        foreach (int L in combLen)
        {
            float[] buf = new float[L];
            int p = 0; float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float o = buf[p];
                lp = o * (1f - damp) + lp * damp;
                buf[p] = d[i] + lp * fb;
                wet[i] += o * 0.25f;
                if (++p >= L) p = 0;
            }
        }
        foreach (int L in new[] { 225, 556 })
        {
            float[] buf = new float[L];
            int p = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float b = buf[p];
                float x = wet[i];
                buf[p] = x + b * 0.5f;
                wet[i] = b - x;
                if (++p >= L) p = 0;
            }
        }
        // 끝 30ms 동안 잔향을 부드럽게 닫음 (잘리는 소리 방지)
        int fade = (int)(0.03f * Rate);
        for (int i = 0; i < d.Length; i++)
        {
            float f = Mathf.Clamp01((d.Length - i) / (float)fade);
            d[i] = d[i] * (1f - mix * 0.3f) + wet[i] * mix * f;
        }
    }

    private static void Normalize(float[] d, float peak)
    {
        float m = 0f;
        for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
        if (m < 1e-5f) return;
        float k = peak / m;
        for (int i = 0; i < d.Length; i++) d[i] *= k;   // 모든 소리를 같은 최대 크기로 (상대 크기는 BaseVolume)
    }

    // 2차 필터 (RBJ 대역 통과)
    private class Biquad
    {
        private float b0, b1, b2, a1, a2, x1, x2, y1, y2;
        public static Biquad BandPass(float freq, float q)
        {
            float w = 2f * Mathf.PI * Mathf.Min(freq, Rate * 0.45f) / Rate;
            float alpha = Mathf.Sin(w) / (2f * q);
            float a0 = 1f + alpha;
            return new Biquad
            {
                b0 = alpha / a0, b1 = 0f, b2 = -alpha / a0,
                a1 = -2f * Mathf.Cos(w) / a0, a2 = (1f - alpha) / a0
            };
        }
        public float Run(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }
}
