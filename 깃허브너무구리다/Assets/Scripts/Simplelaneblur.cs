using UnityEngine;
using UnityEngine.Video;

// ============================================================
// SIMPLE 스킨: MV 가 나올 때 레인 영역 뒤의 영상을 흐리게 보여 주는 판
//
//  [원리]
//   1) MV 영상 텍스처(VideoPlayer.texture)를 1/4 크기 RenderTexture 로 줄여 복사
//   2) 가우시안 블러 셰이더로 가로 → 세로를 2번 반복 (영상 프레임이 바뀔 때만)
//   3) 레인 모양의 사각형 메쉬에 '배경에서 그 위치에 해당하는 부분'만 UV 로 잘라 붙임
//      → 레인 어두운 판(LaneBg) 바로 아래에 깔려서, 레인 안쪽만 흐린 영상이 비쳐 보임
//
//  [필요한 파일]  (외부 에셋 필요 없음)
//   Assets/Resources/Shaders/LaneBlurPass.shader
//   Assets/Resources/Shaders/LaneBlurShow.shader
//   - Show 셰이더가 없으면 이 기능은 조용히 꺼진다.
//   - Pass 셰이더만 없으면 1/16 축소만으로 흐리게 한다 (덜 부드러움).
// ============================================================
public class SimpleLaneBlur : MonoBehaviour
{
    private const int Downsample = 4;          // 원본 대비 축소 배율 (셰이더 있을 때)
    private const int DownsampleNoShader = 16; // 블러 셰이더가 없을 때의 축소 배율
    private const int Iterations = 2;

    private GameManager gm;
    private Material showMat, blurMat;
    private RenderTexture rtA, rtB;
    private Mesh mesh;
    private MeshRenderer mr;
    private readonly Vector3[] verts = new Vector3[4];
    private readonly Vector2[] uvs = new Vector2[4];
    private float left, right, bottom, top, centerX;
    private float strength = 1.5f;
    private long lastFrame = long.MinValue;
    // SIMPLE-DIVERSE: 판을 원근으로 투영해서 그리고, 배경은 화면 위치 기준으로 샘플링
    public bool Diverse { get; set; }
    public Material ShowMaterial { get { return showMat; } }
    private Texture lastSource;

    public static SimpleLaneBlur Create(Transform parent, GameManager gm, float left, float right, float bottom, float top,
                                        int sortingLayerId, int sortingOrder, float strength)
    {
        Shader show = Resources.Load<Shader>("Shaders/LaneBlurShow");
        if (show == null) show = Shader.Find("Hidden/4BTunes/LaneBlurShow");
        if (show == null)
        {
            Debug.LogWarning("[SimpleLaneBlur] Resources/Shaders/LaneBlurShow.shader 가 없어 MV 레인 블러를 끕니다.");
            return null;
        }
        Shader pass = Resources.Load<Shader>("Shaders/LaneBlurPass");
        if (pass == null) pass = Shader.Find("Hidden/4BTunes/LaneBlurPass");

        GameObject go = new GameObject("LaneBlur");
        go.transform.SetParent(parent, false);
        go.transform.position = Vector3.zero;
        SimpleLaneBlur b = go.AddComponent<SimpleLaneBlur>();
        b.gm = gm;
        b.left = left; b.right = right; b.bottom = bottom; b.top = top;
        b.centerX = (left + right) * 0.5f;
        b.strength = Mathf.Max(0.1f, strength);
        b.showMat = new Material(show);
        if (pass != null && pass.isSupported) b.blurMat = new Material(pass);

        b.mesh = new Mesh { name = "LaneBlurQuad" };
        b.mesh.MarkDynamic();
        b.mesh.vertices = b.verts;
        b.mesh.uv = b.uvs;
        b.mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
        b.mr = go.AddComponent<MeshRenderer>();
        b.mr.sharedMaterial = b.showMat;
        b.mr.sortingLayerID = sortingLayerId;
        b.mr.sortingOrder = sortingOrder;
        b.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        b.mr.receiveShadows = false;
        b.mr.enabled = false;
        return b;
    }

