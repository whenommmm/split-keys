# Split Keys

**One phrase. 20 seconds. Everyone has different letters.**

Split Keys is a co-op party game for 2–8 players. It runs in the browser on phones and laptops, with no app and no account. Each round, the whole room types one phrase together, in order, before a 20-second timer runs out. The 26 letters are split between the players, so only you can press your letters. Press one at the wrong moment and the current word is erased.

## How to play

1. One person taps **Create room** and shares the 4-letter room code or the invite link.
2. Everyone else opens the link (or enters the code) and picks a name.
3. The host presses **Start game**.
4. Look at the phrase and at your keys. When the next letter is one of yours, press it. Shout "Who has R?!" when you're stuck.
5. Finish the phrase to move up a level. If time runs out, you lose a heart. Three hearts per run. How far can your group get?

On a laptop you can also type your letters on the keyboard.

### Twists as you level up

| Level | What changes |
|---|---|
| 1–2 | Your key glows when the next letter is yours |
| 3 | No more hints |
| 4+ | Your keys are in a random order |
| 5+ | Halfway through the phrase, everyone's letters are dealt again |

## How it's built

- **Client:** Unity 6 (6000.3), built for WebGL. All screens are built in code in `Assets/Scripts/SplitKeys/Runtime/`, and the browser page is `Assets/WebGLTemplates/SplitKeys/`. Sounds and icons are generated at runtime, so there are no audio or image assets.
- **Bridge:** `Assets/Plugins/WebGL/SplitKeys.jslib` connects the Unity build to the server with Socket.IO.
- **Server:** Node.js with Express and Socket.IO (`server/`). The server owns all game state. Clients only send key presses and draw the state they receive.
- **Tests:** `npm test` runs the game-rule tests and a full multiplayer flow test with real socket clients.

## Run it locally

```bash
npm install
npm run dev        # dev mode on http://localhost:3001 (lets you start alone and add bots)
npm start          # normal mode on http://localhost:3000
npm test
```

To play from your phone on the same Wi-Fi, open `http://<your-computer's-IP>:3000`.

To change the game, open the project in Unity 6000.3, then build for WebGL into `Builds/WebGL` (File > Build Profiles > Web). The server serves whatever is in that folder.

## Deploy

`render.yaml` describes a Render web service. On Render, choose **New > Blueprint** and pick this repository. It runs `npm install`, then `npm start`. The free plan goes to sleep when idle, so open the link a minute before playing.
