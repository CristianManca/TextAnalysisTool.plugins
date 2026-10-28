using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal sealed class RtSettings
{
    public static readonly string[] KnownSessions =
    {
        "acpitrace", "pcitrace", "ndistrace", "wwantrace", "usbtrace", "WinEventsTrace",
        "QCDriver", "GNSS", "MHI", "EMI", "DATAIO", "WNS", "THERMALMDM", "QPCIE", "5GNR"
    };

    public string TmfPath = string.Empty;
    public string PdbPath = string.Empty;
    public string OutputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOCL_RT");
    public List<string> Sessions = new List<string>();
    public bool OpenFirstFile = true;

    private static string SettingsPath
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TATPlugin_ToclRtConsumer",
                "settings.ini");
        }
    }

    public RtSettings Clone()
    {
        RtSettings copy = (RtSettings)MemberwiseClone();
        copy.Sessions = new List<string>(Sessions);
        return copy;
    }

    public static RtSettings Load()
    {
        RtSettings settings = new RtSettings();
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return settings;
            }

            foreach (string line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "Tmf": settings.TmfPath = value; break;
                    case "Pdb": settings.PdbPath = value; break;
                    case "Out": if (value.Length > 0) { settings.OutputFolder = value; } break;
                    case "OpenFirstFile": settings.OpenFirstFile = value != "0"; break;
                    case "Sessions":
                        settings.Sessions = new List<string>(value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                        break;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
        File.WriteAllLines(
            SettingsPath,
            new[]
            {
                "Tmf=" + TmfPath,
                "Pdb=" + PdbPath,
                "Out=" + OutputFolder,
                "Sessions=" + string.Join(";", Sessions.ToArray()),
                "OpenFirstFile=" + (OpenFirstFile ? "1" : "0")
            },
            Encoding.UTF8);
    }
}

// Real-time ETW consumer: attaches to the running TOCL sessions and writes decoded events to text files.
internal sealed class RtConsumer
{
    private sealed class SessionState
    {
        public string Name;
        public ulong Handle;
        public Thread Thread;
        public StreamWriter Writer;
        public EventRecordCallback Callback;
        public string FilePath;
    }

