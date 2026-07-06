module ReaperDashboard.Core.PreviewPolicy

/// Pure preview-staleness policy + naming/hashing. No IO, no Node/Electron —
/// so the whole "is this preview up to date?" decision is unit-tested directly
/// (see tests/parser-smoke.mjs). PreviewManager layers manifest disk IO on top.

open ReaperDashboard.Core

type MediaStamp = { Path: string; Mtime: float }

/// What a preview was rendered from (the parts that decide freshness).
type ManifestFacts =
    { ProjectMtime: float
      Media: MediaStamp list
      RenderHash: string }

/// The live picture of a project, gathered during a scan.
type CurrentFacts =
    { ProjectMtime: float
      Media: MediaStamp list
      RenderHash: string
      PreviewFileExists: bool }

/// Media is unchanged when the exact set of (path, mtime) pairs matches. A
/// changed mtime, an added file, or a removed file all count as a change.
let private mediaMatches (a: MediaStamp list) (b: MediaStamp list) =
    let key = List.map (fun s -> s.Path, s.Mtime) >> List.sortBy fst
    key a = key b

/// The staleness decision. `manifest` is None when nothing was ever rendered.
let evaluate (manifest: ManifestFacts option) (facts: CurrentFacts) : PreviewStatus =
    match manifest with
    | None -> PreviewNone
    | Some m ->
        if not facts.PreviewFileExists then PreviewStale PreviewFileGone
        elif m.ProjectMtime <> facts.ProjectMtime then PreviewStale ProjectChanged
        elif not (mediaMatches m.Media facts.Media) then PreviewStale MediaChanged
        elif m.RenderHash <> facts.RenderHash then PreviewStale SettingsChanged
        else PreviewFresh

// --- Hashing --------------------------------------------------------------------

/// FNV-1a 32-bit as hex. Not crypto — we only need different inputs to yield
/// different strings so render-settings changes are detected.
let hashString (s: string) : string =
    let mutable h = 2166136261u
    for ch in s do
        h <- h ^^^ uint32 (int ch)
        h <- h * 16777619u
    sprintf "%08x" h

/// Hash the project's render-relevant lines (RENDER_FILE, RENDER_CFG block,
/// RENDER_FMT, RENDER_RANGE, RENDER_STEMS, …). Changing any marks previews stale.
let renderSettingsHash (root: RppParser.RppNode) : string =
    let rec collect (node: RppParser.RppNode) : string list =
        [ for c in node.Children do
            if c.Tag.StartsWith "RENDER" then
                yield c.RawLine
                if c.IsBlock then yield! collect c ]
    root |> collect |> String.concat "\n" |> hashString

// --- Naming (pure; avoids NodeApi so it stays testable) --------------------------

let private baseNameNoExt (p: string) : string =
    let afterSlash =
        let i = max (p.LastIndexOf '/') (p.LastIndexOf '\\')
        if i >= 0 then p.Substring (i + 1) else p
    let dot = afterSlash.LastIndexOf '.'
    if dot > 0 then afterSlash.Substring (0, dot) else afterSlash

/// A filesystem-safe, collision-resistant key for a project path. The basename
/// keeps it recognisable in the preview folder; the path hash disambiguates
/// two projects that share a filename in different folders.
let previewKey (projectPath: string) : string =
    let safe =
        (baseNameNoExt projectPath).ToCharArray ()
        |> Array.map (fun c -> if System.Char.IsLetterOrDigit c then c else '_')
        |> System.String
    sprintf "%s-%s" safe (hashString projectPath)
