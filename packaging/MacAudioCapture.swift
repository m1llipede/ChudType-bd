import AVFoundation
import Foundation

func fail(_ message: String, code: Int32 = 2) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(code)
}

let outputHandle: FileHandle
if CommandLine.arguments.count > 1 {
    guard let handle = FileHandle(forWritingAtPath: CommandLine.arguments[1]) else {
        fail("The private ChudType audio stream could not be opened.")
    }
    outputHandle = handle
} else {
    outputHandle = FileHandle.standardOutput
}

var activeEngine: AVAudioEngine?

func startCapture() {
    let engine = AVAudioEngine()
    let input = engine.inputNode
    let source = input.outputFormat(forBus: 0)
    guard source.sampleRate > 0, source.channelCount > 0 else { fail("No working microphone input is available.") }
    guard let target = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 16_000,
                                     channels: 1, interleaved: true),
          let converter = AVAudioConverter(from: source, to: target) else {
        fail("The microphone format could not be converted for transcription.")
    }

    input.installTap(onBus: 0, bufferSize: 1_920, format: source) { buffer, _ in
        let capacity = AVAudioFrameCount(ceil(Double(buffer.frameLength) * 16_000 / source.sampleRate)) + 8
        guard let output = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
        var supplied = false
        var conversionError: NSError?
        converter.convert(to: output, error: &conversionError) { _, status in
            if supplied {
                status.pointee = .noDataNow
                return nil
            }
            supplied = true
            status.pointee = .haveData
            return buffer
        }
        guard conversionError == nil, output.frameLength > 0,
              let samples = output.int16ChannelData?[0] else { return }
        let byteCount = Int(output.frameLength) * MemoryLayout<Int16>.size
        outputHandle.write(Data(bytes: samples, count: byteCount))
    }

    do {
        try engine.start()
        activeEngine = engine
    } catch {
        fail("The microphone could not start: \(error.localizedDescription)")
    }
}

switch AVCaptureDevice.authorizationStatus(for: .audio) {
case .authorized:
    startCapture()
case .notDetermined:
    AVCaptureDevice.requestAccess(for: .audio) { granted in
        DispatchQueue.main.async {
            if granted { startCapture() }
            else { fail("Microphone access is off. Enable ChudType in System Settings → Privacy & Security → Microphone.") }
        }
    }
default:
    fail("Microphone access is off. Enable ChudType in System Settings → Privacy & Security → Microphone.")
}

RunLoop.main.run()
