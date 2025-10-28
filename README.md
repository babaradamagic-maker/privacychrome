# PrivacyChrome

PrivacyChrome is a lightweight Windows utility that monitors Chrome's foreground status and automatically brings it to the front using dual mouse clicks or keyboard triggers. Designed for privacy-conscious workflows with minimal UI and fast response.

## Features

- Tray icon with arm/disarm toggle
- Low-level keyboard and mouse hooks
- Dual-click or hotkey trigger (Ctrl+Shift+Q)
- Chrome/Brave/Chromium window detection
- Automatic window restoration and foreground activation
- Minimizes other windows for focus

## Requirements

- Windows 10 or later
- .NET 8.0 SDK (recommended)
- Admin privileges (for input hooks)

## Build Instructions

```bash
dotnet clean
dotnet build -c Release