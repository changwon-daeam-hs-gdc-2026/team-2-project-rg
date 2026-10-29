using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// FULL COMBO / ALL PERFECT / FAILED 연출 (씬에 배치할 필요 없음 — 코드로 만들고 끝나면 스스로 사라진다)
//  ClearEffect.Play(부모 Transform, 종류, MAX COMBO, 유지 시간, 끝났을 때 콜백)
//
//  FULL COMBO  : 흰색 번쩍 → 띠가 펼쳐지고 글자가 크게 → 제자리, 반짝이 조각, 빛줄기 (흰색)
//  ALL PERFECT : FULL COMBO 와 같은 움직임 + 글자·선·반짝이가 연한 무지개 그라데이션 (천천히 흐름)
//  CLEAR       : "TRACK COMPLETE" — 번쩍임/반짝이/빛줄기 없이 띠가 열리고 글자가 조용히 떠오르는 약한 연출
//  FAILED      : "TRACK CRASH" — 축하가 아닌 연출. 번쩍임/반짝이/빛줄기 없음. 화면이 어둡게 가라앉고,
//                회색 띠 위로 바랜 빨간 글자가 위에서 무겁게 내려앉은 뒤 자간이 아주 조금씩 벌어짐
//
//  효과음(선택): Resources/Audio/etc/allperfect, fullcombo, complete, failed 가 있으면 재생
// ============================================================
public class ClearEffect : MonoBehaviour
{
    public enum ClearType { None, FullCombo, AllPerfect, Failed, Clear }

    private static readonly Color MainColor = Color.white;
    private static readonly Color FailText = new Color(0.86f, 0.38f, 0.40f, 1f);
    private static readonly Color FailLine = new Color(0.55f, 0.56f, 0.60f, 1f);

    private const float BandHeight = 100f;   // 띠 세로 크기
    private const float TitleSize = 68f;     // 글자 크기 (띠 높이에 맞춰 조절)
    private const float CenterY = 0f;        // 화면 정가운데

    // 폰트: Resources 폴더 기준 경로 (확장자 없이). 예) Assets/Resources/Fonts/MyFont SDF.asset → "Fonts/MyFont SDF"
    //  비워 두거나 못 찾으면 MoveSans-Light → TMP 기본 폰트 순서로 사용
    public static string FontPath = "Fonts/MoveSans-Light";

    // ALL PERFECT 무지개: 채도(낮을수록 연함), 글자 전체에 걸친 색 범위, 흐르는 속도
    private const float RainbowSaturation = 0.32f;
    private const float RainbowSpan = 0.8f;
    private const float RainbowSpeed = 0.12f;

    private ClearType type;
    private float hold;
    private Action onDone;

    private CanvasGroup group;
    private Image flash;
    private RectTransform band;
    private Image lineTop, lineBottom;
    private TextMeshProUGUI title;
    private ParallelogramGraphic shine;
    private readonly List<Spark> sparks = new List<Spark>();

    private struct Spark
    {
        public RectTransform rt;
        public Image img;
        public Vector2 velocity;
        public float spin;
        public float life;
    }

    private bool IsFailed { get { return type == ClearType.Failed; } }
    private bool IsAllPerfect { get { return type == ClearType.AllPerfect; } }
    private bool IsComplete { get { return type == ClearType.Clear; } }
    private bool IsQuiet { get { return IsFailed || IsComplete; } } // 번쩍임·반짝이·빛줄기 없는 연출

    public static ClearEffect Play(Transform parent, ClearType type, int maxCombo, float holdSeconds, Action onDone)
    {
        GameObject go = new GameObject("ClearEffect", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false); // GameManager 아래 → 게임 씬과 함께 정리됨
        ClearEffect fx = go.AddComponent<ClearEffect>();
        fx.type = type;
        fx.hold = Mathf.Max(0.5f, holdSeconds);
        fx.onDone = onDone;
        fx.Build();
        fx.StartCoroutine(fx.IsFailed ? fx.RunFailed() : (fx.IsComplete ? fx.RunComplete() : fx.RunClear()));
        return fx;
    }

    // =====================================================================
    //  화면 만들기
    // =====================================================================
    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // 게임 UI 위, 설정창(500) / FPS(1000) 아래
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // 화면 전체: 클리어는 흰색 번쩍, FAILED 는 어둡게 가라앉음
        flash = NewImage("Flash", transform, IsFailed ? new Color(0f, 0f, 0f, 0f) : new Color(1f, 1f, 1f, 0f));
        Stretch(flash.rectTransform);

