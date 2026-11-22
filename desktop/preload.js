// Runs in the OpenVP console window before the page. A web page only ever gets a picked file's contents; the
// desktop app can also say where the file is on disk, so OpenVP can bring its .sig along and reload the program
// when it changes. That's the only thing exposed to the page.
const { contextBridge, webUtils } = require('electron');

contextBridge.exposeInMainWorld('openvpDesktop', {
  /** Full path of a File from an <input type=file> or a drop, or '' if it has none. */
  pathForFile: file => { try { return webUtils.getPathForFile(file) || ''; } catch { return ''; } },
});
