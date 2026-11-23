// A small SIMPL+ highlighter, no libraries. tokenize(text) returns one array of {k, t} per source line, where k is
// the kind of token (kw keyword, ty type, dir # directive, ev event, fn built-in function, num, str, com, id, op, ws)
// and t is its text. Block comments and #HELP_BEGIN..#HELP_END text carry on across lines. Tests: tests/js/splus.test.js
(function (root) {
  "use strict";
  const set = s => new Set(s.split(/\s+/).filter(Boolean));
  // SIMPL+ is not case sensitive, so every word is compared in upper case.
  const TYPES = set(`DIGITAL_INPUT ANALOG_INPUT STRING_INPUT BUFFER_INPUT DIGITAL_OUTPUT ANALOG_OUTPUT STRING_OUTPUT
    INTEGER_PARAMETER LONG_INTEGER_PARAMETER SIGNED_INTEGER_PARAMETER SIGNED_LONG_INTEGER_PARAMETER STRING_PARAMETER
    INTEGER LONG_INTEGER SIGNED_INTEGER SIGNED_LONG_INTEGER STRING STRUCTURE TCP_CLIENT TCP_SERVER UDP_SOCKET
    SOCKETSTATUS FILE_INFO CHAR`);
  const KEYWORDS = set(`IF ELSE WHILE FOR DO UNTIL SWITCH CASE ENDSWITCH DEFAULT BREAK RETURN RETURNVALUE TO STEP
    FUNCTION ENDFUNCTION CALLBACK EVENTHANDLER GLOBAL STRUCTURE ENDSTRUCTURE MAIN ENDMAIN NOT AND OR XOR MOD
    TRUE FALSE TRY CATCH ENDTRY CONTINUE GOTO DELAY_VALUE ASYNC_INPUT`);
  const EVENTS = set(`PUSH RELEASE CHANGE THREADSAFE SOCKETCONNECT SOCKETDISCONNECT SOCKETRECEIVE SOCKETSTATUS`);
  const FUNCS = set(`PRINT TRACE MAKESTRING LEN MID LEFT RIGHT FIND ATOI ITOA CHR BYTE LOWER UPPER LOWERCASE UPPERCASE
    PROCESSLOGIC WAIT CANCELWAIT PAUSEWAIT RESUMEWAIT CANCELALLWAIT DELAY GETSTRING SETSTRING REMOVE CLEARBUFFER
    SOCKETCONNECTCLIENT SOCKETDISCONNECTCLIENT SOCKETSEND SOCKETGETSTATUS SOCKETSERVERSTARTLISTEN
    SETARRAY FILEOPEN FILECLOSE FILEWRITE FILEREAD GETCURRENTTIME ABS MAX MIN`);
  const DIRECTIVES = set(`#DEFINE_CONSTANT #CATEGORY #SYMBOL_NAME #HINT #HELP_BEGIN #HELP_END #DEFAULT_VOLATILE
    #DEFAULT_NONVOLATILE #ENABLE_STACK_CHECKING #ENABLE_TRACE #CRESTRON_LIBRARY #USER_LIBRARY #USER_SIMPLSHARP_LIBRARY
    #CRESTRON_SIMPLSHARP_LIBRARY #INCLUDEPATH #IF_DEFINED #IF_NOT_DEFINED #ENDIF #ELSE #IF_SERIES2 #IF_SERIES3 #IF_SERIES4
    #ENDIF_SERIES2 #ENDIF_SERIES3 #ENDIF_SERIES4 #PRINT_TO_TRACE #OUTPUT_SHIFT #PARAMETER_PROPERTIES #INCLUDE
    #DEFINE #UNDEFINE #PARAMETER_SEND_TYPE #ANALOG_SEND_TYPE #ANALOG_INPUT_SEND_TYPE #LARGE_STACK`);

  const isIdStart = c => /[A-Za-z_]/.test(c);
  const isId = c => /[A-Za-z0-9_]/.test(c);

  function tokenize(text) {
    const lines = String(text).replace(/\r\n?/g, "\n").split("\n");
    const out = [];
    let block = null;              // "/*", "(*" or "help" while a multi-line construct is open
    for (const line of lines) {
      const toks = [];
      const push = (k, t) => { if (!t) return; const p = toks[toks.length - 1]; if (k === "com" && p && p.k === "com") p.t += t; else toks.push({ k, t }); };
      let i = 0;
      while (i < line.length) {
        if (block === "help") {
          const m = /#HELP_END/i.exec(line.slice(i));
          if (!m) { push("com", line.slice(i)); i = line.length; }
          else { push("com", line.slice(i, i + m.index)); push("dir", m[0]); i += m.index + m[0].length; block = null; }
          continue;
        }
        if (block === "/*" || block === "(*") {
          const end = block === "/*" ? "*/" : "*)";
          const j = line.indexOf(end, i);
          if (j < 0) { push("com", line.slice(i)); i = line.length; }
          else { push("com", line.slice(i, j + 2)); i = j + 2; block = null; }
          continue;
        }
        const c = line[i], n = line[i + 1];
        if (c === " " || c === "\t") { let j = i; while (j < line.length && (line[j] === " " || line[j] === "\t")) j++; push("ws", line.slice(i, j)); i = j; }
        else if (c === "/" && n === "/") { push("com", line.slice(i)); i = line.length; }
        else if (c === "/" && n === "*") { block = "/*"; push("com", "/*"); i += 2; }
        else if (c === "(" && n === "*") { block = "(*"; push("com", "(*"); i += 2; }
        else if (c === '"') {
          let j = i + 1;
          while (j < line.length && line[j] !== '"') j += line[j] === "\\" ? 2 : 1;
          push("str", line.slice(i, Math.min(j + 1, line.length))); i = Math.min(j + 1, line.length);
        }
        else if (c === "'") {
          let j = i + 1;
          while (j < line.length && line[j] !== "'") j += line[j] === "\\" ? 2 : 1;
          push("str", line.slice(i, Math.min(j + 1, line.length))); i = Math.min(j + 1, line.length);
        }
        else if (c === "#") {
          let j = i + 1; while (j < line.length && isId(line[j])) j++;
          const w = line.slice(i, j);
          if (DIRECTIVES.has(w.toUpperCase())) { push("dir", w); if (w.toUpperCase() === "#HELP_BEGIN") block = "help"; }
          else push("op", w);
          i = j;
        }
        else if (/[0-9]/.test(c) || (c === "$" && /[0-9A-Fa-f]/.test(n || ""))) {
          let j = i + 1;
          if (c === "0" && /[xX]/.test(n || "")) j = i + 2;
          while (j < line.length && /[0-9A-Fa-f_]/.test(line[j])) j++;
          // 0x1F and $1F are hex; a trailing d or h marks the base in older code.
          if (/[hHdDbB]/.test(line[j] || "") && !isId(line[j + 1] || " ")) j++;
          push("num", line.slice(i, j)); i = j;
        }
        else if (isIdStart(c)) {
          let j = i + 1; while (j < line.length && isId(line[j])) j++;
          const w = line.slice(i, j), u = w.toUpperCase();
          let k = "id";
          if (TYPES.has(u)) k = "ty";
          else if (KEYWORDS.has(u)) k = "kw";
          else if (EVENTS.has(u)) k = "ev";
          else if (FUNCS.has(u)) k = "fn";
          push(k, w); i = j;
        }
        else { push("op", c); i++; }
      }
      out.push(toks);
    }
    return out;
  }

  /** Draw the source into box: a line-number gutter and the highlighted code, one row per line. Returns the row of each line (1-based). */
  function render(box, text, opts) {
    opts = opts || {};
    const doc = box.ownerDocument;
    const lines = tokenize(text);
    box.replaceChildren();
    const rows = [null];
    const frag = doc.createDocumentFragment();
    lines.forEach((toks, idx) => {
      const no = idx + 1;
      const row = doc.createElement("div"); row.className = "sp-row"; row.id = "L" + no;
      const g = doc.createElement("a"); g.className = "sp-no"; g.textContent = String(no); g.href = "#L" + no;
      g.setAttribute("aria-label", "Line " + no);
      if (opts.onLine) g.addEventListener("click", ev => { ev.preventDefault(); opts.onLine(no); });
      const code = doc.createElement("span"); code.className = "sp-code";
      for (const t of toks) {
        if (t.k === "ws" || t.k === "op" || t.k === "id") code.appendChild(doc.createTextNode(t.t));
        else { const s = doc.createElement("span"); s.className = "sp-" + t.k; s.textContent = t.t; code.appendChild(s); }
      }
      if (!toks.length) code.appendChild(doc.createTextNode("​"));
      row.append(g, code); frag.appendChild(row); rows.push(row);
    });
    box.appendChild(frag);
    return rows;
  }

  const api = { tokenize, render };
  if (typeof module !== "undefined" && module.exports) module.exports = api; else root.OpenVPSplus = api;
})(typeof window !== "undefined" ? window : this);
