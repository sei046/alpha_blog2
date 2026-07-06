module ReaperDashboard.Core.RegionRenderMatrix

/// Text-level surgery for the REGION_RENDER_MATRIX block of a .rpp file.
///
/// REAPER stores the Region Render Matrix (the region × track grid used to
/// render stems) as a top-level block:
///
///   <REGION_RENDER_MATRIX
///     ENTRY 1 {5EE49328-....}      ← region id 1 renders track {5EE...}
///     ENTRY 3 {8A426EEF-....}
///   >
///
/// Safety design — this module NEVER round-trips the whole file through a
/// serializer. It splices exactly one block of lines and leaves every other
/// byte of the file untouched:
///
///  * Only `ENTRY <regionId> <trackGuid>` lines are considered editable.
///  * Every other line inside the block (unknown flags, master-track or
///    otherwise-shaped entries, future format extensions) is preserved
///    verbatim, in order, on every save.
///  * All functions here are pure (text in, text out); disk IO and backups
///    live in RppWriter.
///
/// The format was implemented against fixtures, not official documentation —
/// which is why the preserve-unknown rule above is load-bearing. The dry-run
/// diff in the editor shows exactly which lines will change before any write.

let blockTag = "REGION_RENDER_MATRIX"

type Doc =
    { /// File content split into lines (original EOLs recorded separately).
      Lines: string[]
      Eol: string
      /// True when the original text ended with a trailing newline.
      TrailingNewline: bool
      /// Inclusive line-index range of the existing top-level block:
      /// (index of "<REGION_RENDER_MATRIX", index of its closing ">").
      BlockRange: (int * int) option }

type Block =
    { /// (regionId, trackGuid) pairs from ENTRY lines we fully understand.
      Entries: (string * string) list
      /// Verbatim lines inside the block that we do not edit.
      PreservedLines: string list }

// --- Loading ---------------------------------------------------------------------

let private detectEol (text: string) =
    if text.Contains "\r\n" then "\r\n" else "\n"

/// Walk the file once, tracking block depth, to find the matrix block at the
/// top level of the project (depth 1 = direct child of <REAPER_PROJECT).
let load (text: string) : Doc =
    let eol = detectEol text
    let normalized = text.Replace ("\r\n", "\n")
    let trailing = normalized.EndsWith "\n"
    let lines =
        let all = normalized.Split '\n'
        // Drop the phantom empty line produced by a trailing newline; we add
        // the newline back when rendering.
        if trailing then all.[.. all.Length - 2] else all

    let mutable depth = 0
    let mutable start = -1
    let mutable finish = -1
    for i in 0 .. lines.Length - 1 do
        let t = lines.[i].Trim ()
        if t.StartsWith "<" then
            depth <- depth + 1
            if depth = 2 && start < 0 && t.Substring(1).Trim().StartsWith blockTag then
                start <- i
        elif t = ">" then
            if start >= 0 && finish < 0 && depth = 2 && i > start then
                finish <- i
            depth <- depth - 1

    { Lines = lines
      Eol = eol
      TrailingNewline = trailing
      BlockRange = if start >= 0 && finish > start then Some (start, finish) else None }

/// Read the block's contents out of a loaded doc.
let parseBlock (doc: Doc) : Block =
    match doc.BlockRange with
    | None -> { Entries = []; PreservedLines = [] }
    | Some (s, e) ->
        let entries = ResizeArray<string * string> ()
        let preserved = ResizeArray<string> ()
        for i in s + 1 .. e - 1 do
            let raw = doc.Lines.[i]
            let tokens = RppParser.tokenize (raw.Trim ())
            if tokens.Length = 3 && tokens.[0] = "ENTRY" && RppParser.isGuidToken tokens.[2] then
                entries.Add (tokens.[1], tokens.[2])
            else
                preserved.Add raw
        { Entries = List.ofSeq entries; PreservedLines = List.ofSeq preserved }

// --- Rendering -------------------------------------------------------------------

let private entryLine (regionId: string, trackGuid: string) =
    sprintf "    ENTRY %s %s" regionId trackGuid

/// Where to insert a new block if the file has none: after the last top-level
/// MARKER line, else just before the project's closing ">" — both positions
/// REAPER accepts.
let private insertionIndex (lines: string[]) : int =
    let mutable depth = 0
    let mutable lastMarker = -1
    let mutable projectClose = lines.Length - 1
    for i in 0 .. lines.Length - 1 do
        let t = lines.[i].Trim ()
        if t.StartsWith "<" then depth <- depth + 1
        elif t = ">" then
            if depth = 1 then projectClose <- i
            depth <- depth - 1
        elif depth = 1 && t.StartsWith "MARKER " then
            lastMarker <- i
    if lastMarker >= 0 then lastMarker + 1 else projectClose

/// Produce the full new file text with the block replaced (or inserted, or
/// removed when there is nothing left to say). Everything outside the block
/// is emitted byte-for-byte from the original lines.
let render (doc: Doc) (entries: (string * string) list) (preserved: string list) : string =
    let blockLines =
        if List.isEmpty entries && List.isEmpty preserved then
            []
        else
            [ yield "  <" + blockTag
              yield! entries |> List.map entryLine
              yield! preserved
              yield "  >" ]

    let newLines =
        match doc.BlockRange with
        | Some (s, e) ->
            [ yield! doc.Lines.[.. s - 1]
              yield! blockLines
              yield! doc.Lines.[e + 1 ..] ]
        | None when not blockLines.IsEmpty ->
            let idx = insertionIndex doc.Lines
            [ yield! doc.Lines.[.. idx - 1]
              yield! blockLines
              yield! doc.Lines.[idx ..] ]
        | None -> List.ofArray doc.Lines

    let body = String.concat doc.Eol newLines
    if doc.TrailingNewline then body + doc.Eol else body
