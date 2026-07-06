module ReaperDashboard.App.State

/// Elmish model / messages / update. All IO runs as Cmds (promises), so the
/// UI stays responsive while projects scan in the background.

open Elmish
open ReaperDashboard.Core
open ReaperDashboard.Core.Settings

type Filter =
    | FilterAll
    | FilterNeedsPreview
    | FilterHasWarnings
    | FilterMissingRenderRegion
    | FilterFolder of string

type SortColumn =
    | SortName
    | SortStatus
    | SortDuration
    | SortModified
    | SortPath

type SortDir =
    | Asc
    | Desc

/// A matrix cell: (region id, track guid).
type MatrixCell = string * string

/// State of the Region Render Matrix editor overlay. Opened against a fresh
/// read of the .rpp; nothing touches disk until the user confirms the diff.
type MatrixEditor =
    { ProjectPath: string
      ProjectName: string
      /// The exact file text the editor was opened against (backup source).
      SourceText: string
      /// mtime at load — the save aborts if the file changed underneath us.
      LoadedMtimeMs: float
      Regions: Region list
      Tracks: TrackInfo list
      /// Lines inside the matrix block we don't edit (preserved verbatim),
      /// including entries referencing regions/tracks not in this project.
      Preserved: string list
      Original: Set<MatrixCell>
      Current: Set<MatrixCell>
      RegionFilter: string
      TrackFilter: string
      /// Some enable-state while drag-painting cells, None otherwise.
      Painting: bool option
      ShowDiff: bool
      Saving: bool }

/// A single job in the render queue. Jobs run one at a time (REAPER is a
/// single instance, so parallel renders would fight over it).
type RenderJobStatus =
    | JobQueued
    | JobRunning
    | JobDone of output: string
    | JobFailed of reason: string

type RenderJob =
    { Id: int
      Path: string
      Name: string
      Kind: RenderProject.RenderKind
      Status: RenderJobStatus }

type Model =
    { UserData: string option
      Settings: AppSettings
      Projects: Map<string, Project>
      Selected: Set<string>
      /// Anchor for shift-selection; also the project shown in the inspector.
      Focused: string option
      Filter: Filter
      Search: string
      Sort: SortColumn * SortDir
      Scanning: Set<string>
      /// The render queue (previews, WAV bounces, matrix renders), newest last.
      /// Jobs run sequentially; finished jobs stay until cleared.
      Queue: RenderJob list
      NextJobId: int
      /// The resolved preview folder (<userData>/previews or the configured one).
      PreviewFolder: string
      /// The project currently loaded in the audio player, if any.
      NowPlaying: string option
      Log: LogEntry list // newest first, capped
      ShowSettings: bool
      SettingsDraft: AppSettings
      MatrixEditor: MatrixEditor option }

type Msg =
    | UserDataLoaded of string
    | BootLoaded of AppSettings * string[]
    | BootFailed of exn
    | ImportFilesClicked
    | ImportFolderClicked
    | FilesPicked of string[]
    | FolderPicked of string
    | FolderListed of folder: string * rppPaths: string[]
    | FolderListFailed of folder: string * exn
    | ProjectScanned of Project
    | ProjectScanFailed of path: string * exn
    | RescanAll
    | RescanProjects of string list
    | RemoveSelected
    | RowClicked of path: string * meta: bool * shift: bool
    | ClearSelection
    | SetFilter of Filter
    | SetSearch of string
    | SetSort of SortColumn
    | OpenInReaper of paths: string list
    | OpenSucceeded of string
    | OpenFailed of path: string * exn
    | OpenSettingsModal
    | CloseSettingsModal
    | SaveSettingsModal
    | SetDraftReaperPath of string
    | SetDraftPreviewFolder of string
    | SetDraftWavFolder of string
    | SetDraftWavPattern of string
    | BrowseReaperApp
    | ReaperAppPicked of string
    | BrowsePreviewFolder
    | PreviewFolderPicked of string
    | BrowseWavFolder
    | WavFolderPicked of string
    | PersistDone
    | PersistFailed of exn
    | Logged of LogLevel * string
    // --- Render queue: previews / WAV bounces / matrix stems ---
    | RenderPreview of paths: string list
    | RenderMissingPreviews
    | RenderStalePreviews
    | BounceWav of paths: string list
    | RenderMatrix of path: string
    | PumpQueue
    | JobSucceeded of jobId: int * output: string
    | JobErrored of jobId: int * exn
    | ClearFinishedJobs
    // --- Audio player ---
    | PlayPreview of path: string
    | StopPreview
    // --- Region Render Matrix editor ---
    | OpenMatrixEditor of path: string
    | MatrixEditorLoaded of MatrixEditor
    | MatrixEditorLoadFailed of path: string * exn
    | MatrixPaintStart of MatrixCell
    | MatrixPaintOver of MatrixCell
    | MatrixPaintEnd
    | MatrixToggleRegion of regionId: string
    | MatrixToggleTrack of trackGuid: string
    | MatrixEnableAllVisible
    | MatrixClearAllVisible
    | MatrixSetRegionFilter of string
    | MatrixSetTrackFilter of string
    | MatrixRevert
    | MatrixShowDiff of bool
    | MatrixCancel
    | MatrixSaveConfirmed
    | MatrixSaved of path: string * backupPath: string
    | MatrixSaveFailed of exn

