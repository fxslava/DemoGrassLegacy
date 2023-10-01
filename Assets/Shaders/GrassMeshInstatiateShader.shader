Shader "Unlit/GrassMeshInstatiateShader"
{
    Properties
    {
        _GrassBendIntensity ("Grass bend intensity", Float) = 1.0
        _GrassDepthAmbient ("Grass depth of ambient", Float) = 0.1
        _GrassAmbientColor ("Grass ambient color", Color) = (.1, .5, .1, 1)
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

            struct GrassInstanceProperties {
                float4x4 mat;
                float4 color;
            };

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

            float3 BendGrassVertex(float3 vertex, float3 pivot, float3 normal, float intensity) 
            {
                float angle = length(vertex - pivot) * intensity;
                float3 axis = normalize(cross(normal, float3(0,1,0)));
                float3x3 mat = RotateAroundAxisMat(axis, angle);

                return mul(mat, vertex - pivot) + pivot;
            }

            float _GrassBendIntensity;
            float _GrassDepthAmbient;
            float4 _GrassAmbientColor;
            StructuredBuffer<GrassInstanceProperties> _Properties;

            v2f vert (appdata v, uint instanceID: SV_InstanceID)
            {
                v2f o;

                float ambient = saturate((length(v.vertex.y) - _GrassDepthAmbient) / _GrassDepthAmbient);
                float4 pos = mul(_Properties[instanceID].mat, float4(v.vertex.xyz, 1.0f));

                float3 pivot = mul(_Properties[instanceID].mat, float4(0,0,0,1)).xyz;
                float3 normal = -float3(_Properties[instanceID].mat[0].y, _Properties[instanceID].mat[1].y, _Properties[instanceID].mat[2].y);

                pos.xyz = BendGrassVertex(pos.xyz, pivot, normal, _GrassBendIntensity * (1.0f + normal.y));

                o.vertex = mul(UNITY_MATRIX_VP, pos);
                o.color = lerp(_GrassAmbientColor * _Properties[instanceID].color, _Properties[instanceID].color, ambient);

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
