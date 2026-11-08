using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 슬라이드 (레인을 넘나드는 롱노트) 몸통 그리기
//  머리 NoteObject 의 자식. 매 프레임 머리 ~ 끝 경로를 따라 띠 모양 메쉬를 만듦
//   - 세로 위치: 일반 노트와 같은 스크롤 거리 (변속 · STOP 그대로 따라감)
//   - 가로 위치: SlidePath.Evaluate (점 사이 직선 / 처음 천천히 / 끝 천천히)
//   - 그림 · 색: 일반 롱노트 몸통과 같은 그림 · 색 (머리 NoteObject 의 몸통 SpriteRenderer 를 그대로 씀, 그건 숨김)
//   - SIMPLE: 판정선 아래는 직접 잘라냄 / SIMPLE-DIVERSE: 원근 재질이 잘라냄
// ============================================================
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SlideBody : MonoBehaviour
{
    private const float LaneW = 0.8f;
    private const float Inset = 0.02f;            // 일반 롱노트 몸통과 같은 폭 (레인 폭의 95%)
    private const int StraightSteps = 6;           // 직선 구간도 변속에 따라 휘어 보일 수 있어 몇 조각으로
    private const int CurveSteps = 18;

    private MeshFilter mf;
    private MeshRenderer mr;
    private Mesh mesh;
    private MaterialPropertyBlock mpb;
    private static Material simpleMat;
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    private readonly List<Vector3> verts = new List<Vector3>();
    private readonly List<Color> cols = new List<Color>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> tris = new List<int>();
    private readonly List<double> times = new List<double>();

    public static SlideBody Create(Transform parent)
    {
        GameObject go = new GameObject("SlideBody", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        SlideBody b = go.AddComponent<SlideBody>();
        b.Init();
        return b;
    }

    private void Init()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        mesh = new Mesh { name = "SlideBody" };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;
        mpb = new MaterialPropertyBlock();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.enabled = false;
    }

    public void Hide() { if (mr != null && mr.enabled) mr.enabled = false; }

    // fromTime ~ 끝 구간을 그림. laneX = 레인 가운데 x (0 ~ 3)
    public void Draw(NoteData head, float[] laneX, Sprite sprite, Color color, int sortingLayerId, int order, double fromTime, DiversePlayfield pf)
    {
        GameManager gm = GameManager.Instance;
        if (head == null || head.Slide == null || gm == null || sprite == null || laneX == null || laneX.Length < 4) { Hide(); return; }
        SlidePath path = head.Slide;
        double t0 = System.Math.Max(fromTime, path.StartTime), t1 = path.EndTime;
        if (t1 - t0 <= 1e-4) { Hide(); return; }

        // ---- 나눌 시각들: 점마다 끊고, 곡선 구간은 잘게 ----
        times.Clear();
        times.Add(t0);
        for (int i = 0; i < path.Points.Count - 1; i++)
        {
            double a = path.Points[i].Time, b = path.Points[i + 1].Time;
            if (b <= t0 || a >= t1) continue;
            int steps = path.Points[i].Curve == SlideCurve.Straight ? StraightSteps : CurveSteps;
            for (int k = 1; k <= steps; k++)
            {
                double t = a + (b - a) * k / steps;
                if (t > t0 + 1e-6 && t < t1 - 1e-6) times.Add(t);
            }
        }
        times.Add(t1);

        // ---- 띠 만들기 (아래 → 위) ----
        verts.Clear(); cols.Clear(); uvs.Clear(); tris.Clear();
        float hitY = gm.hitLineY;
        double curDist = gm.GetCurrentScrollDist(head.Layer);
        float x0 = laneX[0] - LaneW * 0.5f;
        bool clip = pf == null;                         // SIMPLE: 판정선 아래는 직접 자름
        Rect tr = sprite.textureRect;
        Texture tex = sprite.texture;
        float u0 = tr.xMin / tex.width, u1 = tr.xMax / tex.width, vm = tr.center.y / tex.height;

        bool havePrev = false;
        float pY = 0f, pL = 0f, pR = 0f;
        for (int i = 0; i < times.Count; i++)
        {
            double t = times[i];
            float y = hitY + (float)(gm.GetScrollDistance(t, head.Layer) - curDist) * 0.01f;
            path.Evaluate(t, out float l, out float r);
            float xl = x0 + l * LaneW + Inset, xr = x0 + r * LaneW - Inset;
            if (clip && y < hitY)
            {
                pY = y; pL = xl; pR = xr; havePrev = true;
                continue;                               // 판정선 아래 (다음 점에서 경계를 잘라 넣음)
            }
            if (clip && havePrev && pY < hitY && verts.Count == 0)
            {
                float k = Mathf.InverseLerp(pY, y, hitY);
                AddRow(Mathf.Lerp(pL, xl, k), Mathf.Lerp(pR, xr, k), hitY, color, u0, u1, vm);
            }
            AddRow(xl, xr, y, color, u0, u1, vm);
            havePrev = true; pY = y; pL = xl; pR = xr;
        }
        if (verts.Count < 4) { Hide(); return; }
        for (int i = 0; i + 3 < verts.Count; i += 2)
        {
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i + 1); tris.Add(i + 2); tris.Add(i + 3);
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // ---- 재질 · 순서 ----
        Material m = pf != null ? pf.MaterialFor(tex, true, false) : SimpleMaterial();
        if (mr.sharedMaterial != m) mr.sharedMaterial = m;
        mpb.Clear();
        mpb.SetTexture(MainTexId, tex);
        mr.SetPropertyBlock(mpb);
        mr.sortingLayerID = sortingLayerId;
        mr.sortingOrder = order;
        mr.enabled = true;
    }

    private void AddRow(float xl, float xr, float y, Color c, float u0, float u1, float vm)
    {
        verts.Add(transform.InverseTransformPoint(new Vector3(xl, y, 0f)));
        verts.Add(transform.InverseTransformPoint(new Vector3(xr, y, 0f)));
        cols.Add(c); cols.Add(c);
        uvs.Add(new Vector2(u0, vm)); uvs.Add(new Vector2(u1, vm));
    }

    private static Material SimpleMaterial()
    {
        if (simpleMat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            simpleMat = new Material(sh) { name = "SlideBody (Sprites/Default)" };
        }
        return simpleMat;
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}