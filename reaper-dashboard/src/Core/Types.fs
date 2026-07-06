namespace ReaperDashboard.Core

/// Pure domain types shared across the whole app.
/// This module must stay free of IO and JS interop so the parser,
/// duration calculator and warning scanner remain unit-testable.

type Severity =
    | SevInfo
    | SevWarning
    | SevError

type WarningKind =
    | NoRenderRegion
    | MissingMedia
    | NoAudioItems
    | ParseIssue
    | PluginInfo
    | MatrixIssue

type ProjectWarning =
    { Kind: WarningKind
      Severity: Severity
      Message: string
      Detail: string option }

type DurationSource =
    | FromRenderRegion
    | FromAudioBounds
    | UnknownDuration

type Region =
    { /// The MARKER line's id token — also used by REGION_RENDER_MATRIX entries.
      Id: string
      Name: string
      Start: float
      End: float
      Color: int option }

type Marker =
    { Name: string
      Position: float }

type TrackInfo =
    { /// The {GUID} token from the <TRACK line — the matrix's track identifier.
      Guid: string
      Name: string
      Color: int option
      ItemCount: int }

/// One audio-bearing media item on the timeline (position/length in seconds).
type AudioItem =
    { Position: float
      Length: float
      SourceTypes: string list }

/// A media file referenced by the project.
/// RawPath is exactly what the .rpp contains; ResolvedPath is where we found
/// it on disk (absolute), if we found it at all.
type MediaRef =
    { RawPath: string
      SourceType: string
      ResolvedPath: string option
      Exists: bool }

type PluginRef =
    { Kind: string   // VST / JS / AU / CLAP / ...
      Name: string }

/// State of the project's Region Render Matrix (the region × track grid
/// REAPER uses to render stems).
type RenderMatrixState =
    | MatrixNotScanned
    | MatrixEmpty
    /// Number of region × track assignments this app fully understands.
    | MatrixAssigned of int

/// Overall project status shown as the main pill in the dashboard.
/// Later stages add PreviewMissing / PreviewStale / RenderFailed.
type ProjectStatus =
    | StatusReady
    | StatusMissingMedia
    | StatusNoRenderRegion
    | StatusNeedsScan
    | StatusScanFailed

type Project =
    { Path: string
      Name: string
      Folder: string
      LastModifiedMs: float
      FileSizeBytes: float
      Regions: Region list
      Markers: Marker list
      Tracks: TrackInfo list
      AudioItems: AudioItem list
      MediaRefs: MediaRef list
      Plugins: PluginRef list
      DurationSeconds: float option
      DurationSource: DurationSource
      Warnings: ProjectWarning list
      Status: ProjectStatus
      MatrixState: RenderMatrixState
      ScanError: string option
      ScannedAtMs: float
      // Reserved for MVP 2+ (PreviewManager fills these in).
      PreviewMp3Path: string option
      PreviewWavPath: string option }

type LogLevel =
    | LogInfo
    | LogSuccess
    | LogWarning
    | LogError

type LogEntry =
    { TimeMs: float
      Level: LogLevel
      Message: string }

module Project =

    /// The region that drives duration and (later) preview/bounce rendering.
    /// Matched case-insensitively after trimming; the spec says "render".
    let isRenderRegion (r: Region) =
        r.Name.Trim().ToLowerInvariant() = "render"

    let tryRenderRegion (p: Project) =
        p.Regions |> List.tryFind isRenderRegion

    let hasRenderRegion (p: Project) =
        p.Regions |> List.exists isRenderRegion

    let missingMedia (p: Project) =
        p.MediaRefs |> List.filter (fun m -> not m.Exists)

    let hasBlockingWarnings (p: Project) =
        p.Status = StatusScanFailed
        || p.Warnings |> List.exists (fun w -> w.Severity <> SevInfo)

    let statusLabel =
        function
        | StatusReady -> "Ready"
        | StatusMissingMedia -> "Missing Media"
        | StatusNoRenderRegion -> "No Render Region"
        | StatusNeedsScan -> "Needs Scan"
        | StatusScanFailed -> "Scan Failed"

    /// Sort rank: most broken first when sorting by status descending.
    let statusRank =
        function
        | StatusScanFailed -> 0
        | StatusMissingMedia -> 1
        | StatusNoRenderRegion -> 2
        | StatusNeedsScan -> 3
        | StatusReady -> 4
