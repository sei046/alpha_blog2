# REAPER Project Dashboard

A macOS desktop app for managing large numbers of REAPER `.rpp` projects:
import them into a library, see status/duration/warnings at a glance, keep an
always-up-to-date audio preview of each project, audition it in a built-in
player, and open or render any project in REAPER — including a visual Region
Render Matrix editor for stems.

Built with **Fable** (F# compiled to JavaScript), **Elmish** (model-view-update
architecture), **Feliz/React** for the UI, and **Electron** as the macOS shell.

**Current stage: MVP 1 (dashboard), MVP 2 (preview intelligence + player),
MVP 3 (WAV bounce + render queue), MVP 4/5 (Region Render Matrix viewer &
editor), and the matrix render trigger.** See
[ARCHITECTURE.md](./ARCHITECTURE.md) for the full module map and roadmap.

---

## What works today (MVP 1)

- Import individual `.rpp` files or a whole folder (recursive scan)
- Library persists between launches and is **re-scanned from disk on startup**
  so the dashboard is never stale
- Dashboard table: name, status pill, duration (+ where it came from), render
  region, modified date, path — sortable, searchable, filterable
- Sidebar filters: All / Needs Preview / Has Warnings / Missing Render Region,
  plus per-folder filtering
- Safe `.rpp` parsing: regions, markers, tracks, media items, plugin
  references, `RECORD_PATH`
- Duration: `render` region if present, otherwise first-to-last audio item,
  otherwise Unknown + flagged
- Warnings: missing media files (checked on disk), missing `render` region,
  no audio items, unparseable project files
- Open in REAPER (double-click a row, or the inspector button); multi-select
  with ⌘-click / ⇧-click; batch open
- Right inspector with details, warning list and per-project actions
- Activity log in the bottom panel

### Region Render Matrix editor

- The dashboard's **Matrix** column shows the real matrix state per project
  (Empty / *n* cells).
- **Edit Matrix** (inspector) opens a full-screen editor: regions as rows
  (with colours and lengths), tracks as columns (with colours). Click a cell
  to toggle it, **drag to paint** a range, click a region/track name to
  toggle the whole lane, filter rows/columns by name. Each enabled cell is a
  region × track combination REAPER renders as its own stem.
- Saving is gated behind a **dry-run diff** that lists exactly which
  `ENTRY` lines will be added/removed — nothing else in the file changes.
- Every save: verifies the file hasn't changed on disk since the editor
  opened → validates the edited text re-parses as a project → writes and
  verifies a timestamped backup (`Song.rpp.backup-YYYYMMDD-HHMMSS`) → only
  then replaces the file.
- Any line in the matrix block the app doesn't fully understand (unknown
  flags, master-track entries, future extensions) is preserved verbatim, and
  the editor tells you how many such lines exist.

**Format caveat:** the `REGION_RENDER_MATRIX` / `ENTRY <region-id>
<track-guid>` format was implemented against fixtures and REAPER community
knowledge, not official documentation — that's exactly why the
preserve-unknown + backup + diff design exists. Before trusting it on
important sessions, run one save on a scratch project and confirm REAPER
reads the matrix back as expected (the diff shows you the raw lines).
Region renaming is deliberately not editable yet.

### Audio previews + player (MVP 2)

- Each project tracks an audio **preview** whose freshness is computed on every
  scan. The dashboard's **Preview** column shows *Up to date* / *Stale* /
  *Needs Preview*, and a stale preview flips the project's status pill to
  **Preview Stale**.
- A preview is **stale** when any of these differ from when it was rendered:
  the `.rpp` mtime, any referenced media file's mtime (or a file added/removed),
  or the project's render settings — plus "the preview file is missing". The
  exact reason is shown in the inspector's warnings.
- **Render Preview** (inspector) / **Render Stale Previews** and **Render
  Missing** (toolbar) / **Render N Previews** (multi-select) drive REAPER to
  render, then re-scan so the status updates.
- The bottom **Audio Preview** player is a real audio player: select a project
  with a preview and hit **▶ Play Preview** to audition it without opening
  REAPER.

Each render records a small manifest (`<key>.preview.json`) next to the audio
in the preview folder (configurable in Settings; defaults to the app-data
folder). The manifest is what staleness is judged against.

### WAV bounce + render queue (MVP 3)

- **Bounce WAV** (inspector) bounces a single project; **Bounce N WAVs**
  (multi-select) queues a bounce for each. Bounces use the `render` region if
  the project has one, otherwise full bounds.
- Output folder and filename pattern are configurable in Settings (default:
  next to each `.rpp`, named `$project.wav`). `$project` expands to the project
  name.
- All rendering — previews, WAV bounces, and matrix stems — flows through a
  single **render queue** shown in the bottom panel. Jobs run **one at a time**
  (REAPER is a single instance, so parallel renders would fight over it), each
  with a live status: *Queued → Rendering… → Done / Failed*. Failures show the
  reason inline and never block the rest of the queue. **Clear finished**
  tidies completed jobs.

### Rendering caveat (previews, WAV bounces, matrix stems)

Rendering drives REAPER from the command line
(`REAPER.app/Contents/MacOS/REAPER -renderproject <temp>`), always on a
throwaway **copy** of your project with only the output path + bounds rewritten
— your `.rpp` is never handed to the renderer. The preview keeps your project's
own render **format** (so it comes out in whatever you normally render to; the
player handles WAV/MP3/etc). The numeric REAPER render flags are best-effort
from community knowledge, not an official spec, so on first use confirm a
render actually produces the file you expect — the activity log prints the
exact command and verifies the output exists before reporting success.

