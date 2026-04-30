# Trnscrbr MVP Product Specification

## Product Summary

Trnscrbr is a Windows-first AI-assisted dictation utility. The user records speech with a push-to-talk hotkey or a minimal floating glass button. The app transcribes audio, removes filler words, pauses, and stutters, applies contextual correction, then inserts the cleaned text directly into the currently focused editable field without pressing Enter.

The MVP is Windows-first using .NET with WPF or WinUI. Mac support should be considered later as a proper platform implementation, not forced into the first build.

## Core User Flow

1. User focuses a text field in any normal user-level Windows app.
2. User presses `Ctrl + Win + Space`.
3. Floating glass button appears near bottom-center above the taskbar.
4. Recording starts.
5. User releases the hotkey to stop recording and begin transcription, or uses tray/glass manual controls.
6. App transcribes and cleans the text.
7. App inserts the result into the focused text field using temporary clipboard paste.
8. App restores the previous clipboard contents.
9. User reviews the inserted text in the target app and manually sends/submits if desired.

The app must never press Enter automatically.

## MVP Platform

- Windows MVP first.
- Native Windows desktop app using .NET/WPF or .NET/WinUI.
- Prefer mature Windows integration over cross-platform abstraction for MVP.
- Keep transcription/provider logic modular enough for future Mac implementation.

## App Surfaces

### System Tray

The app always has a system tray icon while running.

Tray/context menu:

- Start Recording / Stop Recording
- Paste Last Transcript
- Show/Hide Floating Button
- Microphone fly-out
- Settings
- Advanced Settings
- Quit

The tray icon also opens the slide-up quick panel from the system tray area.

### Floating Glass Button

Default behavior:

- Hidden after setup.
- Appears when user uses the hotkey or explicitly enables it.
- Defaults to bottom-center above the Windows taskbar.
- User can move it.
- Auto-hides a few seconds after returning to idle.
- Left-click toggles manual recording.
- Right-click opens compact context menu.
- Right-click menu includes recording, Paste Last Transcript, Settings, and Quit.

Visual states:

- Idle: smaller glass button with fixed blue/green/purple glow.
- Recording: larger button/pill with red/orange glow.
- Processing/transcribing: distinct third state, currently green so it is clearly different from recording.

Recording animation:

- Button can expand into a pill.
- Show waveform-style animation reacting to actual microphone input levels.
- Show elapsed timer only after recording has lasted longer than 1 minute.
- At 1, 2, and 3 minutes, show subtle visual duration awareness; never auto-stop.

Cancel:

- No visible cancel button in MVP.
- `Esc` cancels recording or processing.
- Cancel discards audio/result and inserts nothing.

### Settings

Use two settings surfaces:

- Slide-up tray panel for common settings and current status.
- Advanced settings window for deeper configuration.

Slide-up panel:

- Current provider/mode summary.
- Active engine/model.
- Transcription type.
- Microphone choice.
- Start/stop/status.
- Floating button toggle.
- Startup toggle.
- Trailing space toggle.
- Usage/cost warning indicator.

Advanced window:

- Provider setup and API key management.
- Local model downloads.
- Hotkey settings.
- Privacy/context settings.
- Language settings.
- Custom vocabulary.
- Import/export.
- Error log.
- Usage and estimated costs.
- Updates.
- Diagnostics.

Settings should follow Windows light/dark mode automatically.

## Hotkeys

Defaults:

- `Ctrl + Win + Space`: hold to record, release to transcribe.
- `Esc`: cancel current recording or processing.
- Paste Last Transcript: no global shortcut for now; available from tray and floating-button menus.

Push-to-talk behavior:

- Holding `Ctrl + Win + Space` records while held and stops/transcribes on release.
- Quick tapping `Ctrl + Win + Space` no longer toggles recording; release always ends recording.
- Manual start/stop remains available from the tray and floating glass button controls.

All hotkeys should be configurable later through settings where practical.

## Text Insertion

Default insertion method:

1. Save current clipboard contents.
2. Put cleaned transcript on clipboard.
3. Send `Ctrl+V` to focused app.
4. Restore previous clipboard contents shortly after.

Requirements:

