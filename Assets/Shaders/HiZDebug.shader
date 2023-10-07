Shader "HiZDebug" {
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            Texture2D _tex;
            uint _lodId;
            uint _texResolutionX;
            uint _texResolutionY;
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float4 frag (v2f i) : SV_Target {
                uint3 uv = uint3(i.uv.x * _texResolutionX, i.uv.y * _texResolutionY, _lodId);
                float depth = _tex.Load(uv).r;
                return float4(depth, depth, depth, 1.0f);
            }
            ENDCG
        }
    }
}