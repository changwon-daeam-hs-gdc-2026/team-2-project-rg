using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// ============================================================
// 영역(Area) 채보 화면 (SIMPLE-DIVERSE 전용, SimpleGameSkin 이 영역 채보일 때 만든다)
//
//  - 채보의 AREA 줄로 정한 '영역' 레인에 사선 무늬 배경을 그린다 (레인 위 · 노트 아래)
//  - 영역이 없는 구간에만 원래의 반투명 검은 레인 배경을 그린다
//     → 영역이 화면 위에서 내려오기 시작하면 그 지점부터 위쪽은 일반 레인 배경이 사라지고,
//       영역이 끝나는 지점(AREA, ..., 0000)부터 다시 일반 레인 배경이 나온다
//  - 모든 조각은 노트와 똑같이 스크롤되고, 원근 · 레인 접기(FOLD)도 그대로 따라간다
//  - 메시 5장(일반 배경 / 영역 바탕 / 사선 무늬 / 테두리 / 영역 판정선)을 매 프레임 다시 만든다 (조각 수가 적어 가벼움)
//  - 레인 접기(FOLD): 일반 레인 배경만 접히고, 영역(바탕 · 무늬 · 테두리)은 접히지 않고 그대로 그려진다.
//    접히는 동안에는 영역 레인 위에 판정선을 따로 그려 준다 (원래 판정선은 레인과 함께 접혀 사라지므로)
// ============================================================
public class AreaLaneView : MonoBehaviour
{
    // 영역 모양: 검은 판 + 회색 사선 + 밝은 흰 테두리 + 테두리 안쪽으로 번지는 빛 (NoteArt 의 색 · 크기 사용, 에디터와 같음)
    private const float Tile = 0.32f;               // 사선 무늬 한 칸 크기 (월드 유닛)
    private const float EdgeW = NoteArt.AreaEdgeWidth;
    private const float CapH = 0.06f;               // 영역 시작 / 끝 가로선 두께
    private const float GlowW = NoteArt.AreaEdgeGlowWidth;

    private SimpleGameSkin skin;
    private GameManager gm;
    private Layer bg, areaBg, stripe, edge, hitLine, glow;
    private Color bgColor;

    private class Layer
    {
        public Mesh mesh; public MeshRenderer mr;
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> c = new List<Color>();
        public readonly List<int> t = new List<int>();
        public void Clear() { v.Clear(); uv.Clear(); c.Clear(); t.Clear(); }
        public void Apply()
        {
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetColors(c); mesh.SetTriangles(t, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);   // 원근 셰이더가 위치를 바꾸므로 잘리지 않게 크게
            mr.enabled = v.Count > 0;
        }
    }

    public static AreaLaneView Create(SimpleGameSkin skin, GameManager gm)
    {
        GameObject go = new GameObject("AreaLaneView");
        go.transform.SetParent(skin.WorldRoot, false);
        AreaLaneView view = go.AddComponent<AreaLaneView>();
        view.skin = skin;
        view.gm = gm;
        view.Build();
        return view;
    }

    private void Build()
    {
        float opacity = Mathf.Clamp01(SettingsStore.Current.LaneOpacity);
        bgColor = skin.IsDiverse ? new Color(0.03f, 0.04f, 0.075f, opacity) : new Color(0.035f, 0.045f, 0.07f, opacity);
        Texture white = skin.PixelSprite.texture;
        bg = MakeLayer("AreaLaneBg", white, -20, true);                                   // 일반 레인 배경: 접힘
        areaBg = MakeLayer("AreaDarkBg", white, -20, false);                              // 영역: 접히지 않음
        stripe = MakeLayer("AreaStripes", NoteArt.AreaStripeSprite.texture, -19, false);
        edge = MakeLayer("AreaEdges", white, -18, false);
        glow = MakeLayer("AreaEdgeGlow", NoteArt.HGradientSprite.texture, -18, false);       // 테두리 안쪽 빛 (가로 그라데이션)
        hitLine = MakeLayer("AreaHitLine", white, -17, false);                         // 노트 아래
    }

