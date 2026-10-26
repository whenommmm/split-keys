using System;
using UnityEngine;

namespace Handshake.SplitKeys
{
    // Colors, fonts and generated sprites for the "Keycap Candy" look.
    public static class Sk
    {
        public static readonly Color Bg = Hex("#12101C");
        public static readonly Color Mat = Hex("#1B1829");
        public static readonly Color Mat2 = Hex("#252137");
        public static readonly Color Socket = Hex("#0D0B15");
        public static readonly Color Line = Hex("#34304A");
        public static readonly Color Text = Hex("#F4F1FF");
        public static readonly Color Muted = Hex("#A19BBE");
        public static readonly Color Dim = Hex("#6B6587");
        public static readonly Color Ink = Hex("#15121F");
        public static readonly Color Cream = Hex("#FFF3D6");
        public static readonly Color Slate = Hex("#3A3552");
        public static readonly Color Danger = Hex("#FF5252");
        public static readonly Color Ok = Hex("#69DB7C");

        public static readonly string[] PlayerHex = { "#4DABF7", "#FF922B", "#A9E34B", "#F783AC", "#FFD43B", "#9775FA", "#38D9A9", "#E3DEF5" };
        public static readonly string[] PlayerNames = { "Sky", "Tangerine", "Lime", "Bubblegum", "Lemon", "Grape", "Mint", "Pearl" };

        public static Font Display;
        public static Font Body;
        public static Font Bold;

        public static void LoadFonts()
        {
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Display = Resources.Load<Font>("Fonts/Bungee-Regular") ?? fallback;
            Body = Resources.Load<Font>("Fonts/Rubik-Medium") ?? fallback;
            Bold = Resources.Load<Font>("Fonts/Rubik-Bold") ?? fallback;
        }

