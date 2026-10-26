import test from "node:test";
import assert from "node:assert/strict";
import {
  addBot,
  addPlayer,
  advanceRoom,
  applyGuess,
  canStart,
  chooseBotGuess,
  createRoom,
  hintsUnlocked,
  isClose,
  nextWakeAt,
  normalizeName,
  publicSnapshot,
  removePlayer,
  setPlayerConnected,
  startGame,
  switchTeam,
  uniqueName,
  validRoomCode,
  wordsMatch
} from "../server/game.js";

const rng = () => 0;
const SENTENCES = {
  4: ["THE CAT IS SLEEPING", "A DOG EATS SPAGHETTI", "THE SUN IS SMILING"],
  5: ["THE BULL ATE THE BUTTER", "A FISH RIDES A BIKE", "THE MOON WEARS A HAT"]
};

function fullRoom(dev = false) {
  let room = createRoom({ code: "BCDF", playerId: "p1", name: "Ana", now: 0, dev });
  for (const [id, name] of [["p2", "Bo"], ["p3", "Cy"], ["p4", "Di"]]) room = addPlayer(room, { playerId: id, name, now: 0 }).room;
  return room;
}

function drawingRoom(timing = {}) {
  const started = startGame(fullRoom(), "p1", 0, rng, SENTENCES, timing);
  return advanceRoom(started, started.round.phaseEndsAt, rng, SENTENCES, timing);
}

const teamOf = (room, id) => room.players.find((p) => p.id === id).team;

test("room codes use four allowed consonants and names are cleaned", () => {
  assert.equal(validRoomCode("bxkt"), true);
  assert.equal(validRoomCode("ABCD"), false);
  assert.equal(normalizeName("  Sam   Lee "), "Sam Lee");
  assert.equal(normalizeName("ThisNameIsWayTooLong"), null);
  assert.equal(uniqueName([{ name: "Sam" }], "sam"), "sam 2");
});

test("players fill two teams of two and a fifth player is turned away", () => {
  const room = fullRoom();
  assert.deepEqual(room.players.map((p) => p.team), ["sky", "tangerine", "sky", "tangerine"]);
  assert.equal(addPlayer(room, { playerId: "p5", name: "Ed", now: 0 }), null);
  assert.equal(room.players.find((p) => p.id === "p2").color, "#FF922B");
});

