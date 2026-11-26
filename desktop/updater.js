// Update check for the packaged desktop app (OpenVP.exe).
//
// Every push to the production branch publishes a GitHub release with a new OpenVP.exe (see
// .github/workflows/windows-app.yml). This asks GitHub's public API for the latest release, a few seconds after
// start and then every few hours. If it's newer than this app, a banner at the bottom of the console window says
// so; pressing it (or Help > Check for updates) opens a pop-up that offers to update. "Update now" downloads the new OpenVP.exe, checks its
// size (and SHA-256, when GitHub lists one), puts it in place of the current file and restarts the app.
//
// Free: the releases API needs no account or token (GitHub allows 60 unauthenticated requests an hour per IP;
// this makes a few a day). Nothing runs in dev mode (`npm start`).
//
// Swapping the file: the portable OpenVP.exe is a launcher that keeps its own file open for as long as the app
// runs, so Windows refuses to overwrite it and to rename it (EBUSY). The download is therefore saved next to it
// as OpenVP.exe.download, and a small helper script, started just before the app quits, waits for the file to be
// released, moves the download into place and starts it. If the folder can't be written (say, OpenVP.exe sits in Program Files), the new version is saved to
// Downloads instead and shown in Explorer.
const { app, dialog, ipcMain, net, shell } = require('electron');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { Readable } = require('stream');
const { pipeline } = require('stream/promises');
const { spawn } = require('child_process');

const REPO = 'qureshiumar1983-eng/openVirtualProcessor';
const LATEST_API = `https://api.github.com/repos/${REPO}/releases/latest`;
const RELEASES_PAGE = `https://github.com/${REPO}/releases/latest`;
const ASSET_NAME = 'OpenVP.exe';
const FIRST_CHECK_MS = 15 * 1000;
const CHECK_EVERY_MS = 6 * 60 * 60 * 1000;

let opts = null;          // { getWindow, beforeRestart }
let busy = false;         // a check, prompt or download is in progress
let timer = null;
let pending = null;       // { latest, current }: a newer release the banner is offering
let hiddenTag = null;     // the release whose banner was closed (until the app restarts)

const settingsPath = () => path.join(app.getPath('userData'), 'update.json');
function loadSettings() {
  try { return JSON.parse(fs.readFileSync(settingsPath(), 'utf8')); } catch { return {}; }
}
function saveSettings(s) {
  try { fs.writeFileSync(settingsPath(), JSON.stringify(s, null, 2)); } catch { }
}

/** "v0.1.15" -> [0, 1, 15]. Returns null if it isn't a version number. */
function parseVersion(v) {
  const m = /^v?(\d+)\.(\d+)\.(\d+)/.exec(String(v || '').trim());
  return m ? m.slice(1).map(Number) : null;
}
function isNewer(latest, current) {
  const a = parseVersion(latest), b = parseVersion(current);
  if (!a || !b) return false;
  for (let i = 0; i < 3; i++) if (a[i] !== b[i]) return a[i] > b[i];
  return false;
}

/** The portable OpenVP.exe the user launched (electron-builder's portable launcher sets this). */
const portableExe = () => process.env.PORTABLE_EXECUTABLE_FILE || null;

async function fetchLatest() {
  const res = await net.fetch(LATEST_API, {
    headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'OpenVP-desktop-updater' },
  });
  if (!res.ok) throw new Error(`GitHub answered ${res.status}`);
  const rel = await res.json();
  const asset = (rel.assets || []).find(a => a.name === ASSET_NAME);
  return { tag: rel.tag_name, name: rel.name || rel.tag_name, notes: rel.body || '', url: rel.html_url, asset };
}

/**
 * Checks for a newer release. `manual` (a user asked) also reports "you're up to date" and errors, and shows a
 * version the user chose to skip.
 */
async function check(manual = false) {
  if (!app.isPackaged) {
    if (manual) dialog.showMessageBox(win(), { type: 'info', title: 'OpenVP', message: 'Updates are checked only in the packaged OpenVP.exe, not in dev mode.' });
    return;
  }
  if (busy) return;
  busy = true;
  try {
    let latest;
    try { latest = await fetchLatest(); }
    catch (e) {
      if (manual) dialog.showMessageBox(win(), { type: 'warning', title: 'OpenVP', message: "Couldn't check for updates.", detail: e.message });
      return;
    }
    const current = app.getVersion();
    if (!isNewer(latest.tag, current)) {
      pending = null; hideBanner();
      if (manual) dialog.showMessageBox(win(), { type: 'info', title: 'OpenVP', message: `You have the latest version (v${current}).` });
      return;
    }
    const settings = loadSettings();
    if (!manual && settings.skipVersion === latest.tag) return;
    pending = { latest, current };
    if (manual) await prompt(latest, current, settings);
    else showBanner();
  } finally {
    busy = false;
  }
}

