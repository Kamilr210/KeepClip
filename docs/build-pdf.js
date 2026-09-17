"use strict";
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const srcDir = path.join(__dirname, "src");
const outDir = __dirname;
const edge = [
  "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
  "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
  "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
].find((p) => fs.existsSync(p));

if (!edge) {
  console.error("Nie znaleziono przeglądarki Edge ani Chrome. Zainstaluj Edge albo wskaż ścieżkę w build-pdf.js.");
  process.exit(1);
}

function injectToc(html) {
  let n2 = 0, n3 = 0;
  const items = [];
  html = html.replace(/<(h2|h3)([^>]*)>([\s\S]*?)<\/\1>/g, (m, tag, attrs, inner) => {
    if (tag === "h2") { n2++; n3 = 0; } else { n3++; }
    const num = tag === "h2" ? `${n2}.` : `${n2}.${n3}`;
    const id = `s-${n2}-${n3}`;
    items.push({ tag, num, id, text: inner.replace(/<[^>]+>/g, "") });
    return `<${tag} id="${id}"${attrs}><span class="num">${num}</span> ${inner}</${tag}>`;
  });
  const list = items
    .map((i) => `<li class="${i.tag}"><a href="#${i.id}"><span class="num">${i.num}</span> ${i.text}</a></li>`)
    .join("");
  return html.replace('<nav id="toc"></nav>', `<nav id="toc"><div class="toc-title">Spis treści</div><ol class="toc">${list}</ol></nav>`);
}

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "keepclip-docs-"));
let failed = 0;
for (const file of fs.readdirSync(srcDir).filter((f) => f.endsWith(".html")).sort()) {
  const staged = path.join(tmp, file);
  fs.writeFileSync(staged, injectToc(fs.readFileSync(path.join(srcDir, file), "utf8")));
  const pdf = path.join(outDir, file.replace(/\.html$/, ".pdf"));
  const result = spawnSync(edge, [
    "--headless=new",
    "--disable-gpu",
    "--no-first-run",
    `--user-data-dir=${path.join(tmp, "profile")}`,
    `--print-to-pdf=${pdf}`,
    "--no-pdf-header-footer",
    "file:///" + staged.replace(/\\/g, "/"),
  ], { stdio: "ignore" });
  if (result.status !== 0 || !fs.existsSync(pdf)) {
    console.error(`BŁĄD: ${file} (kod ${result.status})`);
    failed++;
    continue;
  }
  console.log(`${path.basename(pdf)}: ${(fs.statSync(pdf).size / 1024).toFixed(0)} KB`);
}
fs.rmSync(tmp, { recursive: true, force: true });
process.exit(failed ? 1 : 0);
