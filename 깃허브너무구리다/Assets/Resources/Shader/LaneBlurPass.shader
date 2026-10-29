// 4B TUNES - MV 레인 블러: 가우시안 블러 1방향 패스 (Graphics.Blit 전용)
//  Assets/Resources/Shaders/ 폴더에 넣어 주세요. (Resources 폴더에 있어야 빌드에 포함됩니다)
Shader "Hidden/4BTunes/LaneBlurPass"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _BlurDir; // xy: 방향 * 퍼짐

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            // 선형 샘플링을 이용한 9탭 가우시안 (5번 읽기)
            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy * _BlurDir.xy;
                fixed4 c = tex2D(_MainTex, i.uv) * 0.2270270;
                c += tex2D(_MainTex, i.uv + d * 1.3846154) * 0.3162162;
                c += tex2D(_MainTex, i.uv - d * 1.3846154) * 0.3162162;
                c += tex2D(_MainTex, i.uv + d * 3.2307692) * 0.0702703;
                c += tex2D(_MainTex, i.uv - d * 3.2307692) * 0.0702703;
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
