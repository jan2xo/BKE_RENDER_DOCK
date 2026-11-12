# Render Dock exact-source CI router extension — startup-gate intent dependency

**Why:** Main's intentional issue-comment Windows router at `d761d939f0563e7240ce25864792d5f359bc0aff` hardcodes legacy `BKE.Desktop.Licensing=2.0.1` and SDK SHA `8563d86976f65cd0cfcdd7e21c6c428c561e5dfe`, while product [PR #30](https://github.com/jan2xo/BKE_RENDER_DOCK/pull/30) legitimately pins merged `BKE.Desktop.Licensing 2.0.2` SDK `e34431f5400aab00e87549b8aae8dedc3104f4bb`. The first exact-head run #43300467717 failed in old main workflow static pin check before product contracts.

**Intent:** trusted-main certification owner strictly recognizes ONLY allowed version+immutable source pairs 2.0.1→8563... or 2.0.2→e344...; requires native Windows SDK fake-pipe denial tests for 2.0.2; builds/executes a nonshippable Render Dock Agent-first direct EXE under 2.0.2 and requires exit 84 when no Agent. Standard source/publish/package proofs unchanged. No versioned env var names or reduced security checks.

**Bootstrap required:** exact-head cheap PR Guard validates intentional CI routing, no product behavior/SDK reference changes in this bootstrap, prior main Windows-x64 package was already certified. Branch-only workflow changes cannot be run through the trusted-main issue-comment router; after merge, explicitly recertify product PR #30 against the new trusted router. **Not required:** DS/Agent/SDK heavy cert again, real TPM, real installed Agent, public release.
**Production LOCKED**: no published installer/software/catalog, production keys/deployment/DB, PayMongo/Resend or force push.
