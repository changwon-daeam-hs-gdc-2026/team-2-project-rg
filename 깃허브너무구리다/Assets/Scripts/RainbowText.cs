using TMPro;
using UnityEngine;

// ============================================================
// ALL PERFECT 무지개 그라데이션 (글자 정점마다 색을 다르게 칠함)
//  ClearEffect 의 ALL PERFECT 글자와 같은 색감.
//  SIMPLE 스킨 PERFECT 판정 글자 · 결과창 ALL PERFECT / PERFECT 에서 함께 사용.
//
//  주의: TMP 는 text / color / alpha 가 바뀌면 정점 색을 원래 색으로 다시 만든다.
//        그래서 무지개가 계속 보이려면 매 프레임 (색·투명도를 바꾼 뒤에) Apply 를 불러야 한다.
// ============================================================
public static class RainbowText
{
    public const float Saturation = 0.32f;  // 채도 (낮을수록 파스텔)
    public const float Span = 0.8f;         // 글자 왼쪽 끝 ~ 오른쪽 끝 사이에 들어가는 색상환 비율
    public const float Speed = 0.12f;       // 초당 색이 흘러가는 양

    public static Color Hue(float hue, float alpha = 1f, float saturation = Saturation)
    {
        Color c = Color.HSVToRGB(Mathf.Repeat(hue, 1f), saturation, 1f);
        c.a = alpha;
        return c;
    }

    public static void Apply(TMP_Text text, float time, float saturation = Saturation, float span = Span, float speed = Speed)
    {
        if (text == null || !text.isActiveAndEnabled) return;
        text.ForceMeshUpdate();
        TMP_TextInfo info = text.textInfo;
        if (info == null || info.characterCount == 0) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ch = info.characterInfo[i];
            if (!ch.isVisible) continue;
            minX = Mathf.Min(minX, ch.bottomLeft.x);
            maxX = Mathf.Max(maxX, ch.topRight.x);
        }
        if (minX > maxX) return;
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
                Color c = Hue(u * span + time * speed, 1f, saturation);
                cols[vi + j] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), cols[vi + j].a);
            }
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }
}