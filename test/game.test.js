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
  updateSettings,
  wordCountForRound,
  validRoomCode,
  wordsMatch
} from "../server/game.js";

const rng = () => 0;
const SENTENCES_3 = { 3: ["CATS EAT PIZZA", "A FLYING PIG", "THE DANCING COW"] };
const SENTENCES = {
  3: ["CATS EAT PIZZA", "A FLYING PIG", "THE DANCING COW"],
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
  assert.equal(sky1.words.length, 3);
  assert.deepEqual([sky1.drawerId, sky1.guesserId], ["p1", "p3"]);
  assert.deepEqual([tan1.drawerId, tan1.guesserId], ["p2", "p4"]);
  assert.equal(sky1.revealed[sky1.words.indexOf("THE") >= 0 ? sky1.words.indexOf("THE") : sky1.words.indexOf("A")], true);
  for (let step = 0; step < 3; step += 1) room = advanceRoom(room, room.round.phaseEndsAt, rng, SENTENCES, timing);
  assert.equal(room.round.number, 2);
  assert.deepEqual([room.round.teams.sky.drawerId, room.round.teams.sky.guesserId], ["p3", "p1"]);
  for (let step = 0; step < 3; step += 1) room = advanceRoom(room, room.round.phaseEndsAt, rng, SENTENCES, timing);
  assert.equal(room.round.teams.sky.words.length, 4);
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
  assert.deepEqual(first.effects.at(-1), { kind: "teamDone", team: "tangerine", place: 1, bonus: 20 });
  const hiddenTan = room.round.teams.tangerine.words.filter((w) => !["A", "THE"].includes(w)).length;
  assert.equal(room.scores.tangerine, hiddenTan * 10 + 20);
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
  room = applyGuess(advanceRoom(room, room.round.phaseEndsAt, rng, SENTENCES, timing), "p3", "CATS EAT PIZZA FLYING PIG DANCING", 1).room;
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

test("guessing a word from the other team's sentence steals it for slightly fewer points", () => {
  let room = drawingRoom();
  const tanWord = room.round.teams.tangerine.words.find((w) => !["A", "THE"].includes(w) && !room.round.teams.sky.words.includes(w));
  const result = applyGuess(room, "p3", tanWord, 1);
  room = result.room;
  assert.equal(result.effects[0].kind, "stolen");
  assert.equal(room.scores.sky, 7);
  const index = room.round.teams.tangerine.words.indexOf(tanWord);
  assert.equal(room.round.teams.tangerine.revealed[index], true);
  assert.equal(room.round.teams.tangerine.by[index], "sky");
  assert.equal(room.round.teams.sky.guesses.at(-1).result, "stolen");
  assert.equal(room.players.find((p) => p.id === "p3").stats.stolen, 1);
  // The owners can no longer score it.
  const after = applyGuess(room, "p4", tanWord, 2).room;
  assert.equal(after.scores.tangerine, 0);
  assert.equal(publicSnapshot(room, 0, "p4").round.teams[1].words[index].by, "sky");
});

test("a sentence finished by a steal gives its owners no finish bonus", () => {
  let room = drawingRoom();
  const tan = room.round.teams.tangerine.words.filter((w) => !["A", "THE"].includes(w));
  const overlap = (w) => room.round.teams.sky.words.includes(w);
  room = applyGuess(room, "p4", tan.slice(0, -1).join(" "), 1).room;
  const last = tan.at(-1);
  if (overlap(last)) return;
  const result = applyGuess(room, "p3", last, 2);
  const done = result.effects.find((e) => e.kind === "teamDone");
  assert.deepEqual(done, { kind: "teamDone", team: "tangerine", place: 0, bonus: 0 });
  assert.equal(result.room.scores.tangerine, (tan.length - 1) * 10);
});

test("a team that finished its own sentence can keep stealing", () => {
  let room = drawingRoom();
  room = applyGuess(room, "p3", room.round.teams.sky.words.join(" "), 1).room;
  assert.equal(room.round.teams.sky.done, true);
  const tanWord = room.round.teams.tangerine.words.find((w) => !["A", "THE"].includes(w) && !room.round.teams.sky.words.includes(w));
  const before = room.scores.sky;
  room = applyGuess(room, "p3", tanWord, 2).room;
  assert.equal(room.scores.sky, before + 7);
});

test("the host can change draw time, hints, sentence length and rounds in the lobby", () => {
  const room = fullRoom();
  assert.equal(updateSettings(room, "p2", { drawSeconds: 60 }), room);
  const changed = updateSettings(room, "p1", { drawSeconds: 120, hintSeconds: 0, words: 3, rounds: 6, bogus: 1 });
  assert.deepEqual(changed.settings, { drawSeconds: 120, hintSeconds: 0, words: "3", rounds: 6 });
  assert.equal(updateSettings(changed, "p1", { drawSeconds: 7 }), changed);
  let game = startGame(changed, "p1", 0, rng, SENTENCES_3);
  assert.equal(game.round.teams.sky.words.length, 3);
  game = advanceRoom(game, game.round.phaseEndsAt, rng, SENTENCES_3);
  assert.equal(game.round.phaseEndsAt - game.round.drawStartedAt, 120000);
  assert.equal(hintsUnlocked(game, game.round.drawStartedAt + 100000), 0);
  assert.equal(publicSnapshot(game, 0, "p1").round.total, 6);
  assert.equal(publicSnapshot(game, 0, "p1").settings.words, "3");
});

test("mixed sentence lengths grow from 3 to 5 words over the match", () => {
  const mix = { words: "mix", rounds: 4 };
  assert.deepEqual([1, 2, 3, 4].map((r) => wordCountForRound(r, mix)), [3, 3, 4, 5]);
  assert.deepEqual([1, 2].map((r) => wordCountForRound(r, { words: "mix", rounds: 2 })), [3, 4]);
  assert.equal(wordCountForRound(3, { words: "5", rounds: 4 }), 5);
});

test("a team that solves its own sentence first gets first place even after the other sentence was stolen out", () => {
  let room = drawingRoom();
  const sky = room.round.teams.sky.words.filter((w) => !["A", "THE"].includes(w));
  const tan = room.round.teams.tangerine.words;
  if (sky.some((w) => tan.includes(w))) return;
  room = applyGuess(room, "p4", sky.join(" "), 1).room;
  assert.equal(room.round.teams.sky.done, true);
  assert.equal(room.round.teams.sky.place, 0);
  const finish = applyGuess(room, "p4", tan.join(" "), 2).effects.find((e) => e.kind === "teamDone");
  assert.deepEqual(finish, { kind: "teamDone", team: "tangerine", place: 1, bonus: 20 });
});
