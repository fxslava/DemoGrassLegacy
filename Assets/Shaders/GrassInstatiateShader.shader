Shader "Unlit/GrassInstatiateShader"
{
    Properties
    {
        _GrassDepthAmbient ("Grass depth of ambient", Float) = 0.1
        _GrassAmbientColor ("Grass ambient color", Color) = (.1, .5, .1, 1)
        _GrassColor ("Grass color", Color) = (1, 1, 1, 1)

        _GrassBendIntensity ("Bend Intensity", Float) = 0

        _GrassWindX ("Wind X", Float) = 0
        _GrassWindY ("Wind Y", Float) = 0

        _BendGrassTex ("Bend Grass Map", 2D) = "black" {}
        _WindTex ("Wind", 2D) = "black" {}
    }
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
            #include "InstanceData.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f
            {
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
            };

            float _GrassDepthAmbient;
            float4 _GrassAmbientColor;
            float4 _GrassColor;
            StructuredBuffer<GrassInstanceData> _Properties;

            sampler2D _BendGrassTex;
            float3 _bendMapOrigin;
            float3 _bendMapInvExtents;
            float _GrassBendIntensity;

            sampler2D _WindTex;
            float _GrassWindX;
            float _GrassWindY;

            float3x3 RotateAroundAxisMat(float3 axis, float angle) {
                float3x3 mat;

                float c = cos(angle);
                float s = sin(angle);
                float C = 1 - c;
                float xx = axis.x * axis.x;
                float yy = axis.y * axis.y;
                float zz = axis.z * axis.z;
                float xy = axis.x * axis.y;
                float xz = axis.x * axis.z;
                float zy = axis.z * axis.y;
                float xs = axis.x * s;
                float ys = axis.y * s;
                float zs = axis.z * s;

                mat[0] = float3(xx * C + c,  xy * C + zs, xz * C - ys);
                mat[1] = float3(xy * C - zs, yy * C + c,  zy * C + xs);
                mat[2] = float3(xz * C + ys, zy * C - xs, zz * C + c);

                return mat;
            }

            float3 BendGrassVertex(float3 vertex, float3 pivot, float2 bendVector, float intensity) 
            {
                float angle = length(vertex - pivot) * intensity;
                float3 axis = normalize(float3(bendVector.y + 0.0001f, 0, -bendVector.x));
                float3x3 mat = RotateAroundAxisMat(axis, angle);

                return mul(mat, vertex - pivot) + pivot;
            }

            float3x3 RotateMatrix(float3 angles) {
                float3x3 mat;

                float sa = sin(angles.x);
                float ca = cos(angles.x);
                float sb = sin(angles.y);
                float cb = cos(angles.y);
                float sg = sin(angles.z);
                float cg = cos(angles.z);

                mat[0] = float3(ca * cb, ca * sb * sg - sa * cg, ca * sb * cg + sa * sg);
                mat[1] = float3(sa * cb, sa * sb * sg + ca * cg, sa * sb * cg - ca * sg);
                mat[2] = float3(-sb, cb * sg, cb * cg);

                return mat;
            }

            float3 WindGrassVertex(float3 vertex, float3 pivot, float2 windVector) 
            {
                float angle = length(vertex - pivot);
                float3x3 mat = RotateMatrix(float3(angle * windVector.x, 0, angle * windVector.y));

                return mul(mat, vertex - pivot) + pivot;
            }

            float2 packDirection(float2 direction) {
                return (direction + float2(1.0f, 1.0f)) * 0.5f;
            }
            
            float2 unpackDirection(float2 direction) {
                return (direction - float2(0.5f, 0.5f)) * 2.0f;
            }

            v2f vert (appdata v, uint instanceID: SV_InstanceID)
            {
                v2f o;

                float ambient = saturate((length(v.vertex.y) - _GrassDepthAmbient) / _GrassDepthAmbient);
                float4 pos = mul(_Properties[instanceID].mat, v.vertex);

                float3 pivot = mul(_Properties[instanceID].mat, float4(0,0,0,1)).xyz;

                float2 uv = 1.0f - 0.5f * (((pivot - _bendMapOrigin) * _bendMapInvExtents).xz + 1.0f);

                float2 bendDir = unpackDirection(tex2Dlod(_BendGrassTex, float4(uv, 0, 0)).rg);
                float bendIntensity = length(bendDir);

                pos.xyz = BendGrassVertex(pos.xyz, pivot, bendDir, bendIntensity * _GrassBendIntensity);

                float2 windDir = float2(_GrassWindX, _GrassWindY);
                float wind = tex2Dlod(_WindTex, float4(uv - windDir * _Time.x, 0, 0)).r;

                pos.xyz = WindGrassVertex(pos.xyz, pivot, windDir * wind);

                o.vertex = UnityObjectToClipPos(pos);

                o.color = lerp(_GrassAmbientColor * _Properties[instanceID].color, 0.5f * _Properties[instanceID].color + 0.5f * _GrassColor, ambient);

                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = i.color;
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
