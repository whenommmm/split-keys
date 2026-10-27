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
        readonly SentenceView sample;
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
            tagline = Ui.Label(c, "Pick a word: easy, medium or hard. Draw it. Your teammate guesses. Riskier words score more.", Sk.Body, 16, Sk.Muted, TextAnchor.UpperCenter);

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
            sample = SentenceView.Create(c);
            footer = Ui.Label(c, "No app. No account. 4 players, 2 teams.", Sk.Body, 14, Sk.Dim, TextAnchor.MiddleCenter);
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
            var example = new[]
            {
                new WordView { text = "TRAFFIC", revealed = true, by = "sky" }, new WordView { text = "J__", revealed = false }
            };
            y += sample.Render(example, _ => Sk.Hex(Sk.PlayerHex[0]), 0, x0, y, col, false, 16) + 28f;
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
        }
    }

    // A team's box in the lobby: header plus two seats.
    public sealed class TeamBox
    {
        public RectTransform Root;
        Framed frame;
        Image dot;
        Text title;
        Text count;
        readonly PlayerRow[] rows = new PlayerRow[2];
        readonly RectTransform[] empties = new RectTransform[2];

        public static TeamBox Create(Transform parent)
        {
            var box = new TeamBox { Root = Ui.Node("TeamBox", parent) };
            box.frame = Ui.Frame(box.Root, Sk.Line, Sk.Mat, 20f, 1.5f);
            Ui.Stretch(box.frame.Root);
            box.dot = Ui.Box(box.Root, Color.white, 6f, "Dot");
            box.title = Ui.Label(box.Root, "", Sk.Display, 16, Sk.Text, TextAnchor.MiddleLeft);
            box.count = Ui.Label(box.Root, "", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight);
            for (var i = 0; i < 2; i++)
            {
                box.rows[i] = PlayerRow.Create(box.Root);
                box.empties[i] = Ui.Node("Empty", box.Root);
                var frame = Ui.Frame(box.empties[i], Sk.Line, Sk.Bg, 14f, 1.5f);
                Ui.Stretch(frame.Root);
                var label = Ui.Label(box.empties[i], "Waiting for a teammate…", Sk.Body, 14, Sk.Dim, TextAnchor.MiddleCenter);
                Ui.Stretch(label.rectTransform);
            }
            return box;
        }

        public const float Height = 44f + 2f * 68f + 8f;

        public void Set(RoomState s, TeamState team, string selfId, float x, float y, float width)
        {
            Ui.Place(Root, x, y, width, Height);
            var color = Sk.Hex(team.color);
            var mine = s.Player(selfId)?.team == team.id;
            frame.Outer.color = mine ? color : Sk.Line;
            dot.color = color;
            Ui.Place(dot, 16f, 17f, 12f, 12f);
            title.text = "TEAM " + team.name.ToUpperInvariant();
            title.color = color;
            Ui.Place(title, 36f, 6f, width - 120f, 34f);
            var members = new List<PlayerState>();
            if (team.playerIds != null) foreach (var id in team.playerIds) { var p = s.Player(id); if (p != null) members.Add(p); }
            count.text = members.Count + "/2";
            Ui.Place(count, width - 76f, 6f, 60f, 34f);
            for (var i = 0; i < 2; i++)
            {
                var has = i < members.Count;
                rows[i].Root.gameObject.SetActive(has);
                empties[i].gameObject.SetActive(!has);
                var ry = 44f + i * 68f;
                if (has)
                {
                    rows[i].Set(members[i], members[i].id == selfId, members[i].id == s.hostId, width - 16f);
                    Ui.Place(rows[i].Root, 8f, ry, width - 16f, 60f);
                }
                else Ui.Place(empties[i], 8f, ry, width - 16f, 60f);
            }
        }
    }

    // The host's game settings: draw time, hint pace, sentence length and rounds.
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
        Framed frame;
        Text title;
        Text note;
        readonly Row[] rows = new Row[Defs.Length];

        public static SettingsPanel Create(Transform parent)
        {
            var panel = new SettingsPanel { Root = Ui.Node("Settings", parent) };
            panel.frame = Ui.Frame(panel.Root, Sk.Line, Sk.Mat, 20f, 1.5f);
            Ui.Stretch(panel.frame.Root);
            panel.title = Ui.Label(panel.Root, "GAME SETTINGS", Sk.Display, 15, Sk.Text, TextAnchor.MiddleLeft);
            panel.note = Ui.Label(panel.Root, "", Sk.Body, 13, Sk.Muted, TextAnchor.MiddleRight);
            panel.note.supportRichText = true;
            for (var r = 0; r < Defs.Length; r++)
            {
                var def = Defs[r];
                var row = new Row { Key = def.key, Values = def.values, Options = new Keycap[def.values.Length] };
                row.Label = Ui.Label(panel.Root, def.label, Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft);
                for (var i = 0; i < def.values.Length; i++)
                {
                    var value = def.values[i];
                    var key = def.key;
                    var cap = Keycap.Create(panel.Root, def.names[i], Sk.Display, 13, Sk.Slate, Sk.Text, 3f, 9f, false);
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
            const float pad = 16f;
            const float gap = 6f;
            var inner = width - pad * 2f;
            Ui.Place(title, pad, 12f, inner / 2f, 24f);
            var host = s.Player(s.hostId);
            note.text = isHost ? "Tap to change" : "Set by " + (host == null ? "the host" : "<color=" + host.color + ">" + host.name.Replace("<", "‹").Replace(">", "›") + "</color>");
            Ui.Place(note, pad + inner / 2f, 12f, inner / 2f, 24f);
            var rowY = 48f;
            foreach (var row in rows)
            {
                Ui.Place(row.Label, pad, rowY, inner, 16f);
                rowY += 22f;
                var current = Current(s.settings, row.Key);
                var ow = (inner - gap * (row.Options.Length - 1)) / row.Options.Length;
                for (var i = 0; i < row.Options.Length; i++)
                {
                    var cap = row.Options[i];
                    var selected = row.Values[i] == current;
                    cap.Interactive = isHost;
                    if (selected) cap.SetColors(Sk.Cream, Sk.Ink, 0.35f);
                    else if (isHost) cap.SetColors(Sk.Slate, Sk.Text, 0.12f);
                    else cap.SetColors(Sk.Mat2, Sk.Dim, 0f);
                    cap.SetSize(ow, 32f);
                    Ui.Place(cap.Root, pad + i * (ow + gap), rowY, ow, 35f);
                }
                rowY += 35f + 12f;
            }
            var height = rowY + 4f;
            Ui.Place(Root, x, y, width, height);
            return height;
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
        readonly TeamBox[] boxes = new TeamBox[2];
        readonly Keycap switchTeam;
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
            for (var i = 0; i < 2; i++) boxes[i] = TeamBox.Create(c);
            switchTeam = Keycap.Create(c, "SWITCH TEAM", Sk.Display, 14, Sk.Slate, Sk.Text, 5f, 12f);
            switchTeam.SetColors(Sk.Slate, Sk.Text, 0.12f);
            switchTeam.OnClick = SkBridge.SwitchTeam;
            hint = Ui.Label(c, "Each round one teammate draws and the other guesses. You swap every round.", Sk.Body, 14, Sk.Muted, TextAnchor.UpperCenter);
            settings = SettingsPanel.Create(c);
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
            if (s == null || s.players == null || s.teams == null || s.teams.Length < 2) return;
            float W = App.W, H = App.H;
            var col = Mathf.Min(W - 32f, App.Wide ? 840f : 520f);
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
            Ui.Stretch(invite.Label.rectTransform, 26f, 0f, 0f, 0f);
            invite.Glyph.rectTransform.anchoredPosition = new Vector2(-iw / 2f + 30f, 0f);
            y += 58f + 10f;
            link.text = SkBridge.InviteUrl(s.code ?? "").Replace("https://", "").Replace("http://", "");
            Ui.Place(link, x0, y, col, 18f);
            y += 18f + 20f;

            // Wide screens: teams on the left, the host's settings on the right. Phones stack them.
            var side = App.Wide;
            var isHost = s.hostId == App.SelfId;
            var leftW = side ? Mathf.Floor((col - 20f) * 0.52f) : col;
            var rightX = side ? x0 + leftW + 20f : x0;
            var rightW = side ? col - leftW - 20f : col;
            var columnTop = y;
            for (var i = 0; i < 2; i++) boxes[i].Set(s, s.teams[i], App.SelfId, x0, y + i * (TeamBox.Height + 12f), leftW);
            y += 2f * TeamBox.Height + 12f + 16f;

            var me = s.Player(App.SelfId);
            var otherTeam = me == null ? null : (me.team == s.teams[0].id ? s.teams[1] : s.teams[0]);
            var canSwitch = otherTeam != null && (otherTeam.playerIds?.Length ?? 0) < 2;
            switchTeam.Root.gameObject.SetActive(canSwitch);
            if (canSwitch)
            {
                switchTeam.SetLabel("JOIN TEAM " + otherTeam.name.ToUpperInvariant());
                switchTeam.SetColors(Sk.Hex(otherTeam.color), Sk.Ink, 0.35f);
                const float sw = 240f;
                switchTeam.SetSize(sw, 46f);
                Ui.Place(switchTeam.Root, x0 + (leftW - sw) / 2f, y, sw, 51f);
                y += 51f + 14f;
            }
            var hh = Ui.Height(hint, leftW);
            Ui.Place(hint, x0, y, leftW, hh + 4f);
            y += hh + 20f;

            if (side) y = Mathf.Max(y, columnTop + settings.Layout(s, isHost, rightX, columnTop, rightW) + 16f);
            else y += settings.Layout(s, isHost, x0, y, col) + 16f;
            dev.Root.gameObject.SetActive(s.dev && isHost);
            if (dev.Root.gameObject.activeSelf)
            {
                dev.Place(x0, y, col, s);
                dev.Add.SetOff(s.players.Length >= 4);
                y += DevPanel.Height + 16f;
            }

            const float bottomH = 100f;
            Ui.Place(bottom, 0f, H - bottomH, W, bottomH);
            Ui.Place(fade, 0f, 0f, W, bottomH);
            var ready = s.ConnectedOn(s.teams[0].id) == 2 && s.ConnectedOn(s.teams[1].id) == 2;
            start.Root.gameObject.SetActive(isHost);
            waiting.Root.gameObject.SetActive(!isHost);
            var startW = Mathf.Min(col, 520f);
            if (isHost)
            {
                start.SetLabel(ready ? "START GAME" : "NEED 2 PLAYERS PER TEAM");
                start.SetOff(!ready);
                start.SetSize(startW, 60f);
                Ui.Place(start.Root, (W - startW) / 2f, bottomH - 16f - 66f, startW, 66f);
            }
            else
            {
                var host = s.Player(s.hostId);
                waiting.Set(ready
                    ? "Waiting for " + (host == null ? "the host" : Colored(host.name, host.color)) + " to start"
                    : "Waiting for 4 players");
                waiting.Place((W - startW) / 2f, bottomH - 16f - 52f, startW);
            }
            page.SetSize(W, Mathf.Max(y + bottomH, H));
        }

        public override void Tick()
        {
            bar.Mute.Refresh(App.Audio.Muted);
            if (waiting.Root.gameObject.activeSelf) waiting.Tick();
        }
    }

    public sealed class GameOverScreen : SkScreen
    {
        readonly ScrollPage page;
        readonly TopBar bar;
        readonly Text over;
        readonly Text winner;
        readonly Keycap[] scores = new Keycap[2];
        readonly Text[] scoreNames = new Text[2];
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
            over = Ui.Label(c, "GAME OVER", Sk.Bold, 13, Sk.Muted, TextAnchor.MiddleCenter);
            winner = Ui.Label(c, "", Sk.Display, 34, Sk.Text, TextAnchor.MiddleCenter);
            winner.horizontalOverflow = HorizontalWrapMode.Overflow;
            for (var i = 0; i < 2; i++)
            {
                scores[i] = Keycap.Create(c, "0", Sk.Display, 46, Sk.Cream, Sk.Ink, 6f, 20f);
                scores[i].Interactive = false;
                scoreNames[i] = Ui.Label(c, "", Sk.Display, 14, Sk.Text, TextAnchor.MiddleCenter);
            }
            stats = Ui.Node("Stats", c);
            inviteMore = Ui.Label(c, "Invite friends", Sk.Bold, 15, Sk.Muted, TextAnchor.MiddleCenter);
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
            bar.Layout(x0, col, 8f);
            var y = 64f;
            Ui.Place(over, x0, y, col, 18f);
            y += 28f;
            var a = s.teams[0];
            var b = s.teams[1];
            if (a.score == b.score) { winner.text = "IT'S A TIE!"; winner.color = Sk.Cream; }
            else
            {
                var top = a.score > b.score ? a : b;
                winner.text = "TEAM " + top.name.ToUpperInvariant() + " WINS!";
                winner.color = Sk.Hex(top.color);
            }
            winner.fontSize = W < 380f ? 28 : 34;
            Ui.Place(winner, 0f, y, W, 48f);
            if (animate && !App.ReducedMotion)
            {
                var rt = winner.rectTransform;
                Tween.Run(rt, 0.45f, t => { var k = Mathf.LerpUnclamped(0.6f, 1f, Tween.OutBack(t)); rt.localScale = new Vector3(k, k, 1f); });
            }
            y += 64f;
            const float size = 120f;
            var gap = Mathf.Min(40f, col - 2f * size);
            var startX = (W - (2f * size + gap)) / 2f;
            for (var i = 0; i < 2; i++)
            {
                var team = s.teams[i];
                scores[i].SetColors(Sk.Hex(team.color), Sk.Ink);
                scores[i].SetLabel(team.score.ToString());
                scores[i].SetSize(size, size);
                Ui.Place(scores[i].Root, startX + i * (size + gap), y, size, size + 6f);
                scoreNames[i].text = team.name.ToUpperInvariant();
                scoreNames[i].color = Sk.Hex(team.color);
                Ui.Place(scoreNames[i], startX + i * (size + gap) - 20f, y + size + 14f, size + 40f, 20f);
            }
            y += size + 50f;
            y = BuildStats(s, x0, y, col);
            Ui.Place(inviteMore, x0, y, col, 44f);
            y += 52f;
            var isHost = s.hostId == App.SelfId;
            dev.Root.gameObject.SetActive(s.dev && isHost);
            if (dev.Root.gameObject.activeSelf)
            {
                dev.Place(x0, y, col, s);
                dev.Add.SetOff(s.players.Length >= 4);
                y += DevPanel.Height + 16f;
            }
            const float bottomH = 100f;
            Ui.Place(bottom, 0f, H - bottomH, W, bottomH);
            Ui.Place(fade, 0f, 0f, W, bottomH);
            var ready = s.ConnectedOn(a.id) == 2 && s.ConnectedOn(b.id) == 2;
            again.Root.gameObject.SetActive(isHost);
            waiting.Root.gameObject.SetActive(!isHost);
            if (isHost)
            {
                again.SetLabel(ready ? "PLAY AGAIN" : "NEED 2 PLAYERS PER TEAM");
                again.SetOff(!ready);
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

        float BuildStats(RoomState s, float x0, float y, float col)
        {
            Ui.Clear(stats);
            Ui.Place(stats, 0f, 0f, 0f, 0f);
            var sorted = new List<PlayerState>(s.players);
            sorted.Sort((p, q) => (q.stats?.guessed ?? 0) + (q.stats?.stolen ?? 0) + (q.stats?.drawn ?? 0) - (p.stats?.guessed ?? 0) - (p.stats?.stolen ?? 0) - (p.stats?.drawn ?? 0));
            const float numW = 72f;
            var nameW = col - numW * 3f;
            Ui.Place(Ui.Label(stats, "PLAYER", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft), x0, y, nameW, 24f);
            Ui.Place(Ui.Label(stats, "GUESSED", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight), x0 + nameW, y, numW, 24f);
            Ui.Place(Ui.Label(stats, "STOLE", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight), x0 + nameW + numW, y, numW, 24f);
            Ui.Place(Ui.Label(stats, "DREW", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleRight), x0 + nameW + numW * 2f, y, numW, 24f);
            y += 30f;
            foreach (var p in sorted)
            {
                if (p.id == App.SelfId) Ui.Place(Ui.Box(stats, Sk.Mat2, 10f), x0 - 6f, y, col + 12f, 44f);
                var avatar = Keycap.Create(stats, string.IsNullOrEmpty(p.name) ? "?" : p.name.Substring(0, 1).ToUpperInvariant(), Sk.Display, 14, Sk.Hex(p.color), Sk.Ink, 3f, 8f, false);
                avatar.Interactive = false;
                avatar.SetSize(28f, 28f);
                Ui.Place(avatar.Root, x0, y + 7f, 28f, 31f);
                Ui.Place(Ui.Label(stats, p.name, Sk.Bold, 16, Sk.Hex(p.color), TextAnchor.MiddleLeft), x0 + 38f, y, nameW - 40f, 44f);
                Ui.Place(Ui.Label(stats, (p.stats?.guessed ?? 0).ToString(), Sk.Bold, 16, Sk.Text, TextAnchor.MiddleRight), x0 + nameW, y, numW, 44f);
                Ui.Place(Ui.Label(stats, (p.stats?.stolen ?? 0).ToString(), Sk.Bold, 16, Sk.Text, TextAnchor.MiddleRight), x0 + nameW + numW, y, numW, 44f);
                Ui.Place(Ui.Label(stats, (p.stats?.drawn ?? 0).ToString(), Sk.Bold, 16, Sk.Text, TextAnchor.MiddleRight), x0 + nameW + numW * 2f, y, numW, 44f);
                Ui.Place(Ui.Box(stats, Sk.Line, 0f), x0, y + 44f, col, 1f);
                y += 45f;
            }
            return y + 18f;
        }

        public override void Tick()
        {
            bar.Mute.Refresh(App.Audio.Muted);
            if (waiting.Root.gameObject.activeSelf) waiting.Tick();
        }
    }
}
