using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
// MODE RANDOM 연출 (게임 화면: 곡 정보 화면이 걷힌 뒤, 레인이 펼쳐지기 전)
//  REVERSE 처럼 화면이 바뀌는 모드가 있으므로 레인이 나오기 전에 먼저 알려 줌
//  어둡게 덮은 화면 위 창에 칸 3개 (아이콘 한 칸 크기의 창)
//   → 아이콘이 위에서 아래로 미끄러지며 바뀌다가 천천히 멈춤 (왼쪽부터)
//   → 고른 조합의 배율 → 보너스(+%p) → 최종 배율 → 창이 사라지면서 레인이 펼쳐지고 곡 시작 (약 8초)
//  Enter : 연출 건너뛰기 (바로 결과)   (게임 화면에서는 취소 없음)
//  모드는 GameModes.BeginPlay 에서 이미 정해져 적용되어 있음 → 이 창은 그 결과를 보여 주기만 함
//  고를 수 없는 조합(함께 쓸 수 없는 모드 · STRICT 처럼 조건이 안 맞는 모드)은 나오지 않음 (GameModes.RollRandom)
// ============================================================
public class GameModeRoulette : MonoBehaviour
{
    private static GameModeRoulette instance;
    private static int closedFrame = -10;
    public static bool IsOpen { get { return instance != null || Time.frameCount <= closedFrame + 1; } }

