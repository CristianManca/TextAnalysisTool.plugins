using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

public sealed class TATPlugin_LiveLogAutoReload
{
    private const int RefreshIntervalMilliseconds = 500;
    private const int MaxReloadDelayMilliseconds = 10000;
    private const int ReloadCostFactor = 4;
    private int m_nextReloadTick;

    private Timer m_timer;
    private object m_textAnalysisTool;
    private FieldInfo m_currentFileNameField;
    private MethodInfo m_reloadLinesMethod;
    private string m_fileName;
    private static ToolStripMenuItem s_autoScrollMenuItem;
    private static ToolStripMenuItem s_autoReloadMenuItem;
    private bool m_scrollPending;
    private long m_fileLength;
    private DateTime m_lastWriteTimeUtc;

    private static string DiagnosticLogPath
    {
        get { return Path.Combine(Path.GetTempPath(), "TATPlugin_LiveLogAutoReload.log"); }
    }

    public TATPlugin_LiveLogAutoReload()
    {
        LoadTailSettings();
        WriteDiagnostic("Plug-in constructor invoked. Assembly: " + typeof(TATPlugin_LiveLogAutoReload).Assembly.Location);
    }

    public string GetFileTypeDescription()
    {
        WriteDiagnostic("GetFileTypeDescription");
        return "All Files (automatically refreshed)";
    }

    public string GetFileTypePattern()
    {
        WriteDiagnostic("GetFileTypePattern");
        return "*.*";
    }

    public bool IsFileTypeSupported(string fileName)
    {
        WriteDiagnostic("IsFileTypeSupported: " + fileName);
        return File.Exists(fileName);
    }

