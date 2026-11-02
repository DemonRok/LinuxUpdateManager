import { execFileSync } from "node:child_process";
import { appendFileSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { calculateNextVersion, parseVersion } from "./release-version.mjs";

const root = process.cwd();
const projectFile = path.join(root, "src/LinuxUpdateManager.App/LinuxUpdateManager.App.csproj");
const changelogFile = path.join(root, "CHANGELOG.md");
const outputFile = process.env.GITHUB_OUTPUT;
const summaryFile = process.env.GITHUB_STEP_SUMMARY;

function git(...args) {
  return execFileSync("git", args, { cwd: root, encoding: "utf8" }).trim();
}

function setOutputs(values) {
  if (outputFile) {
    appendFileSync(outputFile, Object.entries(values).map(([key, value]) => `${key}=${value}`).join("\n") + "\n");
  }
}

function note(message) {
  console.log(`::notice::${message}`);
  if (summaryFile) {
    appendFileSync(summaryFile, `${message}\n\n`);
  }
}

function readProjectVersion() {
  const source = readFileSync(projectFile, "utf8");
  const match = /<Version>([^<]+)<\/Version>/.exec(source);
  if (!match || !parseVersion(match[1])) {
    throw new Error("The application project must contain a valid MAJOR.MINOR.PATCH version.");
  }

  return { source, version: match[1] };
}

function readMessages(reference) {
  const output = git("log", "--format=%B%x1e", `${reference}..HEAD`);
  return output.split("\x1e").map(message => message.trim()).filter(Boolean);
}

function listVersionTags() {
  return git("tag", "--list", "v[0-9]*")
    .split(/\r?\n/)
    .filter(tag => /^v\d+\.\d+\.\d+$/.test(tag))
    .map(tag => ({ tag, version: tag.slice(1), parsed: parseVersion(tag.slice(1)) }))
    .sort((left, right) => compareVersions(right.parsed, left.parsed));
}

function compareVersions(left, right) {
  for (let index = 0; index < 3; index += 1) {
    if (left[index] !== right[index]) {
      return left[index] - right[index];
    }
  }

  return 0;
}

function unreleasedNotes(changelog) {
  const start = changelog.indexOf("## [Unreleased]");
  if (start < 0) {
    return "";
  }

  const contentStart = changelog.indexOf("\n", start) + 1;
  const nextHeading = changelog.indexOf("\n## [", contentStart);
  return changelog.slice(contentStart, nextHeading < 0 ? undefined : nextHeading).trim();
}

function versionNotes(changelog, version) {
  const marker = `## [${version}]`;
  const start = changelog.indexOf(marker);
  if (start < 0) {
    return "";
  }

  const contentStart = changelog.indexOf("\n", start) + 1;
  const nextHeading = changelog.indexOf("\n## [", contentStart);
  return changelog.slice(contentStart, nextHeading < 0 ? undefined : nextHeading).trim();
}

function notesFromCommits(messages) {
  const sections = new Map([["Breaking Changes", []], ["Added", []], ["Fixed", []]]);
  for (const message of messages) {
    const [subject = ""] = message.split(/\r?\n/);
    const match = /^([a-z][a-z0-9-]*)(?:\([^)]*\))?!?:\s*(.+)$/i.exec(subject);
    if (!match) {
      continue;
    }

    const type = match[1].toLowerCase();
    const section = /!:/i.test(subject) ? "Breaking Changes" : type === "feat" ? "Added" : type === "fix" ? "Fixed" : null;
    if (section) {
      sections.get(section).push(`- ${match[2]}`);
    }
  }

  return [...sections]
    .filter(([, entries]) => entries.length > 0)
    .map(([heading, entries]) => `### ${heading}\n\n${entries.join("\n")}`)
    .join("\n\n");
}

function replaceUnreleasedSection(changelog, version, date, notes) {
  const start = changelog.indexOf("## [Unreleased]");
  if (start < 0) {
    throw new Error("CHANGELOG.md is missing its Unreleased section.");
  }

  const contentStart = changelog.indexOf("\n", start) + 1;
  const nextHeading = changelog.indexOf("\n## [", contentStart);
  const suffix = nextHeading < 0 ? "" : changelog.slice(nextHeading + 1).trimStart();
  const prefix = changelog.slice(0, contentStart);
  const releaseSection = `## [${version}] - ${date}\n\n${notes.trim()}\n\n`;
  return `${prefix}\n${releaseSection}${suffix}`.trimEnd() + "\n";
}

async function existingRelease(tag, token) {
  const response = await fetch(`https://api.github.com/repos/DemonRok/LinuxUpdateManager/releases/tags/${encodeURIComponent(tag)}`, {
    headers: {
      Accept: "application/vnd.github+json",
      Authorization: `Bearer ${token}`,
      "X-GitHub-Api-Version": "2022-11-28",
      "User-Agent": "LinuxUpdateManager-release"
    }
  });
  if (response.status === 404) {
    return null;
  }

  if (!response.ok) {
    throw new Error(`GitHub release lookup failed with HTTP ${response.status}.`);
  }

  return response.json();
}

