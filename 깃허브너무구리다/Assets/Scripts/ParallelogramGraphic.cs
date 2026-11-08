using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 평행사변형 UI 그래픽 (스프라이트 없이 메쉬로 그림)
//  - skew     : 윗변이 아랫변보다 오른쪽으로 밀리는 정도(px). 음수면 왼쪽으로 기울어짐
//  - FillAmount / FillFromRight : 기울어진 변과 같은 각도로 왼쪽→오른쪽 / 오른쪽→왼쪽으로 채워짐
//  - SpecialGradient : 한 가지 색 대신 왼쪽 → 오른쪽 솜사탕 그라데이션 (SPECIAL 표시용, color 는 곱해짐)
//  곡 선택창 헤더의 EZ / HD / INS 난이도 표시, SPECIAL 목록 헤더에 사용
// ============================================================
[RequireComponent(typeof(CanvasRenderer))]
public class ParallelogramGraphic : MaskableGraphic
{
    [Tooltip("윗변이 오른쪽으로 밀리는 정도 (px). 음수면 반대로 기울어짐")]
    [SerializeField] private float skew = 14f;
    [Range(0f, 1f)] [SerializeField] private float fillAmount = 1f;
    [SerializeField] private bool fillFromRight = false;
    [SerializeField] private bool specialGradient = false;

    public bool SpecialGradient
    {
        get { return specialGradient; }
        set { if (specialGradient == value) return; specialGradient = value; SetVerticesDirty(); }
    }

    public float Skew
    {
        get { return skew; }
        set { if (Mathf.Approximately(skew, value)) return; skew = value; SetVerticesDirty(); }
    }

    public float FillAmount
    {
        get { return fillAmount; }
        set
        {
            float v = Mathf.Clamp01(value);
            if (Mathf.Approximately(fillAmount, v)) return;
            fillAmount = v;
            SetVerticesDirty();
        }
    }

    public bool FillFromRight
    {
        get { return fillFromRight; }
        set { if (fillFromRight == value) return; fillFromRight = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (fillAmount <= 0f) return;

        Rect r = GetPixelAdjustedRect();
        float s = Mathf.Clamp(skew, -r.width, r.width);
        float baseW = r.width - Mathf.Abs(s);

        float bottomLeft = s >= 0f ? r.xMin : r.xMin - s;
        float topLeft = bottomLeft + s;

        float a = fillFromRight ? baseW * (1f - fillAmount) : 0f;
        float b = fillFromRight ? baseW : baseW * fillAmount;

        Color32 c = color;
        if (specialGradient)
        {
            // 가로로 여러 조각으로 나눠 조각마다 그라데이션 색 (채워진 부분만)
            const int N = 12;
            for (int i = 0; i <= N; i++)
            {
                float x = Mathf.Lerp(a, b, (float)i / N);
                Color g = DifficultyHelper.SpecialAt(0.85f * x / Mathf.Max(1f, baseW)) * color;
                g.a = color.a;
                float u = x / Mathf.Max(1f, baseW);
                vh.AddVert(new Vector3(bottomLeft + x, r.yMin), g, new Vector2(u, 0f));
                vh.AddVert(new Vector3(topLeft + x, r.yMax), g, new Vector2(u, 1f));
                if (i > 0)
                {
                    int k = i * 2;
                    vh.AddTriangle(k - 2, k - 1, k + 1);
                    vh.AddTriangle(k + 1, k, k - 2);
                }
            }
            return;
        }
        vh.AddVert(new Vector3(bottomLeft + a, r.yMin), c, new Vector2(0f, 0f));
        vh.AddVert(new Vector3(topLeft + a, r.yMax), c, new Vector2(0f, 1f));
        vh.AddVert(new Vector3(topLeft + b, r.yMax), c, new Vector2(1f, 1f));
        vh.AddVert(new Vector3(bottomLeft + b, r.yMin), c, new Vector2(1f, 0f));
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}