    private readonly object m_lock = new object();
    private readonly Dictionary<string, SessionState> m_sessions = new Dictionary<string, SessionState>(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> m_message;
    private readonly Action<string, string> m_fileCreated;
    private RtSettings m_settings;
    private TdhDecoder m_decoder;
    private Thread m_monitor;
    private volatile bool m_stop;
    private string m_stamp;

    public RtConsumer(Action<string> message, Action<string, string> fileCreated)
    {
        m_message = message;
        m_fileCreated = fileCreated;
    }

    public bool IsRunning
    {
        get { return m_monitor != null && m_monitor.IsAlive; }
    }

    public string[] ActiveFiles
    {
        get
        {
            lock (m_lock)
            {
                List<string> files = new List<string>();
                foreach (SessionState s in m_sessions.Values)
                {
                    if (s.FilePath != null)
                    {
                        files.Add(s.FilePath);
                    }
                }

                return files.ToArray();
            }
        }
    }

    public void Start(RtSettings settings)
    {
        if (IsRunning)
        {
            return;
        }

        m_settings = settings.Clone();
        m_stop = false;
        m_stamp = DateTime.Now.ToString("yyMMdd_HHmmss");
        Directory.CreateDirectory(m_settings.OutputFolder);

        m_decoder = new TdhDecoder(m_message);
        m_decoder.AddTmf(m_settings.TmfPath);
        m_decoder.AddPdb(m_settings.PdbPath);
        m_decoder.Finish();

        m_monitor = new Thread(MonitorLoop);
        m_monitor.IsBackground = true;
        m_monitor.Name = "TOCL RT monitor";
        m_monitor.Start();
        m_message("waiting for TOCL real time sessions...");
    }

    public void Stop()
    {
        if (m_monitor == null)
        {
            return;
        }

        m_stop = true;
        List<SessionState> sessions;
        lock (m_lock)
        {
            sessions = new List<SessionState>(m_sessions.Values);
        }

        foreach (SessionState s in sessions)
        {
            if (s.Handle != 0)
            {
                EtwNative.CloseTrace(s.Handle);
            }
        }

        m_monitor.Join(5000);
        foreach (SessionState s in sessions)
        {
            if (s.Thread != null)
            {
                s.Thread.Join(3000);
            }
        }

        lock (m_lock)
        {
            foreach (SessionState s in m_sessions.Values)
            {
                CloseWriter(s);
            }

            m_sessions.Clear();
        }

        m_decoder.Dispose();
        m_decoder = null;
        m_monitor = null;
        m_message("stopped");
    }

    public static string DescribeSessions(IEnumerable<string> names)
    {
        StringBuilder sb = new StringBuilder();
        IntPtr mem = Marshal.AllocHGlobal(EtwNative.PropsSize + 2048 * 2);
        try
        {
            foreach (string name in names)
            {
                uint rc = Query(name, mem);
                if (rc != EtwNative.ERROR_SUCCESS && rc != EtwNative.ERROR_MORE_DATA)
                {
                    if (rc != EtwNative.ERROR_WMI_INSTANCE_NOT_FOUND)
                    {
                        sb.AppendLine("cannot query session " + name + " (error " + rc +
                            (rc == EtwNative.ERROR_ACCESS_DENIED ? ": run TextAnalysisTool.NET as administrator" : string.Empty) + ")");
                    }

                    continue;
                }

                bool realTime = ((uint)Marshal.ReadInt32(mem, EtwNative.PropsLogFileMode) & EtwNative.EVENT_TRACE_REAL_TIME_MODE) != 0;
                int fileOffset = Marshal.ReadInt32(mem, EtwNative.PropsLogFileNameOffset);
                string file = fileOffset == 0 ? string.Empty : Marshal.PtrToStringUni(new IntPtr(mem.ToInt64() + fileOffset));
                string mode = realTime ? (file.Length == 0 ? "real-time" : "file+real-time") : "file only (not readable)";
                sb.AppendLine("- " + name + ": " + mode);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }

        return sb.Length == 0 ? "no TOCL session running" : sb.ToString();
    }

    private static uint Query(string name, IntPtr mem)
    {
        int total = EtwNative.PropsSize + 2048 * 2;
        for (int i = 0; i < total; i++)
        {
            Marshal.WriteByte(mem, i, 0);
        }

        Marshal.WriteInt32(mem, 0, total);
        Marshal.WriteInt32(mem, EtwNative.PropsLoggerNameOffset, EtwNative.PropsSize);
        Marshal.WriteInt32(mem, EtwNative.PropsLogFileNameOffset, EtwNative.PropsSize + 1024 * 2);
        return EtwNative.ControlTraceW(0, name, mem, EtwNative.EVENT_TRACE_CONTROL_QUERY);
    }

    private IEnumerable<string> Candidates()
    {
        return m_settings.Sessions.Count > 0 ? (IEnumerable<string>)m_settings.Sessions : RtSettings.KnownSessions;
    }

    private void MonitorLoop()
    {
        HashSet<uint> reported = new HashSet<uint>();
        IntPtr mem = Marshal.AllocHGlobal(EtwNative.PropsSize + 2048 * 2);
        int tick = 0;
        try
        {
            while (!m_stop)
            {
                if (tick % 4 == 0)
                {
                    PollSessions(mem, reported);
                }

                FlushWriters();
                tick++;
                Thread.Sleep(250);
            }
        }
        catch (Exception exception)
        {
            m_message("monitor failed: " + exception.Message);
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    private void PollSessions(IntPtr mem, HashSet<uint> reported)
    {
        HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in Candidates())
        {
            uint rc = Query(name, mem);
            if (rc == EtwNative.ERROR_SUCCESS || rc == EtwNative.ERROR_MORE_DATA)
            {
                present.Add(name);
            }
            else if (rc != EtwNative.ERROR_WMI_INSTANCE_NOT_FOUND && reported.Add(rc))
            {
                m_message("cannot query session " + name + " (error " + rc +
                    (rc == EtwNative.ERROR_ACCESS_DENIED ? ": run TextAnalysisTool.NET as administrator" : string.Empty) + ")");
            }
        }

        lock (m_lock)
        {
            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, SessionState> pair in m_sessions)
            {
                if (!present.Contains(pair.Key) && pair.Value.Thread != null && !pair.Value.Thread.IsAlive)
                {
                    gone.Add(pair.Key);
                }
            }

            foreach (string name in gone)
            {
                m_sessions.Remove(name);
            }

            foreach (string name in present)
            {
                if (m_sessions.ContainsKey(name))
                {
                    continue;
                }

                SessionState state = new SessionState();
                state.Name = name;
                m_sessions[name] = state;
                state.Thread = new Thread(Consume);
                state.Thread.IsBackground = true;
                state.Thread.Name = "TOCL RT " + name;
                state.Thread.Start(state);
            }
        }
    }

    private void FlushWriters()
    {
        lock (m_lock)
        {
            foreach (SessionState s in m_sessions.Values)
            {
                if (s.Writer != null)
                {
                    try
                    {
                        s.Writer.Flush();
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
    }

    private void CloseWriter(SessionState s)
    {
        if (s.Writer != null)
        {
            try
            {
                s.Writer.Dispose();
            }
            catch (IOException)
            {
            }

            s.Writer = null;
        }
    }

    private void Consume(object argument)
    {
        SessionState state = (SessionState)argument;
        IntPtr logfile = IntPtr.Zero;
        IntPtr loggerName = IntPtr.Zero;
        try
        {
            state.Callback = delegate(IntPtr record) { OnEvent(state, record); };
            loggerName = Marshal.StringToHGlobalUni(state.Name);
            EtwNative.EVENT_TRACE_LOGFILE lf = new EtwNative.EVENT_TRACE_LOGFILE();
            lf.LoggerName = loggerName;
            lf.ProcessTraceMode = EtwNative.PROCESS_TRACE_MODE_REAL_TIME | EtwNative.PROCESS_TRACE_MODE_EVENT_RECORD;
            lf.EventRecordCallback = Marshal.GetFunctionPointerForDelegate(state.Callback);
            logfile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(EtwNative.EVENT_TRACE_LOGFILE)));
            Marshal.StructureToPtr(lf, logfile, false);

            ulong handle = EtwNative.OpenTraceW(logfile);
            if (handle == ulong.MaxValue || handle == uint.MaxValue)
            {
                m_message("OpenTrace failed for " + state.Name + " (error " + Marshal.GetLastWin32Error() +
                    "). Session started without -rt?");
                return;
            }

            state.Handle = handle;
            m_message("attached to session " + state.Name);
            OpenWriter(state);

            EtwNative.ProcessTrace(new[] { handle }, 1, IntPtr.Zero, IntPtr.Zero);
            EtwNative.CloseTrace(handle);
            state.Handle = 0;
            m_message("session " + state.Name + " closed");
        }
        catch (Exception exception)
        {
            m_message("session " + state.Name + " failed: " + exception.Message);
        }
        finally
        {
            lock (m_lock)
            {
                CloseWriter(state);
            }

            if (logfile != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(logfile);
            }

            if (loggerName != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(loggerName);
            }
        }
    }

    private void OpenWriter(SessionState state)
    {
        string path = Path.Combine(m_settings.OutputFolder, m_stamp + "_rt_" + state.Name + ".txt");
        try
        {
            FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(true));
            writer.Flush();
            lock (m_lock)
            {
                state.Writer = writer;
                state.FilePath = path;
            }

            m_message("writing " + path);
            m_fileCreated(state.Name, path);
        }
        catch (Exception exception)
        {
            m_message("cannot create " + path + ": " + exception.Message);
        }
    }

    private void OnEvent(SessionState state, IntPtr record)
    {
        // An exception must never escape into the native ETW thread.
        try
        {
            TdhDecoder decoder = m_decoder;
            if (m_stop || decoder == null || decoder.IsInternalEvent(record))
            {
                return;
            }

            string line = decoder.Decode(record);
            lock (m_lock)
            {
                if (state.Writer != null)
                {
                    state.Writer.WriteLine(line);
                }
            }
        }
        catch (Exception exception)
        {
            m_message("decode error in " + state.Name + ": " + exception.Message);
        }
    }
}
