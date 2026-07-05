module ReaperDashboard.Core.WarningScanner

/// Pure warning derivation. Takes facts gathered by ProjectScanner and turns
/// them into user-facing warnings plus an overall status pill.
///
/// MVP 1 detects: missing media files, missing "render" region, projects with
/// no audio items (unknown duration), and parse failures. Plugin references
/// are surfaced as best-effort info; validation against installed plugins is
/// a later stage. Preview staleness / matrix checks arrive in MVP 2/4.

open ReaperDashboard.Core

type ScanFacts =
    { HasRenderRegion: bool
      AudioItemCount: int
      MediaRefs: MediaRef list
      PluginCount: int }

let scan (facts: ScanFacts) : ProjectWarning list * ProjectStatus =
    let missing = facts.MediaRefs |> List.filter (fun m -> not m.Exists)

    let warnings =
        [ for m in missing do
            { Kind = MissingMedia
              Severity = SevError
              Message = sprintf "Missing media (%s)" (if m.SourceType = "" then "?" else m.SourceType)
              Detail = Some m.RawPath }

          if not facts.HasRenderRegion then
            { Kind = NoRenderRegion
              Severity = SevWarning
              Message = "No region named \"render\""
              Detail = Some "Duration falls back to audio bounds; region-based preview/bounce rendering will need this region." }

          if facts.AudioItemCount = 0 then
            { Kind = NoAudioItems
              Severity = SevWarning
              Message = "No audio items found — duration unknown"
              Detail = None }

          if facts.PluginCount > 0 then
            { Kind = PluginInfo
              Severity = SevInfo
              Message = sprintf "%d plugin reference(s) found" facts.PluginCount
              Detail = Some "Installed-plugin validation is best-effort and arrives in a later stage." } ]

    let status =
        if not missing.IsEmpty then StatusMissingMedia
        elif not facts.HasRenderRegion then StatusNoRenderRegion
        else StatusReady

    warnings, status
