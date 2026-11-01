// 4B TUNES - MV 레인 블러: 흐려진 영상을 레인 영역에 그리는 셰이더
//  Assets/Resources/Shaders/ 폴더에 넣어 주세요. (Resources 폴더에 있어야 빌드에 포함됩니다)
//  같은 폴더에 DiverseProject.cginc 도 있어야 합니다.
//  Built-in / URP 둘 다 동작하는 단순 Unlit 투명 셰이더.
Shader "Hidden/4BTunes/LaneBlurShow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ScreenMode ("Use Screen UV", Float) = 0
        _Warp ("SIMPLE-DIVERSE Projection", Float) = 0
        _ScreenToBg ("Screen UV -> Bg UV (scale xy, offset zw)", Vector) = (1,1,0,0)
        [HideInInspector] _DivCamPos ("", Vector) = (0,0,0,0)
        [HideInInspector] _DivCamF ("", Vector) = (0,0,1,0)
        [HideInInspector] _DivCamU ("", Vector) = (0,1,0,0)
        [HideInInspector] _DivCamR ("", Vector) = (1,0,0,0)
        [HideInInspector] _DivProj ("", Vector) = (1,1,0,0)
        [HideInInspector] _DivFade ("", Vector) = (100,101,0,0)
        [HideInInspector] _DivClip ("", Vector) = (-100,0,0,0)
        [HideInInspector] _DivFold ("", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "DisableBatching"="True" }
        Cull Off ZWrite Off ZTest Always
        // 알파 채널은 One OneMinusSrcAlpha → RenderTexture(SIMPLE-DIVERSE)에 그려도 투명도가 맞게 쌓임
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "DiverseProject.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _ScreenMode;
            float _Warp;
            float4 _ScreenToBg;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; float worldY : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                if (_Warp > 0.5) o.pos = DiverseProject(mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz); // SIMPLE-DIVERSE 원근
                o.uv = v.uv;
                o.sp = ComputeScreenPos(o.pos);
                o.worldY = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // SIMPLE-DIVERSE: 원근으로 그려지므로 '화면 위치'로 배경 UV 를 구함
                float2 suv = i.sp.xy / i.sp.w;
                float2 uv = lerp(i.uv, suv * _ScreenToBg.xy + _ScreenToBg.zw, _ScreenMode);
                fixed4 c = tex2D(_MainTex, uv);
                c.a = 1;
                c *= _Color;
                if (_Warp > 0.5) c.a *= DiverseFade(i.worldY); // SIMPLE-DIVERSE: 먼 쪽 끝에서 사라짐
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
