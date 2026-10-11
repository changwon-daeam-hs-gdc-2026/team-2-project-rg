using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

// 결과창(4_Result) 씬을 관리한다.
//  - CoreManager.LastResult 를 읽어서 곡명 / 난이도 / 자켓 / 점수 / ACC / 판정 수 / MAX COMBO / 전체 판정 오차를 표시
//  - 입장 시 암막(검정)에서 서서히 밝아지고, ESC 를 누르면 암막이 덮인 뒤 곡 선택 씬으로 돌아간다.
public class ResultManager : MonoBehaviour
{
    [Header("텍스트")]
    public TextMeshProUGUI songNameText;      // 곡명 (예: Glow Embrace)
    [FormerlySerializedAs("difficultyText")] // 기존 씬에서 난이도 텍스트에 연결해 둔 참조를 그대로 이어받음
    public TextMeshProUGUI composerText;      // 작곡가
    public TextMeshProUGUI scoreText;         // 점수
    public TextMeshProUGUI accuracyText;      // ACC (정확도)
    public TextMeshProUGUI excellentText;     // EXCELLENT 수
    public TextMeshProUGUI greatText;         // GREAT 수
    public TextMeshProUGUI goodText;          // GOOD 수
    public TextMeshProUGUI badText;           // BAD 수
    public TextMeshProUGUI missText;          // MISS 수
    public TextMeshProUGUI maxComboText;      // MAX COMBO
    public TextMeshProUGUI errorText;         // 전체 판정 오차 (FAST = -n, SLOW = +n)

    [Header("이미지")]
    public Image jacketImage;                 // 곡 자켓 (Resources/Sprites/loadimg-large/{곡ID})
    public Image backgroundImage;             // 뒷배경 (Resources/Sprites/blurimg/{곡ID})
    [Range(0f, 1f)] public float backgroundDim = 0.55f; // 배경 밝기 (1 = 원본, 낮을수록 어둡게)

    [Header("선택 요소")]
    public GameObject autoPlayBadge;          // 오토플레이로 플레이했을 때만 켜지는 표시 (없어도 됨)

    [Header("암막 (화면 전환)")]
    public CanvasGroup blackOverlay;          // 화면 전체를 덮는 검정 이미지에 CanvasGroup
    public float fadeInDuration = 0.8f;
    public float fadeOutDuration = 0.6f;

    [Header("연출")]
    public float countUpDuration = 1.2f;      // 점수 / ACC 가 올라가는 시간
    public string scoreFormat = "N0";         // 점수 표기 (예: "N0" → 987,654 / "D7" → 0987654)

    private ResultData result;
    private bool isLeaving = false;
    private Coroutine introRoutine;

    private static readonly Color FastColor = new Color(0.53f, 0.81f, 0.92f); // 게임 내 FAST 색
    private static readonly Color SlowColor = new Color(1f, 0.25f, 0.25f);    // 게임 내 SLOW 색

    void Awake()
    {
        // 게임 씬에서 암전된 채로 넘어오므로, 첫 프레임부터 화면이 검게 보이도록 미리 덮어 둔다.
        if (blackOverlay != null)
        {
            blackOverlay.gameObject.SetActive(true);
            blackOverlay.alpha = 1f;
            blackOverlay.blocksRaycasts = false;
        }
    }

    void Start()
    {
        result = (CoreManager.Instance != null) ? CoreManager.Instance.LastResult : null;
        if (result == null) result = ResultData.CreateSample(); // 결과 씬만 단독 실행했을 때 확인용

        if (blackOverlay != null)
        {
            blackOverlay.gameObject.SetActive(true);
            blackOverlay.transform.SetAsLastSibling();
            blackOverlay.blocksRaycasts = false;
            blackOverlay.alpha = 1f;
        }

        ApplyStaticTexts(result);
        introRoutine = StartCoroutine(IntroRoutine());
    }

    void Update()
    {
        if (isLeaving) return;

        var kb = Keyboard.current;
        if (kb != null && kb[Key.Escape].wasPressedThisFrame)
        {
            StartCoroutine(LeaveRoutine());
        }
    }

