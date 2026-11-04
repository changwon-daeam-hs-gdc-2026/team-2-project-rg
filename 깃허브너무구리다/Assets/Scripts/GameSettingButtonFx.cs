using System;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// GAME SETTING 패널의 버튼 시각 효과 — 마우스 입력은 GameSettingPanel 이 직접 읽어서 넘겨준다
//  (EventSystem 이 없거나 다른 입력 모듈이어도 클릭이 항상 먹도록)
//
//   마우스 올림 : 바탕이 밝아지고 살짝 커짐 (1.03배)
//   누름        : 0.95배로 눌림 + 흰 섬광
//   손 뗌       : 탄력 있게 원래 크기로
// ============================================================
public class GameSettingButtonFx : MonoBehaviour
{
    public Action OnClick;
    public Image Bg, Flash;
    public Color BaseColor = new Color(1f, 1f, 1f, 0.08f);
    public Color HoverColor = new Color(1f, 1f, 1f, 0.16f);
    public bool Interactable = true;

    private bool hover, down;
    private float scale = 1f, scaleVel, flash, hoverT;

    public RectTransform Rect { get { return (RectTransform)transform; } }

    // GameSettingPanel 이 매 프레임 알려 줌
    public void SetHover(bool on) { hover = on; if (!on) down = false; }
    public void Press() { if (!Interactable) return; down = true; flash = 1f; }
    public void Release(bool inside)
    {
        bool wasDown = down;
        down = false;
        if (wasDown && inside && Interactable && OnClick != null) OnClick();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        hoverT = Mathf.MoveTowards(hoverT, hover && Interactable ? 1f : 0f, dt / 0.08f);
        float target = !Interactable ? 1f : down ? 0.95f : (hover ? 1.03f : 1f);
        // 탄력 있는 크기 변화 (스프링)
        scaleVel += ((target - scale) * 900f - scaleVel * 38f) * dt;
        scale += scaleVel * dt;
        transform.localScale = new Vector3(scale, scale, 1f);

        if (Bg != null)
        {
            Color c = Color.Lerp(BaseColor, HoverColor, hoverT);
            if (!Interactable) c.a *= 0.4f;
            Bg.color = c;
        }
        flash = Mathf.MoveTowards(flash, 0f, dt / 0.22f);
        if (Flash != null) Flash.color = new Color(1f, 1f, 1f, 0.30f * flash * flash);
    }
}