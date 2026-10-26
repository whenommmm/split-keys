using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // Countdown, drawing and round-end phases of a match.
    public sealed class GameScreen : SkScreen
    {
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
        readonly Framed paperFrame;
        readonly PaperCanvas paper;
        readonly RectTransform countdown;
        readonly Text countdownNumber;
        readonly Text countdownText;

        // Drawer
        readonly ToolBar tools;

        // Guesser
        readonly RectTransform feed;
        readonly Framed inputBox;
        readonly Text inputText;
        readonly Image caret;
        readonly KeyboardView keyboard;
        string buffer = "";

        // Round results
        readonly RectTransform results;
        readonly CanvasGroup resultsGroup;
        readonly Image dim;
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
            paperFrame = Ui.Frame(Root, Sk.Line, PaperCanvas.Paper, 16f, 3f, "PaperFrame");
            paper = PaperCanvas.Create(paperFrame.Inner.transform, 480, 360);
            Ui.Stretch((RectTransform)paper.transform, 6f, 6f, 6f, 6f);
            paper.Send = SkBridge.Draw;

            countdown = Ui.Node("Countdown", Root);
            var countdownBg = Ui.Box(countdown, Sk.Mat, 14f);
            Ui.Stretch(countdownBg.rectTransform);
            countdownNumber = Ui.Label(countdown, "3", Sk.Display, 72, Sk.Cream, TextAnchor.MiddleCenter);
            countdownText = Ui.Label(countdown, "", Sk.Bold, 17, Sk.Text, TextAnchor.UpperCenter);
            countdownText.supportRichText = true;

            tools = ToolBar.Create(Root);
            tools.OnColor = hex => paper.BrushHex = hex;
            tools.OnSize = size => paper.BrushSize = size;
            tools.OnUndo = paper.Undo;
            tools.OnClear = paper.ClearAll;

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
            dim = Ui.Box(results, Sk.Bg, 0f, "Dim");
            dim.raycastTarget = true;
            Ui.Stretch(dim.rectTransform);
            resultsTitle = Ui.Label(results, "", Sk.Display, 24, Sk.Text, TextAnchor.MiddleCenter);
            resultsFoot = Ui.Label(results, "", Sk.Body, 14, Sk.Muted, TextAnchor.MiddleCenter);
            for (var i = 0; i < 2; i++) panels[i] = ResultPanel.Create(results);
            results.gameObject.SetActive(false);
        }

        RoomState S => App.State;

        public override void OnShow()
        {
            lastSecond = -1;
            lastCount = -1;
        }

        // ---------- server events ----------

        public void OnCanvas(CanvasSync sync)
        {
            if (S == null || !S.HasRound || sync.roundId != S.round.roundId || Me == null || sync.team != Me.team) return;
            paper.SetStrokes(sync.strokes);
        }

        public void OnDraw(DrawOp op)
        {
            if (S == null || !S.HasRound || op.roundId != S.round.roundId || Me == null || op.team != Me.team || IsDrawer) return;
            paper.Apply(op);
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
            var teamColor = team == null ? Sk.Cream : Sk.Hex(team.color);
            switch (fx.kind)
            {
                case "correct":
                    App.Audio.Go();
                    break;
                case "close":
                    App.Audio.Beep();
                    App.Toast(fx.word + " is close!", Sk.Hex("#FFD43B"), Sk.Ink, 1.4f, 118f);
                    break;
                case "teamDone":
                    App.Audio.Jingle();
                    var place = fx.place == 1 ? "finished first! +30" : "finished! +15";
                    App.Toast((team?.name ?? "A team") + " " + place, teamColor, Sk.Ink, 1.8f, 118f);
                    SkBridge.Vibrate(30);
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
                paper.CancelStroke();
                paper.SetStrokes(null);
                tools.Reset();
                buffer = "";
                resultsFor = -1;
                lastCount = -1;
                if (reveal != null && reveal.roundId != builtRound) reveal = null;
            }
            paper.Editable = IsDrawer && s.phase == "drawing";
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
            if (IsDrawer) roleLine.text = "<color=" + me.color + ">YOU'RE DRAWING</color>  ·  " + Escape(guesser?.name ?? "your partner") + " is guessing";
            else if (IsGuesser) roleLine.text = "<color=" + me.color + ">YOU'RE GUESSING</color>  ·  " + Escape(drawer?.name ?? "your partner") + " is drawing";
            else roleLine.text = "Watching this round";
            Ui.Place(roleLine, x0, 116f, col, 22f);

            var shortScreen = H < 760f;
            var sentenceH = sentence.Render(mine?.words, MyColor, s.round.roundId, x0, 146f, col, true, shortScreen ? 16 : 18);
            var canvasTop = 146f + sentenceH + 12f;

            // The bottom area depends on the role.
            var drawing = s.phase == "drawing";
            var guessing = IsGuesser;
            tools.Root.gameObject.SetActive(IsDrawer);
            feed.gameObject.SetActive(guessing);
            inputBox.Root.gameObject.SetActive(guessing);
            keyboard.Root.gameObject.SetActive(guessing);
            var keyH = shortScreen ? 36f : 42f;
            float bottomH;
            if (IsDrawer) bottomH = 34f + 3f + 10f + 37f + 16f;
            else if (guessing) bottomH = 28f + 8f + 48f + 10f + (4f * keyH + 4f * 4f + 3f * 5f) + 12f;
            else bottomH = 16f;
            var available = H - bottomH - canvasTop - 12f;
            var canvasW = col;
            var canvasH = canvasW * 0.75f;
            if (canvasH > available)
            {
                canvasH = Mathf.Max(120f, available);
                canvasW = canvasH / 0.75f;
            }
            Ui.Place(paperFrame.Root, (W - canvasW) / 2f, canvasTop, canvasW, canvasH);
            Ui.Place(countdown, (W - canvasW) / 2f, canvasTop, canvasW, canvasH);
            var y = canvasTop + canvasH + 12f;
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
                keyboard.SetAccent(MyColor);
                keyboard.Layout(x0, y, col, keyH);
                keyboard.SetOff(!drawing || !App.Connected);
            }

            // Countdown overlay on the paper
            countdown.gameObject.SetActive(s.phase == "countdown");
            if (s.phase == "countdown")
            {
                countdownText.text = IsDrawer ? "Get ready to draw your sentence.\nNo letters or numbers!"
                    : IsGuesser ? "Get ready to guess " + Colored(drawer?.name ?? "your partner", me.color) + "'s drawing."
                    : "Get ready!";
                Ui.Place(countdownNumber, 0f, canvasH * 0.12f, canvasW, canvasH * 0.45f);
                Ui.Place(countdownText, 16f, canvasH * 0.6f, canvasW - 32f, canvasH * 0.36f);
            }

            LayoutResults(s);
        }

        void LayoutFeed(RoundTeam mine, float x, float y, float width)
        {
            Ui.Clear(feed);
            Ui.Place(feed, x, y, width, 28f);
            if (mine?.guesses == null) return;
            var px = 0f;
            for (var i = mine.guesses.Length - 1; i >= 0; i--)
            {
                var g = mine.guesses[i];
                var bg = g.result == "correct" ? Sk.Ok : g.result == "close" ? Sk.Hex("#FFD43B") : Sk.Mat2;
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
            // Panels size to their content, then sit centered in the space between title and footer.
            if (side)
            {
                var pw = (col - 16f) / 2f;
                var used = 0f;
                for (var i = 0; i < 2; i++) used = Mathf.Max(used, panels[i].Layout(s, i, x0 + i * (pw + 16f), top, pw, bottom - top));
                var offset = Mathf.Max(0f, (bottom - top - used) / 2f);
                for (var i = 0; i < 2; i++) panels[i].Layout(s, i, x0 + i * (pw + 16f), top + offset, pw, bottom - top);
            }
            else
            {
                var each = (bottom - top - 12f) / 2f;
                var heights = new float[2];
                for (var i = 0; i < 2; i++) heights[i] = panels[i].Layout(s, i, x0, top, col, each);
                var offset = Mathf.Max(0f, (bottom - top - heights[0] - heights[1] - 12f) / 2f);
                panels[0].Layout(s, 0, x0, top + offset, col, each);
                panels[1].Layout(s, 1, x0, top + offset + heights[0] + 12f, col, each);
            }
        }

        // ---------- per frame ----------

        public override void Tick()
        {
            var s = S;
            if (s == null || !s.HasRound) return;
            mute.Refresh(App.Audio.Muted);
            var remaining = (float)(s.round.phaseEndsAt - App.ServerNowMs());
            var total = s.phase == "drawing" && s.round.drawStartedAt > 0 ? s.round.phaseEndsAt - s.round.drawStartedAt : 90000f;
            var secs = Mathf.Max(0, Mathf.CeilToInt(remaining / 1000f));
            var ratio = s.phase == "drawing" ? Mathf.Clamp01(remaining / Mathf.Max(1f, total)) : s.phase == "countdown" ? 1f : 0f;
            var danger = s.phase == "drawing" && secs <= 10;
            seconds.text = s.phase == "drawing" ? secs.ToString() : s.phase == "countdown" ? "90" : "0";
            seconds.color = danger ? Sk.Danger : Sk.Text;
            timerFill.color = danger ? Sk.Danger : Sk.Cream;
            Ui.Place(timerFill, 0f, 0f, Mathf.Max(ratio > 0f ? 8f : 0f, timerTrack.rectTransform.sizeDelta.x * ratio), 8f);
            if (danger && secs != lastSecond && secs > 0) App.Audio.Tick();
            lastSecond = secs;

            if (s.phase == "countdown")
            {
                var n = Mathf.Clamp(secs, 1, 9);
                if (n != lastCount)
                {
                    lastCount = n;
                    countdownNumber.text = n.ToString();
                    App.Audio.Beep();
                    if (!App.ReducedMotion)
                    {
                        var rt = countdownNumber.rectTransform;
                        Tween.Run(rt, 0.25f, t => { var k = Mathf.LerpUnclamped(0.6f, 1f, Tween.OutBack(t)); rt.localScale = new Vector3(k, k, 1f); });
                    }
                }
            }
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
            paper.Editable = IsDrawer && s.phase == "drawing" && App.Connected;
        }
    }

    // A team's score pill in the HUD: color dot, name, score and words found this round.
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
            detail.text = round == null ? "" : round.done ? "DONE" : round.found + "/" + round.total;
            detail.color = round != null && round.done ? Sk.Ok : Sk.Muted;
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
        Framed bg;
        Text header;
        Text points;
        Framed paperFrame;
        PaperCanvas paper;
        SentenceView words;

        public static ResultPanel Create(Transform parent)
        {
            var panel = new ResultPanel { Root = Ui.Node("ResultPanel", parent) };
            panel.bg = Ui.Frame(panel.Root, Sk.Line, Sk.Mat, 20f, 1f);
            Ui.Stretch(panel.bg.Root);
            panel.header = Ui.Label(panel.Root, "", Sk.Display, 16, Sk.Text, TextAnchor.MiddleLeft);
            panel.header.supportRichText = true;
            panel.points = Ui.Label(panel.Root, "", Sk.Display, 16, Sk.Ok, TextAnchor.MiddleRight);
            panel.paperFrame = Ui.Frame(panel.Root, Sk.Line, PaperCanvas.Paper, 12f, 2f);
            panel.paper = PaperCanvas.Create(panel.paperFrame.Inner.transform, 320, 240, "ResultPaper");
            Ui.Stretch((RectTransform)panel.paper.transform, 4f, 4f, 4f, 4f);
            panel.paper.Image.raycastTarget = false;
            panel.words = SentenceView.Create(panel.Root);
            return panel;
        }

        public void SetStrokes(Stroke[] strokes) => paper.SetStrokes(strokes);

        // Lays the panel out within maxHeight and returns the height it actually needs.
        public float Layout(RoomState s, int index, float x, float y, float width, float maxHeight)
        {
            if (s.teams == null || index >= s.teams.Length) return 0f;
            var team = s.teams[index];
            var round = s.RoundTeam(team.id);
            var drawer = s.Player(round?.drawerId);
            header.text = "<color=" + team.color + ">" + team.name.ToUpperInvariant() + "</color>  <size=12><color=#A19BBE>drawn by "
                + (drawer == null ? "?" : drawer.name.Replace("<", "‹").Replace(">", "›")) + "</color></size>";
            Ui.Place(header, 16f, 8f, width - 120f, 30f);
            points.text = round == null ? "" : round.done ? (round.place == 1 ? "1ST  +" : "2ND  +") + round.points : "+" + round.points;
            points.color = round != null && round.done ? Sk.Ok : Sk.Muted;
            Ui.Place(points, width - 136f, 8f, 120f, 30f);
            var key = s.round.roundId * 10 + index;
            var wordsH = words.Render(round?.words, Sk.Hex(team.color), key, 12f, 0f, width - 24f, false, 13);
            var maxCanvasH = maxHeight - 44f - wordsH - 24f;
            var canvasW = width - 24f;
            var canvasH = canvasW * 0.75f;
            if (canvasH > maxCanvasH)
            {
                canvasH = Mathf.Max(60f, maxCanvasH);
                canvasW = canvasH / 0.75f;
            }
            Ui.Place(paperFrame.Root, (width - canvasW) / 2f, 42f, canvasW, canvasH);
            words.Render(round?.words, Sk.Hex(team.color), key, 12f, 42f + canvasH + 10f, width - 24f, false, 13);
            var used = 42f + canvasH + 10f + wordsH + 14f;
            Ui.Place(Root, x, y, width, used);
            return used;
        }
    }
}
