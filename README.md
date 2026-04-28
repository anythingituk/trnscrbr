# Trnscrbr

Windows-first AI-assisted push-to-talk dictation utility.

Trnscrbr records speech from a hotkey or floating glass button, transcribes it, removes filler words and pauses, applies contextual correction, and inserts the cleaned text into the currently focused text field without pressing Enter.

See [PRODUCT_SPEC.md](PRODUCT_SPEC.md) for the current MVP specification.

## Current MVP

- Windows desktop app built with .NET/WPF.
- System tray utility with floating glass recording button.
- Default hotkey: `Ctrl + Win + Space`.
- Default cancel: `Esc`.
- Default Paste Last Transcript: `Ctrl + Win + V`.
- OpenAI bring-your-own-key transcription and cleanup.
- Local model detection only. Local transcription/cleanup execution is not enabled yet.

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
8. Test `Ctrl + Win + V` to paste the last transcript again.

Useful settings while testing:

- Paste method: `Ctrl+V` by default, optional `Shift+Insert`.
- Capture startup buffer: helps avoid clipped first words.
- Diagnostics: refresh/copy logs or open the diagnostics folder.
- Local Models: detects candidate local models but does not use them.

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
- Single-instance relaunch behaviour that shows the running instance.
- Package smoke test script.

## Known MVP Limits

- Local mode cannot transcribe yet.
- Hotkeys are fixed for now.
- Elevated/admin target windows are not supported yet.
- Text insertion depends on the target app accepting clipboard paste.
- No transcript history is stored beyond temporary last-transcript recovery.
