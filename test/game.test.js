import test from "node:test";
import assert from "node:assert/strict";
import {
  addBot,
  addPlayer,
  advanceRoom,
  applyGuess,
  canStart,
  chooseBotGuess,
  chooseBotLevel,
  chooseWord,
  createRoom,
  hintsUnlocked,
  isClose,
  botFace,
  nextWakeAt,
  normalizeName,
  publicSnapshot,
  removePlayer,
  setPlayerConnected,
  solvePoints,
  startGame,
  stealPoints,
  switchTeam,
  uniqueName,
  updateSettings,
  validRoomCode,
  wordsMatch
} from "../server/game.js";

const rng = () => 0;
// With rng = 0 each deck hands out its first unused word: Sky gets the first of each, Tangerine the second.
const WORDS = {
  easy: ["CAT", "DOG", "SUN", "MOON"],
  medium: ["VOLCANO", "GUITAR", "CASTLE", "ROCKET"],
  hard: ["TRAFFIC JAM", "TUG OF WAR", "ECHO", "SHADOW"]
};

function fullRoom(dev = false) {
  let room = createRoom({ code: "BCDF", playerId: "p1", name: "Ana", now: 0, dev });
  for (const [id, name] of [["p2", "Bo"], ["p3", "Cy"], ["p4", "Di"]]) room = addPlayer(room, { playerId: id, name, now: 0 }).room;
  return room;
}

// Starts a game; Sky and Tangerine drawers pick the given levels, so drawing begins at t=1000.
function drawingRoom(skyLevel = "easy", tanLevel = "easy", timing = {}, settings = null) {
  let room = fullRoom();
  if (settings) room = updateSettings(room, "p1", settings);
  room = startGame(room, "p1", 0, rng, WORDS, timing);
  room = chooseWord(room, "p1", skyLevel, 500, rng, timing);
  return chooseWord(room, "p2", tanLevel, 1000, rng, timing);
}

const teamOf = (room, id) => room.players.find((p) => p.id === id).team;

test("room codes use four allowed consonants and names are cleaned", () => {
  assert.equal(validRoomCode("bxkt"), true);
  assert.equal(validRoomCode("ABCD"), false);
  assert.equal(normalizeName("  Sam   Lee "), "Sam Lee");
  assert.equal(normalizeName("ThisNameIsWayTooLong"), null);
  assert.equal(uniqueName([{ name: "Sam" }], "sam"), "sam 2");
});