async function main() {
  const readiness = JSON.parse(readFileSync(path.join(root, ".github/release-readiness.json"), "utf8"));
  if (readiness.phase7Complete !== true || readiness.phase8Complete !== true) {
    note("Automated release skipped: Phase 7 and Phase 8 are not both marked complete.");
    setOutputs({ release: "false" });
    return;
  }

  if (!process.env.UPDATE_SIGNING_PRIVATE_KEY?.trim()) {
    note("Automated release skipped: configure the UPDATE_SIGNING_PRIVATE_KEY Actions secret before publishing signed updates.");
    setOutputs({ release: "false" });
    return;
  }

  const token = process.env.GITHUB_TOKEN;
  if (!token) {
    throw new Error("GITHUB_TOKEN is unavailable to the release job.");
  }

  const { source: projectSource, version: currentVersion } = readProjectVersion();
  const changelog = readFileSync(changelogFile, "utf8");
  const tags = listVersionTags();
  const headSubject = git("log", "-1", "--format=%s");
  const pendingRelease = headSubject === `chore(release): prepare v${currentVersion}`;
  let releaseVersion;
  let releaseNotes;
  let shouldPrepareFiles = false;

  if (pendingRelease) {
    releaseVersion = currentVersion;
    releaseNotes = versionNotes(changelog, releaseVersion);
    if (!releaseNotes) {
      throw new Error("The pending release has no matching CHANGELOG.md section.");
    }
  } else if (tags.length > 0 && tags[0].version === currentVersion) {
    const release = await existingRelease(tags[0].tag, token);
    const expectedAssets = new Set([
      "LinuxUpdateManager-win-x64.zip",
      "LinuxUpdateManager-win-x64.zip.sha256",
      "update-manifest.json",
      "update-manifest.sig"
    ]);
    if (release && [...expectedAssets].every(name => release.assets.some(asset => asset.name === name))) {
      note(`Release ${tags[0].tag} already exists with all expected assets; no duplicate was created.`);
      setOutputs({ release: "false" });
      return;
    }

    releaseVersion = tags[0].version;
    releaseNotes = versionNotes(changelog, releaseVersion);
    if (!releaseNotes) {
      throw new Error(`CHANGELOG.md is missing the ${releaseVersion} release section.`);
    }
  } else {
    let baseReference;
    if (tags.length > 0) {
      if (compareVersions(parseVersion(currentVersion), tags[0].parsed) !== 0) {
        throw new Error(`The project version ${currentVersion} does not match the latest release tag ${tags[0].tag}.`);
      }

      baseReference = tags[0].tag;
    } else {
      const baselineVersion = readFileSync(path.join(root, ".github/release-baseline-version.txt"), "utf8").trim();
      if (currentVersion !== baselineVersion) {
        throw new Error("The project version differs from the configured no-tag release baseline.");
      }

      baseReference = readFileSync(path.join(root, ".github/release-baseline.txt"), "utf8").trim();
      if (!/^[0-9a-f]{40}$/i.test(baseReference)) {
        throw new Error("The no-tag release baseline is not a full commit SHA.");
      }

      git("cat-file", "-e", `${baseReference}^{commit}`);
    }

    const messages = readMessages(baseReference);
    releaseVersion = calculateNextVersion(currentVersion, messages);
    if (!releaseVersion) {
      note("Automated release skipped: commits since the previous release contain no feat:, fix:, or breaking change.");
      setOutputs({ release: "false" });
      return;
    }

    releaseNotes = unreleasedNotes(changelog) || notesFromCommits(messages);
    if (!releaseNotes) {
      throw new Error("No release notes could be assembled from CHANGELOG.md or Conventional Commits.");
    }

    const nextProject = projectSource.replace(/<Version>[^<]+<\/Version>/, `<Version>${releaseVersion}</Version>`);
    writeFileSync(projectFile, nextProject, "utf8");
    writeFileSync(changelogFile, replaceUnreleasedSection(
      changelog,
      releaseVersion,
      new Date().toISOString().slice(0, 10),
      releaseNotes), "utf8");
    shouldPrepareFiles = true;
  }

  const notesPath = path.join(process.env.RUNNER_TEMP || root, "LinuxUpdateManager-release-notes.md");
  writeFileSync(notesPath, `${releaseNotes.trim()}\n`, "utf8");
  note(`Preparing signed release v${releaseVersion}${shouldPrepareFiles ? " from eligible Conventional Commits" : " for retry"}.`);
  setOutputs({ release: "true", version: releaseVersion, notes_path: notesPath });
}

main().catch(error => {
  console.error(error.message);
  process.exitCode = 1;
});
