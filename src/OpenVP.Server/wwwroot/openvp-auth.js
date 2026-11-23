// Shared by every console page. Finds out who you are (GET /api/info), shows the sign-in when the server answers 401, hides or disables what your
// account can't do ([data-perm="LoadProgram"] and so on; the server checks every request anyway) and draws the "who am I" chip into #who-chip.
//   - No users yet: the access-key box, as always.
//   - Users exist: you are sent to login.html (and back to this page afterwards).
(() => {
  let shown = false, infoP = null;
  const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const loadInfo = (fresh) => (infoP && !fresh) ? infoP : (infoP = fetch("/api/info", { cache: "no-store" }).then(r => r.json()).catch(() => null));

  function keyBox(message) {
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
      if (r.ok) location.reload(); else { const j = await r.json().catch(() => ({})); err.textContent = r.status === 429 ? (j.error || "Too many tries.") : "Wrong key."; form.key.select(); }
    };
  }

  async function needLogin(message, expired) {
    const i = await loadInfo(true);
    if (i?.users && !/login\.html$/.test(location.pathname)) {
      location.href = "/login.html?next=" + encodeURIComponent(location.pathname + location.search + location.hash) + (expired ? "&expired=1" : "");
      return;
    }
    keyBox(message);
  }

  const css = document.createElement("style");
  css.textContent = `.who-chip{display:inline-flex;align-items:center;gap:6px;font-size:12px;flex-wrap:wrap}
  .who-chip .av{width:22px;height:22px;border-radius:50%;background:var(--active,#1f2a3a);border:1px solid var(--accent,#4c8dff);display:inline-grid;place-items:center;font-size:10px;font-weight:700;color:var(--text,#e6e9ee)}
  .who-chip button,.who-chip a{font-size:12px;padding:2px 8px;font:inherit;font-size:12px}
  .who-chip .role{color:var(--muted,#8b95a3)}
  dialog.pw-dlg{background:var(--card,#181d23);color:var(--text,#e6e9ee);border:1px solid var(--line,#29303a);border-radius:10px;padding:16px;width:min(420px,calc(100vw - 24px))}
  dialog.pw-dlg::backdrop{background:var(--overlay,#0008)}
  dialog.pw-dlg form{display:flex;flex-direction:column;gap:10px;margin:0}
  dialog.pw-dlg label{display:flex;flex-direction:column;gap:3px;font-size:12px;color:var(--muted,#8b95a3)}
  dialog.pw-dlg input{font:inherit;color:var(--text,#e6e9ee);background:var(--input,#111519);border:1px solid var(--line,#29303a);border-radius:6px;padding:5px 9px}
  dialog.pw-dlg .row{display:flex;gap:8px;justify-content:flex-end}
  dialog.pw-dlg button{font:inherit;color:var(--text,#e6e9ee);background:var(--input,#111519);border:1px solid var(--line,#29303a);border-radius:6px;padding:5px 12px;cursor:pointer}
  dialog.pw-dlg button.primary{border-color:var(--accent,#4c8dff);background:var(--active,#1f2a3a)}
  dialog.pw-dlg .err{color:var(--err-fg,#ff9b9b);min-height:1.3em;font-size:13px}
  [data-perm-off]{opacity:.45;cursor:not-allowed}`;
  document.head.appendChild(css);

  function passwordDialog() {
    let d = document.getElementById("pw-dlg");
    if (!d) {
      d = document.createElement("dialog"); d.id = "pw-dlg"; d.className = "pw-dlg"; d.setAttribute("aria-labelledby", "pw-title");
      d.innerHTML = `<form method="dialog" autocomplete="off"><h2 id="pw-title" style="margin:0;font-size:16px">Change your password</h2>
        <label>Current password<input name="cur" type="password" autocomplete="current-password" required></label>
        <label>New password (at least 8 characters)<input name="neu" type="password" autocomplete="new-password" required minlength="8"></label>
        <label>New password again<input name="neu2" type="password" autocomplete="new-password" required minlength="8"></label>
        <div class="err" role="alert"></div>
        <div class="row"><button type="button" data-x>Cancel</button><button class="primary" value="ok">Change password</button></div></form>`;
      document.body.appendChild(d);
      d.querySelector("[data-x]").onclick = () => d.close();
      d.addEventListener("click", e => { if (e.target === d) d.close(); });
      d.querySelector("form").onsubmit = async e => {
        e.preventDefault();
        const f = e.target, err = d.querySelector(".err");
        if (f.neu.value !== f.neu2.value) { err.textContent = "The two new passwords don't match."; return; }
        const r = await fetch("/api/me/password", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ current: f.cur.value, new: f.neu.value }) });
        const j = await r.json().catch(() => ({}));
        if (r.ok) { f.reset(); err.textContent = ""; d.close(); alert("Your password was changed. You were signed out on your other browsers."); } else err.textContent = j.error || "Couldn't change the password.";
      };
    }
    d.querySelector(".err").textContent = "";
    d.showModal();
  }

  function drawChip(i) {
    const el = document.getElementById("who-chip");
    if (!el || !i?.me) return;
    const m = i.me;
    let h = "";
    if (m.kind === "user") {
      const ini = (m.name || "?").split(/[\s._@-]+/).filter(Boolean).slice(0, 2).map(x => x[0]).join("").toUpperCase();
      h = `<span class="av" aria-hidden="true">${esc(ini)}</span><b>${esc(m.name)}</b><span class="role">${esc(m.role)}</span>`
        + (m.canChangePassword ? `<button type="button" data-pw title="Change your own password">Password…</button>` : "")
        + `<button type="button" data-out title="Sign out of OpenVP in this browser">Sign out</button>`;
    } else if (m.kind === "key") {
      h = `<span class="av" aria-hidden="true">K</span><b>Access key</b><span class="role">admin</span><button type="button" data-out>Sign out</button>`;
    } else if (i.users && m.kind === "local") {
      h = `<span class="role" title="Requests from this computer need no login (Security settings)">this computer</span><a href="/login.html?next=${encodeURIComponent(location.pathname)}">Sign in…</a>`;
    } else if (m.kind === "anonymous") {
      h = `<a href="/login.html?next=${encodeURIComponent(location.pathname)}">Sign in</a>`;
    }
    el.className = "who-chip"; el.innerHTML = h;
    el.querySelector("[data-pw]")?.addEventListener("click", passwordDialog);
    el.querySelector("[data-out]")?.addEventListener("click", async () => { await fetch("/api/logout", { method: "POST" }); location.href = i.users ? "/login.html" : "/"; });
  }

  const api = {
    me: null, info: null,
    // True if this account may do it (the server decides; before the page knows who you are, everything shows).
    can(perm) { return !this.me || !this.me.perms || this.me.perms.includes(perm); },
    canSlot(n) { return !this.me || !this.me.slots || this.me.slots.includes(n); },
    // Call with a fetch Response; shows the sign-in and returns true if it was a 401.
    check(r) { if (r.status === 401) { needLogin(undefined, !!this.me?.authenticated); return true; } return false; },
    // Disables or hides every [data-perm] element the account can't use. data-perm="A,B" needs any of them; data-perm-hide hides instead of dimming.
    apply(root = document) {
      for (const el of root.querySelectorAll("[data-perm]")) {
        const ok = el.dataset.perm.split(",").some(p => this.can(p.trim()));
        if (el.hasAttribute("data-perm-hide")) { el.hidden = !ok; continue; }
        if (!ok) {
          if (!el.hasAttribute("data-perm-off")) { el.dataset.permTitle = el.title || ""; el.setAttribute("data-perm-off", ""); }
          el.title = el.dataset.permWhy || "Your account isn't allowed to do this";
          if ("disabled" in el) el.disabled = true;
        } else if (el.hasAttribute("data-perm-off")) {          // only undo what this function did; the page's own disabling is left alone
          el.removeAttribute("data-perm-off"); el.title = el.dataset.permTitle || ""; if ("disabled" in el) el.disabled = false;
        }
      }
    },
    async init() {
      if (new URLSearchParams(location.search).has("badkey")) keyBox("That key didn't match.");
      const i = await loadInfo(true);
      if (!i) return;
      this.info = i; this.me = i.me;
      if (i.auth && !i.authenticated) { needLogin(); return; }
      drawChip(i); this.apply();
      document.dispatchEvent(new CustomEvent("openvp-me", { detail: i }));
    },
    refreshChip() { loadInfo(true).then(i => { if (i) { this.info = i; this.me = i.me; drawChip(i); this.apply(); } }); },
    initials(name) { return String(name || "?").split(/[\s._@-]+/).filter(Boolean).slice(0, 2).map(x => x[0]).join("").toUpperCase() || "?"; },
  };
  // Calls made before logging in fail with this error on purpose; the login box already explains it.
  addEventListener("unhandledrejection", e => { if (/access key|Sign in/.test(e.reason?.message || "")) e.preventDefault(); });
  window.openvpAuth = api;
})();
