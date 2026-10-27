using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // Bean-shaped player avatars drawn from signed distance functions: a body color, eyes, a mouth and a hat.
    // The part counts match AVATAR_PARTS in server/game.js.
    public static class SkAvatar
    {
        public const int Colors = 12, Eyes = 8, Mouths = 8, Hats = 8;
        public static readonly string[] BodyHex =
        {
            "#FF4D4D", "#FF8A1F", "#FFD23F", "#9BE04B", "#2DBE60", "#2BD4C1",
            "#4DB5FF", "#3D6BFF", "#9B5CFF", "#FF6FB5", "#FFB38A", "#F1ECE3"
        };

        const int Size = 192;
        const string PrefsKey = "splitkeys.avatar";
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static readonly Color InkColor = Sk.Ink;
        static readonly Color WhiteColor = Color.white;

        public static AvatarData Random()
        {
            return new AvatarData
            {
                c = UnityEngine.Random.Range(0, Colors), e = UnityEngine.Random.Range(0, Eyes),
                m = UnityEngine.Random.Range(0, Mouths), h = UnityEngine.Random.Range(0, Hats)
            };
        }

        public static AvatarData Load()
        {
            var parts = PlayerPrefs.GetString(PrefsKey, "").Split(',');
            if (parts.Length == 4 && int.TryParse(parts[0], out var c) && int.TryParse(parts[1], out var e)
                && int.TryParse(parts[2], out var m) && int.TryParse(parts[3], out var h))
                return Clamp(new AvatarData { c = c, e = e, m = m, h = h });
            var fresh = Random();
            Save(fresh);
            return fresh;
        }

        public static void Save(AvatarData a)
        {
            PlayerPrefs.SetString(PrefsKey, a.Key);
            PlayerPrefs.Save();
        }

        public static AvatarData Clamp(AvatarData a)
        {
            if (a == null) return new AvatarData();
            return new AvatarData { c = Wrap(a.c, Colors), e = Wrap(a.e, Eyes), m = Wrap(a.m, Mouths), h = Wrap(a.h, Hats) };
        }

        public static int Wrap(int value, int count) => ((value % count) + count) % count;

        public static Texture2D Texture(AvatarData avatar)
        {
            var a = Clamp(avatar);
            if (cache.TryGetValue(a.Key, out var tex) && tex != null) return tex;
            if (cache.Count > 48)
            {
                foreach (var old in cache.Values) if (old != null) UnityEngine.Object.Destroy(old);
                cache.Clear();
            }
            tex = Paint(a);
            cache[a.Key] = tex;
            return tex;
        }

        // ---------- painting ----------

        sealed class Sheet
        {
            public readonly Color[] Px = new Color[Size * Size];

            // Draws a shape with an optional ink outline `outline` units wide (units: the square spans -1..1).
            public void Shape(Func<Vector2, float> sdf, Color fill, float outline)
            {
                const float scale = Size / 2f;
                for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) / scale - 1f, (y + 0.5f) / scale - 1f);
                    var d = sdf(p) * scale;
                    if (d > outline * scale + 1f) continue;
                    var i = y * Size + x;
                    if (outline > 0f) Blend(i, InkColor, Mathf.Clamp01(0.5f - (d - outline * scale)));
                    Blend(i, fill, Mathf.Clamp01(0.5f - d));
                }
            }

            void Blend(int i, Color c, float a)
            {
                if (a <= 0f) return;
                var dst = Px[i];
                var outA = a + dst.a * (1f - a);
                if (outA <= 0f) return;
                Px[i] = new Color(
                    (c.r * a + dst.r * dst.a * (1f - a)) / outA,
                    (c.g * a + dst.g * dst.a * (1f - a)) / outA,
                    (c.b * a + dst.b * dst.a * (1f - a)) / outA,
                    outA);
            }
        }

        const float Line = 0.05f;
        static readonly Vector2 Body = new Vector2(0f, -0.2f);
        static readonly Vector2 EyeL = new Vector2(-0.22f, 0.1f);
        static readonly Vector2 EyeR = new Vector2(0.22f, 0.1f);
        static readonly Vector2 Mouth = new Vector2(0f, -0.2f);

        static Texture2D Paint(AvatarData a)
        {
            var canvas = new Sheet();
            var color = Sk.Hex(BodyHex[a.c]);
            if (a.h == 6) Headphones(canvas, false);
            canvas.Shape(p => SkGfx.SdBox(p, Body, new Vector2(0.6f, 0.68f), 0.56f), color, Line);
            // A soft belly patch keeps the flat body from looking like a sticker.
            canvas.Shape(p => Ellipse(p, Body + new Vector2(0f, -0.3f), new Vector2(0.34f, 0.2f)), Color.Lerp(color, WhiteColor, 0.28f), 0f);
            DrawEyes(canvas, a.e);
            DrawMouth(canvas, a.m);
            DrawHat(canvas, a.h);

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            tex.SetPixels(canvas.Px);
            tex.Apply(true, true);
            return tex;
        }

        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
        {
            var q = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
            return (q.magnitude - 1f) * Mathf.Min(r.x, r.y);
        }

        // Part of a ring: the half above (up = 1) or below (up = -1) its center.
        static float Arc(Vector2 p, Vector2 c, float r, float width, float up)
        {
            var ring = Mathf.Abs((p - c).magnitude - r) - width;
            var q = p - c;
            if (q.y * up >= 0f) return ring;
            var end = new Vector2(Mathf.Sign(q.x) * r, 0f);
            return (q - end).magnitude - width;
        }

        static float Seg(Vector2 p, Vector2 a, Vector2 b, float width) => SkGfx.SdSegment(p, a, b) - width;

        static void DrawEyes(Sheet canvas, int style)
        {
            const float w = 0.034f;
            switch (style)
            {
                case 0: // dots
                    foreach (var c in new[] { EyeL, EyeR }) canvas.Shape(p => Circle(p, c, 0.085f), InkColor, 0f);
                    break;
                case 1: // googly
                    foreach (var c in new[] { EyeL, EyeR })
                    {
                        canvas.Shape(p => Circle(p, c, 0.15f), WhiteColor, 0.035f);
                        canvas.Shape(p => Circle(p, c + new Vector2(0.03f, -0.03f), 0.07f), InkColor, 0f);
                    }
                    break;
                case 2: // happy
                    foreach (var c in new[] { EyeL, EyeR }) canvas.Shape(p => Arc(p, c - new Vector2(0f, 0.03f), 0.09f, w, 1f), InkColor, 0f);
                    break;
                case 3: // sleepy
                    foreach (var c in new[] { EyeL, EyeR }) canvas.Shape(p => Arc(p, c + new Vector2(0f, 0.04f), 0.09f, w, -1f), InkColor, 0f);
                    break;
                case 4: // angry
                    foreach (var c in new[] { EyeL, EyeR }) canvas.Shape(p => Circle(p, c - new Vector2(0f, 0.02f), 0.075f), InkColor, 0f);
                    canvas.Shape(p => Seg(p, EyeL + new Vector2(-0.12f, 0.17f), EyeL + new Vector2(0.1f, 0.09f), w), InkColor, 0f);
                    canvas.Shape(p => Seg(p, EyeR + new Vector2(0.12f, 0.17f), EyeR + new Vector2(-0.1f, 0.09f), w), InkColor, 0f);
                    break;
                case 5: // wink
                    canvas.Shape(p => Circle(p, EyeL, 0.085f), InkColor, 0f);
                    canvas.Shape(p => Mathf.Min(Seg(p, EyeR + new Vector2(-0.09f, 0.07f), EyeR + new Vector2(0.06f, 0f), w),
                                                Seg(p, EyeR + new Vector2(0.06f, 0f), EyeR + new Vector2(-0.09f, -0.07f), w)), InkColor, 0f);
                    break;
                case 6: // glasses
                    foreach (var c in new[] { EyeL, EyeR })
                    {
                        canvas.Shape(p => Circle(p, c, 0.14f), WhiteColor, 0.04f);
                        canvas.Shape(p => Circle(p, c, 0.055f), InkColor, 0f);
                    }
                    canvas.Shape(p => Seg(p, EyeL + new Vector2(0.15f, 0.02f), EyeR - new Vector2(0.15f, -0.02f), 0.03f), InkColor, 0f);
                    break;
                default: // dizzy
                    foreach (var c in new[] { EyeL, EyeR })
                        canvas.Shape(p => Mathf.Min(Seg(p, c + new Vector2(-0.08f, 0.08f), c + new Vector2(0.08f, -0.08f), w),
                                                    Seg(p, c + new Vector2(-0.08f, -0.08f), c + new Vector2(0.08f, 0.08f), w)), InkColor, 0f);
                    break;
            }
        }

        static void DrawMouth(Sheet canvas, int style)
        {
            const float w = 0.034f;
            var tongue = Sk.Hex("#FF6F91");
            switch (style)
            {
                case 0: // smile
                    canvas.Shape(p => Arc(p, Mouth + new Vector2(0f, 0.08f), 0.15f, w, -1f), InkColor, 0f);
                    break;
                case 1: // big open grin
                    canvas.Shape(p => Mathf.Max(Circle(p, Mouth + new Vector2(0f, 0.06f), 0.19f), p.y - (Mouth.y + 0.06f)), InkColor, 0f);
                    canvas.Shape(p => Mathf.Max(Ellipse(p, Mouth + new Vector2(0f, -0.1f), new Vector2(0.1f, 0.06f)), p.y - (Mouth.y + 0.06f)), tongue, 0f);
                    break;
                case 2: // flat
                    canvas.Shape(p => Seg(p, Mouth + new Vector2(-0.13f, 0f), Mouth + new Vector2(0.13f, 0f), w), InkColor, 0f);
                    break;
                case 3: // surprised
                    canvas.Shape(p => Ellipse(p, Mouth - new Vector2(0f, 0.02f), new Vector2(0.07f, 0.09f)), InkColor, 0f);
                    break;
                case 4: // tongue out
                    canvas.Shape(p => Mathf.Max(SkGfx.SdBox(p, Mouth + new Vector2(0.04f, -0.08f), new Vector2(0.075f, 0.1f), 0.075f), p.y - (Mouth.y + 0.02f)), tongue, 0.03f);
                    canvas.Shape(p => Seg(p, Mouth + new Vector2(-0.15f, 0.02f), Mouth + new Vector2(0.15f, 0.02f), w), InkColor, 0f);
                    break;
                case 5: // toothy grin
                    canvas.Shape(p => SkGfx.SdBox(p, Mouth, new Vector2(0.19f, 0.075f), 0.06f), WhiteColor, 0.034f);
                    canvas.Shape(p => Mathf.Min(Seg(p, Mouth + new Vector2(-0.06f, -0.07f), Mouth + new Vector2(-0.06f, 0.07f), 0.016f),
                                  Mathf.Min(Seg(p, Mouth + new Vector2(0.06f, -0.07f), Mouth + new Vector2(0.06f, 0.07f), 0.016f),
                                            Seg(p, Mouth + new Vector2(-0.18f, 0f), Mouth + new Vector2(0.18f, 0f), 0.016f))), InkColor, 0f);
                    break;
                case 6: // smirk
                    canvas.Shape(p => Mathf.Max(Arc(p, Mouth + new Vector2(0.02f, 0.1f), 0.15f, w, -1f), -(p.x - Mouth.x + 0.02f)), InkColor, 0f);
                    canvas.Shape(p => Seg(p, Mouth + new Vector2(-0.14f, -0.02f), Mouth + new Vector2(0.0f, -0.05f), w), InkColor, 0f);
                    break;
                default: // wobbly
                    canvas.Shape(p =>
                    {
                        var x = Mathf.Clamp(p.x, Mouth.x - 0.16f, Mouth.x + 0.16f);
                        var y = Mouth.y + 0.035f * Mathf.Sin((x - Mouth.x) * 30f);
                        return new Vector2(p.x - x, p.y - y).magnitude * 0.85f - w;
                    }, InkColor, 0f);
                    break;
            }
        }

        static void Headphones(Sheet canvas, bool cups)
        {
            var band = Sk.Hex("#3A3A48");
            if (!cups)
            {
                canvas.Shape(p => Arc(p, new Vector2(0f, 0.08f), 0.7f, 0.05f, 1f), band, 0.04f);
                return;
            }
            var cup = Sk.Hex("#FF3B3B");
            foreach (var x in new[] { -0.64f, 0.64f })
                canvas.Shape(p => SkGfx.SdBox(p, new Vector2(x, 0.06f), new Vector2(0.11f, 0.19f), 0.09f), cup, 0.045f);
        }

        static void DrawHat(Sheet canvas, int style)
        {
            const float top = 0.48f;
            switch (style)
            {
                case 1: // crown
                    canvas.Shape(p => SkGfx.SdPolygon(p, new[] {
                        new Vector2(-0.3f, top - 0.04f), new Vector2(0.3f, top - 0.04f), new Vector2(0.36f, top + 0.34f),
                        new Vector2(0.16f, top + 0.16f), new Vector2(0f, top + 0.4f), new Vector2(-0.16f, top + 0.16f), new Vector2(-0.36f, top + 0.34f) }),
                        Sk.Hex("#FFD23F"), 0.045f);
                    canvas.Shape(p => Circle(p, new Vector2(0f, top + 0.1f), 0.05f), Sk.Hex("#FF3B3B"), 0.03f);
                    break;
                case 2: // party hat
                    canvas.Shape(p => SkGfx.SdPolygon(p, new[] { new Vector2(-0.24f, top - 0.04f), new Vector2(0.24f, top - 0.04f), new Vector2(0.06f, top + 0.42f) }), Sk.Hex("#9B5CFF"), 0.045f);
                    canvas.Shape(p => Seg(p, new Vector2(-0.14f, top + 0.12f), new Vector2(0.18f, top + 0.06f), 0.03f), Sk.Hex("#FFD23F"), 0f);
                    canvas.Shape(p => Circle(p, new Vector2(0.06f, top + 0.44f), 0.07f), WhiteColor, 0.04f);
                    break;
                case 3: // beanie
                    canvas.Shape(p => Mathf.Max(Circle(p, new Vector2(0f, top - 0.12f), 0.48f), top - 0.02f - p.y), Sk.Hex("#2BD4C1"), 0.045f);
                    canvas.Shape(p => SkGfx.SdBox(p, new Vector2(0f, top - 0.01f), new Vector2(0.5f, 0.08f), 0.07f), Sk.Hex("#F1ECE3"), 0.045f);
                    canvas.Shape(p => Circle(p, new Vector2(0f, top + 0.4f), 0.08f), Sk.Hex("#F1ECE3"), 0.04f);
                    break;
                case 4: // cap
                    canvas.Shape(p => SkGfx.SdBox(p, new Vector2(0.28f, top - 0.02f), new Vector2(0.34f, 0.05f), 0.05f), Sk.Hex("#C81E1E"), 0.045f);
                    canvas.Shape(p => Mathf.Max(Circle(p, new Vector2(0f, top - 0.06f), 0.42f), top - 0.02f - p.y), Sk.Hex("#FF3B3B"), 0.045f);
                    canvas.Shape(p => Circle(p, new Vector2(0f, top + 0.36f), 0.05f), Sk.Hex("#C81E1E"), 0.035f);
                    break;
                case 5: // top hat
                    canvas.Shape(p => SkGfx.SdBox(p, new Vector2(0f, top - 0.01f), new Vector2(0.42f, 0.05f), 0.04f), Sk.Hex("#2A2733"), 0.045f);
                    canvas.Shape(p => SkGfx.SdBox(p, new Vector2(0f, top + 0.24f), new Vector2(0.26f, 0.24f), 0.04f), Sk.Hex("#2A2733"), 0.045f);
                    canvas.Shape(p => SkGfx.SdBox(p, new Vector2(0f, top + 0.09f), new Vector2(0.26f, 0.05f), 0.01f), Sk.Hex("#FF3B3B"), 0f);
                    break;
                case 6: // headphones (band drawn behind the body)
                    Headphones(canvas, true);
                    break;
                case 7: // halo
                    canvas.Shape(p => Mathf.Abs(Ellipse(p, new Vector2(0f, top + 0.24f), new Vector2(0.3f, 0.09f))) - 0.035f, Sk.Hex("#FFD23F"), 0.035f);
                    break;
            }
        }
    }

    // An avatar standing in a rect, optionally on a little shadow.
    public sealed class AvatarView
    {
        public RectTransform Root;
        public RawImage Image;
        public Image Shadow;
        string key;

        public static AvatarView Create(Transform parent, bool shadow = false, string name = "Avatar")
        {
            var view = new AvatarView { Root = Ui.Node(name, parent) };
            if (shadow)
            {
                view.Shadow = Ui.Icon(view.Root, SkGfx.Disc, Sk.Alpha(Sk.Ink, 0.25f), "Shadow");
                view.Shadow.preserveAspect = false;
            }
            view.Image = Ui.Node("Image", view.Root).gameObject.AddComponent<RawImage>();
            view.Image.raycastTarget = false;
            return view;
        }

        public void Set(AvatarData avatar)
        {
            var a = SkAvatar.Clamp(avatar);
            if (a.Key == key) return;
            key = a.Key;
            Image.texture = SkAvatar.Texture(a);
        }

        public void Place(float x, float y, float size)
        {
            Ui.Place(Root, x, y, size, size);
            Ui.Place(Image.rectTransform, 0f, 0f, size, size);
            if (Shadow != null) Ui.Place(Shadow, size * 0.16f, size * 0.86f, size * 0.68f, size * 0.12f);
        }

        public void SetAlpha(float a) => Image.color = new Color(1f, 1f, 1f, a);
    }

    // The home screen's avatar maker: arrows either side of the avatar step through hats, eyes,
    // mouths and colors, and the dice picks a random combination. Choices are remembered.
    public sealed class AvatarEditor
    {
        static readonly int[] Counts = { SkAvatar.Hats, SkAvatar.Eyes, SkAvatar.Mouths, SkAvatar.Colors };
        public const float Height = 148f;
        public RectTransform Root;
        public AvatarData Value;
        AvatarView view;
        readonly RectTransform[] left = new RectTransform[4];
        readonly RectTransform[] right = new RectTransform[4];
        Keycap dice;

        public static AvatarEditor Create(Transform parent)
        {
            var editor = new AvatarEditor { Root = Ui.Node("AvatarEditor", parent), Value = SkAvatar.Load() };
            editor.view = AvatarView.Create(editor.Root, true);
            editor.view.Set(editor.Value);
            for (var i = 0; i < 4; i++)
            {
                var part = i;
                editor.left[i] = Arrow(editor.Root, SkGfx.Left, () => editor.Step(part, -1));
                editor.right[i] = Arrow(editor.Root, SkGfx.Right, () => editor.Step(part, 1));
            }
            editor.dice = Keycap.Create(editor.Root, "", Sk.Display, 12, Sk.White, Sk.Ink, 4f, 10f);
            editor.dice.SetGlyph(SkGfx.Dice, Sk.Ink, 24f);
            editor.dice.OnClick = editor.Shuffle;
            return editor;
        }

        static RectTransform Arrow(Transform parent, Sprite sprite, Action onTap)
        {
            var hit = Ui.Node("Arrow", parent);
            var icon = Ui.Icon(hit, sprite, Sk.Ink);
            Ui.Place(icon, 11f, 6f, 22f, 22f);
            Tap.On(hit, onTap);
            return hit;
        }

        int Get(int part) => part == 0 ? Value.h : part == 1 ? Value.e : part == 2 ? Value.m : Value.c;

        void Step(int part, int delta)
        {
            var next = SkAvatar.Wrap(Get(part) + delta, Counts[part]);
            Value = new AvatarData
            {
                h = part == 0 ? next : Value.h, e = part == 1 ? next : Value.e,
                m = part == 2 ? next : Value.m, c = part == 3 ? next : Value.c
            };
            Changed();
        }

        void Shuffle()
        {
            Value = SkAvatar.Random();
            Changed();
        }

        void Changed()
        {
            SkAvatar.Save(Value);
            view.Set(Value);
            var rt = view.Image.rectTransform;
            Tween.Run(rt, 0.25f, t => { var k = Mathf.LerpUnclamped(1.12f, 1f, Tween.OutBack(t)); rt.localScale = new Vector3(k, k, 1f); });
        }

        public void Layout(float x, float y, float width)
        {
            Ui.Place(Root, x, y, width, Height);
            const float size = 136f;
            var ax = (width - size) / 2f;
            view.Place(ax, 4f, size);
            view.Image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            view.Image.rectTransform.anchoredPosition = new Vector2(size / 2f, -size / 2f);
            for (var i = 0; i < 4; i++)
            {
                var rowY = 6f + i * 34f;
                Ui.Place(left[i], ax - 50f, rowY, 44f, 34f);
                Ui.Place(right[i], ax + size + 6f, rowY, 44f, 34f);
            }
            dice.SetSize(44f, 40f);
            Ui.Place(dice.Root, width - 44f, 0f, 44f, 44f);
        }
    }
}
