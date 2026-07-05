module ReaperDashboard.UI.Badges

/// Status pills and small shared visual atoms.

open Feliz
open ReaperDashboard.Core

let private pill (cls: string) (label: string) =
    Html.span [ prop.className ("pill " + cls); prop.text label ]

let statusPill (status: ProjectStatus) =
    let cls =
        match status with
        | StatusReady -> "ok"
        | StatusMissingMedia -> "error"
        | StatusNoRenderRegion -> "warn"
        | StatusNeedsScan -> "neutral"
        | StatusScanFailed -> "error"
    pill cls (Project.statusLabel status)

/// MVP 1: previews don't exist yet, so every project is "Needs Preview".
let previewPill (p: Project) =
    match p.PreviewMp3Path with
    | Some _ -> pill "ok" "Preview OK"
    | None -> pill "neutral" "Needs Preview"

/// MVP 4 parses the Region Render Matrix; until then it is unscanned.
let matrixPill (_p: Project) =
    pill "neutral" "Not Scanned"

let renderRegionCell (p: Project) =
    match Project.tryRenderRegion p with
    | Some r -> pill "ok" (Format.duration (r.End - r.Start))
    | None -> pill "warn" "None"

let severityColor =
    function
    | SevError -> "var(--status-error)"
    | SevWarning -> "var(--status-warn)"
    | SevInfo -> "var(--status-info)"

let severityDot (sev: Severity) =
    Html.span
        [ prop.className "w-dot"
          prop.style [ style.color (severityColor sev) ]
          prop.text "●" ]