    public TextReader GetReaderForFile(string fileName)
    {
        FileStream stream = null;
        try
        {
            WriteDiagnostic("GetReaderForFile: " + fileName);
            stream = new FileStream(
                fileName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            Encoding encoding = GetCurrentEncoding();
            StartWatching(fileName);
            return OpenReader(stream, encoding);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("GetReaderForFile failed: " + exception);
            if (stream != null)
            {
                try
                {
                    stream.Dispose();
                }
                catch (Exception disposeException)
                {
                    WriteDiagnostic("Closing the failed log reader also failed: " + disposeException);
                }
            }

            throw new FileLoadException(
                "The live log plug-in could not open or monitor this file: " + exception.Message,
                fileName);
        }
    }

    private Encoding GetCurrentEncoding()
    {
        object textAnalysisTool = GetTextAnalysisTool();
        if (textAnalysisTool != null)
        {
            PropertyInfo property = textAnalysisTool.GetType().GetProperty(
                "CurrentEncoding",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                Encoding encoding = property.GetValue(textAnalysisTool, null) as Encoding;
                if (encoding != null)
                {
                    return encoding;
                }
            }
        }

        return Encoding.Default;
    }

    private Timer m_retryTimer;
    private string m_pendingFile;
    private int m_retryCount;

    private void DeferStart(string fileName)
    {
        WriteDiagnostic("Main window not ready; deferring start for " + fileName);
        m_pendingFile = fileName;
        m_retryCount = 0;
        if (m_retryTimer != null)
        {
            return;
        }

        m_retryTimer = new Timer();
        m_retryTimer.Interval = 250;
        m_retryTimer.Tick += delegate
        {
            m_retryCount++;
            try
            {
                if (GetTextAnalysisTool() != null)
                {
                    m_retryTimer.Stop();
                    string file = m_pendingFile;
                    m_pendingFile = null;
                    StartWatching(file);
                }
                else if (m_retryCount > 80)
                {
                    m_retryTimer.Stop();
                    WriteDiagnostic("Gave up waiting for the main window.");
                }
            }
            catch (Exception exception)
            {
                m_retryTimer.Stop();
                WriteDiagnostic("Deferred start failed: " + exception);
            }
        };
        m_retryTimer.Start();
    }

    private void StartWatching(string fileName)
    {
        object textAnalysisTool = GetTextAnalysisTool();
        if (textAnalysisTool == null)
        {
            DeferStart(fileName);
            return;
        }

        string fullPath = Path.GetFullPath(fileName);
        if (m_timer != null &&
            ReferenceEquals(textAnalysisTool, m_textAnalysisTool) &&
            string.Equals(fullPath, m_fileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StopWatching();

        Type type = textAnalysisTool.GetType();
        m_currentFileNameField = type.GetField("m_currentFileName", BindingFlags.Instance | BindingFlags.NonPublic);
        m_reloadLinesMethod = type.GetMethod("ReloadLines", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m_currentFileNameField == null || m_reloadLinesMethod == null)
        {
            throw new InvalidOperationException("This TextAnalysisTool.NET version does not expose the file reload hooks required by the live log plug-in.");
        }

        FileInfo file = new FileInfo(fullPath);
        file.Refresh();
        m_fileName = fullPath;
        m_fileLength = file.Length;
        m_lastWriteTimeUtc = file.LastWriteTimeUtc;
        m_textAnalysisTool = textAnalysisTool;

        EnsureMenuItem();
        m_scrollPending = true;
        m_timer = new Timer();
        m_timer.Interval = RefreshIntervalMilliseconds;
        m_timer.Tick += Timer_Tick;
        m_timer.Start();
    }

    private void EnsureMenuItem()
    {
        if (s_autoScrollMenuItem != null)
        {
            return;
        }

        foreach (Form form in Application.OpenForms)
        {
            FieldInfo viewField = form.GetType().GetField("m_menuView", BindingFlags.Instance | BindingFlags.NonPublic);
            object viewMenu = viewField != null ? viewField.GetValue(form) : null;
            if (viewMenu == null)
            {
                continue;
            }

            ToolStripMenuItem toolStripMenu = viewMenu as ToolStripMenuItem;
            if (toolStripMenu == null)
            {
                WriteDiagnostic("Unsupported View menu type: " + viewMenu.GetType());
                return;
            }

            ToolStripMenuItem reloadItem = new ToolStripMenuItem("Live log auto-reload");
            reloadItem.CheckOnClick = true;
            reloadItem.Checked = false;

            ToolStripMenuItem scrollItem = new ToolStripMenuItem("Live log auto-scroll to end");
            scrollItem.CheckOnClick = true;
            scrollItem.Checked = true;
            scrollItem.Enabled = false;

            reloadItem.Click += delegate
            {
                scrollItem.Enabled = reloadItem.Checked;
                if (reloadItem.Checked)
                {
                    ResyncBaseline();
                    m_scrollPending = true;
                }
            };
            scrollItem.Click += delegate
            {
                if (scrollItem.Checked)
                {
                    m_scrollPending = true;
                }
            };

            toolStripMenu.DropDownItems.Add(new ToolStripSeparator());
            toolStripMenu.DropDownItems.Add(reloadItem);
            toolStripMenu.DropDownItems.Add(scrollItem);

            ToolStripMenuItem tailItem = new ToolStripMenuItem("Live log tail mode (last lines only)");
            tailItem.CheckOnClick = true;
            tailItem.Checked = s_tailEnabled;
            ToolStripMenuItem tailLinesItem = new ToolStripMenuItem(TailLinesCaption());
            tailItem.Click += delegate
            {
                s_tailEnabled = tailItem.Checked;
                SaveTailSettings();
                ForceReload();
            };
            tailLinesItem.Click += delegate
            {
                int lines = PromptTailLines(s_tailLines);
                if (lines > 0 && lines != s_tailLines)
                {
                    s_tailLines = lines;
                    tailLinesItem.Text = TailLinesCaption();
                    SaveTailSettings();
                    if (s_tailEnabled)
                    {
                        ForceReload();
                    }
                }
            };
            toolStripMenu.DropDownItems.Add(tailItem);
            toolStripMenu.DropDownItems.Add(tailLinesItem);
            s_autoReloadMenuItem = reloadItem;
            s_autoScrollMenuItem = scrollItem;
            return;
        }
    }

    private const int DefaultTailLines = 100000;
    private static bool s_tailEnabled;
    private static int s_tailLines = DefaultTailLines;

    private static string TailSettingsPath
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TATPlugin_LiveLogAutoReload",
                "settings.ini");
        }
    }

    private static string TailLinesCaption()
    {
        return "Live log tail lines: " + s_tailLines.ToString("N0") + "...";
    }

