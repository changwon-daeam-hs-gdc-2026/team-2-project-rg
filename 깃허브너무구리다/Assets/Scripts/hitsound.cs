using System;
using UnityEngine;

// ============================================================
// 탭음 (hitsound.mp3 와 같은 소리, 코드에 담아 둠)
//  mp3 는 파일 앞에 소리 없는 구간(인코더 지연 약 25 ~ 50ms)이 붙어 있어서 Unity 에서 재생하면 그만큼 늦게 들림
//  → 원본 mp3 를 풀어서 앞의 빈 구간을 없앤 소리(44100Hz · 모노 · 16bit, 약 244ms)를 그대로 담음
//    첫 샘플부터 바로 '탁' 이 나옴. 뒤쪽 거의 안 들리는 꼬리는 5ms 동안 부드럽게 줄여 끊음
//  쓰는 곳: 게임 · 설정창(미리 듣기) · 새 에디터 · 예전 에디터 (모두 HitSound.Clip)
//  다른 탭음을 쓰고 싶으면 UseResource = true 로 두면 예전처럼 Resources/Audio/etc/hitsound 를 씀
// ============================================================
public static class HitSound
{
    public static bool UseResource = false;

    private static AudioClip clip;

    public static AudioClip Clip
    {
        get
        {
            if (clip != null) return clip;
            if (UseResource)
            {
                clip = Resources.Load<AudioClip>("Audio/etc/hitsound");
                if (clip != null)
                {
                    if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                    return clip;
                }
            }
            clip = Build();
            return clip;
        }
    }

