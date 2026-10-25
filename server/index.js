import { createServer } from "node:http";
import { randomBytes } from "node:crypto";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";
import express from "express";
import { Server } from "socket.io";
import { PHRASES } from "./phrases.js";
import {
  BOT_MAX_MS,
  BOT_MIN_MS,
  CODE_ALPHABET,
  MAX_PLAYERS,
  addBot,
  addPlayer,
  advanceRoom,
  applyPress,
  canStart,
  chooseBotPress,
  createRoom,
  disconnectPlayer,
  normalizeName,
  publicSnapshot,
  removeBot,
  removePlayer,
  setPlayerConnected,
  startGame,
  validRoomCode
} from "./game.js";

const buildPath = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../Builds/WebGL");

function makeId() {
  return randomBytes(6).toString("base64url");
}

function makeRoomCode(rooms) {
  for (let attempt = 0; attempt < 32; attempt += 1) {
    let code = "";
    for (let index = 0; index < 4; index += 1) {
      code += CODE_ALPHABET[randomBytes(1)[0] % CODE_ALPHABET.length];
    }
    if (!rooms.has(code)) return code;
  }
  return null;
}

function getPlayer(room, playerId) {
  return room?.players.find((player) => player.id === playerId) || null;
}

export async function startServer({
  port = Number(process.env.PORT) || 3000,
  dev = process.env.DEV === "1",
  timing = {}
} = {}) {
  const app = express();
  const httpServer = createServer(app);
  const io = new Server(httpServer);
  const rooms = new Map();
  const phaseTimers = new Map();
  const botTimers = new Map();
  const disconnectTimers = new Map();
  const emptyRoomTimers = new Map();
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

  const playerTimerKey = (code, playerId) => code + ":" + playerId;
  const clearPlayerTimer = (code, playerId) => {
    const key = playerTimerKey(code, playerId);
    clearTimeout(disconnectTimers.get(key));
    disconnectTimers.delete(key);
  };
  const clearBotTimers = (code) => {
    const timers = botTimers.get(code);
    if (timers) for (const timer of timers.values()) clearTimeout(timer);
    botTimers.delete(code);
  };
  const clearRoomTimers = (code) => {
    clearTimeout(phaseTimers.get(code));
    phaseTimers.delete(code);
    clearTimeout(emptyRoomTimers.get(code));
    emptyRoomTimers.delete(code);
    clearBotTimers(code);
    for (const [key, timer] of disconnectTimers) {
      if (key.startsWith(code + ":")) {
        clearTimeout(timer);
        disconnectTimers.delete(key);
      }
    }
  };
  const sendState = (code) => {
    const room = rooms.get(code);
    if (room) io.to(code).emit("state", publicSnapshot(room, Date.now()));
  };
  const hasConnectedHuman = (room) => room.players.some((player) => player.connected && !player.isBot);

  function scheduleEmptyRoom(code) {
    const room = rooms.get(code);
    if (!room || hasConnectedHuman(room)) {
      clearTimeout(emptyRoomTimers.get(code));
      emptyRoomTimers.delete(code);
      return;
    }
    if (emptyRoomTimers.has(code)) return;
    const timeout = setTimeout(() => {
      emptyRoomTimers.delete(code);
      if (!hasConnectedHuman(rooms.get(code) || { players: [] })) {
        rooms.delete(code);
        clearRoomTimers(code);
      }
    }, 120_000);
    emptyRoomTimers.set(code, timeout);
  }

  function scheduleRoom(code) {
    clearTimeout(phaseTimers.get(code));
    phaseTimers.delete(code);
    const room = rooms.get(code);
    if (!room?.round || !["countdown", "playing", "roundEnd"].includes(room.phase)) return;
    const delay = Math.max(0, room.round.phaseEndsAt - Date.now());
    phaseTimers.set(code, setTimeout(() => {
      phaseTimers.delete(code);
      const current = rooms.get(code);
      if (!current) return;
      const next = advanceRoom(current, Date.now(), Math.random, PHRASES, timing);
      if (next === current) return scheduleRoom(code);
      rooms.set(code, next);
      sendState(code);
      scheduleRoom(code);
      scheduleBots(code);
      scheduleEmptyRoom(code);
    }, delay));
  }

  function scheduleBots(code) {
    const room = rooms.get(code);
    if (!dev || room?.phase !== "playing") {
      clearBotTimers(code);
      return;
    }
    let pending = botTimers.get(code);
    if (!pending) {
      pending = new Map();
      botTimers.set(code, pending);
    }
    for (const player of room.players) {
      if (!player.isBot || pending.has(player.id) || !chooseBotPress(room, player.id)) continue;
      const delay = BOT_MIN_MS + Math.random() * (BOT_MAX_MS - BOT_MIN_MS);
      const timer = setTimeout(() => {
        pending.delete(player.id);
        const current = rooms.get(code);
        const press = chooseBotPress(current, player.id);
        if (press) {
          const result = applyPress(current, player.id, press, Date.now());
          if (result.room !== current) {
            rooms.set(code, result.room);
            if (result.effect) io.to(code).emit("fx", result.effect);
            sendState(code);
            scheduleRoom(code);
          }
        }
        scheduleBots(code);
      }, delay);
      pending.set(player.id, timer);
    }
    if (pending.size === 0) botTimers.delete(code);
  }

  function enterRoom(socket, code, player) {
    socket.data.roomCode = code;
    socket.data.playerId = player.id;
    socket.join(code);
    clearTimeout(emptyRoomTimers.get(code));
    emptyRoomTimers.delete(code);
    sendState(code);
  }

  function leaveSocket(socket, acknowledge) {
    const { roomCode, playerId } = socket.data;
    const room = rooms.get(roomCode);
    if (!room || !playerId) {
      if (acknowledge) acknowledge({ ok: true });
      return;
    }
    clearPlayerTimer(roomCode, playerId);
    let nextRoom = removePlayer(room, playerId);
    if (nextRoom.round) {
      const lost = Object.keys(nextRoom.round.owners).filter((letter) => nextRoom.round.owners[letter] === playerId);
      const recipients = nextRoom.players.filter((player) => player.connected).map((player) => player.id);
      const owners = { ...nextRoom.round.owners };
      lost.forEach((letter, index) => {
        if (recipients.length) owners[letter] = recipients[index % recipients.length];
        else delete owners[letter];
      });
      nextRoom = { ...nextRoom, round: { ...nextRoom.round, owners } };
    }
    socket.leave(roomCode);
    socket.data.roomCode = null;
    socket.data.playerId = null;
    rooms.set(roomCode, nextRoom);
    sendState(roomCode);
    scheduleRoom(roomCode);
    scheduleBots(roomCode);
    scheduleEmptyRoom(roomCode);
    if (acknowledge) acknowledge({ ok: true });
  }

  function handlePress(code, playerId, payload) {
    const room = rooms.get(code);
    if (!room) return { ok: false, error: "NOT_IN_ROOM" };
    const result = applyPress(room, playerId, payload, Date.now());
    if (result.room === room) return { ok: false, error: "IGNORED" };
    rooms.set(code, result.room);
    if (result.effect) io.to(code).emit("fx", result.effect);
    sendState(code);
    scheduleRoom(code);
    scheduleBots(code);
    return { ok: true };
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
      let room = rooms.get(code);
      if (payload.playerId) {
        const savedPlayer = room.players.find((player) => player.id === payload.playerId && !player.isBot);
        if (savedPlayer) {
          if (savedPlayer.connected) return acknowledge({ ok: false, error: "IN_USE" });
          clearPlayerTimer(code, savedPlayer.id);
          room = setPlayerConnected(room, savedPlayer.id, true);
          rooms.set(code, room);
          enterRoom(socket, code, savedPlayer);
          scheduleEmptyRoom(code);
          scheduleRoom(code);
          scheduleBots(code);
          return acknowledge({ ok: true, code, playerId: savedPlayer.id });
        }
      }
      if (room.players.length >= MAX_PLAYERS) return acknowledge({ ok: false, error: "FULL" });
      const added = addPlayer(room, { playerId: makeId(), name, now: Date.now() });
      if (!added) return acknowledge({ ok: false, error: "FULL" });
      rooms.set(code, added.room);
      enterRoom(socket, code, added.player);
      scheduleEmptyRoom(code);
      scheduleRoom(code);
      scheduleBots(code);
      acknowledge({ ok: true, code, playerId: added.player.id });
    });

    socket.on("start_game", (acknowledge = () => {}) => {
      const { roomCode, playerId } = socket.data;
      const room = rooms.get(roomCode);
      if (!room || !getPlayer(room, playerId) || !canStart(room, playerId)) {
        return acknowledge({ ok: false, error: "NOT_READY" });
      }
      const started = startGame(room, playerId, Date.now(), Math.random, PHRASES, timing);
      rooms.set(roomCode, started);
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
        const nextRoom = operation === "add"
          ? addBot(room, makeId(), Date.now())
          : removeBot(room);
        if (!nextRoom || nextRoom === room) {
          return acknowledge({ ok: false, error: operation === "add" ? "FULL" : "NO_BOTS" });
        }
        rooms.set(roomCode, nextRoom);
        sendState(roomCode);
        acknowledge({ ok: true });
      });
    }

    socket.on("press", (payload = {}, acknowledge = () => {}) => {
      acknowledge(handlePress(socket.data.roomCode, socket.data.playerId, payload));
    });

    socket.on("leave_room", (acknowledge = () => {}) => leaveSocket(socket, acknowledge));
    socket.on("disconnect", () => {
      if (shuttingDown) return;
      const { roomCode, playerId } = socket.data;
      const room = rooms.get(roomCode);
      const player = room?.players.find((entry) => entry.id === playerId);
      if (!room || !player || player.isBot) return;
      const updated = disconnectPlayer(room, playerId, Math.random);
      rooms.set(roomCode, updated);
      sendState(roomCode);
      scheduleRoom(roomCode);
      scheduleBots(roomCode);
      scheduleEmptyRoom(roomCode);
      const key = playerTimerKey(roomCode, playerId);
      clearPlayerTimer(roomCode, playerId);
      disconnectTimers.set(key, setTimeout(() => {
        disconnectTimers.delete(key);
        const current = rooms.get(roomCode);
        if (!current?.players.some((entry) => entry.id === playerId && !entry.connected)) return;
        const nextRoom = removePlayer(current, playerId);
        rooms.set(roomCode, nextRoom);
        sendState(roomCode);
        scheduleRoom(roomCode);
        scheduleBots(roomCode);
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

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  startServer().then(({ port }) => {
    console.log("Split Keys server listening on port " + port);
  }).catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}