test("bots draw themselves a face: a body fill plus strokes, all inside the 0..1 square", () => {
  for (const seed of [0, 0.3, 0.7, 0.99]) {
    const strokes = botFace(() => seed);
    assert.equal(strokes[0].t, "fill");
    assert.ok(strokes.length >= 4);
    for (const stroke of strokes) {
      assert.match(stroke.c, /^#[0-9A-F]{6}$/i);
      assert.ok(stroke.p.length >= 2 && stroke.p.every((v) => v >= 0 && v <= 1));
    }
  }
});

test("players fill two teams of two and a fifth player is turned away", () => {
  const room = fullRoom();
  assert.deepEqual(room.players.map((p) => p.team), ["sky", "tangerine", "sky", "tangerine"]);
  assert.equal(addPlayer(room, { playerId: "p5", name: "Ed", now: 0 }), null);
});

test("switching teams only works when the other team has room", () => {
  let room = createRoom({ code: "BCDF", playerId: "p1", name: "Ana", now: 0 });
  room = addPlayer(room, { playerId: "p2", name: "Bo", now: 0 }).room;
  assert.equal(teamOf(switchTeam(room, "p1"), "p1"), "tangerine");
  const full = fullRoom();
  assert.equal(switchTeam(full, "p1"), full);
});

test("the host can start only with two connected players on each team", () => {
  const room = fullRoom();
  assert.equal(canStart(room, "p1"), true);
  assert.equal(canStart(room, "p2"), false);
  assert.equal(canStart(setPlayerConnected(room, "p4", false), "p1"), false);
  assert.equal(canStart(removePlayer(room, "p4"), "p1"), false);
});

test("each round starts with drawers picking from an easy, a medium and a hard word", () => {
  const room = startGame(fullRoom(), "p1", 0, rng, WORDS);
  assert.equal(room.phase, "picking");
  assert.deepEqual(room.round.teams.sky.options, [{ level: "easy", word: "CAT" }, { level: "medium", word: "VOLCANO" }, { level: "hard", word: "TRAFFIC JAM" }]);
  assert.deepEqual(room.round.teams.tangerine.options.map((o) => o.word), ["DOG", "GUITAR", "TUG OF WAR"]);
  assert.deepEqual([room.round.teams.sky.drawerId, room.round.teams.sky.guesserId], ["p1", "p3"]);
  // Only the drawer sees the options.
  assert.equal(publicSnapshot(room, 0, "p1").round.teams[0].options.length, 3);
  assert.equal(publicSnapshot(room, 0, "p3").round.teams[0].options.length, 0);
});

test("only the drawer picks, once; drawing starts as soon as both have picked", () => {
  let room = startGame(fullRoom(), "p1", 0, rng, WORDS);
  assert.equal(chooseWord(room, "p3", "hard", 100, rng), room);
  room = chooseWord(room, "p1", "hard", 100, rng);
  assert.equal(room.round.teams.sky.choice, "hard");
  assert.equal(chooseWord(room, "p1", "easy", 200, rng), room);
  assert.equal(room.phase, "picking");
  assert.equal(publicSnapshot(room, 0, "p4").round.teams[0].level, "hard");
  room = chooseWord(room, "p2", "medium", 300, rng);
  assert.equal(room.phase, "drawing");
  assert.equal(room.round.drawStartedAt, 300);
  assert.deepEqual(room.round.teams.sky.words, ["TRAFFIC", "JAM"]);
});

test("a drawer who doesn't pick in time gets the easy word", () => {
  let room = startGame(fullRoom(), "p1", 0, rng, WORDS, { pick: 10 });
  room = chooseWord(room, "p1", "hard", 100, rng);
  room = advanceRoom(room, 10_000, rng, WORDS);
  assert.equal(room.phase, "drawing");
  assert.equal(room.round.teams.tangerine.choice, "easy");
  assert.deepEqual(room.round.teams.tangerine.words, ["DOG"]);
});

test("small words in phrases are shown from the start", () => {
  const room = drawingRoom("easy", "hard");
  assert.deepEqual(publicSnapshot(room, 1000, "p4").round.teams[1].words.map((w) => w.text), ["___", "OF", "___"]);
});

test("guessers see blanks, drawers see their word, everyone sees both at round end", () => {
  const room = drawingRoom("medium", "easy");
  assert.equal(publicSnapshot(room, 1000, "p3").round.teams[0].words[0].text, "_______");
  assert.equal(publicSnapshot(room, 1000, "p1").round.teams[0].words[0].text, "VOLCANO");
  assert.equal(publicSnapshot(room, 1000, "p2").round.teams[0].words[0].text, "_______");
  const ended = advanceRoom(room, room.round.phaseEndsAt, rng, WORDS);
  assert.equal(publicSnapshot(ended, 0, "p3").round.teams[0].words[0].text, "VOLCANO");
});

test("solving your own word pays (10 + speed) x the multiplier", () => {
  assert.equal(solvePoints("easy", 60_000, 60_000), 20);
  assert.equal(solvePoints("medium", 30_000, 60_000), 30);
  assert.equal(solvePoints("hard", 0, 60_000), 30);
  let room = drawingRoom("medium", "easy");
  const result = applyGuess(room, "p3", "volcano", 1000);
  room = result.room;
  assert.equal(room.round.teams.sky.done, true);
  assert.equal(room.round.teams.sky.solvedBy, "sky");
  assert.equal(room.scores.sky, 40);
  assert.deepEqual(result.effects.at(-1), { kind: "solved", team: "sky", playerId: "p3", word: "VOLCANO", level: "medium", points: 40 });
  assert.equal(room.players.find((p) => p.id === "p1").stats.drawn, 1);
  assert.equal(room.players.find((p) => p.id === "p3").stats.guessed, 1);
});

test("a compound word typed as two words still counts: SAND CASTLE finds SANDCASTLE", () => {
  const compounds = { easy: ["SANDCASTLE", "TREEHOUSE"], medium: ["VOLCANO", "GUITAR"], hard: ["ECHO", "SHADOW"] };
  let room = startGame(fullRoom(), "p1", 0, rng, compounds, {});
  room = chooseWord(chooseWord(room, "p1", "easy", 500, rng), "p2", "easy", 1000, rng);
  room = applyGuess(room, "p3", "sand castle", 2000).room;
  assert.equal(room.round.teams.sky.solvedBy, "sky");
  room = applyGuess(room, "p3", "tree house", 3000).room;
  assert.equal(room.round.teams.tangerine.solvedBy, "sky");
});

test("two-word answers fill in word by word and score when complete", () => {
  let room = drawingRoom("hard", "easy");
  room = applyGuess(room, "p3", "TRAFFIC", 1000).room;
  assert.equal(room.round.teams.sky.done, false);
  assert.equal(publicSnapshot(room, 1000, "p3").round.teams[0].words[0].text, "TRAFFIC");
  room = applyGuess(room, "p3", "JAM", 1000).room;
  assert.equal(room.round.teams.sky.done, true);
  assert.equal(room.scores.sky, 60);
});

test("rivals can steal a word for 7 x its multiplier; the owners lose it but pay no penalty", () => {
  assert.equal(stealPoints("hard"), 21);
  let room = drawingRoom("hard", "easy");
  const result = applyGuess(room, "p4", "TRAFFIC JAM", 2000);
  room = result.room;
  assert.equal(result.effects.find((e) => e.kind === "stolen").points, 21);
  assert.equal(room.scores.tangerine, 21);
  assert.equal(room.round.teams.sky.solvedBy, "tangerine");
  assert.equal(room.players.find((p) => p.id === "p4").stats.stolen, 1);
  assert.equal(applyGuess(room, "p3", "TRAFFIC JAM", 3000).room.scores.sky, 0);
  const ended = advanceRoom(room, room.round.phaseEndsAt, rng, WORDS);
  assert.equal(ended.scores.sky, 0);
  assert.equal(ended.round.teams.sky.penalty, 0);
});

test("risky words nobody guessed cost their team points", () => {
  const room = drawingRoom("hard", "medium");
  const ended = advanceRoom(room, room.round.phaseEndsAt, rng, WORDS);
  assert.equal(ended.phase, "roundEnd");
  assert.deepEqual(ended.scores, { sky: -10, tangerine: -5 });
  assert.equal(ended.round.teams.sky.penalty, 10);
  const easy = drawingRoom("easy", "easy");
  assert.deepEqual(advanceRoom(easy, easy.round.phaseEndsAt, rng, WORDS).scores, { sky: 0, tangerine: 0 });
});

test("the round ends early once both words are taken", () => {
  let room = drawingRoom("easy", "easy");
  room = applyGuess(room, "p3", "CAT", 1000).room;
  assert.equal(room.phase, "drawing");
  room = applyGuess(room, "p4", "DOG", 1500).room;
  assert.equal(room.phase, "roundEnd");
});

test("with stealing off, rival guesses never touch your word", () => {
  let room = drawingRoom("easy", "easy", {}, { stealing: "off" });
  room = applyGuess(room, "p4", "CAT", 1000).room;
  assert.equal(room.round.teams.sky.done, false);
  assert.equal(room.scores.tangerine, 0);
  assert.equal(publicSnapshot(room, 0, "p1").round.stealing, false);
});

test("plurals match and typos one letter off are close", () => {
  assert.equal(wordsMatch("BANANAS", "BANANA"), true);
  assert.equal(isClose("VOLCANE", "VOLCANO"), true);
  assert.equal(isClose("DOG", "CAT"), false);
  const result = applyGuess(drawingRoom("medium", "easy"), "p3", "VOLKANO", 1000);
  assert.equal(result.room.round.teams.sky.guesses.at(-1).result, "close");
});

test("hint letters unlock on the host's schedule, or never when hints are off", () => {
  const room = drawingRoom("medium", "easy", {}, { hintSeconds: 10 });
  const start = room.round.drawStartedAt;
  assert.equal(hintsUnlocked(room, start + 9_000), 0);
  assert.equal(hintsUnlocked(room, start + 11_000), 1);
  assert.match(publicSnapshot(room, start + 11_000, "p3").round.teams[0].words[0].text, /[A-Z]/);
  assert.equal(nextWakeAt(room, start + 1_000), start + 10_000);
  const off = drawingRoom("medium", "easy", {}, { hintSeconds: 0 });
  assert.equal(hintsUnlocked(off, off.round.drawStartedAt + 50_000), 0);
});

test("the host's settings change draw time, rounds and stealing; others can't change them", () => {
  const room = fullRoom();
  assert.equal(updateSettings(room, "p2", { drawSeconds: 45 }), room);
  const changed = updateSettings(room, "p1", { drawSeconds: 90, hintSeconds: 20, rounds: 4, stealing: "off", words: 3 });
  assert.deepEqual(changed.settings, { drawSeconds: 90, hintSeconds: 20, rounds: 4, stealing: "off" });
  assert.equal(updateSettings(changed, "p1", { drawSeconds: 7 }), changed);
});

test("the host can set any draw time (20s to 4 min, in 5s steps) and any hint pace (off, or 5 to 60s)", () => {
  const room = fullRoom();
  const set = (patch) => updateSettings(room, "p1", patch).settings;
  for (const ok of [20, 75, 135, 240]) assert.equal(set({ drawSeconds: ok }).drawSeconds, ok);
  for (const bad of [15, 77, 75.5, 245, "abc"]) assert.equal(updateSettings(room, "p1", { drawSeconds: bad }), room);
  for (const ok of [5, 13, 60]) assert.equal(set({ hintSeconds: ok }).hintSeconds, ok);
  assert.equal(set({ hintSeconds: 0 }).hintSeconds, 0);
  for (const bad of [4, 61, 12.5]) assert.equal(updateSettings(room, "p1", { hintSeconds: bad }), room);
});

test("partners swap roles each round and the game ends after the chosen number of rounds", () => {
  let room = updateSettings(fullRoom(), "p1", { rounds: 4 });
  room = startGame(room, "p1", 0, rng, WORDS);
  const drawers = [];
  for (let step = 0; step < 20 && room.phase !== "gameOver"; step += 1) {
    if (room.phase === "picking") drawers.push(room.round.teams.sky.drawerId);
    room = advanceRoom(room, room.round.phaseEndsAt, rng, WORDS);
  }
  assert.equal(room.phase, "gameOver");
  assert.equal(room.matches, 1);
  assert.deepEqual(drawers, ["p1", "p3", "p1", "p3"]);
  const again = startGame(room, "p1", 0, rng, WORDS);
  assert.deepEqual(again.scores, { sky: 0, tangerine: 0 });
});

test("dev bots fill the teams, pick a level and aim at hidden words", () => {
  let room = createRoom({ code: "BCDF", playerId: "p1", name: "Ana", now: 0, dev: true });
  for (let i = 0; i < 3; i += 1) room = addBot(room, "b" + i, 0);
  assert.equal(room.players.length, 4);
  assert.equal(addBot(room, "b9", 0), null);
  assert.ok(["easy", "medium", "hard"].includes(chooseBotLevel(() => 0.99)));
  room = startGame(room, "p1", 0, rng, WORDS);
  room = advanceRoom(room, room.round.phaseEndsAt, rng, WORDS);
  const guess = chooseBotGuess(room, room.round.teams.sky.guesserId, () => 0.1);
  assert.ok(room.round.teams.sky.words.includes(guess));
});

test("host passes to the earliest connected human", () => {
  assert.equal(setPlayerConnected(fullRoom(), "p1", false).hostId, "p2");
});