// --- Helpers -----------------------------------------------------------------

let private log level msg (model: Model) =
    let entry = { TimeMs = NodeApi.nowMs (); Level = level; Message = msg }
    { model with Log = entry :: model.Log |> List.truncate 500 }

let matchesFilter (filter: Filter) (p: Project) =
    match filter with
    | FilterAll -> true
    | FilterNeedsPreview -> Project.needsPreview p
    | FilterHasWarnings -> Project.hasBlockingWarnings p
    | FilterMissingRenderRegion -> not (Project.hasRenderRegion p)
    | FilterFolder f -> p.Folder = f

let matchesSearch (search: string) (p: Project) =
    let s = search.Trim().ToLowerInvariant ()
    s = ""
    || p.Name.ToLowerInvariant().Contains s
    || p.Path.ToLowerInvariant().Contains s

let sortProjects ((col, dir): SortColumn * SortDir) (projects: Project list) =
    let sorted =
        match col with
        | SortName -> projects |> List.sortBy (fun p -> p.Name.ToLowerInvariant ())
        | SortStatus -> projects |> List.sortBy (fun p -> Project.statusRank p.Status, p.Name.ToLowerInvariant ())
        | SortDuration -> projects |> List.sortBy (fun p -> p.DurationSeconds |> Option.defaultValue -1.0)
        | SortModified -> projects |> List.sortBy (fun p -> p.LastModifiedMs)
        | SortPath -> projects |> List.sortBy (fun p -> p.Path.ToLowerInvariant ())
    match dir with
    | Asc -> sorted
    | Desc -> List.rev sorted

/// The rows currently visible in the table, in display order.
/// Shared by the view and by shift-range selection in update.
let visibleProjects (model: Model) : Project list =
    model.Projects
    |> Map.toList
    |> List.map snd
    |> List.filter (fun p -> matchesFilter model.Filter p && matchesSearch model.Search p)
    |> sortProjects model.Sort

let importedFolders (model: Model) : (string * int) list =
    model.Projects
    |> Map.toList
    |> List.map (fun (_, p) -> p.Folder)
    |> List.countBy id
    |> List.sortBy fst

// --- Matrix editor helpers --------------------------------------------------------

