using System;
using UnityEngine;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // A recessed text field.
    public sealed class InputBox
    {
        public Framed Frame;
        public InputField Field;
        public Text Value;
        public Text Placeholder;
        public bool Error;

        public static InputBox Create(Transform parent, string placeholder, Font font, int size, int limit, TextAnchor align)
        {
            var box = new InputBox();
            box.Frame = Ui.Frame(parent, Sk.Line, Sk.Socket, 14f, 2f, "Input");
            box.Frame.Inner.raycastTarget = true;
            var inset = Ui.Box(box.Frame.Inner.transform, new Color(0, 0, 0, 0.35f), 0f, "InsetShadow");
            inset.rectTransform.anchorMin = new Vector2(0f, 1f);
            inset.rectTransform.anchorMax = new Vector2(1f, 1f);
            inset.rectTransform.pivot = new Vector2(0.5f, 1f);
            inset.rectTransform.offsetMin = new Vector2(10f, -3f);
            inset.rectTransform.offsetMax = new Vector2(-10f, 0f);
            box.Value = Ui.Label(box.Frame.Inner.transform, "", font, size, Sk.Text, align, "Text");
            box.Value.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Stretch(box.Value.rectTransform, 16f, 2f, 16f, 2f);
            box.Placeholder = Ui.Label(box.Frame.Inner.transform, placeholder, font, size, Sk.Dim, align, "Placeholder");
            Ui.Stretch(box.Placeholder.rectTransform, 16f, 2f, 16f, 2f);
            box.Field = box.Frame.Inner.gameObject.AddComponent<InputField>();
            box.Field.textComponent = box.Value;
            box.Field.placeholder = box.Placeholder;
            box.Field.targetGraphic = box.Frame.Inner;
            box.Field.characterLimit = limit;
            box.Field.lineType = InputField.LineType.SingleLine;
            box.Field.customCaretColor = true;
            box.Field.caretColor = Sk.Cream;
            box.Field.caretWidth = 2;
            box.Field.selectionColor = Sk.Alpha(Sk.Cream, 0.3f);
            var colors = box.Field.colors;
            colors.normalColor = colors.highlightedColor = colors.pressedColor = colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1, 1, 1, 0.5f);
            box.Field.colors = colors;
            return box;
        }

        public void Tick()
        {
            Frame.Outer.color = Error ? Sk.Danger : Field.isFocused ? Sk.Cream : Sk.Line;
        }
    }

    // Full-screen vertically scrolling page.
    public sealed class ScrollPage
    {
        public RectTransform Root;
        public RectTransform Viewport;
        public RectTransform Content;
        public ScrollRect Scroll;

        public static ScrollPage Create(Transform parent, string name)
        {
            var page = new ScrollPage();
            page.Root = Ui.Node(name, parent);
            Ui.Stretch(page.Root);
            page.Viewport = Ui.Node("Viewport", page.Root);
            Ui.Stretch(page.Viewport);
            Ui.Hit(page.Viewport);
            page.Viewport.gameObject.AddComponent<RectMask2D>();
            page.Content = Ui.Node("Content", page.Viewport);
            page.Scroll = page.Root.gameObject.AddComponent<ScrollRect>();
            page.Scroll.viewport = page.Viewport;
            page.Scroll.content = page.Content;
            page.Scroll.horizontal = false;
            page.Scroll.vertical = true;
            page.Scroll.movementType = ScrollRect.MovementType.Clamped;
            page.Scroll.scrollSensitivity = 40f;
            page.Scroll.inertia = true;
            return page;
        }

        public void SetSize(float w, float h)
        {
            Content.anchoredPosition = new Vector2(0f, Content.anchoredPosition.y);
            Content.sizeDelta = new Vector2(w, h);
        }
    }

    public sealed class MuteButton
    {
        public Framed Frame;
        Image icon;

        public static MuteButton Create(Transform parent, Action onTap)
        {
            var button = new MuteButton();
            button.Frame = Ui.Frame(parent, Sk.Line, Sk.Alpha(Sk.Bg, 0.6f), 12f, 1f, "Mute");
            button.icon = Ui.Icon(button.Frame.Root, SkGfx.Speaker, Sk.Muted);
            Ui.Place(button.icon, 11f, 11f, 22f, 22f);
            Tap.On(button.Frame.Root, onTap);
            return button;
        }

        public void Place(float x, float y) => Ui.Place(Frame.Root, x, y, 44f, 44f);

        public void Refresh(bool muted)
        {
            icon.sprite = muted ? SkGfx.SpeakerOff : SkGfx.Speaker;
        }
    }

    // "← Leave" on the left, wordmark in the middle, mute on the right.
    public sealed class TopBar
    {
        public RectTransform Root;
        public Text Leave;
        public Image Back;
        public Text Wordmark;
        public MuteButton Mute;

        public static TopBar Create(Transform parent, Action onLeave, Action onMute)
        {
            var bar = new TopBar();
            bar.Root = Ui.Node("TopBar", parent);
            bar.Leave = Ui.Label(bar.Root, "Leave", Sk.Bold, 15, Sk.Muted, TextAnchor.MiddleLeft);
            bar.Back = Ui.Icon(bar.Leave.transform, SkGfx.Back, Sk.Muted, "Back");
            Ui.Place(bar.Back, -2f, 15f, 14f, 14f);
            Tap.On(bar.Leave.rectTransform, onLeave);
            bar.Wordmark = Ui.Label(bar.Root, "SPLIT KEYS", Sk.Display, 14, Sk.Muted, TextAnchor.MiddleCenter);
            bar.Mute = MuteButton.Create(bar.Root, onMute);
            return bar;
        }

        public void Layout(float x0, float width, float y)
        {
            Ui.Place(Root, 0f, 0f, 0f, 0f);
            Ui.Place(Leave, x0 + 16f, y, 80f, 44f);
            Ui.Place(Back, -18f, 15f, 14f, 14f);
            Ui.Place(Wordmark, x0 + 96f, y, width - 192f, 44f);
            Mute.Place(x0 + width - 44f, y);
        }
    }

    public sealed class Tag
    {
        public RectTransform Root;
        public Image Bg;
        public Text Label;
        public Image Icon;
        public float Width;

        public static Tag Create(Transform parent, string text, Color fg, Color bg, Sprite icon = null, Color? border = null)
        {
            var tag = new Tag();
            tag.Root = Ui.Node("Tag", parent);
            if (border.HasValue)
            {
                var frame = Ui.Frame(tag.Root, border.Value, bg, 11f, 1.5f);
                Ui.Stretch(frame.Root);
                tag.Bg = frame.Inner;
            }
            else
            {
                tag.Bg = Ui.Box(tag.Root, bg, 11f);
                Ui.Stretch(tag.Bg.rectTransform);
            }
            var x = 9f;
            if (icon != null)
            {
                tag.Icon = Ui.Icon(tag.Root, icon, fg);
                Ui.Place(tag.Icon, 8f, 5f, 12f, 12f);
                x = 24f;
            }
            tag.Label = Ui.Label(tag.Root, text, Sk.Bold, 11, fg, TextAnchor.MiddleLeft);
            tag.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var textWidth = Ui.Width(tag.Label);
            Ui.Place(tag.Label, x, 0f, textWidth + 2f, 22f);
            tag.Width = x + textWidth + 9f;
            return tag;
        }
    }

    // One player in the lobby list.
    public sealed class PlayerRow
    {
        public RectTransform Root;
        CanvasGroup group;
        Image bg;
        Image accent;
        Keycap avatar;
        Text name;
        Text status;
        RectTransform tags;

        public static PlayerRow Create(Transform parent)
        {
            var row = new PlayerRow();
            row.Root = Ui.Node("PlayerRow", parent);
            row.group = row.Root.gameObject.AddComponent<CanvasGroup>();
            row.bg = Ui.Box(row.Root, Sk.Mat, 14f, "Bg");
            row.accent = Ui.Box(row.Root, Color.white, 1.5f, "Accent");
            row.avatar = Keycap.Create(row.Root, "", Sk.Display, 18, Color.white, Sk.Ink, 3f, 9f, false, "Avatar");
            row.avatar.Interactive = false;
            row.name = Ui.Label(row.Root, "", Sk.Bold, 18, Sk.Text, TextAnchor.MiddleLeft, "Name");
            row.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            row.status = Ui.Label(row.Root, "reconnecting…", Sk.Body, 14, Sk.Muted, TextAnchor.MiddleRight, "Status");
            row.status.fontStyle = FontStyle.Italic;
            row.tags = Ui.Node("Tags", row.Root);
            return row;
        }

        public void Set(PlayerState player, bool isSelf, bool isHost, float width)
        {
            var color = Sk.Hex(player.color);
            Ui.Place(bg, 0f, 0f, width, 60f);
            accent.gameObject.SetActive(isSelf);
            accent.color = color;
            Ui.Place(accent, 0f, 10f, 3f, 40f);
            avatar.SetColors(color, Sk.Ink, 0f);
            avatar.SetLabel(string.IsNullOrEmpty(player.name) ? "?" : player.name.Substring(0, 1).ToUpperInvariant());
            avatar.SetSize(36f, 36f);
            Ui.Place(avatar.Root, 12f, 10f, 36f, 39f);
            name.text = player.name;
            Ui.Place(name, 60f, 0f, width - 60f - 160f, 60f);
            group.alpha = player.connected ? 1f : 0.45f;
            Ui.Clear(tags);
            status.gameObject.SetActive(!player.connected);
            Ui.Place(status, width - 160f, 0f, 146f, 60f);
            if (!player.connected) return;
            var right = width - 12f;
            if (isSelf) right = PlaceTag(Tag.Create(tags, "YOU", color, Sk.Mat, null, color), right);
            if (isHost) right = PlaceTag(Tag.Create(tags, "HOST", Sk.Cream, Sk.Alpha(Sk.Cream, 0.12f), SkGfx.Crown), right);
            if (player.isBot) PlaceTag(Tag.Create(tags, "BOT", Sk.Muted, Sk.Mat2), right);
        }

        static float PlaceTag(Tag tag, float right)
        {
            Ui.Place(tag.Root, right - tag.Width, 19f, tag.Width, 22f);
            return right - tag.Width - 6f;
        }
    }

    // A pill with three bouncing dots, e.g. "Waiting for Sam to start".
    public sealed class WaitingPill
    {
        public RectTransform Root;
        Text label;
        Image[] dots = new Image[3];
        float width;

        public static WaitingPill Create(Transform parent)
        {
            var pill = new WaitingPill();
            pill.Root = Ui.Node("Waiting", parent);
            var bg = Ui.Box(pill.Root, Sk.Mat2, 26f);
            Ui.Stretch(bg.rectTransform);
            pill.label = Ui.Label(pill.Root, "", Sk.Body, 16, Sk.Muted, TextAnchor.MiddleCenter);
            pill.label.supportRichText = true;
            for (var i = 0; i < 3; i++) pill.dots[i] = Ui.Box(pill.Root, Sk.Muted, 3f, "Dot");
            return pill;
        }

        public void Set(string richText) => label.text = richText;

        public void Place(float x, float y, float w)
        {
            width = w;
            Ui.Place(Root, x, y, w, 52f);
            Ui.Place(label, 16f, 0f, w - 60f, 52f);
        }

        public void Tick()
        {
            var textWidth = Mathf.Min(Ui.Width(label), width - 60f);
            var start = 16f + (width - 60f + textWidth) / 2f + 6f;
            for (var i = 0; i < 3; i++)
            {
                var bounce = Mathf.Max(0f, Mathf.Sin((Time.unscaledTime * 1.6f - i * 0.18f) * Mathf.PI * 2f));
                Ui.Place(dots[i], start + i * 10f, 25f - bounce * 4f, 6f, 6f);
            }
        }
    }

    // DEV TOOLS panel with add/remove bot buttons (only in dev mode, only for the host).
    public sealed class DevPanel
    {
        public RectTransform Root;
        public Keycap Add;
        public Keycap Remove;
        Framed frame;
        Text label;

        public static DevPanel Create(Transform parent)
        {
            var panel = new DevPanel();
            panel.Root = Ui.Node("DevPanel", parent);
            panel.frame = Ui.Frame(panel.Root, Sk.Line, Sk.Mat, 22f, 2f);
            Ui.Stretch(panel.frame.Root);
            panel.label = Ui.Label(panel.Root, "DEV TOOLS", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft);
            panel.Add = Keycap.Create(panel.Root, "+ ADD BOT", Sk.Display, 15, Sk.Slate, Sk.Text, 5f, 14f);
            panel.Add.OnClick = SkBridge.AddBot;
            panel.Remove = Keycap.Create(panel.Root, "− REMOVE BOT", Sk.Display, 15, Sk.Slate, Sk.Text, 5f, 14f);
            panel.Remove.OnClick = SkBridge.RemoveBot;
            panel.Add.SetColors(Sk.Slate, Sk.Text, 0.12f);
            panel.Remove.SetColors(Sk.Slate, Sk.Text, 0.12f);
            return panel;
        }

        public const float Height = 112f;

        public void Place(float x, float y, float w, RoomState state)
        {
            Ui.Place(Root, x, y, w, Height);
            Ui.Place(label, 18f, 12f, w - 36f, 20f);
            var bw = (w - 36f - 10f) / 2f;
            Add.SetSize(bw, 50f);
            Remove.SetSize(bw, 50f);
            Ui.Place(Add.Root, 18f, 42f, bw, 55f);
            Ui.Place(Remove.Root, 18f + bw + 10f, 42f, bw, 55f);
            var bots = 0;
            foreach (var p in state.players) if (p.isBot) bots++;
            Add.SetOff(state.players.Length >= 8);
            Remove.SetOff(bots == 0);
        }
    }
}