    private Layer MakeLayer(string name, Texture tex, int order, bool fold)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        Layer l = new Layer();
        l.mesh = new Mesh { name = name };
        l.mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = l.mesh;
        l.mr = go.AddComponent<MeshRenderer>();
        l.mr.shadowCastingMode = ShadowCastingMode.Off;
        l.mr.receiveShadows = false;
        if (skin.Playfield != null) l.mr.sharedMaterial = skin.Playfield.MaterialFor(tex, false, fold);
        else
        {
            Shader sh = Shader.Find("Sprites/Default");
            l.mr.sharedMaterial = new Material(sh) { mainTexture = tex };
        }
        if (skin.LaneBgRenderer != null) l.mr.sortingLayerID = skin.LaneBgRenderer.sortingLayerID;
        l.mr.sortingOrder = order;
        l.mr.enabled = false;
        return l;
    }

    void LateUpdate()
    {
        if (gm == null || skin == null) return;
        bool active = gm.HasAreas;
        if (skin.LaneBgRenderer != null && active) skin.LaneBgRenderer.enabled = false; // 일반 레인 배경은 여기서 대신 그림
        bg.Clear(); areaBg.Clear(); stripe.Clear(); edge.Clear(); hitLine.Clear(); glow.Clear();
        if (active) BuildPieces();
        bg.Apply(); areaBg.Apply(); stripe.Apply(); edge.Apply(); hitLine.Apply(); glow.Apply();
    }

    private void BuildPieces()
    {
        float vis = skin.LaneVisibility;
        if (vis <= 0.001f) return;

        float hitY = gm.hitLineY;
        float y0 = hitY - 3f, y1 = hitY + 30f;    // 원래 레인 배경과 같은 범위
        double nowDist = gm.GetCurrentScrollDist(NoteLayer.Top);
        List<AreaEvent> ev = gm.AreaEvents;

        // 사선 무늬 기준점: 채보 0초 위치 (모든 구간이 같은 기준 → 구간이 바뀌어도 무늬가 끊기지 않음)
        stripeAnchorY = YAt(0.0, nowDist);

        double prevT = double.NegativeInfinity;
        int prevMask = 0, beforeMask = 0;
        for (int i = 0; i <= ev.Count; i++)
        {
            double tEnd = i < ev.Count ? ev[i].Time : double.PositiveInfinity;
            int nextMask = i < ev.Count ? ev[i].Mask : 0;
            Emit(prevT, tEnd, prevMask, beforeMask, nextMask, y0, y1, nowDist, vis);
            if (i < ev.Count) { prevT = ev[i].Time; beforeMask = prevMask; prevMask = ev[i].Mask; }
        }

        // 레인이 접히는 동안: 지금 영역인 레인 위에만 판정선 (접힌 만큼 진하게)
        float folded = 1f - gm.LaneFoldAt(gm.currentPlayTime);
        int maskNow = gm.AreaMaskAt(gm.currentPlayTime);
        if (folded > 0.001f && maskNow != 0)
        {
            Color hc = new Color(1f, 1f, 1f, 0.95f * folded * vis);
            float L = skin.LaneLeft, W = skin.LaneWidth;
            for (int lane = 0; lane < 4; lane++)
            {
                if (!AreaMask.Has(maskNow, lane)) continue;
                int end = lane;
                while (end + 1 < 4 && AreaMask.Has(maskNow, end + 1)) end++;
                Quad(hitLine, L + lane * W, hitY - 0.025f, L + (end + 1) * W, hitY + 0.025f, hc, false, 0f);
                lane = end;
            }
        }
    }

    private float YAt(double t, double nowDist)
    {
        return gm.hitLineY + (float)(gm.GetScrollDistance(t, NoteLayer.Top) - nowDist) * 0.01f;
    }

    private float stripeAnchorY;

    // mask: 이 구간의 영역 레인,  beforeMask / afterMask: 바로 앞 / 뒤 구간의 영역 레인
    //  [개선] 붙어 있는 영역은 한 덩어리로: 가로 시작선 · 끝선은 '영역이 새로 시작되는 / 끝나는 레인'에만 긋는다
    //   (예: 1번 레인 영역이 계속되는 중에 2번 레인 영역이 시작되면 2번 레인 위에만 시작선, 1번 위에는 선 없음)
    private void Emit(double tA, double tB, int mask, int beforeMask, int afterMask, float y0, float y1, double nowDist, float vis)
    {
        bool openStart = double.IsNegativeInfinity(tA), openEnd = double.IsPositiveInfinity(tB);
        float ya = openStart ? y0 - 1f : YAt(tA, nowDist);
        float yb = openEnd ? y1 + 1f : YAt(tB, nowDist);
        if (yb <= y0 || ya >= y1 || yb - ya < 0.0005f) return;
        float cy0 = Mathf.Max(ya, y0), cy1 = Mathf.Min(yb, y1);

        float L = skin.LaneLeft, W = skin.LaneWidth;
        if (mask == 0)
        {
            // 영역이 없는 구간: 원래 레인 배경
            Quad(bg, L - 0.03f, cy0, skin.LaneRight + 0.03f, cy1, Fade(bgColor, vis), false, 0f);
            return;
        }

        Color dark = Fade(NoteArt.AreaBase, vis);
        Color stripeC = Fade(NoteArt.AreaStripe, vis);
        Color edgeC = Fade(NoteArt.AreaEdge, vis);
        Color glowC = Fade(NoteArt.AreaEdgeGlow, vis);

        // 이어진 레인끼리 한 덩어리로
        for (int lane = 0; lane < 4; lane++)
        {
            if (!AreaMask.Has(mask, lane)) continue;
            int end = lane;
            while (end + 1 < 4 && AreaMask.Has(mask, end + 1)) end++;
            float x0 = L + lane * W, x1 = L + (end + 1) * W;

            Quad(areaBg, x0, cy0, x1, cy1, dark, false, 0f);
            Quad(stripe, x0, cy0, x1, cy1, stripeC, true, stripeAnchorY);      // 무늬는 채보 0초에 고정 → 노트와 같이 흘러가고 구간 사이에서 끊기지 않음
            Quad(edge, x0 - EdgeW * 0.5f, cy0, x0 + EdgeW * 0.5f, cy1, edgeC, false, 0f);
            Quad(edge, x1 - EdgeW * 0.5f, cy0, x1 + EdgeW * 0.5f, cy1, edgeC, false, 0f);
            float gw = Mathf.Min(GlowW, (x1 - x0) * 0.45f);
            GradQuad(glow, x0, cy0, x0 + gw, cy1, glowC, false);                // 왼쪽 테두리 → 안쪽으로 옅어짐
            GradQuad(glow, x1 - gw, cy0, x1, cy1, glowC, true);                 // 오른쪽 테두리 → 안쪽으로 옅어짐
            if (!openStart && ya >= y0) CapRuns(lane, end, beforeMask, ya, ya + CapH, edgeC);   // 영역 시작선 (새로 시작된 레인만)
            if (!openEnd && yb <= y1) CapRuns(lane, end, afterMask, yb - CapH, yb, edgeC);      // 영역 끝선 (여기서 끝나는 레인만)
            lane = end;
        }
    }

    private static Color Fade(Color c, float a) { return new Color(c.r, c.g, c.b, c.a * a); }

    // [lane0 ~ lane1] 중 other 에 없는 레인들에만 가로선 (이어진 레인끼리 한 줄로)
    private void CapRuns(int lane0, int lane1, int other, float ya, float yb, Color c)
    {
        float L = skin.LaneLeft, W = skin.LaneWidth;
        for (int l = lane0; l <= lane1; l++)
        {
            if (AreaMask.Has(other, l)) continue;
            int e = l;
            while (e + 1 <= lane1 && !AreaMask.Has(other, e + 1)) e++;
            Quad(edge, L + l * W - EdgeW * 0.5f, ya, L + (e + 1) * W + EdgeW * 0.5f, yb, c, false, 0f);
            l = e;
        }
    }

    // 가로 그라데이션 사각형 (mirror = 오른쪽이 진함)
    private void GradQuad(Layer l, float x0, float y0, float x1, float y1, Color c, bool mirror)
    {
        float cx = (skin.LaneLeft + skin.LaneRight) * 0.5f, k = skin.LaneExpandNow;
        float ax = cx + (x0 - cx) * k, bx = cx + (x1 - cx) * k;
        int b = l.v.Count;
        l.v.Add(new Vector3(ax, y0, 0f)); l.v.Add(new Vector3(bx, y0, 0f));
        l.v.Add(new Vector3(bx, y1, 0f)); l.v.Add(new Vector3(ax, y1, 0f));
        float u0 = mirror ? 1f : 0f, u1 = mirror ? 0f : 1f;
        l.uv.Add(new Vector2(u0, 0.5f)); l.uv.Add(new Vector2(u1, 0.5f));
        l.uv.Add(new Vector2(u1, 0.5f)); l.uv.Add(new Vector2(u0, 0.5f));
        l.c.Add(c); l.c.Add(c); l.c.Add(c); l.c.Add(c);
        l.t.Add(b); l.t.Add(b + 2); l.t.Add(b + 1);
        l.t.Add(b); l.t.Add(b + 3); l.t.Add(b + 2);
    }

    // 월드 사각형 하나 (레인 펼침 연출 배율 반영). pattern: 사선 무늬 UV (uvAnchorY = 무늬 기준 y)
    private void Quad(Layer l, float x0, float y0, float x1, float y1, Color c, bool pattern, float uvAnchorY)
    {
        float cx = (skin.LaneLeft + skin.LaneRight) * 0.5f, k = skin.LaneExpandNow;
        float ax = cx + (x0 - cx) * k, bx = cx + (x1 - cx) * k;
        int b = l.v.Count;
        l.v.Add(new Vector3(ax, y0, 0f)); l.v.Add(new Vector3(bx, y0, 0f));
        l.v.Add(new Vector3(bx, y1, 0f)); l.v.Add(new Vector3(ax, y1, 0f));
        if (pattern)
        {
            l.uv.Add(new Vector2(x0 / Tile, (y0 - uvAnchorY) / Tile)); l.uv.Add(new Vector2(x1 / Tile, (y0 - uvAnchorY) / Tile));
            l.uv.Add(new Vector2(x1 / Tile, (y1 - uvAnchorY) / Tile)); l.uv.Add(new Vector2(x0 / Tile, (y1 - uvAnchorY) / Tile));
        }
        else
        {
            Vector2 m = new Vector2(0.5f, 0.5f);
            l.uv.Add(m); l.uv.Add(m); l.uv.Add(m); l.uv.Add(m);
        }
        l.c.Add(c); l.c.Add(c); l.c.Add(c); l.c.Add(c);
        l.t.Add(b); l.t.Add(b + 2); l.t.Add(b + 1);
        l.t.Add(b); l.t.Add(b + 3); l.t.Add(b + 2);
    }

    void OnDestroy()
    {
        foreach (var l in new[] { bg, areaBg, stripe, edge, hitLine, glow })
            if (l != null && l.mesh != null) Destroy(l.mesh);
    }
}