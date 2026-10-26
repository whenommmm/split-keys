using System;
using System.Runtime.InteropServices;

namespace Handshake.SplitKeys
{
    // Calls into Plugins/WebGL/SplitKeys.jslib. Outside a WebGL player these are no-ops.
    public static class SkBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public const bool Live = true;
        [DllImport("__Internal")] static extern void SK_Connect(string gameObjectName, string callbackName);
        [DllImport("__Internal")] static extern void SK_CreateRoom(string playerName);
        [DllImport("__Internal")] static extern void SK_JoinRoom(string roomCode, string playerName);
        [DllImport("__Internal")] static extern void SK_Guess(string text);
        [DllImport("__Internal")] static extern void SK_Draw(string json);
        [DllImport("__Internal")] static extern void SK_SwitchTeam();
        [DllImport("__Internal")] static extern void SK_UpdateSettings(string json);
        [DllImport("__Internal")] static extern void SK_StartGame();
        [DllImport("__Internal")] static extern void SK_AddBot();
        [DllImport("__Internal")] static extern void SK_RemoveBot();
        [DllImport("__Internal")] static extern void SK_LeaveRoom();
        [DllImport("__Internal")] static extern void SK_SetRoomQuery(string roomCode);
        [DllImport("__Internal")] static extern void SK_RequestInviteCode();
        [DllImport("__Internal")] static extern string SK_InviteUrl(string roomCode);
        [DllImport("__Internal")] static extern void SK_Invite(string roomCode);
        [DllImport("__Internal")] static extern void SK_CopyText(string text, string message);
        [DllImport("__Internal")] static extern void SK_Vibrate(int ms);
        [DllImport("__Internal")] static extern int SK_IsTouch();
        [DllImport("__Internal")] static extern double SK_NowMs();
        [DllImport("__Internal")] static extern int SK_ReducedMotion();

        public static void Connect(string go, string callback) => SK_Connect(go, callback);
        public static void CreateRoom(string name) => SK_CreateRoom(name);
        public static void JoinRoom(string code, string name) => SK_JoinRoom(code, name);
        public static void Guess(string text) => SK_Guess(text);
        public static void Draw(string json) => SK_Draw(json);
        public static void SwitchTeam() => SK_SwitchTeam();
        public static void UpdateSettings(string json) => SK_UpdateSettings(json);
        public static void StartGame() => SK_StartGame();
        public static void AddBot() => SK_AddBot();
        public static void RemoveBot() => SK_RemoveBot();
        public static void LeaveRoom() => SK_LeaveRoom();
        public static void SetRoomQuery(string code) => SK_SetRoomQuery(code ?? "");
        public static void RequestInviteCode() => SK_RequestInviteCode();
        public static string InviteUrl(string code) => SK_InviteUrl(code);
        public static void Invite(string code) => SK_Invite(code);
        public static void CopyText(string text, string message) => SK_CopyText(text, message);
        public static void Vibrate(int ms) => SK_Vibrate(ms);
        public static bool IsTouch() => SK_IsTouch() == 1;
        public static double NowMs() => SK_NowMs();
        public static bool ReducedMotion() => SK_ReducedMotion() == 1;
#else
        public const bool Live = false;
        public static void Connect(string go, string callback) { }
        public static void CreateRoom(string name) { }
        public static void JoinRoom(string code, string name) { }
        public static void Guess(string text) { }
        public static void Draw(string json) { }
        public static void SwitchTeam() { }
        public static void UpdateSettings(string json) { }
        public static void StartGame() { }
        public static void AddBot() { }
        public static void RemoveBot() { }
        public static void LeaveRoom() { }
        public static void SetRoomQuery(string code) { }
        public static void RequestInviteCode() { }
        public static string InviteUrl(string code) => "https://example.com/?room=" + code;
        public static void Invite(string code) { }
        public static void CopyText(string text, string message) { }
        public static void Vibrate(int ms) { }
        public static bool IsTouch() => false;
        public static double NowMs() => (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        public static bool ReducedMotion() => false;
#endif
    }
}
