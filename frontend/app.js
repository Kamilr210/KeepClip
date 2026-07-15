const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => Array.from(document.querySelectorAll(sel));
if (!window.i18n && typeof i18n !== 'undefined') window.i18n = i18n;

const LANG = (() => {
  const SUPPORTED = ['en', 'uk', 'pl', 'ru', ];
  // 1. URL-параметр ?lang=
  try {
    const urlLang = new URLSearchParams(window.location.search).get('lang');
    if (urlLang && SUPPORTED.includes(urlLang)) return urlLang;
  } catch(e) {}
  // 2. localStorage
  try {
    const saved = localStorage.getItem('keepclip_lang');
    if (saved && SUPPORTED.includes(saved)) return saved;
  } catch(e) {}
  // 3. Язык браузера
  const bl = (navigator.language || '').slice(0, 2).toLowerCase();
  if (SUPPORTED.includes(bl)) return bl;
  return 'pl';
})();
const t = (key) => {
  if (!window.i18n) return key;
  const keys = key.split(".");
  let node = window.i18n[LANG] ?? window.i18n["pl"] ?? {};
  for (const k of keys) node = node?.[k];
  return (typeof node === 'string') ? node : key;
};
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
  replayOverlay: $("#replay-overlay"),
  replayBox: $("#replay-box"),
  replayDot: $("#replay-dot"),
  replayStatusText: $("#replay-status-text"),
  replayHotkeyWarn: $("#replay-hotkey-warn"),
  replayError: $("#replay-error"),
  replayEnabled: $("#replay-enabled"),
  replayBackground: $("#replay-background"),
  replayMic: $("#replay-mic"),
  replayDuration: $("#replay-duration"),
  replayFps: $("#replay-fps"),
  replayQuality: $("#replay-quality"),
  replayHotkey: $("#replay-hotkey"),
  replaySaveNow: $("#replay-save-now"),
  replayClose: $("#replay-close"),
  replayApply: $("#replay-apply"),
  replayNavDot: $("#replay-nav-dot"),
  replayAudioOutput: $("#replay-audio-output"),
  replayAudioInput: $("#replay-audio-input"),
  settingsOverlay: $("#settings-overlay"),
  settingsClose: $("#settings-close"),
  settingsOpenReplay: $("#settings-open-replay"),
  // --- НОВЫЕ ЭЛЕМЕНТЫ ПЛЕЕРА ---
  ctrlSnap: $("#ctrl-snap"),
  ctrlMute: $("#ctrl-mute"),
  ctrlStart: $("#ctrl-start"),
  ctrlRewind: $("#ctrl-rewind"),
  ctrlPlay: $("#ctrl-play"),
  ctrlForward: $("#ctrl-forward"),
  ctrlEnd: $("#ctrl-end"),
  ctrlFullscreen: $("#ctrl-fullscreen"),
  ctrlProgress: $("#ctrl-progress"),
  ctrlTimeCur: $("#ctrl-time-current"),
  ctrlTimeTot: $("#ctrl-time-total"),
  ctrlVolume: $("#ctrl-volume"),
  playerContainer: $("#custom-player-container"),
  playerBox: $("#player-box"),
  ctrlSpeed: $("#ctrl-speed"),
  ctrlCc: $("#ctrl-cc"),
  // -----------------------------
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
  els.gameFilter.innerHTML = '<option value="">' + t("search.allGames") + '</option>' +
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
    els.localFree.textContent = dFree > 0 ? t("storage.free").replace("{size}", fmtSize(dFree)) : "—";
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
      els.cloudFree.textContent = t("storage.free").replace("{size}", fmtSize(cFree));
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
        els.cloudFree.textContent = t("storage.notConnected");
        els.cloudFree.classList.add("text-red");
      } else {
        els.cloudFree.textContent = t("storage.unlimited");
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
  els.results.innerHTML = `<div class="empty">${t("search.searching").replace("{q}", escapeHtml(q))}</div>`;
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
    els.results.innerHTML = `<div class="empty">${t("search.noClips")}</div>`;
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
  const status = c.transcribed_at ? "" : ' · <span class="warn">' + t("clip.notTranscribed") + '</span>';
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
    els.results.innerHTML = `<div class="empty">${t("search.noResults")}</div>`;
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
    // The player button keeps its star in a .fav-star span (a bare textContent write
    // would wipe its "Ulubione" label); grid/search stars are the button's only text.
    document.querySelectorAll(`.fav-btn[data-fav-id="${clipId}"]`).forEach((b) => {
      b.classList.toggle("is-fav", isFav);
      const star = b.querySelector(".fav-star");
      if (star) star.textContent = isFav ? "★" : "☆";
      else b.textContent = isFav ? "★" : "☆";
    });
    loadStats();
    if (!els.viewFavorites.hidden) loadFavorites();
  } catch {
    toast(t("toast.folderAddError").replace("{error}", ""), "error");
  }
}

async function loadFavorites() {
  const clips = await fetch("/api/clips?favorite=1&limit=500").then((r) => r.json());
  els.favCount.textContent = clips.length ? `${clips.length} ${clipsWord(clips.length)}` : "";
  if (!clips.length) {
    els.favResults.innerHTML = `<div class="empty">${t("search.noFavorites")}</div>`;
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
    return `<button class="cloud-chip cloud-down" data-cloud-down-id="${c.id}" title="${t('cloud.downloadTooltip')}" aria-label="${t('cloud.downloadAria')}">${CLOUD_DOWN_GLYPH}</button>`;
  }
  return `<button class="cloud-chip cloud-up" data-cloud-up-id="${c.id}" title="${t('cloud.uploadTooltip')}" aria-label="${t('cloud.uploadTooltip')}">${CLOUD_UP_GLYPH}</button>`;
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
    b.title = t("cloud.downloadTooltip");
    b.setAttribute("aria-label", t("cloud.downloadAria"));
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
    b.title = t("cloud.uploadTooltip");
    b.setAttribute("aria-label", t("cloud.uploadTooltip"));
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
    toast(t("toast.cloudConnectFirst"), "error");
    return;
  }
  if (_busyClipIds.has(clipId)) return;  // an op for this clip is already running
  _busyClipIds.add(clipId);
  setClipChipsBusy(clipId, true);
  setPlayerCloudBusy(srcBtn, "☁ Wysyłam…");
  toast(t("toast.cloudUploading"));
  try {
    const r = await fetch(`/api/clips/${clipId}/upload`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    markClipCloudInDom(clipId);
    toast(data.already ? "" + t("toast.cloudAlreadyUploaded") : "" + t("toast.cloudUploaded"));
    loadStats();
    refreshCloudQuota();
    if (currentClipId === clipId) updatePlayerCloudBtn("cloud");
    if (!els.viewCloud.hidden) loadCloud();
  } catch (e) {
    toast(t("toast.cloudUploadFail").replace("{error}", e.message), "error");
    setClipChipsBusy(clipId, false);
    if (currentClipId === clipId) updatePlayerCloudBtn("local");
  } finally {
    _busyClipIds.delete(clipId);
  }
}

