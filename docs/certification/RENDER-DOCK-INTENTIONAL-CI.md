# Render Dock intentional Windows certification bootstrap

**Intent:** turn the existing automatic PR/push full Windows installer/package/Defender-ish certification into an explicitly requested exact-SHA workflow, adding only a cheap PR Guard. PR is durable ledger; certification plan chooses the proof. This is prerequisite to Render Dock's upcoming shared-SDK process-bound standalone startup integration.

**Base main:** `3ff7fbf1211635449c94f3637834f1b4abfb08d9`.
**Required bootstrap proof:** old installed Windows x64 workflow runs on exact PR head *once* under the legacy automatic trigger; check product, SDK pin and installer build. The branch's new manual workflow is registered only after merge. Then product integration will explicitly invoke it on its own exact head.
**Not required:** Agent, DS, Launcher, SDK unrelated suite; use pinned current compiled SDK `8563d86976f65cd0cfcdd7e21c6c428c561e5dfe` for bootstrap only.
**Trust ownership:** Render Dock only owns product boot/UX and build. Agent owns trusted process gate/policy/credentials, SDK owns pipe client. Keep legacy grace and enterprise policy unchanged in this bootstrap.
**Locks:** Production release/install/catalog/signing/DB/deploy all locked. No public artifact publication, workflow only ephemeral uploaded certification evidence.
