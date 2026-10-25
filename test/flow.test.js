import test from "node:test";
import assert from "node:assert/strict";
import { io } from "socket.io-client";
import { startServer } from "../server/index.js";

function watch(socket) {
  let latest = null;
  const listeners = new Set();
  socket.on("state", (state) => {
    latest = state;
    for (const listener of listeners) listener(state);
  });
  return {
    get: () => latest,
    until(predicate, timeoutMs = 4000) {
      if (predicate(latest)) return Promise.resolve(latest);
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
          listeners.delete(onState);
          reject(new Error("Timed out waiting for room state"));
        }, timeoutMs);
        const onState = (state) => {
          if (!predicate(state)) return;
          clearTimeout(timeout);
          listeners.delete(onState);
          resolve(state);
        };
        listeners.add(onState);
      });
    }
  };
}

async function connect(url, clients) {
  const socket = io(url, { reconnection: false, timeout: 3000 });
  clients.push(socket);
  await new Promise((resolve, reject) => {
    socket.once("connect", resolve);
    socket.once("connect_error", reject);
  });
  return { socket, state: watch(socket) };
}

function emitAck(socket, eventName, payload) {
  return new Promise((resolve) => {
    if (payload === undefined) socket.emit(eventName, resolve);
    else socket.emit(eventName, payload, resolve);
  });
}

function correctPress(client, state) {
  const round = state.round;
  const letter = round.phrase[round.cursor];
  return emitAck(client.socket, "press", {
    letter,
    atIndex: round.cursor
  });
}

test("production flow supports two players, mistakes, timeout, disconnect, and rejects dev tools", async (t) => {
  const server = await startServer({
    port: 0,
    dev: false,
    timing: { countdown: 0.05, round: 1.5, roundEnd: 0.05 }
  });
  const clients = [];
  t.after(async () => {
    for (const client of clients) client.disconnect();
    await server.close();
  });
  const url = "http://127.0.0.1:" + server.port;
  const a = await connect(url, clients);
  const b = await connect(url, clients);
  const created = await emitAck(a.socket, "create_room", { name: "A" });
  const joined = await emitAck(b.socket, "join_room", { code: created.code, name: "B" });
  assert.equal(created.ok, true);
  assert.equal(joined.ok, true);
  const lobby = await a.state.until((state) => state?.phase === "lobby" && state.players.length === 2);
  await b.state.until((state) => state?.phase === "lobby" && state.players.length === 2);
  assert.equal(lobby.dev, false);
  assert.equal((await emitAck(a.socket, "add_bot")).ok, false);
  assert.equal((await emitAck(a.socket, "start_game")).ok, true);

  await a.state.until((state) => state?.phase === "playing");
  let state = a.state.get();
  while (state.phase === "playing") {
    const expected = state.round.phrase[state.round.cursor];
    const owner = state.round.owners[expected];
    const client = owner === created.playerId ? a : b;
    const cursor = state.round.cursor;
    assert.equal((await correctPress(client, state)).ok, true);
    state = await a.state.until((next) => next?.phase !== "playing" || next.round.cursor > cursor);
  }
  const clearedA = await a.state.until((next) => next?.phase === "roundEnd" && next.round.result === "cleared");
  const clearedB = await b.state.until((next) => next?.phase === "roundEnd" && next.round.result === "cleared");
  assert.equal(clearedA.level, 2);
  assert.equal(clearedB.round.result, "cleared");

  await a.state.until((next) => next?.phase === "playing" && next.round.roundId === 2);
  state = a.state.get();
  let cursor = state.round.cursor;
  const firstLetter = state.round.phrase[cursor];
  const firstOwner = state.round.owners[firstLetter];
  assert.equal((await emitAck(firstOwner === created.playerId ? a.socket : b.socket, "press", {
    letter: firstLetter,
    atIndex: cursor
  })).ok, true);
  state = await a.state.until((next) => next?.phase === "playing" && next.round.cursor > cursor);
  cursor = state.round.cursor;
  const expected = state.round.phrase[cursor];
  const wrong = Object.keys(state.round.owners).find((letter) => letter !== expected);
  const wrongOwner = state.round.owners[wrong];
  await emitAck(wrongOwner === created.playerId ? a.socket : b.socket, "press", { letter: wrong, atIndex: cursor });
  const resetA = await a.state.until((next) => next?.round.cursor === 0 && next.players.some((player) => player.stats.mistakes > 0));
  const resetB = await b.state.until((next) => next?.round.cursor === 0 && next.players.some((player) => player.stats.mistakes > 0));
  assert.equal(resetA.round.typedBy[0], null);
  assert.equal(resetB.round.typedBy[0], null);

  const timeout = await a.state.until((next) => next?.phase === "roundEnd" && next.round.result === "timeout", 3000);
  assert.equal(timeout.lives, 2);
  await a.state.until((next) => next?.phase === "playing" && next.round.roundId === 3);
  b.socket.disconnect();
  const afterDisconnect = await a.state.until((next) => next?.players.some((player) => player.id === joined.playerId && !player.connected));
  assert.equal(afterDisconnect.round.ownerLetters.length, 26);
  assert.ok(afterDisconnect.round.ownerLetters.every((entry) => entry.playerId === created.playerId));

  const invalid = await connect(url, clients);
  assert.deepEqual(await emitAck(invalid.socket, "join_room", { code: "ZZZZ", name: "Nope" }), { ok: false, error: "NOT_FOUND" });
  const fullHost = await connect(url, clients);
  const fullRoom = await emitAck(fullHost.socket, "create_room", { name: "Host" });
  const members = [];
  for (let index = 0; index < 7; index += 1) {
    const client = await connect(url, clients);
    const result = await emitAck(client.socket, "join_room", { code: fullRoom.code, name: "P" + index });
    assert.equal(result.ok, true);
    members.push(client);
  }
  const ninth = await connect(url, clients);
  assert.deepEqual(await emitAck(ninth.socket, "join_room", { code: fullRoom.code, name: "Ninth" }), { ok: false, error: "FULL" });
});

