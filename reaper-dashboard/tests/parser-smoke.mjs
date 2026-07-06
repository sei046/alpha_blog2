// Smoke tests for the pure Core modules (RppParser, DurationCalculator).
// These run against the Fable-compiled output in build/, without Electron:
//   npm run test:parser
//
// Only pure modules are imported here — nothing that touches Node/Electron
// APIs — so this doubles as a guard that the parser stays IO-free.

import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import { parse, extract, tokenize } from "../build/Core/RppParser.js";
import { calculate } from "../build/Core/DurationCalculator.js";
import { load, parseBlock, render as renderMatrix } from "../build/Core/RegionRenderMatrix.js";
import { evaluate as evalPreview, renderSettingsHash, previewKey } from "../build/Core/PreviewPolicy.js";
import { buildRenderProjectText } from "../build/Core/RenderProject.js";

const here = path.dirname(fileURLToPath(import.meta.url));

// Fable compiles F# lists to iterable linked lists; tuples become JS arrays;
// `option` is erased (Some x -> x, None -> undefined); DU values carry .tag.
const arr = (fsharpList) => Array.from(fsharpList);

let failures = 0;
function check(label, actual, expected) {
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  console.log(`${ok ? "  ok " : "FAIL "}${label}${ok ? "" : ` — expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`}`);
  if (!ok) failures++;
}

// --- tokenizer ---------------------------------------------------------------

check("tokenize: plain", Array.from(tokenize("POSITION 8")), ["POSITION", "8"]);
check(
  "tokenize: double quotes with spaces",
  Array.from(tokenize('NAME "Vox Lead 2"')),
  ["NAME", "Vox Lead 2"]
);
check(
  "tokenize: single-quoted token containing double quotes",
  Array.from(tokenize("NAME 'my \"quoted\" take'")),
  ["NAME", 'my "quoted" take']
);
check(
  "tokenize: backtick-quoted token",
  Array.from(tokenize("NAME `it's a \"mix\"`")),
  ["NAME", "it's a \"mix\""]
);

// --- full sample project -------------------------------------------------------

const text = fs.readFileSync(path.join(here, "..", "samples", "demo-project.rpp"), "utf8");
const parsed = parse(text);
check("parse: sample parses OK (Result tag)", parsed.tag, 0);

const root = parsed.fields[0];
const p = extract(root);

const regions = arr(p.Regions);
check("regions: count", regions.length, 2);
const render = regions.find((r) => r.Name === "render");
check("regions: render region exists", !!render, true);
check("regions: render start", render.Start, 4);
check("regions: render end", render.End, 191.475);

const markers = arr(p.Markers);
check("markers: count", markers.length, 1);
check("markers: name", markers[0].Name, "Chorus 1");

const tracks = arr(p.Tracks);
check("tracks: count", tracks.length, 3);
check("tracks: names", tracks.map((t) => t.Name), ["Drums", "Vox Lead", "MIDI Keys"]);
check("tracks: drum items", tracks[0].ItemCount, 2);

check("audio items: MIDI excluded", arr(p.AudioItems).length, 3);
check(
  "media: three files, section-nested source included",
  arr(p.MediaFiles).map((m) => m[1]),
  ["Audio/drums.wav", "Audio/drums outro.wav", "/Volumes/SSD/Session Files/vox_lead_comp.wav"]
);

check("plugins: found", arr(p.Plugins).map((x) => `${x.Kind}:${x.Name}`), [
  "VST:VST3: Pro-Q 3 (FabFilter)",
  "JS:utility/volume",
]);
check("record path", p.RecordPath, "Audio");

// --- duration ------------------------------------------------------------------

const withRender = calculate(p.Regions, p.AudioItems);
check("duration: uses render region", withRender[0], 191.475 - 4);
check("duration: source is render region (tag)", withRender[1].tag, 0);

// Without the render region it must fall back to audio bounds: 4 .. 191.5.
// Re-parse the sample with the render-region MARKER lines stripped so we
// stay on the public API instead of poking at Fable's list internals.
const textNoRender = text
  .split("\n")
  .filter((l) => !l.trim().startsWith("MARKER 1 "))
  .join("\n");
const pNoRender = extract(parse(textNoRender).fields[0]);
check("regions: render region really stripped", arr(pNoRender.Regions).length, 1);
const fallback = calculate(pNoRender.Regions, pNoRender.AudioItems);
check("duration: audio-bounds fallback", fallback[0], 191.5 - 4);
check("duration: fallback source (tag)", fallback[1].tag, 1);

// --- Region Render Matrix: parse ------------------------------------------------

const DRUMS = "{DDDDDDDD-1111-2222-3333-444444444444}";
const VOX = "{EEEEEEEE-1111-2222-3333-444444444444}";

check("matrix: extract finds entries", arr(p.MatrixEntries).map((e) => `${e[0]}|${e[1]}`), [
  `1|${DRUMS}`,
  `3|${VOX}`,
]);
check("matrix: block found flag", p.MatrixBlockFound, true);
check("regions: ids extracted", arr(p.Regions).map((r) => r.Id), ["1", "3"]);
check("tracks: guids extracted", arr(p.Tracks).map((t) => t.Guid)[0], DRUMS);

