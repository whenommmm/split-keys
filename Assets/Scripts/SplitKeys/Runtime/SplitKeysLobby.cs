using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    public sealed class SplitKeysLobby : MonoBehaviour
    {
        [Serializable] private sealed class Envelope
        {
            public string kind;
            public string eventName;
            public string message;
            public string roomCode;
            public RoomState state;
            public Ack data;
        }

        [Serializable] private sealed class Ack
        {
            public bool ok;
            public string error;
            public string code;
            public string playerId;
        }

        [Serializable] private sealed class RoomState
        {
            public string code;
            public string phase;
            public string hostId;
            public bool dev;
            public long serverNow;
            public int level;
            public int lives;
            public int bestLevel;
            public PlayerState[] players;
            public RoundState round;
        }

        [Serializable] private sealed class RoundState
        {
            public int roundId;
            public string phrase;
            public int cursor;
            public string[] typedBy;
            public LetterOwner[] ownerLetters;
            public long phaseEndsAt;
            public string result;
            public long timeLeftMs;
            public string lastPlayerId;
        }

        [Serializable] private sealed class LetterOwner
        {
            public string letter;
            public string playerId;
        }

        [Serializable] private sealed class PlayerState
        {
            public string id;
            public string name;
            public string color;
            public bool connected;
            public bool isBot;
        }

        private const string Ink = "#F5F3FF";
        private const string Muted = "#9C97B8";
        private const string Surface = "#1E1B2E";
        private const string SurfaceRaised = "#2A2640";
        private const string Accent = "#69DB7C";

        private static SplitKeysLobby instance;
        private RectTransform mainFrame;
        private RectTransform gameScreen;
        private RectTransform boardContent;
        private RectTransform keysGrid;
        private RectTransform timerFill;
        private RectTransform resultOverlay;
        private Image nextCapImage;
        private Text titleText;
        private Text gameLevelText;
        private Text gameTimerText;
        private RectTransform gamePlayerChips;
        private Text nextLabelText;
        private Text nextCapText;
        private Text gameKeysLabel;
        private Text resultHeadline;
        private Text resultDetail;
        private Text resultFooter;
        private InputField nameInput;
        private InputField codeInput;
        private RectTransform homeSection;
        private RectTransform lobbySection;
        private RectTransform playerList;
        private Text roomCodeText;
        private Text connectionText;
        private Text messageText;
        private Text waitingText;
        private Button startButton;
        private RectTransform lobbyDevPanel;
        private RectTransform gameOverDevPanel;
        private Button addBotButton;
        private Button removeBotButton;
        private Button gameOverAddBotButton;
        private Button gameOverRemoveBotButton;
        private Button playAgainButton;
        private Text gameOverWaitingText;
        private string selfPlayerId;
        private RoomState currentState;
        private bool inRoom;
        private double serverOffsetMs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateAtRuntime()
        {
            if (instance != null) return;
            var host = new GameObject("SplitKeysApp");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<SplitKeysLobby>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            BuildInterface();
            Connect();
            ApplyInviteFromUrl();
        }

        private void Connect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_Connect(gameObject.name, nameof(OnBridgeMessage));
#else
            SetConnection("WebGL build required for online play.", Hex(Muted));
#endif
        }

        private void BuildInterface()
        {
            var canvasObject = new GameObject("Split Keys Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(900, 1600);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.35f;

            if (EventSystem.current == null)
            {
                var events = new GameObject("Split Keys EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            var backdrop = CreatePanel("Backdrop", canvas.transform, Hex("#13111C"));
            Stretch(backdrop);
            var frame = CreatePanel("Frame", canvas.transform, Color.clear);
            mainFrame = frame;
            Stretch(frame);
            frame.gameObject.AddComponent<RectMask2D>();
            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = frame;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 55;

            var content = CreatePanel("Content", frame, Color.clear);
            content.anchorMin = new Vector2(0.5f, 1f);
            content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = new Vector2(0, -20);
            content.sizeDelta = new Vector2(820, 0);
            scroll.content = content;
            var contentFitter = content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 24, 24);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            titleText = AddText(content, "SPLIT KEYS", 48, Hex(Ink), TextAnchor.MiddleCenter, 64);
            titleText.fontStyle = FontStyle.Bold;
            AddText(content, "One phrase. 20 seconds. Everyone has different letters.", 20, Hex(Muted), TextAnchor.MiddleCenter, 36);
            connectionText = AddText(content, "Connecting…", 18, Hex(Muted), TextAnchor.MiddleCenter, 24);

            homeSection = CreatePanel("Home", content, Color.clear);
            AddVertical(homeSection, 6, 0, 0);
            homeSection.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            AddText(homeSection, "Choose a name", 22, Hex(Ink), TextAnchor.MiddleLeft, 30);
            nameInput = AddInput(homeSection, "Your name", 54);
            nameInput.characterLimit = 12;
            AddButton(homeSection, "CREATE ROOM", 58, Hex("#69DB7C"), Hex("#13111C"), CreateRoom);

            AddText(homeSection, "OR JOIN A ROOM", 18, Hex(Muted), TextAnchor.MiddleCenter, 32);
            var codeRow = CreatePanel("Join Row", homeSection, Color.clear);
            var row = codeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12;
            row.childControlHeight = true;
            row.childControlWidth = true;
            row.childForceExpandHeight = false;
            row.childForceExpandWidth = false;
            codeInput = AddInput(codeRow, "CODE", 54);
            codeInput.characterLimit = 4;
            var codeLayout = codeInput.GetComponent<LayoutElement>();
            codeLayout.flexibleWidth = 1;
            AddButton(codeRow, "JOIN", 54, Hex("#4DABF7"), Hex("#13111C"), JoinRoom, 220);
            messageText = AddText(homeSection, "", 18, Hex("#FF8A8A"), TextAnchor.MiddleCenter, 26);
            AddText(homeSection, "HOW TO PLAY", 18, Hex(Ink), TextAnchor.MiddleLeft, 26);
            AddText(homeSection,
                "Work together to type a phrase in order. Pressing early erases the current word.\n" +
                "Beat the 20-second timer and protect your 3 hearts.",
                17, Hex(Muted), TextAnchor.UpperLeft, 48);

            lobbySection = CreatePanel("Lobby", content, Color.clear);
            AddVertical(lobbySection, 20, 0, 0);
            lobbySection.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            AddText(lobbySection, "ROOM CODE", 24, Hex(Muted), TextAnchor.MiddleCenter, 44);
            roomCodeText = AddText(lobbySection, "----", 96, Hex(Ink), TextAnchor.MiddleCenter, 128);
            AddButton(lobbySection, "INVITE FRIENDS", 76, Hex(SurfaceRaised), Hex(Ink), InviteFriends);
            AddText(lobbySection, "OPERATORS", 24, Hex(Muted), TextAnchor.MiddleLeft, 44);
            playerList = CreatePanel("Player List", lobbySection, Color.clear);
            AddVertical(playerList, 10, 0, 0);
            playerList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            lobbyDevPanel = BuildDevPanel(lobbySection, "Lobby Dev Panel", AddBot, RemoveBot, out addBotButton, out removeBotButton);
            startButton = AddButton(lobbySection, "START GAME", 82, Hex("#FFF3D6"), Hex("#15121F"), StartGame);
            waitingText = AddText(lobbySection, "Waiting for the host to start…", 24, Hex(Muted), TextAnchor.MiddleCenter, 56);
            AddButton(lobbySection, "LEAVE ROOM", 64, Hex(Surface), Hex(Muted), LeaveRoom);
            AddText(lobbySection, "", 1, Color.clear, TextAnchor.MiddleCenter, 1);
            BuildGameInterface(canvas.transform);

            RefreshView();
        }


        private void BuildGameInterface(Transform canvas)
        {
            gameScreen = CreatePanel("Game Screen", canvas, Hex("#12101C"));
            Stretch(gameScreen);
            gameScreen.gameObject.SetActive(false);
            var screenLayout = gameScreen.gameObject.AddComponent<VerticalLayoutGroup>();
            screenLayout.padding = new RectOffset(20, 20, 18, 14);
            screenLayout.spacing = 8;
            screenLayout.childAlignment = TextAnchor.UpperCenter;
            screenLayout.childControlWidth = true;
            screenLayout.childControlHeight = true;
            screenLayout.childForceExpandWidth = true;
            screenLayout.childForceExpandHeight = false;

            var top = CreatePanel("Game Top Bar", gameScreen, Color.clear);
            AddVertical(top, 4, 0, 0);
            AddHeight(top, 150);
            var topRow = CreatePanel("Status Row", top, Color.clear);
            var rowLayout = topRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            AddHeight(topRow, 48);
            AddText(topRow, "♥ ♥ ♥", 24, Hex("#FF5252"), TextAnchor.MiddleLeft, 42);
            gameLevelText = AddText(topRow, "LEVEL 1", 24, Hex(Ink), TextAnchor.MiddleCenter, 42);
            gameTimerText = AddText(topRow, "20", 24, Hex(Ink), TextAnchor.MiddleRight, 42);

            var timerTrack = CreatePanel("Timer Track", top, Hex(SurfaceRaised));
            AddHeight(timerTrack, 10);
            timerFill = CreatePanel("Timer Fill", timerTrack, Hex("#FFF3D6"));
            timerFill.anchorMin = Vector2.zero;
            timerFill.anchorMax = Vector2.one;
            timerFill.pivot = new Vector2(0, 0.5f);
            timerFill.offsetMin = Vector2.zero;
            timerFill.offsetMax = Vector2.zero;

            var chips = CreatePanel("Player Chips", top, Color.clear);
            gamePlayerChips = chips;
            AddHeight(chips, 34);
            var chipsLayout = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipsLayout.spacing = 8;
            chipsLayout.childAlignment = TextAnchor.MiddleCenter;
            chipsLayout.childControlWidth = false;
            chipsLayout.childControlHeight = true;
            chipsLayout.childForceExpandWidth = false;
            chipsLayout.childForceExpandHeight = false;

            var board = CreatePanel("Phrase Board", gameScreen, Color.clear);
            boardContent = board;
            var boardLayout = board.gameObject.AddComponent<VerticalLayoutGroup>();
            boardLayout.spacing = 10;
            boardLayout.childAlignment = TextAnchor.MiddleCenter;
            boardLayout.childControlWidth = true;
            boardLayout.childControlHeight = false;
            boardLayout.childForceExpandWidth = true;
            boardLayout.childForceExpandHeight = false;
            AddFlexibleHeight(board, 1);

            var next = CreatePanel("Next Callout", gameScreen, Color.clear);
            AddVertical(next, 4, 0, 0);
            AddHeight(next, 142);
            nextLabelText = AddText(next, "NEXT", 16, Hex(Muted), TextAnchor.MiddleCenter, 24);
            var nextCap = CreatePanel("Next Key", next, Hex(SurfaceRaised));
            AddHeight(nextCap, 92);
            nextCapImage = nextCap.GetComponent<Image>();
            nextCapImage.raycastTarget = false;
            nextCapText = AddText(nextCap, "", 42, Hex(Ink), TextAnchor.MiddleCenter, 82);
            Stretch(nextCapText.rectTransform);

            var tray = CreatePanel("Your Key Tray", gameScreen, Hex(Surface));
            AddVertical(tray, 8, 14, 12);
            AddHeight(tray, 310);
            gameKeysLabel = AddText(tray, "YOUR KEYS", 16, Hex(Muted), TextAnchor.MiddleLeft, 30);
            keysGrid = CreatePanel("Letter Keys", tray, Color.clear);
            var grid = keysGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(8, 8);
            grid.cellSize = new Vector2(58, 58);
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 7;
            keysGrid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            resultOverlay = CreatePanel("Round Result Overlay", canvas, new Color(10f / 255f, 8f / 255f, 18f / 255f, 0.88f));
            Stretch(resultOverlay);
            var card = CreatePanel("Result Card", resultOverlay, Hex(Surface));
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(560, 640);
            AddVertical(card, 18, 28, 32);
            resultHeadline = AddText(card, "CLEARED!", 40, Hex("#69DB7C"), TextAnchor.MiddleCenter, 68);
            resultDetail = AddText(card, "", 22, Hex(Ink), TextAnchor.MiddleCenter, 180);
            resultFooter = AddText(card, "", 18, Hex(Muted), TextAnchor.MiddleCenter, 48);
            gameOverDevPanel = BuildDevPanel(card, "Game Over Dev Panel", AddBot, RemoveBot, out gameOverAddBotButton, out gameOverRemoveBotButton);
            playAgainButton = AddButton(card, "PLAY AGAIN", 64, Hex("#FFF3D6"), Hex("#15121F"), StartGame);
            gameOverWaitingText = AddText(card, "Waiting for the host to play again", 18, Hex(Muted), TextAnchor.MiddleCenter, 36);
            gameOverDevPanel.gameObject.SetActive(false);
            playAgainButton.gameObject.SetActive(false);
            gameOverWaitingText.gameObject.SetActive(false);
            resultOverlay.gameObject.SetActive(false);
        }

        private RectTransform BuildDevPanel(Transform parent, string panelName, UnityEngine.Events.UnityAction add, UnityEngine.Events.UnityAction remove, out Button addButton, out Button removeButton)
        {
            var panel = CreatePanel(panelName, parent, Hex("#1B1829"));
            var element = panel.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 112;
            element.preferredHeight = 112;
            AddVertical(panel, 6, 12, 10);
            var title = AddText(panel, "DEV TOOLS", 18, Hex(Muted), TextAnchor.MiddleLeft, 24);
            title.fontStyle = FontStyle.Bold;
            var buttons = CreatePanel("Dev Buttons", panel, Color.clear);
            var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;
            AddHeight(buttons, 54);
            addButton = AddButton(buttons, "+ ADD BOT", 52, Hex(SurfaceRaised), Hex(Ink), add);
            removeButton = AddButton(buttons, "− REMOVE BOT", 52, Hex(SurfaceRaised), Hex(Ink), remove);
            AddDashedBorder(panel);
            return panel;
        }

        private static void AddDashedBorder(RectTransform panel)
        {
            const int horizontalSegments = 32;
            const int verticalSegments = 5;
            for (var i = 0; i < horizontalSegments; i++)
            {
                var x0 = i / (float)horizontalSegments;
                var x1 = (i + 0.52f) / horizontalSegments;
                AddBorderSegment(panel, new Vector2(x0, 0.98f), new Vector2(x1, 1f));
                AddBorderSegment(panel, new Vector2(x0, 0f), new Vector2(x1, 0.02f));
            }
            for (var i = 0; i < verticalSegments; i++)
            {
                var y0 = i / (float)verticalSegments;
                var y1 = (i + 0.52f) / verticalSegments;
                AddBorderSegment(panel, new Vector2(0f, y0), new Vector2(0.003f, y1));
                AddBorderSegment(panel, new Vector2(0.997f, y0), new Vector2(1f, y1));
            }
        }

        private static void AddBorderSegment(RectTransform parent, Vector2 min, Vector2 max)
        {
            var segment = CreatePanel("Dash", parent, Hex("#34304A"));
            segment.anchorMin = min;
            segment.anchorMax = max;
            segment.offsetMin = Vector2.zero;
            segment.offsetMax = Vector2.zero;
            segment.GetComponent<Image>().raycastTarget = false;
            var element = segment.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
        }

        private static void AddHeight(RectTransform rect, float height)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0;
        }

        private static void AddFlexibleHeight(RectTransform rect, float flexible)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            element.flexibleHeight = flexible;
            element.minHeight = 0;
        }

        private void Update()
        {
            if (currentState == null || currentState.round == null ||
                (currentState.phase != "countdown" && currentState.phase != "playing")) return;
            var remainingMs = Math.Max(0, currentState.round.phaseEndsAt - (LocalNowMs() + serverOffsetMs));
            var seconds = Mathf.CeilToInt((float)(remainingMs / 1000.0));
            if (gameTimerText != null)
            {
                gameTimerText.text = currentState.phase == "countdown" ? "20" : seconds.ToString();
                gameTimerText.color = currentState.phase == "playing" && seconds <= 5 ? Hex("#FF5252") : Hex(Ink);
            }
            if (timerFill != null)
            {
                var ratio = currentState.phase == "countdown" ? 1f : Mathf.Clamp01((float)(remainingMs / 20000.0));
                timerFill.anchorMax = new Vector2(ratio, 1f);
            }
            if (currentState.phase == "countdown")
            {
                nextLabelText.text = "GET READY · LEARN YOUR LETTERS";
                nextCapText.text = seconds > 0 ? seconds.ToString() : "GO!";
                nextCapImage.color = Hex("#FFF3D6");
                nextCapText.color = Hex("#15121F");
            }
            else
            {
                var round = currentState.round;
                nextLabelText.text = "NEXT";
                nextCapText.text = round.cursor >= 0 && round.cursor < round.phrase.Length
                    ? round.phrase[round.cursor].ToString()
                    : "";
                var owner = FindOwner(round, nextCapText.text);
                var isMine = !string.IsNullOrEmpty(owner) && owner == selfPlayerId;
                nextLabelText.text = isMine ? "YOU!" : "NEXT";
                nextLabelText.color = isMine ? FindPlayerColor(selfPlayerId) : seconds <= 5 ? Hex("#FF5252") : Hex(Muted);
                nextCapImage.color = isMine ? FindPlayerColor(selfPlayerId) : Hex(SurfaceRaised);
                nextCapText.color = isMine ? Hex("#15121F") : Hex(Ink);
            }
        }

        private static long LocalNowMs()
        {
            return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond - 62135596800000L;
        }

        private string FindOwner(RoundState round, string letter)
        {
            if (round == null || round.ownerLetters == null || string.IsNullOrEmpty(letter)) return null;
            var entry = Array.Find(round.ownerLetters, item => item.letter == letter);
            return entry?.playerId;
        }

        private Color FindPlayerColor(string playerId)
        {
            if (currentState?.players == null) return Hex("#FFF3D6");
            var player = Array.Find(currentState.players, item => item.id == playerId);
            return player == null ? Hex("#FFF3D6") : Hex(player.color);
        }

        private void RenderGame()
        {
            if (currentState?.round == null) return;
            var round = currentState.round;
            gameLevelText.text = "LEVEL " + Mathf.Max(1, currentState.level - (round.result == "cleared" ? 1 : 0));
            for (var i = gamePlayerChips.childCount - 1; i >= 0; i--) Destroy(gamePlayerChips.GetChild(i).gameObject);
            foreach (var player in currentState.players)
            {
                var chip = CreatePanel("Player Chip", gamePlayerChips, Hex("#252137"));
                var chipRow = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
                chipRow.padding = new RectOffset(8, 8, 2, 2);
                chipRow.spacing = 6;
                chipRow.childAlignment = TextAnchor.MiddleCenter;
                chipRow.childControlWidth = false;
                chipRow.childControlHeight = true;
                chipRow.childForceExpandWidth = false;
                chipRow.childForceExpandHeight = false;
                if (player.isBot)
                {
                    AddText(chip, "🤖", 17, Hex(Ink), TextAnchor.MiddleCenter, 28);
                }
                else
                {
                    var avatar = CreatePanel("Avatar", chip, Hex(player.color));
                    var avatarLayout = avatar.gameObject.AddComponent<LayoutElement>();
                    avatarLayout.minWidth = 14;
                    avatarLayout.preferredWidth = 14;
                    avatarLayout.minHeight = 14;
                    avatarLayout.preferredHeight = 14;
                }
                var chipName = AddText(chip, player.name, 15, Hex(player.connected ? Ink : Muted), TextAnchor.MiddleLeft, 28);
                chipName.supportRichText = false;
                if (player.isBot) AddBotTag(chip);
            }

            for (var i = boardContent.childCount - 1; i >= 0; i--) Destroy(boardContent.GetChild(i).gameObject);
            var words = round.phrase.Split(' ');
            var phraseIndex = 0;
            var currentWordStart = round.cursor;
            while (currentWordStart > 0 && round.phrase[currentWordStart - 1] != ' ') currentWordStart--;
            foreach (var word in words)
            {
                var wordRow = CreatePanel("Word", boardContent, Color.clear);
                var row = wordRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.spacing = 5;
                row.childAlignment = TextAnchor.MiddleCenter;
                row.childControlWidth = false;
                row.childControlHeight = false;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = false;
                AddHeight(wordRow, 68);
                for (var i = 0; i < word.Length; i++)
                {
                    var index = phraseIndex + i;
                    var tile = CreatePanel("Tile", wordRow, Hex("#0D0B15"));
                    tile.GetComponent<Image>().raycastTarget = false;
                    var width = word.Length > 8 ? 34 : word.Length > 6 ? 42 : 54;
                    var tileLayout = tile.gameObject.AddComponent<LayoutElement>();
                    tileLayout.minWidth = width;
                    tileLayout.preferredWidth = width;
                    tileLayout.minHeight = 62;
                    tileLayout.preferredHeight = 62;
                    var typedId = round.typedBy != null && index < round.typedBy.Length ? round.typedBy[index] : null;
                    var isTyped = !string.IsNullOrEmpty(typedId);
                    var isCursor = index == round.cursor && currentState.phase != "roundEnd";
                    var isCurrentWord = index >= currentWordStart && index < currentWordStart + word.Length;
                    var tileImage = tile.GetComponent<Image>();
                    tileImage.color = isTyped ? FindPlayerColor(typedId) : isCurrentWord ? Hex("#252137") : Hex("#0D0B15");
                    var textColor = isTyped ? Hex("#15121F") : isCursor ? Hex(Ink) : Hex(Muted);
                    var glyph = AddText(tile, word[i].ToString(), 28, textColor, TextAnchor.MiddleCenter, 60);
                    Stretch(glyph.rectTransform);
                    if (isCursor)
                    {
                        var outline = tile.gameObject.AddComponent<Outline>();
                        outline.effectColor = Hex(Ink);
                        outline.effectDistance = new Vector2(2, 2);
                    }
                }
                phraseIndex += word.Length + 1;
            }

            for (var i = keysGrid.childCount - 1; i >= 0; i--) Destroy(keysGrid.GetChild(i).gameObject);
            var owned = new System.Collections.Generic.List<string>();
            if (round.ownerLetters != null)
            {
                foreach (var item in round.ownerLetters)
                    if (item.playerId == selfPlayerId && !string.IsNullOrEmpty(item.letter)) owned.Add(item.letter);
            }
            owned.Sort(StringComparer.Ordinal);
            var columns = owned.Count <= 4 ? Math.Max(1, owned.Count) : owned.Count <= 8 ? 4 : owned.Count <= 13 ? 5 : 7;
            var grid = keysGrid.GetComponent<GridLayoutGroup>();
            grid.constraintCount = columns;
            grid.cellSize = owned.Count >= 14 ? new Vector2(52, 52) : new Vector2(62, 62);
            foreach (var letter in owned)
            {
                var button = AddButton(keysGrid, letter, 58, FindPlayerColor(selfPlayerId), Hex("#15121F"), delegate { }, grid.cellSize.x);
                button.interactable = currentState.phase == "playing";
                button.onClick.RemoveAllListeners();
                var capturedLetter = letter;
                var trigger = button.gameObject.AddComponent<EventTrigger>();
                if (trigger.triggers == null) trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();
                var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                down.callback.AddListener((_) => PressLetter(capturedLetter));
                trigger.triggers.Add(down);
            }
            gameKeysLabel.text = owned.Count == 0 ? "YOU'RE IN! YOU'LL GET LETTERS NEXT ROUND." : "YOUR KEYS";
            Update();
            var showResult = currentState.phase == "roundEnd" || currentState.phase == "gameOver";
            resultOverlay.gameObject.SetActive(showResult);
            if (showResult)
            {
                if (currentState.phase == "gameOver")
                {
                    resultHeadline.text = "GAME OVER";
                    resultHeadline.color = Hex("#FF5252");
                    resultDetail.text = "LEVELS CLEARED  " + Math.Max(0, currentState.level - 1);
                    resultFooter.text = "Final results…";
                }
                else if (round.result == "cleared")
                {
                    var player = Array.Find(currentState.players, item => item.id == round.lastPlayerId);
                    var spare = Math.Max(0, round.timeLeftMs) / 1000.0;
                    resultHeadline.text = "CLEARED!";
                    resultHeadline.color = Hex("#69DB7C");
                    resultDetail.text = spare.ToString("F1") + "s to spare\n" +
                        "🎯 " + (player?.name ?? "A player") + " hit the last key\n" +
                        "Level " + currentState.level + " next";
                    resultFooter.text = "Next round starting…";
                }
                else
                {
                    var typedCount = 0;
                    if (round.typedBy != null)
                        foreach (var id in round.typedBy) if (!string.IsNullOrEmpty(id)) typedCount++;
                    resultHeadline.text = "TIME'S UP";
                    resultHeadline.color = Hex("#FF5252");
                    var hearts = currentState.lives <= 0 ? "Out of hearts" : currentState.lives == 1 ? "Last heart!" : currentState.lives + " hearts left";
                    resultDetail.text = hearts + "\n" + typedCount + " of " + round.phrase.Replace(" ", "").Length + " letters";
                    resultFooter.text = currentState.lives <= 0 ? "Final results…" : "New phrase, same level";
                }
            }
            var isHost = currentState.hostId == selfPlayerId;
            var showGameOver = currentState.phase == "gameOver";
            gameOverDevPanel.gameObject.SetActive(showGameOver && currentState.dev && isHost);
            playAgainButton.gameObject.SetActive(showGameOver && isHost);
            gameOverWaitingText.gameObject.SetActive(showGameOver && !isHost);
            var botCount = Array.FindAll(currentState.players, player => player.isBot).Length;
            gameOverAddBotButton.interactable = currentState.players.Length < 8;
            gameOverRemoveBotButton.interactable = botCount > 0;
            if (showGameOver && !isHost)
            {
                var host = Array.Find(currentState.players, player => player.id == currentState.hostId);
                gameOverWaitingText.text = "Waiting for " + (host?.name ?? "the host") + " to play again";
            }
        }

        private void AddBotTag(Transform parent)
        {
            var tag = CreatePanel("BOT Tag", parent, Hex("#252137"));
            var element = tag.gameObject.AddComponent<LayoutElement>();
            element.minWidth = 42;
            element.preferredWidth = 42;
            element.minHeight = 22;
            element.preferredHeight = 22;
            var label = AddText(tag, "BOT", 11, Hex(Muted), TextAnchor.MiddleCenter, 20);
            Stretch(label.rectTransform);
        }

        private static string EscapeRichText(string value)
        {
            return (value ?? "").Replace("<", "＜").Replace(">", "＞");
        }

        private void PressLetter(string letter)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (currentState?.round != null && currentState.phase == "playing")
                SK_Press(letter, currentState.round.cursor);
