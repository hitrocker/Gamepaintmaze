Shader "Custom/BallSkin"
{
    // Procedural sports-ball skins painted from the sphere's 3D surface
    // direction (object space) so seams/pentagons never pinch at the poles
    // and roll correctly with the mesh. Lit with the built-in Standard model
    // to match the board tiles.
    Properties
    {
        _SkinType ("Skin Type", Int) = 0
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
            // Object-space direction; unaffected by parent squash/stretch.
            o.objDir = v.vertex.xyz;

            // Force a longitude-aligned tangent so the tangent-space normals
            // built in surf() line up exactly with the mesh basis.
            float3 n = normalize(v.vertex.xyz);
            float3 up = abs(n.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            v.tangent = float4(normalize(cross(up, n)), 1.0);
        }

        // Antialiased coverage for a line/band centered at field==0 with the
        // given half width. fwidth keeps edges crisp on a small on-screen ball.
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

        // Three-octave fractal noise in [0,1]; enough character for cloud
        // bands and dusty surfaces without costing much on a small ball.
        float Fbm(float3 p)
        {
            float sum = 0.5 * ValueNoise(p);
            sum += 0.25 * ValueNoise(p * 2.03);
            sum += 0.125 * ValueNoise(p * 4.01);
            return sum / 0.875;
        }

        // One jittered impact per grid cell: sunken bowl with a raised rim.
        // Returns a signed height, roughly [-1.2, 0.8] after clamping.
        float CraterField(float3 d, float scale)
        {
            float3 p = d * scale;
            float3 base = floor(p);
            float h = 0.0;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        float3 cell = base + float3(x, y, z);
                        float seed = Hash31(cell);
                        float3 center =
                            cell + frac(seed * float3(1.0, 17.13, 113.7));
                        float radius = 0.13 + 0.44 * frac(seed * 51.7);
                        float t = length(p - center) / radius;
                        float bowl = -(1.0 - smoothstep(0.0, 0.9, t));
                        float rim =
                            0.55 * (1.0 - smoothstep(0.0, 0.32, abs(t - 0.95)));
                        h += bowl + rim;
                    }
                }
            }
            return clamp(h, -1.2, 0.8);
        }

        // Implicit heart, negative inside. Used for Pluto's Tombaugh Regio.
        float HeartField(float2 q)
        {
            float a = q.x * q.x + q.y * q.y - 1.0;
            return a * a * a - q.x * q.x * q.y * q.y * q.y;
        }

        // Soft elliptical blob centered on direction c, only on its own
        // hemisphere so the antipode stays clean.
        float OvalSpot(float3 d, float3 c, float rx, float ry)
        {
            float3 up = abs(c.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 t = normalize(cross(up, c));
            float3 b = cross(c, t);
            float2 uv = float2(dot(d, t), dot(d, b));
            float e = length(float2(uv.x / rx, uv.y / ry));
            return step(0.0, dot(d, c)) * (1.0 - smoothstep(0.7, 1.0, e));
        }

        // Returns (angular distance to nearest pentagon center, pentagon edge
        // radius at this bearing). Shared by the soccer colour and height.
        float2 SoccerFields(float3 d)
        {
            // 12 icosahedron vertices -> pentagon centers (normalized).
            const float a = 0.52573111;
            const float b = 0.85065081;
            float3 centers[12];
            centers[0]  = float3(0,  a,  b);
            centers[1]  = float3(0,  a, -b);
            centers[2]  = float3(0, -a,  b);
            centers[3]  = float3(0, -a, -b);
            centers[4]  = float3( a,  b, 0);
            centers[5]  = float3( a, -b, 0);
            centers[6]  = float3(-a,  b, 0);
            centers[7]  = float3(-a, -b, 0);
            centers[8]  = float3( b, 0,  a);
            centers[9]  = float3( b, 0, -a);
            centers[10] = float3(-b, 0,  a);
            centers[11] = float3(-b, 0, -a);

            // Nearest pentagon center (largest dot == smallest angle).
            float3 c = centers[0];
            float md = -1.0;
            for (int i = 0; i < 12; i++)
            {
                float dp = dot(d, centers[i]);
                if (dp > md) { md = dp; c = centers[i]; }
            }

            // Tangent frame at the nearest center for local polar coords.
            float3 refAxis = abs(c.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 t = normalize(cross(refAxis, c));
            float3 bt = cross(c, t);
            float ang = atan2(dot(d, bt), dot(d, t));
            float rad = acos(clamp(md, -1.0, 1.0));

            // 5-fold pentagon edge radius (same shaping as the icon pentagon).
            const float R = 0.42; // pentagon size (radians)
            const float sector = 6.2831853 / 5.0;
            float k = fmod(fmod(ang, sector) + sector, sector) - sector * 0.5;
            float edge = R * cos(3.14159265 / 5.0) / cos(k);
            return float2(rad, edge);
        }

        fixed3 Soccer(float3 d)
        {
            float2 f = SoccerFields(d);
            float rad = f.x;
            float edge = f.y;
            const float seamW = 0.02; // outline thickness (radians)

            float aa = fwidth(rad) + 1e-5;
            float pent = 1.0 - smoothstep(edge - aa, edge + aa, rad);
            float seam = (1.0 - smoothstep(edge + seamW - aa, edge + seamW + aa, rad)) - pent;

            fixed3 col = _BaseColor.rgb;
            col = lerp(col, _LineColor.rgb, saturate(seam));
            col = lerp(col, _AccentColor.rgb, saturate(pent));
            return col;
        }

        fixed3 Basketball(float3 d)
        {
            float w = 0.020;
            float m1 = SeamMask(d.x, w);                 // meridian (x=0)
            float m2 = SeamMask(d.z, w);                 // meridian (z=0)
            float m3 = SeamMask(abs(d.y) - 0.45, w);     // two curved bands
            float lines = max(max(m1, m2), m3);
            return lerp(_BaseColor.rgb, _LineColor.rgb, lines);
        }

        fixed3 Baseball(float3 d)
        {
            float lon = atan2(d.z, d.x);
            float field = d.y - 0.55 * sin(2.0 * lon);   // figure-eight seam
            float dash = step(0.5, frac(lon * 22.0));
            float stitch = SeamMask(field, 0.075) * dash;
            float core = SeamMask(field, 0.013);
            fixed3 col = _BaseColor.rgb;
            col = lerp(col, _AccentColor.rgb, stitch);
            col = lerp(col, _AccentColor.rgb, core);
            return col;
        }

        fixed3 Cricket(float3 d)
        {
            // Equator seam with a polished (slightly darker) hemisphere.
            float polish = smoothstep(-0.1, 0.55, d.x);
            fixed3 col = _BaseColor.rgb * lerp(1.0, 0.82, polish);
            float lon = atan2(d.z, d.x);
            float dash = step(0.45, frac(lon * 22.0));
            float stitch = SeamMask(abs(d.y) - 0.045, 0.030) * dash;
            float core = SeamMask(d.y, 0.016);
            col = lerp(col, _AccentColor.rgb, stitch);
            col = lerp(col, _LineColor.rgb, core);
            return col;
        }

        fixed3 Jupiter(float3 d)
        {
            // Turbulent latitude belts: warp the latitude before banding so the
            // zones churn instead of reading as clean stripes.
            float warp = (Fbm(d * 3.4) - 0.5) * 0.14;
            float lat = d.y + warp;
            float belt = 0.5 + 0.5 * sin(lat * 15.0);
            belt = saturate(belt * 0.70 + 0.30 * (0.5 + 0.5 * sin(lat * 6.3 + 1.4)));
            belt = smoothstep(0.24, 0.72, belt);
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, belt);
            col *= lerp(1.0, 0.72, smoothstep(0.55, 1.0, abs(d.y)));
            float spot = OvalSpot(d, normalize(float3(0.62, -0.34, 0.70)), 0.30, 0.17);
            col = lerp(col, _AccentColor.rgb, spot);
            return col;
        }

        fixed3 Mars(float3 d)
        {
            float maria = Fbm(d * 2.5);
            fixed3 col = lerp(
                _BaseColor.rgb,
                _LineColor.rgb,
                smoothstep(0.42, 0.70, maria));
            col *= lerp(0.84, 1.03, Fbm(d * 7.5));
            float capEdge = 0.94 - 0.05 * Fbm(d * 6.0);
            float cap = smoothstep(capEdge - 0.04, capEdge + 0.01, abs(d.y));
            col = lerp(col, _AccentColor.rgb, cap * 0.86);
            return col;
        }

        fixed3 Venus(float3 d)
        {
            float swirl = (Fbm(d * 2.1) - 0.5) * 3.0;
            float band = 0.5 + 0.5 * sin(d.y * 7.0 + swirl);
            band = smoothstep(0.22, 0.78, band);
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, band * 0.92);
            // Streaks: squash the noise in latitude so highlights smear sideways.
            float bright = saturate((Fbm(float3(d.x, d.y * 5.0, d.z) * 3.0) - 0.60) * 2.35);
            col = lerp(col, _AccentColor.rgb, bright * 0.68);
            col *= lerp(1.0, 0.82, smoothstep(0.6, 1.0, abs(d.y)));
            return col;
        }

        fixed3 Uranus(float3 d)
        {
            // Uranus rolls on its side, so band around x rather than y.
            float axis = d.x;
            float band = 0.5 + 0.5 * sin(axis * 7.0 + (Fbm(d * 2.0) - 0.5) * 1.2);
            band = smoothstep(0.18, 0.82, band);
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, band * 0.96);
            col = lerp(col, _AccentColor.rgb, saturate(0.6 - abs(axis) * 1.6) * 0.28);
            col *= lerp(1.0, 0.72, smoothstep(0.35, 1.0, abs(axis)));
            return col;
        }

        fixed3 Neptune(float3 d)
        {
            float band = 0.5 + 0.5 * sin(d.y * 9.0 + (Fbm(d * 2.3) - 0.5) * 1.8);
            band = smoothstep(0.20, 0.80, band);
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, band * 0.95);
            // Latitude-squashed noise turns blobs into wind streaks.
            float streak = saturate(
                (Fbm(float3(d.x * 2.2, d.y * 12.0, d.z * 2.2)) - 0.60) * 2.4);
            col = lerp(col, fixed3(0.60, 0.70, 0.90), streak * 0.68);
            float spot = OvalSpot(d, normalize(float3(-0.66, -0.26, 0.70)), 0.26, 0.16);
            col = lerp(col, _AccentColor.rgb, spot * 0.92);
            return col;
        }

        fixed3 Mercury(float3 d)
        {
            float c = CraterField(d, 6.0);
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, saturate(-c * 0.75));
            col = lerp(col, _AccentColor.rgb, saturate(c) * 0.55);
            col *= lerp(0.90, 1.08, ValueNoise(d * 5.0));
            return col;
        }

        // The maria are smooth basalt plains, so they both darken the surface
        // and flatten the craters that fall inside them.
        float MoonMaria(float3 d)
        {
            return smoothstep(0.46, 0.60, Fbm(d * 1.9));
        }

        float MoonCraters(float3 d)
        {
            return CraterField(d, 7.5) * (1.0 - 0.78 * MoonMaria(d));
        }

        fixed3 Moon(float3 d)
        {
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, MoonMaria(d));
            float c = MoonCraters(d);
            col *= lerp(0.72, 1.0, saturate(1.0 + c));
            col = lerp(col, _AccentColor.rgb, saturate(c) * 0.45);
            return col;
        }

        // Tombaugh Regio: the bright heart on Pluto's near side.
        float PlutoHeart(float3 d)
        {
            float3 c = normalize(float3(0.16, -0.22, -0.96));
            float3 t = normalize(cross(float3(0, 1, 0), c));
            float3 b = cross(c, t);
            float2 q = float2(dot(d, t), dot(d, b)) / 0.34;
            float near = step(0.25, dot(d, c));
            return near * (1.0 - smoothstep(-0.10, 0.25, HeartField(q)));
        }

        fixed3 Pluto(float3 d)
        {
            float c = CraterField(d, 6.5);
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, saturate(-c * 0.65));
            col *= lerp(0.90, 1.08, ValueNoise(d * 4.0));
            col = lerp(col, _AccentColor.rgb, PlutoHeart(d) * 0.84);
            return col;
        }

        // Falloff around a continent seed; cosR is cos(angular radius).
        float Blob(float3 d, float3 c, float cosR)
        {
            return saturate((dot(d, c) - cosR) / (1.0 - cosR));
        }

        // Stylised landmasses: hand-placed seeds roughly where the real
        // continents sit, so the world reads as Earth and not as noise.
        float Continents(float3 d)
        {
            float f = Blob(d, float3(-0.123,  0.707, -0.696), 0.913); // N America
            f = max(f, Blob(d, float3( 0.483, -0.259, -0.837), 0.949)); // S America
            f = max(f, Blob(d, float3( 0.936,  0.087,  0.341), 0.921)); // Africa
            f = max(f, Blob(d, float3( 0.595,  0.788,  0.159), 0.976)); // Europe
            f = max(f, Blob(d, float3( 0.000,  0.707,  0.707), 0.887)); // Asia
            f = max(f, Blob(d, float3(-0.641, -0.423,  0.641), 0.976)); // Australia
            return f;
        }

        fixed3 Earth(float3 d)
        {
            // Two-scale domain warp keeps coastlines ragged rather than blobby.
            float3 warp = d
                + (float3(
                    ValueNoise(d * 3.1),
                    ValueNoise(d * 3.1 + 11.0),
                    ValueNoise(d * 3.1 + 23.0)) - 0.5) * 0.30
                + (float3(
                    ValueNoise(d * 8.3),
                    ValueNoise(d * 8.3 + 31.0),
                    ValueNoise(d * 8.3 + 53.0)) - 0.5) * 0.16
                + (float3(
                    ValueNoise(d * 19.0),
                    ValueNoise(d * 19.0 + 71.0),
                    ValueNoise(d * 19.0 + 97.0)) - 0.5) * 0.05;
            float f = Continents(normalize(warp));
            float land = smoothstep(0.27, 0.31, f);

            fixed3 ocean = lerp(
                _LineColor.rgb,
                _BaseColor.rgb,
                smoothstep(0.0, 0.26, f));
            fixed3 ground = lerp(
                fixed3(0.24, 0.50, 0.28),
                fixed3(0.62, 0.55, 0.32),
                smoothstep(0.74, 1.0, f));
            fixed3 col = lerp(ocean, ground, land);

            float capEdge = 0.955 - 0.035 * ValueNoise(d * 6.0);
            col = lerp(
                col,
                _AccentColor.rgb,
                smoothstep(capEdge - 0.035, capEdge + 0.01, abs(d.y)) * 0.82);

            float cloud = saturate(
                (Fbm(float3(d.x * 2.0, d.y * 4.5, d.z * 2.0) * 1.7) - 0.55) * 2.8);
            return lerp(col, fixed3(0.72, 0.79, 0.84), cloud * 0.62);
        }

        fixed3 Sun(float3 d)
        {
            float gran = Fbm(d * 7.0);
            fixed3 col = lerp(
                _BaseColor.rgb,
                _AccentColor.rgb,
                smoothstep(0.36, 0.78, gran));
            float spots = smoothstep(0.66, 0.83, Fbm(d * 2.4));
            return lerp(col, _LineColor.rgb, spots * 0.84);
        }

        fixed3 Saturn(float3 d)
        {
            float warp = (Fbm(d * 3.0) - 0.5) * 0.10;
            float band = 0.5 + 0.5 * sin((d.y + warp) * 11.0);
            band = smoothstep(0.26, 0.74, band);
            fixed3 col = lerp(_BaseColor.rgb, _LineColor.rgb, band * 0.78);
            col *= lerp(1.0, 0.72, smoothstep(0.55, 1.0, abs(d.y)));
            return col;
        }

        // Smooth dip/rise of the given depth, peaking at dist == 0 and fading
        // to zero at |dist| == halfWidth.
        float Groove(float dist, float halfWidth, float depth)
        {
            float t = saturate(abs(dist) / halfWidth);
            return depth * (1.0 - t * t);
        }

        float SoccerHeight(float3 d)
        {
            float2 f = SoccerFields(d);
            float toEdge = f.x - f.y; // 0 on the pentagon border
            float h = 1.0 - Groove(toEdge, 0.045, 0.85);
            // Panels dome gently as they get further from the seam.
            h += 0.10 * saturate(abs(toEdge) / 0.35);
            return h;
        }

        float BasketballHeight(float3 d)
        {
            // Deep moulded channels; max() keeps crossings from doubling up.
            float g = Groove(d.x, 0.035, 0.95);
            g = max(g, Groove(d.z, 0.035, 0.95));
            g = max(g, Groove(abs(d.y) - 0.45, 0.035, 0.95));
            return 1.0 - g;
        }

        float BaseballHeight(float3 d)
        {
            float lon = atan2(d.z, d.x);
            float field = d.y - 0.55 * sin(2.0 * lon);
            float h = 1.0 - Groove(field, 0.030, 0.80);
            // Stitches sit raised on both sides of the seam channel.
            float dash = step(0.5, frac(lon * 22.0));
            h += dash * Groove(abs(abs(field) - 0.045), 0.030, 0.45);
            return h;
        }

        float CricketHeight(float3 d)
        {
            float h = 1.0 - Groove(d.y, 0.028, 0.70);
            float lon = atan2(d.z, d.x);
            float dash = step(0.45, frac(lon * 22.0));
            h += dash * Groove(abs(abs(d.y) - 0.045), 0.030, 0.55);
            return h;
        }

        // Gas giants stay perfectly smooth; rocky bodies get mild terrain.
        float MarsHeight(float3 d)
        {
            return 0.72 + 0.28 * Fbm(d * 5.5);
        }

        float CraterHeight(float3 d, float scale)
        {
            return 0.74 + 0.22 * CraterField(d, scale);
        }

        float PlutoHeight(float3 d)
        {
            // The regio is a smooth nitrogen plain, so flatten it.
            return lerp(CraterHeight(d, 6.5), 0.95, PlutoHeart(d));
        }

        float Height(float3 d)
        {
            // Single exit point: the multi-return form makes some GLSL/SPIR-V
            // backends warn about a potentially uninitialised result.
            float h = 1.0;
            if (_SkinType == 1)       h = CricketHeight(d);
            else if (_SkinType == 2)  h = SoccerHeight(d);
            else if (_SkinType == 3)  h = BasketballHeight(d);
            else if (_SkinType == 4)  h = BaseballHeight(d);
            else if (_SkinType == 6)  h = MarsHeight(d);
            else if (_SkinType == 10) h = CraterHeight(d, 6.0);
            else if (_SkinType == 11) h = 0.74 + 0.22 * MoonCraters(d);
            else if (_SkinType == 12) h = PlutoHeight(d);
            return h;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 d = normalize(IN.objDir);
            fixed3 albedo;
            if (_SkinType == 1)      albedo = Cricket(d);
            else if (_SkinType == 2) albedo = Soccer(d);
            else if (_SkinType == 3) albedo = Basketball(d);
            else if (_SkinType == 4) albedo = Baseball(d);
            else if (_SkinType == 5) albedo = Jupiter(d);
            else if (_SkinType == 6) albedo = Mars(d);
            else if (_SkinType == 7) albedo = Venus(d);
            else if (_SkinType == 8) albedo = Uranus(d);
            else if (_SkinType == 9) albedo = Neptune(d);
            else if (_SkinType == 10) albedo = Mercury(d);
            else if (_SkinType == 11) albedo = Moon(d);
            else if (_SkinType == 12) albedo = Pluto(d);
            else if (_SkinType == 13) albedo = Earth(d);
            else if (_SkinType == 14) albedo = Sun(d);
            else if (_SkinType == 15) albedo = Saturn(d);
            else                     albedo = _BaseColor.rgb;

            // Relief from the height field: sample along the same longitude
            // frame that vert() writes into v.tangent.
            float3 up = abs(d.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 T = normalize(cross(up, d));
            float3 B = cross(d, T);
            const float eps = 0.01;
            const float bumpScale = 0.02; // height units per sphere radius
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