async function downloadClipFromCloud(clipId, srcBtn) {
  if (!_cloudConnected) {
    toast(t("toast.cloudConnectFirst"), "error");
    return;
  }
  if (_busyClipIds.has(clipId)) return;  // an op for this clip is already running
  _busyClipIds.add(clipId);
  setClipChipsBusy(clipId, true);
  setPlayerCloudBusy(srcBtn, "☁ Pobieram…");
  toast(t("toast.cloudDownloading"));
  try {
    const r = await fetch(`/api/clips/${clipId}/download`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    markClipLocalInDom(clipId);
    toast(data.already ? "" + t("toast.cloudAlreadyLocal") : "" + t("toast.cloudDownloaded"));
    loadStats();
    refreshCloudQuota();
    if (currentClipId === clipId) updatePlayerCloudBtn("local");
    if (!els.viewCloud.hidden) loadCloud();
  } catch (e) {
    toast(t("toast.cloudDownloadFail").replace("{error}", e.message), "error");
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

  // Иконка: Zdejmij z chmury (только SVG, без отступов)
  const svgDownload = `<svg style="width: 19px; height: 19px;" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><g id="SVGRepo_bgCarrier" stroke-width="0"></g><g id="SVGRepo_tracerCarrier" stroke-linecap="round" stroke-linejoin="round"></g><g id="SVGRepo_iconCarrier"> <path d="M12 22V16M12 22L14 20M12 22L10 20" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"></path> <path d="M22 13.3529C22 15.6958 20.5562 17.7055 18.5 18.5604M14.381 8.02721C14.9767 7.81911 15.6178 7.70588 16.2857 7.70588C16.9404 7.70588 17.5693 7.81468 18.1551 8.01498M7.11616 10.6089C6.8475 10.5567 6.56983 10.5294 6.28571 10.5294C3.91878 10.5294 2 12.4256 2 14.7647C2 16.6611 3.26124 18.2664 5 18.8061M7.11616 10.6089C6.88706 9.9978 6.7619 9.33687 6.7619 8.64706C6.7619 5.52827 9.32028 3 12.4762 3C15.4159 3 17.8371 5.19371 18.1551 8.01498M7.11616 10.6089C7.68059 10.7184 8.20528 10.9374 8.66667 11.2426M18.1551 8.01498C19.0446 8.31916 19.8345 8.83436 20.4633 9.5" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> </g></svg>`;

  // Иконка: Wyślij do chmury (только SVG, без отступов)
  const svgUpload = `<svg style="width: 19px; height: 19px;" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><g id="SVGRepo_bgCarrier" stroke-width="0"></g><g id="SVGRepo_tracerCarrier" stroke-linecap="round" stroke-linejoin="round"></g><g id="SVGRepo_iconCarrier"> <path d="M12 16V22M12 16L14 18M12 16L10 18" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"></path> <path d="M22 13.3529C22 15.6958 20.5562 17.7055 18.5 18.5604M14.381 8.02721C14.9767 7.81911 15.6178 7.70588 16.2857 7.70588C16.9404 7.70588 17.5693 7.81468 18.1551 8.01498M7.11616 10.6089C6.8475 10.5567 6.56983 10.5294 6.28571 10.5294C3.91878 10.5294 2 12.4256 2 14.7647C2 16.6611 3.26124 18.2664 5 18.8061M7.11616 10.6089C6.88706 9.9978 6.7619 9.33687 6.7619 8.64706C6.7619 5.52827 9.32028 3 12.4762 3C15.4159 3 17.8371 5.19371 18.1551 8.01498M7.11616 10.6089C7.68059 10.7184 8.20528 10.9374 8.66667 11.2426M18.1551 8.01498C19.0446 8.31916 19.8345 8.83436 20.4633 9.5" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> </g></svg>`;

  if (storage === "cloud") {
    b.hidden = false;
    b.classList.add("in-cloud");
    b.innerHTML = svgDownload; 
    b.title = "Pobierz klip z chmury z powrotem na dysk"; // Подсказка при наведении осталась
  } else if (_cloudConnected) {
    b.hidden = false;
    b.classList.remove("in-cloud");
    b.innerHTML = svgUpload;
    b.title = "Wyślij klip do chmury (Google Drive)"; // Подсказка при наведении осталась
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
    toast(t("toast.cloudConnectFirst"), "error");
    return;
  }
  const ok = await showConfirm({
    title: t("confirm.uploadFolder.title"),
    body: t("confirm.uploadFolder.body"),
    warning: t("confirm.uploadFolder.warning"),
    yesLabel: t("confirm.uploadFolder.yes"),
    yesClass: "primary",
  });
  if (!ok) return;
  els.btnUploadFolder.disabled = true;
  toast(t("toast.cloudFolderUploading"));
  try {
    const r = await fetch(`/api/folders/${folderId}/upload`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    let msg = t("toast.cloudFolderUploaded").replace("{uploaded}", data.uploaded).replace("{total}", data.total);
    if (data.skipped) msg +=  t("toast.cloudFolderSkipped").replace("{skipped}", data.skipped);
    if (data.failed) msg +=  t("toast.cloudFolderFailed").replace("{failed}", data.failed);
    toast(msg, data.failed ? "error" : "ok");
    loadStats();
    refreshCloudQuota();
    if (_currentFolderId === folderId) openFolder(folderId);
  } catch (e) {
    toast(t("toast.cloudFolderFail").replace("{error}", e.message), "error");
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

  els.cloudAccName.textContent = st.name || t("cloud.accountGoogle");
  els.cloudAccEmail.textContent = st.email || "";
  if (st.photo) { els.cloudAccPhoto.src = st.photo; els.cloudAccPhoto.hidden = false; }
  else { els.cloudAccPhoto.hidden = true; }
  if (st.error) { els.cloudAccError.hidden = false; els.cloudAccError.textContent = st.error; }
  else { els.cloudAccError.hidden = true; }

  const bar = els.cloudQuotaFill.parentElement;
  if (st.limit && st.usage != null) {
    const pct = Math.min(100, Math.round((st.usage / st.limit) * 100));
    els.cloudQuotaFill.style.width = `${pct}%`;
    els.cloudQuotaText.textContent = t("storage.quotaUsed").replace("{used}", fmtSize(st.usage)).replace("{total}", fmtSize(st.limit)).replace("{pct}", pct);
    bar.hidden = false;
  } else if (st.usage != null) {
    bar.hidden = true;
    els.cloudQuotaText.textContent = t("storage.quotaUnlimited").replace("{used}", fmtSize(st.usage));
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
    toast(t("toast.cloudOpenedLogin"));
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
        toast(t("toast.cloudConnected"));
      } else if (tries > 150) {
        _stopCloudPoll(); // ~5 min cap
      }
    }, 2000);
  } catch (e) {
    toast(t("toast.cloudConnectFail").replace("{error}", e.message), "error");
  } finally {
    els.cloudConnectBtn.disabled = false;
  }
}

if (els.cloudConnectBtn) els.cloudConnectBtn.addEventListener("click", startCloudConnect);
if (els.cloudRecheck) els.cloudRecheck.addEventListener("click", loadCloud);
if (els.cloudDisconnectBtn) {
  els.cloudDisconnectBtn.addEventListener("click", async () => {
    const ok = await showConfirm({
      title: t("confirm.disconnectCloud.title"),
      body: t("confirm.disconnectCloud.body"),
      warning: "",
      yesLabel: t("confirm.disconnectCloud.yes"),
      yesClass: "danger",
    });
    if (!ok) return;
    try { await fetch("/api/cloud/disconnect", { method: "POST" }); } catch {}
    setCloudConnected(false);
    setCloudQuota(null);
    _stopCloudPoll();
    loadCloud();
    toast(t("toast.cloudDisconnected"));
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
applyLayout((() => { try { return localStorage.getItem("keepclip_layout"); } catch { return null; } })() || "mosaic");

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
  // Only the star span — textContent on the button would wipe the "Ulubione" label.
  const pfavStar = els.pfav.querySelector(".fav-star");
  if (pfavStar) pfavStar.textContent = isFav ? "★" : "☆";
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
  rebuildSubtitles();

  els.segments.innerHTML = currentSegments.length
    ? currentSegments
        .map(
          (s) =>
            `<div class="seg" data-start="${s.start_s}" data-id="${s.id}">
               <div class="seg-ts">${fmtTime(s.start_s)}</div>
               <div class="seg-text">${escapeHtml(s.text)}</div>
               <button class="seg-edit" title="${t('player.editSegment')}" aria-label="${t('player.editSegment')}">✎</button>
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

// ---------- napisy na wideo (WebVTT budowane z transkrypcji) ----------
// Segmenty klipu są zamieniane w ścieżkę <track> (blob WebVTT), a napisy renderuje
// natywnie przeglądarka — idealna synchronizacja bez własnego timera, działa w
// fullscreen i przy zmianie prędkości. Przełącznik pamiętany w localStorage.
let _subsUrl = null; // blob URL bieżącej ścieżki (zwalniany przy podmianie/zamknięciu)

const subsEnabled = () => localStorage.getItem("kc_subtitles") === "1";

function vttTime(sec) {
  const h = Math.floor(sec / 3600);
  const m = Math.floor((sec % 3600) / 60);
  const s = (sec % 60).toFixed(3).padStart(6, "0");
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${s}`;
}

function clearSubtitles() {
  els.video.querySelectorAll("track").forEach((tr) => tr.remove());
  if (_subsUrl) { URL.revokeObjectURL(_subsUrl); _subsUrl = null; }
}

// (Od)buduj ścieżkę napisów z currentSegments — wołane przy otwarciu klipu i po
// edycji linijki transkrypcji, żeby poprawka była widoczna na wideo od razu.
function rebuildSubtitles() {
  if (!els.ctrlCc) return;
  clearSubtitles();

  const has = currentSegments.length > 0;
  els.ctrlCc.disabled = !has;
  els.ctrlCc.classList.toggle("cc-on", has && subsEnabled());
  if (!has) return;

  // VTT traktuje <, & jako początek znaczników — escapujemy tekst użytkownika.
  const esc = (t) => t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  let vtt = "WEBVTT\n\n";
  for (const s of currentSegments) {
    // Cue nie może mieć zerowej/ujemnej długości — wymuś minimum 0.3 s.
    const end = Math.max(s.end_s, s.start_s + 0.3);
    vtt += `${vttTime(s.start_s)} --> ${vttTime(end)}\n${esc(s.text)}\n\n`;
  }
  _subsUrl = URL.createObjectURL(new Blob([vtt], { type: "text/vtt" }));
  const track = document.createElement("track");
  track.kind = "subtitles";
  track.label = "Transkrypcja";
  track.src = _subsUrl;
  els.video.appendChild(track);
  track.track.mode = subsEnabled() ? "showing" : "hidden";
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
      <span class="seg-edit-hint">${t('player.editHint')}</span>
      <button class="btn ghost seg-edit-cancel">${t('nav.cancel')}</button>
      <button class="btn primary seg-edit-save">${t('player.editSave')}</button>
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
    saveBtn.textContent = t("player.editSaving");
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
      rebuildSubtitles();   // poprawiona linijka od razu trafia do napisów na wideo
      finish();
      toast(t("toast.editSaved"));
    } catch (e) {
      saveBtn.disabled = false;
      saveBtn.textContent = t("folders.addBtn");
      toast(t("toast.editSaveError").replace("{error}", e.message), "error");
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
  // Zamknięcie ✕ w trybie pełnoekranowym musi też opuścić fullscreen,
  // inaczej zostaje czarny pełny ekran bez niczego.
  if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
  els.overlay.hidden = true;
  els.video.pause();
  els.video.removeAttribute("src");
  clearSubtitles();
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
    // W fullscreen Esc obsługuje natywnie przeglądarka (wychodzi z pełnego
    // ekranu) — nie zamykaj wtedy całego odtwarzacza w tym samym naciśnięciu.
    if (document.fullscreenElement) return;
    if (!els.confirmOverlay.hidden) hideConfirm();
    else if (!els.overlay.hidden && editingSegId === null) closePlayer();
  }
});

// ---------- confirm modal + toast ----------
let confirmResolver = null;
const _confirmWarnEl = document.querySelector("#confirm-box .confirm-warn");
const _defaultWarn = _confirmWarnEl ? _confirmWarnEl.innerHTML : "";

function showConfirm({
  title = t("confirm.retranscribeAll.title"),
  body = "",
  warning = null,
  yesLabel = t("confirm.deleteClip.yes"),
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
    els.cutEstimate.textContent = t("cutEstimate.selectRange");
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
      els.cutEstimate.innerHTML = t("cutEstimate.noCompressionSize").replace("{mb}", estMb.toFixed(1));
    } else {
      els.cutEstimate.innerHTML = t("cutEstimate.noCompression");
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
    els.cutEstimate.innerHTML = t("cutEstimate.tooSmall").replace("{mb}", _cutTargetMb).replace("{sec}", duration.toFixed(1));
    els.cutEstimate.classList.add("warn");
    return;
  }
  const videoKbps = Math.floor((videoBytes * 8) / duration / 1000);
  let quality = t("cutEstimate.quality.great");
  if (videoKbps < 1500) quality = t("cutEstimate.quality.bad");
  else if (videoKbps < 3000) quality = t("cutEstimate.quality.medium");
  else if (videoKbps < 6000) quality = t("cutEstimate.quality.good");
  els.cutEstimate.innerHTML = t("cutEstimate.summary").replace("{mb}", _cutTargetMb).replace("{kbps}", videoKbps).replace("{quality}", quality);
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
    els.cutError.textContent = t("toast.cutError");
    els.cutError.hidden = false;
    return;
  }
  els.cutError.hidden = true;
  els.cutGo.disabled = true;
  els.cutGo.textContent = t("toast.cutting");
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
    els.cutGo.textContent = t("toast.cutGoBtn");
  }
});

