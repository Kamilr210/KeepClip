const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => Array.from(document.querySelectorAll(sel));

const els = {
  scan: $("#btn-scan"),
  scanLabel: $("#btn-scan .nav-label"),
  trans: $("#btn-transcribe"),
  cancel: $("#btn-cancel"),
  statClips: $("#stat-clips"),
  statTrans: $("#stat-trans"),
  statFav: $("#stat-fav"),
  localFree: $("#local-free"),
  localClips: $("#local-clips"),
  localTotal: $("#local-total"),
  localFillClips: $("#local-fill-clips"),
  localFillOther: $("#local-fill-other"),
  cloudFree: $("#cloud-free"),
  cloudClips: $("#cloud-clips"),
  cloudTotal: $("#cloud-total"),
  cloudFillClips: $("#cloud-fill-clips"),
  cloudFillOther: $("#cloud-fill-other"),
  progress: $("#progress"),
  fill: $("#progress-fill"),
  ptext: $("#progress-text"),
  pcurrent: $("#progress-current"),
  progClose: $("#progress-close"),
  q: $("#q"),
  gameFilter: $("#game-filter"),
  sort: $("#sort"),
  results: $("#results"),
  layoutBtns: $$(".view-toggle-btn"),
  overlay: $("#player-overlay"),
  pgame: $("#player-game"),
  pfile: $("#player-file"),
  video: $("#player"),
  segments: $("#player-segments"),
  pclose: $("#player-close"),
  pfav: $("#player-fav"),
  pdelete: $("#player-delete"),
  pfix: $("#player-fix"),
  pretrans: $("#player-retranscribe"),
  pfolders: $("#player-folders"),
  pfoldersDropdown: $("#player-folders-dropdown"),
  confirmOverlay: $("#confirm-overlay"),
  confirmTitle: $("#confirm-title"),
  confirmBody: $("#confirm-body"),
  confirmYes: $("#confirm-yes"),
  confirmNo: $("#confirm-no"),
  toast: $("#toast"),
  configOverlay: $("#config-overlay"),
  configTitle: $("#config-title"),
  configIntro: $("#config-intro"),
  configPath: $("#config-path"),
  configBrowse: $("#btn-browse-folder"),
  configError: $("#config-error"),
  configCancel: $("#config-cancel"),
  configSave: $("#config-save"),
  navItems: $$(".nav-item"),
  viewClips: $("#view-clips"),
  viewFolders: $("#view-folders"),
  viewFavorites: $("#view-favorites"),
  favResults: $("#fav-results"),
  favCount: $("#fav-count"),
  foldersIndex: $("#folders-index"),
  foldersGrid: $("#folders-grid"),
  folderDetail: $("#folder-detail"),
  folderDetailName: $("#folder-detail-name"),
  folderDetailCount: $("#folder-detail-count"),
  folderDetailGrid: $("#folder-detail-grid"),
  btnNewFolder: $("#btn-new-folder"),
  btnBackFolders: $("#btn-back-folders"),
  btnRenameFolder: $("#btn-rename-folder"),
  btnDeleteFolder: $("#btn-delete-folder"),
  btnAddClipsToFolder: $("#btn-add-clips-to-folder"),
  selectionBar: $("#selection-bar"),
  selFolderName: $("#sel-folder-name"),
  selCount: $("#sel-count"),
  selCancel: $("#sel-cancel"),
  selAdd: $("#sel-add"),
  pcut: $("#player-cut"),
  cutOverlay: $("#cut-overlay"),
  cutClose: $("#cut-close"),
  cutSourceName: $("#cut-source-name"),
  cutVideo: $("#cut-video"),
  cutRange: $("#cut-range"),
  cutRangeFill: null,  // wired in init
  cutRangeStart: null,
  cutRangeEnd: null,
  cutPlayhead: null,
  cutStartLabel: $("#cut-start-label"),
  cutEndLabel: $("#cut-end-label"),
  cutDurationLabel: $("#cut-duration-label"),
  cutPresets: $$(".cut-preset"),
  cutCustomMb: $("#cut-custom-mb"),
  cutEstimate: $("#cut-estimate"),
  cutError: $("#cut-error"),
  cutCancel: $("#cut-cancel"),
  cutGo: $("#cut-go"),
  viewCloud: $("#view-cloud"),
  cloudNavDot: $("#cloud-nav-dot"),
  cloudLoading: $("#cloud-loading"),
  cloudSetup: $("#cloud-setup"),
  cloudConnect: $("#cloud-connect"),
  cloudConnectBtn: $("#cloud-connect-btn"),
  cloudRecheck: $("#cloud-recheck"),
  cloudAccount: $("#cloud-account"),
  cloudAccName: $("#cloud-acc-name"),
  cloudAccEmail: $("#cloud-acc-email"),
  cloudAccPhoto: $("#cloud-acc-photo"),
  cloudAccError: $("#cloud-acc-error"),
  cloudQuotaFill: $("#cloud-quota-fill"),
  cloudQuotaText: $("#cloud-quota-text"),
  cloudDisconnectBtn: $("#cloud-disconnect-btn"),
  pcloud: $("#player-cloud"),
  btnUploadFolder: $("#btn-upload-folder"),
};
els.cutRangeFill = els.cutRange.querySelector(".range-fill");
els.cutRangeStart = els.cutRange.querySelector(".range-thumb.start");
els.cutRangeEnd = els.cutRange.querySelector(".range-thumb.end");
els.cutPlayhead = els.cutRange.querySelector(".range-playhead");

function fmtTime(s) {
  if (!isFinite(s)) return "0:00";
  s = Math.max(0, Math.floor(s));
  const m = Math.floor(s / 60);
  const sec = String(s % 60).padStart(2, "0");
  if (m >= 60) {
    const h = Math.floor(m / 60);
    return `${h}:${String(m % 60).padStart(2, "0")}:${sec}`;
  }
  return `${m}:${sec}`;
}

function fmtDate(mtime) {
  if (!mtime) return "";
  const d = new Date(mtime * 1000);
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}.${pad(d.getMonth() + 1)}.${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function pluralPl(n, one, few, many) {
  if (n === 1) return one;
  const lastTwo = n % 100;
  if (lastTwo >= 12 && lastTwo <= 14) return many;
  const lastOne = n % 10;
  if (lastOne >= 2 && lastOne <= 4) return few;
  return many;
}
const clipsWord = (n) => pluralPl(n, "klip", "klipy", "klipów");

function parseTime(s) {
  // Accept "M:SS", "MM:SS", "H:MM:SS", or plain seconds. Returns seconds (float) or NaN.
  if (typeof s !== "string" && typeof s !== "number") return NaN;
  s = String(s).trim();
  if (!s) return NaN;
  const parts = s.split(":").map((p) => parseFloat(p));
  if (parts.some((p) => isNaN(p) || p < 0)) return NaN;
  if (parts.length === 1) return parts[0];
  if (parts.length === 2) return parts[0] * 60 + parts[1];
  if (parts.length === 3) return parts[0] * 3600 + parts[1] * 60 + parts[2];
  return NaN;
}

function fmtTimeSec(s) {
  if (!isFinite(s) || s < 0) return "";
  const min = Math.floor(s / 60);
  const sec = (s - min * 60);
  return `${min}:${sec.toFixed(2).padStart(5, "0")}`;
}

function fmtSize(bytes) {
  if (!bytes && bytes !== 0) return "";
  if (bytes >= 1024 ** 4) return (bytes / (1024 ** 4)).toFixed(2) + " TB";
  if (bytes >= 1024 * 1024 * 1024) return (bytes / (1024 ** 3)).toFixed(2) + " GB";
  if (bytes >= 1024 * 1024) return Math.round(bytes / (1024 * 1024)) + " MB";
  if (bytes >= 1024) return Math.round(bytes / 1024) + " KB";
  return bytes + " B";
}

// Cached so the two storage bars can re-render whenever either data source
// (local /api/stats, or the Drive quota) refreshes independently.
let _lastStats = null;
let _cloudQuota = null; // {usage, limit} when connected, else null

async function loadStats() {
  const r = await fetch("/api/stats").then((r) => r.json());
  _lastStats = r;
  const nf = (n) => (n ?? 0).toLocaleString("pl-PL");
  if (els.statClips) els.statClips.textContent = nf(r.clips);
  if (els.statTrans) els.statTrans.textContent = nf(r.transcribed);
  if (els.statFav) els.statFav.textContent = nf(r.favorites);
  renderStorage();
  const cur = els.gameFilter.value;
  els.gameFilter.innerHTML = '<option value="">Wszystkie gry</option>' +
    r.games.map((g) => `<option value="${escapeAttr(g.game)}">${escapeHtml(g.game)} (${g.done}/${g.clips})</option>`).join("");
  if (cur) els.gameFilter.value = cur;
}

// Update the cached Drive quota from a /api/cloud/status payload and repaint.
function setCloudQuota(st) {
  _cloudQuota = st && st.connected && st.usage != null
    ? { usage: st.usage, limit: st.limit }
    : null;
  renderStorage();
}

// Re-pull the Drive quota after an op that changed cloud usage (upload/download).
function refreshCloudQuota() {
  if (!_cloudConnected) return;
  fetch("/api/cloud/status").then((r) => r.json()).then(setCloudQuota).catch(() => {});
}

// Paint both storage bars (local disk + Drive)
function renderStorage() {
  const s = _lastStats || {};
  
  const pct = (part, whole) => {
    if (!whole || whole <= 0 || !part || part <= 0) return 0;
    return Math.max(0, Math.min(100, (part / whole) * 100));
  };

  // ---- ЛОКАЛЬНЫЙ ДИСК ----
  const lClips = s.local_bytes || 0;
  const dTotal = s.disk_total || 0;
  const dFree = s.disk_free != null ? s.disk_free : 0;
  
  const dUsed = dTotal > 0 ? dTotal - dFree : 0;
  const lOther = Math.max(0, dUsed - lClips); 

  if (els.localClips) els.localClips.textContent = fmtSize(lClips);
  if (els.localTotal) els.localTotal.textContent = dTotal ? fmtSize(dTotal) : "—";
  
  if (els.localFree) {
    els.localFree.textContent = dFree > 0 ? `${fmtSize(dFree)} wolne` : "—";
    els.localFree.classList.remove("text-red");
  }

  let lClipsPct = pct(lClips, dTotal);
  let lOtherPct = pct(lOther, dTotal);

  // Даем фиолетовой полоске минимум 0.5% ширины (1-2 пикселя), чтобы ее всегда было видно
  if (lClips > 0 && lClipsPct < 0.2) lClipsPct = 0.5;

  // ИЩЕМ ЭЛЕМЕНТЫ НАПРЯМУЮ, ЧТОБЫ ИЗБЕЖАТЬ ОШИБОК
  const localFillClips = document.getElementById("local-fill-clips");
  const localFillOther = document.getElementById("local-fill-other");

  if (localFillClips) localFillClips.style.width = lClipsPct + "%";
  if (localFillOther) localFillOther.style.width = lOtherPct + "%";


  // ---- ОБЛАКО (Google Drive) ----
  const cClips = s.cloud_bytes || 0;
  if (els.cloudClips) els.cloudClips.textContent = fmtSize(cClips);
  
  const q = _cloudQuota;
  
  // Ищем элементы облака напрямую
  const cloudFillClips = document.getElementById("cloud-fill-clips");
  const cloudFillOther = document.getElementById("cloud-fill-other");
  const cloudBarContainer = cloudFillClips ? cloudFillClips.parentElement : null;
  
  if (q && q.limit) {
    // ОБЛАКО ПОДКЛЮЧЕНО
    const cUsed = q.usage || 0;
    const cOther = Math.max(0, cUsed - cClips);
    const cFree = Math.max(0, q.limit - cUsed);
    
    if (els.cloudTotal) els.cloudTotal.textContent = fmtSize(q.limit);
    if (els.cloudFree) {
      els.cloudFree.textContent = `${fmtSize(cFree)} wolne`;
      els.cloudFree.classList.remove("text-red");
    }
    
    let cClipsPct = pct(cClips, q.limit);
    if (cClips > 0 && cClipsPct < 0.5) cClipsPct = 0.5;
    let cOtherPct = pct(cOther, q.limit);

    if (cloudFillClips) cloudFillClips.style.width = cClipsPct + "%";
    if (cloudFillOther) cloudFillOther.style.width = cOtherPct + "%";
    
    if (cloudBarContainer) cloudBarContainer.style.visibility = "visible";
    
  } else {
    // ОБЛАКО ОТКЛЮЧЕНО ИЛИ БЕЗЛИМИТНО
    if (els.cloudTotal) els.cloudTotal.textContent = (q && _cloudConnected) ? "∞" : "—";
    
    if (els.cloudFree) {
      if (!_cloudConnected) {
        els.cloudFree.textContent = "niepołączono";
        els.cloudFree.classList.add("text-red");
      } else {
        els.cloudFree.textContent = "bez limitu";
        els.cloudFree.classList.remove("text-red");
      }
    }
    
    if (cloudFillClips) cloudFillClips.style.width = "0%";
    if (cloudFillOther) cloudFillOther.style.width = "0%";
    
    if (cloudBarContainer) {
      cloudBarContainer.style.visibility = _cloudConnected ? "visible" : "hidden";
    }
  }
}

function escapeHtml(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[c]));
}
function escapeAttr(s) { return escapeHtml(s); }