#endif
        }

        private static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<RectTransform>();
        }

        private static Text AddText(Transform parent, string value, int size, Color color, TextAnchor anchor, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var element = go.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return text;
        }

        private static InputField AddInput(Transform parent, string placeholderText, float height)
        {
            var background = CreatePanel("Input", parent, Hex(Surface));
            var rect = background;
            rect.sizeDelta = new Vector2(0, height);
            var element = background.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleWidth = 1;
            var text = AddText(background, "", 30, Hex(Ink), TextAnchor.MiddleLeft, height);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(18, 4);
            text.rectTransform.offsetMax = new Vector2(-18, -4);
            var placeholder = AddText(background, placeholderText, 28, Hex(Muted), TextAnchor.MiddleLeft, height);
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(18, 4);
            placeholder.rectTransform.offsetMax = new Vector2(-18, -4);
            var input = background.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = background.GetComponent<Image>();
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = InputField.ContentType.Standard;
            return input;
        }

        private static Button AddButton(Transform parent, string label, float height, Color background, Color foreground, UnityEngine.Events.UnityAction action, float width = 0)
        {
            var go = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = background;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var element = go.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            if (width > 0) element.preferredWidth = width;
            var labelText = AddText(go.transform, label, 28, foreground, TextAnchor.MiddleCenter, height);
            Stretch(labelText.rectTransform);
            labelText.fontStyle = FontStyle.Bold;
            return button;
        }

        private static void AddVertical(RectTransform rect, float spacing, int horizontalPadding, int verticalPadding)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(horizontalPadding, horizontalPadding, verticalPadding, verticalPadding);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out var color) ? color : Color.white;
        }

        private void CreateRoom()
        {
            messageText.text = "";
            EmitCreate(nameInput.text);
        }

        private void JoinRoom()
        {
            messageText.text = "";
            EmitJoin(codeInput.text, nameInput.text);
        }

        private void StartGame()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_StartGame();
