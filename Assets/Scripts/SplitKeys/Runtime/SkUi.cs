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

    // A pressable 3D keycap: a darker edge under a face that sinks when pressed.
    public sealed class Keycap : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        public RectTransform Root;
        public RectTransform Body;
        public Image Shadow;
        public Image EdgeImage;
        public Image Face;
        public Image Shine;
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
        float shineAlpha = 0.35f;
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
            cap.Radius = radius;
            if (shadow)
            {
                cap.Shadow = Ui.Box(root, new Color(0, 0, 0, 0.35f), 0f, "Shadow");
                cap.Shadow.sprite = SkGfx.Soft;
                cap.Shadow.type = Image.Type.Sliced;
                cap.Shadow.pixelsPerUnitMultiplier = 2.4f;
            }
            cap.EdgeImage = Ui.Box(root, Sk.Edge(face), radius, "Edge");
            cap.Body = Ui.Node("Body", root);
            cap.Face = Ui.Box(cap.Body, face, radius, "Face");
            Ui.Stretch(cap.Face.rectTransform);
            cap.Shine = Ui.Box(cap.Body, new Color(1, 1, 1, 0.35f), 1f, "Shine");
            cap.Label = Ui.Label(cap.Body, label, font, fontSize, legend, TextAnchor.MiddleCenter);
            cap.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Stretch(cap.Label.rectTransform);
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

        public void SetColors(Color face, Color legend, float shine = 0.35f)
        {
            faceColor = face;
            legendColor = legend;
            shineAlpha = shine;
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
                Ring = Ui.Box(Root, color, Radius + width, "Ring");
                Ring.transform.SetAsFirstSibling();
            }
            Ring.color = color;
            Ui.Place(Ring.rectTransform, -width, -width, Width + width * 2f, Height + Depth + width * 2f);
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
            var pressed = down || off;
            var face = off ? Sk.Mat2 : faceColor;
            Face.color = face;
            EdgeImage.color = off ? Sk.Edge(Sk.Mat2) : Sk.Edge(faceColor);
            Label.color = off ? Sk.Dim : legendColor;
            if (Glyph != null) Glyph.color = off ? Sk.Dim : legendColor;
            Shine.gameObject.SetActive(!off && shineAlpha > 0f && Height > 20f);
            Shine.color = new Color(1, 1, 1, shineAlpha);
            Ui.Place(EdgeImage.rectTransform, 0f, Depth, Width, Height);
            Ui.Place(Body, 0f, pressed ? Mathf.Max(0f, Depth - 1f) : 0f, Width, Height);
            Ui.Place(Shine.rectTransform, Mathf.Min(Radius, 10f), 2f, Mathf.Max(0f, Width - Mathf.Min(Radius, 10f) * 2f), 2f);
            if (Shadow != null)
            {
                Shadow.gameObject.SetActive(!pressed);
                Ui.Place(Shadow.rectTransform, -10f, Depth - 2f, Width + 20f, Height + 16f);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactive || off) return;
            SetDown(true);
            OnDown?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData) { if (down) SetDown(false); }
        public void OnPointerExit(PointerEventData eventData) { if (down) SetDown(false); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactive || off) return;
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
