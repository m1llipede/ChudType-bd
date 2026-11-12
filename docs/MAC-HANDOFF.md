# ChudType for Mac handoff

## What works

- Apple Silicon self-contained application.
- Private, offline English streaming transcription through Sherpa-ONNX.
- Native AVFoundation microphone capture at 16 kHz mono.
- Hold-to-talk or hands-free activation using F-keys, Space, middle mouse, or mouse buttons 4/5, with an optional modifier.
- Compact controller with distinct ready and recording states.
- Automatic insertion into the application where dictation started when Accessibility is granted.
- Clipboard fallback when automatic insertion is unavailable.
- Local dictionary, snippets, text replacements, launch-at-login, Dock visibility, and light/dark/system appearance.
- Verified model download with a fixed SHA-256 digest.

## Build and package

Requires macOS, Xcode command-line tools, and .NET 10:

```zsh
DOTNET_BIN=/path/to/dotnet ./packaging/Package-Mac.sh
```

The ZIP, DMG, and checksums are written to `artifacts/mac/release`.

## Validation completed

- Release build completes with zero warnings and zero errors.
- Portable core tests: 7 passed.
- Sherpa streaming integration test: 1 passed.
- The installed app captured live microphone audio and returned a local transcript.
- Ready, recording, Settings, and shortcut states were visually inspected on an Apple M4 Max Mac.

## Distribution limitations

Local builds are ad-hoc signed. Public downloads should be signed with the project owner's Apple Developer ID and notarized by Apple. The first launch downloads the English model, so that upstream model URL must remain available. Intel macOS is not currently packaged or tested.

## Windows status

The original Windows application remains intact, and the portable core and Sherpa projects cross-build from macOS. A real Windows machine is still required for end-to-end microphone, shortcut, text-target, installer, and GPU validation.
