// Theme for every console page, remembered per browser (localStorage "openvp.skin").
// Load it in <head> so the theme is set before the page paints. It sets on <html>:
//   data-skin="ios-glass" | "ios-light" | ... (openvp-skins.css has each theme's colours, fonts and shapes)
//   data-theme="light" | "dark"  (the light/dark base the theme sits on; openvp-theme.css and the pages key off it)
//   data-page="index" | "debugger" | ...  (lets the dense windows keep their compact controls)
//   class "os-glass" when the desktop app has made the window see-through for the glass theme
// Every [data-theme-toggle] button opens the Theme menu. "auto" (the default) follows the system: iOS light or iOS dark.
(() => {
  const KEY = "openvp.skin", MODE_KEY = "openvp.theme", DEFAULT = "auto";
  // "iOS glass" is kept but hidden: it ghosts text while scrolling. To bring it back into the menu, run
  // localStorage.setItem("openvp.glass", "1") in the browser console and reload.
  const GLASS_ON = (() => { try { return localStorage.getItem("openvp.glass") === "1"; } catch { return false; } })();
  const SKINS = [
    { id: "ios-glass", name: "iOS glass", group: "Apple", mode: "dark", sw: ["#0A6CFF", "#2E1065"], hidden: !GLASS_ON },
    { id: "ios-light", name: "iOS light", group: "Apple", mode: "light", sw: ["#0A6CFF", "#F2F2F7"] },
    { id: "ios-dark", name: "iOS dark", group: "Apple", mode: "dark", sw: ["#0A6CFF", "#000000"] },
    { id: "auto", name: "Match system", group: "Apple", mode: null, sw: ["#0A6CFF", "#8E8E93"] },
    { id: "android-light", name: "Android light", group: "Android", mode: "light", sw: ["#6750A4", "#FEF7FF"] },
    { id: "android-dark", name: "Android dark", group: "Android", mode: "dark", sw: ["#D0BCFF", "#141218"] },
    { id: "rack", name: "Rack", group: "Other", mode: "dark", sw: ["#FFB000", "#0F0F0E"] },
    { id: "control-room", name: "Control room", group: "Other", mode: "light", sw: ["#00658A", "#E4E4E1"] },
    { id: "scope", name: "Scope", group: "Other", mode: "dark", sw: ["#39FF88", "#090D0B"] },
    { id: "original", name: "Original dark", group: "Other", mode: "dark", sw: ["#4c8dff", "#0f1216"] },
    { id: "original-light", name: "Original light", group: "Other", mode: "light", sw: ["#1d5fd6", "#f3f5f8"] }
  ];
  const BY_ID = Object.fromEntries(SKINS.map(s => [s.id, s]));
  const SHOWN = SKINS.filter(s => !s.hidden);
  const NAMES = Object.fromEntries(SKINS.map(s => [s.id, s.name]));
  const mq = matchMedia("(prefers-color-scheme: light)");
  const root = document.documentElement;
  root.dataset.page = (location.pathname.split("/").pop() || "index.html").replace(/\.html?$/, "") || "index";

  const get = () => { try { const v = localStorage.getItem(KEY); return BY_ID[v] && !BY_ID[v].hidden ? v : DEFAULT; } catch { return DEFAULT; } };
  // The theme actually drawn: "auto" becomes iOS light or iOS dark.
  const skinOf = p => p === "auto" ? (mq.matches ? "ios-light" : "ios-dark") : p;
  const modeOf = p => BY_ID[skinOf(p)].mode;

  let menu = null, opener = null, lastGlass = null;
  // In the desktop app the glass theme makes the window itself see-through, showing the apps behind it.
  const desk = window.openvpDesktop;
  if (desk) root.classList.add("desktop-app");
  function syncWindowGlass(on) {
    root.classList.toggle("os-glass", !!(on && desk?.glassSupported));
    if (on === lastGlass || !desk?.setGlass) return;
    lastGlass = on; desk.setGlass(on);
  }

  function paint() {
    const p = get(), s = skinOf(p), m = modeOf(p);
    root.dataset.skin = s; root.dataset.theme = m; root.dataset.themePref = p;
    syncWindowGlass(s === "ios-glass");
    for (const b of document.querySelectorAll("[data-theme-toggle]")) {
      b.textContent = `◐ ${NAMES[p]} ▾`;
      b.title = `Theme: ${NAMES[p]}. Click to choose another (T on the console cycles through them).`;
      b.setAttribute("aria-haspopup", "menu");
      b.setAttribute("aria-expanded", menu && !menu.hidden && opener === b ? "true" : "false");
    }
    if (menu) for (const it of menu.querySelectorAll("button[data-skin-id]")) it.setAttribute("aria-checked", it.dataset.skinId === p ? "true" : "false");
    dispatchEvent(new CustomEvent("openvp-theme", { detail: { pref: p, theme: m, skin: s } }));
  }
  function set(p) {
    if (p === "light") p = "ios-light"; else if (p === "dark") p = "ios-dark"; // older callers
    if (!BY_ID[p]) p = DEFAULT;
    try { localStorage.setItem(KEY, p); localStorage.setItem(MODE_KEY, modeOf(p)); } catch { }
    paint();
  }

  function buildMenu() {
    menu = document.createElement("div");
    menu.className = "ovpt-menu"; menu.hidden = true;
    menu.setAttribute("role", "menu"); menu.setAttribute("aria-label", "Theme");
    let group = "";
    for (const s of SHOWN) {
      if (s.group !== group) {
        group = s.group;
        const h = document.createElement("h4"); h.textContent = group; h.setAttribute("role", "presentation"); menu.appendChild(h);
      }
      const b = document.createElement("button");
      b.type = "button"; b.dataset.skinId = s.id; b.setAttribute("role", "menuitemradio");
      const l = document.createElement("span"); l.className = "ovpt-l";
      const sw = document.createElement("span"); sw.className = "ovpt-sw"; sw.setAttribute("aria-hidden", "true");
      sw.style.background = `linear-gradient(135deg, ${s.sw[1]} 0 50%, ${s.sw[0]} 50% 100%)`;
      l.append(sw, document.createTextNode(s.name)); b.appendChild(l);
      b.addEventListener("click", () => { set(s.id); close(true); window.openvpSaved?.(); });
      menu.appendChild(b);
    }
    menu.addEventListener("keydown", e => {
      const items = [...menu.querySelectorAll("button")], i = items.indexOf(document.activeElement);
      if (e.key === "ArrowDown") { e.preventDefault(); items[(i + 1) % items.length].focus(); }
      else if (e.key === "ArrowUp") { e.preventDefault(); items[(i - 1 + items.length) % items.length].focus(); }
      else if (e.key === "Home") { e.preventDefault(); items[0].focus(); }
      else if (e.key === "End") { e.preventDefault(); items[items.length - 1].focus(); }
      else if (e.key === "Escape" || e.key === "Tab") { e.preventDefault(); close(true); }
      e.stopPropagation(); // keep page shortcuts (T, D, F…) out of the menu
    });
    document.body.appendChild(menu);
    document.addEventListener("pointerdown", e => { if (!menu.hidden && !menu.contains(e.target) && e.target !== opener) close(false); }, true);
    addEventListener("resize", () => close(false));
    addEventListener("scroll", () => place(), true);
  }
  function place() {
    if (!menu || menu.hidden || !opener) return;
    const r = opener.getBoundingClientRect(), w = menu.offsetWidth, h = menu.offsetHeight;
    let left = Math.min(r.right - w, innerWidth - w - 8); left = Math.max(8, left);
    let top = r.bottom + 6; if (top + h > innerHeight - 8) top = Math.max(8, r.top - h - 6);
    menu.style.left = left + "px"; menu.style.top = top + "px";
  }
  function open(btn) {
    if (!menu) buildMenu();
    opener = btn; menu.hidden = false; paint(); place();
    (menu.querySelector('button[aria-checked="true"]') || menu.querySelector("button")).focus();
  }
  function close(refocus) {
    if (!menu || menu.hidden) return;
    menu.hidden = true; paint();
    if (refocus) opener?.focus();
  }

  const api = {
    get, set, names: NAMES, list: SHOWN.map(s => ({ id: s.id, name: s.name, group: s.group })),
    resolve: () => modeOf(get()), skin: () => skinOf(get()),
    cycle() { const ids = SHOWN.map(s => s.id); set(ids[(ids.indexOf(get()) + 1) % ids.length]); return get(); },
    openMenu(btn) { open(btn || document.querySelector("[data-theme-toggle]")); }
  };
  window.openvpTheme = api;
  paint();
  mq.addEventListener?.("change", () => { if (get() === "auto") paint(); });
  addEventListener("storage", e => { if (e.key === KEY) paint(); });
  document.addEventListener("DOMContentLoaded", () => {
    for (const b of document.querySelectorAll("[data-theme-toggle]")) b.addEventListener("click", e => {
      e.stopPropagation();
      if (menu && !menu.hidden && opener === b) close(true); else open(b);
    });
    paint();
  });
})();
