using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // A drawing kept as strokes plus the pixels they paint. Strokes use 0..1 coordinates (y down) and
    // brush sizes in sheet pixels, and every copy of a sheet renders at the same size, so bucket fills
    // come out identical on every device.
    public sealed class StrokeSheet
    {
        public readonly int Width;
        public readonly int Height;
        public readonly List<Stroke> Strokes = new List<Stroke>();
        public readonly Texture2D Texture;
        // Refuse fills that would reach the edge, so a sheet's see-through outside stays see-through.
        public bool ContainFills;
        public int Version;

        readonly Color32 background;
        readonly Action<Color32[], int, int> paintBase;
        readonly Color32[] pixels;
        readonly Stack<int> floodStack = new Stack<int>();
        readonly List<int> filled = new List<int>();
        bool dirty;

        public StrokeSheet(int width, int height, Color32 background, Action<Color32[], int, int> paintBase = null)
        {
            Width = width;
            Height = height;
            this.background = background;
            this.paintBase = paintBase;
            pixels = new Color32[width * height];
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Redraw();
        }

        public void Redraw()
        {
            for (var i = 0; i < pixels.Length; i++) pixels[i] = background;
            paintBase?.Invoke(pixels, Width, Height);
            foreach (var stroke in Strokes) Paint(stroke, 0);
            dirty = true;
            Version++;
        }

        public void SetStrokes(Stroke[] strokes)
        {
            Strokes.Clear();
            if (strokes != null) Strokes.AddRange(strokes);
            Redraw();
        }

        // Applies a stroke/more/fill/undo/clear message.
        public void Apply(DrawOp op)
        {
            switch (op.op)
            {
                case "stroke":
                case "fill":
                    Add(new Stroke { t = op.op == "fill" ? "fill" : op.t, c = op.c, s = op.s, p = op.p ?? new float[0] });
                    break;
                case "more":
                    if (Strokes.Count == 0 || op.p == null) return;
                    var target = Strokes[Strokes.Count - 1];
                    if (target.t == "fill") return;
                    var from = target.p.Length;
                    var merged = new float[from + op.p.Length];
                    Array.Copy(target.p, merged, from);
                    Array.Copy(op.p, 0, merged, from, op.p.Length);
                    target.p = merged;
                    Extend(target, from);
                    break;
                case "undo":
                    Undo();
                    break;
                case "clear":
                    Strokes.Clear();
                    Redraw();
                    break;
            }
        }

        // Adds a stroke and paints it. Returns false (and keeps nothing) for a fill the sheet refuses.
        public bool Add(Stroke stroke)
        {
            if (!Paint(stroke, 0)) return false;
            Strokes.Add(stroke);
            Version++;
            return true;
        }

        // Paints the points appended to a stroke from index `from` on.
        public void Extend(Stroke stroke, int from)
        {
            Paint(stroke, Mathf.Max(0, from - 2));
            Version++;
        }

        public void Undo()
        {
            if (Strokes.Count == 0) return;
            Strokes.RemoveAt(Strokes.Count - 1);
            Redraw();
        }

        public void Flush()
        {
            if (!dirty) return;
            Texture.SetPixels32(pixels);
            Texture.Apply(false);
            dirty = false;
        }

        bool Paint(Stroke stroke, int fromIndex)
        {
            if (stroke.p == null || stroke.p.Length < 2) return false;
            ColorUtility.TryParseHtmlString(stroke.c, out var color);
            var c32 = (Color32)color;
            if (stroke.t == "fill") return Flood(ToPixel(stroke.p[0], stroke.p[1]), c32);
            var radius = Mathf.Max(0.8f, stroke.s * 0.5f);
            var start = fromIndex - fromIndex % 2;
            var prev = ToPixel(stroke.p[start], stroke.p[start + 1]);
            Dot(prev, radius, c32);
            for (var i = start + 2; i + 1 < stroke.p.Length; i += 2)
            {
                var next = ToPixel(stroke.p[i], stroke.p[i + 1]);
                Segment(prev, next, radius, c32);
                prev = next;
            }
            return true;
        }

        Vector2 ToPixel(float x, float y) => new Vector2(x * (Width - 1), (1f - y) * (Height - 1));

        void Segment(Vector2 a, Vector2 b, float radius, Color32 color)
        {
            var distance = Vector2.Distance(a, b);
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(0.75f, radius * 0.5f)));
            for (var i = 1; i <= steps; i++) Dot(Vector2.Lerp(a, b, i / (float)steps), radius, color);
        }

        void Dot(Vector2 center, float radius, Color32 color)
        {
            var r = Mathf.CeilToInt(radius);
            var cx = Mathf.RoundToInt(center.x);
            var cy = Mathf.RoundToInt(center.y);
            var r2 = radius * radius;
            for (var y = Mathf.Max(0, cy - r); y <= Mathf.Min(Height - 1, cy + r); y++)
            {
                var dy = y - cy;
                var row = y * Width;
                for (var x = Mathf.Max(0, cx - r); x <= Mathf.Min(Width - 1, cx + r); x++)
                {
                    var dx = x - cx;
                    if (dx * dx + dy * dy <= r2) pixels[row + x] = color;
                }
            }
            dirty = true;
        }

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        // Scanline flood fill with 4-way connectivity, so it never leaks through diagonal corners.
        bool Flood(Vector2 at, Color32 color)
        {
            var sx = Mathf.Clamp(Mathf.RoundToInt(at.x), 0, Width - 1);
            var sy = Mathf.Clamp(Mathf.RoundToInt(at.y), 0, Height - 1);
            var target = pixels[sy * Width + sx];
            if (Same(target, color)) return false;
            floodStack.Clear();
            filled.Clear();
            var touchedEdge = false;
            floodStack.Push(sy * Width + sx);
            while (floodStack.Count > 0)
            {
                var index = floodStack.Pop();
                if (!Same(pixels[index], target)) continue;
                var py = index / Width;
                var lx = index % Width;
                var rx = lx;
                while (lx > 0 && Same(pixels[py * Width + lx - 1], target)) lx--;
                while (rx < Width - 1 && Same(pixels[py * Width + rx + 1], target)) rx++;
                if (lx == 0 || rx == Width - 1 || py == 0 || py == Height - 1) touchedEdge = true;
                var upOpen = false;
                var downOpen = false;
                for (var x = lx; x <= rx; x++)
                {
                    pixels[py * Width + x] = color;
                    filled.Add(py * Width + x);
                    if (py + 1 < Height)
                    {
                        var up = (py + 1) * Width + x;
                        if (Same(pixels[up], target)) { if (!upOpen) { floodStack.Push(up); upOpen = true; } }
                        else upOpen = false;
                    }
                    if (py > 0)
                    {
                        var down = (py - 1) * Width + x;
                        if (Same(pixels[down], target)) { if (!downOpen) { floodStack.Push(down); downOpen = true; } }
                        else downOpen = false;
                    }
                }
            }
            if (ContainFills && touchedEdge)
            {
                foreach (var index in filled) pixels[index] = target;
                return false;
            }
            dirty = true;
            return true;
        }
    }

    // Pointer input for drawing on a StrokeSheet. The game's paper makes its own 480x360 sheet;
    // the lobby's avatar pad binds to the player's capsule sheet instead.
    public sealed class PaperCanvas : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public const int TexWidth = 480;
        public const int TexHeight = 360;
        public static readonly Color32 Paper = new Color32(0xFF, 0xF8, 0xEA, 0xFF);
        public static readonly string PaperHex = "#FFF8EA";

        public RawImage Image;
        public StrokeSheet Sheet;
        public List<Stroke> Strokes => Sheet.Strokes;
        public bool Editable;
        public string Mode = "pen";
        public string BrushHex = "#15121F";
        public float BrushSize = 6f;
        public Action<string> Send;
        public Action OnRefused;

        bool ownsSheet;
        int pointer = int.MinValue;
        Vector2 last;
        readonly List<float> pending = new List<float>();
        float lastSend;

        public static PaperCanvas Create(Transform parent, string name = "Paper", StrokeSheet sheet = null)
        {
            var rt = Ui.Node(name, parent);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.raycastTarget = true;
            var canvas = rt.gameObject.AddComponent<PaperCanvas>();
            canvas.Image = image;
            canvas.ownsSheet = sheet == null;
            canvas.Bind(sheet ?? new StrokeSheet(TexWidth, TexHeight, Paper));
            return canvas;
        }

        public void Bind(StrokeSheet sheet)
        {
            if (sheet == Sheet) return;
            CancelStroke();
            Sheet = sheet;
            Image.texture = sheet.Texture;
        }

        public void SetStrokes(Stroke[] strokes) => Sheet.SetStrokes(strokes);
        public void Redraw() => Sheet.Redraw();
        public void Apply(DrawOp op) => Sheet.Apply(op);

        public void Undo()
        {
            if (Sheet.Strokes.Count == 0) return;
            Sheet.Undo();
            Send?.Invoke("{\"op\":\"undo\"}");
        }

        public void ClearAll()
        {
            Sheet.SetStrokes(null);
            Send?.Invoke("{\"op\":\"clear\"}");
        }

        bool ToNormalized(PointerEventData data, out Vector2 normalized)
        {
            normalized = Vector2.zero;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, data.position, data.pressEventCamera, out var local)) return false;
            var rect = rt.rect;
            normalized = new Vector2(Mathf.Clamp01((local.x - rect.xMin) / rect.width), Mathf.Clamp01(1f - (local.y - rect.yMin) / rect.height));
            return true;
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (!Editable || pointer != int.MinValue || !ToNormalized(data, out var p)) return;
            if (Mode == "fill")
            {
                var fill = new Stroke { t = "fill", c = BrushHex, s = 0f, p = new[] { Round(p.x), Round(p.y) } };
                if (!Sheet.Add(fill))
                {
                    OnRefused?.Invoke();
                    return;
                }
                Send?.Invoke("{\"op\":\"fill\",\"c\":\"" + BrushHex + "\",\"p\":[" + Num(fill.p[0]) + "," + Num(fill.p[1]) + "]}");
                return;
            }
            pointer = data.pointerId;
            last = p;
            var hex = Mode == "eraser" ? PaperHex : BrushHex;
            var stroke = new Stroke { c = hex, s = BrushSize, p = new[] { Round(p.x), Round(p.y) } };
            Sheet.Add(stroke);
            pending.Clear();
            Send?.Invoke("{\"op\":\"stroke\",\"c\":\"" + hex + "\",\"s\":" + BrushSize.ToString("0.#", CultureInfo.InvariantCulture) + ",\"p\":[" + Num(stroke.p[0]) + "," + Num(stroke.p[1]) + "]}");
            lastSend = Time.unscaledTime;
        }

        public void OnDrag(PointerEventData data)
        {
            if (!Editable || data.pointerId != pointer || Sheet.Strokes.Count == 0 || !ToNormalized(data, out var p)) return;
            if (Vector2.Distance(p, last) < 0.003f) return;
            last = p;
            var stroke = Sheet.Strokes[Sheet.Strokes.Count - 1];
            var from = stroke.p.Length;
            var grown = new float[from + 2];
            Array.Copy(stroke.p, grown, from);
            grown[from] = Round(p.x);
            grown[from + 1] = Round(p.y);
            stroke.p = grown;
            Sheet.Extend(stroke, from);
            pending.Add(grown[from]);
            pending.Add(grown[from + 1]);
            if (pending.Count >= 24 || Time.unscaledTime - lastSend > 0.05f) Flush();
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data.pointerId != pointer) return;
            Flush();
            pointer = int.MinValue;
        }

        public void CancelStroke()
        {
            Flush();
            pointer = int.MinValue;
        }

        void Flush()
        {
            if (pending.Count == 0) return;
            var sb = new StringBuilder("{\"op\":\"more\",\"p\":[");
            for (var i = 0; i < pending.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Num(pending[i]));
            }
            sb.Append("]}");
            pending.Clear();
            lastSend = Time.unscaledTime;
            Send?.Invoke(sb.ToString());
        }

        static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;
        static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        void LateUpdate()
        {
            if (pending.Count > 0 && Time.unscaledTime - lastSend > 0.05f) Flush();
            Sheet?.Flush();
        }

        void OnDestroy()
        {
            if (ownsSheet && Sheet?.Texture != null) Destroy(Sheet.Texture);
        }
    }

    // The sentence as a row of word pills. Guessers see blanks; found words take the color of
    // the team that found them (so stolen words show in the thief's color).
    public sealed class SentenceView
    {
        public RectTransform Root;
        readonly HashSet<int> shownRevealed = new HashSet<int>();
        int roundKey = -1;

        public static readonly HashSet<string> Given = new HashSet<string> { "A", "AN", "THE" };

        public static SentenceView Create(Transform parent)
        {
            return new SentenceView { Root = Ui.Node("Sentence", parent) };
        }

        // Returns the height used.
        public float Render(WordView[] words, Func<string, Color> colorOf, int key, float x, float y, float width, bool animate, int fontSize = 18)
        {
            Ui.Clear(Root);
            Ui.Place(Root, x, y, width, 10f);
            if (words == null) return 0f;
            if (key != roundKey) { roundKey = key; shownRevealed.Clear(); }
            var pillH = fontSize + 18f;
            const float gap = 8f;
            var rowY = 0f;
            var rowX = 0f;
            var row = new List<(RectTransform rt, float w)>();
            var rows = new List<List<(RectTransform rt, float w)>>();
            for (var i = 0; i < words.Length; i++)
            {
                var word = words[i];
                var given = Given.Contains(word.text) && (word.by == "given" || string.IsNullOrEmpty(word.by));
                var blank = word.text.IndexOf('_') >= 0;
                var display = blank ? string.Join(" ", word.text.ToCharArray()) : word.text;
                var pill = Ui.Node("Word", Root);
                // Tiles: white while hidden, the finder's team color once revealed, grey for small given words.
                var revealed = word.revealed && !given;
                var bg = given ? Sk.Mat2 : revealed ? colorOf(word.by) : Sk.White;
                var box = Ui.Frame(pill, Sk.Ink, bg, 10f, 2.5f);
                Ui.Stretch(box.Root, 0f, 0f, 0f, -3f);
                Ui.Stretch(box.Inner.rectTransform, 2.5f, 2.5f, 2.5f, 5.5f);
                var label = Ui.Label(pill, display, Sk.Display, fontSize, revealed ? Sk.White : given ? Sk.Muted : Sk.Ink, TextAnchor.MiddleCenter);
                if (revealed) Sk.Punch(label, 1.5f, 0f);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                Ui.Stretch(label.rectTransform);
                var w = Ui.Width(label) + (given ? 16f : 24f);
                if (rowX > 0f && rowX + w > width)
                {
                    rows.Add(row);
                    row = new List<(RectTransform, float)>();
                    rowX = 0f;
                }
                row.Add((pill, w));
                rowX += w + gap;
                if (word.revealed && !given && animate && !shownRevealed.Contains(i))
                {
                    var target = pill;
                    Tween.Run(target, 0.3f, t => { var k = Mathf.LerpUnclamped(1.35f, 1f, Tween.OutBack(t)); target.localScale = new Vector3(k, k, 1f); });
                }
                if (word.revealed) shownRevealed.Add(i);
            }
            if (row.Count > 0) rows.Add(row);
            foreach (var r in rows)
            {
                var total = -gap;
                foreach (var (_, w) in r) total += w + gap;
                var px = (width - total) / 2f;
                foreach (var (rt, w) in r)
                {
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(w, pillH);
                    rt.anchoredPosition = new Vector2(px + w / 2f, -(rowY + pillH / 2f));
                    px += w + gap;
                }
                rowY += pillH + gap;
            }
            var height = Mathf.Max(0f, rowY - gap);
            Root.sizeDelta = new Vector2(width, height);
            return height;
        }
    }

    // On-screen QWERTY keyboard made of keycaps.
    public sealed class KeyboardView
    {
        static readonly string[] Rows = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
        public RectTransform Root;
        public Action<char> OnLetter;
        public Action OnSpace, OnBack, OnEnter;
        readonly Dictionary<char, Keycap> letters = new Dictionary<char, Keycap>();
        Keycap space, back, enter;

        public static KeyboardView Create(Transform parent)
        {
            var kb = new KeyboardView { Root = Ui.Node("Keyboard", parent) };
            foreach (var row in Rows)
                foreach (var c in row)
                {
                    var letter = c;
                    var key = Keycap.Create(kb.Root, c.ToString(), Sk.Display, 18, Sk.Slate, Sk.Text, 4f, 8f, false, "Key " + c);
                    key.SetColors(Sk.Slate, Sk.Text, 0.12f);
                    key.OnDown = () => kb.OnLetter?.Invoke(letter);
                    kb.letters[c] = key;
                }
            kb.back = Keycap.Create(kb.Root, "DEL", Sk.Display, 14, Sk.Slate, Sk.Text, 4f, 8f, false);
            kb.back.SetColors(Sk.Slate, Sk.Text, 0.12f);
            kb.back.OnDown = () => kb.OnBack?.Invoke();
            kb.space = Keycap.Create(kb.Root, "SPACE", Sk.Display, 14, Sk.Slate, Sk.Text, 4f, 8f, false);
            kb.space.SetColors(Sk.Slate, Sk.Text, 0.12f);
            kb.space.OnDown = () => kb.OnSpace?.Invoke();
            kb.enter = Keycap.Create(kb.Root, "GUESS", Sk.Display, 15, Sk.Cream, Sk.Ink, 4f, 8f, false);
            kb.enter.OnDown = () => kb.OnEnter?.Invoke();
            return kb;
        }

        public void SetAccent(Color color) => enter.SetColors(color, Sk.IsLight(color) ? Sk.Ink : Sk.White);

        public void SetOff(bool off)
        {
            foreach (var key in letters.Values) if (key.IsOff != off) key.SetOff(off);
            foreach (var key in new[] { space, back, enter }) if (key.IsOff != off) key.SetOff(off);
        }

        public void Flash(char c)
        {
            if (!letters.TryGetValue(c, out var key)) return;
            key.SetDown(true);
            Tween.Run(key, 0.09f, _ => { }, () => key.SetDown(false));
        }

        // Lays the keys out at (x, y) in `width`; returns the height used.
        public float Layout(float x, float y, float width, float keyH)
        {
            const float gap = 5f;
            var keyW = (width - 9f * gap) / 10f;
            Ui.Place(Root, x, y, width, 10f);
            var rowY = 0f;
            for (var r = 0; r < Rows.Length; r++)
            {
                var row = Rows[r];
                var rowW = row.Length * keyW + (row.Length - 1) * gap;
                var startX = r == 2 ? keyW * 1.5f + gap : (width - rowW) / 2f;
                if (r == 2)
                {
                    back.SetSize(keyW * 1.5f, keyH);
                    Ui.Place(back.Root, 0f, rowY, keyW * 1.5f, keyH + 4f);
                }
                for (var i = 0; i < row.Length; i++)
                {
                    var key = letters[row[i]];
                    key.SetSize(keyW, keyH);
                    Ui.Place(key.Root, startX + i * (keyW + gap), rowY, keyW, keyH + 4f);
                }
                rowY += keyH + 4f + gap;
            }
            var spaceW = width * 0.55f;
            space.SetSize(spaceW, keyH);
            Ui.Place(space.Root, 0f, rowY, spaceW, keyH + 4f);
            var enterW = width - spaceW - gap;
            enter.SetSize(enterW, keyH);
            Ui.Place(enter.Root, spaceW + gap, rowY, enterW, keyH + 4f);
            rowY += keyH + 4f;
            Root.sizeDelta = new Vector2(width, rowY);
            return rowY;
        }
    }

    // A horizontal slider for the brush size: a track, a filled part and a round cream knob.
    public sealed class SizeSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public const float Min = 2f;
        public const float Max = 40f;
        public RectTransform Root;
        public Action<float> OnChange;
        public float Value = 6f;
        Image track, fill, knob, knobEdge;

        public static SizeSlider Create(Transform parent)
        {
            var rt = Ui.Node("SizeSlider", parent);
            Ui.Hit(rt);
            var slider = rt.gameObject.AddComponent<SizeSlider>();
            slider.Root = rt;
            slider.track = Ui.Box(rt, Sk.Ink, 6f, "Track");
            slider.fill = Ui.Box(rt, Sk.Cream, 4f, "Fill");
            slider.knobEdge = Ui.Box(rt, Sk.Ink, 15.5f, "KnobEdge");
            slider.knob = Ui.Box(rt, Sk.Cream, 13f, "Knob");
            return slider;
        }

        public void Set(float value)
        {
            Value = Mathf.Clamp(Mathf.Round(value), Min, Max);
            Refresh();
        }

        void Refresh()
        {
            var size = Root.sizeDelta;
            var t = (Value - Min) / (Max - Min);
            var usable = size.x - 26f;
            Ui.Place(track, 0f, size.y / 2f - 6f, size.x, 12f);
            Ui.Place(fill, 3f, size.y / 2f - 3f, 10f + usable * t, 6f);
            Ui.Place(knobEdge, usable * t - 2.5f, size.y / 2f - 13f - 2.5f, 31f, 34f);
            Ui.Place(knob, usable * t, size.y / 2f - 13f, 26f, 26f);
        }

        public void Layout(float x, float y, float width, float height)
        {
            Ui.Place(Root, x, y, width, height);
            Refresh();
        }

        void Pick(PointerEventData data)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, data.position, data.pressEventCamera, out var local)) return;
            var rect = Root.rect;
            var t = Mathf.Clamp01((local.x - rect.xMin - 13f) / Mathf.Max(1f, rect.width - 26f));
            var value = Mathf.Round(Mathf.Lerp(Min, Max, t));
            if (Mathf.Approximately(value, Value)) return;
            Value = value;
            Refresh();
            OnChange?.Invoke(Value);
        }

        public void OnPointerDown(PointerEventData data) => Pick(data);
        public void OnDrag(PointerEventData data) => Pick(data);
    }

    // The drawer's toolbox: 16 colors, a brush-size slider with a live preview, pen / fill /
    // eraser modes, undo and clear.
    public sealed class ToolBar
    {
        public static readonly string[] Colors =
        {
            "#15121F", "#868E96", "#FFFFFF", "#FF5252", "#FF922B", "#FFD43B", "#A9E34B", "#69DB7C",
            "#2B8A3E", "#38D9A9", "#4DABF7", "#1C7ED6", "#9775FA", "#F783AC", "#FFC9A8", "#8B5A2B"
        };
        public RectTransform Root;
        public Action<string> OnColor;
        public Action<float> OnSize;
        public Action<string> OnMode;
        public Action OnUndo, OnClear;
        readonly List<Keycap> swatches = new List<Keycap>();
        Keycap pen, fill, eraser, undo, clear;
        SizeSlider slider;
        Framed preview;
        Image previewDot;
        Text sizeLabel;
        string color = Colors[0];
        string mode = "pen";

        public static ToolBar Create(Transform parent)
        {
            var bar = new ToolBar { Root = Ui.Node("Tools", parent) };
            foreach (var hex in Colors)
            {
                var c = hex;
                var cap = Keycap.Create(bar.Root, "", Sk.Display, 12, Sk.Hex(hex), Sk.Ink, 3f, 8f, false, "Swatch");
                cap.OnDown = () => bar.PickColor(c);
                bar.swatches.Add(cap);
            }
            bar.preview = Ui.Frame(bar.Root, Sk.Ink, PaperCanvas.Paper, 10f, 2.5f, "Preview");
            bar.previewDot = Ui.Box(bar.preview.Root, Color.black, 20f, "Dot");
            bar.slider = SizeSlider.Create(bar.Root);
            bar.slider.OnChange = size => { bar.OnSize?.Invoke(size); bar.Refresh(); };
            bar.sizeLabel = Ui.Label(bar.Root, "", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight);
            bar.pen = Slate(bar.Root, "PEN");
            bar.pen.OnDown = () => bar.PickMode("pen");
            bar.fill = Slate(bar.Root, "FILL");
            bar.fill.OnDown = () => bar.PickMode("fill");
            bar.eraser = Slate(bar.Root, "ERASER");
            bar.eraser.OnDown = () => bar.PickMode("eraser");
            bar.undo = Slate(bar.Root, "UNDO");
            bar.undo.OnDown = () => bar.OnUndo?.Invoke();
            bar.clear = Slate(bar.Root, "CLEAR");
            bar.clear.OnDown = () => bar.OnClear?.Invoke();
            return bar;
        }

        static Keycap Slate(Transform parent, string label)
        {
            var cap = Keycap.Create(parent, label, Sk.Display, 12, Sk.Slate, Sk.Text, 3f, 9f, false);
            cap.SetColors(Sk.Slate, Sk.Text, 0.12f);
            return cap;
        }

        void PickColor(string hex)
        {
            color = hex;
            OnColor?.Invoke(hex);
            if (mode == "eraser") PickMode("pen");
            Refresh();
        }

        void PickMode(string value)
        {
            mode = value;
            OnMode?.Invoke(value);
            Refresh();
        }

        public void Reset()
        {
            color = Colors[0];
            mode = "pen";
            slider.Set(6f);
            OnColor?.Invoke(color);
            OnSize?.Invoke(6f);
            OnMode?.Invoke(mode);
            Refresh();
        }

        void Refresh()
        {
            for (var i = 0; i < swatches.Count; i++) Ring(swatches[i], mode != "eraser" && Colors[i] == color);
            Ring(pen, mode == "pen");
            Ring(fill, mode == "fill");
            Ring(eraser, mode == "eraser");
            var paper = Sk.Hex(PaperCanvas.PaperHex);
            previewDot.color = mode == "eraser" ? paper : Sk.Hex(color);
            preview.Outer.color = mode == "eraser" ? Sk.Danger : Sk.Ink;
            var box = preview.Root.sizeDelta.x;
            var dot = mode == "fill" ? box - 12f : Mathf.Lerp(3f, box - 8f, (slider.Value - SizeSlider.Min) / (SizeSlider.Max - SizeSlider.Min));
            previewDot.rectTransform.anchorMin = previewDot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            previewDot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            previewDot.rectTransform.anchoredPosition = Vector2.zero;
            previewDot.rectTransform.sizeDelta = new Vector2(dot, dot);
            Ui.SetRadius(previewDot, mode == "fill" ? 6f : dot / 2f);
            sizeLabel.text = mode == "fill" ? "FILL" : Mathf.RoundToInt(slider.Value) + " px";
        }

        static void Ring(Keycap cap, bool on)
        {
            if (on) cap.AddRing(Sk.Sky, 3f);
            else if (cap.Ring != null) cap.Ring.color = Color.clear;
        }

        public float Layout(float x, float y, float width)
        {
            Ui.Place(Root, x, y, width, 10f);
            const float gap = 6f;
            var sw = (width - 7 * gap) / 8f;
            var swH = Mathf.Min(30f, sw);
            for (var i = 0; i < swatches.Count; i++)
            {
                var row = i / 8;
                var col = i % 8;
                swatches[i].SetSize(sw, swH);
                Ui.Place(swatches[i].Root, col * (sw + gap), row * (swH + 3f + gap), sw, swH + 3f);
            }
            var rowY = 2f * (swH + 3f) + gap + 12f;
            const float previewSize = 40f;
            Ui.Place(preview.Root, 0f, rowY, previewSize, previewSize);
            slider.Layout(previewSize + 14f, rowY, width - previewSize - 14f - 60f, previewSize);
            Ui.Place(sizeLabel, width - 56f, rowY, 56f, previewSize);
            rowY += previewSize + 12f;
            var tools = new[] { pen, fill, eraser, undo, clear };
            var tw = (width - (tools.Length - 1) * gap) / tools.Length;
            for (var i = 0; i < tools.Length; i++)
            {
                tools[i].SetSize(tw, 34f);
                Ui.Place(tools[i].Root, i * (tw + gap), rowY, tw, 37f);
            }
            Refresh();
            var h = rowY + 37f;
            Root.sizeDelta = new Vector2(width, h);
            return h;
        }

        public static float HeightFor(float width)
        {
            var sw = (width - 7 * 6f) / 8f;
            var swH = Mathf.Min(30f, sw);
            return 2f * (swH + 3f) + 6f + 12f + 40f + 12f + 37f;
        }
    }
}
