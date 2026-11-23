// Draws the tray icon: a round dot in the status colour, as a PNG made here (no image files, no native modules), so the colour can change at run time.
const zlib = require('zlib');

const COLORS = { green: [47, 158, 110], amber: [214, 158, 46], red: [224, 82, 82], grey: [139, 149, 163] };

const crcTable = (() => { const t = new Int32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c; } return t; })();
const crc32 = buf => { let c = -1; for (let i = 0; i < buf.length; i++) c = crcTable[(c ^ buf[i]) & 0xff] ^ (c >>> 8); return (c ^ -1) >>> 0; };
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}

/** A size x size PNG: an anti-aliased filled circle in the colour ('green', 'amber', 'red' or 'grey') with a darker rim. */
function makeIcon(color, size = 32) {
  const [r, g, b] = COLORS[color] || COLORS.grey;
  const raw = Buffer.alloc((size * 4 + 1) * size);
  const c = (size - 1) / 2, rad = size / 2 - 1.5;
  for (let y = 0; y < size; y++) {
    raw[y * (size * 4 + 1)] = 0; // filter: none
    for (let x = 0; x < size; x++) {
      const d = Math.hypot(x - c, y - c);
      const cover = Math.max(0, Math.min(1, rad - d + 0.5));           // 1 inside, fades over one pixel at the edge
      const rim = d > rad - 2 ? 0.72 : 1;                              // a slightly darker ring
      const o = y * (size * 4 + 1) + 1 + x * 4;
      raw[o] = Math.round(r * rim); raw[o + 1] = Math.round(g * rim); raw[o + 2] = Math.round(b * rim); raw[o + 3] = Math.round(255 * cover);
    }
  }
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4); ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

module.exports = { makeIcon, COLORS };
