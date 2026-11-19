# Unsupported symbols and APIs

OpenVP adds a row here whenever a program uses a symbol, type or API it doesn't support yet. Commit this file after running your programs so everyone can see what to build next; **Programs** counts the program loads that hit it, so the top of each kind is the most wanted. Only symbol names are recorded, never program names.

When you implement one, delete its row. If a program still needs it, it comes back on the next run.

Merging: if two people's edits clash, keep both rows; for the same row, add the Programs counts and keep the earliest First seen and latest Last seen.

| Kind | Symbol / API | Details | Programs | First seen | Last seen |
|---|---|---|---:|---|---|
| Compiled primitive | type 107 | unidentified | 2 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 113 | unidentified | 1 | 2026-11-19 | 2026-11-19 |
| Compiled primitive | type 261 | unidentified | 1 | 2026-11-19 | 2026-11-19 |
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
| SIMPL symbol | press and hold v1.2.cmc (SmC 155) | pins in=1 out=2 | 13 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira State Control v3.3.umc (SmC 155) | pins in=7 out=7 | 9 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Command Processor IP v3.3.umc (SmC 155) | pins in=5 out=5 | 8 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Level Control v3.3.umc (SmC 155) | pins in=13 out=13 | 8 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Display Control v2.3.umc (SmC 155) | pins in=8 out=9 | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Display Helper.usp (SmC 103) | pins in=32 out=32 | 7 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Router Control v3.3.umc (SmC 155) | pins in=9 out=9 | 6 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Preset Control v3.3.umc (SmC 155) | pins in=10 out=10 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Full 101+ Keyboard v1.0.cmc (SmC 155) | pins in=68 out=3 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Multicast Address Assigner.usp (SmC 103) | pins in=354 out=300 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Video Router.usp (SmC 103) | pins in=134 out=145 | 5 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge (Multiple Units) Macros v1.7.umc (SmC 155) | pins in=2 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Analog Remap.usp (SmC 103) | pins in=325 out=212 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Analog.usp (SmC 103) | pins in=1 out=79 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Reader.usp (SmC 103) | pins in=2 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Config - Serial.usp (SmC 103) | pins in=1 out=41 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Epson PowerLite Pro G5150NL + G5200WNL + G5350NL Full Control.cmc (SmC 155) | pins in=42 out=42 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple Router.umc (SmC 155) | pins in=408 out=305 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | press and hold2.cmc (SmC 155) | pins in=1 out=3 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Button] V5.2.umc (SmC 155) | pins in=7 out=4 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Level Control] V5.2.umc (SmC 155) | pins in=12 out=8 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [String - Value] V5.2.umc (SmC 155) | pins in=7 out=5 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SerialHasValue.umc (SmC 155) | pins in=32 out=32 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure MXWAPT.umc (SmC 155) | pins in=152 out=240 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony EVI-D100 Camera Control (25 Presets).cmc (SmC 155) | pins in=61 out=61 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | System Boot.umc (SmC 155) | pins in=47 out=47 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | System Presets.umc (SmC 155) | pins in=872 out=872 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | udsrly_mom.cmc (SmC 155) | pins in=4 out=6 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | wait_for_initialization.usp (SmC 103) | pins in=0 out=1 | 4 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ANALOG BUFFER.usp (SmC 103) | pins in=2 out=1 | 3 | 2026-11-19 | 2026-11-19 |
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
| SIMPL symbol | INOGENI TOGGLE ROOMS v2.umc (SmC 155) | pins in=49 out=49 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | LBISS GE Text Dial v1.00.usp (SmC 103) | pins in=5 out=2 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Notification Service Issuer.usp (SmC 103) | pins in=2 out=0 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Notification Service Notifier.usp (SmC 103) | pins in=3 out=9 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Panasonic PT-RZ570 v1.0.umc (SmC 155) | pins in=47 out=47 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Panasonic_PTZ_Camera_Series-HE-UE-UB-HR.umc (SmC 155) | pins in=68 out=68 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Password_v1_1.cmc (SmC 155) | pins in=47 out=10 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Trigger Button] V5.2.umc (SmC 155) | pins in=3 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | shure mic battery v2.0.usp (SmC 103) | pins in=22 out=22 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simple Math.usp (SmC 103) | pins in=23 out=1 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SSH Interface v1.8.cmc (SmC 155) | pins in=8 out=8 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TP Basic v0.9.01.umc (SmC 155) | pins in=378 out=378 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TV Presets Channel Control v1.0.cmc (SmC 155) | pins in=5 out=18 | 3 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Alarm Clock v2.0.cmc (SmC 155) | pins in=25 out=28 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Are You Sure.usp (SmC 103) | pins in=104 out=104 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Automation Helper.usp (SmC 103) | pins in=3 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Initialize v2.x.umc (SmC 155) | pins in=4 out=3 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira Level Control v2.3.umc (SmC 155) | pins in=10 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira State Control v2.3.umc (SmC 155) | pins in=5 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Biamp Tesira VoIP Control v2.3.umc (SmC 155) | pins in=133 out=133 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Christie Proj Basic r4.umc (SmC 155) | pins in=13 out=13 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge (Multiple Units) Single Channel Output Control with Setup v1.7.umc (SmC 155) | pins in=20 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ClearOne Converge (Multiple Units) Single Channel Volume Control v1.7.umc (SmC 155) | pins in=8 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Crestron Certified Drivers TV Tuner Ethernet v1.9.cmc (SmC 155) | pins in=258 out=258 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Digital First Passthru.usp (SmC 103) | pins in=11 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DM-RMC-SCALER setup.umc (SmC 155) | pins in=30 out=6 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DM64x64 Naming (SmC 4054) | pins in=1064 out=0 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | ext_channelpresets_x50.cmc (SmC 155) | pins in=58 out=72 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HD-MD-400-C-E v1.0.cmc (SmC 155) | pins in=136 out=136 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HP_BiampRouterControl.usp (SmC 103) | pins in=1 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | LBISS NEC TV.umc (SmC 155) | pins in=52 out=52 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multi-Analog Comparison.usp (SmC 103) | pins in=3 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Multiple Press Enable.umc (SmC 155) | pins in=34 out=34 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVRAM Toggle.usp (SmC 103) | pins in=3 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | pj Helper.umc (SmC 155) | pins in=15 out=7 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | press and hold v2.umc (SmC 155) | pins in=1 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Core] V5.2.0 RM.umc (SmC 155) | pins in=9 out=10 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Q-SYS [Core] V5.2.0.umc (SmC 155) | pins in=9 out=9 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Qsys Generic Boolean Named Control.usp (SmC 103) | pins in=3 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Room Combiner.umc (SmC 155) | pins in=66 out=99 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Room Settings v1.1.usp (SmC 103) | pins in=8 out=103 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SACN SenderDirectSockets.cmc (SmC 155) | pins in=512 out=0 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serial pacer.usp (SmC 103) | pins in=6 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SERIALIZE_TIME_2.usp (SmC 103) | pins in=1 out=1 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure P300 v1.1.usp (SmC 103) | pins in=362 out=389 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shutdown.umc (SmC 155) | pins in=2 out=24 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simple Alarm Clock.usp (SmC 103) | pins in=11 out=7 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simple LG Display DK.umc (SmC 155) | pins in=8 out=7 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SmartViewGlassSplus.usp (SmC 103) | pins in=4 out=0 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony EVI-D1-- Camera Control (25 Preset w IL clear).umc (SmC 155) | pins in=61 out=61 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony_BRC-X400_SRG-X_series.usp (SmC 103) | pins in=94 out=11 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TST-902 RF/WiFi Reserved Joins (SmC 4722) | pins in=69 out=103 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | udsrly_int.cmc (SmC 155) | pins in=4 out=5 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UInt64 Bit Mask.usp (SmC 103) | pins in=12 out=4 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | USB-EXT-DM Control v1.0.cmc (SmC 155) | pins in=4 out=2 | 2 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | 10 Alarms HM Plus Minus Entry w Rollover.umc (SmC 155) | pins in=52 out=63 | 1 | 2026-11-19 | 2026-11-19 |
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
| SIMPL symbol | Analog Compare Pulse.usp (SmC 103) | pins in=2 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Delay.usp (SmC 103) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Analog Patch r1.umc (SmC 155) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | analog switcher 50 x 1.umc (SmC 155) | pins in=53 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AnalogToString.usp (SmC 103) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | AnalogValueMatch.usp (SmC 103) | pins in=3 out=1 | 1 | 2026-11-19 | 2026-11-19 |
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
| SIMPL symbol | Camera Preset controller r1.umc (SmC 155) | pins in=281 out=281 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Camera Preset controller r2.umc (SmC 155) | pins in=281 out=281 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Camera Tracking.umc (SmC 155) | pins in=33 out=37 | 1 | 2026-11-19 | 2026-11-19 |
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
| SIMPL symbol | Device-Pwr-IsItOn.umc (SmC 155) | pins in=2 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DigitalDelay.usp (SmC 103) | pins in=2 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Display Input 2015.umc (SmC 155) | pins in=13 out=13 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DM-NUX v1.1.cmc (SmC 155) | pins in=19 out=19 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | DMPS3-AUDIO-OFFSET-VALUE.usp (SmC 103) | pins in=3 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Epson PowerLite Pro Z8450WU v1.0.umc (SmC 155) | pins in=63 out=63 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Extron SW HD 4K Series.umc (SmC 155) | pins in=3 out=12 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | First Value Buffer.usp (SmC 103) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Generic CEC Display Control v1.3.cmc (SmC 155) | pins in=24 out=24 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Generic CEC Source Control v1.0.cmc (SmC 155) | pins in=109 out=109 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Generic CEC Source Control v1.1.cmc (SmC 155) | pins in=109 out=109 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | HD-MD4x1-4K-E v1.1.cmc (SmC 155) | pins in=72 out=72 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | IP Connection.umc (SmC 155) | pins in=3 out=3 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | iptv_59.usp (SmC 103) | pins in=61 out=60 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Is It In Use 32 Sources x 32 Channels.umc (SmC 155) | pins in=32 out=66 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Language lookup.usp (SmC 103) | pins in=5 out=35 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Loading Bar v1.0.umc (SmC 155) | pins in=1 out=6 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Memory Store.usp (SmC 103) | pins in=66 out=66 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Mitsubishi Projector Control (Rev 1-00-1).umc (SmC 155) | pins in=11 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NEC MultiSync P551 v1.umc (SmC 155) | pins in=80 out=80 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NEC NP-PA600X v1.0.umc (SmC 155) | pins in=91 out=91 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Config Helper.usp (SmC 103) | pins in=12 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | NVX Stream To Text.usp (SmC 103) | pins in=4 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Panasonic AW-HE100 v1.0.umc (SmC 155) | pins in=58 out=58 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Panasonic PT-LW80NTU v1.0.cmc (SmC 155) | pins in=38 out=38 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Passcode Database v1.0.usp (SmC 103) | pins in=5 out=16 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Passcode Entry.umc (SmC 155) | pins in=22 out=22 | 1 | 2026-11-19 | 2026-11-19 |
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
| SIMPL symbol | Samsung PPM63M7F v1.0.cmc (SmC 155) | pins in=53 out=53 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Samsung_CEC.usp (SmC 103) | pins in=4 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Samsung_MDC_Volume.usp (SmC 103) | pins in=11 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SCHEDULED TRIGGER.usp (SmC 103) | pins in=5 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Serialize Time_V2.usp (SmC 103) | pins in=1 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SERIAL_TIME_2.umc (SmC 155) | pins in=1 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SEVI-25C.cmc (SmC 155) | pins in=56 out=28 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sharp LC 2013-05-24.umc (SmC 155) | pins in=50 out=50 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sharp PN Monitor Control (Rev 2-00-1).umc (SmC 155) | pins in=17 out=17 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure ULX-D.umc (SmC 155) | pins in=2 out=37 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Shure-ULX-D_module V3.umc (SmC 155) | pins in=3 out=76 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simpl Time Analog w setting.umc (SmC 155) | pins in=7 out=7 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Simpl Time Analog.umc (SmC 155) | pins in=2 out=5 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | simple_timer_v10.cmc (SmC 155) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SONY BRC-Z330.umc (SmC 155) | pins in=83 out=84 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Sony EVI-HD1 Camera Control (25 Presets).umc (SmC 155) | pins in=61 out=61 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Splash Mode v2.0.usp (SmC 103) | pins in=54 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | SubPage Navigation v1.0.usp (SmC 103) | pins in=39 out=70 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TCP Helper.usp (SmC 103) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TCPIP Status.umc (SmC 155) | pins in=1 out=11 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Tesira VI (Rev 1.00.9).umc (SmC 155) | pins in=32 out=32 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Text NVRAM.usp (SmC 103) | pins in=10 out=10 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Timed Event Scheduler.usp (SmC 103) | pins in=25 out=25 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Timer Clock.usp (SmC 103) | pins in=2 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | TP Basic v0.9.04.umc (SmC 155) | pins in=430 out=430 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UC-ENGINE-FLEX-TEAMS System Reserved Joins (SmC 16512) | pins in=461 out=434 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | UInt64 Bit Mask_v2.usp (SmC 103) | pins in=12 out=4 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Update Status (SmC 2610) | pins in=0 out=1 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | USB-NX2-Status.cmc (SmC 155) | pins in=2 out=15 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | USB-NX2.cmc (SmC 155) | pins in=5 out=2 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Vaddio RoboSHOT 30 v1.0.umc (SmC 155) | pins in=162 out=162 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Vaddio Roboshot QUSB v1.1.umc (SmC 155) | pins in=30 out=30 | 1 | 2026-11-19 | 2026-11-19 |
| SIMPL symbol | Video Source Send (Rev 0-00-2).umc (SmC 155) | pins in=1 out=2 | 1 | 2026-11-19 | 2026-11-19 |