function showCutSuccess(data) {
  // Re-use the confirm modal as a "done" dialog with custom buttons
  const ok = showConfirm({
    title: t("cutSuccess.title"),
    body: `${data.output_name} — ${data.size_mb} MB`,
    warning: `Plik zapisano w folderze:<br><code>${escapeHtml(data.cuts_root)}</code>`,
    yesLabel: t("cutSuccess.showInFolder"),
    yesClass: "primary",
  });
  // Override the cancel button text to "Zamknij" for this dialog
  els.confirmNo.textContent = t("confirm.closeBtn");
  ok.then(async (clicked) => {
    els.confirmNo.textContent = t("confirm.cancelBtn");
    if (clicked) {
      try {
        await fetch("/api/show-in-explorer", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ path: data.output_path }),
        });
      } catch (e) {
        toast(t("toast.showFolderFail").replace("{error}", e.message), "error");
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
    html += `<div class="folders-dropdown-empty">${t('folders.noFolders')}</div>`;
  } else {
    html += allFolders.map((f) => `
      <label class="folders-dropdown-item">
        <input type="checkbox" data-folder-id="${f.id}" ${memberSet.has(f.id) ? "checked" : ""} />
        <span>${escapeHtml(f.name)}</span>
      </label>`).join("");
  }
  html += `
    <div class="folders-dropdown-new">
      <input id="player-new-folder-name" type="text" placeholder="${t('folders.newFolderPlaceholder')}" autocomplete="off" />
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
      toast(t("toast.folderCreated").replace("{name}", data.name));
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
  els.pretrans.textContent = t("toast.retranscribeBtn");
  try {
    const r = await fetch(`/api/clips/${currentClipId}/retranscribe`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    toast(t("toast.transcribeRetranscribed").replace("{segments}", data.segments).replace("{seconds}", data.seconds));
    const cid = currentClipId;
    closePlayer();
    setTimeout(() => openPlayer(cid, 0), 200);
    await loadStats();
    doSearch(); // odśwież siatkę pod odtwarzaczem, żeby klip stracił etykietę „nie transkrybowane"
  } catch (e) {
    toast(t("toast.transcribeError").replace("{error}", e.message), "error");
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
    title: t("confirm.fixClip.title"),
    body: filename,
    warning: t("confirm.fixClip.warning"),
    yesLabel: t("confirm.fixClip.yes"),
    yesClass: "primary",
  });
  if (!ok) return;
  els.pfix.disabled = true;
  els.pfix.textContent = t("playerActions.fixing");
  try {
    const r = await fetch(`/api/clips/${currentClipId}/fix`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    const msg = data.trimmed_seconds > 0
      ? t("toast.fixRepaired").replace("{seconds}", data.trimmed_seconds.toFixed(0))
      : t("toast.fixRemuxed");
    toast(msg);
    // reload video with cache-busting param so the browser refetches
    const cid = currentClipId;
    closePlayer();
    setTimeout(() => openPlayer(cid, 0), 200);
    await loadStats();
  } catch (e) {
    toast(t("toast.fixFail").replace("{error}", e.message), "error");
  } finally {
    els.pfix.disabled = false;
    els.pfix.textContent = "🔧 " + t("playerActions.fix");
  }
});

// ---------- delete clip ----------
els.pdelete.addEventListener("click", async () => {
  if (!currentClipId) return;
  const filename = els.pfile.textContent;
  const ok = await showConfirm({
    title: t("confirm.deleteClip.title"),
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
      toast(t("toast.deleteSuccess").replace("{filename}", data.filename));
    } else if (data.trash_error) {
      toast(t("toast.deleteTrashFail").replace("{error}", data.trash_error), "error");
    } else {
      toast(t("toast.deleteSuccessOnly").replace("{filename}", data.filename));
    }
    await loadStats();
    await doSearch();
  } catch (e) {
    toast(t("toast.deleteError").replace("{error}", e.message), "error");
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
  if (r.added) parts.push(t("toast.scanParts.added").replace("{n}", r.added));
  if (r.removed) parts.push(t("toast.scanParts.removed").replace("{n}", r.removed));
  if (!parts.length) parts.push(t("toast.scanParts.noChanges"));
  if (r.added || r.removed) {
    toast(`Skan: ${parts.join(", ")}`);
  } else if (showToastAlways) {
    toast(t("toast.scanNoChanges"));
  }
  return r;
}

els.scan.addEventListener("click", async () => {
  els.scan.disabled = true;
  els.scanLabel.textContent = t("toast.scanning");
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
      els.scanLabel.textContent = t("toast.scanLabel");
    }, 2000);
  }
});

let sse = null;
async function startTranscribe(force = false) {
  await fetch("/api/scan", { method: "POST" });
  const url = force ? "/api/transcribe/start?force=true" : "/api/transcribe/start";
  const r = await fetch(url, { method: "POST" }).then((r) => r.json());
  if (!r.started) {
    toast(t("toast.transcribeAlreadyRunning"), "error");
    return;
  }
  // Nothing to do: skip the progress bar entirely and just notify at the bottom.
  if (r.total === 0) {
    toast(force ? t("toast.transcribeNoAll") : t("toast.transcribeNoNew"));
    return;
  }
  toast(force ? t("toast.transcribeStartedAll") : t("toast.transcribeStartedNew"));
  attachStream();
}
els.trans.addEventListener("click", () => startTranscribe(false));

async function retranscribeAll() {
  const stats = await fetch("/api/stats").then((r) => r.json());
  const ok = await showConfirm({
    title: t("confirm.retranscribeAll.title"),
    body: t("confirm.retranscribeAll.body").replace("{clips}", stats.clips),
    warning: t("confirm.retranscribeAll.warning"),
    yesLabel: t("confirm.retranscribeAll.yes"),
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
    else if (action === "replay") openReplayModal();
  });
});

// ---------- settings button in nav ----------
const btnSettings = document.getElementById("btn-settings");
if (btnSettings) btnSettings.addEventListener("click", openSettingsOverlay);

// ---------- settings overlay ----------
function openSettingsOverlay() {
  if (els.settingsOverlay) els.settingsOverlay.hidden = false;
}
function closeSettingsOverlay() {
  if (els.settingsOverlay) els.settingsOverlay.hidden = true;
}
if (els.settingsClose) els.settingsClose.addEventListener("click", closeSettingsOverlay);
if (els.settingsOverlay) {
  els.settingsOverlay.addEventListener("click", (e) => {
    if (e.target === els.settingsOverlay) closeSettingsOverlay();
  });
}

// "Change folder" button inside settings overlay
document.querySelectorAll(".settings-folder-btn[data-action='change-folder']").forEach((btn) => {
  btn.addEventListener("click", () => {
    closeSettingsOverlay();
    openConfigModal({ mode: "change" });
  });
});

// "Open replay settings" button inside settings overlay
if (els.settingsOpenReplay) {
  els.settingsOpenReplay.addEventListener("click", () => {
    closeSettingsOverlay();
    openReplayModal();
  });
}

// ---------- audio device enumeration ----------
async function populateAudioDevices() {
  if (!els.replayAudioOutput || !els.replayAudioInput) return;
  try {
    // Try backend API first (Python can enumerate WASAPI devices)
    const r = await fetch("/api/replay/audio-devices");
    if (r.ok) {
      const data = await r.json();
      fillDeviceSelect(els.replayAudioOutput, data.outputs || [], t("replay.audioDefault"));
      fillDeviceSelect(els.replayAudioInput,  data.inputs  || [], t("replay.audioDefault"));
      return;
    }
  } catch { /* fall through to Web Audio */ }
  // Fallback: browser MediaDevices (may be limited in pywebview without permissions)
  try {
    await navigator.mediaDevices.getUserMedia({ audio: true }).catch(() => {});
    const devices = await navigator.mediaDevices.enumerateDevices();
    const outputs = devices.filter(d => d.kind === "audiooutput");
    const inputs  = devices.filter(d => d.kind === "audioinput");
    fillDeviceSelect(els.replayAudioOutput, outputs.map(d => ({ id: d.deviceId, name: d.label || d.deviceId })), t("replay.audioDefault"));
    fillDeviceSelect(els.replayAudioInput,  inputs.map(d => ({ id: d.deviceId, name: d.label  || d.deviceId })), t("replay.audioDefault"));
  } catch { /* no audio permissions */ }
}

function fillDeviceSelect(sel, devices, defaultLabel) {
  if (!sel) return;
  const current = sel.value;
  // Clear all except first (default) option
  while (sel.options.length > 1) sel.remove(1);
  sel.options[0].textContent = defaultLabel;
  devices.forEach(d => {
    if (!d.id && !d.name) return;
    const opt = document.createElement("option");
    opt.value = d.id || d.name;
    opt.textContent = d.name || d.id;
    sel.appendChild(opt);
  });
  if (current) sel.value = current; // restore selection if possible
}

// ---------- config modal (first-launch + change folder) ----------
async function openConfigModal({ mode = "change" } = {}) {
  const cfg = await fetch("/api/config").then((r) => r.json());
  if (mode === "first") {
    els.configTitle.textContent = t("config.titleFirst");
    els.configIntro.innerHTML =
      t("config.introFirst");
    els.configCancel.hidden = true;
    els.configSave.textContent = t("config.saveFirst");
  } else {
    els.configTitle.textContent = t("config.titleChange");
    els.configIntro.innerHTML =
      t("config.introChange");
    els.configCancel.hidden = false;
    els.configSave.textContent = t("config.saveChange");
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
      toast(t("toast.folderPickerDesktopOnly"), "error");
    }
  });
}

els.configSave.addEventListener("click", async () => {
  const newPath = els.configPath.value.trim();
  if (!newPath) {
    els.configError.textContent = t("toast.configPathError");
    els.configError.hidden = false;
    return;
  }
  els.configSave.disabled = true;
  els.configSave.textContent = t("config.checking");
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
    els.configSave.textContent = els.configCancel.hidden ? t("config.saveFirst") : t("config.saveChange");
  }
});
// ---------- instant replay (rolling buffer + hotkey) ----------
// Friendly labels for the encoder ffmpeg actually initialized with.
const REPLAY_ENC_LABELS = {
  hevc_nvenc: "NVENC HEVC (GPU NVIDIA)",
  hevc_amf: "AMF HEVC (GPU AMD)",
  hevc_qsv: "QuickSync HEVC (GPU Intel)",
  // H.264 names kept for older buffers / the CPU fallback.
  h264_nvenc: "NVENC (GPU NVIDIA)",
  h264_amf: "AMF (GPU AMD)",
  h264_qsv: "QuickSync (GPU Intel)",
  libx264: "x264 (CPU)",
};

let _replayPoll = null; // fast poll while the modal is open

function renderReplayStatus(st) {
  // sidebar dot: green = buffer actively recording
  if (els.replayNavDot) els.replayNavDot.hidden = !(st.enabled && st.running);
  if (!els.replayBox || els.replayOverlay.hidden) return;

  // The tuning grid stays editable the whole time the modal is open — the 3 s status
  // poll must NOT grey it based on server `enabled`, or it clobbers the user's just-
  // toggled-but-not-yet-saved checkbox (settings flashed available, then greyed out).
  els.replaySaveNow.disabled = !st.running || st.saving;

  els.replayDot.className = "replay-dot" + (st.error ? " err" : st.running ? " on" : "");
  let text;
  if (st.saving) text = t("replay.stSaving");
  else if (st.running) {
    const enc = REPLAY_ENC_LABELS[st.encoder] || st.encoder || "?";
    text = t("replay.stOn").replace("{enc}", enc + (st.audio ? "" : " · bez audio"));
  } else if (st.enabled && !st.error) text = t("replay.stStarting");
  else text = t("replay.stOff");
  els.replayStatusText.textContent = text;

  els.replayError.hidden = !st.error;
  els.replayError.textContent = st.error || "";
  // enabled + running but Windows refused the combo → another app owns it
  const hotkeyDead = st.enabled && st.running && !st.hotkey_active;
  els.replayHotkeyWarn.hidden = !hotkeyDead;
  els.replayHotkeyWarn.textContent = hotkeyDead ? t("replay.stHotkeyDead") : "";
}

async function refreshReplayStatus() {
  try {
    const st = await fetch("/api/replay/status").then((r) => r.json());
    renderReplayStatus(st);
    return st;
  } catch {
    return null;
  }
}

async function openReplayModal() {
  const st = await refreshReplayStatus();   // modal still hidden → only updates the sidebar dot
  if (st) {
    els.replayEnabled.checked = !!st.enabled;
    if (els.replayBackground) els.replayBackground.checked = !!st.background;
    els.replayMic.checked = !!st.mic_enabled;
    els.replayDuration.value = String(st.duration_s);
    if (!els.replayDuration.value) els.replayDuration.value = "120"; // non-preset value from settings.json
    els.replayFps.value = String(st.fps);
    els.replayQuality.value = st.quality;
    els.replayHotkey.value = st.hotkey;
    // Restore saved audio device selections after populating
    await populateAudioDevices();
    if (st.audio_output && els.replayAudioOutput) els.replayAudioOutput.value = st.audio_output;
    if (st.audio_input  && els.replayAudioInput)  els.replayAudioInput.value  = st.audio_input;
  }
  els.replayOverlay.hidden = false;          // show BEFORE rendering, so the render isn't skipped
  if (st) renderReplayStatus(st);
  clearInterval(_replayPoll);
  _replayPoll = setInterval(refreshReplayStatus, 3000);
}

function closeReplayModal() {
  els.replayOverlay.hidden = true;
  clearInterval(_replayPoll);
  _replayPoll = null;
  stopHotkeyCapture();
}

els.replayClose.addEventListener("click", closeReplayModal);
els.replayOverlay.addEventListener("click", (e) => {
  if (e.target === els.replayOverlay) closeReplayModal();
});

// (No live grey-out: the tuning grid is always editable while the modal is open.)

// --- hotkey capture: click the field, press a combo, Esc cancels ---
let _hotkeyPrev = null;

function stopHotkeyCapture() {
  if (_hotkeyPrev === null) return;
  els.replayHotkey.classList.remove("capturing");
  els.replayHotkey.blur();
  _hotkeyPrev = null;
}

els.replayHotkey.addEventListener("click", () => {
  _hotkeyPrev = els.replayHotkey.value;
  els.replayHotkey.classList.add("capturing");
  els.replayHotkey.value = t("replay.hotkeyHint");
});

els.replayHotkey.addEventListener("blur", () => {
  if (_hotkeyPrev !== null) {
    els.replayHotkey.value = _hotkeyPrev;
    stopHotkeyCapture();
  }
});

els.replayHotkey.addEventListener("keydown", (e) => {
  if (_hotkeyPrev === null) return;
  e.preventDefault();
  e.stopPropagation();
  if (e.key === "Escape") {
    els.replayHotkey.value = _hotkeyPrev;
    stopHotkeyCapture();
    return;
  }
  if (["Control", "Alt", "Shift", "Meta"].includes(e.key)) return; // wait for the real key
  // Translate the DOM key to the WinForms Keys name the backend parses.
  let key = e.key;
  if (/^[a-z]$/i.test(key)) key = key.toUpperCase();
  else if (/^[0-9]$/.test(key)) key = "D" + key;
  else if (key === " ") key = "Space";
  else if (key.startsWith("Arrow")) key = key.slice(5); // ArrowUp → Up
  const mods = [];
  if (e.ctrlKey) mods.push("Ctrl");
  if (e.altKey) mods.push("Alt");
  if (e.shiftKey) mods.push("Shift");
  if (e.metaKey) mods.push("Win");
  if (!mods.length) return; // a bare key would hijack normal typing system-wide
  els.replayHotkey.value = [...mods, key].join("+");
  _hotkeyPrev = null;
  els.replayHotkey.classList.remove("capturing");
  els.replayHotkey.blur();
});

els.replayApply.addEventListener("click", async () => {
  els.replayApply.disabled = true;
  els.replayApply.textContent = t("replay.applying");
  try {
    const r = await fetch("/api/replay/config", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        enabled: els.replayEnabled.checked,
        background: els.replayBackground ? els.replayBackground.checked : null,
        mic: els.replayMic.checked,
        duration_s: parseInt(els.replayDuration.value, 10),
        fps: parseInt(els.replayFps.value, 10),
        quality: els.replayQuality.value,
        hotkey: els.replayHotkey.value,
        audio_output: els.replayAudioOutput ? (els.replayAudioOutput.value || null) : null,
        audio_input:  els.replayAudioInput  ? (els.replayAudioInput.value  || null) : null,
      }),
    });
    // Empty/non-JSON body (e.g. 404 from an app running an older backend) must not
    // explode into a JSON-parse error — surface the HTTP status instead.
    let st = null;
    try { st = await r.json(); } catch { }
    if (!r.ok) throw new Error((st && st.detail) || `${r.status} ${r.statusText}`.trim());
    if (!st) throw new Error("pusta odpowiedź serwera");
    renderReplayStatus(st);
    toast(t("replay.cfgSaved"));
  } catch (e) {
    toast(t("replay.cfgFail").replace("{error}", e.message), "error");
  } finally {
    els.replayApply.disabled = false;
    els.replayApply.textContent = t("replay.apply");
  }
});

els.replaySaveNow.addEventListener("click", async () => {
  els.replaySaveNow.disabled = true;
  try {
    const r = await fetch("/api/replay/save", { method: "POST" });
    let data = null;
    try { data = await r.json(); } catch { }
    if (!r.ok) throw new Error((data && data.detail) || `${r.status} ${r.statusText}`.trim());
    if (!data) throw new Error("pusta odpowiedź serwera");
    toast(t("replay.savedToast").replace("{file}", data.file));
    loadStats();
    doSearch();
  } catch (e) {
    toast(t("replay.saveFailToast").replace("{error}", e.message), "error");
  } finally {
    els.replaySaveNow.disabled = false;
    refreshReplayStatus();
  }
});

// Sidebar dot stays honest even with the modal closed (buffer dies, app restart…).
refreshReplayStatus();
setInterval(refreshReplayStatus, 15000);

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
      doSearch(); // odśwież siatkę także w trybie pollingu (gdy SSE padło), tak jak robi to happy-path SSE
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
  els.selAdd.textContent = t("selection.adding");
  try {
    await Promise.all(
      ids.map((cid) =>
        fetch(`/api/folders/${folderId}/clips/${cid}`, { method: "POST" })
      )
    );
    toast(t("toast.folderAdded").replace("{n}", ids.length).replace("{word}", t("selection.selectedWord")).replace("{folder}", _selectionTargetFolderName));
    const fid = folderId;
    exitSelectionMode();
    // Jump back into the folder detail view to show the result
    setView("folders");
    openFolder(fid);
  } catch (e) {
    toast(t("toast.folderAddError").replace("{error}", e.message), "error");
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
  const name = window.prompt(t("folders.promptName"));
  if (!name || !name.trim()) return;
  try {
    const r = await fetch("/api/folders", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: name.trim() }),
    });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    toast(t("toast.folderCreateStandalone").replace("{name}", data.name));
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
      `<div class="empty">${t("folders.emptyFolder")}</div>`;
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
  const newName = window.prompt(t("folders.promptRename"), oldName);
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
    toast(t("toast.folderRenamed"));
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
    title: t("confirm.deleteFolder.title"),
    body: t("confirm.deleteFolder.body").replace("{name}", name),
    warning: t("confirm.deleteFolder.warning"),
    yesLabel: t("confirm.deleteFolder.yes"),
    yesClass: "danger",
  });
  if (!ok) return;
  try {
    const r = await fetch(`/api/folders/${_currentFolderId}`, { method: "DELETE" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    toast(t("toast.folderDeleted").replace("{name}", data.deleted_name));
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
// =========================================
// ПОЛНАЯ ЛОГИКА КАСТОМНОГО ПЛЕЕРА
// =========================================

const playerIcons = {
  play: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>`,
  pause: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/></svg>`,
  volOn: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z"/></svg>`,
  volOff: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3z"/></svg>`
};