function win() {
  const w = opts && opts.getWindow();
  return w && !w.isDestroyed() ? w : undefined;
}

async function prompt(latest, current, settings) {
  const canSwap = !!(portableExe() && latest.asset);
  const notes = latest.notes.replace(/\*\*/g, '').trim();
  const { response } = await dialog.showMessageBox(win(), {
    type: 'info',
    title: 'OpenVP update',
    message: `A new version of OpenVP is available: ${latest.tag}`,
    detail: `You have v${current}.\n\n${notes}\n\n` +
      (canSwap ? 'Updating downloads it and restarts OpenVP, which stops the programs that are running for a few seconds.'
               : 'This opens the download page.'),
    buttons: [canSwap ? 'Update now' : 'Download', 'Later', 'Skip this version'],
    defaultId: 0,
    cancelId: 1,
    noLink: true,
  });
  if (response === 2) { saveSettings({ ...settings, skipVersion: latest.tag }); pending = null; hideBanner(); return; }
  if (response !== 0) return;
  if (!canSwap) { shell.openExternal(latest.url || RELEASES_PAGE); return; }
  await downloadAndInstall(latest);
}

const BANNER_ID = 'openvp-update-banner';

/** Runs in the console page: draws the banner. Styles are set from script, so a page's CSP can't block them. */
function bannerScript(id, tag, current) {
  const api = window.openvpDesktop;
  if (!api || !api.updateNow || !document.body) return;
  const old = document.getElementById(id);
  if (old) old.remove();
  const bar = document.createElement('div');
  bar.id = id;
  bar.setAttribute('role', 'status');
  bar.style.cssText = 'position:fixed;left:50%;bottom:14px;transform:translateX(-50%);z-index:2147483647;display:flex;' +
    'align-items:center;gap:10px;max-width:calc(100vw - 28px);box-sizing:border-box;padding:8px 8px 8px 14px;' +
    'border-radius:8px;background:#1f6feb;color:#fff;font:13px/1.3 system-ui,"Segoe UI",sans-serif;' +
    'box-shadow:0 4px 16px rgba(0,0,0,.4)';
  const text = document.createElement('span');
  text.textContent = `OpenVP ${tag} is available (you have v${current}).`;
  const go = document.createElement('button');
  go.type = 'button';
  go.textContent = 'Update…';
  go.style.cssText = 'cursor:pointer;border:0;border-radius:5px;padding:5px 12px;background:#fff;color:#0b3d91;font:inherit;font-weight:600';
  go.addEventListener('click', () => api.updateNow());
  const close = document.createElement('button');
  close.type = 'button';
  close.textContent = '\u00d7';
  close.title = 'Hide until OpenVP is restarted';
  close.setAttribute('aria-label', close.title);
  close.style.cssText = 'cursor:pointer;border:0;background:transparent;color:#fff;font:inherit;font-size:18px;line-height:1;padding:2px 6px';
  close.addEventListener('click', () => { bar.remove(); api.hideUpdate(); });
  bar.append(text, go, close);
  document.body.appendChild(bar);
}

/** Console pages only (the loading screen and error pages have nothing to press). */
function bannerTarget() {
  const w = win();
  if (!w) return null;
  const url = w.webContents.getURL();
  return opts.isConsoleUrl && !opts.isConsoleUrl(url) ? null : w.webContents;
}

function showBanner() {
  if (!pending || hiddenTag === pending.latest.tag) return;
  const wc = bannerTarget();
  if (!wc) return;
  const args = [BANNER_ID, pending.latest.tag, pending.current].map(a => JSON.stringify(a)).join(',');
  wc.executeJavaScript(`(${bannerScript})(${args})`, true).catch(() => { });
}

function hideBanner() {
  const wc = bannerTarget();
  if (wc) wc.executeJavaScript(`(() => { const b = document.getElementById(${JSON.stringify(BANNER_ID)}); if (b) b.remove(); })()`, true).catch(() => { });
}

/** The banner was pressed: the same pop-up as Help > Check for updates, with the notes and the choices. */
async function bannerPressed() {
  if (!pending || busy) return;
  busy = true;
  try { await prompt(pending.latest, pending.current, loadSettings()); }
  finally { busy = false; }
}

async function download(asset, dest, onProgress) {
  const res = await net.fetch(asset.browser_download_url, { headers: { 'User-Agent': 'OpenVP-desktop-updater' } });
  if (!res.ok || !res.body) throw new Error(`Download failed (${res.status})`);
  const total = asset.size || Number(res.headers.get('content-length')) || 0;
  const hash = crypto.createHash('sha256');
  let got = 0;
  const body = Readable.fromWeb(res.body);
  body.on('data', chunk => { got += chunk.length; hash.update(chunk); if (total) onProgress(got / total); });
  await pipeline(body, fs.createWriteStream(dest));
  if (asset.size && got !== asset.size) throw new Error(`The download is incomplete (${got} of ${asset.size} bytes).`);
  const digest = typeof asset.digest === 'string' && asset.digest.startsWith('sha256:') ? asset.digest.slice(7) : null;
  if (digest && hash.digest('hex') !== digest.toLowerCase()) throw new Error("The download doesn't match the release (SHA-256 differs).");
}