module Matrix =

    let visibleRegions (ed: MatrixEditor) =
        let f = ed.RegionFilter.Trim().ToLowerInvariant ()
        ed.Regions
        |> List.filter (fun r -> f = "" || r.Name.ToLowerInvariant().Contains f)

    let visibleTracks (ed: MatrixEditor) =
        let f = ed.TrackFilter.Trim().ToLowerInvariant ()
        ed.Tracks
        |> List.filter (fun t -> f = "" || t.Name.ToLowerInvariant().Contains f)

    let isDirty (ed: MatrixEditor) = ed.Current <> ed.Original

    let added (ed: MatrixEditor) = Set.difference ed.Current ed.Original
    let removed (ed: MatrixEditor) = Set.difference ed.Original ed.Current

    /// Entries ordered by region position in the project, then track order —
    /// keeps saved files deterministic and diffs readable.
    let orderedEntries (ed: MatrixEditor) : (string * string) list =
        let regionOrder = ed.Regions |> List.mapi (fun i r -> r.Id, i) |> Map.ofList
        let trackOrder = ed.Tracks |> List.mapi (fun i t -> t.Guid, i) |> Map.ofList
        ed.Current
        |> Set.toList
        |> List.sortBy (fun (r, t) ->
            (regionOrder.TryFind r |> Option.defaultValue 9999),
            (trackOrder.TryFind t |> Option.defaultValue 9999))

    /// Open the editor against a fresh read of the project file. Entries that
    /// reference regions/tracks missing from the project are demoted to
    /// preserved lines: shown as "other data", never edited, never lost.
    let loadEditor (path: string) : Fable.Core.JS.Promise<MatrixEditor> =
        promise {
            let! st = NodeApi.fsp.stat path
            let! text = NodeApi.fsp.readFile (path, "utf8")
            match RppParser.parse text with
            | Error e ->
                return failwith (sprintf "Cannot edit matrix: project failed to parse (%s)" e)
            | Ok root ->
                let parsed = RppParser.extract root
                let doc = RegionRenderMatrix.load text
                let block = RegionRenderMatrix.parseBlock doc
                let regionIds = parsed.Regions |> List.map (fun r -> r.Id) |> Set.ofList
                let trackGuids = parsed.Tracks |> List.map (fun t -> t.Guid) |> Set.ofList
                let known, orphaned =
                    block.Entries
                    |> List.partition (fun (r, g) -> regionIds.Contains r && trackGuids.Contains g)
                let preserved =
                    block.PreservedLines
                    @ (orphaned |> List.map (fun (r, g) -> sprintf "    ENTRY %s %s" r g))
                let cells = Set.ofList known
                return
                    { ProjectPath = path
                      ProjectName = NodeApi.path.basename (path, NodeApi.path.extname path)
                      SourceText = text
                      LoadedMtimeMs = st.mtimeMs
                      Regions = parsed.Regions
                      Tracks = parsed.Tracks
                      Preserved = preserved
                      Original = cells
                      Current = cells
                      RegionFilter = ""
                      TrackFilter = ""
                      Painting = None
                      ShowDiff = false
                      Saving = false }
        }

    let private setCell (cell: MatrixCell) (enabled: bool) (ed: MatrixEditor) =
        { ed with Current = if enabled then ed.Current.Add cell else ed.Current.Remove cell }

    let paintStart (cell: MatrixCell) (ed: MatrixEditor) =
        let enabling = not (ed.Current.Contains cell)
        { setCell cell enabling ed with Painting = Some enabling }

    let paintOver (cell: MatrixCell) (ed: MatrixEditor) =
        match ed.Painting with
        | Some enabling -> setCell cell enabling ed
        | None -> ed

    /// Row/column toggles: if every visible cell in the lane is enabled,
    /// clear the lane; otherwise fill it.
    let toggleRegion (regionId: string) (ed: MatrixEditor) =
        let tracks = visibleTracks ed
        let allOn = tracks |> List.forall (fun t -> ed.Current.Contains (regionId, t.Guid))
        let current =
            (ed.Current, tracks)
            ||> List.fold (fun acc t ->
                if allOn then acc.Remove (regionId, t.Guid) else acc.Add (regionId, t.Guid))
        { ed with Current = current }

    let toggleTrack (trackGuid: string) (ed: MatrixEditor) =
        let regions = visibleRegions ed
        let allOn = regions |> List.forall (fun r -> ed.Current.Contains (r.Id, trackGuid))
        let current =
            (ed.Current, regions)
            ||> List.fold (fun acc r ->
                if allOn then acc.Remove (r.Id, trackGuid) else acc.Add (r.Id, trackGuid))
        { ed with Current = current }

    let setAllVisible (enabled: bool) (ed: MatrixEditor) =
        let current =
            (ed.Current, [ for r in visibleRegions ed do for t in visibleTracks ed -> r.Id, t.Guid ])
            ||> List.fold (fun acc cell -> if enabled then acc.Add cell else acc.Remove cell)
        { ed with Current = current }

// --- Commands ------------------------------------------------------------------

let private scanCmd (previewFolder: string) (path: string) : Cmd<Msg> =
    Cmd.OfPromise.either (ProjectScanner.scan previewFolder) path ProjectScanned (fun e -> ProjectScanFailed (path, e))

let private persistLibraryCmd (model: Model) : Cmd<Msg> =
    match model.UserData with
    | None -> Cmd.none
    | Some ud ->
        let paths = model.Projects |> Map.toArray |> Array.map fst
        Cmd.OfPromise.either (Settings.saveLibrary ud) paths (fun _ -> PersistDone) PersistFailed

let private persistSettingsCmd (model: Model) : Cmd<Msg> =
    match model.UserData with
    | None -> Cmd.none
    | Some ud ->
        Cmd.OfPromise.either (Settings.saveSettings ud) model.Settings (fun _ -> PersistDone) PersistFailed

// --- Render queue ---------------------------------------------------------------

module Queue =

    let kindLabel =
        function
        | RenderProject.PreviewRender -> "Preview"
        | RenderProject.WavBounce -> "WAV"
        | RenderProject.MatrixRender -> "Matrix"

    let isActive (j: RenderJob) =
        match j.Status with
        | JobQueued | JobRunning -> true
        | _ -> false

    /// A path is "busy" if it has a queued or running job (any kind).
    let pathBusy (model: Model) (path: string) =
        model.Queue |> List.exists (fun j -> j.Path = path && isActive j)

    let anyRunning (model: Model) =
        model.Queue |> List.exists (fun j -> j.Status = JobRunning)

    let hasFinished (model: Model) =
        model.Queue |> List.exists (fun j -> not (isActive j))

    /// The output path/dir for a job's kind.
    let outputFor (model: Model) (kind: RenderProject.RenderKind) (p: Project) : string =
        match kind with
        | RenderProject.PreviewRender ->
            NodeApi.path.join (model.PreviewFolder, PreviewPolicy.previewKey p.Path + ".wav")
        | RenderProject.WavBounce ->
            let folder =
                if model.Settings.WavBounceFolder.Trim () <> "" then model.Settings.WavBounceFolder
                else p.Folder
            NodeApi.path.join (folder, Settings.expandWavPattern model.Settings.WavPattern p.Name)
        | RenderProject.MatrixRender ->
            NodeApi.path.join (p.Folder, "Stems")

