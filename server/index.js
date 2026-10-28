import { createServer } from "node:http";
import { randomBytes } from "node:crypto";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";
import express from "express";
import { Server } from "socket.io";
import { WORDS } from "./words.js";
import {
  BOT_GUESS_MAX_MS,
  BOT_GUESS_MIN_MS,
  CODE_ALPHABET,
  MAX_PLAYERS,
  TEAM_IDS,
  addBot,
  addPlayer,
  advanceRoom,
  applyGuess,
  botFace,
  canStart,
  chooseBotGuess,
  chooseBotLevel,
  chooseWord,
  createRoom,
  nextWakeAt,
  normalizeName,
  publicSnapshot,
  removeBot,
  removePlayer,
  setPlayerConnected,
  startGame,
  switchTeam,
  teamOfDrawer,
  updateSettings,
  teamOfGuesser,
  validRoomCode
} from "./game.js";

const buildPath = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../Builds/WebGL");
const MAX_STROKES = 600;
const MAX_POINTS = 40000;
const AVATAR_LIMITS = { strokes: 300, points: 12000 };
const CANVAS_LIMITS = { strokes: MAX_STROKES, points: MAX_POINTS };

function makeId() {
  return randomBytes(6).toString("base64url");
}

function makeRoomCode(rooms) {
  for (let attempt = 0; attempt < 32; attempt += 1) {
    let code = "";
    for (let index = 0; index < 4; index += 1) code += CODE_ALPHABET[randomBytes(1)[0] % CODE_ALPHABET.length];
    if (!rooms.has(code)) return code;
  }
  return null;
}

// Keeps a stroke sane: colors are #RRGGBB, sizes small, points are 0..1 pairs.
function cleanStroke(op) {
  const color = typeof op.c === "string" && /^#[0-9A-Fa-f]{6}$/.test(op.c) ? op.c : "#15121F";
  const size = Math.min(40, Math.max(1, Number(op.s) || 4));
  return { c: color, s: size, p: cleanPoints(op.p) };
}

function cleanPoints(points) {
  if (!Array.isArray(points)) return [];
  const out = [];
  for (let i = 0; i + 1 < points.length && out.length < 800; i += 2) {
    const x = Number(points[i]);
    const y = Number(points[i + 1]);
    if (!Number.isFinite(x) || !Number.isFinite(y)) continue;
    out.push(Math.round(Math.min(1, Math.max(0, x)) * 1000) / 1000, Math.round(Math.min(1, Math.max(0, y)) * 1000) / 1000);
  }
  return out;
}

// Applies one stroke/more/fill/undo/clear message to a stored drawing and returns what to relay,
// or null if it was refused.
function applyDrawOp(canvas, op, limits) {
  if (op.op === "stroke") {
    const stroke = cleanStroke(op);
    if (!stroke.p.length || canvas.strokes.length >= limits.strokes || canvas.points + stroke.p.length > limits.points) return null;
    canvas.strokes.push(stroke);
    canvas.points += stroke.p.length;
    return { op: "stroke", ...stroke };
  }
  if (op.op === "more") {
    const last = canvas.strokes[canvas.strokes.length - 1];
    const points = cleanPoints(op.p);
    if (!last || last.t === "fill" || !points.length || canvas.points + points.length > limits.points) return null;
    last.p.push(...points);
    canvas.points += points.length;
    return { op: "more", p: points };
  }
  if (op.op === "fill") {
    const point = cleanPoints(op.p).slice(0, 2);
    if (point.length < 2 || canvas.strokes.length >= limits.strokes) return null;
    const fill = { t: "fill", c: cleanStroke(op).c, s: 0, p: point };
    canvas.strokes.push(fill);
    canvas.points += 2;
    return { op: "fill", ...fill };
  }
  if (op.op === "undo") {
    const removed = canvas.strokes.pop();
    if (removed) canvas.points -= removed.p.length;
    return { op: "undo" };
  }
  if (op.op === "clear") {
    canvas.strokes = [];
    canvas.points = 0;
    return { op: "clear" };
  }
  return null;
}

