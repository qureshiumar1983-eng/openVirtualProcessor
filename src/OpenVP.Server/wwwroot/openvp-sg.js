/*
 * openvp-sg.js: draws a smart-graphics project (the model OpenVP's server reads from a .vtz, .c3p, XML or JSON file, see
 * GET /api/slots/{n}/panels/{ipid}/sg) and wires its objects to joins through openvp-crcomlib.js.
 *
 *   OpenVPSg.mount(element, project, { assetBase: "/api/slots/1/panels/03/sg/assets/" })
 *
 * Drawn: buttons (press, feedback, text from a serial join), text and text entry, sliders, gauges, lists, images, boxes and
 * pages/subpages as tabs. Anything else is a labelled placeholder. Joins: digital press/feedback, analog in/out, serial in/out;
 * smart objects use the so<id>.d<n> form. This is a viewer for trying a project against a program, not a copy of Crestron's renderer.
 */
(function (global) {
  "use strict";
  var doc = global.document;

  function el(tag, cls, text) { var e = doc.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
  function pos(e, o) { e.style.left = o.x + "px"; e.style.top = o.y + "px"; e.style.width = o.w + "px"; e.style.height = o.h + "px"; }
  function nm(prefix, so, join) { return so ? "so" + so + "." + prefix + join : prefix + join; }

  function css() {
    if (doc.getElementById("openvp-sg-css")) return;
    var s = el("style"); s.id = "openvp-sg-css";
    s.textContent =
      ".sg-tabs{display:flex;flex-wrap:wrap;gap:4px;margin-bottom:8px}.sg-tabs button{font:inherit;padding:3px 10px;border:1px solid #8886;background:#8882;color:inherit;border-radius:6px;cursor:pointer}.sg-tabs button.on{border-color:#4c8dff;background:#4c8dff33}" +
      ".sg-view{position:relative;overflow:hidden}.sg-page{position:absolute;left:0;top:0;transform-origin:0 0;background:#0003;border:1px solid #8884}" +
      ".sg-o{position:absolute;box-sizing:border-box;font:14px system-ui,sans-serif;overflow:hidden}" +
      ".sg-btn{display:flex;align-items:center;justify-content:center;text-align:center;border:1px solid #8888;border-radius:8px;background:#8883;cursor:pointer;user-select:none;-webkit-user-select:none;touch-action:none;padding:0 6px}" +
      ".sg-btn.on{background:#2f9e6e;border-color:#2f9e6e;color:#fff}.sg-btn.down{outline:2px solid #4c8dff}" +
      ".sg-text{display:flex;align-items:center;padding:0 4px;white-space:pre-wrap;overflow-wrap:anywhere}.sg-text input{width:100%;height:100%;font:inherit;color:inherit;background:#8822;border:1px solid #8886;border-radius:4px;padding:0 6px}" +
      ".sg-slider{display:flex;align-items:center;padding:0 6px}.sg-slider input{width:100%}" +
      ".sg-gauge{border:1px solid #8886;border-radius:6px;background:#8822;position:relative}.sg-gauge i{position:absolute;left:0;bottom:0;width:100%;background:#2f9e6e;transition:height .15s}.sg-gauge.h i{height:100%!important;width:0;transition:width .15s}.sg-gauge b{position:absolute;left:0;right:0;top:50%;transform:translateY(-50%);text-align:center;font:600 13px ui-monospace,monospace;color:inherit;text-shadow:0 0 3px #000a}" +
      ".sg-list{display:flex;flex-direction:column;gap:6px;overflow:auto;padding:2px}.sg-list .sg-btn{position:relative;min-height:36px;flex:0 0 auto}" +
      ".sg-box{border:1px solid #8886;border-radius:6px;background:#8821}.sg-img{display:block}.sg-bad{border:2px dashed #d69e2e;color:#d69e2e;display:flex;align-items:center;justify-content:center;text-align:center;font-size:12px}" +
      ".sg-rep{margin-top:10px;font:13px system-ui,sans-serif;border:1px solid #8886;border-radius:8px;padding:6px 10px}.sg-rep ul{margin:4px 0 0 18px;padding:0}";
    (doc.head || doc.documentElement).appendChild(s);
  }

  function bindButton(e, press, so) {
    if (!press) return;
    var name = nm("d", so, press);
    e.addEventListener("pointerdown", function (ev) { ev.preventDefault(); try { e.setPointerCapture(ev.pointerId); } catch (x) { } e.classList.add("down"); global.CrComLib.publishEvent("boolean", name, true); });
    function up() { if (!e.classList.contains("down")) return; e.classList.remove("down"); global.CrComLib.publishEvent("boolean", name, false); }
    ["pointerup", "pointercancel", "lostpointercapture"].forEach(function (t) { e.addEventListener(t, up); });
    e.addEventListener("contextmenu", function (ev) { ev.preventDefault(); });
  }

  function build(o, opt, subs) {
    var C = global.CrComLib, j = o.joins || {}, so = j.smartObject || 0, e, n;
    function sub(type, name, fn) { subs.push([type, name, C.subscribeState(type, name, fn)]); }
    switch (o.type) {
      case "button":
        e = el("div", "sg-o sg-btn", o.text || ""); pos(e, o);
        bindButton(e, j.press, so);
        if (j.feedback) sub("boolean", nm("d", so, j.feedback), function (v) { e.classList.toggle("on", !!v); });
        if (j.serialIn) sub("string", nm("s", so, j.serialIn), function (v) { e.textContent = v; });
        break;
      case "text":
        e = el("div", "sg-o sg-text"); pos(e, o);
        if (j.serialOut) {
          var inp = el("input"); inp.placeholder = o.text || ""; e.appendChild(inp);
          inp.addEventListener("keydown", function (ev) { if (ev.key === "Enter") C.publishEvent("string", nm("s", so, j.serialOut), inp.value); });
          if (j.serialIn) sub("string", nm("s", so, j.serialIn), function (v) { if (doc.activeElement !== inp) inp.value = v; });
        } else {
          e.textContent = o.text || "";
          if (j.serialIn) sub("string", nm("s", so, j.serialIn), function (v) { e.textContent = v; });   // text only, never HTML
        }
        break;
      case "slider":
        e = el("div", "sg-o sg-slider"); pos(e, o);
        var r = el("input"); r.type = "range"; r.min = 0; r.max = 65535; r.value = 0; e.appendChild(r);
        var last = 0;
        r.addEventListener("input", function () { var t = Date.now(); if (j.analogOut && t - last > 40) { last = t; C.publishEvent("number", nm("a", so, j.analogOut), +r.value); } });
        r.addEventListener("change", function () { if (j.analogOut) C.publishEvent("number", nm("a", so, j.analogOut), +r.value); });
        if (j.analogIn) sub("number", nm("a", so, j.analogIn), function (v) { if (doc.activeElement !== r) r.value = v; });
        break;
      case "gauge":
        e = el("div", "sg-o sg-gauge" + (o.w > o.h ? " h" : "")); pos(e, o);
        var bar = el("i"), lab = el("b", null, "0%"); e.append(bar, lab);
        if (j.analogIn) sub("number", nm("a", so, j.analogIn), function (v) {
          var pct = Math.max(0, Math.min(100, Math.round((+v / 65535) * 100)));
          if (o.w > o.h) bar.style.width = pct + "%"; else bar.style.height = pct + "%";
          lab.textContent = pct + "%";
        });
        break;
      case "list":
        e = el("div", "sg-o sg-list"); pos(e, o);
        var items = o.items && o.items.length ? o.items : ["Item 1"];
        items.forEach(function (label, i) {
          var b = el("div", "sg-btn", label);
          var pj = so ? i + 1 : (j.press ? j.press + i : i + 1);       // smart object: item n is digital join n; otherwise count up from the press join
          bindButton(b, pj, so);
          sub("boolean", nm("d", so, so ? i + 1 : (j.feedback ? j.feedback + i : pj)), function (v) { b.classList.toggle("on", !!v); });
          if (so) sub("string", nm("s", so, i + 1), function (v) { if (v) b.textContent = v; });
          e.appendChild(b);
        });
        break;
      case "image":
        e = el("div", "sg-o"); pos(e, o);
        if (o.image) { var im = el("img", "sg-img"); im.src = opt.assetBase + encodeURIComponent(o.image); im.alt = o.name || ""; im.style.cssText = "width:100%;height:100%;object-fit:contain"; e.appendChild(im); }
        else { e.className += " sg-box"; e.textContent = ""; }
        break;
      case "box":
        e = el("div", "sg-o sg-box"); pos(e, o); if (o.detail) e.title = o.detail;
        break;
      default:
        e = el("div", "sg-o sg-bad", o.detail || ("Not supported: " + (o.text || o.type))); pos(e, o);
    }
    e.dataset.name = o.name || "";
    return e;
  }

  function mount(root, project, opt) {
    css();
    opt = opt || {};
    opt.assetBase = opt.assetBase || "";
    root.textContent = "";
    var subs = [];
    var pages = project.pages || [];
    var tabs = el("div", "sg-tabs");
    var view = el("div", "sg-view");
    root.append(tabs, view);
    var current = null;

    function show(i) {
      subs.forEach(function (s) { global.CrComLib.unsubscribeState(s[0], s[1], s[2]); }); subs = [];
      view.textContent = "";
      Array.prototype.forEach.call(tabs.children, function (b, k) { b.classList.toggle("on", k === i); });
      var p = pages[i]; if (!p) return;
      var page = el("div", "sg-page"); page.style.width = p.width + "px"; page.style.height = p.height + "px";
      (p.objects || []).forEach(function (o) { page.appendChild(build(o, opt, subs)); });
      view.appendChild(page);
      current = { page: page, p: p };
      fit();
    }
    function fit() {
      if (!current) return;
      var w = root.clientWidth || current.p.width, s = Math.min(1, w / current.p.width);
      current.page.style.transform = "scale(" + s + ")";
      view.style.height = Math.ceil(current.p.height * s) + "px";
    }
    pages.forEach(function (p, i) {
      var b = el("button", null, p.name + (p.kind === "subpage" ? " (subpage)" : ""));
      b.type = "button"; b.onclick = function () { show(i); }; tabs.appendChild(b);
    });
    if (!pages.length) view.appendChild(el("div", "sg-rep", "This project has no pages OpenVP could read."));
    if (pages.length) show(0);
    var ro = global.ResizeObserver ? new global.ResizeObserver(fit) : null;
    if (ro) ro.observe(root); else global.addEventListener("resize", fit);

    // What was left out, so a missing control is never a mystery.
    var rep = project.report || {}, lines = (rep.unsupported || []).concat(rep.notes || []);
    if (lines.length) {
      var d = el("details", "sg-rep"); d.appendChild(el("summary", null, "What this view doesn't show (" + lines.length + ")"));
      var ul = el("ul"); lines.forEach(function (l) { ul.appendChild(el("li", null, l)); }); d.appendChild(ul); root.appendChild(d);
    }
    return { destroy: function () { subs.forEach(function (s) { global.CrComLib.unsubscribeState(s[0], s[1], s[2]); }); if (ro) ro.disconnect(); root.textContent = ""; } };
  }

  global.OpenVPSg = { mount: mount };
})(window);
