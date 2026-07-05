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
      Log: LogEntry list // newest first, capped
      ShowSettings: bool
      SettingsDraft: AppSettings }

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
    | BrowseReaperApp
    | ReaperAppPicked of string
    | PersistDone
    | PersistFailed of exn
    | Logged of LogLevel * string

// --- Helpers -----------------------------------------------------------------

let private log level msg (model: Model) =
    let entry = { TimeMs = NodeApi.nowMs (); Level = level; Message = msg }
    { model with Log = entry :: model.Log |> List.truncate 500 }

let matchesFilter (filter: Filter) (p: Project) =
    match filter with
    | FilterAll -> true
    | FilterNeedsPreview -> p.PreviewMp3Path.IsNone
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

// --- Commands ------------------------------------------------------------------

let private scanCmd (path: string) : Cmd<Msg> =
    Cmd.OfPromise.either ProjectScanner.scan path ProjectScanned (fun e -> ProjectScanFailed (path, e))

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
    let scans = paths |> Array.toList |> List.map scanCmd
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
          Log = []
          ShowSettings = false
          SettingsDraft = defaultSettings }
    let model = log LogInfo "REAPER Project Dashboard started" model
    model, Cmd.OfPromise.either NodeApi.getUserDataPath () UserDataLoaded BootFailed

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
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
        let model = { model with Settings = settings; SettingsDraft = settings }
        let model =
            if library.Length > 0 then
                log LogInfo (sprintf "Loaded library: %d project(s); rescanning…" library.Length) model
            else
                log LogInfo "Library is empty — import projects to get started" model
        let projects =
            (model.Projects, library)
            ||> Array.fold (fun acc p -> acc.Add (p, ProjectScanner.placeholder p))
        { model with
            Projects = projects
            Scanning = Set.ofArray library },
        Cmd.batch (library |> Array.toList |> List.map scanCmd)

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
            Cmd.batch (paths |> Array.toList |> List.map scanCmd)

    | RescanProjects paths ->
        let model = log LogInfo (sprintf "Rescanning %d project(s)…" paths.Length) model
        { model with Scanning = (model.Scanning, paths) ||> List.fold (fun s p -> s.Add p) },
        Cmd.batch (paths |> List.map scanCmd)

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
        let model =
            { model with
                Settings = model.SettingsDraft
                ShowSettings = false }
        log LogInfo (sprintf "Settings saved (REAPER: %s)" model.Settings.ReaperAppPath) model,
        persistSettingsCmd model

    | SetDraftReaperPath p ->
        { model with SettingsDraft = { model.SettingsDraft with ReaperAppPath = p } }, Cmd.none

    | BrowseReaperApp ->
        model, Cmd.OfPromise.either NodeApi.chooseApp () ReaperAppPicked BootFailed

    | ReaperAppPicked "" -> model, Cmd.none
    | ReaperAppPicked p ->
        { model with SettingsDraft = { model.SettingsDraft with ReaperAppPath = p } }, Cmd.none

    | PersistDone -> model, Cmd.none
    | PersistFailed e ->
        log LogError (sprintf "Could not save settings/library: %s" e.Message) model, Cmd.none

    | Logged (level, msg) -> log level msg model, Cmd.none