// Snippet from FTS comes with <mark> tags already. We let it through but sanitize the rest.
function safeSnippet(html) {
  // Strip tags except <mark>/</mark>; the snippet is server-generated so we trust it
  // but be defensive in case the segment text contained '<' etc.
  return String(html ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/&lt;mark&gt;/g, "<mark>").replace(/&lt;\/mark&gt;/g, "</mark>");
}

function syncSortOptions() {
  // Show "Trafność" only when there's a search query
  const hasQuery = els.q.value.trim().length > 0;
  const relOpt = els.sort.querySelector('option[value="relevance"]');
  if (relOpt) {
    relOpt.hidden = !hasQuery;
    if (!hasQuery && els.sort.value === "relevance") {
      els.sort.value = "newest";
    }
  }
}

async function doSearch() {
  const q = els.q.value.trim();
  const game = els.gameFilter.value;
  syncSortOptions();
  const sort = els.sort.value || (q ? "relevance" : "newest");
  if (!q) {
    await renderRecent(game, sort);
    return;
  }
  els.results.innerHTML = `<div class="empty">Szukam „${escapeHtml(q)}”…</div>`;
  const r = await fetch(
    `/api/search?q=${encodeURIComponent(q)}&sort=${encodeURIComponent(sort)}&limit=200`
  ).then((r) => r.json());
  let results = r.results || [];
  if (game) results = results.filter((x) => x.game === game);
  renderResults(results);
}

// Re-renderuje aktualną zawartość #results z uwzględnieniem aktywnego układu.
// Ustawiane przez renderRecent (mozaika wrzuca ulubione na początek), zerowane
// przez renderResults (wyniki wyszukiwania zostają w kolejności trafień).
let _repaint = null;

function favoritesFirst(clips) {
  // Stabilnie: najpierw ulubione (zachowując ich kolejność), potem reszta.
  // Dzięki temu duże kafelki górnego rzędu w mozaice to ulubione — a gdy ich
  // za mało, dopełniają je pozostałe (najnowsze) klipy.
  const fav = [];
  const rest = [];
  for (const c of clips) (c.favorite ? fav : rest).push(c);
  return fav.concat(rest);
}

async function renderRecent(game, sort = "newest") {
  const params = new URLSearchParams({ sort, limit: "120" });
  if (game) params.set("game", game);
  const clips = await fetch(`/api/clips?${params}`).then((r) => r.json());
  if (!clips.length) {
    _repaint = null;
    els.results.innerHTML = `<div class="empty">Brak klipów. Kliknij <b>Skanuj folder</b>, a potem <b>Transkrybuj nowe</b>.</div>`;
    return;
  }
  _repaint = () => {
    const ordered = _layoutMode === "mosaic" ? favoritesFirst(clips) : clips;
    els.results.innerHTML = ordered.map((c) => clipCard(c)).join("");
    $$(".result").forEach((el) => el.addEventListener("click", () => openPlayer(parseInt(el.dataset.clipId, 10), 0)));
  };
  _repaint();
}

function thumbImg(clipId, version = "") {
  const v = version ? `?v=${version}` : "";
  return `<img src="/thumb/${clipId}${v}" alt="" loading="lazy" onerror="this.outerHTML='<div class=&quot;placeholder&quot;>&#9205;</div>'" />`;
}

function clipCard(c) {
  const dur = c.duration ? `<span class="ts">${fmtTime(c.duration)}</span>` : "";
  const status = c.transcribed_at ? "" : ' · <span class="warn">nie transkrybowane</span>';
  const date = fmtDate(c.mtime);
  const size = fmtSize(c.size_bytes);
  const checked = _selectionMode && _selectedClipIds.has(c.id);
  const checkbox = _selectionMode
    ? `<div class="select-overlay ${checked ? "checked" : ""}"><div class="select-check">${checked ? "✓" : ""}</div></div>`
    : "";
  return `
    <div class="result ${checked ? "selected" : ""}" data-clip-id="${c.id}" data-size="${c.size_bytes || 0}" data-duration="${c.duration || 0}">
      <div class="thumb">${thumbImg(c.id, c.size_bytes)}${dur}${cloudChip(c)}${checkbox}</div>
      <div class="result-body">
        <div class="result-game">${escapeHtml(c.game)}</div>
        <div class="result-date">
          <span class="result-date-text">${escapeHtml(date)}</span>
          ${_selectionMode ? "" : favStar(c.id, c.favorite)}
        </div>
        <div class="result-meta">${escapeHtml(size)}${status}</div>
      </div>
    </div>`;
}

function renderResults(rows) {
  _repaint = null; // wyniki wyszukiwania zostają w kolejności trafień (bez przestawiania ulubionych)
  if (!rows.length) {
    els.results.innerHTML = `<div class="empty">Brak trafień. Spróbuj innych słów.</div>`;
    return;
  }
  els.results.innerHTML = rows
    .map((r) => {
      const date = fmtDate(r.mtime);
      const size = fmtSize(r.size_bytes);
      const checked = _selectionMode && _selectedClipIds.has(r.clip_id);
      const checkbox = _selectionMode
        ? `<div class="select-overlay ${checked ? "checked" : ""}"><div class="select-check">${checked ? "✓" : ""}</div></div>`
        : "";
      return `
        <div class="result ${checked ? "selected" : ""}" data-clip-id="${r.clip_id}" data-start="${r.start_s}" data-size="${r.size_bytes || 0}" data-duration="${r.duration || 0}">
          <div class="thumb">
            ${thumbImg(r.clip_id, r.size_bytes)}
            <span class="ts">${fmtTime(r.start_s)}</span>
            ${cloudChip({ id: r.clip_id, storage: r.storage })}
            ${checkbox}
          </div>
          <div class="result-body">
            <div class="result-game">${escapeHtml(r.game)}</div>
            <div class="result-text">${safeSnippet(r.snippet)}</div>
            <div class="result-date">
              <span class="result-date-text">${escapeHtml(date)} <span class="result-meta-inline">· ${escapeHtml(size)}</span></span>
              ${_selectionMode ? "" : favStar(r.clip_id, r.favorite)}
            </div>
          </div>
        </div>`;
    })
    .join("");
  $$(".result").forEach((el) => {
    el.addEventListener("click", () => {
      const cid = parseInt(el.dataset.clipId, 10);
      const start = parseFloat(el.dataset.start || "0");
      openPlayer(cid, start);
    });
  });
}

// ---------- favorites ----------
function favStar(clipId, isFav) {
  return `<button class="fav-btn ${isFav ? "is-fav" : ""}" data-fav-id="${clipId}" title="Ulubione" aria-label="Przełącz ulubione">${isFav ? "★" : "☆"}</button>`;
}

async function toggleFavorite(clipId) {
  try {
    const res = await fetch(`/api/clips/${clipId}/favorite`, { method: "POST" });
    if (!res.ok) throw new Error();
    const data = await res.json();
    const isFav = !!data.favorite;
    // Keep every star for this clip in sync (grid card, search card, player header).
    document.querySelectorAll(`.fav-btn[data-fav-id="${clipId}"]`).forEach((b) => {
      b.classList.toggle("is-fav", isFav);
      b.textContent = isFav ? "★" : "☆";
    });
    loadStats();
    if (!els.viewFavorites.hidden) loadFavorites();
  } catch {
    toast("Nie udało się zmienić ulubionych.", "error");
  }
}

async function loadFavorites() {
  const clips = await fetch("/api/clips?favorite=1&limit=500").then((r) => r.json());
  els.favCount.textContent = clips.length ? `${clips.length} ${clipsWord(clips.length)}` : "";
  if (!clips.length) {
    els.favResults.innerHTML = `<div class="empty">Brak ulubionych. Najedź kursorem na klip i kliknij ☆, aby dodać.</div>`;
    return;
  }
  els.favResults.innerHTML = clips.map((c) => clipCard(c)).join("");
  els.favResults.querySelectorAll(".result").forEach((el) =>
    el.addEventListener("click", () => openPlayer(parseInt(el.dataset.clipId, 10), 0))
  );
}

// One delegated, capture-phase listener handles the star on every grid (clips,
// search, favorites, folder detail) and the player header. Capturing on document
// runs before the card's own click, so we suppress opening the player / selecting.
document.addEventListener("click", (e) => {
  const fb = e.target.closest(".fav-btn");
  if (!fb) return;
  e.preventDefault();
  e.stopPropagation();
  const id = parseInt(fb.dataset.favId, 10);
  if (id) toggleFavorite(id);
}, true);

