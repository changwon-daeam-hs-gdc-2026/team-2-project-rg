using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
// 화면 암막 (씬을 넘어가도 남음)
//  게임 → 결과창: 게임 화면에서 암막이 서서히 덮이고(Cover) → 씬이 바뀐 뒤 결과창에서 서서히 걷힘(RevealWhenLoaded)
//  화면 맨 위(Overlay 캔버스 900)에 검은 판 하나. 설정창 · FPS 표시만 그 위
// ============================================================
public class ScreenBlackout : MonoBehaviour
{
    private CanvasGroup group;
    private Coroutine running;

    public float Alpha { get { return group != null ? group.alpha : 0f; } }

    public static ScreenBlackout Create()
    {
        GameObject go = new GameObject("ScreenBlackout", typeof(RectTransform));
        go.layer = 5;
        DontDestroyOnLoad(go);
        Canvas cv = go.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 900;
        ScreenBlackout b = go.AddComponent<ScreenBlackout>();
        b.group = go.AddComponent<CanvasGroup>();
        b.group.alpha = 0f;
        b.group.blocksRaycasts = true;
        Image img = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(go.transform, false);
        img.color = Color.black;
        RectTransform r = img.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        return b;
    }

    // 0 → 1 (부드럽게 시작 · 끝). 다 덮이면 끝나는 코루틴
    public IEnumerator Cover(float duration)
    {
        yield return Fade(0f, 1f, duration);
    }

    // sceneName 씬이 열리면 잠깐 기다렸다가 1 → 0 으로 걷히고 사라짐 (못 찾으면 10초 뒤 그냥 걷힘)
    public void RevealWhenLoaded(string sceneName, float duration, float delay = 0.15f)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(RevealRoutine(sceneName, duration, delay));
    }

    private IEnumerator RevealRoutine(string sceneName, float duration, float delay)
    {
        float wait = 0f;
        while (wait < 10f)
        {
            Scene s = SceneManager.GetSceneByName(sceneName);
            if (s.IsValid() && s.isLoaded) break;
            wait += Time.unscaledDeltaTime;
            yield return null;
        }
        // 결과창이 첫 화면을 그릴 때까지 조금 기다림 (그 사이 이전 씬이 지워짐)
        yield return null;
        yield return new WaitForSecondsRealtime(delay);
        group.blocksRaycasts = false;
        yield return Fade(1f, 0f, duration);
        Destroy(gameObject);
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        float dur = Mathf.Max(0.05f, duration);
        float t = 0f;
        group.alpha = from;
        while (t < dur)
        {
            // 한 프레임이 길어도(로딩 끊김 등) 한 번에 확 바뀌지 않도록 한 프레임에 최대 1/30 초만 진행
            t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float u = Mathf.Clamp01(t / dur);
            group.alpha = Mathf.Lerp(from, to, u * u * (3f - 2f * u));
            yield return null;
        }
        group.alpha = to;
    }
}