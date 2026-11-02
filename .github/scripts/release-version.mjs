export function parseVersion(value) {
  const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(value);
  return match ? match.slice(1).map(Number) : null;
}

export function determineReleaseLevel(messages) {
  let level = null;
  for (const message of messages) {
    const [subject = "", ...bodyLines] = message.trim().split(/\r?\n/);
    if (/^BREAKING CHANGE:\s*\S/im.test(bodyLines.join("\n")) ||
        /^[a-z][a-z0-9-]*(?:\([^\n)]*\))?!:\s*\S/i.test(subject)) {
      return "major";
    }

    const conventional = /^([a-z][a-z0-9-]*)(?:\([^\n)]*\))?:\s*\S/i.exec(subject);
    if (!conventional) {
      continue;
    }

    const type = conventional[1].toLowerCase();
    if (type === "feat") {
      level = "minor";
    } else if (type === "fix" && level === null) {
      level = "patch";
    }
  }

  return level;
}

export function bumpVersion(version, level) {
  const parsed = parseVersion(version);
  if (!parsed || !level) {
    return null;
  }

  let [major, minor, patch] = parsed;
  if (level === "major") {
    return `${major + 1}.0.0`;
  }

  if (level === "minor") {
    if (minor === 9) {
      major += 1;
      minor = 0;
    } else {
      minor += 1;
    }

    return `${major}.${minor}.0`;
  }

  if (level === "patch") {
    return `${major}.${minor}.${patch + 1}`;
  }

  return null;
}

export function calculateNextVersion(version, messages) {
  return bumpVersion(version, determineReleaseLevel(messages));
}
