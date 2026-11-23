/*
 * openvp-crcomlib.js — a stand-in for the parts of CrComLib that CH5 / HTML panels use, talking to OpenVP over WebSocket.
 * Written for OpenVP from CrComLib's public behaviour; it contains no Crestron code.
 *
 * Load it in a CH5 / HTML panel project instead of (or before) Crestron's CrComLib:
 *   <script src="http://<openvp-host>:8080/openvp-crcomlib.js"></script>
 * and open the page with ?ipid=03 (and optionally &slot=1&room=ROOM2&host=10.0.0.5:8080).
 *
 * Signals: publishEvent / subscribeState / unsubscribeState / getState with types "boolean" ("b"), "number" ("n"), "string" ("s")
 * (also "d", "a", "digital", "analog", "serial"). A signal name is any of
 *   - a join number:                "12"
 *   - a typed join:                 "d12", "a3", "s7", "digital_12", "analog:3"
 *   - a smart-graphics object join: "so4.d1" (object 4, digital join 1; the form OpenVP's IP table shows)
 *   - a name from a contract:       "Room1.Light.Press", "Rooms[2].Volume.Set"  (see registerContract)
 *   - a name you mapped yourself:   mapSignals({ "Csig.Room_On": "d5" })
 * A contract or map can be given as window.OPENVP_CONTRACT, OPENVP_CONFIG.contract, or a file next to the page named
 * openvp-contract.json (OpenVP's own format, below) or contract.cse2j (a Contract Editor export, read as far as its layout is known).
 *
 * Components: when the page uses <ch5-button>, <ch5-toggle>, <ch5-slider>, <ch5-list>, <ch5-label>/<ch5-text>, <ch5-textinput>,
 * <ch5-select> or <ch5-image> and nothing else has defined them, OpenVP defines small versions that read the usual
 * attributes (sendeventonclick, receivestateselected, receivestatelabel, receivestatevalue, receivestatesize, receivestateurl, ...).
 * See docs/unsupported.md for what is and isn't covered.
 *
 * OpenVP contract format (openvp-contract.json):
 *   { "name": "Room",
 *     "members":   { "Light.Press": {"type":"b","join":1}, "Light.Fb": {"type":"b","join":1}, "Volume.Set": {"type":"n","join":1}, "Title": {"type":"s","join":1} },
 *     "instances": { "Room1": {"b":0,"n":0,"s":0}, "Room2": {"b":10,"n":5,"s":3} },   // optional: join offset per type
 *     "span":      { "b": 10, "n": 5, "s": 3 } }                                       // optional: joins per instance for Name[i]
 *   "Room1.Light.Press" is digital join 1; "Room2.Light.Press" is digital join 11; "Room[1].Light.Press" is digital join 1 + 1*span.
 *
 * Shared panels (/s/{token}/...): OpenVP sets window.OPENVP_CONFIG = { wsPath, share: true, statusUrl } before
 * this script. The page then always connects to wsPath (the slot and IP ID come from the share link, never from
 * the address bar), and window.CrComLib is pinned to this shim, so a project that bundles its own CrComLib
 * still publishes and subscribes through OpenVP. When the share link is revoked or expires the page reloads
 * and shows the "expired" message.
 *
 * Page options (address bar or OPENVP_CONFIG): kiosk=1 (full screen, no chrome, screen kept awake, hidden exit: tap the top-left
 * corner 5 times; add kioskpin=1234 to ask for a PIN), latency=1 (small round-trip indicator).
 */
