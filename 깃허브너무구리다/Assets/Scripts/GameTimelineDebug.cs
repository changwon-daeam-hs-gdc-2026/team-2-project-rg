using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================
// 게임 화면 타임라인 표시 (확인용)  —  F2 로 켜고 끄기 (Unity 에디터에서는 처음부터 켜짐)
//  왼쪽 아래 작은 상자:
//   t           차트 시각 (0 = 채보 0박). 음수 = 아직 0박 전
//   음악 시작   음악 파일의 0초가 나오는 차트 시각 (TriggerTime) · 남은 시간
//   음악 위치   지금 실제로 재생 중인 음악 위치 (재생 전이면 '대기' / '예약됨')
//   첫 노트     첫 노트의 차트 시각
//  아래 막대: 시작 ~ 음악 끝 사이에서 지금 위치(흰 선) · 음악 시작(하늘) · 첫 노트(노랑)
// ============================================================
public class GameTimelineDebug : MonoBehaviour
{
    private static bool visible, visibleInit;
    private GameManager gm;
    private GUIStyle style;
    private Texture2D white;

    public static void Attach(GameManager manager)
    {
        if (manager == null || manager.GetComponent<GameTimelineDebug>() != null) return;
        if (!visibleInit) { visibleInit = true; visible = Application.isEditor; }   // 에디터에서는 처음부터 보임
        manager.gameObject.AddComponent<GameTimelineDebug>().gm = manager;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f2Key.wasPressedThisFrame) visible = !visible;
    }

    void OnGUI()
    {
        if (!visible || gm == null) return;
        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            style.normal.textColor = Color.white;
        }
        float scale = Screen.height / 1080f;
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        const float W = 380f, H = 150f;
        float x = 20f, y = 1080f - H - 140f;
        Box(new Rect(x, y, W, H), new Color(0f, 0f, 0f, 0.62f));

        double t = gm.currentPlayTime, trig = gm.TriggerTime, first = gm.FirstNoteTime;
        double mpos = gm.MusicPosition;
        string music;
        if (mpos >= 0.0) music = $"<color=#7CFF9A>{mpos:0.000} s  재생 중</color>";
        else if (gm.MusicScheduled) music = $"<color=#FFD36B>예약됨 · {Mathf.Max(0f, (float)gm.MusicStartsIn):0.000} s 뒤 시작</color>";
        else music = $"<color=#AAAAAA>대기 · {Mathf.Max(0f, (float)gm.MusicStartsIn):0.000} s 뒤 시작</color>";

        string txt =
            $"<b>t</b>            {(t < 0 ? "<color=#FF9A9A>" : "<color=#FFFFFF>")}{t:+0.000;-0.000} s</color>\n" +
            $"음악 시작    <color=#8FD8FF>{trig:+0.000;-0.000} s</color>\n" +
            $"음악 위치    {music}\n" +
            $"첫 노트      <color=#FFE58A>{first:+0.000;-0.000} s</color>";
        GUI.Label(new Rect(x + 12f, y + 8f, W - 24f, 100f), txt, style);

        // 막대: [min(-3, t 시작), 음악 끝]
        double from = System.Math.Min(-3.0, System.Math.Min(trig, first) - 1.0);
        double to = System.Math.Max(trig + System.Math.Max(5.0, gm.MusicLength), first + 2.0);
        float bx = x + 12f, by = y + H - 26f, bw = W - 24f;
        Box(new Rect(bx, by, bw, 6f), new Color(1f, 1f, 1f, 0.15f));
        Mark(bx, by, bw, from, to, 0.0, new Color(1f, 1f, 1f, 0.35f), 1f);      // 0박
        Mark(bx, by, bw, from, to, trig, new Color(0.56f, 0.85f, 1f), 3f);      // 음악 시작
        Mark(bx, by, bw, from, to, first, new Color(1f, 0.9f, 0.54f), 3f);      // 첫 노트
        Mark(bx, by, bw, from, to, t, Color.white, 2f, 14f);                     // 지금
        GUI.Label(new Rect(x + W - 70f, y + 6f, 64f, 20f), "<size=11><color=#888888>F2</color></size>", style);

        GUI.matrix = old;
    }

    private void Mark(float bx, float by, float bw, double from, double to, double v, Color c, float w, float h = 10f)
    {
        float k = (float)((v - from) / System.Math.Max(0.001, to - from));
        if (k < 0f || k > 1f) return;
        Box(new Rect(bx + bw * k - w * 0.5f, by + 3f - h * 0.5f, w, h), c);
    }

    private void Box(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, white);
        GUI.color = old;
    }
}