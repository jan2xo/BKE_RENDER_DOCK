#!/usr/bin/env node
// Read-only source-release verifier; output is a candidate BKESIT catalog entry.
// Publication is an independently gated step in the workflow.
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { dirname } from "node:path";
import { pathToFileURL } from "node:url";

export const SOURCE_REPOSITORY = "jan2xo/BKE_RENDER_DOCK";
export const CATALOG_REPOSITORY = "BKESITSolutions/Software-Catalogs";
export const PRODUCT_ID = "bke-render-dock";
export const IMAGE_SOURCE_PATH = "BKE_RENDER_DOCK/Assets/Render Dock.jpeg";
export const IMAGE_TARGET_NAME = "Render-Dock-product.jpeg";
export const REQUIRED_SUFFIXES = [
  "-Windows-x64.exe",
  "-Windows-x64.update.zip",
  "-Windows-x64.update.json",
];
export const EXTRA_FILES = ["SHA256SUMS", "SOURCE.txt"];

export function versionFromTag(tag) {
  if (!/^v[0-9]+\.[0-9]+\.[0-9]+$/.test(tag)) {
    throw new Error("Only stable vMAJOR.MINOR.PATCH release tags are allowed");
  }
  return tag.slice(1);
}
function assert(condition, message) {
  if (!condition) throw new Error(message);
}
function isSha256(value) {
  return typeof value === "string" && /^sha256:[0-9a-f]{64}$/.test(value);
}
export function normalizeRelease({ release, reference, productManifest, image, tag }) {
  const version = versionFromTag(tag);
  assert(reference && reference.type === "commit" && /^[0-9a-f]{40}$/.test(reference.sha), "Release tag must resolve to an exact source commit");
  assert(release && release.tag_name === tag && release.draft === false && release.prerelease === false, "Release must be published and stable");
  assert(productManifest?.productId === PRODUCT_ID && productManifest.version === version, "Source product identity/version does not match release tag");
  assert(productManifest.platform === "windows" && productManifest.architecture === "x64" && productManifest.updateChannel === "stable", "Source manifest must declare stable Windows x64");
  assert(image && image.type === "file" && /^[0-9a-f]{40}$/.test(image.sha) && Number.isSafeInteger(image.size) && image.size > 0, "Missing pinned product image");
  const sourceBase = "https://github.com/" + SOURCE_REPOSITORY + "/releases/download/" + tag + "/";
  const targetTag = PRODUCT_ID + "-v" + version;
  const targetBase = "https://github.com/" + CATALOG_REPOSITORY + "/releases/download/" + targetTag + "/";
  const expectedNames = REQUIRED_SUFFIXES.map((suffix) => "Render-Dock-" + version + suffix).concat(EXTRA_FILES);
  assert(Array.isArray(release.assets), "Missing release assets");
  const sourceAssets = expectedNames.map((name) => {
    const matching = release.assets.filter((asset) => asset.name === name);
    assert(matching.length === 1, "Expected exactly one release asset: " + name);
    const asset = matching[0];
    assert(Number.isSafeInteger(asset.size) && asset.size > 0 && isSha256(asset.digest), "Missing GitHub SHA-256/size for " + name);
    assert(asset.browser_download_url === sourceBase + encodeURIComponent(name), "Unexpected origin for release asset " + name);
    return {
      name,
      sourceUrl: asset.browser_download_url,
      targetUrl: targetBase + encodeURIComponent(name),
      sha256: asset.digest.slice(7),
      bytes: asset.size,
    };
  });
  assert(release.assets.length === expectedNames.length, "Unexpected extra release assets require explicit review");
  return {
    schema: "bkesit.catalog-entry.v1",
    productId: PRODUCT_ID,
    name: "Render Dock",
    category: "Media production",
    platform: "windows",
    architecture: "x64",
    version,
    source: {
      repository: SOURCE_REPOSITORY,
      sha: reference.sha,
      tag,
      releaseUrl: "https://github.com/" + SOURCE_REPOSITORY + "/releases/tag/" + tag,
    },
    distribution: {
      repository: CATALOG_REPOSITORY,
      tag: targetTag,
      releaseUrl: "https://github.com/" + CATALOG_REPOSITORY + "/releases/tag/" + targetTag,
      publicationState: "PENDING",
      assets: sourceAssets,
    },
    image: {
      sourcePath: IMAGE_SOURCE_PATH,
      sourceBlobSha: image.sha,
      targetName: IMAGE_TARGET_NAME,
      url: targetBase + IMAGE_TARGET_NAME,
    },
  };
}
async function getGithub(path) {
  const token = process.env.GITHUB_TOKEN || "";
  const response = await fetch("https://api.github.com/repos/" + SOURCE_REPOSITORY + "/" + path, {
    headers: {
      accept: "application/vnd.github+json",
      "x-github-api-version": "2022-11-28",
      ...(token ? { authorization: "Bearer " + token } : {}),
    },
    signal: AbortSignal.timeout(20_000),
  });
  if (!response.ok) throw new Error("GitHub source query failed (" + response.status + "): " + path);
  return response.json();
}
async function resolveTag(tag) {
  const initial = await getGithub("git/ref/tags/" + encodeURIComponent(tag));
  let pointer = initial.object;
  for (let index = 0; index < 3 && pointer?.type === "tag"; index++) {
    const obj = await getGithub("git/tags/" + pointer.sha);
    pointer = obj.object;
  }
  assert(pointer?.type === "commit" && /^[0-9a-f]{40}$/.test(pointer.sha), "Annotated tag failed to resolve to commit");
  return pointer;
}
async function readJsonAtSha(path, sha) {
  const obj = await getGithub("contents/" + path.split("/").map(encodeURIComponent).join("/") + "?ref=" + sha);
  assert(obj.encoding === "base64" && typeof obj.content === "string", "Expected repository JSON file");
  return JSON.parse(Buffer.from(obj.content.replace(/\s/g, ""), "base64").toString("utf8"));
}
export async function main(args = process.argv.slice(2)) {
  if (args.length !== 2 || args[0] !== "--tag") throw new Error("Usage: node scripts/bkesit-catalog.mjs --tag v1.0.3");
  const tag = args[1];
  versionFromTag(tag);
  const reference = await resolveTag(tag);
  const [release, productManifest, image] = await Promise.all([
    getGithub("releases/tags/" + encodeURIComponent(tag)),
    readJsonAtSha("BKE_RENDER_DOCK/bke.manifest.json", reference.sha),
    getGithub("contents/" + IMAGE_SOURCE_PATH.split("/").map(encodeURIComponent).join("/") + "?ref=" + reference.sha),
  ]);
  const entry = normalizeRelease({ release, reference, productManifest, image, tag });
  const output = "dist/bkesit/catalog-entry.json";
  await mkdir(dirname(output), { recursive: true });
  await writeFile(output, JSON.stringify(entry, null, 2) + "\n", "utf8");
  console.log("VERIFIED " + entry.productId + " " + tag + " source=" + reference.sha);
  console.log("CATALOG_CANDIDATE=" + output + " (NOT PUBLISHED)");
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((error) => { console.error("CATALOG_VALIDATION_FAILED: " + error.message); process.exitCode = 1; });
}
