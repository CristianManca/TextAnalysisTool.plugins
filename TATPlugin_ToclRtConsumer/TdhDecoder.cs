using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Decodes ETW events (manifest events with TDH, WPP events with TMF/PDB) into one text line.
internal sealed class TdhDecoder : IDisposable
{
    private static readonly Guid EventTraceGuid = new Guid("68fdd900-4a3e-11d1-84f4-0000f80464e3");

    private readonly List<IntPtr> m_strings = new List<IntPtr>();
    private readonly List<EtwNative.TDH_CONTEXT> m_contexts = new List<EtwNative.TDH_CONTEXT>();
    private readonly List<string> m_pdbPaths = new List<string>();
    private readonly Action<string> m_message;
    private IntPtr m_wpp = IntPtr.Zero;
    private IntPtr m_contextArray = IntPtr.Zero;
    private string m_tempTmfDirectory;

    public TdhDecoder(Action<string> message)
    {
        m_message = message;
    }

    public bool IsInternalEvent(IntPtr record)
    {
        return ReadGuid(record, EtwNative.RecProviderId) == EventTraceGuid;
    }

    public void AddTmf(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (File.Exists(path))
        {
            AddContext(EtwNative.TDH_CONTEXT_WPP_TMFFILE, path);
            return;
        }

        if (!Directory.Exists(path))
        {
            m_message("TMF path not found: " + path);
            return;
        }

        string[] files = Directory.GetFiles(path, "*.tmf", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            m_message("no .tmf found in " + path);
            return;
        }

        m_tempTmfDirectory = Path.Combine(Path.GetTempPath(), "tocl_rt_tmf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_tempTmfDirectory);
        foreach (string file in files)
        {
            string target = Path.Combine(m_tempTmfDirectory, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }

        AddContext(EtwNative.TDH_CONTEXT_WPP_TMFSEARCHPATH, m_tempTmfDirectory);
        m_message(files.Length + " TMF files loaded from " + path);
    }

    public void AddPdb(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (File.Exists(path))
        {
            m_pdbPaths.Add(path);
        }
        else if (Directory.Exists(path))
        {
            string[] files = Directory.GetFiles(path, "*.pdb", SearchOption.AllDirectories);
            m_pdbPaths.AddRange(files);
            m_message(files.Length + " PDB files found in " + path);
        }
        else
        {
            m_message("PDB path not found: " + path);
        }
    }

    // Must be called once, after AddTmf/AddPdb and before decoding.
    public void Finish()
    {
        if (m_pdbPaths.Count > 0)
        {
            EtwNative.TDH_CONTEXT context = NewContext(EtwNative.TDH_CONTEXT_PDB_PATH, string.Join(";", m_pdbPaths.ToArray()));
            if (OpenWppHandle())
            {
                uint rc = EtwNative.TdhSetDecodingParameter(m_wpp, ref context);
                if (rc != EtwNative.ERROR_SUCCESS)
                {
                    m_message("TdhSetDecodingParameter(PDB) failed (error " + rc + ")");
                }
            }
        }

        if (m_contexts.Count > 0)
        {
            m_contextArray = Marshal.AllocHGlobal(m_contexts.Count * Marshal.SizeOf(typeof(EtwNative.TDH_CONTEXT)));
            for (int i = 0; i < m_contexts.Count; i++)
            {
                Marshal.StructureToPtr(
                    m_contexts[i],
                    new IntPtr(m_contextArray.ToInt64() + i * Marshal.SizeOf(typeof(EtwNative.TDH_CONTEXT))),
                    false);
            }
        }
    }

    public void Dispose()
    {
        if (m_wpp != IntPtr.Zero)
        {
            EtwNative.TdhCloseDecodingHandle(m_wpp);
            m_wpp = IntPtr.Zero;
        }

        foreach (IntPtr s in m_strings)
        {
            Marshal.FreeHGlobal(s);
        }

        m_strings.Clear();
        if (m_contextArray != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(m_contextArray);
            m_contextArray = IntPtr.Zero;
        }

        if (m_tempTmfDirectory != null)
        {
            try
            {
                Directory.Delete(m_tempTmfDirectory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            m_tempTmfDirectory = null;
        }
    }

    private bool OpenWppHandle()
    {
        if (m_wpp == IntPtr.Zero && EtwNative.TdhOpenDecodingHandle(out m_wpp) != EtwNative.ERROR_SUCCESS)
        {
            m_wpp = IntPtr.Zero;
        }

        return m_wpp != IntPtr.Zero;
    }

    private EtwNative.TDH_CONTEXT NewContext(int type, string value)
    {
        IntPtr text = Marshal.StringToHGlobalUni(value);
        m_strings.Add(text);
        EtwNative.TDH_CONTEXT context = new EtwNative.TDH_CONTEXT();
        context.ParameterValue = (ulong)text.ToInt64();
        context.ParameterType = type;
        context.ParameterSize = 0;
        return context;
    }

    private void AddContext(int type, string value)
    {
        EtwNative.TDH_CONTEXT context = NewContext(type, value);
        m_contexts.Add(context);
        if (OpenWppHandle())
        {
            uint rc = EtwNative.TdhSetDecodingParameter(m_wpp, ref context);
            if (rc != EtwNative.ERROR_SUCCESS)
            {
                m_message("TdhSetDecodingParameter failed for " + value + " (error " + rc + ")");
            }
        }
    }

    private uint ContextCount
    {
        get { return (uint)m_contexts.Count; }
    }

    private static Guid ReadGuid(IntPtr record, int offset)
    {
        byte[] bytes = new byte[16];
        Marshal.Copy(new IntPtr(record.ToInt64() + offset), bytes, 0, 16);
        return new Guid(bytes);
    }

    private static string HexDump(IntPtr record)
    {
        int length = (ushort)Marshal.ReadInt16(record, EtwNative.RecUserDataLength);
        IntPtr data = Marshal.ReadIntPtr(record, EtwNative.RecUserData);
        int count = Math.Min(length, 64);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            sb.Append(Marshal.ReadByte(data, i).ToString("X2")).Append(' ');
        }

        if (length > count)
        {
            sb.Append("...");
        }

        return sb.ToString();
    }

    private static string Clean(string text)
    {
        text = text.TrimEnd('\r', '\n');
        return text.Replace('\r', ' ').Replace('\n', ' ');
    }

    public string Decode(IntPtr record)
    {
        ushort flags = (ushort)Marshal.ReadInt16(record, EtwNative.RecFlags);
        uint threadId = (uint)Marshal.ReadInt32(record, EtwNative.RecThreadId);
        uint processId = (uint)Marshal.ReadInt32(record, EtwNative.RecProcessId);
        long timeStamp = Marshal.ReadInt64(record, EtwNative.RecTimeStamp);
        ushort eventId = (ushort)Marshal.ReadInt16(record, EtwNative.RecEventId);
        Guid provider = ReadGuid(record, EtwNative.RecProviderId);

        string time = DateTime.FromFileTimeUtc(timeStamp).ToLocalTime().ToString("HH:mm:ss.fff");
        string head = time + " " + processId + ":" + threadId + " ";

        if (m_wpp != IntPtr.Zero && (flags & EtwNative.EVENT_HEADER_FLAG_TRACE_MESSAGE) != 0)
        {
            string error;
            string message = DecodeWpp(record, out error);
            if (error == null)
            {
                return head + message;
            }

            return head + provider.ToString("B").ToUpperInvariant() + " id=" + eventId + " (not decoded, " + error + ") " + HexDump(record);
        }

        uint size = 0;
        uint status = EtwNative.TdhGetEventInformation(record, ContextCount, m_contextArray, IntPtr.Zero, ref size);
        IntPtr info = IntPtr.Zero;
        try
        {
            if (status == EtwNative.ERROR_INSUFFICIENT_BUFFER)
            {
                info = Marshal.AllocHGlobal((int)size);
                status = EtwNative.TdhGetEventInformation(record, ContextCount, m_contextArray, info, ref size);
            }

            if (status != EtwNative.ERROR_SUCCESS)
            {
                return head + provider.ToString("B").ToUpperInvariant() + " id=" + eventId + " (not decoded, tdh " + status + ") " + HexDump(record);
            }

            return head + DecodeManifest(record, info, flags, eventId, provider);
        }
        finally
        {
            if (info != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(info);
            }
        }
    }

    private string DecodeWpp(IntPtr record, out string error)
    {
        error = null;
        uint size = 8192;
        IntPtr buffer = Marshal.AllocHGlobal((int)size + 2);
        try
        {
            ZeroTerminate(buffer, size);
            uint status = EtwNative.TdhGetWppMessage(m_wpp, record, ref size, buffer);
            if (status == EtwNative.ERROR_INSUFFICIENT_BUFFER)
            {
                Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal((int)size + 2);
                ZeroTerminate(buffer, size);
                status = EtwNative.TdhGetWppMessage(m_wpp, record, ref size, buffer);
            }

            if (status != EtwNative.ERROR_SUCCESS)
            {
                error = "tdh wpp " + status;
                return string.Empty;
            }

            return Clean(Marshal.PtrToStringUni(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ZeroTerminate(IntPtr buffer, uint size)
    {
        Marshal.WriteInt16(buffer, 0, 0);
        Marshal.WriteInt16(buffer, (int)size, 0);
    }

    private static string InfoString(IntPtr info, int offset)
    {
        return offset == 0 ? string.Empty : Marshal.PtrToStringUni(new IntPtr(info.ToInt64() + offset));
    }

    private bool ReadDependent(IntPtr record, IntPtr info, int propertyIndex, out uint value)
    {
        value = 0;
        IntPtr property = new IntPtr(info.ToInt64() + EtwNative.InfoPropertyArray + propertyIndex * EtwNative.PropertyInfoSize);
        int nameOffset = Marshal.ReadInt32(property, 4);
        EtwNative.PROPERTY_DATA_DESCRIPTOR descriptor = new EtwNative.PROPERTY_DATA_DESCRIPTOR();
        descriptor.PropertyName = (ulong)(info.ToInt64() + nameOffset);
        descriptor.ArrayIndex = uint.MaxValue;
        IntPtr descriptorPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(EtwNative.PROPERTY_DATA_DESCRIPTOR)));
        IntPtr buffer = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.StructureToPtr(descriptor, descriptorPtr, false);
            uint size;
            if (EtwNative.TdhGetPropertySize(record, ContextCount, m_contextArray, 1, descriptorPtr, out size) != EtwNative.ERROR_SUCCESS ||
                size == 0 || size > 8)
            {
                return false;
            }

            Marshal.WriteInt64(buffer, 0);
            if (EtwNative.TdhGetProperty(record, ContextCount, m_contextArray, 1, descriptorPtr, size, buffer) != EtwNative.ERROR_SUCCESS)
            {
                return false;
            }

            value = (uint)Marshal.ReadInt64(buffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(descriptorPtr);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private string DecodeManifest(IntPtr record, IntPtr info, ushort flags, ushort eventId, Guid provider)
    {
        string providerName = InfoString(info, Marshal.ReadInt32(info, EtwNative.InfoProviderName));
        if (providerName.Length == 0)
        {
            providerName = provider.ToString("B").ToUpperInvariant();
        }

        string description = providerName;
        string task = InfoString(info, Marshal.ReadInt32(info, EtwNative.InfoTaskName));
        string opcode = InfoString(info, Marshal.ReadInt32(info, EtwNative.InfoOpcodeName));
        if (task.Length > 0)
        {
            description += "/" + task;
        }

        if (opcode.Length > 0)
        {
            description += "/" + opcode;
        }

        description += " (id " + eventId + ")";

        List<string> values = new List<string>();
        List<string> names = new List<string>();
        uint pointerSize = (flags & EtwNative.EVENT_HEADER_FLAG_32_BIT_HEADER) != 0 ? 4u : 8u;
        IntPtr userData = Marshal.ReadIntPtr(record, EtwNative.RecUserData);
        int left = (ushort)Marshal.ReadInt16(record, EtwNative.RecUserDataLength);
        bool broken = false;
        bool formatError = false;
        uint topLevel = (uint)Marshal.ReadInt32(info, EtwNative.InfoTopLevelPropertyCount);

        for (int i = 0; i < topLevel && !broken; i++)
        {
            IntPtr property = new IntPtr(info.ToInt64() + EtwNative.InfoPropertyArray + i * EtwNative.PropertyInfoSize);
            uint propertyFlags = (uint)Marshal.ReadInt32(property, 0);
            string name = InfoString(info, Marshal.ReadInt32(property, 4));
            if ((propertyFlags & EtwNative.PropertyStruct) != 0)
            {
                names.Add(name);
                values.Add("<struct>");
                break;
            }

            ushort inType = (ushort)Marshal.ReadInt16(property, 8);
            ushort outType = (ushort)Marshal.ReadInt16(property, 10);
            int mapNameOffset = Marshal.ReadInt32(property, 12);
            uint count = (ushort)Marshal.ReadInt16(property, 16);
            uint length = (ushort)Marshal.ReadInt16(property, 18);
            if ((propertyFlags & EtwNative.PropertyParamCount) != 0)
            {
                ReadDependent(record, info, (int)count, out count);
            }

            if ((propertyFlags & EtwNative.PropertyParamLength) != 0)
            {
                ReadDependent(record, info, (int)length, out length);
            }

            if (inType == EtwNative.TDH_INTYPE_BINARY && outType == EtwNative.TDH_OUTTYPE_IPV6)
            {
                length = 16;
            }

            if (count == 0)
            {
                count = 1;
            }

            IntPtr map = IntPtr.Zero;
            if (mapNameOffset != 0)
            {
                IntPtr mapName = new IntPtr(info.ToInt64() + mapNameOffset);
                uint mapSize = 0;
                if (EtwNative.TdhGetEventMapInformation(record, mapName, IntPtr.Zero, ref mapSize) == EtwNative.ERROR_INSUFFICIENT_BUFFER)
                {
                    map = Marshal.AllocHGlobal((int)mapSize);
                    if (EtwNative.TdhGetEventMapInformation(record, mapName, map, ref mapSize) != EtwNative.ERROR_SUCCESS)
                    {
                        Marshal.FreeHGlobal(map);
                        map = IntPtr.Zero;
                    }
                }
            }

            try
            {
                for (uint j = 0; j < count; j++)
                {
                    uint bufferSize = 0;
                    ushort consumed;
                    IntPtr output = IntPtr.Zero;
                    try
                    {
                        uint status = EtwNative.TdhFormatProperty(
                            info, map, pointerSize, inType, outType, (ushort)length, (ushort)left, userData,
                            ref bufferSize, IntPtr.Zero, out consumed);
                        if (status == EtwNative.ERROR_INSUFFICIENT_BUFFER)
                        {
                            output = Marshal.AllocHGlobal((int)bufferSize + 2);
                            Marshal.WriteInt16(output, 0, 0);
                            status = EtwNative.TdhFormatProperty(
                                info, map, pointerSize, inType, outType, (ushort)length, (ushort)left, userData,
                                ref bufferSize, output, out consumed);
                        }

                        if (status != EtwNative.ERROR_SUCCESS || output == IntPtr.Zero)
                        {
                            names.Add(name);
                            values.Add("<format error " + status + ">");
                            formatError = true;
                            broken = true;
                            break;
                        }

                        names.Add(count > 1 ? name + "[" + j + "]" : name);
                        values.Add(Marshal.PtrToStringUni(output));
                        userData = new IntPtr(userData.ToInt64() + consumed);
                        left -= consumed;
                    }
                    finally
                    {
                        if (output != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(output);
                        }
                    }
                }
            }
            finally
            {
                if (map != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(map);
                }
            }
        }

        string message = InfoString(info, Marshal.ReadInt32(info, EtwNative.InfoEventMessage));
        string body;
        if (message.Length > 0)
        {
            body = Substitute(message, values);
        }
        else
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(names[i]).Append('=').Append(values[i]);
            }

            body = sb.ToString();
        }

        body = body.TrimEnd('\r', '\n');
        if (formatError)
        {
            body += " | raw: " + HexDump(record);
        }

        return description + ": " + Clean(body);
    }

    // Replaces %1, %2, ... (optionally "%1!format!") in a manifest message template.
    private static string Substitute(string template, List<string> values)
    {
        StringBuilder output = new StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '%' && i + 1 < template.Length)
            {
                if (char.IsDigit(template[i + 1]))
                {
                    int j = i + 1;
                    int n = 0;
                    while (j < template.Length && char.IsDigit(template[j]))
                    {
                        n = n * 10 + (template[j++] - '0');
                    }

                    if (j < template.Length && template[j] == '!')
                    {
                        int k = template.IndexOf('!', j + 1);
                        j = k < 0 ? template.Length : k + 1;
                    }

                    if (n >= 1 && n <= values.Count)
                    {
                        output.Append(values[n - 1]);
                    }

                    i = j - 1;
                    continue;
                }

                if (template[i + 1] == '%')
                {
                    output.Append('%');
                    i++;
                    continue;
                }
            }

            output.Append(c);
        }

        return output.ToString();
    }
}
