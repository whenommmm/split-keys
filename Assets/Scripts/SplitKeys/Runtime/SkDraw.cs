using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // A paper texture the drawer paints on. Strokes use 0..1 coordinates (y down) so every
    // screen size shows the same picture; brush sizes are pixels on a 480-wide canvas.
    public sealed class PaperCanvas : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public static readonly Color32 Paper = new Color32(0xFF, 0xF8, 0xEA, 0xFF);
        public static readonly string PaperHex = "#FFF8EA";

        public RawImage Image;
        public readonly List<Stroke> Strokes = new List<Stroke>();
        public bool Editable;
        public string BrushHex = "#15121F";
        public float BrushSize = 6f;
        public Action<string> Send;

        Texture2D texture;
        Color32[] pixels;
        int width, height;
        bool dirty;
        int pointer = int.MinValue;
        Vector2 last;
        readonly List<float> pending = new List<float>();
        float lastSend;

        public static PaperCanvas Create(Transform parent, int texWidth, int texHeight, string name = "Paper")
        {
            var rt = Ui.Node(name, parent);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.raycastTarget = true;
            var canvas = rt.gameObject.AddComponent<PaperCanvas>();
            canvas.Image = image;
            canvas.width = texWidth;
            canvas.height = texHeight;
            canvas.texture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            canvas.pixels = new Color32[texWidth * texHeight];
            image.texture = canvas.texture;
            canvas.Wipe();
            return canvas;
        }

        public void Wipe()
        {
            for (var i = 0; i < pixels.Length; i++) pixels[i] = Paper;
            dirty = true;
        }

        public void SetStrokes(Stroke[] strokes)
        {
            Strokes.Clear();
            if (strokes != null) Strokes.AddRange(strokes);
            Redraw();
        }

        public void Redraw()
        {
            Wipe();
            foreach (var stroke in Strokes) Paint(stroke, 0);
        }

        // Applies a stroke/more/undo/clear message from the server.
        public void Apply(DrawOp op)
        {
            switch (op.op)
            {
                case "stroke":
                    var stroke = new Stroke { c = op.c, s = op.s, p = op.p ?? new float[0] };
                    Strokes.Add(stroke);
                    Paint(stroke, 0);
                    break;
                case "more":
                    if (Strokes.Count == 0 || op.p == null) return;
                    var target = Strokes[Strokes.Count - 1];
                    var from = target.p.Length;
                    var merged = new float[from + op.p.Length];
                    Array.Copy(target.p, merged, from);
                    Array.Copy(op.p, 0, merged, from, op.p.Length);
                    target.p = merged;
                    Paint(target, Mathf.Max(0, from - 2));
                    break;
                case "undo":
                    if (Strokes.Count > 0) Strokes.RemoveAt(Strokes.Count - 1);
                    Redraw();
                    break;
                case "clear":
                    Strokes.Clear();
                    Redraw();
                    break;
            }
        }

        public void Undo()
        {
            if (Strokes.Count == 0) return;
            Strokes.RemoveAt(Strokes.Count - 1);
            Redraw();
            Send?.Invoke("{\"op\":\"undo\"}");
        }

        public void ClearAll()
        {
            Strokes.Clear();
            Redraw();
            Send?.Invoke("{\"op\":\"clear\"}");
        }

        void Paint(Stroke stroke, int fromIndex)
        {
            if (stroke.p == null || stroke.p.Length < 2) return;
            ColorUtility.TryParseHtmlString(stroke.c, out var color);
            var c32 = (Color32)color;
            var radius = Mathf.Max(1f, stroke.s * 0.5f * width / 480f);
            var start = fromIndex - fromIndex % 2;
            var prev = ToPixel(stroke.p[start], stroke.p[start + 1]);
            Dot(prev, radius, c32);
            for (var i = start + 2; i + 1 < stroke.p.Length; i += 2)
            {
                var next = ToPixel(stroke.p[i], stroke.p[i + 1]);
                Segment(prev, next, radius, c32);
                prev = next;
            }
        }

        Vector2 ToPixel(float x, float y) => new Vector2(x * (width - 1), (1f - y) * (height - 1));

        void Segment(Vector2 a, Vector2 b, float radius, Color32 color)
        {
            var distance = Vector2.Distance(a, b);
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(1f, radius * 0.5f)));
            for (var i = 1; i <= steps; i++) Dot(Vector2.Lerp(a, b, i / (float)steps), radius, color);
        }

        void Dot(Vector2 center, float radius, Color32 color)
        {
            var r = Mathf.CeilToInt(radius);
            var cx = Mathf.RoundToInt(center.x);
            var cy = Mathf.RoundToInt(center.y);
            var r2 = radius * radius;
            for (var y = Mathf.Max(0, cy - r); y <= Mathf.Min(height - 1, cy + r); y++)
            {
                var dy = y - cy;
                var row = y * width;
                for (var x = Mathf.Max(0, cx - r); x <= Mathf.Min(width - 1, cx + r); x++)
                {
                    var dx = x - cx;
                    if (dx * dx + dy * dy <= r2) pixels[row + x] = color;
                }
            }
            dirty = true;
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
            pointer = data.pointerId;
            last = p;
            var stroke = new Stroke { c = BrushHex, s = BrushSize, p = new[] { Round(p.x), Round(p.y) } };
            Strokes.Add(stroke);
            Paint(stroke, 0);
            pending.Clear();
            Send?.Invoke("{\"op\":\"stroke\",\"c\":\"" + BrushHex + "\",\"s\":" + BrushSize.ToString(CultureInfo.InvariantCulture) + ",\"p\":[" + Num(stroke.p[0]) + "," + Num(stroke.p[1]) + "]}");
            lastSend = Time.unscaledTime;
        }

        public void OnDrag(PointerEventData data)
        {
            if (!Editable || data.pointerId != pointer || Strokes.Count == 0 || !ToNormalized(data, out var p)) return;
            if (Vector2.Distance(p, last) < 0.004f) return;
            last = p;
            var stroke = Strokes[Strokes.Count - 1];
            var from = stroke.p.Length;
            var grown = new float[from + 2];
            Array.Copy(stroke.p, grown, from);
            grown[from] = Round(p.x);
            grown[from + 1] = Round(p.y);
            stroke.p = grown;
            Paint(stroke, from - 2);
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
            if (!dirty) return;
            texture.SetPixels32(pixels);
            texture.Apply(false);
            dirty = false;
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }
    }

    // The sentence as a row of word pills. Guessers see blanks; drawers and guessed words show text.
    public sealed class SentenceView
    {
        public RectTransform Root;
        readonly List<RectTransform> pills = new List<RectTransform>();
        readonly HashSet<int> shownRevealed = new HashSet<int>();
        int roundId = -1;

        public static readonly HashSet<string> Given = new HashSet<string> { "A", "AN", "THE" };

        public static SentenceView Create(Transform parent)
        {
            return new SentenceView { Root = Ui.Node("Sentence", parent) };
        }

        // Returns the height used.
        public float Render(WordView[] words, Color teamColor, int round, float x, float y, float width, bool animate, int fontSize = 18)
        {
            Ui.Clear(Root);
            pills.Clear();
            Ui.Place(Root, x, y, width, 10f);
            if (words == null) return 0f;
            if (round != roundId) { roundId = round; shownRevealed.Clear(); }
            var pillH = fontSize + 18f;
            var gap = 8f;
            var rowY = 0f;
            var rowX = 0f;
            var row = new List<(RectTransform rt, float w)>();
            var rows = new List<List<(RectTransform rt, float w)>>();
            for (var i = 0; i < words.Length; i++)
            {
                var word = words[i];
                var given = Given.Contains(word.text);
                var blank = word.text.IndexOf('_') >= 0;
                var display = blank ? string.Join(" ", word.text.ToCharArray()) : word.text;
                var pill = Ui.Node("Word", Root);
                Color bg, fg;
                if (given) { bg = Color.clear; fg = Sk.Muted; }
                else if (word.revealed) { bg = teamColor; fg = Sk.Ink; }
                else if (blank) { bg = Sk.Socket; fg = Sk.Text; }
                else { bg = Sk.Mat2; fg = Sk.Text; }
                var box = given ? null : Ui.Frame(pill, word.revealed ? teamColor : Sk.Line, bg, 10f, word.revealed ? 0f : 1.5f);
                if (box != null) Ui.Stretch(box.Root);
                var label = Ui.Label(pill, display, Sk.Display, fontSize, fg, TextAnchor.MiddleCenter);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                Ui.Stretch(label.rectTransform);
                var w = Ui.Width(label) + (given ? 4f : 22f);
                if (rowX > 0f && rowX + w > width)
                {
                    rows.Add(row);
                    row = new List<(RectTransform, float)>();
                    rowX = 0f;
                }
                row.Add((pill, w));
                rowX += w + gap;
                pills.Add(pill);
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

        public void SetAccent(Color color) => enter.SetColors(color, Sk.Ink);

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

    // The drawer's palette, brush sizes, eraser, undo and clear.
    public sealed class ToolBar
    {
        public static readonly string[] Colors = { "#15121F", "#FF5252", "#FF922B", "#FFD43B", "#69DB7C", "#4DABF7", "#9775FA", "#8B5A2B" };
        public RectTransform Root;
        public Action<string> OnColor;
        public Action<float> OnSize;
        public Action OnUndo, OnClear;
        readonly List<Keycap> swatches = new List<Keycap>();
        Keycap small, large, eraser, undo, clear;
        string selectedColor = Colors[0];
        float selectedSize = 6f;
        bool erasing;

        public static ToolBar Create(Transform parent)
        {
            var bar = new ToolBar { Root = Ui.Node("Tools", parent) };
            foreach (var hex in Colors)
            {
                var color = hex;
                var cap = Keycap.Create(bar.Root, "", Sk.Display, 12, Sk.Hex(hex), Sk.Ink, 3f, 9f, false, "Swatch");
                cap.OnDown = () => bar.Pick(color);
                bar.swatches.Add(cap);
            }
            bar.small = Slate(bar.Root, "THIN");
            bar.small.OnDown = () => bar.Size(6f);
            bar.large = Slate(bar.Root, "THICK");
            bar.large.OnDown = () => bar.Size(16f);
            bar.eraser = Keycap.Create(bar.Root, "ERASER", Sk.Display, 12, Sk.Hex(PaperCanvas.PaperHex), Sk.Ink, 3f, 9f, false);
            bar.eraser.OnDown = () => bar.Erase();
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

        void Pick(string hex)
        {
            selectedColor = hex;
            erasing = false;
            OnColor?.Invoke(hex);
            if (selectedSize > 20f) Size(6f);
            Refresh();
        }

        void Size(float size)
        {
            selectedSize = size;
            if (erasing) { erasing = false; OnColor?.Invoke(selectedColor); }
            OnSize?.Invoke(size);
            Refresh();
        }

        void Erase()
        {
            erasing = true;
            OnColor?.Invoke(PaperCanvas.PaperHex);
            OnSize?.Invoke(28f);
            Refresh();
        }

        public void Reset()
        {
            selectedColor = Colors[0];
            selectedSize = 6f;
            erasing = false;
            OnColor?.Invoke(selectedColor);
            OnSize?.Invoke(selectedSize);
            Refresh();
        }

        void Refresh()
        {
            for (var i = 0; i < swatches.Count; i++)
            {
                var on = !erasing && Colors[i] == selectedColor;
                if (on) swatches[i].AddRing(Sk.Cream, 3f);
                else if (swatches[i].Ring != null) swatches[i].Ring.color = Color.clear;
            }
            Ring(small, !erasing && selectedSize < 10f);
            Ring(large, !erasing && selectedSize >= 10f);
            Ring(eraser, erasing);
        }

        static void Ring(Keycap cap, bool on)
        {
            if (on) cap.AddRing(Sk.Cream, 3f);
            else if (cap.Ring != null) cap.Ring.color = Color.clear;
        }

        public float Layout(float x, float y, float width)
        {
            Ui.Place(Root, x, y, width, 10f);
            const float gap = 6f;
            var sw = (width - (Colors.Length - 1) * gap) / Colors.Length;
            var swH = Mathf.Min(34f, sw);
            for (var i = 0; i < swatches.Count; i++)
            {
                swatches[i].SetSize(sw, swH);
                Ui.Place(swatches[i].Root, i * (sw + gap), 0f, sw, swH + 3f);
            }
            var rowY = swH + 3f + 10f;
            var tools = new[] { small, large, eraser, undo, clear };
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
    }
}
