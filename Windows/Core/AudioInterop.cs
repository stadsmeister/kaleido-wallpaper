using System.Runtime.InteropServices;

namespace KaleidoWallpaper.Core;

// Minimal Core Audio (WASAPI) COM definitions. Methods are declared in vtable order;
// unused trailing methods are left out, unused methods in between are declared with simple types.

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WAVEFORMATEX
{
    public ushort wFormatTag, nChannels;
    public uint nSamplesPerSec, nAvgBytesPerSec;
    public ushort nBlockAlign, wBitsPerSample, cbSize;

    /// <summary>32-bit float PCM.</summary>
    public static WAVEFORMATEX Float(int sampleRate, int channels) => new()
    {
        wFormatTag = 3,                                   // WAVE_FORMAT_IEEE_FLOAT
        nChannels = (ushort)channels,
        nSamplesPerSec = (uint)sampleRate,
        wBitsPerSample = 32,
        nBlockAlign = (ushort)(channels * 4),
        nAvgBytesPerSec = (uint)(sampleRate * channels * 4),
        cbSize = 0
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct AUDIOCLIENT_ACTIVATION_PARAMS
{
    public int ActivationType;                            // 1 = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
    public uint TargetProcessId;
    public int ProcessLoopbackMode;                       // 0 = include the target's whole process tree
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                               [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity,
                                 [In] ref WAVEFORMATEX format, IntPtr audioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, [In] ref WAVEFORMATEX format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig] int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
}

[ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
}

/// <summary>Marker: the completion handler may be called from any thread.</summary>
[ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAgileObject { }

// audio sessions (to list the apps that play sound)
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr volume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
}

[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName(IntPtr name, IntPtr eventContext);
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath(IntPtr path, IntPtr eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingId);
    [PreserveSig] int SetGroupingParam(ref Guid groupingId, IntPtr eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    // IAudioSessionControl2
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint processId);
    [PreserveSig] int IsSystemSoundsSession();
}

internal static class AudioNative
{
    public const int CLSCTX_ALL = 23;
    public const int eRender = 0, eMultimedia = 1;
    public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000,
                      AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000,
                      AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    public const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = "VAD\\Process_Loopback";

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    public static IMMDevice DefaultRenderDevice()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device));
        return device;
    }
}
