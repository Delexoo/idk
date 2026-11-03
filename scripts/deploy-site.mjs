import fs from "node:fs";
import path from "node:path";
import { spawn } from "node:child_process";

const root = path.resolve(import.meta.dirname, "..");
const env = readEnv(path.join(root, ".env"));
const token = env.VERCEL_TOKEN || "";
if (!token) throw new Error("VERCEL_TOKEN is missing from .env.");
const teamId = "team_YBLusghOsaliGBVAnInBdjSr";
const teamSlug = "trymoonrises-projects";
const projectName = "idk";

const project = await ensureProject();
await ensureStripeEnv(project.id);
writeLink(project.id);

const url = await deploy(project.id);
console.log(url);

async function ensureProject() {
  const listed = await vercel(`/v9/projects/${projectName}?teamId=${teamId}`);
  if (listed.ok) return listed.json();
  if (listed.status !== 404) throw new Error(`Could not read the Vercel project: ${listed.status}`);
  const created = await vercel(`/v10/projects?teamId=${teamId}`, {
    method: "POST",
    body: JSON.stringify({ name: projectName, framework: null }),
  });
  if (!created.ok) throw new Error(`Could not create the Vercel project: ${created.status}`);
  return created.json();
}

async function ensureStripeEnv(projectId) {
  const secret = env.STRIPE_SECRET_KEY || "";
  if (!secret) throw new Error("STRIPE_SECRET_KEY is missing from .env.");
  const current = await vercel(`/v9/projects/${projectId}/env?teamId=${teamId}`);
  if (!current.ok) throw new Error(`Could not read Vercel env: ${current.status}`);
  const body = await current.json();
  const envs = Array.isArray(body.envs) ? body.envs : [];
  if (envs.some((item) => item.key === "STRIPE_SECRET_KEY")) return;
  const created = await vercel(`/v10/projects/${projectId}/env?teamId=${teamId}`, {
    method: "POST",
    body: JSON.stringify({
      key: "STRIPE_SECRET_KEY",
      value: secret,
      type: "encrypted",
      target: ["production", "preview"],
    }),
  });
  if (!created.ok) throw new Error(`Could not save the billing key on Vercel: ${created.status}`);
}

function writeLink(projectId) {
  const dir = path.join(root, "website", ".vercel");
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(
    path.join(dir, "project.json"),
    `${JSON.stringify({ orgId: teamId, projectId, projectName }, null, 2)}\n`,
  );
}

function deploy(projectId) {
  return new Promise((resolve, reject) => {
    const child = spawn(
      "cmd.exe",
      ["/d", "/s", "/c", "npx --yes vercel@latest deploy --prod --yes --scope " + teamSlug],
      {
        cwd: path.join(root, "website"),
        env: {
          ...process.env,
          VERCEL_TOKEN: token,
          VERCEL_ORG_ID: teamId,
          VERCEL_PROJECT_ID: projectId,
        },
        windowsHide: true,
      },
    );
    let output = "";
    const keep = (chunk) => {
      output += chunk.toString();
    };
    child.stdout.on("data", keep);
    child.stderr.on("data", keep);
    child.on("error", reject);
    child.on("close", (code) => {
      const urls = [...output.matchAll(/https:\/\/[^\s"']+/g)].map((match) => match[0].replace(/[),."']+$/, ""));
      const production = urls.find((item) => item.includes("idk-trymoonrises-projects.vercel.app"))
        || urls.find((item) => item.includes(".vercel.app") && !item.includes("files.vercel"))
        || urls.at(-1);
      if (code !== 0 || !production) {
        const cleaned = output.replace(/vcp_[A-Za-z0-9]+/g, "[redacted]").slice(-800);
        reject(new Error(cleaned || `Vercel deploy exited ${code}.`));
        return;
      }
      resolve(production);
    });
  });
}

async function vercel(pathname, options = {}) {
  return fetch(`https://api.vercel.com${pathname}`, {
    ...options,
    headers: {
      authorization: `Bearer ${token}`,
      "content-type": "application/json",
    },
  });
}

function readEnv(file) {
  const map = {};
  if (!fs.existsSync(file)) return map;
  for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const index = trimmed.indexOf("=");
    if (index < 1) continue;
    const key = trimmed.slice(0, index).trim();
    let value = trimmed.slice(index + 1).trim();
    if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) {
      value = value.slice(1, -1);
    }
    map[key] = value;
  }
  return map;
}