    // 숫자가 올라가는 연출이 필요 없는 항목들 (처음부터 최종 값 표시)
    private void ApplyStaticTexts(ResultData r)
    {
        if (songNameText != null) songNameText.text = r.SongName + r.SubName;

        if (composerText != null) composerText.text = r.Composer;

        if (jacketImage != null)
        {
            Sprite jacket = Resources.Load<Sprite>($"Sprites/loadimg-large/{r.SongId}");
            if (jacket != null) jacketImage.sprite = jacket;
            jacketImage.preserveAspect = true;
        }

        ApplyBackground(r.SongId);

        if (excellentText != null) excellentText.text = r.Excellent.ToString();
        if (greatText != null) greatText.text = r.Great.ToString();
        if (goodText != null) goodText.text = r.Good.ToString();
        if (badText != null) badText.text = r.Bad.ToString();
        if (missText != null) missText.text = r.Miss.ToString();
        if (maxComboText != null) maxComboText.text = r.MaxCombo.ToString();

        if (errorText != null)
        {
            // 전체 판정 오차: FAST 는 -, SLOW 는 +
            double e = r.AverageErrorMs;
            errorText.text = e.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ms";
            errorText.color = e < -0.05 ? FastColor : (e > 0.05 ? SlowColor : Color.white);
        }

        if (autoPlayBadge != null) autoPlayBadge.SetActive(r.IsAutoPlay);

        if (scoreText != null) scoreText.text = 0.ToString(scoreFormat, CultureInfo.InvariantCulture);
        if (accuracyText != null) accuracyText.text = "0.00%";
    }

    // blurimg 를 화면 전체를 채우도록(비율 유지, 남는 부분은 잘림) 깔고 살짝 어둡게 한다.
    private void ApplyBackground(int songId)
    {
        if (backgroundImage == null) return;

        Sprite bg = Resources.Load<Sprite>($"Sprites/blurimg/{songId}");
        if (bg == null) return; // 이미지가 없으면 씬에 설정해 둔 기본 배경을 유지

        backgroundImage.sprite = bg;
        backgroundImage.type = Image.Type.Simple;
        backgroundImage.preserveAspect = false;
        float d = Mathf.Clamp01(backgroundDim);
        backgroundImage.color = new Color(d, d, d, 1f);
        backgroundImage.raycastTarget = false;
        backgroundImage.transform.SetAsFirstSibling(); // 모든 UI 의 맨 뒤

        AspectRatioFitter fitter = backgroundImage.GetComponent<AspectRatioFitter>();
        if (fitter == null) fitter = backgroundImage.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = bg.rect.width / Mathf.Max(1f, bg.rect.height);
    }

    private IEnumerator IntroRoutine()
    {
        // 암막 걷기
        if (blackOverlay != null)
        {
            float t = 0f;
            float dur = Mathf.Max(0.01f, fadeInDuration);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                blackOverlay.alpha = 1f - Mathf.Clamp01(t / dur);
                yield return null;
            }
            blackOverlay.alpha = 0f;
        }

        // 점수 / ACC 카운트 업
        float c = 0f;
        float cd = Mathf.Max(0.01f, countUpDuration);
        while (c < cd)
        {
            c += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(c / cd);
            float eased = 1f - Mathf.Pow(1f - p, 3f);

            if (scoreText != null) scoreText.text = ((int)(result.Score * eased)).ToString(scoreFormat, CultureInfo.InvariantCulture);
            if (accuracyText != null) accuracyText.text = (result.Accuracy * eased).ToString("F2", CultureInfo.InvariantCulture) + "%";
            yield return null;
        }

        if (scoreText != null) scoreText.text = result.Score.ToString(scoreFormat, CultureInfo.InvariantCulture);
        if (accuracyText != null) accuracyText.text = result.Accuracy.ToString("F2", CultureInfo.InvariantCulture) + "%";
        introRoutine = null;
    }

    private IEnumerator LeaveRoutine()
    {
        isLeaving = true;
        if (introRoutine != null) { StopCoroutine(introRoutine); introRoutine = null; }

        // 연출 중에 나가도 최종 값이 보이도록 고정
        if (scoreText != null) scoreText.text = result.Score.ToString(scoreFormat, CultureInfo.InvariantCulture);
        if (accuracyText != null) accuracyText.text = result.Accuracy.ToString("F2", CultureInfo.InvariantCulture) + "%";

        if (blackOverlay != null)
        {
            blackOverlay.gameObject.SetActive(true);
            blackOverlay.transform.SetAsLastSibling();
            blackOverlay.blocksRaycasts = true;

            float start = blackOverlay.alpha;
            float t = 0f;
            float dur = Mathf.Max(0.01f, fadeOutDuration);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                blackOverlay.alpha = Mathf.Lerp(start, 1f, t / dur);
                yield return null;
            }
            blackOverlay.alpha = 1f;
        }

        if (CoreManager.Instance != null) CoreManager.Instance.LoadSongSelectScene();
        else Debug.Log("[Result] CoreManager 가 없어 곡 선택 씬으로 돌아가지 못했습니다. (결과 씬 단독 실행 중)");
    }
}