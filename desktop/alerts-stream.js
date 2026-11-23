// Listens to the OpenVP server's alert stream (server-sent events at /api/alerts/stream) from the desktop app's main process.
//
// It is the main process, not the page, that listens, so notifications and the tray colour keep working when the console window is closed
// to the tray, and nothing has to be added to preload.js. The server only streams to local requests that need no access key (this app talks to
// localhost), and sends a `status` event first, then `alert` events as they are raised, resolved or tested, and a keep-alive comment every 15 s.
//
// subscribe() reconnects by itself (1 s, 2 s, 4 s ... up to 30 s) when the server restarts or goes away, and says so through onConnection.
const http = require('http');

/** Turns a stream of text chunks into SSE events: { event, data } (data parsed as JSON when it is JSON). Comments (": keepalive") are ignored. */
class SseParser {
  constructor(onEvent) { this.buf = ''; this.onEvent = onEvent; }
  push(chunk) {
    this.buf += chunk.toString('utf8');
    let i;
    // an event ends with a blank line (\n\n, or \r\n\r\n)
    while ((i = this.buf.search(/\r?\n\r?\n/)) >= 0) {
      const raw = this.buf.slice(0, i);
      this.buf = this.buf.slice(i).replace(/^\r?\n\r?\n/, '');
      let event = 'message'; const data = [];
      for (const line of raw.split(/\r?\n/)) {
        if (!line || line.startsWith(':')) continue;
        const c = line.indexOf(':');
        const field = c < 0 ? line : line.slice(0, c);
        const value = c < 0 ? '' : line.slice(c + 1).replace(/^ /, '');
        if (field === 'event') event = value; else if (field === 'data') data.push(value);
      }
      if (!data.length) continue;
      let parsed = data.join('\n');
      try { parsed = JSON.parse(parsed); } catch { }
      this.onEvent({ event, data: parsed });
    }
  }
}

/**
 * @param {object} o
 * @param {string} o.base          the server's address, e.g. 'http://localhost:8080/'
 * @param {(alert:object)=>void} o.onAlert       an alert was raised, resolved or tested: { id, event, kind, severity, title, message, slot, program, time, count }
 * @param {(status:object)=>void} [o.onStatus]   the counts and colour: { critical, warning, info, acknowledged, muted, color }
 * @param {(connected:boolean)=>void} [o.onConnection]
 * @returns {{ stop: () => void }}
 */
function subscribe({ base, onAlert, onStatus, onConnection, retryMs = 1000, maxRetryMs = 30000, idleMs = 45000 }) {
  let stopped = false, req = null, timer = null, attempt = 0, connected = false;
  const url = new URL('api/alerts/stream', base);

  const setConnected = c => { if (c !== connected) { connected = c; try { onConnection && onConnection(c); } catch { } } };
  const later = () => {
    if (stopped) return;
    setConnected(false);
    clearTimeout(timer);
    timer = setTimeout(connect, Math.min(maxRetryMs, retryMs * 2 ** Math.min(attempt++, 10)));
  };

  function connect() {
    if (stopped) return;
    const parser = new SseParser(({ event, data }) => {
      try {
        if (event === 'alert') onAlert && onAlert(data);
        else if (event === 'status') onStatus && onStatus(data.status ?? data);
      } catch { }
    });
    req = http.get(url, { headers: { Accept: 'text/event-stream' } }, res => {
      if (res.statusCode !== 200) { res.resume(); later(); return; }
      attempt = 0; setConnected(true);
      res.on('data', c => parser.push(c));
      res.on('end', later);
      res.on('error', later);
    });
    // The server sends a comment every 15 s; silence for much longer than that means the connection is dead.
    req.setTimeout(idleMs, () => req.destroy());
    req.on('error', later);
  }

  connect();
  return { stop() { stopped = true; clearTimeout(timer); if (req) req.destroy(); setConnected(false); } };
}

module.exports = { subscribe, SseParser };
