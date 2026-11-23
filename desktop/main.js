// OpenVP desktop app: starts the OpenVP server with no console window, waits for it, and shows the web
// console in an Electron window. Quitting the app stops the server. If an OpenVP server is already running
// on the port (say, one started from the command line), the window just connects to it and leaves it running.
//
// Two modes:
//   - Packaged (OpenVP.exe): runs the server bundled in resources/server.
//   - Dev (`npm start`): runs the server from source with `dotnet watch`, serves the web pages straight from
//     src/OpenVP.Server/wwwroot, and reloads the windows when a page changes or the server restarts after a
//     C# change. Needs the .NET 8 SDK.
const { app, BrowserWindow, Menu, Tray, Notification, nativeImage, dialog, shell } = require('electron');
const { spawn, spawnSync } = require('child_process');
const fs = require('fs');
const http = require('http');
const net = require('net');
const path = require('path');
const updater = require('./updater');
const alertsStream = require('./alerts-stream');
const { makeIcon } = require('./tray-icon');

const PORT = 8080;
const BASE = `http://localhost:${PORT}/`;

const DEV = !app.isPackaged;
const SERVER_SRC = path.join(__dirname, '..', 'src', 'OpenVP.Server');
const WWWROOT_SRC = path.join(SERVER_SRC, 'wwwroot');

let server = null;        // the server process, if we started it
let quitting = false;
let mainWindow = null;
let tray = null;          // the tray icon, coloured by the alert status
let stream = null;        // the alert stream subscription (see alerts-stream.js)
let alertStatus = null;   // last { critical, warning, ..., color } from the server, or null while not connected

// Settings of the app itself (not of the OpenVP server), kept in userData/desktop.json.
const prefsPath = () => path.join(app.getPath('userData'), 'desktop.json');
const prefs = { notifications: true, closeToTray: false };
function loadPrefs() { try { Object.assign(prefs, JSON.parse(fs.readFileSync(prefsPath(), 'utf8'))); } catch { } }
function savePrefs() { try { fs.mkdirSync(path.dirname(prefsPath()), { recursive: true }); fs.writeFileSync(prefsPath(), JSON.stringify(prefs, null, 2)); } catch { } }

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (mainWindow) { if (mainWindow.isMinimized()) mainWindow.restore(); mainWindow.show(); mainWindow.focus(); }
  });
  if (process.platform === 'win32') app.setAppUserModelId('org.openvp.desktop');   // Windows needs this to show notifications
  app.whenReady().then(start);
}

const dataDir = () => path.join(app.getPath('userData'), 'data');
const logPath = () => path.join(app.getPath('userData'), 'openvp.log');

function serverExe() {
  return path.join(process.resourcesPath, 'server', process.platform === 'win32' ? 'openvp.exe' : 'openvp');
}

/** Resolves true if something on the port answers like an OpenVP server. */
function isOpenVpRunning() {
  return new Promise(resolve => {
    const req = http.get(BASE + 'api/info', { timeout: 1000 }, res => {
      let body = '';
      res.on('data', d => (body += d));
      res.on('end', () => resolve(res.statusCode === 200 && body.includes('OpenVP')));
    });
    req.on('error', () => resolve(false));
    req.on('timeout', () => { req.destroy(); resolve(false); });
  });
}

function isPortTaken() {
  return new Promise(resolve => {
    const s = net.connect(PORT, '127.0.0.1');
    s.setTimeout(500);
    s.on('connect', () => { s.destroy(); resolve(true); });
    s.on('error', () => resolve(false));
    s.on('timeout', () => { s.destroy(); resolve(false); });
  });
}

function fail(message) {
  dialog.showErrorBox('OpenVP', message);
  quitting = true;
  app.quit();
}

