# Architecture

## Stack

| Layer | Choice | Why |
|---|---|---|
| Language | F# via [Fable](https://fable.io) | Typed domain model, exhaustive pattern matching over `.rpp` structures, compiles to JS |
| UI architecture | [Elmish](https://elmish.github.io) (model-view-update) | Single immutable state tree; async work (scans, renders) is expressed as commands so the UI never blocks |
| View layer | [Feliz](https://zaid-ajaj.github.io/Feliz/) (React) | Declarative views over the Elmish model |
| Shell | Electron | macOS window chrome, native file dialogs, `fs`/`child_process` access for scanning and launching REAPER |
| Bundler | esbuild | Fast, zero-config bundling of Fable's JS output |

The Electron **main process** (`electron/main.js`) is deliberately tiny:
window creation, native dialogs, app paths. Every piece of domain logic lives
in F# under `src/` and runs in the renderer.

## Module map

```
reaper-dashboard/
├── electron/main.js          Electron shell: window, dialogs, IPC (no domain logic)
├── public/                   index.html + styles.css (dark studio theme)
├── samples/                  .rpp fixtures for parser tests
├── tests/parser-smoke.mjs    Smoke tests for the pure Core modules
└── src/
    ├── Core/                 Domain + services (UI-agnostic)
    │   ├── Types.fs              Pure domain types: Project, Region, Warning, Status…
    │   ├── Format.fs             Pure formatting (durations, dates, sizes)
    │   ├── NodeApi.fs            The ONLY module binding Node/Electron APIs
    │   ├── RppParser.fs          Read-only .rpp tokenizer + tree parser + extractors
    │   ├── RegionRenderMatrix.fs Pure text-surgery for the REGION_RENDER_MATRIX block
    │   ├── DurationCalculator.fs render region → audio bounds → Unknown
    │   ├── WarningScanner.fs     Facts → warnings + overall status (pure)
    │   ├── Settings.fs           settings.json / library.json persistence
    │   ├── ReaperLauncher.fs     Safe `open -a REAPER.app project.rpp`
    │   ├── RppWriter.fs          The ONLY writer: mtime guard + validate + backup
    │   └── ProjectScanner.fs     Orchestrates one scan: read → parse → resolve media
    ├── App/
    │   ├── State.fs              Elmish Model / Msg / init / update (+ Matrix editor)
    │   └── Main.fs               Entry point, mounts the Elmish program
    └── UI/                   Feliz views (no IO — dispatch messages only)
        ├── Badges.fs             Status pills, severity dots
        ├── Sidebar.fs            Library / filters / imported folders
        ├── DashboardTable.fs     Toolbar + sortable project table
        ├── Inspector.fs          Selected-project details, warnings, actions
        ├── BottomPanel.fs        Audio player (MVP 2) + activity log
        ├── SettingsModal.fs      REAPER path configuration
        ├── MatrixEditor.fs       Region Render Matrix grid + dry-run diff modal
        └── View.fs               App shell layout
```

### Dependency rules

- `Types` / `Format` / `RppParser` / `RegionRenderMatrix` /
  `DurationCalculator` / `WarningScanner` are **pure** — no Node, no Electron,
  no IO. They are tested directly by `tests/parser-smoke.mjs` and stay
  reusable if the shell ever changes.
- All effects go through `NodeApi` (bindings) and are triggered exclusively
  from Elmish commands in `State.fs`. Views never perform IO.
- The scan pipeline for one project:

```
path ─▶ ProjectScanner.scan
          ├─ fs.stat / fs.readFile
          ├─ RppParser.parse ──▶ raw tree (every line preserved verbatim)
          ├─ RppParser.extract ─▶ regions, markers, tracks, items, media, plugins
          ├─ resolve media paths on disk (project dir, RECORD_PATH)
          ├─ DurationCalculator.calculate
          └─ WarningScanner.scan ─▶ warnings + status
                    ▼
              Project record ─▶ ProjectScanned msg ─▶ Model ─▶ table row
```

Scans run as parallel promises (one command per project); each result updates
its own row, so importing a 200-project folder fills the table progressively
without freezing the UI.

## Persistence

`~/Library/Application Support/reaper-project-dashboard/`

- `settings.json` — REAPER app path, last-used import folder
- `library.json` — the imported `.rpp` paths **only**

Parse results are deliberately *not* cached: projects are re-scanned on every
launch, so the dashboard can never disagree with what's on disk. If startup
scans ever become slow for huge libraries, a content-hash cache can be added
inside `ProjectScanner` without touching anything else.

## Safety contract

These rules are load-bearing, and `RppWriter` (the only module that writes
`.rpp` files) enforces them in code:

1. **Only `RppWriter.saveProjectText` writes to a project file.** Everything
   else in `Core/` is read-only. The scanner never writes.
2. Every write, in order, before the original is touched: (a) re-`stat` the
   file and abort if its mtime changed since the editor loaded it (never
   clobber edits made in REAPER meanwhile); (b) re-parse the new text with
   `RppParser.parse` and abort if it isn't a valid project; (c) write a
   timestamped backup (`Song.rpp.backup-YYYYMMDD-HHMMSS`) and read it back to
   confirm it holds the original bytes; (d) only then replace the file.
3. Edits only ever touch lines the app fully understands. `RegionRenderMatrix`
   splices exactly one block and re-emits every other byte of the file
   verbatim; unknown lines *inside* the matrix block (unknown flags,
   master-track entries, future extensions) are preserved in place. The UI
   surfaces the count of preserved lines and shows a dry-run diff of the exact
   `ENTRY` lines that will change before any write.
4. Never spawn processes through a shell; argv arrays only
   (`ReaperLauncher`).
5. Anything uncertain about REAPER's formats is probed with small fixtures in
   `samples/` + assertions in `tests/` before the app relies on it. The matrix
   write path has round-trip tests asserting the out-of-block bytes are
   identical after edit/remove/insert/empty operations.

## Roadmap

Done so far: **MVP 1** (dashboard) and **MVP 4/5** (Region Render Matrix
viewer + editor — built together because a safe editor needs the whole
`RppWriter` contract anyway). Remaining stages slot into `Core/` without
disturbing what exists:

| Stage | Feature | New modules |
|---|---|---|
| MVP 2 | MP3 previews: staleness detection (project mtime vs preview mtime vs media mtimes), REAPER CLI batch rendering, built-in player | `PreviewManager`, `ReaperRenderer` |
| MVP 3 | WAV bounce + render queue UI, per-project render logs | `RenderQueue` (+ `ReaperRenderer` growth) |
| MVP 4 ✅ | Region Render Matrix: parse + visual viewer | `RegionRenderMatrix` |
| MVP 5 ✅ | Matrix editing: cell/drag/lane toggling, filters, safe save | `RppWriter` (mtime guard, validation, verified backup, dry-run diff) |
| MVP 6 | Master overview session generation | `MasterOverviewCreator` |

Matrix format notes (implemented against fixtures + community knowledge, not
official docs — hence the preserve-unknown design):

- REAPER stores the matrix as a top-level `<REGION_RENDER_MATRIX` block of
  `ENTRY <region-id> <track-guid>` lines. Region id is the MARKER line's id
  token; track guid is the `{...}` on the `<TRACK` line. Both are already
  extracted by `RppParser`.
- `RegionRenderMatrix` only treats `ENTRY <id> <guid>` lines as editable.
  Entries referencing regions/tracks not present in the project are demoted to
  "preserved" lines by the editor loader — shown as opaque data, never lost,
  never toggled.

Notes for MVP 2 (recorded now so decisions aren't lost):

- REAPER supports unattended rendering via
  `reaper -renderproject <file.rpp>` using the project's saved render
  settings; for previews with *forced* settings (MP3, render region), the
  plan is to render through a temporary copy of the project with a rewritten
  `<RENDER_CFG>` block — which is exactly why `RppWriter` safety comes first —
  or via a temporary ReaScript. Both paths will be probed with logging before
  being trusted.
- A preview is stale when any of: `.rpp` mtime, any referenced media mtime,
  or the stored render settings differ from what the preview was rendered
  with. `MediaRef.ResolvedPath` from MVP 1 already gives us the file list.
