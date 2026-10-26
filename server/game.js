import { LEVEL_RANGES, PHRASES } from "./phrases.js";

export const ROUND_SECONDS = 20;
export const COUNTDOWN_SECONDS = 3;
export const ROUND_END_SECONDS = 3.5;
export const START_LIVES = 3;
export const MAX_PLAYERS = 8;
export const MIN_PLAYERS = 2;
export const NAME_MAX = 12;
export const BOT_MIN_MS = 400;
export const BOT_MAX_MS = 1200;
export const BOT_MISTAKE_RATE = 0.05;
export const CODE_ALPHABET = "BCDFGHJKLMNPQRSTVWXZ";
export const LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
export const PLAYER_COLORS = [
  "#4DABF7", "#FF922B", "#A9E34B", "#F783AC",
  "#FFD43B", "#9775FA", "#38D9A9", "#E3DEF5"
];

export function validRoomCode(code) {
  return typeof code === "string" && new RegExp("^["
    + CODE_ALPHABET + "]{4}$").test(code.toUpperCase());
}

export function normalizeName(value) {
  if (typeof value !== "string") return null;
  const name = value.replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim();
  const length = Array.from(name).length;
  return length >= 1 && length <= NAME_MAX ? name : null;
}

export function uniqueName(players, requestedName) {
  const base = normalizeName(requestedName);
  if (!base) return null;
  const taken = new Set(players.map((player) => player.name.toLocaleLowerCase()));
  if (!taken.has(base.toLocaleLowerCase())) return base;
  for (let suffix = 2; suffix < 100; suffix += 1) {
    const ending = " " + suffix;
    const shortened = Array.from(base).slice(0, NAME_MAX - ending.length).join("").trimEnd();
    const candidate = shortened + ending;
    if (!taken.has(candidate.toLocaleLowerCase())) return candidate;
  }
  return null;
}

function makePlayer({ id, name, joinedAt, joinOrder, colorIndex, connected = true, isBot = false }) {
  return {
    id,
    name,
    color: PLAYER_COLORS[colorIndex % PLAYER_COLORS.length],
    connected,
    isBot,
    joinedAt,
    joinOrder,
    stats: { correct: 0, mistakes: 0, finals: 0 }
  };
}

export function createRoom({ code, playerId, name, now, dev = false }) {
  const player = makePlayer({ id: playerId, name, joinedAt: now, joinOrder: 0, colorIndex: 0 });
  return {
    code,
    createdAt: now,
    hostId: playerId,
    dev: Boolean(dev),
    phase: "lobby",
    level: 1,
    lives: START_LIVES,
    bestLevel: 0,
    nextJoinOrder: 1,
    nextBotNumber: 1,
    players: [player],
    roundId: 0,
    round: null,
    usedPhrases: {}
  };
}

export function addPlayer(room, { playerId, name, now }) {
  if (!room || room.players.length >= MAX_PLAYERS) return null;
  const displayName = uniqueName(room.players, name);
  if (!displayName) return null;
  const usedColors = new Set(room.players.map((player) => player.color));
  const nextColor = PLAYER_COLORS.findIndex((color) => !usedColors.has(color));
  const player = makePlayer({
    id: playerId,
    name: displayName,
    joinedAt: now,
    joinOrder: room.nextJoinOrder,
    colorIndex: nextColor < 0 ? room.players.length : nextColor
  });
  return {
    room: { ...room, nextJoinOrder: room.nextJoinOrder + 1, players: [...room.players, player] },
    player
  };
}

export function addBot(room, playerId, now) {
  if (!room?.dev || !["lobby", "gameOver"].includes(room.phase) ||
      room.players.length >= MAX_PLAYERS || typeof playerId !== "string" || !playerId) return null;
  const baseName = "Bot " + room.nextBotNumber;
  const name = uniqueName(room.players, baseName);
  if (!name) return null;
  const usedColors = new Set(room.players.map((player) => player.color));
  const nextColor = PLAYER_COLORS.findIndex((color) => !usedColors.has(color));
  const player = makePlayer({
    id: playerId,
    name,
    joinedAt: now,
    joinOrder: room.nextJoinOrder,
    colorIndex: nextColor < 0 ? room.players.length : nextColor,
    isBot: true
  });
  return {
    ...room,
    nextJoinOrder: room.nextJoinOrder + 1,
    nextBotNumber: room.nextBotNumber + 1,
    players: [...room.players, player]
  };
}

