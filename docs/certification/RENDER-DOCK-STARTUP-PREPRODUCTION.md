# Render Dock — exact-source Agent-bound direct startup preproduction proof

**Intent:** consume the shared `BKE.Desktop.Licensing 2.0.2` startup-gate client from immutable SDK main `e34431f5400aab00e87549b8aae8dedc3104f4bb`, and make Render Dock's **preproduction-certification executable** require the process-bound Agent startup verdict before it can run any enterprise session, legacy grace, notification, licensing, FFmpeg/bootstrap or UI. A missing/untrusted/expired/replaying Agent always denies.

**Base Render Dock main:** `d761d939f0563e7240ce25864792d5f359bc0aff`. **Agent** `1bf1a6f7048918f6a19265f94f687d750bb01931` process-bound pipe service default OFF and nonproduction only. **DS** `9464d6814eb9606b0b2578daa29038c000ed7abc` signed v1/v2 policy cloud protocol source. Physical TPM/installed transport remains unproven.

**Business safety:** current 1.0.3 retail build behavior, including legacy `{grace:true}` HTTP and Air Stack enterprise child-session, is intentionally unchanged; DO NOT silently remove those without an explicit authority/business decision. The strict preproduction gate is compiled only when `BkeStartupGateCertification=true`; CI will compile/run this isolated nonshippable variant, while the normal packaging remains unmodified in behavior. **This is not a production customer anti-bypass claim.** In strict mode the gate comes FIRST, and even the enterprise/grace paths cannot bypass it (some legitimate enterprise pilot starts will be denied until Agent policy is installed).

**Owned contract:** Render Dock product only consumes typed `BkeStandaloneStartupGateClient`, never raw pipe, product-supplied PID/user/key/mode, DS HTTP, or private keys. SDK verifies Agent runtime PID/path/session0/SYSTEM before trusting a bounded, correlated response; Agent owns actual process attestation, installed DS policy, credential verification and durable audit.

**Required exact-head certification graph:** Render Dock explicit `/certify renderdock windows-x64` on this PR; pinned SDK source SHA verification and **SDK Windows fake-pipe denial tests**, strict product compilation and an actual headless native Windows direct EXE denial when Agent pipe is absent; existing product contract tests, self-contained x64 publish and disposable packaging/hashes. **Not required:** DS/Agent/Launcher heavy suites (unchanged owner proof); TPM/manual physical, production deployment/install/release/catalog. Widen only if a genuine trust/installer boundary changes. No production publication.

**Acceptance classification:** source-compiled typed Agent client + real fake pipe refusal + source-verified preproduction RenderDock denial. Not true installed Agent allow path, real signed policy, physical TPM, customer direct-EXE acceptance or release publication. Agent issue #130 remains open.

**Production locked:** no release/tag/catalog, VPS/DB, signing cutover, payments/Resend, customer installer, force push.
