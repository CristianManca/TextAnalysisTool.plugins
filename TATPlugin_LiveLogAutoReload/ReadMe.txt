Live Log Auto-Reload Plug-in
============================

Build
-----
Run Build.cmd. It creates bin\TATPlugin_LiveLogAutoReload.dll in this folder.
Copy the DLL to the TextAnalysisTool.NET application folder (next to
TextAnalysisTool.NET.exe), closing the application first because it locks
the DLL.

Usage
-----
Restart TextAnalysisTool.NET, then open any file. The plug-in handles every
file opened from disk, regardless of extension. Because it claims all files,
other plug-ins (for example TATPlugin_SampleData for *.lines) may not get a
chance to handle their files; remove this DLL if you need them. The plug-in
adds two
items at the end of the View menu:

  Live log auto-reload          (off by default)
    When checked, the plug-in checks the open log every 500 milliseconds
    and reloads it when its size or last-write time changes. The file can
    be written by another process while it is being viewed.

  Live log auto-scroll to end   (on by default, available only while
                                 auto-reload is checked)
    When checked, the last line is selected and scrolled into view when
    the file is opened (with auto-reload on), after each automatic reload,
    and when auto-reload is turned on. Uncheck it to scroll freely while
    the log keeps updating.

The plug-in stops following the file when another file is opened or the
current log is removed. The normal File | Reload command remains available.
Both options are shared by all .log files opened in the session and are not
remembered between runs.

Compatibility
-------------
The plug-in uses reflection on private members of TextAnalysisTool.NET
(reload method, current file name, line display, View menu). It was tested
against version 1.0.9456.32322; other versions may not work.

Diagnostics
-----------
The plug-in writes diagnostic messages to
%TEMP%\TATPlugin_LiveLogAutoReload.log. If the plug-in is disabled, or
auto-reload or auto-scroll does not work, this file can help identify the
error.
