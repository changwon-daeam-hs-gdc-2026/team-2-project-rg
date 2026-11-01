using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

// ============================================================
// 키를 '실제로 누른 시각' 기록 (게임 판정 · 오프셋 측정 공통)
//
//  [문제] 예전에는 Update() 에서 isPressed 를 확인한 '그 프레임의 시각'으로 판정했다.
//   → 키는 이전 프레임과 이번 프레임 사이 어딘가에서 눌렸는데 항상 '프레임 끝'으로 처리되어
//     평균 반 프레임(60fps ≈ 8ms, 30fps ≈ 17ms) 늦게 판정되고, FPS 가 흔들리면 그만큼 오프셋도 흔들렸다.
//  [해결] Input System 이 키 이벤트마다 붙여 주는 시각(InputEvent.time)을 기록해 두고,
//   판정할 때 "그 키가 눌린 지 몇 초 지났는가(PressAge)"만큼 게임 시각을 되돌린다.
//
//  - 씬에 배치할 필요 없음 (게임 시작 시 자동으로 이벤트를 구독)
//  - 키 상태 판단(눌림/뗌)은 지금처럼 Update 의 isPressed 로 하고, 이 클래스는 '시각'만 알려 준다.
// ============================================================
public static class InputTiming
{
    // 너무 오래된 기록은 쓰지 않음 (포커스 이동 · 큰 렉 등)
    public const double MaxAge = 0.1;

    private static readonly Dictionary<Key, double> pressTime = new Dictionary<Key, double>();
    private static readonly Dictionary<Key, double> releaseTime = new Dictionary<Key, double>();
    private static readonly HashSet<Key> isDown = new HashSet<Key>();
    private static readonly HashSet<Key> watched = new HashSet<Key>();
    private static Key[] watchedArray = new Key[0];
    private static bool hooked;
    private static System.Action<InputEventPtr, InputDevice> handler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        pressTime.Clear(); releaseTime.Clear(); isDown.Clear();
        if (handler == null) handler = OnEvent;
        // (도메인 리로드를 끈 에디터 설정에서도 두 번 붙지 않도록 뗐다가 다시 붙임)
        InputSystem.onEvent -= handler;
        InputSystem.onEvent += handler;
        hooked = true;
    }

    // 시각을 기록할 키 등록 (레인 키 · SPACE 등). 여러 번 불러도 됨
    public static void Watch(IEnumerable<Key> keys)
    {
        if (!hooked) Init();
        bool changed = false;
        foreach (Key k in keys)
        {
            if (k == Key.None) continue;
            if (watched.Add(k)) changed = true;
        }
        if (changed)
        {
            watchedArray = new Key[watched.Count];
            watched.CopyTo(watchedArray);
        }
    }

    private static void OnEvent(InputEventPtr ev, InputDevice device)
    {
        Keyboard kb = device as Keyboard;
        if (kb == null || watchedArray.Length == 0) return;
        if (!ev.IsA<StateEvent>() && !ev.IsA<DeltaStateEvent>()) return;

        double t = ev.time;
        for (int i = 0; i < watchedArray.Length; i++)
        {
            Key k = watchedArray[i];
            KeyControl c = kb[k];
            if (c == null) continue;
            float v;
            if (!c.ReadValueFromEvent(ev, out v)) continue; // 이 이벤트에 이 키 정보가 없음
            bool down = v >= 0.5f;
            if (down == isDown.Contains(k)) continue;
            if (down) { isDown.Add(k); pressTime[k] = t; }
            else { isDown.Remove(k); releaseTime[k] = t; }
        }
    }

    // 지금(입력 시계 기준)으로부터 그 키가 눌린 지 몇 초 지났는지. 기록이 없거나 오래됐으면 0
    public static double PressAge(Key k) { return Age(pressTime, k); }
    public static double ReleaseAge(Key k) { return Age(releaseTime, k); }

    private static double Age(Dictionary<Key, double> table, Key k)
    {
        double t;
        if (!table.TryGetValue(k, out t)) return 0.0;
        double age = InputState.currentTime - t;
        if (age < 0.0 || age > MaxAge) return 0.0;
        return age;
    }
}