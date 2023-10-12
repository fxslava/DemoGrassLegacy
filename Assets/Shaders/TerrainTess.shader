Shader "Unlit/TerrainTess"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex tessvert
            #pragma fragment frag
            #pragma hull hs
            #pragma domain ds
            #pragma target 4.6

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 tangent : TANGENT;
                float3 normal : NORMAL;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float2 texcoord: TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            struct hulldata {
              float4 vertex : INTERNALTESSPOS;
              float3 tangent : TANGENT;
              float3 normal : NORMAL;
              float2 texcoord : TEXCOORD0;
            };

            struct tessfactdata
			{
				float Edges[4] : SV_TessFactor;
				float Inside[2] : SV_InsideTessFactor;
			};

            sampler2D _MainTex;
            float4 _MainTex_ST;
            Texture2D _HeightMap;
            sampler2D _DiffuseMap;
            float3 _tileOrigin;
            float _patchSize;
            float _heightScale;
            int _gridDimensions;

            int2 MakeQuadVertex(in uint vertId)
            {
               return bool2(vertId > 0 && vertId < 3, vertId > 1);
            }

            hulldata tessvert (uint vertexID: SV_VertexID, uint instanceID : SV_InstanceID) {
                hulldata o;

                int col = instanceID % _gridDimensions;
                int row = instanceID / _gridDimensions;
                int2 offset = MakeQuadVertex(vertexID);

                float height = _HeightMap.Load(int3(col + offset.x, row + offset.y, 0)).r * _heightScale;
                float3 pos = float3((col + offset.x) * _patchSize, height, (row + offset.y) * _patchSize) + _tileOrigin;
                o.vertex = float4(pos, 1.0f);
                o.texcoord = (float2(col + offset.x + 0.5f, _gridDimensions + 1 - row - offset.y - 0.5f)) * (1.0f / float(_gridDimensions + 1));

                o.normal = float3(0.0, 1.0f, 0.0f);
                o.tangent = float3(1.0, 0.0f, 0.0f);
                return o;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                return o;
            }

            tessfactdata hsconst (InputPatch<hulldata, 4> v) {
                tessfactdata o;
                o.Edges[0] = 1.0f; 
                o.Edges[1] = 1.0f; 
                o.Edges[2] = 1.0f; 
                o.Edges[3] = 1.0f; 
                o.Inside[0] = 1.0f;
                o.Inside[1] = 1.0f;
                return o;
            }

            [domain("quad")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_ccw")]
			[patchconstantfunc("hsconst")]
            [outputcontrolpoints(4)]
            hulldata hs (InputPatch<hulldata, 4> v, uint id : SV_OutputControlPointID) {
                return v[id];
            }

            #define GENERATE_QUAD_BARYCENTRIC(patch, param, bary) (     \
                lerp(                                                   \
                   lerp(patch[0].param, patch[1].param, bary.x),        \
                   lerp(patch[3].param, patch[2].param, bary.x),        \
                   bary.y ))

            [domain("quad")]
            v2f ds (tessfactdata tessFactors, const OutputPatch<hulldata, 4> vi, float2 bary : SV_DomainLocation) {
                appdata v;

                v.vertex   = GENERATE_QUAD_BARYCENTRIC(vi, vertex, bary);
                v.tangent  = GENERATE_QUAD_BARYCENTRIC(vi, tangent, bary);
                v.normal   = GENERATE_QUAD_BARYCENTRIC(vi, normal, bary);
                v.texcoord = GENERATE_QUAD_BARYCENTRIC(vi, texcoord, bary);

                v2f o = vert (v);
                return o;
            }


            half4 frag (v2f i) : SV_Target
            {
                return tex2D(_DiffuseMap, float2(i.texcoord.x, i.texcoord.y));
            }
            ENDCG
        }
    }
}