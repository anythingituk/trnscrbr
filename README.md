# Trnscrbr

Windows-first AI-assisted push-to-talk dictation utility.

Trnscrbr records speech from a hotkey or floating glass button, transcribes it, removes filler words and pauses, applies contextual correction, and inserts the cleaned text into the currently focused text field without pressing Enter.

See [PRODUCT_SPEC.md](PRODUCT_SPEC.md) for the current MVP specification.

## Current MVP

- Windows desktop app built with .NET/WPF.
- System tray utility with floating glass recording button.
- Default hotkey: `Ctrl + Win + Space`.
- Default cancel: `Esc`.
- Paste Last Transcript is available from the tray and glass button menus.
- OpenAI bring-your-own-key transcription processing.
- Free local transcription through user-installed whisper.cpp and local Whisper model files, with optional Ollama cleanup.

## Build Prerequisites

- Windows 10/11
- .NET 8 SDK with Windows Desktop workload/runtime
- Inno Setup 6 for installer builds

Build:

```powershell
dotnet build .\Trnscrbr.sln
```

Build installer:

```powershell
.\scripts\test-package.ps1
```

Installer output:

```text
artifacts\installer\Trnscrbr-Setup-0.1.0-win-x64.exe
```

## Tester Flow

1. Install Trnscrbr with the generated installer.
2. Leave `Start Trnscrbr when Windows starts` checked unless testing startup behaviour.
3. Open Trnscrbr from the tray icon and go to Advanced Settings.
4. Add an OpenAI API key on the Provider tab.
5. Focus a normal editable text field.
6. Hold `Ctrl + Win + Space`, speak, then release to transcribe.
7. Confirm text is inserted for review and Enter is not pressed.
8. Test `Paste Last Transcript` from the tray or glass button menu.

Useful settings while testing:

- Paste method: `Ctrl+V` by default, optional `Shift+Insert`.
- Transcription type: `Clean only` or `Rewrite`.
- Rewrite style: `Plain English`, `Professional`, `Friendly`, `Concise`, or `Native-level English`.
- English spelling: `Auto`, `British English`, `American English`, `Canadian English`, or `Australian English`.
- Capture startup buffer: helps avoid clipped first words.
- Diagnostics: refresh/copy logs or open the diagnostics folder.
- Local Models: run a free quick setup, install the official x64 whisper.cpp CLI, download/remove a verified Whisper model, browse for existing local files, detect common local candidates, and optionally use Ollama cleanup.

## Status

WPF MVP in progress. Current build includes:

- Dynamic tray icon and floating glass button with mic-level waveform states.
- `Ctrl + Win + Space` hold-to-record and `Esc` cancel.
- Lightweight first-run onboarding.
- Compact tray panel with usage estimate, monitor-aware placement, and Windows light/dark theme support.
- OpenAI bring-your-own-key transcription processing.
- Temporary clipboard insertion with clipboard restoration.
- One-hour last transcript recovery from hotkey, tray menu, and floating-button menu.
- Optional voice action commands for deleting the last Trnscrbr insertion or discarding the current dictation.
- Optional cursor-context correction using Windows UI Automation.
- Advanced settings for provider, local model discovery, privacy/context, language, vocabulary, diagnostics, usage, updates, and import/export.
- Windows installer packaging script, GitHub Actions build artifact, and package smoke test.
- Single-instance relaunch behaviour that shows the running instance.

## Known MVP Limits

- GPU-accelerated whisper.cpp builds are not auto-selected yet; the built-in installer uses the official x64 CPU CLI.
- Hotkeys are fixed for now.
- Elevated/admin target windows are detected but not supported yet.
- Text insertion depends on the target app accepting clipboard paste.
- No transcript history is stored beyond temporary last-transcript recovery.
