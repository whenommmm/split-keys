import test from "node:test";
import assert from "node:assert/strict";
import { LEVEL_RANGES, PHRASES, letterCount } from "../server/phrases.js";

test("each level has at least 25 unique phrases in its letter range", () => {
  const all = [];
  for (const [level, list] of Object.entries(PHRASES)) {
    assert.ok(list.length >= 25, "level " + level + " needs 25 phrases");
    assert.equal(new Set(list).size, list.length, "duplicates at level " + level);
    for (const phrase of list) {
      assert.match(phrase, /^[A-Z]+( [A-Z]+)*$/);
      const count = letterCount(phrase);
      assert.ok(count >= LEVEL_RANGES[level][0] && count <= LEVEL_RANGES[level][1],
        phrase + " has " + count + " letters at level " + level);
      all.push(phrase);
    }
  }
  assert.equal(new Set(all).size, all.length, "phrase repeated across levels");
});