/// The IO for one job: read the project, render, and (for previews) write the
/// staleness manifest. Resolves JobDone/JobFailed for the queue to advance.
let private runJobCmd (model: Model) (job: RenderJob) : Cmd<Msg> =
    match model.Projects.TryFind job.Path with
    | None -> Cmd.ofMsg (JobErrored (job.Id, exn "project no longer in library"))
    | Some p ->
        let output = Queue.outputFor model job.Kind p
        let req: RenderProject.RenderRequest =
            { Kind = job.Kind
              OutputPath = output
              RenderRegion = (match job.Kind with RenderProject.MatrixRender -> None | _ -> Project.tryRenderRegion p) }
        let run () =
            promise {
                let! st = NodeApi.fsp.stat p.Path
                let! text = NodeApi.fsp.readFile (p.Path, "utf8")
                let onLog msg = Fable.Core.JS.console.log ("[render] " + msg)
                let! outcome = ReaperRenderer.render model.Settings.ReaperAppPath model.PreviewFolder text req onLog
                // Previews record a manifest so staleness can be judged later.
                if job.Kind = RenderProject.PreviewRender then
                    match RppParser.parse text with
                    | Ok root ->
                        let media =
                            p.MediaRefs
                            |> List.choose (fun r ->
                                match r.ResolvedPath, r.Mtime with
                                | Some path, Some mt -> Some { PreviewPolicy.Path = path; PreviewPolicy.Mtime = mt }
                                | _ -> None)
                        let manifest: PreviewManager.Manifest =
                            { ProjectMtime = st.mtimeMs
                              Media = media
                              RenderHash = PreviewPolicy.renderSettingsHash root
                              PreviewFile = NodeApi.path.basename (outcome.OutputPath, "")
                              Format = NodeApi.path.extname outcome.OutputPath
                              RenderedAt = NodeApi.nowMs () }
                        do! PreviewManager.writeManifest model.PreviewFolder p.Path manifest
                    | Error _ -> ()
                return outcome.OutputPath
            }
        Cmd.OfPromise.either run () (fun out -> JobSucceeded (job.Id, out)) (fun e -> JobErrored (job.Id, e))

/// Append jobs for the given projects+kind (skipping paths already busy) and
/// return the model plus a PumpQueue kick.
let private enqueue (kind: RenderProject.RenderKind) (projects: Project list) (model: Model) : Model * Cmd<Msg> =
    let fresh = projects |> List.filter (fun p -> not (Queue.pathBusy model p.Path))
    if fresh.IsEmpty then model, Cmd.none
    else
        let mutable nextId = model.NextJobId
        let jobs =
            fresh
            |> List.map (fun p ->
                let id = nextId
                nextId <- nextId + 1
                { Id = id; Path = p.Path; Name = p.Name; Kind = kind; Status = JobQueued })
        let model =
            { model with Queue = model.Queue @ jobs; NextJobId = nextId }
            |> log LogInfo (sprintf "Queued %d %s render(s)" jobs.Length (Queue.kindLabel kind))
        model, Cmd.ofMsg PumpQueue

/// Add paths to the library (as placeholders) and kick off scans for them.
let private addAndScan (paths: string[]) (model: Model) : Model * Cmd<Msg> =
    let newPaths = paths |> Array.filter (fun p -> not (model.Projects.ContainsKey p))
    let projects =
        (model.Projects, newPaths)
        ||> Array.fold (fun acc p -> acc.Add (p, ProjectScanner.placeholder p))
    // Re-scan everything we were given, including already-imported paths —
    // re-importing doubles as a refresh.
    let model =
        { model with
            Projects = projects
            Scanning = (model.Scanning, paths) ||> Array.fold (fun s p -> s.Add p) }
    let model =
        if newPaths.Length > 0 then
            log LogInfo (sprintf "Imported %d project(s); scanning…" newPaths.Length) model
        else if paths.Length > 0 then
            log LogInfo (sprintf "Re-scanning %d already-imported project(s)…" paths.Length) model
        else model
    let scans = paths |> Array.toList |> List.map (scanCmd model.PreviewFolder)
    model, Cmd.batch (persistLibraryCmd model :: scans)

// --- Init / update ----------------------------------------------------------------