// ---------- cloud (Google Drive) ----------
let _cloudConnected = false;
// Clip ids whose cloud op (upload OR download) is in flight, so a fast second
// click (the chip lingers a beat before the DOM swap) can't fire a duplicate.
const _busyClipIds = new Set();
const CLOUD_UP_GLYPH =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 18a4 4 0 0 1-.5-7.97A5.5 5.5 0 0 1 17 9.2 3.9 3.9 0 0 1 17 18z"/><path d="M12 12v5M9.5 14.5 12 12l2.5 2.5"/></svg>';
const CLOUD_DOWN_GLYPH =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 18a4 4 0 0 1-.5-7.97A5.5 5.5 0 0 1 17 9.2 3.9 3.9 0 0 1 17 18z"/><path d="M12 17v-5M9.5 14.5 12 17l2.5-2.5"/></svg>';

// A chip in the corner of each thumb. Cloud clips get a "remove from cloud"
// button (download back to disk); local clips get an upload button (only
// actionable while an account is connected, via body.cloud-connected).
function cloudChip(c) {
  if (c && c.storage === "cloud") {
    return `<button class="cloud-chip cloud-down" data-cloud-down-id="${c.id}" title="Zdejmij z chmury (pobierz z powrotem na dysk)" aria-label="Zdejmij z chmury">${CLOUD_DOWN_GLYPH}</button>`;
  }
  return `<button class="cloud-chip cloud-up" data-cloud-up-id="${c.id}" title="Wyślij do chmury" aria-label="Wyślij do chmury">${CLOUD_UP_GLYPH}</button>`;
}

// Toggle the spinning "in progress" state on a clip's chip wherever it shows
// (covers both the upload and the remove buttons for that clip id).
function setClipChipsBusy(clipId, on) {
  document
    .querySelectorAll(`.cloud-up[data-cloud-up-id="${clipId}"], .cloud-down[data-cloud-down-id="${clipId}"]`)
    .forEach((b) => { b.classList.toggle("busy", on); b.disabled = on; });
}

function setCloudConnected(on) {
  _cloudConnected = !!on;
  document.body.classList.toggle("cloud-connected", _cloudConnected);
  if (els.cloudNavDot) els.cloudNavDot.hidden = !_cloudConnected;
  if (currentClipId != null && !els.overlay.hidden) {
    // keep the player button honest if connection flips while a clip is open
    updatePlayerCloudBtn(els.pcloud.classList.contains("in-cloud") ? "cloud" : "local");
  }
}

// Swap a clip's UPLOAD chips to the "remove from cloud" button everywhere it shows.
function markClipCloudInDom(clipId) {
  document.querySelectorAll(`.cloud-up[data-cloud-up-id="${clipId}"]`).forEach((btn) => {
    const b = document.createElement("button");
    b.className = "cloud-chip cloud-down";
    b.dataset.cloudDownId = clipId;
    b.title = "Zdejmij z chmury (pobierz z powrotem na dysk)";
    b.setAttribute("aria-label", "Zdejmij z chmury");
    b.innerHTML = CLOUD_DOWN_GLYPH;
    btn.replaceWith(b);
  });
}

// Inverse: swap a clip's REMOVE chips back to the upload button (restored to disk).
function markClipLocalInDom(clipId) {
  document.querySelectorAll(`.cloud-down[data-cloud-down-id="${clipId}"]`).forEach((btn) => {
    const b = document.createElement("button");
    b.className = "cloud-chip cloud-up";
    b.dataset.cloudUpId = clipId;
    b.title = "Wyślij do chmury";
    b.setAttribute("aria-label", "Wyślij do chmury");
    b.innerHTML = CLOUD_UP_GLYPH;
    btn.replaceWith(b);
  });
}

// Mark the player header button as busy with a progress label, or restore it.
function setPlayerCloudBusy(srcBtn, label) {
  if (!srcBtn || srcBtn.id !== "player-cloud") return;
  srcBtn.disabled = true;
  srcBtn.classList.add("busy");
  srcBtn.textContent = label;
}

