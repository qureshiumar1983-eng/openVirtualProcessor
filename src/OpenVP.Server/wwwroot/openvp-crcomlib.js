/*
 * openvp-crcomlib.js — a small stand-in for CrComLib's join API, talking to OpenVP over WebSocket.
 *
 * Load it in a CH5 / HTML panel project instead of (or before) Crestron's CrComLib:
 *   <script src="http://<openvp-host>:8080/openvp-crcomlib.js"></script>
 * and open the page with ?ipid=03 (and optionally &slot=1&room=ROOM2&host=10.0.0.5:8080).
 *
 * Supported: publishEvent / subscribeState / unsubscribeState / getState for signal names that are
 * join numbers ("1", "27"). Types: "b" digital, "n" analog, "s" serial, "o" with {repeatdigital: bool}.
 * Contract-editor names and CH5 web components that need the full CrComLib are not covered.
 *
 * Shared panels (/s/{token}/...): OpenVP sets window.OPENVP_CONFIG = { wsPath, share: true, statusUrl } before
 * this script. The page then always connects to wsPath (the slot and IP ID come from the share link, never from
 * the address bar), and window.CrComLib is pinned to this shim, so a project that bundles its own CrComLib
 * still publishes and subscribes through OpenVP. When the share link is revoked or expires the page reloads
 * and shows the "expired" message.
 */
(function (global) {
  "use strict";
  var params = new URLSearchParams(global.location.search);
  var cfg = global.OPENVP_CONFIG || {};
  var ipid = (cfg.ipid || params.get("ipid") || "03").toString();
  var slot = cfg.slot || params.get("slot");
  var room = cfg.room || params.get("room");
  var host = cfg.host || params.get("host") || global.location.host;
  var proto = global.location.protocol === "https:" ? "wss://" : "ws://";
  var shared = !!cfg.share;
  var url = shared && cfg.wsPath
    ? proto + global.location.host + cfg.wsPath
    : proto + host + "/ws/panel?ipid=" + encodeURIComponent(ipid) + (slot ? "&slot=" + encodeURIComponent(slot) : "") + (room ? "&room=" + encodeURIComponent(room) : "");

  var state = { b: {}, n: {}, s: {} };
  var subs = { b: {}, n: {}, s: {} };
  var nextId = 1;
  var ws = null;
  var queue = [];
  var listeners = [];
  var pingSent = 0, lastRtt = null, pingTimer = null;

  // Tells OpenVP's IP table what this client is. Nothing personal: screen, platform, language, page.
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
      page: global.location.pathname,
      title: global.document && global.document.title
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

  function norm(type) {
    if (type === "d" || type === "boolean") return "b";
    if (type === "a" || type === "number") return "n";
    if (type === "string") return "s";
    return type;
  }

  function emit(type, join, value) {
    state[type][join] = value;
    var list = subs[type][join];
    if (list) Object.keys(list).forEach(function (id) {
      try { list[id](value); } catch (e) { console.error(e); }
    });
  }

  function send(msg) {
    var text = JSON.stringify(msg);
    if (ws && ws.readyState === 1) ws.send(text); else queue.push(text);
  }

  function connect() {
    ws = new WebSocket(url);
    ws.onopen = function () {
      ws.send(JSON.stringify(clientInfo()));
      while (queue.length) ws.send(queue.shift());
      clearInterval(pingTimer);
      pingTimer = setInterval(ping, 10000);
      setTimeout(ping, 500);
      fire("open");
    };
    ws.onmessage = function (ev) {
      var m = JSON.parse(ev.data);
      if (m.t === "b" || m.t === "n" || m.t === "s") emit(m.t, String(m.j), m.v);
      else {
        if (m.t === "pong" && pingSent) { lastRtt = Date.now() - pingSent; pingSent = 0; }
        fire(m.t, m);
      }
    };
    ws.onclose = function () {
      clearInterval(pingTimer);
      fire("close");
      if (shared && cfg.statusUrl) {
        // Closed because the link was revoked or ran out? Then reload to show the expired page instead of retrying.
        fetch(cfg.statusUrl, { cache: "no-store" }).then(function (r) {
          if (r.status === 410 || r.status === 404) global.location.reload(); else setTimeout(connect, 2000);
        }).catch(function () { setTimeout(connect, 2000); });
        return;
      }
      setTimeout(connect, 2000);
    };
  }

  function fire(name, data) {
    listeners.forEach(function (l) { if (l.name === name) l.fn(data); });
  }

  var api = {
    publishEvent: function (type, signalName, value) {
      type = norm(type);
      if (type === "o") {
        if (value && typeof value === "object" && "repeatdigital" in value) { type = "b"; value = !!value.repeatdigital; }
        else return;
      }
      var join = parseInt(signalName, 10);
      if (!(join > 0)) { console.warn("openvp-crcomlib: only numeric join names are supported:", signalName); return; }
      if (type === "n") value = Math.max(0, Math.min(65535, value | 0));
      if (type === "b") value = !!value;
      send({ t: type, j: join, v: value });
    },
    subscribeState: function (type, signalName, callback) {
      type = norm(type);
      var join = String(parseInt(signalName, 10));
      var id = "sub" + (nextId++);
      (subs[type][join] = subs[type][join] || {})[id] = callback;
      if (join in state[type]) setTimeout(function () { callback(state[type][join]); }, 0);
      return id;
    },
    unsubscribeState: function (type, signalName, id) {
      type = norm(type);
      var list = subs[type][String(parseInt(signalName, 10))];
      if (list) delete list[id];
    },
    getState: function (type, signalName) {
      return state[norm(type)][String(parseInt(signalName, 10))];
    },
    /** OpenVP extra: connection events ("open", "close", "hello", "offline"). */
    on: function (name, fn) { listeners.push({ name: name, fn: fn }); },
    config: { ipid: ipid, slot: slot, room: room, url: url }
  };

  if (shared) {
    // A project's own CrComLib bundle assigns window.CrComLib when it loads; ignore that so the page keeps using OpenVP.
    try { Object.defineProperty(global, "CrComLib", { get: function () { return api; }, set: function () { }, configurable: false }); }
    catch (e) { global.CrComLib = api; }
  } else {
    global.CrComLib = global.CrComLib || api;
  }
  global.OpenVP = api;
  connect();
})(window);