#endif
        }

        private void AddBot()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_AddBot();
#endif
        }

        private void RemoveBot()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_RemoveBot();
#endif
        }

        private void LeaveRoom()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_LeaveRoom();
#else
            inRoom = false;
            selfPlayerId = null;
            currentState = null;
            RefreshView();
#endif
        }

        private void InviteFriends()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (currentState != null) SK_Invite(currentState.code);
#endif
        }

        private void OnBridgeMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            Envelope envelope;
            try { envelope = JsonUtility.FromJson<Envelope>(json); }
            catch { SetMessage("The server sent an unreadable response."); return; }
            if (envelope == null) return;

            if (envelope.kind == "connected") SetConnection("Connected", Hex(Accent));
            else if (envelope.kind == "disconnected") SetConnection("Connection lost. Reopen the game to reconnect.", Hex("#FF8A8A"));
            else if (envelope.kind == "bridgeError") SetConnection(envelope.message, Hex("#FF8A8A"));
            else if (envelope.kind == "invite")
            {
                if (!string.IsNullOrEmpty(envelope.roomCode) && codeInput != null)
                {
                    codeInput.text = envelope.roomCode.ToUpperInvariant();
                    EventSystem.current?.SetSelectedGameObject(nameInput.gameObject);
                }
            }
            else if (envelope.kind == "state")
            {
                currentState = envelope.state;
                if (currentState != null)
                {
                    inRoom = true;
                    serverOffsetMs = currentState.serverNow - LocalNowMs();
                    if (roomCodeText != null) roomCodeText.text = currentState.code;
                }
                RefreshView();
            }
            else if (envelope.kind == "ack") HandleAck(envelope);
            else if (envelope.kind == "shared") SetMessage(envelope.message);
        }

        private void HandleAck(Envelope envelope)
        {
            var ack = envelope.data;
            if (ack == null) return;
            if (ack.ok)
            {
                if (envelope.eventName == "create_room" || envelope.eventName == "join_room" || envelope.eventName == "resume_room")
                {
                    selfPlayerId = ack.playerId;
                    inRoom = true;
                    var code = string.IsNullOrEmpty(ack.code) ? currentState?.code : ack.code;
                    if (!string.IsNullOrEmpty(code)) SetRoomQuery(code);
                    RefreshView();
                }
                else if (envelope.eventName == "leave_room")
                {
                    inRoom = false;
                    selfPlayerId = null;
                    currentState = null;
                    SetRoomQuery("");
                    RefreshView();
                }
                return;
            }

            if (ack.error == "BAD_NAME") SetMessage("Enter a name (1–12 characters).");
            else if (ack.error == "NOT_FOUND") SetMessage(envelope.eventName == "resume_room" ? "That room has ended." : "No room with that code.");
            else if (ack.error == "FULL") SetMessage("That room is full (8 players).");
            else if (ack.error == "NOT_READY") SetMessage("Only the host can start when 2+ players are connected.");
            else if (ack.error == "ROUND_NOT_BUILT") SetMessage("Lobby is ready. The first round comes in the next milestone.");
            else SetMessage("Something went wrong. Please try again.");
        }

        private void RefreshView()
        {
            if (homeSection == null) return;
            var showGame = inRoom && currentState != null && currentState.round != null &&
                (currentState.phase == "countdown" || currentState.phase == "playing" ||
                 currentState.phase == "roundEnd" || currentState.phase == "gameOver");
            mainFrame.gameObject.SetActive(!showGame);
            gameScreen.gameObject.SetActive(showGame);
            if (showGame)
            {
                RenderGame();
                return;
            }
            resultOverlay.gameObject.SetActive(false);
            var showLobby = inRoom && currentState != null && currentState.phase == "lobby";
            homeSection.gameObject.SetActive(!showLobby);
            lobbySection.gameObject.SetActive(showLobby);
            if (!showLobby) return;

            roomCodeText.text = currentState.code;
            for (var i = playerList.childCount - 1; i >= 0; i--) Destroy(playerList.GetChild(i).gameObject);
            foreach (var player in currentState.players)
            {
                var row = CreatePanel("Player Row", playerList, Hex(Surface));
                var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                group.padding = new RectOffset(18, 18, 6, 6);
                group.spacing = 14;
                group.childAlignment = TextAnchor.MiddleLeft;
                group.childControlWidth = true;
                group.childControlHeight = false;
                group.childForceExpandWidth = false;
                var element = row.gameObject.AddComponent<LayoutElement>();
                element.minHeight = 60;
                element.preferredHeight = 60;
                var dot = CreatePanel("Color", row, Hex(player.color));
                var dotLayout = dot.gameObject.AddComponent<LayoutElement>();
                dotLayout.minWidth = 22;
                dotLayout.preferredWidth = 22;
                dotLayout.minHeight = 22;
                dotLayout.preferredHeight = 22;
                var playerText = AddText(row, player.name, 26, Hex(player.connected ? Ink : Muted), TextAnchor.MiddleLeft, 48);
                playerText.supportRichText = false;
                var playerTextLayout = playerText.GetComponent<LayoutElement>();
                playerTextLayout.flexibleWidth = 1;
                if (player.isBot) AddBotTag(row);
                if (player.id == currentState.hostId) AddText(row, "HOST", 18, Hex(Accent), TextAnchor.MiddleRight, 44);
                if (player.id == selfPlayerId) AddText(row, "YOU", 18, Hex(Muted), TextAnchor.MiddleRight, 44);
                if (!player.connected) AddText(row, "OFFLINE", 16, Hex(Muted), TextAnchor.MiddleRight, 44);
            }

            var isHost = currentState.hostId == selfPlayerId;
            lobbyDevPanel.gameObject.SetActive(currentState.dev && isHost);
            var botCount = Array.FindAll(currentState.players, player => player.isBot).Length;
            addBotButton.interactable = currentState.players.Length < 8;
            removeBotButton.interactable = botCount > 0;
            startButton.gameObject.SetActive(isHost);
            waitingText.gameObject.SetActive(!isHost);
            if (isHost)
            {
                var ready = Array.FindAll(currentState.players, player => player.connected).Length >= (currentState.dev ? 1 : 2);
                startButton.interactable = ready;
                var label = startButton.GetComponentInChildren<Text>();
                if (label != null) label.text = ready ? "START GAME" : "NEED 2+ PLAYERS";
            }
            else
            {
                var host = Array.Find(currentState.players, player => player.id == currentState.hostId);
                waitingText.text = "Waiting for " + (host?.name ?? "the host") + " to start";
            }
        }

        private void SetMessage(string value)
        {
            if (messageText != null) messageText.text = value ?? "";
            if (inRoom && connectionText != null)
            {
                connectionText.text = value ?? "";
                connectionText.color = Hex("#FF8A8A");
            }
        }

        private void SetConnection(string value, Color color)
        {
            if (connectionText == null) return;
            connectionText.text = value;
            connectionText.color = color;
        }

        private void ApplyInviteFromUrl()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_RequestInviteCode();
#endif
        }

        private void EmitCreate(string name)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_CreateRoom(name);
#else
            SetMessage("Build for WebGL to create an online room.");
#endif
        }

        private void EmitJoin(string code, string name)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_JoinRoom(code, name);
#else
            SetMessage("Build for WebGL to join an online room.");
#endif
        }

        private void SetRoomQuery(string code)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SK_SetRoomQuery(code);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void SK_Connect(string gameObjectName, string callbackName);
        [DllImport("__Internal")] private static extern void SK_CreateRoom(string playerName);
        [DllImport("__Internal")] private static extern void SK_JoinRoom(string roomCode, string playerName);
        [DllImport("__Internal")] private static extern void SK_StartGame();
        [DllImport("__Internal")] private static extern void SK_AddBot();
        [DllImport("__Internal")] private static extern void SK_RemoveBot();
        [DllImport("__Internal")] private static extern void SK_Press(string letter, int atIndex);
        [DllImport("__Internal")] private static extern void SK_LeaveRoom();
        [DllImport("__Internal")] private static extern void SK_Invite(string roomCode);
        [DllImport("__Internal")] private static extern void SK_SetRoomQuery(string roomCode);
        [DllImport("__Internal")] private static extern void SK_RequestInviteCode();
#endif
    }
}
