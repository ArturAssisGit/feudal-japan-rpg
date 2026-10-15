// Used by SkyboxCycle: shows two 360 sky images and fades smoothly from one to the other.
Shader "Skybox/Panoramic Blend"
{
    Properties
    {
        _TexA ("Sky A (HDR)", 2D) = "grey" {}
        _TexB ("Sky B (HDR)", 2D) = "grey" {}
        _Blend ("Blend A -> B", Range(0, 1)) = 0
        [Gamma] _ExposureA ("Exposure A", Float) = 1
        [Gamma] _ExposureB ("Exposure B", Float) = 1
        [Gamma] _TintA ("Tint A", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _TintB ("Tint B", Color) = (0.5, 0.5, 0.5, 0.5)
        _RotationA ("Rotation A", Range(0, 360)) = 0
        _RotationB ("Rotation B", Range(0, 360)) = 0
        _Brightness ("Brightness", Float) = 1
        _SeamBlend ("Seam Smoothing (0 = off)", Range(0, 0.25)) = 0.06
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _TexA;
            sampler2D _TexB;
            half4 _TexA_HDR;
            half4 _TexB_HDR;
            half _Blend;
            half _ExposureA;
            half _ExposureB;
            half4 _TintA;
            half4 _TintB;
            float _RotationA;
            float _RotationB;
            half _Brightness;
            float _SeamBlend;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float3 RotateY (float3 d, float degrees)
            {
                float a = degrees * UNITY_PI / 180.0;
                float s, c;
                sincos(a, s, c);
                return float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
            }

            // Same mapping as Unity's Skybox/Panoramic (Latitude Longitude, 360 degrees)
            float2 ToRadialCoords (float3 d)
            {
                d = normalize(d);
                float latitude = acos(d.y);
                float longitude = atan2(d.z, d.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            half3 SampleSky (sampler2D tex, half4 hdr, float3 dir, float rotation, half4 tint, half exposure)
            {
                float2 uv = ToRadialCoords(RotateY(dir, rotation));
                half4 c = tex2Dlod(tex, float4(uv, 0, 0)); // mip 0: no thin line at the back

                // Images whose left and right edges don't match leave a hard vertical line.
                // Near that line, mix in the mirrored image so both sides meet at the same color.
                float seamDist = min(uv.x, 1.0 - uv.x);
                if (_SeamBlend > 0.0001 && seamDist < _SeamBlend)
                {
                    half4 mirrored = tex2Dlod(tex, float4(1.0 - uv.x, uv.y, 0, 0));
                    float t = 1.0 - seamDist / _SeamBlend;          // 1 on the line, 0 at the edge of the zone
                    float k = 0.5 * t * t * (3.0 - 2.0 * t);        // exactly 50/50 on the line
                    c = lerp(c, mirrored, k);
                }

                half3 col = DecodeHDR(c, hdr);
                col = col * tint.rgb * unity_ColorSpaceDouble.rgb;
                return col * exposure;
            }

            half4 frag (v2f i) : SV_Target
            {
                half3 a = SampleSky(_TexA, _TexA_HDR, i.dir, _RotationA, _TintA, _ExposureA);
                half3 col = a;
                if (_Blend > 0.001)
                {
                    half3 b = SampleSky(_TexB, _TexB_HDR, i.dir, _RotationB, _TintB, _ExposureB);
                    col = lerp(a, b, _Blend);
                }
                return half4(col * _Brightness, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
