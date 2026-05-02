# Packaging

## Publish self-contained app

```powershell
.\scripts\publish-win-x64.ps1
```

Output:

```text
artifacts\publish\win-x64\
```

## Build installer

Install Inno Setup, then run:

```powershell
.\scripts\publish-win-x64.ps1 -BuildInstaller
```

Output:

```text
artifacts\installer\Trnscrbr-Setup-0.1.0-win-x64.exe
```

The installer shows the current-user startup task checked by default and writes the startup entry only if that task remains selected. API keys remain stored separately in Windows Credential Manager and are not packaged.

## Smoke test packaging

Run:

```powershell
.\scripts\test-package.ps1
```

This publishes the app, verifies that the published executable contains current local-mode and hotkey UX text, builds the installer, and verifies that the expected installer executable exists and is non-empty.
