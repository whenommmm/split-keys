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
    until(predicate, timeoutMs = 5000) {
      if (predicate(latest)) return Promise.resolve(latest);
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => { listeners.delete(onState); reject(new Error("Timed out waiting for state")); }, timeoutMs);
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
  await new Promise((resolve, reject) => { socket.once("connect", resolve); socket.once("connect_error", reject); });
  return { socket, state: watch(socket) };
}

const ack = (socket, event, payload) => new Promise((resolve) => (payload === undefined ? socket.emit(event, resolve) : socket.emit(event, payload, resolve)));
const nextEvent = (socket, event, ms = 3000) => new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error("no " + event)), ms);
  socket.once(event, (data) => { clearTimeout(timer); resolve(data); });
});

test("four players race to guess their team's sentence while drawings stay within each team", async (t) => {
  const server = await startServer({ port: 0, dev: false, timing: { countdown: 0.05, draw: 3, roundEnd: 0.1, rounds: 2 } });
  const clients = [];
  t.after(async () => { for (const c of clients) c.disconnect(); await server.close(); });
  const url = "http://127.0.0.1:" + server.port;
  const [a, b, c, d] = [await connect(url, clients), await connect(url, clients), await connect(url, clients), await connect(url, clients)];
  const created = await ack(a.socket, "create_room", { name: "Ana" });
  const ids = { a: created.playerId };
  for (const [name, client, letter] of [["Bo", b, "b"], ["Cy", c, "c"], ["Di", d, "d"]]) {
    const joined = await ack(client.socket, "join_room", { code: created.code, name });
    assert.equal(joined.ok, true);
    ids[letter] = joined.playerId;
  }
  const fifth = await connect(url, clients);
  assert.deepEqual(await ack(fifth.socket, "join_room", { code: created.code, name: "Ed" }), { ok: false, error: "FULL" });

  const lobby = await a.state.until((s) => s?.players.length === 4);
  assert.deepEqual(lobby.teams.map((team) => team.playerIds.length), [2, 2]);
  assert.equal((await ack(a.socket, "start_game")).ok, true);

  const drawing = await a.state.until((s) => s?.phase === "drawing");
  const sky = drawing.round.teams.find((team) => team.team === "sky");
  assert.equal(sky.drawerId, ids.a);
  assert.equal(sky.guesserId, ids.c);
  const fullSentence = sky.words.map((word) => word.text).join(" ");
  assert.doesNotMatch(fullSentence, /_/);
  const guesserView = await c.state.until((s) => s?.phase === "drawing");
  assert.match(guesserView.round.teams[0].words.map((w) => w.text).join(" "), /_/);

  // The drawing reaches the teammate and the rivals (who may steal), but not the drawer.
  const teammateDraw = nextEvent(c.socket, "draw");
  const rivalDraw = nextEvent(d.socket, "draw");
  let drawerEcho = false;
  a.socket.once("draw", () => { drawerEcho = true; });
  a.socket.emit("draw", { op: "stroke", c: "#FF5252", s: 6, p: [0.1, 0.1, 0.5, 0.5] });
  const relayed = await teammateDraw;
  assert.equal(relayed.op, "stroke");
  assert.equal(relayed.team, "sky");
  assert.deepEqual(relayed.p, [0.1, 0.1, 0.5, 0.5]);
  assert.equal((await rivalDraw).team, "sky");
  const fill = nextEvent(c.socket, "draw");
  a.socket.emit("draw", { op: "fill", c: "#FFD43B", p: [0.3, 0.3] });
  assert.deepEqual(await fill, { roundId: drawing.round.roundId, team: "sky", op: "fill", t: "fill", c: "#FFD43B", s: 0, p: [0.3, 0.3] });
  await new Promise((r) => setTimeout(r, 100));
  assert.equal(drawerEcho, false);

  // A late reconnect of the guesser gets the canvas back.
  const resumed = await connect(url, clients);
  const canvases = [];
  resumed.socket.on("canvas", (canvas) => canvases.push(canvas));
  await ack(resumed.socket, "join_room", { code: created.code, name: "Cy", playerId: ids.c });
  await new Promise((r) => setTimeout(r, 150));
  assert.deepEqual(canvases.map((canvas) => [canvas.team, canvas.strokes.length]), [["sky", 2], ["tangerine", 0]]);

  // Sky's guesser solves the sentence.
  const rivals = nextEvent(a.socket, "canvases", 5000);
  assert.equal((await ack(resumed.socket, "guess", { text: fullSentence })).ok, true);
  const solved = await a.state.until((s) => s?.round?.teams[0].done);
  assert.equal(solved.round.teams[0].place, 1);
  assert.ok(solved.teams[0].score >= 20);
  const ended = await a.state.until((s) => s?.phase === "roundEnd");
  assert.equal(ended.round.teams[1].done, false);
  const both = await rivals;
  assert.equal(both.sky.length, 2);
  assert.deepEqual(both.tangerine, []);

  const round2 = await a.state.until((s) => s?.phase === "drawing" && s.round.number === 2, 5000);
  assert.equal(round2.round.teams[0].drawerId, ids.c);
  await a.state.until((s) => s?.phase === "gameOver", 8000);
});

test("a player who reconnects before the old connection drops takes over the seat", async (t) => {
  const server = await startServer({ port: 0, dev: false });
  const clients = [];
  t.after(async () => { for (const c of clients) c.disconnect(); await server.close(); });
  const url = "http://127.0.0.1:" + server.port;
  const a = await connect(url, clients);
  const created = await ack(a.socket, "create_room", { name: "A" });
  const again = await connect(url, clients);
  assert.deepEqual(await ack(again.socket, "join_room", { code: created.code, name: "A", playerId: created.playerId }), { ok: true, code: created.code, playerId: created.playerId });
  await new Promise((r) => setTimeout(r, 100));
  assert.equal(a.socket.connected, false);
});

test("dev mode lets the host fill the room with bots that guess and doodle", async (t) => {
  const server = await startServer({ port: 0, dev: true, timing: { countdown: 0.05, draw: 3, roundEnd: 0.1 } });
  const clients = [];
  t.after(async () => { for (const c of clients) c.disconnect(); await server.close(); });
  const host = await connect("http://127.0.0.1:" + server.port, clients);
  await ack(host.socket, "create_room", { name: "Solo" });
  for (let i = 0; i < 3; i += 1) assert.equal((await ack(host.socket, "add_bot")).ok, true);
  assert.equal((await ack(host.socket, "add_bot")).ok, false);
  assert.equal((await ack(host.socket, "start_game")).ok, true);
  // The host draws for Sky in round 1, so the bot doodles show up in Tangerine's drawing at the reveal.
  const reveal = await nextEvent(host.socket, "canvases", 6000);
  assert.ok(reveal.tangerine.length > 0);
});