        // 가운데 띠 (안쪽은 RectMask2D 로 잘림 → 빛줄기·글자가 띠 밖으로 나가지 않음)
        Image bandImg = NewImage("Band", transform, IsFailed ? new Color(0.04f, 0.04f, 0.05f, 0.78f)
                                                  : (IsComplete ? new Color(0f, 0f, 0f, 0.5f) : new Color(0f, 0f, 0f, 0.6f)));
        band = bandImg.rectTransform;
        band.anchorMin = new Vector2(0f, 0.5f);
        band.anchorMax = new Vector2(1f, 0.5f);
        band.pivot = new Vector2(0.5f, 0.5f);
        band.anchoredPosition = new Vector2(0f, CenterY);
        band.sizeDelta = Vector2.zero;
        band.gameObject.AddComponent<RectMask2D>();

        Color lineColor = IsFailed ? FailLine : (IsComplete ? new Color(1f, 1f, 1f, 0.7f) : MainColor);
        lineTop = NewImage("LineTop", transform, lineColor);
        lineBottom = NewImage("LineBottom", transform, lineColor);
        foreach (var l in new[] { lineTop, lineBottom })
        {
            RectTransform r = l.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(0f, IsQuiet ? 2f : 4f);
        }

        if (!IsQuiet)
        {
            shine = NewObj("Shine", band).AddComponent<ParallelogramGraphic>();
            shine.raycastTarget = false;
            shine.color = new Color(1f, 1f, 1f, 0.35f);
            shine.Skew = 80f;
            RectTransform sr = shine.rectTransform;
            sr.anchorMin = new Vector2(0.5f, 0f);
            sr.anchorMax = new Vector2(0.5f, 1f);
            sr.pivot = new Vector2(0.5f, 0.5f);
            sr.sizeDelta = new Vector2(160f, 0f);
            sr.anchoredPosition = new Vector2(-2000f, 0f);
        }

        TMP_FontAsset font = string.IsNullOrEmpty(FontPath) ? null : Resources.Load<TMP_FontAsset>(FontPath);
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts/MoveSans-Light");
        if (font == null) font = TMP_Settings.defaultFontAsset;

        string label = type == ClearType.AllPerfect ? "ALL PERFECT"
                     : type == ClearType.FullCombo ? "FULL COMBO"
                     : type == ClearType.Clear ? "TRACK COMPLETE"
                     : "TRACK CRASH";
        title = NewText("Title", band, label, TitleSize, font, FontStyles.Bold);
        title.color = IsFailed ? FailText : MainColor;
        title.rectTransform.anchoredPosition = Vector2.zero;
        title.rectTransform.sizeDelta = new Vector2(1800f, BandHeight);

