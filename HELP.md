# Creating a TextAnalysisTool.NET plug-in

Reference: https://dlaa.me/blog/post/tatplugins

## How plug-ins work

TextAnalysisTool.NET loads every `TATPlugin_*.dll` found next to
`TextAnalysisTool.NET.exe` at startup. On each file open, plug-ins are asked
in turn whether they support the file. The first one that does returns a
`TextReader`; the app displays its lines. If none does, the default parser
is used. Loaded plug-ins are listed in **Help > Installed plug-ins**.

## Requirements

- .NET Framework 4.x C# compiler (`csc.exe`, no Visual Studio needed):
  `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- A class library (DLL) named `TATPlugin_<Name>.dll`.

## The contract

Create one **public sealed class** named like the DLL, without the `.dll`
extension (e.g. `TATPlugin_MyPlugin`), with a public parameterless
constructor and these public methods. The interface is matched by
reflection, so the class does not need to implement it.

```csharp
using System.IO;

public sealed class TATPlugin_MyPlugin
{
    public string GetFileTypeDescription() { return "My Files"; }
    public string GetFileTypePattern()     { return "*.my"; }

    public bool IsFileTypeSupported(string fileName)
    {
        return Path.GetExtension(fileName) == ".my";
    }

    public TextReader GetReaderForFile(string fileName)
    {
        try
        {
            return new StreamReader(fileName);
        }
        catch (Exception e)
        {
            // FileLoadException = "the file is bad, not the plug-in"
            throw new FileLoadException(e.Message, fileName);
        }
    }
}
```

Optionally, to compile against the interface, implement
`TextAnalysisTool.NET.Plugin.ITextAnalysisToolPlugin` and build with
`/define:EXCLUDE_ITextAnalysisToolPlugin_DEFINITION` when the definition is
not available.

## Build

```bat
csc /nologo /target:library /reference:System.Windows.Forms.dll ^
    /out:bin\TATPlugin_MyPlugin.dll TATPlugin_MyPlugin.cs
```

See `TATPlugin_LiveLogAutoReload\Build.cmd` for a complete script.

## Install

1. Close TextAnalysisTool.NET (it locks the DLL).
2. Copy the DLL next to `TextAnalysisTool.NET.exe`. For an installation in
   `C:\Program Files` this needs administrator rights.
3. Start the app and check **Help > Installed plug-ins**.

Only the copy next to the executable you actually launch is loaded; make sure
you update the right one if several installations exist.

## Rules and pitfalls

- **Errors:** an exception other than `FileLoadException` makes the app mark
  the plug-in `[Disabled]` for the rest of the session. Throw
  `FileLoadException` for expected problems, without an inner exception (the
  app inspects `GetBaseException()`).
- **Do not add `GetSettingsDialog` returning a `Form`:** in the tested
  version it caused the plug-in to be disabled. The plug-in contract above
  is sufficient.
- **Claiming all files:** `*.*` / `return true` takes precedence over other
  plug-ins, which may then never be called.
- **File locking:** open files with `FileShare.ReadWrite | FileShare.Delete`
  if they can be written by other processes.
- **Startup timing:** when a file is opened from the command line or by
  double-click, the main window may not exist yet when `GetReaderForFile` is
  called. Defer any UI work with a `System.Windows.Forms.Timer`.
- **UI thread:** plug-in calls happen on the UI thread; use WinForms timers,
  not threads, for periodic work.

## Going beyond the official API (reflection)

The official API has no hooks for menus, file watching or scrolling.
`TATPlugin_LiveLogAutoReload` reaches into private members through
reflection:

| Need | Member used |
|------|-------------|
| Main app object | `m_textAnalysisTool` field on the form in `Application.OpenForms` |
| Current file | `m_currentFileName` field of the app object |
| Reload | `ReloadLines()` method of the app object |
| View menu | `m_menuView` (`ToolStripMenuItem`) on the main form |
| Line list | `m_lineCollectionDisplay.ListBox` (virtual; `Items.Count` is 0) |
| Line count | `ListBox.CurrentDisplayLines.Count` |
| Select / scroll | `ListBox.SelectIndex(int)` |

These members are private and may change between versions (tested with
`1.0.9456.32322`). Look up members with a small reflection program
(`Assembly.LoadFrom(exe)` then `GetFields/GetMethods` with
`BindingFlags.NonPublic | BindingFlags.Instance`) and use
`BindingFlags.Public | BindingFlags.NonPublic` for all lookups.

## Debugging

- Write diagnostics to `%TEMP%\TATPlugin_<Name>.log` (see
  `WriteDiagnostic` in the LiveLogAutoReload source).
- Log the assembly location in the constructor to verify which DLL is loaded.
- `[Disabled]` in **Installed plug-ins** means an unexpected exception was
  thrown; the diagnostic log shows which one.
