# Writing a TextAnalysisTool.NET plug-in

Complete guide to creating, building, installing, debugging and distributing
plug-ins for [TextAnalysisTool.NET](https://textanalysistool.github.io/).

Sources of this guide: the application documentation
(`TextAnalysisTool.NET.txt`, section *Plug-ins*), the article
<https://dlaa.me/blog/post/tatplugins> (with the official `SamplePlugins.zip`),
and the experience gained writing `TATPlugin_LiveLogAutoReload` in this repo.

Contents

1. [What a plug-in is](#1-what-a-plug-in-is)
2. [Prerequisites](#2-prerequisites)
3. [Naming rules](#3-naming-rules)
4. [The plug-in interface](#4-the-plug-in-interface)
5. [The TextReader you return](#5-the-textreader-you-return)
6. [Minimal plug-in](#6-minimal-plug-in)
7. [Error handling](#7-error-handling)
8. [Building](#8-building)
9. [Installing and verifying](#9-installing-and-verifying)
10. [Debugging](#10-debugging)
11. [Plug-ins with UI or background work](#11-plug-ins-with-ui-or-background-work)
12. [Beyond the official API: reflection](#12-beyond-the-official-api-reflection)
13. [Pitfalls checklist](#13-pitfalls-checklist)
14. [Ideas](#14-ideas)
15. [Repository layout and conventions](#15-repository-layout-and-conventions)

---

## 1. What a plug-in is

Plug-ins let you add support for specialized file types. Every time a file is
opened, each plug-in is asked whether it can parse that file. The plug-in that
accepts the file produces a textual representation (a `TextReader`) which the
application displays in its normal line view, so all features (filters,
markers, search, find, copy, ...) work on the plug-in output. If no plug-in
accepts the file, the default parser shows the file contents directly.

Typical uses: decompress/decrypt files, convert binary formats to text,
list the contents of an archive, normalize time stamps, filter out irrelevant
lines, pretty-print XML, translate text, read data from a network socket.

The application loads plug-ins **once, at startup**, from its own folder. They
appear in **Help > Installed plug-ins**, and their file types are added to the
*Files of type* box of the **File > Open** dialog.

## 2. Prerequisites

- The .NET Framework 4.x C# compiler. No Visual Studio is required:

  ```
  %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
  %WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe    (32-bit)
  ```

  Use a Visual Studio Developer Command Prompt if you prefer `csc`/`MSBuild`
  on the `PATH`.
- A copy of TextAnalysisTool.NET (`TextAnalysisTool.NET.exe` and its
  `.config`) to test with.
- Target **.NET Framework** (the plug-in runs inside the application). Match
  the application's runtime: check `TextAnalysisTool.NET.exe.config`.

## 3. Naming rules

| Item | Rule |
|------|------|
| DLL file | `TATPlugin_<Name>.dll` (the prefix `TATPlugin_` is required, otherwise it is not found) |
| Class | one **public** class (use `sealed`), named exactly like the DLL without extension: `TATPlugin_<Name>` |
| Constructor | public, parameterless |
| Location | same folder as `TextAnalysisTool.NET.exe` |

Plug-ins are matched by name and signature through reflection; keep the
class name identical to the file name.

## 4. The plug-in interface

The application defines this interface (it is `internal`, so you cannot
reference it from your assembly; you copy it or just implement the methods
with the same signatures):

```csharp
namespace TextAnalysisTool.NET.Plugin
{
    internal interface ITextAnalysisToolPlugin
    {
        // Text for the "Files of type" combo box in File > Open. Ex: "XML Files"
        string GetFileTypeDescription();

        // Pattern for the same combo box. Ex: "*.xml"
        string GetFileTypePattern();

        // Called on every file open to see whether the plug-in handles the
        // file. Prefer deciding from the file name; opening, reading and
        // closing the file is acceptable when really needed.
        bool IsFileTypeSupported(string fileName);

        // Returns the reader the application uses to read the file.
        System.IO.TextReader GetReaderForFile(string fileName);
    }
}
```

`fileName` is always the full path to the file.

Two ways to write the class:

1. **Implement the interface**: copy `Plugin.cs` (from the official
   `SamplePlugins.zip`) into your project and write
   `: TextAnalysisTool.NET.Plugin.ITextAnalysisToolPlugin`.
2. **Just provide the four public methods** with the right signatures
   (what the sample plug-ins do): wrap the base-type declaration in
   `#if !EXCLUDE_ITextAnalysisToolPlugin_DEFINITION` and compile with
   `/define:EXCLUDE_ITextAnalysisToolPlugin_DEFINITION` when you do not
   include `Plugin.cs`.

```csharp
public sealed class TATPlugin_SampleData
#if !EXCLUDE_ITextAnalysisToolPlugin_DEFINITION
	: TextAnalysisTool.NET.Plugin.ITextAnalysisToolPlugin
#endif
{
    ...
}
```

> Do **not** add extra members whose types come from other libraries (for
> example a `GetSettingsDialog()` returning a `System.Windows.Forms.Form`).
> In the tested version this made the application disable the plug-in.
> Keep the public surface to the four methods above.

## 5. The TextReader you return

Only two members are ever called on the reader: **`ReadLine()`** and
**`Dispose()`**. Everything else can be left unimplemented. `ReadLine()` must
return `null` at the end of the data.

Derive from `TextReader` and override `ReadLine()`:

```csharp
internal sealed class MyReader : TextReader
{
    public override string ReadLine()
    {
        // return the next line, or null when finished
    }

    protected override void Dispose(bool disposing)
    {
        // release the underlying stream/socket here
        base.Dispose(disposing);
    }
}
```

The reader can do anything: tweak lines, filter them, transform formats,
produce completely synthetic data, or read from a network socket. If the
file can be written by another program while you read it, open it with
`FileShare.ReadWrite | FileShare.Delete`.

For ordinary text, returning a `StreamReader` is enough:

```csharp
return new StreamReader(
    new FileStream(fileName, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete),
    Encoding.Default, true);
```

## 6. Minimal plug-in

`TATPlugin_SampleData.cs` (from the official samples): opens files named like
`3.lines` and shows that many lines of sample text.

```csharp
using System;
using System.IO;

public sealed class TATPlugin_SampleData
#if !EXCLUDE_ITextAnalysisToolPlugin_DEFINITION
	: TextAnalysisTool.NET.Plugin.ITextAnalysisToolPlugin
#endif
{
	public TATPlugin_SampleData() { }

	public string GetFileTypeDescription() { return "Sample Data Files"; }
	public string GetFileTypePattern()     { return "*.lines"; }

	public bool IsFileTypeSupported(string fileName)
	{
		return ".lines" == Path.GetExtension(fileName);
	}

	public TextReader GetReaderForFile(string fileName)
	{
		try
		{
			return new SampleDataReader(
				int.Parse(Path.GetFileNameWithoutExtension(fileName)));
		}
		catch (Exception e)
		{
			// FileLoadException = load/parse problem that is NOT the
			// plug-in's fault (see "Error handling")
			throw new FileLoadException(e.Message, fileName);
		}
	}

	internal sealed class SampleDataReader : TextReader
	{
		private int m_numberLines;
		private int m_lineNumber = 1;

		public SampleDataReader(int numberLines) { m_numberLines = numberLines; }

		public override string ReadLine()
		{
			if (m_lineNumber > m_numberLines) { return null; }
			return string.Concat(m_lineNumber++, ": The quick brown fox jumps over a lazy dog.");
		}
	}
}
```

Other official sample: `TATPlugin_XMLFormatter` loads well-formed XML and
pretty-prints it (reader that loads the document, formats it and returns the
formatted lines).

## 7. Error handling

The application distinguishes two kinds of failure when `GetReaderForFile`
throws:

| Exception | Meaning | Result |
|-----------|---------|--------|
| `System.IO.FileLoadException` | The plug-in works, but the file cannot be read/parsed | Error shown to the user; plug-in stays enabled |
| Anything else | The plug-in is faulty | Plug-in is marked **[Disabled]** for the rest of the session |

Rules:

- Wrap expected failures (missing file, bad format, sharing violation) in
  `FileLoadException`: `throw new FileLoadException(message, fileName);`
- **Do not give it an inner exception.** The application calls
  `GetBaseException()` on the exception, and an inner exception would hide
  the `FileLoadException` and cause the plug-in to be disabled.
- Exceptions thrown from `IsFileTypeSupported` should be avoided entirely;
  return `false` instead.
- Dispose any stream you opened if you fail before returning the reader.

## 8. Building

Compile a library; reference `System.Windows.Forms.dll` only if you need it.

```bat
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo ^
    /target:library ^
    /define:EXCLUDE_ITextAnalysisToolPlugin_DEFINITION ^
    /reference:System.Windows.Forms.dll ^
    /out:bin\TATPlugin_MyPlugin.dll TATPlugin_MyPlugin.cs
```

`TATPlugin_LiveLogAutoReload\Build.cmd` is a complete script that also
locates `csc.exe` and creates `bin\`.

Build with the same bitness the application runs as (the AnyCPU default is
fine).

## 9. Installing and verifying

1. **Close** TextAnalysisTool.NET: it locks the DLL, and plug-ins are only
   loaded at startup.
2. Copy `TATPlugin_<Name>.dll` into the same folder as
   `TextAnalysisTool.NET.exe`.
   - In `C:\Program Files\...` this needs administrator rights.
3. Start the application.
4. Open **Help > Installed plug-ins**: the plug-in must be listed with its
   description, pattern and assembly path, and **not** `[Disabled]`.
5. Open a file of the supported type (**File > Open** shows the new type in
   *Files of type*).

If you have more than one installation (for example under `Program Files` and
in `Downloads`), only the DLL next to the executable you actually launch is
loaded. Check the assembly path shown in *Installed plug-ins* to be sure.

Files opened by double-click or from the command line use whichever
executable the file association points to.

## 10. Debugging

- **Log to a file.** The plug-in runs inside another process, so a debugger
  is not always handy. Append to `%TEMP%\TATPlugin_<Name>.log`:

  ```csharp
  private static void WriteDiagnostic(string message)
  {
      try
      {
          File.AppendAllText(
              Path.Combine(Path.GetTempPath(), "TATPlugin_MyPlugin.log"),
              DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
  }
  ```

- Log in the constructor (including
  `typeof(TATPlugin_MyPlugin).Assembly.Location`) to confirm **which DLL is
  loaded**.
- Log every method call and every caught exception (full `ToString()`).
- **`[Disabled]`** in *Installed plug-ins* means an exception other than
  `FileLoadException` escaped, or the class could not be used. The log shows
  which one.
- **Plug-in not listed at all:** wrong file name prefix, class name differs
  from file name, class not public, DLL in the wrong folder, or the
  application was not restarted.
- **No calls in the log:** the application you are looking at is not the one
  that loaded your DLL.
- To attach a debugger, start TextAnalysisTool.NET and use *Debug > Attach
  to Process* in Visual Studio, or add `System.Diagnostics.Debugger.Launch()`
  temporarily.

## 11. Plug-ins with UI or background work

Plug-ins are generally UI-less, but they do not have to be (see the
WPPFormatter plug-in: <https://dlaa.me/blog/post/10043509>). Rules:

- `GetReaderForFile` and the other interface methods are called on the **UI
  thread**. Do not block them for long.
- For periodic work use `System.Windows.Forms.Timer` (runs on the UI thread)
  rather than threads, so you may touch controls safely.
- Anything you create from `GetReaderForFile` must tolerate being called for
  every file open; keep per-file state, and stop timers/watchers when
  another file is opened.
- **Startup timing:** when a file is opened from the command line or by
  double-click, the main window may not exist yet when `GetReaderForFile`
  runs (`Application.OpenForms` is empty). If you need the window, defer
  with a short timer and retry (see `DeferStart` in
  `TATPlugin_LiveLogAutoReload.cs`).
- Do not show message boxes from `IsFileTypeSupported`.

## 12. Beyond the official API: reflection

The official API has **no hooks** for menus, file monitoring, reloading,
scrolling or selection. The only way to get these is reflection on the
application's private members. This is what `TATPlugin_LiveLogAutoReload`
does. It is fragile: members can change in any release. It was tested with
**TextAnalysisTool.NET 1.0.9456.32322**.

Members used by the LiveLogAutoReload plug-in:

| Need | Member |
|------|--------|
| Main application object | field `m_textAnalysisTool` on the main form found in `Application.OpenForms` (type `TextAnalysisTool.NET.CTextAnalysisTool`) |
| Current file name | private field `m_currentFileName` of the application object |
| Reload the file | method `ReloadLines()` of the application object |
| Current encoding | property `CurrentEncoding` of the application object |
| View menu | field `m_menuView` on the main form, a `ToolStripMenuItem` (use `DropDownItems.Add(...)` to add items) |
| Line display | field `m_lineCollectionDisplay` on the main form; its `ListBox` property returns the line list box |
| Line count | `ListBox.CurrentDisplayLines.Count` (the list box is virtual: `Items.Count` is always 0) |
| Select / scroll to a line | method `ListBox.SelectIndex(int index)` |

How to discover members for another version: write a tiny console program
that loads the executable and dumps what you need.

```csharp
var asm = System.Reflection.Assembly.LoadFrom(@"C:\path\TextAnalysisTool.NET.exe");
var flags = System.Reflection.BindingFlags.Instance
          | System.Reflection.BindingFlags.Public
          | System.Reflection.BindingFlags.NonPublic
          | System.Reflection.BindingFlags.DeclaredOnly;
var type = asm.GetType("TextAnalysisTool.NET.LineCollectionDisplayListBox");
foreach (var f in type.GetFields(flags))  Console.WriteLine("F " + f.FieldType.Name + " " + f.Name);
foreach (var m in type.GetMethods(flags)) Console.WriteLine("M " + m.Name);
```

Guidelines for reflection-based plug-ins:

- Always include `BindingFlags.Public | BindingFlags.NonPublic` in lookups
  (a missing flag returns `null` and typically a `NullReferenceException`).
- Look up members once, and verify they are not `null`. Throw a clear
  `FileLoadException`/log message if the application version is not
  supported instead of letting a `NullReferenceException` escape.
- The `ToolStrip` menu types are used (not `MainMenu`/`MenuItem`).
- Menu items you add live for the whole session; add them once (static
  field) and enable/disable them as needed.
- Call reflection-based code only from the UI thread.

## 13. Pitfalls checklist

- [ ] DLL named `TATPlugin_<Name>.dll`, class named `TATPlugin_<Name>`, public
      and `sealed`, with a public parameterless constructor.
- [ ] Only the four interface methods are public (no extra `Form`-returning
      members).
- [ ] Exceptions from `GetReaderForFile` are `FileLoadException` without an
      inner exception; anything else disables the plug-in.
- [ ] `IsFileTypeSupported` never throws and is cheap.
- [ ] Files that other programs write are opened with
      `FileShare.ReadWrite | FileShare.Delete`.
- [ ] The application was closed before copying the DLL and restarted
      afterwards.
- [ ] The DLL was copied next to the executable you actually run.
- [ ] A plug-in claiming every file (`*.*`, or `IsFileTypeSupported` returning
      `true` for everything) is asked in order of loading and takes files from
      other plug-ins.
- [ ] Startup-time file opens are handled (main window may not exist yet).
- [ ] Stop timers, watchers and streams when another file is opened.

## 14. Ideas

- Pretty-print or filter XML/JSON logs.
- Decode binary trace formats into text lines.
- Merge or interleave several log files by time stamp.
- List the files of a `.zip`, or open a compressed `.gz` log.
- Normalize time stamps or strip noise lines before analysis.
- Follow a growing log (this repo's `TATPlugin_LiveLogAutoReload`).

## 15. Repository layout and conventions

```
TextAnalysisTool.plugins\
  HELP.md                         this guide
  TATPlugin_<Name>\
    TATPlugin_<Name>.cs           source
    Build.cmd                     builds to bin\TATPlugin_<Name>.dll
    ReadMe.txt                    plug-in specific usage
```

- One folder per plug-in with its own `Build.cmd` and `ReadMe.txt`.
- `bin\`, `*.dll` and `*.exe` are git-ignored; commit sources only.
- Document in each plug-in's ReadMe: what it does, how to build and install,
  options/menus it adds, the application version it was tested with, and
  where its diagnostics are written.