function startServer() {
  fs.mkdirSync(dataDir(), { recursive: true });
  const args = [`--urls=http://0.0.0.0:${PORT}`, `--OpenVP:DataDirectory=${dataDir()}`];
  if (DEV) {
    // dotnet watch rebuilds and restarts the server when C# changes; pages come straight from the source folder.
    server = spawn('dotnet', ['watch', 'run', '--project', SERVER_SRC, '--', ...args, `--webroot=${WWWROOT_SRC}`], {
      cwd: SERVER_SRC,
      windowsHide: true,
      stdio: ['ignore', 'inherit', 'inherit'],
      detached: process.platform !== 'win32',   // own process group, so the whole tree can be stopped
      env: { ...process.env, DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER: '1', DOTNET_WATCH_RESTART_ON_RUDE_EDIT: '1' },
    });
  } else {
    const exe = serverExe();
    if (!fs.existsSync(exe)) throw new Error(`Can't find the OpenVP server at ${exe}`);
    const log = fs.createWriteStream(logPath());
    server = spawn(exe, args, { cwd: app.getPath('userData'), windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    server.stdout.pipe(log);
    server.stderr.pipe(log);
  }
  server.on('exit', code => {
    server = null;
    if (!quitting) fail(`The OpenVP server stopped (exit code ${code}).` + (DEV ? '' : `\n\nSee the log for details:\n${logPath()}`));
  });
  server.on('error', e => {
    if (quitting) return;
    fail(DEV ? `Couldn't run "dotnet watch": ${e.message}\n\nDev mode needs the .NET 8 SDK.` : `The OpenVP server didn't start: ${e.message}`);
  });
}

/** Dev mode: reload every window when a page in wwwroot changes, or when the server comes back after a restart. */
function watchForChanges() {
  const reloadAll = () => BrowserWindow.getAllWindows().forEach(w => {
    if (isOpenVpUrl(w.webContents.getURL())) w.webContents.reloadIgnoringCache();
  });
  let timer = null;
  try {
    fs.watch(WWWROOT_SRC, { recursive: true }, () => { clearTimeout(timer); timer = setTimeout(reloadAll, 150); });
  } catch (e) { console.warn('Not watching wwwroot:', e.message); }

  let wasUp = true;
  setInterval(async () => {
    const up = await isOpenVpRunning();
    if (up && !wasUp) reloadAll();
    wasUp = up;
  }, 1000);
}

async function waitUntilReady(ms) {
  const until = Date.now() + ms;
  while (Date.now() < until) {
    if (await isOpenVpRunning()) return true;
    if (quitting) return false;
    await new Promise(r => setTimeout(r, 250));
  }
  return false;
}

const isOpenVpUrl = url => {
  try { const u = new URL(url); return u.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(u.hostname) && +u.port === PORT; }
  catch { return false; }
};

const POPUP_OPTIONS = { width: 1100, height: 800, autoHideMenuBar: true, backgroundColor: '#202020' };
const popups = new Map();   // window.open name -> its window, so a named window (openvp-debugger-2) is reused, as the browser does

/** The handlers every OpenVP window has: pop-ups, and keeping navigation inside OpenVP. */
function wire(win) {
  // Pop-ups on the OpenVP server (test panels, debugger, paths) get their own app window; other links open in
  // the normal browser. The pop-up is made here and the opener's request is denied, not 'allow'ed: with 'allow'
  // Electron puts a same-origin child in the opener's renderer process, so a busy debugger would freeze the
  // paths window. A window we create ourselves gets its own process. (No page uses window.opener or the
  // return value of window.open, which is null for a denied request.)
  win.webContents.setWindowOpenHandler(({ url, frameName }) => {
    if (isOpenVpUrl(url)) { openPopup(url, frameName); return { action: 'deny' }; }
    if (/^(https?|mailto):/.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  win.webContents.on('will-navigate', (e, url) => {
    if (!isOpenVpUrl(url)) { e.preventDefault(); if (/^https?:/.test(url)) shell.openExternal(url); }
  });
}

function openPopup(url, name) {
  const existing = name && popups.get(name);
  if (existing && !existing.isDestroyed()) {
    if (existing.isMinimized()) existing.restore();
    if (existing.webContents.getURL() !== url) existing.loadURL(url);
    existing.focus();
    return;
  }
  const win = new BrowserWindow({ ...POPUP_OPTIONS, title: 'OpenVP', icon: path.join(__dirname, 'build', 'icon.ico'),
    webPreferences: { contextIsolation: true, sandbox: true } });
  if (name) { popups.set(name, win); win.on('closed', () => { if (popups.get(name) === win) popups.delete(name); }); }
  wire(win);
  win.loadURL(url);
}

function createWindow(width, height) {
  const win = new BrowserWindow({
    width, height,
    title: 'OpenVP',
    backgroundColor: '#202020',
    autoHideMenuBar: true,
    icon: path.join(__dirname, 'build', 'icon.ico'),
    // The preload lets the console ask where a picked program file is, to load it with its .sig and watch it.
    webPreferences: { contextIsolation: true, sandbox: true, preload: path.join(__dirname, 'preload.js') },
  });
  wire(win);
  return win;
}

async function start() {
  loadPrefs();
  setMenu();
  mainWindow = createWindow(1280, 860);
  // With "keep running in the tray" on, closing the window only hides it; OpenVP (and its programs) keep running until Quit in the tray menu.
  mainWindow.on('close', e => { if (!quitting && prefs.closeToTray && tray) { e.preventDefault(); mainWindow.hide(); } });
  mainWindow.on('closed', () => { mainWindow = null; app.quit(); });
  createTray();
  mainWindow.loadFile(path.join(__dirname, 'loading.html'));

  if (!(await isOpenVpRunning())) {
    if (await isPortTaken())
      return fail(`Port ${PORT} is already used by another program, so OpenVP can't start.\n\nClose that program and try again.`);
    try { startServer(); } catch (e) { return fail(e.message); }
    // The first dev build can take a while.
    if (!(await waitUntilReady(DEV ? 300000 : 60000))) {
      if (!quitting) fail('The OpenVP server didn\'t start.' + (DEV ? '' : `\n\nSee the log for details:\n${logPath()}`));
      return;
    }
  }
  if (!mainWindow) return;
  mainWindow.loadURL(BASE);
  startAlerts();
  if (DEV) watchForChanges();
  else updater.start({ getWindow: () => mainWindow, beforeRestart: restartInto });
}

// ---------------------------------------------------------------- alerts: tray icon and desktop notifications

function showMainWindow() {
  if (!mainWindow) return;
  if (mainWindow.isMinimized()) mainWindow.restore();
  mainWindow.show();
  mainWindow.focus();
}

const statusText = () => {
  if (!alertStatus) return 'OpenVP: not connected';
  const { critical = 0, warning = 0 } = alertStatus;
  if (!critical && !warning) return 'OpenVP: all clear';
  return 'OpenVP: ' + [critical && `${critical} critical`, warning && `${warning} warning${warning > 1 ? 's' : ''}`].filter(Boolean).join(', ');
};

function updateTray() {
  if (!tray) return;
  const color = alertStatus ? alertStatus.color : 'grey';
  tray.setImage(nativeImage.createFromBuffer(makeIcon(color)));
  tray.setToolTip(statusText());
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: statusText(), enabled: false },
    { type: 'separator' },
    { label: 'Open console', click: showMainWindow },
    { label: 'Alerts…', click: () => openPopup(BASE + 'alerts.html', 'openvp-alerts') },
    { label: 'Metrics…', click: () => openPopup(BASE + 'metrics.html', 'openvp-metrics') },
    { type: 'separator' },
    { label: 'Show desktop notifications', type: 'checkbox', checked: prefs.notifications, click: i => { prefs.notifications = i.checked; savePrefs(); } },
    { label: 'Keep running in the tray when the window is closed', type: 'checkbox', checked: prefs.closeToTray, click: i => { prefs.closeToTray = i.checked; savePrefs(); } },
    { type: 'separator' },
    { label: 'Quit OpenVP', click: () => { quitting = true; app.quit(); } },
  ]));
}

function createTray() {
  try {
    tray = new Tray(nativeImage.createFromBuffer(makeIcon('grey')));
    tray.on('click', showMainWindow);
    tray.on('double-click', showMainWindow);
    updateTray();
  } catch (e) { console.warn('No tray icon:', e.message); tray = null; }
}

/** One native notification for an alert the server streamed. Critical ones stay until dismissed where the OS allows. */
function notify(a) {
  if (!prefs.notifications || !Notification.isSupported()) return;
  const mark = a.event === 'resolved' ? 'Resolved: ' : a.event === 'test' ? 'Test: ' : '';
  const n = new Notification({
    title: `OpenVP · ${mark}${a.title}`,
    body: a.message || '',
    urgency: a.severity === 'critical' && a.event !== 'resolved' ? 'critical' : 'normal',
    timeoutType: a.severity === 'critical' && a.event !== 'resolved' ? 'never' : 'default',
  });
  n.on('click', () => { showMainWindow(); openPopup(BASE + 'alerts.html', 'openvp-alerts'); });
  n.show();
}

function startAlerts() {
  if (stream) return;
  stream = alertsStream.subscribe({
    base: BASE,
    onAlert: notify,
    onStatus: s => { alertStatus = s; updateTray(); },
    onConnection: c => { if (!c) { alertStatus = null; updateTray(); } },
  });
}

/** After an update: stop the server, wait for the port to free up, start the new OpenVP.exe and quit. */
async function restartInto(exe) {
  stopServer();
  for (let i = 0; i < 40 && await isPortTaken(); i++) await new Promise(r => setTimeout(r, 250));
  app.releaseSingleInstanceLock();
  spawn(exe, [], { detached: true, stdio: 'ignore', cwd: path.dirname(exe) }).unref();
  app.exit(0);
}

/** The menu bar (hidden until Alt is pressed): the usual items plus Help > Check for updates. */
function setMenu() {
  Menu.setApplicationMenu(Menu.buildFromTemplate([
    { role: 'fileMenu' },
    { role: 'editMenu' },
    { role: 'viewMenu' },
    { role: 'windowMenu' },
    { label: 'Help', submenu: [
      { label: 'Check for updates…', click: () => updater.check(true) },
      { label: 'Releases page', click: () => shell.openExternal('https://github.com/qureshiumar1983-eng/openVirtualProcessor/releases') },
      { type: 'separator' },
      { label: `OpenVP v${app.getVersion()}`, enabled: false },
    ] },
  ]));
}

function stopServer() {
  quitting = true;
  if (!server) return;
  const s = server;
  server = null;
  try {
    // dotnet watch runs the server as a child process, so stop the whole tree. (This also takes down cloudflared,
    // the internet tunnel, which the server starts as its own child, so no tunnel is left running.)
    if (process.platform === 'win32') spawnSync('taskkill', ['/pid', String(s.pid), '/T', '/F'], { windowsHide: true });
    else if (DEV) process.kill(-s.pid, 'SIGKILL');   // dotnet watch ignores SIGTERM
    else s.kill();
  } catch { }
}

app.on('before-quit', () => { quitting = true; if (stream) stream.stop(); if (tray) { tray.destroy(); tray = null; } stopServer(); });
app.on('window-all-closed', () => app.quit());
process.on('exit', stopServer);
for (const sig of ['SIGINT', 'SIGTERM']) process.on(sig, () => { stopServer(); app.quit(); });
