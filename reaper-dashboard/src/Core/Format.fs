module ReaperDashboard.Core.Format

/// Small pure formatting helpers used by the UI.

let private pad2 (n: int) = if n < 10 then "0" + string n else string n

/// mm:ss for short durations, h:mm:ss when an hour or longer.
let duration (seconds: float) =
    let total = max 0 (int (System.Math.Round seconds))
    let h = total / 3600
    let m = (total % 3600) / 60
    let s = total % 60
    if h > 0 then sprintf "%d:%s:%s" h (pad2 m) (pad2 s)
    else sprintf "%d:%s" m (pad2 s)

let durationSourceLabel =
    function
    | ReaperDashboard.Core.FromRenderRegion -> "Render region"
    | ReaperDashboard.Core.FromAudioBounds -> "Audio bounds"
    | ReaperDashboard.Core.UnknownDuration -> "Unknown"

/// Best-effort CSS colour from a REAPER colour int (PEAKCOL / region colour).
/// REAPER sets 0x1000000 as the "custom colour" flag; the low 24 bits are the
/// colour with platform-dependent byte order — treated here as R,G,B (macOS).
let reaperColor (c: int) : string =
    let rgb = c &&& 0xFFFFFF
    sprintf "rgb(%d,%d,%d)" (rgb &&& 0xFF) ((rgb >>> 8) &&& 0xFF) ((rgb >>> 16) &&& 0xFF)

let fileSize (bytes: float) =
    if bytes >= 1024.0 * 1024.0 then sprintf "%.1f MB" (bytes / 1024.0 / 1024.0)
    elif bytes >= 1024.0 then sprintf "%.0f KB" (bytes / 1024.0)
    else sprintf "%.0f B" bytes

/// Epoch milliseconds -> "YYYY-MM-DD HH:MM" in local time.
/// (Fable compiles System.DateTime to a JS Date, so this is cheap.)
let dateTime (epochMs: float) =
    let d = Fable.Core.JS.Constructors.Date.Create epochMs
    sprintf "%04d-%02d-%02d %s:%s" d.Year d.Month d.Day (pad2 d.Hour) (pad2 d.Minute)

/// Epoch milliseconds -> "HH:MM:SS" for the activity log.
let timeOfDay (epochMs: float) =
    let d = Fable.Core.JS.Constructors.Date.Create epochMs
    sprintf "%s:%s:%s" (pad2 d.Hour) (pad2 d.Minute) (pad2 d.Second)
