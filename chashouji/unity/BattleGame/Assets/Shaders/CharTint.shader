// 角色网格：采样立绘，再按 _Tint.a 把颜色往 _Tint.rgb 拉 —— 劣势方压暗，不叠色相
// （叠红会把黑发染成棕色，人物形象就变了）。
Shader "Chashouji/CharTint" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint (rgb=色 a=混合量)", Vector) = (0,0,0,0)
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off  ZWrite Off  Lighting Off  Fog { Mode Off }
        Blend SrcAlpha OneMinusSrcAlpha
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _Tint;

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                fixed4 c = tex2D(_MainTex, i.uv);
                float3 t = _Tint.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                t = GammaToLinearSpace(t);
                #endif
                c.rgb = lerp(c.rgb, t, _Tint.a);
                return c;
            }
            ENDCG
        }
    }
}
