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
artifacts\installer\Trnscrbr-Setup-0.2.0-win-x64.exe
```

The installer shows the current-user startup task checked by default and writes the startup entry only if that task remains selected. API keys remain stored separately in Windows Credential Manager and are not packaged.

## Smoke test packaging

Run:

```powershell
.\scripts\test-package.ps1
```

This publishes the app, verifies that the published executable contains current local-mode and hotkey UX text, builds the installer, verifies that the expected installer executable exists and is non-empty, silently installs it to a temporary folder, checks the installed executable version, launches the installed app when no other Trnscrbr instance is running, and uninstalls it.

To skip the temporary install/uninstall check:

```powershell
.\scripts\test-package.ps1 -SkipInstallSmokeTest
```

To keep install/version verification but skip launch:

```powershell
.\scripts\test-package.ps1 -SkipLaunchSmokeTest
```
