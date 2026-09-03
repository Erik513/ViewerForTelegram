"use strict";

const host = window.chrome && window.chrome.webview
  ? (msg) => window.chrome.webview.postMessage(msg)
  : (msg) => console.log("host <-", msg);

const CACHE = "https://cache.viewerfortelegram/";

const settingsEl = document.getElementById("settings");
const cacheEl = document.getElementById("cache");
const groupEl = document.getElementById("group");
const rangeEl = document.getElementById("range");
const volumeEl = document.getElementById("volume");
const searchEl = document.getElementById("search");
const statusEl = document.getElementById("status");
const rowsBody = document.getElementById("rows");
const emptyNote = document.getElementById("empty");

let songs = [];
let sortKey = null;
let sortDir = "asc";

rangeEl.value = localStorage.getItem("rangeDays") || "7";

const savedVol = parseInt(localStorage.getItem("volume"), 10);
volumeEl.value = String(isNaN(savedVol) ? 40 : Math.min(100, Math.max(0, savedVol)));

// ---- von C# aufgerufen ----
window.tv = {
  setConnected(ok) {
    if (!ok) statusEl.textContent = "Nicht angemeldet – Einstellungen öffnen.";
  },
  setChats(list) {
    groupEl.innerHTML = "";
    for (const c of list || []) {
      const opt = document.createElement("option");
      opt.value = c.id;
      opt.textContent = "[" + c.kind + "] " + c.title;
      groupEl.appendChild(opt);
    }
    groupEl.disabled = list.length === 0;

    const remembered = localStorage.getItem("group");
    if (remembered && [...groupEl.options].some((o) => o.value === remembered)) {
      groupEl.value = remembered;
    }
    if (groupEl.value) requestLoad();
  },
  setSongs(list) {
    songs = Array.isArray(list) ? list : [];
    songs.forEach((s, i) => { s._i = i; });
    sortKey = null;
    render();
  },
  setStatus(text) {
    statusEl.textContent = text || "";
  },
  setCacheInfo(text) {
    cacheEl.textContent = text || "Cache: –";
  },
  setProgress(fileId, percent) {
    const bar = progressFor(fileId);
    if (!bar) return;
    if (percent >= 100) { bar.hidden = true; return; }
    bar.hidden = false;
    bar.value = percent;
  },
  clearProgress(fileId) {
    const bar = progressFor(fileId);
    if (bar) bar.hidden = true;
  }
};

function progressFor(fileId) {
  const row = rowsBody.querySelector('tr[data-file-id="' + cssEscape(fileId) + '"]');
  return row ? row.querySelector("progress") : null;
}

