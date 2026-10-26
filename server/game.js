import { GIVEN_WORDS, SENTENCES } from "./sentences.js";

export const COUNTDOWN_SECONDS = 4;
export const DRAW_SECONDS = 90;
export const ROUND_END_SECONDS = 8;
export const TOTAL_ROUNDS = 4;
export const HINT_EVERY_SECONDS = 30;
export const WORD_POINTS = 10;
export const FINISH_BONUS = [30, 15];
export const MAX_PLAYERS = 4;
export const TEAM_SIZE = 2;
export const NAME_MAX = 12;
export const MAX_GUESS_LENGTH = 60;
export const FEED_LENGTH = 8;
export const BOT_GUESS_MIN_MS = 5000;
export const BOT_GUESS_MAX_MS = 9000;
export const CODE_ALPHABET = "BCDFGHJKLMNPQRSTVWXZ";
export const TEAM_IDS = ["sky", "tangerine"];
export const TEAMS = {
  sky: { id: "sky", name: "Sky", color: "#4DABF7" },
  tangerine: { id: "tangerine", name: "Tangerine", color: "#FF922B" }
};

// ---------- names & codes ----------

export function validRoomCode(code) {
  return typeof code === "string" && new RegExp("^[" + CODE_ALPHABET + "]{4}$").test(code.toUpperCase());
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

// ---------- randomness ----------

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

// ---------- players & teams ----------

function freshStats() {
  return { guessed: 0, drawn: 0, guesses: 0 };
}

function makePlayer({ id, name, joinOrder, team, connected = true, isBot = false }) {
  return { id, name, team, color: TEAMS[team].color, connected, isBot, joinOrder, stats: freshStats() };
}

export function teamMembers(room, team) {
  return room.players.filter((player) => player.team === team).sort((a, b) => a.joinOrder - b.joinOrder);
}

function openTeam(room) {
  const counts = TEAM_IDS.map((team) => teamMembers(room, team).length);
  if (counts.every((count) => count >= TEAM_SIZE)) return null;
  return counts[1] < counts[0] ? TEAM_IDS[1] : TEAM_IDS[0];
}

export function createRoom({ code, playerId, name, now, dev = false }) {
  return {
    code,
    createdAt: now,
    hostId: playerId,
    dev: Boolean(dev),
    phase: "lobby",
    nextJoinOrder: 1,
    nextBotNumber: 1,
    players: [makePlayer({ id: playerId, name, joinOrder: 0, team: TEAM_IDS[0] })],
    scores: { sky: 0, tangerine: 0 },
    matches: 0,
    roundId: 0,
    roundNumber: 0,
    round: null,
    usedSentences: []
  };
}

export function addPlayer(room, { playerId, name, now }) {
  if (!room || room.players.length >= MAX_PLAYERS) return null;
  const team = openTeam(room);
  const displayName = uniqueName(room.players, name);
  if (!team || !displayName) return null;
  const player = makePlayer({ id: playerId, name: displayName, joinOrder: room.nextJoinOrder, team });
  return { room: { ...room, nextJoinOrder: room.nextJoinOrder + 1, players: [...room.players, player] }, player };
}

export function addBot(room, playerId, now) {
  if (!room?.dev || !["lobby", "gameOver"].includes(room.phase) || typeof playerId !== "string" || !playerId) return null;
  const team = openTeam(room);
  const name = uniqueName(room.players, "Bot " + room.nextBotNumber);
  if (!team || !name) return null;
  const player = makePlayer({ id: playerId, name, joinOrder: room.nextJoinOrder, team, isBot: true });
  return { ...room, nextJoinOrder: room.nextJoinOrder + 1, nextBotNumber: room.nextBotNumber + 1, players: [...room.players, player] };
}

export function removeBot(room) {
  if (!room?.dev || !["lobby", "gameOver"].includes(room.phase)) return room;
  const bot = room.players.filter((player) => player.isBot).sort((a, b) => b.joinOrder - a.joinOrder)[0];
  return bot ? removePlayer(room, bot.id) : room;
}

export function switchTeam(room, playerId) {
  if (!room || !["lobby", "gameOver"].includes(room.phase)) return room;
  const player = room.players.find((entry) => entry.id === playerId);
  if (!player) return room;
  const target = player.team === TEAM_IDS[0] ? TEAM_IDS[1] : TEAM_IDS[0];
  if (teamMembers(room, target).length >= TEAM_SIZE) return room;
  return {
    ...room,
    players: room.players.map((entry) => entry.id === playerId
      ? { ...entry, team: target, color: TEAMS[target].color }
      : entry)
  };
}

function nextHost(players) {
  return players.filter((player) => player.connected && !player.isBot).sort((a, b) => a.joinOrder - b.joinOrder)[0]?.id || null;
}

export function setPlayerConnected(room, playerId, connected) {
  if (!room) return null;
  const target = room.players.find((player) => player.id === playerId);
  if (!target || target.connected === connected) return room;
  const players = room.players.map((player) => player.id === playerId ? { ...player, connected } : player);
  let hostId = room.hostId;
  if (!connected && hostId === playerId) hostId = nextHost(players);
  else if (connected && !hostId) hostId = playerId;
  return { ...room, hostId, players };
}

export function removePlayer(room, playerId) {
  if (!room) return null;
  const players = room.players.filter((player) => player.id !== playerId);
  if (players.length === room.players.length) return room;
  return { ...room, hostId: room.hostId === playerId ? nextHost(players) : room.hostId, players };
}

export function canStart(room, playerId) {
  if (!room || !["lobby", "gameOver"].includes(room.phase) || room.hostId !== playerId) return false;
  return TEAM_IDS.every((team) => teamMembers(room, team).filter((player) => player.connected).length === TEAM_SIZE);
}

// ---------- rounds ----------

export function wordCountForRound(roundNumber) {
  return roundNumber <= 2 ? 4 : 5;
}

export function pickSentences(count, used = [], rng = Math.random, sentences = SENTENCES) {
  const list = sentences[count] || [];
  let pool = list.filter((sentence) => !used.includes(sentence));
  let nextUsed = used;
  if (pool.length < TEAM_IDS.length) {
    pool = [...list];
    nextUsed = used.filter((sentence) => !list.includes(sentence));
  }
  const picked = shuffled(pool, rng).slice(0, TEAM_IDS.length);
  return { picked, used: [...nextUsed, ...picked] };
}

// Order in which hint letters appear: one random letter of each hidden word, then more.
function hintPlan(words, rng) {
  const slots = [];
  words.forEach((word, wordIndex) => {
    if (GIVEN_WORDS.has(word)) return;
    shuffled([...word].map((_, letter) => letter), rng).slice(0, Math.max(1, Math.floor(word.length / 2)))
      .forEach((letter, depth) => slots.push({ wordIndex, letter, depth }));
  });
  return shuffled(slots, rng).sort((a, b) => a.depth - b.depth).map(({ wordIndex, letter }) => [wordIndex, letter]);
}

export function startRound(room, now, rng = Math.random, sentences = SENTENCES, timing = {}) {
  const roundNumber = room.roundNumber + 1;
  const { picked, used } = pickSentences(wordCountForRound(roundNumber), room.usedSentences, rng, sentences);
  const teams = {};
  TEAM_IDS.forEach((team, index) => {
    const members = teamMembers(room, team);
    const drawer = members[(roundNumber - 1) % Math.max(1, members.length)];
    const guesser = members.find((player) => player.id !== drawer?.id);
    const words = picked[index].split(" ");
    teams[team] = {
      drawerId: drawer?.id || null,
      guesserId: guesser?.id || null,
      words,
      revealed: words.map((word) => GIVEN_WORDS.has(word)),
      hints: hintPlan(words, rng),
      guesses: [],
      done: false,
      place: 0,
      points: 0
    };
  });
  return {
    ...room,
    phase: "countdown",
    roundId: room.roundId + 1,
    roundNumber,
    usedSentences: used,
    round: {
      roundId: room.roundId + 1,
      number: roundNumber,
      phaseEndsAt: now + (timing.countdown ?? COUNTDOWN_SECONDS) * 1000,
      drawStartedAt: null,
      teams
    }
  };
}

export function startGame(room, playerId, now, rng = Math.random, sentences = SENTENCES, timing = {}) {
  if (!canStart(room, playerId)) return null;
  return startRound({
    ...room,
    players: room.players.map((player) => ({ ...player, stats: freshStats() })),
    scores: { sky: 0, tangerine: 0 },
    roundNumber: 0,
    round: null
  }, now, rng, sentences, timing);
}

function finishRound(room, now, timing = {}) {
  return {
    ...room,
    phase: "roundEnd",
    round: { ...room.round, phaseEndsAt: now + (timing.roundEnd ?? ROUND_END_SECONDS) * 1000 }
  };
}

export function advanceRoom(room, now, rng = Math.random, sentences = SENTENCES, timing = {}) {
  if (!room?.round || now < room.round.phaseEndsAt) return room;
  if (room.phase === "countdown") {
    const start = room.round.phaseEndsAt;
    return {
      ...room,
      phase: "drawing",
      round: { ...room.round, drawStartedAt: start, phaseEndsAt: start + (timing.draw ?? DRAW_SECONDS) * 1000 }
    };
  }
  if (room.phase === "drawing") return finishRound(room, room.round.phaseEndsAt, timing);
  if (room.phase === "roundEnd") {
    if (room.roundNumber >= (timing.rounds ?? TOTAL_ROUNDS)) return { ...room, phase: "gameOver", matches: room.matches + 1 };
    return startRound(room, room.round.phaseEndsAt, rng, sentences, timing);
  }
  return room;
}

// Number of hint letters unlocked at `now` during the drawing phase.
export function hintsUnlocked(room, now, timing = {}) {
  if (room?.phase !== "drawing" || !room.round?.drawStartedAt) return 0;
  return Math.max(0, Math.floor((now - room.round.drawStartedAt) / ((timing.hintEvery ?? HINT_EVERY_SECONDS) * 1000)));
}

// When the server should next wake up for this room (phase change or a new hint letter).
export function nextWakeAt(room, now, timing = {}) {
  if (!room?.round || !["countdown", "drawing", "roundEnd"].includes(room.phase)) return null;
  let wake = room.round.phaseEndsAt;
  if (room.phase === "drawing") {
    const every = (timing.hintEvery ?? HINT_EVERY_SECONDS) * 1000;
    const nextHint = room.round.drawStartedAt + (hintsUnlocked(room, now, timing) + 1) * every;
    if (nextHint < wake) wake = nextHint;
  }
  return wake;
}

// ---------- guessing ----------

export function normalizeWord(value) {
  return String(value || "").toUpperCase().replace(/[^A-Z]/g, "");
}

function stem(word) {
  if (word.length > 4 && word.endsWith("ES")) return word.slice(0, -2);
  if (word.length > 3 && word.endsWith("S")) return word.slice(0, -1);
  return word;
}

export function wordsMatch(guess, word) {
  return guess === word || (guess.length >= 3 && stem(guess) === stem(word));
}

function editDistance(a, b) {
  const dp = Array.from({ length: a.length + 1 }, (_, i) => [i, ...Array(b.length).fill(0)]);
  for (let j = 1; j <= b.length; j += 1) dp[0][j] = j;
  for (let i = 1; i <= a.length; i += 1) {
    for (let j = 1; j <= b.length; j += 1) {
      dp[i][j] = Math.min(dp[i - 1][j] + 1, dp[i][j - 1] + 1, dp[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
    }
  }
  return dp[a.length][b.length];
}

export function isClose(guess, word) {
  if (guess.length < 3 || wordsMatch(guess, word)) return false;
  const limit = word.length >= 8 ? 2 : word.length >= 4 ? 1 : 0;
  return limit > 0 && editDistance(guess, word) <= limit;
}

export function teamOfGuesser(room, playerId) {
  if (!room?.round) return null;
  return TEAM_IDS.find((team) => room.round.teams[team]?.guesserId === playerId) || null;
}

export function teamOfDrawer(room, playerId) {
  if (!room?.round) return null;
  return TEAM_IDS.find((team) => room.round.teams[team]?.drawerId === playerId) || null;
}

function bumpStats(players, id, change) {
  return players.map((player) => player.id === id
    ? { ...player, stats: { ...player.stats, ...Object.fromEntries(Object.entries(change).map(([k, v]) => [k, player.stats[k] + v])) } }
    : player);
}

export function applyGuess(room, playerId, text, now, timing = {}) {
  if (room?.phase !== "drawing" || typeof text !== "string") return { room, effects: [] };
  const team = teamOfGuesser(room, playerId);
  const state = team && room.round.teams[team];
  if (!state || state.done) return { room, effects: [] };
  const guesses = text.slice(0, MAX_GUESS_LENGTH).split(/\s+/).map(normalizeWord).filter(Boolean).slice(0, 6);
  if (guesses.length === 0) return { room, effects: [] };

  const revealed = [...state.revealed];
  const feed = [...state.guesses];
  const effects = [];
  let found = 0;
  for (const guess of guesses) {
    const hits = state.words.map((word, index) => (!revealed[index] && wordsMatch(guess, word) ? index : -1)).filter((index) => index >= 0);
    if (hits.length) {
      hits.forEach((index) => { revealed[index] = true; });
      found += hits.length;
      feed.push({ text: guess, result: "correct" });
      effects.push({ kind: "correct", team, playerId, word: state.words[hits[0]] });
      continue;
    }
    const close = state.words.some((word, index) => !revealed[index] && isClose(guess, word));
    feed.push({ text: guess, result: close ? "close" : "wrong" });
    if (close) effects.push({ kind: "close", team, playerId, word: guess });
  }

  let players = bumpStats(room.players, playerId, { guesses: guesses.length, guessed: found });
  if (found && state.drawerId) players = bumpStats(players, state.drawerId, { drawn: found });
  const done = revealed.every(Boolean);
  const place = done ? TEAM_IDS.filter((other) => room.round.teams[other].done).length + 1 : 0;
  const points = state.points + found * WORD_POINTS + (done ? FINISH_BONUS[place - 1] || 0 : 0);
  const scores = { ...room.scores, [team]: room.scores[team] + (points - state.points) };
  if (done) effects.push({ kind: "teamDone", team, place });

  let next = {
    ...room,
    players,
    scores,
    round: {
      ...room.round,
      teams: { ...room.round.teams, [team]: { ...state, revealed, guesses: feed.slice(-FEED_LENGTH), done, place, points, doneAt: done ? now : null } }
    }
  };
  if (TEAM_IDS.every((id) => next.round.teams[id].done)) next = finishRound(next, now, timing);
  return { room: next, effects };
}

// Dev bots: a guesser bot sometimes types a right word, sometimes a near miss or a wrong one.
export function chooseBotGuess(room, botId, rng = Math.random) {
  const team = teamOfGuesser(room, botId);
  const state = team && room.round.teams[team];
  if (!state || state.done) return null;
  const hidden = state.words.filter((word, index) => !state.revealed[index]);
  if (!hidden.length) return null;
  const roll = rng();
  if (roll < 0.55) return hidden[randomIndex(hidden.length, rng)];
  if (roll < 0.75) {
    const word = hidden[randomIndex(hidden.length, rng)];
    return word.length >= 4 ? word.slice(0, -1) + (word.endsWith("Z") ? "Y" : "Z") : "BANANA";
  }
  return ["BANANA", "HOUSE", "RUNNING", "BLUE", "MONSTER"][randomIndex(5, rng)];
}

// ---------- snapshots ----------

function wordView(word, index, state, visible, unlocked) {
  if (visible || state.revealed[index]) return { text: word, length: word.length, revealed: state.revealed[index] };
  const letters = [...word].map(() => "_");
  state.hints.slice(0, unlocked).forEach(([wordIndex, letter]) => {
    if (wordIndex === index) letters[letter] = word[letter];
  });
  return { text: letters.join(""), length: word.length, revealed: false };
}

// Each viewer gets their own snapshot: secret words are only sent to the team's drawer
// (and to everyone once the round is over).
export function publicSnapshot(room, serverNow, viewerId = null, timing = {}) {
  if (!room) return null;
  const viewer = room.players.find((player) => player.id === viewerId);
  const reveal = room.phase === "roundEnd" || room.phase === "gameOver";
  const unlocked = hintsUnlocked(room, serverNow, timing);
  const round = room.round && {
    roundId: room.round.roundId,
    number: room.round.number,
    total: timing.rounds ?? TOTAL_ROUNDS,
    phaseEndsAt: room.round.phaseEndsAt,
    drawStartedAt: room.round.drawStartedAt,
    teams: TEAM_IDS.map((team) => {
      const state = room.round.teams[team];
      const visible = reveal || state.drawerId === viewerId;
      const ownTeam = viewer?.team === team;
      return {
        team,
        drawerId: state.drawerId,
        guesserId: state.guesserId,
        words: state.words.map((word, index) => wordView(word, index, state, visible, unlocked)),
        found: state.revealed.filter((shown, index) => shown && !GIVEN_WORDS.has(state.words[index])).length,
        total: state.words.filter((word) => !GIVEN_WORDS.has(word)).length,
        done: state.done,
        place: state.place,
        points: state.points,
        guesses: ownTeam || reveal ? state.guesses : []
      };
    })
  };
  return {
    code: room.code,
    phase: room.phase,
    serverNow,
    hostId: room.hostId,
    dev: Boolean(room.dev),
    matches: room.matches,
    teams: TEAM_IDS.map((team) => ({
      id: team,
      name: TEAMS[team].name,
      color: TEAMS[team].color,
      score: room.scores[team],
      playerIds: teamMembers(room, team).map((player) => player.id)
    })),
    players: room.players.map(({ id, name, color, team, connected, isBot, stats }) => ({
      id, name, color, team, connected, isBot: Boolean(isBot), stats: { ...stats }
    })),
    round
  };
}
