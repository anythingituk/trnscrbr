# Changelog

## 0.2.9

- Added OpenAI transcription latency diagnostics with stage timings and slow-processing notices.
- Reduced cleanup latency by requesting minimal reasoning, low verbosity, and bounded cleanup output.
- Added automatic English language hinting after repeated English dictations while Language is set to Auto.
- Show newest diagnostics first and added helper text explaining Clean only versus Rewrite.

## 0.2.8

- Fixed the mini settings status strip tooltip so it shows the full hotkey hint when the hint is truncated.

## 0.2.7

- Combined Provider and Local Models into one AI Models tab.
- Simplified local model setup so selected models show Download only when needed and report when already downloaded.
- Moved local LLM/Ollama controls under an advanced-user section.
- Kept the Updates version display simple and added a latest GitHub release link.
- Fixed the mini settings status strip so long messages no longer break the compact layout.

## 0.2.6

- Simplified first setup into a Home tab with only dictation engine choice, setup action, and recording hotkey.
- Defaulted new installs to local Small AI setup while still offering Medium, Large, and OpenAI API choices.
- Removed the mini settings minimize button so the compact panel only closes back to the system tray.
- Reduced visible OpenAI provider controls by removing the separate test button and extra help links.

## 0.2.5

- Released the current mini settings panel with draggable header controls, minimize and close buttons, and an option to keep the panel visible when switching apps.
- Added clearer mini settings recording feedback and hotkey display.
- Reissued the installer from the current main branch because the v0.2.4 release tag was cut before these UI changes.

## 0.2.2

- Added optional OpenAI speaker filtering with an Ignore other speakers privacy setting.
- Speaker filtering uses diarized transcription and keeps the dominant speaker before cleanup/insertion.

## 0.2.0

- Added managed Free Quick Setup for local Whisper dictation.
- Added local setup repair for missing CLI/model files and inactive Local mode.
- Added local readiness status in the tray panel with Repair, Details, and Test actions.
- Added local test phrase flows in Advanced Settings and the tray panel.
- Added configurable hotkey presets for toggle recording and push-to-talk.
- Added clearer floating recording/transcribing feedback with fade-away idle behavior.
- Improved microphone no-input recovery and local setup status messages.
- Updated packaging smoke tests to verify published UX text, installer output, installed version, and installed-app launch.
