# BKE Render Dock (Windows)

SANE & BEAST. A lightweight Windows tool for rapid media output with smart path handling, one-click navigation, and shared BKE licensing authorization.

---

## What’s in this build

- **Output root**: `D:\BKE_RENDER_DOCK` (auto-fallback to `C:\BKE_RENDER_DOCK`)
- **On-demand Admin (UAC)**: If Windows blocks folder creation, the app relaunches **as Administrator**.
- **Double-click to open output**: Double-click the app window to open the **most recent** output folder.
- **Authorization**: startup checks remote operational grace, then falls through to the shared localhost BKE Licensing Agent unless grace is explicitly active.
- **Fail closed**: protected RenderDock and FFmpeg bootstrap run only after remote grace or explicit Agent authorization.

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






Roadmap (optional)

 GUI Credentials dialog (R2 / Hugging Face / YouTube) using a secrets store

 “Open Output” button/menu for user discoverability

 Optional single-instance mutex
