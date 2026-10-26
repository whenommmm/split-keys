using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Handshake.SplitKeys
{
    // Boots the game, owns the room state and decides which screen is showing.
    public sealed class SplitKeysApp : MonoBehaviour
    {
        public static readonly string[] Rules =
        {
            "Two teams of two. Each round one teammate draws and the other guesses. Swap every round.",
            "Draw your secret sentence with colors, fill and any brush size. No letters or numbers!",
            "Guess word by word: +10 for your team's words. Peek at the other team's drawing and steal theirs for +7.",
            "Finish your own sentence first for a bonus. The host sets draw time, hints, sentence length and rounds."
        };

        const string CodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";

        public static Texture2D FadeTexture;
        static SplitKeysApp instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            var host = new GameObject("SplitKeysApp");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<SplitKeysApp>();
        }

        public RectTransform ScreensLayer;
        public RectTransform OverlayLayer;
        public float W = 390f;
        public float H = 844f;
        public bool Wide => W >= 600f;
        public bool Short => H <= 700f;
        public bool IsTouch;
        public bool ReducedMotion;
        public SkAudio Audio;

        public RoomState State;
        public string SelfId;
        public bool InRoom;
        public bool Connected;
        public string InviteCode;
        public bool Resuming;
        public string Notice;
        public float ConnectingFor => Connected ? 0f : Time.unscaledTime - disconnectedAt;

        Canvas canvas;
        CanvasScaler scaler;
        RawImage dots;
        RawImage glow;
        HomeScreen home;
        LobbyScreen lobby;
        GameScreen game;
        GameOverScreen over;
        SkScreen current;

        RectTransform toast;
        Image toastBg;
        Image toastIcon;
        Text toastText;
        RectTransform conn;
        Text connText;

        double serverOffset;
        float disconnectedAt;
        int screenW, screenH;
        string leftCode;
        Keyboard hookedKeyboard;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Sk.LoadFonts();
            SkGfx.Init();
            FadeTexture = MakeFade();
            Tween.Init(gameObject);
            Audio = gameObject.AddComponent<SkAudio>();
            Audio.Init(PlayerPrefs.GetInt("splitkeys.muted", 0) == 1);
            IsTouch = SkBridge.IsTouch();
            ReducedMotion = SkBridge.ReducedMotion();
            disconnectedAt = Time.unscaledTime;

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Sk.Bg;
                cam.cullingMask = 0;
            }

            BuildCanvas();
            home = new HomeScreen(this);
            lobby = new LobbyScreen(this);
            game = new GameScreen(this);
            over = new GameOverScreen(this);
            home.Visible = lobby.Visible = game.Visible = over.Visible = false;
            BuildOverlay();
            ApplyScale();
            Route();
            SkBridge.Connect(gameObject.name, nameof(OnBridgeMessage));
        }

        // ---------- canvas & scaling ----------

        void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.referencePixelsPerUnit = 100f;

            if (EventSystem.current == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            var root = (RectTransform)canvasGo.transform;
            var bg = Ui.Box(root, Sk.Bg, 0f, "Background");
            Ui.Stretch(bg.rectTransform);
            dots = Ui.Node("Dots", root).gameObject.AddComponent<RawImage>();
            dots.texture = SkGfx.Dots;
            dots.raycastTarget = false;
            Ui.Stretch(dots.rectTransform);
            glow = Ui.Node("Glow", root).gameObject.AddComponent<RawImage>();
            glow.texture = SkGfx.Glow;
            glow.raycastTarget = false;
            ScreensLayer = Ui.Node("Screens", root);
            Ui.Stretch(ScreensLayer);
            OverlayLayer = Ui.Node("Overlay", root);
            Ui.Stretch(OverlayLayer);
        }

        void ApplyScale()
        {
            screenW = Screen.width;
            screenH = Screen.height;
            float sw = Mathf.Max(1, screenW), sh = Mathf.Max(1, screenH);
            var aspect = sw / sh;
            // Phones in portrait match width (390 units across); everything else fits a 390x844 frame.
            var scale = aspect < 0.62f ? sw / 390f : Mathf.Min(sw / 390f, sh / 844f);
            scale = Mathf.Max(0.3f, scale);
            scaler.scaleFactor = scale;
            W = sw / scale;
            H = sh / scale;
            dots.uvRect = new Rect(0f, 0f, W / 22f, H / 22f);
            Ui.Place(glow, -W * 0.3f, -H * 0.3f, W * 1.6f, H * 0.6f);
            current?.Layout();
            LayoutOverlay();
        }

        static Texture2D MakeFade()
        {
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < 64; y++)
            {
                var a = Mathf.Clamp01(1f - y / 63f * 1.6f + 0.35f);
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }

        // ---------- overlay: toast + connection banner ----------

        void BuildOverlay()
        {
            conn = Ui.Node("Connection", OverlayLayer);
            var connBg = Ui.Box(conn, Sk.Slate, 0f);
            Ui.Stretch(connBg.rectTransform);
            connText = Ui.Label(conn, "Reconnecting…", Sk.Bold, 14, Sk.Text, TextAnchor.MiddleCenter);
            Ui.Stretch(connText.rectTransform);
            conn.gameObject.SetActive(false);

            toast = Ui.Node("Toast", OverlayLayer);
            toastBg = Ui.Box(toast, Sk.Cream, 20f);
            Ui.Stretch(toastBg.rectTransform);
            toastIcon = Ui.Icon(toast, SkGfx.Burst, Sk.Ink);
            toastText = Ui.Label(toast, "", Sk.Bold, 15, Sk.Ink, TextAnchor.MiddleLeft);
            toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
            toast.gameObject.SetActive(false);
        }

        void LayoutOverlay()
        {
            Ui.Place(conn, 0f, 0f, W, 40f);
        }

        public void Toast(string message, Color bg, Color fg, float seconds, float y, Sprite icon = null)
        {
            toast.gameObject.SetActive(true);
            toastBg.color = bg;
            toastText.color = fg;
            toastText.text = message;
            toastIcon.gameObject.SetActive(icon != null);
            if (icon != null) { toastIcon.sprite = icon; toastIcon.color = fg; }
            var textW = Ui.Width(toastText);
            var iconW = icon != null ? 24f : 0f;
            var w = Mathf.Min(W * 0.9f, textW + iconW + 36f);
            Ui.Place(toast, (W - w) / 2f, y, w, 40f);
            if (icon != null) Ui.Place(toastIcon, 16f, 10f, 20f, 20f);
            Ui.Place(toastText, 18f + iconW, 0f, w - 36f - iconW, 40f);
            var group = toast.GetComponent<CanvasGroup>();
            if (group == null) group = toast.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var total = seconds + 0.48f;
            Tween.Run(toast, total, t =>
            {
                var time = t * total;
                var a = time < 0.24f ? time / 0.24f : time > total - 0.24f ? (total - time) / 0.24f : 1f;
                group.alpha = Mathf.Clamp01(a);
                toast.anchoredPosition = new Vector2(toast.anchoredPosition.x, -(y - 8f * (1f - Mathf.Clamp01(time / 0.24f))));
            }, () => toast.gameObject.SetActive(false));
        }

        void ToastInfo(string message) => Toast(message, Sk.Cream, Sk.Ink, 1.6f, current == game ? 120f : 64f);

        // ---------- routing ----------

        SkScreen Decide()
        {
            if (!InRoom || State == null) return home;
            switch (State.phase)
            {
                case "lobby": return lobby;
                case "countdown":
                case "drawing":
                case "roundEnd": return State.HasRound ? game : lobby;
                case "gameOver": return over;
                default: return home;
            }
        }

        void Route()
        {
            var next = Decide();
            if (next != current)
            {
                if (current != null) current.Visible = false;
                current = next;
                current.Visible = true;
                current.OnShow();
                current.Render();
                if (!ReducedMotion)
                {
                    var screen = current;
                    Tween.Run(screen.Root, 0.24f, t =>
                    {
                        var k = Tween.OutCubic(t);
                        screen.Group.alpha = k;
                        screen.Root.anchoredPosition = new Vector2(0f, -8f * (1f - k));
                    }, () => screen.Root.anchoredPosition = Vector2.zero);
                }
                return;
            }
            current.Render();
        }

        // ---------- bridge messages ----------

        public void OnBridgeMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            Envelope e;
            try { e = JsonUtility.FromJson<Envelope>(json); }
            catch (Exception) { return; }
            if (e == null) return;
            switch (e.kind)
            {
                case "connected":
                    Connected = true;
                    conn.gameObject.SetActive(false);
                    if (current == home) home.Render();
                    break;
                case "disconnected":
                    Connected = false;
                    disconnectedAt = Time.unscaledTime;
                    if (current == home) home.Render();
                    break;
                case "bridgeError":
                    break;
                case "invite":
                    SetInvite(e.roomCode);
                    break;
                case "resuming":
                    Resuming = true;
                    SetInvite(e.roomCode);
                    break;
                case "state":
                    OnState(e.state);
                    break;
                case "fx":
                    if (e.fx != null && current == game) game.OnFx(e.fx);
                    break;
                case "canvas":
                    if (e.canvas != null) game.OnCanvas(e.canvas);
                    break;
                case "draw":
                    if (e.draw != null) game.OnDraw(e.draw);
                    break;
                case "canvases":
                    if (e.all != null) game.OnCanvases(e.all);
                    break;
                case "ack":
                    HandleAck(e.eventName, e.data ?? new Ack());
                    break;
                case "shared":
                    ToastInfo(e.message);
                    break;
            }
        }

        void SetInvite(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            var valid = code.Length == 4;
            foreach (var c in code) if (CodeAlphabet.IndexOf(c) < 0) valid = false;
            if (InRoom) return;
            InviteCode = valid ? code : null;
            if (current == home) home.Render();
        }

        void OnState(RoomState state)
        {
            if (state == null || string.IsNullOrEmpty(state.code)) return;
            if (leftCode != null && state.code == leftCode) return;
            State = state;
            serverOffset = state.serverNow - SkBridge.NowMs();
            InRoom = true;
            Route();
        }

        void HandleAck(string eventName, Ack a)
        {
            switch (eventName)
            {
                case "create_room":
                case "join_room":
                case "resume_room":
                    Resuming = false;
                    if (a.ok)
                    {
                        SelfId = a.playerId;
                        InRoom = true;
                        leftCode = null;
                        Notice = null;
                        InviteCode = null;
                        SkBridge.SetRoomQuery(a.code);
                        Route();
                        return;
                    }
                    var invited = InviteCode != null;
                    switch (a.error)
                    {
                        case "BAD_NAME":
                            home.ShowError(true, "Enter a name (1–12 characters).");
                            break;
                        case "NOT_FOUND":
                            if (eventName == "resume_room" || invited)
                            {
                                Notice = eventName == "resume_room" ? "That room has ended." : "No room with that code.";
                                ClearInvite();
                                home.SetBusy(null);
                            }
                            else home.ShowError(false, "No room with that code.");
                            break;
                        case "FULL":
                            if (invited) { Notice = "That room is full (8 players)."; home.SetBusy(null); }
                            else home.ShowError(false, "That room is full (8 players).");
                            break;
                        case "OFFLINE":
                            home.SetBusy(null);
                            ToastInfo("You're offline. Reconnecting…");
                            break;
                        default:
                            home.SetBusy(null);
                            ToastInfo("Something went wrong. Try again.");
                            break;
                    }
                    if (current == home) home.Render();
                    break;
                case "leave_room":
                    ResetToHome();
                    break;
                case "start_game":
                    if (!a.ok) ToastInfo(a.error == "OFFLINE" ? "You're offline. Reconnecting…" : "Need 2+ players to start.");
                    break;
            }
        }

        void ResetToHome()
        {
            leftCode = State?.code;
            InRoom = false;
            State = null;
            SelfId = null;
            InviteCode = null;
            SkBridge.SetRoomQuery("");
            Route();
        }

        // ---------- actions used by screens ----------

        public void CreateRoom(string name) => SkBridge.CreateRoom(name);
        public void JoinRoom(string code, string name) => SkBridge.JoinRoom(code, name);
        public void StartGame() => SkBridge.StartGame();

        public void Invite()
        {
            if (State != null) SkBridge.Invite(State.code);
        }

        public void Leave()
        {
            if (!InRoom) return;
            if (Connected) SkBridge.LeaveRoom();
            else ResetToHome();
        }

        public void ClearInvite()
        {
            InviteCode = null;
            SkBridge.SetRoomQuery("");
        }

        public void ToggleMute()
        {
            Audio.SetMuted(!Audio.Muted);
            PlayerPrefs.SetInt("splitkeys.muted", Audio.Muted ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void Guess(string text)
        {
            if (State == null || State.phase != "drawing" || !Connected) return;
            SkBridge.Guess(text);
        }

        public double ServerNowMs() => SkBridge.NowMs() + serverOffset;

        // ---------- frame loop ----------

        void Update()
        {
            if (Screen.width != screenW || Screen.height != screenH) ApplyScale();
            HookKeyboard();
            current?.Tick();
            var showConn = InRoom && !Connected && Time.unscaledTime - disconnectedAt > 0.5f;
            if (conn.gameObject.activeSelf != showConn) conn.gameObject.SetActive(showConn);
            if (showConn) connText.text = Time.unscaledTime - disconnectedAt > 15f ? "Still trying… check your connection." : "Reconnecting…";
        }

        void HookKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == hookedKeyboard) return;
            if (hookedKeyboard != null) hookedKeyboard.onTextInput -= OnTextInput;
            hookedKeyboard = keyboard;
            if (keyboard != null) keyboard.onTextInput += OnTextInput;
        }

        void OnTextInput(char c)
        {
            if (current != game || TextFieldFocused()) return;
            game.OnTypedChar(c);
        }

        static bool TextFieldFocused()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent<InputField>(out var field) && field.isFocused;
        }

        void OnDestroy()
        {
            if (hookedKeyboard != null) hookedKeyboard.onTextInput -= OnTextInput;
        }
    }
}
