# Unsupported symbols and APIs

OpenVP adds a row here whenever a program uses a symbol, type or API it doesn't support yet. Commit this file after running your programs so everyone can see what to build next; **Programs** counts the program loads that hit it, so the top of each kind is the most wanted. Only symbol names are recorded, never program names.

When you implement one, delete its row. If a program still needs it, it comes back on the next run.

Merging: if two people's edits clash, keep both rows; for the same row, add the Programs counts and keep the earliest First seen and latest Last seen.

| Kind | Symbol / API | Details | Programs | First seen | Last seen |
|---|---|---|---:|---|---|
| Compiled primitive | type 41 | unidentified | 19 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 45 | unidentified | 10 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 144 | unidentified | 8 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 63 | unidentified | 8 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 66 | unidentified | 8 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 107 | unidentified | 2 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 109 | unidentified | 2 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 113 | unidentified | 1 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 200 | unidentified | 1 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 21 | unidentified | 1 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 261 | unidentified | 1 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 65 | unidentified | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-USB-HID (SmC 5564) | processor hardware (Cresnet, COM, IR, relays...) | 20 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N I/O Ports (SmC 7054) | processor hardware (Cresnet, COM, IR, relays...) | 12 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N Relay (SmC 7055) | processor hardware (Cresnet, COM, IR, relays...) | 12 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N System Control (SmC 7058) | processor hardware (Cresnet, COM, IR, relays...) | 12 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N System Information (SmC 7059) | processor hardware (Cresnet, COM, IR, relays...) | 12 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N System Monitor (SmC 7057) | processor hardware (Cresnet, COM, IR, relays...) | 12 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4N User Program Init (SmC 7060) | processor hardware (Cresnet, COM, IR, relays...) | 12 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP3 two-way serial driver (SmC 3389) | processor hardware (Cresnet, COM, IR, relays...) | 11 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Crestron DB serial-controlled device (SmC 108) | processor hardware (Cresnet, COM, IR, relays...) | 11 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | COM 2-Way Serial Driver (SmC 6301) | processor hardware (Cresnet, COM, IR, relays...) | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 I/O Ports (SmC 6582) | processor hardware (Cresnet, COM, IR, relays...) | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 Relay (SmC 6583) | processor hardware (Cresnet, COM, IR, relays...) | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 System Control (SmC 6586) | processor hardware (Cresnet, COM, IR, relays...) | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 System Information (SmC 6587) | processor hardware (Cresnet, COM, IR, relays...) | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 System Monitor (SmC 6585) | processor hardware (Cresnet, COM, IR, relays...) | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP4 User Program Init (SmC 6588) | processor hardware (Cresnet, COM, IR, relays...) | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Crestron DB IR-controlled device (SmC 107) | processor hardware (Cresnet, COM, IR, relays...) | 8 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | COM 2-Way Serial Driver (3-pin) (SmC 6361) | processor hardware (Cresnet, COM, IR, relays...) | 6 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CP3 RS-232 only two-way serial driver (SmC 3398) | processor hardware (Cresnet, COM, IR, relays...) | 6 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-CP3-IO8 (SmC 3390) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-CP3-RY8 (SmC 3391) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-CP3-SYSTEMCONTROL (SmC 3395) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-CP3-SYSTEMINFORMATION (SmC 3396) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-CP3-SYSTEMMONITOR (SmC 3394) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-CP3-USERPROGINIT (SmC 4320) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 System Control (SmC 4415) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 System Information (SmC 4416) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 System Monitor (SmC 4414) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 User Program Information (SmC 4627) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | GLS-PART-CN (SmC 5206) | processor hardware (Cresnet, COM, IR, relays...) | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C3COM-3 Two-way serial driver (SmC 3472) | processor hardware (Cresnet, COM, IR, relays...) | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Digital Inputs (SmC 4381) | processor hardware (Cresnet, COM, IR, relays...) | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Relays (SmC 4382) | processor hardware (Cresnet, COM, IR, relays...) | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-C2N-IO-COM1-232 Two-way serial driver (SmC 734) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-C2N-IO-RY2 (SmC 940) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Two-way serial driver (SmC 4378) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | RMC4 Digital Input (SmC 6539) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | RMC4 Relay (SmC 6540) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | RMC4 System Control (SmC 6543) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | RMC4 System Information (SmC 6544) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | RMC4 System Monitor (SmC 6542) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | RMC4 User Program Init (SmC 6545) | processor hardware (Cresnet, COM, IR, relays...) | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-COM6 two-way serial driver (SmC 3680) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-CRESNETINFO (SmC 4123) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-IO8 (SmC 3682) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-RY8 (SmC 3683) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-SYSTEMCONTROL (SmC 3687) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-SYSTEMINFORMATION (SmC 3688) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-SYSTEMMONITOR (SmC 3686) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-USERPROGINIT (SmC 4318) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-DMTX201C-HDMI (SmC 2575) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-DMTX201C-VGA (SmC 2576) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-COM6 RS-232 only two-way serial driver (SmC 3667) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-COM6 two-way serial driver (SmC 3666) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-CRESNETINFO (SmC 4124) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-IO8 (SmC 3668) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-RY8 (SmC 3669) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-SYSTEMCONTROL (SmC 3673) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-SYSTEMINFORMATION (SmC 3674) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-SYSTEMMONITOR (SmC 3672) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-PRO3-USERPROGINIT (SmC 4319) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2N-CBD Advanced Button Events (SmC 2736) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2N-CBD-P (SmC 2735) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2N-CBD-P Brightness and Bargraph Functions (SmC 2737) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2N-CBD-P Brightness and Bargraph Settings (SmC 2738) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2N-CBD-P Versiport (SmC 2739) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-SCALER-C (SmC 3334) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-SCALER-C DM Input Slot (SmC 3339) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-SCALER-C Scaler (SmC 3338) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-SCALER-C Two-way serial driver (SmC 3336) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-TX-201-C (SmC 2574) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Aux 1 (SmC 5104) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Aux 1 EQ (SmC 5086) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Aux 1 Mix (SmC 5085) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Aux 2 (SmC 5105) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Aux 2 EQ (SmC 5088) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Aux 2 Mix (SmC 5087) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 DM 3 (SmC 5108) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 DM 3 Mix (SmC 5091) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 DM 4 (SmC 5109) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 DM 4 Mix (SmC 5092) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 HDMI 1 (SmC 5106) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 HDMI 1 Mix (SmC 5089) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 HDMI 2 (SmC 5107) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 HDMI 2 Mix (SmC 5090) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 1 HDMI (SmC 4834) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 2 HDMI (SmC 4835) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 3 HDMI/VGA (SmC 4836) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 4 HDMI/VGA (SmC 4837) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 5 HDMI/VGA/BNC (SmC 4838) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 6 DM (SmC 4613) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Input 7 DM (SmC 4614) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Mic 1 (SmC 4828) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Mic 2 (SmC 4829) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Mic 3 (SmC 4830) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Mic 4 (SmC 4831) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Mic 5 (SmC 4832) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Mic 6 (SmC 4833) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Program (SmC 5103) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Program EQ (SmC 5084) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Program Mix (SmC 5083) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-300-C AV Control (SmC 5102) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-300-C Device Control (SmC 4816) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C AirMedia (SmC 5877) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C AirMedia OSD Control (SmC 5880) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Analog Audio Inputs (SmC 6248) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Aux 1 (SmC 5886) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Aux 1 EQ (SmC 5888) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Aux 1 Mix (SmC 5887) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Aux 2 (SmC 5889) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Aux 2 EQ (SmC 5891) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Aux 2 Mix (SmC 5890) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C AV Control (SmC 5858) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Control (SmC 5856) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Device Control (SmC 5857) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Digital Mixer 1 (SmC 5893) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Digital Mixer 2 (SmC 5895) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C DM 3 (SmC 5896) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C DM 4 (SmC 5899) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C H.264 Stream In (SmC 5881) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C HDMI 1 (SmC 5892) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C HDMI 2 (SmC 5894) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 1 HDMI (SmC 5867) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 2 HDMI (SmC 5868) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 3 HDMI (SmC 5869) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 4 HDMI (SmC 5870) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 5 HDMI (SmC 5871) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 6 HDMI (SmC 5872) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 7 DM (SmC 5873) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 8 DM (SmC 5875) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Input 9 AirMedia (SmC 6249) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Mic 1 (SmC 5860) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Mic 2 (SmC 5861) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Mic 3 (SmC 5862) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Mic 4 (SmC 5863) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Mic 5 (SmC 5864) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Mic 6 (SmC 5865) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Program (SmC 5883) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Program EQ (SmC 5885) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Program Mix (SmC 5884) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-350-C Stream Receive (SmC 6250) | processor hardware (Cresnet, COM, IR, relays...) | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-COM6 RS-232 only two-way serial driver (SmC 3681) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-AV3-IR8 One-way serial driver (SmC 3685) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-DMTX200-C-2G-HDMI (SmC 3384) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-DMTX200-C-2G-VGA (SmC 3385) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-REMOTE-BACNET-DEVICE v1.0 (SmC 3560) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C2I-REMOTE-BACNET-DEVICES v1.0 (SmC 3559) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | C3RY-8 (SmC 3473) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CM2-KPCN Advanced Button Events (SmC 19503) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CM2-KPCN Ambient Light Sensor (SmC 19494) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CM2-KPCN Backlight LED Settings (SmC 19493) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CM2-KPCN Button/LED Functions (SmC 19492) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CM2-KPCN Temperature Sensor Input (SmC 19496) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | CM2-KPCN Versiport (SmC 19495) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | COM 2-Way Serial Driver (SmC 6361) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-4K-100-C-1G HDMI (SmC 5223) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-4KZ-SCALER-C (SmC 6431) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-4KZ-SCALER-C DM Input Slot (SmC 6436) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-4KZ-SCALER-C HDMI (SmC 6437) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-RMC-4KZ-SCALER-C Scaler (SmC 6435) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DM-TX-200-C-2G (SmC 3383) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 Analog Audio Output (SmC 4985) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 DM/HDMI Output (SmC 5158) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3 One-way serial driver (SmC 4380) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C AV Control (SmC 5118) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C Device Control (SmC 5152) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C Digital Inputs (SmC 5116) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C Relays (SmC 5197) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV DM Input 1 (SmC 5123) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV DM Input 2 (SmC 5124) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV HDMI Input 1 (SmC 5119) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV HDMI Input 2 (SmC 5120) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV HDMI Input 3 (SmC 5121) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV HDMI Input 4 (SmC 5122) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV Mic (SmC 4984) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV VGA Input 1 (SmC 4986) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV VGA Input 2 (SmC 4987) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV VGA Input 3 (SmC 4988) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | DMPS3-4K-150-C-AV VGA Input 4 (SmC 4989) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | GLS-ODT-C-CN (SmC 3478) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | GLS-ODT-C-CN Sensor Configuration Settings (SmC 3790) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | GLS-ODT-C-CN Sensor IR Settings (SmC 3481) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | HDBT Compatible Two-way Serial Driver (SmC 4574) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Hosted Analog Input Object (SmC 3563) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Hosted Analog Value Object (SmC 20238) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Hosted Analog Value Object (SmC 3576) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 Cresnet Information (SmC 6611) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 Front Panel (SmC 6606) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 Front Panel Buttons (SmC 6607) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 IO (SmC 6603) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 Relay (SmC 6604) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 System Control (SmC 6609) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 System Information (SmC 6610) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 System Monitor (SmC 6608) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | MPC3-102 User Program Initialize (SmC 6612) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Remote Analog Value Object (SmC 20241) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Remote Analog Value Object (SmC 3579) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Remote Binary Output Object (SmC 20234) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | Remote Binary Output Object (SmC 3568) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | ST-IO (SmC 136) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL hardware | XiO Cloud Room (SmC 8905) | processor hardware (Cresnet, COM, IR, relays...) | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | COM 2-Way Serial Driver (SmC 6301) | slot, port or extender inside an IP-ID device: not mapped to joins | 10 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM NAX Routing (SmC 7048) | slot, port or extender inside an IP-ID device: not mapped to joins | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM NAX RX (SmC 7049) | slot, port or extender inside an IP-ID device: not mapped to joins | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM NAX TX (SmC 7050) | slot, port or extender inside an IP-ID device: not mapped to joins | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM Preview Image (SmC 15765) | slot, port or extender inside an IP-ID device: not mapped to joins | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM Preview Image Location (SmC 15766) | slot, port or extender inside an IP-ID device: not mapped to joins | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM Preview Image Preview Images (SmC 15769) | slot, port or extender inside an IP-ID device: not mapped to joins | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM XIO Routing (SmC 6094) | slot, port or extender inside an IP-ID device: not mapped to joins | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | XIO Routing Subscribed Streams (SmC 6095) | slot, port or extender inside an IP-ID device: not mapped to joins | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Automatic Power Settings (SmC 15503) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Background Image From Local Drive (SmC 16471) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Background Image From Remote Drive (SmC 16472) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x HDMI In (SmC 7065) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Network (SmC 7074) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Stream Receive (SmC 7064) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Stream Transmit (SmC 7063) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Test Pattern Generator (SmC 16473) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Text Overlay (SmC 16789) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x USB (SmC 7075) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x USB Hotkey (SmC 16260) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TST-902 (EX) (SmC 5187) | slot, port or extender inside an IP-ID device: not mapped to joins | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | COM 2-Way Serial Driver (3-pin) (SmC 6361) | slot, port or extender inside an IP-ID device: not mapped to joins | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | Crestron Go Notification Widget Functions (SmC 7131) | slot, port or extender inside an IP-ID device: not mapped to joins | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x Background Image (SmC 16470) | slot, port or extender inside an IP-ID device: not mapped to joins | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36x HDMI Out (SmC 7067) | slot, port or extender inside an IP-ID device: not mapped to joins | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HDBT Compatible Two-way Serial Driver (SmC 4574) | slot, port or extender inside an IP-ID device: not mapped to joins | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TST-1080 Buttons (SmC 17308) | slot, port or extender inside an IP-ID device: not mapped to joins | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-1060 Buttons (SmC 5933) | slot, port or extender inside an IP-ID device: not mapped to joins | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-1070 Buttons (SmC 6841) | slot, port or extender inside an IP-ID device: not mapped to joins | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX200-C-2G-HDMI (SmC 3384) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX200-C-2G-VGA (SmC 3385) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | Crestron App Notification Widget Functions (SmC 5618) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DGE-100 HDMI In (SmC 5326) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DGE-100 HDMI Out (SmC 5327) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DGE-100 Two-way serial driver (SmC 5265) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DGE-100 USB (SmC 5263) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Automatic Power Settings (SmC 15514) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Background Image (SmC 16474) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Background Image From Local Drive (SmC 16475) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Background Image From Remote Drive (SmC 16476) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC HDMI In (SmC 15512) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC HDMI Out (SmC 15513) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Network (SmC 15516) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Stream Receive (SmC 15511) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Stream Transmit (SmC 15510) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Test Pattern Generator (SmC 16477) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC Text Overlay (SmC 15515) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC USB (SmC 15517) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-36xC USB Hotkey (SmC 16261) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 (v2) Automatic Power Settings (SmC 15425) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 (v2) HDMI Out (SmC 15424) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 (v2) Network (SmC 15428) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 (v2) Stream Receive (SmC 15423) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 Background Image (SmC 16703) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 Background Image From Local Drive (SmC 16704) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30 Background Image From Remote Drive (SmC 16705) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30 (v2) HDMI In (SmC 15440) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30 (v2) Network (SmC 15443) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30 (v2) Stream Transmit (SmC 15439) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30 Test Pattern Generator (SmC 16684) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760 DM In (SmC 15306) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760 Network (SmC 15307) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760 Stream Transmit (SmC 15303) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760 Test Pattern Generator (SmC 16670) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4K-SCALER-C DM Input Slot (SmC 5222) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4K-SCALER-C Scaler (SmC 5221) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4K-SCALER-C Two-way serial driver (SmC 5219) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-4K-100-C-1G HDMI (SmC 5224) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-HD Advanced (SmC 6235) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | From Remote Drive (SmC 16470) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-CTL-101 DIGI-IN Port (SmC 16983) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-CTL-101 HDMI In (SmC 16965) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-CTL-101 HDMI Out (SmC 16967) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-CTL-101 Network (SmC 16971) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-CTL-101 Relays (SmC 16970) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Advanced Settings (SmC 18375) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Camera Names (SmC 18371) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Config Names (SmC 18369) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 General Settings (SmC 18370) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Layouts (SmC 18367) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Scenario Names (SmC 18373) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 System (SmC 18366) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TPMC-3SMD Buttons (SmC 3488) | slot, port or extender inside an IP-ID device: not mapped to joins | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX201C-HDMI (SmC 2575) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX201C-VGA (SmC 2576) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX401C-CVBS (SmC 3874) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX401C-DP (SmC 3872) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX401C-HDMI (SmC 3871) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX401C-VGA (SmC 3873) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX4K202C-HDMI (SmC 5474) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2I-DMTX4K202C-HDMI2 (SmC 5475) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C3COM-3 Two-way serial driver (SmC 3472) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | CEN-ODT-C-POE Configuration Settings (SmC 6805) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | Crestron DB serial-controlled device (SmC 108) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Inputs (SmC 17537) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial NAX (SmC 17543) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Output 1 (SmC 17539) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Output 2 (SmC 17540) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Output 3 (SmC 17541) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Output 4 (SmC 17542) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Output Mixer 1 (SmC 17532) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Output Mixer 2 (SmC 17533) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Output Mixer 3 (SmC 17534) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial Output Mixer 4 (SmC 17535) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial RX Zone (SmC 17547) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NAX-AUD-IO-Commercial TX Source (SmC 17545) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D200 Automatic Power Settings (SmC 16768) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D200 HDMI Out (SmC 16767) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D200 Network (SmC 16771) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D200 Stream Receive (SmC 16766) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E20-2G HDMI In (SmC 16988) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E20-2G Network (SmC 16991) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E20-2G Stream Transmit (SmC 16987) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-200 Two-way serial driver (SmC 2946) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-200-C (SmC 4061) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-200-C DM Input Slot (SmC 2995) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-200-C Scaler (SmC 2994) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4K-SCALER-C (SmC 5313) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4KZ-SCALER-C (SmC 6431) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4KZ-SCALER-C DM Input Slot (SmC 6436) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4KZ-SCALER-C HDMI (SmC 6437) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4KZ-SCALER-C Scaler (SmC 6435) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-SCALER-C (SmC 3334) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-SCALER-C DM Input Slot (SmC 3339) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-SCALER-C Scaler (SmC 3338) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-SCALER-C Two-way serial driver (SmC 3336) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-200-C-2G (SmC 4058) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-201-C (SmC 4057) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-401-C (SmC 4059) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-401-C Two-way serial driver (SmC 3878) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-4K-202-C DMNet (SmC 5473) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM64x64 System (SmC 4053) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM64x64 USB Routing (SmC 4052) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (1-8) (SmC 6029) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (17-24) (SmC 6029) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (25-32) (SmC 6029) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (33-40) (SmC 6029) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (9-16) (SmC 6029) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-HD (41-48) (SmC 4872) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-HD (49-56) (SmC 4872) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-HD (57-64) (SmC 4872) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-O-C (1-8) (SmC 4894) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-O-C (17-24) (SmC 4894) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-O-C (9-16) (SmC 4894) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-O-HD (25-32) (SmC 5446) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-O-HD (33-40) (SmC 5446) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-O-HD (41-48) (SmC 5446) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4K-C Advanced (SmC 5212) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4K-CO-HD Output 1 (SmC 4759) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4K-CO-HD Output 2 (SmC 4760) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4K-HD Advanced (SmC 4752) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4K-HDO Output 1 (SmC 5324) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4K-HDO Output 2 (SmC 5325) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-C Advanced. (SmC 6345) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-CO-HD Output 1. (SmC 6295) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-CO-HD Output 2. (SmC 6296) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-HDO Output 1 (SmC 2445) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-HDO Output 2 (SmC 2446) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Advanced Settings (SmC 20134) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Camera Names (SmC 20131) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Config Names (SmC 20130) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Layouts (SmC 20128) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 Scenario Names (SmC 20133) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | IV-SAM-VX2 System (SmC 20127) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TS-1070 Buttons (SmC 6991) | slot, port or extender inside an IP-ID device: not mapped to joins | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | AM-200 AirMedia (SmC 6464) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | AM-200 AirMedia Administration (SmC 6684) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | AM-200 AirMedia OSD Control (SmC 6465) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | AM-200 Display Control (SmC 6463) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | AM-200 HDMI In (SmC 6461) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | AM-200 HDMI Out (SmC 6462) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2N-CBD Advanced Button Events (SmC 2736) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2N-CBD-P (SmC 2735) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2N-CBD-P Brightness and Bargraph Functions (SmC 2737) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2N-CBD-P Brightness and Bargraph Settings (SmC 2738) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | C2N-CBD-P Versiport (SmC 2739) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | COM 2-Way Serial Driver (SmC 6361) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | Crestron DB IR-controlled device (SmC 107) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DGE-1000 HDMI In (SmC 17636) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DGE-1000 HDMI Out (SmC 17637) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DGE-1000 USB (SmC 17631) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DGE-1000 USB2 (SmC 17633) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) Automatic Power Settings (SmC 15406) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) HDMI In 1 (SmC 15403) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) HDMI In 2 (SmC 15404) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) HDMI Out (SmC 15405) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) Network (SmC 15410) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) Stream Receive (SmC 15402) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) Stream Transmit (SmC 15401) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) Text Overlay (SmC 15407) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x (v2) USB (SmC 15411) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x Background Image (SmC 16772) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x Background Image From Local Drive (SmC 16773) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x Background Image From Remote Drive (SmC 16774) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35x Test Pattern Generator (SmC 16778) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) Automatic Power Settings (SmC 15419) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) HDMI In 1 (SmC 15416) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) HDMI In 2 (SmC 15417) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) HDMI Out (SmC 15418) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) Network (SmC 15421) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) Stream Receive (SmC 15415) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) Stream Transmit (SmC 15414) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) Text Overlay (SmC 15420) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC (v2) USB (SmC 15422) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC Background Image (SmC 16779) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC Background Image From Local Drive (SmC 16780) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC Background Image From Remote Drive (SmC 16781) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-35xC Test Pattern Generator (SmC 16782) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Automatic Power Settings (SmC 18566) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Background Image (SmC 18567) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Background Image From Local Drive (SmC 18568) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Background Image From Remote Drive (SmC 18569) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 BTS RX (SmC 20010) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 BTS TX (SmC 20009) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 DM H.264 TX (SmC 20261) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 eARC RX (SmC 20007) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 eARC TX (SmC 20006) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 HDMI 1 In (SmC 18561) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 HDMI 2 In (SmC 18562) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 HDMI Out (SmC 18565) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Multi-View Controls (SmC 19891) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Multi-View Text Overlay (SmC 18973) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Multi-View Window Controls (SmC 18974) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Network (SmC 18574) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Stream Receive (SmC 18559) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Stream Transmit (SmC 18558) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Test Pattern Generator (SmC 18573) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 Text Overlay (SmC 18570) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 USB (SmC 18575) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 USB Hotkey (SmC 18576) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 USB-C 1 In (SmC 18563) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-384 USB-C 2 In (SmC 18564) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30C (v2) Automatic Power Settings (SmC 15431) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30C (v2) HDMI Out (SmC 15430) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30C (v2) Network (SmC 15432) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30C (v2) Stream Receive (SmC 15429) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30C Background Image (SmC 16709) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30C Background Image From Local Drive (SmC 16710) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-D30C Background Image From Remote Drive (SmC 16711) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E10-E20 HDMI In (SmC 16384) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E10-E20 Network (SmC 16387) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E10-E20 Stream Transmit (SmC 16383) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30C (v2) HDMI In (SmC 15447) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30C (v2) Network (SmC 15448) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30C (v2) Stream Transmit (SmC 15446) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E30C Test Pattern Generator (SmC 16687) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760C DM In (SmC 15311) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760C Network (SmC 15312) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760C Stream Transmit (SmC 15308) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-NVX-E760C Test Pattern Generator (SmC 16671) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-RMC-4K-SCALER-C (SmC 5217) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-200-C-2G (SmC 3383) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-4K-202-C Two-way serial driver (SmC 5477) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-4KZ-100-C-1G (SmC 6747) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DM-TX-4KZ-100-C-1G HDMI (SmC 6750) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (1-8) (SmC 5441) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (17-24) (SmC 5441) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (25-32) (SmC 5441) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (33-40) (SmC 5441) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMB-4K-I-C (9-16) (SmC 5441) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-C Advanced (SmC 6345) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-HDO Output 1 (SmC 6428) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-4KZ-HDO Output 2 (SmC 6429) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-C-DSP Advanced (SmC 2660) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-CO-HD Output 1 (SmC 2662) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-CO-HD Output 2 (SmC 2663) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | DMC-HD-DSP Advanced (SmC 1952) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | From Remote Drive (SmC 16474) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | From Remote Drive (SmC 16703) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | From Remote Drive (SmC 16709) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-MD4x1-4KZ-E AUX Output (SmC 15233) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-MD4x1-4KZ-E HDMI Input 1 (SmC 15226) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-MD4x1-4KZ-E HDMI Input 2 (SmC 15227) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-MD4x1-4KZ-E HDMI Input 3 (SmC 15228) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-MD4x1-4KZ-E HDMI Input 4 (SmC 15229) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-MD4x1-4KZ-E HDMI Output (SmC 15232) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-MD4x1-4KZ-E Network (SmC 15234) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Audio (SmC 18915) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Aux 1 Mixer (SmC 18942) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Aux 2 Mixer (SmC 18943) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Background Image 1 (SmC 18927) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Background Image 2 (SmC 18928) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Background Image From Local Drive 1 (SmC 18929) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Background Image From Local Drive 2 (SmC 18930) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Background Image From Remote Drive 1 (SmC 18931) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Background Image From Remote Drive 2 (SmC 18932) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 DM 1 (SmC 18919) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 DM 2 (SmC 18920) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 HDMI Input 1 (SmC 18911) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 HDMI Input 2 (SmC 18912) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 HDMI Input 3 (SmC 18913) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 HDMI Input 4 (SmC 18914) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 HDMI Output 1 (SmC 18923) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 HDMI Output 2 (SmC 18924) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Image Overlay 1 (SmC 18933) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Image Overlay 2 (SmC 18934) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Image Overlay From Local Drive 1 (SmC 18935) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Image Overlay From Local Drive 2 (SmC 18936) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Image Overlay From Remote Drive 1 (SmC 18937) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Image Overlay From Remote Drive 2 (SmC 18938) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Mixer 1 (SmC 18939) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Mixer 2 (SmC 18940) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Network (SmC 18944) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Output 1 (SmC 18917) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Output 2 (SmC 18918) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Text Overlay 1 (SmC 18925) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | HD-PS402 Text Overlay 2 (SmC 18926) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TS-1080 Buttons (SmC 18711) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TS-770 Buttons (SmC 6973) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-1080 Buttons (SmC 18537) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-550 Buttons (SmC 4241) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-560 Buttons (SmC 5929) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-570 Buttons (SmC 6821) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-750 Buttons (SmC 3748) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-760 Buttons (SmC 5931) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | TSW-770 Buttons (SmC 6839) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL sub-device | USB-C 2 In (SmC 7067) | slot, port or extender inside an IP-ID device: not mapped to joins | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serial I/O (SmC 101) | pins in=102 out=2 | 18 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Make String Permanent (SmC 858) | pins in=20 out=0 | 17 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Stepper (SmC 98) | pins in=1 out=101 | 17 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple One Shots (SmC 21) | pins in=21 out=20 | 16 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog RAM (SmC 34) | pins in=348 out=245 | 14 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serial/Analog One-Shot (SmC 72) | pins in=1 out=2 | 14 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Truth Table (SmC 28) | pins in=4 out=2 | 14 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Button Presser (SmC 99) | pins in=1 out=1 | 13 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Oscillator (SmC 8) | pins in=1 out=1 | 13 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | press and hold v1.2.cmc (SmC 155) | pins in=1 out=2 | 13 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple NOT (SmC 418) | pins in=20 out=20 | 12 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | When (SmC 74) | pins in=1 out=1 | 12 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NOR (SmC 4) | pins in=7 out=1 | 11 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Comparison (Full Set) (SmC 492) | pins in=2 out=6 | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira State Control v3.3.umc (SmC 155) | pins in=7 out=7 | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Stepper with Progress & Reset (SmC 777) | pins in=2 out=79 | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor IP v3.3.umc (SmC 155) | pins in=5 out=5 | 8 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Level Control v3.3.umc (SmC 155) | pins in=13 out=13 | 8 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Clock Driver (SmC 43) | pins in=0 out=1 | 8 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Debounce (SmC 50) | pins in=1 out=1 | 8 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Ethernet Offline Manager. (SmC 633) | pins in=2 out=1 | 8 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Display Control v2.3.umc (SmC 155) | pins in=8 out=9 | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Display Helper.usp (SmC 103) | pins in=32 out=32 | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Scaler with I/O Limits (SmC 541) | pins in=1 out=1 | 6 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Router Control v3.3.umc (SmC 155) | pins in=9 out=9 | 6 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Ethernet Offline Manager (SmC 511) | pins in=2 out=1 | 6 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Interlock-Toggle (SmC 419) | pins in=22 out=20 | 6 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Ring Counter with Seed (SmC 451) | pins in=4 out=2 | 6 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Preset Control v3.3.umc (SmC 155) | pins in=10 out=10 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Control Crosspoint Routing. (SmC 447) | pins in=161 out=117 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Equipment Crosspoint Routing. (SmC 446) | pins in=161 out=117 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Full 101+ Keyboard v1.0.cmc (SmC 155) | pins in=68 out=3 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intersystem Communications (SmC 327) | pins in=3 out=257 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple Discrete Serial Send (SmC 3106) | pins in=59 out=59 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Multicast Address Assigner.usp (SmC 103) | pins in=354 out=300 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Video Router.usp (SmC 103) | pins in=134 out=145 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Retriggerable One Shot (SmC 10) | pins in=3 out=2 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Ring Counter (SmC 17) | pins in=3 out=2 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Activity Detection. (SmC 1069) | pins in=0 out=2 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Increment with Optional Feedback (SmC 605) | pins in=4 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Ramp (SmC 22) | pins in=3 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Sum (SmC 57) | pins in=64 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge (Multiple Units) Macros v1.7.umc (SmC 155) | pins in=2 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Analog Remap.usp (SmC 103) | pins in=325 out=212 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Analog.usp (SmC 103) | pins in=1 out=79 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Reader.usp (SmC 103) | pins in=2 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Serial.usp (SmC 103) | pins in=1 out=41 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Digital Sum (SmC 61) | pins in=8 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Epson PowerLite Pro G5150NL + G5200WNL + G5350NL Full Control.cmc (SmC 155) | pins in=42 out=42 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple Router.umc (SmC 155) | pins in=408 out=305 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | press and hold2.cmc (SmC 155) | pins in=1 out=3 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Button] V5.2.umc (SmC 155) | pins in=7 out=4 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Level Control] V5.2.umc (SmC 155) | pins in=12 out=8 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [String - Value] V5.2.umc (SmC 155) | pins in=7 out=5 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serial Gather (SmC 62) | pins in=1 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SerialHasValue.umc (SmC 155) | pins in=32 out=32 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure MXWAPT.umc (SmC 155) | pins in=152 out=240 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony EVI-D100 Camera Control (25 Presets).cmc (SmC 155) | pins in=61 out=61 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | System Boot.umc (SmC 155) | pins in=47 out=47 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | System Presets.umc (SmC 155) | pins in=872 out=872 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE-FLEX-TEAMS Meeting Room Reserved Joins (SmC 16666) | pins in=75 out=75 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE-FLEX-TEAMS System4 Reserved Joins (SmC 16514) | pins in=501 out=468 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | udsrly_mom.cmc (SmC 155) | pins in=4 out=6 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | wait_for_initialization.usp (SmC 103) | pins in=0 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ANALOG BUFFER.usp (SmC 103) | pins in=2 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Increment (SmC 543) | pins in=3 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Value Counter.usp (SmC 103) | pins in=8 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AnalogHasValue.umc (SmC 155) | pins in=32 out=32 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Auto Sync Detect v2.0.umc (SmC 155) | pins in=32 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor IP v1.6.umc (SmC 155) | pins in=110 out=110 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira VoIP Control v3.3.umc (SmC 155) | pins in=135 out=135 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Room Devices 4.0.umc (SmC 155) | pins in=769 out=769 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco SX80 v2.3.9.2 SG.umc (SmC 155) | pins in=331 out=334 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge Dialing v1.8.umc (SmC 155) | pins in=54 out=54 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge Feedback Processor v1.7.umc (SmC 155) | pins in=1 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cn-spw2.cmc (SmC 155) | pins in=15 out=4 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Analog Reroute.usp (SmC 103) | pins in=133 out=67 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Digitals.usp (SmC 103) | pins in=1 out=69 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Equipment/Control Crosspoint Connect. (SmC 448) | pins in=7 out=0 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | INOGENI TOGGLE ROOMS v2.umc (SmC 155) | pins in=49 out=49 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | LBISS GE Text Dial v1.00.usp (SmC 103) | pins in=5 out=2 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Logic Wave Delay (SmC 408) | pins in=1 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Notification Service Issuer.usp (SmC 103) | pins in=2 out=0 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Notification Service Notifier.usp (SmC 103) | pins in=3 out=9 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Panasonic PT-RZ570 v1.0.umc (SmC 155) | pins in=47 out=47 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Panasonic_PTZ_Camera_Series-HE-UE-UB-HR.umc (SmC 155) | pins in=68 out=68 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Password_v1_1.cmc (SmC 155) | pins in=47 out=10 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Trigger Button] V5.2.umc (SmC 155) | pins in=3 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serial Concatenation (SmC 126) | pins in=4 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Set System Clock (SmC 154) | pins in=4 out=0 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | shure mic battery v2.0.usp (SmC 103) | pins in=22 out=22 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simple Math.usp (SmC 103) | pins in=23 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SSH Interface v1.8.cmc (SmC 155) | pins in=8 out=8 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | String Length (SmC 2506) | pins in=1 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Touchpanel Sleep/Wake Manager (SmC 392) | pins in=2 out=0 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TP Basic v0.9.01.umc (SmC 155) | pins in=378 out=378 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TV Presets Channel Control v1.0.cmc (SmC 155) | pins in=5 out=18 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE-FLEX-TEAMS System2 Reserved Joins (SmC 16513) | pins in=178 out=83 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Alarm Clock v2.0.cmc (SmC 155) | pins in=25 out=28 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog to Serial (SmC 77) | pins in=5 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Are You Sure.usp (SmC 103) | pins in=104 out=104 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ASCII Keypad (SmC 123) | pins in=130 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Automation Helper.usp (SmC 103) | pins in=3 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Initialize v2.x.umc (SmC 155) | pins in=4 out=3 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Level Control v2.3.umc (SmC 155) | pins in=10 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira State Control v2.3.umc (SmC 155) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira VoIP Control v2.3.umc (SmC 155) | pins in=133 out=133 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | BottomBarNavigationButtonRoomControl (SmC 2942) | pins in=16 out=8 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | BottomBarNavigationButtonSettings (SmC 2942) | pins in=10 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | BottomBarNavigationRoomControl (SmC 2942) | pins in=8 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ButtonsCameraControl (SmC 2942) | pins in=40 out=20 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ButtonsMicRouting (SmC 2942) | pins in=11 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ButtonsPresent (SmC 2942) | pins in=8 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ButtonsVideoCall (SmC 2942) | pins in=8 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ButtonsZoomCall (SmC 2942) | pins in=10 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ButtonVolumes (SmC 2942) | pins in=26 out=20 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | CamerasButtonRoomControl (SmC 2942) | pins in=46 out=23 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | CeilingMicButtonRoomControl (SmC 2942) | pins in=20 out=10 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Christie Proj Basic r4.umc (SmC 155) | pins in=13 out=13 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge (Multiple Units) Single Channel Output Control with Setup v1.7.umc (SmC 155) | pins in=20 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge (Multiple Units) Single Channel Volume Control v1.7.umc (SmC 155) | pins in=8 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Clock Driver w/out DST (SmC 3078) | pins in=0 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Crestron Certified Drivers TV Tuner Ethernet v1.9.cmc (SmC 155) | pins in=258 out=258 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DestinationButtonPresentation (SmC 2942) | pins in=128 out=64 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Digital First Passthru.usp (SmC 103) | pins in=11 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplaysButtonRoomControl (SmC 2942) | pins in=8 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DM-RMC-SCALER setup.umc (SmC 155) | pins in=30 out=6 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DM64x64 Naming (SmC 4054) | pins in=1064 out=0 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DMXLightsButtonRoomControl (SmC 2942) | pins in=38 out=19 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Exclusive OR (SmC 5) | pins in=2 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ext_channelpresets_x50.cmc (SmC 155) | pins in=58 out=72 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | FocusButtonPresentation (SmC 2942) | pins in=15 out=8 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HD-MD-400-C-E v1.0.cmc (SmC 155) | pins in=136 out=136 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HP_BiampRouterControl.usp (SmC 103) | pins in=1 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | LBISS NEC TV.umc (SmC 155) | pins in=52 out=52 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | LightsButtonRoomControl (SmC 2942) | pins in=10 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Logic Wave Pulse (SmC 409) | pins in=3 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicLevelsButtonRoomControl (SmC 2942) | pins in=54 out=42 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneButtonMicRouting (SmC 2942) | pins in=141 out=141 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2441 VC v1.04_[101-Presentation] 4-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2441 VC v1.04_[101-Presentation] 4-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2441 VC v1.04_[102-Video Conference] 3-Directory_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2441 VC v1.04_[102-Video Conference] 4-Camera Controls_CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2441 VC v1.04_[102-Video Conference] 4-Camera Controls_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2441 VC v1.04_[103-Lync] 1-Camera Controls_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2441 VC v1.04_[104-Audio Conference] 1-AC Dial_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[101-Presentation Separate] 4-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[101-Presentation Separate] 4-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[102-Video Conference Separate] 3-Directory_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[102-Video Conference Separate] 4-Camera Controls_CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[102-Video Conference Separate] 4-Camera Controls_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[103-Lync Separate] 1-Camera Controls_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[104-Audio Conference Separate] 1-AC Dial_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[104-Audio Conference Separate] 1-AC Dial_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[201-Presenation Combined] 4-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[201-Presenation Combined] 4-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[202-Video Conference Combined] 3-Directory_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[202-Video Conference Combined] 4-Camera Controls_CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[202-Video Conference Combined] 4-Camera Controls_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[203-Lync Combined] 1-Camera Controls_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[204-Audio Conference Combined] 1-AC Dial_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2443 VC v1.03_[204-Audio Conference Combined] 1-AC Dial_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2450 VC v1.10_[101-Presentation] 4-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2450 VC v1.10_[101-Presentation] 4-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2450 VC v1.10_[102-Video Conference] 3-Directory_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2450 VC v1.10_[102-Video Conference] 4-Camera Controls_CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2450 VC v1.10_[102-Video Conference] 4-Camera Controls_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2450 VC v1.10_[103-Lync] 1-Camera Controls_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van iPad 2450 VC v1.10_[104-Audio Conference] 1-AC Dial_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.04_[Advance Page] 02-Cable TV_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.04_[Advance Page] 02-Cable TV_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[101-Present] [101-Presentation] 3-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[101-Present] [101-Presentation] 3-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 3-Directory-2263_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 3-Directory-2339_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 3-Directory-2429_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 3-Directory-2441_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 3-Directory-2443_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 3-Directory-2444_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 3-Directory-2447_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 3-Directory-2450_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2263_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2263_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2339_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2339_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2429_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2429_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2441_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2441_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2444_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2444_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2447_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2447_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2450_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2450_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [103-Lync] 1-Camera Controls-2263_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [103-Lync] 1-Camera Controls-2339_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [103-Lync] 1-Camera Controls-2429_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [103-Lync] 1-Camera Controls-2441_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [103-Lync] 1-Camera Controls-2443_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [103-Lync] 1-Camera Controls-2444_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [103-Lync] 1-Camera Controls-2447_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [103-Lync] 1-Camera Controls-2450_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2263_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2263_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2339_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2339_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2429_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2429_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2441_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2441_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2443_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2443_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2444_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2444_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2447_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2447_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2450_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2450_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[Advance Page] 02-Cable TV_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.07_[Advance Page] 02-Cable TV_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [101-Presentation] 3-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [101-Presentation] 3-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 3-Directory-2263_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 3-Directory-2339_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 3-Directory-2429_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 3-Directory-2441_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 3-Directory-2443_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 3-Directory-2444_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 3-Directory-2447_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 3-Directory-2450_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2263_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2263_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2339_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2339_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2429_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2429_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2441_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2441_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443-2 screen_LYNC CAMERA CTL_6.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443-4 screen_LYNC CAMERA CTL_7.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2444_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2444_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2447_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2447_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2450_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2450_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2263_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2339_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2429_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2441_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2443-2 screen_LYNC CAMERA CTL_8.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2443-4 screen_LYNC CAMERA CTL_9.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2443_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2444_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2447_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [103-Lync] 1-Camera Controls-2450_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2263_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2263_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2339_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2339_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2429_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2429_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2441_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2441_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2443_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2443_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2444_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2444_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2447_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2447_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2450_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2450_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[Advance Page] 02-Cable TV_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Tech iPad v1.13_[Advance Page] 02-Cable TV_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[101-Presentation Separate] 4-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[101-Presentation Separate] 4-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[102-Video Conference Separate] 3-Directory_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[102-Video Conference Separate] 4-Camera Controls_CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[102-Video Conference Separate] 4-Camera Controls_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[103-Lync Separate] 1-Camera Controls_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[104-Audio Conference Separate] 1-AC Dial_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[104-Audio Conference Separate] 1-AC Dial_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[201-Presenation Combined 2] 4-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[201-Presenation Combined 2] 4-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[202-Video Conference Combined 2] 3-Directory_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[202-Video Conference Combined 2] 4-Camera Controls_CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[202-Video Conference Combined 2] 4-Camera Controls_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[203-Lync Combined 2] 1-Camera Controls_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[204-Audio Conference Combined 2] 1-AC Dial_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2443 VC v1.11_[204-Audio Conference Combined 2] 1-AC Dial_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2444 VC v1.11_[101-Presentation] 4-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2444 VC v1.11_[101-Presentation] 4-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2444 VC v1.11_[102-Video Conference] 3-Directory_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2444 VC v1.11_[102-Video Conference] 4-Camera Controls_CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2444 VC v1.11_[102-Video Conference] 4-Camera Controls_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2444 VC v1.11_[103-Lync] 1-Camera Controls_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MT Van Web 2444 VC v1.11_[104-Audio Conference] 1-AC Dial_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multi-Analog Comparison.usp (SmC 103) | pins in=3 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple Press Enable.umc (SmC 155) | pins in=34 out=34 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NAND (SmC 2) | pins in=2 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVRAM Toggle.usp (SmC 103) | pins in=3 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | pj Helper.umc (SmC 155) | pins in=15 out=7 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Poll Manager (SmC 345) | pins in=2 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | PopupButtonMicRouting (SmC 2942) | pins in=23 out=12 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | PowerOffButtons (SmC 2942) | pins in=6 out=3 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | press and hold v2.umc (SmC 155) | pins in=1 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ProjectorButtonRoomControl (SmC 2942) | pins in=40 out=20 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pulse Stretcher (SmC 11) | pins in=1 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Core] V5.2.0 RM.umc (SmC 155) | pins in=9 out=10 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Core] V5.2.0.umc (SmC 155) | pins in=9 out=9 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Generic Boolean Named Control.usp (SmC 103) | pins in=3 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Room Combiner.umc (SmC 155) | pins in=66 out=99 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Room Settings v1.1.usp (SmC 103) | pins in=8 out=103 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | RoomsButtonSettings (SmC 2942) | pins in=22 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SACN SenderDirectSockets.cmc (SmC 155) | pins in=512 out=0 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serial Force (SmC 470) | pins in=251 out=0 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serial pacer.usp (SmC 103) | pins in=6 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serial to Analog (SmC 96) | pins in=1 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SERIALIZE_TIME_2.usp (SmC 103) | pins in=1 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ShadesButtonRoomControl (SmC 2942) | pins in=12 out=7 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure P300 v1.1.usp (SmC 103) | pins in=362 out=389 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shutdown.umc (SmC 155) | pins in=2 out=24 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SideBarNavigationButton (SmC 2942) | pins in=10 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SignageButtonSettings (SmC 2942) | pins in=30 out=15 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simple Alarm Clock.usp (SmC 103) | pins in=11 out=7 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simple LG Display DK.umc (SmC 155) | pins in=8 out=7 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SmartViewGlassSplus.usp (SmC 103) | pins in=4 out=0 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony EVI-D1-- Camera Control (25 Preset w IL clear).umc (SmC 155) | pins in=61 out=61 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony_BRC-X400_SRG-X_series.usp (SmC 103) | pins in=94 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtonPresentation (SmC 2942) | pins in=107 out=54 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TS-1070 Button Toolbar (SmC 6996) | pins in=23 out=23 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TST-1080 System4 Reserved Joins (SmC 17320) | pins in=289 out=289 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TST-902 RF/WiFi Reserved Joins (SmC 4722) | pins in=69 out=103 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE-FLEX-TEAMS UC-Engine Reserved Joins (SmC 16515) | pins in=300 out=213 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | udsrly_int.cmc (SmC 155) | pins in=4 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UInt64 Bit Mask.usp (SmC 103) | pins in=12 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | USB-EXT-DM Control v1.0.cmc (SmC 155) | pins in=4 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WallStateButtonSettings (SmC 2942) | pins in=12 out=6 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 10 Alarms HM Plus Minus Entry w Rollover.umc (SmC 155) | pins in=52 out=63 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_SLF_1_YRK_ 35024_30_31_2019-01-22_[Pages] 2 Video_Subpage Reference List Horizontal.ced (SmC 2942) | pins in=4061 out=4043 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_SLF_1_YRK_35041_2019-01-22_[Pages] 2 Video_Subpage Reference List Horizontal.ced (SmC 2942) | pins in=4061 out=4043 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_SLF_1_YRK_35_FLR_ 902_2019-01-24_[Sub Pages] [Mics] Mics_subref_1 lectern_Subpage Reference List Horizontal.ced (SmC 2942) | pins in=4140 out=4108 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_SLF_1_YRK_35_FLR_1060 2019-01-14_[Pages] 7 Cameras_CAM DPAD.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_SLF_1_YRK_35_FLR_1060 2019-01-14_[Sub Pages] [Utility] Projectors Screens_Projectors.ced (SmC 2942) | pins in=4117 out=4070 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_SLF_1_YRK_35_FLR_1060 2019-01-14_[Sub Pages] [Vid Conf] vc_Directory_VC Contacts.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_Sunlife_1Y_CMT_ 902-2019-01-23_[Sub Pages] [Vid Conf] vc_Camera_CAM DPAD.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_Sunlife_1Y_CMT_ 902-2019-01-23_[Sub Pages] [Vid Conf] vc_Directory_VC Contacts.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_Sunlife_OY 34 EBR 902_2022-03-08_[Sub Pages] [System] Password_Password Keypad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_Sunlife_OY 34 EBR 902_2022-03-08_[Sub Pages] [Vid Conf] vc_Camera_CAM DPAD.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_Sunlife_OY 34 EBR 902_2022-03-08_[Sub Pages] [Vid Conf] vc_Directory_VC Contacts.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 119241_Sunlife_OY 34 EBR 902_2022-03-08_[Sub Pages] [Video] tv_Transport_TV_DPad.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 16x16 Source Filter r2.umc (SmC 155) | pins in=34 out=16 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 1Beyond AutoTracker_IV-CAMA3_v1.0.cmc (SmC 155) | pins in=55 out=55 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 1x32 Source Filter r2.umc (SmC 155) | pins in=35 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 1x64 Source Filter r2.umc (SmC 155) | pins in=67 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 40 Analog 40 Digital Aram.umc (SmC 155) | pins in=182 out=182 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 5WayCombineRoomConfigurationsAndWallStuff.umc (SmC 155) | pins in=4 out=91 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 5WayCombineWallsOverride.umc (SmC 155) | pins in=13 out=8 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 70x70 Source Filter r2.umc (SmC 155) | pins in=142 out=70 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | add nums.usp (SmC 103) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Addressbook File Operations v2.0(DG).usp (SmC 103) | pins in=12 out=109 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Alarm Clock v2.1.umc (SmC 155) | pins in=25 out=28 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Compare (SmC 35) | pins in=3 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Compare Pulse.usp (SmC 103) | pins in=2 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Delay.usp (SmC 103) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog DivMod (SmC 114) | pins in=1 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Patch r1.umc (SmC 155) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Step (SmC 45) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | analog switcher 50 x 1.umc (SmC 155) | pins in=53 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AnalogToString.usp (SmC 103) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AnalogValueMatch.usp (SmC 103) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ASCII Serial Decoder (SmC 54) | pins in=2 out=59 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ATCDialKeypad3 (SmC 2942) | pins in=21 out=21 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ATOI s+.usp (SmC 103) | pins in=10 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Auto Adjust Font Size v1.0.usp (SmC 103) | pins in=4 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Auto Sync Detect Queue v2.0.usp (SmC 103) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AutoSwitch (Rev 1-00-0).usp (SmC 103) | pins in=5 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Aver-Camera v2.0.umc (SmC 155) | pins in=142 out=138 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Aver-Camera.umc (SmC 155) | pins in=46 out=42 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Aver-Camera_IP_v2.umc (SmC 155) | pins in=179 out=179 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Aver-Camera_RS_v2.umc (SmC 155) | pins in=179 out=179 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AVoIP Router.usp (SmC 103) | pins in=91 out=61 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor IP v2.2.umc (SmC 155) | pins in=5 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor IP v2.3.umc (SmC 155) | pins in=5 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor RS232 v2.3.umc (SmC 155) | pins in=4 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor RS232 v3.3.umc (SmC 155) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Level Control (Rev 0-00-0).usp (SmC 103) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Level Control v2.0.umc (SmC 155) | pins in=10 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Preset Control v2.3.umc (SmC 155) | pins in=10 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Router Control v2.3.umc (SmC 155) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira State Control v2.0.umc (SmC 155) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira VoIP Control v2.2.umc (SmC 155) | pins in=133 out=133 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira VoIP States.umc (SmC 155) | pins in=2 out=32 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Black Magic Video Hub 70 x 70 r4.usp (SmC 103) | pins in=52 out=132 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | blink_fb.usp (SmC 103) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | BottomBarNavigationButton.ButtonObject[0] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | BottomBarNavigationButton.ButtonObject[1] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | BottomBarNavigationButton.ButtonObject[2] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ButtonsMainPage (SmC 2942) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ButtonsPresets (SmC 2942) | pins in=158 out=79 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | CABLE_DPAD (SmC 2942) | pins in=13 out=13 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Camera Preset controller r1.umc (SmC 155) | pins in=281 out=281 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Camera Preset controller r2.umc (SmC 155) | pins in=281 out=281 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Camera Tracking.umc (SmC 155) | pins in=33 out=37 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | CameraSourceSelections.Audience (SmC 2942) | pins in=30 out=20 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | CameraSourceSelections.Presenter (SmC 2942) | pins in=30 out=20 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cart Mic Routing.usp (SmC 103) | pins in=60 out=61 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Camera Preset Recall_store.umc (SmC 155) | pins in=45 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco SX80 v2.3.9 preset 35 savedif.umc (SmC 155) | pins in=346 out=346 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco SX80 v2.3.9 preset35.umc (SmC 155) | pins in=344 out=344 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco SX80 v2.3.9.umc (SmC 155) | pins in=324 out=324 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco SX80 v2.3.umc (SmC 155) | pins in=315 out=315 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Touch 10 Room Control Processor v1.0.usp (SmC 103) | pins in=3 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Touch 10 Room Control Slider v1.0.usp (SmC 103) | pins in=3 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Cisco Touch 10 Room Control Toggle Button v1.0.usp (SmC 103) | pins in=4 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | CiscoTeamsCallState.usp (SmC 103) | pins in=3 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge VH20 Dialing v1.7.umc (SmC 155) | pins in=40 out=40 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Clearone ConvergePro2 AnalogDialer Control v1.4.umc (SmC 155) | pins in=35 out=35 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Clock Audio CDT100 mkI v3.3.1.umc (SmC 155) | pins in=105 out=105 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Clock Audio CDT100 mkI v3.4.1.umc (SmC 155) | pins in=105 out=105 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Clock Audio CDT100 mkII v3.3.1.umc (SmC 155) | pins in=141 out=141 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Clock Audio CDT100 mkIII v3.4.1.umc (SmC 155) | pins in=151 out=151 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Clock Audio CDT100 v2.1.umc (SmC 155) | pins in=173 out=173 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Colon Cleanser.usp (SmC 103) | pins in=8 out=8 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Countdown Timer with Analog Timer Output.umc (SmC 155) | pins in=2 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Crestron Certified Drivers Display Serial v2.0.cmc (SmC 155) | pins in=900 out=900 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Crestron Device Drivers Display Ethernet v2.3.cmc (SmC 155) | pins in=898 out=898 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DENON DN-700R Full Control v1.0.umc (SmC 155) | pins in=722 out=859 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DestinationGroup.DestinationButton[0] (SmC 2942) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DestinationGroup.DestinationButton[1] (SmC 2942) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DestinationGroup.DestinationButton[2] (SmC 2942) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DestinationGroup.DestinationButton[3] (SmC 2942) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DestinationGroup.DestinationButton[4] (SmC 2942) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DestinationGroup.DestinationButton[5] (SmC 2942) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DestinationGroup.DestinationButton[6] (SmC 2942) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Device-Pwr-IsItOn.umc (SmC 155) | pins in=2 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DigitalDelay.usp (SmC 103) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Display Input 2015.umc (SmC 155) | pins in=13 out=13 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonRoomControl (SmC 2942) | pins in=12 out=9 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[0] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[10] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[11] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[12] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[13] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[14] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[15] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[16] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[1] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[2] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[3] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[4] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[5] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[6] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[7] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[8] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayButtonsSettings.DisplayButtonSettings[9] (SmC 2942) | pins in=3 out=0 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DisplayObject (SmC 2942) | pins in=3 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DM-NUX v1.1.cmc (SmC 155) | pins in=19 out=19 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DMPS3-AUDIO-OFFSET-VALUE.usp (SmC 103) | pins in=3 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Dpad-Camera (SmC 2942) | pins in=13 out=13 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Elvium [21111] [Combine] [UC ENGINE] 2021-06-08_Present_Presentation Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Elvium [21111] [Combine] [UC ENGINE] 2021-06-08_[Popup] Mics admin_TableMicsSubpageReferenceList.ced (SmC 2942) | pins in=4260 out=4226 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Elvium [21111] [Combine] [UC ENGINE] 2021-06-08_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Elvium [21111] [Combine] [UC ENGINE] 2021-10-20_Present_Presentation Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Elvium [21111] [Combine] [UC ENGINE] 2021-10-20_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Epson PowerLite Pro Z8450WU v1.0.umc (SmC 155) | pins in=63 out=63 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Extron SW HD 4K Series.umc (SmC 155) | pins in=3 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | First Value Buffer.usp (SmC 103) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Generic CEC Display Control v1.3.cmc (SmC 155) | pins in=24 out=24 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Generic CEC Source Control v1.0.cmc (SmC 155) | pins in=109 out=109 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Generic CEC Source Control v1.1.cmc (SmC 155) | pins in=109 out=109 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HD-MD4x1-4K-E v1.1.cmc (SmC 155) | pins in=72 out=72 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Command Center_[Present] Video Routing - Sources_Video Routing - Sources.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Page] Present_Presentation Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Page] Settings_Settings Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Present] Audio_Audio Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Present] Audio_Audio Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Present] Video_Presentation Video Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Present] Video_Presentation Video Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Room Control] Cable TV_Cable TV Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4055 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Room Control] Mics - Adv_Subpage Ref - Mics.ced (SmC 2942) | pins in=4425 out=4358 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Fitness Center_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Modal Popup] Volume_Subpage Ref - Volume.ced (SmC 2942) | pins in=4160 out=4124 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Page] 02 Video Routing_Sources Local.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Page] 02 Video Routing_Video Destinations.ced (SmC 2942) | pins in=4223 out=4083 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Page] 03 Video Call_Video Call Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Page] 04 Microphones_Subpage Ref - Mics.ced (SmC 2942) | pins in=5096 out=4952 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Page] 05 Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Page] 06 System_System Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Page] 07 Presets - Edit_Presets Include List.ced (SmC 2942) | pins in=4171 out=4089 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Page] 07 Presets_Presets.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Room Control] Blinds or Shades_Subpage Ref - Shades.ced (SmC 2942) | pins in=4127 out=4091 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Room Control] Cable TV_Cable TV Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4091 out=4059 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Room Control] DMX Fixtures_Fixture List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Room Control] Lights_Subpage Ref - Lights.ced (SmC 2942) | pins in=4207 out=4099 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Room Control] Projectors_Subpage Ref - Projectors.ced (SmC 2942) | pins in=4191 out=4163 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Room Control] Shure Mics_Subpage Ref - Shure Mics.ced (SmC 2942) | pins in=4061 out=4039 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[System Info] Displays_Subpage Ref - Displays Info.ced (SmC 2942) | pins in=4125 out=4017 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[System Info] NVX_Subpage Ref - NVX Info.ced (SmC 2942) | pins in=4751 out=4259 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4044 out=4044 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Video Call] Camera Control-2-Routing_Camera 1 Select.ced (SmC 2942) | pins in=4051 out=4029 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Video Call] Camera Control-2-Routing_Camera 2 Select.ced (SmC 2942) | pins in=4051 out=4029 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Video Call] Camera Control_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4051 out=4029 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme Tech & Cart_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Page] Present_Presentation Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Page] Settings_Settings Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Page] Video Call_Video Call Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Present] Video_Presentation Video Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Room Control] Cable TV_Cable TV Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4055 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Settings] Camera - Scenarios_Camera Tracking Scenarios.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Settings] System Info - NVX_Subpage Ref - NVX Info.ced (SmC 2942) | pins in=4232 out=4100 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Video Call] Camera Control_Camera Tracking Layouts.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Video Call] Camera Control_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4063 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine Intact_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Page] Present_Presentation Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Page] Room Control_Room Control Page List_2.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Page] Settings_Settings Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Page] Video Call_Video Call Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Present] Audio_Presentation Audio Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Present] Video_Presentation Video Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] Blinds or Shades_Subpage Ref - Shades.ced (SmC 2942) | pins in=4127 out=4091 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] Cable TV_Cable TV Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] Cable TV_Splash CATV Preset List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] Cable TV_Splash CATV Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4055 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] DMX Fixtures-1_Fixture List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] Lights_Subpage Ref - Lights.ced (SmC 2942) | pins in=4207 out=4099 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] Mics - Adv_Subpage Ref - Mics.ced (SmC 2942) | pins in=4480 out=4413 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Room Control] Projectors_Subpage Ref - Projectors.ced (SmC 2942) | pins in=4191 out=4163 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4044 out=4044 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Video Call] Camera Control-2-Routing_Camera 1 Select.ced (SmC 2942) | pins in=4051 out=4029 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Video Call] Camera Control-2-Routing_Camera 2 Select.ced (SmC 2942) | pins in=4051 out=4029 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Video Call] Camera Control_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4063 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HitPlayUITheme UCEngine_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HomePageButtonList (SmC 2942) | pins in=516 out=1514 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Page] Present_Presentation Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Page] Settings_Settings Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Page] Video Call_Video Call Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Present] Video-Matrix_Sources Local.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Present] Video-Matrix_Video Destinations.ced (SmC 2942) | pins in=4223 out=4083 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Present] Video_Presentation Video Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Blinds or Shades_Subpage Ref - Shades.ced (SmC 2942) | pins in=4063 out=4043 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Cable TV_Cable TV Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Cable TV_Splash CATV Preset List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Cable TV_Splash CATV Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4079 out=4051 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Lights_Subpage Ref - Lights.ced (SmC 2942) | pins in=4051 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Mics - Adv_Subpage Ref - Mics.ced (SmC 2942) | pins in=4425 out=4358 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Projectors_Subpage Ref - Projectors.ced (SmC 2942) | pins in=4131 out=4109 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Room Control] Table Layout_Subpage Ref - Table Layouts.ced (SmC 2942) | pins in=4130 out=4088 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Video Call] Camera Control_Camera - Output Layouts.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Video Call] Camera Control_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4063 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-240443 GS1 UCEngine 3-1 Conf_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Page] Present_Presentation Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Page] Settings_Settings Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Page] Video Call_Video Call Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Room Control] Cable TV_Cable TV Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Room Control] Cable TV_Splash CATV Preset List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Room Control] Cable TV_Splash CATV Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4043 out=4027 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Room Control] Mics - Adv_Subpage Ref - Mics.ced (SmC 2942) | pins in=4208 out=4172 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Settings] NVX_Subpage Ref - NVX Info.ced (SmC 2942) | pins in=4136 out=4064 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Video Call] Camera Control VX_Camera Tracking Layouts.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Video Call] Camera Control VX_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4063 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Video Call] Camera Control_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4037 out=4022 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 MP UC_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Page] Present_Presentation Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Page] Settings_Settings Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Page] Video Call_Video Call Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Room Control] Cable TV_Cable TV Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Room Control] Cable TV_Splash CATV Preset List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Room Control] Cable TV_Splash CATV Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4043 out=4027 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Room Control] Mics - Adv_Subpage Ref - Mics.ced (SmC 2942) | pins in=4208 out=4172 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Settings] NVX_Subpage Ref - NVX Info.ced (SmC 2942) | pins in=4136 out=4064 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Video Call] Camera Control VX_Camera Tracking Layouts.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Video Call] Camera Control VX_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4063 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Video Call] Camera Control_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4037 out=4022 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HPt-251054 UC 13th Floor MPR_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_0-Home_NavMenu-MainPg.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_[f-Popup] Audio_Conf_Audio_Conf_KeyPad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_[f-Popup] Audio_Ctrl_Audio_Levels_SRLH.ced (SmC 2942) | pins in=4104 out=4084 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_[f-Popup] Camera_Ctrl_Cam_Nav.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_[f-Popup] Camera_Ctrl_Cam_Pst.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_[f-Popup] Camera_Ctrl_Cam_Sel_DHBL.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_[f-Popup] Display_Controls_Display_Ctrl_SRL.ced (SmC 2942) | pins in=4052 out=4037 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_[f-Popup] Switching_Destination_1.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Lecture_Room_TST902_[f-Popup] Switching_Sources_1.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_0-Home_NavMenu-MainPg.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Audio_Ctrl_Audio_Levels_SRLH.ced (SmC 2942) | pins in=4168 out=4140 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Audio_Ctrl_Audio_Levels_SRLH_Speakers.ced (SmC 2942) | pins in=4168 out=4140 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Cable_TV_Ctrl_Cable_TV_Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Cable_TV_Ctrl_CATV_NAV.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Cable_TV_Ctrl_CATV_Presets.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Cable_TV_Ctrl_CATV_Remote_Btns_1.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Cable_TV_Ctrl_CATV_Remote_Btns_2.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Cable_TV_Ctrl_Simple Keypad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Camera_Ctrl_Cam_Nav.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Camera_Ctrl_Cam_Pst.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Camera_Ctrl_Cam_Sel_DHBL.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Display_Controls_Display_Ctrl_SRL.ced (SmC 2942) | pins in=4066 out=4049 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Environment_Blinds.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Environment_Lights.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Environment_Shades.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Room_Controls_Preset_Source.ced (SmC 2942) | pins in=18 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Room_Controls_Room_Presets.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Switching_Destination_1.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_Main_Conf_Room_TST902_[f-Popup] Switching_Sources_1.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_0-Home_NavMenu-MainPg.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Audio_Conf_Audio_Conf_KeyPad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Audio_Ctrl_Audio_Levels_SRLH.ced (SmC 2942) | pins in=4168 out=4140 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Cable_TV_Ctrl_Cable_TV_Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Cable_TV_Ctrl_CATV_NAV.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Cable_TV_Ctrl_CATV_Presets.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Cable_TV_Ctrl_CATV_Remote_Btns_1.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Cable_TV_Ctrl_CATV_Remote_Btns_2.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Cable_TV_Ctrl_Simple Keypad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Camera_Ctrl_Cam_Nav.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Camera_Ctrl_Cam_Pst.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Camera_Ctrl_Cam_Sel_DHBL.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Display_Controls_Display_Ctrl_SRL.ced (SmC 2942) | pins in=4066 out=4049 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Environment_Blinds.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Environment_Lights.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Environment_Shades.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Room_Controls_Preset_Source.ced (SmC 2942) | pins in=18 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Room_Controls_Room_Presets.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Switching_Destination_1.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HSN_RCP4_TSW1060_[f-Popup] Switching_Sources_1.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Inogeni TOGGLE ROOMS Demo v1.0_Main_Tab Button Vertical.ced (SmC 2942) | pins in=6 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact Balsam (Rev 0-00-0)_Camera Control_DPad.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact Balsam (Rev 0-00-0)_PASSWORD_Simple Keypad_2.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact Balsam (Rev 0-00-0)_PRESENTATION_Presentation Sources.ced (SmC 2942) | pins in=4058 out=4040 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact Balsam (Rev 0-00-0)_Routing_Video Routing.ced (SmC 2942) | pins in=4058 out=4040 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact Balsam (Rev 0-00-0)_SELECTION_Button List Vertical.ced (SmC 2942) | pins in=12 out=9 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact Balsam (Rev 0-00-0)_TV Tuner_TV Tuner Keypad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact Balsam (Rev 0-00-0)_TV Tuner_TV Tuner Nav DPad.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact Balsam (Rev 0-00-0)_VConf_Content Sources.ced (SmC 2942) | pins in=4055 out=4038 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-FlexB-TS-770_[Room Control] Cable TV_Cable TV Selection.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-FlexB-UC Engine_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-FlexB-UC Engine_[Page] Room Control_Room Control Page List_2.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-FlexB-UC Engine_[Room Control] Cable TV_Splash CATV Preset List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-FlexB-UC Engine_[Room Control] Cable TV_Splash CATV Submenu.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-FlexB-UC Engine_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4055 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-FlexB-UC Engine_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurpose-UCEngine_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurpose-UCEngine_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurpose-UCEngine_[Present] Video_Presentation Video Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurpose-UCEngine_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4055 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurpose-UCEngine_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurpose-UCEngine_[Video Call] Camera Control_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4063 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Page] Present_Presentation Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Page] Settings_Settings Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Present] Video_Presentation Video Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4055 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Settings] Camera - Scenarios_Camera Tracking Scenarios.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[System Info] NVX_Subpage Ref - NVX Info.ced (SmC 2942) | pins in=4136 out=4064 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Video Call] Camera Control_Camera Tracking Layouts.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Intact-MultiPurposeA&B-UCEngine_[Video Call] Camera Control_Subpage Ref - Camera Select.ced (SmC 2942) | pins in=4063 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | IP Connection.umc (SmC 155) | pins in=3 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | iptv_59.usp (SmC 103) | pins in=61 out=60 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | IPTV_Channels (SmC 2942) | pins in=2008 out=1506 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Is It In Use 32 Sources x 32 Channels.umc (SmC 155) | pins in=32 out=66 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Lakeside [20164] [Combine] [UC ENGINE] 2020-12-03_Present_Presentation Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Lakeside [20164] [Combine] [UC ENGINE] 2020-12-03_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Lakeside [20164] [Combine] [Web] 2020-12-03_Present_Presentation Outputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Lakeside [20164] [Combine] [Web] 2020-12-03_[Video Call] Content_Presentation Inputs.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Language lookup.usp (SmC 103) | pins in=5 out=35 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Loading Bar v1.0.umc (SmC 155) | pins in=1 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | LYNCCAMERACTL7 (SmC 2942) | pins in=13 out=13 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MatrixListDestinations (SmC 2942) | pins in=46 out=1220 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MatrixListSources (SmC 2942) | pins in=102 out=666 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Memory Store.usp (SmC 103) | pins in=66 out=66 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.ButtonObject (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[0] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[10] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[1] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[2] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[3] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[4] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[5] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[6] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[7] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[8] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MicrophoneLevels.LevelObject[9] (SmC 2942) | pins in=5 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Mitsubishi Projector Control (Rev 1-00-1).umc (SmC 155) | pins in=11 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [101-Presentation] 3-Cable Box_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [101-Presentation] 3-Cable Box_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 3-Directory-2263_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 3-Directory-2339_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 3-Directory-2429_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 3-Directory-2441_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 3-Directory-2443_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 3-Directory-2444_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 3-Directory-2447_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 3-Directory-2450_Contacts Scrolling List.ced (SmC 2942) | pins in=516 out=261 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2263_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2263_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2339_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2339_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2429_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2429_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2441_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2441_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443-2 screen_LYNC CAMERA CTL_6.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443-4 screen_LYNC CAMERA CTL_7.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2443_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2444_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2444_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2447_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2447_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2450_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [102-Video Conference] 4-Camera Controls-2450_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2263_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2339_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2429_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2441_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2443-2 screen_LYNC CAMERA CTL_8.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2443-4 screen_LYNC CAMERA CTL_9.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2443_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2444_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2447_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [103-Lync] 1-Camera Controls-2450_LYNC CAMERA CTL.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2263_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2263_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2339_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2339_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2429_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2429_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2441_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2441_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2443_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2443_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2444_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2444_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2447_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2447_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2450_VTC KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[102-VC Rooms] [104-Audio Conference] 1-AC Dial-2450_VTC KYPD_1.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[Advance Page] 02-Cable TV_TV CURSOR.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[Advance Page] 02-Cable TV_TV KYPD.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | MTVanTech-DivRmsiPadv2.2_[Advance Page] 12-Camera Controls_DPad.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NEC MultiSync P551 v1.umc (SmC 155) | pins in=80 out=80 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NEC NP-PA600X v1.0.umc (SmC 155) | pins in=91 out=91 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Config Helper.usp (SmC 103) | pins in=12 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Stream To Text.usp (SmC 103) | pins in=4 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[0] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[100] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[101] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[102] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[103] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[104] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[105] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[106] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[107] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[108] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[109] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[10] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[110] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[111] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[112] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[113] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[114] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[115] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[116] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[11] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[12] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[13] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[14] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[15] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[16] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[17] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[18] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[19] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[1] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[20] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[21] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[22] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[23] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[24] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[25] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[26] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[27] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[28] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[29] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[2] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[30] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[31] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[32] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[33] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[34] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[35] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[36] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[37] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[38] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[39] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[3] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[40] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[41] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[42] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[43] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[44] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[45] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[46] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[47] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[48] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[49] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[4] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[50] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[51] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[52] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[53] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[54] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[55] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[56] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[57] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[58] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[59] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[5] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[60] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[61] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[62] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[63] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[64] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[65] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[66] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[67] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[68] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[69] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[6] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[70] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[71] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[72] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[73] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[74] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[75] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[76] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[77] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[78] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[79] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[7] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[80] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[81] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[82] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[83] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[84] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[85] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[86] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[87] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[88] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[89] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[8] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[90] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[91] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[92] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[93] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[94] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[95] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[96] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[97] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[98] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[99] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVXButtonsSettings.NVXButtonSettings[9] (SmC 2942) | pins in=6 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers Collaboration + Cafe TSW570_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers Collaboration + Cafe TSW570_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4071 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers Collaboration + Cafe TSW570_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4061 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Page] Main Menu_Main Menu Icon List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Page] Present_Presentation Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Page] Room Control_Room Control Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Page] Settings_Settings Page List.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Present] Video Routing_Sources Local.ced (SmC 2942) | pins in=12042 out=8028 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Present] Video Routing_Video Destinations.ced (SmC 2942) | pins in=4223 out=4083 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Room Control] Displays_Subpage Ref - Displays.ced (SmC 2942) | pins in=4055 out=4035 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Room Control] Mics - Adv_Subpage Ref - Mics.ced (SmC 2942) | pins in=4190 out=4148 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[System Info] NVX_Subpage Ref - NVX Info.ced (SmC 2942) | pins in=4265 out=4128 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Omers-11thFl-EventSpace-TS1070_[Tabs] Sidebar_Sidebar Tab Subpage Reference List.ced (SmC 2942) | pins in=4042 out=4042 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Panasonic AW-HE100 v1.0.umc (SmC 155) | pins in=58 out=58 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Panasonic PT-LW80NTU v1.0.cmc (SmC 155) | pins in=38 out=38 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Passcode Database v1.0.usp (SmC 103) | pins in=5 out=16 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Passcode Entry.umc (SmC 155) | pins in=22 out=22 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Password (SmC 2942) | pins in=21 out=21 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | password with backdoor password.umc (SmC 155) | pins in=15 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot Custom [Digitial].usp (SmC 103) | pins in=10 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot Custom [Serial].usp (SmC 103) | pins in=6 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot Master [Devices].usp (SmC 103) | pins in=10 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot Master [Send].usp (SmC 103) | pins in=20 out=14 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [Biamp Errors].usp (SmC 103) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [Biamp].umc (SmC 155) | pins in=9 out=9 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [Display].umc (SmC 155) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [Microphone - Wireless].umc (SmC 155) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [Microphone].umc (SmC 155) | pins in=4 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [Processor].umc (SmC 155) | pins in=18 out=18 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [QSC] [V5.2].umc (SmC 155) | pins in=11 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [Receiver Data].umc (SmC 155) | pins in=8 out=8 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [TouchPanel].umc (SmC 155) | pins in=10 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [WallSensor].umc (SmC 155) | pins in=6 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Pivot [Wireless APT].umc (SmC 155) | pins in=3 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | PNP_70x70_BM_Changer.umc (SmC 155) | pins in=218 out=218 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | PNP_70x70_Video_Changer.umc (SmC 155) | pins in=218 out=218 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Polling when Quiet v2.umc (SmC 155) | pins in=4 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Polycom Eagle Eye Camera w-Iris v3-lk.umc (SmC 155) | pins in=96 out=96 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Polycom Group Series Driver 4.0.3.umc (SmC 155) | pins in=973 out=973 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Polycom RealPresence Group Series v2.4.3-MGJ.umc (SmC 155) | pins in=365 out=365 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Presentation_Timer_v1_0.cmc (SmC 155) | pins in=13 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Projector Power 2017 r1.umc (SmC 155) | pins in=13 out=13 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Projector Power 2017.umc (SmC 155) | pins in=13 out=13 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ProjectorObject.ProjectorObject[0] (SmC 2942) | pins in=6 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ProjectorObject.ProjectorObject[1] (SmC 2942) | pins in=6 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ProjectorObject.ProjectorObject[2] (SmC 2942) | pins in=6 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ProjList (SmC 2942) | pins in=627 out=1221 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Button] V5.0.umc (SmC 155) | pins in=7 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Button] V5.3.umc (SmC 155) | pins in=7 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Core] V5.0.0.umc (SmC 155) | pins in=9 out=9 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Core] V5.3.umc (SmC 155) | pins in=9 out=9 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Level Control] V5.0.umc (SmC 155) | pins in=12 out=8 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Level Control] V5.3.umc (SmC 155) | pins in=12 out=8 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [String - Value] V5.3.umc (SmC 155) | pins in=7 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Core.umc (SmC 155) | pins in=14 out=14 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Core.usp (SmC 103) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Fader.usp (SmC 103) | pins in=7 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys LED State Control v3.0.usp (SmC 103) | pins in=2 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Level Control v3.0.usp (SmC 103) | pins in=12 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Processor Control v3.1.usp (SmC 103) | pins in=13 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Snapshot Controller.usp (SmC 103) | pins in=7 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys State Control v3.0.usp (SmC 103) | pins in=6 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Room Info v1.2.usp (SmC 103) | pins in=2 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | RoomControl-List (SmC 2942) | pins in=2008 out=1511 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Samsung PPM63M7F v1.0.cmc (SmC 155) | pins in=53 out=53 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Samsung_CEC.usp (SmC 103) | pins in=4 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Samsung_MDC_Volume.usp (SmC 103) | pins in=11 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SCHEDULED TRIGGER.usp (SmC 103) | pins in=5 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serialize Time_V2.usp (SmC 103) | pins in=1 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SERIAL_TIME_2.umc (SmC 155) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Settings-List (SmC 2942) | pins in=2008 out=1509 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SettingsPage.SettingsButtons (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SEVI-25C.cmc (SmC 155) | pins in=56 out=28 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sharp LC 2013-05-24.umc (SmC 155) | pins in=50 out=50 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sharp PN Monitor Control (Rev 2-00-1).umc (SmC 155) | pins in=17 out=17 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure ULX-D.umc (SmC 155) | pins in=2 out=37 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure-ULX-D_module V3.umc (SmC 155) | pins in=3 out=76 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SideBarButtonList (SmC 2942) | pins in=524 out=1522 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SideBarNavigationButton.Button[0] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SideBarNavigationButton.Button[1] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SideBarNavigationButton.Button[2] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simpl Time Analog w setting.umc (SmC 155) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simpl Time Analog.umc (SmC 155) | pins in=2 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | simple_timer_v10.cmc (SmC 155) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SONY BRC-Z330.umc (SmC 155) | pins in=83 out=84 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony EVI-HD1 Camera Control (25 Presets).umc (SmC 155) | pins in=61 out=61 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtons.Button[0] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtons.Button[1] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtons.Button[2] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtons.Button[3] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtons.Button[4] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtons.Button[5] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtons.Button[6] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SourceButtons.Button[7] (SmC 2942) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Splash Mode v2.0.usp (SmC 103) | pins in=54 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SubPage Navigation v1.0.usp (SmC 103) | pins in=39 out=70 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TCP Helper.usp (SmC 103) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TCPIP Status.umc (SmC 155) | pins in=1 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Tesira VI (Rev 1.00.9).umc (SmC 155) | pins in=32 out=32 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Text NVRAM.usp (SmC 103) | pins in=10 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Timed Event Scheduler.usp (SmC 103) | pins in=25 out=25 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Timer Clock.usp (SmC 103) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ToolsPasscodeChangeKeypad2 (SmC 2942) | pins in=21 out=21 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ToolsPasscodeEntryKeypad3 (SmC 2942) | pins in=21 out=20 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TP Basic v0.9.04.umc (SmC 155) | pins in=430 out=430 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TS-770 Button Toolbar (SmC 6978) | pins in=23 out=23 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TST-1080 App Control (SmC 17309) | pins in=210 out=108 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TST-1080 Button Toolbar (SmC 17313) | pins in=23 out=23 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TST-1080 System Reserved Joins (SmC 17317) | pins in=47 out=47 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TSW-1060 Hard Buttons Reserved Joins (SmC 5977) | pins in=16 out=16 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TSW-1070 App Control (SmC 6858) | pins in=210 out=108 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TSW-1070 Button Toolbar (SmC 6862) | pins in=23 out=23 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TSW-1070 Zoom Room App Reserved Joins (SmC 6873) | pins in=3 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TSW-560 Hard Buttons Reserved Joins (SmC 5973) | pins in=16 out=16 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TVCURSOR4 (SmC 2942) | pins in=13 out=13 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TVKYPD6 (SmC 2942) | pins in=21 out=21 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE LYNC Meeting Room Reserved Joins (SmC 6486) | pins in=75 out=75 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE System2 Reserved Joins (SmC 6615) | pins in=83 out=83 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE-FLEX-TEAMS Audio Reserved Joins (SmC 16509) | pins in=151 out=142 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE-FLEX-TEAMS System Reserved Joins (SmC 16512) | pins in=461 out=434 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UInt64 Bit Mask_v2.usp (SmC 103) | pins in=12 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Update Status (SmC 2610) | pins in=0 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | USB-NX2-Status.cmc (SmC 155) | pins in=2 out=15 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | USB-NX2.cmc (SmC 155) | pins in=5 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Vaddio RoboSHOT 30 v1.0.umc (SmC 155) | pins in=162 out=162 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Vaddio Roboshot QUSB v1.1.umc (SmC 155) | pins in=30 out=30 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Video Source Send (Rev 0-00-2).umc (SmC 155) | pins in=1 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | VTCContactList-Polycom12 (SmC 2942) | pins in=2066 out=1545 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | VTCDialKeypad2 (SmC 2942) | pins in=21 out=21 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | VTCMenu-OSD1 (SmC 2942) | pins in=51 out=51 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WestLincolnPartitioni-Type3-TSW-1070_v001_VC_VTC Menu - OSD.ced (SmC 2942) | pins in=10 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WestLincolnPartitioni-Type3-TSW-1070_v001_[popup] Adv Settings Passcode Entry_Tools Passcode Entry Keypad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WestLincolnPartitioni-Type3-TSW-1070_v001_[sbpg] ATC Dial Pad_ATC Dial Keypad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WestLincolnPartitioni-Type3-TSW-1070_v001_[sbpg][Adv Settings] Passcode Change_Tools Passcode Change Keypad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WestLincolnPartitioni-Type3-TSW-1070_v001_[sbpg][VTC] Contacts (Polycom)_VTC Contact List - Polycom.ced (SmC 2942) | pins in=46 out=26 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WestLincolnPartitioni-Type3-TSW-1070_v001_[sbpg][VTC] Dial Pad_VTC Dial Keypad.ced (SmC 2942) | pins in=12 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WidgetList (SmC 2942) | pins in=626 out=1216 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | WMList (SmC 2942) | pins in=1314 out=702 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | xpanel Manual Control_Page1_DPad.ced (SmC 2942) | pins in=5 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL+ module | Append2String.dll | MissingFieldException: Field not found: 'Crestron.Logos.SplusObjects.SplusObject.OnEventStatement'. | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL+ module | Auto_Adjust_Font_Size_v1_0.dll | MissingFieldException: Field not found: 'Crestron.Logos.SplusObjects.SplusObject.OnEventStatement'. | 1 | 2026-11-19 | 2026-11-19 |
