import { CARDS } from "./cards.js";

const MAX_PLAYERS = 8;
const SESSION_ROUNDS = 5;
const WIN_SCORE = 8;
const CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

function json(data, status) {
  return new Response(JSON.stringify(data), {
    status: status || 200,
    headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" }
  });
}

function randomInt(max) {
  const bytes = new Uint32Array(1);
  crypto.getRandomValues(bytes);
  return bytes[0] % max;
}

function roomCode() {
  let code = "";
  for (let i = 0; i < 5; i += 1) code += CODE_ALPHABET[randomInt(CODE_ALPHABET.length)];
  return code;
}

function cleanName(value) {
  const name = String(value || "").replace(/[<>\r\n\t]/g, " ").replace(/\s+/g, " ").trim().slice(0, 18);
  return name || "Operator";
}

function shuffled(items) {
  const copy = items.slice();
  for (let i = copy.length - 1; i > 0; i -= 1) {
    const j = randomInt(i + 1);
    const value = copy[i];
    copy[i] = copy[j];
    copy[j] = value;
  }
  return copy;
}

function publicSnapshot(room) {
  if (!room) return null;
  const snapshot = {
    code: room.code,
    phase: room.phase,
    round: room.round,
    maxRounds: SESSION_ROUNDS,
    hostId: room.hostId,
    players: room.players.map((p) => ({ id: p.id, name: p.name, online: p.online, ready: p.ready, score: p.score })),
    deadline: room.deadline || 0,
    category: room.category || "",
    evidence: room.evidence || [],
    chat: room.chat || [],
    options: room.phase === "vote" || room.phase === "results" || room.phase === "duoGuess" || room.phase === "finished" ? (room.options || []) : [],
    result: room.result || null,
    duo: room.duo || false,
    teamScore: room.teamScore || 0
  };
  return snapshot;
}

function privateSnapshot(room, playerId) {
  if (!room || !room.privateByPlayer) return null;
  return room.privateByPlayer[playerId] || null;
}

function sendTo(socket, room, playerId, type) {
  try {
    socket.send(JSON.stringify({ type: type || "state", state: publicSnapshot(room), privateInfo: privateSnapshot(room, playerId) }));
  } catch (_) {
    // A dropped client is handled by the WebSocket close callback.
  }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/api/health") return json({ ok: true, service: "signal-noise" });

    if (url.pathname === "/api/rooms" && request.method === "POST") {
      let body;
      try {
        const raw = await request.text();
        if (raw.length > 4096) return json({ error: "Request is too large." }, 413);
        body = JSON.parse(raw);
      } catch (_) {
        return json({ error: "Send a valid JSON request." }, 400);
      }
      if (body.action === "create") {
        for (let attempt = 0; attempt < 8; attempt += 1) {
          const code = roomCode();
          const stub = env.ROOMS.get(env.ROOMS.idFromName(code));
          const response = await stub.fetch("https://room/create", {
            method: "POST",
            headers: { "content-type": "application/json" },
            body: JSON.stringify({ code, name: cleanName(body.name) })
          });
          if (response.status !== 409) return response;
        }
        return json({ error: "Could not reserve a room code. Try again." }, 503);
      }
      if (body.action === "join") {
        const code = String(body.code || "").toUpperCase().replace(/\s+/g, "");
        if (!/^[A-HJ-NP-Z2-9]{5}$/.test(code)) return json({ error: "That room code is not valid." }, 400);
        const stub = env.ROOMS.get(env.ROOMS.idFromName(code));
        return stub.fetch("https://room/join", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ code, name: cleanName(body.name) })
        });
      }
      return json({ error: "Choose create or join." }, 400);
    }

    const connectMatch = url.pathname.match(/^\/api\/rooms\/([A-HJ-NP-Z2-9]{5})\/connect$/);
    if (connectMatch && request.method === "GET") {
      if (request.headers.get("Upgrade") !== "websocket") return json({ error: "A WebSocket connection is required." }, 426);
      const code = connectMatch[1];
      const stub = env.ROOMS.get(env.ROOMS.idFromName(code));
      return stub.fetch(new Request("https://room/connect" + url.search, request));
    }

    if (url.pathname.startsWith("/api/")) return json({ error: "Not found." }, 404);
    return env.ASSETS.fetch(request);
  }
};

export class Room {
  constructor(ctx) {
    this.ctx = ctx;
    this.room = null;
    this.loaded = this.load();
  }

  async load() {
    this.room = await this.ctx.storage.get("room") || null;
  }

  async save() {
    if (this.room) await this.ctx.storage.put("room", this.room);
  }