async function downloadAndInstall(latest) {
  const exe = portableExe();
  const dir = path.dirname(exe);
  const w = win();
  const progress = p => { if (w && !w.isDestroyed()) w.setProgressBar(p); };
  const tmp = path.join(dir, `${ASSET_NAME}.download`);
  let dest = tmp, inPlace = true;
  try {
    try { fs.accessSync(dir, fs.constants.W_OK); } catch { inPlace = false; }
    if (!inPlace) dest = path.join(app.getPath('downloads'), `OpenVP-${latest.tag}.exe`);
    progress(0.01);
    await download(latest.asset, dest, progress);
  } catch (e) {
    try { fs.rmSync(dest, { force: true }); } catch { }
    progress(-1);
    dialog.showMessageBox(win(), { type: 'error', title: 'OpenVP update', message: "The update couldn't be downloaded.", detail: e.message });
    return;
  }
  progress(-1);

  if (!inPlace) {
    shell.showItemInFolder(dest);
    dialog.showMessageBox(win(), { type: 'info', title: 'OpenVP update',
      message: `OpenVP ${latest.tag} was saved to your Downloads folder.`,
      detail: `OpenVP can't replace itself in ${dir}. Close OpenVP and run the new file instead.` });
    return;
  }

  // The swap happens after this app has quit (see the top of this file).
  let launch;
  try { launch = swapHelper(exe, tmp); }
  catch (e) {
    try { fs.rmSync(tmp, { force: true }); } catch { }
    dialog.showMessageBox(win(), { type: 'error', title: 'OpenVP update', message: "The update couldn't be installed.", detail: e.message });
    return;
  }
  await opts.beforeRestart(launch);
}

/**
 * Writes the helper script and returns a function that starts it. The script tries for a minute to move
 * `download` over `exe` (it succeeds once the old launcher has exited and let go of the file), then starts `exe`:
 * the new version, or the old one again if the move never worked. The paths go in through the environment, so
 * odd characters in a folder name can't break the script.
 */
function swapHelper(exe, download) {
  const script = path.join(os.tmpdir(), `openvp-update-${process.pid}.cmd`);
  fs.writeFileSync(script, [
    '@echo off',
    'for /l %%i in (1,1,60) do (',
    '  move /y "%OPENVP_UPDATE_NEW%" "%OPENVP_UPDATE_EXE%" >nul 2>&1 && goto run',
    '  ping -n 2 127.0.0.1 >nul',
    ')',
    ':run',
    'start "" "%OPENVP_UPDATE_EXE%"',
    '(goto) 2>nul & del "%~f0"',
    '',
  ].join('\r\n'));
  return () => spawn(process.env.ComSpec || 'cmd.exe', ['/d', '/c', script], {
    detached: true, stdio: 'ignore', windowsHide: true, cwd: path.dirname(exe),
    env: { ...process.env, OPENVP_UPDATE_EXE: exe, OPENVP_UPDATE_NEW: download },
  }).unref();
}

/** Deletes what an earlier update left behind: a download that was never moved into place, or an old .old file. */
function cleanUp() {
  const exe = portableExe();
  if (!exe) return;
  for (const f of [exe + '.old', path.join(path.dirname(exe), `${ASSET_NAME}.download`)]) {
    try { fs.rmSync(f, { force: true }); } catch { }
  }
}

/**
 * Starts checking. `getWindow` returns the window to attach pop-ups and the banner to; `isConsoleUrl(url)` says
 * whether a page is OpenVP's console (the banner is shown only there); `beforeRestart(launch)` stops the
 * server, calls `launch()` (which starts the helper that swaps the file and starts the new version) and quits.
 */
function start(options) {
  if (!app.isPackaged || timer) return;
  opts = options;
  cleanUp();
  // Only the main window's own page may ask; the banner is redrawn after each page change.
  const fromWindow = e => { const w = win(); return !!w && e.sender === w.webContents; };
  ipcMain.on('openvp:update-now', e => { if (fromWindow(e)) bannerPressed(); });
  ipcMain.on('openvp:update-hide', e => { if (fromWindow(e) && pending) hiddenTag = pending.latest.tag; });
  const w = win();
  if (w) w.webContents.on('did-finish-load', showBanner);
  setTimeout(() => check(false), FIRST_CHECK_MS);
  timer = setInterval(() => check(false), CHECK_EVERY_MS);
}

module.exports = { start, check, isNewer, parseVersion, bannerScript };
