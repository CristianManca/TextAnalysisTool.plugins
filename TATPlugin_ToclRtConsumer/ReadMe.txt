TATPlugin_ToclRtConsumer
========================

Real-time ETW consumer for TOCL sessions, built into TextAnalysisTool.NET.
It does what tocl_rt does (C:\_GIT_Workspace\tocl-rt-consumer): attaches to
sessions started with -rt and writes one decoded text file per session:

    <output>\<yyMMdd_HHmmss>_rt_<session>.txt

Line format: HH:mm:ss.fff PID:TID <description>: <message>

Install
-------
1. Run Build.cmd, copy bin\TATPlugin_ToclRtConsumer.dll next to
   TextAnalysisTool.NET.exe, restart TAT.
2. Run TextAnalysisTool.NET as administrator (needed to query/open ETW sessions).

Use
---
Menu "TOCL RT":
  Configure...        TMF folder (WPP), PDB file/folder, output folder, sessions,
                      "open first session log in TAT" option.
  Start / Stop        start or stop the consumer (waits for sessions to appear).
  List sessions       shows which TOCL sessions are running and their mode.
  Open session log    opens a written log in TAT.
  Open output folder  Explorer on the output folder.
  Show diagnostic log %TEMP%\TATPlugin_ToclRtConsumer.log

Settings: %APPDATA%\TATPlugin_ToclRtConsumer\settings.ini
Combine with TATPlugin_LiveLogAutoReload to follow the log live.

Notes
-----
- Sessions must be started with -rt (not only -o) to be readable.
- No sessions checked = all known sessions (acpitrace, pcitrace, ndistrace,
  wwantrace, usbtrace, WinEventsTrace, QCDriver, GNSS, MHI, EMI, DATAIO, WNS,
  THERMALMDM, QPCIE, 5GNR).
- The plug-in never parses files itself; it only adds the menu.