        // 반짝이 조각 (FAILED 에는 없음)
        int count = IsQuiet ? 0 : (IsAllPerfect ? 36 : 24);
        for (int i = 0; i < count; i++)
        {
            Image img = NewImage("Spark" + i, transform, Color.white);
            RectTransform r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            float size = UnityEngine.Random.Range(8f, 18f);
            r.sizeDelta = new Vector2(size, size);
            r.anchoredPosition = new Vector2(0f, CenterY);
            r.localRotation = Quaternion.Euler(0f, 0f, 45f);
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float speed = UnityEngine.Random.Range(380f, 1100f);
            Color c = IsAllPerfect ? Color.HSVToRGB(UnityEngine.Random.value, RainbowSaturation, 1f) : MainColor;
            img.color = new Color(c.r, c.g, c.b, 0f);
            sparks.Add(new Spark
            {
                rt = r, img = img,
                velocity = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed * 0.35f),
                spin = UnityEngine.Random.Range(-360f, 360f),
                life = UnityEngine.Random.Range(0.6f, 1.1f)
            });
        }

        PlaySound(IsFailed ? "Audio/etc/failed" : IsComplete ? "Audio/etc/complete" : (IsAllPerfect ? "Audio/etc/allperfect" : "Audio/etc/fullcombo"));
    }

    // =====================================================================
    //  FULL COMBO / ALL PERFECT
    // =====================================================================
    private IEnumerator RunClear()
    {
        float t = 0f;
        float total = 0.9f + hold + 0.4f;
        float outStart = 0.9f + hold;

        while (t < total)
        {
            t += Time.unscaledDeltaTime;

            // 흰색 번쩍
            flash.color = new Color(1f, 1f, 1f, 0.55f * (1f - Smooth(t / 0.35f)));

            // 띠 펼침 / 접힘
            float open = EaseOut(Mathf.Clamp01(t / 0.25f));
            float close = t > outStart ? Smooth((t - outStart) / 0.4f) : 0f; // 위아래에서 닫힘
            float h = BandHeight * open * (1f - close);
            band.sizeDelta = new Vector2(0f, h);
            UpdateLines(1920f * EaseOut(Mathf.Clamp01((t - 0.05f) / 0.45f)), h); // 선은 가로 길이 그대로, 띠와 함께 가운데로 모임

            // 글자: 크게 → 제자리, 자간 좁혀짐
            float tp = EaseOut(Mathf.Clamp01((t - 0.05f) / 0.4f));
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, tp);
            title.alpha = tp; // 닫힐 때는 띠(마스크)에 위아래로 잘리며 사라짐
            title.characterSpacing = Mathf.Lerp(48f, 6f, tp);

            // ALL PERFECT: 글자·선 무지개
            if (IsAllPerfect)
            {
                ApplyRainbow(t);
                lineTop.color = Rainbow(t * RainbowSpeed, 1f);
                lineBottom.color = Rainbow(t * RainbowSpeed + 0.5f, 1f);
            }

            // 빛줄기 (두 번 훑음)
            float s1 = Mathf.Clamp01((t - 0.3f) / 0.55f);
            float s2 = Mathf.Clamp01((t - 1.6f) / 0.55f);
            float sweep = (t < 1.6f) ? s1 : s2;
            shine.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-1150f, 1150f, EaseInOut(sweep)), 0f);
            shine.enabled = (sweep > 0f && sweep < 1f) && close <= 0f;

            // 반짝이
            float st = t - 0.05f;
            for (int i = 0; i < sparks.Count; i++)
            {
                Spark s = sparks[i];
                if (st < 0f) continue;
                float p = Mathf.Clamp01(st / s.life);
                float travel = 1f - Mathf.Pow(1f - p, 2f);
                s.rt.anchoredPosition = new Vector2(0f, CenterY) + s.velocity * travel * s.life * 0.6f;
                s.rt.localRotation = Quaternion.Euler(0f, 0f, 45f + s.spin * st);
                s.rt.localScale = Vector3.one * (1f - p * 0.6f);
                Color c = s.img.color;
                c.a = p < 0.1f ? p / 0.1f : 1f - p;
                s.img.color = c;
            }

            group.alpha = 1f - Smooth((close - 0.75f) / 0.25f); // 다 닫힌 뒤 남은 선만 흐려짐
            yield return null;
        }

        Finish();
        Destroy(gameObject);
    }

    // =====================================================================
    //  FAILED
    // =====================================================================
    private IEnumerator RunFailed()
    {
        float t = 0f;
        float total = 0.8f + hold + 0.6f;
        float outStart = 0.8f + hold;

        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            float close = t > outStart ? Smooth((t - outStart) / 0.6f) : 0f;

            // 화면이 천천히 어둡게 가라앉음
            flash.color = new Color(0f, 0f, 0f, 0.55f * Smooth(t / 0.6f) * (1f - close * 0.3f));

            // 띠는 천천히 열림
            float open = Smooth(t / 0.45f);
            float h = BandHeight * open * (1f - close);
            band.sizeDelta = new Vector2(0f, h);
            UpdateLines(1920f * Smooth((t - 0.1f) / 0.7f), h); // 선은 가로 길이 그대로, 띠와 함께 가운데로 모임

            // 글자: 위에서 무겁게 내려앉음 → 자간이 아주 조금씩 벌어짐
            float drop = Smooth((t - 0.15f) / 0.45f);
            title.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(26f, 0f, drop));
            title.rectTransform.localScale = Vector3.one;
            title.alpha = drop; // 닫힐 때는 띠(마스크)에 위아래로 잘리며 사라짐
            title.characterSpacing = Mathf.Lerp(0f, 8f, Smooth((t - 0.5f) / (hold + 0.6f)));

            group.alpha = 1f - Smooth((close - 0.75f) / 0.25f); // 다 닫힌 뒤 남은 선만 흐려짐
            yield return null;
        }

        Finish();
        Destroy(gameObject);
    }

    // =====================================================================
    //  공통
    // =====================================================================
    // =====================================================================
    //  TRACK COMPLETE (일반 클리어) — 약한 연출
    // =====================================================================
    private IEnumerator RunComplete()
    {
        float t = 0f;
        float total = 0.6f + hold + 0.4f;
        float outStart = 0.6f + hold;

        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            float close = t > outStart ? Smooth((t - outStart) / 0.4f) : 0f;

            float open = Smooth(t / 0.35f);
            float h = BandHeight * open * (1f - close);
            band.sizeDelta = new Vector2(0f, h);
            UpdateLines(1920f * Smooth((t - 0.05f) / 0.6f), h); // 선은 가로 길이 그대로, 띠와 함께 가운데로 모임

            float tp = Smooth((t - 0.1f) / 0.45f);
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, tp);
            title.alpha = tp; // 닫힐 때는 띠(마스크)에 위아래로 잘리며 사라짐
            title.characterSpacing = Mathf.Lerp(16f, 6f, tp);

            group.alpha = 1f - Smooth((close - 0.75f) / 0.25f); // 다 닫힌 뒤 남은 선만 흐려짐
            yield return null;
        }

        Finish();
        Destroy(gameObject);
    }

    private void UpdateLines(float width, float bandH)
    {
        float th = IsQuiet ? 2f : 4f;
        lineTop.rectTransform.sizeDelta = new Vector2(width, th);
        lineBottom.rectTransform.sizeDelta = new Vector2(width, th);
        lineTop.rectTransform.anchoredPosition = new Vector2(0f, CenterY + bandH * 0.5f + 6f);
        lineBottom.rectTransform.anchoredPosition = new Vector2(0f, CenterY - bandH * 0.5f - 6f);
    }

    // 글자 전체에 걸쳐 왼쪽 → 오른쪽으로 이어지는 연한 무지개 (글자마다가 아니라 단어 전체 기준), 천천히 흐름
    private void ApplyRainbow(float time)
    {
        title.ForceMeshUpdate();
        TMP_TextInfo info = title.textInfo;
        if (info == null || info.characterCount == 0) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ch = info.characterInfo[i];
            if (!ch.isVisible) continue;
            minX = Mathf.Min(minX, ch.bottomLeft.x);
            maxX = Mathf.Max(maxX, ch.topRight.x);
        }
        float width = Mathf.Max(1f, maxX - minX);

        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ch = info.characterInfo[i];
            if (!ch.isVisible) continue;
            int mi = ch.materialReferenceIndex;
            int vi = ch.vertexIndex;
            Vector3[] verts = info.meshInfo[mi].vertices;
            Color32[] cols = info.meshInfo[mi].colors32;
            for (int j = 0; j < 4; j++)
            {
                float u = (verts[vi + j].x - minX) / width;
                Color c = Rainbow(u * RainbowSpan + time * RainbowSpeed, 1f);
                cols[vi + j] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), cols[vi + j].a);
            }
        }
        title.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private static Color Rainbow(float hue, float alpha)
    {
        Color c = Color.HSVToRGB(Mathf.Repeat(hue, 1f), RainbowSaturation, 1f);
        c.a = alpha;
        return c;
    }

    // 씬이 바뀌는 등으로 중간에 사라져도 결과창 전환이 막히지 않도록 OnDestroy 에서도 한 번만 호출
    void OnDestroy() { Finish(); }

    private void Finish()
    {
        if (onDone == null) return;
        Action a = onDone;
        onDone = null;
        a();
    }

    private void PlaySound(string path)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null) return;
        AudioSource src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.PlayOneShot(clip, SettingsStore.MusicVolume); // 설정 > MUSIC VOLUME 을 따름
    }

    private static GameObject NewObj(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        Image img = NewObj(name, parent).AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, TMP_FontAsset font, FontStyles style)
    {
        TextMeshProUGUI t = NewObj(name, parent).AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = false;
#pragma warning restore CS0618
        t.overflowMode = TextOverflowModes.Overflow;
        RectTransform r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        return t;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    private static float EaseOut(float x) { return 1f - Mathf.Pow(1f - x, 3f); }
    private static float EaseIn(float x) { return x * x * x; }
    private static float EaseInOut(float x) { return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f; }
    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}