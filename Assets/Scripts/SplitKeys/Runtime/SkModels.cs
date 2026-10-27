using System;

namespace Handshake.SplitKeys
{
    // Shapes of the JSON the browser bridge forwards from the game server.
    [Serializable] public sealed class Envelope
    {
        public string kind;
        public string eventName;
        public string message;
        public string roomCode;
        public RoomState state;
        public Ack data;
        public Fx fx;
        public CanvasSync canvas;
        public DrawOp draw;
        public AllCanvases all;
    }

    [Serializable] public sealed class Ack
    {
        public bool ok;
        public string error;
        public string code;
        public string playerId;
    }

    [Serializable] public sealed class Fx
    {
        public string kind;
        public string team;
        public string from;
        public int bonus;
        public string level;
        public int points;
        public string playerId;
        public string word;
        public int place;
    }

    [Serializable] public sealed class Stroke
    {
        public string t;
        public string c;
        public float s;
        public float[] p;
    }

    [Serializable] public sealed class DrawOp
    {
        public int roundId;
        public string team;
        public string op;
        public string t;
        public string c;
        public float s;
        public float[] p;
    }

    [Serializable] public sealed class CanvasSync
    {
        public int roundId;
        public string team;
        public Stroke[] strokes;
    }

    [Serializable] public sealed class AllCanvases
    {
        public int roundId;
        public Stroke[] sky;
        public Stroke[] tangerine;
    }

    [Serializable] public sealed class RoomState
    {
        public string code;
        public string phase;
        public string hostId;
        public bool dev;
        public long serverNow;
        public int matches;
        public GameSettings settings;
        public TeamState[] teams;
        public PlayerState[] players;
        public RoundState round;

        // JsonUtility never leaves nested objects null, so check for content instead.
        public bool HasRound => round != null && round.teams != null && round.teams.Length > 0;

        public PlayerState Player(string id)
        {
            if (players == null || string.IsNullOrEmpty(id)) return null;
            foreach (var player in players) if (player.id == id) return player;
            return null;
        }

        public TeamState Team(string id)
        {
            if (teams == null) return null;
            foreach (var team in teams) if (team.id == id) return team;
            return null;
        }

        public RoundTeam RoundTeam(string team)
        {
            if (!HasRound) return null;
            foreach (var entry in round.teams) if (entry.team == team) return entry;
            return null;
        }

        public TeamState OtherTeam(string team)
        {
            if (teams == null) return null;
            foreach (var entry in teams) if (entry.id != team) return entry;
            return null;
        }

        public int ConnectedOn(string team)
        {
            var count = 0;
            if (players != null) foreach (var p in players) if (p.team == team && p.connected) count++;
            return count;
        }

        public int CountOn(string team)
        {
            var count = 0;
            if (players != null) foreach (var p in players) if (p.team == team) count++;
            return count;
        }
    }

    [Serializable] public sealed class GameSettings
    {
        public int drawSeconds = 60;
        public int hintSeconds = 15;
        public int rounds = 6;
        public string stealing = "on";
    }

    [Serializable] public sealed class TeamState
    {
        public string id;
        public string name;
        public string color;
        public int score;
        public string[] playerIds;
    }

    [Serializable] public sealed class RoundState
    {
        public int roundId;
        public int number;
        public int total;
        public long phaseEndsAt;
        public long drawStartedAt;
        public bool stealing = true;
        public RoundTeam[] teams;
    }

    [Serializable] public sealed class RoundTeam
    {
        public string team;
        public string drawerId;
        public string guesserId;
        public WordOption[] options;
        public string level;
        public int multiplier;
        public string solvedBy;
        public int penalty;
        public WordView[] words;
        public int found;
        public int total;
        public bool done;
        public bool selfFinished;
        public int place;
        public int points;
        public GuessView[] guesses;
    }

    [Serializable] public sealed class WordOption
    {
        public string level;
        public string word;
    }

    [Serializable] public sealed class WordView
    {
        public string text;
        public int length;
        public bool revealed;
        public string by;
    }

    [Serializable] public sealed class GuessView
    {
        public string text;
        public string result;
    }

    [Serializable] public sealed class PlayerState
    {
        public string id;
        public string name;
        public string color;
        public string team;
        public bool connected;
        public bool isBot;
        public PlayerStats stats;
    }

    [Serializable] public sealed class PlayerStats
    {
        public int guessed;
        public int stolen;
        public int drawn;
        public int guesses;
    }
}