  async fetch(request) {
    await this.loaded;
    const url = new URL(request.url);
    if (url.pathname === "/create" && request.method === "POST") return this.create(request);
    if (url.pathname === "/join" && request.method === "POST") return this.join(request);
    if (url.pathname === "/connect" && request.method === "GET") return this.connect(request, url);
    if (url.pathname === "/exists") return json({ exists: Boolean(this.room) });
    return json({ error: "Not found." }, 404);
  }

  async create(request) {
    if (this.room) return json({ error: "Code collision." }, 409);
    const body = await request.json();
    const player = this.makePlayer(body.name, true);
    this.room = {
      code: body.code,
      createdAt: Date.now(),
      updatedAt: Date.now(),
      phase: "lobby",
      hostId: player.id,
      players: [player],
      round: 0,
      deadline: 0,
      cardId: "",
      answer: "",
      decoy: "",
      options: [],
      category: "",
      privateByPlayer: {},
      submittedClues: [],
      evidence: [],
      votes: [],
      chat: [],
      result: null,
      duo: false,
      duoTransmitterId: "",
      duoClue: "",
      teamScore: 0
    };
    await this.save();
    return json({ code: this.room.code, playerId: player.id, token: player.token });
  }

  async join(request) {
    if (!this.room) return json({ error: "Room not found. Check the code and try again." }, 404);
    const body = await request.json();
    if (this.room.phase !== "lobby") return json({ error: "This game has already started." }, 409);
    if (this.room.players.length >= MAX_PLAYERS) return json({ error: "This room is full." }, 409);
    const base = cleanName(body.name);
    let name = base;
    let suffix = 2;
    while (this.room.players.some((p) => p.name.toLowerCase() === name.toLowerCase())) {
      const tail = " " + suffix;
      name = base.slice(0, 18 - tail.length) + tail;
      suffix += 1;
    }
    const player = this.makePlayer(name, false);
    this.room.players.push(player);
    this.room.updatedAt = Date.now();
    await this.save();
    this.broadcast();
    return json({ code: this.room.code, playerId: player.id, token: player.token });
  }

  makePlayer(name, host) {
    return {
      id: crypto.randomUUID(),
      token: crypto.randomUUID() + crypto.randomUUID(),
      name: cleanName(name),
      host: Boolean(host),
      online: false,
      ready: false,
      score: 0,
      lastChatAt: 0
    };
  }

  async connect(request, url) {
    if (!this.room) return json({ error: "Room not found." }, 404);
    const token = url.searchParams.get("token") || "";
    const player = this.room.players.find((p) => p.token === token);
    if (!player) return json({ error: "Reconnect token is not valid." }, 401);
    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    this.ctx.acceptWebSocket(server, [player.id]);
    server.serializeAttachment({ playerId: player.id, token: player.token });
    player.online = true;
    this.room.updatedAt = Date.now();
    await this.save();
    server.send(JSON.stringify({ type: "state", state: publicSnapshot(this.room), privateInfo: privateSnapshot(this.room, player.id) }));
    this.broadcast(server);
    return new Response(null, { status: 101, webSocket: client });
  }

  webSocketMessage(socket, message) {
    this.loaded.then(() => this.handleMessage(socket, message));
  }

