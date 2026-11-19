/* debugger-wave.js - logic-analyzer style waveform panel for the OpenVP debugger.
 *
 * Plain script, no dependencies, no globals except window.WaveView.
 *
 *   var view = WaveView.create(container, {
 *     format(kind, value) => string,   // optional, kind: "Digital" | "Analog" | "Serial"
 *     onRemove(name),                  // optional, user clicked the x on a lane
 *     onReorder(names),                // optional, user dragged a lane to a new position
 *     maxChanges: 20000,               // optional, history cap per lane
 *     now: () => ms,                   // optional clock (default Date.now)
 *     span: 10000                      // optional initial visible span in ms
 *   });
 *   view.setSignals([{ name, kind, value }])
 *   view.push({ time, signal, kind, value })
 *   view.clear()
 *   view.setCursor(timeMs | null)
 *   view.destroy()
 *   // additions: view.goLive(), view.setSpan(ms), view.isLive(), view.getView() -> {start,end,span,live},
 *   //            view.setMarker(timeMs | null), view.resize()
 */
(function () {
  "use strict";
  if (window.WaveView) return;

  var LH = 22;        // lane height
  var AX = 24;        // time axis height
  var GUTTER = 150;   // lane-name gutter width
  var MIN_SPAN = 0.05;            // ms
  var MAX_SPAN = 6 * 3600 * 1000; // ms

  var CSS = [
    ".wv-root{--wv-bg:var(--bg,#0f1216);--wv-pane:var(--pane,#151a20);--wv-head:var(--head,#1b2129);--wv-line:var(--line,#29303a);",
    "--wv-text:var(--text,#e6e9ee);--wv-muted:var(--muted,#8b95a3);--wv-accent:var(--accent,#4c8dff);--wv-hi:var(--hi,#7fe0b1);",
    "--wv-lo:var(--lo,#6b7480);--wv-ana:var(--ana,#f2cf7a);--wv-ser:var(--ser,#9ecbff);--wv-cur:var(--brk,#e0a052);--wv-sel:var(--sel,#22344f);",
    "position:relative;display:flex;flex-direction:column;width:100%;height:100%;min-height:60px;overflow:hidden;box-sizing:border-box;",
    "background:var(--wv-pane);color:var(--wv-text);font:12px ui-monospace,Menlo,Consolas,monospace;user-select:none;-webkit-user-select:none}",
    ".wv-root *{box-sizing:border-box}",
    ".wv-bar{flex:none;display:flex;align-items:center;gap:6px;height:26px;padding:0 6px;background:var(--wv-head);border-bottom:1px solid var(--wv-line)}",
    ".wv-bar .wv-sp{flex:1}",
    ".wv-btn{font:inherit;color:var(--wv-text);background:var(--wv-pane);border:1px solid var(--wv-line);border-radius:3px;padding:1px 7px;height:20px;cursor:pointer}",
    ".wv-btn:hover{border-color:var(--wv-accent)}",
    ".wv-btn.wv-on{background:var(--wv-sel);border-color:var(--wv-accent);color:var(--wv-hi)}",
    ".wv-info{color:var(--wv-muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}",
    ".wv-scroll{position:relative;flex:1 1 auto;min-height:0;overflow-x:hidden;overflow-y:auto}",
    ".wv-inner{display:flex;align-items:stretch;width:100%}",
    ".wv-gutter{flex:none;width:" + GUTTER + "px;position:relative;background:var(--wv-head);border-right:1px solid var(--wv-line);z-index:2}",
    ".wv-gh{position:sticky;top:0;z-index:3;height:" + AX + "px;background:var(--wv-head);border-bottom:1px solid var(--wv-line);color:var(--wv-muted);padding:0 6px;line-height:" + (AX - 1) + "px}",
    ".wv-lanes{position:relative}",
    ".wv-lanes>.wv-lane{position:absolute;left:0;right:0}",
    ".wv-lane{position:relative;display:flex;align-items:center;gap:4px;height:" + LH + "px;padding:0 4px 0 7px;border-bottom:1px solid var(--wv-line);border-left:3px solid var(--wv-lo);cursor:grab}",
    ".wv-lane.wv-k-Digital{border-left-color:var(--wv-hi)}.wv-lane.wv-k-Analog{border-left-color:var(--wv-ana)}.wv-lane.wv-k-Serial{border-left-color:var(--wv-ser)}",
    ".wv-lane.wv-drag{opacity:.4}",
    ".wv-lane.wv-ovt::before,.wv-lane.wv-ovb::after{content:'';position:absolute;left:0;right:0;height:2px;background:var(--wv-accent);z-index:4}",
    ".wv-lane.wv-ovt::before{top:-1px}.wv-lane.wv-ovb::after{bottom:-1px}",
    ".wv-name{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}",
    ".wv-x{flex:none;width:14px;height:14px;line-height:12px;text-align:center;border-radius:3px;color:var(--wv-muted);cursor:pointer;visibility:hidden}",
    ".wv-lane:hover .wv-x{visibility:visible}.wv-x:hover{background:var(--wv-line);color:var(--wv-text)}",
    ".wv-plot{flex:1 1 auto;min-width:0;position:relative}",
    ".wv-stick{position:sticky;top:0;left:0;width:100%;overflow:hidden}",
    ".wv-cv{position:absolute;left:0;top:0;display:block;touch-action:none;cursor:crosshair}",
    ".wv-cv.wv-pan{cursor:grabbing}"
  ].join("");

  function injectCss() {
    if (document.getElementById("wv-style")) return;
    var s = document.createElement("style");
    s.id = "wv-style";
    s.textContent = CSS;
    (document.head || document.documentElement).appendChild(s);
  }

  // ------------------------------------------------------------ helpers

  function canonKind(k) {
    var s = String(k == null ? "" : k).toLowerCase();
    if (s.charAt(0) === "a") return "Analog";
    if (s.charAt(0) === "s") return "Serial";
    return "Digital";
  }
  function isHigh(v) { return v === true || v === 1 || v === "1" || v === "True" || v === "true"; }
  function normValue(kind, v) {
    if (kind === "Digital") return isHigh(v) ? 1 : 0;
    if (kind === "Analog") return Number(v) || 0;
    return v == null ? "" : String(v);
  }
  function escSerial(s) {
    return String(s).replace(/[\x00-\x1F\x7F-\xFF\\]/g, function (c) {
      return c === "\\" ? "\\\\" : "\\x" + c.charCodeAt(0).toString(16).toUpperCase().padStart(2, "0");
    });
  }
  function defFormat(kind, v) {
    if (kind === "Digital") return isHigh(v) ? "1" : "0";
    if (kind === "Analog") return String(Number(v) || 0);
    return escSerial(v == null ? "" : v);
  }
  function pad(n, w) { return String(n).padStart(w || 2, "0"); }

  // last index i with arr[i] <= t, or -1
  function idxAt(arr, t) {
    var lo = 0, hi = arr.length - 1, r = -1;
    while (lo <= hi) {
      var m = (lo + hi) >> 1;
      if (arr[m] <= t) { r = m; lo = m + 1; } else hi = m - 1;
    }
    return r;
  }

  function fmtClock(t, step) {
    var d = new Date(t);
    var s = pad(d.getHours()) + ":" + pad(d.getMinutes()) + ":" + pad(d.getSeconds());
    if (step >= 1000) return s;
    s += "." + pad(d.getMilliseconds(), 3);
    if (step < 1) {
      var us = Math.round((t - Math.floor(t)) * 1000);
      if (us > 999) us = 999;
      s += "·" + pad(us, 3);
    }
    return s;
  }
  function fmtDt(ms) {
    var sign = ms < 0 ? "-" : "+", a = Math.abs(ms);
    if (a < 1) return sign + (a * 1000).toFixed(a < 0.01 ? 1 : 0) + " µs";
    if (a < 1000) return sign + (a < 10 ? a.toFixed(2) : a < 100 ? a.toFixed(1) : a.toFixed(0)) + " ms";
    if (a < 60000) return sign + (a / 1000).toFixed(3) + " s";
    var m = Math.floor(a / 60000);
    return sign + m + "m " + ((a - m * 60000) / 1000).toFixed(1) + "s";
  }
  function fmtSpan(ms) {
    if (ms < 1) return (ms * 1000).toFixed(0) + " µs";
    if (ms < 1000) return (+ms.toPrecision(3)) + " ms";
    if (ms < 60000) return (+(ms / 1000).toPrecision(3)) + " s";
    if (ms < 3600000) return (+(ms / 60000).toPrecision(3)) + " min";
    return (+(ms / 3600000).toPrecision(3)) + " h";
  }
  function niceStep(raw) {
    var p = Math.pow(10, Math.floor(Math.log(raw) / Math.LN10));
    var m = raw / p;
    return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p;
  }

  // ------------------------------------------------------------ create

  function create(container, opts) {
    opts = opts || {};
    injectCss();
    var format = typeof opts.format === "function" ? opts.format : defFormat;
    var nowFn = typeof opts.now === "function" ? opts.now : Date.now;
    var cap = opts.maxChanges > 100 ? opts.maxChanges | 0 : 20000;

    var lanes = [];            // ordered lane objects
    var byName = Object.create(null);
    var userOrdered = false;
    var span = opts.span > 0 ? opts.span : 10000;
    var tEndFixed = 0;
    var following = true;
    var cursorT = null, cursorPaused = false;
    var markerT = null;
    var hoverX = -1, hoverY = -1;
    var destroyed = false;
    var dirty = true, rafId = 0, lastDraw = 0;
    var plotW = 0, plotH = 0, dpr = 1;
    var col = {};
    var charW = 7.2;

    // ---- DOM
    var root = document.createElement("div");
    root.className = "wv-root";
    var bar = document.createElement("div"); bar.className = "wv-bar";
    var presets = [["100 ms", 100], ["1 s", 1000], ["10 s", 10000], ["1 min", 60000]];
    var presetBtns = presets.map(function (p) {
      var b = document.createElement("button");
      b.type = "button"; b.className = "wv-btn"; b.textContent = p[0]; b.title = "Show " + p[0];
      b.addEventListener("click", function () { setSpan(p[1]); });
      bar.appendChild(b);
      return b;
    });
    var sp = document.createElement("span"); sp.className = "wv-sp"; bar.appendChild(sp);
    var info = document.createElement("span"); info.className = "wv-info"; bar.appendChild(info);
    var liveBtn = document.createElement("button");
    liveBtn.type = "button"; liveBtn.className = "wv-btn"; liveBtn.textContent = "⟳ live";
    liveBtn.title = "Follow the live edge";
    liveBtn.addEventListener("click", goLive);
    bar.appendChild(liveBtn);
    root.appendChild(bar);

    var scroller = document.createElement("div"); scroller.className = "wv-scroll";
    var inner = document.createElement("div"); inner.className = "wv-inner";
    var gutter = document.createElement("div"); gutter.className = "wv-gutter";
    var gh = document.createElement("div"); gh.className = "wv-gh"; gh.textContent = "signals";
    gutter.appendChild(gh);
    var lanesBox = document.createElement("div"); lanesBox.className = "wv-lanes";   // lane labels are created only for the lanes in view
    gutter.appendChild(lanesBox);
    var plot = document.createElement("div"); plot.className = "wv-plot";
    var stick = document.createElement("div"); stick.className = "wv-stick";
    var cv = document.createElement("canvas"); cv.className = "wv-cv";
    stick.appendChild(cv); plot.appendChild(stick);
    inner.appendChild(gutter); inner.appendChild(plot);
    scroller.appendChild(inner); root.appendChild(scroller);
    container.appendChild(root);
    var ctx = cv.getContext("2d");

    // ---- time model
    function nowT() { return nowFn(); }
    function viewEnd() { return following ? nowT() : tEndFixed; }
    function minT() {
      var m = Infinity;
      for (var i = 0; i < lanes.length; i++) if (lanes[i].t.length) m = Math.min(m, lanes[i].t[0]);
      return m;
    }
    function clampEnd(e) {
      var hi = nowT() + span * 0.05;
      var m = minT();
      var lo = isFinite(m) ? m : hi - span;
      if (e > hi) e = hi;
      if (e < lo) e = lo;
      return e;
    }
    function pauseFollow() {
      if (following) { tEndFixed = nowT(); following = false; cursorPaused = false; }
      updateBar();
    }
    function goLive() {
      following = true; cursorPaused = false;
      updateBar(); schedule();
    }
    function setSpan(ms) {
      ms = Math.max(MIN_SPAN, Math.min(MAX_SPAN, ms));
      var e = viewEnd();
      span = ms;
      if (!following) tEndFixed = e;
      updateBar(); schedule();
    }
    function zoomAt(factor, frac, t) {
      var ns = Math.max(MIN_SPAN, Math.min(MAX_SPAN, span * factor));
      pauseFollow();
      span = ns;
      tEndFixed = clampEnd(t + (1 - frac) * ns);
      updateBar(); schedule();
    }
    function panBy(dms) {
      pauseFollow();
      tEndFixed = clampEnd(tEndFixed + dms);
      schedule();
    }
    function tAtX(x) { var e = viewEnd(); return e - span + (x / plotW) * span; }

    function updateBar() {
      liveBtn.classList.toggle("wv-on", following);
      for (var i = 0; i < presets.length; i++) {
        presetBtns[i].classList.toggle("wv-on", Math.abs(span - presets[i][1]) < 1e-6);
      }
    }

    // ---- lanes
    function makeLane(sig) {
      var kind = canonKind(sig.kind);
      return { name: String(sig.name), kind: kind, t: [], v: [], init: sig.value === undefined ? undefined : normValue(kind, sig.value), el: null };
    }

    var gwin = { a: -1, b: -1 };
    function buildGutter() {
      lanesBox.style.height = lanes.length * LH + "px";
      gwin.a = gwin.b = -1;      // lanes may have changed: rebuild the visible labels
      renderGutter();
    }
    // Only the labels in (and just around) the viewport exist, so thousands of lanes cost the same as a few.
    function renderGutter() {
      if (dragName != null) return;
      var st = scroller.scrollTop, h = scroller.clientHeight || 300;
      var a = Math.max(0, Math.floor(st / LH) - 2), b = Math.min(lanes.length, Math.ceil((st + h) / LH) + 2);
      if (a === gwin.a && b === gwin.b) return;
      gwin.a = a; gwin.b = b;
      while (lanesBox.firstChild) lanesBox.removeChild(lanesBox.firstChild);
      var frag = document.createDocumentFragment();
      for (var i = a; i < b; i++) frag.appendChild(makeLaneEl(lanes[i], i));
      lanesBox.appendChild(frag);
    }
    function makeLaneEl(l, i) {
        var el = document.createElement("div");
        el.className = "wv-lane wv-k-" + l.kind;
        el.style.top = i * LH + "px";
        el.draggable = true;
        el.dataset.i = i;
        var nm = document.createElement("span"); nm.className = "wv-name"; nm.textContent = l.name; nm.title = l.name + " (" + l.kind + ")";
        var x = document.createElement("span"); x.className = "wv-x"; x.textContent = "×"; x.title = "Remove lane";
        x.addEventListener("click", function (e) { e.stopPropagation(); removeLane(l.name); });
        x.addEventListener("mousedown", function (e) { e.stopPropagation(); });
        el.appendChild(nm); el.appendChild(x);
        el.addEventListener("dragstart", function (e) {
          el.classList.add("wv-drag");
          try { e.dataTransfer.setData("text/plain", l.name); e.dataTransfer.effectAllowed = "move"; } catch (_) { }
          dragName = l.name;
        });
        el.addEventListener("dragend", function () { dragName = null; clearOver(); el.classList.remove("wv-drag"); });
        el.addEventListener("dragover", function (e) {
          if (dragName == null) return;
          e.preventDefault();
          var r = el.getBoundingClientRect();
          var after = e.clientY > r.top + r.height / 2;
          clearOver();
          el.classList.add(after ? "wv-ovb" : "wv-ovt");
        });
        el.addEventListener("dragleave", function () { el.classList.remove("wv-ovt", "wv-ovb"); });
        el.addEventListener("drop", function (e) {
          if (dragName == null) return;
          e.preventDefault();
          var r = el.getBoundingClientRect();
          var after = e.clientY > r.top + r.height / 2;
          var from = lanes.findIndex(function (q) { return q.name === dragName; });
          var to = i + (after ? 1 : 0);
          clearOver();
          if (from < 0) return;
          var item = lanes.splice(from, 1)[0];
          if (from < to) to--;
          lanes.splice(to, 0, item);
          userOrdered = true;
          dragName = null;
          buildGutter(); layout(); schedule();
          if (opts.onReorder) { try { opts.onReorder(lanes.map(function (q) { return q.name; })); } catch (_) { } }
        });
        l.el = el;
        return el;
    }
    var dragName = null;
    function clearOver() {
      var o = lanesBox.querySelectorAll(".wv-ovt,.wv-ovb");
      for (var i = 0; i < o.length; i++) o[i].classList.remove("wv-ovt", "wv-ovb");
    }

    function removeLane(name) {
      var i = lanes.findIndex(function (l) { return l.name === name; });
      if (i < 0) return;
      lanes.splice(i, 1);
      delete byName[name];
      buildGutter(); layout(); schedule();
      if (opts.onRemove) { try { opts.onRemove(name); } catch (_) { } }
    }

    function setSignals(list) {
      list = list || [];
      var old = byName, next = Object.create(null), arr = [];
      list.forEach(function (s) {
        if (s == null || s.name == null) return;
        var name = String(s.name);
        if (next[name]) return;
        var kind = canonKind(s.kind);
        var l = old[name];
        if (l && l.kind === kind) {
          if (l.init === undefined && s.value !== undefined && !l.t.length) l.init = normValue(kind, s.value);
        } else l = makeLane(s);
        next[name] = l; arr.push(l);
      });
      if (userOrdered) {
        var kept = lanes.filter(function (l) { return next[l.name] === l; });
        var seen = Object.create(null);
        kept.forEach(function (l) { seen[l.name] = 1; });
        arr.forEach(function (l) { if (!seen[l.name]) kept.push(l); });
        arr = kept;
      }
      lanes = arr; byName = next;
      buildGutter(); layout(); schedule();
    }

    function push(ev) {
      if (!ev) return;
      var l = byName[ev.signal];
      if (!l) return;
      var t = Number(ev.time);
      if (!isFinite(t)) return;
      var v = normValue(l.kind, ev.value);
      var T = l.t, V = l.v, n = T.length;
      if (n === 0 || t >= T[n - 1]) {
        var prev = n ? V[n - 1] : l.init;
        if (l.kind !== "Serial" && prev === v && n) return;
        T.push(t); V.push(v);
      } else {
        var i = idxAt(T, t) + 1;
        T.splice(i, 0, t); V.splice(i, 0, v);
      }
      if (T.length > cap + Math.max(256, cap >> 4)) {
        var drop = T.length - cap;
        l.init = V[drop - 1];
        T.splice(0, drop); V.splice(0, drop);
      }
      schedule();
    }

    function clear() {
      lanes.forEach(function (l) { l.t = []; l.v = []; });
      markerT = null; cursorT = null; cursorPaused = false;
      schedule();
    }

    function setCursor(t) {
      if (t == null || !isFinite(Number(t))) {
        cursorT = null;
        if (cursorPaused) { following = true; cursorPaused = false; updateBar(); }
        schedule();
        return;
      }
      cursorT = Number(t);
      var wasFollowing = following;
      if (following) { tEndFixed = nowT(); following = false; cursorPaused = true; }
      var e = tEndFixed, s0 = e - span, m = span * 0.05;
      if (cursorT < s0 + m || cursorT > e - m) tEndFixed = clampEnd(cursorT + span / 2);
      void wasFollowing;
      updateBar(); schedule();
    }

    function setMarker(t) { markerT = t == null ? null : Number(t); schedule(); }

    // ---- layout / canvas sizing
    function readColors() {
      var cs = getComputedStyle(root);
      function g(n, d) { var v = cs.getPropertyValue(n).trim(); return v || d; }
      col.bg = g("--wv-bg", "#0f1216"); col.pane = g("--wv-pane", "#151a20"); col.head = g("--wv-head", "#1b2129");
      col.line = g("--wv-line", "#29303a"); col.text = g("--wv-text", "#e6e9ee"); col.muted = g("--wv-muted", "#8b95a3");
      col.accent = g("--wv-accent", "#4c8dff"); col.hi = g("--wv-hi", "#7fe0b1"); col.lo = g("--wv-lo", "#6b7480");
      col.ana = g("--wv-ana", "#f2cf7a"); col.ser = g("--wv-ser", "#9ecbff"); col.cur = g("--wv-cur", "#e0a052");
      col.sel = g("--wv-sel", "#22344f");
    }

    function layout() {
      if (destroyed) return;
      var h = scroller.clientHeight;
      var w = plot.clientWidth;
      if (!h || !w) { plotW = 0; return; }
      var contentH = AX + lanes.length * LH;
      inner.style.minHeight = h + "px";
      stick.style.height = h + "px";
      gutter.style.minHeight = Math.max(h, contentH) + "px";
      // plot must be at least as tall as the scroller; gutter drives scroll height
      dpr = window.devicePixelRatio || 1;
      plotW = w; plotH = h;
      cv.style.width = w + "px"; cv.style.height = h + "px";
      var pw = Math.round(w * dpr), ph = Math.round(h * dpr);
      if (cv.width !== pw) cv.width = pw;
      if (cv.height !== ph) cv.height = ph;
      readColors();
      renderGutter();
      schedule();
    }

    // ---- drawing
    function schedule() {
      dirty = true;
      if (!rafId && !destroyed) rafId = requestAnimationFrame(frame);
    }
    function frame(ts) {
      rafId = 0;
      if (destroyed) return;
      if (dirty || (following && ts - lastDraw >= 33)) {
        lastDraw = ts;
        draw();
      }
      if (following && plotW) rafId = requestAnimationFrame(frame);   // a hidden panel (no size) stops animating; resize restarts it
    }

    function valueAt(l, t) {
      var i = idxAt(l.t, t);
      if (i >= 0) return { v: l.v[i], t0: l.t[i] };
      return l.init === undefined ? null : { v: l.init, t0: null };
    }

    function draw() {
      dirty = false;
      if (!plotW || !plotH) { layout(); if (!plotW) return; }
      var W = plotW, H = plotH;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.fillStyle = col.pane; ctx.fillRect(0, 0, W, H);
      ctx.font = "12px ui-monospace, Menlo, Consolas, monospace";
      charW = ctx.measureText("0").width || 7.2;
      ctx.textBaseline = "middle";

      var tEnd = viewEnd(), tStart = tEnd - span, ppm = W / span;
      var sy = scroller.scrollTop;
      function X(t) { return (t - tStart) * ppm; }

      info.textContent = "span " + fmtSpan(span) + (following ? " · live" : " · paused");

      // lane stripes + separators
      ctx.save();
      ctx.beginPath(); ctx.rect(0, AX, W, H - AX); ctx.clip();
      var i0 = Math.max(0, Math.floor(sy / LH) - 1), i1 = Math.min(lanes.length, Math.ceil((sy + H) / LH) + 1);   // lanes in view
      for (var i = i0; i < i1; i++) {
        var y0 = AX + i * LH - sy;
        if (y0 > H || y0 + LH < AX) continue;
        if (i & 1) { ctx.fillStyle = "rgba(255,255,255,0.025)"; ctx.fillRect(0, y0, W, LH); }
        ctx.fillStyle = col.line; ctx.fillRect(0, y0 + LH - 1, W, 1);
      }

      // grid + ticks
      var rawStep = span * 120 / W;
      var step = Math.max(0.001, niceStep(rawStep));
      var n0 = Math.ceil(tStart / step), ticks = [];
      for (var n = n0, tk; (tk = n * step) <= tEnd && ticks.length < 200; n++) ticks.push(tk);
      ctx.fillStyle = "rgba(255,255,255,0.05)";
      for (i = 0; i < ticks.length; i++) ctx.fillRect(Math.round(X(ticks[i])), AX, 1, H - AX);

      // lanes
      for (i = i0; i < i1; i++) {
        var ly = AX + i * LH - sy;
        if (ly > H || ly + LH < AX) continue;
        var l = lanes[i];
        if (l.kind === "Digital") drawDigital(l, ly, tStart, tEnd, ppm, W);
        else if (l.kind === "Analog") drawAnalog(l, ly, tStart, tEnd, ppm, W);
        else drawSerial(l, ly, tStart, tEnd, ppm, W);
      }
      if (!lanes.length) {
        ctx.fillStyle = col.muted; ctx.textAlign = "center";
        ctx.fillText("no signals – add one from the signal list", W / 2, AX + 30);
        ctx.textAlign = "left";
      }

      // marker / cursor / hover lines
      if (markerT != null) {
        var mx = X(markerT);
        if (mx >= -1 && mx <= W + 1) vline(mx, col.accent, true);
      }
      if (cursorT != null) {
        var cx = X(cursorT);
        if (cx >= -1 && cx <= W + 1) {
          ctx.fillStyle = col.cur; ctx.globalAlpha = 0.9;
          ctx.fillRect(Math.round(cx) - 1, AX, 2, H - AX);
          ctx.globalAlpha = 1;
        }
      }
      var hovering = hoverX >= 0 && hoverX <= W;
      if (hovering) {
        var ht = tAtX(hoverX);
        vline(hoverX, "rgba(230,233,238,0.55)", false);
        // per-lane values
        for (i = i0; i < i1; i++) {
          var hy = AX + i * LH - sy;
          if (hy > H || hy + LH < AX) continue;
          var r = valueAt(lanes[i], ht);
          if (!r) continue;
          var txt = format(lanes[i].kind, r.v);
          if (txt.length > 40) txt = txt.slice(0, 39) + "…";
          var tw = txt.length * charW + 8;
          var px = hoverX + 6;
          if (px + tw > W - 2) px = hoverX - 6 - tw;
          var c = lanes[i].kind === "Digital" ? (r.v ? col.hi : col.lo) : lanes[i].kind === "Analog" ? col.ana : col.ser;
          ctx.fillStyle = "rgba(15,18,22,0.92)"; ctx.fillRect(px, hy + 3, tw, LH - 7);
          ctx.strokeStyle = c; ctx.lineWidth = 1; ctx.strokeRect(px + 0.5, hy + 3.5, tw - 1, LH - 8);
          ctx.fillStyle = c; ctx.fillText(txt, px + 4, hy + LH / 2);
        }
      }
      ctx.restore();

      // axis band (drawn last, above lanes)
      ctx.fillStyle = col.head; ctx.fillRect(0, 0, W, AX);
      ctx.fillStyle = col.line; ctx.fillRect(0, AX - 1, W, 1);
      ctx.fillStyle = col.muted; ctx.textAlign = "left";
      var lastEnd = -1e9;
      for (i = 0; i < ticks.length; i++) {
        var x = Math.round(X(ticks[i]));
        ctx.fillStyle = col.line; ctx.fillRect(x, AX - 6, 1, 5);
        var lab = fmtClock(ticks[i], step);
        var lw = lab.length * charW;
        if (x + 3 > lastEnd + 8 && x + 3 + lw < W) {
          ctx.fillStyle = col.muted; ctx.fillText(lab, x + 3, AX / 2 - 1);
          lastEnd = x + 3 + lw;
        }
      }
      if (markerT != null) {
        var mx2 = X(markerT);
        if (mx2 >= 0 && mx2 <= W) { ctx.fillStyle = col.accent; tri(mx2); }
      }
      if (cursorT != null) {
        var cx2 = X(cursorT);
        if (cx2 >= 0 && cx2 <= W) { ctx.fillStyle = col.cur; tri(cx2); }
      }
      if (hovering) {
        var ht2 = tAtX(hoverX);
        var lab2 = fmtClock(ht2, Math.min(step, 1));
        if (step >= 1) lab2 = fmtClock(ht2, 0.5).replace(/·\d+$/, "");
        if (markerT != null) lab2 += "  Δ " + fmtDt(ht2 - markerT);
        var bw = lab2.length * charW + 10;
        var bx = Math.max(2, Math.min(W - bw - 2, hoverX - bw / 2));
        ctx.fillStyle = "#0f1216"; ctx.fillRect(bx, 2, bw, AX - 5);
        ctx.strokeStyle = col.muted; ctx.lineWidth = 1; ctx.strokeRect(bx + 0.5, 2.5, bw - 1, AX - 6);
        ctx.fillStyle = col.text; ctx.fillText(lab2, bx + 5, AX / 2 - 1);
      }
    }

    function tri(x) {
      ctx.beginPath(); ctx.moveTo(x - 4, AX - 1); ctx.lineTo(x + 4, AX - 1); ctx.lineTo(x, AX - 7); ctx.closePath(); ctx.fill();
    }
    function vline(x, color, dashed) {
      ctx.save();
      ctx.strokeStyle = color; ctx.lineWidth = 1;
      if (dashed) ctx.setLineDash([4, 3]);
      var px = Math.round(x) + 0.5;
      ctx.beginPath(); ctx.moveTo(px, AX); ctx.lineTo(px, plotH); ctx.stroke();
      ctx.restore();
    }

    function drawDigital(l, y0, tStart, tEnd, ppm, W) {
      var T = l.t, V = l.v, n = T.length;
      var yH = y0 + 5, yL = y0 + LH - 6;
      var i = idxAt(T, tStart);
      var cur = i >= 0 ? V[i] : l.init;
      var hiP = new Path2D(), loP = new Path2D(), vP = new Path2D(), fill = new Path2D();
      var xs = 0, known = cur !== undefined, j = i + 1;
      function seg(x0, x1, lv) {
        if (x1 <= x0) return;
        var y = lv ? yH : yL;
        (lv ? hiP : loP).moveTo(x0, y + 0.5); (lv ? hiP : loP).lineTo(x1, y + 0.5);
        if (lv) fill.rect(x0, yH, x1 - x0, yL - yH);
      }
      for (; j < n && T[j] <= tEnd; j++) {
        var x = (T[j] - tStart) * ppm;
        if (known) { seg(xs, x, cur); vP.moveTo(x + 0.5, yH); vP.lineTo(x + 0.5, yL); }
        xs = x < 0 ? 0 : x; cur = V[j]; known = true;
      }
      if (known) seg(xs, W, cur);
      else {
        ctx.save(); ctx.strokeStyle = col.line; ctx.setLineDash([2, 4]);
        ctx.beginPath(); ctx.moveTo(0, y0 + LH / 2); ctx.lineTo(W, y0 + LH / 2); ctx.stroke(); ctx.restore();
      }
      ctx.globalAlpha = 0.10; ctx.fillStyle = col.hi; ctx.fill(fill); ctx.globalAlpha = 1;
      ctx.lineWidth = 1.5;
      ctx.strokeStyle = col.lo; ctx.stroke(loP);
      ctx.strokeStyle = col.hi; ctx.stroke(hiP);
      ctx.lineWidth = 1; ctx.strokeStyle = col.hi; ctx.globalAlpha = 0.85; ctx.stroke(vP); ctx.globalAlpha = 1;
    }

    function drawAnalog(l, y0, tStart, tEnd, ppm, W) {
      var T = l.t, V = l.v, n = T.length;
      var top = y0 + 4, bot = y0 + LH - 5;
      function yOf(v) { v = v < 0 ? 0 : v > 65535 ? 65535 : v; return Math.round(bot - (v / 65535) * (bot - top)) + 0.5; }
      var i = idxAt(T, tStart);
      var cur = i >= 0 ? V[i] : l.init;
      var p = new Path2D(), f = new Path2D();
      var known = cur !== undefined, y = known ? yOf(cur) : 0, j = i + 1, xs = 0;
      if (known) { p.moveTo(0, y); f.moveTo(0, bot + 0.5); f.lineTo(0, y); }
      for (; j < n && T[j] <= tEnd; j++) {
        var x = (T[j] - tStart) * ppm, ny = yOf(V[j]);
        if (!known) { p.moveTo(x, ny); f.moveTo(x, bot + 0.5); f.lineTo(x, ny); known = true; }
        else { p.lineTo(x, y); p.lineTo(x, ny); f.lineTo(x, y); f.lineTo(x, ny); }
        y = ny; xs = x;
      }
      if (!known) return;
      p.lineTo(W, y); f.lineTo(W, y); f.lineTo(W, bot + 0.5); f.closePath();
      ctx.globalAlpha = 0.09; ctx.fillStyle = col.ana; ctx.fill(f); ctx.globalAlpha = 1;
      ctx.lineWidth = 1.5; ctx.strokeStyle = col.ana; ctx.stroke(p);
      void xs;
      // value labels where there is room
      ctx.fillStyle = col.ana; ctx.textAlign = "left";
      var k = i < 0 ? 0 : i; // segment index whose value starts the view
      var sx = i >= 0 ? 0 : null;
      for (; k < n; k++) {
        var xk = i >= 0 && k === i ? 0 : (T[k] - tStart) * ppm;
        if (xk > W) break;
        if (k > i && T[k] > tEnd) break;
        var xe = k + 1 < n ? (T[k + 1] - tStart) * ppm : W;
        var vv = V[k];
        var txt = format("Analog", vv);
        var w = txt.length * charW;
        var sxx = Math.max(xk, 0) + 4;
        if (Math.min(xe, W) - sxx >= w + 4) {
          var yy = yOf(vv);
          var ty = yy > y0 + LH / 2 ? y0 + 8 : y0 + LH - 7;
          ctx.fillText(txt, sxx, ty);
        }
      }
      if (i < 0 && l.init !== undefined) {
        var xe0 = n && T[0] <= tEnd ? (T[0] - tStart) * ppm : W;
        var t0 = format("Analog", l.init), w0 = t0.length * charW;
        if (xe0 - 4 >= w0 + 4) ctx.fillText(t0, 4, yOf(l.init) > y0 + LH / 2 ? y0 + 8 : y0 + LH - 7);
      }
      void sx;
    }

    function drawSerial(l, y0, tStart, tEnd, ppm, W) {
      var T = l.t, V = l.v, n = T.length;
      var top = y0 + 4, bot = y0 + LH - 5, mid = (top + bot) / 2;
      var i = idxAt(T, tStart);
      var endNow = Math.max(nowT(), n ? T[n - 1] : 0);
      ctx.lineWidth = 1;
      // idle baseline
      ctx.strokeStyle = col.line; ctx.beginPath(); ctx.moveTo(0, Math.round(mid) + 0.5); ctx.lineTo(W, Math.round(mid) + 0.5); ctx.stroke();
      var lastTick = -10;
      for (var k = Math.max(i, 0); k < n && T[k] <= tEnd; k++) {
        var v = V[k];
        if (v === "") continue;
        var x0 = (T[k] - tStart) * ppm;
        var x1 = ((k + 1 < n ? T[k + 1] : endNow) - tStart) * ppm;
        if (x1 < 0) continue;
        if (x1 - x0 < 3) {
          var tx = Math.round(x0);
          if (tx !== lastTick) {
            ctx.strokeStyle = col.ser; ctx.beginPath(); ctx.moveTo(tx + 0.5, top); ctx.lineTo(tx + 0.5, bot); ctx.stroke();
            lastTick = tx;
          }
          continue;
        }
        var c = Math.min(4, (x1 - x0) / 3);
        ctx.beginPath();
        ctx.moveTo(x0, mid); ctx.lineTo(x0 + c, top); ctx.lineTo(x1 - c, top); ctx.lineTo(x1, mid);
        ctx.lineTo(x1 - c, bot); ctx.lineTo(x0 + c, bot); ctx.closePath();
        ctx.globalAlpha = 0.16; ctx.fillStyle = col.ser; ctx.fill(); ctx.globalAlpha = 1;
        ctx.strokeStyle = col.ser; ctx.stroke();
        var vis0 = Math.max(x0 + c + 2, 3), vis1 = Math.min(x1 - c - 2, W);
        if (vis1 - vis0 > charW * 2) {
          var txt = format("Serial", v);
          ctx.save();
          ctx.beginPath(); ctx.rect(vis0, top, vis1 - vis0, bot - top); ctx.clip();
          ctx.fillStyle = col.ser; ctx.textAlign = "left";
          ctx.fillText(txt, vis0, mid + 0.5);
          ctx.restore();
        }
      }
    }

    // ---- interaction
    var ptrs = new Map();
    var drag = null, pinch = null;

    function localX(e) { var r = cv.getBoundingClientRect(); return e.clientX - r.left; }
    function localY(e) { var r = cv.getBoundingClientRect(); return e.clientY - r.top; }

    cv.addEventListener("pointerdown", function (e) {
      if (e.pointerType === "mouse" && e.button !== 0) return;
      try { cv.setPointerCapture(e.pointerId); } catch (_) { }
      ptrs.set(e.pointerId, { x: localX(e), y: localY(e) });
      if (ptrs.size === 1) {
        drag = { id: e.pointerId, x0: localX(e), end0: viewEnd(), moved: false };
      } else if (ptrs.size === 2) {
        drag = null;
        var pts = Array.from(ptrs.values());
        var d = Math.abs(pts[0].x - pts[1].x) || 1;
        var cx = (pts[0].x + pts[1].x) / 2;
        pinch = { d0: d, span0: span, t: tAtX(cx), frac: cx / plotW };
      }
    });
    cv.addEventListener("pointermove", function (e) {
      var x = localX(e), y = localY(e);
      if (ptrs.has(e.pointerId)) ptrs.set(e.pointerId, { x: x, y: y });
      if (pinch && ptrs.size >= 2) {
        var pts = Array.from(ptrs.values());
        var d = Math.max(8, Math.abs(pts[0].x - pts[1].x));
        var cx = (pts[0].x + pts[1].x) / 2;
        pauseFollow();
        span = Math.max(MIN_SPAN, Math.min(MAX_SPAN, pinch.span0 * pinch.d0 / d));
        tEndFixed = clampEnd(pinch.t + (1 - cx / plotW) * span);
        hoverX = -1; updateBar(); schedule();
        return;
      }
      if (drag && drag.id === e.pointerId) {
        var dx = x - drag.x0;
        if (!drag.moved && Math.abs(dx) > 3) { drag.moved = true; cv.classList.add("wv-pan"); }
        if (drag.moved) {
          pauseFollow();
          tEndFixed = clampEnd(drag.end0 - dx / (plotW / span));
        }
      }
      if (e.pointerType === "mouse" || e.pointerType === "pen") { hoverX = x; hoverY = y; }
      schedule();
    });
    function endPtr(e, cancel) {
      var was = ptrs.has(e.pointerId);
      ptrs.delete(e.pointerId);
      if (ptrs.size < 2) pinch = null;
      if (drag && drag.id === e.pointerId) {
        if (!drag.moved && !cancel && was) setMarker(tAtX(localX(e)));
        drag = null; cv.classList.remove("wv-pan");
      }
      if (e.pointerType === "touch") hoverX = -1;
      schedule();
    }
    cv.addEventListener("pointerup", function (e) { endPtr(e, false); });
    cv.addEventListener("pointercancel", function (e) { endPtr(e, true); });
    cv.addEventListener("pointerleave", function (e) { if (e.pointerType !== "touch") { hoverX = -1; schedule(); } });
    cv.addEventListener("dblclick", function () { setMarker(null); });
    cv.addEventListener("wheel", function (e) {
      e.preventDefault();
      var k = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? 400 : 1;
      var dx = e.deltaX * k, dy = e.deltaY * k;
      if (e.altKey) { scroller.scrollTop += dy; return; }
      if (e.ctrlKey || e.shiftKey || (Math.abs(dx) > Math.abs(dy))) {
        var d = Math.abs(dx) > Math.abs(dy) ? dx : dy;
        panBy(d * (span / plotW));
        return;
      }
      var x = localX(e);
      zoomAt(Math.exp(dy * 0.0015), x / plotW, tAtX(x));
    }, { passive: false });
    scroller.addEventListener("scroll", function () { renderGutter(); schedule(); });

    // ---- resize
    var ro = null;
    if (typeof ResizeObserver !== "undefined") {
      ro = new ResizeObserver(function () { layout(); });
      ro.observe(root); ro.observe(scroller);
    } else window.addEventListener("resize", layout);

    updateBar();
    layout();
    schedule();

    var view = {
      setSignals: setSignals,
      push: push,
      clear: clear,
      setCursor: setCursor,
      destroy: function () {
        destroyed = true;
        if (rafId) cancelAnimationFrame(rafId);
        if (ro) ro.disconnect(); else window.removeEventListener("resize", layout);
        if (root.parentNode) root.parentNode.removeChild(root);
      },
      goLive: goLive,
      setSpan: setSpan,
      setMarker: setMarker,
      isLive: function () { return following; },
      getView: function () { var e = viewEnd(); return { start: e - span, end: e, span: span, live: following }; },
      resize: layout
    };
    return view;
  }

  window.WaveView = { create: create };
})();
