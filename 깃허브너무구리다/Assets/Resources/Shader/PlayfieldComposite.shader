// 4B TUNES - SIMPLE-DIVERSE 스킨: 원근 카메라로 그린 플레이 영역(RenderTexture)을 화면에 합성
//  Assets/Resources/Shaders/ 폴더에 넣어 주세요. (Resources 폴더에 있어야 빌드에 포함됩니다)
//  RenderTexture 는 미리 곱한 알파(premultiplied) 상태라 Blend One OneMinusSrcAlpha 로 합성.
//  위쪽(먼 쪽) 끝은 _FadeStart ~ _FadeEnd (화면 세로 0 ~ 1) 사이에서 부드럽게 사라짐.
Shader "Hidden/4BTunes/PlayfieldComposite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FadeStart ("Fade Start", Float) = 0.8
        _FadeEnd ("Fade End", Float) = 0.9
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _FadeStart, _FadeEnd;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float f = 1.0 - smoothstep(_FadeStart, _FadeEnd, i.uv.y);
                return c * (f * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
