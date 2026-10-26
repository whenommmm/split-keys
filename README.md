# Split Keys

**Draw a secret sentence. Your teammate guesses it. Beat the other team.**

Split Keys is a team drawing-and-guessing game for 4 players: two teams of two, Sky and Tangerine. It runs in the browser on phones and laptops, with no app and no account. Think skribbl, but for whole sentences like *THE BULL ATE THE BUTTER*, and both teams race at the same time.

## How to play

1. One person taps **Create room** and shares the 4-letter room code or the invite link.
2. Everyone else opens the link (or enters the code) and picks a name. Players are split into two teams of two, and you can switch teams in the lobby.
3. The host can change the **game settings** in the lobby, then presses **Start game**:

   | Setting | Options |
   |---|---|
   | Draw time | 60 s, 90 s, 2 min, 3 min |
   | Hint letter every | off, 15 s, 30 s, 45 s (reveals one letter of a random hidden word) |
   | Sentence length | 3, 4 or 5 words, or Mix (grows from 3 to 5 over the match) |
   | Rounds | 2, 4, 6 or 8 |
4. Each round, one teammate **draws** and the other **guesses**. You swap roles every round, so everyone draws twice.
   - **Drawer:** you see your team's secret sentence. Draw it! No letters or numbers. You get:
     - 16 colors
     - a brush-size slider with a live preview
     - pen, fill bucket and eraser
     - undo and clear
   - **Guesser:** you see blanks for each word (small words like *THE* and *A* are filled in). Type words on the keyboard. Right words pop into place, and near misses are flagged as "close".
5. Both teams play at the same time with different sentences. Guessers can flip to the other team's drawing and **steal** their words:
   - Your own words are worth +10 each.
   - Stolen words are worth +7, and the other team can no longer score them.
   - Once your sentence is done, keep stealing for the rest of the round.
6. Finishing your own sentence earns a bonus: +20 if you're first, +10 if you're second. A sentence emptied by the other team's steals earns no bonus.
7. After each round, both drawings are revealed side by side. After 4 rounds, the team with more points wins.

On a laptop, guessers can type on their real keyboard.

## How it's built

- **Client:** Unity 6 (6000.3), built for WebGL. All screens are built in code in `Assets/Scripts/SplitKeys/Runtime/`, and the browser page is `Assets/WebGLTemplates/SplitKeys/`.
  - Sounds and icons are generated at runtime, so there are no audio or image assets.
  - Drawings are stored as strokes in 0–1 coordinates, so they look the same on every screen size.
- **Bridge:** `Assets/Plugins/WebGL/SplitKeys.jslib` connects the Unity build to the server with Socket.IO.
- **Server:** Node.js with Express and Socket.IO (`server/`).
  - The server owns all game state, and each player gets their own snapshot: secret sentences only go to the team's drawer.
  - Drawing strokes are relayed only to your teammate, and stored so a player who reconnects gets the canvas back.
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
