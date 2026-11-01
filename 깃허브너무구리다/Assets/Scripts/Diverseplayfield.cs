using System.Collections.Generic;
using UnityEngine;

// ============================================================
// SIMPLE-DIVERSE 스킨: 레인 · 노트를 기울어진 원근으로 그리기
//
//  [원리]  카메라를 추가하지 않는다.
//   - 레인 · 노트 · 마디선 스프라이트에 DiverseSprite 셰이더 재질을 씌우고,
//     셰이더가 정점 위치를 '기울어진 가상 카메라'로 투영해서 그린다.
//   - 원래 카메라가 그대로 그리므로 정렬 순서 · UI · 일시정지 화면은 지금과 같고,
//     Unlit 이라 2D 조명(URP 2D Light)의 영향을 받지 않는다.
//   - 게임 로직 · 노트 위치 계산은 그대로. 보이는 각도만 바뀜.
//   - 판정선 아래로 노트가 안 보이게 하는 처리도 셰이더가 직접 함 (SpriteMask 대신).
//   - 먼 쪽 끝은 부드럽게 사라짐 (노트가 갑자기 튀어나오지 않음).
//
//  [가상 카메라 맞춤]  아래 값으로 위치를 자동 계산 (해상도 · 화면비가 달라도 같은 모양)
//   Tilt            기울기 (클수록 더 눕고 먼 쪽이 좁아짐)
//   Fov             시야각 (클수록 원근감이 강함)
//   HitScreenY      판정선이 화면 아래에서 몇 % 높이에 오는지
//   LaneWidthAtHit  판정선에서 레인 전체 폭이 화면 폭의 몇 %인지
//
//  [필요한 파일]  Assets/Resources/Shaders/DiverseSprite.shader + DiverseProject.cginc
//                 (없으면 평면 SIMPLE 로 대신 그림)
// ============================================================
public class DiversePlayfield : MonoBehaviour
{
    public static float Tilt = 50f;   // 55 → 50: 레인이 덜 누워서 위아래로 조금 더 길게 보임
    public static float Fov = 40f;
    public static float HitScreenY = 0.15f;
    public static float LaneWidthAtHit = 0.36f;   // 0.46 → 0.36 (좌우 폭 좁게)
    public const float FadeScreenLength = 0.05f;  // 먼 쪽 끝에서 사라지는 구간 (화면 높이 비율, 0.10 → 0.05)

    public float FarY { get; private set; }       // 레인이 보이는 먼 쪽 끝 (월드 y)
    public Shader Shader { get; private set; }

    // 텍스처마다 재질을 따로 둠 (스프라이트끼리 텍스처가 섞여 그려지는 일이 없도록)
    private readonly Dictionary<long, Material> materials = new Dictionary<long, Material>();
    private readonly List<Material> extraMaterials = new List<Material>();   // MV 블러 등 (값만 넣어 줌)
    private const int MaxMaterials = 128;
    // 재질 수가 넘쳤을 때 쓰는 공용 재질 (스프라이트는 텍스처를 스스로 넘겨 주므로 공용이어도 그려짐)
    //  [버그 수정] 예전에는 '접히지 않는' 공용 재질이 없어서, 재질 수가 넘친 뒤 생긴 영역 노트가 접히는 재질을 받아 안 보였다
    private Material fallbackSprite, fallbackClip, fallbackSpriteNoFold, fallbackClipNoFold;

    private GameManager gm;
    private float laneCenterX, laneWidth;
    private Vector3 camPos, camF, camU, camR;
    private float projX, projY;

    public static Shader LoadShader()
    {
        Shader s = Resources.Load<Shader>("Shaders/DiverseSprite");
        if (s == null) s = Shader.Find("Hidden/4BTunes/DiverseSprite");
        return s != null && s.isSupported ? s : null;
    }

    public static bool IsSupported() { return LoadShader() != null; }

