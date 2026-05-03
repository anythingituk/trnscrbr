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

## Publish a real update

The app checks the latest GitHub Release at:

```text
https://github.com/anythingituk/trnscrbr/releases/latest
```

To make updates work for real users, publish releases with the Windows installer attached. The release workflow runs when a version tag is pushed:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

The workflow builds and smoke-tests the installer, creates a GitHub Release, and uploads:

```text
Trnscrbr-Setup-<version>-win-x64.exe
```

Use a new SemVer tag for each public installer, for example `v0.2.1`, `v0.3.0`, or `v1.0.0`. The app compares the release tag with its own version and opens the installer asset when a newer release is available.
