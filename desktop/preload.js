// Runs in the OpenVP console window before the page. A web page only ever gets a picked file's contents; the
// desktop app can also say where the file is on disk, so OpenVP can bring its .sig along and reload the program
// when it changes. The page can also tell the app that the "update available" banner (which the app draws in the
// page, see updater.js) was pressed or closed. Nothing else is exposed to the page.
const { contextBridge, ipcRenderer, webUtils } = require('electron');

contextBridge.exposeInMainWorld('openvpDesktop', {
  /** Full path of a File from an <input type=file> or a drop, or '' if it has none. */
  pathForFile: file => { try { return webUtils.getPathForFile(file) || ''; } catch { return ''; } },
  /** The update banner was pressed: opens the app's update pop-up (it still asks before updating). */
  updateNow: () => ipcRenderer.send('openvp:update-now'),
  /** The update banner was closed: don't show it again until the app restarts. */
  hideUpdate: () => ipcRenderer.send('openvp:update-hide'),
});