const doc = load(text);
const block = parseBlock(doc);
check("matrix doc: block range found", doc.BlockRange != null, true);
check("matrix block: entries", arr(block.Entries).length, 2);
check("matrix block: unknown line preserved verbatim", arr(block.PreservedLines), [
  '    FUTURE_FLAG 42 "unknown data the editor must preserve"',
]);

// --- Region Render Matrix: surgical writes ---------------------------------------

// Everything OUTSIDE the block must be byte-identical after a rewrite.
const outside = (t) => {
  const lines = t.split("\n");
  const s = lines.findIndex((l) => l.trim().startsWith("<REGION_RENDER_MATRIX"));
  if (s < 0) return t;
  let depth = 1, e = s;
  for (let i = s + 1; i < lines.length; i++) {
    const tr = lines[i].trim();
    if (tr.startsWith("<")) depth++;
    else if (tr === ">") { depth--; if (depth === 0) { e = i; break; } }
  }
  return [...lines.slice(0, s), ...lines.slice(e + 1)].join("\n");
};

// 1. Identity rewrite: same entries + preserved lines.
const same = renderMatrix(doc, block.Entries, block.PreservedLines);
check("matrix write: outside-block text is byte-identical", outside(same), outside(text));
check("matrix write: identity keeps both entries", arr(parseBlock(load(same)).Entries).length, 2);
check("matrix write: identity keeps unknown line", arr(parseBlock(load(same)).PreservedLines).length, 1);
check("matrix write: identity result still parses", parse(same).tag, 0);

// 2. Remove one entry: only that ENTRY line disappears.
// (F# lists need constructing via Fable's runtime library; locate it by name
// so the pinned version can move without breaking this test.)
const fableLib = fs
  .readdirSync(path.join(here, "..", "build", "fable_modules"))
  .find((d) => d.startsWith("fable-library-js"));
const { ofArray } = await import(`../build/fable_modules/${fableLib}/List.js`);
const oneEntry = ofArray([["1", DRUMS]]);
const removedText = renderMatrix(doc, oneEntry, block.PreservedLines);
check("matrix write: removal keeps outside text identical", outside(removedText), outside(text));
check("matrix write: removal leaves one entry", arr(parseBlock(load(removedText)).Entries).map((e) => e[0]), ["1"]);
check("matrix write: unknown line survives removal", arr(parseBlock(load(removedText)).PreservedLines).length, 1);
check("matrix write: removal result still parses", parse(removedText).tag, 0);

// 3. Insert into a project with no matrix block at all.
const noBlockText = outside(text);
const docNoBlock = load(noBlockText);
check("matrix: no block detected in stripped project", docNoBlock.BlockRange == null, true);
const inserted = renderMatrix(docNoBlock, oneEntry, ofArray([]));
check("matrix write: inserted block parses", parse(inserted).tag, 0);
check("matrix write: inserted entry readable", arr(parseBlock(load(inserted)).Entries).map((e) => `${e[0]}|${e[1]}`), [`1|${DRUMS}`]);
check("matrix write: insertion keeps rest of file identical", outside(inserted), noBlockText);

// 4. Empty matrix + nothing preserved -> block omitted entirely.
const emptied = renderMatrix(doc, ofArray([]), ofArray([]));
check("matrix write: fully-empty matrix removes block", load(emptied).BlockRange == null, true);
check("matrix write: emptied file otherwise identical", emptied, outside(text));

// --- Preview staleness (pure) ---------------------------------------------------

// Fable: option None -> undefined; F# records -> plain objects; DU -> {tag,fields}.
// `ofArray` (F# list constructor) is imported in the matrix-write section above.
const mediaStamp = (p, m) => ({ Path: p, Mtime: m });
const listOf = (xs) => ofArray(xs);

const manifest = {
  ProjectMtime: 1000,
  Media: listOf([mediaStamp("/a.wav", 500), mediaStamp("/b.wav", 600)]),
  RenderHash: "hash1",
  PreviewFile: "x.wav",
  Format: ".wav",
  RenderedAt: 1,
};
const facts = (over) => ({
  ProjectMtime: 1000,
  Media: listOf([mediaStamp("/a.wav", 500), mediaStamp("/b.wav", 600)]),
  RenderHash: "hash1",
  PreviewFileExists: true,
  ...over,
});

check("preview: no manifest -> PreviewNone", evalPreview(undefined, facts({})).tag, /* PreviewNone */ 1);
check("preview: all matching -> Fresh", evalPreview(manifest, facts({})).tag, /* PreviewFresh */ 2);
check("preview: file gone -> Stale", evalPreview(manifest, facts({ PreviewFileExists: false })).tag, /* PreviewStale */ 3);
check("preview: project changed -> Stale", evalPreview(manifest, facts({ ProjectMtime: 1001 })).tag, 3);
check(
  "preview: media mtime changed -> Stale",
  evalPreview(manifest, facts({ Media: listOf([mediaStamp("/a.wav", 999), mediaStamp("/b.wav", 600)]) })).tag,
  3
);
check(
  "preview: media order does not matter",
  evalPreview(manifest, facts({ Media: listOf([mediaStamp("/b.wav", 600), mediaStamp("/a.wav", 500)]) })).tag,
  2
);
check(
  "preview: media removed -> Stale",
  evalPreview(manifest, facts({ Media: listOf([mediaStamp("/a.wav", 500)]) })).tag,
  3
);
check("preview: settings changed -> Stale", evalPreview(manifest, facts({ RenderHash: "hash2" })).tag, 3);