  async handleMessage(socket, message) {
    if (!this.room) return;
    let packet;
    try {
      const text = typeof message === "string" ? message : new TextDecoder().decode(message);
      if (text.length > 4096) return;
      packet = JSON.parse(text);
    } catch (_) {
      return;
    }
    const attachment = socket.deserializeAttachment() || {};
    const player = this.room.players.find((p) => p.id === attachment.playerId && p.token === attachment.token);
    if (!player || !player.online) return;
    const now = Date.now();

    if (packet.type === "ready" && this.room.phase === "lobby") {
      player.ready = Boolean(packet.ready);
    } else if (packet.type === "start" && this.room.phase === "lobby") {
      if (player.id !== this.room.hostId) return this.notice(socket, player.id, "Only the host can start the game.");
      if (this.room.players.length < 2) return this.notice(socket, player.id, "Invite at least one more player first.");
      if (!this.room.players.every((p) => p.ready && p.online)) return this.notice(socket, player.id, "Everyone needs to be online and ready.");
      this.room.players.forEach((p) => { p.score = 0; });
      this.room.teamScore = 0;
      this.room.round = 0;
      this.beginRound();
    } else if (packet.type === "transmit" && this.room.phase === "transmit") {
      const secret = this.room.privateByPlayer[player.id];
      const index = Number(packet.clueIndex);
      if (!secret || !Array.isArray(secret.clueOptions) || !Number.isInteger(index) || index < 0 || index >= secret.clueOptions.length) return;
      if (this.room.submittedClues.some((item) => item.playerId === player.id)) return;
      this.room.submittedClues.push({ playerId: player.id, text: secret.clueOptions[index] });
      if (this.room.players.filter((p) => p.online).every((p) => this.room.submittedClues.some((item) => item.playerId === p.id))) this.beginDiscussion();
    } else if (packet.type === "chat" && this.room.phase === "discussion") {
      const text = String(packet.text || "").replace(/[<>\r\n\t]/g, " ").replace(/\s+/g, " ").trim().slice(0, 220);
      if (!text || now - player.lastChatAt < 800) return;
      player.lastChatAt = now;
      this.room.chat.push({ playerId: player.id, name: player.name, text, at: now });
      if (this.room.chat.length > 30) this.room.chat.shift();
    } else if (packet.type === "vote" && this.room.phase === "vote") {
      if (this.room.votes.some((v) => v.playerId === player.id)) return;
      const answer = String(packet.answer || "");
      const suspect = String(packet.suspect || "");
      if (!this.room.options.includes(answer)) return;
      if (!this.room.players.some((p) => p.id === suspect)) return;
      this.room.votes.push({ playerId: player.id, answer, suspect });
      if (this.room.players.filter((p) => p.online).every((p) => this.room.votes.some((v) => v.playerId === p.id))) this.resolveStandard();
    } else if (packet.type === "duoTransmit" && this.room.phase === "duoTransmit") {
      if (player.id !== this.room.duoTransmitterId) return;
      const secret = this.room.privateByPlayer[player.id];
      const index = Number(packet.clueIndex);
      if (!secret || !Array.isArray(secret.clueOptions) || !Number.isInteger(index) || index < 0 || index >= secret.clueOptions.length) return;
      this.room.duoClue = secret.clueOptions[index];
      this.room.phase = "duoGuess";
      this.room.deadline = Date.now() + 45000;
      this.setDeadlineAlarm();
    } else if (packet.type === "duoGuess" && this.room.phase === "duoGuess") {
      if (player.id === this.room.duoTransmitterId) return;
      const answer = String(packet.answer || "");
      if (!this.room.options.includes(answer)) return;
      this.resolveDuo(player.id, answer);
    } else if (packet.type === "next" && this.room.phase === "results") {
      if (player.id !== this.room.hostId) return;
      if (this.room.round >= SESSION_ROUNDS || this.room.players.some((p) => p.score >= WIN_SCORE) || (this.room.duo && this.room.teamScore >= 6)) {
        this.room.phase = "finished";
        this.room.deadline = 0;
      } else {
        this.beginRound();
      }
    } else if (packet.type === "restart" && this.room.phase === "finished") {
      if (player.id !== this.room.hostId) return;
      this.room.players.forEach((p) => { p.score = 0; p.ready = p.online; });
      this.room.round = 0;
      this.room.teamScore = 0;
      this.room.result = null;
      this.room.phase = "lobby";
      this.room.deadline = 0;
    }

    this.room.updatedAt = now;
    await this.save();
    this.broadcast();
  }

  notice(socket, playerId, message) {
    sendTo(socket, this.room, playerId, "notice");
    try { socket.send(JSON.stringify({ type: "notice", message })); } catch (_) {}
  }

  beginRound() {
    const room = this.room;
    room.round += 1;
    room.result = null;
    room.chat = [];
    room.votes = [];
    room.submittedClues = [];
    room.evidence = [];
    room.duoClue = "";
    const card = CARDS[randomInt(CARDS.length)];
    room.cardId = card.id;
    room.category = card.category;
    room.answer = card.answer;
    room.decoy = card.decoy;
    room.options = shuffled(card.options);
    room.duo = room.players.length === 2;
    room.privateByPlayer = {};

    if (room.duo) {
      room.duoTransmitterId = room.players[(room.round - 1) % room.players.length].id;
      room.players.forEach((p) => {
        const transmitter = p.id === room.duoTransmitterId;
        room.privateByPlayer[p.id] = transmitter
          ? { role: "carrier", answer: card.answer, category: card.category, clueOptions: shuffled(card.keeperClues) }
          : { role: "decoder", category: card.category };
      });
      room.phase = "duoTransmit";
      room.deadline = Date.now() + 45000;
    } else {
      const noiseIndex = randomInt(room.players.length);
      room.players.forEach((p, index) => {
        if (index === noiseIndex) {
          room.privateByPlayer[p.id] = { role: "noise", category: card.category, clueOptions: shuffled(card.noiseClues) };
        } else {
          room.privateByPlayer[p.id] = { role: "keeper", category: card.category, clueOptions: shuffled(card.keeperClues) };
        }
      });
      room.phase = "transmit";
      room.deadline = Date.now() + 30000;
    }
    this.setDeadlineAlarm();
  }