export function removeBot(room) {
  if (!room?.dev || !["lobby", "gameOver"].includes(room.phase)) return room;
  const bot = [...room.players].filter((player) => player.isBot)
    .sort((a, b) => b.joinOrder - a.joinOrder)[0];
  return bot ? removePlayer(room, bot.id) : room;
}

export function setPlayerConnected(room, playerId, connected) {
  if (!room) return null;
  const target = room.players.find((player) => player.id === playerId);
  if (!target || target.connected === connected) return room;
  const players = room.players.map((player) => player.id === playerId ? { ...player, connected } : player);
  let hostId = room.hostId;
  if (!connected && hostId === playerId) {
    const nextHost = players.filter((player) => player.connected && !player.isBot).sort((a, b) => a.joinOrder - b.joinOrder)[0];
    hostId = nextHost?.id || null;
  } else if (connected && !hostId) {
    hostId = playerId;
  }
  return { ...room, hostId, players };
}

export function removePlayer(room, playerId) {
  if (!room) return null;
  const players = room.players.filter((player) => player.id !== playerId);
  if (players.length === room.players.length) return room;
  let hostId = room.hostId;
  if (hostId === playerId) {
    const nextHost = players.filter((player) => player.connected && !player.isBot).sort((a, b) => a.joinOrder - b.joinOrder)[0];
    hostId = nextHost?.id || null;
  }
  return { ...room, hostId, players };
}

export function canStart(room, playerId) {
  const minimumPlayers = room?.dev ? 1 : MIN_PLAYERS;
  return Boolean(
    room && ["lobby", "gameOver"].includes(room.phase) && room.hostId === playerId &&
    room.players.filter((player) => player.connected).length >= minimumPlayers
  );
}

function randomIndex(max, rng) {
  return Math.min(max - 1, Math.floor(rng() * max));
}

