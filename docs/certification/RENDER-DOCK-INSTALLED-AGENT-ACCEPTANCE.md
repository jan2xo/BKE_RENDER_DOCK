# Render Dock preproduction Agent-grant acceptance and ACCOUNT compatibility

**Current certification intent:** finish Render Dock's **Agent-authoritative startup consumption** without shipping a new retail policy. The source-only gate currently approves a signed-policy/machine/credential decision but then re-runs the legacy commercial-key-only flow. That can block legitimate `ACCOUNT` startup after a valid Agent grant. Resolve this **inside Render Dock**, without weakening the Agent or the cloud.

**Immutable bases:** Render Dock `2e9f573ee901a56fc7f5cc824ff0e1f613b99a94`; shared BKE SDK `e34431f5400aab00e87549b8aae8dedc3104f4bb` (source-built `BKE.Desktop.Licensing 2.0.2`), Licensing Agent `1bf1a6f7048918f6a19265f94f687d750bb01931`, Digital Solutions `9464d6814eb9606b0b2578daa29038c000ed7abc`.

**Ownership & security:**
- Agent is the only source of machine-bound `ACCOUNT`/`LICENSE_KEY`/`EITHER` startup authorization, with OS-observed process, signed installed DS policy, verified credential and durable grant audit.
- Render Dock's preproduction strict compilation must require this grant **before** Air Stack, grace, notifications, FFmpeg or UI, and must **not** reimpose the legacy key-only flow after an Agent grant. Legacy enterprise session can still provide product-specific updates/notifications; it is NOT a substitute for Agent gate.
- For normal retail `BkeStartupGateCertification=false`, legacy enterprise, grace and license-key behaviors remain **byte-for-byte equivalent**; no silent customer license policy change.
- A distinct preproduction-only `BkeStartupGateAcceptance=true` compilation (requires strict gate) will exit 0 **only after** typed SDK Agent grant. It is for isolated human/CI positive-gate observation without GUI hangs and never packaged or published. Failure remains exit 84 and does not fall back to grace. Compiled only into separately built binary, not runtime env flag.

**Product-side acceptance:** add a Windows installed-real-Agent acceptance script that refuses to provision keys, alter policies, stop services, elevate permissions, or touch live/production. It verifies approved physical/VM disposable machine and exact SDK/Agent SHA, requires a preexisting signed DS policy and Agent-authorized credential, runs the exact installed acceptance binary from protected Program Files, checks a new Agent-owned durable SQLite grant matching actual PID/process generation/image hash/SID, and reports denials distinctly. The script may document human-provided prerequisites; any unexecutable physical/manual item remains explicitly UNPROVEN.

**Minimum complete exact-head graph:** one explicitly dispatched Render Dock `/certify renderdock windows-x64`: source/package/sdk contract tests, real hosted Windows missing-Agent exit 84 + separate headless acceptance-gated denial, self-contained app/updater payload/installer/hash. No repeat Agent/DS/SDK owner suites because producer/protocol/SDK source not changed; recheck their immutable source SHAs. No physical TPM or production/real installed positive claim without actual signed Agent execution evidence.

**Not required:** full DS suite, Agent CI, Launcher, hardware TPM/quote, production signing, customer install. Widen only for actual shared trust change discovered.

**Business decision deferred:** legacy `{grace:true}` and Air Stack policy remain in normal release. Strict mode treats both as *not authority*. No production release, deployment, catalog activation, payments, email or forced push.