function cssEscape(v) {
  return String(v).replace(/"/g, '\\"');
}

// ---- Auslöser ----
settingsEl.addEventListener("click", () => host({ type: "settings" }));

groupEl.addEventListener("change", () => {
  localStorage.setItem("group", groupEl.value);
  requestLoad();
});

rangeEl.addEventListener("change", () => {
  localStorage.setItem("rangeDays", rangeEl.value);
  requestLoad();
});

volumeEl.addEventListener("input", () => {
  localStorage.setItem("volume", volumeEl.value);
  const v = currentVolume();
  rowsBody.querySelectorAll("audio").forEach((a) => { a.volume = v; });
});

function currentVolume() {
  const v = parseInt(volumeEl.value, 10);
  return isNaN(v) ? 0.4 : Math.min(1, Math.max(0, v / 100));
}

function requestLoad() {
  if (!groupEl.value) return;
  statusEl.textContent = "Lade ...";
  host({ type: "load", chatId: groupEl.value, days: parseInt(rangeEl.value, 10) });
}

// ---- Rendering ----
// Voller Neuaufbau - nur bei neuen Daten (setSongs).
function render() {
  const sorted = [...songs].sort(compare);
  rowsBody.innerHTML = "";
  for (const s of sorted) rowsBody.appendChild(buildRow(s));
  emptyNote.hidden = songs.length > 0;
  applySearch();
  updateSortIndicators();
}

// Nur umsortieren - vorhandene <tr> (und damit laufende Wiedergabe) bleiben
// erhalten, es werden keine <audio> neu gebaut.
function applySort() {
  const byId = new Map(
    [...rowsBody.querySelectorAll("tr")].map((r) => [r.dataset.fileId, r]));
  for (const s of [...songs].sort(compare)) {
    const r = byId.get(String(s.fileId));
    if (r) rowsBody.appendChild(r); // ans Ende verschieben -> Zielreihenfolge
  }
  updateSortIndicators();
}

function buildRow(s) {
  const tr = document.createElement("tr");
  tr.dataset.fileId = s.fileId;
  tr.dataset.search = (s.performer + " " + s.title + " " + s.fileName).toLowerCase();

  tr.appendChild(cell(s.dateDisplay, s.dateIso));

  const songTd = document.createElement("td");
  const title = document.createElement("div");
  title.className = "song-title";
  title.textContent = s.title;
  songTd.appendChild(title);
  if (s.performer) {
    const sub = document.createElement("div");
    sub.className = "song-sub";
    sub.textContent = s.performer;
    songTd.appendChild(sub);
  }
  tr.appendChild(songTd);

  const fileTd = document.createElement("td");
  fileTd.className = "file-cell";

  const audio = document.createElement("audio");
  audio.controls = true;
  audio.preload = "none";
  audio.volume = currentVolume();
  audio.src = CACHE + s.fileId;
  audio.addEventListener("play", () => pauseOthers(audio));

  const bar = document.createElement("progress");
  bar.max = 100;
  bar.value = 0;
  bar.hidden = true;
  bar.title = "Klicken: Laden abbrechen";
  bar.style.cursor = "pointer";

  // Abbruch NUR per Klick auf den Balken - audio-Events (pause/abort/suspend)
  // feuern bei großen Dateien auch ungewollt und würden den Download killen.
  bar.addEventListener("click", () => {
    if (bar.hidden) return;
    bar.hidden = true;
    host({ type: "cancel", fileId: s.fileId });
    audio.pause();
    audio.removeAttribute("src");
    audio.load();
    audio.src = CACHE + s.fileId;
  });

  const dur = document.createElement("span");
  dur.className = "dur";
  const fmt = ext(s.fileName);
  let durSet = false;
  const setMeta = (secs) => {
    dur.textContent = fmtSecs(secs) + " · " + s.sizeDisplay + (fmt ? " · " + fmt : "");
  };
  if (s.durationSec != null) {
    setMeta(s.durationSec);
    durSet = true;
  } else {
    dur.textContent = "– · " + s.sizeDisplay + (fmt ? " · " + fmt : "");
  }

  // Nur EINMAL nachtragen, wenn Telegram keine Dauer kannte - sonst
  // flackert die Anzeige bei kopflosen VBR-Dateien.
  audio.addEventListener("loadedmetadata", () => {
    if (!durSet && isFinite(audio.duration) && audio.duration > 0) {
      setMeta(audio.duration);
      durSet = true;
    }
  });

  fileTd.append(audio, bar, dur);
  tr.appendChild(fileTd);
  return tr;
}

function cell(text, sortValue) {
  const td = document.createElement("td");
  td.textContent = text || "";
  if (sortValue != null) td.dataset.sort = sortValue;
  return td;
}

function ext(fileName) {
  const i = (fileName || "").lastIndexOf(".");
  return i > 0 ? fileName.slice(i + 1).toUpperCase() : "";
}

function pauseOthers(current) {
  rowsBody.querySelectorAll("audio").forEach((a) => {
    if (a !== current) a.pause();
  });
}

// ---- Sortierung ----
document.querySelectorAll("th[data-key]").forEach((th) => {
  if (th.dataset.key === "none") return;
  th.addEventListener("click", () => {
    const key = th.dataset.key;
    if (sortKey !== key) { sortKey = key; sortDir = "asc"; }
    else if (sortDir === "asc") { sortDir = "desc"; }
    else { sortKey = null; sortDir = "asc"; }
    applySort();
  });
});

function compare(a, b) {
  if (sortKey === null) return a._i - b._i;
  const av = sortKey === "date" ? a.dateIso : a.title.toLowerCase();
  const bv = sortKey === "date" ? b.dateIso : b.title.toLowerCase();
  const r = av < bv ? -1 : av > bv ? 1 : 0;
  return sortDir === "asc" ? r : -r;
}

function updateSortIndicators() {
  document.querySelectorAll("th[data-key]").forEach((th) => {
    th.classList.remove("sort-asc", "sort-desc");
    if (th.dataset.key === sortKey) {
      th.classList.add(sortDir === "asc" ? "sort-asc" : "sort-desc");
    }
  });
}

// ---- Suche ----
searchEl.addEventListener("input", applySearch);

function applySearch() {
  const q = searchEl.value.trim().toLowerCase();
  let shown = 0;
  rowsBody.querySelectorAll("tr").forEach((tr) => {
    const match = q === "" || tr.dataset.search.includes(q);
    tr.classList.toggle("hidden", !match);
    if (match) shown++;
  });
  if (songs.length === 0) {
    statusEl.textContent = "Keine Audios im Zeitraum.";
  } else if (q === "") {
    statusEl.textContent = songs.length + " Audios";
  } else {
    statusEl.textContent = shown + " von " + songs.length + " Audios";
  }
}

function fmtSecs(total) {
  const s = Math.round(total);
  return Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0");
}
