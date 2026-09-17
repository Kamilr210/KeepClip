"use strict";
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..", "frontend");
global.window = {};
const i18n = require(path.join(root, "i18n.js"));

function flatten(obj, prefix = "", out = {}) {
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === "object") flatten(v, key, out);
    else out[key] = v;
  }
  return out;
}

const errors = [];
const warnings = [];
const langs = Object.keys(i18n);
const flat = Object.fromEntries(langs.map((l) => [l, flatten(i18n[l])]));
const allKeys = new Set(langs.flatMap((l) => Object.keys(flat[l])));

for (const lang of langs) {
  const missing = [...allKeys].filter((k) => !(k in flat[lang]));
  if (missing.length) errors.push(`${lang}: brakuje ${missing.length} kluczy: ${missing.join(", ")}`);
  for (const [k, v] of Object.entries(flat[lang]))
    if (typeof v !== "string") errors.push(`${lang}: ${k} nie jest tekstem`);
}

for (const key of allKeys) {
  const slots = langs
    .filter((l) => typeof flat[l][key] === "string")
    .map((l) => [...flat[l][key].matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(","));
  if (new Set(slots).size > 1) warnings.push(`${key}: różne symbole {x} między językami`);
}

const patterns = [
  /\bt\(\s*["'`]([\w.\-]+)["'`]\s*\)/g,
  /data-i18n(?:-html|-title|-aria|-placeholder)?="([\w.\-]+)"/g,
  /setAttr\([^,]+,\s*'[^']+',\s*'([\w.\-]+)'\)/g,
];
const used = new Set();
for (const file of ["app.js", "index.html"]) {
  const src = fs.readFileSync(path.join(root, file), "utf8");
  for (const re of patterns) for (const m of src.matchAll(re)) used.add(m[1]);
}
const onboarding = fs.readFileSync(path.join(root, "onboarding.js"), "utf8");
for (const m of onboarding.matchAll(/\btxt\(\s*"([\w.\-]+)"/g)) used.add("onboarding." + m[1]);
for (const m of onboarding.matchAll(/\{\s*id:\s*"([\w\-]+)"/g)) {
  const id = m[1];
  if (id.startsWith("hint:")) continue;
  for (const part of ["title", "body"]) used.add(`onboarding.steps.${id}.${part}`);
}

const unknown = [...used].filter((k) => !(k in flat.pl) && !k.endsWith("."));
if (unknown.length) errors.push(`klucze użyte w kodzie bez tłumaczenia PL: ${unknown.join(", ")}`);

console.log(`Języki: ${langs.join(", ")}; kluczy: ${allKeys.size}; użytych w kodzie: ${used.size}`);
for (const w of warnings) console.log(`ostrzeżenie: ${w}`);
for (const e of errors) console.error(`BŁĄD: ${e}`);
if (errors.length) process.exit(1);
console.log("Tłumaczenia OK.");
