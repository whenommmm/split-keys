using System;
using UnityEngine;
using UnityEngine.EventSystems;
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
            box.Frame = Ui.Frame(parent, Sk.Ink, Sk.Socket, 12f, 2.5f, "Input");
            box.Frame.Inner.raycastTarget = true;
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
            box.Field.caretColor = Sk.Ink;
            box.Field.caretWidth = 2;
            box.Field.selectionColor = Sk.Alpha(Sk.Sky, 0.3f);
            var colors = box.Field.colors;
            colors.normalColor = colors.highlightedColor = colors.pressedColor = colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1, 1, 1, 0.5f);
            box.Field.colors = colors;
            return box;
        }

        public void Tick()
        {
            Frame.Outer.color = Error ? Sk.Danger : Field.isFocused ? Sk.Sky : Sk.Ink;
            var width = Error || Field.isFocused ? 3.5f : 2.5f;
            if (Mathf.Abs(Frame.BorderWidth - width) > 0.01f) Frame.SetBorder(width);
        }
    }

    // A scroll view whose mouse wheel and trackpad scrolling glides to its target instead of jumping,
    // with a soft rubber band at the ends when dragging on a phone.
    public sealed class SmoothScroll : ScrollRect
    {
        // Scroll deltas arrive in proportion to the browser's wheel pixels (a 100px notch moves about 90 units),
        // except the first event after the page loads, which can be huge; capping each step keeps it from
        // flinging the page to the bottom.
        const float WheelStep = 150f;
        const float MaxStep = 140f;
        float target;
        bool gliding;

        float MaxOffset => Mathf.Max(0f, content.rect.height - viewport.rect.height);

        public override void OnScroll(PointerEventData data)
        {
            if (!IsActive() || content == null) return;
            if (!gliding) target = content.anchoredPosition.y;
            var step = Mathf.Clamp(data.scrollDelta.y * WheelStep, -MaxStep, MaxStep);
            target = Mathf.Clamp(target - step, 0f, MaxOffset);
            gliding = true;
            StopMovement();
        }

        public override void OnBeginDrag(PointerEventData data)
        {
            gliding = false;
            base.OnBeginDrag(data);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!gliding || content == null) return;
            var pos = content.anchoredPosition;
            var goal = Mathf.Clamp(target, 0f, MaxOffset);
            var next = Mathf.Lerp(pos.y, goal, 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
            if (Mathf.Abs(next - goal) < 0.5f)
            {
                next = goal;
                gliding = false;
            }
            content.anchoredPosition = new Vector2(pos.x, next);
        }

        public void JumpTo(float y)
        {
            gliding = false;
            StopMovement();
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
        }
    }

    // Full-screen vertically scrolling page.
    public sealed class ScrollPage
    {
        public RectTransform Root;
        public RectTransform Viewport;
        public RectTransform Content;
        public SmoothScroll Scroll;

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
            page.Scroll = page.Root.gameObject.AddComponent<SmoothScroll>();
            page.Scroll.viewport = page.Viewport;
            page.Scroll.content = page.Content;
            page.Scroll.horizontal = false;
            page.Scroll.vertical = true;
            page.Scroll.movementType = ScrollRect.MovementType.Elastic;
            page.Scroll.elasticity = 0.12f;
            page.Scroll.inertia = true;
            page.Scroll.decelerationRate = 0.12f;
            return page;
        }

        public void SetSize(float w, float h)
        {
            Content.anchoredPosition = new Vector2(0f, Content.anchoredPosition.y);
            Content.sizeDelta = new Vector2(w, h);
        }
    }

    // The draggable part of a StepSlider: reports where along it the pointer is, 0..1.
    public sealed class SliderBar : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Action<float> OnPick;
        public Action OnRelease;
        public bool Interactive = true;
        public bool Held { get; private set; }

        public void OnPointerDown(PointerEventData data)
        {
            if (!Interactive) return;
            Held = true;
            Pick(data);
        }

        public void OnDrag(PointerEventData data)
        {
            if (Held) Pick(data);
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (!Held) return;
            Held = false;
            OnRelease?.Invoke();
        }

        void Pick(PointerEventData data)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, data.position, data.pressEventCamera, out var local)) return;
            var rect = rt.rect;
            OnPick?.Invoke(Mathf.Clamp01((local.x - rect.xMin - 13f) / Mathf.Max(1f, rect.width - 26f)));
        }
    }

    // Picks one of a list of values: drag the knob for big jumps, tap − or + for exact steps.
    // OnChange reports each new value and whether the choice is final (released or stepped).
    public sealed class StepSlider
    {
        public const float Height = 40f;
        public RectTransform Root;
        public int[] Values;
        public int Index;
        public Action<int, bool> OnChange;
        SliderBar bar;
        Image track, fill, knobEdge, knob;
        Keycap minus, plus;
        float barWidth;

        public bool Held => bar.Held;

        public static StepSlider Create(Transform parent, int[] values)
        {
            var slider = new StepSlider { Root = Ui.Node("StepSlider", parent), Values = values };
            slider.minus = Keycap.Create(slider.Root, "−", Sk.Display, 22, Sk.White, Sk.Ink, 3f, 9f, false);
            slider.minus.OnClick = () => slider.Step(-1);
            slider.plus = Keycap.Create(slider.Root, "+", Sk.Display, 22, Sk.White, Sk.Ink, 3f, 9f, false);
            slider.plus.OnClick = () => slider.Step(1);
            var barRt = Ui.Node("Bar", slider.Root);
            Ui.Hit(barRt);
            slider.bar = barRt.gameObject.AddComponent<SliderBar>();
            slider.bar.OnPick = slider.Pick;
            slider.bar.OnRelease = () => slider.OnChange?.Invoke(slider.Values[slider.Index], true);
            slider.track = Ui.Box(barRt, Sk.Ink, 6f, "Track");
            slider.fill = Ui.Box(barRt, Sk.Cream, 4f, "Fill");
            slider.knobEdge = Ui.Box(barRt, Sk.Ink, 15.5f, "KnobEdge");
            slider.knob = Ui.Box(barRt, Sk.Cream, 13f, "Knob");
            return slider;
        }

        // Shows a value from the server, unless the host is mid-drag (their own knob wins).
        public void SetValue(int value)
        {
            if (Held) return;
            var best = 0;
            for (var i = 1; i < Values.Length; i++) if (Mathf.Abs(Values[i] - value) < Mathf.Abs(Values[best] - value)) best = i;
            Index = best;
            Refresh();
        }

        public void SetInteractive(bool on)
        {
            bar.Interactive = on;
            minus.Root.gameObject.SetActive(on);
            plus.Root.gameObject.SetActive(on);
            knob.color = on ? Sk.Cream : Sk.Mat2;
            fill.color = on ? Sk.Cream : Sk.Dim;
        }

        void Pick(float t)
        {
            var index = Mathf.RoundToInt(t * (Values.Length - 1));
            if (index == Index) return;
            Index = index;
            Refresh();
            OnChange?.Invoke(Values[Index], false);
        }

        void Step(int delta)
        {
            var index = Mathf.Clamp(Index + delta, 0, Values.Length - 1);
            if (index == Index) return;
            Index = index;
            Refresh();
            OnChange?.Invoke(Values[Index], true);
        }

        public void Layout(float x, float y, float width)
        {
            Ui.Place(Root, x, y, width, Height + 4f);
            var buttons = minus.Root.gameObject.activeSelf;
            const float bw = 44f;
            if (buttons)
            {
                minus.SetSize(bw, 36f);
                plus.SetSize(bw, 36f);
                Ui.Place(minus.Root, 0f, 2f, bw, 39f);
                Ui.Place(plus.Root, width - bw, 2f, bw, 39f);
            }
            var bx = buttons ? bw + 10f : 0f;
            barWidth = width - bx * 2f;
            Ui.Place((RectTransform)bar.transform, bx, 0f, barWidth, Height);
            Refresh();
        }

        void Refresh()
        {
            var t = Values.Length > 1 ? Index / (Values.Length - 1f) : 0f;
            var usable = barWidth - 26f;
            Ui.Place(track, 0f, Height / 2f - 6f, barWidth, 12f);
            Ui.Place(fill, 3f, Height / 2f - 3f, 10f + usable * t, 6f);
            Ui.Place(knobEdge, usable * t - 2.5f, Height / 2f - 13f - 2.5f, 31f, 34f);
            Ui.Place(knob, usable * t, Height / 2f - 13f, 26f, 26f);
        }
    }

    public sealed class MuteButton
    {
        public Framed Frame;
        Image icon;

        public static MuteButton Create(Transform parent, Action onTap)
        {
            var button = new MuteButton();
            button.Frame = Ui.Frame(parent, Sk.Ink, Sk.White, 12f, 2.5f, "Mute");
            button.icon = Ui.Icon(button.Frame.Root, SkGfx.Speaker, Sk.Ink);
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
            bar.Leave = Sk.Punch(Ui.Label(bar.Root, "Leave", Sk.Display, 18, Sk.White, TextAnchor.MiddleLeft), 1.5f, 2f);
            bar.Back = Ui.Icon(bar.Leave.transform, SkGfx.Back, Sk.White, "Back");
            Ui.Place(bar.Back, -2f, 15f, 14f, 14f);
            Tap.On(bar.Leave.rectTransform, onLeave);
            bar.Wordmark = Sk.Punch(Ui.Label(bar.Root, SplitKeysApp.GameName, Sk.Display, 20, Sk.White, TextAnchor.MiddleCenter), 1.5f, 2.5f);
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

    // One player in the lobby: their avatar standing over a name plate, or an empty spot.
    public sealed class Seat
    {
        public RectTransform Root;
        CanvasGroup group;
        AvatarView avatar;
        Text ghost;
        Image plateEdge;
        Image plate;
        Text name;
        Text badge;
        Image crown;

        public static Seat Create(Transform parent)
        {
            var seat = new Seat { Root = Ui.Node("Seat", parent) };
            seat.group = seat.Root.gameObject.AddComponent<CanvasGroup>();
            seat.avatar = AvatarView.Create(seat.Root, true);
            seat.ghost = Sk.Punch(Ui.Label(seat.Root, "?", Sk.Display, 56, Sk.Alpha(Sk.White, 0.75f), TextAnchor.MiddleCenter, "Ghost"), 2f, 0f);
            seat.plateEdge = Ui.Box(seat.Root, Sk.Ink, 12.5f, "PlateEdge");
            seat.plate = Ui.Box(seat.Root, Sk.White, 10f, "Plate");
            seat.name = Ui.Label(seat.Root, "", Sk.Display, 17, Sk.Ink, TextAnchor.MiddleCenter, "Name");
            seat.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            seat.badge = Ui.Label(seat.Root, "", Sk.Display, 13, Sk.White, TextAnchor.MiddleCenter, "Badge");
            Sk.Punch(seat.badge, 1.5f, 0f);
            seat.crown = Ui.Icon(seat.Root, SkGfx.Crown, Sk.Cream, "Crown");
            var crownLine = seat.crown.gameObject.AddComponent<Outline>();
            crownLine.effectColor = Sk.Ink;
            crownLine.effectDistance = new Vector2(1.5f, -1.5f);
            return seat;
        }

        public const float PlateH = 32f;

        public void Set(PlayerState player, bool isSelf, bool isHost, Color team, float x, float y, float width, float size)
        {
            Ui.Place(Root, x, y, width, size + PlateH + 22f);
            var empty = player == null;
            avatar.Root.gameObject.SetActive(!empty);
            ghost.gameObject.SetActive(empty);
            crown.gameObject.SetActive(!empty && isHost);
            badge.gameObject.SetActive(!empty && (isSelf || player.isBot || !player.connected));
            var plateW = Mathf.Min(width, 150f);
            var plateX = (width - plateW) / 2f;
            var plateY = size + 4f;
            if (empty)
            {
                group.alpha = 1f;
                Ui.Place(ghost, (width - size) / 2f, 0f, size, size);
                name.text = "Waiting…";
                name.color = Sk.Muted;
                plate.color = Sk.Alpha(Sk.White, 0.6f);
                plateEdge.color = Sk.Alpha(Sk.Ink, 0.35f);
            }
            else
            {
                group.alpha = player.connected ? 1f : 0.5f;
                avatar.Set(player.id);
                avatar.Place((width - size) / 2f, 0f, size);
                name.text = player.name;
                name.color = Sk.Ink;
                plate.color = Sk.White;
                plateEdge.color = Sk.Ink;
                badge.text = !player.connected ? "RECONNECTING" : isSelf ? "YOU" : "BOT";
                Ui.Place(badge, 0f, plateY + PlateH + 4f, width, 18f);
                Ui.Place(crown, plateX - 10f, plateY - 13f, 28f, 22f);
                crown.rectTransform.localEulerAngles = new Vector3(0f, 0f, 18f);
            }
            Ui.Place(plateEdge, plateX - 2.5f, plateY - 2.5f, plateW + 5f, PlateH + 8f);
            Ui.Place(plate, plateX, plateY, plateW, PlateH);
            Ui.Place(name, plateX + 6f, plateY, plateW - 12f, PlateH);
            name.fontSize = Ui.Width(name) > plateW - 16f ? 14 : 17;
            plate.color = empty ? plate.color : (isSelf ? Sk.Cream : Sk.White);
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
            var edge = Ui.Box(pill.Root, Sk.Ink, 28.5f);
            Ui.Stretch(edge.rectTransform, -2.5f, -2.5f, -2.5f, -6f);
            var bg = Ui.Box(pill.Root, Sk.White, 26f);
            Ui.Stretch(bg.rectTransform);
            pill.label = Ui.Label(pill.Root, "", Sk.Bold, 16, Sk.Ink, TextAnchor.MiddleCenter);
            pill.label.supportRichText = true;
            for (var i = 0; i < 3; i++) pill.dots[i] = Ui.Box(pill.Root, Sk.Ink, 3f, "Dot");
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
        Card card;
        Text label;

        public static DevPanel Create(Transform parent)
        {
            var panel = new DevPanel();
            panel.Root = Ui.Node("DevPanel", parent);
            panel.card = Card.Create(panel.Root, Sk.Mat);
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
            card.Place(0f, 0f, w, Height);
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