(function (global) {
  "use strict";
  var doc = global.document;
  var params = new URLSearchParams((global.location && global.location.search) || "");
  var cfg = global.OPENVP_CONFIG || {};
  var ipid = (cfg.ipid || params.get("ipid") || "03").toString();
  var slot = cfg.slot || params.get("slot");
  var room = cfg.room || params.get("room");
  var host = cfg.host || params.get("host") || (global.location && global.location.host);
  var proto = global.location && global.location.protocol === "https:" ? "wss://" : "ws://";
  var shared = !!cfg.share;
  var url = shared && cfg.wsPath
    ? proto + global.location.host + cfg.wsPath
    : proto + host + "/ws/panel?ipid=" + encodeURIComponent(ipid) + (slot ? "&slot=" + encodeURIComponent(slot) : "") + (room ? "&room=" + encodeURIComponent(room) : "");

  var KINDS = { b: "b", d: "b", boolean: "b", digital: "b", n: "n", a: "n", number: "n", analog: "n", s: "s", string: "s", serial: "s", o: "o", object: "o" };
  function norm(type) { return KINDS[String(type).toLowerCase()] || type; }

  var state = { b: {}, n: {}, s: {} };       // last value per key; key = join, or "so<id>.<join>" for a smart object
  var subs = { b: {}, n: {}, s: {} };        // key -> { id: callback }
  var pending = [];                          // subscriptions whose signal name doesn't resolve (yet): { id, type, name, cb }
  var nextId = 1;
  var ws = null;
  var queue = [];
  var listeners = [];
  var pingSent = 0, lastRtt = null, pingTimer = null, watchdog = null, lastSeen = 0;
  var retry = 0, reconnectTimer = null;
  var warned = {};

  // ------------------------------------------------------------------ names, joins and contracts

  var directMap = {};        // "Csig.Room_On" -> { t, j, so }
  var contractNames = {};    // "Room1.Light.Press" -> { t, j, so }
  var contracts = [];        // kept for Name[i] lookups

  function keyOf(j, so) { return so ? "so" + so + "." + j : String(j); }

  function parseJoinName(type, name) {
    var s = String(name == null ? "" : name).trim();
    var m;
    if ((m = /^(\d{1,5})$/.exec(s))) return { t: type, j: +m[1], so: 0 };
    if ((m = /^so(\d+)\.([a-z]+)[_:\-]?(\d+)$/i.exec(s)) && KINDS[m[2].toLowerCase()]) return { t: KINDS[m[2].toLowerCase()], j: +m[3], so: +m[1] };
    if ((m = /^([a-z]+)[_:\-]?(\d{1,5})$/i.exec(s)) && KINDS[m[1].toLowerCase()] && KINDS[m[1].toLowerCase()] !== "o") return { t: KINDS[m[1].toLowerCase()], j: +m[2], so: 0 };
    return null;
  }

  /** The join a signal name stands for, or null. The type the caller asked for is only a default; a contract or typed name decides its own. */
  function resolve(type, name) {
    var s = String(name == null ? "" : name).trim();
    if (directMap[s]) return directMap[s];
    var r = parseJoinName(type, s);
    if (r && r.j > 0) return r;
    if (contractNames[s]) return contractNames[s];
    // Rooms[2].Light.Press: instance index times the contract's span for that type
    var m = /^(.*?)\[(\d+)\](.*)$/.exec(s);
    if (m) {
      var base = (m[1] + "[]" + m[3]);
      var hit = contractNames[base];
      if (hit && hit.span) return { t: hit.t, j: hit.j + (+m[2]) * hit.span, so: hit.so };
    }
    return null;
  }

  function addName(table, name, t, j, so, span) { table[name] = { t: t, j: j, so: so || 0, span: span || 0 }; }

  /**
   * Register an OpenVP-format contract (see the header). Returns the number of names it added.
   */
  function registerContract(def) {
    if (!def || typeof def !== "object" || !def.members) return 0;
    var members = def.members, count = 0;
    var span = def.span || {};
    // The default span of a type is the highest join of that type in the contract.
    Object.keys(members).forEach(function (k) {
      var t = norm(members[k].type), j = members[k].join | 0;
      if (t in state && !(t in span)) span[t] = 0;
      if (t in state && j > (span[t] || 0)) span[t] = j;
    });
    var prefixes = def.instances ? Object.keys(def.instances) : [""];
    prefixes.forEach(function (inst) {
      var off = (def.instances && def.instances[inst]) || {};
      Object.keys(members).forEach(function (k) {
        var mem = members[k], t = norm(mem.type);
        if (!(t in state) || !(mem.join > 0)) return;
        var j = (mem.join | 0) + (off[t] | 0);
        var full = inst ? inst + "." + k : k;
        addName(contractNames, full, t, j, mem.so, 0); count++;
        if (def.name && inst === "") { addName(contractNames, def.name + "." + k, t, j, mem.so, 0); count++; }
        if (def.name && inst) { /* "Room.Room1.Light.Press" is not a thing; Room1.Light.Press already covers it */ }
      });
    });
    // Name[i].Member form: stored as "Name[].Member" with the span of its type.
    if (def.name) Object.keys(members).forEach(function (k) {
      var mem = members[k], t = norm(mem.type);
      if (!(t in state) || !(mem.join > 0)) return;
      addName(contractNames, def.name + "[]." + k, t, mem.join | 0, mem.so, span[t] || 0);
    });
    contracts.push(def);
    retryPending();
    return count;
  }

  /**
   * Read a Contract Editor export (.cse2j) as far as its layout is known: nested objects whose leaves have a Type of
   * Boolean / Integer / String. Joins are numbered from 1 per type and direction in the order the file lists them
   * (events are things the panel sends, states are what it receives). This follows the file's published shape as best we know it
   * and is not checked against Crestron's own tool, so check a few names against your program.
   */
  function contractFromCse2j(json, name) {
    var members = {}, counters = {};
    var root = json && (json.Objects || json.objects || json.Contract || json.contract || json);
    function typeOf(node) {
      var t = String(node.Type || node.type || node.DataType || node.dataType || "").toLowerCase();
      if (/^(bool|boolean|digital)/.test(t)) return "b";
      if (/^(int|integer|number|analog|ushort|short|uint|long)/.test(t)) return "n";
      if (/^(str|string|serial)/.test(t)) return "s";
      return null;
    }
    function dirOf(node) {
      var d = String(node.Direction || node.direction || node.Kind || node.kind || node.Category || "").toLowerCase();
      return /event|command|out|send|press|set|action/.test(d) ? "out" : "in";
    }
    function walk(node, path, depth) {
      if (!node || typeof node !== "object" || depth > 12) return;
      if (Array.isArray(node)) { node.forEach(function (c) { walk(c, path, depth + 1); }); return; }
      var t = typeOf(node);
      if (t && path.length) {
        var d = dirOf(node), key = t + d;
        counters[key] = (counters[key] || 0) + 1;
        members[path.join(".")] = { type: t, join: counters[key], dir: d };
        return;
      }
      Object.keys(node).forEach(function (k) {
        var v = node[k];
        if (v && typeof v === "object" && !/^(Type|Direction|Meta|Metadata|Description)$/i.test(k)) {
          if (/^(Objects|Members|Children|Items|Signals|Events|States)$/i.test(k)) walk(v, path, depth + 1);
          else walk(v, path.concat(k), depth + 1);
        }
      });
    }
    walk(root, [], 0);
    return { name: name || (json && (json.ContractName || json.Name || json.name)) || "", members: members };
  }

  function mapSignals(map) {
    Object.keys(map || {}).forEach(function (name) {
      var r = parseJoinName("b", map[name]);
      if (r) directMap[name] = r;
    });
    retryPending();
  }

  function unresolved(type, name) {
    var key = type + ":" + name;
    if (warned[key]) return;
    warned[key] = true;
    console.warn("openvp-crcomlib: no join for signal name \"" + name + "\". Use a join number, a typed join like d5, a contract (openvp-contract.json) or OpenVP.mapSignals().");
    fire("unmapped", { type: type, name: name });
  }

  function loadContractFiles() {
    if (!global.fetch) return;
    var base = (global.location && global.location.href) || "";
    function get(file, parse) {
      try {
        global.fetch(new URL(file, base).toString(), { cache: "no-store" }).then(function (r) { return r.ok ? r.json() : null; })
          .then(function (j) { if (j) parse(j); }, function () { });
      } catch (e) { }
    }
    get("openvp-contract.json", function (j) { if (j.members) registerContract(j); if (j.signals) mapSignals(j.signals); });
    get("contract.cse2j", function (j) { registerContract(contractFromCse2j(j)); });
  }

  // ------------------------------------------------------------------ transport

  function clientInfo() {
    var n = global.navigator || {}, s = global.screen || {};
    var info = {
      t: "info",
      screen: s.width && s.height ? s.width + "x" + s.height : undefined,
      viewport: global.innerWidth + "x" + global.innerHeight,
      pixelRatio: String(global.devicePixelRatio || 1),
      platform: (n.userAgentData && n.userAgentData.platform) || n.platform,
      language: n.language,
      touch: n.maxTouchPoints != null ? String(n.maxTouchPoints) : undefined,
      cores: n.hardwareConcurrency != null ? String(n.hardwareConcurrency) : undefined,
      memoryGb: n.deviceMemory != null ? String(n.deviceMemory) : undefined,
      connection: n.connection && n.connection.effectiveType,
      page: global.location && global.location.pathname,
      title: doc && doc.title
    };
    try { info.timezone = Intl.DateTimeFormat().resolvedOptions().timeZone; } catch (e) { }
    return info;
  }

  function ping() {
    if (!ws || ws.readyState !== 1) return;
    pingSent = Date.now();
    var m = { t: "ping" };
    if (lastRtt != null) m.rtt = lastRtt;
    ws.send(JSON.stringify(m));
  }

  // Round-trip history for the indicator: the last 30 measurements.
  var latency = { ms: null, min: null, avg: null, max: null, history: [] };
  function recordLatency(ms) {
    latency.history.push(ms); if (latency.history.length > 30) latency.history.shift();
    var h = latency.history, sum = 0, i;
    for (i = 0; i < h.length; i++) sum += h[i];
    latency.ms = ms; latency.min = Math.min.apply(null, h); latency.max = Math.max.apply(null, h); latency.avg = sum / h.length;
    fire("latency", latency);
  }

  function emit(type, key, value) {
    state[type][key] = value;
    var list = subs[type][key];
    if (list) Object.keys(list).forEach(function (id) {
      try { list[id](value); } catch (e) { console.error(e); }
    });
  }

  function send(msg) {
    var text = JSON.stringify(msg);
    if (ws && ws.readyState === 1) ws.send(text); else queue.push(text);
  }

  function connect() {
    clearTimeout(reconnectTimer);
    ws = new global.WebSocket(url);
    ws.onopen = function () {
      retry = 0; lastSeen = Date.now();
      ws.send(JSON.stringify(clientInfo()));
      while (queue.length) ws.send(queue.shift());
      clearInterval(pingTimer); clearInterval(watchdog);
      pingTimer = setInterval(ping, 10000);
      // A connection that goes quiet without closing (Wi-Fi dropped, laptop slept) is replaced: the server pings every 5 s.
      watchdog = setInterval(function () { if (Date.now() - lastSeen > 25000 && ws && ws.readyState === 1) { try { ws.close(); } catch (e) { } } }, 5000);
      setTimeout(ping, 500);
      fire("open");
    };
    ws.onmessage = function (ev) {
      lastSeen = Date.now();
      var m;
      try { m = JSON.parse(ev.data); } catch (e) { return; }
      if (m.t === "b" || m.t === "n" || m.t === "s") emit(m.t, keyOf(m.j, m.so), m.v);
      else {
        if (m.t === "pong" && pingSent) { lastRtt = Date.now() - pingSent; pingSent = 0; recordLatency(lastRtt); }
        else if (m.t === "sping") { try { ws.send(JSON.stringify({ t: "spong", id: m.id })); } catch (e) { } }
        fire(m.t, m);
      }
    };
    ws.onclose = function () {
      clearInterval(pingTimer); clearInterval(watchdog);
      fire("close");
      if (shared && cfg.statusUrl) {
        // Closed because the link was revoked or ran out? Then reload to show the expired page instead of retrying.
        global.fetch(cfg.statusUrl, { cache: "no-store" }).then(function (r) {
          if (r.status === 410 || r.status === 404) global.location.reload(); else scheduleReconnect();
        }).catch(scheduleReconnect);
        return;
      }
      scheduleReconnect();
    };
  }

  // 1 s, 2 s, 4 s ... up to 10 s, with a little jitter so a room of panels doesn't all come back in the same instant.
  function scheduleReconnect() {
    var wait = Math.min(10000, 1000 * Math.pow(2, retry++)) + Math.random() * 400;
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(connect, wait);
  }

  function fire(name, data) {
    listeners.forEach(function (l) { if (l.name === name) l.fn(data); });
  }

  // ------------------------------------------------------------------ the CrComLib surface

  function attach(s) {
    var key = keyOf(s.r.j, s.r.so);
    (subs[s.r.t][key] = subs[s.r.t][key] || {})[s.id] = s.cb;
    s.key = key; s.rt = s.r.t;
    if (key in state[s.r.t]) setTimeout(function () { s.cb(state[s.r.t][key]); }, 0);
  }

  function retryPending() {
    pending = pending.filter(function (s) {
      var r = resolve(s.type, s.name);
      if (!r) return true;
      s.r = r; attach(s);
      return false;
    });
  }

  var live = {};   // id -> subscription (resolved or pending)

  var api = {
    version: "openvp-1",
    publishEvent: function (type, signalName, value) {
      type = norm(type);
      if (type === "o") {
        if (value && typeof value === "object" && "repeatdigital" in value) { type = "b"; value = !!value.repeatdigital; }
        else return;
      }
      var r = resolve(type, signalName);
      if (!r) { unresolved(type, signalName); return; }
      var t = r.t;
      if (t === "n") value = Math.max(0, Math.min(65535, Math.round(Number(value)) || 0));
      else if (t === "b") value = !!value;
      else value = value == null ? "" : String(value);
      var msg = { t: t, j: r.j, v: value };
      if (r.so) msg.so = r.so;
      send(msg);
    },
    subscribeState: function (type, signalName, callback) {
      type = norm(type);
      var id = "sub" + (nextId++);
      var s = { id: id, type: type, name: signalName, cb: callback, r: null };
      live[id] = s;
      var r = resolve(type, signalName);
      if (r) { s.r = r; attach(s); }
      else { pending.push(s); unresolved(type, signalName); }
      return id;
    },
    unsubscribeState: function (type, signalName, id) {
      var s = live[id];
      if (!s) return;
      delete live[id];
      pending = pending.filter(function (p) { return p !== s; });
      if (s.key != null && subs[s.rt][s.key]) delete subs[s.rt][s.key][id];
    },
    getState: function (type, signalName) {
      var r = resolve(norm(type), signalName);
      return r ? state[r.t][keyOf(r.j, r.so)] : undefined;
    },
    /** OpenVP extra: connection events ("open", "close", "hello", "offline", "latency", "unmapped"). */
    on: function (name, fn) { listeners.push({ name: name, fn: fn }); },
    /** OpenVP extras for contracts and names (see the header). */
    registerContract: registerContract,
    contractFromCse2j: contractFromCse2j,
    mapSignals: mapSignals,
    resolve: function (type, name) { var r = resolve(norm(type), name); return r ? { type: r.t, join: r.j, smartObject: r.so } : null; },
    latency: latency,
    config: { ipid: ipid, slot: slot, room: room, url: url },
    // A few helpers CH5 projects call; there is no Crestron panel here, so these answer for a browser.
    isCrestronTouchscreen: function () { return false; },
    isCrestronDevice: function () { return false; },
    isIosDevice: function () { return /iPad|iPhone|iPod/.test((global.navigator && global.navigator.userAgent) || ""); },
    isMobileDevice: function () { return /Android|iPad|iPhone|iPod|Mobile/.test((global.navigator && global.navigator.userAgent) || ""); },
    getCrComLibVersion: function () { return "openvp-1"; }
  };

  if (shared) {
    // A project's own CrComLib bundle assigns window.CrComLib when it loads; ignore that so the page keeps using OpenVP.
    try { Object.defineProperty(global, "CrComLib", { get: function () { return api; }, set: function () { }, configurable: false }); }
    catch (e) { global.CrComLib = api; }
  } else {
    global.CrComLib = global.CrComLib || api;
  }
  global.OpenVP = api;

  // ------------------------------------------------------------------ components

  function boolOf(v) { return v === true || v === 1 || v === "1" || v === "true" || v === "True"; }

  function defineComponents() {
    if (!doc || !global.customElements || global.customElements.get === undefined) return;
    var css = doc.createElement("style");
    css.setAttribute("data-openvp-ch5", "");
    css.textContent =
      "ch5-button,ch5-toggle,ch5-slider,ch5-label,ch5-text,ch5-textinput,ch5-select,ch5-list,ch5-image{display:inline-block;box-sizing:border-box;vertical-align:top}" +
      "ch5-list{display:block}ch5-list[orientation=horizontal]{display:flex;flex-wrap:nowrap;overflow:auto}" +
      "ch5-button>button{font:inherit;color:inherit;width:100%;height:100%;min-height:2.4em;padding:.4em 1em;border:1px solid #8886;border-radius:6px;background:#8883;cursor:pointer;touch-action:manipulation;user-select:none;-webkit-user-select:none}" +
      "ch5-button.ch5-button--selected>button{background:#2f9e6e;border-color:#2f9e6e;color:#fff}" +
      "ch5-button>button:disabled{opacity:.45;cursor:not-allowed}" +
      "ch5-toggle>label{display:inline-flex;align-items:center;gap:.5em;cursor:pointer}" +
      "ch5-slider>input{width:100%;min-width:8em}" +
      "ch5-image>img{max-width:100%;height:auto;display:block}" +
      "[data-ch5-hidden]{display:none!important}";
    (doc.head || doc.documentElement).appendChild(css);

    // Reads attribute `attr` as a signal name and calls fn(value) on every change; the subscription ends when the element leaves the page.
    function bind(el, attr, type, fn) {
      var name = el.getAttribute(attr);
      if (!name) return;
      var id = api.subscribeState(type, name, fn);
      (el._ch5subs = el._ch5subs || []).push([type, name, id]);
    }
    function unbind(el) { (el._ch5subs || []).forEach(function (s) { api.unsubscribeState(s[0], s[1], s[2]); }); el._ch5subs = []; }
    function publish(el, attr, type, value) {
      var name = el.getAttribute(attr);
      if (name) api.publishEvent(type, name, value);
    }
    function common(el) {
      bind(el, "receivestateshow", "boolean", function (v) { if (boolOf(v)) el.removeAttribute("data-ch5-hidden"); else el.setAttribute("data-ch5-hidden", ""); });
      if (el.getAttribute("show") === "false") el.setAttribute("data-ch5-hidden", "");
    }
    function define(name, cls) { if (!global.customElements.get(name)) global.customElements.define(name, cls); }
    function safeUrl(u) { return /^(https?:|data:image\/(png|jpe?g|gif|webp|bmp);|blob:|\/|\.\.?\/|[\w\-.%\/]+$)/i.test(String(u).trim()) ? u : ""; }

    function El(connected, disconnected) {
      return class extends global.HTMLElement {
        connectedCallback() { if (this._ch5up) return; this._ch5up = true; connected.call(this, this); common(this); }
        disconnectedCallback() { unbind(this); this._ch5up = false; if (disconnected) disconnected.call(this, this); }
      };
    }

    define("ch5-button", El(function (el) {
      var label = el.getAttribute("label") || el.textContent.trim();
      el.textContent = "";
      var b = doc.createElement("button");
      b.type = "button"; b.textContent = label;
      el.appendChild(b);
      el._btn = b;
      var down = false;
      var touch = el.getAttribute("sendeventontouch");
      b.addEventListener("pointerdown", function (e) { down = true; if (b.setPointerCapture) { try { b.setPointerCapture(e.pointerId); } catch (x) { } } if (touch) publish(el, "sendeventontouch", "boolean", true); });
      function up() { if (!down) return; down = false; if (touch) publish(el, "sendeventontouch", "boolean", false); }
      ["pointerup", "pointercancel", "lostpointercapture"].forEach(function (t) { b.addEventListener(t, up); });
      b.addEventListener("click", function () {
        if (!el.getAttribute("sendeventonclick")) return;
        publish(el, "sendeventonclick", "boolean", true); publish(el, "sendeventonclick", "boolean", false);   // a click is a pulse
      });
      b.addEventListener("contextmenu", function (e) { e.preventDefault(); });
      bind(el, "receivestatelabel", "string", function (v) { b.textContent = v; });
      bind(el, "receivestateselected", "boolean", function (v) { el.classList.toggle("ch5-button--selected", boolOf(v)); if (boolOf(v)) el.setAttribute("selected", ""); else el.removeAttribute("selected"); });
      bind(el, "receivestateenable", "boolean", function (v) { b.disabled = !boolOf(v); });
    }));

    define("ch5-toggle", El(function (el) {
      var lab = doc.createElement("label"), box = doc.createElement("input"), txt = doc.createElement("span");
      box.type = "checkbox"; txt.textContent = el.getAttribute("label") || "";
      el.textContent = ""; lab.appendChild(box); lab.appendChild(txt); el.appendChild(lab);
      var on = el.getAttribute("labelon"), off = el.getAttribute("labeloff");
      function show() { if (on || off) txt.textContent = box.checked ? (on || "") : (off || ""); }
      box.addEventListener("change", function () { publish(el, "sendeventonchange", "boolean", box.checked); show(); });
      bind(el, "receivestatevalue", "boolean", function (v) { box.checked = boolOf(v); show(); });
      bind(el, "receivestatelabel", "string", function (v) { txt.textContent = v; });
    }));

    define("ch5-slider", El(function (el) {
      var inp = doc.createElement("input");
      inp.type = "range";
      inp.min = el.getAttribute("min") || "0"; inp.max = el.getAttribute("max") || "65535"; inp.step = el.getAttribute("step") || "1";
      inp.value = el.getAttribute("value") || inp.min;
      el.textContent = ""; el.appendChild(inp);
      var last = 0;
      inp.addEventListener("input", function () {
        var now = Date.now();
        if (now - last < 40) return;   // at most about 25 updates a second while dragging; the last value is sent on release
        last = now; publish(el, "sendeventonchange", "number", +inp.value);
      });
      inp.addEventListener("change", function () { publish(el, "sendeventonchange", "number", +inp.value); });
      bind(el, "receivestatevalue", "number", function (v) { if (doc.activeElement !== inp) inp.value = v; });
    }));

    function labelClass() {
      return El(function (el) {
        var span = doc.createElement("span");
        span.textContent = el.getAttribute("label") || el.textContent.trim();
        el.textContent = ""; el.appendChild(span);
        // Text only. A serial value is never put in as HTML, whatever the program sends.
        bind(el, "receivestatelabel", "string", function (v) { span.textContent = v; });
        bind(el, "receivestatescriptlabelhtml", "string", function (v) { span.textContent = v; });
      });
    }
    define("ch5-label", labelClass());
    define("ch5-text", labelClass());

    define("ch5-textinput", El(function (el) {
      var inp = doc.createElement("input");
      inp.type = el.getAttribute("type") === "password" ? "password" : "text";
      inp.placeholder = el.getAttribute("placeholder") || "";
      el.textContent = ""; el.appendChild(inp);
      var typing = false;   // text the person is still typing is not overwritten by feedback; once it is sent (Enter) or left, feedback applies again
      function sendIt() { typing = false; publish(el, "sendeventonchange", "string", inp.value); }
      inp.addEventListener("input", function () { typing = true; });
      inp.addEventListener("blur", function () { typing = false; });
      inp.addEventListener("keydown", function (e) { if (e.key === "Enter") sendIt(); });
      inp.addEventListener("change", sendIt);
      bind(el, "receivestatevalue", "string", function (v) { if (!typing) inp.value = v; });
    }));

    define("ch5-select", El(function (el) {
      var options = [];
      Array.prototype.slice.call(el.children).forEach(function (c) {
        if (/^(ch5-select-option|option)$/i.test(c.tagName)) options.push({ label: c.getAttribute("label") || c.textContent.trim(), value: c.getAttribute("value") });
      });
      var sel = doc.createElement("select");
      function fill(n) {
        sel.textContent = "";
        for (var i = 0; i < n; i++) {
          var o = doc.createElement("option");
          o.value = String(i); o.textContent = (options[i] && options[i].label) || ("Option " + (i + 1));
          sel.appendChild(o);
        }
      }
      fill(options.length || +(el.getAttribute("size") || 0));
      el.textContent = ""; el.appendChild(sel);
      sel.addEventListener("change", function () { publish(el, "sendeventonchange", "number", +sel.value); });
      bind(el, "receivestatesize", "number", function (v) { var keep = sel.value; fill(+v || 0); sel.value = keep; });
      bind(el, "receivestatevalue", "number", function (v) { sel.value = String(v); });
    }));

    define("ch5-image", El(function (el) {
      var img = doc.createElement("img");
      img.alt = el.getAttribute("alt") || "";
      var u = el.getAttribute("url");
      if (u) img.src = safeUrl(u);
      el.textContent = ""; el.appendChild(img);
      bind(el, "receivestateurl", "string", function (v) { img.src = safeUrl(v); });
    }));

    // A list: a <template> child is stamped out once per item. {{idx}} in the template (text, attributes) is the item number from 0,
    // and {{idx+1}} / {{idx-1}} etc. do the sums, so a signal name can be "item{{idx+1}}press" with join numbers 1, 2, 3 ...
    define("ch5-list", El(function (el) {
      var tpl = el.querySelector("template");
      var html = tpl ? tpl.innerHTML : "";
      var holder = doc.createElement("div");
      holder.style.cssText = el.getAttribute("orientation") === "horizontal" ? "display:flex;gap:6px" : "display:flex;flex-direction:column;gap:6px";
      if (tpl) tpl.remove();
      el.textContent = ""; el.appendChild(holder);
      var shown = -1;
      function render(n) {
        n = Math.max(0, Math.min(500, n | 0));
        if (n === shown) return;
        shown = n;
        // Children's subscriptions end with the children (their disconnectedCallback).
        holder.textContent = "";
        for (var i = 0; i < n; i++) {
          var item = doc.createElement("div");
          item.innerHTML = html.replace(/\{\{\s*idx\s*(?:([+-])\s*(\d+))?\s*\}\}/g, function (_, op, num) { return String(op === "-" ? i - (+num) : op === "+" ? i + (+num) : i); });
          holder.appendChild(item);
        }
      }
      var fixed = el.getAttribute("size");
      if (fixed != null) render(+fixed);
      bind(el, "receivestatesize", "number", function (v) { render(+v); });
    }));
  }

  var KNOWN = "ch5-button,ch5-toggle,ch5-slider,ch5-label,ch5-text,ch5-textinput,ch5-select,ch5-list,ch5-image";
  function maybeDefineComponents() {
    if (cfg.components === false || !doc || !global.customElements) return;
    // Leave it to the project's own CH5 bundle if that defines the elements; only fill in what is missing, after the page has parsed.
    var used = doc.querySelector(KNOWN) || doc.querySelector("template");
    if (used || cfg.components === true) defineComponents();
  }
  api.defineComponents = defineComponents;
  /** For pages that measure the round trip some other way (the generated panel uses a small HTTP request). */
  api.setLatency = function (ms) { recordLatency(ms); };

  // ------------------------------------------------------------------ kiosk mode and the latency badge

  function kioskWanted() {
    var q = params.get("kiosk");
    try {
      if (q === "1" || q === "true") { global.localStorage.setItem("openvp.kiosk", "1"); return true; }
      if (q === "0" || q === "false") { global.localStorage.removeItem("openvp.kiosk"); global.localStorage.removeItem("openvp.kioskpin"); return false; }
      return cfg.kiosk === true || global.localStorage.getItem("openvp.kiosk") === "1";
    } catch (e) { return q === "1" || q === "true" || cfg.kiosk === true; }
  }

  function startKiosk() {
    var html = doc.documentElement;
    html.classList.add("openvp-kiosk");
    var st = doc.createElement("style");
    st.textContent =
      "html.openvp-kiosk,html.openvp-kiosk body{overscroll-behavior:none;-webkit-user-select:none;user-select:none;-webkit-touch-callout:none;cursor:none}" +
      "html.openvp-kiosk [data-chrome]{display:none!important}" +
      "html.openvp-kiosk body{touch-action:manipulation}" +
      "#openvp-kiosk-exit{position:fixed;inset:0;z-index:2147483647;background:#000c;display:grid;place-items:center;font:16px system-ui,sans-serif;color:#fff;cursor:default}" +
      "#openvp-kiosk-exit form{background:#1b2027;border:1px solid #2c333d;border-radius:10px;padding:18px;min-width:min(320px,90vw);display:grid;gap:10px}" +
      "#openvp-kiosk-exit input,#openvp-kiosk-exit button{font:inherit;color:inherit;background:#232a33;border:1px solid #3a4452;border-radius:6px;padding:8px 10px}";
    (doc.head || html).appendChild(st);
    var pin = params.get("kioskpin");
    try { if (pin) global.localStorage.setItem("openvp.kioskpin", pin); else pin = global.localStorage.getItem("openvp.kioskpin"); } catch (e) { }

    // Screen on: the Wake Lock API where the browser has it (needs a secure page; localhost and https qualify). It is released when the tab hides, so ask again on return.
    var lock = null;
    function wake() {
      if (!(global.navigator && global.navigator.wakeLock) || doc.visibilityState !== "visible") return;
      global.navigator.wakeLock.request("screen").then(function (l) { lock = l; l.addEventListener("release", function () { lock = null; }); }, function () { });
    }
    doc.addEventListener("visibilitychange", function () { if (!lock) wake(); });
    wake();
    api.kiosk = { active: true, wakeLock: function () { return !!lock; } };

    // Full screen needs a tap first; the first touch does it, and so does coming back after the user left it by accident.
    function fs() {
      var el = html;
      if (!doc.fullscreenElement && el.requestFullscreen) { try { var p = el.requestFullscreen({ navigationUI: "hide" }); if (p && p.catch) p.catch(function () { }); } catch (e) { } }
      wake();
    }
    doc.addEventListener("pointerdown", fs, { passive: true });
    doc.addEventListener("contextmenu", function (e) { e.preventDefault(); });

    // A page that has been offline for a minute reloads when it is back, so a panel never sits on stale content after the server restarted.
    var offlineAt = 0;
    api.on("close", function () { offlineAt = offlineAt || Date.now(); });
    api.on("open", function () { if (offlineAt && Date.now() - offlineAt > 60000) global.location.reload(); offlineAt = 0; });
    global.addEventListener("online", function () { retry = 0; if (!ws || ws.readyState > 1) connect(); });

    // The hidden exit: five taps within three seconds in the top-left corner.
    var taps = [];
    doc.addEventListener("pointerdown", function (e) {
      if (e.clientX > 72 || e.clientY > 72) return;
      var now = Date.now();
      taps = taps.filter(function (t) { return now - t < 3000; }); taps.push(now);
      if (taps.length >= 5) { taps = []; askExit(); }
    }, { passive: true });

    function leave() {
      try { global.localStorage.removeItem("openvp.kiosk"); global.localStorage.removeItem("openvp.kioskpin"); } catch (e) { }
      if (doc.fullscreenElement && doc.exitFullscreen) { try { doc.exitFullscreen(); } catch (e) { } }
      if (lock) { try { lock.release(); } catch (e) { } }
      var u = new URL(global.location.href); u.searchParams.set("kiosk", "0"); u.searchParams.delete("kioskpin");
      global.location.replace(u.toString());
    }
    function askExit() {
      if (doc.getElementById("openvp-kiosk-exit")) return;
      var box = doc.createElement("div"); box.id = "openvp-kiosk-exit";
      box.innerHTML = "<form><b>Leave kiosk mode?</b>" + (pin ? "<input type=password inputmode=numeric autocomplete=off placeholder=PIN>" : "") + "<div style='display:flex;gap:8px;justify-content:flex-end'><button type=button data-no>Stay</button><button data-yes>Leave</button></div><div data-err style='color:#ff9b9b;min-height:1.2em'></div></form>";
      doc.body.appendChild(box);
      var f = box.querySelector("form"), inp = box.querySelector("input");
      if (inp) inp.focus();
      box.querySelector("[data-no]").onclick = function () { box.remove(); };
      f.onsubmit = function (e) { e.preventDefault(); if (pin && inp.value !== pin) { box.querySelector("[data-err]").textContent = "Wrong PIN."; inp.select(); return; } leave(); };
      setTimeout(function () { if (box.parentNode) box.remove(); }, 30000);
    }
  }

  function showLatencyBadge() {
    var b = doc.createElement("div");
    b.id = "openvp-latency";
    b.setAttribute("aria-label", "Round-trip time to OpenVP");
    b.style.cssText = "position:fixed;right:6px;bottom:6px;z-index:2147483000;font:11px/1.2 ui-monospace,Menlo,Consolas,monospace;padding:2px 6px;border-radius:9px;background:#000a;color:#fff;pointer-events:none";
    b.textContent = "-- ms";
    doc.body.appendChild(b);
    function paint() {
      var ms = latency.ms;
      b.textContent = ms == null ? "-- ms" : Math.round(ms) + " ms";
      b.style.background = ms == null ? "#000a" : ms < 50 ? "#1e7a52cc" : ms < 200 ? "#b07d12cc" : "#b03a2ecc";
    }
    api.on("latency", paint);
    api.on("close", function () { latency.ms = null; paint(); });
  }
  api.showLatency = function () { if (doc && doc.body && !doc.getElementById("openvp-latency")) showLatencyBadge(); };

  function ready() {
    maybeDefineComponents();
    if (kioskWanted()) startKiosk();
    if (params.get("latency") === "1" || cfg.latency === true) api.showLatency();
  }

  // ------------------------------------------------------------------ start

  if (global.OPENVP_CONTRACT) registerContract(global.OPENVP_CONTRACT);
  if (cfg.contract) registerContract(cfg.contract);
  if (cfg.signals) mapSignals(cfg.signals);
  if (global.WebSocket && cfg.connect !== false) connect();
  if (doc) {
    // OpenVP's own pages (data-openvp-page on <html>) have no contract; a project's pages may.
    if (!doc.documentElement.hasAttribute("data-openvp-page") && cfg.contractFiles !== false) loadContractFiles();
    if (doc.readyState === "loading") doc.addEventListener("DOMContentLoaded", ready); else ready();
    // Back on the page after the device slept: don't wait out the back-off.
    doc.addEventListener("visibilitychange", function () { if (doc.visibilityState === "visible" && (!ws || ws.readyState > 1)) { retry = 0; connect(); } });
  }
})(typeof window !== "undefined" ? window : globalThis);
