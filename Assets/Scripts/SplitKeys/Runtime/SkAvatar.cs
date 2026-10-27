using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // Players' capsule self-portraits. Everyone draws on a capsule body outlined on a see-through
    // square, inside and outside it. Each player's drawing is a StrokeSheet that follows their strokes live.
    public static class Avatars
    {
        public const int Size = 256;
        // The capsule body as fractions of the square from the top-left; botFace() in server/game.js draws to match.
        public const float Left = 0.25f, Right = 0.75f, Top = 0.24f, Bottom = 0.93f;
        const float Outline = 7f;
        const string PrefsKey = "splitkeys.avatar.drawing";
        static readonly Dictionary<string, StrokeSheet> sheets = new Dictionary<string, StrokeSheet>();

        public static StrokeSheet Sheet(string playerId)
        {
            playerId = playerId ?? "";
            if (!sheets.TryGetValue(playerId, out var sheet))
            {
                sheet = new StrokeSheet(Size, Size, new Color32(0, 0, 0, 0), PaintCapsule) { ContainFills = true };
                sheets[playerId] = sheet;
            }
            return sheet;
        }

        public static void Apply(DrawOp op)
        {
            if (!string.IsNullOrEmpty(op?.playerId)) Sheet(op.playerId).Apply(op);
        }

        public static void Set(AvatarSync avatar)
        {
            if (!string.IsNullOrEmpty(avatar?.playerId)) Sheet(avatar.playerId).SetStrokes(avatar.strokes);
        }

        // Drops every drawing when leaving a room; your own stays saved on the device.
        public static void Forget()
        {
            foreach (var sheet in sheets.Values) UnityEngine.Object.Destroy(sheet.Texture);
            sheets.Clear();
        }

        public static void Flush()
        {
            foreach (var sheet in sheets.Values) sheet.Flush();
        }

        public static string Json(List<Stroke> strokes) => JsonUtility.ToJson(new AvatarSync { playerId = "", strokes = strokes.ToArray() });

        public static void SaveLocal(List<Stroke> strokes)
        {
            PlayerPrefs.SetString(PrefsKey, Json(strokes));
            PlayerPrefs.Save();
        }

        static Stroke[] LoadLocal()
        {
            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<AvatarSync>(json)?.strokes; }
            catch (Exception) { return null; }
        }

        // After joining a room: put your saved drawing on your capsule and share it with everyone.
        public static void Upload(string selfId)
        {
            var saved = LoadLocal();
            if (saved == null || saved.Length == 0) return;
            var sheet = Sheet(selfId);
            sheet.SetStrokes(saved);
            SkBridge.AvatarSet(Json(sheet.Strokes));
        }

        // The body: an ink outline around a white inside, hard-edged so fills stop exactly at the line.
        static void PaintCapsule(Color32[] px, int w, int h)
        {
            Color32 ink = Sk.Ink;
            Color32 white = Color.white;
            var center = new Vector2((Left + Right) / 2f * w, (Top + Bottom) / 2f * h);
            var half = new Vector2((Right - Left) / 2f * w, (Bottom - Top) / 2f * h);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                // Texture rows run bottom-up; the capsule is laid out top-down.
                var d = SkGfx.SdBox(new Vector2(x + 0.5f, h - y - 0.5f), center, half, half.x);
                if (d <= -Outline) px[y * w + x] = white;
                else if (d <= 0f) px[y * w + x] = ink;
            }
        }
    }

    // A player's capsule wherever it appears, optionally standing on a little shadow.
    public sealed class AvatarView
    {
        public RectTransform Root;
        public RawImage Image;
        public Image Shadow;

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

        public void Set(string playerId) => Image.texture = Avatars.Sheet(playerId).Texture;

        public void Place(float x, float y, float size)
        {
            Ui.Place(Root, x, y, size, size);
            Ui.Place(Image.rectTransform, 0f, 0f, size, size);
            if (Shadow != null) Ui.Place(Shadow, size * 0.2f, size * 0.88f, size * 0.6f, size * 0.1f);
        }
    }

    // Reports where a pointer presses or drags inside a rect, as 0..1 from the bottom-left.
    public sealed class PointerPad : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<Vector2> OnPoint;

        public static PointerPad On(RectTransform rt, Action<Vector2> onPoint)
        {
            Ui.Hit(rt);
            var pad = rt.gameObject.AddComponent<PointerPad>();
            pad.OnPoint = onPoint;
            return pad;
        }

        public void OnPointerDown(PointerEventData data) => Report(data);
        public void OnDrag(PointerEventData data) => Report(data);

        void Report(PointerEventData data)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, data.position, data.pressEventCamera, out var local)) return;
            var rect = rt.rect;
            OnPoint?.Invoke(new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height));
        }
    }

    // Any color: a hue/saturation wheel plus a brightness bar, with a preview and DONE.
    public sealed class ColorWheel
    {
        static Texture2D wheelTexture;
        public RectTransform Root;
        public Action<string> OnPick;
        Card card;
        Text title;
        RawImage disc;
        Image discMarker;
        Image barEdge;
        RawImage bar;
        Texture2D barTexture;
        Image barMarker;
        Framed preview;
        Keycap done;
        float h, s = 1f, v = 1f;
        float discX, discY, discSize, barX, barY, barW;

        public static ColorWheel Create(Transform parent)
        {
            var wheel = new ColorWheel { Root = Ui.Node("ColorWheel", parent) };
            wheel.card = Card.Create(wheel.Root, Sk.White, 18f);
            wheel.title = Ui.Label(wheel.Root, "PICK ANY COLOR", Sk.Display, 20, Sk.Ink, TextAnchor.MiddleLeft);
            wheel.disc = Ui.Node("Disc", wheel.Root).gameObject.AddComponent<RawImage>();
            wheel.disc.texture = WheelTexture();
            PointerPad.On(wheel.disc.rectTransform, wheel.PickHueSat);
            wheel.discMarker = Marker(wheel.Root, "DiscMarker");
            wheel.barTexture = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            wheel.barEdge = Ui.Box(wheel.Root, Sk.Ink, 10f, "BarEdge");
            wheel.bar = Ui.Node("Bar", wheel.Root).gameObject.AddComponent<RawImage>();
            wheel.bar.texture = wheel.barTexture;
            PointerPad.On(wheel.bar.rectTransform, p => { wheel.v = Mathf.Clamp01(p.x); wheel.Changed(); });
            wheel.barMarker = Marker(wheel.Root, "BarMarker");
            wheel.preview = Ui.Frame(wheel.Root, Sk.Ink, Color.white, 12f, 2.5f, "Preview");
            wheel.done = Keycap.Create(wheel.Root, "DONE", Sk.Display, 20, Sk.Cream, Sk.Ink, 5f, 10f);
            wheel.done.OnClick = wheel.Hide;
            wheel.Root.gameObject.SetActive(false);
            return wheel;
        }

        static Image Marker(Transform parent, string name)
        {
            var marker = Ui.Icon(parent, SkGfx.Disc, Color.white, name);
            var line = marker.gameObject.AddComponent<Outline>();
            line.effectColor = Sk.Ink;
            line.effectDistance = new Vector2(2.5f, -2.5f);
            return marker;
        }

        public void Show(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            Color.RGBToHSV(color, out h, out s, out v);
            Root.gameObject.SetActive(true);
            Refresh();
        }

        public void Hide() => Root.gameObject.SetActive(false);

        void PickHueSat(Vector2 p)
        {
            var d = (p - new Vector2(0.5f, 0.5f)) * 2f;
            s = Mathf.Clamp01(d.magnitude);
            h = Mathf.Repeat(Mathf.Atan2(d.y, d.x) / (Mathf.PI * 2f), 1f);
            // Picking a hue while the color is near black would show no change, so bring the brightness up.
            if (v < 0.2f) v = 1f;
            Changed();
        }

        void Changed()
        {
            Refresh();
            OnPick?.Invoke("#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(h, s, v)));
        }

        void Refresh()
        {
            for (var i = 0; i < barTexture.width; i++) barTexture.SetPixel(i, 0, Color.HSVToRGB(h, s, i / (barTexture.width - 1f)));
            barTexture.Apply(false);
            preview.Inner.color = Color.HSVToRGB(h, s, v);
            var angle = h * Mathf.PI * 2f;
            var r = s * discSize / 2f;
            Ui.Place(discMarker, discX + discSize / 2f + Mathf.Cos(angle) * r - 9f, discY + discSize / 2f - Mathf.Sin(angle) * r - 9f, 18f, 18f);
            Ui.Place(barMarker, barX + v * barW - 9f, barY + 3f, 18f, 18f);
        }

        // Lays the picker over a rect (the drawing pad and its tools).
        public void Layout(float x, float y, float width, float height)
        {
            Ui.Place(Root, x, y, width, height);
            card.Place(0f, 0f, width, height);
            const float pad = 14f;
            Ui.Place(title, pad, pad, width - pad * 2f, 26f);
            discSize = Mathf.Max(80f, Mathf.Min(width - pad * 2f, height - pad * 2f - 26f - 10f - 14f - 24f - 16f - 51f));
            discX = (width - discSize) / 2f;
            discY = pad + 26f + 10f;
            Ui.Place(disc.rectTransform, discX, discY, discSize, discSize);
            barX = pad;
            barY = discY + discSize + 14f;
            barW = width - pad * 2f;
            Ui.Place(barEdge, barX - 2.5f, barY - 2.5f, barW + 5f, 29f);
            Ui.Place(bar.rectTransform, barX, barY, barW, 24f);
            var rowY = barY + 24f + 16f;
            Ui.Place(preview.Root, pad, rowY, 56f, 46f);
            done.SetSize(width - pad * 3f - 56f, 46f);
            Ui.Place(done.Root, pad * 2f + 56f, rowY, width - pad * 3f - 56f, 51f);
            Refresh();
        }

        static Texture WheelTexture()
        {
            if (wheelTexture != null) return wheelTexture;
            const int size = 192;
            wheelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            var half = size / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x + 0.5f - half, y + 0.5f - half) / half;
                var r = d.magnitude;
                var color = Color.HSVToRGB(Mathf.Repeat(Mathf.Atan2(d.y, d.x) / (Mathf.PI * 2f), 1f), Mathf.Clamp01(r), 1f);
                color.a = Mathf.Clamp01((1f - r) * half);
                pixels[y * size + x] = color;
            }
            wheelTexture.SetPixels(pixels);
            wheelTexture.Apply(false, true);
            return wheelTexture;
        }
    }

    // The lobby's "draw yourself" card: your capsule big enough to draw on with a thumb, colors (plus
    // the wheel for any other), pen or fill, two brush sizes, undo and clear. Every stroke goes to the room live.
    public sealed class AvatarPad
    {
        static readonly string[] Palette = { "#17141F", "#FFFFFF", "#8A8F99", "#FF3B3B", "#FF8A1F", "#FFD23F", "#5ED16A", "#2BD4C1", "#2E86FF", "#9B5CFF", "#FF6FB5" };
        const float Thin = 6f, Thick = 14f;
        public RectTransform Root;
        Card card;
        Text title;
        Text hint;
        Framed well;
        PaperCanvas canvas;
        readonly List<Keycap> swatches = new List<Keycap>();
        Keycap custom;
        Keycap pen, fill, thin, thick, undo, clear;
        ColorWheel wheel;
        string color = Palette[0];
        string customColor = "#FFFFFF";
        bool usingCustom;
        float size = Thin;
        string boundId;
        int savedVersion = -1;
        float changedAt = -1f;

        public static AvatarPad Create(Transform parent)
        {
            var pad = new AvatarPad { Root = Ui.Node("AvatarPad", parent) };
            pad.card = Card.Create(pad.Root, Sk.White, 20f);
            pad.title = Ui.Label(pad.Root, "DRAW YOURSELF", Sk.Display, 24, Sk.Ink, TextAnchor.MiddleLeft);
            pad.hint = Ui.Label(pad.Root, "Everyone in the room sees it live.", Sk.Bold, 13, Sk.Muted, TextAnchor.MiddleLeft);
            pad.well = Ui.Frame(pad.Root, Sk.Ink, Sk.Mat2, 14f, 2.5f, "Well");
            pad.canvas = PaperCanvas.Create(pad.well.Inner.transform, "AvatarCanvas", Avatars.Sheet(""));
            Ui.Stretch((RectTransform)pad.canvas.transform, 4f, 4f, 4f, 4f);
            pad.canvas.Send = SkBridge.AvatarDraw;
            pad.canvas.BrushHex = pad.color;
            pad.canvas.BrushSize = Thin;
            // A fill that would leak out of an open shape is refused; the pad gives a little shake instead.
            var wellRt = pad.well.Root;
            pad.canvas.OnRefused = () => Tween.Run(wellRt, 0.3f, t => wellRt.localEulerAngles = new Vector3(0f, 0f, 2f * Tween.Shake(t)), () => wellRt.localEulerAngles = Vector3.zero);
            foreach (var hex in Palette)
            {
                var c = hex;
                var cap = Keycap.Create(pad.Root, "", Sk.Display, 12, Sk.Hex(hex), Sk.Ink, 3f, 8f, false, "Swatch");
                cap.OnDown = () => pad.PickColor(c, false);
                pad.swatches.Add(cap);
            }
            pad.custom = Keycap.Create(pad.Root, "", Sk.Display, 12, Sk.White, Sk.Ink, 3f, 8f, false, "AnyColor");
            pad.custom.SetGlyph(SpectrumRing(), Color.white, 26f);
            pad.custom.OnClick = () => pad.wheel.Show(pad.usingCustom ? pad.color : pad.customColor);
            pad.pen = Tool(pad.Root, "PEN", () => pad.PickMode("pen"));
            pad.fill = Tool(pad.Root, "FILL", () => pad.PickMode("fill"));
            pad.thin = Tool(pad.Root, "THIN", () => pad.PickSize(Thin));
            pad.thick = Tool(pad.Root, "THICK", () => pad.PickSize(Thick));
            pad.undo = Tool(pad.Root, "UNDO", () => pad.canvas.Undo());
            pad.clear = Tool(pad.Root, "CLEAR", () => pad.canvas.ClearAll());
            pad.wheel = ColorWheel.Create(pad.Root);
            pad.wheel.OnPick = hex => pad.PickColor(hex, true);
            return pad;
        }

        static Keycap Tool(Transform parent, string label, Action onDown)
        {
            var cap = Keycap.Create(parent, label, Sk.Display, 14, Sk.White, Sk.Ink, 3f, 9f, false);
            cap.OnDown = onDown;
            return cap;
        }

        // A small rainbow ring that marks the "any color" swatch.
        static Sprite spectrum;
        static Sprite SpectrumRing()
        {
            if (spectrum != null) return spectrum;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f);
                var r = d.magnitude / (size / 2f);
                var color = Color.HSVToRGB(Mathf.Repeat(Mathf.Atan2(d.y, d.x) / (Mathf.PI * 2f), 1f), 1f, 1f);
                color.a = Mathf.Clamp01((0.32f - Mathf.Abs(r - 0.62f)) * size / 2f);
                pixels[y * size + x] = color;
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            spectrum = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return spectrum;
        }

        void PickColor(string hex, bool fromWheel)
        {
            color = hex;
            usingCustom = fromWheel;
            if (fromWheel) customColor = hex;
            canvas.BrushHex = hex;
            Refresh();
        }

        void PickMode(string mode)
        {
            canvas.Mode = mode;
            Refresh();
        }

        void PickSize(float value)
        {
            size = value;
            canvas.BrushSize = value;
            Refresh();
        }

        void Refresh()
        {
            for (var i = 0; i < swatches.Count; i++) Ring(swatches[i], !usingCustom && Palette[i] == color);
            custom.SetColors(Sk.Hex(customColor), Sk.Ink);
            Ring(custom, usingCustom);
            Ring(pen, canvas.Mode == "pen");
            Ring(fill, canvas.Mode == "fill");
            Ring(thin, Mathf.Approximately(size, Thin));
            Ring(thick, Mathf.Approximately(size, Thick));
        }

        static void Ring(Keycap cap, bool on)
        {
            if (on) cap.AddRing(Sk.Sky, 3f);
            else if (cap.Ring != null) cap.Ring.color = Color.clear;
        }

        // Points the pad at your own capsule; it can only be drawn on while the room is in the lobby.
        public void Bind(string selfId, bool editable)
        {
            if (!string.IsNullOrEmpty(selfId) && selfId != boundId)
            {
                boundId = selfId;
                canvas.Bind(Avatars.Sheet(selfId));
                savedVersion = canvas.Sheet.Version;
            }
            canvas.Editable = editable && boundId != null;
            if (!canvas.Editable) wheel.Hide();
        }

        // Forgets the bound capsule (after leaving a room, when every sheet is dropped).
        public void Unbind()
        {
            boundId = null;
            canvas.Bind(Avatars.Sheet(""));
        }

        // Lays out the card at (x, y) and returns its height.
        public float Layout(float x, float y, float width)
        {
            const float pad = 18f;
            const float gap = 8f;
            var inner = width - pad * 2f;
            Ui.Place(title, pad, 14f, inner, 30f);
            Ui.Place(hint, pad, 42f, inner, 18f);
            var top = 70f;
            var side = Mathf.Min(inner, 260f);
            Ui.Place(well.Root, (width - side) / 2f, top, side, side);
            var padTop = top;
            top += side + 16f;
            var sw = (inner - 5f * gap) / 6f;
            for (var i = 0; i < 12; i++)
            {
                var cap = i < swatches.Count ? swatches[i] : custom;
                cap.SetSize(sw, 32f);
                Ui.Place(cap.Root, pad + (i % 6) * (sw + gap), top + (i / 6) * (35f + gap), sw, 35f);
            }
            top += 2f * 35f + gap + 14f;
            var tools = new[] { pen, fill, thin, thick, undo, clear };
            var tw = (inner - 5f * 6f) / 6f;
            for (var i = 0; i < tools.Length; i++)
            {
                tools[i].SetSize(tw, 36f);
                Ui.Place(tools[i].Root, pad + i * (tw + 6f), top, tw, 39f);
            }
            top += 39f + 18f;
            Refresh();
            Ui.Place(Root, x, y, width, top);
            card.Place(0f, 0f, width, top);
            wheel.Layout(10f, padTop - 6f, width - 20f, top - padTop - 8f);
            return top;
        }

        // Saves your drawing on the device a moment after you stop changing it.
        public void Tick()
        {
            var sheet = canvas.Sheet;
            if (sheet == null || boundId == null) return;
            if (sheet.Version == savedVersion) { changedAt = -1f; return; }
            if (changedAt < 0f) changedAt = Time.unscaledTime;
            if (Time.unscaledTime - changedAt < 0.6f) return;
            Avatars.SaveLocal(sheet.Strokes);
            savedVersion = sheet.Version;
            changedAt = -1f;
        }
    }
}
