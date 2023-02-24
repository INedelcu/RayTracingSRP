Shader "Unlit/RayTracing/Glass"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _MagicValue("MagicValue", Range(0,1)) = 0.0
        _AbsorptionCoefficient("AbsorptionCoefficient", Range(0,1)) = 0.0
        _RefractiveIndex("AbsorptionCoefficient", Range(0,2)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "DisableBatching" = "True"}
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };
       
        fixed4 _Color;

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Albedo comes from a texture tinted by color
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            // Metallic and smoothness come from slider variables
            o.Metallic = 1;
            o.Smoothness = 1;
            o.Alpha = c.a;
        }
        ENDCG
    }

    SubShader
    {
        Pass
        {
            Name "Test"
            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM

            #include "UnityShaderVariables.cginc"
            #include "UnityRaytracingMeshUtils.cginc"
            #include "Light.hlsl"
            #include "RayPayload.hlsl"
            #include "Globals.hlsl"

            #pragma raytracing test

            float4 _Color;

            float _MagicValue;

            float _RefractiveIndex;
            float _AbsorptionCoefficient;

            Texture2D _MainTex;
            float4 _MainTex_ST;

            SamplerState sampler_linear_repeat;

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float3 position;
                float3 normal;
                float2 uv;
            };

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                INTERPOLATE_ATTRIBUTE(position);
                INTERPOLATE_ATTRIBUTE(normal);
                INTERPOLATE_ATTRIBUTE(uv);
                return v;
            }

            void fresnel(in float3 I, in float3 N, in float ior, out float kr)
            {
                float cosi = clamp(-1, 1, dot(I, N));
                float etai = 1, etat = ior;
                if (cosi > 0)
                {
                    float temp = etai;
                    etai = etat;
                    etat = temp;
                }
                // Compute sini using Snell's law
                float sint = etai / etat * sqrt(max(0.f, 1 - cosi * cosi));
                // Total internal reflection
                if (sint >= 1)
                {
                    kr = 1;
                }
                else
                {
                    float cost = sqrt(max(0, 1 - sint * sint));
                    cosi = abs(cosi);
                    float Rs = ((etat * cosi) - (etai * cost)) / ((etat * cosi) + (etai * cost));
                    float Rp = ((etai * cosi) - (etat * cost)) / ((etai * cosi) + (etat * cost));
                    kr = (Rs * Rs + Rp * Rp) / 2;
                }
                // As a consequence of the conservation of energy, transmittance is given by:
                // kt = 1 - kr;
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);

                float3 worldPosition = mul(ObjectToWorld(), float4(v.position, 1));

                if (payload.bounceIndex < 5)
                {
                    bool isFrontFace = (HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE);

                    float3 e0 = v1.position - v0.position;
                    float3 e1 = v2.position - v0.position;

                    float3 faceNormal = normalize(mul(lerp(v.normal, normalize(cross(e0, e1)) , _MagicValue), (float3x3)WorldToObject()));

                    faceNormal = isFrontFace ? faceNormal : -faceNormal;

                    float refractiveIndex = isFrontFace ? (1.0f / _RefractiveIndex) : (_RefractiveIndex / 1.0f);

                    float kr;
                    fresnel(WorldRayDirection(), faceNormal, _RefractiveIndex, kr);

                    float3 refractedRay = refract(WorldRayDirection(), faceNormal, refractiveIndex);
                    float3 reflectedRay = reflect(WorldRayDirection(), faceNormal);

                    RayDesc ray;
                    ray.Origin = worldPosition + 0.01f * refractedRay;
                    ray.Direction = refractedRay;
                    ray.TMin = 0;
                    ray.TMax = 1e20f;

                    RayPayload refrRayPayload;
                    refrRayPayload.color = float4(0, 0, 0, 0);
                    refrRayPayload.worldPos = float4(0, 0, 0, 1);
                    refrRayPayload.bounceIndex = payload.bounceIndex + 1;

                    TraceRay(g_SceneAccelStruct, 0, 0xFF, 0, 1, 0, ray, refrRayPayload);

                    ray.Origin = worldPosition + 0.01f * reflectedRay;
                    ray.Direction = reflectedRay;
                    ray.TMin = 0;
                    ray.TMax = 1e20f;

                    RayPayload reflRayPayload;
                    reflRayPayload.color = float4(0, 0, 0, 0);
                    reflRayPayload.worldPos = float4(0, 0, 0, 1);
                    reflRayPayload.bounceIndex = payload.bounceIndex + 1;

                    TraceRay(g_SceneAccelStruct, 0, 0xFF, 0, 1, 0, ray, reflRayPayload);

                    float3 specColor = float3(0, 0, 0);

                    if (payload.bounceIndex == 0)
                    {
                        float3 vecToLight = normalize(PointLightPosition.xyz - worldPosition);
                        specColor = pow(max(dot(reflectedRay, vecToLight), 0), 19.3) * PointLightColor;
                    }

                    float3 refractedColorWithAbsorbtion = refrRayPayload.color.xyz * exp(-(1 - _Color.xyz) * _AbsorptionCoefficient * refrRayPayload.color.w);
                    payload.color.xyz = (lerp(refractedColorWithAbsorbtion, reflRayPayload.color.xyz, kr) + specColor);
                    payload.color.w = RayTCurrent();

                    if (payload.bounceIndex == 0)
                    {
                        payload.worldPos = float4(worldPosition, 1);
                    }
                }
                else
                {
                    float3 albedo = _Color.xyz;

                    payload.color = float4(albedo, RayTCurrent());
                    payload.worldPos = float4(worldPosition, 1);
                }
            }

            ENDHLSL
        }
    }
    FallBack "Diffuse"
}
