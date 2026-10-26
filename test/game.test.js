import test from "node:test";
import assert from "node:assert/strict";
import {
  addPlayer,
  addBot,
  advanceRoom,
  applyPress,
  canStart,
  chooseBotPress,
  createRoom,
  dealLetters,
  disconnectPlayer,
  normalizeName,
  publicSnapshot,
  removePlayer,
  removeBot,
  selectPhrase,
  setPlayerConnected,
  startGame,
  startRound,
  uniqueName,
  validRoomCode
} from "../server/game.js";

const rng = () => 0;
const fixedPhrases = { 1: ["HOT DOG"], 2: ["SILLY ROBOT"], 3: ["MY SOCKS ARE ON FIRE"], 4: ["THE SILLY PENGUIN WORE MY HAT"], 5: ["THE WIGGLY ROBOT BORROWED THREE PIES"], 6: ["THE FLUFFY PENGUIN INVENTED THE PURPLE CAKE"] };

function hostRoom() {
  return createRoom({ code: "BCDF", playerId: "p1", name: "Rae", now: 10 });
}
function twoPlayerRoom() {
  return addPlayer(hostRoom(), { playerId: "p2", name: "Jo", now: 20 }).room;
}
function playingRoom(phrase = "HOT DOG") {
  const phrases = { ...fixedPhrases, 1: [phrase] };
  const started = startGame(twoPlayerRoom(), "p1", 0, rng, phrases);
  return advanceRoom(started, 3000, rng, phrases);
}
function pressOwned(room, playerId, letter, atIndex, now = 4000) {
  return applyPress(room, playerId, { letter, atIndex }, now);
}

test("room codes use four allowed consonants and are case-insensitive", () => {
  assert.equal(validRoomCode("BCDF"), true);
  assert.equal(validRoomCode("bcdf"), true);
  assert.equal(validRoomCode("ABCD"), false);
  assert.equal(validRoomCode("BCD"), false);
});

test("names are trimmed and limited to twelve characters", () => {
  assert.equal(normalizeName("  Sam  "), "Sam");
  assert.equal(normalizeName("   "), null);
  assert.equal(normalizeName("abcdefghijklM"), null);
});

test("duplicate names receive the first available numbered suffix", () => {
  let room = hostRoom();
  room = addPlayer(room, { playerId: "p2", name: "rae", now: 20 }).room;
  assert.equal(room.players[1].name, "rae 2");
  assert.equal(uniqueName(room.players, "Rae"), "Rae 3");
});

test("joining after a player leaves receives an unused color", () => {
  let room = hostRoom();
  room = addPlayer(room, { playerId: "p2", name: "Jo", now: 20 }).room;
  room = addPlayer(room, { playerId: "p3", name: "Kai", now: 30 }).room;
  room = removePlayer(room, "p2");
  room = addPlayer(room, { playerId: "p4", name: "Lee", now: 40 }).room;
  assert.notEqual(room.players[2].color, room.players[0].color);
});

test("public snapshots include lobby details and round ownership but omit join timestamps", () => {
  const snapshot = publicSnapshot(hostRoom(), 100);
  assert.equal(snapshot.phase, "lobby");
  assert.equal(snapshot.serverNow, 100);
  assert.equal(snapshot.players[0].color, "#4DABF7");
  assert.equal("joinedAt" in snapshot.players[0], false);
});

test("only the host can start after two players are connected", () => {
  let room = hostRoom();
  assert.equal(canStart(room, "p1"), false);
  room = addPlayer(room, { playerId: "p2", name: "Jo", now: 20 }).room;
  assert.equal(canStart(room, "p1"), true);
  assert.equal(canStart(room, "p2"), false);
});

test("dev rooms allow the solo host to start but production rooms still need two humans", () => {
  const devRoom = createRoom({ code: "BCDF", playerId: "p1", name: "Rae", now: 10, dev: true });
  assert.equal(canStart(devRoom, "p1"), true);
  assert.equal(canStart(hostRoom(), "p1"), false);
});

test("dev bots fill the next color slot and the most recently added bot is removed", () => {
  const devRoom = createRoom({ code: "BCDF", playerId: "p1", name: "Rae", now: 10, dev: true });
  const first = addBot(devRoom, "bot1", 20);
  const second = addBot(first, "bot2", 30);
  assert.equal(second.players[1].name, "Bot 1");
  assert.equal(second.players[1].isBot, true);
  assert.equal(second.players[1].color, "#FF922B");
  assert.deepEqual(removeBot(second).players.map((player) => player.id), ["p1", "bot1"]);
  assert.equal(addBot(hostRoom(), "bot1", 20), null);
});

