// Small helpers shared by the Backups, Versions, Schedules and Push pages.
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const pad = (n, w = 2) => String(n).padStart(w, "0");
const num = n => Number(n || 0).toLocaleString();
const fmtSize = b => b == null ? "" : b < 1024 ? `${b} B` : b < 1048576 ? `${(b / 1024).toFixed(1)} KB` : b < 1073741824 ? `${(b / 1048576).toFixed(1)} MB` : `${(b / 1073741824).toFixed(2)} GB`;
const when = t => { const d = new Date(t); return isNaN(d) ? "" : `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`; };
const ago = t => { if (!t) return "never"; const s = Math.max(0, (Date.now() - new Date(t)) / 1000); return s < 2 ? "just now" : s < 90 ? `${Math.round(s)} s ago` : s < 5400 ? `${Math.round(s / 60)} min ago` : s < 129600 ? `${Math.round(s / 3600)} h ago` : `${Math.round(s / 86400)} d ago`; };

async function api(path, method = "GET", body, raw) {
  const r = await fetch(path, { method, headers: body === undefined || body instanceof FormData ? {} : { "content-type": "application/json" }, body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body) });
  if (openvpAuth.check(r)) throw new Error("Log in with the access key first.");
  if (raw && r.ok) return r;
  const ct = r.headers.get("content-type") || "";
  const data = ct.includes("json") ? await r.json() : await r.text();
  if (!r.ok) { const e = new Error((data && data.error) || data || r.statusText); e.data = data; e.status = r.status; throw e; }
  return data;
}

let _failT = 0, _savedT = 0;
function fail(msg) { const b = $("#fail"); if (!b) return alert(msg); b.textContent = msg; b.hidden = false; clearTimeout(_failT); _failT = setTimeout(() => b.hidden = true, 12000); }
function flash(t = "Saved") { const el = $("#saved"); if (!el) return; el.textContent = t; clearTimeout(_savedT); _savedT = setTimeout(() => el.textContent = "", 2500); }
function pill(kind, text, title) { return `<span class="pill ${kind}"${title ? ` title="${esc(title)}"` : ""}>${esc(text)}</span>`; }

// A yes/no question in a dialog (the page has <dialog id="cf-dlg"> with #cf-title, #cf-text, #cf-ok).
function confirmBox(title, text, ok = "OK", danger = false) {
  return new Promise(res => {
    const d = $("#cf-dlg"); $("#cf-title").textContent = title; $("#cf-text").textContent = text; const b = $("#cf-ok"); b.textContent = ok; b.classList.toggle("danger", danger);
    d.onclose = () => res(d.returnValue === "ok"); d.returnValue = ""; d.showModal();
  });
}
// One line of text in a dialog (<dialog id="in-dlg"> with #in-title, #in-label, #in-text, #in-ok).
function askText(title, label, value = "", ok = "Save", multiline = false) {
  return new Promise(res => {
    const d = $("#in-dlg"); $("#in-title").textContent = title; $("#in-label").textContent = label; const t = $("#in-text"); t.value = value; $("#in-ok").textContent = ok;
    d.onclose = () => res(d.returnValue === "ok" ? t.value : null); d.returnValue = ""; d.showModal(); t.focus(); t.select();
  });
}
function wireDialogs() {
  for (const d of $$("dialog")) {
    d.addEventListener("click", e => { if (e.target === d) d.close("cancel"); });
    for (const b of d.querySelectorAll("[data-close]")) b.onclick = () => d.close("cancel");
  }
  const cf = $("#cf-dlg"); if (cf) $("#cf-form").onsubmit = () => { cf.returnValue = "ok"; };
  const inn = $("#in-dlg"); if (inn) $("#in-form").onsubmit = () => { inn.returnValue = "ok"; };
}
function slotLabel(s) { return `Slot ${s.number}${s.name ? " · " + s.name : ""}${s.program ? " · " + s.program : ""}`; }
async function downloadBlob(r, fallback) {
  const cd = r.headers.get("content-disposition") || "";
  const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd);
  const blob = await r.blob();
  const a = document.createElement("a"); a.href = URL.createObjectURL(blob); a.download = m ? decodeURIComponent(m[1]) : fallback; document.body.appendChild(a); a.click();
  setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  return blob.size;
}
// The report from POST /api/slots/{n}/versions/diff as HTML.
function diffHtml(r) {
  if (r.identical) return `<p>${esc(r.a)} and ${esc(r.b)} have no differences.</p>` + (r.notes || []).map(n => `<div class="note">${esc(n)}</div>`).join("");
  let h = `<p class="muted">Comparing <b>${esc(r.a)}</b> (before) with <b>${esc(r.b)}</b> (after)</p><p>${esc((r.summary || []).join(" "))}</p>`;
  for (const s of r.sections) {
    if (!s.items.length) continue;
    h += `<h3>${esc(s.title)} <span class="muted">· ${s.added} added, ${s.removed} removed, ${s.changed} changed</span></h3><ul>`;
    for (const i of s.items) h += `<li class="${esc(i.change)}"><span class="tag">${i.change === "added" ? "+" : i.change === "removed" ? "−" : "~"}</span> ${esc(i.name)}${i.detail ? ` <span class="detail">— ${esc(i.detail)}</span>` : ""}</li>`;
    h += "</ul>";
  }
  return h + (r.notes || []).map(n => `<div class="note" style="margin-top:8px">${esc(n)}</div>`).join("");
}
document.addEventListener("DOMContentLoaded", () => { wireDialogs(); });
