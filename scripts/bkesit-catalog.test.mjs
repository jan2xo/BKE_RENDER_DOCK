import test from "node:test";
import assert from "node:assert/strict";
import {
  normalizeRelease, versionFromTag, SOURCE_REPOSITORY, CATALOG_REPOSITORY,
  IMAGE_TARGET_NAME,
} from "./bkesit-catalog.mjs";

const SHA = "a".repeat(40);
const version = "1.0.3";
const names = [
  "Render-Dock-" + version + "-Windows-x64.exe",
  "Render-Dock-" + version + "-Windows-x64.update.zip",
  "Render-Dock-" + version + "-Windows-x64.update.json",
  "SHA256SUMS", "SOURCE.txt",
];
function fixture() {
  return {
    tag: "v" + version,
    reference: { type: "commit", sha: SHA },
    productManifest: { productId: "bke-render-dock", version, platform: "windows", architecture: "x64", updateChannel: "stable" },
    image: { type: "file", sha: "b".repeat(40), size: 1024 },
    release: { tag_name: "v" + version, draft: false, prerelease: false, assets: names.map((name) => ({
      name, size: 1200, digest: "sha256:" + "c".repeat(64),
      browser_download_url: "https://github.com/" + SOURCE_REPOSITORY + "/releases/download/v1.0.3/" + encodeURIComponent(name),
    })) },
  };
}
test("accept exact stable provenance and construct BKESIT URLs without uploading", () => {
  const output = normalizeRelease(fixture());
  assert.equal(output.schema, "bkesit.catalog-entry.v1");
  assert.equal(output.productId, "bke-render-dock");
  assert.equal(output.source.sha, SHA);
  assert.equal(output.distribution.repository, CATALOG_REPOSITORY);
  assert.equal(output.distribution.publicationState, "PENDING");
  assert.equal(output.distribution.assets.length, 5);
  assert.match(output.distribution.assets[0].targetUrl, /BKESITSolutions\/Software-Catalogs\/releases\/download\/bke-render-dock-v1\.0\.3/);
  assert.match(output.image.url, new RegExp(IMAGE_TARGET_NAME));
});
test("reject unstable or malicious release tags", () => {
  for (const tag of ["v1.0.3-pre", "refs/heads/main", "../v1.0.3", "v1.0.3;exit", "v1.0.3.4"]) {
    assert.throws(() => versionFromTag(tag));
  }
});
test("reject draft/prerelease/mismatched product/manifests", () => {
  for (const field of ["draft", "prerelease"]) {
    const x = fixture(); x.release[field] = true;
    assert.throws(() => normalizeRelease(x));
  }
  const y = fixture(); y.productManifest.productId = "other";
  assert.throws(() => normalizeRelease(y));
});
test("reject substituted asset URLs, missing/multiple assets and unknown extras", () => {
  const a = fixture(); a.release.assets[0].browser_download_url = "https://example.com/binary.exe";
  assert.throws(() => normalizeRelease(a));
  const b = fixture(); b.release.assets.pop();
  assert.throws(() => normalizeRelease(b));
  const c = fixture(); c.release.assets.push({...c.release.assets[0]});
  assert.throws(() => normalizeRelease(c));
});
test("reject absent GitHub digest and non-commit tag", () => {
  const a = fixture(); a.release.assets[0].digest = null;
  assert.throws(() => normalizeRelease(a));
  const b = fixture(); b.reference.type = "tree";
  assert.throws(() => normalizeRelease(b));
});
