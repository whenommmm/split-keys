using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // Small helpers for building uGUI by hand. Every rect is anchored to its parent's top-left,
    // and positions are given as (x right, y down) in canvas units.
    public static class Ui
    {
        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Place(Component c, float x, float y, float w, float h) => Place((RectTransform)c.transform, x, y, w, h);

        public static void Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static Image Box(Transform parent, Color color, float radius, string name = "Box")
        {
            var rt = Node(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            SetRadius(image, radius);
            return image;
        }

        public static void SetRadius(Image image, float radius)
        {
            if (radius <= 0.01f)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                return;
            }
            image.sprite = SkGfx.Round;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = SkGfx.RoundRadius / radius;
        }

        public static Image Icon(Transform parent, Sprite sprite, Color color, string name = "Icon")
        {
            var rt = Node(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string value, Font font, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, string name = "Label")
        {
            var rt = Node(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.text = value ?? "";
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static float Width(Text text)
        {
            var settings = text.GetGenerationSettings(Vector2.zero);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text.cachedTextGeneratorForLayout.GetPreferredWidth(text.text, settings) / Mathf.Max(0.01f, text.pixelsPerUnit);
        }

        public static float Height(Text text, float width)
        {
            var settings = text.GetGenerationSettings(new Vector2(width, 0f));
            return text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text, settings) / Mathf.Max(0.01f, text.pixelsPerUnit);
        }

        // A rounded box with a border: an outer rect in the border color and an inner fill.
        public static Framed Frame(Transform parent, Color border, Color fill, float radius, float borderWidth, string name = "Frame")
        {
            var root = Node(name, parent);
            var outer = Box(root, border, radius, "Border");
            Stretch(outer.rectTransform);
            var inner = Box(root, fill, Mathf.Max(0f, radius - borderWidth), "Fill");
            Stretch(inner.rectTransform, borderWidth, borderWidth, borderWidth, borderWidth);
            return new Framed { Root = root, Outer = outer, Inner = inner, BorderWidth = borderWidth, Radius = radius };
        }

        // Makes the rect receive pointer events. A label already is a graphic, so it just turns raycasts on.
        public static Graphic Hit(RectTransform rt)
        {
            if (rt.TryGetComponent<Graphic>(out var graphic))
            {
                graphic.raycastTarget = true;
                return graphic;
            }
            var image = rt.gameObject.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0);
            image.raycastTarget = true;
            return image;
        }

        public static void Clear(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string Plural(int n, string one, string many) => n + " " + (n == 1 ? one : many);
    }

    // A white card with a thick ink outline and a hard ink drop below it.
    public sealed class Card
    {
        public RectTransform Root;
        public Image Drop;
        public Image Border;
        public Image Fill;
        public const float Edge = 3f;
        public const float DropDepth = 5f;

        public static Card Create(Transform parent, Color fill, float radius = 18f, string name = "Card")
        {
            var card = new Card { Root = Ui.Node(name, parent) };
            card.Drop = Ui.Box(card.Root, Sk.Ink, radius + Edge, "Drop");
            card.Border = Ui.Box(card.Root, Sk.Ink, radius + Edge, "Border");
            card.Fill = Ui.Box(card.Root, fill, radius, "Fill");
            return card;
        }

        public void Place(float x, float y, float w, float h)
        {
            Ui.Place(Root, x, y, w, h);
            Ui.Place(Drop, -Edge, -Edge + DropDepth, w + Edge * 2f, h + Edge * 2f);
            Ui.Place(Border, -Edge, -Edge, w + Edge * 2f, h + Edge * 2f);
            Ui.Place(Fill, 0f, 0f, w, h);
        }
    }

    public sealed class Framed
    {
        public RectTransform Root;
        public Image Outer;
        public Image Inner;
        public float BorderWidth;
        public float Radius;

        public void SetBorder(float width)
        {
            BorderWidth = width;
            Ui.Stretch(Inner.rectTransform, width, width, width, width);
            Ui.SetRadius(Inner, Mathf.Max(0f, Radius - width));
        }

        public void SetColors(Color border, Color fill)
        {
            Outer.color = border;
            Inner.color = fill;
        }
    }

    // A chunky button: a flat face inside a thick ink outline, sitting on an ink lip it sinks into when pressed.
    public sealed class Keycap : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        public const float Border = 2.5f;
        public RectTransform Root;
        public RectTransform Body;
        public Image EdgeImage;
        public Image Face;
        public Image Ring;
        public Text Label;
        public Image Glyph;
        public Action OnDown;
        public Action OnClick;
        public bool Interactive = true;
        public float Depth;
        public float Radius;
        public float Width;
        public float Height;

        Color faceColor;
        Color legendColor;
        Outline labelOutline;
        bool off;
        bool down;

        public bool IsOff => off;

        public static Keycap Create(Transform parent, string label, Font font, int fontSize, Color face, Color legend, float depth, float radius, bool shadow = true, string name = "Cap")
        {
            var root = Ui.Node(name, parent);
            Ui.Hit(root);
            var cap = root.gameObject.AddComponent<Keycap>();
            cap.Root = root;
            cap.Depth = depth;
            cap.Radius = Mathf.Min(radius, 12f);
            cap.EdgeImage = Ui.Box(root, Sk.Ink, cap.Radius + Border, "Edge");
            cap.Body = Ui.Node("Body", root);
            cap.Face = Ui.Box(cap.Body, face, cap.Radius, "Face");
            Ui.Stretch(cap.Face.rectTransform);
            cap.Label = Ui.Label(cap.Body, label, font, fontSize, legend, TextAnchor.MiddleCenter);
            cap.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Stretch(cap.Label.rectTransform);
            cap.labelOutline = cap.Label.gameObject.AddComponent<Outline>();
            cap.labelOutline.effectColor = Sk.Ink;
            cap.labelOutline.effectDistance = new Vector2(1.5f, -1.5f);
            cap.faceColor = face;
            cap.legendColor = legend;
            return cap;
        }

        public void SetSize(float w, float h)
        {
            Width = w;
            Height = h;
            Root.sizeDelta = new Vector2(w, h + Depth);
            Apply();
        }

        // `shine` is kept for callers from the old keycap look; the flat buttons ignore it.
        public void SetColors(Color face, Color legend, float shine = 0f)
        {
            faceColor = face;
            legendColor = legend;
            Apply();
        }

        public void SetLabel(string value) => Label.text = value;

        public void SetOff(bool value)
        {
            off = value;
            Apply();
        }

        public void SetDown(bool value)
        {
            down = value;
            Apply();
        }

        public Image AddRing(Color color, float width)
        {
            if (Ring == null)
            {
                Ring = Ui.Box(Root, color, Radius + Border + width, "Ring");
                Ring.transform.SetAsFirstSibling();
            }
            Ring.color = color;
            Ui.Place(Ring.rectTransform, -Border - width, -Border - width, Width + (Border + width) * 2f, Height + Depth + (Border + width) * 2f);
            return Ring;
        }

        public Image SetGlyph(Sprite sprite, Color color, float size)
        {
            if (Glyph == null) Glyph = Ui.Icon(Body, sprite, color, "Glyph");
            Glyph.sprite = sprite;
            Glyph.color = color;
            Glyph.rectTransform.anchorMin = Glyph.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            Glyph.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Glyph.rectTransform.anchoredPosition = Vector2.zero;
            Glyph.rectTransform.sizeDelta = new Vector2(size, size);
            return Glyph;
        }

        void Apply()
        {
            var pressed = down;
            var face = off ? Sk.Mat2 : faceColor;
            var legend = off ? Sk.Dim : legendColor;
            Face.color = face;
            EdgeImage.color = off ? Sk.Dim : Sk.Ink;
            Label.color = legend;
            // Light legends on colored faces get an ink outline so they read like game text.
            labelOutline.enabled = !off && Sk.IsLight(legend) && !Sk.IsLight(face);
            if (Glyph != null) Glyph.color = legend;
            var sink = pressed ? Depth : 0f;
            Ui.Place(EdgeImage.rectTransform, -Border, sink - Border, Width + Border * 2f, Height + Depth - sink + Border * 2f);
            Ui.Place(Body, 0f, sink, Width, Height);
        }

        bool pressFired;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactive || off) return;
            SetDown(true);
            pressFired = true;
            OnDown?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData) { if (down) SetDown(false); }
        public void OnPointerExit(PointerEventData eventData) { if (down) SetDown(false); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactive || off) return;
            // A very quick tap can arrive as a click without its press; still count it.
            if (!pressFired) OnDown?.Invoke();
            pressFired = false;
            OnClick?.Invoke();
        }
    }

    // A flat tap target (text links, rows) that reports clicks.
    public sealed class Tap : MonoBehaviour, IPointerClickHandler
    {
        public Action OnClick;
        public void OnPointerClick(PointerEventData eventData) => OnClick?.Invoke();

        public static Tap On(RectTransform rt, Action action)
        {
            Ui.Hit(rt);
            if (!rt.TryGetComponent<Tap>(out var tap)) tap = rt.gameObject.AddComponent<Tap>();
            tap.OnClick = action;
            return tap;
        }
    }

    // Minimal tween runner. Tweens with the same key replace each other.
    public sealed class Tween : MonoBehaviour
    {
        sealed class Item
        {
            public object Key;
            public float Start;
            public float Duration;
            public Action<float> Step;
            public Action Done;
        }

        static Tween host;
        readonly List<Item> items = new List<Item>();

        public static void Init(GameObject owner)
        {
            if (host == null) host = owner.AddComponent<Tween>();
        }

        public static void Run(object key, float duration, Action<float> step, Action done = null, float delay = 0f)
        {
            if (host == null) return;
            if (key != null) host.items.RemoveAll(item => Equals(item.Key, key));
            var entry = new Item { Key = key, Start = Time.unscaledTime + delay, Duration = Mathf.Max(0.0001f, duration), Step = step, Done = done };
            host.items.Add(entry);
            if (delay <= 0f) step(0f);
        }

        public static void Cancel(object key)
        {
            if (host != null) host.items.RemoveAll(item => Equals(item.Key, key));
        }

        void Update()
        {
            var now = Time.unscaledTime;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (i >= items.Count) continue;
                var item = items[i];
                if (now < item.Start) continue;
                var t = Mathf.Clamp01((now - item.Start) / item.Duration);
                try { item.Step(t); }
                catch (Exception) { items.Remove(item); continue; }
                if (t >= 1f)
                {
                    items.Remove(item);
                    try { item.Done?.Invoke(); } catch (Exception) { }
                }
            }
        }

        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        public static float OutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        // Left-right shake that settles back to zero.
        public static float Shake(float t)
        {
            return Mathf.Sin(t * Mathf.PI * 6f) * (1f - t);
        }
    }
}
