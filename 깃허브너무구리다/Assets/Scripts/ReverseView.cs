using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 게임 모드 REVERSE (SIMPLE 스킨): 노트가 내려오는 방향을 반대로
//  게임 카메라가 그리는 화면을 위아래로만 뒤집음 (좌우 · 레인 순서 · 키는 그대로)
//   → 판정선 · 키 빔 · 판정 이펙트 등 판정 부분 전체가 화면 위쪽, 노트는 아래에서 위로 올라감
//  · 배경(MV 포함)은 뒤집지 않음 (SpriteRenderer.flipY 로 한 번 더 뒤집어 바로 보이게)
//  · 이 카메라로 그리던 화면 UI(Screen Space - Camera)는 Overlay 로 바꿔 글자가 뒤집히지 않게
//    (위치를 월드 좌표에서 계산하는 UI 는 뒤집힌 카메라 기준으로 계산되므로 판정선을 따라 위로 감)
//  SIMPLE-DIVERSE(원근)는 DiversePlayfield 가 따로 위아래를 바꿈
// ============================================================
[DisallowMultipleComponent]
public class ReverseView : MonoBehaviour
{
    private Camera cam;
    private SpriteRenderer bg;
    private float scanTimer;
    private readonly HashSet<Canvas> done = new HashSet<Canvas>();
    private static readonly Matrix4x4 FlipY = Matrix4x4.Scale(new Vector3(1f, -1f, 1f));

    public static void Attach(Camera cam, SpriteRenderer background)
    {
        if (cam == null) return;
        ReverseView v = cam.GetComponent<ReverseView>();
        if (v == null) v = cam.gameObject.AddComponent<ReverseView>();
        v.cam = cam;
        v.bg = background;
        v.Apply();
        v.ScanCanvases();
    }

    // 원래 투영(정사영 크기 · 화면비가 바뀌어도 따라감)에 위아래 뒤집기를 곱함
    private void Apply()
    {
        if (cam == null) return;
        cam.ResetProjectionMatrix();
        cam.projectionMatrix = FlipY * cam.projectionMatrix;
        if (bg != null) bg.flipY = true;
    }

    void LateUpdate()
    {
        Apply();
        scanTimer -= Time.unscaledDeltaTime;
        if (scanTimer <= 0f) { scanTimer = 0.1f; ScanCanvases(); }
    }

    void OnPreCull() { Apply(); }

    // 위아래가 뒤집히면 삼각형의 앞뒤도 바뀜 → 앞면만 그리는 재질이 사라지지 않게 (기본 렌더 파이프라인)
    void OnPreRender() { GL.invertCulling = true; }
    void OnPostRender() { GL.invertCulling = false; }

    void OnDisable()
    {
        GL.invertCulling = false;
        if (cam != null) cam.ResetProjectionMatrix();
        if (bg != null) bg.flipY = false;
    }

    // 이 카메라로 그리던 화면 UI 를 Overlay 로 (뒤집힌 투영으로 그리면 글자가 거꾸로 보임)
    private void ScanCanvases()
    {
        if (cam == null) return;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null || done.Contains(c) || !c.isRootCanvas) continue;
            if (c.renderMode != RenderMode.ScreenSpaceCamera || c.worldCamera != cam) continue;
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            done.Add(c);
        }
    }
}