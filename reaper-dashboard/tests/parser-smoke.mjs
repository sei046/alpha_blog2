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

// --- corrupt input must fail loudly, not crash ---------------------------------

check("parse: truncated file is an Error", parse("<REAPER_PROJECT 0.1\n  TEMPO 120\n").tag, 1);
check("parse: non-project root is an Error", parse("<NOT_A_PROJECT\n>\n").tag, 1);
check("parse: stray closer is an Error", parse(">\n").tag, 1);

console.log(failures === 0 ? "\nAll parser smoke tests passed." : `\n${failures} test(s) FAILED.`);
process.exit(failures === 0 ? 0 : 1);
