/* Samouczek KeepClip.
   Silnik prowadzi użytkownika po prawdziwym interfejsie: przyciemnia aplikację,
   podświetla wskazany element i pokazuje przy nim małą kartę z opisem.
   Kroki są opisane deklaratywnie (STEPS), a elementy wyszukiwane przez rejestr
   celów (TARGETS), więc dodanie kroku nie wymaga zmian w samym silniku. */
(function () {
  "use strict";

  var VERSION = 1;                 // Musi zgadzać się z OnboardingService.CurrentVersion.
  var LOCAL_KEY = "keepclip_onboarding";
  var GAP = 14;                    // Odstęp karty od podświetlonego elementu.
  var MARGIN = 16;                 // Minimalny margines od krawędzi okna.
  var FADE = 200;                  // Czas pojawienia się karty (ms).
  var STEP_GAP = 150;              // Przerwa między krokami (ms).
  var MAX_SPOT = 0.4;              // Element wyższy niż ta część okna zostaje podświetlony tylko u góry.

  /* ------------------------------------------------------------------ */
  /* Teksty                                                              */
  /* ------------------------------------------------------------------ */

  // window.t zwraca klucz, gdy tłumaczenia brak — zamieniamy to na pusty tekst,
  // dzięki czemu brakująca wskazówka po prostu znika zamiast wyświetlać klucz.
  function txt(key, fallback) {
    var full = "onboarding." + key;
    var value = typeof window.t === "function" ? window.t(full) : full;
    return value === full ? (fallback === undefined ? "" : fallback) : value;
  }

  function stepTxt(id, part) {
    return txt("steps." + id + "." + part, "");
  }

  /* ------------------------------------------------------------------ */
  /* Rejestr celów                                                       */
  /* ------------------------------------------------------------------ */

  // Każdy cel to lista kandydatów — wygrywa pierwszy widoczny. Dzięki temu krok
  // działa też przy pustej bibliotece, gdy element pierwszego wyboru nie istnieje.
  var TARGETS = {
    ClipLibrary:         ["#results", "#view-clips .search-bar"],
    ClipsRootSetting:    ["#view-settings [data-action='change-folder']"],
    GameFilter:          ["#game-filter"],
    ClipCard:            ["#results .result"],
    ClipFavorite:        ["#results .result .fav-btn", ".side-nav .nav-item[data-view='favorites']"],
    TranscribeTool:      ["#btn-transcribe"],
    TranscriptEditor:    ["#results .result", ".side-nav .nav-item[data-view='clips']"],
    CollectionsPanel:    ["#btn-new-folder", "#folders-grid", ".side-nav .nav-item[data-view='folders']"],
    GoogleDriveSettings: ["#cloud-connect-btn", "#cloud-recheck", "#cloud-account", "#sidebar-cloud-card"],
    RecorderSettings:    ["#btn-replay"],
    OnboardingRestart:   ["#btn-run-onboarding"]
  };

  function isVisible(el) {
    if (!el || !el.isConnected) return false;
    if (el.closest("[hidden]")) return false;
    var style = window.getComputedStyle(el);
    if (style.visibility === "hidden" || style.display === "none" || style.opacity === "0") return false;
    var r = el.getBoundingClientRect();
    return r.width >= 4 && r.height >= 4;
  }

  function resolveTarget(name) {
    var list = TARGETS[name] || [];
    for (var i = 0; i < list.length; i++) {
      var el = document.querySelector(list[i]);
      if (isVisible(el)) return el;
    }
    return null;
  }

  /* ------------------------------------------------------------------ */
  /* Nawigacja między widokami                                           */
  /* ------------------------------------------------------------------ */

  var VIEW_BUTTON = {
    clips:     ".side-nav .nav-item[data-view='clips']",
    folders:   ".side-nav .nav-item[data-view='folders']",
    favorites: ".side-nav .nav-item[data-view='favorites']",
    cloud:     ".side-nav .nav-item[data-view='cloud']",
    settings:  "#btn-settings"
  };

  function currentView() {
    var current = document.querySelector(".side-nav .nav-item.active");
    return current ? current.dataset.view : "clips";
  }

  // Klikamy prawdziwy przycisk nawigacji, żeby wykonała się cała logika widoku
  // (wczytanie folderów, stanu chmury, ulubionych) zamiast samego przełączenia klas.
  function gotoView(view) {
    if (!view || currentView() === view) return false;
    var btn = document.querySelector(VIEW_BUTTON[view] || "");
    if (!btn) return false;
    btn.click();
    return true;
  }

  // Okna modalne aplikacji mogą zawierać niezapisane dane (wycinanie fragmentu,
  // wybór folderu, edycja wersu transkrypcji). Nie przechodzimy wtedy nigdzie sami.
  function appIsBusy() {
    var blockers = ["#player-overlay", "#cut-overlay", "#config-overlay", "#replay-overlay", "#confirm-overlay"];
    for (var i = 0; i < blockers.length; i++) {
      var el = document.querySelector(blockers[i]);
      if (el && !el.hidden) return true;
    }
    return !!document.querySelector(".seg.editing");
  }

  /* ------------------------------------------------------------------ */
  /* Kroki                                                               */
  /* ------------------------------------------------------------------ */

  // `when` pozwala pominąć krok w konfiguracji, w której nie ma sensu.
  // `placement` to preferowana strona karty — silnik zmieni ją, gdy zabraknie miejsca.
  // `allowTargetInteraction` / `allowBackgroundInteraction` odblokowują klikanie.
  var STEPS = [
    { id: "welcome", kind: "modal" },

    { id: "automatic-clips", view: "clips", target: "ClipLibrary", placement: "bottom",
      when: function (ctx) { return ctx.configured; } },

    { id: "clips-folder", view: "settings", target: "ClipsRootSetting", placement: "left",
      when: function (ctx) { return !ctx.configured; } },

    { id: "games-organized",  view: "clips",   target: "GameFilter",          placement: "bottom" },
    { id: "clip-editing",     view: "clips",   target: "ClipCard",            placement: "right" },
    { id: "transcription",    view: "clips",   target: "TranscribeTool",      placement: "right" },
    { id: "transcript-edit",  view: "clips",   target: "TranscriptEditor",    placement: "right" },
    { id: "favorites",        view: "clips",   target: "ClipFavorite",        placement: "right" },
    { id: "collections",      view: "folders", target: "CollectionsPanel",    placement: "bottom" },
    { id: "cloud",            view: "cloud",   target: "GoogleDriveSettings", placement: "right" },

    { id: "done", kind: "modal", view: "clips" }
  ];

  /* ------------------------------------------------------------------ */
  /* Zapis stanu                                                         */
  /* ------------------------------------------------------------------ */

  // Stan trzyma serwer (settings.json). Gdy punkt końcowy jest niedostępny —
  // na przykład przy nieprzebudowanym backendzie — silnik korzysta z localStorage,
  // ale tylko na czas trwania sesji: okno aplikacji dostaje przy każdym starcie
  // losowy port, a localStorage jest przypisany do adresu razem z portem, więc
  // po restarcie zapas awaryjny jest pusty. Dlatego bez serwera samouczek
  // nie uruchamia się sam (patrz maybeAutoStart).
  var store = {
    state: null,
    remote: true,

    empty: function () {
      return {
        started: false, completed: false, skipped: false, current_step: null,
        completed_version: 0, current_version: VERSION, seen_hints: []
      };
    },

    readLocal: function () {
      try {
        var raw = window.localStorage.getItem(LOCAL_KEY);
        if (raw) return Object.assign(this.empty(), JSON.parse(raw));
      } catch (e) { /* Pusty stan jest poprawnym wynikiem. */ }
      return this.empty();
    },

    writeLocal: function () {
      try { window.localStorage.setItem(LOCAL_KEY, JSON.stringify(this.state)); } catch (e) {}
    },

    load: function () {
      var self = this;
      return fetch("/api/onboarding")
        .then(function (r) { if (!r.ok) throw new Error("http " + r.status); return r.json(); })
        .then(function (data) { self.remote = true; self.state = data; return data; })
        .catch(function () {
          self.remote = false;
          self.state = self.readLocal();
          return self.state;
        });
    },

    patch: function (patch) {
      var self = this;
      var next = Object.assign({}, this.state || this.empty());
      next.seen_hints = (next.seen_hints || []).slice();
      Object.keys(patch).forEach(function (key) {
        if (key === "seen_hint") {
          if (patch[key] && next.seen_hints.indexOf(patch[key]) === -1) next.seen_hints.push(patch[key]);
        } else if (patch[key] !== null && patch[key] !== undefined) {
          next[key] = patch[key];
        }
      });
      if (next.completed) { next.completed_version = VERSION; next.current_step = null; }
      this.state = next;

      if (!this.remote) { this.writeLocal(); return Promise.resolve(this.state); }
      return fetch("/api/onboarding", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(patch)
      })
        .then(function (r) { if (!r.ok) throw new Error("http " + r.status); return r.json(); })
        .then(function (data) { self.state = data; return data; })
        .catch(function () { self.writeLocal(); return self.state; });
    },

    restart: function () {
      var self = this;
      if (!this.remote) {
        this.state = Object.assign({}, this.state, { started: true, skipped: false, current_step: null });
        this.writeLocal();
        return Promise.resolve(this.state);
      }
      return fetch("/api/onboarding/restart", { method: "POST" })
        .then(function (r) { if (!r.ok) throw new Error("http " + r.status); return r.json(); })
        .then(function (data) { self.state = data; return data; })
        .catch(function () { return self.state; });
    }
  };

  /* ------------------------------------------------------------------ */
  /* Warstwa nakładki                                                    */
  /* ------------------------------------------------------------------ */

  var dom = null;          // { veil, spot, card }
  var active = false;
  var steps = [];
  var index = -1;
  var targetEl = null;
  var lastRect = null;
  var rafId = 0;
  var lastLookup = 0;
  var token = 0;           // Unieważnia trwające przejście, gdy użytkownik kliknie szybciej.
  var skipOpen = false;
  var context = { configured: true };

  var reduceMotion = window.matchMedia
    ? window.matchMedia("(prefers-reduced-motion: reduce)").matches
    : false;

  function mount() {
    if (dom) return dom;
    var veil = document.createElement("div");
    veil.className = "kc-ob-veil";

    var spot = document.createElement("div");
    spot.className = "kc-ob-spot";

    var card = document.createElement("div");
    card.className = "kc-ob-card";
    card.setAttribute("role", "dialog");
    card.setAttribute("aria-modal", "true");
    card.setAttribute("aria-live", "polite");

    document.body.appendChild(veil);
    document.body.appendChild(spot);
    document.body.appendChild(card);
    dom = { veil: veil, spot: spot, card: card };

    veil.addEventListener("mousedown", function (e) { e.preventDefault(); e.stopPropagation(); });
    document.addEventListener("keydown", onKeyDown, true);
    window.addEventListener("resize", reposition);
    return dom;
  }

  function unmount() {
    if (!dom) return;
    document.removeEventListener("keydown", onKeyDown, true);
    window.removeEventListener("resize", reposition);
    if (rafId) cancelAnimationFrame(rafId);
    rafId = 0;
    releaseTarget();
    dom.veil.remove();
    dom.spot.remove();
    dom.card.remove();
    dom = null;
  }

  function releaseTarget() {
    if (targetEl) targetEl.classList.remove("kc-ob-live");
    targetEl = null;
    lastRect = null;
  }

  function delay(ms) {
    return new Promise(function (resolve) { setTimeout(resolve, reduceMotion ? 0 : ms); });
  }

  /* ------------------------------------------------------------------ */
  /* Pozycjonowanie                                                      */
  /* ------------------------------------------------------------------ */

  // Element wyższy niż okno (np. cała siatka klipów) zostaje przycięty do widoku,
  // żeby obwódka reflektora nie uciekała poza ekran.
  function viewportRect(el) {
    var r = el.getBoundingClientRect();
    var top = Math.max(r.top, MARGIN);
    var left = Math.max(r.left, 0);
    var bottom = Math.min(r.bottom, window.innerHeight - 4);
    var right = Math.min(r.right, window.innerWidth);
    // Bardzo wysoki element (cała siatka klipów) dostaje reflektor tylko na górnej
    // części — inaczej podświetlenie zajęłoby cały ekran i nie zostałoby miejsca na kartę.
    var limit = window.innerHeight * MAX_SPOT;
    if (bottom - top > limit) bottom = top + limit;
    return {
      top: top, left: left, bottom: bottom, right: right,
      width: Math.max(0, right - left),
      height: Math.max(0, bottom - top)
    };
  }

  function sameRect(a, b) {
    if (!a || !b) return false;
    return Math.abs(a.top - b.top) < 0.5 && Math.abs(a.left - b.left) < 0.5 &&
           Math.abs(a.width - b.width) < 0.5 && Math.abs(a.height - b.height) < 0.5;
  }

  function placeSpotlight(rect) {
    var pad = 6;
    var radius = 12;
    if (targetEl) {
      var corner = parseFloat(window.getComputedStyle(targetEl).borderTopLeftRadius) || 0;
      radius = Math.min(22, Math.max(8, corner + pad));
    }
    dom.spot.style.top = (rect.top - pad) + "px";
    dom.spot.style.left = (rect.left - pad) + "px";
    dom.spot.style.width = (rect.width + pad * 2) + "px";
    dom.spot.style.height = (rect.height + pad * 2) + "px";
    dom.spot.style.borderRadius = radius + "px";
  }

  // Wybiera pierwszą stronę z wystarczającą ilością miejsca; gdy żadna nie mieści
  // karty, bierze tę z największym zapasem i dosuwa kartę do krawędzi okna.
  function placeCard(rect, preferred) {
    var card = dom.card;
    var cw = card.offsetWidth;
    var ch = card.offsetHeight;
    var vw = window.innerWidth;
    var vh = window.innerHeight;

    var space = {
      right:  vw - rect.right - GAP - MARGIN,
      left:   rect.left - GAP - MARGIN,
      bottom: vh - rect.bottom - GAP - MARGIN,
      top:    rect.top - GAP - MARGIN
    };
    var need = { right: cw, left: cw, bottom: ch, top: ch };

    var order = [];
    [preferred, "right", "bottom", "left", "top"].forEach(function (side) {
      if (side && order.indexOf(side) === -1) order.push(side);
    });

    var side = null;
    for (var i = 0; i < order.length; i++) {
      if (space[order[i]] >= need[order[i]]) { side = order[i]; break; }
    }
    var fits = !!side;
    if (!side) {
      side = order.slice().sort(function (a, b) {
        return (space[b] - need[b]) - (space[a] - need[a]);
      })[0];
    }

    var left, top;
    if (side === "right")       { left = rect.right + GAP;      top = rect.top + rect.height / 2 - ch / 2; }
    else if (side === "left")   { left = rect.left - GAP - cw;  top = rect.top + rect.height / 2 - ch / 2; }
    else if (side === "bottom") { top = rect.bottom + GAP;      left = rect.left + rect.width / 2 - cw / 2; }
    else                        { top = rect.top - GAP - ch;    left = rect.left + rect.width / 2 - cw / 2; }

    left = Math.min(Math.max(left, MARGIN), Math.max(MARGIN, vw - cw - MARGIN));
    top = Math.min(Math.max(top, MARGIN + 20), Math.max(MARGIN, vh - ch - MARGIN));

    card.style.left = left + "px";
    card.style.top = top + "px";

    // Strzałka pokazuje kierunek celu tylko wtedy, gdy karta faktycznie stoi obok niego.
    card.classList.toggle("kc-ob-noarrow", !fits);
    var arrow = card.querySelector(".kc-ob-arrow");
    if (arrow && fits) {
      arrow.dataset.side = side;
      if (side === "right" || side === "left") {
        arrow.style.left = "";
        arrow.style.top = clamp(rect.top + rect.height / 2 - top - 5, 14, ch - 24) + "px";
      } else {
        arrow.style.top = "";
        arrow.style.left = clamp(rect.left + rect.width / 2 - left - 5, 14, cw - 24) + "px";
      }
    }
  }

  function clamp(value, min, max) {
    return Math.min(Math.max(value, min), Math.max(min, max));
  }

  function reposition() {
    if (!dom || !active || !targetEl) return;
    var step = steps[index];
    if (!step) return;
    lastRect = viewportRect(targetEl);
    placeSpotlight(lastRect);
    placeCard(lastRect, step.placement);
  }

  // Pętla śledzi układ: przewijanie, zmianę rozmiaru okna, inne skalowanie systemu
  // oraz podmianę zawartości (np. odświeżenie siatki klipów po skanie).
  function track() {
    rafId = requestAnimationFrame(track);
    if (!active || !dom) return;
    var step = steps[index];
    if (!step || !step.target) return;

    if (!targetEl || !isVisible(targetEl)) {
      var now = Date.now();
      if (now - lastLookup < 200) return;
      lastLookup = now;
      var found = resolveTarget(step.target);
      if (found) { attachTarget(found, step); reposition(); }
      else if (targetEl) { detachToCenter(); }
      return;
    }

    var rect = viewportRect(targetEl);
    if (!sameRect(rect, lastRect)) {
      lastRect = rect;
      placeSpotlight(rect);
      placeCard(rect, step.placement);
    }
  }

  function attachTarget(el, step) {
    releaseTarget();
    targetEl = el;
    if (step.allowTargetInteraction) el.classList.add("kc-ob-live");
    dom.spot.classList.add("kc-ob-on");
    dom.veil.classList.remove("kc-ob-plain");
    dom.veil.classList.toggle("kc-ob-pass", step.allowBackgroundInteraction === true);
    dom.card.classList.remove("kc-ob-modal");
  }

  // Cel zniknął albo nigdy się nie pojawił — krok zostaje pokazany jako mała karta
  // na środku, bez reflektora. Samouczek nigdy nie przerywa się z tego powodu.
  function detachToCenter() {
    releaseTarget();
    if (!dom) return;
    dom.spot.classList.remove("kc-ob-on");
    dom.veil.classList.add("kc-ob-plain");
    dom.veil.classList.remove("kc-ob-pass");
    dom.card.classList.add("kc-ob-modal", "kc-ob-noarrow");
    dom.card.style.left = "";
    dom.card.style.top = "";
  }

  // Wysoki element wystarczy przewinąć tak, aby jego góra weszła w kadr; centrowanie
  // zaprowadziłoby w środek długiej listy, daleko od tego, co krok opisuje.
  function scrollIntoViewIfNeeded(el) {
    var r = el.getBoundingClientRect();
    var visible = Math.min(r.bottom, window.innerHeight - MARGIN) - Math.max(r.top, MARGIN);
    if (visible >= Math.min(r.height, window.innerHeight * MAX_SPOT)) return false;
    var scroller = document.scrollingElement || document.documentElement;
    var top = Math.max(0, window.scrollY + r.top - window.innerHeight * 0.22);
    try {
      scroller.scrollTo({ top: top, behavior: reduceMotion ? "auto" : "smooth" });
    } catch (e) {
      scroller.scrollTop = top;
    }
    return true;
  }

  // Zwraca kandydata o najwyższym priorytecie. Gdy znajdzie tylko zapasowy, czeka
  // jeszcze chwilę — panel z pierwszego wyboru może się dopiero renderować
  // (np. widok chmury czeka na odpowiedź o stanie połączenia).
  function waitForTarget(name, timeout, stillValid) {
    var list = TARGETS[name] || [];
    var deadline = Date.now() + timeout;
    var best = null;
    var bestIndex = list.length;
    var grace = 0;
    return new Promise(function (resolve) {
      (function attempt() {
        if (!stillValid()) return resolve(null);
        for (var i = 0; i < bestIndex; i++) {
          var el = document.querySelector(list[i]);
          if (isVisible(el)) { best = el; bestIndex = i; break; }
        }
        if (bestIndex === 0) return resolve(best);
        if (best && !grace) grace = Date.now() + 500;
        if ((grace && Date.now() > grace) || Date.now() > deadline) return resolve(best);
        setTimeout(attempt, 60);
      })();
    });
  }

  /* ------------------------------------------------------------------ */
  /* Rysowanie karty                                                     */
  /* ------------------------------------------------------------------ */

  function spotlightSteps() {
    return steps.filter(function (s) { return s.kind !== "modal"; });
  }

  function buildCard(html) {
    dom.card.innerHTML = '<span class="kc-ob-arrow"></span>' + html;
  }

  // Pusty tekst oznacza element opcjonalny — znika zamiast zostawiać pustą linię.
  function setText(selector, value) {
    var el = dom.card.querySelector(selector);
    if (!el) return;
    if (!value) { el.remove(); return; }
    el.textContent = value;
  }

  function button(label, className, handler) {
    var b = document.createElement("button");
    b.type = "button";
    b.className = className;
    b.textContent = label;
    b.addEventListener("click", handler);
    return b;
  }

  function renderStep(step) {
    var isModal = step.kind === "modal";
    dom.card.classList.toggle("kc-ob-modal", isModal);
    dom.card.classList.toggle("kc-ob-noarrow", isModal);

    if (isModal) {
      buildCard(
        '<h3 class="kc-ob-title"></h3>' +
        '<p class="kc-ob-body"></p>' +
        '<p class="kc-ob-note"></p>' +
        '<div class="kc-ob-actions"></div>'
      );
      setText(".kc-ob-title", stepTxt(step.id, "title"));
      setText(".kc-ob-body", stepTxt(step.id, "body"));
      setText(".kc-ob-note", stepTxt(step.id, "note"));

      var modalActions = dom.card.querySelector(".kc-ob-actions");
      var secondary = stepTxt(step.id, "secondary");
      if (secondary) modalActions.appendChild(button(secondary, "btn ghost", askSkip));
      modalActions.appendChild(
        button(stepTxt(step.id, "primary") || txt("next", "Dalej"), "btn primary", next));
      dom.card.style.left = "";
      dom.card.style.top = "";
      return;
    }

    var list = spotlightSteps();
    var position = list.indexOf(step) + 1;
    var total = list.length;

    buildCard(
      '<div class="kc-ob-head">' +
        '<span class="kc-ob-step"></span>' +
        '<span class="kc-ob-head-spacer"></span>' +
        '<button type="button" class="kc-ob-x">✕</button>' +
      '</div>' +
      '<h3 class="kc-ob-title"></h3>' +
      '<p class="kc-ob-body"></p>' +
      '<p class="kc-ob-hint"><span class="kc-ob-hint-ic">💡</span><span class="kc-ob-hint-text"></span></p>' +
      '<div class="kc-ob-progress"><i></i></div>' +
      '<div class="kc-ob-actions">' +
        '<button type="button" class="kc-ob-skip"></button>' +
        '<span class="kc-ob-actions-spacer"></span>' +
      '</div>'
    );

    setText(".kc-ob-step", txt("stepOf", "Krok {n} z {total}")
      .replace("{n}", String(position)).replace("{total}", String(total)));
    setText(".kc-ob-title", stepTxt(step.id, "title"));
    setText(".kc-ob-body", stepTxt(step.id, "body"));

    var hint = stepTxt(step.id, "hint");
    if (hint) setText(".kc-ob-hint-text", hint);
    else dom.card.querySelector(".kc-ob-hint").remove();

    dom.card.querySelector(".kc-ob-progress > i").style.width =
      Math.round((position / total) * 100) + "%";

    var skipLabel = txt("skip", "Pomiń samouczek");
    var skipBtn = dom.card.querySelector(".kc-ob-skip");
    skipBtn.textContent = skipLabel;
    skipBtn.addEventListener("click", askSkip);

    var close = dom.card.querySelector(".kc-ob-x");
    close.setAttribute("aria-label", skipLabel);
    close.addEventListener("click", askSkip);

    var row = dom.card.querySelector(".kc-ob-actions");
    if (index > 0) row.appendChild(button(txt("back", "Wstecz"), "btn ghost", back));
    var last = index === steps.length - 1;
    row.appendChild(button(last ? txt("finish", "Zakończ") : txt("next", "Dalej"), "btn primary", next));
  }

  function focusPrimary() {
    var primary = dom.card.querySelector(".btn.primary") || dom.card.querySelector("button");
    if (primary) primary.focus({ preventScroll: true });
  }

  /* ------------------------------------------------------------------ */
  /* Przejścia między krokami                                            */
  /* ------------------------------------------------------------------ */

  function show(i) {
    if (!dom) return Promise.resolve();
    index = i;
    var step = steps[i];
    token += 1;
    var mine = token;
    var valid = function () { return mine === token && active; };

    dom.card.classList.remove("kc-ob-on");

    return delay(STEP_GAP).then(function () {
      if (!valid()) return;
      if (step.view && currentView() !== step.view && !appIsBusy()) {
        gotoView(step.view);
        return delay(140);
      }
    }).then(function () {
      if (!valid()) return null;
      if (!step.target) {
        releaseTarget();
        dom.spot.classList.remove("kc-ob-on");
        dom.veil.classList.add("kc-ob-plain");
        dom.veil.classList.remove("kc-ob-pass");
        var scroller = document.scrollingElement || document.documentElement;
        scroller.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" });
        return null;
      }
      return waitForTarget(step.target, 1400, valid);
    }).then(function (el) {
      if (!valid() || !el) return el || null;
      if (scrollIntoViewIfNeeded(el)) return delay(220).then(function () { return el; });
      return el;
    }).then(function (el) {
      if (!valid()) return;
      renderStep(step);
      if (step.target && el) {
        attachTarget(el, step);
        lastRect = null;
        reposition();
      } else if (step.target) {
        detachToCenter();
      }
      requestAnimationFrame(function () {
        if (!valid()) return;
        dom.card.classList.add("kc-ob-on");
        focusPrimary();
      });
      store.patch({ current_step: step.id });
    });
  }

  function next() {
    if (index >= steps.length - 1) return finish();
    show(index + 1);
  }

  function back() {
    if (index <= 0) return;
    show(index - 1);
  }

  function finish() {
    if (!appIsBusy()) gotoView("clips");
    store.patch({ completed: true });
    teardown();
  }

  /* ------------------------------------------------------------------ */
  /* Pominięcie                                                          */
  /* ------------------------------------------------------------------ */

  function askSkip() {
    if (!dom || skipOpen) return;
    skipOpen = true;

    dom.card.classList.add("kc-ob-modal", "kc-ob-noarrow");
    dom.card.style.left = "";
    dom.card.style.top = "";
    buildCard(
      '<h3 class="kc-ob-title"></h3>' +
      '<p class="kc-ob-body"></p>' +
      '<div class="kc-ob-actions"></div>'
    );
    setText(".kc-ob-title", txt("skipTitle", "Pominąć samouczek?"));
    setText(".kc-ob-body", txt("skipBody", ""));

    var actions = dom.card.querySelector(".kc-ob-actions");
    actions.appendChild(button(txt("skipStay", "Wróć"), "btn ghost", function () {
      skipOpen = false;
      var step = steps[index];
      if (!step) { teardown(); return; }
      renderStep(step);
      if (targetEl) reposition();
      dom.card.classList.add("kc-ob-on");
      focusPrimary();
    }));
    actions.appendChild(button(txt("skipGo", "Pomiń"), "btn primary", function () {
      skipOpen = false;
      var step = steps[index];
      store.patch({ skipped: true, current_step: step ? step.id : null });
      teardown();
    }));
    focusPrimary();
  }

  /* ------------------------------------------------------------------ */
  /* Klawiatura                                                          */
  /* ------------------------------------------------------------------ */

  function focusable() {
    return Array.prototype.filter.call(
      dom.card.querySelectorAll("button, [href], input, select, textarea, [tabindex]:not([tabindex='-1'])"),
      function (el) { return !el.disabled; }
    );
  }

  function onKeyDown(e) {
    if (!active || !dom) return;

    if (e.key === "Escape") {
      e.preventDefault(); e.stopPropagation();
      if (!skipOpen) askSkip();
      return;
    }
    // Skupienie nie może uciec pod nakładkę.
    if (e.key === "Tab") {
      var items = focusable();
      if (!items.length) return;
      var first = items[0];
      var last = items[items.length - 1];
      if (!dom.card.contains(document.activeElement)) { e.preventDefault(); first.focus(); return; }
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      return;
    }
    if (skipOpen) return;
    // Krok z klikalnym celem może zawierać pole tekstowe — tam klawisze należą do niego.
    var focused = document.activeElement;
    if (focused && !dom.card.contains(focused) &&
        (focused.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(focused.tagName))) return;
    if (e.key === "Enter") {
      // Enter na przycisku wywołuje jego własne kliknięcie — nie dublujemy akcji.
      if (focused && dom.card.contains(focused) && focused.tagName === "BUTTON") return;
      e.preventDefault(); next();
      return;
    }
    if (e.key === "ArrowRight") { e.preventDefault(); next(); }
    else if (e.key === "ArrowLeft") { e.preventDefault(); back(); }
  }

  /* ------------------------------------------------------------------ */
  /* Uruchamianie i kończenie                                            */
  /* ------------------------------------------------------------------ */

  function loadContext() {
    return fetch("/api/config")
      .then(function (r) { return r.json(); })
      .then(function (cfg) { return { configured: cfg.configured !== false }; })
      .catch(function () { return { configured: true }; });
  }

  function begin(startStepId) {
    if (active) return Promise.resolve();
    return loadContext().then(function (ctx) {
      context = ctx;
      steps = STEPS.filter(function (s) { return !s.when || s.when(context); });
      if (!steps.length) return;
      active = true;
      skipOpen = false;
      mount();
      dom.veil.classList.add("kc-ob-on", "kc-ob-plain");
      store.patch({ started: true });
      if (!rafId) track();

      var at = 0;
      if (startStepId) {
        for (var i = 0; i < steps.length; i++) {
          if (steps[i].id === startStepId) { at = i; break; }
        }
      }
      return show(at);
    });
  }

  function teardown() {
    active = false;
    token += 1;
    skipOpen = false;
    index = -1;
    steps = [];
    if (dom) {
      dom.card.classList.remove("kc-ob-on");
      dom.veil.classList.remove("kc-ob-on");
      dom.spot.classList.remove("kc-ob-on");
    }
    setTimeout(function () { if (!active) unmount(); }, reduceMotion ? 0 : FADE);
  }

  /* ------------------------------------------------------------------ */
  /* Wznowienie                                                          */
  /* ------------------------------------------------------------------ */

  function askResume(stepId) {
    active = true;
    steps = STEPS.filter(function (s) { return !s.when || s.when(context); });
    mount();
    dom.veil.classList.add("kc-ob-on", "kc-ob-plain");
    dom.spot.classList.remove("kc-ob-on");
    dom.card.classList.add("kc-ob-modal", "kc-ob-noarrow");
    dom.card.style.left = "";
    dom.card.style.top = "";
    buildCard(
      '<h3 class="kc-ob-title"></h3>' +
      '<p class="kc-ob-body"></p>' +
      '<div class="kc-ob-actions"></div>'
    );
    setText(".kc-ob-title", txt("resumeTitle", "Samouczek nie został ukończony"));
    setText(".kc-ob-body", txt("resumeBody", ""));

    var actions = dom.card.querySelector(".kc-ob-actions");
    actions.appendChild(button(txt("resumeSkip", "Pomiń"), "btn ghost", function () {
      store.patch({ skipped: true });
      teardown();
    }));
    actions.appendChild(button(txt("resumeRestart", "Od początku"), "btn ghost", function () {
      teardown();
      setTimeout(function () { begin(null); }, FADE);
    }));
    actions.appendChild(button(txt("resumeContinue", "Kontynuuj"), "btn primary", function () {
      teardown();
      setTimeout(function () { begin(stepId); }, FADE);
    }));

    requestAnimationFrame(function () {
      dom.card.classList.add("kc-ob-on");
      focusPrimary();
    });
  }

  /* ------------------------------------------------------------------ */
  /* Podpowiedzi o nowych funkcjach                                      */
  /* ------------------------------------------------------------------ */

  // Onboarding = pierwsze zapoznanie z aplikacją. Podpowiedzi = pojedyncze nowości
  // w kolejnych wydaniach; nowa funkcja NIE uruchamia całego samouczka od nowa.
  //
  //   KeepClipOnboarding.registerFeatureHint({
  //     id: "share-v1", target: "ShareButton", placement: "left",
  //     title: "Udostępnianie", description: "Możesz teraz wysłać klip jednym kliknięciem."
  //   });
  //
  // Cel musi istnieć w TARGETS. Zobaczoną podpowiedź zapamiętuje seen_hints.
  var hints = [];

  function registerFeatureHint(hint) {
    if (!hint || !hint.id || !hint.target) return;
    if (hints.some(function (h) { return h.id === hint.id; })) return;
    hints.push(hint);
  }

  function seenHint(id) {
    var seen = (store.state && store.state.seen_hints) || [];
    return seen.indexOf(id) !== -1;
  }

  function showHint(hint) {
    return new Promise(function (resolve) {
      var el = resolveTarget(hint.target);
      if (!el) { resolve(false); return; }

      active = true;
      steps = [{ id: "hint:" + hint.id, target: hint.target, placement: hint.placement || "right" }];
      index = 0;
      mount();
      dom.veil.classList.add("kc-ob-on");
      buildCard(
        '<div class="kc-ob-head"><span class="kc-ob-new"></span></div>' +
        '<h3 class="kc-ob-title"></h3>' +
        '<p class="kc-ob-body"></p>' +
        '<div class="kc-ob-actions"><span class="kc-ob-actions-spacer"></span></div>'
      );
      setText(".kc-ob-new", txt("hintNew", "Nowość"));
      setText(".kc-ob-title", hint.title || "");
      setText(".kc-ob-body", hint.description || "");
      dom.card.querySelector(".kc-ob-actions").appendChild(
        button(txt("hintOk", "Rozumiem"), "btn primary", function () {
          store.patch({ seen_hint: hint.id });
          teardown();
          resolve(true);
        })
      );

      attachTarget(el, steps[0]);
      lastRect = null;
      reposition();
      if (!rafId) track();
      requestAnimationFrame(function () {
        dom.card.classList.add("kc-ob-on");
        focusPrimary();
      });
    });
  }

  function showPendingHints() {
    if (active || !store.state) return Promise.resolve();
    // Nowy użytkownik ma najpierw przejść (lub pominąć) samouczek.
    if (!store.state.completed && !store.state.skipped) return Promise.resolve();
    return hints.filter(function (h) { return !seenHint(h.id); })
      .reduce(function (chain, hint) {
        return chain.then(function () { return showHint(hint); });
      }, Promise.resolve());
  }

  /* ------------------------------------------------------------------ */
  /* Wejście z ustawień                                                  */
  /* ------------------------------------------------------------------ */

  // Ręczne uruchomienie nie kasuje informacji, że ktoś już przeszedł samouczek.
  function restart() {
    if (active) return Promise.resolve();
    return store.restart().then(function () { return begin(null); });
  }

  function bindSettingsButton() {
    var btn = document.getElementById("btn-run-onboarding");
    if (!btn || btn.dataset.bound === "1") return;
    btn.dataset.bound = "1";
    btn.addEventListener("click", function () { restart(); });
  }

  /* ------------------------------------------------------------------ */
  /* Start                                                               */
  /* ------------------------------------------------------------------ */

  function maybeAutoStart() {
    var s = store.state;
    if (!s || active) return;
    // Gdy serwer nie potrafi zapisać stanu, samouczek nie miałby jak zapamiętać,
    // że ktoś go już widział, i wracałby przy każdym uruchomieniu. Zostaje wtedy
    // wyłącznie ręczne uruchomienie z Ustawień.
    if (!store.remote) {
      console.warn("KeepClip: /api/onboarding nie odpowiada — samouczek nie uruchomi " +
                   "się automatycznie. Przebuduj aplikację (dotnet build -c Release).");
      return;
    }
    if (s.completed || s.skipped) { showPendingHints(); return; }
    // Otwarte okno modalne aplikacji może zawierać niezapisane dane — czekamy.
    if (appIsBusy()) { setTimeout(maybeAutoStart, 1500); return; }
    if (s.started && s.current_step) askResume(s.current_step);
    else begin(null);
  }

  function boot() {
    bindSettingsButton();
    store.load()
      .then(loadContext)
      .then(function (ctx) {
        context = ctx;
        if (ctx.configured) { setTimeout(maybeAutoStart, 500); return; }
        // Świeża instalacja najpierw prosi o folder z klipami — samouczek czeka.
        document.addEventListener("keepclip:configured", function () {
          loadContext().then(function (fresh) {
            context = fresh;
            setTimeout(maybeAutoStart, 700);
          });
        }, { once: true });
      });
  }

  window.KeepClipOnboarding = {
    start: function (stepId) { return begin(stepId || null); },
    restart: restart,
    stop: teardown,
    isActive: function () { return active; },
    state: function () { return store.state; },
    registerFeatureHint: registerFeatureHint,
    showPendingHints: showPendingHints,
    targets: TARGETS,
    steps: STEPS
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
