import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";

const root = process.cwd();
const tracked = execFileSync("git", ["ls-files", "-z"], { cwd: root, encoding: "utf8" })
  .split("\0")
  .filter(Boolean);
const blockedName = /(^|\/)(?:config\.json(?:\..*)?|servers\.json(?:\..*)?|history\.db|.*\.(?:sqlite|db|log|lum|pfx|p12|pem|key)|\.env(?:\..*)?|id_(?:rsa|ed25519)|credentials?\.json)$/i;
const ipv4Pattern = /\b(?:\d{1,3}\.){3}\d{1,3}\b/g;
const internalHostPattern = /\b(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?:local|internal|lan|corp|intranet|home)\b/gi;
const privateKeyPattern = /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/;
const tokenPatterns = [
  /\bgh[pousr]_[A-Za-z0-9]{20,}\b/,
  /\bgithub_pat_[A-Za-z0-9_]{20,}\b/,
  /\bAKIA[0-9A-Z]{16}\b/
];
const findings = [];

function isDocumentationAddress(value) {
  const octets = value.split(".").map(Number);
  return value === "127.0.0.1" ||
    octets[0] === 192 && octets[1] === 0 && octets[2] === 2 ||
    octets[0] === 198 && octets[1] === 51 && octets[2] === 100 ||
    octets[0] === 203 && octets[1] === 0 && octets[2] === 113;
}

for (const relativePath of tracked) {
  const normalizedPath = relativePath.split(path.sep).join("/");
  if (blockedName.test(normalizedPath)) {
    findings.push({ path: normalizedPath, reason: "runtime or secret file name" });
    continue;
  }

  const bytes = readFileSync(path.join(root, relativePath));
  if (bytes.includes(0)) {
    continue;
  }

  const content = bytes.toString("utf8");
  const contentWithoutAssemblyVersions = content.replace(/\b(?:AssemblyVersion|Version)\s*=\s*\d+\.\d+\.\d+\.\d+\b/gi, "");
  if (privateKeyPattern.test(content) || tokenPatterns.some(pattern => pattern.test(content))) {
    findings.push({ path: normalizedPath, reason: "private key or credential pattern" });
  }

  for (const address of contentWithoutAssemblyVersions.match(ipv4Pattern) || []) {
    if (!isDocumentationAddress(address)) {
      findings.push({ path: normalizedPath, reason: "non-documentation IPv4 address" });
      break;
    }
  }

  if (internalHostPattern.test(contentWithoutAssemblyVersions)) {
    findings.push({ path: normalizedPath, reason: "internal hostname suffix" });
    internalHostPattern.lastIndex = 0;
  }
}

if (findings.length > 0) {
  for (const finding of findings) {
    console.error(`Repository validation failed in ${finding.path}: ${finding.reason}.`);
  }

  process.exitCode = 1;
} else {
  console.log(`Validated ${tracked.length} tracked files for runtime data and common infrastructure secrets.`);
}
