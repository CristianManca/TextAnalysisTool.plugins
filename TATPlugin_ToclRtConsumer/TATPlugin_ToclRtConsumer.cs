using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

public sealed class TATPlugin_ToclRtConsumer
{
    private static readonly object s_gate = new object();
    private static RtSettings s_settings;
    private static RtConsumer s_consumer;
    private static Form s_mainForm;
    private static ToolStripMenuItem s_menu;
    private static ToolStripMenuItem s_startItem;
    private static ToolStripMenuItem s_stopItem;
    private static ToolStripMenuItem s_filesItem;
    private static Timer s_retryTimer;
    private static int s_retryCount;
    private static bool s_firstFileOpened;

    public TATPlugin_ToclRtConsumer()
    {
        try
        {
            s_settings = RtSettings.Load();
            s_consumer = new RtConsumer(Log, OnFileCreated);
            BeginMenuInstall();
        }
        catch (Exception exception)
        {
            Log("constructor: " + exception);
        }
    }

    public string GetFileTypeDescription()
    {
        return "TOCL RT logs (*_rt_*.txt)";
    }

    public string GetFileTypePattern()
    {
        return "*_rt_*.txt";
    }

    // The plug-in only adds a menu; it never takes over file parsing.
    public bool IsFileTypeSupported(string fileName)
    {
        return false;
    }

    public TextReader GetReaderForFile(string fileName)
    {
        throw new FileLoadException("This plug-in does not parse files.", fileName);
    }

    private static void BeginMenuInstall()
    {
        s_retryTimer = new Timer();
        s_retryTimer.Interval = 250;
        s_retryTimer.Tick += delegate
        {
            s_retryCount++;
            try
            {
                if (InstallMenu() || s_retryCount > 120)
                {
                    s_retryTimer.Stop();
                }
            }
            catch (Exception exception)
            {
                s_retryTimer.Stop();
                Log("menu install failed: " + exception);
            }
        };
        s_retryTimer.Start();
    }

    private static bool InstallMenu()
    {
        foreach (Form form in Application.OpenForms)
        {
            FieldInfo viewField = form.GetType().GetField("m_menuView", BindingFlags.Instance | BindingFlags.NonPublic);
            ToolStripMenuItem view = viewField != null ? viewField.GetValue(form) as ToolStripMenuItem : null;
            if (view == null || view.Owner == null)
            {
                continue;
            }

            s_mainForm = form;
            ToolStripMenuItem menu = new ToolStripMenuItem("TOCL RT");
            ToolStripMenuItem configure = new ToolStripMenuItem("Configure...");
            configure.Click += delegate { Configure(); };
            s_startItem = new ToolStripMenuItem("Start");
            s_startItem.Click += delegate { StartConsumer(); };
            s_stopItem = new ToolStripMenuItem("Stop");
            s_stopItem.Enabled = false;
            s_stopItem.Click += delegate { StopConsumer(); };
            ToolStripMenuItem list = new ToolStripMenuItem("List sessions");
            list.Click += delegate { ListSessions(); };
            s_filesItem = new ToolStripMenuItem("Open session log");
            s_filesItem.DropDownOpening += delegate { FillFilesMenu(); };
            s_filesItem.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
            ToolStripMenuItem folder = new ToolStripMenuItem("Open output folder");
            folder.Click += delegate { OpenOutputFolder(); };
            ToolStripMenuItem diag = new ToolStripMenuItem("Show diagnostic log");
            diag.Click += delegate { OpenDiagnosticLog(); };

            menu.DropDownItems.Add(configure);
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(s_startItem);
            menu.DropDownItems.Add(s_stopItem);
            menu.DropDownItems.Add(list);
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(s_filesItem);
            menu.DropDownItems.Add(folder);
            menu.DropDownItems.Add(diag);

            ToolStrip strip = view.Owner;
            int index = Math.Max(0, strip.Items.Count - 1);
            strip.Items.Insert(index, menu);
            s_menu = menu;
            form.FormClosing += delegate { SafeStop(); };
            Log("menu installed");
            return true;
        }

        return false;
    }

