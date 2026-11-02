import { createHash, createPrivateKey, createPublicKey, sign } from "node:crypto";
import { createReadStream, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { once } from "node:events";

const [packagePath, assetDirectory, version] = process.argv.slice(2);
if (!packagePath || !assetDirectory || !/^\d+\.\d+\.\d+$/.test(version || "")) {
  throw new Error("Usage: node sign-release.mjs <package.zip> <asset-directory> <version>");
}

const privatePem = process.env.UPDATE_SIGNING_PRIVATE_KEY?.replace(/\\n/g, "\n");
if (!privatePem?.trim()) {
  throw new Error("UPDATE_SIGNING_PRIVATE_KEY is required to sign update metadata.");
}

const publicKeySource = readFileSync("src/LinuxUpdateManager.Core/Updates/UpdateSigningPublicKey.cs", "utf8");
const publicKeyMatch = /internal const string Pem = """\r?\n([\s\S]*?)\r?\n\s*""";/.exec(publicKeySource);
if (!publicKeyMatch) {
  throw new Error("The pinned update public key could not be read.");
}

const publicPem = `${publicKeyMatch[1].split(/\r?\n/).map(line => line.replace(/^ {8}/, "")).join("\n")}\n`;
const privateKey = createPrivateKey(privatePem);
const pinnedPublicKey = createPublicKey(publicPem);
const derivedPublicKey = createPublicKey(privateKey);
const pinnedDer = pinnedPublicKey.export({ type: "spki", format: "der" });
const derivedDer = derivedPublicKey.export({ type: "spki", format: "der" });
if (!pinnedDer.equals(derivedDer)) {
  throw new Error("The configured update signing key does not match the application's pinned public key.");
}

const hash = createHash("sha256");
const packageStream = createReadStream(packagePath);
packageStream.on("data", chunk => hash.update(chunk));
await once(packageStream, "end");
const sha256 = hash.digest("hex");
const manifest = {
  version,
  tagName: `v${version}`,
  assetName: path.basename(packagePath),
  sha256
};
const manifestBytes = Buffer.from(`${JSON.stringify(manifest, null, 2)}\n`, "utf8");
const signature = sign("RSA-SHA256", manifestBytes, privateKey).toString("base64");

mkdirSync(assetDirectory, { recursive: true });
writeFileSync(path.join(assetDirectory, "update-manifest.json"), manifestBytes, { flag: "wx" });
writeFileSync(path.join(assetDirectory, "update-manifest.sig"), `${signature}\n`, { flag: "wx" });
writeFileSync(path.join(assetDirectory, `${path.basename(packagePath)}.sha256`), `${sha256}  ${path.basename(packagePath)}\n`, { flag: "wx" });
console.log(`Created signed manifest and SHA-256 checksum for ${path.basename(packagePath)}.`);
