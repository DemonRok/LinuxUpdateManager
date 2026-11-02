import test from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { calculateNextVersion, determineReleaseLevel, parseVersion } from "./release-version.mjs";

const scriptsDirectory = path.dirname(fileURLToPath(import.meta.url));

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

test("eligible fixes after a completed release create the next patch release", () => {
  const repository = mkdtempSync(path.join(os.tmpdir(), "lum-release-version-test-"));
  const git = (...args) => execFileSync("git", args, { cwd: repository, stdio: "ignore" });

  try {
    mkdirSync(path.join(repository, ".github/scripts"), { recursive: true });
    mkdirSync(path.join(repository, ".github"), { recursive: true });
    mkdirSync(path.join(repository, "src/LinuxUpdateManager.App"), { recursive: true });
    writeFileSync(path.join(repository, ".github/scripts/prepare-release.mjs"), readFileSync(path.join(scriptsDirectory, "prepare-release.mjs")));
    writeFileSync(path.join(repository, ".github/scripts/release-version.mjs"), readFileSync(path.join(scriptsDirectory, "release-version.mjs")));
    writeFileSync(path.join(repository, ".github/release-readiness.json"), JSON.stringify({ phase7Complete: true, phase8Complete: true }));
    writeFileSync(path.join(repository, "src/LinuxUpdateManager.App/LinuxUpdateManager.App.csproj"), "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>\n");
    writeFileSync(path.join(repository, "CHANGELOG.md"), "# Changelog\n\n## [Unreleased]\n\n## [1.0.0] - 2026-11-02\n\n### Added\n\n- Initial release.\n");

    git("init");
    git("config", "user.name", "Release test");
    git("config", "user.email", "release-test@example.invalid");
    git("add", "-A");
    git("commit", "-m", "chore(release): prepare v1.0.0");
    git("tag", "v1.0.0");
    writeFileSync(path.join(repository, "fix.txt"), "fix\n");
    git("add", "fix.txt");
    git("commit", "-m", "fix: parse signed release manifests");

    const outputFile = path.join(repository, "github-output.txt");
    execFileSync(process.execPath, [path.join(repository, ".github/scripts/prepare-release.mjs")], {
      cwd: repository,
      env: { ...process.env, GITHUB_OUTPUT: outputFile, GITHUB_TOKEN: "test-token", UPDATE_SIGNING_PRIVATE_KEY: "test-key" },
      stdio: "ignore"
    });

    assert.match(readFileSync(outputFile, "utf8"), /release=true\nversion=1\.0\.1\n/);
    assert.match(readFileSync(path.join(repository, "src/LinuxUpdateManager.App/LinuxUpdateManager.App.csproj"), "utf8"), /<Version>1\.0\.1<\/Version>/);
    assert.match(readFileSync(path.join(repository, "CHANGELOG.md"), "utf8"), /## \[1\.0\.1\] - \d{4}-\d{2}-\d{2}/);
  } finally {
    rmSync(repository, { recursive: true, force: true });
  }
});