if (els.video) {
  // 1. Play / Pause
  const togglePlay = () => els.video.paused ? els.video.play() : els.video.pause();
  els.ctrlPlay.addEventListener("click", togglePlay);
  els.video.addEventListener("click", togglePlay);
  
  els.video.addEventListener("play", () => els.ctrlPlay.innerHTML = playerIcons.pause);
  els.video.addEventListener("pause", () => els.ctrlPlay.innerHTML = playerIcons.play);

  // 2. Управление временем (Перемотка)
  els.ctrlStart.addEventListener("click", () => els.video.currentTime = 0);
  els.ctrlEnd.addEventListener("click", () => els.video.currentTime = els.video.duration);
  els.ctrlRewind.addEventListener("click", () => els.video.currentTime = Math.max(0, els.video.currentTime - 5));
  els.ctrlForward.addEventListener("click", () => els.video.currentTime = Math.min(els.video.duration, els.video.currentTime + 5));

  // 3. Dźwięk — klik na głośnik otwiera suwak głośności.
  // UWAGA: selektor musi być zakotwiczony przy TYM przycisku — okno „Wytnij” ma
  // własny .volume-mixer-popover wcześniej w DOM i document.querySelector łapał
  // tamten (ukryty) popover, przez co klik w głośnik "nic nie robił".
  const volumePopover = els.ctrlMute?.closest(".volume-control-wrap")
    ?.querySelector(".volume-mixer-popover");

  if (els.ctrlMute && volumePopover && els.ctrlVolume) {

    // Otwórz/zamknij suwak; przy otwarciu pokaż aktualną głośność.
    els.ctrlMute.addEventListener("click", (e) => {
      e.stopPropagation(); // nie doklikuj do document (zamknąłby popover od razu)
      els.ctrlVolume.value = els.video.muted ? 0 : els.video.volume;
      volumePopover.classList.toggle("show");
    });

    // Klik/przeciąganie samego suwaka nie może zamykać popovera.
    volumePopover.addEventListener("click", (e) => {
      e.stopPropagation();
    });

    // Klik gdziekolwiek indziej zamyka popover.
    document.addEventListener("click", () => {
      volumePopover.classList.remove("show");
    });

    // Zmiana głośności suwakiem; 0 = wyciszenie (ikona to odzwierciedla).
    els.ctrlVolume.addEventListener("input", (e) => {
      const vol = parseFloat(e.target.value);
      els.video.volume = vol;
      els.video.muted = (vol === 0);
      els.ctrlMute.innerHTML = els.video.muted ? playerIcons.volOff : playerIcons.volOn;
    });

  } else {
    console.error("Brak elementów suwaka głośności w HTML!");
  }

  // 4. Pełny ekran — celem jest CAŁY #player-box (nagłówek z ✕ + wideo + panel
  // sterowania z paskiem przewijania), nie sam kontener wideo: fullscreen na
  // kontenerze zostawiał użytkownika bez seeka, prędkości i widocznego wyjścia.
  const fsIcons = {
    expand: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z"/></svg>`,
    compress: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z"/></svg>`,
  };
  const toggleFullscreen = () => {
    if (!document.fullscreenElement) {
      els.playerBox.requestFullscreen().catch(err => console.log(err));
    } else {
      document.exitFullscreen();
    }
  };
  els.ctrlFullscreen.addEventListener("click", toggleFullscreen);

  // 4a. Auto-chowanie pasków w fullscreen: po ~2,5 s bezruchu myszy nagłówek
  // i panel sterowania znikają (są nakładkami — obraz cały czas zajmuje pełny
  // ekran), wracają przy ruchu myszy/klawiszu. Przy pauzie zostają widoczne.
  let _fsChromeTimer = null;
  function fsShowChrome() {
    els.playerBox.classList.remove("chrome-hidden");
    clearTimeout(_fsChromeTimer);
    if (document.fullscreenElement && !els.video.paused) {
      _fsChromeTimer = setTimeout(() => els.playerBox.classList.add("chrome-hidden"), 2500);
    }
  }
  els.playerBox.addEventListener("mousemove", () => {
    if (document.fullscreenElement) fsShowChrome();
  });
  els.video.addEventListener("pause", fsShowChrome);
  els.video.addEventListener("play", () => {
    if (document.fullscreenElement) fsShowChrome();
  });
  document.addEventListener("fullscreenchange", () => {
    els.ctrlFullscreen.innerHTML = document.fullscreenElement ? fsIcons.compress : fsIcons.expand;
    if (document.fullscreenElement) fsShowChrome();
    else {
      clearTimeout(_fsChromeTimer);
      els.playerBox.classList.remove("chrome-hidden");
    }
  });

  // 4b. Prędkość odtwarzania — przycisk cyklicznie przełącza typowe wartości;
  // nowy klip zawsze startuje od 1× (loadedmetadata niżej).
  const SPEEDS = [1, 1.25, 1.5, 2, 0.5, 0.75];
  const setSpeed = (v) => {
    els.video.playbackRate = v;
    els.ctrlSpeed.textContent = `${v}×`;
  };
  els.ctrlSpeed.addEventListener("click", () => {
    const i = SPEEDS.indexOf(els.video.playbackRate);
    setSpeed(SPEEDS[(i + 1) % SPEEDS.length] ?? 1);
  });

  // 4c. Napisy z transkrypcji — przełącznik; wybór pamiętany między klipami.
  if (els.ctrlCc) {
    els.ctrlCc.addEventListener("click", () => {
      const on = !subsEnabled();
      localStorage.setItem("kc_subtitles", on ? "1" : "0");
      const tr = els.video.querySelector("track");
      if (tr) tr.track.mode = on ? "showing" : "hidden";
      els.ctrlCc.classList.toggle("cc-on", on && !els.ctrlCc.disabled);
    });
  }

  // 5. Длинный ползунок времени
  let isDragging = false;
  
  els.video.addEventListener("loadedmetadata", () => {
    els.ctrlProgress.max = els.video.duration;
    els.ctrlTimeTot.textContent = fmtTime(els.video.duration);
    setSpeed(1); // każdy klip startuje w normalnym tempie
  });

  els.video.addEventListener("timeupdate", () => {
    if (!isDragging) {
      els.ctrlProgress.value = els.video.currentTime;
      els.ctrlTimeCur.textContent = fmtTime(els.video.currentTime);
    }
  });

  els.ctrlProgress.addEventListener("mousedown", () => isDragging = true);
  els.ctrlProgress.addEventListener("input", () => {
    els.ctrlTimeCur.textContent = fmtTime(els.ctrlProgress.value);
  });
  els.ctrlProgress.addEventListener("change", () => {
    els.video.currentTime = els.ctrlProgress.value;
    isDragging = false;
  });

  // 6. ФИЧА: Кнопка создания скриншота
  els.ctrlSnap.addEventListener("click", () => {
    const canvas = document.createElement("canvas");
    canvas.width = els.video.videoWidth;
    canvas.height = els.video.videoHeight;
    const ctx = canvas.getContext("2d");
    ctx.drawImage(els.video, 0, 0, canvas.width, canvas.height);
    
    // Скачиваем картинку
    const a = document.createElement("a");
    a.href = canvas.toDataURL("image/jpeg");
    a.download = `KeepClip_Screenshot_${fmtTime(els.video.currentTime).replace(':','-')}.jpg`;
    a.click();
    toast(t("toast.screenshotSaved")); // Используем твою функцию toast
  });
}