    // alpha: 레인 보임 정도 (등장·프리키·곡 종료 페이드), expand: 레인 펼침 비율 (0 ~ 1)
    public void Tick(float alpha, float expand)
    {
        Texture src = CurrentVideoTexture();
        float bgBright = gm.bgRenderer != null && gm.bgRenderer.enabled ? gm.bgRenderer.color.maxColorComponent : 0f;
        float a = alpha * Mathf.Clamp01(bgBright);
        if (src == null || a <= 0.001f)
        {
            mr.enabled = false;
            return;
        }

        UpdateBlur(src);
        UpdateQuad(Mathf.Max(0.001f, expand));
        showMat.mainTexture = rtA;
        showMat.SetColor("_Color", new Color(1f, 1f, 1f, a));
        showMat.SetFloat("_ScreenMode", Diverse ? 1f : 0f);
        showMat.SetFloat("_Warp", Diverse ? 1f : 0f);
        if (Diverse) showMat.SetVector("_ScreenToBg", ScreenToBg());
        mr.enabled = true;
    }

    private Texture CurrentVideoTexture()
    {
        if (gm == null || gm.mvPlayer == null || gm.bgRenderer == null) return null;
        if (CoreManager.Instance == null || !CoreManager.Instance.IsMvOn) return null;
        VideoPlayer vp = gm.mvPlayer;
        if (string.IsNullOrEmpty(vp.url) || !vp.isPrepared) return null;
        return vp.texture;
    }

    private void UpdateBlur(Texture src)
    {
        int ds = blurMat != null ? Downsample : DownsampleNoShader;
        int w = Mathf.Max(16, src.width / ds);
        int h = Mathf.Max(16, src.height / ds);
        bool resized = Ensure(ref rtA, w, h) | Ensure(ref rtB, w, h);

        // 영상 프레임이 그대로면 다시 계산하지 않음 (일시정지 · 같은 프레임)
        long frame = gm.mvPlayer.frame;
        if (!resized && frame == lastFrame && src == lastSource) return;
        lastFrame = frame;
        lastSource = src;

        Graphics.Blit(src, rtA);
        if (blurMat == null) return;
        for (int i = 0; i < Iterations; i++)
        {
            float s = strength * (i + 1);
            blurMat.SetVector("_BlurDir", new Vector4(s, 0f, 0f, 0f));
            Graphics.Blit(rtA, rtB, blurMat);
            blurMat.SetVector("_BlurDir", new Vector4(0f, s, 0f, 0f));
            Graphics.Blit(rtB, rtA, blurMat);
        }
    }

    // 화면 UV(0~1) → 배경 스프라이트 UV : 평면 카메라가 보는 영역과 배경 bounds 의 관계
    private Vector4 ScreenToBg()
    {
        Camera main = gm.SceneCamera;
        if (main == null || !main.orthographic) return new Vector4(1f, 1f, 0f, 0f);
        Bounds bb = gm.bgRenderer.bounds;
        float oh = main.orthographicSize * 2f, ow = oh * main.aspect;
        float sx = Mathf.Max(0.0001f, bb.size.x), sy = Mathf.Max(0.0001f, bb.size.y);
        Vector3 cp = main.transform.position;
        return new Vector4(ow / sx, oh / sy, (cp.x - ow * 0.5f - bb.min.x) / sx, (cp.y - oh * 0.5f - bb.min.y) / sy);
    }

    private static bool Ensure(ref RenderTexture rt, int w, int h)
    {
        if (rt != null && rt.width == w && rt.height == h) return false;
        if (rt != null) { rt.Release(); Destroy(rt); }
        rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
        {
            name = "LaneBlurRT",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
        return true;
    }

    // 레인 사각형(펼침 반영) + 배경 스프라이트 안에서의 위치를 UV 로
    private void UpdateQuad(float k)
    {
        float x0 = centerX + (left - centerX) * k;
        float x1 = centerX + (right - centerX) * k;
        Bounds bb = gm.bgRenderer.bounds;
        float z = gm.bgRenderer.transform.position.z - 0.5f; // 배경 바로 앞
        Vector3[] w = {
            new Vector3(x0, bottom, z), new Vector3(x0, top, z),
            new Vector3(x1, top, z),    new Vector3(x1, bottom, z)
        };
        for (int i = 0; i < 4; i++)
        {
            verts[i] = transform.InverseTransformPoint(w[i]);
            uvs[i] = new Vector2((w[i].x - bb.min.x) / Mathf.Max(0.0001f, bb.size.x),
                                 (w[i].y - bb.min.y) / Mathf.Max(0.0001f, bb.size.y));
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (rtA != null) { rtA.Release(); Destroy(rtA); }
        if (rtB != null) { rtB.Release(); Destroy(rtB); }
        if (showMat != null) Destroy(showMat);
        if (blurMat != null) Destroy(blurMat);
        if (mesh != null) Destroy(mesh);
    }
}