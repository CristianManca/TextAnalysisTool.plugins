# TATPlugin_LiveLogAutoReload

Plug-in for [TextAnalysisTool.NET](https://dlaa.me/blog/post/tatplugins) that follows a log file while another process writes to it.

## Features

Two items are added at the end of the **View** menu:

| Menu item | Default | Behavior |
|---|---|---|
| **Live log auto-reload** | off | Every 500 ms checks the open file; reloads it when size or last-write time changes. |
| **Live log auto-scroll to end** | on (enabled only while auto-reload is on) | Selects and scrolls to the last line on open, after each reload, and when auto-reload is turned on. Uncheck to scroll freely while the log keeps updating. |

The plug-in handles **every file** opened from disk, whatever the extension. Following stops when another file is opened or the file is removed. `File | Reload` keeps working. Options are not remembered between runs.

**Large logs:** TAT can only reload the whole file, which blocks its UI. The plug-in therefore measures each reload and waits at least 4× its duration (between 0.5 s and 10 s) before the next one, so the UI stays responsive as the log grows. Slow reloads are recorded in the diagnostic log.

> Because it claims all files, other plug-ins (e.g. `*.lines` sample) may never get a chance to handle theirs. Remove this DLL if you need them.

## Build and install

1. Run `Build.cmd` → `bin\TATPlugin_LiveLogAutoReload.dll`.
2. Close TextAnalysisTool.NET (it locks the DLL).
3. Copy the DLL next to `TextAnalysisTool.NET.exe` (copying into `Program Files` needs elevation).
4. Start TAT and open a file.

## Compatibility

Uses reflection on private members of TAT (reload method, current file name, line display, View menu). Tested with **1.0.9456.32322**; other versions may not work.

## Diagnostics

Messages go to `%TEMP%\TATPlugin_LiveLogAutoReload.log`. Check it if the plug-in is `[Disabled]` or reload/scroll does not work.

See also the [plug-in development guide](../HELP.md).
