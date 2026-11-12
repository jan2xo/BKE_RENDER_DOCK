# Render Dock (Windows)

Render Dock is a Windows media-output product built on reusable BKE capabilities.

## Capability composition

```text
Render Dock
├── BKE.Desktop.Licensing 2.0.2
│     ↓
│   BKE Licensing Agent
│
├── BKE.Notifications 0.5.0
│     ↓
│   BKE Licensing Agent
│
└── BKE.Updater 0.4.0
      ↓
    BKE Licensing Agent
```

Render Dock owns product behavior and UI. It does not define Licensing Agent transport, updater protocol DTOs, signed-policy verification, trusted keys, download grants, installer authority, or privileged updater execution.

## Licensing

Standalone startup uses `BKE.Desktop.Licensing` 2.0.2:

```text
WHAT I NEED
- product identity: bke-render-dock
- current product version
- stable installation identity

WHAT I GIVE TO THE SDK
- productId
- version
- installationId

WHAT I GET
- typed authorization result
```

The SDK owns the standard authorization / Agent-owned activation choreography. Render Dock fails closed when authorization is not granted.

The existing operational grace check remains a separate legacy product integration in this release and is intentionally **not** retired by this source change. Air Stack enterprise redemption remains unchanged in the normal shipping build. Neither grace HTTP nor enterprise redemption constitutes the new process-bound Agent startup proof.

### Strict Agent-bound startup — PREPRODUCTION ONLY

A separate, **nonshippable** Render Dock certification build sets `BkeStartupGateCertification=true` at compile time. That build asks `BkeStandaloneStartupGateClient` **before** attempting enterprise redemption, the legacy grace endpoint, notifications, commercial key activation, FFmpeg or application UI. The SDK uses the Agent's Windows named pipe `bke.standalone-startup.v1`; it rejects a spoofed local server by checking the OS-observed process, its installed protected runtime path and SYSTEM identity. The product never transmits a PID, product ID, user SID, key, activation mode or executable path as authority.

When the Agent or signed installed policy is absent, invalid, unreachable, or denies, the preproduction executable **exits with code 84** without entering the UI and without falling back to grace or enterprise. Ordinary 1.0.3 packaging has this conditional feature disabled and intentionally retains its existing behavior. **This is source-only/negative acceptance**, not a validated installed-Agent success or production anti-bypass claim. A genuine installed DS signed activation policy, real Agent runtime composition, enterprise-mode policy reconciliation, and signed release/install provenance remain separate certification gates. Do not publish the strict certification variant to customers.

This product consumes the SDK built from exact merged source `e34431f5400aab00e87549b8aae8dedc3104f4bb` (unreleased 2.0.2); the Agent Host process trust contract is pinned to source `1bf1a6f7048918f6a19265f94f687d750bb01931` for this verification. Production is locked.

Air Stack enterprise child-session redemption also remains a separate Agent-authenticated named-pipe capability. When that enterprise session is redeemed, Render Dock does not run standalone licensing or standalone update-notification behavior.

## Update discovery

Standalone Render Dock uses `BKE.Updater` 0.4.0 after the main form is shown:

```text
WHAT I NEED
- productId: bke-render-dock
- current version

WHAT I GET
- UpToDate
- UpdateAvailable
- Deferred
- Failed + typed UpdateError
```

Render Dock does **not** call Licensing Agent updater HTTP routes directly. The fixed product-to-Agent transport is private to `BKE.Updater`.

For `UpdateAvailable`, Render Dock currently shows informational product UI only. Download/install/privileged execution is intentionally not called directly from the product because that capability has not yet been promoted into the canonical SDK contract.

## What’s in this build

- **Version:** 1.0.3
- **Runtime:** .NET 10, self-contained Windows x64 release
- **Output root:** `D:\BKE_RENDER_DOCK` with fallback to `C:\BKE_RENDER_DOCK`
- **On-demand Admin (UAC):** if Windows blocks folder creation, the app may relaunch as Administrator for that product operation
- **Double-click to open output:** double-click the app window to open the most recent output folder
- **Authorization:** operational grace first; otherwise the canonical BKE licensing SDK capability
- **Enterprise launch:** Air Stack child-session redemption remains Agent-authenticated named-pipe IPC
- **Notifications:** Agent-backed startup inbox + in-product notification tray; product has no publication authority
- **Update discovery:** canonical BKE updater SDK capability; no product-local updater transport
- **Fail closed licensing:** protected startup proceeds only after grace, redeemed enterprise session, or explicit Agent authorization

## Requirements

Development/build requirements:

- Windows 10/11
- .NET 10 SDK
- Git

The release application is published self-contained for Windows x64.

## Pinned SDK bootstrap

BKE packages are prepared from the exact canonical `bke-sdk` merge pinned by:

```powershell
.\scripts\bootstrap-bke-sdk.ps1
```

The script checks out the exact SDK commit, verifies the resolved SHA, and packs only the capabilities Render Dock consumes:

```text
BKE.Desktop.Licensing 2.0.2
BKE.Notifications      0.5.0
BKE.Updater            0.4.0
```

The generated NuGet files live under `packages/` and are ignored by Git. Render Dock no longer keeps old SDK package binaries in its source history as active build inputs.

## Product identity

- display name: `Render Dock`
- product ID: `bke-render-dock`
- current version: `1.0.3`
- entry point: `RENDER DOCK.exe`
- platform: `windows`
- architecture: `x64`

Treat `productId` as immutable across the commercial lifecycle.

## Build & Run

Prepare the pinned SDK capabilities first:

```powershell
.\scripts\bootstrap-bke-sdk.ps1
```

Then build from Visual Studio or the command line.

The GitHub Actions Windows installer lane performs this bootstrap automatically before tests, restore, publish, and installer certification.
