using UnityEngine;

// ============================================================
// 오디오 출력 깨워 두기
//  [버그] 탭음을 끄고 플레이하면 곡의 처음 1 ~ 3초가 들리지 않음
//   원인: 블루투스 이어폰 · 일부 USB DAC · HDMI(TV · 사운드바) 같은 장치는 '완전한 무음(0)'이 이어지면 스스로 꺼짐(절전)
//         곡 진입 화면 동안은 소리가 하나도 없어서 장치가 꺼지고, 음악이 시작된 뒤에야 다시 켜지느라 앞부분이 잘림
//         (탭음을 켜 두면 첫 노트 전후의 탭음 · 키 입력 소리가 장치를 미리 깨워서 잘 드러나지 않았음)
//   해결: 들리지 않을 만큼 아주 작은 소리(약 -68dB 잡음)를 게임 내내 계속 흘려서 장치가 꺼지지 않게 함
//  CoreManager 가 처음 한 번 만듦 (씬이 바뀌어도 남음). 일시정지 중에도 계속 흐름
// ============================================================
public class AudioKeepAlive : MonoBehaviour
{
    // 약 -68dBFS — 일반 음량에서는 들리지 않음. 장치에 따라 '무음' 판단 기준이 달라서 너무 작으면 효과가 없음
    //  (그래도 곡 앞부분이 잘리면 조금 올려 보고, 아주 조용한 곳에서 '쉬' 소리가 들리면 조금 내림)
    public static float Level = 0.001f;
    private static AudioKeepAlive instance;

    public static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("AudioKeepAlive");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AudioKeepAlive>();
        instance.Begin();
    }

    private void Begin()
    {
        const int rate = 44100;
        int n = rate;   // 1초짜리를 반복
        float[] d = new float[n];
        var rng = new System.Random(7);
        for (int i = 0; i < n; i++) d[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * Level;
        AudioClip clip = AudioClip.Create("KeepAlive", n, 1, rate, false);
        clip.SetData(d, 0);

        AudioSource src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = 1f;
        src.priority = 0;                 // 다른 소리가 많아도 끊기지 않게
        src.spatialBlend = 0f;
        src.ignoreListenerPause = true;   // 일시정지(AudioListener.pause) 중에도 계속
        src.bypassEffects = true;
        src.bypassReverbZones = true;
        src.Play();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}