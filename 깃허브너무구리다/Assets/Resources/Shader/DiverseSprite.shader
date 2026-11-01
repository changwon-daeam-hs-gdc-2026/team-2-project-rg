// 4B TUNES - SIMPLE-DIVERSE 스킨: 레인 · 노트 스프라이트를 원근(기울어진 레인)으로 그리는 셰이더
//  Assets/Resources/Shaders/ 폴더에 넣어 주세요. (Resources 폴더에 있어야 빌드에 포함됩니다)
//
//  카메라를 추가하지 않고, 정점 위치만 '기울어진 가상 카메라'로 투영해서 그린다.
//   → 원래 카메라 · 정렬 순서 · UI 는 그대로, 조명(2D Light)의 영향도 받지 않음 (Unlit)
//  가상 카메라 값(_Div*)은 DiversePlayfield.cs 가 재질마다 매 프레임 넣어 준다.
//  _ClipBelow = 1 이면 판정선 아래(_DivClip.x 보다 아래)는 그리지 않음 (노트 · 마디선)
Shader "Hidden/4BTunes/DiverseSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _ClipBelow ("Clip Below Judge Line", Float) = 0
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
        // DisableBatching: 정점 위치를 셰이더에서 직접 투영하므로 묶어 그리기(배칭)를 끔
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "DiverseProject.cginc"

            sampler2D _MainTex;
            float _ClipBelow;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float worldY : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                o.pos = DiverseProject(wp);
                o.color = v.color;
                o.uv = v.uv;
                o.worldY = wp.y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_ClipBelow > 0.5) clip(i.worldY - _DivClip.x);
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.a *= DiverseFade(i.worldY);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
