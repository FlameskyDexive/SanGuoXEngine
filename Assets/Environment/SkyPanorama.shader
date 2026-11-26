Shader "Sanguo/SkyPanorama"

Variants
{
}

Properties
{
    [MainTexture] _MainTex ("Panorama (equirectangular HDR)", Texture2D) = "white"
}

Pass "SkyPanorama"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull None
    ZWrite Off
    ZTest Off

	GLSLPROGRAM
        Vertex
        {
            #include "XEngineCG"
            #include "VertexAttributes"

            out vec3 vDirection;

            void main()
            {
                gl_Position = TransformSkyboxClip(vertexPosition);
                vDirection = vertexPosition;
            }
        }

        Fragment
        {
            #include "XEngineCG"

            layout (location = 0) out vec4 fragColor;

            in vec3 vDirection;

            uniform sampler2D _MainTex;

            void main()
            {
                vec3 direction = normalize(vDirection);
                // Equirectangular mapping: u wraps around Y (Repeat import setting), v spans pole to pole.
                float u = atan(direction.z, direction.x) * 0.15915494 + 0.5;
                float v = asin(clamp(direction.y, -1.0, 1.0)) * 0.31830989 + 0.5;
                vec3 color = texture(_MainTex, vec2(u, v)).rgb;
                fragColor = vec4(color, 1.0);
            }
        }
    ENDGLSL

    SLANGPROGRAM
        Vertex
        {
            #include "XEngineCG"

            struct VSInput
            {
                float3 vertexPosition : POSITION;
            };

            struct VSOutput
            {
                float4 position : SV_Position;
                float3 vDirection : TEXCOORD0;
            };

            VSOutput main(VSInput input)
            {
                VSOutput output;
                output.position = TransformSkyboxClip(input.vertexPosition);
                output.vDirection = input.vertexPosition;
                return output;
            }
        }

        Fragment
        {
            #include "XEngineCG"

            Texture2D _MainTex : register(t0);
            SamplerState _MainTexSampler : register(s0);

            struct PSInput
            {
                float4 position : SV_Position;
                float3 vDirection : TEXCOORD0;
            };

            float4 main(PSInput input) : SV_Target
            {
                float3 direction = normalize(input.vDirection);
                float u = atan2(direction.z, direction.x) * 0.15915494 + 0.5;
                float v = asin(clamp(direction.y, -1.0, 1.0)) * 0.31830989 + 0.5;
                float3 color = _MainTex.Sample(_MainTexSampler, float2(u, v)).rgb;
                return float4(color, 1.0);
            }
        }
    ENDSLANG
}