test("bots wait for their phrase letter and can make a deliberate wrong press", () => {
  const room = createRoom({ code: "BCDF", playerId: "p1", name: "Rae", now: 10, dev: true });
  const withBot = addBot(room, "bot1", 20);
  const started = startGame(withBot, "p1", 0, rng, fixedPhrases);
  const owners = Object.fromEntries([..."ABCDEFGHIJKLMNOPQRSTUVWXYZ"].map((letter) => [letter, "bot1"]));
  const playing = { ...started, phase: "playing", round: { ...started.round, owners } };
  assert.deepEqual(chooseBotPress(playing, "bot1", () => 0.99), { letter: "H", atIndex: 0 });
  assert.equal(chooseBotPress(playing, "p1", () => 0.99), null);
  const values = [0, 0];
  const mistake = chooseBotPress(playing, "bot1", () => values.shift() ?? 0);
  assert.notEqual(mistake.letter, "H");
  assert.equal(mistake.atIndex, 0);
});

test("host handoff selects the earliest connected player", () => {
  let room = hostRoom();
  room = addPlayer(room, { playerId: "p2", name: "Jo", now: 20 }).room;
  room = addPlayer(room, { playerId: "p3", name: "Kai", now: 30 }).room;
  room = setPlayerConnected(room, "p1", false);
  assert.equal(room.hostId, "p2");
  assert.equal(room.players[0].connected, false);
});

test("leaving removes a player and transfers host", () => {
  let room = addPlayer(hostRoom(), { playerId: "p2", name: "Jo", now: 20 }).room;
  room = removePlayer(room, "p1");
  assert.equal(room.hostId, "p2");
  assert.deepEqual(room.players.map((player) => player.id), ["p2"]);
});

test("dealing gives all 26 letters only to connected players and stays balanced", () => {
  const owners = dealLetters("HOT DOG", ["p1", "p2", "p3"], rng);
  assert.equal(Object.keys(owners).length, 26);
  assert.deepEqual(new Set(Object.values(owners)), new Set(["p1", "p2", "p3"]));
  const counts = Object.values(owners).reduce((all, id) => ({ ...all, [id]: (all[id] || 0) + 1 }), {});
  assert.ok(Math.max(...Object.values(counts)) - Math.min(...Object.values(counts)) <= 1);
  for (const id of ["p1", "p2", "p3"]) {
    assert.ok([...new Set("HOTDOG")].some((letter) => owners[letter] === id));
  }
});

test("presses during countdown, from another owner, or with stale cursor are ignored", () => {
  const room = startGame(twoPlayerRoom(), "p1", 0, rng, fixedPhrases);
  const letterOwner = room.round.owners.H;
  assert.equal(room.phase, "countdown");
  assert.equal(applyPress(room, letterOwner, { letter: "H", atIndex: 0 }, 100).room, room);
  const wrongOwner = letterOwner === "p1" ? "p2" : "p1";
  const playing = advanceRoom(room, 3000, rng, fixedPhrases);
  assert.equal(applyPress(playing, wrongOwner, { letter: "H", atIndex: 0 }, 4000).room, playing);
  assert.equal(applyPress(playing, letterOwner, { letter: "H", atIndex: 2 }, 4000).room, playing);
});

test("correct presses advance the cursor and skip spaces", () => {
  let room = playingRoom();
  for (const letter of ["H", "O", "T"]) {
    const owner = room.round.owners[letter];
    room = pressOwned(room, owner, letter, room.round.cursor).room;
  }
  assert.equal(room.round.cursor, 4);
});

test("mistake clears only the current word and preserves finished words", () => {
  let room = playingRoom();
  for (const letter of ["H", "O", "T"]) {
    room = pressOwned(room, room.round.owners[letter], letter, room.round.cursor).room;
  }
  const cursor = room.round.cursor;
  const finishedWord = room.round.typedBy.slice(0, 3);
  const owner = room.round.owners.D;
  room = pressOwned(room, owner, "D", cursor).room;
  const wrong = Object.keys(room.round.owners).find((letter) => letter !== room.round.phrase[room.round.cursor] && room.round.owners[letter] === owner);
  assert.ok(wrong);
  room = pressOwned(room, owner, wrong, room.round.cursor).room;
  assert.equal(room.round.cursor, 4);
  assert.deepEqual(room.round.typedBy.slice(0, 3), finishedWord);
  assert.equal(room.round.typedBy[4], null);
  assert.equal(room.players.find((player) => player.id === owner).stats.mistakes, 1);
});