async function uploadClipToCloud(clipId, srcBtn) {
  if (!_cloudConnected) {
    toast("Najpierw połącz z Google Drive (zakładka Chmura).", "error");
    return;
  }
  if (_busyClipIds.has(clipId)) return;  // an op for this clip is already running
  _busyClipIds.add(clipId);
  setClipChipsBusy(clipId, true);
  setPlayerCloudBusy(srcBtn, "☁ Wysyłam…");
  toast("Wysyłam klip do chmury…");
  try {
    const r = await fetch(`/api/clips/${clipId}/upload`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    markClipCloudInDom(clipId);
    toast(data.already ? "Klip jest już w chmurze." : "Wysłano do chmury. Lokalny plik trafił do Kosza.");
    loadStats();
    refreshCloudQuota();
    if (currentClipId === clipId) updatePlayerCloudBtn("cloud");
    if (!els.viewCloud.hidden) loadCloud();
  } catch (e) {
    toast(`Nie udało się wysłać: ${e.message}`, "error");
    setClipChipsBusy(clipId, false);
    if (currentClipId === clipId) updatePlayerCloudBtn("local");
  } finally {
    _busyClipIds.delete(clipId);
  }
}

async function downloadClipFromCloud(clipId, srcBtn) {
  if (!_cloudConnected) {
    toast("Najpierw połącz z Google Drive (zakładka Chmura).", "error");
    return;
  }
  if (_busyClipIds.has(clipId)) return;  // an op for this clip is already running
  _busyClipIds.add(clipId);
  setClipChipsBusy(clipId, true);
  setPlayerCloudBusy(srcBtn, "☁ Pobieram…");
  toast("Pobieram klip z chmury na dysk…");
  try {
    const r = await fetch(`/api/clips/${clipId}/download`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    markClipLocalInDom(clipId);
    toast(data.already ? "Klip jest już lokalnie." : "Zdjęto z chmury — plik wrócił na dysk.");
    loadStats();
    refreshCloudQuota();
    if (currentClipId === clipId) updatePlayerCloudBtn("local");
    if (!els.viewCloud.hidden) loadCloud();
  } catch (e) {
    toast(`Nie udało się zdjąć z chmury: ${e.message}`, "error");
    setClipChipsBusy(clipId, false);
    if (currentClipId === clipId) updatePlayerCloudBtn("cloud");
  } finally {
    _busyClipIds.delete(clipId);
  }
}

// Delegated capture listeners for the per-card chips (mirror the fav-btn one).
document.addEventListener("click", (e) => {
  const up = e.target.closest(".cloud-up");
  if (up) {
    e.preventDefault();
    e.stopPropagation();
    const id = parseInt(up.dataset.cloudUpId, 10);
    if (id) uploadClipToCloud(id, up);
    return;
  }
  const down = e.target.closest(".cloud-down");
  if (down) {
    e.preventDefault();
    e.stopPropagation();
    const id = parseInt(down.dataset.cloudDownId, 10);
    if (id) downloadClipFromCloud(id, down);
  }
}, true);

function updatePlayerCloudBtn(storage) {
  const b = els.pcloud;
  if (!b) return;
  b.classList.remove("busy");
  b.disabled = false;
  if (storage === "cloud") {
    // Clickable: now offers to bring the clip back down to local disk.
    b.hidden = false;
    b.classList.add("in-cloud");
    b.textContent = "☁ Zdejmij z chmury";
    b.title = "Pobierz klip z chmury z powrotem na dysk";
  } else if (_cloudConnected) {
    b.hidden = false;
    b.classList.remove("in-cloud");
    b.textContent = "☁ Wyślij do chmury";
    b.title = "Wyślij klip do chmury (Google Drive)";
  } else {
    b.hidden = true;
  }
}

if (els.pcloud) {
  els.pcloud.addEventListener("click", () => {
    if (currentClipId == null || els.pcloud.disabled) return;
    if (els.pcloud.classList.contains("in-cloud")) {
      downloadClipFromCloud(currentClipId, els.pcloud);
    } else {
      uploadClipToCloud(currentClipId, els.pcloud);
    }
  });
}

async function uploadFolderToCloud(folderId) {
  if (!_cloudConnected) {
    toast("Najpierw połącz z Google Drive (zakładka Chmura).", "error");
    return;
  }
  const ok = await showConfirm({
    title: "Wysłać folder do chmury?",
    body: "Wszystkie lokalne klipy z tego folderu trafią na Twój Google Drive, a ich pliki na dysku — do Kosza.",
    warning: "Przy dużych klipach może to chwilę potrwać.",
    yesLabel: "Tak, wyślij",
    yesClass: "primary",
  });
  if (!ok) return;
  els.btnUploadFolder.disabled = true;
  toast("Wysyłam folder do chmury…");
  try {
    const r = await fetch(`/api/folders/${folderId}/upload`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    let msg = `Wysłano ${data.uploaded} z ${data.total}.`;
    if (data.skipped) msg += ` ${data.skipped} już w chmurze.`;
    if (data.failed) msg += ` Nieudane: ${data.failed}.`;
    toast(msg, data.failed ? "error" : "ok");
    loadStats();
    refreshCloudQuota();
    if (_currentFolderId === folderId) openFolder(folderId);
  } catch (e) {
    toast(`Nie udało się: ${e.message}`, "error");
  } finally {
    els.btnUploadFolder.disabled = false;
  }
}
if (els.btnUploadFolder) {
  els.btnUploadFolder.addEventListener("click", () => {
    if (_currentFolderId != null) uploadFolderToCloud(_currentFolderId);
  });
}

// ---- Cloud view: connect / status / disconnect ----
let _cloudPollTimer = null;

function _showCloudPanel(which) {
  els.cloudLoading.hidden = which !== "loading";
  els.cloudSetup.hidden = which !== "setup";
  els.cloudConnect.hidden = which !== "connect";
  els.cloudAccount.hidden = which !== "account";
}

async function loadCloud() {
  _showCloudPanel("loading");
  let st;
  try {
    st = await fetch("/api/cloud/status").then((r) => r.json());
  } catch {
    _showCloudPanel("setup");
    return;
  }
  setCloudConnected(!!st.connected);
  setCloudQuota(st);  // keep the sidebar Drive bar in sync
  if (!st.configured) { _showCloudPanel("setup"); return; }
  if (!st.connected) { _showCloudPanel("connect"); return; }

  els.cloudAccName.textContent = st.name || "Konto Google";
  els.cloudAccEmail.textContent = st.email || "";
  if (st.photo) { els.cloudAccPhoto.src = st.photo; els.cloudAccPhoto.hidden = false; }
  else { els.cloudAccPhoto.hidden = true; }
  if (st.error) { els.cloudAccError.hidden = false; els.cloudAccError.textContent = st.error; }
  else { els.cloudAccError.hidden = true; }

  const bar = els.cloudQuotaFill.parentElement;
  if (st.limit && st.usage != null) {
    const pct = Math.min(100, Math.round((st.usage / st.limit) * 100));
    els.cloudQuotaFill.style.width = `${pct}%`;
    els.cloudQuotaText.textContent = `${fmtSize(st.usage)} z ${fmtSize(st.limit)} (${pct}%)`;
    bar.hidden = false;
  } else if (st.usage != null) {
    bar.hidden = true;
    els.cloudQuotaText.textContent = `Wykorzystano ${fmtSize(st.usage)} (limit nieograniczony)`;
  } else {
    bar.hidden = true;
    els.cloudQuotaText.textContent = "";
  }
  _showCloudPanel("account");
}

function _stopCloudPoll() { if (_cloudPollTimer) { clearInterval(_cloudPollTimer); _cloudPollTimer = null; } }

async function startCloudConnect() {
  els.cloudConnectBtn.disabled = true;
  try {
    const r = await fetch("/api/cloud/connect", { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    window.open(data.auth_url, "_blank", "noopener");
    toast("Otwarto stronę logowania Google w nowej karcie.");
    _stopCloudPoll();
    let tries = 0;
    _cloudPollTimer = setInterval(async () => {
      tries++;
      let st;
      try { st = await fetch("/api/cloud/status").then((r) => r.json()); } catch { st = {}; }
      if (st.connected) {
        _stopCloudPoll();
        setCloudConnected(true);
        setCloudQuota(st);
        loadCloud();
        toast("Połączono z Google Drive.");
      } else if (tries > 150) {
        _stopCloudPoll(); // ~5 min cap
      }
    }, 2000);
  } catch (e) {
    toast(`Nie udało się rozpocząć łączenia: ${e.message}`, "error");
  } finally {
    els.cloudConnectBtn.disabled = false;
  }
}

if (els.cloudConnectBtn) els.cloudConnectBtn.addEventListener("click", startCloudConnect);
if (els.cloudRecheck) els.cloudRecheck.addEventListener("click", loadCloud);
if (els.cloudDisconnectBtn) {
  els.cloudDisconnectBtn.addEventListener("click", async () => {
    const ok = await showConfirm({
      title: "Odłączyć Google Drive?",
      body: "KeepClip straci dostęp do Twojego Dysku. Klipy już wysłane zostają w chmurze, ale nie odtworzysz ich, dopóki nie połączysz się ponownie.",
      warning: "",
      yesLabel: "Odłącz",
      yesClass: "danger",
    });
    if (!ok) return;
    try { await fetch("/api/cloud/disconnect", { method: "POST" }); } catch {}
    setCloudConnected(false);
    setCloudQuota(null);
    _stopCloudPoll();
    loadCloud();
    toast("Odłączono Google Drive.");
  });
}

// Returning from the Google consent tab should refresh the Cloud view at once.
window.addEventListener("focus", () => { if (!els.viewCloud.hidden) loadCloud(); });

// ---------- clip layout: równa siatka vs mozaika ----------
let _layoutMode = "grid";
function applyLayout(mode) {
  _layoutMode = mode === "mosaic" ? "mosaic" : "grid";
  els.results.classList.toggle("layout-mosaic", _layoutMode === "mosaic");
  els.layoutBtns.forEach((b) => b.classList.toggle("active", b.dataset.layout === _layoutMode));
  try { localStorage.setItem("keepclip_layout", _layoutMode); } catch {}
  if (_repaint) _repaint(); // przełączenie układu przestawia ulubione do góry (mozaika)
}
els.layoutBtns.forEach((b) => b.addEventListener("click", () => applyLayout(b.dataset.layout)));
applyLayout((() => { try { return localStorage.getItem("keepclip_layout"); } catch { return null; } })() || "grid");

// ---------- hover preview (YouTube-style) ----------
const HOVER_DELAY_MS = 250;
let _hoverTimer = null;
let _activePreview = null; // { card, video }

function _stopActivePreview() {
  if (!_activePreview) return;
  const { card, video } = _activePreview;
  card.classList.remove("previewing");
  try {
    video.pause();
    video.removeAttribute("src");
    video.load();
  } catch {}
  video.remove();
  _activePreview = null;
}

function _startPreview(card) {
  _stopActivePreview();
  const cid = parseInt(card.dataset.clipId, 10);
  if (!cid) return;
  const size = card.dataset.size || "0";
  const duration = parseFloat(card.dataset.duration || "0");
  // If this card came from a search result, jump to the matched moment.
  // Otherwise start at 5s (skips loading screens / intro UI). For very short
  // clips, start at the midpoint; if we don't know the duration yet, still
  // start at 5s — browser will just clamp if the file is shorter.
  const matchStart = parseFloat(card.dataset.start || "NaN");
  let startAt;
  if (isFinite(matchStart) && matchStart > 0) {
    startAt = matchStart;
  } else if (duration > 0 && duration < 10) {
    startAt = Math.max(0, duration / 2);
  } else {
    startAt = 5;
  }

  const thumb = card.querySelector(".thumb");
  if (!thumb) return;
  const video = document.createElement("video");
  video.className = "preview-video";
  video.muted = true;
  video.loop = true;
  video.playsInline = true;
  video.preload = "auto";
  // Media fragments URI (#t=N) is the most reliable way to start a streamed
  // <video> at an offset — way more dependable than setting currentTime, which
  // gets clobbered by Chrome's autoplay/streaming machinery.
  const fragment = startAt > 0 ? `#t=${startAt.toFixed(2)}` : "";
  video.src = `/video/${cid}?v=${size}${fragment}`;
  video.addEventListener("loadedmetadata", () => {
    video.play().catch(() => {});
  }, { once: true });
  thumb.appendChild(video);
  card.classList.add("previewing");
  _activePreview = { card, video };
}

// Single delegated listener on the results grid — works for both initial render
// and re-renders after search/sort, no need to rebind on each.
els.results.addEventListener("mouseover", (e) => {
  if (_selectionMode) return;  // no previews while picking clips for a folder
  const card = e.target.closest(".result");
  if (!card) return;
  if (_activePreview && _activePreview.card === card) return;
  clearTimeout(_hoverTimer);
  _hoverTimer = setTimeout(() => _startPreview(card), HOVER_DELAY_MS);
});
els.results.addEventListener("mouseout", (e) => {
  const card = e.target.closest(".result");
  if (!card) return;
  // mouseout fires when moving between children — only act if we actually left the card
  const goingTo = e.relatedTarget;
  if (goingTo && card.contains(goingTo)) return;
  clearTimeout(_hoverTimer);
  if (_activePreview && _activePreview.card === card) _stopActivePreview();
});
// Scroll = stop preview (avoids playing video flying off-screen)
els.results.addEventListener("scroll", _stopActivePreview, { passive: true });
window.addEventListener("scroll", _stopActivePreview, { passive: true });

// ---------- player ----------
let currentSegments = [];
let currentClipId = null;
let segmentTickerInterval = null;
let editingSegId = null;  // id of the segment currently being hand-edited, or null

async function openPlayer(clipId, startAt) {
  currentClipId = clipId;
  const data = await fetch(`/api/segments/${clipId}`).then((r) => r.json());
  els.pgame.textContent = data.clip.game;
  els.pfile.textContent = data.clip.filename;
  const isFav = !!data.clip.favorite;
  els.pfav.dataset.favId = String(clipId);
  els.pfav.classList.toggle("is-fav", isFav);
  els.pfav.textContent = isFav ? "★" : "☆";
  updatePlayerCloudBtn(data.clip.storage);
  // Show the clip's thumbnail as a poster so the modal paints the first frame
  // immediately instead of flashing black while the stream starts buffering.
  els.video.poster = `/thumb/${clipId}?v=${data.clip.size_bytes}`;
  // Cache-bust the video URL by size_bytes — when a clip is fixed its content
  // (and size) changes, so this URL changes and the browser refetches instead
  // of serving the old bytes from cache.
  els.video.src = `/video/${clipId}?v=${data.clip.size_bytes}`;
  els.video.currentTime = 0;
  currentSegments = data.segments;

  els.segments.innerHTML = currentSegments.length
    ? currentSegments
        .map(
          (s) =>
            `<div class="seg" data-start="${s.start_s}" data-id="${s.id}">
               <div class="seg-ts">${fmtTime(s.start_s)}</div>
               <div class="seg-text">${escapeHtml(s.text)}</div>
               <button class="seg-edit" title="Popraw tekst" aria-label="Popraw tekst">✎</button>
             </div>`
        )
        .join("")
    : `<div class="empty">Ten klip nie był jeszcze transkrybowany.</div>`;

  els.segments.querySelectorAll(".seg").forEach((el) => {
    el.addEventListener("click", (e) => {
      // Don't seek when interacting with the inline editor or its trigger.
      if (e.target.closest(".seg-edit, .seg-edit-box") || el.classList.contains("editing")) return;
      const t = parseFloat(el.dataset.start);
      els.video.currentTime = t;
      els.video.play();
    });
    const editBtn = el.querySelector(".seg-edit");
    if (editBtn) {
      editBtn.addEventListener("click", (e) => {
        e.stopPropagation();
        enterSegEdit(el);
      });
    }
  });

  els.overlay.hidden = false;
  els.video.addEventListener(
    "loadedmetadata",
    () => {
      if (startAt > 0) {
        els.video.currentTime = Math.max(0, startAt - 0.5);
      }
      els.video.play().catch(() => {});
    },
    { once: true }
  );

  startSegmentTicker();
}

function startSegmentTicker() {
  stopSegmentTicker();
  segmentTickerInterval = setInterval(() => {
    if (!currentSegments.length) return;
    const t = els.video.currentTime;
    let activeId = null;
    for (const s of currentSegments) {
      if (t >= s.start_s && t <= s.end_s) {
        activeId = s.id;
        break;
      }
    }
    els.segments.querySelectorAll(".seg").forEach((el) => {
      const isActive = parseInt(el.dataset.id, 10) === activeId;
      if (isActive && !el.classList.contains("active")) {
        el.classList.add("active");
        // Don't yank the view while the user is hand-editing a line.
        if (editingSegId === null) el.scrollIntoView({ block: "nearest", behavior: "smooth" });
      } else if (!isActive) {
        el.classList.remove("active");
      }
    });
  }, 300);
}
function stopSegmentTicker() {
  if (segmentTickerInterval) {
    clearInterval(segmentTickerInterval);
    segmentTickerInterval = null;
  }
}

// Inline-edit a single transcript line. Whisper isn't always accurate, so this
// lets the user correct the wording by hand and persist it (search updates too).
function enterSegEdit(segEl) {
  if (segEl.classList.contains("editing")) return;
  const id = parseInt(segEl.dataset.id, 10);
  const seg = currentSegments.find((s) => s.id === id);
  if (!seg) return;

  editingSegId = id;
  els.video.pause();  // keep the active-line auto-scroll from fighting the cursor
  segEl.classList.add("editing");

  const textDiv = segEl.querySelector(".seg-text");
  const editBtn = segEl.querySelector(".seg-edit");
  textDiv.hidden = true;
  if (editBtn) editBtn.hidden = true;

  const box = document.createElement("div");
  box.className = "seg-edit-box";
  box.innerHTML = `
    <textarea class="seg-edit-input" rows="2"></textarea>
    <div class="seg-edit-actions">
      <span class="seg-edit-hint">Ctrl+Enter zapisuje · Esc anuluje</span>
      <button class="btn ghost seg-edit-cancel">Anuluj</button>
      <button class="btn primary seg-edit-save">Zapisz</button>
    </div>`;
  // Clicks inside the editor must never seek the video.
  box.addEventListener("click", (e) => e.stopPropagation());
  segEl.appendChild(box);

  const ta = box.querySelector(".seg-edit-input");
  ta.value = seg.text;
  ta.focus();
  ta.setSelectionRange(ta.value.length, ta.value.length);

  const finish = () => {
    box.remove();
    textDiv.hidden = false;
    if (editBtn) editBtn.hidden = false;
    segEl.classList.remove("editing");
    editingSegId = null;
  };

  const save = async () => {
    const newText = ta.value.trim();
    if (!newText) { ta.focus(); return; }
    if (newText === seg.text) { finish(); return; }
    const saveBtn = box.querySelector(".seg-edit-save");
    saveBtn.disabled = true;
    saveBtn.textContent = "Zapisuję…";
    try {
      const r = await fetch(`/api/segments/${id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text: newText }),
      });
      const d = await r.json();
      if (!r.ok) throw new Error(d.detail || r.statusText);
      seg.text = d.text;
      textDiv.innerHTML = escapeHtml(d.text);
      finish();
      toast("Zapisano poprawkę.");
    } catch (e) {
      saveBtn.disabled = false;
      saveBtn.textContent = "Zapisz";
      toast(`Nie udało się zapisać: ${e.message}`, "error");
    }
  };

  box.querySelector(".seg-edit-cancel").addEventListener("click", finish);
  box.querySelector(".seg-edit-save").addEventListener("click", save);
  ta.addEventListener("keydown", (e) => {
    if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); finish(); }  // cancel edit, don't close player
    else if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); save(); }
  });
}

function closePlayer() {
  els.overlay.hidden = true;
  els.video.pause();
  els.video.removeAttribute("src");
  els.video.load();
  stopSegmentTicker();
  closeFoldersDropdown();
  editingSegId = null;
}

els.pclose.addEventListener("click", closePlayer);
els.overlay.addEventListener("click", (e) => {
  if (e.target === els.overlay) closePlayer();
});
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    if (!els.confirmOverlay.hidden) hideConfirm();
    else if (!els.overlay.hidden && editingSegId === null) closePlayer();
  }
});

// ---------- confirm modal + toast ----------
let confirmResolver = null;
const _confirmWarnEl = document.querySelector("#confirm-box .confirm-warn");
const _defaultWarn = _confirmWarnEl ? _confirmWarnEl.innerHTML : "";

function showConfirm({
  title = "Na pewno?",
  body = "",
  warning = null,
  yesLabel = "Tak, usuń",
  yesClass = "danger",
} = {}) {
  els.confirmTitle.textContent = title;
  els.confirmBody.textContent = body;
  if (_confirmWarnEl) {
    _confirmWarnEl.innerHTML = warning !== null ? warning : _defaultWarn;
  }
  els.confirmYes.textContent = yesLabel;
  els.confirmYes.className = `btn ${yesClass}`;
  els.confirmOverlay.hidden = false;
  return new Promise((resolve) => {
    confirmResolver = resolve;
    setTimeout(() => els.confirmNo.focus(), 0);
  });
}
function hideConfirm(answer = false) {
  els.confirmOverlay.hidden = true;
  if (confirmResolver) {
    confirmResolver(answer);
    confirmResolver = null;
  }
}
els.confirmYes.addEventListener("click", () => hideConfirm(true));
els.confirmNo.addEventListener("click", () => hideConfirm(false));
els.confirmOverlay.addEventListener("click", (e) => {
  if (e.target === els.confirmOverlay) hideConfirm(false);
});

let toastTimer = null;
function toast(msg, kind = "ok") {
  els.toast.textContent = msg;
  els.toast.className = kind === "error" ? "error" : "";
  els.toast.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { els.toast.hidden = true; }, 3500);
}

// ---------- player: cut fragment ----------
let _cutTargetMb = null;    // null = no compression (default)
let _cutStartS = 0;
let _cutEndS = 0;
let _cutMaxS = 0;            // = video duration
let _cutPlayheadTimer = null;

function openCutModal() {
  if (!currentClipId) return;
  els.cutSourceName.textContent = els.pfile.textContent;

  // Load the same video into the modal preview (cache-bust by size, same as main player)
  const mainSrc = els.video.src;
  els.cutVideo.src = mainSrc;
  els.cutVideo.currentTime = 0;

  // Default range: ±5s around current player position
  const ct = els.video.currentTime || 0;
  const dur = els.video.duration || ct + 10;
  _cutMaxS = dur;
  _cutStartS = Math.max(0, ct - 5);
  _cutEndS = Math.min(dur, _cutStartS + 10);

  // Default preset = bez kompresji
  _cutTargetMb = null;
  els.cutPresets.forEach((b) => b.classList.toggle("active", b.dataset.mb === ""));
  els.cutCustomMb.value = "";
  els.cutError.hidden = true;

  els.cutOverlay.hidden = false;

  // Wait for cut-video metadata to know real duration, then position thumbs
  const onReady = () => {
    _cutMaxS = els.cutVideo.duration || _cutMaxS;
    _cutStartS = Math.min(_cutStartS, _cutMaxS - 0.1);
    _cutEndS = Math.min(_cutEndS, _cutMaxS);
    if (_cutEndS - _cutStartS < 0.5) _cutEndS = Math.min(_cutMaxS, _cutStartS + 5);
    _updateCutSliderUI();
    updateCutEstimate();
    _startPlayheadSync();
  };
  if (els.cutVideo.readyState >= 1) onReady();
  else els.cutVideo.addEventListener("loadedmetadata", onReady, { once: true });

  // initial paint with the guess values (will adjust on metadata if needed)
  _updateCutSliderUI();
  updateCutEstimate();
}

function closeCutModal() {
  els.cutOverlay.hidden = true;
  els.cutVideo.pause();
  els.cutVideo.removeAttribute("src");
  els.cutVideo.load();
  _stopPlayheadSync();
}

function _startPlayheadSync() {
  _stopPlayheadSync();
  _cutPlayheadTimer = setInterval(() => {
    if (_cutMaxS <= 0) return;
    const t = els.cutVideo.currentTime;
    const pct = (t / _cutMaxS) * 100;
    els.cutPlayhead.style.left = `${pct}%`;
    els.cutPlayhead.classList.toggle("visible", !els.cutVideo.paused);
  }, 80);
}
function _stopPlayheadSync() {
  if (_cutPlayheadTimer) {
    clearInterval(_cutPlayheadTimer);
    _cutPlayheadTimer = null;
  }
  els.cutPlayhead.classList.remove("visible");
}

function _updateCutSliderUI() {
  if (_cutMaxS <= 0) return;
  const startPct = (_cutStartS / _cutMaxS) * 100;
  const endPct = (_cutEndS / _cutMaxS) * 100;
  els.cutRangeStart.style.left = `${startPct}%`;
  els.cutRangeEnd.style.left = `${endPct}%`;
  els.cutRangeFill.style.left = `${startPct}%`;
  els.cutRangeFill.style.width = `${endPct - startPct}%`;
  els.cutStartLabel.textContent = fmtTimeSec(_cutStartS);
  els.cutEndLabel.textContent = fmtTimeSec(_cutEndS);
  els.cutDurationLabel.textContent = `${(_cutEndS - _cutStartS).toFixed(2)} s`;
}

// --- range slider drag ---
let _cutDragging = null;  // "start" | "end" | null
els.cutRangeStart.addEventListener("pointerdown", (e) => _beginCutDrag(e, "start"));
els.cutRangeEnd.addEventListener("pointerdown", (e) => _beginCutDrag(e, "end"));
els.cutRange.addEventListener("pointermove", _onCutDragMove);
els.cutRange.addEventListener("pointerup", _endCutDrag);
els.cutRange.addEventListener("pointercancel", _endCutDrag);
els.cutRange.addEventListener("pointerleave", _endCutDrag);
// Click on track = move nearest thumb
els.cutRange.addEventListener("click", (e) => {
  if (e.target !== els.cutRange && !e.target.classList.contains("range-track") && !e.target.classList.contains("range-fill")) return;
  if (_cutMaxS <= 0) return;
  const rect = els.cutRange.getBoundingClientRect();
  const pct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
  const t = pct * _cutMaxS;
  // pick whichever thumb is closer
  const distToStart = Math.abs(t - _cutStartS);
  const distToEnd = Math.abs(t - _cutEndS);
  if (distToStart < distToEnd) {
    _cutStartS = Math.min(t, _cutEndS - 0.1);
    els.cutVideo.currentTime = _cutStartS;
  } else {
    _cutEndS = Math.max(t, _cutStartS + 0.1);
    els.cutVideo.currentTime = _cutEndS;
  }
  _updateCutSliderUI();
  updateCutEstimate();
});

function _beginCutDrag(e, which) {
  e.preventDefault();
  _cutDragging = which;
  els.cutRange.setPointerCapture(e.pointerId);
  (which === "start" ? els.cutRangeStart : els.cutRangeEnd).classList.add("dragging");
}
function _onCutDragMove(e) {
  if (!_cutDragging || _cutMaxS <= 0) return;
  const rect = els.cutRange.getBoundingClientRect();
  const pct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
  const t = pct * _cutMaxS;
  if (_cutDragging === "start") {
    _cutStartS = Math.min(t, _cutEndS - 0.1);
    els.cutVideo.currentTime = _cutStartS;
  } else {
    _cutEndS = Math.max(t, _cutStartS + 0.1);
    els.cutVideo.currentTime = _cutEndS;
  }
  _updateCutSliderUI();
  updateCutEstimate();
}
function _endCutDrag() {
  if (!_cutDragging) return;
  els.cutRangeStart.classList.remove("dragging");
  els.cutRangeEnd.classList.remove("dragging");
  _cutDragging = null;
}

function _currentCutTimes() {
  return { start: _cutStartS, end: _cutEndS, duration: _cutEndS - _cutStartS };
}

function updateCutEstimate() {
  const { start, end, duration } = _currentCutTimes();
  const valid = isFinite(start) && isFinite(end) && duration > 0;
  if (!valid) {
    els.cutEstimate.textContent = "Zaznacz zakres na pasku.";
    els.cutEstimate.classList.add("warn");
    return;
  }

  if (_cutTargetMb == null) {
    // No compression — estimate from source bitrate (very rough)
    const src = document.querySelector(`.result[data-clip-id="${currentClipId}"]`);
    const srcSize = parseFloat(src?.dataset?.size || "0");
    const srcDur = parseFloat(src?.dataset?.duration || "0");
    if (srcSize && srcDur) {
      const estMb = (srcSize / srcDur * duration) / 1024 / 1024;
      els.cutEstimate.innerHTML = `Bez kompresji — szacowany rozmiar: <b>~${estMb.toFixed(1)} MB</b> (jakość 1:1 z oryginałem).`;
    } else {
      els.cutEstimate.innerHTML = `Bez kompresji — jakość 1:1 z oryginałem.`;
    }
    els.cutEstimate.classList.remove("warn");
    return;
  }

  // With compression target — compute the bitrate the backend will use
  const targetBytes = _cutTargetMb * 1024 * 1024;
  const audioKbps = 128;
  const audioBytes = (audioKbps * 1000 / 8) * duration;
  const videoBytes = targetBytes - audioBytes - 50000;
  if (videoBytes < 100_000) {
    els.cutEstimate.innerHTML = `⚠ ${_cutTargetMb} MB to za mało na <b>${duration.toFixed(1)}s</b> wideo. Zwiększ rozmiar albo skróć fragment.`;
    els.cutEstimate.classList.add("warn");
    return;
  }
  const videoKbps = Math.floor((videoBytes * 8) / duration / 1000);
  let quality = "świetna";
  if (videoKbps < 1500) quality = "słaba (rozmazane)";
  else if (videoKbps < 3000) quality = "średnia";
  else if (videoKbps < 6000) quality = "dobra";
  els.cutEstimate.innerHTML =
    `Cel: <b>${_cutTargetMb} MB</b> · bitrate wideo ~${videoKbps} kbps · jakość: <b>${quality}</b>`;
  els.cutEstimate.classList.toggle("warn", videoKbps < 1500);
}

els.cutPresets.forEach((btn) => {
  btn.addEventListener("click", () => {
    els.cutPresets.forEach((b) => b.classList.remove("active"));
    btn.classList.add("active");
    _cutTargetMb = btn.dataset.mb ? parseFloat(btn.dataset.mb) : null;
    els.cutCustomMb.value = "";
    updateCutEstimate();
  });
});

els.cutCustomMb.addEventListener("input", () => {
  const v = parseFloat(els.cutCustomMb.value);
  if (v > 0) {
    _cutTargetMb = v;
    els.cutPresets.forEach((b) => b.classList.remove("active"));
  }
  updateCutEstimate();
});

els.cutClose.addEventListener("click", closeCutModal);
els.cutCancel.addEventListener("click", closeCutModal);
els.cutOverlay.addEventListener("click", (e) => {
  if (e.target === els.cutOverlay) closeCutModal();
});

els.cutGo.addEventListener("click", async () => {
  const { start, end, duration } = _currentCutTimes();
  if (!isFinite(start) || !isFinite(end) || duration <= 0) {
    els.cutError.textContent = "Wpisz prawidłowy zakres (Od < Do).";
    els.cutError.hidden = false;
    return;
  }
  els.cutError.hidden = true;
  els.cutGo.disabled = true;
  els.cutGo.textContent = "Wycinam…";
  try {
    const r = await fetch(`/api/clips/${currentClipId}/cut`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        start, end,
        target_size_mb: _cutTargetMb,
      }),
    });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    closeCutModal();
    showCutSuccess(data);
    // The cut now lives inside the library, so refresh the grid + game filter
    // to surface it (and the "Wycinki" category) without a manual scan.
    if (data.in_library) {
      await loadStats();
      await doSearch();
    }
  } catch (e) {
    els.cutError.textContent = e.message;
    els.cutError.hidden = false;
  } finally {
    els.cutGo.disabled = false;
    els.cutGo.textContent = "Wytnij fragment";
  }
});

function showCutSuccess(data) {
  // Re-use the confirm modal as a "done" dialog with custom buttons
  const ok = showConfirm({
    title: "Gotowe!",
    body: `${data.output_name} — ${data.size_mb} MB`,
    warning: `Plik zapisano w folderze:<br><code>${escapeHtml(data.cuts_root)}</code>`,
    yesLabel: "Pokaż w folderze",
    yesClass: "primary",
  });
  // Override the cancel button text to "Zamknij" for this dialog
  els.confirmNo.textContent = "Zamknij";
  ok.then(async (clicked) => {
    els.confirmNo.textContent = "Anuluj";  // restore default
    if (clicked) {
      try {
        await fetch("/api/show-in-explorer", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ path: data.output_path }),
        });
      } catch (e) {
        toast(`Nie udało się otworzyć folderu: ${e.message}`, "error");
      }
    }
  });
}

els.pcut.addEventListener("click", openCutModal);
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape" && !els.cutOverlay.hidden) closeCutModal();
});

// ---------- player: add/remove from folders ----------
async function openFoldersDropdown() {
  if (!currentClipId) return;
  const [allFolders, membership] = await Promise.all([
    fetch("/api/folders").then((r) => r.json()),
    fetch(`/api/clips/${currentClipId}/folders`).then((r) => r.json()),
  ]);
  const memberSet = new Set(membership.folder_ids);

  let html = "";
  if (allFolders.length === 0) {
    html += `<div class="folders-dropdown-empty">Brak folderów. Utwórz pierwszy poniżej.</div>`;
  } else {
    html += allFolders.map((f) => `
      <label class="folders-dropdown-item">
        <input type="checkbox" data-folder-id="${f.id}" ${memberSet.has(f.id) ? "checked" : ""} />
        <span>${escapeHtml(f.name)}</span>
      </label>`).join("");
  }
  html += `
    <div class="folders-dropdown-new">
      <input id="player-new-folder-name" type="text" placeholder="nowy folder..." autocomplete="off" />
      <button id="player-new-folder-btn" class="btn primary" style="padding: 6px 12px; font-size: 13px;">Dodaj</button>
    </div>`;
  els.pfoldersDropdown.innerHTML = html;
  els.pfoldersDropdown.hidden = false;

  // wire checkboxes
  els.pfoldersDropdown.querySelectorAll('input[type="checkbox"]').forEach((cb) => {
    cb.addEventListener("change", async () => {
      const fid = parseInt(cb.dataset.folderId, 10);
      cb.disabled = true;
      try {
        if (cb.checked) {
          await fetch(`/api/folders/${fid}/clips/${currentClipId}`, { method: "POST" });
        } else {
          await fetch(`/api/folders/${fid}/clips/${currentClipId}`, { method: "DELETE" });
        }
      } finally {
        cb.disabled = false;
      }
    });
  });

  // wire new-folder add
  const newInput = document.getElementById("player-new-folder-name");
  const newBtn = document.getElementById("player-new-folder-btn");
  const createInline = async () => {
    const name = newInput.value.trim();
    if (!name) return;
    newBtn.disabled = true;
    try {
      const r = await fetch("/api/folders", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name }),
      });
      const data = await r.json();
      if (!r.ok) throw new Error(data.detail || r.statusText);
      // add the clip to the new folder automatically
      await fetch(`/api/folders/${data.id}/clips/${currentClipId}`, { method: "POST" });
      toast(`Utworzono „${data.name}" i dodano klip.`);
      await openFoldersDropdown();
    } catch (e) {
      toast(e.message, "error");
    } finally {
      newBtn.disabled = false;
    }
  };
  newBtn.addEventListener("click", createInline);
  newInput.addEventListener("keydown", (e) => { if (e.key === "Enter") createInline(); });
  setTimeout(() => newInput.focus(), 50);
}

function closeFoldersDropdown() {
  els.pfoldersDropdown.hidden = true;
  els.pfoldersDropdown.innerHTML = "";
}

els.pfolders.addEventListener("click", (e) => {
  e.stopPropagation();
  if (els.pfoldersDropdown.hidden) {
    openFoldersDropdown();
  } else {
    closeFoldersDropdown();
  }
});
document.addEventListener("click", (e) => {
  if (!els.pfoldersDropdown.hidden &&
      !els.pfoldersDropdown.contains(e.target) &&
      e.target !== els.pfolders) {
    closeFoldersDropdown();
  }
});

// ---------- re-transcribe clip ----------
els.pretrans.addEventListener("click", async () => {
  if (!currentClipId) return;
  els.pretrans.disabled = true;
  const orig = els.pretrans.textContent;
  els.pretrans.textContent = "🔁 Transkrybuję…";
  try {
    const r = await fetch(`/api/clips/${currentClipId}/retranscribe`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    toast(`Transkrybowano ponownie — ${data.segments} segmentów w ${data.seconds}s.`);
    const cid = currentClipId;
    closePlayer();
    setTimeout(() => openPlayer(cid, 0), 200);
    await loadStats();
  } catch (e) {
    toast(`Błąd transkrypcji: ${e.message}`, "error");
  } finally {
    els.pretrans.disabled = false;
    els.pretrans.textContent = orig;
  }
});

// ---------- fix clip ----------
els.pfix.addEventListener("click", async () => {
  if (!currentClipId) return;
  const filename = els.pfile.textContent;
  const ok = await showConfirm({
    title: "Napraw klip?",
    body: filename,
    warning: "Próba naprawy pliku z NVIDIA ShadowPlay: ffmpeg zremuxuje plik (i jeśli trzeba — odetnie uszkodzone intro). Oryginał trafi do Kosza Windows. Może zająć od kilku sekund do minuty zależnie od wielkości.",
    yesLabel: "Tak, napraw",
    yesClass: "primary",
  });
  if (!ok) return;
  els.pfix.disabled = true;
  els.pfix.textContent = "🔧 Naprawiam…";
  try {
    const r = await fetch(`/api/clips/${currentClipId}/fix`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    const msg = data.trimmed_seconds > 0
      ? `Naprawiono — odcięto pierwsze ${data.trimmed_seconds.toFixed(0)}s uszkodzonego intra.`
      : "Naprawiono przez remux (bez utraty zawartości).";
    toast(msg);
    // reload video with cache-busting param so the browser refetches
    const cid = currentClipId;
    closePlayer();
    setTimeout(() => openPlayer(cid, 0), 200);
    await loadStats();
  } catch (e) {
    toast(`Naprawa nie powiodła się: ${e.message}`, "error");
  } finally {
    els.pfix.disabled = false;
    els.pfix.textContent = "🔧 Napraw klip";
  }
});

// ---------- delete clip ----------
els.pdelete.addEventListener("click", async () => {
  if (!currentClipId) return;
  const filename = els.pfile.textContent;
  const ok = await showConfirm({
    title: "Usuń klip?",
    body: filename,
  });
  if (!ok) return;
  const clipId = currentClipId;
  els.pdelete.disabled = true;
  // Release the video file BEFORE deleting. While the player streams a clip the
  // OS keeps its handle open, so Windows refuses to move it to the Recycle Bin
  // (WinError 32). closePlayer() drops the <video> src; the short wait lets the
  // server-side stream actually close before we ask to trash the file.
  closePlayer();
  await new Promise((resolve) => setTimeout(resolve, 200));
  try {
    const r = await fetch(`/api/clips/${clipId}?delete_file=true`, { method: "DELETE" });
    if (!r.ok) {
      const err = await r.text();
      throw new Error(err || r.statusText);
    }
    const data = await r.json();
    if (data.file_sent_to_trash) {
      toast(`Usunięto „${data.filename}" — plik trafił do Kosza.`);
    } else if (data.trash_error) {
      toast(`Wpis usunięty, ale pliku nie udało się przenieść do Kosza: ${data.trash_error}`, "error");
    } else {
      toast(`Usunięto „${data.filename}".`);
    }
    await loadStats();
    await doSearch();
  } catch (e) {
    toast(`Błąd usuwania: ${e.message}`, "error");
  } finally {
    els.pdelete.disabled = false;
  }
});

// ---------- search / filter ----------
let searchTimer = null;
els.q.addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(doSearch, 200);
});
els.gameFilter.addEventListener("change", doSearch);
els.sort.addEventListener("change", doSearch);

// ---------- actions ----------
async function runScan({ showToastAlways = false } = {}) {
  const r = await fetch("/api/scan", { method: "POST" }).then((r) => r.json());
  const parts = [];
  if (r.added) parts.push(`+${r.added} nowych`);
  if (r.removed) parts.push(`-${r.removed} usuniętych`);
  if (r.added || r.removed) {
    toast(`Skan: ${parts.join(", ")}`);
  } else if (showToastAlways) {
    toast("Skan zakończony — bez zmian.");
  }
  return r;
}

els.scan.addEventListener("click", async () => {
  els.scan.disabled = true;
  els.scanLabel.textContent = "Skanuję…";
  try {
    const r = await runScan({ showToastAlways: true });
    const parts = [];
    if (r.added) parts.push(`+${r.added} nowych`);
    if (r.removed) parts.push(`-${r.removed} usuniętych`);
    if (!parts.length) parts.push("bez zmian");
    els.scanLabel.textContent = parts.join(", ");
    await loadStats();
    await doSearch();
  } finally {
    setTimeout(() => {
      els.scan.disabled = false;
      els.scanLabel.textContent = "Skanuj folder";
    }, 2000);
  }
});

let sse = null;
async function startTranscribe(force = false) {
  await fetch("/api/scan", { method: "POST" });
  const url = force ? "/api/transcribe/start?force=true" : "/api/transcribe/start";
  const r = await fetch(url, { method: "POST" }).then((r) => r.json());
  if (!r.started) {
    toast("Transkrypcja już trwa.", "error");
    return;
  }
  // Nothing to do: skip the progress bar entirely and just notify at the bottom.
  if (r.total === 0) {
    toast(force ? "Brak klipów do transkrypcji." : "Brak nowych klipów do transkrypcji.");
    return;
  }
  toast(force ? "Rozpoczęto transkrypcję wszystkich klipów…" : "Rozpoczęto transkrypcję nowych klipów…");
  attachStream();
}
els.trans.addEventListener("click", () => startTranscribe(false));

async function retranscribeAll() {
  const stats = await fetch("/api/stats").then((r) => r.json());
  const ok = await showConfirm({
    title: "Transkrybuj ponownie wszystkie klipy?",
    body: `${stats.clips} klipów zostanie przetranskrybowanych od nowa (istniejące segmenty zostaną nadpisane).`,
    warning: "Może zająć kilkanaście minut. Przyda się, jeśli aktualizowaliśmy logikę transkrypcji (np. dokładniejsze znaczniki czasu) — wcześniejsze klipy nadal mają stare segmenty.",
    yesLabel: "Tak, transkrybuj wszystkie",
    yesClass: "primary",
  });
  if (!ok) return;
  await startTranscribe(true);
}

// ---------- sidebar tool actions ----------
document.querySelectorAll(".nav-tool[data-action]").forEach((btn) => {
  btn.addEventListener("click", () => {
    const action = btn.dataset.action;
    if (action === "retranscribe-all") retranscribeAll();
    else if (action === "change-folder") openConfigModal({ mode: "change" });
  });
});

// ---------- config modal (first-launch + change folder) ----------
async function openConfigModal({ mode = "change" } = {}) {
  const cfg = await fetch("/api/config").then((r) => r.json());
  if (mode === "first") {
    els.configTitle.textContent = "Witaj w Klipy!";
    els.configIntro.innerHTML =
      "Aby zacząć, wskaż folder z klipami. Aplikacja przeszuka go wraz z pod-folderami " +
      "(typowo każda gra ma osobny pod-folder). Najczęściej dla NVIDIA ShadowPlay jest to " +
      "<code>C:\\Users\\&lt;ty&gt;\\Videos\\NVIDIA</code>.";
    els.configCancel.hidden = true;
    els.configSave.textContent = "Użyj tego folderu";
  } else {
    els.configTitle.textContent = "Zmień folder z klipami";
    els.configIntro.innerHTML =
      "Po zmianie folderu klipy z poprzedniej lokalizacji znikną z listy " +
      "(pliki na dysku zostają nienaruszone). Z nowego folderu zostaną wczytane od nowa.";
    els.configCancel.hidden = false;
    els.configSave.textContent = "Zapisz i przeskanuj";
  }
  els.configPath.value = cfg.clips_root || "";
  els.configError.hidden = true;
  els.configError.textContent = "";
  els.configOverlay.hidden = false;
  setTimeout(() => {
    els.configPath.focus();
    els.configPath.select();
  }, 50);
}

function closeConfigModal() {
  els.configOverlay.hidden = true;
}

els.configCancel.addEventListener("click", closeConfigModal);
els.configOverlay.addEventListener("click", (e) => {
  if (e.target === els.configOverlay && !els.configCancel.hidden) closeConfigModal();
});
els.configPath.addEventListener("keydown", (e) => {
  if (e.key === "Enter") els.configSave.click();
});

if (els.configBrowse) {
  els.configBrowse.addEventListener("click", async () => {
    if (window.pywebview && window.pywebview.api) {
      const selectedPath = await window.pywebview.api.pick_folder();
      if (selectedPath) {
        els.configPath.value = selectedPath;
        // Чтобы сделать UX еще круче: можно автоматически переводить фокус 
        // на кнопку "Записать", когда папка успешно выбрана:
        els.configSave.focus();
      }
    } else {
      toast("Wybór folderu działa tylko w aplikacji desktopowej.", "error");
    }
  });
}

els.configSave.addEventListener("click", async () => {
  const newPath = els.configPath.value.trim();
  if (!newPath) {
    els.configError.textContent = "Wpisz ścieżkę do folderu.";
    els.configError.hidden = false;
    return;
  }
  els.configSave.disabled = true;
  els.configSave.textContent = "Sprawdzam…";
  try {
    const r = await fetch("/api/config", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ clips_root: newPath }),
    });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    closeConfigModal();
    const scan = data.scan || {};
    const parts = [];
    if (scan.added) parts.push(`+${scan.added} nowych`);
    if (scan.removed) parts.push(`-${scan.removed} starych usuniętych`);
    if (!parts.length && scan.found) parts.push(`${scan.found} klipów`);
    toast(`Folder ustawiony: ${data.clips_root}${parts.length ? " — " + parts.join(", ") : ""}`);
    await loadStats();
    await doSearch();
  } catch (e) {
    els.configError.textContent = e.message;
    els.configError.hidden = false;
  } finally {
    els.configSave.disabled = false;
    els.configSave.textContent = els.configCancel.hidden ? "Użyj tego folderu" : "Zapisz i przeskanuj";
  }
});
els.cancel.addEventListener("click", async () => {
  await fetch("/api/transcribe/cancel", { method: "POST" });
});

// Dismiss the finished progress card (button is only shown once the run ends).
els.progClose.addEventListener("click", () => {
  els.progress.hidden = true;
  els.progClose.hidden = true;
  els.fill.style.width = "0%";
  els.ptext.textContent = "0 / 0";
  els.pcurrent.textContent = "";
});

function attachStream() {
  if (sse) sse.close();
  els.progress.hidden = false;
  els.progClose.hidden = true;   // not dismissible while a run is in progress
  els.cancel.hidden = false;
  els.trans.disabled = true;
  sse = new EventSource("/api/transcribe/stream");
  sse.onmessage = (ev) => {
    try {
      const s = JSON.parse(ev.data);
      const pct = s.total ? Math.round((s.done / s.total) * 100) : 0;
      els.fill.style.width = `${pct}%`;
      els.ptext.textContent = `${s.done} / ${s.total}`;
      if (s.current) {
        els.pcurrent.textContent = `▶ ${s.current.game} / ${s.current.filename}`;
      } else if (!s.running) {
        els.pcurrent.textContent = s.error ? `Błąd: ${s.error}` : "Zakończono.";
      }
      if (!s.running && s.finished_at) {
        sse.close();
        sse = null;
        els.cancel.hidden = true;
        els.trans.disabled = false;
        els.progClose.hidden = false;   // run done → allow dismissing the progress card
        loadStats();
        doSearch();
      }
    } catch (e) {
      console.error(e);
    }
  };
  sse.onerror = () => {
    // Try to recover via polling once
    if (sse) {
      sse.close();
      sse = null;
    }
    pollOnce();
  };
}

async function pollOnce() {
  try {
    const s = await fetch("/api/transcribe/status").then((r) => r.json());
    if (s.running) {
      setTimeout(pollOnce, 1500);
    } else {
      els.cancel.hidden = true;
      els.trans.disabled = false;
      els.progClose.hidden = false;
      loadStats();
    }
  } catch {
    setTimeout(pollOnce, 3000);
  }
}

// ---------- folders view ----------
let _currentFolderId = null;

// ---------- multi-select mode (for bulk-adding to a folder) ----------
let _selectionMode = false;
let _selectionTargetFolderId = null;
let _selectionTargetFolderName = "";
let _selectedClipIds = new Set();
let _alreadyInFolder = new Set();

function _updateSelectionUI() {
  const n = _selectedClipIds.size;
  els.selCount.textContent = String(n);
  els.selAdd.disabled = n === 0;
  els.selAdd.textContent = n === 0 ? "Dodaj zaznaczone" : `Dodaj ${n} ${clipsWord(n)}`;
}

async function startSelectionMode(folderId, folderName) {
  _selectionMode = true;
  _selectionTargetFolderId = folderId;
  _selectionTargetFolderName = folderName;
  _selectedClipIds.clear();
  // Pre-load which clips are already in the folder so we don't double-add
  try {
    const data = await fetch(`/api/folders/${folderId}/clips?limit=10000`).then((r) => r.json());
    _alreadyInFolder = new Set((data.clips || []).map((c) => c.id));
  } catch {
    _alreadyInFolder = new Set();
  }
  els.selectionBar.hidden = false;
  els.selFolderName.textContent = folderName;
  _updateSelectionUI();
  // Switch to clips view so the user can pick
  els.foldersIndex.hidden = true;
  els.folderDetail.hidden = true;
  setView("clips");
  // Re-render so cards show checkboxes
  await doSearch();
}

function exitSelectionMode() {
  _selectionMode = false;
  _selectionTargetFolderId = null;
  _selectionTargetFolderName = "";
  _selectedClipIds.clear();
  _alreadyInFolder.clear();
  els.selectionBar.hidden = true;
  doSearch();
}

async function saveSelection() {
  const folderId = _selectionTargetFolderId;
  const ids = Array.from(_selectedClipIds);
  if (!folderId || !ids.length) return;
  els.selAdd.disabled = true;
  els.selAdd.textContent = "Dodaję…";
  try {
    await Promise.all(
      ids.map((cid) =>
        fetch(`/api/folders/${folderId}/clips/${cid}`, { method: "POST" })
      )
    );
    toast(`Dodano ${ids.length} ${clipsWord(ids.length)} do folderu „${_selectionTargetFolderName}".`);
    const fid = folderId;
    exitSelectionMode();
    // Jump back into the folder detail view to show the result
    setView("folders");
    openFolder(fid);
  } catch (e) {
    toast(`Błąd dodawania: ${e.message}`, "error");
    _updateSelectionUI();
  }
}

els.selCancel.addEventListener("click", () => {
  exitSelectionMode();
  // return to the folder we came from
  if (_selectionTargetFolderId) {
    setView("folders");
    openFolder(_selectionTargetFolderId);
  }
});
els.selAdd.addEventListener("click", saveSelection);

// Toggle selection on card click (when selection mode is active).
// Search results can show the same clip in multiple cards (one per matching
// segment) — selection is per-CLIP, so we need to update every card with
// that clip_id, not just the one the user clicked.
function _syncCardVisualState(cid, isSelected) {
  document.querySelectorAll(`.result[data-clip-id="${cid}"]`).forEach((c) => {
    c.classList.toggle("selected", isSelected);
    const overlay = c.querySelector(".select-overlay");
    if (overlay) {
      overlay.classList.toggle("checked", isSelected);
      const check = overlay.querySelector(".select-check");
      if (check) check.textContent = isSelected ? "✓" : "";
    }
  });
}

els.results.addEventListener("click", (e) => {
  if (!_selectionMode) return;
  const card = e.target.closest(".result");
  if (!card) return;
  e.preventDefault();
  e.stopPropagation();
  const cid = parseInt(card.dataset.clipId, 10);
  const willBeSelected = !_selectedClipIds.has(cid);
  if (willBeSelected) {
    _selectedClipIds.add(cid);
  } else {
    _selectedClipIds.delete(cid);
  }
  _syncCardVisualState(cid, willBeSelected);
  _updateSelectionUI();
}, true);  // capture phase so we run before the normal openPlayer handler

function setView(view) {
  els.navItems.forEach((t) => t.classList.toggle("active", t.dataset.view === view));
  els.viewClips.hidden = view !== "clips";
  els.viewFolders.hidden = view !== "folders";
  els.viewFavorites.hidden = view !== "favorites";
  els.viewCloud.hidden = view !== "cloud";
  if (view === "folders" && _currentFolderId == null) {
    loadFoldersIndex();
  }
  if (view === "favorites") {
    loadFavorites();
  }
  if (view === "cloud") {
    loadCloud();
  }
}

els.navItems.forEach((t) =>
  t.addEventListener("click", () => {
    const view = t.dataset.view;
    if (view === "folders") {
      _currentFolderId = null;
      els.foldersIndex.hidden = false;
      els.folderDetail.hidden = true;
    }
    setView(view);
  })
);

async function loadFoldersIndex() {
  const folders = await fetch("/api/folders").then((r) => r.json());
  if (!folders.length) {
    els.foldersGrid.innerHTML = `
      <div class="folder-new-card" id="empty-new-folder">
        <div class="folder-new-card-inner">
          <div class="icon">+</div>
          <div>Utwórz pierwszy folder</div>
        </div>
      </div>`;
    document.getElementById("empty-new-folder")?.addEventListener("click", promptNewFolder);
    return;
  }
  els.foldersGrid.innerHTML = folders.map(folderCard).join("") + `
    <div class="folder-new-card" id="grid-new-folder">
      <div class="folder-new-card-inner">
        <div class="icon">+</div>
        <div>Nowy folder</div>
      </div>
    </div>`;
  document.getElementById("grid-new-folder")?.addEventListener("click", promptNewFolder);
  els.foldersGrid.querySelectorAll(".folder-card").forEach((card) => {
    card.addEventListener("click", () => openFolder(parseInt(card.dataset.folderId, 10)));
  });
}

function folderCard(f) {
  const sample = f.sample_clip_ids || [];
  let collage;
  if (sample.length === 0) {
    collage = `<div class="folder-empty">📁</div>`;
  } else if (sample.length === 1) {
    collage = `<div class="folder-collage single">
      <img src="/thumb/${sample[0]}" alt="" loading="lazy" onerror="this.outerHTML='<div class=&quot;placeholder&quot;>⏵</div>'" />
    </div>`;
  } else {
    const cells = sample.slice(0, 4).map((cid) =>
      `<img src="/thumb/${cid}" alt="" loading="lazy" onerror="this.outerHTML='<div class=&quot;placeholder&quot;>⏵</div>'" />`
    ).join("");
    collage = `<div class="folder-collage">${cells}</div>`;
  }
  return `
    <div class="folder-card" data-folder-id="${f.id}">
      ${collage}
      <div class="folder-card-body">
        <div class="folder-card-name">${escapeHtml(f.name)}</div>
        <div class="folder-card-meta">${f.clip_count} ${clipsWord(f.clip_count)}</div>
      </div>
    </div>`;
}

async function promptNewFolder() {
  const name = window.prompt("Nazwa nowego folderu:");
  if (!name || !name.trim()) return;
  try {
    const r = await fetch("/api/folders", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: name.trim() }),
    });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    toast(`Utworzono folder „${data.name}".`);
    await loadFoldersIndex();
  } catch (e) {
    toast(e.message, "error");
  }
}