// Управление пробелом для плеера
document.addEventListener("keydown", (e) => {
  if (e.key === " " && !els.overlay.hidden && !["INPUT", "TEXTAREA"].includes(e.target.tagName)) {
    e.preventDefault();
    if(els.video) els.video.paused ? els.video.play() : els.video.pause();
  }
});
/* ── Кастомный плеер для окна "Wytnij fragment" ── */
(function() {
  const video = document.getElementById('cut-video');
  const btnPlay    = document.getElementById('cut-ctrl-play');
  const btnMute    = document.getElementById('cut-ctrl-mute');
  const btnStart   = document.getElementById('cut-ctrl-start');
  const btnRewind  = document.getElementById('cut-ctrl-rewind');
  const btnForward = document.getElementById('cut-ctrl-forward');
  const btnEnd     = document.getElementById('cut-ctrl-end');
  const volume     = document.getElementById('cut-ctrl-volume');
  const progress   = document.getElementById('cut-ctrl-progress');
  const timeCur    = document.getElementById('cut-ctrl-time-current');
  const timeTotal  = document.getElementById('cut-ctrl-time-total');

  if (!video || !btnPlay || !progress) return;

  const fmt = (s) => {
    if (!isFinite(s)) return '0:00';
    const m = Math.floor(s / 60), sec = Math.floor(s % 60);
    return `${m}:${String(sec).padStart(2, '0')}`;
  };

  const syncPlayIcon = () => {
    btnPlay.innerHTML = video.paused
      ? '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>'
      : '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/></svg>';
  };

  const syncMuteIcon = () => {
    btnMute.innerHTML = video.muted
      ? '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z"/></svg>'
      : '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z"/></svg>';
  };

  btnPlay.addEventListener('click', () => { video.paused ? video.play() : video.pause(); });
  video.addEventListener('play', syncPlayIcon);
  video.addEventListener('pause', syncPlayIcon);

  btnMute.addEventListener('click', () => { video.muted = !video.muted; syncMuteIcon(); });
  volume.addEventListener('input', () => { video.volume = volume.value; video.muted = false; syncMuteIcon(); });

  btnStart.addEventListener('click', () => { video.currentTime = 0; });
  btnEnd.addEventListener('click', () => { video.currentTime = video.duration || 0; });
  btnRewind.addEventListener('click', () => { video.currentTime = Math.max(0, video.currentTime - 5); });
  btnForward.addEventListener('click', () => { video.currentTime = Math.min(video.duration || 0, video.currentTime + 5); });

  video.addEventListener('loadedmetadata', () => {
    progress.max = video.duration;
    timeTotal.textContent = fmt(video.duration);
  });

  let dragging = false;
  video.addEventListener('timeupdate', () => {
    if (!dragging) {
      progress.value = video.currentTime;
      timeCur.textContent = fmt(video.currentTime);
    }
  });
  progress.addEventListener('mousedown', () => dragging = true);
  progress.addEventListener('input', () => { timeCur.textContent = fmt(progress.value); });
  progress.addEventListener('change', () => { video.currentTime = progress.value; dragging = false; });

  // Пробел работает и в cut-overlay
  document.addEventListener('keydown', (e) => {
    if (e.key === ' ' && document.getElementById('cut-overlay') && !document.getElementById('cut-overlay').hidden
        && !['INPUT','TEXTAREA'].includes(e.target.tagName)) {
      e.preventDefault();
      video.paused ? video.play() : video.pause();
    }
  });
})();

