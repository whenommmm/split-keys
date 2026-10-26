using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // Countdown, playing and round-result phases.
    public sealed class GameScreen : SkScreen
    {
        // HUD
        readonly Image[] hearts = new Image[3];
        readonly Text level;
        readonly Text seconds;
        readonly MuteButton mute;
        readonly Image timerTrack;
        readonly Image timerFill;
        readonly RectTransform chipsRoot;
        readonly List<Chip> chips = new List<Chip>();
        string chipsSignature;
        float chipsBottom;

        // Phrase board
        readonly RectTransform boardHolder;
        readonly RectTransform board;
        readonly List<TileView> tiles = new List<TileView>();
        int builtRound = -1;
        string[] previousTyped;

        // NEXT callout
        readonly Text nextLabel;
        readonly Keycap nextCap;
        float nextCapSize;
        float nextCapX, nextCapY;

        // Key tray
        readonly RectTransform tray;
        readonly Image trayBorder;
        readonly Image trayBg;
        readonly Text trayTitle;
        readonly Image traySwatch;
        readonly Text trayColor;
        readonly Text trayTip;
        readonly Text spectator;
        readonly RectTransform keysRoot;
        readonly CanvasGroup keysGroup;
        readonly List<Keycap> keys = new List<Keycap>();
        readonly List<char> keyLetters = new List<char>();
        string keysSignature;
        bool flipKeys;
        float trayShakeUntil;

        // Reshuffle banner
        readonly RectTransform banner;

        // Round result
        readonly RectTransform result;
        readonly Image dim;
        readonly Framed card;
        readonly Image resultHeart;
        readonly Text headline;
        readonly Text sub;
        readonly RectTransform resultBoard;
        readonly Text extra;
        readonly Text foot;
        readonly Image progressTrack;
        readonly Image progressFill;
        readonly RectTransform confetti;
        readonly List<TileView> resultTiles = new List<TileView>();
        int resultShownFor = -1;

        int lastCountdown = -1;
        int lastSecond = -1;
        bool goPlayed;
        int previousLives = -1;
        int previousLevel = -1;

        public GameScreen(SplitKeysApp app) : base(app, "Game")
        {
            for (var i = 0; i < 3; i++) hearts[i] = Ui.Icon(Root, SkGfx.Heart, Sk.Danger, "Heart");
            level = Ui.Label(Root, "LEVEL 1", Sk.Display, 22, Sk.Text, TextAnchor.MiddleCenter);
            level.horizontalOverflow = HorizontalWrapMode.Overflow;
            seconds = Ui.Label(Root, "20", Sk.Display, 22, Sk.Text, TextAnchor.MiddleRight);
            mute = MuteButton.Create(Root, app.ToggleMute);
            timerTrack = Ui.Box(Root, Sk.Mat2, 5f, "TimerTrack");
            timerFill = Ui.Box(timerTrack.transform, Sk.Cream, 5f, "TimerFill");
            chipsRoot = Ui.Node("Chips", Root);

            boardHolder = Ui.Node("BoardHolder", Root);
            board = Ui.Node("Board", boardHolder);

            nextLabel = Ui.Label(Root, "NEXT", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleCenter);
            nextLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            nextCap = Keycap.Create(Root, "", Sk.Display, 46, Sk.Slate, Sk.Text, 5f, 16f);
            nextCap.SetColors(Sk.Slate, Sk.Text, 0.12f);
            nextCap.Interactive = false;

            tray = Ui.Node("Tray", Root);
            trayBorder = Ui.Box(tray, Sk.Line, 23f, "TrayBorder");
            trayBg = Ui.Box(tray, Sk.Mat, 22f, "TrayBg");
            trayTitle = Ui.Label(tray, "YOUR KEYS", Sk.Bold, 12, Sk.Muted, TextAnchor.MiddleLeft);
            traySwatch = Ui.Box(tray, Color.white, 4f, "Swatch");
            trayColor = Ui.Label(tray, "", Sk.Bold, 14, Color.white, TextAnchor.MiddleRight);
            trayColor.horizontalOverflow = HorizontalWrapMode.Overflow;
            trayTip = Ui.Label(tray, "You can type these on your keyboard", Sk.Body, 13, Sk.Muted, TextAnchor.MiddleCenter);
            spectator = Ui.Label(tray, "You're in! You'll get letters next round.", Sk.Bold, 16, Sk.Text, TextAnchor.MiddleCenter);
            keysRoot = Ui.Node("Keys", tray);
            keysGroup = keysRoot.gameObject.AddComponent<CanvasGroup>();

            banner = Ui.Node("Banner", Root);
            var bannerBg = Ui.Box(banner, Sk.Ink, 0f);
            Ui.Stretch(bannerBg.rectTransform);
            var bannerText = Ui.Label(banner, "RESHUFFLE!", Sk.Display, 28, Sk.Cream, TextAnchor.MiddleCenter);
            Ui.Stretch(bannerText.rectTransform);
            banner.gameObject.SetActive(false);

            result = Ui.Node("Result", Root);
            dim = Ui.Box(result, new Color(10f / 255f, 8f / 255f, 18f / 255f, 0.86f), 0f, "Dim");
            dim.raycastTarget = true;
            Ui.Stretch(dim.rectTransform);
            confetti = Ui.Node("Confetti", result);
            card = Ui.Frame(result, Sk.Line, Sk.Mat, 22f, 1f, "Card");
            resultHeart = Ui.Icon(card.Root, SkGfx.Heart, Sk.Danger, "Heart");
            headline = Ui.Label(card.Root, "", Sk.Display, 36, Sk.Ok, TextAnchor.MiddleCenter);
            headline.horizontalOverflow = HorizontalWrapMode.Overflow;
            sub = Ui.Label(card.Root, "", Sk.Bold, 18, Sk.Text, TextAnchor.MiddleCenter);
            resultBoard = Ui.Node("ResultBoard", card.Root);
            extra = Ui.Label(card.Root, "", Sk.Body, 15, Sk.Text, TextAnchor.MiddleCenter);
            extra.supportRichText = true;
            foot = Ui.Label(card.Root, "", Sk.Body, 14, Sk.Muted, TextAnchor.MiddleLeft);
            progressTrack = Ui.Box(card.Root, Sk.Mat2, 2f, "ProgressTrack");
            progressFill = Ui.Box(progressTrack.transform, Sk.Cream, 2f, "ProgressFill");
            result.gameObject.SetActive(false);
        }

        public override void OnShow()
        {
            previousLives = App.State?.lives ?? -1;
            previousLevel = App.State?.RoundLevel ?? -1;
            lastSecond = -1;
        }

        RoomState S => App.State;

        // ---------- rendering ----------

        public override void Render()
        {
            var s = S;
            if (s == null || !s.HasRound) return;
            var r = s.round;
            if (r.roundId != builtRound)
            {
                BuildTiles(r);
                builtRound = r.roundId;
                previousTyped = null;
                lastCountdown = -1;
                goPlayed = false;
            }
            RebuildChips(s);
            RebuildKeys(s, r);
            Layout();
            UpdateTiles(s, r);
            UpdateHud(s);
            UpdateResult(s, r);
            previousTyped = r.typedBy == null ? null : (string[])r.typedBy.Clone();
        }

        void BuildTiles(RoundState r)
        {
            Ui.Clear(board);
            tiles.Clear();
            foreach (var c in r.phrase) tiles.Add(c == ' ' ? null : TileView.Create(board, c, 40f));
            resultShownFor = -1;
        }

        void RebuildChips(RoomState s)
        {
            var signature = (App.SelfId ?? "") + "|";
            foreach (var p in s.players) signature += p.id + (p.connected ? "1" : "0") + p.name + ";";
            if (signature == chipsSignature) return;
            chipsSignature = signature;
            Ui.Clear(chipsRoot);
            chips.Clear();
            var ordered = new List<PlayerState>(s.players);
            ordered.Sort((a, b) => (b.id == App.SelfId).CompareTo(a.id == App.SelfId));
            foreach (var p in ordered) chips.Add(Chip.Create(chipsRoot, p, p.id == App.SelfId));
        }

        List<char> OwnedLetters(RoundState r)
        {
            var owned = new List<char>();
            if (r.ownerLetters == null) return owned;
            foreach (var entry in r.ownerLetters)
                if (entry.playerId == App.SelfId && !string.IsNullOrEmpty(entry.letter)) owned.Add(entry.letter[0]);
            owned.Sort();
            if (r.twists != null && r.twists.shuffleKeys)
            {
                var random = new System.Random(r.roundId * 7919 + (App.SelfId ?? "").GetHashCode());
                for (var i = owned.Count - 1; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    (owned[i], owned[j]) = (owned[j], owned[i]);
                }
            }
            return owned;
        }

        void RebuildKeys(RoomState s, RoundState r)
        {
            var owned = OwnedLetters(r);
            var self = s.Player(App.SelfId);
            var signature = new string(owned.ToArray()) + "|" + (self?.color ?? "");
            if (signature == keysSignature) return;
            keysSignature = signature;
            Ui.Clear(keysRoot);
            keys.Clear();
            keyLetters.Clear();
            var color = self == null ? Sk.Cream : Sk.Hex(self.color);
            for (var i = 0; i < owned.Count; i++)
            {
                var letter = owned[i];
                var key = Keycap.Create(keysRoot, letter.ToString(), Sk.Display, 30, color, Sk.Ink, 5f, 14f, true, "Key " + letter);
                key.OnDown = () => App.PressLetter(letter);
                keys.Add(key);
                keyLetters.Add(letter);
                if (flipKeys && !App.ReducedMotion)
                {
                    var rt = key.Body;
                    Tween.Run(rt, 0.28f, t => rt.localScale = new Vector3(Mathf.Abs(Mathf.Cos(t * Mathf.PI)), 1f, 1f), () => rt.localScale = Vector3.one, i * 0.03f);
                    rt.localScale = new Vector3(1f, 1f, 1f);
                }
            }
            flipKeys = false;
        }

        // ---------- layout ----------

        public override void Layout()
        {
            var s = S;
            if (s == null || !s.HasRound) return;
            var r = s.round;
            float W = App.W, H = App.H;
            var col = Mathf.Min(W - 32f, 720f);
            var x0 = (W - col) / 2f;

            // HUD row
            for (var i = 0; i < 3; i++) Ui.Place(hearts[i], x0 + i * 28f, 18f, 24f, 24f);
            Ui.Place(level, x0 + 100f, 8f, col - 200f, 44f);
            mute.Place(x0 + col - 44f, 8f);
            Ui.Place(seconds, x0 + col - 44f - 70f, 8f, 60f, 44f);
            Ui.Place(timerTrack, x0, 58f, col, 10f);

            // Chips wrap onto extra rows when they don't fit.
            var cy = 78f;
            var rowStart = 0;
            var rowWidth = 0f;
            for (var i = 0; i <= chips.Count; i++)
            {
                var fits = i < chips.Count && (rowWidth == 0f || rowWidth + 8f + chips[i].Width <= col);
                if (fits)
                {
                    rowWidth += (rowWidth == 0f ? 0f : 8f) + chips[i].Width;
                    continue;
                }
                var cx = x0 + (col - rowWidth) / 2f;
                for (var j = rowStart; j < i; j++)
                {
                    chips[j].Place(cx, cy);
                    cx += chips[j].Width + 8f;
                }
                if (i == chips.Count) break;
                cy += 36f;
                rowStart = i;
                rowWidth = chips[i].Width;
            }
            chipsBottom = cy + 30f;

            // Key tray, anchored to the bottom.
            var shortScreen = App.Short;
            int count = keys.Count, cols;
            float keyH;
            if (count <= 4) { cols = Mathf.Max(1, count); keyH = shortScreen ? 64f : 76f; }
            else if (count <= 8) { cols = 4; keyH = shortScreen ? 58f : 68f; }
            else if (count <= 13) { cols = 5; keyH = shortScreen ? 54f : 64f; }
            else { cols = 7; keyH = shortScreen ? 46f : 52f; }
            var gap = cols == 7 ? 8f : 10f;
            var rowsCount = count == 0 ? 0 : Mathf.CeilToInt(count / (float)cols);
            var keyW = Mathf.Min((col - (cols - 1) * gap) / cols, 110f);
            var gridW = cols * keyW + (cols - 1) * gap;
            var bodyH = count == 0 ? 56f : rowsCount * (keyH + 5f) + (rowsCount - 1) * gap;
            var trayH = 12f + 22f + 12f + bodyH + 18f;
            var trayW = W <= 760f ? W : col + 32f;
            var trayX = (W - trayW) / 2f;
            var trayTop = H - trayH;
            Ui.Place(tray, trayX, trayTop, trayW, trayH);
            Ui.Place(trayBorder, 0f, -1f, trayW, trayH + 40f);
            Ui.Place(trayBg, 1f, 0f, trayW - 2f, trayH + 40f);
            if (W <= 760f) { Ui.Place(trayBorder, -1f, -1f, trayW + 2f, trayH + 40f); Ui.Place(trayBg, 0f, 0f, trayW, trayH + 40f); }
            var innerX = (trayW - col) / 2f;
            Ui.Place(trayTitle, innerX, 12f, 120f, 22f);
            var self = S.Player(App.SelfId);
            trayColor.text = self == null ? "" : Sk.ColorName(self.color);
            trayColor.color = self == null ? Sk.Muted : Sk.Hex(self.color);
            traySwatch.color = trayColor.color;
            var colorW = Ui.Width(trayColor);
            Ui.Place(trayColor, innerX + col - colorW - 2f, 12f, colorW + 2f, 22f);
            Ui.Place(traySwatch, innerX + col - colorW - 22f, 16f, 14f, 14f);
            trayTip.gameObject.SetActive(App.Wide && !App.IsTouch && count > 0);
            Ui.Place(trayTip, innerX + 120f, 12f, col - 240f - colorW, 22f);
            spectator.gameObject.SetActive(count == 0);
            Ui.Place(spectator, innerX, 46f, col, 56f);
            Ui.Place(keysRoot, innerX + (col - gridW) / 2f, 46f, gridW, bodyH);
            for (var i = 0; i < keys.Count; i++)
            {
                var row = i / cols;
                var column = i % cols;
                var inRow = Mathf.Min(cols, count - row * cols);
                var rowOffset = (gridW - (inRow * keyW + (inRow - 1) * gap)) / 2f;
                keys[i].Label.fontSize = keyH >= 60f ? 30 : 26;
                keys[i].SetSize(keyW, keyH);
                Ui.Place(keys[i].Root, rowOffset + column * (keyW + gap), row * (keyH + 5f + gap), keyW, keyH + 5f);
            }

            // NEXT callout above the tray.
            nextCapSize = shortScreen ? 68f : 84f;
            var nextTop = trayTop - 16f - (16f + 6f + nextCapSize + 5f);
            Ui.Place(nextLabel, x0, nextTop, col, 16f);
            nextCapX = (W - nextCapSize) / 2f;
            nextCapY = nextTop + 22f;
            PlaceNextCap(false);

            // Phrase board fills what's left.
            var top = chipsBottom + 16f;
            var bottomEdge = nextTop - 16f;
            LayoutBoard(r, x0, col, top, Mathf.Max(60f, bottomEdge - top));
            Ui.Place(banner, 0f, top + (bottomEdge - top) / 2f - 32f, W, 64f);
            banner.pivot = new Vector2(0.5f, 0.5f);
            banner.anchoredPosition = new Vector2(W / 2f, -(top + (bottomEdge - top) / 2f));
            banner.localEulerAngles = new Vector3(0f, 0f, -3f);

            LayoutResult(s, r);
        }

        void PlaceNextCap(bool go)
        {
            var w = go ? nextCapSize * 1.6f : nextCapSize;
            nextCap.SetSize(w, nextCapSize);
            Ui.Place(nextCap.Root, (App.W - w) / 2f, nextCapY, w, nextCapSize + 5f);
            nextCap.Label.fontSize = Mathf.RoundToInt(nextCapSize * (go ? 0.4f : 0.55f));
        }

        float BaseTile(int letters)
        {
            float W = App.W;
            float tile;
            if (App.Wide) tile = letters <= 9 ? 60f : letters <= 17 ? 54f : letters <= 25 ? 48f : 44f;
            else tile = letters <= 9 ? Mathf.Min(48f, 0.11f * W) : letters <= 17 ? Mathf.Min(42f, 0.095f * W) : letters <= 25 ? Mathf.Min(36f, 0.084f * W) : Mathf.Min(32f, 0.076f * W);
            if (App.Short && !App.Wide) tile *= 0.88f;
            return tile;
        }

        static List<(int start, int length)> Words(string phrase)
        {
            var words = new List<(int, int)>();
            var start = 0;
            for (var i = 0; i <= phrase.Length; i++)
            {
                if (i < phrase.Length && phrase[i] != ' ') continue;
                if (i > start) words.Add((start, i - start));
                start = i + 1;
            }
            return words;
        }

        static List<List<(int start, int length)>> Rows(List<(int start, int length)> words, float tile, float maxW)
        {
            var rows = new List<List<(int, int)>>();
            var current = new List<(int, int)>();
            var width = 0f;
            foreach (var word in words)
            {
                var ww = word.length * tile + (word.length - 1) * 4f;
                if (current.Count > 0 && width + tile * 0.45f + ww > maxW)
                {
                    rows.Add(current);
                    current = new List<(int, int)>();
                    width = 0f;
                }
                width += (current.Count > 0 ? tile * 0.45f : 0f) + ww;
                current.Add(word);
            }
            if (current.Count > 0) rows.Add(current);
            return rows;
        }

        static float RowWidth(List<(int start, int length)> row, float tile)
        {
            var w = 0f;
            for (var i = 0; i < row.Count; i++) w += (i > 0 ? tile * 0.45f : 0f) + row[i].length * tile + (row[i].length - 1) * 4f;
            return w;
        }

        float FitTile(string phrase, int letters, float maxW, float maxH, out List<List<(int start, int length)>> rows)
        {
            var words = Words(phrase);
            var tile = BaseTile(letters);
            while (true)
            {
                rows = Rows(words, tile, maxW);
                var widest = 0f;
                foreach (var row in rows) widest = Mathf.Max(widest, RowWidth(row, tile));
                var height = rows.Count * (tile * 1.15f + 3f) + (rows.Count - 1) * 14f;
                if ((widest <= maxW && height <= maxH) || tile <= 16f) return tile;
                tile *= 0.92f;
            }
        }

        void LayoutBoard(RoundState r, float x0, float col, float top, float height)
        {
            var tile = FitTile(r.phrase, r.LetterTotal(), col, height, out var rows);
            var total = rows.Count * (tile * 1.15f + 3f) + (rows.Count - 1) * 14f;
            Ui.Place(boardHolder, 0f, 0f, App.W, App.H);
            Ui.Place(board, 0f, 0f, App.W, App.H);
            var y = top + (height - total) / 2f;
            foreach (var row in rows)
            {
                var x = x0 + (col - RowWidth(row, tile)) / 2f;
                foreach (var (start, length) in row)
                {
                    for (var i = 0; i < length; i++)
                    {
                        var t = tiles[start + i];
                        if (t == null) continue;
                        t.Resize(tile);
                        t.Place(x, y);
                        x += tile + 4f;
                    }
                    x += tile * 0.45f - 4f;
                }
                y += tile * 1.15f + 3f + 14f;
            }
        }

        // ---------- per-state updates ----------

        static int WordStart(string phrase, int cursor)
        {
            var start = Mathf.Clamp(cursor, 0, phrase.Length);
            while (start > 0 && phrase[start - 1] != ' ') start--;
            return start;
        }

        void UpdateTiles(RoomState s, RoundState r)
        {
            var live = s.phase == "playing" || s.phase == "countdown";
            var wordStart = WordStart(r.phrase, r.cursor);
            var wordEnd = wordStart;
            while (wordEnd < r.phrase.Length && r.phrase[wordEnd] != ' ') wordEnd++;
            for (var i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if (tile == null) continue;
                var typer = r.TypedBy(i);
                if (typer != null)
                {
                    var player = s.Player(typer);
                    tile.Set(TileView.Kind.Typed, player == null ? Sk.Muted : Sk.Hex(player.color));
                }
                else if (live && i == r.cursor) tile.Set(TileView.Kind.Cursor, Color.white);
                else if (live && i >= wordStart && i < wordEnd) tile.Set(TileView.Kind.CurrentWord, Color.white);
                else tile.Set(TileView.Kind.Waiting, Color.white);

                if (previousTyped == null || App.ReducedMotion) continue;
                var before = i < previousTyped.Length && !string.IsNullOrEmpty(previousTyped[i]);
                if (!before && typer != null) tile.SnapIn();
                else if (before && typer == null) tile.Erase();
            }
        }

        void UpdateHud(RoomState s)
        {
            var lives = Mathf.Clamp(s.lives, 0, 3);
            for (var i = 0; i < 3; i++)
            {
                var full = i < lives;
                hearts[i].sprite = full ? SkGfx.Heart : SkGfx.HeartLine;
                hearts[i].color = full ? Sk.Danger : Sk.Line;
            }
            if (previousLives >= 0 && lives < previousLives && lives < 3 && !App.ReducedMotion)
            {
                var heart = hearts[lives].rectTransform;
                var image = hearts[lives];
                Tween.Run(heart, 0.5f, t =>
                {
                    image.sprite = SkGfx.Heart;
                    image.color = Color.Lerp(Sk.Danger, Sk.Line, t);
                    heart.localEulerAngles = new Vector3(0f, 0f, 20f * Mathf.Sin(t * Mathf.PI * 3f) * (1f - t));
                }, () =>
                {
                    image.sprite = SkGfx.HeartLine;
                    image.color = Sk.Line;
                    heart.localEulerAngles = Vector3.zero;
                });
            }
            previousLives = lives;

            var roundLevel = s.RoundLevel;
            level.text = "LEVEL " + roundLevel;
            if (previousLevel >= 0 && roundLevel != previousLevel && !App.ReducedMotion)
            {
                var rt = level.rectTransform;
                Tween.Run(rt, 0.35f, t => { var k = 1f + 0.25f * Mathf.Sin(t * Mathf.PI); rt.localScale = new Vector3(k, k, 1f); });
            }
            previousLevel = roundLevel;
            foreach (var chip in chips)
            {
                var p = s.Player(chip.PlayerId);
                if (p != null) chip.Set(p);
            }
        }

        // ---------- result overlay ----------

        void UpdateResult(RoomState s, RoundState r)
        {
            var show = s.phase == "roundEnd";
            if (!show)
            {
                result.gameObject.SetActive(false);
                return;
            }
            if (resultShownFor == r.roundId) return;
            resultShownFor = r.roundId;
            result.gameObject.SetActive(true);
            var cleared = r.result == "cleared";
            if (cleared)
            {
                App.Audio.Jingle();
                SkBridge.Vibrate(30);
                Confetti(s);
            }
            else
            {
                App.Audio.TimeUp();
                SkBridge.Vibrate(200);
                Ui.Clear(confetti);
            }
            if (!App.ReducedMotion)
            {
                var rt = card.Root;
                Tween.Run(rt, 0.4f, t => { var k = Mathf.LerpUnclamped(0.92f, 1f, Tween.OutBack(t)); rt.localScale = new Vector3(k, k, 1f); });
            }
            LayoutResult(s, r);
        }

        void LayoutResult(RoomState s, RoundState r)
        {
            if (!result.gameObject.activeSelf) return;
            float W = App.W, H = App.H;
            Ui.Place(result, 0f, 0f, W, H);
            Ui.Place(dim, 0f, 0f, W, H);
            Ui.Place(confetti, 0f, 0f, W, H);
            var cw = Mathf.Min(W - 32f, 400f);
            var inner = cw - 48f;
            var cleared = r.result == "cleared";
            var y = 24f;
            resultHeart.gameObject.SetActive(!cleared);
            if (!cleared)
            {
                Ui.Place(resultHeart, (cw - 36f) / 2f, y, 36f, 36f);
                y += 44f;
            }
            headline.text = cleared ? "CLEARED!" : "TIME'S UP";
            headline.color = cleared ? Sk.Ok : Sk.Danger;
            headline.fontSize = cw < 340f ? 30 : 36;
            Ui.Place(headline, 0f, y, cw, 44f);
            y += 48f;
            if (cleared) sub.text = (Mathf.Max(0, r.timeLeftMs) / 1000f).ToString("0.0") + "s to spare";
            else sub.text = s.lives <= 0 ? "Out of hearts" : s.lives == 1 ? "Last heart!" : s.lives + " hearts left";
            Ui.Place(sub, 0f, y, cw, 26f);
            y += 26f + 16f;

            // Mini mosaic of the phrase.
            Ui.Clear(resultBoard);
            resultTiles.Clear();
            var words = Words(r.phrase);
            var tile = Mathf.Min(30f, BaseTile(r.LetterTotal()) * 0.75f);
            List<List<(int start, int length)>> rows;
            while (true)
            {
                rows = Rows(words, tile, inner);
                var widest = 0f;
                foreach (var row in rows) widest = Mathf.Max(widest, RowWidth(row, tile));
                if (widest <= inner || tile <= 14f) break;
                tile *= 0.92f;
            }
            var boardH = rows.Count * (tile * 1.15f + 3f) + (rows.Count - 1) * 8f;
            Ui.Place(resultBoard, 24f, y, inner, boardH);
            var by = 0f;
            foreach (var row in rows)
            {
                var x = (inner - RowWidth(row, tile)) / 2f;
                foreach (var (start, length) in row)
                {
                    for (var i = 0; i < length; i++)
                    {
                        var index = start + i;
                        var t = TileView.Create(resultBoard, r.phrase[index], tile);
                        var typer = r.TypedBy(index);
                        var player = s.Player(typer);
                        t.Set(typer != null ? TileView.Kind.Typed : TileView.Kind.Waiting, player == null ? Sk.Muted : Sk.Hex(player.color));
                        t.Place(x, by);
                        resultTiles.Add(t);
                        x += tile + 4f;
                    }
                    x += tile * 0.45f - 4f;
                }
                by += tile * 1.15f + 3f + 8f;
            }
            y += boardH + 16f;

            if (cleared)
            {
                var last = s.Player(r.lastPlayerId);
                extra.text = last == null ? "" : Colored(last.name, last.color) + " hit the last key";
            }
            else extra.text = r.LettersTyped() + " of " + r.LetterTotal() + " letters";
            Ui.Place(extra, 0f, y, cw, 22f);
            y += 22f + 18f;
            foot.text = cleared ? "Level " + s.level + " next" : s.lives <= 0 ? "Final results…" : "New phrase, same level";
            Ui.Place(foot, 24f, y, inner, 18f);
            y += 24f;
            Ui.Place(progressTrack, 24f, y, inner, 4f);
            y += 4f + 24f;
            Ui.Place(card.Root, (W - cw) / 2f, (H - y) / 2f, cw, y);
            card.Root.pivot = new Vector2(0.5f, 0.5f);
            card.Root.anchoredPosition = new Vector2(W / 2f, -H / 2f);
        }

        void Confetti(RoomState s)
        {
            Ui.Clear(confetti);
            if (App.ReducedMotion) return;
            var random = new System.Random();
            var colors = new List<Color>();
            foreach (var p in s.players) colors.Add(Sk.Hex(p.color));
            if (colors.Count == 0) colors.Add(Sk.Cream);
            var W = App.W;
            var H = App.H;
            for (var i = 0; i < 24; i++)
            {
                var piece = Ui.Box(confetti, colors[i % colors.Count], 2f, "Piece");
                var rt = piece.rectTransform;
                var startX = W / 2f + (float)(random.NextDouble() - 0.5) * Mathf.Min(W, 400f);
                var startY = H / 2f - 160f;
                var drift = (float)(random.NextDouble() - 0.5) * 160f;
                var fall = 260f + (float)random.NextDouble() * 220f;
                var spin = (float)(random.NextDouble() - 0.5) * 720f;
                var delay = (float)random.NextDouble() * 0.25f;
                Ui.Place(rt, startX, startY, 8f, 8f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                Tween.Run(rt, 1.4f, t =>
                {
                    rt.anchoredPosition = new Vector2(startX + drift * t, -(startY + fall * t * t - 80f * Mathf.Sin(t * Mathf.PI)));
                    rt.localEulerAngles = new Vector3(0f, 0f, spin * t);
                    piece.color = Sk.Alpha(piece.color, t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f);
                }, null, delay);
            }
        }

        // ---------- per frame ----------

        public override void Tick()
        {
            var s = S;
            if (s == null || !s.HasRound) return;
            var r = s.round;
            mute.Refresh(App.Audio.Muted);
            var remaining = (float)(r.phaseEndsAt - App.ServerNowMs());
            var playing = s.phase == "playing";
            var countdown = s.phase == "countdown";

            // Timer
            float ratio;
            int secs;
            if (countdown) { ratio = 1f; secs = 20; }
            else if (playing) { ratio = Mathf.Clamp01(remaining / 20000f); secs = Mathf.Clamp(Mathf.CeilToInt(remaining / 1000f), 0, 20); }
            else { ratio = Mathf.Clamp01(r.timeLeftMs / 20000f); secs = Mathf.CeilToInt(r.timeLeftMs / 1000f); }
            var danger = playing && secs <= 5;
            seconds.text = secs.ToString();
            seconds.color = danger ? Sk.Danger : Sk.Text;
            var pulse = danger ? 0.75f + 0.25f * Mathf.Abs(Mathf.Cos(remaining / 1000f * Mathf.PI)) : 1f;
            timerFill.color = danger ? Sk.Alpha(Sk.Danger, pulse) : Sk.Cream;
            var trackW = timerTrack.rectTransform.sizeDelta.x;
            Ui.Place(timerFill, 0f, 0f, Mathf.Max(ratio > 0f ? 10f : 0f, trackW * ratio), 10f);
            if (playing && secs <= 5 && secs > 0 && secs != lastSecond) App.Audio.Tick();
            lastSecond = secs;

            // NEXT callout
            var self = s.Player(App.SelfId);
            var mine = self == null ? Sk.Cream : Sk.Hex(self.color);
            if (countdown)
            {
                var n = Mathf.Clamp(Mathf.CeilToInt(remaining / 1000f), 1, 3);
                nextLabel.text = "GET READY · LEARN YOUR LETTERS";
                nextLabel.color = Sk.Muted;
                if (n != lastCountdown)
                {
                    lastCountdown = n;
                    App.Audio.Beep();
                    PlaceNextCap(false);
                    nextCap.SetColors(Sk.Slate, Sk.Text, 0.12f);
                    nextCap.SetLabel(n.ToString());
                    PopNext();
                }
            }
            else if (playing && 20000f - remaining < 450f)
            {
                PlaceNextCap(true);
                nextCap.SetColors(Sk.Cream, Sk.Ink);
                nextCap.SetLabel("GO!");
                nextLabel.text = "";
                if (!goPlayed)
                {
                    goPlayed = true;
                    App.Audio.Go();
                    PopNext();
                }
            }
            else if (playing)
            {
                goPlayed = true;
                var letter = r.NextLetter;
                var owner = r.OwnerOf(letter);
                var hint = r.twists != null && r.twists.hint && owner == App.SelfId && owner != null;
                PlaceNextCap(false);
                nextCap.SetLabel(letter.ToString());
                nextCap.SetColors(hint ? mine : Sk.Slate, hint ? Sk.Ink : Sk.Text, hint ? 0.35f : 0.12f);
                nextLabel.text = hint ? "YOU!" : "NEXT";
                nextLabel.color = hint ? mine : danger ? Sk.Danger : Sk.Muted;
                var bob = hint && !App.ReducedMotion ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI / 0.6f)) * 4f : 0f;
                nextCap.Root.anchoredPosition = new Vector2(nextCap.Root.anchoredPosition.x, -(nextCapY - bob));
            }
            else
            {
                PlaceNextCap(false);
                nextCap.SetLabel("");
                nextCap.SetColors(Sk.Slate, Sk.Text, 0.12f);
                nextLabel.text = "NEXT";
                nextLabel.color = Sk.Muted;
            }

            // Keys: usable only while playing and connected; hint ring on the next letter.
            var usable = playing && App.Connected;
            var hintOn = playing && r.twists != null && r.twists.hint;
            for (var i = 0; i < keys.Count; i++)
            {
                if (keys[i].IsOff == usable) keys[i].SetOff(!usable);
                var hinted = hintOn && keyLetters[i] == r.NextLetter;
                if (hinted)
                {
                    var alpha = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.2f);
                    keys[i].AddRing(Sk.Alpha(Sk.Cream, alpha), 3f);
                }
                else if (keys[i].Ring != null) keys[i].Ring.color = Color.clear;
            }
            keysGroup.alpha = usable ? 1f : 0.55f;

            // Cursor blink
            var blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
            foreach (var tile in tiles) tile?.Blink(blink);

            // Tray shake for "Not your key"
            var shake = 0f;
            if (Time.unscaledTime < trayShakeUntil) shake = 6f * Tween.Shake(Mathf.Clamp01(1f - (trayShakeUntil - Time.unscaledTime) / 0.3f));
            tray.anchoredPosition = new Vector2((App.W - tray.sizeDelta.x) / 2f + shake, tray.anchoredPosition.y);

            // Result progress line
            if (result.gameObject.activeSelf && s.phase == "roundEnd")
            {
                var total = 3500f;
                var k = Mathf.Clamp01(1f - remaining / total);
                var w = progressTrack.rectTransform.sizeDelta.x;
                Ui.Place(progressFill, 0f, 0f, w * k, 4f);
            }
        }

        void PopNext()
        {
            if (App.ReducedMotion) return;
            var body = nextCap.Body;
            Tween.Run(body, 0.25f, t =>
            {
                var k = Mathf.LerpUnclamped(0.6f, 1f, Tween.OutBack(t));
                body.localScale = new Vector3(k, k, 1f);
            }, () => body.localScale = Vector3.one);
        }

        // ---------- input & effects ----------

        public void OnTypedChar(char c)
        {
            var s = S;
            if (s == null || !s.HasRound || s.phase != "playing") return;
            c = char.ToUpperInvariant(c);
            if (c < 'A' || c > 'Z') return;
            var index = keyLetters.IndexOf(c);
            if (index < 0)
            {
                trayShakeUntil = Time.unscaledTime + 0.3f;
                trayTitle.text = "NOT YOUR KEY";
                CancelInvokeTitle();
                return;
            }
            var key = keys[index];
            key.SetDown(true);
            Tween.Run(key, 0.09f, _ => { }, () => key.SetDown(false));
            App.PressLetter(c);
        }

        void CancelInvokeTitle()
        {
            Tween.Run(trayTitle, 0.8f, _ => { }, () => trayTitle.text = "YOUR KEYS");
        }

        public override void OnFx(Fx fx)
        {
            var s = S;
            if (s == null) return;
            var player = s.Player(fx.playerId);
            switch (fx.kind)
            {
                case "mistake":
                    App.Audio.Buzz();
                    if (player != null)
                        App.Toast(Escape(player.name) + " pressed " + fx.letter, Sk.Hex(player.color), Sk.Ink, 1.2f, chipsBottom + 8f, SkGfx.Burst);
                    foreach (var chip in chips) if (chip.PlayerId == fx.playerId) chip.Shake();
                    if (!App.ReducedMotion)
                    {
                        Tween.Run(boardHolder, 0.3f, t => boardHolder.anchoredPosition = new Vector2(3f * Tween.Shake(t), 0f), () => boardHolder.anchoredPosition = Vector2.zero);
                    }
                    if (fx.playerId == App.SelfId)
                    {
                        SkBridge.Vibrate(80);
                        Tween.Run(trayBorder, 0.4f, t => trayBorder.color = Color.Lerp(Sk.Danger, Sk.Line, t));
                    }
                    break;
                case "correct":
                    foreach (var chip in chips) if (chip.PlayerId == fx.playerId) chip.Bump();
                    break;
                case "reshuffle":
                    App.Audio.Whoosh();
                    flipKeys = true;
                    ShowBanner();
                    break;
            }
        }

        void ShowBanner()
        {
            banner.gameObject.SetActive(true);
            var W = App.W;
            var centerY = banner.anchoredPosition.y;
            Tween.Run(banner, 1f, t =>
            {
                float x;
                if (t < 0.2f) x = Mathf.Lerp(-W, 0f, Tween.OutCubic(t / 0.2f));
                else if (t < 0.8f) x = 0f;
                else x = Mathf.Lerp(0f, W, (t - 0.8f) / 0.2f);
                banner.anchoredPosition = new Vector2(W / 2f + x, centerY);
            }, () => banner.gameObject.SetActive(false));
        }
    }
}
