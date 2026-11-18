// OpenVP desktop app: starts the OpenVP server (bundled in resources/server) with no console window,
// waits for it, and shows the web console in an Electron window. Quitting the app stops the server.
// If an OpenVP server is already running on the port (say, one started from the command line), the
// window just connects to it and leaves it running.
const { app, BrowserWindow, dialog, shell } = require('electron');
const { spawn } = require('child_process');
const fs = require('fs');
const http = require('http');
const net = require('net');
const path = require('path');

const PORT = 8080;
const BASE = `http://localhost:${PORT}/`;

let server = null;        // the server process, if we started it
let quitting = false;
let mainWindow = null;

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (mainWindow) { if (mainWindow.isMinimized()) mainWindow.restore(); mainWindow.focus(); }
  });
  app.whenReady().then(start);
}

const dataDir = () => path.join(app.getPath('userData'), 'data');
const logPath = () => path.join(app.getPath('userData'), 'openvp.log');

function serverExe() {
  const base = app.isPackaged ? path.join(process.resourcesPath, 'server') : path.join(__dirname, 'server');
  return path.join(base, process.platform === 'win32' ? 'openvp.exe' : 'openvp');
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
  const exe = serverExe();
  if (!fs.existsSync(exe)) throw new Error(`Can't find the OpenVP server at ${exe}`);
  fs.mkdirSync(dataDir(), { recursive: true });
  const log = fs.createWriteStream(logPath());
  server = spawn(exe, [`--urls=http://0.0.0.0:${PORT}`, `--OpenVP:DataDirectory=${dataDir()}`], {
    cwd: app.getPath('userData'),
    windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  server.stdout.pipe(log);
  server.stderr.pipe(log);
  server.on('exit', code => {
    server = null;
    if (!quitting) fail(`The OpenVP server stopped (exit code ${code}).\n\nSee the log for details:\n${logPath()}`);
  });
  server.on('error', e => { if (!quitting) fail(`The OpenVP server didn't start: ${e.message}`); });
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

function createWindow(width, height) {
  const win = new BrowserWindow({
    width, height,
    title: 'OpenVP',
    backgroundColor: '#202020',
    autoHideMenuBar: true,
    icon: path.join(__dirname, 'build', 'icon.ico'),
    webPreferences: { contextIsolation: true, sandbox: true },
  });
  // Pop-ups on the OpenVP server (test panels, debugger) get their own app window; other links open in
  // the normal browser.
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (isOpenVpUrl(url))
      return { action: 'allow', overrideBrowserWindowOptions: { width: 1100, height: 800, autoHideMenuBar: true, backgroundColor: '#202020' } };
    if (/^(https?|mailto):/.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  win.webContents.on('will-navigate', (e, url) => {
    if (!isOpenVpUrl(url)) { e.preventDefault(); if (/^https?:/.test(url)) shell.openExternal(url); }
  });
  return win;
}

async function start() {
  mainWindow = createWindow(1280, 860);
  mainWindow.on('closed', () => { mainWindow = null; app.quit(); });
  mainWindow.loadFile(path.join(__dirname, 'loading.html'));

  if (!(await isOpenVpRunning())) {
    if (await isPortTaken())
      return fail(`Port ${PORT} is already used by another program, so OpenVP can't start.\n\nClose that program and try again.`);
    try { startServer(); } catch (e) { return fail(e.message); }
    if (!(await waitUntilReady(60000))) {
      if (!quitting) fail(`The OpenVP server didn't start.\n\nSee the log for details:\n${logPath()}`);
      return;
    }
  }
  if (mainWindow) mainWindow.loadURL(BASE);
}

function stopServer() {
  quitting = true;
  if (!server) return;
  const s = server;
  server = null;
  try { s.kill(); } catch { }
}

app.on('before-quit', stopServer);
app.on('window-all-closed', () => app.quit());
process.on('exit', stopServer);
