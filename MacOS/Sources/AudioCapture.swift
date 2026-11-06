import AppKit
import CoreMedia
import ScreenCaptureKit

/// Captures the sound of one app (or of the whole computer) with ScreenCaptureKit.
/// Only audio is used; the required video stream is kept at 2×2 pixels, 2 fps.
final class AudioCapture: NSObject, SCStreamOutput, SCStreamDelegate {
    static let sampleRate = 48_000

    private let analyzer: SpectrumAnalyzer
    private let queue = DispatchQueue(label: "kaleido.audio")
    private var stream: SCStream?
    private var mono = [Float](repeating: 0, count: 8192)     // capture queue only

    /// Human-readable state for the menu ("Listening to Music", "Waiting for Spotify to start", …).
    var onStatus: ((String) -> Void)?

    init(analyzer: SpectrumAnalyzer) {
        self.analyzer = analyzer
    }

    @MainActor
    func start(bundleID: String?, name: String?) async {
        await stop()
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
            guard let display = content.displays.first else {
                report("No display found")
                return
            }
            let filter: SCContentFilter
            if let bundleID {
                guard let app = content.applications.first(where: { $0.bundleIdentifier == bundleID }) else {
                    report("Waiting for \(name ?? bundleID) to start")
                    return
                }
                filter = SCContentFilter(display: display, including: [app], exceptingWindows: [])
            } else {
                filter = SCContentFilter(display: display, excludingApplications: [], exceptingWindows: [])
            }

            let config = SCStreamConfiguration()
            config.capturesAudio = true
            config.excludesCurrentProcessAudio = true
            config.sampleRate = AudioCapture.sampleRate
            config.channelCount = 2
            config.width = 2
            config.height = 2
            config.minimumFrameInterval = CMTime(value: 1, timescale: 2)
            config.showsCursor = false

            let newStream = SCStream(filter: filter, configuration: config, delegate: self)
            try newStream.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue)
            try newStream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)   // frames are ignored
            try await newStream.startCapture()
            stream = newStream
            report(bundleID == nil ? "Listening to all computer audio" : "Listening to \(name ?? bundleID!)")
        } catch {
            if !CGPreflightScreenCaptureAccess() {
                report("No Screen Recording permission")
            } else {
                report("Can't capture audio: \(error.localizedDescription)")
            }
        }
    }

    @MainActor
    func stop() async {
        guard let current = stream else { return }
        stream = nil
        try? await current.stopCapture()
    }

    private func report(_ text: String) {
        DispatchQueue.main.async { [weak self] in self?.onStatus?(text) }
    }

    // MARK: SCStreamOutput

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .audio, sampleBuffer.isValid else { return }
        let frames = sampleBuffer.numSamples
        guard frames > 0 else { return }
        try? sampleBuffer.withAudioBufferList { list, _ in
            if mono.count < frames { mono = [Float](repeating: 0, count: frames) }
            mono.withUnsafeMutableBufferPointer { out in
                for i in 0..<frames { out[i] = 0 }
                var channels = 0
                // ScreenCaptureKit delivers Float32; handle both planar and interleaved layouts
                for buffer in list {
                    guard let data = buffer.mData else { continue }
                    let ch = max(1, Int(buffer.mNumberChannels))
                    let samples = data.assumingMemoryBound(to: Float.self)
                    let n = min(frames, Int(buffer.mDataByteSize) / (MemoryLayout<Float>.size * ch))
                    for c in 0..<ch {
                        for i in 0..<n { out[i] += samples[i * ch + c] }
                    }
                    channels += ch
                }
                guard channels > 0, let base = out.baseAddress else { return }
                let gain = 1 / Float(channels)
                for i in 0..<frames { out[i] *= gain }
                analyzer.append(base, count: frames)
            }
        }
    }

    // MARK: SCStreamDelegate

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        DispatchQueue.main.async { [weak self] in
            guard let self, self.stream === stream else { return }
            self.stream = nil
            self.onStatus?("Capture stopped")
        }
    }
}
