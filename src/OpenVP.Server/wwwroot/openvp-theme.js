// Light / dark / auto theme for every console page, remembered per browser (localStorage "openvp.theme").
// Load it in <head> so the theme is set before the page paints. It sets data-theme="light"|"dark" on <html>
// (auto follows the system) and wires every [data-theme-toggle] button.
(() => {
  const KEY = "openvp.theme", ORDER = ["auto", "light", "dark"];
  const mq = matchMedia("(prefers-color-scheme: light)");
  const get = () => { try { const v = localStorage.getItem(KEY); return v === "light" || v === "dark" ? v : "auto"; } catch { return "auto"; } };
  const resolve = p => p === "auto" ? (mq.matches ? "light" : "dark") : p;
  const NAMES = { auto: "Auto", light: "Light", dark: "Dark" }, ICONS = { auto: "◐", light: "☀", dark: "☾" };
  function paint() {
    const p = get(), r = resolve(p);
    const root = document.documentElement;
    root.dataset.theme = r; root.dataset.themePref = p;
    for (const b of document.querySelectorAll("[data-theme-toggle]")) {
      b.textContent = `${ICONS[p]} ${NAMES[p]}`;
      b.title = `Theme: ${NAMES[p].toLowerCase()}${p === "auto" ? " (follows your system)" : ""}. Click to change (T on the console).`;
    }
    dispatchEvent(new CustomEvent("openvp-theme", { detail: { pref: p, theme: r } }));
  }
  function set(p) {
    if (!ORDER.includes(p)) p = "auto";
    try { localStorage.setItem(KEY, p); } catch { }
    paint();
  }
  const api = {
    get, resolve: () => resolve(get()), set, names: NAMES,
    cycle() { set(ORDER[(ORDER.indexOf(get()) + 1) % ORDER.length]); return get(); }
  };
  window.openvpTheme = api;
  paint();
  mq.addEventListener?.("change", () => { if (get() === "auto") paint(); });
  addEventListener("storage", e => { if (e.key === KEY) paint(); });
  document.addEventListener("DOMContentLoaded", () => {
    for (const b of document.querySelectorAll("[data-theme-toggle]")) b.addEventListener("click", () => { api.cycle(); window.openvpSaved?.(); });
    paint();
  });
})();