test("switching teams only works when the other team has room", () => {
  let room = createRoom({ code: "BCDF", playerId: "p1", name: "Ana", now: 0 });
  room = addPlayer(room, { playerId: "p2", name: "Bo", now: 0 }).room;
  const moved = switchTeam(room, "p1");
  assert.equal(teamOf(moved, "p1"), "tangerine");
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

test("each team gets its own sentence and partners swap roles every round", () => {
  const timing = { countdown: 1, draw: 10, roundEnd: 1 };
  let room = startGame(fullRoom(), "p1", 0, rng, SENTENCES, timing);
  const sky1 = room.round.teams.sky;
  const tan1 = room.round.teams.tangerine;
  assert.notEqual(sky1.words.join(" "), tan1.words.join(" "));
  assert.equal(sky1.words.length, 4);
  assert.deepEqual([sky1.drawerId, sky1.guesserId], ["p1", "p3"]);
  assert.deepEqual([tan1.drawerId, tan1.guesserId], ["p2", "p4"]);
  assert.equal(sky1.revealed[sky1.words.indexOf("THE") >= 0 ? sky1.words.indexOf("THE") : sky1.words.indexOf("A")], true);
  for (let step = 0; step < 3; step += 1) room = advanceRoom(room, room.round.phaseEndsAt, rng, SENTENCES, timing);
  assert.equal(room.round.number, 2);
  assert.deepEqual([room.round.teams.sky.drawerId, room.round.teams.sky.guesserId], ["p3", "p1"]);
  for (let step = 0; step < 3; step += 1) room = advanceRoom(room, room.round.phaseEndsAt, rng, SENTENCES, timing);
  assert.equal(room.round.teams.sky.words.length, 5);
});

test("guessers only see blanks; drawers see their own sentence; everyone sees all at round end", () => {
  const room = drawingRoom();
  const guesserView = publicSnapshot(room, 0, "p3");
  const drawerView = publicSnapshot(room, 0, "p1");
  const rivalView = publicSnapshot(room, 0, "p2");
  const words = room.round.teams.sky.words;
  const hidden = words.findIndex((word) => !["A", "THE"].includes(word));
  assert.match(guesserView.round.teams[0].words[hidden].text, /^_+$/);
  assert.equal(drawerView.round.teams[0].words[hidden].text, words[hidden]);
  assert.match(rivalView.round.teams[0].words[hidden].text, /^_+$/);
  const ended = advanceRoom(room, room.round.phaseEndsAt, rng, SENTENCES);
  assert.equal(publicSnapshot(ended, 0, "p3").round.teams[0].words[hidden].text, words[hidden]);
});

test("a right word is revealed and scores; near misses are flagged; drawers cannot guess", () => {
  let room = drawingRoom();
  const words = room.round.teams.sky.words;
  const target = words.find((word) => !["A", "THE"].includes(word));
  assert.equal(applyGuess(room, "p1", target, 1).room, room);
  const near = target.slice(0, -1) + (target.endsWith("Q") ? "Z" : "Q");
  const miss = applyGuess(room, "p3", near, 1);
  assert.equal(miss.room.round.teams.sky.guesses.at(-1).result, isClose(near, target) ? "close" : "wrong");
  room = applyGuess(room, "p3", target.toLowerCase(), 2).room;
  assert.equal(room.round.teams.sky.revealed[words.indexOf(target)], true);
  assert.equal(room.scores.sky, 10);
  assert.equal(room.players.find((p) => p.id === "p3").stats.guessed, 1);
  assert.equal(room.players.find((p) => p.id === "p1").stats.drawn, 1);
});

test("plurals match and typos one letter off are close", () => {
  assert.equal(wordsMatch("BANANAS", "BANANA"), true);
  assert.equal(wordsMatch("HATS", "HAT"), true);
  assert.equal(isClose("BUTER", "BUTTER"), true);
  assert.equal(isClose("DOG", "CAT"), false);
});

test("the first team to finish gets the bigger bonus and the round ends when both are done", () => {
  let room = drawingRoom();
  const solve = (r, team, guesser) => applyGuess(r, guesser, r.round.teams[team].words.join(" "), 5);
  const first = solve(room, "tangerine", "p4");
  room = first.room;
  assert.deepEqual(first.effects.at(-1), { kind: "teamDone", team: "tangerine", place: 1 });
  const hiddenTan = room.round.teams.tangerine.words.filter((w) => !["A", "THE"].includes(w)).length;
  assert.equal(room.scores.tangerine, hiddenTan * 10 + 30);
  assert.equal(room.phase, "drawing");
  room = solve(room, "sky", "p3").room;
  assert.equal(room.round.teams.sky.place, 2);
  assert.equal(room.phase, "roundEnd");
});

test("hint letters unlock over time for guessers only", () => {
  const room = drawingRoom({ hintEvery: 30 });
  const start = room.round.drawStartedAt;
  assert.equal(hintsUnlocked(room, start + 29_000), 0);
  assert.equal(hintsUnlocked(room, start + 31_000), 1);
  const before = publicSnapshot(room, start + 1_000, "p3").round.teams[0].words.map((w) => w.text).join(" ");
  const after = publicSnapshot(room, start + 31_000, "p3").round.teams[0].words.map((w) => w.text).join(" ");
  assert.notEqual(before, after);
  assert.equal(nextWakeAt(room, start + 1_000), start + 30_000);
});

test("after the last round the game ends and play again resets the scores", () => {
  const timing = { countdown: 1, draw: 1, roundEnd: 1, rounds: 2 };
  let room = startGame(fullRoom(), "p1", 0, rng, SENTENCES, timing);
  room = applyGuess(advanceRoom(room, room.round.phaseEndsAt, rng, SENTENCES, timing), "p3", "CAT DOG SUN SLEEPING", 1).room;
  for (let step = 0; step < 6 && room.phase !== "gameOver"; step += 1) room = advanceRoom(room, room.round.phaseEndsAt, rng, SENTENCES, timing);
  assert.equal(room.phase, "gameOver");
  assert.equal(room.matches, 1);
  assert.ok(room.scores.sky > 0);
  const again = startGame(room, "p1", 100, rng, SENTENCES, timing);
  assert.deepEqual(again.scores, { sky: 0, tangerine: 0 });
  assert.equal(again.round.number, 1);
});

test("dev bots join the open team and bot guessers aim at hidden words", () => {
  let room = createRoom({ code: "BCDF", playerId: "p1", name: "Ana", now: 0, dev: true });
  for (let i = 0; i < 3; i += 1) room = addBot(room, "b" + i, 0);
  assert.equal(room.players.length, 4);
  assert.equal(addBot(room, "b9", 0), null);
  room = advanceRoom(startGame(room, "p1", 0, rng, SENTENCES), 4000, rng, SENTENCES);
  const guess = chooseBotGuess(room, room.round.teams.sky.guesserId, () => 0.1);
  assert.ok(room.round.teams.sky.words.includes(guess));
});

test("host passes to the earliest connected human", () => {
  const room = setPlayerConnected(fullRoom(), "p1", false);
  assert.equal(room.hostId, "p2");
});
