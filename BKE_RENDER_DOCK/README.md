# Render Dock (Windows)

## Update notifications

Standalone Render Dock checks only the local Licensing Agent after its main form is shown and may display native Update/Later UI for `bke-render-dock`. When the existing Agent-authenticated Air Stack enterprise session is redeemed, Render Dock suppresses its prominent update prompt so Air Stack owns the bundled-component surface. Render Dock never contacts Digital Solutions, downloads an updater artifact, or invokes Updater Core directly.

SANE & BEAST. A lightweight Windows tool for rapid media output with smart path handling, one-click navigation, and shared licensing authorization.

---

## What’s in this build

- **Output root**: `D:\BKE_RENDER_DOCK` (auto-fallback to `C:\BKE_RENDER_DOCK`)
- **On-demand Admin (UAC)**: If Windows blocks folder creation, the app relaunches **as Administrator**.
- **Double-click to open output**: Double-click the app window to open the **most recent** output folder.
- **Authorization**: startup checks remote operational grace, then falls through to the shared localhost Licensing Agent unless grace is explicitly active. The product-side authorization transport is provided by the released `BKE.Desktop.Client` 1.0.0 SDK package.
- **Enterprise launch**: Air Stack child-session redemption remains a separate Agent-authenticated named-pipe contract and is not replaced by the desktop SDK.
- **Fail closed**: protected Render Dock and FFmpeg bootstrap run only after remote grace, redeemed enterprise session, or explicit Agent authorization.

---

## Requirements

- Windows 10/11
- .NET **8.0+** (project targets `net8.0-windows`)

## Product identity convention

- `displayName` is the human-readable customer-facing product brand: `Render Dock`.
- `productId` is the machine/licensing identity: `bke-render-dock`.
- New products use `bke-<normalized-product-name>`; for example, `Air Stack` uses `bke-air-stack`.
- Treat `productId` as immutable after a real commercial lifecycle begins.
- Executable, repository, project, and other internal names do not need to match `displayName` exactly.

---

## Build & Run

### Visual Studio
1. Open the solution.
2. Set **Startup project** to this app.
3. Build & Run (Debug or Release).

### From command line (PowerShell)
```powershell
# Run normally
& ".\RENDER DOCK.exe"
```

Roadmap (optional)

- GUI Credentials dialog (R2 / Hugging Face / YouTube) using a secrets store
- “Open Output” button/menu for user discoverability
- Optional single-instance mutex
