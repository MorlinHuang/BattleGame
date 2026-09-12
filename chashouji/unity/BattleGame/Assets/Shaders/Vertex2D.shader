// 顶点色 2D 画笔。混合模式由材质参数给：正常叠加用 SrcAlpha/OneMinusSrcAlpha，
// 网页版里 globalCompositeOperation='lighter' 的地方用 SrcAlpha/One。
// Cull Off：画布坐标系 y 向下，落到 Unity 世界时 y 取负，三角形绕序整体翻了个面。
Shader "Chashouji/Vertex2D" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off  ZWrite Off  Lighting Off  Fog { Mode Off }
        Blend [_SrcBlend] [_DstBlend]
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 col : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                fixed4 c = v.color;
                // 顶点色是照网页版按 sRGB 写的；工程若跑在线性色彩空间要先转过去
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = GammaToLinearSpace(c.rgb);
                #endif
                o.col = c;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                fixed4 c = tex2D(_MainTex, i.uv) * i.col;
                c.rgb *= c.a;          // 预乘，配合 Blend One / OneMinusSrcAlpha 语义一致
                return c;
            }
            ENDCG
        }
    }
}