let init () : Model * Cmd<Msg> =
    let model =
        { UserData = None
          Settings = defaultSettings
          Projects = Map.empty
          Selected = Set.empty
          Focused = None
          Filter = FilterAll
          Search = ""
          Sort = SortName, Asc
          Scanning = Set.empty
          Queue = []
          NextJobId = 1
          PreviewFolder = ""
          NowPlaying = None
          Log = []
          ShowSettings = false
          SettingsDraft = defaultSettings
          MatrixEditor = None }
    let model = log LogInfo "REAPER Project Dashboard started" model
    model, Cmd.OfPromise.either NodeApi.getUserDataPath () UserDataLoaded BootFailed

let rec update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UserDataLoaded ud ->
        let load () =
            promise {
                let! settings = Settings.loadSettings ud
                let! library = Settings.loadLibrary ud
                return settings, library
            }
        { model with UserData = Some ud },
        Cmd.OfPromise.either load () BootLoaded BootFailed

    | BootLoaded (settings, library) ->
        let previewFolder =
            match model.UserData with
            | Some ud -> Settings.effectivePreviewFolder ud settings
            | None -> ""
        let model = { model with Settings = settings; SettingsDraft = settings; PreviewFolder = previewFolder }
        let model =
            if library.Length > 0 then
                log LogInfo (sprintf "Loaded library: %d project(s); rescanning…" library.Length) model
            else
                log LogInfo "Library is empty — import projects to get started" model
        let model = log LogInfo (sprintf "Preview folder: %s" previewFolder) model
        let projects =
            (model.Projects, library)
            ||> Array.fold (fun acc p -> acc.Add (p, ProjectScanner.placeholder p))
        { model with
            Projects = projects
            Scanning = Set.ofArray library },
        Cmd.batch (library |> Array.toList |> List.map (scanCmd previewFolder))

    | BootFailed e ->
        // Also reused as the generic failure handler for dialog commands.
        log LogError (sprintf "Error: %s" e.Message) model, Cmd.none

    | ImportFilesClicked ->
        model,
        Cmd.OfPromise.either NodeApi.chooseRppFiles model.Settings.LastImportDir FilesPicked BootFailed

    | ImportFolderClicked ->
        model,
        Cmd.OfPromise.either
            (NodeApi.chooseFolder "Import folder of REAPER projects")
            model.Settings.LastImportDir
            FolderPicked
            BootFailed

    | FilesPicked paths when paths.Length = 0 -> model, Cmd.none
    | FilesPicked paths ->
        let settings = { model.Settings with LastImportDir = NodeApi.path.dirname paths.[0] }
        let model, cmd = addAndScan paths { model with Settings = settings }
        model, Cmd.batch [ cmd; persistSettingsCmd model ]

    | FolderPicked "" -> model, Cmd.none
    | FolderPicked folder ->
        let settings = { model.Settings with LastImportDir = folder }
        let model = log LogInfo (sprintf "Searching %s for .rpp files…" folder) { model with Settings = settings }
        model,
        Cmd.batch
            [ Cmd.OfPromise.either
                ProjectScanner.findRppFiles
                folder
                (fun found -> FolderListed (folder, found))
                (fun e -> FolderListFailed (folder, e))
              persistSettingsCmd model ]

    | FolderListed (folder, found) when found.Length = 0 ->
        log LogWarning (sprintf "No .rpp files found under %s" folder) model, Cmd.none
    | FolderListed (_, found) ->
        addAndScan found model

    | FolderListFailed (folder, e) ->
        log LogError (sprintf "Could not read folder %s: %s" folder e.Message) model, Cmd.none

    | ProjectScanned p ->
        let model =
            { model with
                Projects = model.Projects.Add (p.Path, p)
                Scanning = model.Scanning.Remove p.Path }
        let model =
            match p.Status with
            | StatusScanFailed ->
                log LogError (sprintf "Scan failed: %s — %s" p.Name (p.ScanError |> Option.defaultValue "unknown error")) model
            | StatusMissingMedia ->
                log LogWarning (sprintf "Scanned %s: %d missing media file(s)" p.Name (Project.missingMedia p).Length) model
            | _ ->
                log LogInfo (sprintf "Scanned %s (%s)" p.Name (Project.statusLabel p.Status)) model
        model, Cmd.none

    | ProjectScanFailed (path, e) ->
        // File unreadable/vanished — keep the row, badge it as failed.
        let name = NodeApi.path.basename (path, NodeApi.path.extname path)
        let failed =
            { ProjectScanner.placeholder path with
                Status = StatusScanFailed
                ScanError = Some e.Message
                ScannedAtMs = NodeApi.nowMs ()
                Warnings =
                    [ { Kind = ParseIssue
                        Severity = SevError
                        Message = "Could not read project file"
                        Detail = Some e.Message } ] }
        let model =
            { model with
                Projects = model.Projects.Add (path, failed)
                Scanning = model.Scanning.Remove path }
        log LogError (sprintf "Could not read %s: %s" name e.Message) model, Cmd.none

    | RescanAll ->
        let paths = model.Projects |> Map.toArray |> Array.map fst
        if paths.Length = 0 then model, Cmd.none
        else
            let model = log LogInfo (sprintf "Rescanning %d project(s)…" paths.Length) model
            { model with Scanning = Set.ofArray paths },
            Cmd.batch (paths |> Array.toList |> List.map (scanCmd model.PreviewFolder))

    | RescanProjects paths ->
        let model = log LogInfo (sprintf "Rescanning %d project(s)…" paths.Length) model
        { model with Scanning = (model.Scanning, paths) ||> List.fold (fun s p -> s.Add p) },
        Cmd.batch (paths |> List.map (scanCmd model.PreviewFolder))

    | RemoveSelected ->
        // Removes rows from the library only — never touches files on disk.
        let removedCount = model.Selected.Count
        let projects = (model.Projects, model.Selected) ||> Set.fold (fun acc p -> acc.Remove p)
        let model =
            { model with
                Projects = projects
                Selected = Set.empty
                Focused = None }
        let model = log LogInfo (sprintf "Removed %d project(s) from the library (files untouched)" removedCount) model
        model, persistLibraryCmd model

    | RowClicked (path, meta, shift) ->
        let selected =
            if shift then
                let visible = visibleProjects model |> List.map (fun p -> p.Path)
                match model.Focused with
                | Some anchor when List.contains anchor visible && List.contains path visible ->
                    let ai = visible |> List.findIndex ((=) anchor)
                    let pi = visible |> List.findIndex ((=) path)
                    let lo, hi = min ai pi, max ai pi
                    let range = visible |> List.skip lo |> List.take (hi - lo + 1) |> Set.ofList
                    if meta then Set.union model.Selected range else range
                | _ -> Set.singleton path
            elif meta then
                if model.Selected.Contains path then model.Selected.Remove path
                else model.Selected.Add path
            else
                Set.singleton path
        { model with Selected = selected; Focused = Some path }, Cmd.none

    | ClearSelection ->
        { model with Selected = Set.empty; Focused = None }, Cmd.none

    | SetFilter f -> { model with Filter = f }, Cmd.none
    | SetSearch s -> { model with Search = s }, Cmd.none

    | SetSort col ->
        let sort =
            match model.Sort with
            | c, Asc when c = col -> col, Desc
            | _ -> col, Asc
        { model with Sort = sort }, Cmd.none

    | OpenInReaper paths ->
        let reaper = model.Settings.ReaperAppPath
        let model =
            (model, paths)
            ||> List.fold (fun m p ->
                log LogInfo (sprintf "Opening in REAPER: %s" (NodeApi.path.basename (p, ""))) m)
        let cmds =
            paths
            |> List.map (fun p ->
                Cmd.OfPromise.either
                    (ReaperLauncher.openProject reaper)
                    p
                    (fun _ -> OpenSucceeded p)
                    (fun e -> OpenFailed (p, e)))
        model, Cmd.batch cmds

    | OpenSucceeded path ->
        log LogSuccess (sprintf "Handed %s to REAPER" (NodeApi.path.basename (path, ""))) model, Cmd.none

    | OpenFailed (path, e) ->
        log LogError (sprintf "Failed to open %s: %s" (NodeApi.path.basename (path, "")) e.Message) model, Cmd.none

    | OpenSettingsModal ->
        { model with ShowSettings = true; SettingsDraft = model.Settings }, Cmd.none
    | CloseSettingsModal ->
        { model with ShowSettings = false }, Cmd.none
    | SaveSettingsModal ->
        let previewFolder =
            match model.UserData with
            | Some ud -> Settings.effectivePreviewFolder ud model.SettingsDraft
            | None -> model.PreviewFolder
        let folderChanged = previewFolder <> model.PreviewFolder
        let model =
            { model with
                Settings = model.SettingsDraft
                PreviewFolder = previewFolder
                ShowSettings = false }
        let model = log LogInfo (sprintf "Settings saved (REAPER: %s)" model.Settings.ReaperAppPath) model
        // A new preview folder means previews must be re-discovered.
        if folderChanged && not model.Projects.IsEmpty then
            let paths = model.Projects |> Map.toArray |> Array.map fst
            let model = log LogInfo "Preview folder changed — rescanning previews…" model
            { model with Scanning = Set.ofArray paths },
            Cmd.batch (persistSettingsCmd model :: (paths |> Array.toList |> List.map (scanCmd previewFolder)))
        else
            model, persistSettingsCmd model

    | SetDraftReaperPath p ->
        { model with SettingsDraft = { model.SettingsDraft with ReaperAppPath = p } }, Cmd.none
    | SetDraftPreviewFolder p ->
        { model with SettingsDraft = { model.SettingsDraft with PreviewFolder = p } }, Cmd.none

    | BrowseReaperApp ->
        model, Cmd.OfPromise.either NodeApi.chooseApp () ReaperAppPicked BootFailed

    | ReaperAppPicked "" -> model, Cmd.none
    | ReaperAppPicked p ->
        { model with SettingsDraft = { model.SettingsDraft with ReaperAppPath = p } }, Cmd.none

    | BrowsePreviewFolder ->
        model,
        Cmd.OfPromise.either (NodeApi.chooseFolder "Choose preview render folder") model.PreviewFolder PreviewFolderPicked BootFailed
    | PreviewFolderPicked "" -> model, Cmd.none
    | PreviewFolderPicked p ->
        { model with SettingsDraft = { model.SettingsDraft with PreviewFolder = p } }, Cmd.none

    | SetDraftWavFolder p ->
        { model with SettingsDraft = { model.SettingsDraft with WavBounceFolder = p } }, Cmd.none
    | SetDraftWavPattern p ->
        { model with SettingsDraft = { model.SettingsDraft with WavPattern = p } }, Cmd.none
    | BrowseWavFolder ->
        model,
        Cmd.OfPromise.either (NodeApi.chooseFolder "Choose WAV bounce folder") model.Settings.WavBounceFolder WavFolderPicked BootFailed
    | WavFolderPicked "" -> model, Cmd.none
    | WavFolderPicked p ->
        { model with SettingsDraft = { model.SettingsDraft with WavBounceFolder = p } }, Cmd.none

    | PersistDone -> model, Cmd.none
    | PersistFailed e ->
        log LogError (sprintf "Could not save settings/library: %s" e.Message) model, Cmd.none

    | Logged (level, msg) -> log level msg model, Cmd.none

    // --- Render queue ----------------------------------------------------------

    | RenderPreview paths ->
        enqueue RenderProject.PreviewRender (paths |> List.choose model.Projects.TryFind) model

    | RenderMissingPreviews ->
        let targets =
            model.Projects |> Map.toList |> List.map snd
            |> List.filter (fun p -> p.PreviewStatus = PreviewNone)
        if targets.IsEmpty then log LogInfo "No projects are missing a preview." model, Cmd.none
        else enqueue RenderProject.PreviewRender targets model

    | RenderStalePreviews ->
        let targets =
            model.Projects |> Map.toList |> List.map snd
            |> List.filter Project.previewIsStale
        if targets.IsEmpty then log LogInfo "No previews are stale." model, Cmd.none
        else enqueue RenderProject.PreviewRender targets model

    | BounceWav paths ->
        enqueue RenderProject.WavBounce (paths |> List.choose model.Projects.TryFind) model

    | RenderMatrix path ->
        enqueue RenderProject.MatrixRender (path |> model.Projects.TryFind |> Option.toList) model

    | PumpQueue ->
        // Start the next queued job, unless one is already running.
        if Queue.anyRunning model then model, Cmd.none
        else
            match model.Queue |> List.tryFind (fun j -> j.Status = JobQueued) with
            | None -> model, Cmd.none
            | Some job ->
                let queue =
                    model.Queue
                    |> List.map (fun j -> if j.Id = job.Id then { j with Status = JobRunning } else j)
                let model =
                    { model with Queue = queue }
                    |> log LogInfo (sprintf "Rendering %s: %s…" (Queue.kindLabel job.Kind) job.Name)
                model, runJobCmd model job

    | JobSucceeded (jobId, output) ->
        match model.Queue |> List.tryFind (fun j -> j.Id = jobId) with
        | None -> model, Cmd.ofMsg PumpQueue
        | Some job ->
            let queue = model.Queue |> List.map (fun j -> if j.Id = jobId then { j with Status = JobDone output } else j)
            let model =
                { model with Queue = queue }
                |> log LogSuccess (sprintf "%s rendered: %s → %s" (Queue.kindLabel job.Kind) job.Name output)
            // Previews change on-disk state the dashboard reads, so re-scan.
            let rescan =
                if job.Kind = RenderProject.PreviewRender then
                    [ scanCmd model.PreviewFolder job.Path ]
                else []
            { model with Scanning = if rescan.IsEmpty then model.Scanning else model.Scanning.Add job.Path },
            Cmd.batch (Cmd.ofMsg PumpQueue :: rescan)

    | JobErrored (jobId, e) ->
        match model.Queue |> List.tryFind (fun j -> j.Id = jobId) with
        | None -> model, Cmd.ofMsg PumpQueue
        | Some job ->
            let queue = model.Queue |> List.map (fun j -> if j.Id = jobId then { j with Status = JobFailed e.Message } else j)
            { model with Queue = queue }
            |> log LogError (sprintf "%s render failed: %s — %s" (Queue.kindLabel job.Kind) job.Name e.Message),
            Cmd.ofMsg PumpQueue

    | ClearFinishedJobs ->
        { model with Queue = model.Queue |> List.filter Queue.isActive }, Cmd.none

    | PlayPreview path ->
        // The <audio> element is driven by NowPlaying in the view.
        { model with NowPlaying = Some path }, Cmd.none
    | StopPreview ->
        { model with NowPlaying = None }, Cmd.none

    // --- Region Render Matrix editor -------------------------------------------

    | OpenMatrixEditor path ->
        let name = NodeApi.path.basename (path, NodeApi.path.extname path)
        let model = log LogInfo (sprintf "Opening Region Render Matrix editor for %s…" name) model
        model,
        Cmd.OfPromise.either Matrix.loadEditor path MatrixEditorLoaded (fun e -> MatrixEditorLoadFailed (path, e))

    | MatrixEditorLoaded ed ->
        let model =
            if ed.Regions.IsEmpty then
                log LogWarning (sprintf "%s has no regions — the matrix has no rows to edit" ed.ProjectName) model
            else
                log LogInfo
                    (sprintf "Matrix editor: %d region(s) × %d track(s), %d assignment(s)%s"
                        ed.Regions.Length ed.Tracks.Length ed.Original.Count
                        (if ed.Preserved.IsEmpty then "" else sprintf " (+%d preserved line(s) of other matrix data)" ed.Preserved.Length))
                    model
        { model with MatrixEditor = Some ed }, Cmd.none

    | MatrixEditorLoadFailed (path, e) ->
        log LogError (sprintf "Could not open matrix editor for %s: %s" (NodeApi.path.basename (path, "")) e.Message) model,
        Cmd.none

    | MatrixPaintStart cell ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (Matrix.paintStart cell) }, Cmd.none
    | MatrixPaintOver cell ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (Matrix.paintOver cell) }, Cmd.none
    | MatrixPaintEnd ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (fun ed -> { ed with Painting = None }) }, Cmd.none
    | MatrixToggleRegion rid ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (Matrix.toggleRegion rid) }, Cmd.none
    | MatrixToggleTrack guid ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (Matrix.toggleTrack guid) }, Cmd.none
    | MatrixEnableAllVisible ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (Matrix.setAllVisible true) }, Cmd.none
    | MatrixClearAllVisible ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (Matrix.setAllVisible false) }, Cmd.none
    | MatrixSetRegionFilter s ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (fun ed -> { ed with RegionFilter = s }) }, Cmd.none
    | MatrixSetTrackFilter s ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (fun ed -> { ed with TrackFilter = s }) }, Cmd.none
    | MatrixRevert ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (fun ed -> { ed with Current = ed.Original }) }, Cmd.none
    | MatrixShowDiff show ->
        { model with MatrixEditor = model.MatrixEditor |> Option.map (fun ed -> { ed with ShowDiff = show }) }, Cmd.none

    | MatrixCancel ->
        let model =
            match model.MatrixEditor with
            | Some ed when Matrix.isDirty ed ->
                log LogInfo (sprintf "Matrix editor closed for %s — changes discarded, file untouched" ed.ProjectName) model
            | _ -> model
        { model with MatrixEditor = None }, Cmd.none

    | MatrixSaveConfirmed ->
        match model.MatrixEditor with
        | None -> model, Cmd.none
        | Some ed ->
            let save () =
                promise {
                    let doc = RegionRenderMatrix.load ed.SourceText
                    let newText = RegionRenderMatrix.render doc (Matrix.orderedEntries ed) ed.Preserved
                    return! RppWriter.saveProjectText ed.ProjectPath ed.SourceText ed.LoadedMtimeMs newText
                }
            { model with MatrixEditor = Some { ed with Saving = true } },
            Cmd.OfPromise.either save () (fun backup -> MatrixSaved (ed.ProjectPath, backup)) MatrixSaveFailed

    | MatrixSaved (path, backup) ->
        let name = NodeApi.path.basename (path, NodeApi.path.extname path)
        let model =
            model
            |> log LogSuccess (sprintf "Region Render Matrix saved for %s (validated OK)" name)
            |> log LogInfo (sprintf "Backup written: %s" backup)
        { model with
            MatrixEditor = None
            Scanning = model.Scanning.Add path },
        scanCmd model.PreviewFolder path

    | MatrixSaveFailed e ->
        let model =
            { model with
                MatrixEditor = model.MatrixEditor |> Option.map (fun ed -> { ed with Saving = false; ShowDiff = false }) }
        log LogError (sprintf "Matrix save aborted: %s" e.Message) model, Cmd.none