  beginDiscussion() {
    const room = this.room;
    room.evidence = shuffled(this.room.submittedClues.map((item) => item.text));
    room.phase = "discussion";
    room.deadline = Date.now() + 45000;
    this.setDeadlineAlarm();
  }

  beginVote() {
    this.room.phase = "vote";
    this.room.deadline = Date.now() + 30000;
    this.setDeadlineAlarm();
  }

  resolveStandard() {
    const room = this.room;
    const votes = room.votes;
    const n = votes.length;
    const answerCount = votes.filter((v) => v.answer === room.answer).length;
    const answerCorrect = n > 0 && answerCount > n / 2;
    const tally = {};
    votes.forEach((v) => { tally[v.suspect] = (tally[v.suspect] || 0) + 1; });
    const max = Math.max(0, ...Object.values(tally));
    const leaders = Object.keys(tally).filter((id) => tally[id] === max);
    const noise = Object.keys(room.privateByPlayer).find((id) => room.privateByPlayer[id].role === "noise");
    const noiseCaught = leaders.length === 1 && leaders[0] === noise;
    const keepersWin = answerCorrect && noiseCaught;
    room.players.forEach((p) => {
      if (room.privateByPlayer[p.id].role === "noise") p.score += keepersWin ? 0 : 2;
      else p.score += (keepersWin ? 2 : 0) + (votes.some((v) => v.playerId === p.id && v.answer === room.answer) ? 1 : 0);
    });
    room.result = {
      kind: "standard",
      winner: keepersWin ? "keepers" : "noise",
      answer: room.answer,
      decoy: room.decoy,
      answerCorrect,
      noiseCaught,
      noisePlayerId: noise,
      votes: votes.map((v) => ({ playerId: v.playerId, answer: v.answer, suspect: v.suspect })),
      message: keepersWin ? "The signal came through, and the Noise Agent was exposed." : answerCorrect ? "The signal was right, but the Noise Agent slipped away." : noiseCaught ? "You found the Noise Agent, but the signal was lost." : "The Noise Agent changed the signal and escaped detection."
    };
    room.phase = "results";
    room.deadline = 0;
  }

  resolveDuo(decoderId, answer) {
    const room = this.room;
    const correct = answer === room.answer;
    if (correct) {
      room.teamScore += 2;
      room.players.forEach((p) => { p.score += 2; });
    }
    room.result = {
      kind: "duo",
      winner: correct ? "duo" : "signal",
      answer: room.answer,
      guess: answer,
      correct,
      transmitterId: room.duoTransmitterId,
      decoderId,
      clue: room.duoClue,
      message: correct ? "A clean transmission. Two points for the team." : "The signal scattered. Reset, rotate, and try again."
    };
    room.phase = "results";
    room.deadline = 0;
  }

  setDeadlineAlarm() {
    if (this.room.deadline) this.ctx.storage.setAlarm(this.room.deadline);
  }

  async alarm() {
    await this.loaded;
    if (!this.room) return;
    const room = this.room;
    if (room.phase === "transmit") this.beginDiscussion();
    else if (room.phase === "discussion") this.beginVote();
    else if (room.phase === "vote") this.resolveStandard();
    else if (room.phase === "duoTransmit") {
      room.duoClue = (room.privateByPlayer[room.duoTransmitterId].clueOptions || [""])[0];
      room.phase = "duoGuess";
      room.deadline = Date.now() + 30000;
      this.setDeadlineAlarm();
    } else if (room.phase === "duoGuess") {
      const decoder = room.players.find((p) => p.id !== room.duoTransmitterId);
      this.resolveDuo(decoder ? decoder.id : "", "");
    }
    room.updatedAt = Date.now();
    await this.save();
    this.broadcast();
  }

  webSocketClose(socket) {
    this.loaded.then(async () => {
      const attachment = socket.deserializeAttachment() || {};
      const player = this.room && this.room.players.find((p) => p.id === attachment.playerId);
      if (!player) return;
      player.online = false;
      this.room.updatedAt = Date.now();
      await this.save();
      this.broadcast();
    });
  }

  webSocketError(socket) {
    this.webSocketClose(socket);
  }

  broadcast(except) {
    if (!this.room) return;
    for (const socket of this.ctx.getWebSockets()) {
      if (except && socket === except) continue;
      const attachment = socket.deserializeAttachment() || {};
      sendTo(socket, this.room, attachment.playerId);
    }
  }
}
