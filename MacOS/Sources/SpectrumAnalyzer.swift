import Accelerate
import Foundation

/// Turns raw audio into the same byte spectrum a Web Audio `AnalyserNode` produces
/// (Blackman window, exponential smoothing, -100…-30 dB mapped to 0…255), so the
/// kaleidoscope reacts like it does in the browser visualizer.
///
/// FFT size and smoothing are adjustable: a shorter window and less smoothing react sooner
/// (the window's centre lies half a window in the past, smoothing adds a few frames).
final class SpectrumAnalyzer {
    static let maxSize = 4096
    let minDecibels: Float = -100
    let maxDecibels: Float = -30

    private(set) var fftSize = 4096                 // main thread
    private var smoothing: Float = 0.65             // main thread
    private var setups: [Int: FFTSetup] = [:]       // by fft size
    private var windows: [Int: [Float]] = [:]
    private var smoothed: [Float]                   // main thread

    private var ring = [Float](repeating: 0, count: SpectrumAnalyzer.maxSize)
    private var writeIndex = 0
    private var lastInput = Date.distantPast
    private let lock = NSLock()

    init() {
        for log2n in [11, 12] {
            let n = 1 << log2n
            guard let setup = vDSP_create_fftsetup(vDSP_Length(log2n), FFTRadix(kFFTRadix2)) else {
                fatalError("Could not set up the FFT")
            }
            setups[n] = setup
            windows[n] = (0..<n).map { i in
                let x = Float(i) / Float(n)
                return 0.42 - 0.5 * cos(2 * .pi * x) + 0.08 * cos(4 * .pi * x)   // Blackman, as in Web Audio
            }
        }
        smoothed = [Float](repeating: 0, count: fftSize / 2)
    }

    deinit { setups.values.forEach { vDSP_destroy_fftsetup($0) } }

    /// Main thread. `fftSize` must be 2048 or 4096.
    func configure(fftSize: Int, smoothing: Float) {
        let size = setups[fftSize] != nil ? fftSize : 4096
        if size != self.fftSize { smoothed = [Float](repeating: 0, count: size / 2) }
        self.fftSize = size
        self.smoothing = smoothing
    }

    /// Called from the capture queue with mono samples.
    func append(_ samples: UnsafePointer<Float>, count: Int) {
        lock.lock()
        defer { lock.unlock() }
        let mask = SpectrumAnalyzer.maxSize - 1
        for i in 0..<count {
            ring[writeIndex] = samples[i]
            writeIndex = (writeIndex + 1) & mask
        }
        lastInput = Date()
    }

    /// True while audio buffers are arriving (also when they contain silence).
    var hasRecentInput: Bool {
        lock.lock()
        defer { lock.unlock() }
        return Date().timeIntervalSince(lastInput) < 0.5
    }

    /// Byte spectrum (fftSize / 2 values) of the most recent fftSize samples. Main thread only.
    func byteSpectrum() -> [UInt8] {
        let n = fftSize, half = n / 2
        guard let setup = setups[n], let window = windows[n] else { return [] }
        let log2n = vDSP_Length(n == 2048 ? 11 : 12)

        var frame = [Float](repeating: 0, count: n)
        lock.lock()
        let mask = SpectrumAnalyzer.maxSize - 1
        let start = writeIndex - n                       // the newest n samples, oldest first
        for i in 0..<n { frame[i] = ring[(start + i) & mask] }
        lock.unlock()

        var windowed = [Float](repeating: 0, count: n)
        vDSP.multiply(frame, window, result: &windowed)

        var real = [Float](repeating: 0, count: half)
        var imag = [Float](repeating: 0, count: half)
        var mags = [Float](repeating: 0, count: half)
        real.withUnsafeMutableBufferPointer { rp in
            imag.withUnsafeMutableBufferPointer { ip in
                var split = DSPSplitComplex(realp: rp.baseAddress!, imagp: ip.baseAddress!)
                windowed.withUnsafeBufferPointer { fp in
                    fp.baseAddress!.withMemoryRebound(to: DSPComplex.self, capacity: half) { cp in
                        vDSP_ctoz(cp, 2, &split, 1, vDSP_Length(half))
                    }
                }
                vDSP_fft_zrip(setup, &split, 1, log2n, FFTDirection(FFT_FORWARD))
                vDSP_zvabs(&split, 1, &mags, 1, vDSP_Length(half))
            }
        }

        // vDSP's real FFT is scaled by 2; Web Audio divides the magnitude by fftSize.
        let scale = 1 / Float(2 * n)
        mags[0] = 0                                       // DC (holds the packed Nyquist value too)
        let range = maxDecibels - minDecibels
        var out = [UInt8](repeating: 0, count: half)
        for k in 0..<half {
            smoothed[k] = smoothing * smoothed[k] + (1 - smoothing) * mags[k] * scale
            let db = 20 * log10(max(smoothed[k], 1e-12))
            out[k] = UInt8(max(0, min(255, (db - minDecibels) / range * 255)))
        }
        return out
    }
}
