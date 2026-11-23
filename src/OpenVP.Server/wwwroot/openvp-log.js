// The slot log widget, shared by the console (index.html) and the full-page log (log.html):
// level filters with counts, text search, colour by level, pause/resume, copy, download, expandable entries (stack traces),
// and links in the text: signal names and join names open the slot's debugger on that signal, IP IDs and slot numbers
// jump to the console, file paths open the Files view or are copied.
//
//   const log = OpenVPLog.create(rootElement, { slot: 1, listId: "log", filterId: "log-filter", onLink(kind, value, slot) {...} });
//   log.setSlot(2); log.setEntries([{ time, level, message, detail }]); log.add(entry); log.setSignals(["Room_On", ...]);
//   OpenVPLog.openDebugger(slot, signalName)     // used by the links; also useful on its own
//   OpenVPLog.renderExceptions(container, list, emptyText, {slot})  // the "Exceptions" list of a SIMPL# slot
(() => {
  "use strict";
  const LEVELS = ["Error", "Warning", "Notice", "Info", "Console"];
  const KEY = "openvp.log.levels";
  const pad = (n, w = 2) => String(n).padStart(w, "0");
  const fmtTime = t => { const d = new Date(t); return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`; };
  const stamp = () => { const d = new Date(); return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}-${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`; };

  // ---------------------------------------------------------------- opening the debugger on a signal
  // A debugger window already open for the slot (the named window openvp-debugger-N) is told with postMessage; otherwise the
  // page is opened with ?signal= in the URL. Either way the debugger sets its search, selects the row and scrolls to it.
  function openDebugger(slot, signal) {
    const name = "openvp-debugger-" + slot;
    const url = `debugger.html?slot=${slot}` + (signal ? `&signal=${encodeURIComponent(signal)}` : "");
    let w = null;
    try { w = window.open("", name); } catch { }
    if (!w) { try { window.open(url, name); } catch { } return; }
    let live = false;
    try { live = !!w.__openvpDebugger; } catch { }
    if (live) { try { w.postMessage({ type: "openvp-focus-signal", slot, signal }, location.origin); w.focus(); return; } catch { } }
    try { w.location.href = url; w.focus(); } catch { window.open(url, name); }
  }

  // ---------------------------------------------------------------- links in a log line
  const RE = new RegExp([
    "(?<join>\\bIPID[0-9A-Fa-f]{2,}\\.(?:in|out)\\.[dasDAS]\\d+\\b)",
    "(?<path>[A-Za-z]:\\\\[^\\s\"'<>|]+|\\\\(?:NVRAM|USER)\\\\[^\\s\"'<>|]*|/(?:nvram|user)/[^\\s\"'<>|]+|/(?:[\\w.@+-]+/)+[\\w.@+-]+|\\bdata/[\\w./-]+)",
    "(?<ip>\\bIP ?ID:? ?[0-9A-Fa-f]{2}\\b|\\bIPID[0-9A-Fa-f]{2}\\b)",
    "(?<slot>\\b[Ss]lot ?\\d{1,2}\\b)",
    "(?<word>[A-Za-z_#][\\w.:#\\-\\[\\]]*)"
  ].join("|"), "g");

  function link(kind, value, text, title) {
    const a = document.createElement("a");
    a.href = "#"; a.className = "ovl-link ovl-" + kind; a.dataset.k = kind; a.dataset.v = value; a.textContent = text; a.title = title;
    return a;
  }

  // The message as nodes: plain text with links where a signal, join, IP ID, slot or path is recognised.
  function linkify(text, slot, sigs) {
    const out = document.createDocumentFragment();
    let last = 0, m;
    RE.lastIndex = 0;
    while ((m = RE.exec(text))) {
      let s = m[0], g = m.groups, node = null;
      if (g.word) {
        s = s.replace(/[.:]+$/, "");
        if (s.length < 2 || !sigs || !sigs.has(s)) continue;
        node = link("sig", s, s, `Open the debugger on ${s}`);
      } else if (g.join) node = link("sig", s, s, `Open the debugger on ${s}`);
      else if (g.path) {
        s = s.replace(/[.,;:)\]'"]+$/, "");
        const pm = /^[\\/](nvram|user)[\\/](.*)$/i.exec(s);
        node = pm ? link("path", pm[1].toLowerCase() + "|" + pm[2].replace(/\\/g, "/"), s, "Show it in the Files list")
                  : link("hostpath", s, s, "Click to copy this path");
      } else if (g.ip) node = link("ipid", s.replace(/^IP ?ID:? ?/i, "").toUpperCase(), s, "Show this IP ID in the IP table");
      else if (g.slot) node = link("slot", s.replace(/\D/g, ""), s, `Go to slot ${s.replace(/\D/g, "")}`);
      if (!node) continue;
      if (m.index > last) out.appendChild(document.createTextNode(text.slice(last, m.index)));
      out.appendChild(node);
      last = m.index + s.length;
      RE.lastIndex = last;
    }
    if (last < text.length) out.appendChild(document.createTextNode(text.slice(last)));
    return out;
  }

  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch { }
    try {
      const ta = document.createElement("textarea"); ta.value = text; ta.style.cssText = "position:fixed;opacity:0";
      document.body.appendChild(ta); ta.select(); const ok = document.execCommand("copy"); ta.remove(); return ok;
    } catch { return false; }
  }

  // ---------------------------------------------------------------- the widget
  function create(root, o = {}) {
    const MAX = o.max || 2000;
    let slot = o.slot || 1, entries = [], held = [], paused = false, sigs = null, sigSlot = 0, seq = 0;
    let levels;
    try { levels = new Set(JSON.parse(localStorage.getItem(KEY) || "null") || LEVELS); } catch { levels = new Set(LEVELS); }
    root.classList.add("ovl");
    root.innerHTML =
      '<div class="ovl-bar"><div class="ovl-chips" role="group" aria-label="Show log levels"></div>' +
      `<input type="search" class="ovl-filter" id="${o.filterId || ""}" placeholder="Search the log (press L)" aria-label="Search the log" autocomplete="off">` +
      '<span class="ovl-btns"><button type="button" class="ovl-pause" title="Stop the list from changing while you read it. New lines are kept and added when you resume.">Pause</button>' +
      '<button type="button" class="ovl-copy" title="Copy the lines shown (after the filters) to the clipboard">Copy</button>' +
      '<button type="button" class="ovl-dl" title="Download the lines shown (after the filters) as a text file">Download</button></span></div>' +
      `<div class="ovl-list mono" id="${o.listId || ""}" title="Names of signals, IP IDs, slots and file paths in a line are links"></div>`;
    const chips = root.querySelector(".ovl-chips"), filter = root.querySelector(".ovl-filter"), list = root.querySelector(".ovl-list");
    const pauseBtn = root.querySelector(".ovl-pause"), copyBtn = root.querySelector(".ovl-copy"), dlBtn = root.querySelector(".ovl-dl");
    const chipEls = {};
    for (const lv of LEVELS) {
      const b = document.createElement("button");
      b.type = "button"; b.className = "ovl-chip lv-" + lv; b.dataset.lv = lv;
      b.title = `Show or hide ${lv} lines`;
      b.addEventListener("click", () => { if (levels.has(lv)) levels.delete(lv); else levels.add(lv); try { localStorage.setItem(KEY, JSON.stringify([...levels])); } catch { } paintChips(); render(); });
      chips.appendChild(b); chipEls[lv] = b;
    }
    const counts = () => { const c = {}; for (const e of entries) c[e.level] = (c[e.level] || 0) + 1; return c; };
    function paintChips() {
      const c = counts();
      for (const lv of LEVELS) {
        const b = chipEls[lv];
        b.classList.toggle("off", !levels.has(lv));
        b.setAttribute("aria-pressed", levels.has(lv) ? "true" : "false");
        b.textContent = `${lv} ${c[lv] || 0}`;
        b.hidden = lv === "Console" && !c[lv];
      }
    }
    const matches = e => {
      if (!levels.has(e.level) && LEVELS.includes(e.level)) return false;
      const q = filter.value.trim().toLowerCase();
      return !q || (e.message + " " + (e.detail || "") + " " + e.level).toLowerCase().includes(q);
    };
    function lineEl(e) {
      const d = document.createElement("div");
      d.className = "ovl-line lv-" + e.level;
      const head = document.createElement("div"); head.className = "ovl-head";
      if (e.detail) {
        const t = document.createElement("button"); t.type = "button"; t.className = "ovl-tog"; t.textContent = "▸"; t.title = "Show the stack trace"; t.setAttribute("aria-expanded", "false");
        head.appendChild(t);
      } else { const sp = document.createElement("span"); sp.className = "ovl-tog ovl-nt"; head.appendChild(sp); }
      const tm = document.createElement("span"); tm.className = "ovl-t"; tm.textContent = fmtTime(e.time);
      const lv = document.createElement("span"); lv.className = "ovl-lv"; lv.textContent = e.level;
      const msg = document.createElement("span"); msg.className = "ovl-msg"; msg.appendChild(linkify(e.message, slot, sigs));
      head.append(tm, " ", lv, " ", msg);
      d.appendChild(head);
      if (e.detail) {
        const pre = document.createElement("pre"); pre.className = "ovl-detail"; pre.hidden = true;
        pre.appendChild(linkify(e.detail, slot, sigs));
        d.appendChild(pre);
      }
      d._e = e;
      return d;
    }
    const atBottom = () => list.scrollHeight - list.scrollTop - list.clientHeight < 40;
    function render() {
      const frag = document.createDocumentFragment();
      for (const e of entries) if (matches(e)) frag.appendChild(lineEl(e));
      list.replaceChildren(frag);
      list.scrollTop = list.scrollHeight;
      empty();
    }
    function empty() {
      let n = list.querySelector(".ovl-empty");
      if (list.querySelector(".ovl-line")) { n?.remove(); return; }
      if (!n) { n = document.createElement("div"); n.className = "ovl-empty"; list.appendChild(n); }
      n.textContent = entries.length ? "No line matches the filters." : "Nothing logged yet.";
    }
    function addNow(e) {
      entries.push(e);
      if (entries.length > MAX) entries.splice(0, entries.length - MAX);
      if (matches(e)) {
        const stick = atBottom();
        list.querySelector(".ovl-empty")?.remove();
        list.appendChild(lineEl(e));
        while (list.children.length > MAX) list.removeChild(list.firstChild);
        if (stick) list.scrollTop = list.scrollHeight;
      }
    }
    function paintPause() { pauseBtn.textContent = paused ? `Resume${held.length ? ` (${held.length} new)` : ""}` : "Pause"; pauseBtn.classList.toggle("on", paused); }

    const api = {
      el: root, list, filter,
      get slot() { return slot; },
      setSlot(n) { slot = n; },
      setEntries(arr) {
        entries = arr.map(e => ({ time: e.time, level: e.level, message: e.message, detail: e.detail || null, n: ++seq })).slice(-MAX);
        held = []; paintChips(); paintPause(); render();
      },
      add(e) {
        const x = { time: e.time, level: e.level, message: e.message, detail: e.detail || null, n: ++seq };
        if (paused) { held.push(x); paintPause(); return; }
        addNow(x); paintChips();
      },
      setSignals(names) { sigs = names ? new Set(names) : null; render(); },
      /** Signal names of the slot, for linking (fetched once; call again after a new program is loaded). */
      async refreshSignals(force) {
        if (!force && sigs && sigSlot === slot) return;
        const mine = slot;
        try {
          const r = await fetch(`/api/slots/${mine}/signals`);
          if (!r.ok) throw 0;
          const arr = await r.json();
          if (mine !== slot) return;
          sigSlot = mine; sigs = new Set(arr.map(s => s.name)); render();
        } catch { if (mine === slot) { sigs = null; sigSlot = 0; } }
      },
      clear() { entries = []; held = []; paintChips(); render(); },
      focusFilter() { filter.focus(); filter.select(); },
      visibleText() {
        return entries.filter(matches).map(e => `${new Date(e.time).toLocaleString()}  ${e.level.padEnd(7)} ${e.message}` + (e.detail ? "\n" + e.detail.replace(/^/gm, "    ") : "")).join("\n");
      },
      pause(on) { paused = on; if (!on) { const h = held; held = []; for (const e of h) addNow(e); paintChips(); } paintPause(); }
    };

    filter.addEventListener("input", render);
    filter.addEventListener("keydown", e => { if (e.key === "Escape") { filter.value = ""; render(); filter.blur(); } });
    pauseBtn.addEventListener("click", () => api.pause(!paused));
    copyBtn.addEventListener("click", async () => {
      const ok = await copyText(api.visibleText());
      copyBtn.textContent = ok ? "Copied" : "Copy failed";
      setTimeout(() => { copyBtn.textContent = "Copy"; }, 1500);
    });
    dlBtn.addEventListener("click", () => {
      const a = document.createElement("a");
      a.href = URL.createObjectURL(new Blob([api.visibleText() + "\n"], { type: "text/plain" }));
      a.download = `openvp-slot${pad(slot)}-log-${stamp()}.txt`;
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    });
    list.addEventListener("click", async e => {
      const tog = e.target.closest(".ovl-tog");
      if (tog && !tog.classList.contains("ovl-nt")) {
        const pre = tog.closest(".ovl-line").querySelector(".ovl-detail");
        pre.hidden = !pre.hidden; tog.textContent = pre.hidden ? "▸" : "▾"; tog.setAttribute("aria-expanded", String(!pre.hidden));
        return;
      }
      const a = e.target.closest("a.ovl-link");
      if (!a) return;
      e.preventDefault();
      const k = a.dataset.k, v = a.dataset.v;
      if (k === "sig") openDebugger(slot, v);
      else if (k === "hostpath") { const ok = await copyText(v); a.title = ok ? "Copied" : "Couldn't copy"; }
      else if (o.onLink) o.onLink(k, v, slot);
    });
    paintChips(); paintPause(); empty();
    return api;
  }

  // ---------------------------------------------------------------- exceptions
  // A frame in a SIMPL+ source file links to that line in the source view (splus.html).
  function isSplusFile(name) { return /\.(usp|usl|inc|h)$/i.test(name || ""); }
  function splusLink(slot, f, cls) {
    const a = document.createElement("a"); a.className = cls; a.target = "_blank"; a.rel = "noopener";
    a.href = `splus.html?slot=${encodeURIComponent(slot)}&file=${encodeURIComponent(f.shortFile)}&line=${f.line}`;
    a.textContent = `${f.shortFile}:${f.line}`; a.title = "Show this line in the SIMPL+ source";
    return a;
  }
  function renderExceptions(box, items, emptyText, opts) {
    const slot = opts && opts.slot;
    box.replaceChildren();
    if (!items.length) { const d = document.createElement("div"); d.className = "ovl-empty"; d.textContent = emptyText || "No exceptions. When a SIMPL# program throws one that nothing catches, it is listed here with its stack trace."; box.appendChild(d); return; }
    for (const x of items) {
      const det = document.createElement("details"); det.className = "ovl-exc";
      const sum = document.createElement("summary");
      const t = document.createElement("span"); t.className = "ovl-t"; t.textContent = fmtTime(x.time);
      const ty = document.createElement("b"); ty.textContent = x.type.split(".").pop();
      ty.title = x.type;
      const top = x.frames.find(f => f.file);
      sum.append(t, " ", ty, ": ", document.createTextNode(x.message), " ");
      const src = document.createElement("span"); src.className = "ovl-src"; src.textContent = `in ${x.source}` + (top ? ` · ${top.shortFile}:${top.line}` : ""); sum.appendChild(src);
      const topSp = slot && x.frames.find(f => isSplusFile(f.shortFile));
      if (topSp) { sum.append(" "); sum.appendChild(splusLink(slot, topSp, "ovl-file")); }
      det.appendChild(sum);
      const body = document.createElement("div"); body.className = "ovl-frames";
      for (const f of x.frames) {
        const r = document.createElement("div"); r.className = "ovl-frame mono";
        const m = document.createElement("span"); m.textContent = f.method;
        r.appendChild(m);
        if (f.file) {
          const fl = slot && isSplusFile(f.shortFile) ? splusLink(slot, f, "ovl-file") : document.createElement("span");
          if (fl.tagName !== "A") { fl.className = "ovl-file"; fl.textContent = `${f.shortFile}:${f.line}`; }
          fl.title = f.file; r.append(" ", fl);
        }
        body.appendChild(r);
      }
      if (!x.hasSymbols) {
        const n = document.createElement("div"); n.className = "ovl-note";
        n.textContent = "No file and line numbers: the program's .pdb wasn't found next to it (or isn't a portable PDB). Put it beside the .dll or inside the .cpz and load the program again.";
        body.appendChild(n);
      }
      const pre = document.createElement("pre"); pre.className = "ovl-detail"; pre.textContent = x.stack;
      const full = document.createElement("details"); const fs = document.createElement("summary"); fs.textContent = "Full text"; full.append(fs, pre);
      body.appendChild(full);
      det.appendChild(body);
      box.appendChild(det);
    }
  }

  window.OpenVPLog = { create, renderExceptions, openDebugger, linkify, copyText };
})();
