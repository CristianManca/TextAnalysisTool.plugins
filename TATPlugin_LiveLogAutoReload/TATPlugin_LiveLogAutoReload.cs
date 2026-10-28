using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

public sealed class TATPlugin_LiveLogAutoReload
{
    private const int RefreshIntervalMilliseconds = 500;

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
            return new StreamReader(stream, encoding, true);
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
            s_autoReloadMenuItem = reloadItem;
            s_autoScrollMenuItem = scrollItem;
            return;
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

        m_fileLength = file.Length;
        m_lastWriteTimeUtc = file.LastWriteTimeUtc;

        try
        {
            m_reloadLinesMethod.Invoke(m_textAnalysisTool, null);
            m_scrollPending = true;
            ScrollToEnd();
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
