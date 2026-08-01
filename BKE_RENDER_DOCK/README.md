# BKE Render Dock (Windows)

SANE & BEAST. A lightweight Windows tool for rapid media output with smart path handling, one-click navigation, and a simple offline expiry guard.

---

## What’s in this build

- **Output root**: `D:\BKE_RENDER_DOCK` (auto-fallback to `C:\BKE_RENDER_DOCK`)
- **On-demand Admin (UAC)**: If Windows blocks folder creation, the app relaunches **as Administrator**.
- **Double-click to open output**: Double-click the app window to open the **most recent** output folder.
- **Expiry guard (`bro.dat`)**: `%LOCALAPPDATA%\bro.dat` stores an **expiry (UTC)** and **last-seen** time.
  - CLI controls: `--set-expiry`, `--show-expiry`, `--extend-days`

> Note: `bro.dat` is **lightly obfuscated** (Base64) and **machine+user bound**. It deters casual tampering. We can upgrade to DPAPI / AES-GCM later without breaking usage.

---

## Requirements

- Windows 10/11
- .NET **6.0+** (project targets `net6.0-windows`)

---

## Build & Run

### Visual Studio
1. Open the solution.
2. Set **Startup project** to this app.
3. Build & Run (Debug or Release).

### From command line (PowerShell)
```powershell
# Run normally
& ".\BKE RENDER DOCK.exe"



POWERSHELL
# Set a specific expiry (UTC)
& ".\BKE RENDER DOCK.exe" --set-expiry=2025-12-31T23:59:59Z

# Show current expiry and remaining time
& ".\BKE RENDER DOCK.exe" --show-expiry

# Extend the current expiry by N days (negative to shorten)
& ".\BKE RENDER DOCK.exe" --extend-days=7
& ".\BKE RENDER DOCK.exe" --extend-days=-7

CMD
"BKE RENDER DOCK.exe" --set-expiry=2025-12-31T23:59:59Z
"BKE RENDER DOCK.exe" --show-expiry
"BKE RENDER DOCK.exe" --extend-days=7
"BKE RENDER DOCK.exe" --extend-days=-7



Roadmap (optional)

 Switch bro.dat storage to DPAPI (per-user encryption)

 Add AES-GCM + PBKDF2 hard-mode with master password

 GUI Credentials dialog (R2 / Hugging Face / YouTube) using a secrets store

 “Open Output” button/menu for user discoverability

 Optional single-instance mutex
