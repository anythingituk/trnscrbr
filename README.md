# Trnscrbr

Windows-first AI-assisted push-to-talk dictation utility.

Trnscrbr records speech from a hotkey or floating glass button, transcribes it, removes filler words and pauses, applies contextual correction, and inserts the cleaned text into the currently focused text field without pressing Enter.

See [PRODUCT_SPEC.md](PRODUCT_SPEC.md) for the current MVP specification.

## MVP Direction

- Windows desktop app.
- .NET/WPF or .NET/WinUI.
- System tray utility with floating glass recording button.
- Default hotkey: `Ctrl + Win + Space`.
- Default cancel: `Esc`.
- Default Paste Last Transcript: `Ctrl + Win + V`.
- OpenAI bring-your-own-key first.
- Local mode with explicit model downloads.

## Build Prerequisites

- Windows 10/11
- .NET 8 SDK with Windows Desktop workload/runtime

Build:

```powershell
dotnet build .\Trnscrbr.sln
```

## Status

WPF MVP in progress. Current build includes:

- Dynamic tray icon and floating glass button with mic-level waveform states.
- `Ctrl + Win + Space` push-to-talk/toggle, `Esc` cancel, and `Ctrl + Win + V` paste last transcript.
- OpenAI bring-your-own-key transcription and cleanup.
- Temporary clipboard insertion with clipboard restoration.
- One-hour last transcript recovery from hotkey, tray menu, and floating-button menu.
- Optional voice action command for deleting the last Trnscrbr insertion.
- Advanced settings for provider, local model discovery, privacy/context, language, vocabulary, diagnostics, usage, and import/export.
- Windows installer packaging script and GitHub Actions build artifact.
