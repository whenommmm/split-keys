using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    public abstract class SkScreen
    {
        protected readonly SplitKeysApp App;
        public RectTransform Root;
        public CanvasGroup Group;

        protected SkScreen(SplitKeysApp app, string name)
        {
            App = app;
            Root = Ui.Node(name, app.ScreensLayer);
            Ui.Stretch(Root);
            Group = Root.gameObject.AddComponent<CanvasGroup>();
        }

        public bool Visible
        {
            get => Root.gameObject.activeSelf;
            set => Root.gameObject.SetActive(value);
        }

        public virtual void Layout() { }
        public virtual void Render() { }
        public virtual void Tick() { }
        public virtual void OnFx(Fx fx) { }
        public virtual void OnShow() { }

        protected static string Escape(string value) => (value ?? "").Replace("<", "‹").Replace(">", "›");

        protected static string Colored(string text, string hex) => "<color=" + hex + ">" + Escape(text) + "</color>";
    }

    public sealed class HomeScreen : SkScreen
    {
        static readonly int[] LogoTilt = { -4, 2, -2, 3, -1, 2, -3, 1, -2 };
        const string LogoLetters = "SPLITKEYS";

        readonly ScrollPage page;
        readonly MuteButton mute;
        readonly Keycap[] logo = new Keycap[9];
        readonly RectTransform[] logoSlots = new RectTransform[9];
        readonly Text tagline;
        readonly RectTransform notice;
        readonly Text noticeText;
        readonly Framed panel;
        readonly Text inviteHeader;
        readonly Keycap[] inviteCaps = new Keycap[4];
        readonly Text nameLabel;
        readonly InputBox name;
        readonly Text nameError;
        readonly Keycap create;
        readonly Image dividerLeft, dividerRight;
        readonly Text dividerText;
        readonly InputBox code;
        readonly Keycap join;
        readonly Text codeError;
        readonly Keycap inviteJoin;
        readonly Text switchLink;
        readonly Text slowLine;
        readonly Text howTitle;
        readonly Keycap[] bullets = new Keycap[4];
        readonly Text[] rules = new Text[4];
        readonly RectTransform mini;
        readonly List<TileView> miniTiles = new List<TileView>();
        readonly Text footer;

        string busy;

        public HomeScreen(SplitKeysApp app) : base(app, "Home")
        {
            page = ScrollPage.Create(Root, "Page");
            var c = page.Content;
            mute = MuteButton.Create(c, app.ToggleMute);
            for (var i = 0; i < 9; i++)
            {
                logoSlots[i] = Ui.Node("LogoSlot", c);
                logo[i] = Keycap.Create(logoSlots[i], LogoLetters[i].ToString(), Sk.Display, 30, Sk.Hex(Sk.PlayerHex[i % 8]), Sk.Ink, 5f, 13f);
                logo[i].Interactive = false;
                logoSlots[i].localEulerAngles = new Vector3(0f, 0f, LogoTilt[i]);
            }
            tagline = Ui.Label(c, "One phrase. 20 seconds. Everyone has different letters.", Sk.Body, 16, Sk.Muted, TextAnchor.UpperCenter);

            notice = Ui.Node("Notice", c);
            var noticeBg = Ui.Box(notice, Sk.Mat, 14f);
            Ui.Stretch(noticeBg.rectTransform);
            var noticeBar = Ui.Box(notice, Sk.Danger, 2f);
            Ui.Place(noticeBar, 0f, 0f, 4f, 52f);
            noticeText = Ui.Label(notice, "", Sk.Bold, 15, Sk.Text, TextAnchor.MiddleLeft);
            var close = Ui.Label(notice, "×", Sk.Bold, 22, Sk.Muted, TextAnchor.MiddleCenter, "Close");
            Tap.On(close.rectTransform, () => { App.Notice = null; Render(); });
            close.name = "Close";

            panel = Ui.Frame(c, Sk.Line, Sk.Mat, 22f, 1f, "Panel");
            var p = panel.Root;
            inviteHeader = Ui.Label(p, "You're invited to", Sk.Body, 16, Sk.Muted, TextAnchor.MiddleCenter);
            for (var i = 0; i < 4; i++)
            {
                inviteCaps[i] = Keycap.Create(p, "", Sk.Display, 22, Sk.Slate, Sk.Text, 5f, 10f);
                inviteCaps[i].Interactive = false;
                inviteCaps[i].SetColors(Sk.Slate, Sk.Text, 0.12f);
            }
            nameLabel = Ui.Label(p, "YOUR NAME", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft);
            name = InputBox.Create(p, "e.g. Sam", Sk.Bold, 18, 12, TextAnchor.MiddleLeft);
            name.Field.text = PlayerPrefs.GetString("splitkeys.name", "");
            name.Field.onValueChanged.AddListener(_ => { name.Error = false; nameError.text = ""; Layout(); });
            name.Field.onEndEdit.AddListener(_ => { if (EnterPressed()) { if (App.InviteCode != null) JoinInvite(); else Create(); } });
            nameError = Ui.Label(p, "", Sk.Bold, 14, Sk.Danger, TextAnchor.MiddleLeft);
            create = Keycap.Create(p, "CREATE ROOM", Sk.Display, 18, Sk.Cream, Sk.Ink, 6f, 14f);
            create.OnClick = Create;
            dividerLeft = Ui.Box(p, Sk.Line, 0f);
            dividerRight = Ui.Box(p, Sk.Line, 0f);
            dividerText = Ui.Label(p, "OR JOIN A ROOM", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleCenter);
            code = InputBox.Create(p, "ABCD", Sk.Display, 26, 4, TextAnchor.MiddleCenter);
            code.Field.onValidateInput = (text, index, ch) =>
            {
                ch = char.ToUpperInvariant(ch);
                return ch >= 'A' && ch <= 'Z' ? ch : '\0';
            };
            code.Field.onValueChanged.AddListener(_ => { code.Error = false; codeError.text = ""; Layout(); });
            code.Field.onEndEdit.AddListener(_ => { if (EnterPressed()) Join(); });
            join = Keycap.Create(p, "JOIN", Sk.Display, 18, Sk.Slate, Sk.Text, 6f, 14f);
            join.SetColors(Sk.Slate, Sk.Text, 0.12f);
            join.OnClick = Join;
            codeError = Ui.Label(p, "", Sk.Bold, 14, Sk.Danger, TextAnchor.MiddleLeft);
            inviteJoin = Keycap.Create(p, "JOIN ROOM", Sk.Display, 18, Sk.Cream, Sk.Ink, 6f, 14f);
            inviteJoin.OnClick = JoinInvite;
            switchLink = Ui.Label(p, "Or create a new room", Sk.Bold, 15, Sk.Muted, TextAnchor.MiddleCenter);
            Tap.On(switchLink.rectTransform, () => { App.ClearInvite(); Render(); });

            slowLine = Ui.Label(c, "Waking up the server. The first visit can take up to a minute.", Sk.Body, 15, Sk.Muted, TextAnchor.UpperCenter);
            howTitle = Ui.Label(c, "HOW TO PLAY", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft);
            for (var i = 0; i < 4; i++)
            {
                bullets[i] = Keycap.Create(c, (i + 1).ToString(), Sk.Display, 16, Sk.Slate, Sk.Text, 3f, 8f, false);
                bullets[i].SetColors(Sk.Slate, Sk.Text, 0.12f);
                bullets[i].Interactive = false;
                rules[i] = Ui.Label(c, SplitKeysApp.Rules[i], Sk.Body, 16, Sk.Text, TextAnchor.MiddleLeft);
            }
            mini = Ui.Node("MiniBoard", c);
            var sample = "HOT DOG";
            for (var i = 0; i < sample.Length; i++)
            {
                if (sample[i] == ' ') { miniTiles.Add(null); continue; }
                var tile = TileView.Create(mini, sample[i], 34f);
                miniTiles.Add(tile);
            }
            footer = Ui.Label(c, "No app. No account. 2–8 players.", Sk.Body, 14, Sk.Dim, TextAnchor.MiddleCenter);
        }

        static bool EnterPressed()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
        }

        public void SetBusy(string value)
        {
            busy = value;
            Render();
        }

        public void ShowError(bool onName, string message)
        {
            busy = null;
            if (onName)
            {
                nameError.text = message;
                name.Error = true;
                ShakeRect(name.Frame.Root);
            }
            else
            {
                codeError.text = message;
                code.Error = true;
                ShakeRect(code.Frame.Root);
            }
            Render();
        }

        static void ShakeRect(RectTransform rt)
        {
            var origin = rt.anchoredPosition;
            Tween.Run(rt, 0.3f, t => rt.anchoredPosition = origin + new Vector2(6f * Tween.Shake(t), 0f), () => rt.anchoredPosition = origin);
        }

        string ReadName()
        {
            var value = (name.Field.text ?? "").Trim();
            if (value.Length == 0 || value.Length > 12)
            {
                ShowError(true, "Enter a name (1–12 characters).");
                return null;
            }
            PlayerPrefs.SetString("splitkeys.name", value);
            PlayerPrefs.Save();
            return value;
        }

        void Create()
        {
            if (busy != null || !App.Connected) return;
            var value = ReadName();
            if (value == null) return;
            SetBusy("create");
            App.CreateRoom(value);
        }

        void Join()
        {
            if (busy != null || !App.Connected) return;
            var value = ReadName();
            if (value == null) return;
            var roomCode = (code.Field.text ?? "").Trim().ToUpperInvariant();
            if (roomCode.Length != 4)
            {
                ShowError(false, "Room codes are 4 letters.");
                return;
            }
            SetBusy("join");
            App.JoinRoom(roomCode, value);
        }

        void JoinInvite()
        {
            if (busy != null || !App.Connected || App.InviteCode == null) return;
            var value = ReadName();
            if (value == null) return;
            SetBusy("join");
            App.JoinRoom(App.InviteCode, value);
        }

        public override void OnShow()
        {
            busy = null;
            page.Content.anchoredPosition = Vector2.zero;
        }

        public override void Render()
        {
            var invited = App.InviteCode != null;
            var connecting = !App.Connected;
            create.SetLabel(connecting ? "CONNECTING" : busy == "create" ? "CREATING…" : "CREATE ROOM");
            create.SetOff(connecting || busy != null);
            join.SetLabel(connecting ? "…" : busy == "join" ? "…" : "JOIN");
            join.SetOff(connecting || busy != null);
            inviteJoin.SetLabel(connecting ? "CONNECTING" : App.Resuming ? "REJOINING…" : busy == "join" ? "JOINING…" : "JOIN ROOM");
            inviteJoin.SetOff(connecting || busy != null || App.Resuming);
            if (invited)
                for (var i = 0; i < 4; i++) inviteCaps[i].SetLabel(i < App.InviteCode.Length ? App.InviteCode[i].ToString() : "");
            Layout();
        }

        public override void Layout()
        {
            float W = App.W, H = App.H;
            var col = Mathf.Min(W - 32f, 440f);
            var x0 = (W - col) / 2f;
            var invited = App.InviteCode != null;
            mute.Place(x0 + col - 44f, 10f);

            var cap = App.Wide ? 60f : 52f;
            var row1 = 5 * cap + 4 * 6f;
            var lx = (W - row1) / 2f;
            var y = App.Wide ? 46f : 66f;
            for (var i = 0; i < 9; i++)
            {
                var rowIndex = i < 5 ? 0 : 1;
                var index = i < 5 ? i : i - 5;
                var x = lx + index * (cap + 6f) + (rowIndex == 1 ? cap / 2f + 3f : 0f);
                var slot = logoSlots[i];
                slot.anchorMin = slot.anchorMax = new Vector2(0f, 1f);
                slot.pivot = new Vector2(0.5f, 0.5f);
                slot.sizeDelta = new Vector2(cap, cap + 5f);
                slot.anchoredPosition = new Vector2(x + cap / 2f, -(y + rowIndex * (cap + 12f) + (cap + 5f) / 2f));
                logo[i].Label.fontSize = Mathf.RoundToInt(cap * 0.58f);
                logo[i].SetSize(cap, cap);
                Ui.Place(logo[i].Root, 0f, 0f, cap, cap + 5f);
            }
            y += 2 * cap + 22f + 18f;

            var tw = Mathf.Min(col, 300f);
            var th = Ui.Height(tagline, tw);
            Ui.Place(tagline, (W - tw) / 2f, y, tw, th + 4f);
            y += th + 20f;

            notice.gameObject.SetActive(!string.IsNullOrEmpty(App.Notice));
            if (notice.gameObject.activeSelf)
            {
                noticeText.text = App.Notice;
                Ui.Place(notice, x0, y, col, 52f);
                Ui.Place(noticeText, 18f, 0f, col - 64f, 52f);
                Ui.Place((RectTransform)notice.Find("Close"), col - 46f, 4f, 44f, 44f);
                y += 52f + 16f;
            }

            // Panel contents, laid out top to bottom.
            const float pad = 20f;
            var iw = col - pad * 2f;
            var py = pad;
            inviteHeader.gameObject.SetActive(invited);
            for (var i = 0; i < 4; i++) inviteCaps[i].Root.gameObject.SetActive(invited);
            if (invited)
            {
                Ui.Place(inviteHeader, pad, py, iw, 22f);
                py += 30f;
                var cw = 44f;
                var total = 4 * cw + 3 * 8f;
                for (var i = 0; i < 4; i++)
                {
                    inviteCaps[i].SetSize(cw, 48f);
                    Ui.Place(inviteCaps[i].Root, (col - total) / 2f + i * (cw + 8f), py, cw, 53f);
                }
                py += 53f + 18f;
            }
            Ui.Place(nameLabel, pad, py, iw, 16f);
            py += 24f;
            Ui.Place(name.Frame.Root, pad, py, iw, 56f);
            py += 56f;
            nameError.gameObject.SetActive(!string.IsNullOrEmpty(nameError.text));
            if (nameError.gameObject.activeSelf)
            {
                Ui.Place(nameError, pad, py + 6f, iw, 20f);
                py += 28f;
            }
            py += 16f;

            create.Root.gameObject.SetActive(!invited);
            dividerLeft.gameObject.SetActive(!invited);
            dividerRight.gameObject.SetActive(!invited);
            dividerText.gameObject.SetActive(!invited);
            code.Frame.Root.gameObject.SetActive(!invited);
            join.Root.gameObject.SetActive(!invited);
            codeError.gameObject.SetActive(!invited && !string.IsNullOrEmpty(codeError.text));
            inviteJoin.Root.gameObject.SetActive(invited);
            switchLink.gameObject.SetActive(invited);
            if (invited)
            {
                inviteJoin.SetSize(iw, 60f);
                Ui.Place(inviteJoin.Root, pad, py, iw, 66f);
                py += 66f + 10f;
                Ui.Place(switchLink, pad, py, iw, 40f);
                py += 40f;
            }
            else
            {
                create.SetSize(iw, 60f);
                Ui.Place(create.Root, pad, py, iw, 66f);
                py += 66f + 20f;
                var dw = Ui.Width(dividerText) + 24f;
                Ui.Place(dividerText, (col - dw) / 2f, py, dw, 16f);
                Ui.Place(dividerLeft, pad, py + 8f, (iw - dw) / 2f, 1f);
                Ui.Place(dividerRight, (col + dw) / 2f, py + 8f, (iw - dw) / 2f, 1f);
                py += 16f + 20f;
                var bw = 104f;
                Ui.Place(code.Frame.Root, pad, py, iw - bw - 12f, 56f);
                join.SetSize(bw, 56f);
                Ui.Place(join.Root, pad + iw - bw, py, bw, 62f);
                py += 62f;
                if (codeError.gameObject.activeSelf)
                {
                    Ui.Place(codeError, pad, py + 6f, iw, 20f);
                    py += 28f;
                }
            }
            py += pad;
            Ui.Place(panel.Root, x0, y, col, py);
            y += py + 14f;

            var showSlow = !App.Connected && App.ConnectingFor > 6f;
            slowLine.gameObject.SetActive(showSlow);
            if (showSlow)
            {
                var sh = Ui.Height(slowLine, col);
                Ui.Place(slowLine, x0, y, col, sh + 4f);
                y += sh + 12f;
            }
            y += 14f;

            Ui.Place(howTitle, x0, y, col, 16f);
            y += 28f;
            for (var i = 0; i < 4; i++)
            {
                var rw = col - 46f;
                var rh = Mathf.Max(35f, Ui.Height(rules[i], rw) + 4f);
                bullets[i].SetSize(32f, 32f);
                Ui.Place(bullets[i].Root, x0, y + (rh - 35f) / 2f, 32f, 35f);
                Ui.Place(rules[i], x0 + 46f, y, rw, rh);
                y += rh + 10f;
            }
            y += 8f;
            var tile = 34f;
            var widthMini = 3 * tile + 2 * 4f + tile * 0.45f + 3 * tile + 2 * 4f;
            Ui.Place(mini, (W - widthMini) / 2f, y, widthMini, tile * 1.15f + 3f);
            var mx = 0f;
            var colors = new[] { Sk.Hex(Sk.PlayerHex[0]), Sk.Hex(Sk.PlayerHex[1]), Sk.Hex(Sk.PlayerHex[0]) };
            for (var i = 0; i < miniTiles.Count; i++)
            {
                var t = miniTiles[i];
                if (t == null) { mx += tile * 0.45f - 4f; continue; }
                if (i < 3) t.Set(TileView.Kind.Typed, colors[i]);
                else if (i == 4) t.Set(TileView.Kind.Cursor, Color.white);
                else t.Set(TileView.Kind.CurrentWord, Color.white);
                t.Place(mx, 0f);
                mx += tile + 4f;
            }
            y += tile * 1.15f + 3f + 28f;
            Ui.Place(footer, x0, y, col, 20f);
            y += 20f + 32f;
            page.SetSize(W, Mathf.Max(y, H));
        }

        public override void Tick()
        {
            name.Tick();
            code.Tick();
            mute.Refresh(App.Audio.Muted);
            var phase = Time.unscaledTime % 3f;
            for (var i = 0; i < 9; i++)
            {
                var start = 0.4f + i * 0.09f;
                var down = !App.ReducedMotion && phase >= start && phase < start + 0.12f;
                if (down != (logo[i].Body.anchoredPosition.y < -0.5f)) logo[i].SetDown(down);
            }
            if (Mathf.FloorToInt(App.ConnectingFor) == 6 && !App.Connected && !slowLine.gameObject.activeSelf) Layout();
            if (miniTiles.Count > 4 && miniTiles[4] != null) miniTiles[4].Blink(Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f);
        }
    }

    public sealed class LobbyScreen : SkScreen
    {
        readonly ScrollPage page;
        readonly TopBar bar;
        readonly Text codeLabel;
        readonly Keycap[] codeCaps = new Keycap[4];
        readonly Keycap invite;
        readonly Text link;
        readonly RectTransform best;
        readonly Text bestText;
        readonly Text playersLabel;
        readonly Text playersCount;
        readonly List<PlayerRow> rows = new List<PlayerRow>();
        readonly RectTransform slot;
        readonly Framed slotFrame;
        readonly Text slotText;
        readonly Text yourColor;
        readonly DevPanel dev;
        readonly RectTransform bottom;
        readonly RawImage fade;
        readonly Keycap start;
        readonly WaitingPill waiting;
        bool wasReady;
        int lastCount;

        public LobbyScreen(SplitKeysApp app) : base(app, "Lobby")
        {
            page = ScrollPage.Create(Root, "Page");
            var c = page.Content;
            bar = TopBar.Create(c, app.Leave, app.ToggleMute);
            codeLabel = Ui.Label(c, "ROOM CODE", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleCenter);
            for (var i = 0; i < 4; i++)
            {
                codeCaps[i] = Keycap.Create(c, "", Sk.Display, 36, Sk.Slate, Sk.Text, 5f, 14f);
                codeCaps[i].SetColors(Sk.Slate, Sk.Text, 0.12f);
                codeCaps[i].OnClick = () => SkBridge.CopyText(App.State?.code ?? "", "Code copied!");
            }
            invite = Keycap.Create(c, "INVITE FRIENDS", Sk.Display, 16, Sk.Slate, Sk.Text, 6f, 14f);
            invite.SetColors(Sk.Slate, Sk.Text, 0.12f);
            invite.SetGlyph(SkGfx.Share, Sk.Text, 18f);
            invite.OnClick = app.Invite;
            link = Ui.Label(c, "", Sk.Body, 13, Sk.Dim, TextAnchor.MiddleCenter);
            best = Ui.Node("Best", c);
            var bestBg = Ui.Box(best, Sk.Mat2, 15f);
            Ui.Stretch(bestBg.rectTransform);
            bestText = Ui.Label(best, "", Sk.Bold, 14, Sk.Text, TextAnchor.MiddleCenter);
            Ui.Stretch(bestText.rectTransform);
            playersLabel = Ui.Label(c, "PLAYERS", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft);
            playersCount = Ui.Label(c, "", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight);
            slot = Ui.Node("Slot", c);
            slotFrame = Ui.Frame(slot, Sk.Line, Sk.Bg, 14f, 2f);
            Ui.Stretch(slotFrame.Root);
            slotText = Ui.Label(slot, "+ Invite more players (up to 8)", Sk.Body, 15, Sk.Muted, TextAnchor.MiddleCenter);
            Ui.Stretch(slotText.rectTransform);
            Tap.On(slot, app.Invite);
            yourColor = Ui.Label(c, "", Sk.Body, 14, Sk.Muted, TextAnchor.MiddleLeft);
            yourColor.supportRichText = true;
            dev = DevPanel.Create(c);

            bottom = Ui.Node("Bottom", Root);
            fade = Ui.Node("Fade", bottom).gameObject.AddComponent<RawImage>();
            fade.texture = SplitKeysApp.FadeTexture;
            fade.color = Sk.Bg;
            fade.raycastTarget = false;
            start = Keycap.Create(bottom, "START GAME", Sk.Display, 18, Sk.Cream, Sk.Ink, 6f, 14f);
            start.OnClick = App.StartGame;
            waiting = WaitingPill.Create(bottom);
        }

        public override void OnShow()
        {
            page.Content.anchoredPosition = Vector2.zero;
            lastCount = App.State?.players?.Length ?? 0;
            wasReady = false;
        }

        public override void Render()
        {
            var s = App.State;
            if (s == null) return;
            var count = s.players?.Length ?? 0;
            if (count > lastCount) App.Audio.Join();
            lastCount = count;
            Layout();
        }

        public override void Layout()
        {
            var s = App.State;
            if (s == null || s.players == null) return;
            float W = App.W, H = App.H;
            var col = Mathf.Min(W - 32f, 520f);
            var x0 = (W - col) / 2f;
            bar.Layout(x0, col, 8f);
            var y = 64f;
            Ui.Place(codeLabel, x0, y, col, 16f);
            y += 26f;
            const float cw = 60f, ch = 68f;
            var total = 4 * cw + 3 * 8f;
            for (var i = 0; i < 4; i++)
            {
                codeCaps[i].SetLabel(s.code != null && i < s.code.Length ? s.code[i].ToString() : "");
                codeCaps[i].SetSize(cw, ch);
                Ui.Place(codeCaps[i].Root, (W - total) / 2f + i * (cw + 8f), y, cw, ch + 5f);
            }
            y += ch + 5f + 20f;
            const float iw = 250f;
            invite.SetSize(iw, 52f);
            Ui.Place(invite.Root, (W - iw) / 2f, y, iw, 58f);
            invite.Label.alignment = TextAnchor.MiddleCenter;
            Ui.Stretch(invite.Label.rectTransform, 26f, 0f, 0f, 0f);
            invite.Glyph.rectTransform.anchoredPosition = new Vector2(-iw / 2f + 30f, 0f);
            y += 58f + 10f;
            link.text = SkBridge.InviteUrl(s.code ?? "").Replace("https://", "").Replace("http://", "");
            Ui.Place(link, x0, y, col, 18f);
            y += 18f + 14f;

            best.gameObject.SetActive(s.bestLevel > 0);
            if (s.bestLevel > 0)
            {
                bestText.text = "Room best: " + Ui.Plural(s.bestLevel, "level", "levels");
                var bw = Ui.Width(bestText) + 32f;
                Ui.Place(best, (W - bw) / 2f, y, bw, 30f);
                y += 30f + 16f;
            }

            Ui.Place(playersLabel, x0, y, col / 2f, 16f);
            playersCount.text = s.players.Length + "/8";
            Ui.Place(playersCount, x0 + col / 2f, y, col / 2f, 16f);
            y += 26f;
            while (rows.Count < s.players.Length) rows.Add(PlayerRow.Create(page.Content));
            for (var i = 0; i < rows.Count; i++)
            {
                var active = i < s.players.Length;
                rows[i].Root.gameObject.SetActive(active);
                if (!active) continue;
                var player = s.players[i];
                rows[i].Set(player, player.id == App.SelfId, player.id == s.hostId, col);
                Ui.Place(rows[i].Root, x0, y, col, 60f);
                y += 68f;
            }
            slot.gameObject.SetActive(s.players.Length < 8);
            if (slot.gameObject.activeSelf)
            {
                Ui.Place(slot, x0, y, col, 60f);
                y += 68f;
            }
            var self = s.Player(App.SelfId);
            yourColor.text = self == null ? "" : "You're " + Colored(Sk.ColorName(self.color), self.color);
            Ui.Place(yourColor, x0, y, col, 20f);
            y += 34f;

            var isHost = s.hostId == App.SelfId;
            dev.Root.gameObject.SetActive(s.dev && isHost);
            if (dev.Root.gameObject.activeSelf)
            {
                dev.Place(x0, y, col, s);
                y += DevPanel.Height + 16f;
            }

            const float bottomH = 100f;
            Ui.Place(bottom, 0f, H - bottomH, W, bottomH);
            Ui.Place(fade, 0f, 0f, W, bottomH);
            var ready = s.ConnectedCount() >= (s.dev ? 1 : 2);
            start.Root.gameObject.SetActive(isHost);
            waiting.Root.gameObject.SetActive(!isHost);
            if (isHost)
            {
                start.SetLabel(ready ? "START GAME" : "NEED 2+ PLAYERS");
                start.SetOff(!ready);
                start.SetSize(col, 60f);
                Ui.Place(start.Root, x0, bottomH - 16f - 66f, col, 66f);
                if (ready && !wasReady && !App.ReducedMotion)
                {
                    var face = start.Face;
                    Tween.Run(face, 0.5f, t => face.color = Color.Lerp(Color.white, Sk.Cream, t));
                }
                wasReady = ready;
            }
            else
            {
                var host = s.Player(s.hostId);
                waiting.Set("Waiting for " + (host == null ? "the host" : Colored(host.name, host.color)) + " to start");
                waiting.Place(x0, bottomH - 16f - 52f, col);
            }
            page.SetSize(W, Mathf.Max(y + bottomH, H));
        }

        public override void Tick()
        {
            bar.Mute.Refresh(App.Audio.Muted);
            if (waiting.Root.gameObject.activeSelf) waiting.Tick();
            var pulse = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI));
            slotFrame.Outer.color = Sk.Alpha(Sk.Line, pulse);
        }
    }

    public sealed class GameOverScreen : SkScreen
    {
        readonly ScrollPage page;
        readonly TopBar bar;
        readonly Text over;
        readonly Keycap badge;
        readonly RectTransform ribbon;
        readonly Text cleared;
        readonly Text bestLine;
        readonly RectTransform awards;
        readonly RectTransform stats;
        readonly Text inviteMore;
        readonly DevPanel dev;
        readonly RectTransform bottom;
        readonly RawImage fade;
        readonly Keycap again;
        readonly WaitingPill waiting;
        readonly List<RectTransform> awardCards = new List<RectTransform>();
        bool animate;

        public GameOverScreen(SplitKeysApp app) : base(app, "GameOver")
        {
            page = ScrollPage.Create(Root, "Page");
            var c = page.Content;
            bar = TopBar.Create(c, app.Leave, app.ToggleMute);
            over = Ui.Label(c, "GAME OVER", Sk.Bold, 13, Sk.Muted, TextAnchor.MiddleCenter);
            badge = Keycap.Create(c, "0", Sk.Display, 64, Sk.Cream, Sk.Ink, 6f, 22f);
            badge.Interactive = false;
            ribbon = Ui.Node("Ribbon", c);
            var ribbonBg = Ui.Box(ribbon, Sk.Cream, 4f);
            Ui.Stretch(ribbonBg.rectTransform);
            var ribbonText = Ui.Label(ribbon, "NEW ROOM BEST!", Sk.Display, 13, Sk.Ink, TextAnchor.MiddleCenter);
            Ui.Stretch(ribbonText.rectTransform);
            ribbon.localEulerAngles = new Vector3(0f, 0f, -8f);
            cleared = Ui.Label(c, "LEVELS CLEARED", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleCenter);
            bestLine = Ui.Label(c, "", Sk.Body, 15, Sk.Muted, TextAnchor.UpperCenter);
            awards = Ui.Node("Awards", c);
            stats = Ui.Node("Stats", c);
            inviteMore = Ui.Label(c, "Invite more players", Sk.Bold, 15, Sk.Muted, TextAnchor.MiddleCenter);
            Tap.On(inviteMore.rectTransform, app.Invite);
            dev = DevPanel.Create(c);
            bottom = Ui.Node("Bottom", Root);
            fade = Ui.Node("Fade", bottom).gameObject.AddComponent<RawImage>();
            fade.texture = SplitKeysApp.FadeTexture;
            fade.color = Sk.Bg;
            fade.raycastTarget = false;
            again = Keycap.Create(bottom, "PLAY AGAIN", Sk.Display, 18, Sk.Cream, Sk.Ink, 6f, 14f);
            again.OnClick = app.StartGame;
            waiting = WaitingPill.Create(bottom);
        }

        public override void OnShow()
        {
            page.Content.anchoredPosition = Vector2.zero;
            animate = true;
            var s = App.State;
            var levels = s == null ? 0 : Mathf.Max(0, s.level - 1);
            App.Audio.Fanfare(levels > 0);
        }

        public override void Render() => Layout();

        public override void Layout()
        {
            var s = App.State;
            if (s == null || s.players == null) return;
            float W = App.W, H = App.H;
            var col = Mathf.Min(W - 32f, 560f);
            var x0 = (W - col) / 2f;
            bar.Layout(x0, col, 8f);
            var y = 64f;
            Ui.Place(over, x0, y, col, 18f);
            y += 30f;
            var levels = Mathf.Max(0, s.level - 1);
            badge.SetLabel(levels.ToString());
            badge.SetSize(132f, 132f);
            var badgeX = (W - 132f) / 2f;
            Ui.Place(badge.Root, badgeX, y, 132f, 138f);
            var isNewBest = App.BestAtRunStart >= 0 && levels > App.BestAtRunStart && levels > 0;
            ribbon.gameObject.SetActive(isNewBest);
            ribbon.anchorMin = ribbon.anchorMax = new Vector2(0f, 1f);
            ribbon.pivot = new Vector2(0.5f, 0.5f);
            ribbon.sizeDelta = new Vector2(150f, 26f);
            ribbon.anchoredPosition = new Vector2(badgeX + 132f, -(y + 14f));
            if (animate && !App.ReducedMotion)
            {
                var rt = badge.Root;
                Tween.Run(rt, 0.45f, t => { var k = Mathf.LerpUnclamped(0.6f, 1f, Tween.OutBack(t)); rt.localScale = new Vector3(k, k, 1f); });
            }
            y += 138f + 10f;
            Ui.Place(cleared, x0, y, col, 16f);
            y += 26f;
            if (levels == 0) bestLine.text = "No levels cleared. Run it back!";
            else if (isNewBest) bestLine.text = "";
            else bestLine.text = "Room best: " + Ui.Plural(s.bestLevel, "level", "levels");
            var bh = string.IsNullOrEmpty(bestLine.text) ? 0f : Ui.Height(bestLine, col) + 4f;
            Ui.Place(bestLine, x0, y, col, bh);
            y += bh + 18f;

            y = BuildAwards(s, x0, y, col);
            y = BuildStats(s, x0, y, col);
            Ui.Place(inviteMore, x0, y, col, 44f);
            y += 52f;
            var isHost = s.hostId == App.SelfId;
            dev.Root.gameObject.SetActive(s.dev && isHost);
            if (dev.Root.gameObject.activeSelf)
            {
                dev.Place(x0, y, col, s);
                y += DevPanel.Height + 16f;
            }

            const float bottomH = 100f;
            Ui.Place(bottom, 0f, H - bottomH, W, bottomH);
            Ui.Place(fade, 0f, 0f, W, bottomH);
            again.Root.gameObject.SetActive(isHost);
            waiting.Root.gameObject.SetActive(!isHost);
            if (isHost)
            {
                again.SetSize(col, 60f);
                Ui.Place(again.Root, x0, bottomH - 16f - 66f, col, 66f);
            }
            else
            {
                var host = s.Player(s.hostId);
                waiting.Set("Waiting for " + (host == null ? "the host" : Colored(host.name, host.color)) + " to play again");
                waiting.Place(x0, bottomH - 16f - 52f, col);
            }
            page.SetSize(W, Mathf.Max(y + bottomH, H));
            animate = false;
        }

        float BuildAwards(RoomState s, float x0, float y, float col)
        {
            Ui.Clear(awards);
            awardCards.Clear();
            Ui.Place(awards, 0f, 0f, 0f, 0f);
            var defs = new (Sprite icon, string title, Func<PlayerState, int> score, string one, string many)[]
            {
                (SkGfx.Bolt, "SPEED DEMON", p => p.stats?.correct ?? 0, "letter", "letters"),
                (SkGfx.Target, "CLUTCH", p => p.stats?.finals ?? 0, "last letter", "last letters"),
                (SkGfx.Burst, "BUTTERFINGERS", p => p.stats?.mistakes ?? 0, "oops", "oops"),
            };
            var cards = new List<(Sprite, string, List<PlayerState>, int, string, string)>();
            foreach (var def in defs)
            {
                var max = 0;
                foreach (var p in s.players) max = Mathf.Max(max, def.score(p));
                if (max == 0) continue;
                var winners = new List<PlayerState>();
                foreach (var p in s.players) if (def.score(p) == max) winners.Add(p);
                cards.Add((def.icon, def.title, winners, max, def.one, def.many));
            }
            if (cards.Count == 0) return y;
            var wide = App.Wide && cards.Count > 1;
            var cardW = wide ? (col - (cards.Count - 1) * 12f) / cards.Count : col;
            var cardH = wide ? 132f : 76f;
            float[] tilts = { -1.5f, 1f, -0.5f };
            for (var i = 0; i < cards.Count; i++)
            {
                var (icon, title, winners, value, one, many) = cards[i];
                var color = winners.Count == 1 ? Sk.Hex(winners[0].color) : Sk.Cream;
                var card = Ui.Node("Award", awards);
                var holder = Ui.Node("Holder", card);
                var bg = Ui.Box(holder, Sk.Mat, 16f);
                Ui.Place(bg, 0f, 0f, cardW, cardH);
                var stripe = Ui.Box(holder, color, 2f);
                Ui.Place(stripe, 14f, 0f, cardW - 28f, 4f);
                var iconImage = Ui.Icon(holder, icon, color);
                var titleText = Ui.Label(holder, title, Sk.Display, 15, Sk.Text, wide ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
                var names = new List<string>();
                foreach (var w in winners) names.Add(Colored(w.name, w.color));
                var who = Ui.Label(holder, string.Join(" & ", names), Sk.Bold, 17, Sk.Text, wide ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
                who.supportRichText = true;
                var stat = Ui.Label(holder, Ui.Plural(value, one, many), Sk.Body, 14, Sk.Muted, wide ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
                if (wide)
                {
                    Ui.Place(iconImage, (cardW - 30f) / 2f, 14f, 30f, 30f);
                    Ui.Place(titleText, 8f, 50f, cardW - 16f, 22f);
                    Ui.Place(who, 8f, 74f, cardW - 16f, 24f);
                    Ui.Place(stat, 8f, 98f, cardW - 16f, 20f);
                }
                else
                {
                    Ui.Place(iconImage, 18f, (cardH - 30f) / 2f, 30f, 30f);
                    Ui.Place(titleText, 62f, 12f, cardW - 76f, 22f);
                    who.text = string.Join(" & ", names) + "  <color=#A19BBE>· " + Ui.Plural(value, one, many) + "</color>";
                    Ui.Place(who, 62f, 34f, cardW - 76f, 28f);
                    stat.gameObject.SetActive(false);
                }
                var cx = wide ? x0 + i * (cardW + 12f) : x0;
                var cy = wide ? y : y + i * (cardH + 10f);
                Ui.Place(card, cx, cy, cardW, cardH);
                holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
                holder.pivot = new Vector2(0.5f, 0.5f);
                holder.sizeDelta = new Vector2(cardW, cardH);
                holder.anchoredPosition = Vector2.zero;
                holder.localEulerAngles = new Vector3(0f, 0f, tilts[i % 3] * (wide ? 1f : 0.35f));
                if (animate && !App.ReducedMotion)
                {
                    var group = card.gameObject.AddComponent<CanvasGroup>();
                    var h = holder;
                    Tween.Run(card, 0.35f, t =>
                    {
                        var k = Tween.OutCubic(t);
                        group.alpha = k;
                        h.anchoredPosition = new Vector2(0f, -16f * (1f - k));
                    }, null, 0.25f + i * 0.1f);
                    group.alpha = 0f;
                }
                awardCards.Add(card);
            }
            return y + (wide ? cardH : cards.Count * (cardH + 10f) - 10f) + 22f;
        }

        float BuildStats(RoomState s, float x0, float y, float col)
        {
            Ui.Clear(stats);
            Ui.Place(stats, 0f, 0f, 0f, 0f);
            var sorted = new List<PlayerState>(s.players);
            sorted.Sort((a, b) => (b.stats?.correct ?? 0).CompareTo(a.stats?.correct ?? 0));
            var numW = 64f;
            var nameW = col - numW * 3f;
            Header(x0, y, nameW, numW);
            y += 30f;
            foreach (var p in sorted)
            {
                var self = p.id == App.SelfId;
                if (self)
                {
                    var hl = Ui.Box(stats, Sk.Mat2, 10f);
                    Ui.Place(hl, x0 - 6f, y, col + 12f, 44f);
                }
                var avatar = Keycap.Create(stats, string.IsNullOrEmpty(p.name) ? "?" : p.name.Substring(0, 1).ToUpperInvariant(), Sk.Display, 14, Sk.Hex(p.color), Sk.Ink, 3f, 8f, false);
                avatar.Interactive = false;
                avatar.SetSize(28f, 28f);
                Ui.Place(avatar.Root, x0, y + 7f, 28f, 31f);
                var name = Ui.Label(stats, p.name, Sk.Bold, 16, Sk.Hex(p.color), TextAnchor.MiddleLeft);
                Ui.Place(name, x0 + 38f, y, nameW - 40f, 44f);
                Cell((p.stats?.correct ?? 0).ToString(), x0 + nameW, y);
                Cell((p.stats?.finals ?? 0).ToString(), x0 + nameW + numW, y);
                Cell((p.stats?.mistakes ?? 0).ToString(), x0 + nameW + numW * 2f, y);
                var divider = Ui.Box(stats, Sk.Line, 0f);
                Ui.Place(divider, x0, y + 44f, col, 1f);
                y += 45f;
            }
            return y + 18f;

            void Header(float hx, float hy, float nw, float w)
            {
                Ui.Place(Ui.Label(stats, "PLAYER", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft), hx, hy, nw, 24f);
                Ui.Place(Ui.Label(stats, "LETTERS", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight), hx + nw, hy, w, 24f);
                Ui.Place(Ui.Label(stats, "CLUTCH", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight), hx + nw + w, hy, w, 24f);
                Ui.Place(Ui.Label(stats, "OOPS", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight), hx + nw + w * 2f, hy, w, 24f);
            }

            void Cell(string value, float cx, float cy)
            {
                Ui.Place(Ui.Label(stats, value, Sk.Bold, 16, Sk.Text, TextAnchor.MiddleRight), cx, cy, numW, 44f);
            }
        }

        public override void Tick()
        {
            bar.Mute.Refresh(App.Audio.Muted);
            if (waiting.Root.gameObject.activeSelf) waiting.Tick();
        }
    }
}