    public static DiversePlayfield Create(Transform parent, GameManager gm, float laneLeft, float laneRight)
    {
        Shader shader = LoadShader();
        if (shader == null)
        {
            Debug.LogWarning("[DiversePlayfield] Resources/Shaders/DiverseSprite.shader 가 없어 SIMPLE 로 그립니다.");
            return null;
        }
        GameObject go = new GameObject("DiversePlayfield");
        go.transform.SetParent(parent, false);
        DiversePlayfield p = go.AddComponent<DiversePlayfield>();
        p.gm = gm;
        p.Shader = shader;
        p.laneCenterX = (laneLeft + laneRight) * 0.5f;
        p.laneWidth = Mathf.Max(0.1f, laneRight - laneLeft);
        p.fallbackSprite = p.NewMaterial(null, false);
        p.fallbackClip = p.NewMaterial(null, true);
        p.fallbackSpriteNoFold = p.NewMaterial(null, false);
        p.fallbackClipNoFold = p.NewMaterial(null, true);
        p.noFold.Add(p.fallbackSpriteNoFold);
        p.noFold.Add(p.fallbackClipNoFold);
        p.Refresh();
        return p;
    }

    // 스프라이트 하나를 원근 재질로 (clipBelow: 판정선 아래는 안 그림, fold: 레인 접기(FOLD)를 따라감)
    public void Apply(SpriteRenderer sr, bool clipBelow) { Apply(sr, clipBelow, true); }

    public void Apply(SpriteRenderer sr, bool clipBelow, bool fold)
    {
        if (sr == null) return;
        Texture tex = sr.sprite != null ? sr.sprite.texture : null;
        sr.sharedMaterial = GetMaterial(tex, clipBelow, fold);
        sr.maskInteraction = SpriteMaskInteraction.None; // 자르기는 셰이더가 함
    }

    // 메시(영역 배경 등)에 쓸 원근 재질
    public Material MaterialFor(Texture tex, bool clipBelow, bool fold = true) { return GetMaterial(tex, clipBelow, fold); }

    // ===== 레인 접기 (채보 FOLD / UNFOLD) =====
    //  k: 가로 배율 (1 = 그대로, 0 = 가운데 한 줄로 접힘), alpha: 투명도, centerX: 접히는 중심
    //  셰이더(DiverseProject.cginc)가 정점의 x 를 중심 쪽으로 모으고 알파를 곱한다 → 레인 · 노트 · 효과가 한꺼번에 접힘
    private Vector4 vFold = new Vector4(1f, 1f, 0f, 1f);
    private readonly HashSet<Material> noFold = new HashSet<Material>();

    public void SetFold(float k, float alpha, float centerX)
    {
        Vector4 v = new Vector4(Mathf.Clamp01(k), Mathf.Clamp01(alpha), centerX, 1f);
        if (v == vFold) return;
        vFold = v;
        // 값은 Refresh 에서 모든 재질에 들어간다
    }

    // MV 블러처럼 다른 셰이더도 같은 투영 값을 받도록 등록
    public void Register(Material m)
    {
        if (m != null && !extraMaterials.Contains(m)) { extraMaterials.Add(m); Push(m); }
    }

    private Material GetMaterial(Texture tex, bool clip, bool fold = true)
    {
        if (tex == null) return Fallback(clip, fold);
        long id = tex != null ? tex.GetInstanceID() : 0;
        long key = (id << 2) | (clip ? 1L : 0L) | (fold ? 0L : 2L);
        Material m;
        if (materials.TryGetValue(key, out m) && m != null) return m;
        if (materials.Count >= MaxMaterials) return Fallback(clip, fold);
        m = NewMaterial(tex, clip);
        if (!fold) noFold.Add(m);
        materials[key] = m;
        Push(m);
        return m;
    }

    private Material Fallback(bool clip, bool fold)
    {
        if (fold) return clip ? fallbackClip : fallbackSprite;
        return clip ? fallbackClipNoFold : fallbackSpriteNoFold;
    }

    private Material NewMaterial(Texture tex, bool clip)
    {
        Material m = new Material(Shader) { name = "DiverseSprite" + (clip ? "Clip" : "") };
        if (tex != null) m.mainTexture = tex;
        m.SetFloat("_ClipBelow", clip ? 1f : 0f);
        Push(m);
        return m;
    }