test("a wrong key on the first letter of a word still counts as a mistake", () => {
  let room = playingRoom();
  for (const letter of ["H", "O", "T"]) {
    room = pressOwned(room, room.round.owners[letter], letter, room.round.cursor).room;
  }
  const owner = room.round.owners.D;
  const wrong = Object.keys(room.round.owners).find((letter) => letter !== "D" && room.round.owners[letter] === owner);
  const result = pressOwned(room, owner, wrong, room.round.cursor);
  assert.equal(result.room.round.cursor, 4);
  assert.equal(result.room.players.find((player) => player.id === owner).stats.mistakes, 1);
  assert.equal(result.effect.kind, "mistake");
});

test("the last correct press clears the round, levels up, records best, and awards the final", () => {
  let room = playingRoom();
  while (room.phase === "playing") {
    const letter = room.round.phrase[room.round.cursor];
    const owner = room.round.owners[letter];
    room = pressOwned(room, owner, letter, room.round.cursor, 5000).room;
  }
  assert.equal(room.phase, "roundEnd");
  assert.equal(room.round.result, "cleared");
  assert.equal(room.level, 2);
  assert.equal(room.bestLevel, 1);
  assert.equal(room.players.reduce((sum, player) => sum + player.stats.finals, 0), 1);
});

test("a timeout removes a life and the result phase starts the next round", () => {
  let room = playingRoom();
  room = advanceRoom(room, room.round.phaseEndsAt);
  assert.equal(room.round.result, "timeout");
  assert.equal(room.lives, 2);
  room = advanceRoom(room, room.round.phaseEndsAt);
  assert.equal(room.phase, "countdown");
  assert.equal(room.round.roundId, 2);
});

test("the third timeout reaches game over after the result overlay", () => {
  let room = playingRoom();
  for (let lives = 3; lives > 0; lives -= 1) {
    room = advanceRoom(room, room.round.phaseEndsAt);
    assert.equal(room.lives, lives - 1);
    if (lives > 1) {
      room = advanceRoom(room, room.round.phaseEndsAt);
      room = advanceRoom(room, room.round.phaseEndsAt);
    } else {
      room = advanceRoom(room, room.round.phaseEndsAt);
    }
  }
  assert.equal(room.phase, "gameOver");
});

test("a disconnect mid-round gives the leaver's letters to connected players", () => {
  let room = playingRoom();
  const disconnected = disconnectPlayer(room, "p1", rng);
  assert.equal(disconnected.players.find((player) => player.id === "p1").connected, false);
  assert.equal(disconnected.hostId, "p2");
  assert.ok(Object.values(disconnected.round.owners).every((id) => id === "p2"));
});

test("phrases are not repeated until the entire level list is consumed", () => {
  const phrases = { 1: ["HOT DOG", "PIZZA TIME"] };
  let used = {};
  const first = selectPhrase(1, used, rng, phrases);
  used = first.usedPhrases;
  const second = selectPhrase(1, used, rng, phrases);
  used = second.usedPhrases;
  assert.notEqual(first.phrase, second.phrase);
  const third = selectPhrase(1, used, rng, phrases);
  assert.equal(third.phrase, first.phrase);
  assert.deepEqual(third.usedPhrases[1], [first.phrase]);
});


test("players can join between runs while the room shows game over", () => {
  const over = { ...twoPlayerRoom(), phase: "gameOver" };
  const joined = addPlayer(over, { playerId: "p3", name: "Late", now: 30 });
  assert.ok(joined);
  assert.equal(joined.room.players.length, 3);
});

test("from level 5 the letters are dealt again once half the phrase is typed", () => {
  const phrases = { ...fixedPhrases, 5: ["ABCD EFGH"] };
  let room = { ...twoPlayerRoom(), level: 5 };
  room = advanceRoom(startRound(room, 0, rng, phrases), 3000, rng, phrases);
  assert.deepEqual(publicSnapshot(room, 0).round.twists, { hint: false, shuffleKeys: true, midRoundDeal: true });
  let reshuffles = 0;
  for (let i = 0; i < 4; i += 1) {
    const { cursor, phrase, owners } = room.round;
    const result = pressOwned(room, owners[phrase[cursor]], phrase[cursor], cursor);
    if (result.effect.reshuffle) reshuffles += 1;
    room = result.room;
  }
  assert.equal(reshuffles, 1);
  assert.equal(room.round.reshuffled, true);
  assert.equal(Object.keys(room.round.owners).length, 26);
  const { cursor, phrase, owners } = room.round;
  const next = pressOwned(room, owners[phrase[cursor]], phrase[cursor], cursor);
  assert.equal(next.effect.reshuffle, false);
});

test("levels below 5 never re-deal mid-round", () => {
  let room = playingRoom("ABCD EFGH");
  for (let i = 0; i < 5; i += 1) {
    const { cursor, phrase, owners } = room.round;
    const result = pressOwned(room, owners[phrase[cursor]], phrase[cursor], cursor);
    assert.equal(result.effect.reshuffle, false);
    room = result.room;
  }
});
