# Trnscrbr

Windows-first AI-assisted push-to-talk dictation utility.

Trnscrbr records speech from a hotkey or floating glass button, transcribes it, removes filler words and pauses, applies contextual correction, and inserts the cleaned text into the currently focused text field without pressing Enter.

See [PRODUCT_SPEC.md](PRODUCT_SPEC.md) for the current MVP specification.

## Current MVP

- Windows desktop app built with .NET/WPF.
- System tray utility with floating glass recording button.
- Default toggle recording hotkey: `Ctrl + Alt + R`.
- Default push-to-talk hotkey: `Ctrl + Alt + Space`.
- Default cancel: `Esc`.
- Paste Last Transcript is available from the tray and glass button menus.
- Free local transcription through managed whisper.cpp setup and verified local Whisper model downloads.
- Optional OpenAI bring-your-own-key transcription processing.

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
artifacts\installer\Trnscrbr-Setup-0.2.0-win-x64.exe
```

## Tester Flow

1. Install Trnscrbr with the generated installer.
2. Leave `Start Trnscrbr when Windows starts` checked unless testing startup behaviour.
3. After setup, confirm the mini settings panel opens in front of other windows.
4. Choose an AI model and microphone in the mini settings panel. For free local use, choose **Small - recommended, fastest** unless testing slower models.
5. Open Advanced Settings, go to **Local Models**, run **Free Quick Setup**, and confirm it reports local mode ready.
6. Click **Try Test Phrase**, speak for 5 seconds, and confirm the transcript appears.
7. Focus a normal editable text field.
8. Press `Ctrl + Alt + R` or `F9`, speak, then press the same shortcut again to transcribe.
9. Confirm text is inserted for review and Enter is not pressed.
10. Test `Paste Last Transcript` from the tray or glass button menu.

Useful settings while testing:

- Paste method: `Ctrl+V` by default, optional `Shift+Insert`.
- Transcription type: `Clean only` or `Rewrite`.
- Rewrite style: `Plain English`, `Professional`, `Friendly`, `Concise`, or `Native-level English`.
- English spelling: `Auto`, `British English`, `American English`, `Canadian English`, or `Australian English`.
- Capture startup buffer: `Off` or a short buffer to help avoid clipped first words.
- Hotkeys: choose preset toggle and push-to-talk shortcuts if the defaults conflict with Windows or another app.
- Diagnostics: refresh/copy logs or open the diagnostics folder.
- Local Models: run Free Quick Setup, try a test phrase, check PC guidance, download/remove verified local AI models, browse for existing local files, detect common local candidates, and optionally use Ollama cleanup from advanced local AI settings.

## Status

WPF MVP in progress. Current build includes:

- Dynamic tray icon and floating glass button with mic-level waveform states.
- `Ctrl + Alt + R` toggle recording, `Ctrl + Alt + Space` push-to-talk, and `Esc` cancel.
- First launch opens the compact mini settings panel so users can choose AI model, microphone, and rewrite style.
- Compact tray panel with monitor-aware placement, foreground launch after setup, and Windows light/dark theme support.
- Managed free local AI setup with model download, PC performance guidance, and test phrase flow.
- Optional OpenAI bring-your-own-key transcription processing.
- Temporary clipboard insertion with clipboard restoration.
- One-hour last transcript recovery from hotkey, tray menu, and floating-button menu.
- Optional voice action commands for deleting the last Trnscrbr insertion or discarding the current dictation.
- Optional cursor-context correction using Windows UI Automation.
- Advanced settings for provider, local model discovery, privacy/context, language, vocabulary, diagnostics, usage, updates, and import/export.
- Windows installer packaging script, GitHub Actions build artifact, and package smoke test.
- Single-instance relaunch behaviour that shows the running instance.

## Known MVP Limits

- GPU-accelerated local AI builds are not auto-selected yet; the built-in installer uses the official x64 CPU engine.
- Hotkeys use configurable presets rather than arbitrary custom key capture.
- Elevated/admin target windows are detected but not supported yet.
- Text insertion depends on the target app accepting clipboard paste.
- No transcript history is stored beyond temporary last-transcript recovery.
