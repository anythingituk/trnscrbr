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

The installer writes the optional current-user startup entry only when the startup task is selected. API keys remain stored separately in Windows Credential Manager and are not packaged.
