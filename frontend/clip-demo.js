function createClipDemoPanel({ elements, request, t, notify, pickFile }) {
  let clipId = null;
  let revision = 0;
  let attachment = null;
  let busy = false;
  let activity = "";
  let loadError = false;

  function render() {
    elements.path.disabled = busy || clipId == null;
    elements.save.disabled = busy || clipId == null;
    elements.browse.hidden = !pickFile;
    elements.browse.disabled = busy || clipId == null;
    elements.remove.hidden = !attachment?.path;
    elements.remove.disabled = busy || clipId == null;
    elements.show.hidden = !attachment?.path;
    elements.show.disabled = busy || !attachment?.available;
    elements.save.textContent = attachment?.path ? t("demo.replace") : t("demo.assign");
    elements.status.classList.toggle("is-missing", !!attachment?.path && !attachment.available);
    elements.status.textContent = activity || (loadError ? t("demo.loadFailed") : attachment?.path
      ? t(attachment.available ? "demo.linked" : "demo.missing").replace("{name}", attachment.filename || attachment.path)
      : t("demo.empty"));
    elements.status.title = attachment?.path || "";
  }

  function current(id, version) { return clipId === id && revision === version; }

  async function json(url, options) {
    const response = await request(url, options);
    const data = await response.json();
    if (!response.ok) throw new Error(data.detail || response.statusText);
    return data;
  }

  function apply(data) {
    attachment = data;
    loadError = false;
    elements.path.value = data.path || "";
  }

  async function open(id) {
    const version = ++revision;
    clipId = id;
    attachment = null;
    loadError = false;
    elements.path.value = "";
    busy = true;
    activity = t("demo.loading");
    render();
    try {
      const data = await json(`/api/clips/${id}/demo`);
      if (current(id, version)) apply(data);
    } catch {
      if (current(id, version)) loadError = true;
    } finally {
      if (current(id, version)) { busy = false; activity = ""; render(); }
    }
  }

  function close() {
    revision++;
    clipId = null;
    attachment = null;
    busy = false;
    activity = "";
    loadError = false;
    elements.path.value = "";
    render();
  }

  async function change(remove) {
    if (busy || clipId == null || (remove && !attachment?.path)) return;
    const id = clipId;
    const version = revision;
    const path = elements.path.value.trim();
    if (!remove && !path) { notify(t("demo.pathRequired"), "error"); return; }
    busy = true;
    activity = t(remove ? "demo.removing" : "demo.saving");
    render();
    try {
      const options = remove ? { method: "DELETE" } : {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ path }),
      };
      const data = await json(`/api/clips/${id}/demo`, options);
      if (current(id, version)) {
        apply(data);
        notify(t(remove ? "demo.removed" : "demo.saved"));
      }
    } catch (error) {
      if (current(id, version)) notify(t("demo.failed").replace("{error}", error.message), "error");
    } finally {
      if (current(id, version)) { busy = false; activity = ""; render(); }
    }
  }

  async function browse() {
    if (busy || clipId == null || !pickFile) return;
    const id = clipId;
    const version = revision;
    busy = true;
    activity = t("demo.choosing");
    render();
    try {
      const path = await pickFile();
      if (path && current(id, version)) elements.path.value = path;
    } catch (error) {
      if (current(id, version)) notify(t("demo.failed").replace("{error}", error.message), "error");
    } finally {
      if (current(id, version)) { busy = false; activity = ""; render(); }
    }
  }

  async function show() {
    if (busy || !attachment?.available || !attachment.path) return;
    const id = clipId;
    const version = revision;
    try {
      await json("/api/show-in-explorer", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ path: attachment.path }),
      });
    } catch (error) {
      if (current(id, version)) notify(t("demo.showFailed").replace("{error}", error.message), "error");
    }
  }

  elements.save.addEventListener("click", () => change(false));
  elements.remove.addEventListener("click", () => change(true));
  elements.browse.addEventListener("click", browse);
  elements.show.addEventListener("click", show);
  elements.path.addEventListener("keydown", (event) => {
    if (event.key === "Enter") { event.preventDefault(); void change(false); }
  });
  render();
  return { open, close };
}

if (typeof module !== "undefined") module.exports = { createClipDemoPanel };
