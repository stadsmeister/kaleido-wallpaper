using System.Runtime.InteropServices;
using static KaleidoWallpaper.Core.AudioNative;

namespace KaleidoWallpaper.Core;

/// <summary>
/// Captures what one app plays (WASAPI process loopback, including its child processes, Windows 10 2004+)
/// or everything the computer plays (loopback of the default output). Audio is converted by Windows
/// to 48 kHz float stereo, mixed to mono and handed to the <see cref="SpectrumAnalyzer"/>.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    public const int SampleRate = 48000;

    private readonly SpectrumAnalyzer _analyzer;
    private Thread? _thread;
    private volatile bool _stop;

    /// <summary>Raised on the capture thread with a human-readable state.</summary>
    public event Action<string>? Status;

    public AudioCapture(SpectrumAnalyzer analyzer) => _analyzer = analyzer;

    public bool IsRunning => _thread?.IsAlive == true;

    /// <param name="processId">Process to capture (with its child processes), or null for all computer audio.</param>
    public void Start(int? processId, string label)
    {
        Stop();
        _stop = false;
        _thread = new Thread(() => Run(processId, label)) { IsBackground = true, Name = "Kaleido audio" };
        _thread.SetApartmentState(ApartmentState.MTA);      // WASAPI objects live on this MTA thread
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(1500);
        _thread = null;
    }

    public void Dispose() => Stop();

    private void Run(int? processId, string label)
    {
        IAudioClient? client = null;
        try
        {
            client = processId is int pid ? ActivateProcessLoopback(pid) : ActivateSystemLoopback();
            var format = WAVEFORMATEX.Float(SampleRate, 2);
            Marshal.ThrowExceptionForHR(client.Initialize(0 /* shared */,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                2_000_000 /* 200 ms */, 0, ref format, IntPtr.Zero));
            Guid iid = typeof(IAudioCaptureClient).GUID;
            Marshal.ThrowExceptionForHR(client.GetService(ref iid, out object service));
            var capture = (IAudioCaptureClient)service;
            Marshal.ThrowExceptionForHR(client.Start());
            Status?.Invoke(processId is null ? "Listening to all computer audio" : $"Listening to {label}");

            var interleaved = new float[9600];
            var mono = new float[4800];
            while (!_stop)
            {
                Thread.Sleep(5);
                while (!_stop && capture.GetNextPacketSize(out uint packet) == 0 && packet > 0)
                {
                    Marshal.ThrowExceptionForHR(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                    int n = (int)frames;
                    if (mono.Length < n) { mono = new float[n]; interleaved = new float[n * 2]; }
                    if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0 || data == IntPtr.Zero)
                    {
                        Array.Clear(mono, 0, n);
                    }
                    else
                    {
                        Marshal.Copy(data, interleaved, 0, n * 2);
                        for (int i = 0; i < n; i++) mono[i] = 0.5f * (interleaved[2 * i] + interleaved[2 * i + 1]);
                    }
                    capture.ReleaseBuffer(frames);
                    _analyzer.Append(mono, n);
                }
            }
            client.Stop();
            Marshal.ReleaseComObject(capture);
        }
        catch (Exception ex)
        {
            Status?.Invoke($"Can't capture audio ({ex.Message})");
        }
        finally
        {
            if (client != null) Marshal.ReleaseComObject(client);
        }
    }

    private static IAudioClient ActivateSystemLoopback()
    {
        var device = DefaultRenderDevice();
        Guid iid = typeof(IAudioClient).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object client));
        Marshal.ReleaseComObject(device);
        return (IAudioClient)client;
    }

    private static IAudioClient ActivateProcessLoopback(int processId)
    {
        var parameters = new AUDIOCLIENT_ACTIVATION_PARAMS { ActivationType = 1, TargetProcessId = (uint)processId, ProcessLoopbackMode = 0 };
        int paramSize = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
        IntPtr pParams = Marshal.AllocHGlobal(paramSize);
        IntPtr pVariant = Marshal.AllocHGlobal(24);                       // PROPVARIANT
        try
        {
            Marshal.StructureToPtr(parameters, pParams, false);
            for (int i = 0; i < 24; i++) Marshal.WriteByte(pVariant, i, 0);
            Marshal.WriteInt16(pVariant, 0, 65);                          // VT_BLOB
            Marshal.WriteInt32(pVariant, 8, paramSize);                   // blob.cbSize
            Marshal.WriteIntPtr(pVariant, IntPtr.Size == 8 ? 16 : 12, pParams);   // blob.pBlobData

            var handler = new ActivationHandler();
            ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, typeof(IAudioClient).GUID,
                                        pVariant, handler, out _);
            return handler.Wait(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Marshal.FreeHGlobal(pVariant);
            Marshal.FreeHGlobal(pParams);
        }
    }

    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        private readonly ManualResetEventSlim _done = new();
        private IAudioClient? _client;
        private int _hr;

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            int hr = operation.GetActivateResult(out int activateResult, out object iface);
            _hr = hr < 0 ? hr : activateResult;
            _client = iface as IAudioClient;
            _done.Set();
            return 0;
        }

        public IAudioClient Wait(TimeSpan timeout)
        {
            if (!_done.Wait(timeout)) throw new TimeoutException("audio activation timed out");
            Marshal.ThrowExceptionForHR(_hr);
            return _client ?? throw new InvalidOperationException("no audio client");
        }
    }
}