// The stale reason should distinguish causes (field 0 of the DU).
check("preview: file-gone reason tag", evalPreview(manifest, facts({ PreviewFileExists: false })).fields[0].tag, 0);
check("preview: settings-changed reason tag", evalPreview(manifest, facts({ RenderHash: "z" })).fields[0].tag, 3);

// renderSettingsHash reacts to RENDER lines and ignores the rest.
const hashOf = (t) => renderSettingsHash(parse(t).fields[0]);
const baseR = '<REAPER_PROJECT 0.1\n  TEMPO 120 4 4\n  RENDER_FILE "out.wav"\n  <RENDER_CFG\n    ZXZhdxgA\n  >\n>\n';
const baseR2 = '<REAPER_PROJECT 0.1\n  TEMPO 130 4 4\n  RENDER_FILE "out.wav"\n  <RENDER_CFG\n    ZXZhdxgA\n  >\n>\n';
const baseR3 = '<REAPER_PROJECT 0.1\n  TEMPO 120 4 4\n  RENDER_FILE "different.wav"\n  <RENDER_CFG\n    ZXZhdxgA\n  >\n>\n';
check("hash: ignores non-RENDER change (tempo)", hashOf(baseR) === hashOf(baseR2), true);
check("hash: reacts to RENDER_FILE change", hashOf(baseR) === hashOf(baseR3), false);

check("previewKey: filesystem-safe", /^[A-Za-z0-9_]+-[0-9a-f]{8}$/.test(previewKey("/Vol/My Song (mix).rpp")), true);
check(
  "previewKey: same name diff folder -> diff key",
  previewKey("/a/Song.rpp") === previewKey("/b/Song.rpp"),
  false
);

// --- Render-project transform (pure) --------------------------------------------

const projForRender =
  '<REAPER_PROJECT 0.1 "7" 1\n' +
  '  RECORD_PATH "Audio" ""\n' +
  '  RENDER_FILE "old/output.wav"\n' +
  '  RENDER_PATTERN "$project"\n' +
  '  <RENDER_CFG\n    ZXZhdxgA\n  >\n' +
  '  <TRACK {G1}\n    NAME "Drums"\n  >\n' +
  '>\n';

const previewReq = { Kind: { tag: 0 }, OutputPath: "/previews/x.wav", RenderRegion: { Id: "1", Name: "render", Start: 4, End: 191.5, Color: undefined } };
const renderedProj = buildRenderProjectText(previewReq, projForRender);

check("render-xform: result still parses", parse(renderedProj).tag, 0);
check("render-xform: RENDER_FILE replaced", renderedProj.includes('RENDER_FILE "/previews/x.wav"'), true);
check("render-xform: old RENDER_FILE gone", renderedProj.includes('RENDER_FILE "old/output.wav"'), false);
check("render-xform: single-file pattern", renderedProj.includes('RENDER_PATTERN ""'), true);
check("render-xform: bounds pinned to render region", renderedProj.includes("RENDER_RANGE 0 4 191.5 0 0"), true);
check("render-xform: RENDER_CFG (format) preserved", renderedProj.includes("ZXZhdxgA"), true);
check("render-xform: track data untouched", renderedProj.includes('NAME "Drums"'), true);
check("render-xform: RECORD_PATH untouched", renderedProj.includes('RECORD_PATH "Audio" ""'), true);

// No render region -> entire project bounds.
const noRegionReq = { ...previewReq, RenderRegion: undefined };
check("render-xform: no region -> entire project bounds", buildRenderProjectText(noRegionReq, projForRender).includes("RENDER_RANGE 1 0 0 0 0"), true);

// Missing RENDER_FILE line is inserted, not dropped.
const noRenderFile = '<REAPER_PROJECT 0.1\n  TEMPO 120 4 4\n>\n';
const insertedRender = buildRenderProjectText(noRegionReq, noRenderFile);
check("render-xform: RENDER_FILE inserted when absent", insertedRender.includes('RENDER_FILE "/previews/x.wav"'), true);
check("render-xform: inserted result parses", parse(insertedRender).tag, 0);

// --- corrupt input must fail loudly, not crash ---------------------------------

check("parse: truncated file is an Error", parse("<REAPER_PROJECT 0.1\n  TEMPO 120\n").tag, 1);
check("parse: non-project root is an Error", parse("<NOT_A_PROJECT\n>\n").tag, 1);
check("parse: stray closer is an Error", parse(">\n").tag, 1);

console.log(failures === 0 ? "\nAll parser smoke tests passed." : `\n${failures} test(s) FAILED.`);
process.exit(failures === 0 ? 0 : 1);
