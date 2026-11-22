OpenVP starter CH5 / HTML panel
===============================

As a shared panel
  1. Zip the files (index.html at the top) as a .ch5z, or use the .ch5z you downloaded.
  2. In the OpenVP console, click an IP ID in the IP table, then "Upload .ch5z..." and choose it.
  3. Click "Share..." for that IP ID. The link opens this page, connected to that slot and IP ID.

As a local page
  Open index.html with the address of the OpenVP console and the IP ID:
      index.html?host=localhost:8080&ipid=03&slot=1
  The page loads openvp-crcomlib.js from that console. (Over http://, from a page served by the console itself,
  host can be left out.)

Joins: b = digital, n = analog (0-65535), s = serial. Only numeric join names work with OpenVP's join library.
The page expects the OpenVP starter program (digital 1 lamp, analog 1 echo, serial 1 upper-cased on serial 2), but any
program works: change the joins in index.html and app.js.
