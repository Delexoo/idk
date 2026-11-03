import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const log = JSON.parse(fs.readFileSync(path.join(root, "brain", "log.json"), "utf8"));
const repo = String(log.repo || "");
if (!/^[\w.-]+\/[\w.-]+$/.test(repo)) throw new Error("brain/log.json needs a repo owner/name.");

const seen = new Set();
for (const update of log.updates) {
  if (seen.has(update.version)) throw new Error(`Version ${update.version} is listed twice.`);
  seen.add(update.version);
  checkUpdate(update);
}

const releases = {
  repo,
  updates: log.updates.map((update) => ({
    version: update.version,
    date: update.date,
    title: update.title,
    tag: `v${update.version}`,
    url: `https://github.com/${repo}/releases/tag/v${update.version}`,
    sections: update.sections,
  })),
};

fs.writeFileSync(path.join(root, "website", "releases.json"), `${JSON.stringify(releases, null, 2)}\n`);

const page = path.join(root, "website", "releases.html");
const html = fs.readFileSync(page, "utf8");
const start = "<!-- brain:notes -->";
const end = "<!-- /brain:notes -->";
if (!html.includes(start) || !html.includes(end)) {
  throw new Error("website/releases.html is missing the brain note markers.");
}
const notes = releases.updates.map((update, index) => renderUpdate(update, index)).join("\n");
const next = html.replace(
  new RegExp(`${escapeRegExp(start)}[\\s\\S]*${escapeRegExp(end)}`),
  `${start}\n${notes}\n      ${end}`,
);
fs.writeFileSync(page, next);
console.log(`Release notes rendered for ${releases.updates.map((update) => update.version).join(", ")}.`);

function checkUpdate(update) {
  if (!Array.isArray(update.sections) || update.sections.length === 0) {
    throw new Error(`${update.version} needs sections.`);
  }
  let count = 0;
  for (const section of update.sections) {
    if (section.notes) throw new Error(`${update.version} ${section.heading} uses notes. Use added and fixes.`);
    for (const key of ["added", "fixes"]) {
      const notes = section[key];
      if (notes == null) continue;
      if (!Array.isArray(notes) || notes.some((note) => typeof note !== "string" || !note.trim())) {
        throw new Error(`${update.version} ${section.heading} has a bad ${key} list.`);
      }
      count += notes.length;
    }
    if (!section.added?.length && !section.fixes?.length) {
      throw new Error(`${update.version} ${section.heading} has no added or fixes.`);
    }
  }
  if (count === 0) throw new Error(`${update.version} has no notes.`);
}

function renderUpdate(update, index) {
  const latest = index === 0 ? ` <span class="release-latest">Latest</span>` : "";
  const sections = ["added", "fixes"]
    .map((key) => renderGroup(update.sections, key === "added" ? "Added" : "Bug fixes", key))
    .filter(Boolean)
    .join("\n");
  return `      <article class="release" id="v${escapeHtml(update.version)}">
        <header class="release-meta">
          <a class="release-tag" href="${escapeHtml(update.url)}">${escapeHtml(update.tag)}</a>${latest}
          <time datetime="${escapeHtml(update.date)}">${escapeHtml(formatDate(update.date))}</time>
        </header>
        <h2>${escapeHtml(update.title)}</h2>
${sections}
      </article>`;
}

function renderGroup(sections, label, key) {
  const blocks = sections
    .filter((section) => section[key]?.length)
    .map((section) => `        <h4>${escapeHtml(section.heading)}</h4>\n${list(section[key])}`);
  if (!blocks.length) return "";
  return `        <h3>${label}</h3>\n${blocks.join("\n")}`;
}

function list(notes) {
  return `        <ul>\n${notes.map((note) => `          <li>${escapeHtml(note)}</li>`).join("\n")}\n        </ul>`;
}

function formatDate(iso) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!match) throw new Error(`Bad release date: ${iso}`);
  return new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])))
    .toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
