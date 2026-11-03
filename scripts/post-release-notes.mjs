import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const log = JSON.parse(fs.readFileSync(path.join(root, "brain", "log.json"), "utf8"));
const repo = String(log.repo || "");
if (!/^[\w.-]+\/[\w.-]+$/.test(repo)) throw new Error("brain/log.json needs a repo owner/name.");
const token = process.env.GH_TOKEN || process.env.GITHUB_TOKEN || "";
if (!token) throw new Error("Set GITHUB_TOKEN before posting release notes.");

let changed = 0;
for (const update of log.updates) {
  const tag = `v${update.version}`;
  const name = `${update.version} · ${update.title}`;
  const body = releaseBody(update);
  const existing = await findRelease(tag);
  if (!existing) {
    const response = await fetch(`https://api.github.com/repos/${repo}/releases`, {
      method: "POST",
      headers: githubHeaders(),
      body: JSON.stringify({
        tag_name: tag,
        target_commitish: update.target || "main",
        name,
        body,
      }),
    });
    if (!response.ok) throw new Error(`Could not post ${tag}: ${response.status} ${await safeText(response)}`);
    changed += 1;
    console.log(`Posted ${tag}.`);
    continue;
  }
  if (existing.name === name && existing.body.trim() === body.trim()) {
    console.log(`${tag} already matches the log.`);
    continue;
  }
  const response = await fetch(`https://api.github.com/repos/${repo}/releases/${existing.id}`, {
    method: "PATCH",
    headers: githubHeaders(),
    body: JSON.stringify({ name, body }),
  });
  if (!response.ok) throw new Error(`Could not update ${tag}: ${response.status} ${await safeText(response)}`);
  changed += 1;
  console.log(`Updated ${tag}.`);
}
if (changed === 0) console.log("GitHub releases already match the log.");

function releaseBody(update) {
  return [["Added", "added"], ["Bug fixes", "fixes"]]
    .flatMap(([label, key]) => {
      const blocks = update.sections
        .filter((section) => section[key]?.length)
        .flatMap((section) => [`### ${section.heading}`, ...section[key].map((note) => `- ${note}`), ""]);
      return blocks.length ? [`## ${label}`, ...blocks] : [];
    })
    .join("\n")
    .trim();
}

async function findRelease(tag) {
  const response = await fetch(`https://api.github.com/repos/${repo}/releases/tags/${encodeURIComponent(tag)}`, {
    headers: githubHeaders(),
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`Could not check ${tag}: ${response.status} ${await safeText(response)}`);
  return response.json();
}

function githubHeaders() {
  return {
    authorization: `Bearer ${token}`,
    accept: "application/vnd.github+json",
    "user-agent": "idk-release-notes",
    "x-github-api-version": "2022-11-28",
  };
}

async function safeText(response) {
  const text = await response.text();
  return text.replace(/ghp_[A-Za-z0-9]+/g, "[redacted]").replace(/github_pat_[A-Za-z0-9_]+/g, "[redacted]").slice(0, 300);
}
