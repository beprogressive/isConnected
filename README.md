# IsConnected

Small Windows tray utility that checks connectivity by sending ICMP ping requests to Google's public DNS server (`8.8.8.8`). If that ping fails, it sends one fallback ping to Cloudflare DNS (`1.1.1.1`) for that check cycle.

The app is intentionally lightweight: it reports whether either target responds to ICMP ping. It does not validate DNS resolution, HTTP access, captive portals, VPN routing, or service-specific availability. Networks that block ICMP can therefore be reported as offline even when browser traffic works.

## Behavior

- Green tray icon: ping succeeded.
- Red crossed tray icon: ping failed.
- Left-click or right-click the tray icon to open the menu.
- `Autostart` toggles startup with Windows for the current user.
- `Highlight issue` shows a pulsing visual warning when connectivity is offline.
- `Show current speed` displays a lightweight top-right overlay with current download/upload traffic for the active Windows network interface. It reads Windows interface byte counters and does not generate network traffic.
- `Highlight area` selects whether the warning appears around every connected screen, on one edge, or from one corner.
- `Highlight color` stores the selected warning color in `%APPDATA%\IsConnected\settings.json`.
- `Ping interval` stores the selected frequency in `%APPDATA%\IsConnected\settings.json`.
- `Test issue` previews the same issue effects for 5 seconds without disconnecting from the internet.
- `About` shows the app version and a link to the GitHub repository.

Only one instance can run per user session. If another instance is already running, a new launch exits immediately.

## Settings

User preferences are stored in `%APPDATA%\IsConnected\settings.json`.

Current settings:

- `IntervalSeconds`: ping frequency. Supported values are `5`, `10`, `30`, `60`, and `300`.
- `HighlightIssue`: enables or disables the offline visual warning.
- `ShowCurrentSpeed`: enables or disables the current network speed overlay.
- `HighlightArea`: visual warning placement. Supported values are `FullScreen`, `Left`, `Right`, `Top`, `Bottom`, `TopLeft`, `TopRight`, `BottomLeft`, and `BottomRight`.
- `HighlightColorArgb`: warning color stored as an ARGB integer.

If the settings file is missing, unreadable, invalid, or contains unsupported values, the app falls back to defaults where needed.

## Autostart

`Autostart` writes the current executable path to the current user's Windows Run registry key:

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
```

The setting is per Windows user and does not require administrator permissions under normal user profiles.

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