    private static AudioClip Build()
    {
        byte[] pcm = Convert.FromBase64String(Data);
        int n = pcm.Length / 2;
        float[] d = new float[n];
        for (int i = 0; i < n; i++) d[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
        AudioClip c = AudioClip.Create("HitSound", n, 1, 44100, false);
        c.SetData(d, 0);
        return c;
    }

    // 16bit PCM (리틀 엔디언) · 44100Hz · 모노 → base64
    private const string Data =
        "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" +
        "3P+BAJEAGwCD/y//Yv/p/z0AMAAPAO3/j/8f/yr/vP8WALD/Gf8n/8H/HwD5/8v/8P8TALT/CP/K/iv/sv8TAEcAKADU/8P/AAAMALv/Uf8F/wj/ff8GAAwA" +
        "lv85/1f/xP8PAO7/g/8x/yP/Jv8s/2b/uP+z/2z/i/8iAHAAHgDL/wgAawAxAIX/cP9UADsBLAFYAIH/C/8D/1v/4/9ZAIIAPgCz/y7/5v7s/iX/Vv+H/woA" +
        "0QBHASkB2ACTACcAjv85/3X/BQCFAL8AggDH/yP/Vv89AAgBQQH9AH4AEgDS/4T/NP9k/ycA1AD+AOoAzgCPADUA+f/p/+z/6//O/7D/9v+nABsB6ABkAOv/" +
        "nf/G/2YAtgArAGr/af8+ACUBSwGjAOL/iP90/5n/HQCmAKwAVwAtAE4AgACZAJAAewBzAGAAEACM/zP/cf9KADEBfQHzAO3/I/82/xAA0gDXAF8A7v+Z/3P/" +
        "xf9cAH0AGgDs/xEA4/9L//r+Sf/g/1AAaABMAEgAYgBYACgAHABHAGwAVQAaAP3/BwD3/8j/yf/9/xMA/v/o/8n/kf9p/2n/a/9d/2X/qv8bAG0AXAD1/5P/" +
        "ef+D/2H/JP81/7X/OgBQAOv/Zv8t/23/+f98ALsAsQB5ABMAe//7/vX+YP/w/44AIgFEAbwA7v9o/1D/df+0/xIAawBOAJ7/Af8m/9D/PwA2AA4A8f+2/3r/" +
        "ov8UAC4Ay/+E/8P/PQCIAJEAZAAMAMj/2P8cADoAQwCHAOYA/QDhAOMA1gCCAE0AlgARAT8BBQGDANX/Of/7/j7/0f9NAGcALgDy//r/RQChANcA0QCOAC4A" +
        "+v8hAGoAdAA7ACAAYADGAPQA3wDXAAoBSAE3Ab4AIwDL/+P/OABhACkA0//B/+n/DwBFAKYA2wCLAOX/Rf/d/ub+iP9yAPgAyAA+APz/OQCMAIsAWQBZAJIA" +
        "rgBxAAoA5P8qAI4ApwBaAN7/jP+7/1sAzgCBALH/Hf8L/yX/K/88/3b/t//Y/+z/KQCLALEASgCs/3//4v9WAHEALwDA/2f/bf/K/w4A+//h//n/8P+E/xb/" +
        "Ev9M/2L/Uf9R/3H/lf+l/6r/xP/h/8H/Vv/w/uf+Uv/2/2AATwAbAEcArQCsABIAUf/x/hX/hv/0/z0ASgDl/xT/cP6B/gv/gP/D/+//7/+8/4v/bv9B/wf/" +
        "7/4i/57/MwCYALMAqQCCACEAl/9B/1f/pv/D/4b/PP9U/9X/SwBLAN3/Yf8n/zH/Y/++/zEAbgBBAOX/sf+v/9f/JwBsAFMA8P+q/7//EABhAJYAxwD9APwA" +
        "nAAnAPv/CAAvAIoA8QDoAHAAHAAoADIA9f+t/7P/EgBvAGUAEgAFAF8AjgAwALb/xf9MALQAuQCtAOAAIAETAbIARwALABUAZQDdADwBVAEuAecAiQAlAOf/" +
        "8f89ALcAPwGPAVcBmgC3/xb/5/4Z/33/8P9uAOcALQEeAcgATgDR/3v/Y/+H/+z/kQAyAWgBGwGVACAAz/+p/8n/QwDgABMBjwDQ/6b/JwCfAHoA5P9q/2b/" +
        "sf/k/9L/pf+G/2f/Ov8a/zH/h//i//f/2v/h/wkA+f+g/1H/Q/9o/7L/EABfAJIAoQBtAOX/Ov/L/u/+qv+FAOUApwAiAKj/R//0/r3+xf4g/6H/8/8SAD4A" +
        "cQBIAJn/xP5P/mf+1v5P/7b/BgAaANP/Y/8m/zz/if/u/0EAVgAuAOb/iP8w/xT/Sv+x/yAAbwB2AC0Atv9L/zT/iv/s/93/f/9Y/3//nf+U/4v/iv+R/7n/" +
        "//8wACYA9v/C/47/U/8W/wf/Wv/8/3MAZwARAOj//v8UABUAEAACAPH/EgBsAKcAdgD9/57/iP+b/6z/yv8cAJgA+QAVAfAAmAAlAM3/tv/H/+X/FgBbAJMA" +
        "mQBjABUA6f/y/wcABgAMADIAXgBcABcAtP+P/9z/XwCxAMUA0gDoAPUA7ADAAGwAEADc/9T/8v9QAPwAqwHiAW8BlQCx/wv/5f5S//X/ZACRAIsAHQBM/7P+" +
        "1f5x/+7/BwDS/5z/tv8TAFkAZgBgADsA5P+2/xcAzABBATMBtwD+/1X/CP8m/43/FACYAOoA4QB5APL/mP97/3j/iv+4/8//p/93/4r/xv/c/8L/qv+6/+P/" +
        "/v/0/9b/wv+7/6r/iv91/5D/8f+BAPYABQGxAEgADAD3/93/t/+i/6j/sf+z/8//EgA9AA8Arf+C/6n/zf+z/5f/v//y/8n/af9n/+P/WQBpAD4AGQD0/7j/" +
        "hP+F/7//BAAoACcAHgAyAHMAxwD7APAAuAB8AEQA9/+V/1L/Vv+M/8v/+v8MAAsAHAA5ACwA7P+o/3b/UP9m/+b/mQANAQsBuABlAEkAUwBRADQAEQDx/+T/" +
        "DwB0AMMAogAgALH/tf8LAE4ATwAxABIA9//0/yQAXQBdADIALgBiAH4ASQDv/7//0f8CACUAJgANAP//IwBnAIcAeAB5AJcAkABXAEAAeADDANoAqgBMAPH/" +
        "wf+6/9H/AgA4AFMAVQA/APv/m/9m/3n/sv/r/x0AVACeANoAugA5AMT/qP+u/5D/h//m/3YAuwCfAG8ARQAAALX/qv/u/z4AXwBbAFUANQDP/1D/Ff8s/2X/" +
        "qv/p//r/0v+f/3P/RP8r/0r/iv+6/8n/0v/v/yAARgAyANH/UP8L/z3/tP8RAC4AKAAZAAQA3v+i/1n/Gv8H/zj/nf/2/woA2P+W/3T/gP+o/9X/7v/b/6j/" +
        "j/+v/9D/t/+F/4D/s//s/wsAMQCMAOcA0AA5AI3/H//6/iT/l/8jAIcAkwBBANn/vv/1/zMAUwBdAD0A3P9n/zT/bv/r/14AlACMAG0AXwBfAFsAXABuAHAA" +
        "LwC5/1f/Uf+n/woAJAD9/9j/0v/o/x8AaQCaAJsAZQD4/3//Sf95/97/LwBEADIALAA3ACsAAwD2/xUAJQD2/7L/vv8qAIYAewBAACYAHgD3/7z/nf+1/wEA" +
        "YQCcAIcAOwAAAAEAIgA/AFwAhQCrALIAjgBQACcAJwAsAB0AEQAZACoAQgBkAH4AdQA6ANT/gf9+/6X/tv/M/yoAsQAEAegAYwDG/3//rf8bAJgABAElAdkA" +
        "YgAcAAkA5P+i/3r/jv/J/xYAZQCXAKEAlABlAPz/h/9f/53/BQA8AB4A5P/V/+T/7P/6/xMAFwAIAAQA9//O/7b/x//W/8v/y//k//T/6v/s/xYARQA2AN7/" +
        "af8G/+X+Kf+r/xMAPgBDAC8ACgDq/9b/uf+E/1f/XP+J/5n/ef9l/3//nP+h/7L/4v8NABAA8//l//z/BADS/5H/ff+N/6L/xP/+/zQARAAgAOH/sv+d/5L/" +
        "jP+G/3X/cf+b/9X/9P///wAA3v+b/3L/iv/J//3/CgD7/+X/zP+2/7D/vf/U/+j/5v/A/5P/if+q/+H/DgAXAAQAAgAkAE8AXgBEABgACgAWAAgAzf+T/3L/" +
        "Wv9O/3H/zf86AHEAWgAjAP7/6v/e/+n////+/+j/1v/T/+D/BQAzAEsAOgAHANP/wv/X/wgAVACbAJQAIgCM/zf/R/+f/wkAXACAAHsAYgBKAD4AOgAkAN//" +
        "i/9//9f/SgCNAIsASwDs/7D/v/8EAFYAhwBtAA4Apv9h/1r/ov8VAGAAZQBVAEoAIwDX/47/d/+e/+T/GAAlACAAEgD0/9z/+v9HAH8AfQBaAC4AAQDa/7P/" +
        "d/86/y7/Wv+c/+//VQChAKEAVgDh/3T/Pf87/0z/dv/L/xQACgDK/63/yf/r//T/9v8RAE0AhgCEACoAp/9E/yX/Q/+S/wcAfAC7AKAAMACX/xf/6f4S/13/" +
        "lf+4/9z/7P/L/5X/iP+n/8v/7P8OABoA/P/L/7P/v//b/+//6v/K/6P/mf/E/xcAWgBjADoABgDf/8X/xf/j/wMAAwDl/8D/nf+E/4H/if+M/5b/wv8PAFsA" +
        "egBUAPX/kv9k/33/v//8/yQAQQBTAEwAMQAVAAgAAgABAAMAAwD2/+j/7v////b/2P/V/wAALgBAADoAKAAPAPP/1/+//7L/qv+v/9n/KABpAHgAbgBvAHkA" +
        "cAA8ANj/cv9U/5z/FgB7ALQAxACtAH0AUQA8AEMAbwCZAHAA6f9i/yn/Pv+J//P/RQBbAFoAaABxAF4AMgDy/6b/dP97/67/8f86AG8AZgAmAPH/8/8JAAUA" +
        "+P/+/w8AHwA8AGYAfwB4AG0AfgCbAIsAPADh/63/m/+U/4//iv9//37/lv/F/wEANwBMADQABADW/6//k/+Q/6//4v8QADcAZwCYAJkAUgDr/6n/qv/G/87/" +
        "zf/p/xkAMQAyADQALwAUAPb/5v/m//3/LgBZAFAAFgDR/6f/pP/A/+b//f/+//L/1/+a/03/Nf9+//z/UQBMAAwAzv+5/8b/5v8JABQA8f+6/5r/nv+4/93/" +
        "BAAgACYAHAAXAC0ASgBGABkA4P+r/37/bP+K/9L/KQBoAFwA+v95/yT/Gf9D/4L/wP/q/wEAJQBnAKIAogBhAAMAq/9+/4b/qP/F/+P/BwAZAA8ABgAaAEIA" +
        "YABYADMAEwAKAAwABwD0/8b/gv9Y/3z/3/88AGYAcABxAFIABQC8/7P/5f8gADwANQAeAA4AEAAiAD0ASAAqAPH/zv/d/wsANgBOAEkAHwDi/8f/6P8IAOL/" +
        "mf+S/+D/NgBTADoAFAD6/+n/3P/q/yEAYwB/AG8ASQAVANf/sP+///D/DAAFAPj/6f/A/5H/mP/a/w8A/v+8/4D/ZP9b/2n/qP8PAFIANgDV/3j/R/9F/3n/" +
        "4/9bAJ4AkgBiAEgAQwAwAAsA7P/a/8T/sf/W/0oAxADWAGkA1f94/3f/vv8PACkA9f+d/2D/Z/+m/+b/AQD8/+v/1//U//L/GgAaAOz/yP/i/yMARwA6ACMA" +
        "JgA6AFEAawB7AHMAVQAoAPL/yf/B/8j/v/+y/8z/DQBAAEYALgAIAN7/yP/Z//z/FgAsAFEAfwCfAJgAZAAhAPn/+v8OACgASgBvAHIALwC1/1P/Sv+J/9j/" +
        "GgBQAIMArwC8AI8AQAALAAwAMABZAHUAewBtAEsAEADM/6n/vf/2/zoAfQCwALEAZwDx/5T/eP+M/6f/u//Y/wYAQAB9ALUA1wDNAJoAVAAKAM//wv/1/0kA" +
        "ggCPAJAAowC8AL4AowB1ADcA6v+t/57/wv8EADsAQgAgAPn/3//X/wAAVwCWAIsAYQBNAEQAJwD4/9n/6f8tAHsArADLAOgA5ACnAEkA7v+c/2f/d//Q/zcA" +
        "agBTAAoAw/+j/6b/tv/X/wYAFADi/5v/ef+I/7f/8f8gAD4AYQCLAKEAngCYAIsATADV/2v/Vf+e/wwAXABhACEAzv+a/43/jP99/1//Qv84/0n/av+U/9D/" +
        "DAAgAAcA5//S/8T/yf/4/z4AcAB5AGYAQwARANj/qP+a/8H/CgA+ADoAHQARAAoA4v+a/1z/Qv9Q/3r/qf/A/7H/f/9B/x7/L/9k/5f/tf++/8P/2v8AAAsA" +
        "2f+P/3j/qP/6/1kAuQDsALsAMQCg/1D/WP+S/8f/1//V/9v/8f8EAP//2P+q/5j/of+p/63/sP+n/5j/nf+s/5b/Vf8i/0T/xf9tANYAxQBkAAwA5v/b/97/" +
        "/f84AG8AiACKAIkAeQBBAOT/k/+A/63/8v8nADcAIADp/6X/eP98/7L/AgBLAGwAWAAbANf/rv+3//P/RQB2AG4ARwAnABoADQDn/63/j/+w//X/OQB+ALoA" +
        "uwB3ADgAQABwAIMAXAAOAMH/lf+R/6j/yf/p/wIAFAAjADUASQBOAC0A8P/F/8n/5/8PAD4AXABJABkA8v/W/7r/rP+4/9j/FABpAKYApQB+AFgAOQAhAB0A" +
        "IwAeABMAEAAhAEsAfgCAADYA1/+q/7X/1f/3/xYALAApAPz/rv94/4b/z/8sAIIAuwDAAJkAUwDt/4L/V/+M//D/SACUANwA+gC+ADIAnf9K/1X/mf/r/0MA" +
        "pwDtAN4AfAAMAMz/vP/D/9f/+/8sAFEAQQDy/5f/av9t/4X/wv8+ANUAOQE4AdEAIQBs///+9v5A/87/gQAGARYBuAAxALv/fP+J/9T/KgBmAIoApgC1AJUA" +
        "MwCu/0z/PP+C////fAC2AIoABwBg/+D+zv4z/9T/agDUAAsBBwHGAEsAsP8w/xL/av8CAI4A5QALAQkB2gCIADIA9v/g/+X/+P8LABsAKwAhANP/T//p/vT+" +
        "aP/8/2oAiQBWAPv/tv+X/4f/gP+g/+//QgB5AJgAqgCiAHgAPAADAOD/3/8IAFEAmgC7AJ8ATgDu/6//pf+7/9L/5v8BAA0A5v+e/3T/gv+k/7P/sf+6/+n/" +
        "PgCNAJoAWAABANP/3P8LAEcAgwC4ANEAuQB8AFQAXgByAGgATQA9ADYALwAnABQA8//Z/9b/5v/+/xMAGAAPAPv/0f+S/17/YP+Z/+b/IgA3ACoAEwAMAB8A" +
        "QQBXAEIAAQC5/5//zv8vAIIAnAB/AEAA6v+f/5r/5P86AGAASgALALz/j/+v/wsAXgBxADsA3P+K/2r/df+K/5L/gf9X/zf/U/+u/xMASQBAAAsAzP+o/7X/" +
        "7P8tAF4AfwCdALIApwB2ACwA1v+V/5T/1/8mAFsAgACYAH4AJgC6/2n/R/9W/5r/9f8vADAABwDB/2n/Lv9G/53/7/8nAFMAaQBZADgAGQDu/8b/yv///00A" +
        "pADyABEB8gCmADwAzf+Q/6b/8v9DAIUAqgCYAEoA4v+L/2L/Zf9+/6f/7v9PAJcAlQBIAND/XP8h/0j/vP9BAKkA1wDCAIIASAAiAPv/1v/S/wMAYADDAPEA" +
        "yQBvABgAyv9//1j/fv/e/zEARgAhAOr/v/+r/63/wv/m/xEAOABJAD0AIgAHAO//1f/B/8D/0v/w/yAAaQCiAJIANQDL/5P/ov/z/2EAogCKADkA7f/E/8n/" +
        "8/8WABAA9v/w////DAAVABQA+v/O/77/6f83AHMAgQBvAFUAOgAXAPL/1f/I/8z/5v8ZAFoAkwCxAKMAXADr/43/df+Z/83/+f8bADAANQAsABcAAwD//wgA" +
        "AwDo/9b/4/8AABMAHwA1AE8AVwBKADcAJQAaABwADwDF/0n/7P7r/jD/h//l/0kAjgCCACcAu/+A/4L/o/++/8X/xP/M/+b/FABNAHkAdwBBAPD/sP+g/73/" +
        "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" +
        "wP+//8n/1//m//b/BwAPAAoAAgAEAAoAAQDl/8r/x//a//P/BAAMAAwABgD9//X/8P/t/+z/7f/x//n/BQAQABYAEwAJAPj/3//G/7r/w//a/+z/8f/q/+H/" +
        "3v/k/+//9f/x/+f/4f/l/+//9v/w/9f/rv+I/3//nv/d/x0APgAwAAIA0/+6/7z/zv/l//r/BQACAPT/5//h/+L/5f/p/+7/9v/9//7/+v/y/+T/0//C/7n/" +
        "uf/F/93/+/8SABAA8v/G/6T/m/+m/7f/yv/e//T/DgApAD4AQQAuABAA+f/4/w0ALABBAD4AIwD9/97/1f/k/wMAHgAmABcA/v/q/97/1f/P/8//1v/d/+P/" +
        "6v/z//n/9//r/93/1v/d/+//BQAaACsAMAAoABoAFAAYABoADAD3//H/BAAnAEMASQA4ABYA8P/X/9H/3f/1/xMAKgArABMA7P/G/6//rv/A/9z/9P///wEA" +
        "AQABAAEAAgAIAA8AFQAcACYAKgAkABYACwAHAAoAFAAeACEAHAAWABgAIAAgABAA9f/c/8//1v/w/w4AHQAVAPr/2f+7/6r/qv+4/8//6/8GABgAGAAMAAEA" +
        "//8EAA0AFwAdABsAEgAJAAQAAgAFABIAJwA1ADYALQAgABMABQD6//H/6P/f/97/6f/3//7//P/5//v/AAAIAAwABgD1/+P/3P/i/+///v8LABAACwD///f/" +
        "+v8EAAsABQDv/9j/0f/j/wYAKwBEAEoAPQAjAAsAAwAHAAsACAD//+//2v/H/8H/yP/U/9z/2//S/8j/wP/B/8f/y//R/9//9f8HAAwACwANABIAFwAZABgA" +
        "EAD+/+j/3f/m//n/CAALAAgABgABAPX/3//K/7//wP/D/8L/vv++/8T/zP/R/9L/1//j//P/AAAEAAEA+v/w/+H/1P/U/+b//v8RABwAIwAlAB4ACwDy/9n/" +
        "xP+9/8v/6f8EAAsA///t/+H/3//p//z/DgALAO//yv+z/7b/y//n/wQAHAAlABwACgAAAAIABQD///X/8v/7/wkADgADAO7/2v/R/9T/3f/o//P/9//x/+j/" +
        "7P8FACUANQAtABcA///p/9r/2P/n//r//v/v/9n/0P/Z/+r/+f8FABIAHgAiAB0ADwD7/+f/2v/f//H/AgAGAPv/6P/Q/7f/p/+o/7z/3f8DACYAOwA3AB8A" +
        "BgD4//X/9//5//v/+//7/wAACQAPAAsA///3//z/DwApADkANQAcAPr/2P+8/6r/rP/D/+b/AwALAP7/5P/I/7X/sf+8/9f//v8lAD0AQAAzACAADAD+////" +
        "FQAzAEQAPQApABgAFAAdACkAKQAYAP3/4v/L/7r/sv+4/8X/zf/N/9L/5P/+/xAAFgASAAwAAQDx/9//1P/X/+n/BAAiAD8AVgBgAFcAQgAxACsAKQAlACAA" +
        "HwAgABQA9//T/7r/uP/G/9f/3P/Q/7n/oP+R/5T/rv/W//r/CwAIAPv/7//r//H//v8HAAcABAAEAAkADwAUABsAIAAbAAoA9v/v//r/DQAaABgACQDw/9L/" +
        "tf+k/6X/uP/S/+L/4f/R/7f/mf+B/37/k/+5/9//+v8LABgAHwAfABYADQAKABAAGgAlAC0AMAAsAB8ACwD1/+j/6//+/xQAHgAWAAAA5//S/8T/uf+u/6H/" +
        "mv+j/7v/z//R/8j/xv/S/+L/6//y/wAAFQAnADAALwAnABsAFQAbACwAQwBSAFIAQAAhAP7/4v/R/83/0f/a/+X/7v/0//b/8//p/9n/x/+6/7n/xP/S/97/" +
        "6P/3/wwAIgA1AEIARAA5ACgAHQAZABUADQAIAAoAEQAdADIASwBWAEMAGQDt/8//wP+8/8H/zf/X/9z/3f/g/+f/9P8FABQAHAAcABUABwD1/+b/5f/0/w8A" +
        "KgA/AEwATwBIADkAJAAMAPr/+P8BAAwAEwAZAB8AGwALAPj/7f/p/+P/1P/D/7j/uP/E/9n/9P8QACgAOAA7ADcANgA7ADkAJgAKAPX/7v/0/wAAEAAiADEA" +
        "NQArABQA+//s/+v/7v/q/+D/2v/g/+7//v8LAA4AAQDq/9f/0f/Y/+P/6//x//f/AgATACMAKwAoAB8AFQANAAMA+v/z/+v/4//f/+P/8P/+/wUA/f/m/8v/" +
        "uP+1/7r/wP/H/9D/2f/f/+T/6//0//j/9//4/wAACgAPAA4ACwAHAAIA+v/z//D/8f/0//f/+v/8//v/+P/0/+7/4//T/8L/uP+8/87/5P/v/+T/yf+x/7D/" +
        "xv/l//7/DwAbACIAJAAgABsAEwAFAPX/7f/y////CQAOABAADwAGAPr/9v/9/wgACQD8/+r/2//U/9P/2v/q////DgAOAAEA8P/k/+D/4v/n/+7/9v8AAA4A" +
        "HgAqACoAGwAGAPf/9f/6/wAABQALABAAEAAOAA4AEAASABEAEwAVABMACgD+//T/7P/j/9//4//q/+z/6P/k/+T/5v/m/+f/6//y//7/CgATABYAFAAVABkA" +
        "HAAcABkAFwAaACEAKgAvACoAHAANAAQA/v/z/+T/2v/e/+3//P8AAPL/2P/D/7//z//o//3/CQANAAsABAD6//P/9v/+//3/7v/e/+H//P8fADsASQBIADMA" +
        "EgD4//b/CAAdACkAKAAZAP7/4//X/93/6v/1/wAACgALAAEA8f/i/9T/yP/D/83/5v8EABoAIgAdAA8A/v/x/+7/9/8OACgAOQA3ACgAFQAGAP////8EAAgA" +
        "CQAIAAcAAQD1/+X/1f/J/8X/y//d//L/AAACAPr/7//j/9n/0//U/+H/+P8SACMAIAARAAEA9v/u/+7//P8SACMAJgAgABYABwDv/9b/xf/B/8b/zv/V/9n/" +
        "3f/g/+L/4v/f/9z/2v/Y/9j/4P/u//v//v/6//P/7//z/wAAEQAdAB8AHAAYAAwA9P/Z/8n/zP/Z/+b/7//z/+//4P/K/7f/r/+3/8v/4P/p/+T/2f/S/9T/" +
        "4f/3/w4AGgAXAAwABQADAAMAAQD8//b/8//1//7/CAANAA4AEAASAAwA/v/t/97/1//Y/97/4v/k/+f/7f/y//H/6//m/+f/7P/0//r/+f/2//j/AQANABkA" +
        "JQArACcAFwAGAPv/9//3//n/AQAKAAoA///0//L/9//4//T/8P/r/+f/5f/r//j/BAAKAAgA+//o/9r/2v/q//7/DAAQABAADwAMAAgAAwD+//r/+/8DABAA" +
        "HQAiABYAAADx/+//9P/4//f/9P/y/+//6//l/9//2v/b/+X/9f8GABYAHwAeABgAEAAGAPv/8//x//j/BwARAA8AAgD0/+//+P8LACAALwA0ADEAJAARAPr/" +
        "5//f/+D/6P/3/wgADwAMAAAA8P/d/8//zf/Y/+3/BAAYACoANgA0ACUAEAABAPv//v8HABEAGgAhACQAJAAjAB4AFQAKAAAA/v8DAAoAEAAVABAAAgDz/+z/" +
        "7//5/wcAEQAVABEABgDy/93/z//N/9v/8/8IABEADQAKAA8AFAARAAQA8v/k/+L/7P/7/wMABAD///b/6v/f/9n/2v/h/+v/8f/w/+r/5v/m/+v/7//w//D/" +
        "8v/0//T/8P/n/9z/1f/Z/+X/8f/4//z//v/8//f/8P/m/93/3P/k//D/+v/+////AQAFAAkACAD+/+z/2f/M/8v/1v/o//n/BAAFAPf/4v/U/9D/1v/h/+3/" +
        "8//u/+X/4f/j/+j/7v/y//b/+/8DAAkACgADAPj/7v/r//D/AQAWACMAIwAYAAoA/P/u/97/z//G/8T/yP/U/+T/7v/w/+z/4v/X/9H/0P/U/97/7f///w0A" +
        "EwAQAAkABAAEAAUACQARABgAGQARAAcA///6//X/8f/0//z/BgAOAAwA+//m/9T/xP+5/7v/zv/p/wAACwAKAAQAAAD9//n/9P/w//T/AwAXACoAOQBAADsA" +
        "LQAdABUAFwAdACEAIQAfABYACAD7//X/9v/8/wIAAgD+//7/AgAFAAIA+v/y/+7/8P/0//z/CAAUAB0AIQAhAB4AGAATABQAHgAqADQAOwA6ADEAJQAZAAwA" +
        "/f/0//X//f8HABAAEQAIAPr/8f/y//j//f/8//f/9f/4/wAACAAMAAoABwAGAAkAEAAbACcALgAsAB8AEAAEAP////8GAA0ADgAJAAEA9//u/+T/2f/U/9r/" +
        "7P8EABYAGgAOAP//9v/4//3/AwAJAAwACQAFAAUABgAHAAgACgAIAAEA+f/y/+7/7v/1//7//v/0/+n/5P/m/+7/+v8EAAgACQAFAP3/9v/z//L/8v/x/+7/" +
        "6f/q//L/+/8CAAgACgAJAAgABgAEAAAA+//2//L/8P/w//X/+v/5//H/6f/i/+D/4f/j/+f/7v/3////BQAGAAEA/f/8//v/+f/6//z/+//1//D/7P/o/+j/" +
        "7v/4/wIABwAGAP//9f/t/+r/7v/1/wAADAATABAACgABAPb/7f/l/+D/5//4/wcACwAFAPr/7//q/+v/7v/z//v/AwAFAAIA/P/5//f/9P/x//P/+v8DAAkA" +
        "DAANAAoABgD///n/+P///wsAFwAaABIABwD8//T/8P/z//j/+P/2//f/+/////z/8v/o/+b/8f8HAB0AJgAjABgACAD4//D/9P8AAA8AHAAjACEAGgALAPX/" +
        "4//f/+f/9/8IABMAFAAQAAsAAwD3/+7/7f/0////CwASABEABwD7//P/7//v//X//v8KABYAHQAcABEA/v/r/+L/5//4/w0AHQAfABIA/v/t/+f/6v/z//7/" +
        "BQAIAAYAAwD///r/9f/2//3/BwATAB0AIQAgABsAEgAHAP3/+f/9/wcAEgAYABYAEAAIAP//9f/t/+f/5P/l/+3/+P8AAAUABgAAAPf/8v/y//X//P8GAA8A" +
        "EQAKAP3/9f/4/wQAFAAgACMAIAAbABYAEAAIAAAA+P/x//H/+f8GABEAEQAEAPL/4v/X/9T/2v/q//7/EAAbABsAFQAPAA0ACgAGAP///f8BAAkADwAPAAkA" +
        "AQD7//z/AwAOABUAFgATAA0AAQD1/+v/5f/n//L/AAALABMAFQARAAYA9P/k/9z/3v/n//T/AwATAB4AIAAZAAsA/f/2//b/9//5//z//v///wAAAwAHAA0A" +
        "EAAMAAUAAQD////////+//7/AAACAAMABQAIAAwADQAIAAAA+f/3//X/8f/t/+r/6//y//7/DgAdACMAHwARAP//8v/v//f/AQAKABIAFgAUABAACwAFAAEA" +
        "/v/+/wAABAAGAAQAAQD9//r/+v/+/wMADQAXABsAEwACAOz/2v/S/9f/5P/0/wIADQAQAA4ACQABAPf/8f/v//P/+f8BAAcACAAFAP3/8v/o/+P/5//y//7/" +
        "BQAGAP//9f/q/+T/5P/r//P/+v/8//n/9P/w/+r/5//m/+r/8f/8/wUACgALAAYA+//w/+n/6f/u//b//v8DAAIA/v/3/+//6//t//X///8JAA4ADQAGAP//" +
        "+//6//3///8AAAMACAAOABAACwACAPj/8v/w//T/+f8AAAcACwAMAAYA/v/2//L/9P/6/wEABgAIAAgACAAFAAIA/v/7//r//v8FAAsADgAMAAUA/f/2//T/" +
        "+P///wgADQANAAkAAwD7//X/8f/v/+//8//5////BQAHAAUAAAD7//f/9f/1//f//P8CAAYACAAGAAMAAAD/////AAABAAIAAgABAP7/+//4//f/9//5//v/" +
        "/v8AAAAA/v/7//n/+P/4//j/+f/7//7/AAAAAP//////////AAAAAAEAAgADAAMAAwAEAAQABAADAAMAAwACAAEAAAD+//7//v///wAAAQABAAEAAAD+//3/" +
        "/f/9//7//////wAAAAAAAAEAAQABAAEAAQABAAEAAAAAAAAAAQABAAEAAAAAAAAAAAA=";
}