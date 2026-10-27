import { WORDS } from "./words.js";

// Risky Picks: two teams of two. Each round both drawers pick a word (EASY x1, MEDIUM x2,
// HARD x3), draw it at the same time, and their partners race to guess it. Rivals may steal.
export const PICK_SECONDS = 10;
export const DRAW_SECONDS = 60;
export const ROUND_END_SECONDS = 7;
export const TOTAL_ROUNDS = 6;
export const HINT_EVERY_SECONDS = 15;
export const BASE_POINTS = 10;
export const SPEED_POINTS = 10;
export const STEAL_POINTS = 7;
export const LEVEL_IDS = ["easy", "medium", "hard"];
export const LEVELS = {
  easy: { name: "EASY", multiplier: 1, penalty: 0 },
  medium: { name: "MEDIUM", multiplier: 2, penalty: 5 },
  hard: { name: "HARD", multiplier: 3, penalty: 10 }
};
export const MAX_PLAYERS = 4;
export const TEAM_SIZE = 2;
export const NAME_MAX = 12;
export const MAX_GUESS_LENGTH = 60;
export const FEED_LENGTH = 8;
export const BOT_GUESS_MIN_MS = 4000;
export const BOT_GUESS_MAX_MS = 8000;
export const CODE_ALPHABET = "BCDFGHJKLMNPQRSTVWXZ";
// Little words in phrases (TUG OF WAR) are shown from the start.
export const GIVEN_WORDS = new Set(["A", "AN", "THE", "OF", "AND"]);
// What the host can change in the lobby, and the defaults.
export const SETTING_CHOICES = {
  drawSeconds: [45, 60, 90, 120],
  hintSeconds: [0, 10, 15, 20],
  rounds: [4, 6, 8, 10],
  stealing: ["on", "off"]
};
export const DEFAULT_SETTINGS = { drawSeconds: DRAW_SECONDS, hintSeconds: HINT_EVERY_SECONDS, rounds: TOTAL_ROUNDS, stealing: "on" };
export const TEAM_IDS = ["sky", "tangerine"];
export const TEAMS = {
  sky: { id: "sky", name: "Sky", color: "#2E86FF" },
  tangerine: { id: "tangerine", name: "Tangerine", color: "#FF7A1A" }
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
  return { guessed: 0, stolen: 0, drawn: 0, guesses: 0 };
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
    settings: { ...DEFAULT_SETTINGS },
    matches: 0,
    roundId: 0,
    roundNumber: 0,
    round: null,
    usedWords: []
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

// Applies the host's lobby choices; anything outside the allowed values is ignored.
export function updateSettings(room, playerId, patch) {
  if (!room || room.hostId !== playerId || !["lobby", "gameOver"].includes(room.phase) || !patch || typeof patch !== "object") return room;
  const settings = { ...room.settings };
  for (const [key, choices] of Object.entries(SETTING_CHOICES)) {
    if (!(key in patch)) continue;
    const value = key === "stealing" ? String(patch[key]) : Number(patch[key]);
    if (choices.includes(value)) settings[key] = value;
  }
  const changed = Object.keys(settings).some((key) => settings[key] !== room.settings[key]);
  return changed ? { ...room, settings } : room;
}

// Effective timings: test overrides first, then the host's settings.
function rules(room, timing = {}) {
  const settings = room.settings || DEFAULT_SETTINGS;
  return {
    pick: timing.pick ?? PICK_SECONDS,
    draw: timing.draw ?? settings.drawSeconds,
    roundEnd: timing.roundEnd ?? ROUND_END_SECONDS,
    rounds: timing.rounds ?? settings.rounds,
    hintEvery: timing.hintEvery ?? settings.hintSeconds,
    stealing: settings.stealing !== "off"
  };
}

export function canStart(room, playerId) {
  if (!room || !["lobby", "gameOver"].includes(room.phase) || room.hostId !== playerId) return false;
  return TEAM_IDS.every((team) => teamMembers(room, team).filter((player) => player.connected).length === TEAM_SIZE);
}

// ---------- rounds ----------

// Draws an unused word from a deck; a deck that runs dry starts over.
function drawWord(level, used, rng, words) {
  const deck = words[level] || [];
  let pool = deck.filter((word) => !used.has(word));
  if (pool.length === 0) {
    deck.forEach((word) => used.delete(word));
    pool = [...deck];
  }
  const word = pool[randomIndex(pool.length, rng)];
  used.add(word);
  return word;
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

function emptyTarget() {
  return { choice: null, words: [], revealed: [], by: [], hints: [] };
}

export function startRound(room, now, rng = Math.random, words = WORDS, timing = {}) {
  const roundNumber = room.roundNumber + 1;
  const used = new Set(room.usedWords);
  const teams = {};
  for (const team of TEAM_IDS) {
    const members = teamMembers(room, team);
    const drawer = members[(roundNumber - 1) % Math.max(1, members.length)];
    const guesser = members.find((player) => player.id !== drawer?.id);
    teams[team] = {
      drawerId: drawer?.id || null,
      guesserId: guesser?.id || null,
      options: LEVEL_IDS.map((level) => ({ level, word: drawWord(level, used, rng, words) })),
      ...emptyTarget(),
      guesses: [],
      done: false,
      solvedBy: null,
      points: 0,
      penalty: 0,
      doneAt: null
    };
  }
  return {
    ...room,
    phase: "picking",
    roundId: room.roundId + 1,
    roundNumber,
    usedWords: [...used],
    round: {
      roundId: room.roundId + 1,
      number: roundNumber,
      phaseEndsAt: now + rules(room, timing).pick * 1000,
      drawStartedAt: null,
      teams
    }
  };
}

export function startGame(room, playerId, now, rng = Math.random, words = WORDS, timing = {}) {
  if (!canStart(room, playerId)) return null;
  return startRound({
    ...room,
    players: room.players.map((player) => ({ ...player, stats: freshStats() })),
    scores: { sky: 0, tangerine: 0 },
    roundNumber: 0,
    round: null
  }, now, rng, words, timing);
}

function applyChoice(state, level, rng) {
  const option = state.options.find((entry) => entry.level === level) || state.options[0];
  const words = option.word.split(" ");
  return {
    ...state,
    choice: option.level,
    words,
    revealed: words.map((word) => GIVEN_WORDS.has(word)),
    by: words.map((word) => (GIVEN_WORDS.has(word) ? "given" : null)),
    hints: hintPlan(words, rng)
  };
}

function beginDrawing(room, start, rng, timing) {
  const teams = { ...room.round.teams };
  for (const team of TEAM_IDS) if (!teams[team].choice) teams[team] = applyChoice(teams[team], "easy", rng);
  return {
    ...room,
    phase: "drawing",
    round: { ...room.round, teams, drawStartedAt: start, phaseEndsAt: start + rules(room, timing).draw * 1000 }
  };
}

// A drawer locks in EASY, MEDIUM or HARD. Once both drawers have picked, drawing starts at once.
export function chooseWord(room, playerId, level, now, rng = Math.random, timing = {}) {
  if (room?.phase !== "picking" || !LEVEL_IDS.includes(level)) return room;
  const team = teamOfDrawer(room, playerId);
  if (!team || room.round.teams[team].choice) return room;
  const next = {
    ...room,
    round: { ...room.round, teams: { ...room.round.teams, [team]: applyChoice(room.round.teams[team], level, rng) } }
  };
  return TEAM_IDS.every((id) => next.round.teams[id].choice) ? beginDrawing(next, now, rng, timing) : next;
}

// Unsolved risky words cost their team points when the round ends.
function finishRound(room, now, timing = {}) {
  const scores = { ...room.scores };
  const teams = { ...room.round.teams };
  for (const team of TEAM_IDS) {
    const state = teams[team];
    if (state.done || !state.choice) continue;
    const penalty = LEVELS[state.choice].penalty;
    scores[team] -= penalty;
    teams[team] = { ...state, penalty, points: state.points - penalty };
  }
  return {
    ...room,
    scores,
    phase: "roundEnd",
    round: { ...room.round, teams, phaseEndsAt: now + rules(room, timing).roundEnd * 1000 }
  };
}

export function advanceRoom(room, now, rng = Math.random, words = WORDS, timing = {}) {
  if (!room?.round || now < room.round.phaseEndsAt) return room;
  if (room.phase === "picking") return beginDrawing(room, room.round.phaseEndsAt, rng, timing);
  if (room.phase === "drawing") return finishRound(room, room.round.phaseEndsAt, timing);
  if (room.phase === "roundEnd") {
    if (room.roundNumber >= rules(room, timing).rounds) return { ...room, phase: "gameOver", matches: room.matches + 1 };
    return startRound(room, room.round.phaseEndsAt, rng, words, timing);
  }
  return room;
}

// Number of hint letters unlocked at `now` during the drawing phase.
export function hintsUnlocked(room, now, timing = {}) {
  const every = room ? rules(room, timing).hintEvery : 0;
  if (room?.phase !== "drawing" || !room.round?.drawStartedAt || !every) return 0;
  return Math.max(0, Math.floor((now - room.round.drawStartedAt) / (every * 1000)));
}

// When the server should next wake up for this room (phase change or a new hint letter).
export function nextWakeAt(room, now, timing = {}) {
  if (!room?.round || !["picking", "drawing", "roundEnd"].includes(room.phase)) return null;
  let wake = room.round.phaseEndsAt;
  const every = rules(room, timing).hintEvery * 1000;
  if (room.phase === "drawing" && every > 0) {
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

function otherTeam(team) {
  return TEAM_IDS.find((id) => id !== team);
}

function copyState(state) {
  return { ...state, revealed: [...state.revealed], by: [...state.by], guesses: [...state.guesses] };
}

// Reveals every hidden word in `state` that matches `guess`, crediting `team`. Returns the indexes.
function reveal(state, guess, team) {
  const hits = [];
  state.words.forEach((word, index) => {
    if (!state.revealed[index] && wordsMatch(guess, word)) {
      state.revealed[index] = true;
      state.by[index] = team;
      hits.push(index);
    }
  });
  return hits;
}

// Points for guessing your own word: (10 + up to 10 for speed) x the difficulty multiplier.
export function solvePoints(level, remainingMs, totalMs) {
  const speed = Math.round(SPEED_POINTS * Math.min(1, Math.max(0, remainingMs / Math.max(1, totalMs))));
  return LEVELS[level].multiplier * (BASE_POINTS + speed);
}

export function stealPoints(level) {
  return LEVELS[level].multiplier * STEAL_POINTS;
}

// A guess is checked against your own word first, then (if stealing is on) the other team's.
// Whoever completes a word scores it: owners get the full value, thieves get 7 x the multiplier.
export function applyGuess(room, playerId, text, now, timing = {}) {
  if (room?.phase !== "drawing" || typeof text !== "string") return { room, effects: [] };
  const team = teamOfGuesser(room, playerId);
  if (!team) return { room, effects: [] };
  const rival = otherTeam(team);
  const stealing = rules(room, timing).stealing;
  const guesses = text.slice(0, MAX_GUESS_LENGTH).split(/\s+/).map(normalizeWord).filter(Boolean).slice(0, 6);
  if (guesses.length === 0) return { room, effects: [] };

  const states = { [team]: copyState(room.round.teams[team]), [rival]: copyState(room.round.teams[rival]) };
  const own = states[team];
  const theirs = states[rival];
  const feed = own.guesses;
  const effects = [];
  for (const guess of guesses) {
    if (!own.done && reveal(own, guess, team).length) {
      feed.push({ text: guess, result: "correct" });
      effects.push({ kind: "correct", team, playerId, word: guess });
      continue;
    }
    if (stealing && !theirs.done && reveal(theirs, guess, team).length) {
      feed.push({ text: guess, result: "stolen" });
      continue;
    }
    const targets = [own.done ? null : own, stealing && !theirs.done ? theirs : null].filter(Boolean);
    const close = targets.some((state) => state.words.some((word, index) => !state.revealed[index] && isClose(guess, word)));
    feed.push({ text: guess, result: close ? "close" : "wrong" });
    if (close) effects.push({ kind: "close", team, playerId, word: guess });
  }
  own.guesses = feed.slice(-FEED_LENGTH);

  const scores = { ...room.scores };
  let players = bumpStats(room.players, playerId, { guesses: guesses.length });
  const total = room.round.phaseEndsAt - room.round.drawStartedAt;
  for (const id of [team, rival]) {
    const state = states[id];
    if (state.done || !state.revealed.length || !state.revealed.every(Boolean)) continue;
    state.done = true;
    state.doneAt = now;
    state.solvedBy = team;
    const word = state.words.join(" ");
    if (id === team) {
      const points = solvePoints(state.choice, room.round.phaseEndsAt - now, total);
      state.points += points;
      scores[team] += points;
      players = bumpStats(players, playerId, { guessed: 1 });
      if (state.drawerId) players = bumpStats(players, state.drawerId, { drawn: 1 });
      effects.push({ kind: "solved", team, playerId, word, level: state.choice, points });
    } else {
      const points = stealPoints(state.choice);
      own.points += points;
      scores[team] += points;
      players = bumpStats(players, playerId, { stolen: 1 });
      effects.push({ kind: "stolen", team, from: id, playerId, word, level: state.choice, points });
    }
  }

  let next = { ...room, players, scores, round: { ...room.round, teams: { ...room.round.teams, ...states } } };
  if (TEAM_IDS.every((id) => next.round.teams[id].done)) next = finishRound(next, now, timing);
  return { room: next, effects };
}

// A bot's self-portrait for its capsule avatar, in the same strokes players draw: a body fill,
// eyes, a mouth and sometimes something sticking out of the top. The capsule spans x 0.25..0.75,
// y 0.24..0.93 of the square.
const BOT_COLORS = ["#FF3B3B", "#FF8A1F", "#FFD23F", "#5ED16A", "#2BD4C1", "#9B5CFF", "#FF6FB5", "#4DB5FF"];
export function botFace(rng = Math.random) {
  const ink = "#17141F";
  const body = BOT_COLORS[randomIndex(BOT_COLORS.length, rng)];
  const strokes = [{ t: "fill", c: body, s: 0, p: [0.5, 0.62] }];
  const eyes = randomIndex(3, rng);
  if (eyes === 0) strokes.push({ c: ink, s: 14, p: [0.41, 0.52] }, { c: ink, s: 14, p: [0.59, 0.52] });
  else if (eyes === 1) strokes.push({ c: ink, s: 6, p: [0.36, 0.52, 0.45, 0.52] }, { c: ink, s: 6, p: [0.55, 0.52, 0.64, 0.52] });
  else strokes.push({ c: "#FFFFFF", s: 22, p: [0.5, 0.5] }, { c: ink, s: 10, p: [0.51, 0.51] });
  const mouth = randomIndex(2, rng);
  if (mouth === 0) strokes.push({ c: ink, s: 6, p: [0.41, 0.68, 0.46, 0.73, 0.54, 0.73, 0.59, 0.68] });
  else strokes.push({ c: ink, s: 6, p: [0.42, 0.72, 0.58, 0.72] });
  const top = randomIndex(3, rng);
  if (top === 0) strokes.push({ c: ink, s: 6, p: [0.5, 0.24, 0.5, 0.1] }, { c: "#FF3B3B", s: 14, p: [0.5, 0.09] });
  else if (top === 1) strokes.push({ c: ink, s: 7, p: [0.32, 0.3, 0.36, 0.13, 0.43, 0.25, 0.5, 0.1, 0.57, 0.25, 0.64, 0.13, 0.68, 0.3] });
  return strokes;
}

// Dev bots: drawers pick a random difficulty; guessers mostly try their own word,
// sometimes steal, sometimes miss.
export function chooseBotLevel(rng = Math.random) {
  return LEVEL_IDS[randomIndex(LEVEL_IDS.length, rng)];
}

export function chooseBotGuess(room, botId, rng = Math.random) {
  const team = teamOfGuesser(room, botId);
  if (!team) return null;
  const hidden = (state) => (state.done ? [] : state.words.filter((word, index) => !state.revealed[index]));
  const mine = hidden(room.round.teams[team]);
  const theirs = rules(room).stealing ? hidden(room.round.teams[otherTeam(team)]) : [];
  const roll = rng();
  if (mine.length && roll < 0.45) return mine[randomIndex(mine.length, rng)];
  if (theirs.length && (roll < 0.6 || !mine.length)) return theirs[randomIndex(theirs.length, rng)];
  if (roll < 0.8 && mine.length) {
    const word = mine[randomIndex(mine.length, rng)];
    return word.length >= 4 ? word.slice(0, -1) + (word.endsWith("Z") ? "Y" : "Z") : "BANANA";
  }
  return ["BANANA", "HOUSE", "RUNNING", "BLUE", "MONSTER"][randomIndex(5, rng)];
}

// ---------- snapshots ----------

function wordView(word, index, state, visible, unlocked) {
  const by = state.by[index] || "";
  if (visible || state.revealed[index]) return { text: word, length: word.length, revealed: state.revealed[index], by };
  const letters = [...word].map(() => "_");
  state.hints.slice(0, unlocked).forEach(([wordIndex, letter]) => {
    if (wordIndex === index) letters[letter] = word[letter];
  });
  return { text: letters.join(""), length: word.length, revealed: false, by };
}

// Each viewer gets their own snapshot: the drawer sees their options and word; everyone else
// sees blanks until the round is over.
export function publicSnapshot(room, serverNow, viewerId = null, timing = {}) {
  if (!room) return null;
  const viewer = room.players.find((player) => player.id === viewerId);
  const reveal = room.phase === "roundEnd" || room.phase === "gameOver";
  const unlocked = hintsUnlocked(room, serverNow, timing);
  const round = room.round && {
    roundId: room.round.roundId,
    number: room.round.number,
    total: rules(room, timing).rounds,
    phaseEndsAt: room.round.phaseEndsAt,
    drawStartedAt: room.round.drawStartedAt,
    stealing: rules(room, timing).stealing,
    teams: TEAM_IDS.map((team) => {
      const state = room.round.teams[team];
      const isDrawer = state.drawerId === viewerId;
      const visible = reveal || isDrawer;
      const ownTeam = viewer?.team === team;
      return {
        team,
        drawerId: state.drawerId,
        guesserId: state.guesserId,
        options: isDrawer && room.phase === "picking" ? state.options : [],
        level: state.choice || "",
        multiplier: state.choice ? LEVELS[state.choice].multiplier : 0,
        words: state.words.map((word, index) => wordView(word, index, state, visible, unlocked)),
        found: state.revealed.filter((shown, index) => shown && !GIVEN_WORDS.has(state.words[index])).length,
        total: state.words.filter((word) => !GIVEN_WORDS.has(word)).length,
        done: state.done,
        solvedBy: state.solvedBy || "",
        points: state.points,
        penalty: state.penalty,
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
    settings: { ...(room.settings || DEFAULT_SETTINGS) },
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