## Requirements

| Dependency | Version | Why |
|---|---|---|
| macOS | 12+ recommended | Target platform |
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 | Compiles the F# source via Fable |
| [Node.js](https://nodejs.org) | 20+ | Electron + build tooling |
| REAPER | any recent | The app launches it; not needed to browse |

```bash
# with Homebrew:
brew install dotnet-sdk node
```

## Run it on macOS

```bash
cd reaper-dashboard
npm install        # installs Electron/React/esbuild and restores the Fable tool
npm start          # compiles F# -> JS, bundles, launches the app
```

`npm start` is three steps you can also run separately:

```bash
dotnet fable src -o build   # F# -> JavaScript (npm run compile)
npm run bundle              # esbuild -> dist/renderer.js
npm run app                 # electron .
```

For development, run these in two terminals and relaunch the app to pick up
changes:

```bash
npm run watch:fable     # recompile F# on save
npm run watch:bundle    # rebundle on change
```

### Parser tests

```bash
npm run test:parser
```

Runs the smoke suite in `tests/parser-smoke.mjs` against
`samples/demo-project.rpp` — tokenizer quoting rules, region pairing,
MIDI-vs-audio item classification, duration fallback, and corrupt-file
handling. Add your own `.rpp` files to `samples/` when you hit a project the
parser mis-reads, and extend the test.

## Configuring the REAPER path

Click **Settings** (top right). The default is `/Applications/REAPER.app`;
use **Browse…** if yours lives elsewhere. Projects are opened with
`open -a <REAPER.app> <project.rpp>` — no shell interpolation, and a running
REAPER instance is reused rather than launching a second copy.

Settings and the imported library are stored as plain JSON in
`~/Library/Application Support/reaper-project-dashboard/`
(`settings.json`, `library.json`). Delete them to reset the app.

## How the .rpp parser works

`.rpp` files are a line-oriented text tree: blocks open with `<TAG …` and
close with a lone `>`, and every other line is a leaf of whitespace-separated
tokens, where strings are quoted with `"`, `'` or a backtick (REAPER picks a
quote character that doesn't occur in the content, so there are no escape
sequences).

`src/Core/RppParser.fs` does this in two phases:

1. **Tree parse** — every line becomes a node holding its *raw text*, its
   tokens, and its children. Nothing is interpreted, normalised or dropped at
   this stage, and unbalanced blocks (truncated/corrupt files) return a clear
   error instead of a partial result. Keeping unknown lines verbatim is what
   will let the future `RppWriter` round-trip files byte-for-byte, editing
   only the specific entries it understands.
2. **Extraction** — targeted, best-effort readers walk the tree:
   - `MARKER <id> <pos> <name> <isRegion> <color>` lines; a *region* is two
     MARKER lines sharing an id, the first carrying name + start, the second
     the end position. Unpaired starts are kept as zero-length regions rather
     than dropped.
   - `<TRACK` blocks: `NAME`, `PEAKCOL` (colour), `<ITEM` children with
     `POSITION`/`LENGTH` and nested `<SOURCE type>` blocks (recursing through
     `SECTION` wrappers), collecting `FILE` references. Items are classified
     audio/non-audio by source type (`WAVE`, `FLAC`, `MP3`, … vs `MIDI` etc.).
   - FX references (`<VST`, `<JS`, `<AU`, `<CLAP`, …) anywhere in the tree —
     names only for now; matching against installed plugins is explicitly
     best-effort and comes with the full warning scanner.

Media paths are then resolved against the project folder and the project's
`RECORD_PATH` (absolute paths as-is) and checked on disk; misses become
**Missing Media** warnings that show the exact path from the project file.

## Safety model

- MVP 1 is strictly **read-only** with respect to your projects.
- Future write features (matrix editing, region renames) land only together
  with: timestamped backups before every write, re-parse validation of the
  written file, and a dry-run diff preview. That contract is recorded in
  [ARCHITECTURE.md](./ARCHITECTURE.md).
- Removing projects from the library never touches disk.
- REAPER is launched via argv arrays (never a shell), so paths with spaces or
  quotes are inert.

## Known limitations (MVP 1)

- Preview / Matrix / Last-render columns are placeholders ("Needs Preview",
  "Not Scanned") until MVP 2 and 4 fill them in.
- Plugin references are listed but not validated against installed plugins.
- Item classification and the audio-source-type list are best-effort; unusual
  sources (video, subprojects) may affect duration accuracy. The `~` marker in
  the Duration column tells you the value came from item bounds, not a region.
- Media resolution doesn't yet follow REAPER's full search order (e.g.
  alternate search paths configured in REAPER's preferences).
- The Electron renderer runs with `nodeIntegration` (standard for a local
  trusted tool, but will be tightened to a preload bridge before any feature
  renders untrusted content).
- Drag-and-drop import and app packaging (`.dmg`) are not wired up yet.

## Next steps

MVP 2 (MP3 previews + built-in player) → MVP 3 (WAV bounce + render queue) →
MVP 4 (matrix viewer) → MVP 5 (matrix editor with backups/validation) →
MVP 6 (master overview session). Details in
[ARCHITECTURE.md](./ARCHITECTURE.md#roadmap).