    private static void FillFilesMenu()
    {
        s_filesItem.DropDownItems.Clear();
        string[] files = s_consumer.ActiveFiles;
        if (files.Length == 0)
        {
            ToolStripMenuItem none = new ToolStripMenuItem("(none)");
            none.Enabled = false;
            s_filesItem.DropDownItems.Add(none);
            return;
        }

        foreach (string file in files)
        {
            string path = file;
            ToolStripMenuItem item = new ToolStripMenuItem(Path.GetFileName(path));
            item.Click += delegate { OpenInTat(path); };
            s_filesItem.DropDownItems.Add(item);
        }
    }

    private static void Configure()
    {
        using (ConfigDialog dialog = new ConfigDialog(s_settings))
        {
            if (dialog.ShowDialog(s_mainForm) != DialogResult.OK)
            {
                return;
            }

            dialog.ApplyTo(s_settings);
        }

        try
        {
            s_settings.Save();
        }
        catch (Exception exception)
        {
            MessageBox.Show(s_mainForm, "Cannot save settings: " + exception.Message, "TOCL RT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (s_consumer.IsRunning)
        {
            MessageBox.Show(s_mainForm, "Settings saved. Stop and Start again to apply them.", "TOCL RT");
        }
    }

    private static void StartConsumer()
    {
        try
        {
            s_firstFileOpened = false;
            s_consumer.Start(s_settings);
            s_startItem.Enabled = false;
            s_stopItem.Enabled = true;
            s_menu.Text = "TOCL RT (running)";
        }
        catch (Exception exception)
        {
            Log("start failed: " + exception);
            MessageBox.Show(s_mainForm, exception.Message, "TOCL RT - cannot start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void StopConsumer()
    {
        SafeStop();
        s_startItem.Enabled = true;
        s_stopItem.Enabled = false;
        s_menu.Text = "TOCL RT";
    }

    private static void SafeStop()
    {
        try
        {
            s_consumer.Stop();
        }
        catch (Exception exception)
        {
            Log("stop failed: " + exception);
        }
    }

    private static void ListSessions()
    {
        System.Collections.Generic.IEnumerable<string> names = s_settings.Sessions.Count > 0
            ? (System.Collections.Generic.IEnumerable<string>)s_settings.Sessions
            : RtSettings.KnownSessions;
        MessageBox.Show(s_mainForm, RtConsumer.DescribeSessions(names), "TOCL RT - sessions");
    }

    private static void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(s_settings.OutputFolder);
            Process.Start("explorer.exe", "\"" + s_settings.OutputFolder + "\"");
        }
        catch (Exception exception)
        {
            Log("open folder failed: " + exception);
        }
    }

    private static void OpenDiagnosticLog()
    {
        string path = DiagnosticPath;
        if (File.Exists(path))
        {
            OpenInTat(path);
        }
    }

    private static void OnFileCreated(string session, string path)
    {
        bool open;
        lock (s_gate)
        {
            open = s_settings.OpenFirstFile && !s_firstFileOpened;
            if (open)
            {
                s_firstFileOpened = true;
            }
        }

        if (open)
        {
            OpenInTat(path);
        }
    }

    private static void OpenInTat(string path)
    {
        Form form = s_mainForm;
        if (form == null)
        {
            return;
        }

        Action open = delegate
        {
            try
            {
                MethodInfo load = form.GetType().GetMethod("LoadFile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                if (load == null)
                {
                    Log("MainWindow.LoadFile not found");
                    return;
                }

                load.Invoke(form, new object[] { path });
            }
            catch (Exception exception)
            {
                Log("open in TAT failed: " + exception);
            }
        };

        if (form.InvokeRequired)
        {
            form.BeginInvoke(open);
        }
        else
        {
            open();
        }
    }

    private static string DiagnosticPath
    {
        get { return Path.Combine(Path.GetTempPath(), "TATPlugin_ToclRtConsumer.log"); }
    }

    private static void Log(string message)
    {
        try
        {
            lock (s_gate)
            {
                File.AppendAllText(DiagnosticPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
        }
        catch (IOException)
        {
        }
    }
}
