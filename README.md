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

## Status

Planning/specification stage.

