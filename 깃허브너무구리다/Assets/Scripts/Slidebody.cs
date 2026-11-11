using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 슬라이드 (레인을 넘나드는 롱노트) 몸통 그리기
//  머리 NoteObject 의 자식. 매 프레임 머리 ~ 끝 경로를 따라 띠 모양 메쉬를 만듦
//   - 세로 위치: 일반 노트와 같은 스크롤 거리 (변속 · STOP 그대로 따라감)
//   - 가로 위치: SlidePath.Evaluate (점 사이 직선 / 처음 천천히 / 끝 천천히 / 직각)
//   - 직각: 다음 점 시각까지 제자리로 곧게 → 그 높이에서 몸통과 같은 굵기의 가로 띠로 옆 레인까지 꺾임
//           꺾이는 모서리는 대각선으로 나눠 몸통 무늬가 90도 돌아 이어지게 (관이 꺾인 모양)
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
    // 직각 꺾임: 가로 띠 두께 = 몸통 폭 (앞 · 뒤 점과 가까우면 그 간격까지만) — 에디터와 같은 규칙

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
    private readonly List<float> rowY = new List<float>(), rowL = new List<float>(), rowR = new List<float>();

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
            int c = path.Points[i].Curve;
            int steps = c == SlideCurve.Straight || c == SlideCurve.Corner ? StraightSteps : CurveSteps;
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

        layerNow = head.Layer; curDistNow = curDist; hitYNow = hitY; clipNow = clip;
        colorNow = color; u0Now = u0; u1Now = u1; vmNow = vm;

        // 1) 줄(가로 한 줄 = 높이 y 에서 왼쪽 xl ~ 오른쪽 xr)을 이어 몸통을 만들고,
        //    직각 꺾임에서는 줄을 끊고 '팔꿈치' 조각을 따로 붙임
        rowY.Clear(); rowL.Clear(); rowR.Clear();
        for (int i = 0; i < times.Count; i++)
        {
            double t = times[i];
            float y = YAt(gm, t);
            SlidePoint cornerFrom = CornerEndingAt(path, t);
            if (cornerFrom != null && t > t0 + 1e-6)
            {
                SlidePoint to = NextPoint(path, cornerFrom);
                float al = x0 + cornerFrom.Lane * LaneW + Inset, ar = x0 + (cornerFrom.Lane + cornerFrom.Width) * LaneW - Inset;
                float bl = x0 + to.Lane * LaneW + Inset, br = x0 + (to.Lane + to.Width) * LaneW - Inset;
                // 가로 띠 두께 = 몸통 폭 (관처럼 같은 굵기로 꺾임). 앞 · 뒤 점과 너무 가까우면 그 간격에 맞춰 얇게
                float thick = Mathf.Min(ar - al, br - bl);
                float yPrev = YAt(gm, cornerFrom.Time);
                thick = Mathf.Min(thick, y - yPrev);
                SlidePoint after = NextPoint(path, to);
                if (after != to) thick = Mathf.Min(thick, YAt(gm, after.Time) - y);
                thick = Mathf.Max(0.02f, thick);
                float lo = y - thick * 0.5f, hi = y + thick * 0.5f;
                // 앞 레인 몸통은 띠 아래 변(lo)까지
                while (rowY.Count > 1 && rowY[rowY.Count - 1] > lo) RemoveLastRow();
                AddRawRow(lo, al, ar);
                FlushRun();
                EmitCorner(al, ar, bl, br, lo, hi);
                // 다음 레인 몸통은 띠 위 변(hi)부터
                AddRawRow(hi, bl, br);
                continue;
            }
            path.Evaluate(t, out float l, out float r);
            float xl = x0 + l * LaneW + Inset, xr = x0 + r * LaneW - Inset;
            if (rowY.Count > 0 && y < rowY[rowY.Count - 1]) y = rowY[rowY.Count - 1];   // 띠 위 변보다 아래로 내려오지 않게
            AddRawRow(y, xl, xr);
        }
        FlushRun();
        if (tris.Count < 3) { Hide(); return; }

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

    private void AddRawRow(float y, float xl, float xr) { rowY.Add(y); rowL.Add(xl); rowR.Add(xr); }
    private void RemoveLastRow() { int k = rowY.Count - 1; rowY.RemoveAt(k); rowL.RemoveAt(k); rowR.RemoveAt(k); }

    // ---- 이번 Draw 동안 쓰는 값 ----
    private int layerNow;
    private double curDistNow;
    private float hitYNow, u0Now, u1Now, vmNow;
    private bool clipNow;
    private Color colorNow;
    private readonly List<Vector3> polyP = new List<Vector3>(), clipP = new List<Vector3>();   // x, y, z = 텍스처 u

    private float YAt(GameManager gm, double t)
    {
        return hitYNow + (float)(gm.GetScrollDistance(t, layerNow) - curDistNow) * 0.01f;
    }

    // 모아 둔 줄들을 사각형으로 이어 붙이고 비움 (u: 왼쪽 u0 → 오른쪽 u1)
    private void FlushRun()
    {
        for (int i = 0; i + 1 < rowY.Count; i++)
        {
            if (rowY[i + 1] - rowY[i] <= 1e-5f) continue;
            polyP.Clear();
            polyP.Add(new Vector3(rowL[i], rowY[i], u0Now));
            polyP.Add(new Vector3(rowL[i + 1], rowY[i + 1], u0Now));
            polyP.Add(new Vector3(rowR[i + 1], rowY[i + 1], u1Now));
            polyP.Add(new Vector3(rowR[i], rowY[i], u1Now));
            EmitPoly();
        }
        rowY.Clear(); rowL.Clear(); rowR.Clear();
    }

    // 직각 꺾임 조각: 앞 레인 팔꿈치 + 가로 띠 + 다음 레인 팔꿈치
    //  팔꿈치 사각형은 대각선(안쪽 모서리 ↔ 바깥 모서리)으로 나눠, 세로 몸통 쪽은 무늬가 가로로(u = x),
    //  가로 띠 쪽은 무늬가 세로로(u = y) 흐르게 → 몸통 무늬가 90도 돌아 이어짐 (바깥 테두리끼리 · 안쪽 테두리끼리 이어짐)
    private void EmitCorner(float al, float ar, float bl, float br, float lo, float hi)
    {
        float a = u0Now, b = u1Now;
        if (bl > al)
        {
            // 오른쪽으로 꺾임: 바깥 테두리 = 세로의 왼쪽(u0) → 띠의 위쪽(u0)
            Tri(al, lo, a, ar, lo, b, al, hi, a);                    // 앞 팔꿈치: 세로 몸통 쪽
            Tri(ar, lo, b, ar, hi, a, al, hi, a);                    //            띠 쪽
            if (bl > ar) Quad(ar, lo, b, bl, lo, b, bl, hi, a, ar, hi, a);   // 띠
            Tri(bl, lo, b, br, lo, b, bl, hi, a);                    // 다음 팔꿈치: 띠 쪽
            Tri(br, lo, b, br, hi, b, bl, hi, a);                    //              세로 몸통 쪽
        }
        else
        {
            // 왼쪽으로 꺾임: 바깥 테두리 = 세로의 오른쪽(u1) → 띠의 위쪽(u1)
            Tri(al, lo, a, ar, lo, b, ar, hi, b);                    // 앞 팔꿈치: 세로 몸통 쪽
            Tri(al, lo, a, ar, hi, b, al, hi, b);                    //            띠 쪽
            if (br < al) Quad(br, lo, a, al, lo, a, al, hi, b, br, hi, b);   // 띠
            Tri(bl, lo, a, br, lo, a, br, hi, b);                    // 다음 팔꿈치: 띠 쪽
            Tri(bl, lo, a, br, hi, b, bl, hi, a);                    //              세로 몸통 쪽
        }
    }

    private void Tri(float x0, float y0, float ua, float x1, float y1, float ub, float x2, float y2, float uc)
    {
        polyP.Clear();
        polyP.Add(new Vector3(x0, y0, ua)); polyP.Add(new Vector3(x1, y1, ub)); polyP.Add(new Vector3(x2, y2, uc));
        EmitPoly();
    }

    private void Quad(float x0, float y0, float ua, float x1, float y1, float ub, float x2, float y2, float uc, float x3, float y3, float ud)
    {
        polyP.Clear();
        polyP.Add(new Vector3(x0, y0, ua)); polyP.Add(new Vector3(x1, y1, ub));
        polyP.Add(new Vector3(x2, y2, uc)); polyP.Add(new Vector3(x3, y3, ud));
        EmitPoly();
    }

    // polyP(볼록 다각형)를 SIMPLE 이면 판정선 아래를 잘라낸 뒤 삼각형으로 (모두 같은 감는 방향)
    private void EmitPoly()
    {
        List<Vector3> P = polyP;
        if (clipNow)
        {
            clipP.Clear();
            float H = hitYNow;
            for (int i = 0; i < polyP.Count; i++)
            {
                Vector3 c = polyP[i], n = polyP[(i + 1) % polyP.Count];
                bool cin = c.y >= H, nin = n.y >= H;
                if (cin) clipP.Add(c);
                if (cin != nin)
                {
                    float k = (H - c.y) / (n.y - c.y);
                    clipP.Add(new Vector3(Mathf.Lerp(c.x, n.x, k), H, Mathf.Lerp(c.z, n.z, k)));
                }
            }
            P = clipP;
        }
        if (P.Count < 3) return;
        // 넓이 부호로 감는 방향 확인 (예전 띠와 같은 시계 방향으로 맞춤)
        float area = 0f;
        for (int i = 0; i < P.Count; i++) { Vector3 c = P[i], n = P[(i + 1) % P.Count]; area += c.x * n.y - n.x * c.y; }
        if (Mathf.Abs(area) < 1e-7f) return;
        bool flip = area > 0f;   // 반시계면 뒤집음
        int baseIdx = verts.Count;
        for (int i = 0; i < P.Count; i++)
        {
            verts.Add(transform.InverseTransformPoint(new Vector3(P[i].x, P[i].y, 0f)));
            cols.Add(colorNow);
            uvs.Add(new Vector2(P[i].z, vmNow));
        }
        for (int i = 1; i + 1 < P.Count; i++)
        {
            tris.Add(baseIdx);
            if (flip) { tris.Add(baseIdx + i + 1); tris.Add(baseIdx + i); }
            else { tris.Add(baseIdx + i); tris.Add(baseIdx + i + 1); }
        }
    }

    // 시각 t 가 '직각 구간의 끝 점'이면 그 구간의 시작 점 (레인이 실제로 바뀔 때만), 아니면 null
    private static SlidePoint CornerEndingAt(SlidePath path, double t)
    {
        for (int i = 0; i < path.Points.Count - 1; i++)
        {
            SlidePoint a = path.Points[i], b = path.Points[i + 1];
            if (a.Curve != SlideCurve.Corner || System.Math.Abs(b.Time - t) > 1e-6) continue;
            if (a.Lane == b.Lane && a.Width == b.Width) return null;
            return a;
        }
        return null;
    }

    private static SlidePoint NextPoint(SlidePath path, SlidePoint a)
    {
        int i = path.Points.IndexOf(a);
        return i >= 0 && i + 1 < path.Points.Count ? path.Points[i + 1] : a;
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