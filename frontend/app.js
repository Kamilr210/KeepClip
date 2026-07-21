const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => Array.from(document.querySelectorAll(sel));
if (!window.i18n && typeof i18n !== 'undefined') window.i18n = i18n;

const LANG = (() => {
  const SUPPORTED = ['en', 'uk', 'pl', 'ru', ];
  try {
    const urlLang = new URLSearchParams(window.location.search).get('lang');
    if (urlLang && SUPPORTED.includes(urlLang)) return urlLang;
  } catch(e) {}
  try {
    const saved = localStorage.getItem('keepclip_lang');
    if (saved && SUPPORTED.includes(saved)) return saved;
  } catch(e) {}
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

function localizeBackendError(error) {
  const message = String(error || "");
  const decodeAudioPrefix = "Nie udało się zdekodować audio:";
  if (message.startsWith(decodeAudioPrefix)) {
    return t("progress.decodeAudioError")
      .replace("{path}", message.slice(decodeAudioPrefix.length).trim());
  }
  return message;
}
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
  singleProgress: $("#single-progress"),
  singleProgressFill: $("#single-progress-fill"),
  singleProgressText: $("#single-progress-text"),
  singleProgressCurrent: $("#single-progress-current"),
  singleProgressClose: $("#single-progress-close"),
  q: $("#q"),
  gameFilter: $("#game-filter"),
  sort: $("#sort"),
  results: $("#results"),
  layoutBtns: $$(".view-toggle-btn"),
  overlay: $("#player-overlay"),
  pgame: $("#player-game"),
  pfile: $("#player-file"),
  video: $("#player"),
  playerPreparing: $("#player-preparing"),
  playerPreparingTitle: $("#player-preparing-title"),
  playerPreparingFill: $("#player-preparing-fill"),
  playerPreparingText: $("#player-preparing-text"),
  playerPreparingDetail: $("#player-preparing-detail"),
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
  cutRangeFill: null,
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
  viewSettings: $("#view-settings"),
  settingsOpenReplay: $("#settings-open-replay"),
  settingsTheme: $("#accent-theme-select"),
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
};
els.cutRangeFill = els.cutRange.querySelector(".range-fill");
els.cutRangeStart = els.cutRange.querySelector(".range-thumb.start");
els.cutRangeEnd = els.cutRange.querySelector(".range-thumb.end");
els.cutPlayhead = els.cutRange.querySelector(".range-playhead");

function callWindowApi(method, ...args) {
  const api = window.pywebview?.api;
  if (!api || typeof api[method] !== "function") {
    console.error(`Most okna nie udostępnia metody ${method}.`);
    return;
  }
  Promise.resolve(api[method](...args)).catch((error) => {
    console.error(`Nie udało się wykonać operacji okna ${method}:`, error);
  });
}

(function initTitlebar() {
  document.getElementById("btn-minimize")?.addEventListener("click", () => {
    callWindowApi("minimize_window");
  });

  document.getElementById("btn-maximize")?.addEventListener("click", () => {
    callWindowApi(
      "toggle_maximize_window",
      window.screen.availWidth,
      window.screen.availHeight,
      window.screen.availLeft || 0,
      window.screen.availTop || 0,
    );
  });

  document.getElementById("btn-close")?.addEventListener("click", () => {
    callWindowApi("close_window");
  });
})();
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

function clipsWord(n) {
  const category = new Intl.PluralRules(LANG).select(Number(n));
  const key = `clipCount.${category}`;
  const translated = t(key);
  return translated === key ? t("clipCount.other") : translated;
}

function parseTime(s) {
// Przyjmuje M:SS, H:MM:SS albo liczbę sekund.
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

// Oba paski miejsca mogą odświeżać się niezależnie, dlatego przechowują ostatnie dane.
let _lastStats = null;
let _cloudQuota = null;

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
  return r;
}

function setCloudQuota(st) {
  _cloudQuota = st && st.connected && st.usage != null
    ? { usage: st.usage, limit: st.limit }
    : null;
  renderStorage();
}

function refreshCloudQuota() {
  if (!_cloudConnected) return;
  fetch("/api/cloud/status").then((r) => r.json()).then((status) => {
    setCloudConnected(!!status.connected, status.email, !status.error);
    setCloudQuota(status);
  }).catch(() => { _cloudAvailable = false; });
}

function renderStorage() {
  const s = _lastStats || {};
  
  const pct = (part, whole) => {
    if (!whole || whole <= 0 || !part || part <= 0) return 0;
    return Math.max(0, Math.min(100, (part / whole) * 100));
  };

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

    // Minimalna szerokość pozwala zobaczyć użycie mniejsze niż jeden piksel.
  if (lClips > 0 && lClipsPct < 0.2) lClipsPct = 0.5;

  const localFillClips = document.getElementById("local-fill-clips");
  const localFillOther = document.getElementById("local-fill-other");

  if (localFillClips) localFillClips.style.width = lClipsPct + "%";
  if (localFillOther) localFillOther.style.width = lOtherPct + "%";


  const cClips = s.cloud_bytes || 0;
  if (els.cloudClips) els.cloudClips.textContent = fmtSize(cClips);
  
  const q = _cloudQuota;
  
  const cloudFillClips = document.getElementById("cloud-fill-clips");
  const cloudFillOther = document.getElementById("cloud-fill-other");
  const cloudBarContainer = cloudFillClips ? cloudFillClips.parentElement : null;
  
  if (q && q.limit) {
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

// Wynik FTS zawiera znaczniki <mark>; pozostały kod HTML musi zostać usunięty.
function safeSnippet(html) {
  return String(html ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/&lt;mark&gt;/g, "<mark>").replace(/&lt;\/mark&gt;/g, "</mark>");
}

function syncSortOptions() {
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

let _repaint = null;

function favoritesFirst(clips) {
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
  _repaint = null;
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

function favStar(clipId, isFav) {
  return `<button class="fav-btn ${isFav ? "is-fav" : ""}" data-fav-id="${clipId}" title="${t("playerActions.favorite")}" aria-label="${t("ui.addToFav")}"><span class="fav-star">${isFav ? "★" : "☆"}</span></button>`;
}

function setFavoriteIcon(button, isFavorite) {
  if (!button) return;
  let star = button.querySelector(".fav-star");
  if (!star) {
    Array.from(button.childNodes)
      .filter((node) => node.nodeType === Node.TEXT_NODE)
      .forEach((node) => node.remove());
    star = document.createElement("span");
    star.className = "fav-star";
    button.prepend(star);
  }
  star.textContent = isFavorite ? "★" : "☆";
}

async function toggleFavorite(clipId) {
  try {
    const res = await fetch(`/api/clips/${clipId}/favorite`, { method: "POST" });
    if (!res.ok) throw new Error();
    const data = await res.json();
    const isFav = !!data.favorite;
    // Synchronizuje wszystkie wystąpienia klipu bez usuwania etykiety przycisku odtwarzacza.
    document.querySelectorAll(`.fav-btn[data-fav-id="${clipId}"]`).forEach((b) => {
      b.classList.toggle("is-fav", isFav);
      setFavoriteIcon(b, isFav);
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

// Faza przechwytywania obsługuje gwiazdkę przed kliknięciem otwierającym odtwarzacz.
document.addEventListener("click", (e) => {
  const fb = e.target.closest(".fav-btn");
  if (!fb) return;
  e.preventDefault();
  e.stopPropagation();
  const id = parseInt(fb.dataset.favId, 10);
  if (id) toggleFavorite(id);
}, true);

let _cloudConnected = false;
let _cloudAvailable = false;
// Blokuje powtórne kliknięcie, dopóki operacja chmurowa danego klipu trwa.
const _busyClipIds = new Set();
const CLOUD_UP_GLYPH =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 18a4 4 0 0 1-.5-7.97A5.5 5.5 0 0 1 17 9.2 3.9 3.9 0 0 1 17 18z"/><path d="M12 12v5M9.5 14.5 12 12l2.5 2.5"/></svg>';
const CLOUD_DOWN_GLYPH =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 18a4 4 0 0 1-.5-7.97A5.5 5.5 0 0 1 17 9.2 3.9 3.9 0 0 1 17 18z"/><path d="M12 17v-5M9.5 14.5 12 17l2.5-2.5"/></svg>';

function cloudChip(c) {
  if (c && c.storage === "cloud") {
    return `<button class="cloud-chip cloud-down" data-cloud-down-id="${c.id}" title="${t('cloud.downloadTooltip')}" aria-label="${t('cloud.downloadAria')}">${CLOUD_DOWN_GLYPH}</button>`;
  }
  return `<button class="cloud-chip cloud-up" data-cloud-up-id="${c.id}" title="${t('cloud.uploadTooltip')}" aria-label="${t('cloud.uploadTooltip')}">${CLOUD_UP_GLYPH}</button>`;
}

function setClipChipsBusy(clipId, on) {
  document
    .querySelectorAll(`.cloud-up[data-cloud-up-id="${clipId}"], .cloud-down[data-cloud-down-id="${clipId}"]`)
    .forEach((b) => { b.classList.toggle("busy", on); b.disabled = on; });
}

function setCloudConnected(on, email, available = on) {
  _cloudConnected = !!on;
  _cloudAvailable = _cloudConnected && !!available;
  document.body.classList.toggle("cloud-connected", _cloudConnected);
  if (els.cloudNavDot) els.cloudNavDot.hidden = !_cloudConnected;
  // Mini-karta w sidebarze pokazuje konto po połączeniu.
  const sccEmail = document.getElementById("scc-email");
  if (sccEmail) sccEmail.textContent = _cloudConnected && email ? email : "";
  if (currentClipId != null && !els.overlay.hidden) {
    updatePlayerCloudBtn(els.pcloud.classList.contains("in-cloud") ? "cloud" : "local");
  }
}

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

function setSideActionLabel(button, translationKey) {
  if (!button) return;
  let label = button.querySelector(".side-action-label");
  if (!label) {
    label = document.createElement("span");
    label.className = "side-action-label";
    button.appendChild(label);
  }
  label.dataset.i18n = translationKey;
  label.textContent = t(translationKey);
}

function setSideActionIcon(button, svgMarkup) {
  if (!button) return;
  const icon = button.querySelector("svg");
  if (icon) {
    icon.outerHTML = svgMarkup;
  } else {
    button.insertAdjacentHTML("afterbegin", svgMarkup);
  }
}

function setPlayerCloudBusy(srcBtn, translationKey) {
  if (!srcBtn || srcBtn.id !== "player-cloud") return;
  srcBtn.disabled = true;
  srcBtn.classList.add("busy");
  setSideActionLabel(srcBtn, translationKey);
}

async function uploadClipToCloud(clipId, srcBtn) {
  if (!_cloudConnected) {
    toast(t("toast.cloudConnectFirst"), "error");
    return;
  }
  if (_busyClipIds.has(clipId)) return;
  _busyClipIds.add(clipId);
  setClipChipsBusy(clipId, true);
  setPlayerCloudBusy(srcBtn, "cloud.uploadingBtn");
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
  if (_busyClipIds.has(clipId)) return;
  _busyClipIds.add(clipId);
  setClipChipsBusy(clipId, true);
  setPlayerCloudBusy(srcBtn, "cloud.downloadingBtn");
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

  const svgDownload = `<svg style="width: 19px; height: 19px;" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><g id="SVGRepo_bgCarrier" stroke-width="0"></g><g id="SVGRepo_tracerCarrier" stroke-linecap="round" stroke-linejoin="round"></g><g id="SVGRepo_iconCarrier"> <path d="M12 22V16M12 22L14 20M12 22L10 20" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"></path> <path d="M22 13.3529C22 15.6958 20.5562 17.7055 18.5 18.5604M14.381 8.02721C14.9767 7.81911 15.6178 7.70588 16.2857 7.70588C16.9404 7.70588 17.5693 7.81468 18.1551 8.01498M7.11616 10.6089C6.8475 10.5567 6.56983 10.5294 6.28571 10.5294C3.91878 10.5294 2 12.4256 2 14.7647C2 16.6611 3.26124 18.2664 5 18.8061M7.11616 10.6089C6.88706 9.9978 6.7619 9.33687 6.7619 8.64706C6.7619 5.52827 9.32028 3 12.4762 3C15.4159 3 17.8371 5.19371 18.1551 8.01498M7.11616 10.6089C7.68059 10.7184 8.20528 10.9374 8.66667 11.2426M18.1551 8.01498C19.0446 8.31916 19.8345 8.83436 20.4633 9.5" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> </g></svg>`;

  const svgUpload = `<svg style="width: 19px; height: 19px;" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><g id="SVGRepo_bgCarrier" stroke-width="0"></g><g id="SVGRepo_tracerCarrier" stroke-linecap="round" stroke-linejoin="round"></g><g id="SVGRepo_iconCarrier"> <path d="M12 16V22M12 16L14 18M12 16L10 18" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"></path> <path d="M22 13.3529C22 15.6958 20.5562 17.7055 18.5 18.5604M14.381 8.02721C14.9767 7.81911 15.6178 7.70588 16.2857 7.70588C16.9404 7.70588 17.5693 7.81468 18.1551 8.01498M7.11616 10.6089C6.8475 10.5567 6.56983 10.5294 6.28571 10.5294C3.91878 10.5294 2 12.4256 2 14.7647C2 16.6611 3.26124 18.2664 5 18.8061M7.11616 10.6089C6.88706 9.9978 6.7619 9.33687 6.7619 8.64706C6.7619 5.52827 9.32028 3 12.4762 3C15.4159 3 17.8371 5.19371 18.1551 8.01498M7.11616 10.6089C7.68059 10.7184 8.20528 10.9374 8.66667 11.2426M18.1551 8.01498C19.0446 8.31916 19.8345 8.83436 20.4633 9.5" stroke="#ffffff" stroke-width="1.5" stroke-linecap="round"></path> </g></svg>`;

  if (storage === "cloud") {
    b.hidden = false;
    b.classList.add("in-cloud");
    setSideActionIcon(b, svgDownload);
    b.title = t("cloud.downloadTooltip");
  } else if (_cloudConnected) {
    b.hidden = false;
    b.classList.remove("in-cloud");
    setSideActionIcon(b, svgUpload);
    b.title = t("cloud.uploadTooltip");
  } else {
    b.hidden = true;
  }
  b.setAttribute("aria-label", b.title);
  setSideActionLabel(b, "playerActions.cloud");
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

let _cloudPollTimer = null;
let _cloudSyncPromise = null;

async function syncCloudLibrary({ notify = true } = {}) {
  if (!_cloudConnected) return null;
  if (_cloudSyncPromise) return _cloudSyncPromise;

  _cloudSyncPromise = (async () => {
    try {
      const response = await fetch("/api/cloud/sync", { method: "POST" });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(data.detail || response.statusText);

      if (data.imported || data.updated) await refreshLibraryViews();
      if (notify && data.imported) {
        toast(t("toast.cloudSynced").replace("{n}", data.imported));
      }
      return data;
    } catch (error) {
      if (notify) {
        toast(t("toast.cloudSyncFail").replace("{error}", error.message), "error");
      }
      return null;
    } finally {
      _cloudSyncPromise = null;
    }
  })();

  return _cloudSyncPromise;
}

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
  setCloudConnected(!!st.connected, st.email, !st.error);
  setCloudQuota(st);
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
        setCloudConnected(true, st.email);
        setCloudQuota(st);
        toast(t("toast.cloudConnected"));
        await syncCloudLibrary({ notify: true });
        await loadCloud();
      } else if (tries > 150) {
  _stopCloudPoll();
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

// Po powrocie z ekranu zgody Google odświeża stan połączenia.
window.addEventListener("focus", () => { if (!els.viewCloud.hidden) loadCloud(); });

let _layoutMode = "grid";
function applyLayout(mode) {
  _layoutMode = mode === "mosaic" ? "mosaic" : "grid";
  els.results.classList.toggle("layout-mosaic", _layoutMode === "mosaic");
  els.layoutBtns.forEach((b) => b.classList.toggle("active", b.dataset.layout === _layoutMode));
  try { localStorage.setItem("keepclip_layout", _layoutMode); } catch {}
  if (_repaint) _repaint();
}
els.layoutBtns.forEach((b) => b.addEventListener("click", () => applyLayout(b.dataset.layout)));
applyLayout((() => { try { return localStorage.getItem("keepclip_layout"); } catch { return null; } })() || "mosaic");

const HOVER_DELAY_MS = 250;
let _hoverTimer = null;
let _activePreview = null;
let _previewToken = 0;

function _stopActivePreview() {
  _previewToken++;
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

async function _startPreview(card) {
  _stopActivePreview();
  const token = _previewToken;
  const cid = parseInt(card.dataset.clipId, 10);
  if (!cid) return;
  const duration = parseFloat(card.dataset.duration || "0");
  // Wynik wyszukiwania startuje w trafieniu, a zwykły podgląd omija początek klipu.
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
  let playback;
  try {
    playback = await fetch(`/api/playback/${cid}?start=false`).then((r) => r.ok ? r.json() : null);
  } catch { return; }
  if (token !== _previewToken || !card.matches(":hover") || !playback?.ready || !playback.url) return;
  const video = document.createElement("video");
  video.className = "preview-video";
  video.muted = true;
  video.loop = true;
  video.playsInline = true;
  video.preload = "auto";
// Fragment adresu jest stabilniejszy niż właściwość currentTime przy strumieniowaniu w Chrome.
  const fragment = startAt > 0 ? `#t=${startAt.toFixed(2)}` : "";
  video.src = `${playback.url}${fragment}`;
  video.addEventListener("loadedmetadata", () => {
    video.play().catch(() => {});
  }, { once: true });
  thumb.appendChild(video);
  card.classList.add("previewing");
  _activePreview = { card, video };
}

els.results.addEventListener("mouseover", (e) => {
  if (_selectionMode) return;
  const card = e.target.closest(".result");
  if (!card) return;
  if (_activePreview && _activePreview.card === card) return;
  clearTimeout(_hoverTimer);
  _hoverTimer = setTimeout(() => _startPreview(card), HOVER_DELAY_MS);
});
els.results.addEventListener("mouseout", (e) => {
  const card = e.target.closest(".result");
  if (!card) return;
// Zdarzenie mouseout występuje też między elementami karty, więc reaguje dopiero po jej opuszczeniu.
  const goingTo = e.relatedTarget;
  if (goingTo && card.contains(goingTo)) return;
  clearTimeout(_hoverTimer);
  if (_activePreview && _activePreview.card === card) _stopActivePreview();
});
els.results.addEventListener("scroll", _stopActivePreview, { passive: true });
window.addEventListener("scroll", _stopActivePreview, { passive: true });

let currentSegments = [];
let currentClipId = null;
let currentClipStorage = "local";
let _cloudPlaybackErrorClipId = null;
let segmentTickerInterval = null;
let editingSegId = null;
let _playbackRequestToken = 0;

const wait = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

function hidePlaybackPreparation() {
  els.playerPreparing.hidden = true;
}

function showPlaybackPreparation(status) {
  const percent = Math.max(0, Math.min(100, Math.round((status.progress || 0) * 100)));
  els.playerPreparing.hidden = false;
  els.playerPreparingTitle.textContent = t("player.preparingPreview");
  els.playerPreparingFill.style.width = `${percent}%`;
  els.playerPreparingText.textContent = `${percent}%`;
  els.playerPreparingDetail.textContent = t("player.preparingNotice")
    .replace("{fps}", String(Math.round(status.source_fps || 0)));
}

async function resolvePlaybackSource(clipId) {
  const token = ++_playbackRequestToken;
  while (token === _playbackRequestToken && currentClipId === clipId && !els.overlay.hidden) {
    let status;
    try {
      const response = await fetch(`/api/playback/${clipId}`);
      if (!response.ok) throw new Error(`${response.status}`);
      status = await response.json();
    } catch {
      els.playerPreparing.hidden = false;
      els.playerPreparingTitle.textContent = t("player.previewError");
      els.playerPreparingDetail.textContent = t("player.previewErrorDetail");
      return null;
    }

    if (status.ready && status.url) {
      hidePlaybackPreparation();
      return status.url;
    }
    if (status.error) {
      els.playerPreparing.hidden = false;
      els.playerPreparingTitle.textContent = t("player.previewError");
      els.playerPreparingFill.style.width = "0%";
      els.playerPreparingText.textContent = "";
      els.playerPreparingDetail.textContent = status.error;
      return null;
    }

    showPlaybackPreparation(status);
    await wait(500);
  }
  return null;
}

async function openPlayer(clipId, startAt) {
  _stopActivePreview();
  _cloudPlaybackErrorClipId = null;
  currentClipId = clipId;
  const data = await fetch(`/api/segments/${clipId}`).then((r) => r.json());
  if (currentClipId !== clipId) return;
  currentClipStorage = data.clip.storage || "local";
  if (currentClipStorage === "cloud" && (!_cloudConnected || !_cloudAvailable)) {
    currentClipId = null;
    toast(t("toast.cloudPlaybackDisconnected"), "error");
    return;
  }
  els.pgame.textContent = data.clip.game;
  els.pfile.textContent = data.clip.filename;
  const isFav = !!data.clip.favorite;
  els.pfav.dataset.favId = String(clipId);
  els.pfav.classList.toggle("is-fav", isFav);
  setFavoriteIcon(els.pfav, isFav);
  updatePlayerCloudBtn(data.clip.storage);
  // Miniatura zapobiega czarnemu ekranowi podczas rozpoczęcia buforowania.
  els.video.poster = `/thumb/${clipId}?v=${data.clip.size_bytes}`;
  els.video.pause();
  els.video.removeAttribute("src");
  els.video.load();
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
    : `<div class="empty">${t("player.notTranscribed")}</div>`;

  els.segments.querySelectorAll(".seg").forEach((el) => {
    el.addEventListener("click", (e) => {
    // Obsługa edytora nie może przewijać odtwarzacza.
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
  const playbackUrl = await resolvePlaybackSource(clipId);
  if (!playbackUrl || currentClipId !== clipId || els.overlay.hidden) return;
  els.video.src = playbackUrl;
  els.video.currentTime = 0;
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

// Segmenty klipu tworzą ścieżkę napisów WebVTT, którą przeglądarka synchronizuje
// bez własnego zegara również w trybie pełnoekranowym i przy zmianie prędkości.
let _subsUrl = null;

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

// Odbudowuje ścieżkę także po ręcznej edycji transkrypcji.
function rebuildSubtitles() {
  if (!els.ctrlCc) return;
  clearSubtitles();

  const has = currentSegments.length > 0;
  els.ctrlCc.disabled = !has;
  els.ctrlCc.classList.toggle("cc-on", has && subsEnabled());
  if (!has) return;

    // VTT interpretuje znaki < i &, więc tekst użytkownika wymaga zakodowania.
  const esc = (t) => t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  let vtt = "WEBVTT\n\n";
  for (const s of currentSegments) {
    // Każdy napis musi mieć dodatnią długość czasu.
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
  // Nie przewija listy podczas ręcznej edycji.
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

function enterSegEdit(segEl) {
  if (segEl.classList.contains("editing")) return;
  const id = parseInt(segEl.dataset.id, 10);
  const seg = currentSegments.find((s) => s.id === id);
  if (!seg) return;

  editingSegId = id;
  els.video.pause();
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
        rebuildSubtitles();
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
      if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); finish(); }
    else if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); save(); }
  });
}

function closePlayer() {
  _playbackRequestToken++;
  currentClipId = null;
  currentClipStorage = "local";
  _cloudPlaybackErrorClipId = null;
  hidePlaybackPreparation();
// Zamknięcie odtwarzacza musi najpierw opuścić tryb pełnoekranowy.
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

els.video.addEventListener("error", () => {
  if (currentClipStorage !== "cloud" || currentClipId == null) return;
  if (_cloudPlaybackErrorClipId === currentClipId) return;
  _cloudPlaybackErrorClipId = currentClipId;
  _cloudAvailable = false;
  toast(t("toast.cloudPlaybackDisconnected"), "error");
});

els.pclose.addEventListener("click", closePlayer);
els.overlay.addEventListener("click", (e) => {
  if (e.target === els.overlay) closePlayer();
});
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
// W trybie pełnoekranowym pierwsze Esc obsługuje natywnie przeglądarka.
    if (document.fullscreenElement) return;
    if (!els.confirmOverlay.hidden) hideConfirm();
    else if (!els.overlay.hidden && editingSegId === null) closePlayer();
  }
});

let confirmResolver = null;
const _confirmWarnEl = document.querySelector("#confirm-box .confirm-warn");
const _defaultWarn = _confirmWarnEl ? _confirmWarnEl.innerHTML : "";

function showConfirm({
  title = t("confirm.retranscribeAll.title"),
  body = "",
  warning = null,
  warningClass = "",
  yesLabel = t("confirm.deleteClip.yes"),
  yesClass = "danger",
} = {}) {
  els.confirmTitle.textContent = title;
  els.confirmBody.textContent = body;
  if (_confirmWarnEl) {
    _confirmWarnEl.innerHTML = warning !== null ? warning : _defaultWarn;
    _confirmWarnEl.className = `confirm-warn${warningClass ? ` ${warningClass}` : ""}`;
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

let _cutTargetMb = null;
let _cutStartS = 0;
let _cutEndS = 0;
let _cutMaxS = 0;
let _cutPlayheadTimer = null;

function openCutModal() {
  if (!currentClipId) return;
  els.cutSourceName.textContent = els.pfile.textContent;

  const ct = els.video.currentTime || 0;
  els.video.pause();

  const mainSrc = els.video.src;
  els.cutVideo.src = mainSrc;
  els.cutVideo.currentTime = 0;

  const dur = els.video.duration || ct + 10;
  _cutMaxS = dur;
  _cutStartS = Math.max(0, ct - 5);
  _cutEndS = Math.min(dur, _cutStartS + 10);

  _cutTargetMb = null;
  els.cutPresets.forEach((b) => b.classList.toggle("active", b.dataset.mb === ""));
  els.cutCustomMb.value = "";
  els.cutError.hidden = true;

  els.cutOverlay.hidden = false;

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

let _cutDragging = null;
els.cutRangeStart.addEventListener("pointerdown", (e) => _beginCutDrag(e, "start"));
els.cutRangeEnd.addEventListener("pointerdown", (e) => _beginCutDrag(e, "end"));
els.cutRange.addEventListener("pointermove", _onCutDragMove);
els.cutRange.addEventListener("pointerup", _endCutDrag);
els.cutRange.addEventListener("pointercancel", _endCutDrag);
els.cutRange.addEventListener("pointerleave", _endCutDrag);
els.cutRange.addEventListener("click", (e) => {
  if (e.target !== els.cutRange && !e.target.classList.contains("range-track") && !e.target.classList.contains("range-fill")) return;
  if (_cutMaxS <= 0) return;
  const rect = els.cutRange.getBoundingClientRect();
  const pct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
  const t = pct * _cutMaxS;
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
    // Bez kompresji szacuje rozmiar na podstawie przepływności pliku źródłowego.
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

  // Dla kompresji odtwarza obliczenie docelowej przepływności używane przez serwer.
  const targetBytes = _cutTargetMb * 1000000;
  const audioKbps = 128;
  const audioBytes = (audioKbps * 1000 / 8) * duration;
  const videoBytes = targetBytes - 100000 - audioBytes - 50000;
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

let _singleProgressTimer = null;
let _singleProgressHideTimer = null;
let _singleProgressKind = "";
let _singleProgressFilename = "";
let _singleProgressSeenId = "";
let _singleProgressActive = false;
let _singleProgressFinished = false;

function singleOperationTitle(kind) {
  return t(kind === "transcription" ? "progress.singleTranscription" : "progress.singleCut");
}

function singleOperationStage(stage) {
  const key = `progress.${stage || "preparing"}`;
  const label = t(key);
  return label === key ? t("progress.preparing") : label;
}

function renderSingleOperationProgress(progress, stage) {
  const pct = Math.max(0, Math.min(100, Math.round(progress * 100)));
  els.singleProgressFill.style.width = `${_singleProgressActive ? Math.max(2, pct) : pct}%`;
  els.singleProgressText.textContent = `${pct}%`;
  const filename = _singleProgressFilename ? `: ${_singleProgressFilename}` : "";
  els.singleProgressCurrent.textContent =
    `${singleOperationTitle(_singleProgressKind)}${filename} — ${singleOperationStage(stage)}`;
}

async function pollSingleOperationProgress() {
  if (!_singleProgressActive) return;
  try {
    const response = await fetch("/api/single-operation/status", { cache: "no-store" });
    if (!response.ok) return;
    const status = await response.json();
    if (!_singleProgressActive || status.kind !== _singleProgressKind) return;
    if (status.active) _singleProgressSeenId = status.id;
    if (!_singleProgressSeenId || status.id !== _singleProgressSeenId) return;
    renderSingleOperationProgress(status.progress || 0, status.stage);
    if (!status.active && status.success !== null) {
      finishSingleOperationProgress(!!status.success, status.error || "");
    }
  } catch {
    // Następna próba odpytywania nastąpi automatycznie.
  }
}

function startSingleOperationProgress(kind, filename = "") {
  if (_singleProgressTimer) clearInterval(_singleProgressTimer);
  if (_singleProgressHideTimer) clearTimeout(_singleProgressHideTimer);
  if (els.singleProgress.parentElement !== document.body) {
    document.body.appendChild(els.singleProgress);
  }
  _singleProgressKind = kind;
  _singleProgressFilename = filename;
  _singleProgressSeenId = "";
  _singleProgressActive = true;
  _singleProgressFinished = false;
  els.singleProgress.hidden = false;
  els.singleProgress.classList.remove("operation-error");
  els.singleProgressClose.hidden = true;
  renderSingleOperationProgress(0, "preparing");
  setTimeout(pollSingleOperationProgress, 120);
  _singleProgressTimer = setInterval(pollSingleOperationProgress, 300);
}

function finishSingleOperationProgress(success, error = "") {
  if (_singleProgressFinished) return;
  _singleProgressFinished = true;
  _singleProgressActive = false;
  if (_singleProgressTimer) {
    clearInterval(_singleProgressTimer);
    _singleProgressTimer = null;
  }
  els.singleProgress.classList.toggle("operation-error", !success);
  if (success) {
    els.singleProgressFill.style.width = "100%";
    els.singleProgressText.textContent = "100%";
    els.singleProgressCurrent.textContent =
      `${singleOperationTitle(_singleProgressKind)} — ${t("progress.done")}`;
    _singleProgressHideTimer = setTimeout(() => {
      if (!_singleProgressActive) els.singleProgress.hidden = true;
      _singleProgressHideTimer = null;
    }, 1600);
  } else {
    els.singleProgressText.textContent = "!";
    els.singleProgressCurrent.textContent = t("progress.error")
      .replace("{error}", localizeBackendError(error));
  }
  els.singleProgressClose.hidden = false;
}

els.singleProgressClose.addEventListener("click", () => {
  _singleProgressActive = false;
  if (_singleProgressTimer) clearInterval(_singleProgressTimer);
  if (_singleProgressHideTimer) clearTimeout(_singleProgressHideTimer);
  _singleProgressTimer = null;
  _singleProgressHideTimer = null;
  els.singleProgress.hidden = true;
  els.singleProgress.classList.remove("operation-error");
  els.singleProgressFill.style.width = "0%";
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
  startSingleOperationProgress("cut", els.cutSourceName.textContent);
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
    finishSingleOperationProgress(true);
    closeCutModal();
    showCutSuccess(data);
    if (data.in_library) {
      await refreshLibraryViews();
    }
  } catch (e) {
    finishSingleOperationProgress(false, e.message);
    els.cutError.textContent = e.message;
    els.cutError.hidden = false;
  } finally {
    els.cutGo.disabled = false;
    els.cutGo.textContent = t("toast.cutGoBtn");
  }
});

function showCutSuccess(data) {
  const outputDirectory = data.output_directory || data.cuts_root;
  const ok = showConfirm({
    title: t("cutSuccess.title"),
    body: `${data.output_name} — ${data.size_mb} MB`,
    warning: `Plik zapisano w folderze:<br><code>${escapeHtml(outputDirectory)}</code>`,
    warningClass: "success",
    yesLabel: t("cutSuccess.showInFolder"),
    yesClass: "primary",
  });
  els.confirmNo.textContent = t("confirm.closeBtn");
  ok.then(async (clicked) => {
    els.confirmNo.textContent = t("confirm.cancelBtn");
    if (clicked) {
      try {
        const r = await fetch("/api/show-in-explorer", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ path: outputDirectory }),
        });
        if (!r.ok) {
          const error = await r.json().catch(() => ({}));
          throw new Error(error.detail || r.statusText);
        }
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

async function openFoldersDropdown() {
  if (!currentClipId) return;
  const clipId = currentClipId;
  els.pfolders.disabled = true;

  let allFolders;
  let membership;
  try {
    const [foldersResponse, membershipResponse] = await Promise.all([
      fetch("/api/folders"),
      fetch(`/api/clips/${clipId}/folders`),
    ]);
    if (!foldersResponse.ok || !membershipResponse.ok) {
      const failed = !foldersResponse.ok ? foldersResponse : membershipResponse;
      const error = await failed.json().catch(() => ({}));
      throw new Error(error.detail || failed.statusText);
    }
    [allFolders, membership] = await Promise.all([
      foldersResponse.json(),
      membershipResponse.json(),
    ]);
  } catch (e) {
    closeFoldersDropdown();
    toast(e.message, "error");
    return;
  } finally {
    els.pfolders.disabled = false;
  }

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
      <button id="player-new-folder-btn" class="btn primary" style="padding: 6px 12px; font-size: 13px;">${t('folders.addBtn')}</button>
    </div>`;
  els.pfoldersDropdown.innerHTML = html;
  if (els.pfoldersDropdown.parentElement !== document.body) {
    document.body.appendChild(els.pfoldersDropdown);
  }
  els.pfoldersDropdown.classList.add("player-folders-portal");
  els.pfoldersDropdown.hidden = false;
  positionFoldersDropdown();

  els.pfoldersDropdown.querySelectorAll('input[type="checkbox"]').forEach((cb) => {
    cb.addEventListener("change", async () => {
      const fid = parseInt(cb.dataset.folderId, 10);
      const shouldBeChecked = cb.checked;
      cb.disabled = true;
      try {
        const r = await fetch(`/api/folders/${fid}/clips/${clipId}`, {
          method: shouldBeChecked ? "POST" : "DELETE",
        });
        if (!r.ok) {
          const error = await r.json().catch(() => ({}));
          throw new Error(error.detail || r.statusText);
        }
        await refreshLibraryViews();
      } catch (e) {
        cb.checked = !shouldBeChecked;
        toast(e.message, "error");
      } finally {
        cb.disabled = false;
      }
    });
  });

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
      const addResponse = await fetch(`/api/folders/${data.id}/clips/${clipId}`, { method: "POST" });
      if (!addResponse.ok) {
        const error = await addResponse.json().catch(() => ({}));
        throw new Error(error.detail || addResponse.statusText);
      }
      toast(t("toast.folderCreated").replace("{name}", data.name));
      await refreshLibraryViews();
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

function positionFoldersDropdown() {
  if (els.pfoldersDropdown.hidden) return;
  const buttonRect = els.pfolders.getBoundingClientRect();
  const menuRect = els.pfoldersDropdown.getBoundingClientRect();
  const margin = 8;
  let left = buttonRect.left - menuRect.width - margin;
  if (left < margin) left = buttonRect.right + margin;
  left = Math.max(margin, Math.min(left, window.innerWidth - menuRect.width - margin));
  const top = Math.max(margin, Math.min(buttonRect.top, window.innerHeight - menuRect.height - margin));
  els.pfoldersDropdown.style.left = `${left}px`;
  els.pfoldersDropdown.style.top = `${top}px`;
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
window.addEventListener("resize", closeFoldersDropdown);

els.pretrans.addEventListener("click", async () => {
  if (!currentClipId) return;
  els.pretrans.disabled = true;
  setSideActionLabel(els.pretrans, "toast.retranscribeBtn");
  startSingleOperationProgress("transcription", els.pfile.textContent);
  try {
    const r = await fetch(`/api/clips/${currentClipId}/retranscribe`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    finishSingleOperationProgress(true);
    toast(t("toast.transcribeRetranscribed").replace("{segments}", data.segments).replace("{seconds}", data.seconds));
    const cid = currentClipId;
    closePlayer();
    setTimeout(() => openPlayer(cid, 0), 200);
    await loadStats();
    doSearch();
  } catch (e) {
    finishSingleOperationProgress(false, e.message);
    toast(t("toast.transcribeError").replace("{error}", e.message), "error");
  } finally {
    els.pretrans.disabled = false;
    setSideActionLabel(els.pretrans, "playerActions.retranscribe");
  }
});

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
  setSideActionLabel(els.pfix, "playerActions.fixing");
  try {
    const r = await fetch(`/api/clips/${currentClipId}/fix`, { method: "POST" });
    const data = await r.json();
    if (!r.ok) throw new Error(data.detail || r.statusText);
    const msg = data.trimmed_seconds > 0
      ? t("toast.fixRepaired").replace("{seconds}", data.trimmed_seconds.toFixed(0))
      : t("toast.fixRemuxed");
    toast(msg);
    const cid = currentClipId;
    closePlayer();
    setTimeout(() => openPlayer(cid, 0), 200);
    await loadStats();
  } catch (e) {
    toast(t("toast.fixFail").replace("{error}", e.message), "error");
  } finally {
    els.pfix.disabled = false;
    setSideActionLabel(els.pfix, "playerActions.fix");
  }
});

els.pdelete.addEventListener("click", async () => {
  if (!currentClipId) return;
  const filename = els.pfile.textContent;
  const ok = await showConfirm({
    title: t("confirm.deleteClip.title"),
    body: filename,
    warning: currentClipStorage === "cloud" ? t("confirm.deleteClip.cloudWarning") : null,
    yesLabel: currentClipStorage === "cloud" ? t("confirm.deleteClip.cloudYes") : t("confirm.deleteClip.yes"),
  });
  if (!ok) return;
  const clipId = currentClipId;
  els.pdelete.disabled = true;
  // Najpierw zwalnia strumień wideo, aby Windows mógł przenieść plik do Kosza.
  closePlayer();
  await new Promise((resolve) => setTimeout(resolve, 200));
  try {
    const r = await fetch(`/api/clips/${clipId}?delete_file=true`, { method: "DELETE" });
    if (!r.ok) {
      const err = await r.json().catch(() => ({}));
      throw new Error(err.detail || r.statusText);
    }
    const data = await r.json();
    if (data.cloud_deleted) {
      toast(t("toast.deleteCloudSuccess").replace("{filename}", data.filename));
    } else if (data.file_sent_to_trash) {
      toast(t("toast.deleteSuccess").replace("{filename}", data.filename));
    } else if (data.trash_error) {
      toast(t("toast.deleteTrashFail").replace("{error}", data.trash_error), "error");
    } else {
      toast(t("toast.deleteSuccessOnly").replace("{filename}", data.filename));
    }
    await refreshLibraryViews();
  } catch (e) {
    toast(t("toast.deleteError").replace("{error}", e.message), "error");
  } finally {
    els.pdelete.disabled = false;
  }
});

let searchTimer = null;
els.q.addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(doSearch, 200);
});
els.gameFilter.addEventListener("change", doSearch);
els.sort.addEventListener("change", doSearch);

async function runScan({ showToastAlways = false, announceChanges = true } = {}) {
  const response = await fetch("/api/scan", { method: "POST" });
  const r = await response.json();
  if (!response.ok) throw new Error(r.detail || response.statusText);
  const parts = [];
  if (r.added) parts.push(t("toast.scanParts.added").replace("{n}", r.added));
  if (r.removed) parts.push(t("toast.scanParts.removed").replace("{n}", r.removed));
  if (r.updated) parts.push(t("toast.scanParts.updated").replace("{n}", r.updated));
  if (!parts.length) parts.push(t("toast.scanParts.noChanges"));
  if (announceChanges && (r.added || r.removed || r.updated)) {
    toast(t("toast.scanResult").replace("{parts}", parts.join(", ")));
  } else if (showToastAlways) {
    toast(t("toast.scanNoChanges"));
  }
  return r;
}

function libraryStatsSignature(stats) {
  if (!stats) return "";
  const games = (stats.games || []).map((g) => [g.game, g.clips, g.done]);
  return JSON.stringify([
    stats.clips,
    stats.transcribed,
    stats.segments,
    stats.local_bytes,
    stats.cloud_bytes,
    stats.favorites,
    games,
  ]);
}

let _libraryRefreshPromise = null;
let _libraryRefreshQueued = false;

async function refreshLibraryViews({ skipStats = false } = {}) {
  if (_libraryRefreshPromise) {
    _libraryRefreshQueued = true;
    return _libraryRefreshPromise;
  }

  const refresh = (async () => {
    let omitStats = skipStats;
    do {
      _libraryRefreshQueued = false;
      const jobs = [];
      if (!omitStats) jobs.push(loadStats());
      if (!els.viewClips.hidden) jobs.push(doSearch());
      if (!els.viewFavorites.hidden) jobs.push(loadFavorites());
      if (!els.viewCloud.hidden) jobs.push(loadCloud());
      if (!els.viewFolders.hidden) {
        jobs.push(_currentFolderId == null ? loadFoldersIndex() : openFolder(_currentFolderId));
      }
      await Promise.all(jobs);
      omitStats = false;
    } while (_libraryRefreshQueued);
  })();

  _libraryRefreshPromise = refresh;
  try {
    await refresh;
  } finally {
    if (_libraryRefreshPromise === refresh) _libraryRefreshPromise = null;
  }
}

let _autoLibrarySyncTimer = null;
let _autoLibrarySyncBusy = false;

async function autoSyncLibrary() {
  if (_autoLibrarySyncBusy || document.hidden || !els.configOverlay.hidden) return;
  _autoLibrarySyncBusy = true;
  try {
    const previousSignature = libraryStatsSignature(_lastStats);
    const scan = await runScan({ announceChanges: false });
    const stats = await loadStats();
    const libraryChanged = scan.added || scan.removed || scan.updated ||
      libraryStatsSignature(stats) !== previousSignature;
    if (libraryChanged) await refreshLibraryViews({ skipStats: true });
  } catch (e) {
    console.warn("Automatyczne odświeżanie biblioteki nie powiodło się:", e);
  } finally {
    _autoLibrarySyncBusy = false;
  }
}

function startAutoLibrarySync() {
  if (_autoLibrarySyncTimer) return;
  _autoLibrarySyncTimer = setInterval(autoSyncLibrary, 2500);
}

window.addEventListener("focus", () => {
  if (_autoLibrarySyncTimer) autoSyncLibrary();
});
document.addEventListener("visibilitychange", () => {
  if (!document.hidden && _autoLibrarySyncTimer) autoSyncLibrary();
});

els.scan.addEventListener("click", async () => {
  els.scan.disabled = true;
  els.scanLabel.textContent = t("toast.scanning");
  try {
    const r = await runScan({ announceChanges: false });
    const cloudWasConnected = _cloudConnected;
    const cloud = cloudWasConnected
      ? await syncCloudLibrary({ notify: false })
      : null;
    const parts = [];
    if (r.added) parts.push(t("toast.scanParts.added").replace("{n}", r.added));
    if (r.removed) parts.push(t("toast.scanParts.removed").replace("{n}", r.removed));
    if (r.updated) parts.push(t("toast.scanParts.updated").replace("{n}", r.updated));
    if (cloud) {
      if (cloud.imported) parts.push(t("toast.scanParts.cloudAdded").replace("{n}", cloud.imported));
      if (cloud.updated) parts.push(t("toast.scanParts.cloudUpdated").replace("{n}", cloud.updated));
      if (!cloud.imported && !cloud.updated) {
        parts.push(t("toast.scanParts.cloudChecked").replace("{n}", cloud.found || 0));
      }
    } else if (cloudWasConnected) {
      parts.push(t("toast.scanParts.cloudFailed"));
    }
    if (!parts.length) parts.push(t("toast.scanParts.noChanges"));
    els.scanLabel.textContent = parts.join(", ");
    toast(t("toast.scanResult").replace("{parts}", parts.join(", ")), cloudWasConnected && !cloud ? "error" : "ok");
    await refreshLibraryViews();
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

document.querySelectorAll(".nav-tool[data-action]").forEach((btn) => {
  btn.addEventListener("click", () => {
    const action = btn.dataset.action;
    if (action === "retranscribe-all") retranscribeAll();
    else if (action === "change-folder") openConfigModal({ mode: "change" });
    else if (action === "replay") openReplayModal();
  });
});

// Ustawienia to pełny widok (data-view="settings" w sidebarze), nie modal —
// przycisk w nawigacji łapie standardowy handler navItems → setView("settings").

function normalizeAccentTheme(theme) {
  return theme === "green" ? "green" : "purple";
}

function applyAccentTheme(theme) {
  const normalized = normalizeAccentTheme(theme);
  document.documentElement.dataset.accentTheme = normalized;
  if (els.settingsTheme) els.settingsTheme.value = normalized;
  const favicon = document.querySelector('link[rel="icon"]');
  if (favicon) favicon.href = normalized === "green" ? "/icons/favicon-green.svg" : "/icons/favicon.svg";
  return normalized;
}

applyAccentTheme(document.documentElement.dataset.accentTheme);

if (els.settingsTheme) {
  els.settingsTheme.addEventListener("change", async () => {
    const previous = normalizeAccentTheme(document.documentElement.dataset.accentTheme);
    const selected = applyAccentTheme(els.settingsTheme.value);
    els.settingsTheme.disabled = true;
    try {
      const response = await fetch("/api/accent-theme", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ theme: selected }),
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(data.detail || response.statusText);
      applyAccentTheme(data.theme);
    } catch (e) {
      applyAccentTheme(previous);
      toast(t("settings.themeSaveError").replace("{error}", e.message), "error");
    } finally {
      els.settingsTheme.disabled = false;
    }
  });
}

document.querySelectorAll(".settings-folder-btn[data-action='change-folder']").forEach((btn) => {
  btn.addEventListener("click", () => openConfigModal({ mode: "change" }));
});

if (els.settingsOpenReplay) {
  els.settingsOpenReplay.addEventListener("click", openReplayModal);
}

async function populateAudioDevices() {
  if (!els.replayAudioOutput || !els.replayAudioInput) return;
  try {
    const r = await fetch("/api/replay/audio-devices");
    if (r.ok) {
      const data = await r.json();
      fillDeviceSelect(els.replayAudioOutput, data.outputs || [], t("replay.audioDefault"));
      fillDeviceSelect(els.replayAudioInput,  data.inputs  || [], t("replay.audioDefault"));
      return;
    }
    } catch { /* Przechodzi do zapasowej obsługi Web Audio. */ }
  // Awaryjnie używa interfejsu MediaDevices, który w WebView2 może mieć ograniczone uprawnienia.
  try {
    await navigator.mediaDevices.getUserMedia({ audio: true }).catch(() => {});
    const devices = await navigator.mediaDevices.enumerateDevices();
    const outputs = devices.filter(d => d.kind === "audiooutput");
    const inputs  = devices.filter(d => d.kind === "audioinput");
    fillDeviceSelect(els.replayAudioOutput, outputs.map(d => ({ id: d.deviceId, name: d.label || d.deviceId })), t("replay.audioDefault"));
    fillDeviceSelect(els.replayAudioInput,  inputs.map(d => ({ id: d.deviceId, name: d.label  || d.deviceId })), t("replay.audioDefault"));
  } catch { /* Brak uprawnień do urządzeń dźwiękowych. */ }
}

function fillDeviceSelect(sel, devices, defaultLabel) {
  if (!sel) return;
  const current = sel.value;
  while (sel.options.length > 1) sel.remove(1);
  sel.options[0].textContent = defaultLabel;
  devices.forEach(d => {
    if (!d.id && !d.name) return;
    const opt = document.createElement("option");
    opt.value = d.id || d.name;
    opt.textContent = d.name || d.id;
    sel.appendChild(opt);
  });
  if (current) sel.value = current;
}

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
    if (scan.added) parts.push(t("toast.scanParts.added").replace("{n}", scan.added));
    if (scan.removed) parts.push(t("toast.scanParts.removed").replace("{n}", scan.removed));
    if (scan.updated) parts.push(t("toast.scanParts.updated").replace("{n}", scan.updated));
    if (!parts.length && scan.found) {
      parts.push(t("toast.scanParts.found").replace("{n}", scan.found));
    }
    toast(t("toast.folderSet")
      .replace("{path}", data.clips_root)
      .replace("{details}", parts.length ? ` — ${parts.join(", ")}` : ""));
    await refreshLibraryViews();
    startAutoLibrarySync();
  } catch (e) {
    els.configError.textContent = e.message;
    els.configError.hidden = false;
  } finally {
    els.configSave.disabled = false;
    els.configSave.textContent = els.configCancel.hidden ? t("config.saveFirst") : t("config.saveChange");
  }
});
const REPLAY_ENC_LABELS = {
  hevc_nvenc: "NVENC HEVC (GPU NVIDIA)",
  hevc_amf: "AMF HEVC (GPU AMD)",
  hevc_qsv: "QuickSync HEVC (GPU Intel)",
  h264_nvenc: "NVENC (GPU NVIDIA)",
  h264_amf: "AMF (GPU AMD)",
  h264_qsv: "QuickSync (GPU Intel)",
  libx264: "x264 (CPU)",
};

let _replayPoll = null;

function renderReplayStatus(st) {
  if (els.replayNavDot) els.replayNavDot.hidden = !(st.enabled && st.running);
  if (!els.replayBox || els.replayOverlay.hidden) return;

  // Odpytywanie statusu nie może nadpisywać niezapisanych zmian w otwartym formularzu.
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
  // Włączony bufor bez aktywnego skrótu oznacza zwykle konflikt z inną aplikacją.
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
  const st = await refreshReplayStatus();
  if (st) {
    els.replayEnabled.checked = !!st.enabled;
    if (els.replayBackground) els.replayBackground.checked = !!st.background;
    els.replayMic.checked = !!st.mic_enabled;
    els.replayDuration.value = String(st.duration_s);
  if (!els.replayDuration.value) els.replayDuration.value = "120";
    els.replayFps.value = String(st.fps);
    els.replayQuality.value = st.quality;
    els.replayHotkey.value = st.hotkey;
    await populateAudioDevices();
    if (st.audio_output && els.replayAudioOutput) els.replayAudioOutput.value = st.audio_output;
    if (st.audio_input  && els.replayAudioInput)  els.replayAudioInput.value  = st.audio_input;
  }
  els.replayOverlay.hidden = false;
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
  if (["Control", "Alt", "Shift", "Meta"].includes(e.key)) return;
  // Tłumaczy nazwę klawisza DOM na format WinForms oczekiwany przez serwer.
  let key = e.key;
  if (/^[a-z]$/i.test(key)) key = key.toUpperCase();
  else if (/^[0-9]$/.test(key)) key = "D" + key;
  else if (key === " ") key = "Space";
  else if (key.startsWith("Arrow")) key = key.slice(5);
  const mods = [];
  if (e.ctrlKey) mods.push("Ctrl");
  if (e.altKey) mods.push("Alt");
  if (e.shiftKey) mods.push("Shift");
  if (e.metaKey) mods.push("Win");
  if (!mods.length) return; // Sam klawisz przechwytywałby zwykłe pisanie w całym systemie.
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
  // Starsza część serwerowa może zwrócić pustą odpowiedź; wtedy pokazuje stan HTTP.
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

refreshReplayStatus();
setInterval(refreshReplayStatus, 15000);

els.cancel.addEventListener("click", async () => {
  await fetch("/api/transcribe/cancel", { method: "POST" });
});

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
  els.progClose.hidden = true;
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
        els.pcurrent.textContent = s.error
          ? t("progress.error").replace("{error}", localizeBackendError(s.error))
          : t("progress.done");
      }
      if (!s.running && s.finished_at) {
        sse.close();
        sse = null;
        els.cancel.hidden = true;
        els.trans.disabled = false;
    els.progClose.hidden = false;
        loadStats();
        doSearch();
      }
    } catch (e) {
      console.error(e);
    }
  };
  sse.onerror = () => {
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
      doSearch();
    }
  } catch {
    setTimeout(pollOnce, 3000);
  }
}

let _currentFolderId = null;

let _selectionMode = false;
let _selectionTargetFolderId = null;
let _selectionTargetFolderName = "";
let _selectedClipIds = new Set();
let _alreadyInFolder = new Set();

function _updateSelectionUI() {
  const n = _selectedClipIds.size;
  els.selCount.textContent = String(n);
  els.selAdd.disabled = n === 0;
  els.selAdd.textContent = n === 0
    ? t("selection.addSelected")
    : t("selection.addN").replace("{n}", n).replace("{word}", clipsWord(n));
}

async function startSelectionMode(folderId, folderName) {
  _selectionMode = true;
  _selectionTargetFolderId = folderId;
  _selectionTargetFolderName = folderName;
  _selectedClipIds.clear();
  try {
    const data = await fetch(`/api/folders/${folderId}/clips?limit=10000`).then((r) => r.json());
    _alreadyInFolder = new Set((data.clips || []).map((c) => c.id));
  } catch {
    _alreadyInFolder = new Set();
  }
  els.selectionBar.hidden = false;
  els.selFolderName.textContent = folderName;
  _updateSelectionUI();
  els.foldersIndex.hidden = true;
  els.folderDetail.hidden = true;
  setView("clips");
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
    setView("folders");
    openFolder(fid);
  } catch (e) {
    toast(t("toast.folderAddError").replace("{error}", e.message), "error");
    _updateSelectionUI();
  }
}

els.selCancel.addEventListener("click", () => {
  exitSelectionMode();
  if (_selectionTargetFolderId) {
    setView("folders");
    openFolder(_selectionTargetFolderId);
  }
});
els.selAdd.addEventListener("click", saveSelection);

// Jeden klip może wystąpić w wielu wynikach, więc zaznaczenie synchronizuje wszystkie karty.
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
}, true); // Faza przechwytywania wykonuje się przed zwykłym otwarciem odtwarzacza.

function setView(view) {
  els.navItems.forEach((t) => t.classList.toggle("active", t.dataset.view === view));
  els.viewClips.hidden = view !== "clips";
  els.viewFolders.hidden = view !== "folders";
  els.viewFavorites.hidden = view !== "favorites";
  els.viewCloud.hidden = view !== "cloud";
  if (els.viewSettings) els.viewSettings.hidden = view !== "settings";
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

const sidebarCloudCard = document.getElementById("sidebar-cloud-card");
if (sidebarCloudCard) sidebarCloudCard.addEventListener("click", () => setView("cloud"));

async function loadFoldersIndex() {
  const folders = await fetch("/api/folders").then((r) => r.json());
  if (!folders.length) {
    els.foldersGrid.innerHTML = `
      <div class="folder-new-card" id="empty-new-folder">
        <div class="folder-new-card-inner">
          <div class="icon">+</div>
          <div>${t("folders.createFirst")}</div>
        </div>
      </div>`;
    document.getElementById("empty-new-folder")?.addEventListener("click", promptNewFolder);
    return;
  }
  els.foldersGrid.innerHTML = folders.map(folderCard).join("") + `
    <div class="folder-new-card" id="grid-new-folder">
      <div class="folder-new-card-inner">
        <div class="icon">+</div>
        <div>${t("folders.newFolderLabel")}</div>
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

// Serwer kończy pracę po pięciu minutach bez sygnału; aktywna strona wysyła go co 30 sekund.
function sendHeartbeat() {
  fetch("/api/heartbeat", { method: "POST" }).catch(() => {});
}
sendHeartbeat();
setInterval(sendHeartbeat, 30000);

(async function bootstrap() {
  try {
    const cfg = await fetch("/api/config").then((r) => r.json());
    if (!cfg.configured) {
      await openConfigModal({ mode: "first" });
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
  try {
    const cloudStatus = await fetch("/api/cloud/status").then((r) => r.json());
    setCloudConnected(!!cloudStatus.connected, cloudStatus.email, !cloudStatus.error);
    setCloudQuota(cloudStatus);
    if (cloudStatus.connected) await syncCloudLibrary({ notify: false });
  } catch { }
  checkForUpdate();
  const s = await fetch("/api/transcribe/status").then((r) => r.json());
  if (s.running) attachStream();
  await renderRecent("");
  startAutoLibrarySync();
})();

const playerIcons = {
  play: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>`,
  pause: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/></svg>`,
  volOn: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z"/></svg>`,
  volOff: `<svg viewBox="0 0 24 24" fill="currentColor"><path d="M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3z"/></svg>`
};

if (els.video) {
  const togglePlay = () => els.video.paused ? els.video.play() : els.video.pause();
  els.ctrlPlay.addEventListener("click", togglePlay);
  els.video.addEventListener("click", togglePlay);
  
  els.video.addEventListener("play", () => els.ctrlPlay.innerHTML = playerIcons.pause);
  els.video.addEventListener("pause", () => els.ctrlPlay.innerHTML = playerIcons.play);

  els.ctrlStart.addEventListener("click", () => els.video.currentTime = 0);
  els.ctrlEnd.addEventListener("click", () => els.video.currentTime = els.video.duration);
  els.ctrlRewind.addEventListener("click", () => els.video.currentTime = Math.max(0, els.video.currentTime - 5));
  els.ctrlForward.addEventListener("click", () => els.video.currentTime = Math.min(els.video.duration, els.video.currentTime + 5));

// Selektory głośności są zakotwiczone przy odtwarzaczu, bo okno wycinania ma własny mikser.
  const volumePopover = els.ctrlMute?.closest(".volume-control-wrap")
    ?.querySelector(".volume-mixer-popover");

  if (els.ctrlMute && volumePopover && els.ctrlVolume) {

    els.ctrlMute.addEventListener("click", (e) => {
  e.stopPropagation();
      els.ctrlVolume.value = els.video.muted ? 0 : els.video.volume;
      volumePopover.classList.toggle("show");
    });

    volumePopover.addEventListener("click", (e) => {
      e.stopPropagation();
    });

    document.addEventListener("click", () => {
      volumePopover.classList.remove("show");
    });

    els.ctrlVolume.addEventListener("input", (e) => {
      const vol = parseFloat(e.target.value);
      els.video.volume = vol;
      els.video.muted = (vol === 0);
      els.ctrlMute.innerHTML = els.video.muted ? playerIcons.volOff : playerIcons.volOn;
    });

  } else {
    console.error("Brak elementów suwaka głośności w HTML!");
  }

// Tryb pełnoekranowy obejmuje cały element #player-box, aby zachować nagłówek i sterowanie.
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

// W pełnym ekranie sterowanie znika po bezczynności, ale pozostaje widoczne podczas pauzy.
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

// Nowy klip zawsze rozpoczyna odtwarzanie z prędkością 1×.
  const SPEEDS = [1, 1.25, 1.5, 2, 0.5, 0.75];
  const setSpeed = (v) => {
    els.video.playbackRate = v;
    els.ctrlSpeed.textContent = `${v}×`;
  };
  els.ctrlSpeed.addEventListener("click", () => {
    const i = SPEEDS.indexOf(els.video.playbackRate);
    setSpeed(SPEEDS[(i + 1) % SPEEDS.length] ?? 1);
  });

// Ustawienie napisów jest pamiętane między klipami.
  if (els.ctrlCc) {
    els.ctrlCc.addEventListener("click", () => {
      const on = !subsEnabled();
      localStorage.setItem("kc_subtitles", on ? "1" : "0");
      const tr = els.video.querySelector("track");
      if (tr) tr.track.mode = on ? "showing" : "hidden";
      els.ctrlCc.classList.toggle("cc-on", on && !els.ctrlCc.disabled);
    });
  }

  let isDragging = false;
  
  els.video.addEventListener("loadedmetadata", () => {
    els.ctrlProgress.max = els.video.duration;
    els.ctrlTimeTot.textContent = fmtTime(els.video.duration);
  setSpeed(1);
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

  els.ctrlSnap.addEventListener("click", () => {
    const canvas = document.createElement("canvas");
    canvas.width = els.video.videoWidth;
    canvas.height = els.video.videoHeight;
    const ctx = canvas.getContext("2d");
    ctx.drawImage(els.video, 0, 0, canvas.width, canvas.height);
    
    const a = document.createElement("a");
    a.href = canvas.toDataURL("image/jpeg");
    a.download = `KeepClip_Screenshot_${fmtTime(els.video.currentTime).replace(':','-')}.jpg`;
    a.click();
  toast(t("toast.screenshotSaved"));
  });
}

document.addEventListener("keydown", (e) => {
  if (e.key === " " && !els.overlay.hidden && !["INPUT", "TEXTAREA"].includes(e.target.tagName)) {
    e.preventDefault();
    if(els.video) els.video.paused ? els.video.play() : els.video.pause();
  }
});
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

  document.addEventListener('keydown', (e) => {
    if (e.key === ' ' && document.getElementById('cut-overlay') && !document.getElementById('cut-overlay').hidden
        && !['INPUT','TEXTAREA'].includes(e.target.tagName)) {
      e.preventDefault();
      video.paused ? video.play() : video.pause();
    }
  });
})();

/* DEV:START — panel logów usuwany z publicznego wydania przez build.ps1 -PublicRelease */
(function initDevLogs() {
  const overlay = document.getElementById("logs-overlay");
  const btn = document.getElementById("btn-logs");
  if (!overlay || !btn) return; // W publicznym wydaniu odpowiadający HTML jest usunięty.

  const output = document.getElementById("logs-output");
  const autoscroll = document.getElementById("logs-autoscroll");
  let lastSeq = 0;
  let pollTimer = null;

// Serwer potwierdza flagę kompilacji, zanim interfejs pokaże narzędzie deweloperskie.
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

// ---------- aktualizacje aplikacji ----------
// Cichy check przy starcie; baner pokazuje się tylko, gdy jest nowsze wydanie z plikiem
// instalatora. „Pobierz i zainstaluj" pobiera setup po stronie backendu (bez SmartScreen,
// bo plik nie ma znacznika przeglądarki) i uruchamia go — aplikacja wtedy sama się zamyka,
// a instalator aktualizuje ją w miejscu, nie ruszając biblioteki ani ustawień.
async function checkForUpdate() {
  try {
    const st = await fetch("/api/update/check").then((r) => r.json());
    if (!st.available) return;
    if (sessionStorage.getItem("kc_upd_dismiss") === st.latest) return;
    const line = document.getElementById("update-version-line");
    if (line) line.textContent = `KeepClip ${st.latest} — ${t("update.youHave").replace("{v}", st.current)}`;
    const banner = document.getElementById("update-banner");
    if (banner) banner.hidden = false;
    window._kcLatest = st.latest;
  } catch { /* brak sieci — bez banera */ }
}

(function wireUpdateBanner() {
  const banner = document.getElementById("update-banner");
  const btnLater = document.getElementById("update-later");
  const btnGo = document.getElementById("update-install");
  if (!banner || !btnLater || !btnGo) return;

  btnLater.addEventListener("click", () => {
    banner.hidden = true;
    if (window._kcLatest) sessionStorage.setItem("kc_upd_dismiss", window._kcLatest);
  });

  btnGo.addEventListener("click", async () => {
    btnGo.disabled = true;
    btnLater.disabled = true;
    try {
      const r = await fetch("/api/update/install", { method: "POST" });
      if (!r.ok) throw new Error((await r.json().catch(() => null))?.detail || r.statusText);
      const poll = setInterval(async () => {
        try {
          const s = await fetch("/api/update/status").then((x) => x.json());
          if (s.phase === "downloading") {
            btnGo.textContent = t("update.downloading").replace("{p}", s.percent ?? 0);
          } else if (s.phase === "starting") {
            clearInterval(poll);
            btnGo.textContent = t("update.starting");
            toast(t("update.startingToast"));
          } else if (s.phase === "error") {
            clearInterval(poll);
            toast(t("update.failed").replace("{error}", s.error || "?"), "error");
            btnGo.disabled = false;
            btnLater.disabled = false;
            btnGo.textContent = t("update.installBtn");
          }
        } catch { /* backend znika przy starcie instalatora — to oczekiwane */ }
      }, 500);
    } catch (e) {
      toast(t("update.failed").replace("{error}", e.message), "error");
      btnGo.disabled = false;
      btnLater.disabled = false;
    }
  });
})();
