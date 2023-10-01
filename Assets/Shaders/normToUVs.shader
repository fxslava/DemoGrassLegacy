Shader "Unlit/normToUVs"
{
    SubShader
    {
        Cull Off
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float3 normal : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.uv.x * 2.0f - 1.0f, v.uv.y * 2.0f - 1.0f, 0, 1.0f);
                o.normal = mul(unity_ObjectToWorld, v.normal).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // sample the texture
                return float4(normalize(i.normal), 1.0f);
            }
            ENDCG
        }
    }
}
