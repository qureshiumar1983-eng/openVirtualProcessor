// Shared by the console pages: the alert badge, and (if the person turns it on) browser notifications.
//
// The server streams alerts at /api/alerts/stream (server-sent events). In a browser the page shows them with the Web Notifications API, but
// only while a console page is open and only after the person opted in here (the browser asks for permission once). In the desktop app the
// app itself shows notifications, so this does nothing there.
(() => {
  const KEY = "openvp.notify";
  const desktop = !!window.openvpDesktop;
  let es = null, last = null, retry = 0, timer = 0;
  const listeners = new Set();

  const get = () => { try { return localStorage.getItem(KEY) === "1"; } catch { return false; } };
  const set = v => { try { localStorage.setItem(KEY, v ? "1" : "0"); } catch { } };
  const supported = () => !desktop && "Notification" in window;

  function show(a) {
    if (!supported() || Notification.permission !== "granted") return;
    const icon = { critical: "🔴", warning: "🟠", info: "🔵" }[a.severity] || "";
    const prefix = a.event === "resolved" ? "✅ Resolved: " : a.event === "test" ? "🧪 Test: " : icon + " ";
    try {
      const n = new Notification(prefix + a.title, { body: a.message, tag: a.key || a.id, requireInteraction: a.severity === "critical" && a.event !== "resolved" });
      n.onclick = () => { window.focus(); window.open("alerts.html", "openvp-alerts"); n.close(); };
    } catch { }
  }

  function connect() {
    if (es || !supported() || !get() || Notification.permission !== "granted") return;
    es = new EventSource("/api/alerts/stream");
    es.addEventListener("alert", e => { try { show(JSON.parse(e.data)); } catch { } });
    es.addEventListener("status", e => { try { last = JSON.parse(e.data).status; listeners.forEach(f => f(last)); } catch { } });
    es.onopen = () => { retry = 0; };
    es.onerror = () => {
      es.close(); es = null;
      clearTimeout(timer); timer = setTimeout(connect, Math.min(30000, 1000 * 2 ** retry++));
    };
  }

  // Polls the status for the badge. Cheap: one tiny request every 5 s while the page is visible.
  async function poll() {
    if (document.hidden) return;
    try {
      const r = await fetch("/api/alerts/status");
      if (!r.ok) return;
      last = (await r.json()).status;
      listeners.forEach(f => f(last));
    } catch { }
  }

  window.openvpAlerts = {
    supported, desktop,
    enabled: () => supported() && get() && Notification.permission === "granted",
    permission: () => supported() ? Notification.permission : "unsupported",
    async enable() {
      if (!supported()) return false;
      const p = Notification.permission === "granted" ? "granted" : await Notification.requestPermission();
      if (p !== "granted") { set(false); return false; }
      set(true); connect(); return true;
    },
    disable() { set(false); if (es) { es.close(); es = null; } },
    /** Calls f with {critical, warning, info, acknowledged, muted, color} now and whenever it changes. */
    onStatus(f) { listeners.add(f); if (last) f(last); return () => listeners.delete(f); },
    /** Puts a count on an element: red for critical, amber for warnings, hidden when all is well. */
    badge(el) {
      return this.onStatus(s => {
        const n = s.critical + s.warning;
        el.hidden = n === 0;
        el.textContent = n > 99 ? "99+" : String(n);
        el.dataset.color = s.color;
        el.title = n ? `${s.critical} critical, ${s.warning} warning` : "";
      });
    }
  };
  poll(); setInterval(poll, 5000);
  document.addEventListener("visibilitychange", () => { if (!document.hidden) poll(); });
  connect();
})();
