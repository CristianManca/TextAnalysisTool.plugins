# TATPlugin_ToclRtConsumer

Real-time ETW consumer for TOCL sessions, built into [TextAnalysisTool.NET](https://dlaa.me/blog/post/tatplugins). It reimplements `tocl_rt` (`tocl-rt-consumer`) in C#: it attaches to sessions started with `-rt`, decodes events (WPP via TMF/PDB, manifest events via TDH) and writes one text file per session.

Output file: `<output>\<yyMMdd_HHmmss>_rt_<session>.txt`
Line format: `HH:mm:ss.fff PID:TID <description>: <message>`

## Requirements

- TextAnalysisTool.NET **run as administrator** (needed to query/open ETW sessions).
- TOCL sessions started with `-rt` (a session with only `-o` cannot be read).

## Build and install

1. Run `Build.cmd` → `bin\TATPlugin_ToclRtConsumer.dll`.
2. Close TAT, copy the DLL next to `TextAnalysisTool.NET.exe`, restart TAT as administrator.

## Menu **TOCL RT**

| Item | Action |
|---|---|
| Configure... | TMF folder (WPP), PDB file/folder, output folder, sessions, "open first session log in TAT". |
| Start / Stop | Start the consumer (waits for sessions to appear) or stop it. |
| List sessions | Shows running TOCL sessions and their mode. |
| Open session log | Opens a written log in TAT. |
| Open output folder | Opens the folder in Explorer. |
| Show diagnostic log | Opens `%TEMP%\TATPlugin_ToclRtConsumer.log`. |

Settings are stored in `%APPDATA%\TATPlugin_ToclRtConsumer\settings.ini`.

## Sessions

With no session checked, all known ones are monitored: `acpitrace`, `pcitrace`, `ndistrace`, `wwantrace`, `usbtrace`, `WinEventsTrace`, `QCDriver`, `GNSS`, `MHI`, `EMI`, `DATAIO`, `WNS`, `THERMALMDM`, `QPCIE`, `5GNR`. Custom names can be added in Configure.

## Tips

- Combine with **TATPlugin_LiveLogAutoReload** to follow the opened log live.
- The plug-in never parses files itself; it only adds the menu.
- Status: compiles and loads its contract correctly; end-to-end testing against a real `-rt` session is still pending.

See also the [plug-in development guide](../HELP.md).
