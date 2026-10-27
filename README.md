# Split Keys

**Pick a risky word. Draw it. Your teammate guesses it. Beat the other team.**

Split Keys is a team drawing-and-guessing game for 4 players: two teams of two, Sky and Tangerine. It runs in the browser on phones and laptops, with no app and no account. It's like skribbl, with a twist: before drawing, you choose how risky your word is, and both teams race at the same time.

## How to play

1. One person taps **Create room** and shares the 4-letter room code or the invite link.
2. Everyone else opens the link (or enters the code) and picks a name. Players are split into two teams of two, and you can switch teams in the lobby.
3. **Draw yourself.** In the lobby everyone gets a blank capsule body to turn into their character:
   - fill it with any color (there's a color wheel for custom ones)
   - draw eyes and a face on it
   - add hair, ears or a hat outside it

   All four players draw at the same time and see each other's capsules appear live. Your drawing is saved on your device for next time, and it's locked in once the match starts.
4. The host can change the **game settings** in the lobby, then presses **Start game**:

   | Setting | Options |
   |---|---|
   | Draw time | 45 s, 60 s, 90 s, 2 min |
   | Hint letter every | off, 10 s, 15 s, 20 s (reveals one letter of the word) |
   | Rounds | 4, 6, 8 or 10 |
   | Stealing | on or off |
5. Each round, one teammate **draws** and the other **guesses**. You swap roles every round.
6. **Pick your word.** The drawer gets 10 seconds to choose one of three words:

   | Level | Example | Multiplier | If nobody gets it |
   |---|---|---|---|
   | EASY | BOOK | ×1 | no penalty |
   | MEDIUM | CHIMNEY | ×2 | −5 |
   | HARD | BLACK HOLE | ×3 | −10 |

   If the time runs out, you get the EASY word. Both teams see which level the other team picked.
7. **Draw it.** No letters or numbers. You get:
   - 16 colors
   - a brush-size slider with a live preview
   - pen, fill bucket and eraser
   - undo and clear
8. **Guess it.** The guesser types words on the keyboard. In phrases like *TRAFFIC JAM*, each right word pops into place on its own, and near misses are flagged as "close".
9. **Scoring:**
   - Getting your own word scores (10 + up to 10 for speed) × the multiplier, so a fast HARD word is worth up to 60.
   - With stealing on, guessers can flip to the other team's drawing and **steal** their word for 7 × its multiplier. The other team then can't score it, but doesn't lose points either.
   - A MEDIUM or HARD word that nobody gets costs its team 5 or 10 points.
10. After each round, both drawings are revealed side by side. After the last round, the team with more points wins.

On a laptop, guessers can type on their real keyboard.

## How it's built

- **Client:** Unity 6 (6000.3), built for WebGL. All screens are built in code in `Assets/Scripts/SplitKeys/Runtime/`, and the browser page is `Assets/WebGLTemplates/SplitKeys/`.
  - Sounds and icons are generated at runtime, so there are no audio or image assets.
  - Avatars are drawings like the game's own: strokes in 0–1 coordinates on a capsule template. The server relays them live and keeps each player's drawing so late joiners get everyone's.
  - Fonts: Lilita One and Rubik, both under the SIL Open Font License (`Assets/Resources/Fonts/`).
  - Drawings are stored as strokes in 0–1 coordinates, so they look the same on every screen size.
- **Bridge:** `Assets/Plugins/WebGL/SplitKeys.jslib` connects the Unity build to the server with Socket.IO.
- **Server:** Node.js with Express and Socket.IO (`server/`).
  - The server owns all game state, and each player gets their own snapshot: the word choices and the secret word only go to the team's drawer.
  - Drawing strokes are relayed to the other players (so the other team can steal), and stored so a player who reconnects gets the canvas back.
- **Tests:** `npm test` runs the game-rule tests and full multiplayer flow tests with real socket clients.

## Run it locally

```bash
npm install
npm run dev        # dev mode on http://localhost:3001 (the host can add bots to fill the teams)
npm start          # normal mode on http://localhost:3000
npm test
```

To play from your phone on the same Wi-Fi, open `http://<your-computer's-IP>:3000`.

To change the game, open the project in Unity 6000.3, then build for WebGL into `Builds/WebGL` (File > Build Profiles > Web). The server serves whatever is in that folder.

## Deploy

`render.yaml` describes a Render web service. On Render, choose **New > Blueprint** and pick this repository. It runs `npm install`, then `npm start`. The free plan goes to sleep when idle, so open the link a minute before playing.