function shuffled(items, rng) {
  const result = [...items];
  for (let i = result.length - 1; i > 0; i -= 1) {
    const j = randomIndex(i + 1, rng);
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
}

export function dealLetters(phrase, playerIds, rng = Math.random) {
  const players = [...playerIds];
  if (players.length === 0) return {};
  const needed = shuffled([...new Set(phrase.replaceAll(" ", ""))], rng);
  const remaining = [...LETTERS].filter((letter) => !needed.includes(letter));
  const order = [...needed, ...shuffled(remaining, rng)];
  const shuffledPlayers = shuffled(players, rng);
  const owners = {};
  order.forEach((letter, index) => { owners[letter] = shuffledPlayers[index % shuffledPlayers.length]; });
  return owners;
}

export function selectPhrase(level, usedPhrases = {}, rng = Math.random, phrases = PHRASES) {
  const phraseLevel = Math.min(6, Math.max(1, level));
  const list = phrases[phraseLevel] || [];
  if (list.length === 0) throw new Error("No phrases configured for level " + phraseLevel);
  const previouslyUsed = usedPhrases[phraseLevel] || [];
  let unused = list.filter((phrase) => !previouslyUsed.includes(phrase));
  let used = previouslyUsed;
  if (unused.length === 0) {
    unused = [...list];
    used = [];
  }
  const phrase = unused[randomIndex(unused.length, rng)];
  return {
    phrase,
    usedPhrases: { ...usedPhrases, [phraseLevel]: [...used, phrase] }
  };
}

export function startRound(room, now, rng = Math.random, phrases = PHRASES, timing = {}) {
  const selected = selectPhrase(room.level, room.usedPhrases, rng, phrases);
  const playerIds = room.players.filter((player) => player.connected).map((player) => player.id);
  const owners = dealLetters(selected.phrase, playerIds, rng);
  const typedBy = Array(selected.phrase.length).fill(null);
  return {
    ...room,
    phase: "countdown",
    roundId: room.roundId + 1,
    usedPhrases: selected.usedPhrases,
    round: {
      roundId: room.roundId + 1,
      phrase: selected.phrase,
      cursor: 0,
      typedBy,
      owners,
      phaseEndsAt: now + (timing.countdown ?? COUNTDOWN_SECONDS) * 1000,
      result: null,
      timeLeftMs: null,
      lastPlayerId: null
    }
  };
}

export function startGame(room, playerId, now, rng = Math.random, phrases = PHRASES, timing = {}) {
  if (!canStart(room, playerId)) return null;
  const resetPlayers = room.players.map((player) => ({
    ...player,
    stats: { correct: 0, mistakes: 0, finals: 0 }
  }));
  return startRound({
    ...room,
    players: resetPlayers,
    phase: "lobby",
    level: 1,
    lives: START_LIVES,
    round: null
  }, now, rng, phrases, timing);
}

function finishRound(room, result, now, lastPlayerId = null, timing = {}) {
  const oldRound = room.round;
  const timeLeftMs = Math.max(0, oldRound.phaseEndsAt - now);
  let level = room.level;
  let bestLevel = room.bestLevel;
  let lives = room.lives;
  const players = room.players.map((player) => {
    if (result === "cleared" && player.id === lastPlayerId) {
      return { ...player, stats: { ...player.stats, finals: player.stats.finals + 1 } };
    }
    return player;
  });
  if (result === "cleared") {
    level += 1;
    bestLevel = Math.max(bestLevel, level - 1);
  } else {
    lives = Math.max(0, lives - 1);
  }
  return {
    ...room,
    players,
    level,
    lives,
    bestLevel,
    phase: "roundEnd",
    round: {
      ...oldRound,
      result,
      timeLeftMs,
      lastPlayerId,
      phaseEndsAt: now + (timing.roundEnd ?? ROUND_END_SECONDS) * 1000
    }
  };
}

export function advanceRoom(room, now, rng = Math.random, phrases = PHRASES, timing = {}) {
  if (!room?.round || now < room.round.phaseEndsAt) return room;
  if (room.phase === "countdown") {
    return {
      ...room,
      phase: "playing",
      round: { ...room.round, phaseEndsAt: room.round.phaseEndsAt + (timing.round ?? ROUND_SECONDS) * 1000 }
    };
  }
  if (room.phase === "playing") return finishRound(room, "timeout", room.round.phaseEndsAt, null, timing);
  if (room.phase === "roundEnd") {
    if (room.lives <= 0) return { ...room, phase: "gameOver" };
    return startRound(room, room.round.phaseEndsAt, rng, phrases, timing);
  }
  return room;
}

function wordStart(phrase, cursor) {
  let start = cursor;
  while (start > 0 && phrase[start - 1] !== " ") start -= 1;
  return start;
}

export const RESHUFFLE_LEVEL = 5;

export function twistsForLevel(level) {
  return { hint: level <= 2, shuffleKeys: level >= 4, midRoundDeal: level >= RESHUFFLE_LEVEL };
}

function lettersTyped(typedBy) {
  return typedBy.filter(Boolean).length;
}

export function applyPress(room, playerId, { letter, atIndex }, now, rng = Math.random) {
  const round = room?.round;
  const normalizedLetter = typeof letter === "string" ? letter.toUpperCase() : "";
  if (room?.phase !== "playing" || !round) return { room, effect: null };
  if (!/^[A-Z]$/.test(normalizedLetter) || round.owners[normalizedLetter] !== playerId) {
    return { room, effect: null };
  }
  if (!Number.isInteger(atIndex) || atIndex !== round.cursor) return { room, effect: null };
  const expected = round.phrase[round.cursor];
  if (expected !== normalizedLetter) {
    const start = wordStart(round.phrase, round.cursor);
    const typedBy = [...round.typedBy];
    for (let i = start; i < round.cursor; i += 1) {
      if (round.phrase[i] !== " ") typedBy[i] = null;
    }
    const players = room.players.map((player) => player.id === playerId
      ? { ...player, stats: { ...player.stats, mistakes: player.stats.mistakes + 1 } }
      : player);
    return {
      room: {
        ...room,
        players,
        round: { ...round, cursor: start, typedBy, lastPlayerId: playerId }
      },
      effect: { kind: "mistake", playerId, letter: normalizedLetter }
    };
  }
  const typedBy = [...round.typedBy];
  typedBy[round.cursor] = playerId;
  let cursor = round.cursor + 1;
  while (cursor < round.phrase.length && round.phrase[cursor] === " ") cursor += 1;
  const players = room.players.map((player) => player.id === playerId
    ? { ...player, stats: { ...player.stats, correct: player.stats.correct + 1 } }
    : player);
  if (cursor >= round.phrase.length) {
    const finished = finishRound({
      ...room,
      players,
      round: { ...round, cursor, typedBy }
    }, "cleared", now, playerId);
    return { room: finished, effect: { kind: "cleared", playerId, letter: normalizedLetter } };
  }
  let owners = round.owners;
  let reshuffled = Boolean(round.reshuffled);
  const totalLetters = round.phrase.replaceAll(" ", "").length;
  if (room.level >= RESHUFFLE_LEVEL && !reshuffled && lettersTyped(typedBy) * 2 >= totalLetters) {
    const dealTo = room.players.filter((player) => player.connected).map((player) => player.id);
    owners = dealLetters(round.phrase.slice(cursor), dealTo, rng);
    reshuffled = true;
  }
  return {
    room: {
      ...room,
      players,
      round: { ...round, cursor, typedBy, owners, reshuffled, lastPlayerId: playerId }
    },
    effect: { kind: "correct", playerId, letter: normalizedLetter, reshuffle: reshuffled && !round.reshuffled }
  };
}

export function redistributeLetters(room, playerId, rng = Math.random) {
  if (!room?.round) return room;
  const remaining = room.players.filter((player) => player.connected && player.id !== playerId).map((player) => player.id);
  if (remaining.length === 0) return room;
  const lost = Object.keys(room.round.owners).filter((letter) => room.round.owners[letter] === playerId);
  const recipients = shuffled(remaining, rng);
  const owners = { ...room.round.owners };
  lost.forEach((letter, index) => { owners[letter] = recipients[index % recipients.length]; });
  return { ...room, round: { ...room.round, owners } };
}

export function disconnectPlayer(room, playerId, rng = Math.random) {
  if (!room) return null;
  const disconnected = setPlayerConnected(room, playerId, false);
  return redistributeLetters(disconnected, playerId, rng);
}

export function chooseBotPress(room, botId, rng = Math.random) {
  if (room?.phase !== "playing" || !room.round) return null;
  const bot = room.players.find((player) => player.id === botId && player.isBot && player.connected);
  const { phrase, cursor, owners } = room.round;
  if (!bot || cursor < 0 || cursor >= phrase.length) return null;
  const expected = phrase[cursor];
  if (expected === " " || owners[expected] !== botId) return null;
  if (rng() < BOT_MISTAKE_RATE) {
    const incorrect = Object.keys(owners).filter((letter) => owners[letter] === botId && letter !== expected);
    if (incorrect.length) return { letter: incorrect[randomIndex(incorrect.length, rng)], atIndex: cursor };
  }
  return { letter: expected, atIndex: cursor };
}

export function publicSnapshot(room, serverNow) {
  if (!room) return null;
  const roundLevel = room.round?.result === "cleared" ? room.level - 1 : room.level;
  const round = room.round && {
    ...room.round,
    reshuffled: Boolean(room.round.reshuffled),
    twists: twistsForLevel(roundLevel),
    ownerLetters: Object.entries(room.round.owners).map(([letter, playerId]) => ({ letter, playerId }))
  };
  return {
    code: room.code,
    phase: room.phase,
    serverNow,
    hostId: room.hostId,
    dev: Boolean(room.dev),
    level: room.level,
    lives: room.lives,
    bestLevel: room.bestLevel,
    players: room.players.map(({ id, name, color, connected, isBot, stats }) => ({
      id, name, color, connected, isBot: Boolean(isBot), stats: { ...stats }
    })),
    round
  };
}