// Rebuilds a whole drawing from an uploaded stroke list, dropping anything malformed or over the limits.
function cleanDrawing(list, limits) {
  const canvas = { strokes: [], points: 0 };
  if (!Array.isArray(list)) return canvas;
  for (const entry of list) {
    if (!entry || typeof entry !== "object") continue;
    applyDrawOp(canvas, { ...entry, op: entry.t === "fill" ? "fill" : "stroke" }, limits);
  }
  return canvas;
}

export async function startServer({
  port = Number(process.env.PORT) || 3000,
  dev = process.env.DEV === "1",
  timing = {}
} = {}) {
  const app = express();
  const httpServer = createServer(app);
  const io = new Server(httpServer, { maxHttpBufferSize: 256 * 1024 });
  const rooms = new Map();
  const canvases = new Map();
  const avatars = new Map();
  const phaseTimers = new Map();
  const botTimers = new Map();
  const disconnectTimers = new Map();
  const emptyRoomTimers = new Map();
  const playerSockets = new Map();
  let shuttingDown = false;

  app.disable("x-powered-by");
  app.get("/health", (_request, response) => response.json({ ok: true, rooms: rooms.size }));
  app.use((request, response, next) => {
    if (request.path.endsWith(".br")) {
      const originalExtension = path.extname(request.path.slice(0, -3));
      response.setHeader("Content-Encoding", "br");
      if (originalExtension === ".wasm") response.type("application/wasm");
      else if (originalExtension === ".js") response.type("application/javascript");
      else response.type("application/octet-stream");
    }
    next();
  });
  app.use(express.static(buildPath));

  const key = (code, playerId) => code + ":" + playerId;
  const canvasKey = (code, roundId, team) => code + ":" + roundId + ":" + team;
  const socketOf = (code, playerId) => playerSockets.get(key(code, playerId));

  function clearPlayerTimer(code, playerId) {
    clearTimeout(disconnectTimers.get(key(code, playerId)));
    disconnectTimers.delete(key(code, playerId));
  }

  function clearBotTimers(code) {
    const timers = botTimers.get(code);
    if (timers) for (const timer of timers.values()) clearTimeout(timer);
    botTimers.delete(code);
  }

  function clearRoomTimers(code) {
    clearTimeout(phaseTimers.get(code));
    phaseTimers.delete(code);
    clearTimeout(emptyRoomTimers.get(code));
    emptyRoomTimers.delete(code);
    clearBotTimers(code);
    for (const map of [playerSockets, canvases, avatars]) {
      for (const entry of map.keys()) if (entry.startsWith(code + ":")) map.delete(entry);
    }
    for (const [entry, timer] of disconnectTimers) {
      if (entry.startsWith(code + ":")) {
        clearTimeout(timer);
        disconnectTimers.delete(entry);
      }
    }
  }

  // Every player gets a snapshot tailored to them (drawers see their sentence, guessers don't).
  function sendState(code) {
    const room = rooms.get(code);
    if (!room) return;
    const now = Date.now();
    for (const player of room.players) {
      const socket = socketOf(code, player.id);
      if (socket) socket.emit("state", publicSnapshot(room, now, player.id, timing));
    }
  }

  function emitToTeam(code, team, event, payload, exceptId = null) {
    const room = rooms.get(code);
    if (!room) return;
    for (const player of room.players) {
      if (player.team !== team || player.id === exceptId) continue;
      socketOf(code, player.id)?.emit(event, payload);
    }
  }

  function strokesFor(code, roundId, team) {
    const entry = canvasKey(code, roundId, team);
    if (!canvases.has(entry)) canvases.set(entry, { strokes: [], points: 0 });
    return canvases.get(entry);
  }

  // A (re)joining player gets both drawings back: their own team's and the one they can steal from.
  function sendCanvas(socket, room) {
    if (!room?.round || !["picking", "drawing"].includes(room.phase)) return;
    for (const team of TEAM_IDS) {
      socket.emit("canvas", { roundId: room.round.roundId, team, strokes: strokesFor(room.code, room.round.roundId, team).strokes });
    }
  }

  // Each player's capsule self-portrait, drawn in the lobby and kept for the whole visit.
  function avatarOf(code, playerId) {
    const entry = key(code, playerId);
    if (!avatars.has(entry)) avatars.set(entry, { strokes: [], points: 0 });
    return avatars.get(entry);
  }

  function avatarsPayload(code) {
    const room = rooms.get(code);
    return { list: (room?.players || []).map((player) => ({ playerId: player.id, strokes: avatarOf(code, player.id).strokes })) };
  }

  // Avatars can only change between matches, so they stay fixed while people play.
  const canEditAvatars = (room) => ["lobby", "gameOver"].includes(room?.phase);

  function handleAvatarDraw(code, playerId, op) {
    const room = rooms.get(code);
    if (!canEditAvatars(room) || !room.players.some((player) => player.id === playerId) || !op || typeof op !== "object") return;
    const relay = applyDrawOp(avatarOf(code, playerId), op, AVATAR_LIMITS);
    if (relay) emitToRoomExcept(code, "avatar_draw", { playerId, ...relay }, playerId);
  }

  function setAvatar(code, playerId, strokes, exceptId) {
    avatars.set(key(code, playerId), cleanDrawing(strokes, AVATAR_LIMITS));
    emitToRoomExcept(code, "avatar", { playerId, strokes: avatarOf(code, playerId).strokes }, exceptId);
  }

  function emitToRoomExcept(code, event, payload, exceptId) {
    const room = rooms.get(code);
    if (!room) return;
    for (const player of room.players) {
      if (player.id !== exceptId) socketOf(code, player.id)?.emit(event, payload);
    }
  }

  function canvasesPayload(code) {
    const room = rooms.get(code);
    const payload = { roundId: room.round.roundId, sky: [], tangerine: [] };
    for (const team of TEAM_IDS) payload[team] = strokesFor(code, room.round.roundId, team).strokes;
    return payload;
  }

  const hasConnectedHuman = (room) => room.players.some((player) => player.connected && !player.isBot);

  function scheduleEmptyRoom(code) {
    const room = rooms.get(code);
    if (!room || hasConnectedHuman(room)) {
      clearTimeout(emptyRoomTimers.get(code));
      emptyRoomTimers.delete(code);
      return;
    }
    if (emptyRoomTimers.has(code)) return;
    emptyRoomTimers.set(code, setTimeout(() => {
      emptyRoomTimers.delete(code);
      if (!hasConnectedHuman(rooms.get(code) || { players: [] })) {
        rooms.delete(code);
        clearRoomTimers(code);
      }
    }, 120_000));
  }

  // Stores the new room state, shares both drawings when a round ends and drops old canvases.
  function setRoom(code, next) {
    const previous = rooms.get(code);
    rooms.set(code, next);
    if (previous?.phase === "drawing" && next.phase === "roundEnd") io.to(code).emit("canvases", canvasesPayload(code));
    if (previous && previous.roundId !== next.roundId) {
      for (const entry of [...canvases.keys()]) {
        if (entry.startsWith(code + ":") && !entry.startsWith(code + ":" + next.roundId + ":")) canvases.delete(entry);
      }
    }
  }

  function scheduleRoom(code) {
    clearTimeout(phaseTimers.get(code));
    phaseTimers.delete(code);
    const wake = nextWakeAt(rooms.get(code), Date.now(), timing);
    if (wake == null) return;
    phaseTimers.set(code, setTimeout(() => {
      phaseTimers.delete(code);
      const current = rooms.get(code);
      if (!current) return;
      const next = advanceRoom(current, Date.now(), Math.random, WORDS, timing);
      if (next !== current) setRoom(code, next);
      sendState(code);
      scheduleRoom(code);
      scheduleBots(code);
      scheduleEmptyRoom(code);
    }, Math.max(0, wake - Date.now()) + 5));
  }

  // Dev bots: drawers pick a difficulty and scribble, guessers type now and then.
  function scheduleBots(code) {
    const room = rooms.get(code);
    if (!dev || !["picking", "drawing"].includes(room?.phase)) {
      clearBotTimers(code);
      return;
    }
    let pending = botTimers.get(code);
    if (!pending) {
      pending = new Map();
      botTimers.set(code, pending);
    }
    for (const player of room.players) {
      if (!player.isBot || pending.has(player.id)) continue;
      if (room.phase === "picking") {
        const team = teamOfDrawer(room, player.id);
        if (!team || room.round.teams[team].choice) continue;
        pending.set(player.id, setTimeout(() => {
          pending.delete(player.id);
          handleChoice(code, player.id, chooseBotLevel());
        }, 1500));
        continue;
      }
      const guessing = teamOfGuesser(room, player.id);
      const drawing = teamOfDrawer(room, player.id);
      if (!guessing && !drawing) continue;
      const delay = guessing ? BOT_GUESS_MIN_MS + Math.random() * (BOT_GUESS_MAX_MS - BOT_GUESS_MIN_MS) : 900;
      pending.set(player.id, setTimeout(() => {
        pending.delete(player.id);
        const current = rooms.get(code);
        if (current?.phase !== "drawing") return;
        if (guessing) {
          const text = chooseBotGuess(current, player.id);
          if (text) handleGuess(code, player.id, text);
        } else if (teamOfDrawer(current, player.id)) {
          const x = Math.random() * 0.7 + 0.15;
          const y = Math.random() * 0.7 + 0.15;
          const points = [];
          for (let i = 0; i < 12; i += 1) points.push(x + Math.sin(i * 0.6) * 0.012 * i, y + Math.cos(i * 0.6) * 0.012 * i);
          handleDraw(code, player.id, { op: "stroke", c: "#15121F", s: 6, p: points });
        }
        scheduleBots(code);
      }, delay));
    }
  }

  function enterRoom(socket, code, player) {
    socket.data.roomCode = code;
    socket.data.playerId = player.id;
    socket.join(code);
    playerSockets.set(key(code, player.id), socket);
    clearTimeout(emptyRoomTimers.get(code));
    emptyRoomTimers.delete(code);
    sendState(code);
    const room = rooms.get(code);
    sendCanvas(socket, room);
    socket.emit("avatars", avatarsPayload(code));
    if (room?.phase === "roundEnd") socket.emit("canvases", canvasesPayload(code));
  }

  function leaveSocket(socket, acknowledge) {
    const { roomCode, playerId } = socket.data;
    const room = rooms.get(roomCode);
    if (!room || !playerId) {
      if (acknowledge) acknowledge({ ok: true });
      return;
    }
    clearPlayerTimer(roomCode, playerId);
    socket.leave(roomCode);
    socket.data.roomCode = null;
    socket.data.playerId = null;
    playerSockets.delete(key(roomCode, playerId));
    avatars.delete(key(roomCode, playerId));
    setRoom(roomCode, removePlayer(room, playerId));
    sendState(roomCode);
    scheduleRoom(roomCode);
    scheduleBots(roomCode);
    scheduleEmptyRoom(roomCode);
    if (acknowledge) acknowledge({ ok: true });
  }

  function handleChoice(code, playerId, level) {
    const room = rooms.get(code);
    if (!room) return { ok: false, error: "NOT_IN_ROOM" };
    const next = chooseWord(room, playerId, level, Date.now(), Math.random, timing);
    if (next === room) return { ok: false, error: "IGNORED" };
    setRoom(code, next);
    const team = TEAM_IDS.find((id) => next.round.teams[id].drawerId === playerId);
    io.to(code).emit("fx", { kind: "picked", team, level: next.round.teams[team].choice });
    sendState(code);
    scheduleRoom(code);
    scheduleBots(code);
    return { ok: true };
  }

  function handleGuess(code, playerId, text) {
    const room = rooms.get(code);
    if (!room) return { ok: false, error: "NOT_IN_ROOM" };
    const result = applyGuess(room, playerId, text, Date.now(), timing);
    if (result.room === room) return { ok: false, error: "IGNORED" };
    setRoom(code, result.room);
    for (const effect of result.effects) {
      if (effect.kind === "solved" || effect.kind === "stolen") io.to(code).emit("fx", effect);
      else emitToTeam(code, effect.team, "fx", effect);
    }
    sendState(code);
    scheduleRoom(code);
    scheduleBots(code);
    return { ok: true };
  }

  function handleDraw(code, playerId, op) {
    const room = rooms.get(code);
    if (room?.phase !== "drawing" || !op || typeof op !== "object") return;
    const team = teamOfDrawer(room, playerId);
    if (!team) return;
    const relay = applyDrawOp(strokesFor(code, room.round.roundId, team), op, CANVAS_LIMITS);
    // Everyone but the drawer sees the drawing: teammates guess it, opponents can steal from it.
    if (relay) emitToRoomExcept(code, "draw", { roundId: room.round.roundId, team, ...relay }, playerId);
  }

  io.on("connection", (socket) => {
    socket.on("create_room", (payload = {}, acknowledge = () => {}) => {
      const name = normalizeName(payload.name);
      if (!name) return acknowledge({ ok: false, error: "BAD_NAME" });
      const code = makeRoomCode(rooms);
      if (!code) return acknowledge({ ok: false, error: "UNAVAILABLE" });
      const playerId = makeId();
      const room = createRoom({ code, playerId, name, now: Date.now(), dev });
      rooms.set(code, room);
      enterRoom(socket, code, room.players[0]);
      acknowledge({ ok: true, code, playerId });
    });

    socket.on("join_room", (payload = {}, acknowledge = () => {}) => {
      const code = typeof payload.code === "string" ? payload.code.trim().toUpperCase() : "";
      const name = normalizeName(payload.name);
      if (!name) return acknowledge({ ok: false, error: "BAD_NAME" });
      if (!validRoomCode(code) || !rooms.has(code)) return acknowledge({ ok: false, error: "NOT_FOUND" });
      const room = rooms.get(code);
      if (payload.playerId) {
        const saved = room.players.find((player) => player.id === payload.playerId && !player.isBot);
        if (saved) {
          if (saved.connected) {
            // Same player reconnecting before the old connection timed out: move the seat over.
            const stale = socketOf(code, saved.id);
            if (stale && stale.id !== socket.id) {
              stale.data.roomCode = null;
              stale.data.playerId = null;
              stale.leave(code);
              stale.disconnect(true);
            }
            enterRoom(socket, code, saved);
            return acknowledge({ ok: true, code, playerId: saved.id });
          }
          clearPlayerTimer(code, saved.id);
          setRoom(code, setPlayerConnected(room, saved.id, true));
          enterRoom(socket, code, saved);
          scheduleEmptyRoom(code);
          return acknowledge({ ok: true, code, playerId: saved.id });
        }
      }
      if (room.players.length >= MAX_PLAYERS) return acknowledge({ ok: false, error: "FULL" });
      const added = addPlayer(room, { playerId: makeId(), name, now: Date.now() });
      if (!added) return acknowledge({ ok: false, error: "FULL" });
      setRoom(code, added.room);
      enterRoom(socket, code, added.player);
      scheduleEmptyRoom(code);
      acknowledge({ ok: true, code, playerId: added.player.id });
    });

    socket.on("switch_team", (acknowledge = () => {}) => {
      const { roomCode, playerId } = socket.data;
      const room = rooms.get(roomCode);
      if (!room) return acknowledge({ ok: false, error: "NOT_IN_ROOM" });
      const next = switchTeam(room, playerId);
      if (next === room) return acknowledge({ ok: false, error: "TEAM_FULL" });
      setRoom(roomCode, next);
      sendState(roomCode);
      acknowledge({ ok: true });
    });

    socket.on("update_settings", (payload = {}, acknowledge = () => {}) => {
      const { roomCode, playerId } = socket.data;
      const room = rooms.get(roomCode);
      if (!room) return acknowledge({ ok: false, error: "NOT_IN_ROOM" });
      const next = updateSettings(room, playerId, payload);
      if (next === room) return acknowledge({ ok: false, error: "UNCHANGED" });
      setRoom(roomCode, next);
      sendState(roomCode);
      acknowledge({ ok: true });
    });

    socket.on("start_game", (acknowledge = () => {}) => {
      const { roomCode, playerId } = socket.data;
      const room = rooms.get(roomCode);
      if (!room || !canStart(room, playerId)) return acknowledge({ ok: false, error: "NOT_READY" });
      setRoom(roomCode, startGame(room, playerId, Date.now(), Math.random, WORDS, timing));
      sendState(roomCode);
      scheduleRoom(roomCode);
      scheduleBots(roomCode);
      acknowledge({ ok: true });
    });

    for (const [eventName, operation] of [["add_bot", "add"], ["remove_bot", "remove"]]) {
      socket.on(eventName, (acknowledge = () => {}) => {
        const { roomCode, playerId } = socket.data;
        const room = rooms.get(roomCode);
        if (!dev || !room || room.hostId !== playerId || !["lobby", "gameOver"].includes(room.phase)) {
          return acknowledge({ ok: false, error: "NOT_ALLOWED" });
        }
        const botId = makeId();
        const next = operation === "add" ? addBot(room, botId, Date.now()) : removeBot(room);
        if (!next || next === room) return acknowledge({ ok: false, error: operation === "add" ? "FULL" : "NO_BOTS" });
        setRoom(roomCode, next);
        sendState(roomCode);
        if (operation === "add") setAvatar(roomCode, botId, botFace(), null);
        for (const player of room.players) if (!next.players.includes(player)) avatars.delete(key(roomCode, player.id));
        acknowledge({ ok: true });
      });
    }

    socket.on("choose_word", (payload = {}, acknowledge = () => {}) => {
      acknowledge(handleChoice(socket.data.roomCode, socket.data.playerId, payload.level));
    });

    socket.on("guess", (payload = {}, acknowledge = () => {}) => {
      acknowledge(handleGuess(socket.data.roomCode, socket.data.playerId, payload.text));
    });

    socket.on("draw", (payload = {}) => handleDraw(socket.data.roomCode, socket.data.playerId, payload));

    socket.on("avatar_draw", (payload = {}) => handleAvatarDraw(socket.data.roomCode, socket.data.playerId, payload));

    socket.on("avatar_set", (payload = {}) => {
      const { roomCode, playerId } = socket.data;
      if (canEditAvatars(rooms.get(roomCode))) setAvatar(roomCode, playerId, payload.strokes, playerId);
    });

    socket.on("leave_room", (acknowledge = () => {}) => leaveSocket(socket, acknowledge));

    socket.on("disconnect", () => {
      if (shuttingDown) return;
      const { roomCode, playerId } = socket.data;
      const room = rooms.get(roomCode);
      const player = room?.players.find((entry) => entry.id === playerId);
      if (!room || !player || player.isBot) return;
      playerSockets.delete(key(roomCode, playerId));
      setRoom(roomCode, setPlayerConnected(room, playerId, false));
      sendState(roomCode);
      scheduleEmptyRoom(roomCode);
      clearPlayerTimer(roomCode, playerId);
      // Seats are held mid-match so a dropped phone can come back; in the lobby they free up after a minute.
      disconnectTimers.set(key(roomCode, playerId), setTimeout(() => {
        disconnectTimers.delete(key(roomCode, playerId));
        const current = rooms.get(roomCode);
        if (!current?.players.some((entry) => entry.id === playerId && !entry.connected)) return;
        if (!["lobby", "gameOver"].includes(current.phase)) return;
        avatars.delete(key(roomCode, playerId));
        setRoom(roomCode, removePlayer(current, playerId));
        sendState(roomCode);
        scheduleEmptyRoom(roomCode);
      }, 60_000));
    });
  });

  await new Promise((resolve, reject) => {
    httpServer.once("error", reject);
    httpServer.listen(port, "0.0.0.0", () => {
      httpServer.removeListener("error", reject);
      resolve();
    });
  });
  const address = httpServer.address();
  return {
    port: typeof address === "object" && address ? address.port : port,
    close: () => new Promise((resolve) => {
      shuttingDown = true;
      io.close(() => {
        for (const code of rooms.keys()) clearRoomTimers(code);
        for (const timer of emptyRoomTimers.values()) clearTimeout(timer);
        for (const timer of disconnectTimers.values()) clearTimeout(timer);
        resolve();
      });
    })
  };
}

// Render's free plan puts a service to sleep after 15 minutes without visitors, and the next player
// then waits about a minute for it to wake. On Render, visit our own public address every 10 minutes
// so it never falls asleep. RENDER_EXTERNAL_URL is set by Render; elsewhere this does nothing.
const KEEP_AWAKE_MS = 10 * 60 * 1000;

function keepAwake(url) {
  setInterval(() => {
    fetch(url + "/health").catch((error) => console.warn("Keep-awake ping failed: " + error.message));
  }, KEEP_AWAKE_MS).unref();
  console.log("Keeping awake by visiting " + url + " every 10 minutes");
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  startServer().then(({ port }) => {
    console.log("Split Keys server listening on port " + port);
    if (process.env.RENDER_EXTERNAL_URL) keepAwake(process.env.RENDER_EXTERNAL_URL);
  }).catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}
