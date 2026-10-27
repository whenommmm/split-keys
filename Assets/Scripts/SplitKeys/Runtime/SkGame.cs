using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // Picking, drawing and round-end phases of a match.
    public sealed class GameScreen : SkScreen
    {
        static readonly string[] Levels = { "easy", "medium", "hard" };

        public static Color LevelColor(string level) => Sk.Hex(LevelHex(level));
        public static string LevelHex(string level) => level == "hard" ? "#FF5252" : level == "medium" ? "#FFD43B" : "#69DB7C";
        public static int Multiplier(string level) => level == "hard" ? 3 : level == "medium" ? 2 : 1;
        public static string LevelName(string level) => string.IsNullOrEmpty(level) ? "" : level.ToUpperInvariant() + " ×" + Multiplier(level);

        // HUD
        readonly Text roundLabel;
        readonly Text seconds;
        readonly MuteButton mute;
        readonly Image timerTrack;
        readonly Image timerFill;
        readonly TeamPill[] teamPills = new TeamPill[2];
        readonly Text roleLine;

        // Middle
        readonly SentenceView sentence;
        readonly Keycap tabOurs;
        readonly Keycap tabTheirs;
        readonly Framed paperFrame;
        readonly PaperCanvas ours;
        readonly PaperCanvas theirs;
        readonly RectTransform picker;
        readonly Text pickTitle;
        readonly Keycap[] pickCards = new Keycap[3];
        readonly Text pickStatus;

        // Drawer
        readonly ToolBar tools;

        // Guesser
        readonly RectTransform feed;
        readonly Framed inputBox;
        readonly Text inputText;
        readonly Image caret;
        readonly KeyboardView keyboard;
        string buffer = "";
        bool watchingTheirs;
        bool autoSwitched;

        // Round results
        readonly RectTransform results;
        readonly CanvasGroup resultsGroup;
        readonly Text resultsTitle;
        readonly Text resultsFoot;
        readonly ResultPanel[] panels = new ResultPanel[2];

        int builtRound = -1;
        int lastSecond = -1;
        int lastCount = -1;
        int resultsFor = -1;
        AllCanvases reveal;

        bool IsDrawer => MyRound != null && MyRound.drawerId == App.SelfId;
        bool IsGuesser => MyRound != null && MyRound.guesserId == App.SelfId;
        PlayerState Me => App.State?.Player(App.SelfId);
        RoundTeam MyRound => Me == null ? null : App.State.RoundTeam(Me.team);
        TeamState Rival => Me == null ? null : App.State.OtherTeam(Me.team);
        RoundTeam RivalRound => Rival == null ? null : App.State.RoundTeam(Rival.id);
        Color MyColor => Me == null ? Sk.Cream : Sk.Hex(Me.color);

        public GameScreen(SplitKeysApp app) : base(app, "Game")
        {
            roundLabel = Ui.Label(Root, "ROUND 1/4", Sk.Display, 20, Sk.Text, TextAnchor.MiddleLeft);
            roundLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            seconds = Ui.Label(Root, "90", Sk.Display, 22, Sk.Text, TextAnchor.MiddleRight);
            mute = MuteButton.Create(Root, app.ToggleMute);
            timerTrack = Ui.Box(Root, Sk.Mat2, 4f, "TimerTrack");
            timerFill = Ui.Box(timerTrack.transform, Sk.Cream, 4f, "TimerFill");
            for (var i = 0; i < 2; i++) teamPills[i] = TeamPill.Create(Root);
            roleLine = Ui.Label(Root, "", Sk.Bold, 15, Sk.Muted, TextAnchor.MiddleCenter);
            roleLine.supportRichText = true;

            sentence = SentenceView.Create(Root);
            tabOurs = Keycap.Create(Root, "OUR DRAWING", Sk.Display, 13, Sk.Cream, Sk.Ink, 3f, 10f, false);
            tabOurs.OnDown = () => Watch(false);
            tabTheirs = Keycap.Create(Root, "STEAL", Sk.Display, 13, Sk.Slate, Sk.Text, 3f, 10f, false);
            tabTheirs.OnDown = () => Watch(true);
            paperFrame = Ui.Frame(Root, Sk.Line, PaperCanvas.Paper, 16f, 3f, "PaperFrame");
            ours = PaperCanvas.Create(paperFrame.Inner.transform, "OurPaper");
            Ui.Stretch((RectTransform)ours.transform, 6f, 6f, 6f, 6f);
            ours.Send = SkBridge.Draw;
            theirs = PaperCanvas.Create(paperFrame.Inner.transform, "TheirPaper");
            Ui.Stretch((RectTransform)theirs.transform, 6f, 6f, 6f, 6f);
            theirs.Image.raycastTarget = false;

            // The pick screen sits over the paper: three cards for the drawer, a status for everyone else.
            picker = Ui.Node("Picker", Root);
            var pickerBg = Ui.Box(picker, Sk.Mat, 14f);
            Ui.Stretch(pickerBg.rectTransform);
            pickTitle = Ui.Label(picker, "PICK YOUR WORD", Sk.Display, 20, Sk.Text, TextAnchor.MiddleCenter);
            pickTitle.supportRichText = true;
            for (var i = 0; i < 3; i++)
            {
                var level = Levels[i];
                var card = Keycap.Create(picker, "", Sk.Display, 20, LevelColor(level), Sk.Ink, 5f, 14f);
                card.Label.supportRichText = true;
                card.OnClick = () => App.ChooseWord(level);
                pickCards[i] = card;
            }
            pickStatus = Ui.Label(picker, "", Sk.Bold, 16, Sk.Muted, TextAnchor.UpperCenter);
            pickStatus.supportRichText = true;

            tools = ToolBar.Create(Root);
            tools.OnColor = hex => ours.BrushHex = hex;
            tools.OnSize = size => ours.BrushSize = size;
            tools.OnMode = mode => ours.Mode = mode;
            tools.OnUndo = ours.Undo;
            tools.OnClear = ours.ClearAll;

            feed = Ui.Node("Feed", Root);
            inputBox = Ui.Frame(Root, Sk.Line, Sk.Socket, 14f, 2f, "GuessInput");
            inputText = Ui.Label(inputBox.Inner.transform, "", Sk.Display, 20, Sk.Text, TextAnchor.MiddleLeft);
            inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
            caret = Ui.Box(inputBox.Inner.transform, Sk.Cream, 1f, "Caret");
            keyboard = KeyboardView.Create(Root);
            keyboard.OnLetter = c => Type(c);
            keyboard.OnSpace = () => Type(' ');
            keyboard.OnBack = Backspace;
            keyboard.OnEnter = Submit;

            results = Ui.Node("Results", Root);
            resultsGroup = results.gameObject.AddComponent<CanvasGroup>();
            var dim = Ui.Box(results, Sk.Bg, 0f, "Dim");
            dim.raycastTarget = true;
            Ui.Stretch(dim.rectTransform);
            resultsTitle = Ui.Label(results, "", Sk.Display, 24, Sk.Text, TextAnchor.MiddleCenter);
            resultsFoot = Ui.Label(results, "", Sk.Body, 14, Sk.Muted, TextAnchor.MiddleCenter);
            for (var i = 0; i < 2; i++) panels[i] = ResultPanel.Create(results);
            results.gameObject.SetActive(false);
        }

        RoomState S => App.State;

        Color TeamColor(string team)
        {
            var t = S?.Team(team);
            return t == null ? Sk.Cream : Sk.Hex(t.color);
        }

        public override void OnShow()
        {
            lastSecond = -1;
            lastCount = -1;
        }

        void Watch(bool rival)
        {
            if (!IsGuesser) return;
            watchingTheirs = rival;
            Layout();
        }

        // ---------- server events ----------

        PaperCanvas CanvasFor(string team)
        {
            if (Me == null) return null;
            return team == Me.team ? ours : theirs;
        }

        public void OnCanvas(CanvasSync sync)
        {
            if (S == null || !S.HasRound || sync.roundId != S.round.roundId) return;
            CanvasFor(sync.team)?.SetStrokes(sync.strokes);
        }

        public void OnDraw(DrawOp op)
        {
            if (S == null || !S.HasRound || op.roundId != S.round.roundId) return;
            if (Me != null && op.team == Me.team && IsDrawer) return;
            CanvasFor(op.team)?.Apply(op);
        }

        public void OnCanvases(AllCanvases all)
        {
            reveal = all;
            if (results.gameObject.activeSelf) FillResults();
        }

        public override void OnFx(Fx fx)
        {
            var s = S;
            if (s == null) return;
            var team = s.Team(fx.team);
            var teamColor = TeamColor(fx.team);
            switch (fx.kind)
            {
                case "picked":
                    App.Audio.Beep();
                    if (fx.level == "hard" || fx.level == "medium")
                        App.Toast((team?.name ?? "A team") + " went " + LevelName(fx.level) + "!", LevelColor(fx.level), Sk.Ink, 1.4f, 118f);
                    break;
                case "correct":
                    App.Audio.Go();
                    break;
                case "solved":
                    App.Audio.Jingle();
                    App.Toast((team?.name ?? "A team") + " got " + fx.word + "! +" + fx.points, teamColor, Sk.Ink, 1.8f, 118f);
                    SkBridge.Vibrate(30);
                    break;
                case "close":
                    App.Audio.Beep();
                    App.Toast(fx.word + " is close!", Sk.Hex("#FFD43B"), Sk.Ink, 1.4f, 118f);
                    break;
                case "stolen":
                    var mine = Me != null && fx.from == Me.team;
                    if (mine) { App.Audio.Buzz(); SkBridge.Vibrate(80); }
                    else App.Audio.Go();
                    App.Toast((team?.name ?? "A team") + " stole " + fx.word + "! +" + fx.points, teamColor, Sk.Ink, 1.8f, 118f, SkGfx.Burst);
                    break;
            }
        }

        // ---------- input ----------

        public void OnTypedChar(char c)
        {
            if (!IsGuesser || S?.phase != "drawing") return;
            c = char.ToUpperInvariant(c);
            if (c == ' ' || (c >= 'A' && c <= 'Z'))
            {
                keyboard.Flash(c);
                Type(c);
            }
        }

        void Type(char c)
        {
            if (!IsGuesser || S?.phase != "drawing") return;
            if (c == ' ' && (buffer.Length == 0 || buffer.EndsWith(" "))) return;
            if (buffer.Length >= 28) return;
            buffer += c;
            App.Audio.Clack(0);
            RefreshInput();
        }

        void Backspace()
        {
            if (buffer.Length == 0) return;
            buffer = buffer.Substring(0, buffer.Length - 1);
            RefreshInput();
        }

        void Submit()
        {
            var text = buffer.Trim();
            if (text.Length == 0 || !IsGuesser || S?.phase != "drawing") return;
            App.Guess(text);
            buffer = "";
            RefreshInput();
        }

        void RefreshInput()
        {
            inputText.text = buffer;
            var inner = inputBox.Root.sizeDelta;
            Ui.Place(caret, 14f + Mathf.Min(Ui.Width(inputText), inner.x - 40f) + 2f, 12f, 3f, Mathf.Max(8f, inner.y - 28f));
        }

        // ---------- rendering ----------

        public override void Render()
        {
            var s = S;
            if (s == null || !s.HasRound) return;
            if (s.round.roundId != builtRound)
            {
                builtRound = s.round.roundId;
                ours.CancelStroke();
                ours.SetStrokes(null);
                theirs.SetStrokes(null);
                tools.Reset();
                buffer = "";
                resultsFor = -1;
                lastCount = -1;
                watchingTheirs = false;
                autoSwitched = false;
                if (reveal != null && reveal.roundId != builtRound) reveal = null;
            }
            // Once your own word is taken, the guesser's view jumps to the rival drawing to keep stealing.
            if (IsGuesser && s.phase == "drawing" && MyRound.done && s.round.stealing && !autoSwitched)
            {
                autoSwitched = true;
                watchingTheirs = true;
                if (RivalRound != null && !RivalRound.done) App.Toast("Your word is done! Steal from " + Rival.name, MyColor, Sk.Ink, 2f, 118f);
            }
            ours.Editable = IsDrawer && s.phase == "drawing";
            Layout();
            UpdateResults(s);
        }

        public override void Layout()
        {
            var s = S;
            if (s == null || !s.HasRound) return;
            float W = App.W, H = App.H;
            var col = Mathf.Min(W - 32f, 560f);
            var x0 = (W - col) / 2f;
            var me = Me;
            var mine = MyRound;
            var rival = Rival;

            // HUD
            roundLabel.text = "ROUND " + s.round.number + "/" + Mathf.Max(1, s.round.total);
            Ui.Place(roundLabel, x0, 8f, 200f, 44f);
            mute.Place(x0 + col - 44f, 8f);
            Ui.Place(seconds, x0 + col - 44f - 70f, 8f, 60f, 44f);
            Ui.Place(timerTrack, x0, 58f, col, 8f);
            var pillW = (col - 10f) / 2f;
            for (var i = 0; i < 2; i++)
            {
                var team = s.teams != null && i < s.teams.Length ? s.teams[i] : null;
                teamPills[i].Set(team, team == null ? null : s.RoundTeam(team.id), me != null && team != null && me.team == team.id);
                teamPills[i].Place(x0 + i * (pillW + 10f), 76f, pillW);
            }

            var drawer = mine == null ? null : s.Player(mine.drawerId);
            var guesser = mine == null ? null : s.Player(mine.guesserId);
            var stealView = IsGuesser && watchingTheirs && rival != null && s.round.stealing;
            var myLevel = mine == null || string.IsNullOrEmpty(mine.level) ? "" : "  ·  <color=" + LevelHex(mine.level) + ">" + LevelName(mine.level) + "</color>";
            var rivalRound = RivalRound;
            if (IsDrawer) roleLine.text = "<color=" + me.color + ">YOU'RE DRAWING</color>" + myLevel + "  ·  " + Escape(guesser?.name ?? "your partner") + " guesses";
            else if (stealView) roleLine.text = "<color=" + rival.color + ">STEALING</color>  ·  " + Escape(rival.name) + "'s word is worth +" + (7 * Multiplier(rivalRound?.level));
            else if (IsGuesser) roleLine.text = "<color=" + me.color + ">YOU'RE GUESSING</color>" + myLevel + "  ·  " + Escape(drawer?.name ?? "your partner") + " draws";
            else roleLine.text = "Watching this round";
            Ui.Place(roleLine, x0, 116f, col, 22f);

            var shortScreen = H < 760f;
            var shownWords = stealView ? RivalRound?.words : mine?.words;
            var sentenceH = sentence.Render(shownWords, TeamColor, s.round.roundId * 10 + (stealView ? 1 : 0), x0, 146f, col, true, shortScreen ? 16 : 18);
            var y = 146f + sentenceH + 12f;

            // Guessers can flip between their own team's drawing and the rival drawing.
            var tabs = IsGuesser && rival != null && s.round.stealing && s.phase != "picking";
            tabOurs.Root.gameObject.SetActive(tabs);
            tabTheirs.Root.gameObject.SetActive(tabs);
            if (tabs)
            {
                var tw = (col - 8f) / 2f;
                tabOurs.SetLabel(mine != null && mine.done ? "OUR DRAWING · DONE" : "OUR DRAWING");
                tabTheirs.SetLabel(rivalRound != null && rivalRound.done ? "THEIRS · DONE" : "STEAL " + LevelName(rivalRound?.level));
                tabOurs.SetColors(watchingTheirs ? Sk.Slate : MyColor, watchingTheirs ? Sk.Text : Sk.Ink, watchingTheirs ? 0.12f : 0.35f);
                tabTheirs.SetColors(watchingTheirs ? Sk.Hex(rival.color) : Sk.Slate, watchingTheirs ? Sk.Ink : Sk.Text, watchingTheirs ? 0.35f : 0.12f);
                tabOurs.SetSize(tw, 30f);
                tabTheirs.SetSize(tw, 30f);
                Ui.Place(tabOurs.Root, x0, y, tw, 33f);
                Ui.Place(tabTheirs.Root, x0 + tw + 8f, y, tw, 33f);
                y += 33f + 10f;
            }
            var canvasTop = y;
            ours.gameObject.SetActive(!stealView);
            theirs.gameObject.SetActive(stealView);
            paperFrame.Outer.color = stealView ? Sk.Hex(rival.color) : Sk.Line;

            // The bottom area depends on the role.
            var drawing = s.phase == "drawing";
            var guessing = IsGuesser;
            tools.Root.gameObject.SetActive(IsDrawer);
            feed.gameObject.SetActive(guessing);
            inputBox.Root.gameObject.SetActive(guessing);
            keyboard.Root.gameObject.SetActive(guessing);
            var keyH = shortScreen ? 36f : 42f;
            float bottomH;
            if (IsDrawer) bottomH = ToolBar.HeightFor(col) + 16f;
            else if (guessing) bottomH = 28f + 8f + 48f + 10f + (4f * keyH + 4f * 4f + 3f * 5f) + 12f;
            else bottomH = 16f;
            var available = H - bottomH - canvasTop - 12f;
            var canvasW = col;
            var canvasH = canvasW * 0.75f;
            if (canvasH > available)
            {
                canvasH = Mathf.Max(110f, available);
                canvasW = canvasH / 0.75f;
            }
            Ui.Place(paperFrame.Root, (W - canvasW) / 2f, canvasTop, canvasW, canvasH);
            Ui.Place(picker, (W - canvasW) / 2f, canvasTop, canvasW, canvasH);
            y = canvasTop + canvasH + 12f;
            if (IsDrawer)
            {
                tools.Layout(x0, y, col);
                // The drawer sees the partner's latest guesses on top of the paper.
                feed.gameObject.SetActive(mine?.guesses != null && mine.guesses.Length > 0);
                LayoutFeed(mine, (W - canvasW) / 2f + 12f, canvasTop + 12f, canvasW - 24f);
            }
            else if (guessing)
            {
                LayoutFeed(mine, x0, y, col);
                y += 28f + 8f;
                Ui.Place(inputBox.Root, x0, y, col, 48f);
                Ui.Place(inputText, 14f, 0f, col - 28f, 44f);
                RefreshInput();
                y += 48f + 10f;
                keyboard.SetAccent(stealView ? Sk.Hex(rival.color) : MyColor);
                keyboard.Layout(x0, y, col, keyH);
                keyboard.SetOff(!drawing || !App.Connected);
            }

            picker.gameObject.SetActive(s.phase == "picking");
            if (s.phase == "picking") LayoutPicker(s, mine, drawer, canvasW, canvasH);

            LayoutResults(s);
        }

        void LayoutPicker(RoomState s, RoundTeam mine, PlayerState drawer, float w, float h)
        {
            var choosing = IsDrawer && mine != null && string.IsNullOrEmpty(mine.level);
            foreach (var card in pickCards) card.Root.gameObject.SetActive(choosing);
            if (choosing)
            {
                pickTitle.text = "PICK YOUR WORD";
                Ui.Place(pickTitle, 0f, 10f, w, 30f);
                const float top = 48f, gap = 10f;
                var cardW = w - 32f;
                var cardH = Mathf.Min(70f, (h - top - 14f - 2f * gap) / 3f - 5f);
                for (var i = 0; i < 3; i++)
                {
                    var option = mine.options != null && i < mine.options.Length ? mine.options[i] : null;
                    var level = option?.level ?? Levels[i];
                    var card = pickCards[i];
                    card.SetColors(LevelColor(level), Sk.Ink);
                    card.Label.fontSize = cardH >= 56f ? 20 : 16;
                    card.SetLabel("<size=" + (cardH >= 56f ? 12 : 10) + ">" + LevelName(level) + "</size>\n" + (option?.word ?? "?"));
                    card.SetSize(cardW, cardH);
                    Ui.Place(card.Root, 16f, top + i * (cardH + 5f + gap), cardW, cardH + 5f);
                }
                return;
            }
            var lines = new System.Collections.Generic.List<string>();
            if (IsDrawer && mine?.words != null && mine.words.Length > 0)
            {
                var word = string.Join(" ", System.Array.ConvertAll(mine.words, v => v.text));
                lines.Add("Your word: <color=#F4F1FF>" + word + "</color>");
                lines.Add("");
            }
            foreach (var team in s.teams)
            {
                var round = s.RoundTeam(team.id);
                var status = round == null || string.IsNullOrEmpty(round.level) ? "picking…" : "<color=" + LevelHex(round.level) + ">" + LevelName(round.level) + "</color>";
                lines.Add("<color=" + team.color + ">" + team.name.ToUpperInvariant() + "</color>   " + status);
            }
            pickTitle.text = IsDrawer ? "LOCKED IN!" : Escape(drawer?.name ?? "Your partner") + " is picking a word…";
            pickStatus.text = string.Join("\n", lines);
            Ui.Place(pickTitle, 16f, h * 0.18f, w - 32f, 34f);
            Ui.Place(pickStatus, 16f, h * 0.18f + 48f, w - 32f, h * 0.7f);
        }

        void LayoutFeed(RoundTeam mine, float x, float y, float width)
        {
            Ui.Clear(feed);
            Ui.Place(feed, x, y, width, 28f);
            if (mine?.guesses == null) return;
            var px = 0f;
            var rivalColor = Rival == null ? Sk.Cream : Sk.Hex(Rival.color);
            for (var i = mine.guesses.Length - 1; i >= 0; i--)
            {
                var g = mine.guesses[i];
                var bg = g.result == "correct" ? Sk.Ok : g.result == "stolen" ? rivalColor : g.result == "close" ? Sk.Hex("#FFD43B") : Sk.Mat2;
                var fg = g.result == "wrong" ? Sk.Muted : Sk.Ink;
                var pill = Ui.Node("Guess", feed);
                var box = Ui.Box(pill, bg, 13f);
                Ui.Stretch(box.rectTransform);
                var label = Ui.Label(pill, g.text, Sk.Bold, 13, fg, TextAnchor.MiddleCenter);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                Ui.Stretch(label.rectTransform);
                var w = Ui.Width(label) + 22f;
                if (px + w > width) { Object.Destroy(pill.gameObject); break; }
                Ui.Place(pill, px, 0f, w, 26f);
                px += w + 6f;
            }
        }

        // ---------- round results ----------

        void UpdateResults(RoomState s)
        {
            var show = s.phase == "roundEnd";
            results.gameObject.SetActive(show);
            if (!show) return;
            if (resultsFor != s.round.roundId)
            {
                resultsFor = s.round.roundId;
                var mine = MyRound;
                if (mine != null && !mine.done) App.Audio.TimeUp();
                if (!App.ReducedMotion) Tween.Run(results, 0.3f, t => resultsGroup.alpha = t);
            }
            FillResults();
            LayoutResults(s);
        }

        void FillResults()
        {
            var s = S;
            if (s == null || !s.HasRound) return;
            for (var i = 0; i < 2 && i < s.teams.Length; i++)
            {
                Stroke[] strokes = null;
                if (reveal != null && reveal.roundId == s.round.roundId) strokes = s.teams[i].id == "sky" ? reveal.sky : reveal.tangerine;
                panels[i].SetStrokes(strokes);
            }
        }

        void LayoutResults(RoomState s)
        {
            if (!results.gameObject.activeSelf) return;
            float W = App.W, H = App.H;
            Ui.Place(results, 0f, 0f, W, H);
            resultsTitle.text = "ROUND " + s.round.number + " OF " + s.round.total;
            Ui.Place(resultsTitle, 0f, 20f, W, 36f);
            Ui.Place(resultsFoot, 0f, H - 40f, W, 24f);
            var side = W >= 700f;
            var col = Mathf.Min(W - 32f, side ? 900f : 520f);
            var x0 = (W - col) / 2f;
            const float top = 66f;
            var bottom = H - 50f;
            // Panels size to their content, then sit centered between the title and the footer.
            if (side)
            {
                var pw = (col - 16f) / 2f;
                var used = 0f;
                for (var i = 0; i < 2; i++) used = Mathf.Max(used, panels[i].Layout(s, i, x0 + i * (pw + 16f), top, pw, bottom - top, TeamColor));
                var offset = Mathf.Max(0f, (bottom - top - used) / 2f);
                for (var i = 0; i < 2; i++) panels[i].Layout(s, i, x0 + i * (pw + 16f), top + offset, pw, bottom - top, TeamColor);
            }
            else
            {
                var each = (bottom - top - 12f) / 2f;
                var heights = new float[2];
                for (var i = 0; i < 2; i++) heights[i] = panels[i].Layout(s, i, x0, top, col, each, TeamColor);
                var offset = Mathf.Max(0f, (bottom - top - heights[0] - heights[1] - 12f) / 2f);
                panels[0].Layout(s, 0, x0, top + offset, col, each, TeamColor);
                panels[1].Layout(s, 1, x0, top + offset + heights[0] + 12f, col, each, TeamColor);
            }
        }

        // ---------- per frame ----------

        public override void Tick()
        {
            var s = S;
            if (s == null || !s.HasRound) return;
            mute.Refresh(App.Audio.Muted);
            var remaining = (float)(s.round.phaseEndsAt - App.ServerNowMs());
            const float pickTotal = 10000f;
            var total = s.phase == "drawing" && s.round.drawStartedAt > 0 ? s.round.phaseEndsAt - s.round.drawStartedAt : pickTotal;
            var secs = Mathf.Max(0, Mathf.CeilToInt(remaining / 1000f));
            var ratio = s.phase == "roundEnd" ? 0f : Mathf.Clamp01(remaining / Mathf.Max(1f, total));
            var danger = (s.phase == "drawing" && secs <= 10) || (s.phase == "picking" && secs <= 3);
            seconds.text = s.phase == "roundEnd" ? "0" : secs.ToString();
            seconds.color = danger ? Sk.Danger : Sk.Text;
            timerFill.color = danger ? Sk.Danger : Sk.Cream;
            Ui.Place(timerFill, 0f, 0f, Mathf.Max(ratio > 0f ? 8f : 0f, timerTrack.rectTransform.sizeDelta.x * ratio), 8f);
            if (danger && secs != lastSecond && secs > 0) App.Audio.Tick();
            lastSecond = secs;

            if (s.phase == "picking") lastCount = 1;
            else if (s.phase == "drawing" && lastCount != 0)
            {
                lastCount = 0;
                App.Audio.Go();
            }

            if (s.phase == "roundEnd" && results.gameObject.activeSelf)
                resultsFoot.text = (s.round.number >= s.round.total ? "Final scores in " : "Next round in ") + secs + "…";

            // Physical keyboard: backspace and enter for the guesser.
            var kb = Keyboard.current;
            if (kb != null && IsGuesser && s.phase == "drawing")
            {
                if (kb.backspaceKey.wasPressedThisFrame) Backspace();
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Submit();
            }
            caret.gameObject.SetActive(IsGuesser && s.phase == "drawing" && Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f);
            ours.Editable = IsDrawer && s.phase == "drawing" && App.Connected;
        }
    }

    // A team's score pill in the HUD: color dot, name, words found this round and score.
    public sealed class TeamPill
    {
        public RectTransform Root;
        Framed frame;
        Image dot;
        Text name;
        Text detail;
        Text score;

        public static TeamPill Create(Transform parent)
        {
            var pill = new TeamPill { Root = Ui.Node("TeamPill", parent) };
            pill.frame = Ui.Frame(pill.Root, Sk.Mat2, Sk.Mat2, 17f, 1.5f);
            Ui.Stretch(pill.frame.Root);
            pill.dot = Ui.Box(pill.Root, Color.white, 5f, "Dot");
            pill.name = Ui.Label(pill.Root, "", Sk.Display, 13, Sk.Text, TextAnchor.MiddleLeft);
            pill.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            pill.detail = Ui.Label(pill.Root, "", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft);
            pill.detail.horizontalOverflow = HorizontalWrapMode.Overflow;
            pill.score = Ui.Label(pill.Root, "", Sk.Display, 16, Sk.Text, TextAnchor.MiddleRight);
            pill.score.horizontalOverflow = HorizontalWrapMode.Overflow;
            return pill;
        }

        public void Set(TeamState team, RoundTeam round, bool mine)
        {
            if (team == null) return;
            var color = Sk.Hex(team.color);
            frame.Outer.color = mine ? color : Sk.Mat2;
            dot.color = color;
            name.text = team.name.ToUpperInvariant();
            name.color = color;
            score.text = team.score.ToString();
            if (round == null) detail.text = "";
            else if (round.done) detail.text = round.solvedBy == team.id ? "GOT IT" : "STOLEN";
            else if (string.IsNullOrEmpty(round.level)) detail.text = "PICKING";
            else detail.text = GameScreen.LevelName(round.level);
            detail.color = round == null ? Sk.Muted
                : round.done ? (round.solvedBy == team.id ? Sk.Ok : Sk.Danger)
                : string.IsNullOrEmpty(round.level) ? Sk.Muted : GameScreen.LevelColor(round.level);
        }

        // Name and words-found on the left, score on the right; the name shrinks on narrow pills.
        public void Place(float x, float y, float width)
        {
            Ui.Place(Root, x, y, width, 34f);
            Ui.Place(dot, 12f, 12f, 10f, 10f);
            var scoreW = Ui.Width(score) + 4f;
            Ui.Place(score, width - 12f - scoreW, 0f, scoreW, 34f);
            name.fontSize = 13;
            var room = width - 28f - scoreW - 18f;
            var detailW = Ui.Width(detail) + 2f;
            if (Ui.Width(name) + 8f + detailW > room) name.fontSize = 11;
            var nameW = Ui.Width(name) + 2f;
            Ui.Place(name, 28f, 0f, nameW, 34f);
            Ui.Place(detail, 28f + nameW + 6f, 0f, detailW, 34f);
        }
    }

    // One team's drawing, sentence and points on the round results.
    public sealed class ResultPanel
    {
        public RectTransform Root;
        Text header;
        Text points;
        Framed paperFrame;
        PaperCanvas paper;
        SentenceView words;

        public static ResultPanel Create(Transform parent)
        {
            var panel = new ResultPanel { Root = Ui.Node("ResultPanel", parent) };
            var bg = Ui.Frame(panel.Root, Sk.Line, Sk.Mat, 20f, 1f);
            Ui.Stretch(bg.Root);
            panel.header = Ui.Label(panel.Root, "", Sk.Display, 16, Sk.Text, TextAnchor.MiddleLeft);
            panel.header.supportRichText = true;
            panel.points = Ui.Label(panel.Root, "", Sk.Display, 16, Sk.Ok, TextAnchor.MiddleRight);
            panel.paperFrame = Ui.Frame(panel.Root, Sk.Line, PaperCanvas.Paper, 12f, 2f);
            panel.paper = PaperCanvas.Create(panel.paperFrame.Inner.transform, "ResultPaper");
            Ui.Stretch((RectTransform)panel.paper.transform, 4f, 4f, 4f, 4f);
            panel.paper.Image.raycastTarget = false;
            panel.words = SentenceView.Create(panel.Root);
            return panel;
        }

        public void SetStrokes(Stroke[] strokes) => paper.SetStrokes(strokes);

        // Lays the panel out within maxHeight and returns the height it actually needs.
        public float Layout(RoomState s, int index, float x, float y, float width, float maxHeight, System.Func<string, Color> colorOf)
        {
            if (s.teams == null || index >= s.teams.Length) return 0f;
            var team = s.teams[index];
            var round = s.RoundTeam(team.id);
            var drawer = s.Player(round?.drawerId);
            header.text = "<color=" + team.color + ">" + team.name.ToUpperInvariant() + "</color>  "
                + (round == null || string.IsNullOrEmpty(round.level) ? "" : "<size=12><color=" + GameScreen.LevelHex(round.level) + ">" + GameScreen.LevelName(round.level) + "</color></size>  ")
                + "<size=12><color=#A19BBE>by " + (drawer == null ? "?" : drawer.name.Replace("<", "‹").Replace(">", "›")) + "</color></size>";
            Ui.Place(header, 16f, 8f, width - 120f, 30f);
            // Points this round can mix a solve or a steal with a penalty, so show the net with the outcome.
            var net = round == null ? "" : round.points.ToString("+#;-#;0");
            if (round == null) points.text = "";
            else if (round.done && round.solvedBy != team.id) { points.text = round.points == 0 ? "STOLEN" : "STOLEN  " + net; points.color = Sk.Danger; }
            else { points.text = net; points.color = round.points > 0 ? Sk.Ok : round.points < 0 ? Sk.Danger : Sk.Muted; }
            Ui.Place(points, width - 136f, 8f, 120f, 30f);
            var key = s.round.roundId * 10 + 5 + index;
            var wordsH = words.Render(round?.words, colorOf, key, 12f, 0f, width - 24f, false, 13);
            var maxCanvasH = maxHeight - 44f - wordsH - 24f;
            var canvasW = width - 24f;
            var canvasH = canvasW * 0.75f;
            if (canvasH > maxCanvasH)
            {
                canvasH = Mathf.Max(60f, maxCanvasH);
                canvasW = canvasH / 0.75f;
            }
            Ui.Place(paperFrame.Root, (width - canvasW) / 2f, 42f, canvasW, canvasH);
            words.Render(round?.words, colorOf, key, 12f, 42f + canvasH + 10f, width - 24f, false, 13);
            var used = 42f + canvasH + 10f + wordsH + 14f;
            Ui.Place(Root, x, y, width, used);
            return used;
        }
    }
}
