// OpenVP starter panel. Joins are plain numbers:  b = digital, n = analog (0-65535), s = serial.
//   CrComLib.publishEvent("b", "1", true)      send a press on digital join 1
//   CrComLib.subscribeState("b", "1", fn)      get feedback for digital join 1
// Works as a shared-panel CH5 project (zip this folder as .ch5z and upload it for an IP ID; OpenVP adds
// openvp-crcomlib.js for you) and as a plain page: open index.html with  ?host=<openvp-host>:8080&ipid=03&slot=1
(function () {
  "use strict";
  var params = new URLSearchParams(location.search);

  function boot() {
    var C = window.CrComLib;
    var stateEl = document.getElementById("state");
    function connected(on) { stateEl.textContent = on ? "connected" : "offline"; stateEl.classList.toggle("on", on); }
    if (window.OpenVP && window.OpenVP.on) {
      window.OpenVP.on("open", function () { connected(true); });
      window.OpenVP.on("close", function () { connected(false); });
      window.OpenVP.on("offline", function () { connected(false); });
      window.OpenVP.on("hello", function () { connected(true); });
    }

    // Buttons: press and release send digital join N high and low; feedback join N lights the button.
    document.querySelectorAll(".btn").forEach(function (btn) {
      var join = btn.dataset.join;
      function press(v) { C.publishEvent("b", join, v); }
      btn.addEventListener("pointerdown", function (e) { btn.setPointerCapture(e.pointerId); press(true); });
      btn.addEventListener("pointerup", function () { press(false); });
      btn.addEventListener("pointercancel", function () { press(false); });
      btn.addEventListener("keydown", function (e) { if ((e.key === " " || e.key === "Enter") && !e.repeat) { e.preventDefault(); press(true); } });
      btn.addEventListener("keyup", function (e) { if (e.key === " " || e.key === "Enter") press(false); });
      C.subscribeState("b", join, function (v) { btn.classList.toggle("on", !!v); });
    });

    // Slider: analog join 1, throttled; the program's feedback is shown next to it.
    var level = document.getElementById("level"), levelFb = document.getElementById("level-fb"), last = 0;
    level.addEventListener("input", function () {
      var now = Date.now();
      if (now - last > 60) { last = now; C.publishEvent("n", "1", +level.value); }
    });
    level.addEventListener("change", function () { C.publishEvent("n", "1", +level.value); });
    C.subscribeState("n", "1", function (v) { levelFb.textContent = v; });

    // Text: serial join 1 out, serial feedback 2 and 10 in.
    document.getElementById("text-form").addEventListener("submit", function (e) {
      e.preventDefault();
      var box = document.getElementById("text");
      C.publishEvent("s", "1", box.value);
    });
    C.subscribeState("s", "2", function (v) { document.getElementById("text-fb").textContent = v || " "; });
    C.subscribeState("s", "10", function (v) { document.getElementById("status-fb").textContent = v || " "; });
    if (!window.OpenVP || !window.OpenVP.on) connected(true);
  }

  if (window.CrComLib && window.OpenVP) { boot(); return; }
  // Opened as a plain page: load OpenVP's join library from the console.
  var host = params.get("host") || location.host;
  if (!host) { document.getElementById("state").textContent = "add ?host=<openvp-host>:8080 to the address"; return; }
  var s = document.createElement("script");
  s.src = (location.protocol === "https:" ? "https://" : "http://") + host + "/openvp-crcomlib.js";
  s.onload = boot;
  s.onerror = function () { document.getElementById("state").textContent = "can't reach " + host; };
  document.head.appendChild(s);
})();
