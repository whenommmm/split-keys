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
        public string playerId;
        public string letter;
    }

    [Serializable] public sealed class RoomState
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

        // JsonUtility never leaves nested objects null, so check for a phrase instead.
        public bool HasRound => round != null && !string.IsNullOrEmpty(round.phrase);

        public PlayerState Player(string id)
        {
            if (players == null || string.IsNullOrEmpty(id)) return null;
            foreach (var player in players) if (player.id == id) return player;
            return null;
        }

        public int IndexOf(string id)
        {
            if (players == null) return -1;
            for (var i = 0; i < players.Length; i++) if (players[i].id == id) return i;
            return -1;
        }

        public int ConnectedCount()
        {
            var count = 0;
            if (players != null) foreach (var player in players) if (player.connected) count++;
            return count;
        }

        // The level the current round is played at (the server bumps level as soon as a round is cleared).
        public int RoundLevel => HasRound && round.result == "cleared" ? Math.Max(1, level - 1) : Math.Max(1, level);
    }

    [Serializable] public sealed class RoundState
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
        public bool reshuffled;
        public Twists twists;

        public string TypedBy(int index)
        {
            if (typedBy == null || index < 0 || index >= typedBy.Length) return null;
            return string.IsNullOrEmpty(typedBy[index]) ? null : typedBy[index];
        }

        public string OwnerOf(char letter)
        {
            if (ownerLetters == null) return null;
            var key = letter.ToString();
            foreach (var entry in ownerLetters) if (entry.letter == key) return entry.playerId;
            return null;
        }

        public int LetterTotal()
        {
            var count = 0;
            foreach (var c in phrase) if (c != ' ') count++;
            return count;
        }

        public int LettersTyped()
        {
            var count = 0;
            for (var i = 0; i < phrase.Length; i++) if (TypedBy(i) != null) count++;
            return count;
        }

        public char NextLetter => cursor >= 0 && cursor < phrase.Length ? phrase[cursor] : ' ';
    }

    [Serializable] public sealed class Twists
    {
        public bool hint;
        public bool shuffleKeys;
        public bool midRoundDeal;
    }

    [Serializable] public sealed class LetterOwner
    {
        public string letter;
        public string playerId;
    }

    [Serializable] public sealed class PlayerState
    {
        public string id;
        public string name;
        public string color;
        public bool connected;
        public bool isBot;
        public PlayerStats stats;
    }

    [Serializable] public sealed class PlayerStats
    {
        public int correct;
        public int mistakes;
        public int finals;
    }
}