test("dev mode permits solo play, exposes bot metadata, and schedules bot presses", async (t) => {
  const server = await startServer({
    port: 0,
    dev: true,
    timing: { countdown: 0.05, round: 12, roundEnd: 0.05 }
  });
  const clients = [];
  t.after(async () => {
    for (const client of clients) client.disconnect();
    await server.close();
  });
  const host = await connect("http://127.0.0.1:" + server.port, clients);
  const created = await emitAck(host.socket, "create_room", { name: "Solo" });
  await host.state.until((state) => state?.players.length === 1);
  assert.equal(host.state.get().dev, true);
  assert.equal((await emitAck(host.socket, "start_game")).ok, true);
  await host.state.until((state) => state?.phase === "playing");
  assert.equal(host.state.get().round.ownerLetters.length, 26);
  await emitAck(host.socket, "leave_room");

  const room = await emitAck(host.socket, "create_room", { name: "BotHost" });
  const added = await emitAck(host.socket, "add_bot");
  assert.equal(added.ok, true);
  const withBot = await host.state.until((state) => state?.players.length === 2 && state.players.some((player) => player.isBot));
  assert.equal(withBot.players.find((player) => player.isBot).name, "Bot 1");
  const humanPresses = new Set();
  host.socket.on("state", (state) => {
    if (state?.phase !== "playing" || !state.round) return;
    const letter = state.round.phrase[state.round.cursor];
    const key = state.round.roundId + ":" + state.round.cursor;
    if (state.round.owners[letter] !== room.playerId || humanPresses.has(key)) return;
    humanPresses.add(key);
    host.socket.emit("press", { letter, atIndex: state.round.cursor }, () => {});
  });
  assert.equal((await emitAck(host.socket, "start_game")).ok, true);
  await host.state.until((state) => state?.phase === "playing");
  const botActed = await host.state.until((state) => {
    const bot = state?.players.find((player) => player.isBot);
    return bot && (bot.stats.correct > 0 || bot.stats.mistakes > 0);
  }, 5000);
  assert.ok(botActed.players.find((player) => player.isBot).stats.correct + botActed.players.find((player) => player.isBot).stats.mistakes > 0);
  assert.ok(room.code);
});
