// Generates a VPM repository listing (index.json) + landing page into ./_site
// from every Packages/*/package.json, referencing the release zips built in ./dist.
import { readdirSync, readFileSync, writeFileSync, mkdirSync, existsSync, cpSync } from "node:fs";
import { createHash } from "node:crypto";
import { join } from "node:path";

const REPO = process.env.REPO;                 // "owner/repo"
const LISTING_URL = process.env.LISTING_URL;   // "https://owner.github.io/repo"
if (!REPO || !LISTING_URL) {
  console.error("REPO and LISTING_URL env vars are required");
  process.exit(1);
}

const LISTING = {
  name: "Bluscream's Unity Editor Scripts",
  id: "dev.bluscream.vpm",
  author: "Bluscream",
  url: `${LISTING_URL}/index.json`,
};

const pkgsDir = "Packages";
const packages = {};

for (const dir of readdirSync(pkgsDir, { withFileTypes: true })) {
  if (!dir.isDirectory()) continue;
  const pjPath = join(pkgsDir, dir.name, "package.json");
  if (!existsSync(pjPath)) continue;

  const meta = JSON.parse(readFileSync(pjPath, "utf8"));
  const { name, version } = meta;
  const tag = `${name}-${version}`;
  const zipPath = join("dist", `${tag}.zip`);
  if (!existsSync(zipPath)) {
    console.warn(`! missing zip for ${tag}, skipping`);
    continue;
  }
  const sha = createHash("sha256").update(readFileSync(zipPath)).digest("hex");

  meta.url = `https://github.com/${REPO}/releases/download/${tag}/${tag}.zip`;
  meta.zipSHA256 = sha;

  packages[name] ??= { versions: {} };
  packages[name].versions[version] = meta;
  console.log(`+ ${name}@${version} (${sha.slice(0, 12)}…)`);
}

const listing = { ...LISTING, packages };

mkdirSync("_site", { recursive: true });
writeFileSync("_site/index.json", JSON.stringify(listing, null, 2));

// Landing page: copy template and inject the listing URL.
let html = readFileSync(join("Website", "index.html"), "utf8");
html = html.replaceAll("{{LISTING_URL}}", `${LISTING_URL}/index.json`);
writeFileSync("_site/index.html", html);

console.log(`\nWrote _site/index.json with ${Object.keys(packages).length} packages.`);
