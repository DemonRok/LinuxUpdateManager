import test from "node:test";
import assert from "node:assert/strict";
import { calculateNextVersion, determineReleaseLevel, parseVersion } from "./release-version.mjs";

test("fix commits increase patch without a two digit cap", () => {
  assert.equal(calculateNextVersion("0.1.9", ["fix: correct update check"]), "0.1.10");
});

test("feature commits increase minor and roll over after minor nine", () => {
  assert.equal(calculateNextVersion("0.9.16", ["feat: add update history"]), "1.0.0");
  assert.equal(calculateNextVersion("1.9.25", ["feat: add export"]), "2.0.0");
});

test("breaking changes take priority over other commit types", () => {
  assert.equal(calculateNextVersion("1.4.8", ["fix: handle edge case", "feat!: replace config schema"]), "2.0.0");
  assert.equal(determineReleaseLevel(["refactor: change internals\n\nBREAKING CHANGE: remove old endpoint"]), "major");
});

test("the highest applicable level is selected once per release", () => {
  assert.equal(calculateNextVersion("0.4.7", ["fix: correct label", "feat: add support dialog"]), "0.5.0");
});

test("docs and maintenance commits do not create a release", () => {
  assert.equal(calculateNextVersion("0.4.7", ["docs: update readme", "chore: refresh workflow"]), null);
  assert.equal(parseVersion("v1.2.3"), null);
});
