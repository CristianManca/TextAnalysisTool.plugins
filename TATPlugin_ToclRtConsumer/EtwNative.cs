using System;
using System.Runtime.InteropServices;

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void EventRecordCallback(IntPtr eventRecord);

// Native ETW / TDH declarations used by the real-time consumer.
internal static class EtwNative
{
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_ACCESS_DENIED = 5;
    public const uint ERROR_INSUFFICIENT_BUFFER = 122;
    public const uint ERROR_MORE_DATA = 234;
    public const uint ERROR_WMI_INSTANCE_NOT_FOUND = 4201;

    public const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
    public const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    public const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
    public const uint EVENT_TRACE_CONTROL_QUERY = 0;

    public const ushort EVENT_HEADER_FLAG_32_BIT_HEADER = 0x0020;
    public const ushort EVENT_HEADER_FLAG_TRACE_MESSAGE = 0x0100;

    public const int TDH_CONTEXT_WPP_TMFFILE = 0;
    public const int TDH_CONTEXT_WPP_TMFSEARCHPATH = 1;
    public const int TDH_CONTEXT_PDB_PATH = 4;

    public const ushort TDH_INTYPE_BINARY = 14;
    public const ushort TDH_OUTTYPE_IPV6 = 24;

    public const uint PropertyStruct = 0x1;
    public const uint PropertyParamLength = 0x2;
    public const uint PropertyParamCount = 0x4;

    // Layout of EVENT_RECORD (offsets are the same on x86 and x64 up to UserData)
    public const int RecFlags = 4;
    public const int RecThreadId = 8;
    public const int RecProcessId = 12;
    public const int RecTimeStamp = 16;
    public const int RecProviderId = 24;
    public const int RecEventId = 40;
    public const int RecUserDataLength = 86;
    public static int RecUserData { get { return 88 + IntPtr.Size; } }

    // Layout of EVENT_TRACE_PROPERTIES
    public const int PropsSize = 120;
    public const int PropsLogFileMode = 64;
    public static int PropsLogFileNameOffset { get { return 104 + IntPtr.Size; } }
    public static int PropsLoggerNameOffset { get { return 108 + IntPtr.Size; } }

    // Layout of TRACE_EVENT_INFO / EVENT_PROPERTY_INFO
    public const int InfoProviderName = 52;
    public const int InfoTaskName = 68;
    public const int InfoOpcodeName = 72;
    public const int InfoEventMessage = 76;
    public const int InfoTopLevelPropertyCount = 104;
    public const int InfoPropertyArray = 112;
    public const int PropertyInfoSize = 24;

    [StructLayout(LayoutKind.Sequential)]
    public struct TDH_CONTEXT
    {
        public ulong ParameterValue;
        public int ParameterType;
        public uint ParameterSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTY_DATA_DESCRIPTOR
    {
        public ulong PropertyName;
        public uint ArrayIndex;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Ints3 { public int a, b, c; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Ints8 { public int a, b, c, d, e, f, g, h; }

    // TIME_ZONE_INFORMATION is 172 bytes; only its size matters here.
    [StructLayout(LayoutKind.Sequential)]
    public struct TimeZoneBlock
    {
        public Ints8 p0, p1, p2, p3, p4;
        public Ints3 p5;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EVENT_TRACE
    {
        public ushort Size;
        public ushort FieldTypeFlags;
        public uint Version;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid Guid;
        public uint KernelTime;
        public uint UserTime;
        public uint InstanceId;
        public uint ParentInstanceId;
        public Guid ParentGuid;
        public IntPtr MofData;
        public uint MofLength;
        public uint ClientContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACE_LOGFILE_HEADER
    {
        public uint BufferSize;
        public uint Version;
        public uint ProviderVersion;
        public uint NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint BuffersWritten;
        public Guid LogInstanceGuid;
        public IntPtr LoggerName;
        public IntPtr LogFileName;
        public TimeZoneBlock TimeZone;
        public long BootTime;
        public long PerfFreq;
        public long StartTime;
        public uint ReservedFlags;
        public uint BuffersLost;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EVENT_TRACE_LOGFILE
    {
        public IntPtr LogFileName;
        public IntPtr LoggerName;
        public long CurrentTime;
        public uint BuffersRead;
        public uint ProcessTraceMode;
        public EVENT_TRACE CurrentEvent;
        public TRACE_LOGFILE_HEADER LogfileHeader;
        public IntPtr BufferCallback;
        public uint BufferSize;
        public uint Filled;
        public uint EventsLost;
        public IntPtr EventRecordCallback;
        public uint IsKernelTrace;
        public IntPtr Context;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ControlTraceW(ulong traceHandle, string instanceName, IntPtr properties, uint controlCode);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ulong OpenTraceW(IntPtr logfile);

    [DllImport("advapi32.dll")]
    public static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

    [DllImport("advapi32.dll")]
    public static extern uint CloseTrace(ulong traceHandle);

    [DllImport("tdh.dll")]
    public static extern uint TdhGetEventInformation(IntPtr eventRecord, uint contextCount, IntPtr contexts, IntPtr buffer, ref uint bufferSize);

    [DllImport("tdh.dll")]
    public static extern uint TdhGetEventMapInformation(IntPtr eventRecord, IntPtr mapName, IntPtr buffer, ref uint bufferSize);

    [DllImport("tdh.dll")]
    public static extern uint TdhFormatProperty(
        IntPtr eventInfo, IntPtr mapInfo, uint pointerSize, ushort propertyInType, ushort propertyOutType,
        ushort propertyLength, ushort userDataLength, IntPtr userData, ref uint bufferSize, IntPtr buffer,
        out ushort userDataConsumed);

    [DllImport("tdh.dll")]
    public static extern uint TdhGetPropertySize(IntPtr eventRecord, uint contextCount, IntPtr contexts, uint propertyDataCount, IntPtr propertyData, out uint size);

    [DllImport("tdh.dll")]
    public static extern uint TdhGetProperty(IntPtr eventRecord, uint contextCount, IntPtr contexts, uint propertyDataCount, IntPtr propertyData, uint bufferSize, IntPtr buffer);

    [DllImport("tdh.dll")]
    public static extern uint TdhOpenDecodingHandle(out IntPtr handle);

    [DllImport("tdh.dll")]
    public static extern uint TdhCloseDecodingHandle(IntPtr handle);

    [DllImport("tdh.dll")]
    public static extern uint TdhSetDecodingParameter(IntPtr handle, ref TDH_CONTEXT context);

    [DllImport("tdh.dll")]
    public static extern uint TdhGetWppMessage(IntPtr handle, IntPtr eventRecord, ref uint bufferSize, IntPtr buffer);
}
