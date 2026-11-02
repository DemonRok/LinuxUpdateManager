import { execFileSync } from "node:child_process";
import { createReadStream, statSync } from "node:fs";
import https from "node:https";
import path from "node:path";

const [version, notesPath, assetDirectory] = process.argv.slice(2);
if (!/^\d+\.\d+\.\d+$/.test(version || "") || !notesPath || !assetDirectory) {
  throw new Error("Usage: node publish-release.mjs <version> <notes-file> <asset-directory>");
}

const repository = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;
if (repository !== "DemonRok/LinuxUpdateManager" || !token) {
  throw new Error("The release job is not authenticated for the expected GitHub repository.");
}

const tag = `v${version}`;
const apiRoot = `https://api.github.com/repos/${repository}`;
const assetFiles = [
  "LinuxUpdateManager-win-x64.zip",
  "LinuxUpdateManager-win-x64.zip.sha256",
  "update-manifest.json",
  "update-manifest.sig"
].map(name => ({ name, path: path.join(assetDirectory, name) }));

function git(...args) {
  return execFileSync("git", args, { encoding: "utf8" }).trim();
}

function githubHeaders() {
  return {
    Accept: "application/vnd.github+json",
    Authorization: `Bearer ${token}`,
    "X-GitHub-Api-Version": "2022-11-28",
    "User-Agent": "LinuxUpdateManager-release"
  };
}

async function getRelease() {
  const response = await fetch(`${apiRoot}/releases/tags/${encodeURIComponent(tag)}`, { headers: githubHeaders() });
  if (response.status === 404) {
    return null;
  }

  if (!response.ok) {
    throw new Error(`GitHub release lookup failed with HTTP ${response.status}.`);
  }

  return response.json();
}

async function createRelease(notes) {
  const response = await fetch(`${apiRoot}/releases`, {
    method: "POST",
    headers: { ...githubHeaders(), "Content-Type": "application/json" },
    body: JSON.stringify({
      tag_name: tag,
      target_commitish: "main",
      name: `Linux Update Manager ${version}`,
      body: notes,
      draft: false,
      prerelease: false
    })
  });

  if (response.ok) {
    return response.json();
  }

  if (response.status === 422) {
    const concurrentRelease = await getRelease();
    if (concurrentRelease) {
      return concurrentRelease;
    }
  }

  throw new Error(`GitHub could not create release ${tag} (HTTP ${response.status}).`);
}

async function uploadAsset(uploadUrl, asset) {
  const url = new URL(uploadUrl.replace(/\{\?.*\}$/, ""));
  url.searchParams.set("name", asset.name);
  const fileSize = statSync(asset.path).size;
  await new Promise((resolve, reject) => {
    const request = https.request(url, {
      method: "POST",
      headers: {
        ...githubHeaders(),
        "Content-Type": asset.name.endsWith(".zip") ? "application/zip" : "application/octet-stream",
        "Content-Length": fileSize
      }
    }, response => {
      let responseText = "";
      response.setEncoding("utf8");
      response.on("data", chunk => { responseText += chunk; });
      response.on("end", () => {
        if (response.statusCode >= 200 && response.statusCode < 300) {
          resolve();
          return;
        }

        reject(new Error(`GitHub could not upload ${asset.name} (HTTP ${response.statusCode}): ${responseText.slice(0, 500)}`));
      });
    });
    request.on("error", reject);
    createReadStream(asset.path).on("error", reject).pipe(request);
  });
}

function commitVersionChange() {
  git("config", "user.name", "github-actions[bot]");
  git("config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com");
  git("add", "src/LinuxUpdateManager.App/LinuxUpdateManager.App.csproj", "CHANGELOG.md");
  const staged = git("diff", "--cached", "--name-only");
  if (staged) {
    git("commit", "-m", `chore(release): prepare v${version}`);
  }

  git("push", "origin", "HEAD:main");

  if (!git("tag", "--list", tag)) {
    git("tag", "-a", tag, "-m", `Linux Update Manager ${version}`);
    git("push", "origin", `refs/tags/${tag}`);
  }
}

for (const asset of assetFiles) {
  if (!statSync(asset.path, { throwIfNoEntry: false })) {
    throw new Error(`Release asset '${asset.name}' was not created.`);
  }
}

commitVersionChange();
let release = await getRelease();
if (!release) {
  const notes = (await import("node:fs/promises")).readFile(notesPath, "utf8");
  release = await createRelease(await notes);
}

const existingAssets = new Set(release.assets.map(asset => asset.name));
for (const asset of assetFiles) {
  if (existingAssets.has(asset.name)) {
    console.log(`Release asset already exists: ${asset.name}`);
    continue;
  }

  await uploadAsset(release.upload_url, asset);
  console.log(`Uploaded release asset: ${asset.name}`);
}

console.log(`Published https://github.com/${repository}/releases/tag/${tag}`);
