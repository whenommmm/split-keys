const LEVEL_RANGES = {
  1: [6, 9],
  2: [10, 13],
  3: [14, 17],
  4: [18, 21],
  5: [22, 25],
  6: [26, 30]
};

const levelOne = [
  "HOT DOG", "PIZZA TIME", "RED HAT", "BIG BUG", "MUD PIE",
  "SAD CAT", "FUN RUN", "TINY COW", "BUNNY HOP", "SILLY HAT",
  "MATH HOG", "CUP OF TEA", "WOW A FOX", "FAT DUCK", "CAT NAP",
  "LOST SHOE", "YUM YUM", "BIG PUG", "JAZZY MUG", "BEEP BOOP",
  "MAD COW", "PUPPY JAM", "TACO CAT", "ZANY KID", "PIG WIG"
];

const levelTwo = [
  "BANANA PHONE", "COOKIE MONSTER", "SILLY ROBOT", "PENGUIN PARTY",
  "TURTLE TAXI", "DANCING PICKLE", "GIANT CUPCAKE", "FROZEN WAFFLE",
  "COSMIC BANANA", "WIGGLY NOODLE", "GOBLIN SOCKS", "PIRATE PUPPY",
  "SLEEPY DRAGON", "SNEAKY POTATO", "UNICORN JUICE", "BUBBLE TROUBLE",
  "WOBBLY PANCAKE", "PICKLE ROCKET", "NOODLE WIZARD", "ROBOT PIGEON",
  "TINY DINOSAUR", "WIZARD FROG", "DIZZY CUPCAKE", "PARTY GIRAFFE",
  "MYSTERY HAT"
];

const adjectives = [
  "TINY", "SILLY", "FUZZY", "DIZZY", "ZANY", "WIGGLY", "SLEEPY",
  "JAZZY", "BUBBLY", "GRUMPY", "COSMIC", "FLUFFY", "SNEAKY", "GIGGLY",
  "WOBBLY", "POLKA DOT", "MYSTERY", "GIGANTIC", "BACKWARDS", "CURIOUS"
];
const creatures = [
  "CAT", "DOG", "PIG", "COW", "DUCK", "MOOSE", "LLAMA", "PENGUIN",
  "PIGEON", "GOBLIN", "ROBOT", "TURTLE", "HAMSTER", "UNICORN", "OTTER",
  "CHICKEN", "YETI", "PUPPY", "WALRUS", "KANGAROO"
];
const actions = [
  "ATE", "WORE", "HAD", "MET", "GOT", "DREW", "MAILED", "JUGGLED",
  "BORROWED", "TICKLED", "CARRIED", "HUGGED", "PAINTED", "FOLLOWED",
  "ORDERED", "INVENTED", "STOLE", "DANCED WITH", "SANG TO", "FOUND"
];
const objects = [
  "A PIE", "MY HAT", "A WIG", "THE MOON", "A SPOON", "MY SOCKS",
  "THREE PIES", "BIG SHOES", "A TACO", "THE ROBOT", "A BANANA",
  "THE HOMEWORK", "A TINY HAT", "THE WIZARD", "A NOODLE", "TWO PICKLES",
  "A SILLY SONG", "THE PURPLE CAKE", "A GIANT WAFFLE", "THE NEIGHBOR'S HAT"
];

function letterCount(phrase) {
  return phrase.replaceAll(" ", "").length;
}

const candidates = [];
for (const adjective of adjectives) {
  for (const creature of creatures) {
    for (const action of actions) {
      for (const object of objects) {
        candidates.push("THE " + adjective + " " + creature + " " + action + " " + object);
      }
    }
  }
}
const seen = new Set([...levelOne, ...levelTwo]);
const generated = Object.fromEntries([3, 4, 5, 6].map((level) => [level, []]));
for (const phrase of candidates) {
  if (!/^[A-Z]+( [A-Z]+)*$/.test(phrase) || seen.has(phrase)) continue;
  const count = letterCount(phrase);
  for (const [levelText, range] of Object.entries(LEVEL_RANGES)) {
    const level = Number(levelText);
    if (level < 3 || generated[level].length >= 32) continue;
    if (count >= range[0] && count <= range[1]) {
      generated[level].push(phrase);
      seen.add(phrase);
      break;
    }
  }
}

export const PHRASES = {
  1: levelOne,
  2: levelTwo,
  3: generated[3],
  4: generated[4],
  5: generated[5],
  6: generated[6]
};

export { LEVEL_RANGES, letterCount };

