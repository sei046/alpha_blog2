module ReaperDashboard.Core.RenderProject

/// Pure .rpp render-config transform. Given a project's text, produce a copy
/// with the output path + render bounds forced while KEEPING the project's own
/// `<RENDER_CFG>` (audio format) and every non-RENDER line verbatim. No IO —
/// ReaperRenderer layers the temp-file write + REAPER spawn on top.
///
/// Splitting this out keeps the string surgery unit-testable (see
/// tests/parser-smoke.mjs) without dragging in Node/Electron.

open ReaperDashboard.Core

type RenderKind =
    | PreviewRender
    | WavBounce
    | MatrixRender

type RenderRequest =
    { Kind: RenderKind
      /// Absolute output file (preview/wav) or output directory (matrix).
      OutputPath: string
      /// The "render" region, when the project has one (drives bounds).
      RenderRegion: Region option }

let private eol (text: string) = if text.Contains "\r\n" then "\r\n" else "\n"

/// Replace the first top-level `tag ...` leaf line, or insert `line` right
/// after the project header if absent. Depth tracking keeps us at the project
/// level so we never touch a RENDER_FILE nested inside some other block.
let private setTopLevelLine (tag: string) (line: string) (lines: string list) : string list =
    let mutable depth = 0
    let mutable replaced = false
    let out =
        [ for raw in lines do
            let t = (raw: string).Trim ()
            let atProjectLevel = depth = 1
            if t.StartsWith "<" then
                depth <- depth + 1
                yield raw
            elif t = ">" then
                depth <- depth - 1
                yield raw
            elif atProjectLevel && not replaced && (t = tag || t.StartsWith (tag + " ")) then
                replaced <- true
                yield "  " + line
            else
                yield raw ]
    if replaced then out
    else
        match out with
        | header :: rest -> header :: ("  " + line) :: rest
        | [] -> [ "  " + line ]

/// The full transformed project text. RENDER_PATTERN "" means "use RENDER_FILE
/// exactly" (a single master-mix file); matrix stems use a $region-$track
/// pattern so each region/track becomes its own file.
let buildRenderProjectText (req: RenderRequest) (originalText: string) : string =
    let e = eol originalText
    let trailing = originalText.EndsWith e
    let body = if trailing then originalText.Substring (0, originalText.Length - e.Length) else originalText
    let lines = body.Replace("\r\n", "\n").Split '\n' |> List.ofArray

    // Bounds: mode 0 = custom time range (pinned to the render region when we
    // have one); mode 1 = entire project.
    let boundsLine =
        match req.RenderRegion with
        | Some r -> sprintf "RENDER_RANGE 0 %g %g 0 0" r.Start r.End
        | None -> "RENDER_RANGE 1 0 0 0 0"

    let transforms =
        match req.Kind with
        | PreviewRender
        | WavBounce ->
            [ "RENDER_FILE", sprintf "RENDER_FILE \"%s\"" req.OutputPath
              "RENDER_PATTERN", "RENDER_PATTERN \"\""
              "RENDER_RANGE", boundsLine
              "RENDER_STEMS", "RENDER_STEMS 0" ]         // 0 = master mix, not stems
        | MatrixRender ->
            // Best-effort: render every region via the Region Render Matrix,
            // one file per region/track, named by wildcards, into OutputPath.
            [ "RENDER_FILE", sprintf "RENDER_FILE \"%s\"" req.OutputPath
              "RENDER_PATTERN", "RENDER_PATTERN \"$region-$track\""
              "RENDER_RANGE", "RENDER_RANGE 3 0 0 0 0"   // 3 = project regions
              "RENDER_STEMS", "RENDER_STEMS 32" ]          // 32 = region render matrix

    let final = (lines, transforms) ||> List.fold (fun acc (tag, line) -> setTopLevelLine tag line acc)
    let outText = String.concat e final
    if trailing then outText + e else outText