- Never press Enter.
- Preserve punctuation, new lines, and non-English characters.
- Add trailing space by default.
- Trailing space is configurable.
- If insertion fails, keep the cleaned transcript in temporary last-transcript recovery.
- Show a small failure message near the floating button.
- Log failure metadata without transcript content.

Paste Last Transcript:

- No default global shortcut for now to avoid Windows clipboard/accessibility shortcut collisions.
- Uses same trailing-space setting as normal insertion.
- Temporary last transcript clears after 1 hour or app exit.
- No transcript history is stored by default.

Delete That:

- If voice action commands are enabled, "delete that" should remove only the last text inserted by Trnscrbr.
- Do not use `Ctrl+Z`.
- Track exact last inserted text in temporary memory.

Elevated Windows:

- MVP supports normal user-level apps.
- If insertion fails due to elevated/admin target, show an informational message suggesting elevated-window support in settings.
- Elevated-window support should be designed for later using a separate elevated helper, not by running the full app as admin.

## Transcription Processing

Transcription types:

1. Clean only: remove pauses, filler words, and stutters while preserving wording.
2. Rewrite: produce a cleaner, more polished transcript.

Rewrite style options:

- Plain English
- Professional
- Friendly
- Concise
- Native-level English

Contextual correction:

- Enabled by default in both transcription types.
- Correct obvious recognition errors using nearby context.
- Can be disabled in settings.
- Example: "Paul's hotkey" should become "pause hotkey" when surrounding context is hotkeys/settings.

Temporary context:

- Keep current transcript while processing.
- Keep previous cleaned transcript in memory only.
- Use previous cleaned transcript as optional correction context.
- Clear temporary context on app quit.
- Do not persist transcript history by default.

Text around cursor:

- Optional setting, off by default.
- User must explicitly enable it.
- Enabling shows a warning about privacy and reliability.
- When enabled, app may try to read surrounding text near the active cursor as correction context.

Custom vocabulary:

- Simple local list of words and phrases.
- Used for cleanup/context correction.
- Not necessarily injected into the transcription engine.
- Stored locally for MVP.
- Future sync/export to OneDrive/Google Drive can be considered later.

## Spoken Commands

Punctuation/layout commands:

- "new line"
- "full stop"
- "question mark"
- "comma"

Command interpretation:

- Use smart command detection by default.
- The AI should infer whether words are intended as commands or literal text.
- Future settings may include always/never convert spoken punctuation commands.

Voice action commands:

- Disabled by default.
- User can explicitly enable in settings.

Candidate actions:

- "cancel that": discard current recording/transcript if still active.
- "delete that": remove last text inserted by Trnscrbr if possible.
- "stop recording": stop active recording and transcribe.
- "scratch that": cancel/delete depending on state.

## Providers

First-run choices:

- Cloud managed by app.
- Bring your own API key.
- Local mode.

MVP provider availability:

- Bring your own API key is available.
- Local mode is planned; current implementation can detect candidate local model files/setups but does not run them.
- Cloud managed by app is designed in the UI but should be disabled or marked as planned/future until backend/billing exists.

Initial cloud provider:

- OpenAI API first.
- Later candidates: Deepgram and AssemblyAI.
- MVP uses one selected provider for the whole pipeline.
- MVP supports one active provider/key at a time.

API key storage:

- Store locally on the device.
- Prefer Windows Credential Manager.
- Settings allow add, renew/update, and delete.
- Never export API keys.
- Logs show API key presence as yes/no only.

Provider setup:

- Test connection before saving.
- If test fails, allow saving with warning.
- Include provider setup help links.
- For OpenAI:
  - https://platform.openai.com/api-keys
  - https://platform.openai.com/docs/quickstart/overview
  - https://help.openai.com/en/articles/4936850-where-do-i-find-my-openai-api-key%23.woff2

No-provider behavior:

- If no provider is configured and user starts recording, show message near button:
  - "Provider required. Right-click for Settings."
- Do not open settings automatically.

## Local Mode

Principles:

- Local mode means no audio or transcript text is sent to cloud services.
- Not enabled by default.
- Presented as a free privacy-friendly option.
- User must explicitly download models.

Model presentation:

- Bundle as Small / Medium / Large presets.
- Hide separate speech and cleanup/rewrite model choices for MVP.

Presets:

- Small: fastest, lowest disk/RAM.
- Medium: balanced.
- Large: best quality, slower, higher disk/RAM.

