module ReaperDashboard.Core.ProjectScanner

/// Orchestrates one project scan: read the .rpp, parse it, resolve media
/// references on disk, compute duration and warnings, and assemble the
/// Project record shown in the dashboard. Read-only — never touches the .rpp.

open Fable.Core
open ReaperDashboard.Core

/// A placeholder row shown while a scan is in flight (or before the first one).
let placeholder (rppPath: string) : Project =
    { Path = rppPath
      Name = NodeApi.path.basename (rppPath, NodeApi.path.extname rppPath)
      Folder = NodeApi.path.dirname rppPath
      LastModifiedMs = 0.0
      FileSizeBytes = 0.0
      Regions = []
      Markers = []
      Tracks = []
      AudioItems = []
      MediaRefs = []
      Plugins = []
      DurationSeconds = None
      DurationSource = UnknownDuration
      Warnings = []
      Status = StatusNeedsScan
      MatrixState = MatrixNotScanned
      ScanError = None
      ScannedAtMs = 0.0
      PreviewMp3Path = None
      PreviewWavPath = None }

/// Try candidate locations for a media path referenced by the project:
/// absolute paths as-is; relative paths against the project folder, then
/// against the project's RECORD_PATH (itself possibly relative).
let private resolveMediaRef
    (projectDir: string)
    (recordPath: string option)
    (sourceType: string, rawPath: string)
    : JS.Promise<MediaRef> =

    let candidates =
        if NodeApi.path.isAbsolute rawPath then
            [ rawPath ]
        else
            let fromProject = NodeApi.path.join (projectDir, rawPath)
            match recordPath with
            | Some rp ->
                let rpAbs =
                    if NodeApi.path.isAbsolute rp then rp
                    else NodeApi.path.join (projectDir, rp)
                [ fromProject; NodeApi.path.join (rpAbs, rawPath) ]
            | None -> [ fromProject ]

    let rec firstExisting (cands: string list) : JS.Promise<string option> =
        match cands with
        | [] -> Promise.lift None
        | c :: rest ->
            NodeApi.fileExists c
            |> Promise.bind (fun ok -> if ok then Promise.lift (Some c) else firstExisting rest)

    firstExisting candidates
    |> Promise.map (fun resolved ->
        { RawPath = rawPath
          SourceType = sourceType
          ResolvedPath = resolved
          Exists = resolved.IsSome })

/// Build a Project record for a file whose text could not be parsed.
let private failedScan (rppPath: string) (st: NodeApi.Stats option) (error: string) : Project =
    { placeholder rppPath with
        LastModifiedMs = st |> Option.map (fun s -> s.mtimeMs) |> Option.defaultValue 0.0
        FileSizeBytes = st |> Option.map (fun s -> s.size) |> Option.defaultValue 0.0
        Warnings =
            [ { Kind = ParseIssue
                Severity = SevError
                Message = "Could not parse project file"
                Detail = Some error } ]
        Status = StatusScanFailed
        ScanError = Some error
        ScannedAtMs = NodeApi.nowMs () }

/// Scan one .rpp file. Rejections (unreadable file etc.) are handled by the
/// caller; parse errors resolve successfully as a StatusScanFailed project so
/// the row still appears in the dashboard with a clear badge.
let scan (rppPath: string) : JS.Promise<Project> =
    promise {
        let! st = NodeApi.fsp.stat rppPath
        let! text = NodeApi.fsp.readFile (rppPath, "utf8")

        match RppParser.parse text with
        | Error e -> return failedScan rppPath (Some st) e
        | Ok root ->
            let parsed = RppParser.extract root
            let projectDir = NodeApi.path.dirname rppPath

            let! mediaRefs =
                parsed.MediaFiles
                |> List.map (resolveMediaRef projectDir parsed.RecordPath)
                |> Promise.all

            let mediaRefs = List.ofArray mediaRefs
            let duration, durationSource =
                DurationCalculator.calculate parsed.Regions parsed.AudioItems

            let hasRender = parsed.Regions |> List.exists Project.isRenderRegion
            let matrixState =
                if parsed.MatrixEntries.IsEmpty then MatrixEmpty
                else MatrixAssigned parsed.MatrixEntries.Length
            let warnings, status =
                WarningScanner.scan
                    { HasRenderRegion = hasRender
                      AudioItemCount = parsed.AudioItems.Length
                      MediaRefs = mediaRefs
                      PluginCount = parsed.Plugins.Length
                      RegionCount = parsed.Regions.Length
                      MatrixState = matrixState }

            return
                { Path = rppPath
                  Name = NodeApi.path.basename (rppPath, NodeApi.path.extname rppPath)
                  Folder = projectDir
                  LastModifiedMs = st.mtimeMs
                  FileSizeBytes = st.size
                  Regions = parsed.Regions
                  Markers = parsed.Markers
                  Tracks = parsed.Tracks
                  AudioItems = parsed.AudioItems
                  MediaRefs = mediaRefs
                  Plugins = parsed.Plugins
                  DurationSeconds = duration
                  DurationSource = durationSource
                  Warnings = warnings
                  Status = status
                  MatrixState = matrixState
                  ScanError = None
                  ScannedAtMs = NodeApi.nowMs ()
                  PreviewMp3Path = None
                  PreviewWavPath = None }
    }

/// Find all .rpp files under a folder (recursive), excluding REAPER's own
/// auto-backups (*.rpp-bak) and undo files.
let findRppFiles (folder: string) : JS.Promise<string[]> =
    NodeApi.listFilesRecursive folder
    |> Promise.map (fun files ->
        files
        |> Array.filter (fun f -> f.ToLowerInvariant().EndsWith ".rpp"))
