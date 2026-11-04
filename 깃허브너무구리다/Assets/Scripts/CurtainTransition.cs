using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 검은 막 전환 (곡 정보 없이 막만) — 씬에 배치할 필요 없음
//
//  게임에 들어갈 때(SongIntroOverlay)의 검은 막과 같은 모양 · 같은 움직임만 따로 뗀 것.
//  일시정지 → QUIT 으로 곡 선택창에 나갈 때 쓴다 (곡 정보 · 로딩 막대 등 중간 내용은 없음).
//
//  흐름
//   1) 비스듬한 검은 막이 왼쪽 → 오른쪽으로 화면을 덮음
//   2) 다 덮이면 onCovered (곡 선택 씬 불러오기)
//   3) 곡 선택 씬이 준비되면(SongSelectManager.Start → NotifyReady) 막이 오른쪽으로 걷힘
//   * 8초 안에 준비 신호가 없으면 그냥 걷힌다 (안전장치)
// ============================================================
public class CurtainTransition : MonoBehaviour
{
    private static CurtainTransition instance;
    public static bool IsActive { get { return instance != null; } }

    private const float CoverSeconds = 0.45f;     // 덮는 시간 (곡 정보 화면과 같음)
    private const float RevealSeconds = 0.5f;     // 걷히는 시간
    private const float MinCovered = 0.15f;       // 다 덮인 뒤 최소 유지 시간 (너무 번쩍 걷히지 않게)
    private const float SafetyTimeout = 8f;

    private ParallelogramGraphic curtain;
    private Action onCovered;
    private bool ready;

    public static void Play(Action onCovered)
    {
        if (instance != null) Destroy(instance.gameObject);
        GameObject go = new GameObject("CurtainTransition", typeof(RectTransform));
        DontDestroyOnLoad(go);          // 씬이 바뀌어도 막이 남아 있어야 걷히는 것까지 보임
        instance = go.AddComponent<CurtainTransition>();
        instance.onCovered = onCovered;
        instance.Build();
        instance.StartCoroutine(instance.Run());
    }

    // 다음 씬이 준비됐을 때 부른다 (곡 선택창 Start)
    public static void NotifyReady()
    {
        if (instance != null) instance.ready = true;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 800;                    // 곡 정보 화면과 같은 높이 (FPS 표시 1000 아래)
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();  // 전환 중 아래 화면 클릭 차단

        GameObject c = new GameObject("Curtain", typeof(RectTransform));
        c.layer = 5;
        c.transform.SetParent(transform, false);
        curtain = c.AddComponent<ParallelogramGraphic>();
        curtain.color = new Color(0.02f, 0.025f, 0.04f, 1f);   // 곡 정보 화면의 막과 같은 색
        curtain.Skew = 420f;
        curtain.FillAmount = 0f;
        curtain.raycastTarget = true;
        RectTransform r = curtain.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(-440f, 0f); r.offsetMax = new Vector2(440f, 0f);   // 비스듬한 끝이 화면 밖까지 덮도록 넓게
    }

    private IEnumerator Run()
    {
        // 1) 왼쪽 → 오른쪽으로 덮음
        float t = 0f;
        curtain.FillFromRight = false;
        while (t < CoverSeconds)
        {
            t += UiTime.Delta;
            curtain.FillAmount = EaseInOut(Mathf.Clamp01(t / CoverSeconds));
            yield return null;
        }
        curtain.FillAmount = 1f;

        // 2) 덮였으니 다음 씬으로
        if (onCovered != null) { Action a = onCovered; onCovered = null; a(); }

        // 3) 준비될 때까지 덮은 채로 기다림
        float waited = 0f;
        while ((!ready || waited < MinCovered) && waited < SafetyTimeout)
        {
            waited += UiTime.Delta;
            yield return null;
        }
        // 씬이 막 바뀐 프레임은 무거우므로 한 프레임 쉬고 걷음 (걷히는 첫 움직임이 끊기지 않게)
        yield return null;

        // 4) 오른쪽으로 걷힘
        t = 0f;
        curtain.FillFromRight = true;
        while (t < RevealSeconds)
        {
            t += UiTime.Delta;
            curtain.FillAmount = 1f - EaseInOut(Mathf.Clamp01(t / RevealSeconds));
            yield return null;
        }
        Destroy(gameObject);
    }

    private static float EaseInOut(float x) { return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f; }
}