Model handling:

- Detect installed local AI/model setups and suggest them.
- Offer detected models but do not use them by default.
- Provide managed downloads from official model repositories only.
- Show estimated download size, disk usage, and RAM recommendation.
- Downloads must be cancellable and resumable.
- Verify checksums/signatures before use.
- Allow removing downloaded local models from settings.
- Warn if local mode may be slow on the user's hardware.
- Detect GPU availability and use automatically where possible.
- Include setting to force CPU-only mode.

## Microphone Handling

- Default to Windows default input device.
- User can choose a specific mic via context menu fly-out and settings.
- Multiple microphones should be supported.
- Recording does not auto-stop on silence.
- Pauses/silence are handled by cleanup.
- No maximum recording duration for MVP.

## Language

- Design for multiple languages from the start.
- Support automatic language detection.
- Support manual language selection.
- Support English spelling preference: Auto, British English, American English, Canadian English, and Australian English.
- Auto English spelling should infer from the user's Windows region.
- Automatic punctuation enabled.

## Onboarding

First launch should include lightweight onboarding, not just provider choice.

Include:

- Provider choice: Cloud managed by app, Bring your own API key, Local mode.
- User can skip setup and configure later.
- Explain `Ctrl + Win + Space`.
- Explain insertion into the focused text field.
- State that the app does not press Enter.
- Explain tray icon controls/settings.
- Explain local mode is free/private but requires model downloads.
- Explain cloud/API mode may use third-party services.
- Explain temporary clipboard use and restoration.

## Startup And Installation

- Installer required for MVP.
- Launch on Windows startup enabled by default.
- Startup can be disabled in settings.
- Manual updates for MVP.
- Settings show update availability.
- Do not include portable-build link until an official portable build exists.

## Data Retention

Default:

- No transcript history.
- No raw audio retention.
- Audio exists only during processing and is deleted immediately after success/failure/cancel.
- Temporary last transcript only, cleared after 1 hour or app exit.
- Previous cleaned transcript may be kept in memory as temporary context.

Usage stats:

- Store usage metadata across sessions.
- Include total usage and monthly breakdown.
- Show token/API usage where available.
- Show estimated cloud costs in provider billing currency, for example USD for OpenAI.
- Clearly label cost estimates as estimated.
- Include link to provider usage dashboard, for OpenAI:
  - https://platform.openai.com/usage
  - https://help.openai.com/en/articles/10478918-api-usage-dashboard
- Allow monthly usage/cost warning threshold.
- Threshold only warns; it does not block transcription.
- Show warning near floating button and in tray panel.
- Include speaking/dictation speed, such as words per minute.

## Diagnostics And Logs

Diagnostics:

- Anonymous diagnostics/crash reports are opt-in only.
- Off by default.

Error logs:

- Include app/system errors.
- Include provider/API request IDs and status codes where available.
- Include non-content audio metadata such as duration, sample rate, selected mic, and temporary file size.
- Include OS/app version, provider name, model name, hotkey config, and microphone device name.
- Never include dictated/transcribed text.
- Never include raw audio.
- Never include API keys.
- API key presence is yes/no only.

Diagnostics UX:

- Include Copy Diagnostics button.
- Redact sensitive fields.
- Microphone device names do not need to be redacted.

## Import And Export

- Include import/export for settings and custom vocabulary in MVP.
- Exports never include API keys.

## Accessibility

- Tray panel/settings must support keyboard-only access.
- Floating button does not need to be keyboard-focusable in MVP.
- Feedback is visual only for MVP.
- No recording start/stop sounds.
- No haptic/keyboard-style feedback.

## Explicit Non-Goals For MVP

- Transcript history.
- Raw audio history.
- Cloud managed by app backend/billing.
- Multiple saved provider keys.
- Named profiles.
- Automatic AI-created profiles.
- Dedicated privacy screen.
- Command-line interface.
- Elevated helper implementation.
- Mac app.
- Portable build.

## Open Engineering Decisions

- Local model repository and exact Small/Medium/Large preset mappings.
- Clipboard restoration strategy for complex clipboard formats.
- Reliable detection of paste success/failure.
- Method for reading text around cursor when enabled.
- Best approach for "delete that" across common Windows controls.
- Packaging/update framework.