async function openFolder(folderId) {
  _currentFolderId = folderId;
  els.foldersIndex.hidden = true;
  els.folderDetail.hidden = false;
  if (els.btnUploadFolder) els.btnUploadFolder.hidden = !_cloudConnected;
  const data = await fetch(`/api/folders/${folderId}/clips?sort=added`).then((r) => r.json());
  els.folderDetailName.textContent = data.folder.name;
  const n = data.clips.length;
  els.folderDetailCount.textContent = `${n} ${clipsWord(n)}`;
  if (!n) {
    els.folderDetailGrid.innerHTML =
      `<div class="empty">Folder jest pusty. Otwórz dowolny klip i kliknij <b>📁 Foldery</b> w playerze, żeby go dodać.</div>`;
  } else {
    els.folderDetailGrid.innerHTML = data.clips.map(clipCard).join("");
    els.folderDetailGrid.querySelectorAll(".result").forEach((el) =>
      el.addEventListener("click", () => openPlayer(parseInt(el.dataset.clipId, 10), 0))
    );
  }
}

els.btnNewFolder.addEventListener("click", promptNewFolder);
els.btnBackFolders.addEventListener("click", () => {
  _currentFolderId = null;
  els.folderDetail.hidden = true;
  els.foldersIndex.hidden = false;
  loadFoldersIndex();
});
els.btnRenameFolder.addEventListener("click", async () => {
  if (_currentFolderId == null) return;
  const oldName = els.folderDetailName.textContent;
  const newName = window.prompt("Nowa nazwa folderu:", oldName);
  if (!newName || newName.trim() === oldName) return;
  try {
    const r = await fetch(`/api/folders/${_currentFolderId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: newName.trim() }),
    });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    els.folderDetailName.textContent = newName.trim();
    toast("Zmieniono nazwę folderu.");
  } catch (e) {
    toast(e.message, "error");
  }
});
els.btnAddClipsToFolder.addEventListener("click", () => {
  if (_currentFolderId == null) return;
  startSelectionMode(_currentFolderId, els.folderDetailName.textContent);
});
els.btnDeleteFolder.addEventListener("click", async () => {
  if (_currentFolderId == null) return;
  const name = els.folderDetailName.textContent;
  const ok = await showConfirm({
    title: "Usunąć folder?",
    body: `Folder „${name}" zostanie usunięty. Klipy same w sobie zostają.`,
    warning: "Nie można cofnąć — będziesz musiał utworzyć folder ponownie i dodać klipy.",
    yesLabel: "Tak, usuń folder",
    yesClass: "danger",
  });
  if (!ok) return;
  try {
    const r = await fetch(`/api/folders/${_currentFolderId}`, { method: "DELETE" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    toast(`Usunięto folder „${data.deleted_name}".`);
    _currentFolderId = null;
    els.folderDetail.hidden = true;
    els.foldersIndex.hidden = false;
    loadFoldersIndex();
  } catch (e) {
    toast(e.message, "error");
  }
});

// ---------- heartbeat ----------
// Server auto-shuts down after 5 min of no heartbeat. We send one immediately and then
// every 30 s while the page is alive. Closing the tab silently stops the heartbeats.
function sendHeartbeat() {
  fetch("/api/heartbeat", { method: "POST" }).catch(() => {});
}
sendHeartbeat();
setInterval(sendHeartbeat, 30000);

// Reattach SSE if a transcription is already running on page load.
// Auto-scan the source folder on startup so new/removed clips show up immediately.
// On the very first launch, prompt the user to pick a clips folder.
(async function bootstrap() {
  try {
    const cfg = await fetch("/api/config").then((r) => r.json());
    if (!cfg.configured) {
      await openConfigModal({ mode: "first" });
      // bail — bootstrap will resume after the user saves (modal triggers full reload via doSearch/loadStats)
      return;
    }
  } catch (e) {
    console.warn("config check failed:", e);
  }
  try {
    await runScan();
  } catch (e) {
    console.warn("auto-scan failed:", e);
  }
  await loadStats();
  // Reflect cloud connection app-wide (toggles the per-card upload chips) so the
  // user doesn't have to open the Cloud tab first.
  fetch("/api/cloud/status")
    .then((r) => r.json())
    .then((st) => { setCloudConnected(!!st.connected); setCloudQuota(st); })
    .catch(() => {});
  const s = await fetch("/api/transcribe/status").then((r) => r.json());
  if (s.running) attachStream();
  await renderRecent("");

  // Ожидаем готовности моста между Python и JS
window.addEventListener('pywebviewready', function() {
  
  // Кнопка свернуть
  const btnMinimize = document.getElementById('btn-minimize');
  if (btnMinimize) {
    btnMinimize.addEventListener('click', () => {
      window.pywebview.api.minimize_window();
    });
  }

  // Кнопка закрыть
  const btnClose = document.getElementById('btn-close');
  if (btnClose) {
    btnClose.addEventListener('click', () => {
      window.pywebview.api.close_window();
    });
  }
  
});
// Надежная инициализация кнопок управления окном
function initTitlebar() {
  const btnMinimize = document.getElementById('btn-minimize');
  if (btnMinimize) {
    btnMinimize.addEventListener('click', () => {
      if (window.pywebview && window.pywebview.api) {
        window.pywebview.api.minimize_window();
      }
    });
  }

  const btnMaximize = document.getElementById('btn-maximize');
  if (btnMaximize) {
    btnMaximize.addEventListener('click', () => {
      if (window.pywebview && window.pywebview.api) {
        // Получаем размеры рабочей области без панели задач прямо из браузера
        const aw = window.screen.availWidth;
        const ah = window.screen.availHeight;
        // Координаты отступа (если панель задач сбоку или сверху)
        const al = window.screen.availLeft || 0;
        const at = window.screen.availTop || 0;
        
        // Отправляем эти безопасные цифры в Python
        window.pywebview.api.toggle_maximize_window(aw, ah, al, at);
      }
    });
  }

  const btnClose = document.getElementById('btn-close');
  if (btnClose) {
    btnClose.addEventListener('click', () => {
      if (window.pywebview && window.pywebview.api) {
        window.pywebview.api.close_window();
      }
    });
  }
}

if (window.pywebview && window.pywebview.api) {
  initTitlebar();
} else {
  window.addEventListener('pywebviewready', initTitlebar);
}
})();