    public void ApplyAll(Transform root, bool clipBelow)
    {
        if (root == null) return;
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true)) Apply(sr, clipBelow);
    }

    // 렌더링 직전에 가상 카메라 값 갱신 (화면비가 바뀌어도 맞음)
    void Update() { Refresh(); }
    void LateUpdate() { Refresh(); }

    private Vector4 vFade, vClip, vProj;

    private void Refresh()
    {
        if (gm == null) return;
        Camera main = gm.SceneCamera;
        float aspect = main != null ? main.aspect : 16f / 9f;
        // 먼 쪽 끝 = 원래 평면 화면의 맨 위 (노트가 처음 보이기 시작하는 시점이 기존과 같도록). 매 프레임 지금 카메라로 계산
        FarY = main != null && main.orthographic ? main.transform.position.y + main.orthographicSize : gm.hitLineY + 8f;

        float a = Tilt * Mathf.Deg2Rad;
        float t = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
        camF = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));   // 앞 (위쪽 + 화면 안쪽)
        camU = new Vector3(0f, Mathf.Cos(a), -Mathf.Sin(a));  // 위
        camR = Vector3.right;
        float zh = laneWidth / (Mathf.Max(0.05f, LaneWidthAtHit) * 2f * t * Mathf.Max(0.1f, aspect)); // 판정선까지 깊이
        float vh = (HitScreenY - 0.5f) * zh * 2f * t;                                                // 판정선의 화면 세로 위치
        Vector3 hit = new Vector3(laneCenterX, gm.hitLineY, 0f);
        camPos = hit - (camF * zh + camU * vh);
        projX = 1f / (t * Mathf.Max(0.1f, aspect));
        projY = 1f / t;

        // 먼 쪽 끝 페이드: 화면에서 FadeScreenLength 만큼 아래 지점의 월드 y 를 찾음
        float farV = Viewport(new Vector3(laneCenterX, FarY, 0f)).y;
        float fadeStartY = WorldYAtViewport(farV - FadeScreenLength);

        vProj = new Vector4(projX, projY, 0f, 0f);
        vFade = new Vector4(Mathf.Min(fadeStartY, FarY - 0.05f), FarY, 0f, 0f);
        vClip = new Vector4(gm.hitLineY + gm.noteClipOffset, 0f, 0f, 0f);

        // 이 판의 재질에만 값을 넣음 (재시도 · 다른 곡으로 넘어갈 때 이전 판과 섞이지 않음)
        Push(fallbackSprite);
        Push(fallbackClip);
        Push(fallbackSpriteNoFold);
        Push(fallbackClipNoFold);
        foreach (var m in materials.Values) Push(m);
        for (int i = extraMaterials.Count - 1; i >= 0; i--)
        {
            if (extraMaterials[i] == null) extraMaterials.RemoveAt(i);
            else Push(extraMaterials[i]);
        }
    }

    private void Push(Material m)
    {
        if (m == null) return;
        m.SetVector("_DivCamPos", camPos);
        m.SetVector("_DivCamF", camF);
        m.SetVector("_DivCamU", camU);
        m.SetVector("_DivCamR", camR);
        m.SetVector("_DivProj", vProj);
        m.SetVector("_DivFade", vFade);
        m.SetVector("_DivClip", vClip);
        m.SetVector("_DivFold", noFold.Contains(m) ? new Vector4(1f, 1f, 0f, 0f) : vFold);
    }

    // 월드 → 화면 비율 좌표 (0~1)
    public Vector2 Viewport(Vector3 world)
    {
        Vector3 r = world - camPos;
        float z = Mathf.Max(0.001f, Vector3.Dot(r, camF));
        return new Vector2(0.5f + 0.5f * Vector3.Dot(r, camR) * projX / z,
                           0.5f + 0.5f * Vector3.Dot(r, camU) * projY / z);
    }

    // 레인 가운데 줄에서 화면 세로 비율 v 에 해당하는 월드 y (이분 탐색)
    private float WorldYAtViewport(float v)
    {
        float lo = gm.hitLineY, hi = FarY;
        for (int i = 0; i < 30; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Viewport(new Vector3(laneCenterX, mid, 0f)).y < v) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    // 월드 좌표 → 화면 픽셀 (HUD 배치용)
    public Vector3 WorldToScreen(Vector3 world)
    {
        Camera main = gm.SceneCamera;
        Vector2 v = Viewport(world);
        Rect pr = main != null ? main.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
        return new Vector3(pr.x + v.x * pr.width, pr.y + v.y * pr.height, 1f);
    }

    void OnDestroy()
    {
        foreach (var m in materials.Values) if (m != null) Destroy(m);
        materials.Clear();
        if (fallbackSprite != null) Destroy(fallbackSprite);
        if (fallbackClip != null) Destroy(fallbackClip);
        if (fallbackSpriteNoFold != null) Destroy(fallbackSpriteNoFold);
        if (fallbackClipNoFold != null) Destroy(fallbackClipNoFold);
    }
}