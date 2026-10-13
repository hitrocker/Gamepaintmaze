Shader "Custom/BallSkinSports"
{
    // Extra sports skins (_SkinType 16-25). Kept in a second shader because
    // Unity's Cg parser overflows when these sit in Custom/BallSkin.
    Properties
    {
        _SkinType ("Skin Type", Int) = 16
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _LineColor ("Line Color", Color) = (0,0,0,1)
        _AccentColor ("Accent Color", Color) = (1,1,1,1)
        _Glossiness ("Glossiness", Range(0,1)) = 0.5
        _BumpStrength ("Bump Strength", Range(0,3)) = 1.0
        _AoStrength ("Crevice Shading", Range(0,1)) = 0.35
        _LitStrength ("Lit Albedo Strength", Range(0,1)) = 1.0
        _EmissionColor ("Emission Tint", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        struct Input
        {
            float3 objDir;
        };

        int _SkinType;
        fixed4 _BaseColor;
        fixed4 _LineColor;
        fixed4 _AccentColor;
        half _Glossiness;
        half _BumpStrength;
        half _AoStrength;
        half _LitStrength;
        fixed4 _EmissionColor;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.objDir = v.vertex.xyz;
            float3 n = normalize(v.vertex.xyz);
            float3 up = abs(n.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            v.tangent = float4(normalize(cross(up, n)), 1.0);
        }

        float SeamMask(float field, float halfWidth)
        {
            float aa = fwidth(field) * 1.5 + 1e-5;
            return 1.0 - smoothstep(halfWidth, halfWidth + aa, abs(field));
        }

        float Hash31(float3 p)
        {
            return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = Hash31(i + float3(0, 0, 0));
            float n100 = Hash31(i + float3(1, 0, 0));
            float n010 = Hash31(i + float3(0, 1, 0));
            float n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1));
            float n101 = Hash31(i + float3(1, 0, 1));
            float n011 = Hash31(i + float3(0, 1, 1));
            float n111 = Hash31(i + float3(1, 1, 1));
            float x00 = lerp(n000, n100, f.x);
            float x10 = lerp(n010, n110, f.x);
            float x01 = lerp(n001, n101, f.x);
            float x11 = lerp(n011, n111, f.x);
            return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
        }

        float Fbm(float3 p)
        {
            float sum = 0.5 * ValueNoise(p);
            sum += 0.25 * ValueNoise(p * 2.03);
            sum += 0.125 * ValueNoise(p * 4.01);
            return sum / 0.875;
        }

        float Groove(float dist, float halfWidth, float depth)
        {
            float t = saturate(abs(dist) / halfWidth);
            return depth * (1.0 - t * t);
        }

        fixed3 Tennis(float3 d)
        {
            float lon = atan2(d.z, d.x);
            float field = d.y - 0.58 * sin(2.0 * lon);
            fixed3 col = lerp(_BaseColor.rgb, _AccentColor.rgb, SeamMask(field, 0.055));
            return lerp(col, _LineColor.rgb, SeamMask(field, 0.018));
        }

        float TennisHeight(float3 d)
        {
            float lon = atan2(d.z, d.x);
            return 1.0 - Groove(d.y - 0.58 * sin(2.0 * lon), 0.040, 0.75);
        }

        fixed3 Volleyball(float3 d)
        {
            float lon = atan2(d.z, d.x);
            float c1 = d.y - 0.52 * sin(lon);
            float c2 = d.y - 0.52 * sin(lon + 2.0943951);
            float c3 = d.y - 0.52 * sin(lon + 4.1887902);
            float seams = max(max(SeamMask(c1, 0.016), SeamMask(c2, 0.016)),
                              SeamMask(c3, 0.016));
            float id = step(0.0, c1) + 2.0 * step(0.0, c2) + 4.0 * step(0.0, c3);
            float blue = max(1.0 - abs(id - 1.0), 1.0 - abs(id - 6.0));
            float yellow = max(1.0 - abs(id - 2.0), 1.0 - abs(id - 5.0));
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, saturate(blue));
            col = lerp(col, _AccentColor.rgb, saturate(yellow));
            return lerp(col, float3(0.12, 0.12, 0.14), seams);
        }

        float VolleyballHeight(float3 d)
        {
            float lon = atan2(d.z, d.x);
            float g = Groove(d.y - 0.52 * sin(lon), 0.032, 0.80);
            g = max(g, Groove(d.y - 0.52 * sin(lon + 2.0943951), 0.032, 0.80));
            g = max(g, Groove(d.y - 0.52 * sin(lon + 4.1887902), 0.032, 0.80));
            return 1.0 - g;
        }

        float HexDist(float2 p)
        {
            p.x += 0.5 * floor(p.y);
            float2 f = frac(p) - 0.5;
            return length(float2(f.x * 1.12, f.y));
        }

        float HexCell(float2 p)
        {
            p.x += 0.5 * floor(p.y);
            float2 f = frac(p) - 0.5;
            return max(abs(f.x) * 0.8660254 + abs(f.y) * 0.5, abs(f.y));
        }

        float TriplanarHex(float3 d, float scale, float circular)
        {
            float3 a = abs(d);
            float3 w = a * a * a * a;
            w /= max(w.x + w.y + w.z, 1e-4);
            float hx = lerp(HexCell(d.yz * scale), HexDist(d.yz * scale), circular);
            float hy = lerp(HexCell(d.xz * scale), HexDist(d.xz * scale), circular);
            float hz = lerp(HexCell(d.xy * scale), HexDist(d.xy * scale), circular);
            return hx * w.x + hy * w.y + hz * w.z;
        }

        // Regular hex dimples, triplanar so cube-face UVs do not seam.
        // Moon stays on irregular CraterField noise in Custom/BallSkin.
        float GolfDimple(float3 d)
        {
            return TriplanarHex(d, 5.6, 1.0);
        }

        fixed3 Golf(float3 d)
        {
            float dimple = 1.0 - smoothstep(0.20, 0.34, GolfDimple(d));
            return lerp(_BaseColor.rgb, _LineColor.rgb, dimple * 0.24);
        }

        float GolfHeight(float3 d)
        {
            return 1.0 - 0.55 * (1.0 - smoothstep(0.16, 0.34, GolfDimple(d)));
        }

        float BowlingHole(float3 d)
        {
            // Holes sit on the camera-facing hemisphere (shop looks down -Z).
            float a1 = acos(clamp(dot(d, normalize(float3(0.22, 0.48, -0.72))), -1.0, 1.0));
            float a2 = acos(clamp(dot(d, normalize(float3(-0.22, 0.48, -0.72))), -1.0, 1.0));
            float a3 = acos(clamp(dot(d, normalize(float3(0.0, 0.16, -0.84))), -1.0, 1.0));
            return saturate(1.0 - smoothstep(0.125, 0.155, min(a1, min(a2, a3))));
        }

        fixed3 Bowling(float3 d)
        {
            float split = d.x + 0.22 * sin(d.y * 3.4 + d.z * 1.1);
            fixed3 col = lerp(_LineColor.rgb, _BaseColor.rgb,
                              smoothstep(-0.12, 0.12, split));
            return lerp(col, _AccentColor.rgb, BowlingHole(d));
        }

        float BowlingHeight(float3 d)
        {
            return 1.0 - 0.85 * BowlingHole(d);
        }

        fixed3 EightBall(float3 d)
        {
            float3 face = normalize(float3(0.0, 0.08, -1.0));
            float3 up = abs(face.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 t = normalize(cross(up, face));
            float3 b = cross(face, t);
            float2 uv = float2(dot(d, t), dot(d, b));
            float disc = (1.0 - smoothstep(0.33, 0.38, length(uv))) *
                         step(0.15, saturate(dot(d, face)));
            float e1 = length(float2(uv.x / 0.09, (uv.y - 0.10) / 0.09));
            float e2 = length(float2(uv.x / 0.09, (uv.y + 0.10) / 0.09));
            float ring1 = smoothstep(0.70, 0.82, e1) * (1.0 - smoothstep(1.02, 1.14, e1));
            float ring2 = smoothstep(0.70, 0.82, e2) * (1.0 - smoothstep(1.02, 1.14, e2));
            fixed3 col = lerp(_BaseColor.rgb, _AccentColor.rgb, disc);
            return lerp(col, _LineColor.rgb, saturate(ring1 + ring2) * disc);
        }

        fixed3 BeachBall(float3 d)
        {
            float lon = atan2(d.z, d.x);
            float t = frac(lon / 6.2831853 + 0.5);
            float s = fmod(floor(t * 8.0), 4.0);
            float seam = SeamMask(frac(t * 8.0) - 0.5, 0.035);
            fixed3 col = _BaseColor.rgb;
            col = lerp(col, float3(0.96, 0.96, 0.94),
                       step(0.5, s) * (1.0 - step(1.5, s)));
            col = lerp(col, _AccentColor.rgb, step(1.5, s) * (1.0 - step(2.5, s)));
            col = lerp(col, _LineColor.rgb, step(2.5, s));
            return lerp(col, float3(0.12, 0.12, 0.14), seam);
        }

        float WiffleSlot(float3 d)
        {
            float3 n = normalize(float3(0.0, 0.12, -1.0));
            float3 t = normalize(cross(float3(0, 1, 0), n));
            float3 b = cross(n, t);
            float2 uv = float2(dot(d, t), dot(d, b));
            float ang = atan2(uv.y, uv.x);
            float rad = length(uv);
            float sector = fmod(fmod(ang, 0.78539816) + 0.78539816, 0.78539816)
                           - 0.39269908;
            float oblong = abs(sector) / 0.18 + abs(rad - 0.50) / 0.16;
            return step(0.08, dot(d, n)) * (1.0 - smoothstep(0.85, 1.15, oblong));
        }

        fixed3 Wiffle(float3 d)
        {
            return lerp(_BaseColor.rgb, _LineColor.rgb, WiffleSlot(d));
        }

        float WiffleHeight(float3 d)
        {
            return 1.0 - 0.90 * WiffleSlot(d);
        }

        fixed3 Spikeball(float3 d)
        {
            float lon = atan2(d.z, d.x);
            float hex = HexCell(float2(lon * 2.546479, d.y * 5.4));
            return lerp(_BaseColor.rgb, _LineColor.rgb, smoothstep(0.30, 0.36, hex));
        }

        fixed3 Dodgeball(float3 d)
        {
            float lines = max(SeamMask(d.y, 0.018), SeamMask(d.x, 0.018));
            return lerp(_BaseColor.rgb, _LineColor.rgb, lines);
        }

        float TakrawWeave(float3 d)
        {
            float lon = atan2(d.z, d.x);
            float u = lon * 3.8197186;
            float v = d.y * 8.0;
            float su = abs(frac(u) - 0.5);
            float sv = abs(frac(v) - 0.5);
            float strip = frac(floor(u) + floor(v)) > 0.5 ? su : sv;
            return 1.0 - smoothstep(0.15, 0.22, strip);
        }

        fixed3 Takraw(float3 d)
        {
            float rattan = TakrawWeave(d);
            fixed3 col = lerp(_LineColor.rgb, _BaseColor.rgb, rattan);
            return lerp(col, _AccentColor.rgb, rattan * 0.28 * Fbm(d * 7.0));
        }

        float TakrawHeight(float3 d)
        {
            return 0.82 + 0.22 * TakrawWeave(d);
        }

        fixed3 SkinAlbedo(float3 d)
        {
            if (_SkinType == 16) return Tennis(d);
            if (_SkinType == 17) return Volleyball(d);
            if (_SkinType == 18) return Golf(d);
            if (_SkinType == 19) return Bowling(d);
            if (_SkinType == 20) return EightBall(d);
            if (_SkinType == 21) return BeachBall(d);
            if (_SkinType == 22) return Wiffle(d);
            if (_SkinType == 23) return Spikeball(d);
            if (_SkinType == 24) return Dodgeball(d);
            return Takraw(d);
        }

        float Height(float3 d)
        {
            float h = 1.0;
            if (_SkinType == 16) h = TennisHeight(d);
            if (_SkinType == 17) h = VolleyballHeight(d);
            if (_SkinType == 18) h = GolfHeight(d);
            if (_SkinType == 19) h = BowlingHeight(d);
            if (_SkinType == 22) h = WiffleHeight(d);
            if (_SkinType == 25) h = TakrawHeight(d);
            return h;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 d = normalize(IN.objDir);
            fixed3 albedo = SkinAlbedo(d);

            float3 up = abs(d.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 T = normalize(cross(up, d));
            float3 B = cross(d, T);
            const float eps = 0.01;
            const float bumpScale = 0.02;
            float h0 = Height(d);
            float hT = Height(normalize(d + T * eps));
            float hB = Height(normalize(d + B * eps));
            float2 slope = float2(h0 - hT, h0 - hB) *
                           (_BumpStrength * bumpScale / eps);

            o.Normal = normalize(float3(slope, 1.0));
            o.Albedo = albedo * _LitStrength *
                       lerp(1.0 - _AoStrength, 1.0, saturate(h0));
            o.Emission = albedo * _EmissionColor.rgb;
            o.Metallic = 0.0;
            o.Smoothness = lerp(_Glossiness * 0.45, _Glossiness, saturate(h0));
        }
        ENDCG
    }
    FallBack "Diffuse"
}