/* DEV:START — application log viewer; stripped from public CI releases (build.ps1 -PublicRelease) */
(function initDevLogs() {
  const overlay = document.getElementById("logs-overlay");
  const btn = document.getElementById("btn-logs");
  if (!overlay || !btn) return;   // HTML stripped (public release) → nothing to wire

  const output = document.getElementById("logs-output");
  const autoscroll = document.getElementById("logs-autoscroll");
  let lastSeq = 0;
  let pollTimer = null;

  // Reveal the tool only on developer builds. The backend reports `dev` from a
  // compile-time flag, so a public release (where this block was stripped anyway)
  // would also report dev:false — belt and suspenders.
  fetch("/api/config").then((r) => r.json()).then((cfg) => {
    if (cfg && cfg.dev) btn.hidden = false;
  }).catch(() => {});

  async function poll() {
    try {
      const r = await fetch(`/api/logs?since=${lastSeq}`);
      if (!r.ok) return;
      const data = await r.json();
      lastSeq = data.seq;
      if (data.lines && data.lines.length) {
        const atBottom = output.scrollTop + output.clientHeight >= output.scrollHeight - 30;
        output.textContent += (output.textContent ? "\n" : "") + data.lines.join("\n");
        if (autoscroll.checked && atBottom) output.scrollTop = output.scrollHeight;
      }
    } catch {}
  }

  function open() {
    output.textContent = "";
    lastSeq = 0;
    overlay.hidden = false;
    poll().then(() => { output.scrollTop = output.scrollHeight; });
    clearInterval(pollTimer);
    pollTimer = setInterval(poll, 1500);
  }
  function close() {
    overlay.hidden = true;
    clearInterval(pollTimer);
    pollTimer = null;
  }

  btn.addEventListener("click", open);
  document.getElementById("logs-close").addEventListener("click", close);
  overlay.addEventListener("click", (e) => { if (e.target === overlay) close(); });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && !overlay.hidden) { e.stopPropagation(); close(); }
  });
  document.getElementById("logs-copy").addEventListener("click", async () => {
    try { await navigator.clipboard.writeText(output.textContent); toast("Logi skopiowane do schowka."); }
    catch { toast("Nie udało się skopiować.", "error"); }
  });
  document.getElementById("logs-clear").addEventListener("click", async () => {
    try { await fetch("/api/logs/clear", { method: "POST" }); } catch {}
    output.textContent = "";
    lastSeq = 0;
  });
})();
/* DEV:END */
