# IsConnected

Small Windows tray utility that checks internet connectivity by pinging Google's public DNS server (`8.8.8.8`). If that ping fails, it makes one fallback ping to Cloudflare DNS (`1.1.1.1`) for that check cycle.

## Behavior

- Green tray icon: ping succeeded.
- Red crossed tray icon: ping failed.
- Left-click or right-click the tray icon to open the menu.
- `Autostart` toggles startup with Windows for the current user.
- `Ping interval` stores the selected frequency in `%APPDATA%\IsConnected\settings.json`.

## Build

```powershell
dotnet build -c Release
```

## Publish lightweight exe

This creates a small framework-dependent executable. It requires .NET 8 Desktop Runtime on the target machine.

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\publish
```

Run:

```powershell
.\publish\IsConnected.exe
```

## Publish self-contained exe

Use this only when the target machine may not have .NET 8 Desktop Runtime installed. The output is much larger.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\publish-self-contained
```
