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
        readonly ScrollPage page;
        readonly MuteButton mute;
        readonly RectTransform logo;
        readonly Text logoTop;
        readonly Text logoBottom;
        readonly Text tagline;
        readonly RectTransform notice;
        readonly Text noticeText;
        readonly Card panel;
        readonly Text inviteHeader;
        readonly Keycap[] inviteCaps = new Keycap[4];
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
        readonly Card howCard;
        readonly Text howTitle;
        readonly Keycap[] bullets = new Keycap[4];
        readonly Text[] rules = new Text[4];
        readonly Keycap[] levels = new Keycap[3];
        readonly Text footer;

        string busy;

        public HomeScreen(SplitKeysApp app) : base(app, "Home")
        {
            page = ScrollPage.Create(Root, "Page");
            var c = page.Content;
            mute = MuteButton.Create(c, app.ToggleMute);
            logo = Ui.Node("Logo", c);
            var words = SplitKeysApp.GameName.Split(' ');
            logoTop = Sk.Punch(Ui.Label(logo, words[0], Sk.Display, 76, Sk.White, TextAnchor.MiddleCenter, "Top"), 4f, 7f);
            logoBottom = Sk.Punch(Ui.Label(logo, words.Length > 1 ? words[1] : "", Sk.Display, 76, Sk.Cream, TextAnchor.MiddleCenter, "Bottom"), 4f, 7f);
            logoTop.horizontalOverflow = logoBottom.horizontalOverflow = HorizontalWrapMode.Overflow;
            tagline = Sk.Punch(Ui.Label(c, "Pick a word: easy, medium or hard. Draw it. Your teammate guesses. Riskier words score more.", Sk.Bold, 17, Sk.White, TextAnchor.UpperCenter), 0f, 2f);

            notice = Ui.Node("Notice", c);
            var noticeEdge = Ui.Box(notice, Sk.Ink, 14.5f);
            Ui.Stretch(noticeEdge.rectTransform, -2.5f, -2.5f, -2.5f, -2.5f);
            var noticeBg = Ui.Box(notice, Sk.White, 12f);
            Ui.Stretch(noticeBg.rectTransform);
            var noticeBar = Ui.Box(notice, Sk.Danger, 2f);
            Ui.Place(noticeBar, 0f, 0f, 6f, 52f);
            noticeText = Ui.Label(notice, "", Sk.Bold, 15, Sk.Ink, TextAnchor.MiddleLeft);
            var close = Ui.Label(notice, "×", Sk.Bold, 24, Sk.Ink, TextAnchor.MiddleCenter, "Close");
            Tap.On(close.rectTransform, () => { App.Notice = null; Render(); });
            close.name = "Close";

            panel = Card.Create(c, Sk.White, 20f, "Panel");
            var p = panel.Root;
            inviteHeader = Ui.Label(p, "You're invited to", Sk.Bold, 16, Sk.Muted, TextAnchor.MiddleCenter);
            for (var i = 0; i < 4; i++)
            {
                inviteCaps[i] = Keycap.Create(p, "", Sk.Display, 26, Sk.Sky, Sk.White, 4f, 10f);
                inviteCaps[i].Interactive = false;
            }
            name = InputBox.Create(p, "Your name", Sk.Bold, 18, 12, TextAnchor.MiddleCenter);
            name.Field.text = PlayerPrefs.GetString("splitkeys.name", "");
            name.Field.onValueChanged.AddListener(_ => { name.Error = false; nameError.text = ""; Layout(); });
            name.Field.onEndEdit.AddListener(_ => { if (EnterPressed()) { if (App.InviteCode != null) JoinInvite(); else Create(); } });
            nameError = Ui.Label(p, "", Sk.Bold, 14, Sk.Danger, TextAnchor.MiddleCenter);
            create = Keycap.Create(p, "CREATE ROOM", Sk.Display, 24, Sk.Cream, Sk.Ink, 6f, 12f);
            create.OnClick = Create;
            dividerLeft = Ui.Box(p, Sk.Ink, 0f);
            dividerRight = Ui.Box(p, Sk.Ink, 0f);
            dividerText = Ui.Label(p, "OR JOIN A ROOM", Sk.Display, 15, Sk.Ink, TextAnchor.MiddleCenter);
            code = InputBox.Create(p, "CODE", Sk.Display, 26, 4, TextAnchor.MiddleCenter);
            code.Field.onValidateInput = (text, index, ch) =>
            {
                ch = char.ToUpperInvariant(ch);
                return ch >= 'A' && ch <= 'Z' ? ch : '\0';
            };
            code.Field.onValueChanged.AddListener(_ => { code.Error = false; codeError.text = ""; Layout(); });
            code.Field.onEndEdit.AddListener(_ => { if (EnterPressed()) Join(); });
            join = Keycap.Create(p, "JOIN", Sk.Display, 22, Sk.Sky, Sk.White, 6f, 12f);
            join.OnClick = Join;
            codeError = Ui.Label(p, "", Sk.Bold, 14, Sk.Danger, TextAnchor.MiddleLeft);
            inviteJoin = Keycap.Create(p, "JOIN ROOM", Sk.Display, 24, Sk.Cream, Sk.Ink, 6f, 12f);
            inviteJoin.OnClick = JoinInvite;
            switchLink = Ui.Label(p, "Or create a new room", Sk.Bold, 15, Sk.Muted, TextAnchor.MiddleCenter);
            Tap.On(switchLink.rectTransform, () => { App.ClearInvite(); Render(); });

            slowLine = Sk.Punch(Ui.Label(c, "Waking up the server. The first visit can take up to a minute.", Sk.Bold, 15, Sk.White, TextAnchor.UpperCenter), 0f, 2f);
            howCard = Card.Create(c, Sk.White, 20f, "HowToPlay");
            var h = howCard.Root;
            howTitle = Ui.Label(h, "HOW TO PLAY", Sk.Display, 24, Sk.Ink, TextAnchor.MiddleLeft);
            var bulletColors = new[] { Sk.Sky, Sk.Cream, Sk.Tangerine, Sk.Danger };
            for (var i = 0; i < 4; i++)
            {
                var light = Sk.IsLight(bulletColors[i]);
                bullets[i] = Keycap.Create(h, (i + 1).ToString(), Sk.Display, 18, bulletColors[i], light ? Sk.Ink : Sk.White, 3f, 8f, false);
                bullets[i].Interactive = false;
                rules[i] = Ui.Label(h, SplitKeysApp.Rules[i], Sk.Body, 16, Sk.Ink, TextAnchor.MiddleLeft);
            }
            var levelIds = new[] { "easy", "medium", "hard" };
            for (var i = 0; i < 3; i++)
            {
                levels[i] = Keycap.Create(h, GameScreen.LevelName(levelIds[i]), Sk.Display, 15, GameScreen.LevelColor(levelIds[i]), Sk.Ink, 3f, 8f, false);
                levels[i].Interactive = false;
            }
            footer = Sk.Punch(Ui.Label(c, "No app. No account. 4 players, 2 teams.", Sk.Bold, 14, Sk.White, TextAnchor.MiddleCenter), 0f, 2f);
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
            page.Scroll.JumpTo(0f);
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
            App.Backdrop(Sk.Sky, Sk.Tangerine, App.Wide ? 0.5f : 0.34f);
            var col = Mathf.Min(W - 32f, 440f);
            var x0 = (W - col) / 2f;
            var invited = App.InviteCode != null;
            mute.Place(x0 + col - 44f, 12f);

            // Wordmark: two chunky lines, tilted like a sticker.
            var y = App.Wide ? 26f : 44f;
            var size = App.Wide ? 80 : 68;
            logoTop.fontSize = logoBottom.fontSize = size;
            var lineH = size * 0.92f;
            Ui.Place(logo, 0f, y, W, lineH * 2f);
            logo.pivot = new Vector2(0.5f, 0.5f);
            logo.anchoredPosition = new Vector2(W / 2f, -(y + lineH));
            logo.localEulerAngles = new Vector3(0f, 0f, 4f);
            Ui.Place(logoTop, 0f, 0f, W, lineH);
            Ui.Place(logoBottom, 0f, lineH - 6f, W, lineH);
            y += lineH * 2f + 18f;

            var tw = Mathf.Min(col, 330f);
            var th = Ui.Height(tagline, tw);
            Ui.Place(tagline, (W - tw) / 2f, y, tw, th + 4f);
            y += th + 24f;

            notice.gameObject.SetActive(!string.IsNullOrEmpty(App.Notice));
            if (notice.gameObject.activeSelf)
            {
                noticeText.text = App.Notice;
                Ui.Place(notice, x0, y, col, 52f);
                Ui.Place(noticeText, 20f, 0f, col - 66f, 52f);
                Ui.Place((RectTransform)notice.Find("Close"), col - 46f, 4f, 44f, 44f);
                y += 52f + 20f;
            }

            // The card: name, then create or join.
            const float pad = 20f;
            var iw = col - pad * 2f;
            var py = pad;
            inviteHeader.gameObject.SetActive(invited);
            for (var i = 0; i < 4; i++) inviteCaps[i].Root.gameObject.SetActive(invited);
            Ui.Place(name.Frame.Root, pad, py, iw, 56f);
            py += 56f;
            nameError.gameObject.SetActive(!string.IsNullOrEmpty(nameError.text));
            if (nameError.gameObject.activeSelf)
            {
                Ui.Place(nameError, pad, py + 6f, iw, 20f);
                py += 28f;
            }
            py += 18f;

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
                Ui.Place(inviteHeader, pad, py, iw, 22f);
                py += 30f;
                const float cw = 48f;
                var total = 4 * cw + 3 * 8f;
                for (var i = 0; i < 4; i++)
                {
                    inviteCaps[i].SetSize(cw, 52f);
                    Ui.Place(inviteCaps[i].Root, (col - total) / 2f + i * (cw + 8f), py, cw, 56f);
                }
                py += 56f + 20f;
                inviteJoin.SetSize(iw, 62f);
                Ui.Place(inviteJoin.Root, pad, py, iw, 68f);
                py += 68f + 10f;
                Ui.Place(switchLink, pad, py, iw, 40f);
                py += 40f;
            }
            else
            {
                create.SetSize(iw, 62f);
                Ui.Place(create.Root, pad, py, iw, 68f);
                py += 68f + 22f;
                var dw = Ui.Width(dividerText) + 24f;
                Ui.Place(dividerText, (col - dw) / 2f, py, dw, 18f);
                Ui.Place(dividerLeft, pad, py + 8f, (iw - dw) / 2f, 2.5f);
                Ui.Place(dividerRight, (col + dw) / 2f, py + 8f, (iw - dw) / 2f, 2.5f);
                py += 18f + 20f;
                const float bw = 110f;
                Ui.Place(code.Frame.Root, pad, py, iw - bw - 12f, 58f);
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
            panel.Place(x0, y, col, py);
            y += py + 26f;

            var showSlow = !App.Connected && App.ConnectingFor > 6f;
            slowLine.gameObject.SetActive(showSlow);
            if (showSlow)
            {
                var sh = Ui.Height(slowLine, col);
                Ui.Place(slowLine, x0, y, col, sh + 4f);
                y += sh + 16f;
            }

            // How to play.
            var hy = 18f;
            Ui.Place(howTitle, pad, hy, iw, 30f);
            hy += 42f;
            for (var i = 0; i < 4; i++)
            {
                var rw = iw - 46f;
                var rh = Mathf.Max(34f, Ui.Height(rules[i], rw) + 4f);
                bullets[i].SetSize(32f, 32f);
                Ui.Place(bullets[i].Root, pad, hy + (rh - 35f) / 2f, 32f, 35f);
                Ui.Place(rules[i], pad + 46f, hy, rw, rh);
                hy += rh + 12f;
                if (i == 1)
                {
                    var lw = (iw - 46f - 16f) / 3f;
                    for (var k = 0; k < 3; k++)
                    {
                        levels[k].SetSize(lw, 30f);
                        Ui.Place(levels[k].Root, pad + 46f + k * (lw + 8f), hy, lw, 33f);
                    }
                    hy += 33f + 16f;
                }
            }
            hy += 6f;
            howCard.Place(x0, y, col, hy);
            y += hy + 26f;
            Ui.Place(footer, x0, y, col, 20f);
            y += 20f + 32f;
            page.SetSize(W, Mathf.Max(y, H));
        }

        public override void Tick()
        {
            name.Tick();
            code.Tick();
            mute.Refresh(App.Audio.Muted);
            if (Mathf.FloorToInt(App.ConnectingFor) == 6 && !App.Connected && !slowLine.gameObject.activeSelf) Layout();
        }
    }

    // A team's side of the lobby: a team-colored card with its two players standing in it.
    public sealed class TeamBox
    {
        public RectTransform Root;
        Card card;
        Text title;
        Text count;
        public Keycap Join;
        readonly Seat[] seats = new Seat[2];

        public static TeamBox Create(Transform parent)
        {
            var box = new TeamBox { Root = Ui.Node("TeamBox", parent) };
            box.card = Card.Create(box.Root, Sk.Sky, 20f);
            box.title = Sk.Punch(Ui.Label(box.Root, "", Sk.Display, 26, Sk.White, TextAnchor.MiddleLeft), 2f, 3f);
            box.title.horizontalOverflow = HorizontalWrapMode.Overflow;
            box.count = Sk.Punch(Ui.Label(box.Root, "", Sk.Display, 18, Sk.White, TextAnchor.MiddleRight), 1.5f, 2f);
            box.Join = Keycap.Create(box.Root, "JOIN", Sk.Display, 16, Sk.White, Sk.Ink, 4f, 10f);
            box.Join.OnClick = SkBridge.SwitchTeam;
            for (var i = 0; i < 2; i++) box.seats[i] = Seat.Create(box.Root);
            return box;
        }

        // Two players side by side, or stacked when the box is tall and narrow (beside the drawing pad).
        public static float HeightFor(float width, bool stacked = false) =>
            16f + 34f + 14f + (stacked ? 2f : 1f) * (AvatarSize(width, stacked) + Seat.PlateH + 22f) + (stacked ? 20f : 12f);

        static float AvatarSize(float width, bool stacked) => stacked ? Mathf.Min(150f, width - 70f) : Mathf.Min(118f, (width - 40f - 16f) / 2f - 8f);

        public float Set(RoomState s, TeamState team, string selfId, bool canJoin, float x, float y, float width, bool stacked = false)
        {
            var height = HeightFor(width, stacked);
            Ui.Place(Root, x, y, width, height);
            var color = Sk.Hex(team.color);
            card.Fill.color = color;
            card.Place(0f, 0f, width, height);
            // Narrow boxes shrink the title, then drop "TEAM", so it never runs into the count.
            var room = width - 40f - (canJoin ? 92f : 50f);
            title.fontSize = 26;
            title.text = "TEAM " + team.name.ToUpperInvariant();
            if (Ui.Width(title) > room) title.fontSize = 21;
            if (Ui.Width(title) > room) title.text = team.name.ToUpperInvariant();
            Ui.Place(title, 20f, 14f, room, 36f);
            var members = new List<PlayerState>();
            if (team.playerIds != null) foreach (var id in team.playerIds) { var p = s.Player(id); if (p != null) members.Add(p); }
            count.text = members.Count + "/2";
            Join.Root.gameObject.SetActive(canJoin);
            count.gameObject.SetActive(!canJoin);
            Ui.Place(count, width - 90f, 14f, 70f, 36f);
            if (canJoin)
            {
                Join.SetSize(84f, 34f);
                Ui.Place(Join.Root, width - 20f - 84f, 14f, 84f, 38f);
            }
            var size = AvatarSize(width, stacked);
            var seatW = stacked ? width - 40f : (width - 40f - 16f) / 2f;
            for (var i = 0; i < 2; i++)
            {
                var player = i < members.Count ? members[i] : null;
                var sx = stacked ? 20f : 20f + i * (seatW + 16f);
                var sy = stacked ? 64f + i * (size + Seat.PlateH + 30f) : 64f;
                seats[i].Set(player, player != null && player.id == selfId, player != null && player.id == s.hostId, color, sx, sy, seatW, size);
            }
            return height;
        }
    }

    // The host's game settings: draw time, hint pace, rounds and stealing.
    public sealed class SettingsPanel
    {
        sealed class Row
        {
            public string Key;
            public string[] Values;
            public Text Label;
            public Keycap[] Options;
        }

        static readonly (string label, string key, string[] values, string[] names)[] Defs =
        {
            ("DRAW TIME", "drawSeconds", new[] { "45", "60", "90", "120" }, new[] { "45S", "60S", "90S", "2 MIN" }),
            ("HINT LETTER EVERY", "hintSeconds", new[] { "0", "10", "15", "20" }, new[] { "OFF", "10S", "15S", "20S" }),
            ("ROUNDS", "rounds", new[] { "4", "6", "8", "10" }, new[] { "4", "6", "8", "10" }),
            ("STEALING", "stealing", new[] { "on", "off" }, new[] { "ON", "OFF" })
        };

        public RectTransform Root;
        Card card;
        Text title;
        Text note;
        readonly Row[] rows = new Row[Defs.Length];

        public static SettingsPanel Create(Transform parent)
        {
            var panel = new SettingsPanel { Root = Ui.Node("Settings", parent) };
            panel.card = Card.Create(panel.Root, Sk.White, 20f);
            panel.title = Ui.Label(panel.Root, "GAME SETTINGS", Sk.Display, 22, Sk.Ink, TextAnchor.MiddleLeft);
            panel.note = Ui.Label(panel.Root, "", Sk.Bold, 13, Sk.Muted, TextAnchor.MiddleRight);
            panel.note.supportRichText = true;
            for (var r = 0; r < Defs.Length; r++)
            {
                var def = Defs[r];
                var row = new Row { Key = def.key, Values = def.values, Options = new Keycap[def.values.Length] };
                row.Label = Ui.Label(panel.Root, def.label, Sk.Display, 14, Sk.Muted, TextAnchor.MiddleLeft);
                for (var i = 0; i < def.values.Length; i++)
                {
                    var value = def.values[i];
                    var key = def.key;
                    var cap = Keycap.Create(panel.Root, def.names[i], Sk.Display, 16, Sk.White, Sk.Ink, 4f, 9f);
                    cap.OnClick = () => SkBridge.UpdateSettings("{\"" + key + "\":" + (key == "stealing" ? "\"" + value + "\"" : value) + "}");
                    row.Options[i] = cap;
                }
                panel.rows[r] = row;
            }
            return panel;
        }

        static string Current(GameSettings settings, string key)
        {
            if (settings == null) return "";
            switch (key)
            {
                case "drawSeconds": return settings.drawSeconds.ToString();
                case "hintSeconds": return settings.hintSeconds.ToString();
                case "stealing": return settings.stealing;
                default: return settings.rounds.ToString();
            }
        }

        // Returns the height used.
        public float Layout(RoomState s, bool isHost, float x, float y, float width)
        {
            const float pad = 20f;
            const float gap = 8f;
            var inner = width - pad * 2f;
            Ui.Place(title, pad, 16f, inner * 0.6f, 30f);
            var host = s.Player(s.hostId);
            note.text = isHost ? "Tap to change" : "Set by " + (host == null ? "the host" : host.name.Replace("<", "‹").Replace(">", "›"));
            Ui.Place(note, pad + inner * 0.4f, 16f, inner * 0.6f, 30f);
            var rowY = 58f;
            foreach (var row in rows)
            {
                Ui.Place(row.Label, pad, rowY, inner, 18f);
                rowY += 24f;
                var current = Current(s.settings, row.Key);
                var ow = (inner - gap * (row.Options.Length - 1)) / row.Options.Length;
                for (var i = 0; i < row.Options.Length; i++)
                {
                    var cap = row.Options[i];
                    var selected = row.Values[i] == current;
                    cap.Interactive = isHost;
                    if (selected) cap.SetColors(Sk.Cream, Sk.Ink);
                    else if (isHost) cap.SetColors(Sk.White, Sk.Ink);
                    else cap.SetColors(Sk.Mat2, Sk.Dim);
                    cap.SetSize(ow, 36f);
                    Ui.Place(cap.Root, pad + i * (ow + gap), rowY, ow, 40f);
                }
                rowY += 40f + 14f;
            }
            var height = rowY + 4f;
            Ui.Place(Root, x, y, width, height);
            card.Place(0f, 0f, width, height);
            return height;
        }
    }

    public sealed class LobbyScreen : SkScreen
    {
        readonly ScrollPage page;
        readonly TopBar bar;
        readonly Card codeCard;
        readonly Text codeLabel;
        readonly Text codeText;
        readonly Keycap invite;
        readonly Text link;
        readonly AvatarPad pad;
        readonly TeamBox[] boxes = new TeamBox[2];
        readonly RectTransform vs;
        readonly Text hint;
        readonly SettingsPanel settings;
        readonly DevPanel dev;
        readonly RectTransform bottom;
        readonly RawImage fade;
        readonly Keycap start;
        readonly WaitingPill waiting;
        int lastCount;

        public LobbyScreen(SplitKeysApp app) : base(app, "Lobby")
        {
            page = ScrollPage.Create(Root, "Page");
            var c = page.Content;
            bar = TopBar.Create(c, app.Leave, app.ToggleMute);
            codeCard = Card.Create(c, Sk.White, 20f, "CodeCard");
            codeLabel = Ui.Label(codeCard.Root, "ROOM CODE", Sk.Display, 15, Sk.Muted, TextAnchor.MiddleCenter);
            codeText = Ui.Label(codeCard.Root, "", Sk.Display, 52, Sk.Ink, TextAnchor.MiddleCenter);
            codeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Tap.On(codeText.rectTransform, () => SkBridge.CopyText(App.State?.code ?? "", "Code copied!"));
            invite = Keycap.Create(codeCard.Root, "INVITE FRIENDS", Sk.Display, 20, Sk.Sky, Sk.White, 5f, 12f);
            invite.SetGlyph(SkGfx.Share, Sk.White, 20f);
            invite.OnClick = app.Invite;
            link = Ui.Label(codeCard.Root, "", Sk.Body, 13, Sk.Muted, TextAnchor.MiddleCenter);
            for (var i = 0; i < 2; i++) boxes[i] = TeamBox.Create(c);
            pad = AvatarPad.Create(c);
            vs = Ui.Node("VS", c);
            var vsEdge = Ui.Icon(vs, SkGfx.Disc, Sk.Ink, "Edge");
            Ui.Stretch(vsEdge.rectTransform, -3f, -3f, -3f, -7f);
            var vsFace = Ui.Icon(vs, SkGfx.Disc, Sk.Cream, "Face");
            Ui.Stretch(vsFace.rectTransform);
            var vsText = Ui.Label(vs, "VS", Sk.Display, 26, Sk.Ink, TextAnchor.MiddleCenter);
            Ui.Stretch(vsText.rectTransform, 0f, 2f, 0f, 0f);
            vs.localEulerAngles = new Vector3(0f, 0f, -8f);
            hint = Sk.Punch(Ui.Label(c, "Each round one teammate draws and the other guesses. You swap every round.", Sk.Bold, 15, Sk.White, TextAnchor.UpperCenter), 0f, 2f);
            settings = SettingsPanel.Create(c);
            dev = DevPanel.Create(c);
            bottom = Ui.Node("Bottom", Root);
            fade = Ui.Node("Fade", bottom).gameObject.AddComponent<RawImage>();
            fade.texture = SplitKeysApp.FadeTexture;
            fade.color = Sk.Alpha(Sk.Ink, 0.55f);
            fade.raycastTarget = false;
            start = Keycap.Create(bottom, "START GAME", Sk.Display, 26, Sk.Cream, Sk.Ink, 6f, 12f);
            start.OnClick = App.StartGame;
            waiting = WaitingPill.Create(bottom);
        }

        public override void OnShow()
        {
            page.Scroll.JumpTo(0f);
            lastCount = App.State?.players?.Length ?? 0;
        }

        // After leaving a room every avatar sheet is dropped, so the pad lets go of yours too.
        public void ForgetAvatars() => pad.Unbind();

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
            if (s == null || s.players == null || s.teams == null || s.teams.Length < 2) return;
            float W = App.W, H = App.H;
            App.Backdrop(Sk.Sky, Sk.Tangerine, 0.5f);
            var col = Mathf.Min(W - 32f, App.Wide ? 860f : 520f);
            var x0 = (W - col) / 2f;
            bar.Layout(x0, col, 8f);
            var y = 66f;

            // Room code card: the code, then the invite button and link.
            var codeW = Mathf.Min(col, 520f);
            var cx = (W - codeW) / 2f;
            codeText.text = string.Join(" ", (s.code ?? "").ToCharArray());
            Ui.Place(codeLabel, 0f, 14f, codeW, 20f);
            Ui.Place(codeText, 0f, 34f, codeW, 58f);
            const float iw = 260f;
            invite.SetSize(iw, 52f);
            Ui.Place(invite.Root, (codeW - iw) / 2f, 100f, iw, 57f);
            Ui.Stretch(invite.Label.rectTransform, 30f, 0f, 0f, 0f);
            invite.Glyph.rectTransform.anchoredPosition = new Vector2(-iw / 2f + 30f, 0f);
            link.text = SkBridge.InviteUrl(s.code ?? "").Replace("https://", "").Replace("http://", "");
            Ui.Place(link, 0f, 164f, codeW, 18f);
            codeCard.Place(cx, y, codeW, 192f);
            y += 192f + 28f;

            // Everyone draws their capsule while waiting. Wide screens put the pad between the two teams so
            // you watch both sides draw; phones put it on top with the teams (and a VS badge) below.
            var isHost = s.hostId == App.SelfId;
            var me = s.Player(App.SelfId);
            pad.Bind(App.SelfId, s.phase == "lobby" && App.Connected);
            var side = App.Wide;
            const float vsSize = 60f;
            if (side)
            {
                const float padW = 340f;
                var boxW = (col - padW - 40f) / 2f;
                var padH = pad.Layout(x0 + boxW + 20f, y, padW);
                var boxH = TeamBox.HeightFor(boxW, true);
                for (var i = 0; i < 2; i++)
                {
                    var team = s.teams[i];
                    var canJoin = me != null && me.team != team.id && (team.playerIds?.Length ?? 0) < 2;
                    boxes[i].Set(s, team, App.SelfId, canJoin, i == 0 ? x0 : x0 + boxW + padW + 40f, y, boxW, true);
                }
                vs.gameObject.SetActive(false);
                y += Mathf.Max(padH, boxH) + 28f;
            }
            else
            {
                y += pad.Layout(x0, y, col) + 28f;
                var boxH = TeamBox.HeightFor(col);
                for (var i = 0; i < 2; i++)
                {
                    var team = s.teams[i];
                    var canJoin = me != null && me.team != team.id && (team.playerIds?.Length ?? 0) < 2;
                    boxes[i].Set(s, team, App.SelfId, canJoin, x0, y + i * (boxH + 40f), col);
                }
                vs.gameObject.SetActive(true);
                Ui.Place(vs, (W - vsSize) / 2f, y + boxH + 20f - vsSize / 2f, vsSize, vsSize);
                vs.pivot = new Vector2(0.5f, 0.5f);
                vs.anchoredPosition += new Vector2(vsSize / 2f, -vsSize / 2f);
                y += 2f * boxH + 40f + 24f;
            }

            var hw = Mathf.Min(col, 420f);
            var hh = Ui.Height(hint, hw);
            Ui.Place(hint, (W - hw) / 2f, y, hw, hh + 4f);
            y += hh + 24f;

            var sw = Mathf.Min(col, 520f);
            y += settings.Layout(s, isHost, (W - sw) / 2f, y, sw) + 28f;
            dev.Root.gameObject.SetActive(s.dev && isHost);
            if (dev.Root.gameObject.activeSelf)
            {
                dev.Place((W - sw) / 2f, y, sw, s);
                dev.Add.SetOff(s.players.Length >= 4);
                y += DevPanel.Height + 24f;
            }

            const float bottomH = 104f;
            Ui.Place(bottom, 0f, H - bottomH, W, bottomH);
            Ui.Place(fade, 0f, 0f, W, bottomH);
            var ready = s.ConnectedOn(s.teams[0].id) == 2 && s.ConnectedOn(s.teams[1].id) == 2;
            start.Root.gameObject.SetActive(isHost);
            waiting.Root.gameObject.SetActive(!isHost);
            var startW = Mathf.Min(col, 460f);
            if (isHost)
            {
                start.SetLabel(ready ? "START GAME" : "NEED 2 PLAYERS PER TEAM");
                start.Label.fontSize = ready ? 26 : 19;
                start.SetOff(!ready);
                start.SetSize(startW, 62f);
                Ui.Place(start.Root, (W - startW) / 2f, bottomH - 18f - 68f, startW, 68f);
            }
            else
            {
                var host = s.Player(s.hostId);
                waiting.Set(ready
                    ? "Waiting for " + (host == null ? "the host" : Colored(host.name, host.color)) + " to start"
                    : "Waiting for 4 players");
                waiting.Place((W - startW) / 2f, bottomH - 18f - 58f, startW);
            }
            page.SetSize(W, Mathf.Max(y + bottomH, H));
        }

        public override void Tick()
        {
            bar.Mute.Refresh(App.Audio.Muted);
            if (waiting.Root.gameObject.activeSelf) waiting.Tick();
            pad.Tick();
        }
    }

    public sealed class GameOverScreen : SkScreen
    {
        readonly ScrollPage page;
        readonly TopBar bar;
        readonly Text over;
        readonly Text winner;
        readonly Card[] podiums = new Card[2];
        readonly Text[] scores = new Text[2];
        readonly Text[] scoreNames = new Text[2];
        readonly AvatarView[] standing = new AvatarView[4];
        readonly Image crown;
        readonly Card statsCard;
        readonly RectTransform stats;
        readonly Text inviteMore;
        readonly DevPanel dev;
        readonly RectTransform bottom;
        readonly RawImage fade;
        readonly Keycap again;
        readonly WaitingPill waiting;
        bool animate;

        public GameOverScreen(SplitKeysApp app) : base(app, "GameOver")
        {
            page = ScrollPage.Create(Root, "Page");
            var c = page.Content;
            bar = TopBar.Create(c, app.Leave, app.ToggleMute);
            over = Sk.Punch(Ui.Label(c, "GAME OVER", Sk.Display, 20, Sk.White, TextAnchor.MiddleCenter), 1.5f, 2f);
            winner = Sk.Punch(Ui.Label(c, "", Sk.Display, 54, Sk.White, TextAnchor.MiddleCenter), 3.5f, 6f);
            winner.horizontalOverflow = HorizontalWrapMode.Overflow;
            for (var i = 0; i < 4; i++) standing[i] = AvatarView.Create(c, false);
            for (var i = 0; i < 2; i++)
            {
                podiums[i] = Card.Create(c, Sk.Sky, 16f, "Podium");
                scores[i] = Sk.Punch(Ui.Label(podiums[i].Root, "0", Sk.Display, 52, Sk.White, TextAnchor.MiddleCenter), 3f, 4f);
                scoreNames[i] = Sk.Punch(Ui.Label(podiums[i].Root, "", Sk.Display, 18, Sk.White, TextAnchor.MiddleCenter), 1.5f, 2f);
            }
            crown = Ui.Icon(c, SkGfx.Crown, Sk.Cream, "Crown");
            statsCard = Card.Create(c, Sk.White, 20f, "Stats");
            stats = Ui.Node("StatsRows", statsCard.Root);
            inviteMore = Sk.Punch(Ui.Label(c, "Invite friends", Sk.Display, 18, Sk.White, TextAnchor.MiddleCenter), 1.5f, 2f);
            Tap.On(inviteMore.rectTransform, app.Invite);
            dev = DevPanel.Create(c);
            bottom = Ui.Node("Bottom", Root);
            fade = Ui.Node("Fade", bottom).gameObject.AddComponent<RawImage>();
            fade.texture = SplitKeysApp.FadeTexture;
            fade.color = Sk.Alpha(Sk.Ink, 0.55f);
            fade.raycastTarget = false;
            again = Keycap.Create(bottom, "PLAY AGAIN", Sk.Display, 26, Sk.Cream, Sk.Ink, 6f, 12f);
            again.OnClick = app.StartGame;
            waiting = WaitingPill.Create(bottom);
        }

        public override void OnShow()
        {
            page.Scroll.JumpTo(0f);
            animate = true;
            App.Audio.Fanfare(true);
        }

        public override void Render() => Layout();

        public override void Layout()
        {
            var s = App.State;
            if (s == null || s.players == null || s.teams == null || s.teams.Length < 2) return;
            float W = App.W, H = App.H;
            var col = Mathf.Min(W - 32f, 560f);
            var x0 = (W - col) / 2f;
            var a = s.teams[0];
            var b = s.teams[1];
            var tie = a.score == b.score;
            var top = a.score >= b.score ? 0 : 1;
            if (tie) App.Backdrop(Sk.Hex(a.color), Sk.Hex(b.color), 0.5f);
            else App.Backdrop(Sk.Hex(s.teams[top].color), Sk.Hex(s.teams[top].color));
            bar.Layout(x0, col, 8f);
            var y = 64f;
            Ui.Place(over, x0, y, col, 24f);
            y += 30f;
            winner.text = tie ? "IT'S A TIE!" : s.teams[top].name.ToUpperInvariant() + " WINS!";
            winner.fontSize = W < 380f ? 44 : 54;
            Ui.Place(winner, 0f, y, W, 64f);
            winner.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            winner.rectTransform.anchoredPosition = new Vector2(W / 2f, -(y + 32f));
            winner.rectTransform.localEulerAngles = new Vector3(0f, 0f, 3f);
            if (animate && !App.ReducedMotion)
            {
                var rt = winner.rectTransform;
                Tween.Run(rt, 0.45f, t => { var k = Mathf.LerpUnclamped(0.6f, 1f, Tween.OutBack(t)); rt.localScale = new Vector3(k, k, 1f); });
            }
            y += 84f;

            // Podiums: each team's players stand on a block showing their score; the winners' block is taller.
            var pw = Mathf.Min(200f, (col - 24f) / 2f);
            var gap = Mathf.Min(40f, col - 2f * pw);
            var startX = (W - (2f * pw + gap)) / 2f;
            var avatarSize = Mathf.Min(120f, pw * 0.62f);
            const float tall = 150f, low = 112f;
            var baseY = y + avatarSize + 6f + tall;
            for (var i = 0; i < 2; i++)
            {
                var team = s.teams[i];
                var px = startX + i * (pw + gap);
                var ph = tie || i == top ? tall : low;
                var py = baseY - ph;
                podiums[i].Fill.color = Sk.Hex(team.color);
                podiums[i].Place(px, py, pw, ph);
                scores[i].text = team.score.ToString();
                Ui.Place(scores[i], 0f, 12f, pw, 62f);
                scoreNames[i].text = team.name.ToUpperInvariant();
                Ui.Place(scoreNames[i], 0f, 74f, pw, 24f);
                var members = new List<PlayerState>();
                if (team.playerIds != null) foreach (var id in team.playerIds) { var p = s.Player(id); if (p != null) members.Add(p); }
                for (var k = 0; k < 2; k++)
                {
                    var view = standing[i * 2 + k];
                    var has = k < members.Count;
                    view.Root.gameObject.SetActive(has);
                    if (!has) continue;
                    view.Set(members[k].id);
                    // Capsules fill the middle of their square, so teammates' squares overlap to stand together.
                    var ax = px + pw / 2f + (members.Count == 1 ? -avatarSize / 2f : k == 0 ? -avatarSize * 0.82f : -avatarSize * 0.18f);
                    view.Place(ax, py - avatarSize + 8f, avatarSize);
                }
            }
            crown.gameObject.SetActive(!tie);
            if (!tie) Ui.Place(crown, startX + top * (pw + gap) + pw / 2f - 24f, baseY - tall - avatarSize - 26f, 48f, 36f);
            y = baseY + 36f;

            y = BuildStats(s, x0, y, col);
            Ui.Place(inviteMore, x0, y, col, 44f);
            y += 56f;
            var isHost = s.hostId == App.SelfId;
            dev.Root.gameObject.SetActive(s.dev && isHost);
            if (dev.Root.gameObject.activeSelf)
            {
                dev.Place(x0, y, col, s);
                dev.Add.SetOff(s.players.Length >= 4);
                y += DevPanel.Height + 20f;
            }
            const float bottomH = 104f;
            Ui.Place(bottom, 0f, H - bottomH, W, bottomH);
            Ui.Place(fade, 0f, 0f, W, bottomH);
            var ready = s.ConnectedOn(a.id) == 2 && s.ConnectedOn(b.id) == 2;
            again.Root.gameObject.SetActive(isHost);
            waiting.Root.gameObject.SetActive(!isHost);
            var startW = Mathf.Min(col, 460f);
            if (isHost)
            {
                again.SetLabel(ready ? "PLAY AGAIN" : "NEED 2 PLAYERS PER TEAM");
                again.Label.fontSize = ready ? 26 : 19;
                again.SetOff(!ready);
                again.SetSize(startW, 62f);
                Ui.Place(again.Root, (W - startW) / 2f, bottomH - 18f - 68f, startW, 68f);
            }
            else
            {
                var host = s.Player(s.hostId);
                waiting.Set("Waiting for " + (host == null ? "the host" : Colored(host.name, host.color)) + " to play again");
                waiting.Place((W - startW) / 2f, bottomH - 18f - 58f, startW);
            }
            page.SetSize(W, Mathf.Max(y + bottomH, H));
            animate = false;
        }

        float BuildStats(RoomState s, float x0, float y, float col)
        {
            Ui.Clear(stats);
            Ui.Place(stats, 0f, 0f, col, 10f);
            var sorted = new List<PlayerState>(s.players);
            sorted.Sort((p, q) => (q.stats?.guessed ?? 0) + (q.stats?.stolen ?? 0) + (q.stats?.drawn ?? 0) - (p.stats?.guessed ?? 0) - (p.stats?.stolen ?? 0) - (p.stats?.drawn ?? 0));
            const float pad = 18f;
            const float numW = 70f;
            var inner = col - pad * 2f;
            var nameW = inner - numW * 3f;
            var ry = 16f;
            Ui.Place(Ui.Label(stats, "PLAYER", Sk.Display, 14, Sk.Muted, TextAnchor.MiddleLeft), pad, ry, nameW, 24f);
            Ui.Place(Ui.Label(stats, "GUESSED", Sk.Display, 14, Sk.Muted, TextAnchor.MiddleRight), pad + nameW, ry, numW, 24f);
            Ui.Place(Ui.Label(stats, "STOLE", Sk.Display, 14, Sk.Muted, TextAnchor.MiddleRight), pad + nameW + numW, ry, numW, 24f);
            Ui.Place(Ui.Label(stats, "DREW", Sk.Display, 14, Sk.Muted, TextAnchor.MiddleRight), pad + nameW + numW * 2f, ry, numW, 24f);
            ry += 30f;
            foreach (var p in sorted)
            {
                if (p.id == App.SelfId) Ui.Place(Ui.Box(stats, Sk.Alpha(Sk.Cream, 0.45f), 10f), pad - 8f, ry, inner + 16f, 50f);
                var avatar = AvatarView.Create(stats, false);
                avatar.Set(p.id);
                avatar.Place(pad - 6f, ry - 2f, 54f);
                var nameText = Ui.Label(stats, p.name, Sk.Display, 18, Sk.Ink, TextAnchor.MiddleLeft);
                Ui.Place(nameText, pad + 52f, ry, nameW - 54f, 50f);
                var dot = Ui.Box(stats, Sk.Hex(p.color), 5f);
                Ui.Place(dot, pad + 52f + Mathf.Min(Ui.Width(nameText), nameW - 70f) + 8f, ry + 20f, 10f, 10f);
                Ui.Place(Ui.Label(stats, (p.stats?.guessed ?? 0).ToString(), Sk.Display, 20, Sk.Ink, TextAnchor.MiddleRight), pad + nameW, ry, numW, 50f);
                Ui.Place(Ui.Label(stats, (p.stats?.stolen ?? 0).ToString(), Sk.Display, 20, Sk.Ink, TextAnchor.MiddleRight), pad + nameW + numW, ry, numW, 50f);
                Ui.Place(Ui.Label(stats, (p.stats?.drawn ?? 0).ToString(), Sk.Display, 20, Sk.Ink, TextAnchor.MiddleRight), pad + nameW + numW * 2f, ry, numW, 50f);
                ry += 52f;
            }
            ry += 10f;
            statsCard.Place(x0, y, col, ry);
            return y + ry + 26f;
        }

        public override void Tick()
        {
            bar.Mute.Refresh(App.Audio.Muted);
            if (waiting.Root.gameObject.activeSelf) waiting.Tick();
        }
    }
}