    // 크기 (1920 x 1080 기준)
    private const float Slot = 108f;                 // 아이콘 크기
    private const float SlotGap = 112f;              // 칸 사이 간격 (긴 모드 이름이 들어가도록 넓게)
    private const float Pitch = Slot * 1.25f;        // 칸 안 아이콘 간격 (멈추면 이웃 아이콘은 창 밖)
    private const float ReelH = Slot * 1.5f;         // 창 높이: 위아래 끝은 그라데이션으로 서서히 사라짐
    private const float ReelFade = Slot * 0.25f;     // 창 위아래 끝을 덮는 검은 그라데이션 길이 (멈춘 아이콘에는 닿지 않음)
    private const float PanelH = 600f;            // (모드 설명이 들어가도록 세로를 늘림)
    // 시간 (초)
    private const float AppearTime = 0.5f;
    private const float FirstStop = 2.0f, StopGap = 0.85f;   // 첫 릴이 멈추는 시각, 릴 사이 간격
    private const float CountTime = 0.9f, BonusTime = 0.7f, HoldTime = 2.0f, CloseTime = 0.45f;

    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 1f);   // 창 바탕: 완전한 검정 (덮는 그라데이션과 이어지도록)
    private static readonly Color TextMain = new Color(0.96f, 0.97f, 1f, 1f);
    private static readonly Color TextSub = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color TextDim = new Color(1f, 1f, 1f, 0.28f);
    private static readonly Color Gold = new Color(1f, 0.82f, 0.36f, 1f);

    private class Reel
    {
        public RectTransform view;                    // 잘라내는 창 (RectMask2D)
        public GameModeBadge[] items = new GameModeBadge[4];
        public int[] itemIndex = { -99, -99, -99, -99 };
        public TextMeshProUGUI name, mult, desc;     // desc: 모드 설명 (멈춘 뒤 나타남)
        public List<GameMode> seq = new List<GameMode>();   // 굴러가는 순서 (seq[N] = 최종 모드)
        public int N;
        public float stopAt;
        public bool stopped;
        public float stopTime = -1f;                  // 멈춘 시각 (spin 기준)
        public float reveal;                          // 이름 · 배율이 나타나는 정도 0 → 1
        public int shownName = -1;
        public CanvasGroup cg;                        // 나타날 때 칸마다 차례로
        public RectTransform[] fades = new RectTransform[2];   // 위 · 아래 검은 그라데이션
        public Image underline;                       // 멈추면 가운데에서 양옆으로 펼쳐지는 모드 색 선
    }

    private const float PanelScale = 1.1f;            // 처음 크기보다 살짝 크게

    private TextMeshProUGUI titleText, subText;
    private CanvasGroup panelGroup;

    // 세로 그라데이션 그림: 위쪽(바깥) 불투명 → 아래쪽(안쪽) 투명
    private static Sprite fadeSprite;
    private static Sprite FadeSprite
    {
        get
        {
            if (fadeSprite != null) return fadeSprite;
            const int H = 64;
            Texture2D tex = new Texture2D(1, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < H; y++)
            {
                float u = y / (H - 1f);                  // 0 = 아래(안쪽), 1 = 위(바깥)
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, u * u * (3f - 2f * u)));
            }
            tex.Apply();
            fadeSprite = Sprite.Create(tex, new Rect(0, 0, 1, H), new Vector2(0.5f, 0.5f), 100f);
            return fadeSprite;
        }
    }                   // 창만 (배경은 바로 보이고, 창이 그 위로 나타남)

    private readonly Reel[] reels = new Reel[GameModes.MaxSelected];
    private TextMeshProUGUI baseText, bonusText, totalText, hintText;
    private CanvasGroup group;
    private RectTransform panel;
    private Texture2D blurTex;
    private TMP_FontAsset bold, reg;
    private Action onDone, onCancel;
    private List<GameMode> result;
    private double baseK, bonus;
    private bool skip, cancelled, closing, inGame;
    private float spin;                               // 릴이 돌기 시작한 뒤 흐른 시간
    private GameMode[] pool;
    private readonly System.Random rng = new System.Random(Guid.NewGuid().GetHashCode());

    // inGame = true : 게임 화면 — 이미 정해진 조합(GameModes.RandomResult)을 보여 줌, 취소 없음, 흐리게 찍지 않고 어둡게만 덮음
    public static void Show(Scene scene, TMP_FontAsset nameFont, Action onDone, Action onCancel, bool inGame = false)
    {
        if (instance != null) return;
        GameObject go = new GameObject("GameModeRoulette", typeof(RectTransform));
        go.layer = 5;
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(go, scene);
        GameModeRoulette r = go.AddComponent<GameModeRoulette>();
        instance = r;
        r.onDone = onDone;
        r.onCancel = onCancel;
        r.inGame = inGame;
        r.StartCoroutine(r.Run(nameFont));
    }

    void OnDestroy()
    {
        if (blurTex != null) Destroy(blurTex);
        if (instance == this) instance = null;
        closedFrame = Mathf.Max(closedFrame, Time.frameCount);
    }

    // ----- 부드러운 움직임 곡선 -----
    private static float OutCubic(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    private static float OutQuart(float x) { x = Mathf.Clamp01(x); float y = 1f - x; return 1f - y * y * y * y; }
    private static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }

    // =====================================================================
    //  진행
    // =====================================================================
    private IEnumerator Run(TMP_FontAsset nameFont)
    {
        // 조합: 게임 화면이면 이미 정해진 것, 아니면 지금 정함 (연출은 보여 주기만)
        if (inGame && GameModes.RandomResult != null && GameModes.RandomResult.Count == GameModes.MaxSelected)
            result = new List<GameMode>(GameModes.RandomResult);
        else result = GameModes.RollRandom(rng);
        baseK = GameModes.MultiplierOf(result);
        bonus = GameModes.RandomResultBonus;
        pool = GameModes.RandomPool();

        if (!inGame)
        {
            yield return new WaitForEndOfFrame();
            blurTex = PlayConfirmDialog.CaptureBlurred();
        }
        Build(nameFont);
        for (int i = 0; i < reels.Length; i++) PlaceReel(reels[i], 0f);

        // 나타남: 창이 살짝 커지며 → 제목 자간이 좁혀지며 → 칸이 하나씩 떠오름 (릴은 이미 천천히 돌기 시작)
        float appearAll = AppearTime + 0.08f * reels.Length + 0.2f;
        for (float t = 0f; t < appearAll; t += Time.unscaledDeltaTime)
        {
            ApplyAppear(t);
            StepReels(Time.unscaledDeltaTime);
            if (CheckKeys()) break;
            yield return null;
        }
        ApplyAppear(99f);
        if (cancelled) { yield return Close(false); yield break; }

        // 1) 릴이 굴러가다 하나씩 멈춤
        float end = FirstStop + StopGap * (reels.Length - 1) + 0.35f;
        while (spin < end && !skip)
        {
            StepReels(Time.unscaledDeltaTime);
            if (CheckKeys()) break;
            yield return null;
        }
        if (cancelled) { yield return Close(false); yield break; }
        if (skip) FinishReels();

        // 2) 조합 배율이 올라감 (100% → 390%)
        float dur = skip ? 0f : CountTime;
        for (float t = 0f; t < dur && !skip; t += Time.unscaledDeltaTime)
        {
            float e = Smoother(t / dur);
            baseText.text = GameModes.Percent(1.0 + (baseK - 1.0) * e);
            baseText.alpha = OutCubic(t / (dur * 0.4f));
            StepReels(Time.unscaledDeltaTime);
            if (CheckKeys()) break;
            yield return null;
        }
        if (cancelled) { yield return Close(false); yield break; }
        baseText.text = GameModes.Percent(baseK);
        baseText.alpha = 1f;

        // 3) 보너스 → 최종 배율 (보너스는 아래에서 떠오르고, 최종 배율은 살짝 커졌다 자리 잡음)
        string b = GameModes.BonusText(bonus);
        bonusText.text = "RANDOM BONUS  <color=#" + ColorUtility.ToHtmlStringRGB(Gold) + ">" + b + "</color>";
        totalText.text = GameModes.Percent(baseK + bonus);
        dur = skip ? 0f : BonusTime;
        for (float t = 0f; t < dur && !skip; t += Time.unscaledDeltaTime)
        {
            float e = OutCubic(t / dur);
            bonusText.alpha = e;
            totalText.alpha = e;
            totalText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, e);
            StepReels(Time.unscaledDeltaTime);
            if (CheckKeys()) break;
            yield return null;
        }
        if (cancelled) { yield return Close(false); yield break; }
        bonusText.alpha = 1f; totalText.alpha = 1f;
        totalText.rectTransform.localScale = Vector3.one;

        // 결과를 충분히 보여 준 뒤 시작 (안내 글자는 서서히 사라짐)
        float hold = skip ? 0.9f : HoldTime;
        for (float t = 0f; t < hold; t += Time.unscaledDeltaTime)
        {
            hintText.alpha = 1f - OutCubic(t / 0.4f);
            StepReels(Time.unscaledDeltaTime);
            var kb = Keyboard.current;
            if (!inGame && kb != null && kb.escapeKey.wasPressedThisFrame) { cancelled = true; break; }
            yield return null;
        }
        yield return Close(!cancelled);
    }

    // 나타나는 연출 (t = 시작 뒤 흐른 시간)
    private void ApplyAppear(float t)
    {
        float e = OutCubic(t / AppearTime);
        group.alpha = inGame ? 1f : e;                // 게임 화면: 흐린 배경은 처음부터 (검은 화면이 비치지 않게)
        panelGroup.alpha = e;
        panel.localScale = Vector3.one * PanelScale * Mathf.Lerp(0.94f, 1f, e);
        panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(-18f, 0f, e));
        float et = OutCubic((t - 0.05f) / 0.6f);
        titleText.characterSpacing = Mathf.Lerp(34f, 14f, et);
        titleText.alpha = et;
        subText.alpha = OutCubic((t - 0.18f) / 0.4f);
        for (int i = 0; i < reels.Length; i++)
        {
            float ei = OutCubic((t - 0.15f - 0.08f * i) / 0.45f);
            reels[i].cg.alpha = ei;
            float dy = Mathf.Lerp(-14f, 0f, ei);
            reels[i].view.anchoredPosition = new Vector2(reels[i].view.anchoredPosition.x, ReelY + dy);
            for (int side = 0; side < 2; side++)
            {
                RectTransform f = reels[i].fades[side];
                f.anchoredPosition = new Vector2(f.anchoredPosition.x, ReelY + dy + (side == 0 ? 1f : -1f) * (ReelH * 0.5f - ReelFade * 0.5f));
            }
        }
    }

    private IEnumerator Close(bool start)
    {
        if (closing) yield break;
        closing = true;
        closedFrame = Time.frameCount;
        // 시작: 곧바로 알림 → 창이 사라지는 동안 레인이 펼쳐지기 시작
        if (start && onDone != null) onDone();
        float fade = inGame ? CloseTime : 0.2f;
        for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
        {
            float e = Smoother(t / fade);
            group.alpha = 1f - e;
            panel.localScale = Vector3.one * PanelScale * Mathf.Lerp(1f, 1.03f, e);
            StepReels(Time.unscaledDeltaTime);
            yield return null;
        }
        if (!start && onCancel != null) onCancel();
        Destroy(gameObject);
    }

    // ESC = 취소 (곡 선택창에서만), Enter = 건너뛰기. true 면 지금 단계를 끝냄
    private bool CheckKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return false;
        if (!inGame && kb.escapeKey.wasPressedThisFrame) { cancelled = true; return true; }
        // 게임 화면에서는 Space 가 레인 키일 수 있어 Enter 만
        bool skipKey = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || (!inGame && kb.spaceKey.wasPressedThisFrame);
        if (skipKey) { skip = true; return true; }
        return false;
    }

    // =====================================================================
    //  릴
    //   위치 pos(아이콘 몇 개만큼 굴렀나) = N × 곡선(시간 / 멈추는 시각)
    //   곡선 = (서서히 빨라짐) × (끝에서 아주 천천히 멈춤) → 처음과 끝 모두 툭 끊기지 않음
    // =====================================================================
    private void StepReels(float dt)
    {
        spin += dt;
        for (int i = 0; i < reels.Length; i++)
        {
            Reel r = reels[i];
            float pos;
            if (skip && !r.stopped) { r.stopped = true; r.stopTime = spin; }
            if (!r.stopped && spin >= r.stopAt) { r.stopped = true; r.stopTime = r.stopAt; }
            if (!r.stopped)
            {
                float p = spin / r.stopAt;
                float spinUp = Smoother(Mathf.Clamp01(p / 0.18f));
                pos = r.N * OutQuart(p) * spinUp;                               // 0 에서 서서히 출발 → 끝에서 아주 천천히 멈춤
                pos = Mathf.Min(pos, r.N);
            }
            else pos = r.N;
            PlaceReel(r, pos);

            // 이름: 도는 동안은 가운데 아이콘 이름을 옅게, 멈추면 진하게 + 배율이 나타남
            if (r.stopped) r.reveal = Mathf.MoveTowards(r.reveal, 1f, dt / 0.35f);
            int center = Mathf.Clamp(Mathf.RoundToInt(pos), 0, r.seq.Count - 1);
            if (!r.stopped && center != r.shownName)
            {
                r.shownName = center;
                r.name.text = GameModes.Info(r.seq[center]).Name;
            }
            else if (r.stopped && r.shownName != r.N)
            {
                r.shownName = r.N;
                GameModeInfo info = GameModes.Info(r.seq[r.N]);
                r.name.text = info.Name;
                r.mult.text = GameModes.Percent(info.Multiplier);
                r.mult.color = info.Multiplier > 1.0001 ? info.Color : new Color(1f, 1f, 1f, 0.45f);
            }
            float rv = OutCubic(r.reveal);
            r.name.color = Color.Lerp(TextDim, TextMain, rv);
            r.name.characterSpacing = r.stopped ? Mathf.Lerp(9f, 3f, rv) : 3f;            // 멈추면 자간이 좁혀지며 또렷해짐
            r.mult.alpha = rv;
            if (r.desc != null)
            {
                if (r.stopped && r.desc.text.Length == 0) r.desc.text = GameModes.Info(r.seq[r.N]).Desc;
                r.desc.alpha = Mathf.Clamp01(rv * 1.2f - 0.2f) * 0.8f;
            }
            // 모드 색 밑줄: 가운데에서 양옆으로
            Color mc = GameModes.Info(r.seq[r.N]).Color;
            r.underline.color = new Color(mc.r, mc.g, mc.b, 0.9f * rv);
            r.underline.rectTransform.sizeDelta = new Vector2(Slot * 0.8f * rv, 3f);
            // 멈추는 순간 아이콘이 살짝 커졌다 돌아옴
            float u = r.stopped ? Mathf.Clamp01((spin - r.stopTime) / 0.32f) : 1f;
            float pop = 1f + 0.08f * Mathf.Sin(u * Mathf.PI);
            // (창이 아니라 멈춘 아이콘만 키움 — 창을 키우면 위아래 그라데이션 밖으로 잘린 끝이 드러남)
            r.items[((r.N % 4) + 4) % 4].transform.localScale = new Vector3(pop, pop, 1f);
        }
    }

    // 건너뛰기: 모든 릴을 바로 멈춘 상태로
    private void FinishReels()
    {
        foreach (var r in reels)
        {
            if (!r.stopped) { r.stopped = true; r.stopTime = spin; }
            r.reveal = 1f;
        }
        StepReels(0f);
    }

    // pos 위치로 릴의 아이콘들을 놓음 (아이콘 j 의 높이 = (j - pos) × 간격 → pos 가 늘면 아래로 내려감)
    private void PlaceReel(Reel r, float pos)
    {
        int baseJ = Mathf.FloorToInt(pos) - 1;
        for (int k = 0; k < 4; k++)
        {
            int j = baseJ + k;
            GameModeBadge b = r.items[((j % 4) + 4) % 4];
            int idx = Mathf.Clamp(j, 0, r.seq.Count - 1);
            int slotK = ((j % 4) + 4) % 4;
            if (r.itemIndex[slotK] != j)
            {
                r.itemIndex[slotK] = j;
                b.Set(r.seq[idx]);
                b.gameObject.SetActive(j >= 0 && j < r.seq.Count);
            }
            float y = (j - pos) * Pitch;
            ((RectTransform)b.transform).anchoredPosition = new Vector2(0f, y);

        }
    }

    // =====================================================================
    //  만들기
    // =====================================================================
    private const float ReelY = 90f, NameY = -10f, MultY = -36f, DescY = -78f;

    private void Build(TMP_FontAsset nameFont)
    {
        SettingsUIKit.EnsureFont();
        reg = SettingsWindow.RegularFont != null ? SettingsWindow.RegularFont : SettingsUIKit.Font;
        bold = SettingsWindow.BoldFont != null ? SettingsWindow.BoldFont : reg;
        if (nameFont != null) bold = nameFont;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 320;              // 게임 UI · 모드 설정창 위, 설정창(500) 아래
        CanvasScaler sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        RawImage blur = new GameObject("Blur", typeof(RectTransform)).AddComponent<RawImage>();
        blur.gameObject.layer = 5;
        blur.transform.SetParent(transform, false);
        blur.texture = blurTex;
        blur.color = blurTex != null ? Color.white : new Color(0f, 0f, 0f, 0.6f);
        SettingsUIKit.Stretch(blur.rectTransform);
        if (inGame)
        {
            // 게임 화면: 모든 UI 위에 그 곡의 흐린 배경 그림(Sprites/blurimg/{곡ID}, SPECIAL 이면 _4)을 깔고 그 위에 창
            //  (MV 곡은 영상이 아직 재생 전이라 배경이 검게 보이므로) — 창이 사라질 때 함께 사라짐
            blur.gameObject.SetActive(false);
            var core = CoreManager.Instance;
            Sprite bgs = core != null && core.CurrentSongData != null ? BackgroundBlur.Get(SongArt.Load("Sprites/blurimg", core.CurrentSongData.Id, core.CurrentLevelId)) : null;   // 원본 → 흐리게
            Image bgImg = SettingsUIKit.Img("SongBackground", transform, bgs != null ? Color.white : Color.black, true);
            bgImg.sprite = bgs;
            SettingsUIKit.Stretch(bgImg.rectTransform);
            if (bgs != null)
            {
                AspectRatioFitter fit = bgImg.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = bgs.rect.width / Mathf.Max(1f, bgs.rect.height);
            }
        }
        Image back = SettingsUIKit.Img("Backdrop", transform, new Color(0f, 0f, 0f, inGame ? 0.45f : 0.72f), true);
        SettingsUIKit.Stretch(back.rectTransform);

        float w = reels.Length * Slot + (reels.Length - 1) * SlotGap + 340f;
        Image p = Round(SettingsUIKit.Img("Panel", transform, PanelColor, true), 18f);
        panel = p.rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(w, PanelH);
        panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        panelGroup.alpha = 0f;
        Image border = SettingsUIKit.Img("Border", panel, new Color(1f, 1f, 1f, 0.55f));
        border.sprite = GameModes.RingSprite; border.type = Image.Type.Sliced; border.fillCenter = false;
        border.pixelsPerUnitMultiplier = 16f / 18f;
        SettingsUIKit.Stretch(border.rectTransform);

        TextMeshProUGUI title = Txt(panel, "MODE RANDOM", 30, TextAlignmentOptions.Center, Gold, bold);
        title.characterSpacing = 14f;
        Place(title.rectTransform, 0f, 236f, w, 44f);
        titleText = title;
        TextMeshProUGUI sub = Txt(panel, "3 MODES · BONUS UP TO +" + Math.Round(GameModes.RandomBonusMax * 100.0).ToString("0") + "%p", 14, TextAlignmentOptions.Center, TextDim, bold);
        sub.characterSpacing = 6f;
        Place(sub.rectTransform, 0f, 200f, w, 22f);
        subText = sub;

        float x0 = -((reels.Length - 1) * (Slot + SlotGap)) * 0.5f;
        for (int i = 0; i < reels.Length; i++)
        {
            Reel r = new Reel();
            float cx = x0 + i * (Slot + SlotGap);

            // 잘라내는 창: 아이콘 한 칸 크기
            r.view = SettingsUIKit.Rect("Reel" + i, panel);
            // 창: 위아래 끝은 검은 그라데이션(창 바탕색 → 투명)으로 덮어 아이콘이 부드럽게 사라지고 나타남
            Place(r.view, cx, ReelY, Slot + 24f, ReelH);
            r.view.gameObject.AddComponent<RectMask2D>();
            r.cg = r.view.gameObject.AddComponent<CanvasGroup>();
            // 위 · 아래 검은 그라데이션 (아이콘 위에 덮음 — 바탕이 완전한 검정이라 경계가 보이지 않음)
            for (int side = 0; side < 2; side++)
            {
                Image fade = SettingsUIKit.Img(side == 0 ? "FadeTop" : "FadeBottom", panel, Color.black);
                fade.sprite = FadeSprite;
                fade.raycastTarget = false;
                Place(fade.rectTransform, cx, ReelY + (side == 0 ? 1f : -1f) * (ReelH * 0.5f - ReelFade * 0.5f), Slot + 40f, ReelFade + 2f);
                fade.rectTransform.localScale = new Vector3(1f, side == 0 ? 1f : -1f, 1f);   // 아래쪽은 뒤집어 씀
                r.fades[side] = fade.rectTransform;
            }
            r.underline = SettingsUIKit.Img("Underline", panel, Color.clear);
            Place(r.underline.rectTransform, cx, ReelY - ReelH * 0.5f - 2f, 0f, 3f);
            for (int k = 0; k < 4; k++)
            {
                RectTransform br = GameModes.CreateBadge(r.view, pool[0], Slot);
                br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 0.5f);
                r.items[k] = br.GetComponent<GameModeBadge>();
            }

            r.name = Txt(panel, "", 17, TextAlignmentOptions.Center, TextDim, bold);
            r.name.characterSpacing = 3f;
            r.name.enableAutoSizing = true; r.name.fontSizeMax = 17; r.name.fontSizeMin = 11;
            Place(r.name.rectTransform, cx, NameY, Slot + SlotGap - 8f, 26f);
            r.mult = Txt(panel, "", 15, TextAlignmentOptions.Center, TextSub, bold);
            r.mult.alpha = 0f;
            Place(r.mult.rectTransform, cx, MultY, Slot + SlotGap - 8f, 22f);
            // 모드 설명 (한국어, 2 ~ 3줄, 멈춘 뒤 서서히 나타남)
            r.desc = Txt(panel, "", 13, TextAlignmentOptions.Top, TextSub, reg);
            r.desc.fontStyle = FontStyles.Normal;
            r.desc.textWrappingMode = TextWrappingModes.Normal;
            r.desc.enableAutoSizing = true; r.desc.fontSizeMax = 13; r.desc.fontSizeMin = 10;
            r.desc.lineSpacing = -6f;
            r.desc.alpha = 0f;
            Place(r.desc.rectTransform, cx, DescY, Slot + SlotGap - 20f, 48f);

            // 굴러갈 순서: 앞쪽은 무작위 (같은 모드가 붙지 않게), seq[N] = 최종 모드, 뒤로 몇 개 더 (멈춘 뒤 위쪽에 보임)
            r.N = 14 + 5 * i;                       // 굴러가는 아이콘 수 (적당히 — 너무 정신없지 않게)
            r.stopAt = FirstStop + StopGap * i;
            GameMode last = (GameMode)(-1);
            for (int j = 0; j <= r.N + 2; j++)
            {
                GameMode m;
                if (j == r.N) m = result[i];
                else
                {
                    int guard = 0;
                    do { m = pool[rng.Next(pool.Length)]; guard++; }
                    while (guard < 8 && (m == last || (j == r.N - 1 && m == result[i]) || (j == r.N + 1 && m == result[i])));
                }
                r.seq.Add(m);
                last = m;
            }
            reels[i] = r;
        }

        // 아래: 왼쪽 조합 배율 · 가운데 보너스 · 오른쪽 최종 배율 (예전 배치)
        Image line = SettingsUIKit.Img("Line", panel, new Color(1f, 1f, 1f, 0.08f));
        Place(line.rectTransform, 0f, -122f, w - 120f, 1f);
        float rowY = -166f;
        baseText = Txt(panel, "", 22, TextAlignmentOptions.MidlineLeft, TextMain, bold);
        baseText.alpha = 0f;
        Place(baseText.rectTransform, -w * 0.5f + 60f + 150f, rowY, 300f, 40f);
        bonusText = Txt(panel, "", 16, TextAlignmentOptions.Center, TextSub, bold);
        bonusText.characterSpacing = 4f;
        bonusText.alpha = 0f;
        Place(bonusText.rectTransform, 0f, rowY, 360f, 30f);
        totalText = Txt(panel, "", 40, TextAlignmentOptions.MidlineRight, Gold, bold);
        totalText.alpha = 0f;
        Place(totalText.rectTransform, w * 0.5f - 60f - 150f, rowY, 300f, 56f);
        hintText = Txt(panel, inGame ? "ENTER  SKIP" : "ENTER  SKIP     ESC  CANCEL", 12, TextAlignmentOptions.Center, TextDim, bold);
        hintText.characterSpacing = 4f;
        Place(hintText.rectTransform, 0f, -268f, w, 20f);
    }

    // 패널 가운데 기준 (x, y) 에 (w, h)
    private static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    private static Image Round(Image img, float radius)
    {
        img.sprite = GameModes.BadgeSprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 16f / Mathf.Max(1f, radius);
        return img;
    }

    private static TextMeshProUGUI Txt(Transform parent, string text, float size, TextAlignmentOptions align, Color color, TMP_FontAsset font)
    {
        TextMeshProUGUI t = SettingsUIKit.Txt("T", parent, text, size, align, color, FontStyles.Bold);
        if (font != null) t.font = font;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}