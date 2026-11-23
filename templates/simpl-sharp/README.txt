OpenVP starter SIMPL# Pro program
=================================

1. Build it (needs the .NET 8 SDK):

       dotnet build

2. In the OpenVP console, pick a slot, choose  bin/Debug/net8.0/StarterProgram.dll  and click Load & start.
   The IP table shows a touch panel at IP ID 03 and an EISC at IP ID 10. Click "open test panel" next to the panel.

3. Press digital 1 on the panel: the lamp (digital feedback 1) toggles.
   Press digital 10: the program sends it over the EISC. Load a second program with an EISC on 127.0.0.2 to react to it.

Edit ControlSystem.cs and build again. Turn on "Reload when the file changes" in the console (desktop app, or a browser on
the OpenVP computer) and the slot reloads every time you build.

lib/ holds OpenVP's own copy of the SIMPL# Pro API (SimplSharpPro.dll, OpenVP.Core.dll). It contains no Crestron code. To
build the same program for a real processor, point the two <Reference> lines in StarterProgram.csproj at Crestron's SDK.