    private static void LoadTailSettings()
    {
        try
        {
            if (!File.Exists(TailSettingsPath))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(TailSettingsPath))
            {
                string[] parts = line.Split(new[] { '=' }, 2);
                int number;
                if (parts.Length != 2)
                {
                    continue;
                }

                if (parts[0].Trim() == "TailEnabled")
                {
                    s_tailEnabled = parts[1].Trim() == "1";
                }
                else if (parts[0].Trim() == "TailLines" && int.TryParse(parts[1].Trim(), out number) && number > 0)
                {
                    s_tailLines = number;
                }
            }
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Cannot read settings: " + exception.Message);
        }
    }

    private static void SaveTailSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TailSettingsPath));
            File.WriteAllLines(
                TailSettingsPath,
                new[] { "TailEnabled=" + (s_tailEnabled ? "1" : "0"), "TailLines=" + s_tailLines });
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Cannot save settings: " + exception.Message);
        }
    }

    private static int PromptTailLines(int current)
    {
        using (Form dialog = new Form())
        {
            dialog.Text = "Live log tail mode";
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ClientSize = new System.Drawing.Size(330, 100);

            Label label = new Label();
            label.Text = "Number of last lines to load:";
            label.SetBounds(10, 12, 310, 18);
            NumericUpDown number = new NumericUpDown();
            number.Minimum = 100;
            number.Maximum = 100000000;
            number.Increment = 10000;
            number.Value = Math.Min(number.Maximum, Math.Max(number.Minimum, current));
            number.SetBounds(10, 34, 150, 22);
            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(165, 64, 75, 26);
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(245, 64, 75, 26);
            dialog.Controls.AddRange(new Control[] { label, number, ok, cancel });
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;

            Form owner = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            return dialog.ShowDialog(owner) == DialogResult.OK ? (int)number.Value : 0;
        }
    }

    // In tail mode only the last N lines are handed to TAT, so reload cost stays bounded.
    private static TextReader OpenReader(FileStream stream, Encoding encoding)
    {
        if (!s_tailEnabled)
        {
            return new StreamReader(stream, encoding, true);
        }

        byte[] head = new byte[4];
        int headLength = stream.Read(head, 0, 4);
        stream.Position = 0;
        bool utf8Bom = headLength >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
        bool wide = headLength >= 2 && ((head[0] == 0xFE && head[1] == 0xFF) || (head[0] == 0xFF && head[1] == 0xFE));
        if (wide)
        {
            WriteDiagnostic("Tail mode not supported for UTF-16/32 files; loading the whole file.");
            return new StreamReader(stream, encoding, true);
        }

        long start = FindTailStart(stream, s_tailLines);
        if (start <= 0)
        {
            stream.Position = 0;
            return new StreamReader(stream, encoding, true);
        }

        stream.Position = start;
        WriteDiagnostic("Tail mode: loading from byte " + start + " of " + stream.Length);
        return new StreamReader(stream, utf8Bom ? Encoding.UTF8 : encoding, false);
    }

    private static long FindTailStart(FileStream stream, int lines)
    {
        long end = stream.Length;
        if (end == 0)
        {
            return 0;
        }

        stream.Position = end - 1;
        if (stream.ReadByte() == '\n')
        {
            end--;
        }

        byte[] buffer = new byte[65536];
        long position = end;
        int count = 0;
        while (position > 0)
        {
            int size = (int)Math.Min(buffer.Length, position);
            position -= size;
            stream.Position = position;
            int read = 0;
            while (read < size)
            {
                int n = stream.Read(buffer, read, size - read);
                if (n <= 0)
                {
                    return 0;
                }

                read += n;
            }

            for (int i = size - 1; i >= 0; i--)
            {
                if (buffer[i] == '\n' && ++count == lines)
                {
                    return position + i + 1;
                }
            }
        }

        return 0;
    }

    private void ForceReload()
    {
        if (m_textAnalysisTool == null || m_reloadLinesMethod == null)
        {
            return;
        }

        try
        {
            m_reloadLinesMethod.Invoke(m_textAnalysisTool, null);
            ResyncBaseline();
            m_scrollPending = true;
            ScrollToEnd();
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Reload after option change failed: " + exception);
        }
    }

    private void ResyncBaseline()
    {
        if (m_fileName == null || !File.Exists(m_fileName))
        {
            return;
        }

        FileInfo file = new FileInfo(m_fileName);
        file.Refresh();
        m_fileLength = file.Length;
        m_lastWriteTimeUtc = file.LastWriteTimeUtc;
    }

    private void ScrollToEnd()
    {
        if (s_autoScrollMenuItem == null || !s_autoScrollMenuItem.Checked)
        {
            return;
        }

        try
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Form form in Application.OpenForms)
            {
                FieldInfo displayField = form.GetType().GetField("m_lineCollectionDisplay", BindingFlags.Instance | BindingFlags.NonPublic);
                object display = displayField != null ? displayField.GetValue(form) : null;
                if (display == null)
                {
                    continue;
                }

                ListBox listBox = display.GetType().GetProperty("ListBox", flags).GetValue(display, null) as ListBox;
                object lines = listBox.GetType().GetProperty("CurrentDisplayLines", flags).GetValue(listBox, null);
                int count = lines != null ? (int)lines.GetType().GetProperty("Count", flags).GetValue(lines, null) : 0;
                if (count > 0)
                {
                    MethodInfo selectIndex = listBox.GetType().GetMethod("SelectIndex", flags);
                    selectIndex.Invoke(listBox, new object[] { count - 1 });
                    WriteDiagnostic("Auto-scroll: lines=" + count);
                }

                return;
            }
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Auto-scroll failed: " + exception);
        }
    }

    private object GetTextAnalysisTool()
    {
        foreach (Form form in Application.OpenForms)
        {
            FieldInfo field = form.GetType().GetField("m_textAnalysisTool", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(form);
            }
        }

        return null;
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        if (m_textAnalysisTool == null || !File.Exists(m_fileName))
        {
            StopWatching();
            return;
        }

        string currentFileName = m_currentFileNameField.GetValue(m_textAnalysisTool) as string;
        if (!string.Equals(currentFileName, m_fileName, StringComparison.OrdinalIgnoreCase))
        {
            StopWatching();
            return;
        }

        FileInfo file = new FileInfo(m_fileName);
        file.Refresh();
        bool reloadEnabled = s_autoReloadMenuItem != null && s_autoReloadMenuItem.Checked;
        if (!reloadEnabled)
        {
            m_fileLength = file.Length;
            m_lastWriteTimeUtc = file.LastWriteTimeUtc;
            return;
        }

        if (file.Length == m_fileLength && file.LastWriteTimeUtc == m_lastWriteTimeUtc)
        {
            if (m_scrollPending)
            {
                m_scrollPending = false;
                ScrollToEnd();
            }

            return;
        }

        // A full reload costs time proportional to the file size and blocks the UI thread:
        // wait several times the last reload duration so the UI keeps some breathing room.
        if (Environment.TickCount - m_nextReloadTick < 0)
        {
            return;
        }

        m_fileLength = file.Length;
        m_lastWriteTimeUtc = file.LastWriteTimeUtc;

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            m_reloadLinesMethod.Invoke(m_textAnalysisTool, null);
            m_scrollPending = true;
            ScrollToEnd();
            stopwatch.Stop();
            long wait = Math.Min(MaxReloadDelayMilliseconds, Math.Max(RefreshIntervalMilliseconds, stopwatch.ElapsedMilliseconds * ReloadCostFactor));
            m_nextReloadTick = Environment.TickCount + (int)wait;
            if (wait > RefreshIntervalMilliseconds)
            {
                WriteDiagnostic("Reload took " + stopwatch.ElapsedMilliseconds + " ms; next reload in " + wait + " ms");
            }
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Automatic reload failed: " + exception);
            StopWatching();
            TargetInvocationException invocationException = exception as TargetInvocationException;
            Exception error = invocationException != null && invocationException.InnerException != null
                ? invocationException.InnerException
                : exception;
            MessageBox.Show(
                error.Message,
                "Live log refresh failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StopWatching()
    {
        if (m_timer != null)
        {
            m_timer.Stop();
            m_timer.Tick -= Timer_Tick;
            m_timer.Dispose();
            m_timer = null;
        }

        m_textAnalysisTool = null;
        m_nextReloadTick = Environment.TickCount;
        m_currentFileNameField = null;
        m_reloadLinesMethod = null;
        m_fileName = null;
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            File.AppendAllText(
                DiagnosticLogPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
        }
        catch (IOException exception)
        {
            Trace.WriteLine(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.WriteLine(exception);
        }
    }
}
