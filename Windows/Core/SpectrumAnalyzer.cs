namespace KaleidoWallpaper.Core;

/// <summary>
/// Turns raw audio into the same byte spectrum a Web Audio <c>AnalyserNode</c> produces
/// (Blackman window, exponential smoothing, -100…-30 dB mapped to 0…255), so the kaleidoscope
/// reacts exactly like the browser visualizer and the macOS version.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int MaxSize = 4096;
    private const double MinDecibels = -100, MaxDecibels = -30;

    private readonly float[] _ring = new float[MaxSize];
    private readonly object _lock = new();
    private int _writeIndex;
    private long _lastInputTicks;

    // only touched on the UI thread
    private int _fftSize = 4096;
    private double _smoothing = 0.65;
    private double[] _smoothed = new double[MaxSize / 2];
    private readonly Dictionary<int, double[]> _windows = new();

    public SpectrumAnalyzer()
    {
        foreach (var n in new[] { 2048, 4096 })
        {
            var w = new double[n];
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                w[i] = 0.42 - 0.5 * Math.Cos(2 * Math.PI * x) + 0.08 * Math.Cos(4 * Math.PI * x);   // Blackman
            }
            _windows[n] = w;
        }
    }

    public int FftSize => _fftSize;

    /// <summary>UI thread. <paramref name="fftSize"/> must be 2048 or 4096.</summary>
    public void Configure(int fftSize, double smoothing)
    {
        int size = _windows.ContainsKey(fftSize) ? fftSize : 4096;
        if (size != _fftSize) _smoothed = new double[size / 2];
        _fftSize = size;
        _smoothing = smoothing;
    }

    /// <summary>Called from the capture thread with mono samples.</summary>
    public void Append(float[] samples, int count)
    {
        lock (_lock)
        {
            for (int i = 0; i < count; i++)
            {
                _ring[_writeIndex] = samples[i];
                _writeIndex = (_writeIndex + 1) & (MaxSize - 1);
            }
            _lastInputTicks = Environment.TickCount64;
        }
    }

    /// <summary>True while audio buffers arrive (also when they contain silence).</summary>
    public bool HasRecentInput
    {
        get { lock (_lock) return Environment.TickCount64 - _lastInputTicks < 500; }
    }

    /// <summary>Byte spectrum (fftSize / 2 values) of the newest fftSize samples. UI thread only.</summary>
    public byte[] ByteSpectrum()
    {
        int n = _fftSize, half = n / 2;
        var re = new double[n];
        var im = new double[n];
        var window = _windows[n];
        lock (_lock)
        {
            int start = _writeIndex - n;                       // oldest of the newest n samples
            for (int i = 0; i < n; i++) re[i] = _ring[(start + i) & (MaxSize - 1)] * window[i];
        }
        Fft(re, im);

        var output = new byte[half];
        for (int k = 0; k < half; k++)
        {
            double mag = k == 0 ? 0 : Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / n;   // Web Audio scaling
            _smoothed[k] = _smoothing * _smoothed[k] + (1 - _smoothing) * mag;
            double db = 20 * Math.Log10(Math.Max(_smoothed[k], 1e-12));
            output[k] = (byte)Math.Clamp((db - MinDecibels) / (MaxDecibels - MinDecibels) * 255, 0, 255);
        }
        return output;
    }

    /// <summary>In-place iterative radix-2 FFT.</summary>
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            int halfLen = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < halfLen; k++)
                {
                    int a = i + k, b = a + halfLen;
                    double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    double t = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = t;
                }
            }
        }
    }
}