        public static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out var color) ? color : Color.white;
        }

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // color-mix(in srgb, face 55%, black)
        public static Color Edge(Color face) => new Color(face.r * 0.55f, face.g * 0.55f, face.b * 0.55f, face.a);

        public static string ColorName(string hex)
        {
            for (var i = 0; i < PlayerHex.Length; i++)
                if (string.Equals(PlayerHex[i], hex, StringComparison.OrdinalIgnoreCase)) return PlayerNames[i];
            return "your color";
        }
    }

    // Sprites drawn from signed distance functions at startup, so the build needs no image assets.
    public static class SkGfx
    {
        public const float RoundRadius = 32f;
        public static Sprite Round;
        public static Sprite Soft;
        public static Texture2D Dots;
        public static Texture2D Glow;
        public static Sprite Heart, HeartLine, Speaker, SpeakerOff, Crown, Bolt, Target, Burst, Share, Back;

        public static void Init()
        {
            if (Round != null) return;
            Round = SlicedSprite(128, RoundRadius, 0f, 48);
            Soft = SlicedSprite(128, 40f, 14f, 60);
            Dots = MakeDots();
            Glow = MakeGlow();
            Heart = IconSprite(p => SdHeart(p));
            HeartLine = IconSprite(p => Mathf.Abs(SdHeart(p)) - 0.07f);
            Speaker = IconSprite(SdSpeaker);
            SpeakerOff = IconSprite(p => Mathf.Min(SdSpeakerBody(p),
                Mathf.Min(SdSegment(p, new Vector2(0.28f, -0.28f), new Vector2(0.78f, 0.28f)) - 0.08f,
                          SdSegment(p, new Vector2(0.28f, 0.28f), new Vector2(0.78f, -0.28f)) - 0.08f)));
            Crown = IconSprite(p => SdPolygon(p, new[] {
                new Vector2(-0.75f, -0.55f), new Vector2(0.75f, -0.55f), new Vector2(0.85f, 0.45f),
                new Vector2(0.4f, 0.05f), new Vector2(0f, 0.65f), new Vector2(-0.4f, 0.05f), new Vector2(-0.85f, 0.45f) }) - 0.04f);
            Bolt = IconSprite(p => SdPolygon(p, new[] {
                new Vector2(0.2f, 0.9f), new Vector2(-0.55f, -0.08f), new Vector2(-0.05f, -0.08f),
                new Vector2(-0.25f, -0.9f), new Vector2(0.55f, 0.12f), new Vector2(0.05f, 0.12f) }) - 0.03f);
            Target = IconSprite(p =>
            {
                var r = p.magnitude;
                return Mathf.Min(Mathf.Abs(r - 0.78f) - 0.1f, Mathf.Min(Mathf.Abs(r - 0.44f) - 0.1f, r - 0.14f));
            });
            Burst = IconSprite(p =>
            {
                var points = new Vector2[16];
                for (var i = 0; i < 16; i++)
                {
                    var angle = i * Mathf.PI / 8f + Mathf.PI / 2f;
                    var radius = i % 2 == 0 ? 0.92f : 0.45f;
                    points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
                return SdPolygon(p, points);
            });
            Back = IconSprite(p => Mathf.Min(
                SdSegment(p, new Vector2(-0.55f, 0f), new Vector2(0.65f, 0f)) - 0.11f,
                Mathf.Min(SdSegment(p, new Vector2(-0.6f, 0f), new Vector2(-0.1f, 0.5f)) - 0.11f,
                          SdSegment(p, new Vector2(-0.6f, 0f), new Vector2(-0.1f, -0.5f)) - 0.11f)));
            Share = IconSprite(p =>
            {
                var box = Mathf.Max(SdBox(p, new Vector2(0f, -0.35f), new Vector2(0.62f, 0.5f), 0.12f), -SdBox(p, new Vector2(0f, -0.35f), new Vector2(0.48f, 0.36f), 0.02f));
                box = Mathf.Max(box, -SdBox(p, new Vector2(0f, 0.2f), new Vector2(0.22f, 0.3f), 0f));
                var stem = SdSegment(p, new Vector2(0f, -0.25f), new Vector2(0f, 0.75f)) - 0.08f;
                var left = SdSegment(p, new Vector2(0f, 0.8f), new Vector2(-0.32f, 0.48f)) - 0.08f;
                var right = SdSegment(p, new Vector2(0f, 0.8f), new Vector2(0.32f, 0.48f)) - 0.08f;
                return Mathf.Min(box, Mathf.Min(stem, Mathf.Min(left, right)));
            });
        }

        // White rounded square with 9-slice borders; Image.pixelsPerUnitMultiplier picks the on-screen radius.
        static Sprite SlicedSprite(int size, float radius, float softness, int border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            var half = size / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                var d = SdBox(p, Vector2.zero, new Vector2(half - softness - 1f, half - softness - 1f), radius);
                float a;
                if (softness > 0f) a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((softness - d) / (softness * 2f)));
                else a = Mathf.Clamp01(0.5f - d);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        static Sprite IconSprite(Func<Vector2, float> sdf)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pixels = new Color32[size * size];
            var scale = 2f / size;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) * scale - 1f, (y + 0.5f) * scale - 1f);
                var d = sdf(p) / scale;
                var a = Mathf.Clamp01(0.5f - d);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static Texture2D MakeDots()
        {
            const int size = 22;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 0);
            pixels[(size - 2) * size + 1] = new Color32(255, 255, 255, 14);
            pixels[(size - 2) * size + 2] = new Color32(255, 255, 255, 6);
            pixels[(size - 3) * size + 1] = new Color32(255, 255, 255, 6);
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D MakeGlow()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                var a = Mathf.Clamp01(1f - p.magnitude);
                pixels[y * size + x] = new Color32(151, 117, 250, (byte)(a * a * 41f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static float SdBox(Vector2 p, Vector2 center, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + new Vector2(radius, radius);
            var outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static float SdSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var pa = p - a;
            var ba = b - a;
            var h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        static float SdPolygon(Vector2 p, Vector2[] v)
        {
            var d = Vector2.Dot(p - v[0], p - v[0]);
            var s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                var c1 = p.y >= v[i].y;
                var c2 = p.y < v[j].y;
                var c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s *= -1f;
            }
            return s * Mathf.Sqrt(d);
        }

        static float SdHeart(Vector2 p)
        {
            // Inigo Quilez's heart, scaled to fill the icon square.
            p = new Vector2(Mathf.Abs(p.x) / 1.15f, (p.y + 0.82f) / 1.15f);
            float d;
            if (p.y + p.x > 1f) d = (p - new Vector2(0.25f, 0.75f)).magnitude - Mathf.Sqrt(2f) / 4f;
            else
            {
                var a = (p - new Vector2(0f, 1f)).sqrMagnitude;
                var m = 0.5f * Mathf.Max(p.x + p.y, 0f);
                var b = (p - new Vector2(m, m)).sqrMagnitude;
                d = Mathf.Sqrt(Mathf.Min(a, b)) * Mathf.Sign(p.x - p.y);
            }
            return d * 1.15f;
        }

        static float SdSpeakerBody(Vector2 p)
        {
            return SdPolygon(p, new[] {
                new Vector2(-0.85f, -0.25f), new Vector2(-0.45f, -0.25f), new Vector2(0.05f, -0.7f),
                new Vector2(0.05f, 0.7f), new Vector2(-0.45f, 0.25f), new Vector2(-0.85f, 0.25f) }) - 0.04f;
        }

        static float SdSpeaker(Vector2 p)
        {
            var q = p - new Vector2(0.05f, 0f);
            var wedge = Mathf.Abs(q.y) * 0.9f - q.x;
            var arc1 = Mathf.Max(Mathf.Abs(q.magnitude - 0.38f) - 0.075f, wedge);
            var arc2 = Mathf.Max(Mathf.Abs(q.magnitude - 0.7f) - 0.075f, wedge);
            return Mathf.Min(SdSpeakerBody(p), Mathf.Min(arc1, arc2));
        }
    }
}
