Shader "Custom/HighlightZoneURP"
{
    Properties
    {
        _BaseMap    ("Albedo (RGB)", 2D)         = "white" {}
        _BaseColor  ("Color Tint",   Color)      = (1,1,1,1)
        _GhostAlpha ("Ghost Alpha",  Range(0,1)) = 0.15
        _BoxCenter  ("Box Center",   Vector)     = (0,0,0,0)
        _BoxExtents ("Box Extents",  Vector)     = (1,1,1,0)
        _BoxR0      ("Box Row 0",    Vector)     = (1,0,0,0)
        _BoxR1      ("Box Row 1",    Vector)     = (0,1,0,0)
        _BoxR2      ("Box Row 2",    Vector)     = (0,0,1,0)
        _FullOpaque ("Full Opaque",  Float)      = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
        }

        // ── Single combined pass: inside = opaque, outside = ghost ────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Alpha blending for the ghost regions
            Blend SrcAlpha OneMinusSrcAlpha
            // ZWrite on so opaque inside parts occlude each other correctly
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _GhostAlpha;
                float4 _BoxCenter;
                float4 _BoxExtents;
                float4 _BoxR0;
                float4 _BoxR1;
                float4 _BoxR2;
                float  _FullOpaque;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            // Returns 1 if world position is inside the oriented bounding box
            float insideBox(float3 wp)
            {
                float3 local = wp - _BoxCenter.xyz;
                float3 proj  = float3(
                    dot(local, _BoxR0.xyz),
                    dot(local, _BoxR1.xyz),
                    dot(local, _BoxR2.xyz));
                float3 q = abs(proj) - _BoxExtents.xyz;
                return (q.x < 0.0 && q.y < 0.0 && q.z < 0.0) ? 1.0 : 0.0;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Simple diffuse lighting
                Light  mainLight = GetMainLight();
                float3 normal    = normalize(IN.normalWS);
                float  diff      = saturate(dot(normal, mainLight.direction));
                texCol.rgb      *= mainLight.color * (diff * 0.8 + 0.2);

                if (_FullOpaque > 0.5)
                {
                    // Reset mode — fully opaque everywhere
                    texCol.a = 1.0;
                }
                else
                {
                    // Highlight mode — inside box stays opaque, outside becomes ghost
                    float inside = insideBox(IN.positionWS);
                    texCol.a = (inside > 0.5) ? 1.0 : _GhostAlpha;
                }

                return texCol;
            }
            ENDHLSL
        }

        // ── Shadow caster ─────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_shadowcaster

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _GhostAlpha;
                float4 _BoxCenter;
                float4 _BoxExtents;
                float4 _BoxR0;
                float4 _BoxR1;
                float4 _BoxR2;
                float  _FullOpaque;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings shadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, _MainLightPosition.xyz));
                return OUT;
            }

            half4 shadowFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}