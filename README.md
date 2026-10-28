# TextAnalysisTool.plugins

Plug-ins for [TextAnalysisTool.NET](https://dlaa.me/blog/post/tatplugins) (TAT), the Windows log viewer with filters and markers. A plug-in is a single `TATPlugin_*.dll` copied next to `TextAnalysisTool.NET.exe`; TAT loads it at startup.

## Plug-ins

| Plug-in | What it does |
|---|---|
| [`TATPlugin_LiveLogAutoReload`](TATPlugin_LiveLogAutoReload/README.md) | Follows a log while it is written: auto-reload, auto-scroll to the end, and a *tail mode* that loads only the last N lines so big logs stay fast. Works with any file type. |
| [`TATPlugin_ToclRtConsumer`](TATPlugin_ToclRtConsumer/README.md) | Real-time ETW consumer for TOCL sessions (`-rt`), written in C#. Decodes events (WPP via TMF/PDB, manifest via TDH), writes one text file per session and opens it in TAT. Configurable from a **TOCL RT** menu. |

Used together, the consumer writes the log and LiveLogAutoReload keeps the view up to date.

## Quick start

1. Build a plug-in: run `Build.cmd` in its folder (needs only the .NET Framework 4 C# compiler already present on Windows; output in `bin\`).
2. Close TAT and copy the DLL next to `TextAnalysisTool.NET.exe` (copying into `Program Files` needs elevation).
3. Start TAT. Menus added by the plug-ins appear in the main window.
4. Check **Help | Installed plug-ins** to confirm the plug-in is active (not `[Disabled]`).

`TATPlugin_ToclRtConsumer` also requires TAT to run **as administrator** to read ETW sessions.

## Writing your own plug-in

[`HELP.md`](HELP.md) is a complete guide: the plug-in contract, loading rules, adding menus through reflection, deferring work until the main window exists, building, debugging and common pitfalls.

## Repository layout

```
HELP.md                        plug-in development guide
TATPlugin_<Name>/
    TATPlugin_<Name>.cs        source (plus extra .cs files if needed)
    Build.cmd                  builds bin\TATPlugin_<Name>.dll
    README.md                  plug-in specific usage
```

Build output (`bin\`, `*.dll`, `*.exe`) is git-ignored.

## Compatibility

Plug-ins use reflection on TAT's private members, so they depend on the TAT build. They were tested with **TextAnalysisTool.NET 1.0.9456.32322**; other versions may not work.
