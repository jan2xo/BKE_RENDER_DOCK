# Render Dock preproduction Agent-grant acceptance and ACCOUNT compatibility

> **Current-source policy update (Render Dock PR #33, November 13, 2026):** The unsigned `GracePeriodClient` HTTP `{grace:true}` startup override is retired from both regular and strict build source. Standalone retail startup now requires explicit authorization from the shared licensing SDK; authenticated Air Stack enterprise child-session remains separate. This file's original PR #32 notes below document the **earlier certified state** and must not be treated as a current authorization exception. The real installed-Agent positive grant, complete release provenance and physical TPM acceptance remain **UNPROVEN** until approved independent evidence exists. No public binary/deployment is authorized.



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


## Human/approved disposable Windows acceptance — not executed by normal CI

**Do not run on a customer/productive machine.** This procedure requires an
operator-controlled disposable Windows x64 VM or physical test device with
administrative rights, Python 3 (`py -3`), .NET 10 and Git.

1. Confirm **actual** BKE Licensing Agent service and its Host runtime were
   installed as SYSTEM under protected `%ProgramFiles%\BKE Digital Solutions\Licensing Agent`
   and started with a *nonproduction* DS origin and
   `BKE_STANDALONE_STARTUP_GATE_ENABLED=1`. Never contact production DS.
2. Before product startup, Digital Solutions must have issued a **real signed,
   nonproduction** startup policy after dual-ADMIN review, bound to this
   exact `RENDER DOCK.exe` hash, exact `bke-render-dock` product/version,
   installation GUID, permitted Windows SID, activation mode and valid time
   window. Privileged Agent installer must have installed both the signed
   policy and the approved **public-only** issuer trust. Do not create keys or
   sign policies in Render Dock or on the customer machine.
3. The Agent must already have the corresponding separately proved commercial
   `LICENSE_KEY` credential or account entitlement + fresh account open proof
   for the canonical signed activation mode. A `LICENSE_KEY` test does **not**
   require Google login. The Agent's SQL-derived `ACCOUNT` mode must not
   trigger Render Dock's legacy commercial-key-only second check.
4. Build the *dedicated* headless acceptance executable from the exact reviewed
   Render Dock source and pinned merged SDK. Run on the disposable build
   machine, then have the **authorized nonproduction installer/operator**
   stage that exact executable under
   `%ProgramFiles%\BKE Digital Solutions\Render Dock\RENDER DOCK.exe`.
   **Do not overwrite a customer installation, touch production signing,
   or publish the build.**

```powershell
.\scripts\bootstrap-bke-sdk.ps1 -CertifyStartupGateClient

dotnet publish 'BKE_RENDER_DOCK/RENDER DOCK.csproj' --configuration Release -r win-x64 --self-contained true -p:BkeStartupGateCertification=true -p:BkeStartupGateAcceptance=true -o .\artifacts\preproduction-acceptance

# Record the exact SHA; the privileged operator stages only this reviewed binary.
(Get-FileHash -LiteralPath '.\artifacts\preproduction-acceptance\RENDER DOCK.exe' -Algorithm SHA256).Hash.ToLowerInvariant()
```

5. Before launching the **already staged** binary, the human operator creates
   `bke-startup-acceptance.json` next to it. This marker is **not licensing
   authority**; it distinguishes a specifically reviewed **NONSHIPPABLE**
   headless test from ordinary retail software. All fields must match the
   source checkout and *exact installed* image:

```json
{
  "schema": "bke.renderdock-startup-acceptance-marker.v1",
  "purpose": "NONSHIPPABLE",
  "source_sha": "<exact-reviewed-Render-Dock-commit>",
  "image_sha256": "<exact-installed-EXE-sha256-lowercase>"
}
```

6. From the exact reviewed Render Dock checkout, on that approved machine,
   launch the **read-only** acceptance script. It refuses to provision keys,
   change Agent state, install services or rewrite policy. It verifies the
   executable hash, protected installed service, one matching policy,
   trusted-runtime presence, and an actual SDK startup grant for the launched
   process. It then independently reads the Agent **durable SQLite grant**
   using `py -3` and requires the exact PID + UTC process-start ticks + Windows
   SID + product + version + executable hash + **ACCOUNT/LICENSE_KEY** record.

```powershell
.\scripts\certification\invoke-installed-agent-startup-acceptance.ps1 `
  -ExpectedRenderDockSourceSha '<reviewed-40-char-sha>' `
  -ExpectedAgentSourceSha '<reviewed-Agent-40-char-sha>' `
  -ExpectedExecutableSha256 '<installed-exe-sha256>' `
  -ExpectedCredential 'LICENSE_KEY' `
  -EvidenceDirectory "$env:TEMP\bke-disposable-startup-evidence" `
  -ApprovedDisposableMachine
```

Result **PASS** requires exit code **0** from that specially compiled binary
**and** exactly one independently verified Agent-owned process grant. The
approved operator retains the sanitized evidence file and the independent
source/installer/policy provenance review. A missing Agent or invalid/expired
policy is **DENY** (typically product exit code 84); it is not a successful
positive test. Any headless exit with no matching audit row is **FAIL**.

## Acceptance matrix and remaining proof

| Gate | Automated | Human/installed |
|---|---|---|
| Unsigned retail grace retired by follow-on PR #33; Air Stack remains separate | Follow-on source + Windows proof required | No production cutover |
| Strict Agent gate denies missing Agent **before all legacy paths** | Real Windows disposable CI PASS on previous wave | No local action needed |
| `ACCOUNT` grant does **not** invoke commercial-only legacy check | New source/compiled condition test | Real signed ACCOUNT grant required |
| Headless acceptance cannot compile into normal shipping build | New exact-head Windows test | Not for customer distribution |
| Read-only grant verifier rejects wrong PID, start time, SID, image, product, version, credential | Disposable Python/SQLite fixtures | Check actual protected Agent ledger |
| Actual `ACCOUNT`/key policy, signed issuer, installed Agent SYSTEM pipe, audit-backed positive process | **NOT COVERED** by hosted Windows missing-Agent run | Run approved isolated Windows procedure above |
| Physical TPM/EK/AIK, signed GitHub release-to-installer provenance, GUI/customer E2E | **NOT COVERED** | Independent certification/approvals required |

`ExpectedAgentSourceSha` in the human-run report is an **operator-declared
source pin**, not cryptographically verified GitHub-release-to-installed-binary
provenance. Never promote it to a publisher/signature claim.

**Strict production locks remain:** no production deployment/DB, public
release/installer, signing cutover, live commerce/email, catalog activation,
customer installation or force push. [Agent #130](https://github.com/jan2xo/bke-licensing-agent/issues/130)
stays OPEN until real installed success, policy/legacy decision and release
provenance are independently proved.
