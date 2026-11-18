// Shared by the console and debugger: asks for the access key when the server answers 401.
(() => {
  let shown = false;
  function show(message) {
    if (shown) return; shown = true;
    const wrap = document.createElement("div");
    wrap.style.cssText = "position:fixed;inset:0;z-index:100;display:grid;place-items:center;background:rgba(5,8,12,.78);padding:16px";
    wrap.innerHTML = `
      <form style="width:min(420px,100%);background:#181d23;border:1px solid #29303a;border-radius:10px;padding:18px;color:#e6e9ee;font:14px/1.45 system-ui,sans-serif">
        <div style="font-size:16px;font-weight:600;margin-bottom:6px">OpenVP access key</div>
        <div style="color:#8b95a3;margin-bottom:12px">The server printed the key when it started. It's also saved in <code>data/apikey.txt</code>.</div>
        <input name="key" type="password" autocomplete="current-password" placeholder="access key" required
          style="width:100%;box-sizing:border-box;font:inherit;color:inherit;background:#111519;border:1px solid #29303a;border-radius:6px;padding:7px 9px">
        <div data-err style="color:#e05252;min-height:1.4em;margin:6px 0"></div>
        <button style="font:inherit;color:inherit;background:#1f2a3a;border:1px solid #4c8dff;border-radius:6px;padding:6px 14px;cursor:pointer">Log in</button>
      </form>`;
    document.body.appendChild(wrap);
    const form = wrap.querySelector("form"), err = wrap.querySelector("[data-err]");
    if (message) err.textContent = message;
    form.key.focus();
    form.onsubmit = async e => {
      e.preventDefault();
      const r = await fetch("/api/login", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ key: form.key.value }) });
      if (r.ok) location.reload(); else { err.textContent = "Wrong key."; form.key.select(); }
    };
  }
  // Calls made before logging in fail with this error on purpose; the login box already explains it.
  addEventListener("unhandledrejection", e => { if (/access key/.test(e.reason?.message || "")) e.preventDefault(); });
  window.openvpAuth = {
    // Call with a fetch Response; shows the login box and returns true if it was a 401.
    check(r) { if (r.status === 401) { show(); return true; } return false; },
    async init() {
      if (new URLSearchParams(location.search).has("badkey")) show("That key didn't match.");
      try { const i = await (await fetch("/api/info")).json(); if (i.auth && !i.authenticated) show(); } catch { }
    }
  };
})();
