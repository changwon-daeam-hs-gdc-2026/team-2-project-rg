using UnityEngine;
using UnityEngine.UI;

// ============================================================
// 평행사변형 UI 그래픽 (스프라이트 없이 메쉬로 그림)
//  - skew     : 윗변이 아랫변보다 오른쪽으로 밀리는 정도(px). 음수면 왼쪽으로 기울어짐
//  - FillAmount / FillFromRight : 기울어진 변과 같은 각도로 왼쪽→오른쪽 / 오른쪽→왼쪽으로 채워짐
//  곡 선택창 헤더의 EZ / HD / INS 난이도 표시에 사용
// ============================================================
[RequireComponent(typeof(CanvasRenderer))]
public class ParallelogramGraphic : MaskableGraphic
{
    [Tooltip("윗변이 오른쪽으로 밀리는 정도 (px). 음수면 반대로 기울어짐")]
    [SerializeField] private float skew = 14f;
    [Range(0f, 1f)] [SerializeField] private float fillAmount = 1f;
    [SerializeField] private bool fillFromRight = false;

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