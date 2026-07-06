module ReaperDashboard.Core.RppParser

/// Safe, read-only parser for REAPER .rpp project files.
///
/// The .rpp format is a line-oriented tree:
///   <REAPER_PROJECT 0.1 "7.x" 1700000000
///     TEMPO 120 4 4
///     <TRACK {GUID}
///       NAME "Drums"
///       <ITEM
///         POSITION 8
///         LENGTH 12.5
///         <SOURCE WAVE
///           FILE "Audio/drums.wav"
///         >
///       >
///     >
///     MARKER 1 0 render 1 0 1 ...
///   >
///
/// Design rules (these matter for safety):
///  * Every line is kept verbatim in the tree (`RawLine`), including lines we
///    do not understand. Nothing is normalised or discarded, so a future
///    RppWriter can round-trip the file byte-for-byte outside the specific
///    entries it edits.
///  * This module NEVER writes files. Writing (with timestamped backups and
///    validation) arrives as a separate RppWriter module in MVP 5.
///  * Extraction is best-effort: malformed lines are skipped, never fatal.

open ReaperDashboard.Core

type RppNode =
    { /// First token — block tags without the leading '<' (e.g. "TRACK").
      Tag: string
      /// All tokens including the tag, with quotes stripped.
      Tokens: string[]
      /// The original line exactly as it appeared (trimmed of indentation).
      RawLine: string
      IsBlock: bool
      Children: RppNode list }

/// Everything MVP 1 extracts from a project file.
type ParsedProject =
    { Root: RppNode
      Regions: Region list
      Markers: Marker list
      Tracks: TrackInfo list
      AudioItems: AudioItem list
      /// (source type, raw file path) — deduplicated, order preserved.
      MediaFiles: (string * string) list
      Plugins: PluginRef list
      RecordPath: string option
      /// (region id, track guid) pairs from the REGION_RENDER_MATRIX block
      /// that this app fully understands. Anything else in the block is left
      /// untouched by the editor (see RegionRenderMatrix.fs).
      MatrixEntries: (string * string) list
      /// True if the project has a REGION_RENDER_MATRIX block at all.
      MatrixBlockFound: bool }

// --- Tokenizer ---------------------------------------------------------------

/// Split one .rpp line into tokens. REAPER quotes strings with ", ' or `
/// (it picks a quote character that does not occur in the content, so there
/// are no escape sequences to handle inside a quoted token).
let tokenize (line: string) : string[] =
    let tokens = ResizeArray<string> ()
    let n = line.Length
    let mutable i = 0
    while i < n do
        // skip whitespace
        while i < n && (line.[i] = ' ' || line.[i] = '\t') do
            i <- i + 1
        if i < n then
            let c = line.[i]
            if c = '"' || c = '\'' || c = '`' then
                let quote = c
                i <- i + 1
                let start = i
                while i < n && line.[i] <> quote do
                    i <- i + 1
                tokens.Add (line.Substring (start, i - start))
                if i < n then i <- i + 1 // consume closing quote
            else
                let start = i
                while i < n && line.[i] <> ' ' && line.[i] <> '\t' do
                    i <- i + 1
                tokens.Add (line.Substring (start, i - start))
    tokens.ToArray ()

// --- Tree parser -------------------------------------------------------------

/// Parse full .rpp text into a raw tree. Returns Error with a human-readable
/// message when the block structure is unbalanced (a corrupt or truncated
/// file) — callers surface that as a Scan Failed status, never a crash.
let parse (text: string) : Result<RppNode, string> =
    let lines = text.Replace("\r\n", "\n").Split '\n'
    // Stack of open blocks: (raw line, tokens, accumulated children).
    let mutable stack: (string * string[] * ResizeArray<RppNode>) list = []
    let mutable root: RppNode option = None
    let mutable error: string option = None
    let mutable lineNo = 0

    for raw in lines do
        lineNo <- lineNo + 1
        if error.IsNone then
            let line = raw.Trim ()
            if line = "" then ()
            elif line.StartsWith "<" then
                let tokens = tokenize (line.Substring 1)
                stack <- (line, tokens, ResizeArray ()) :: stack
            elif line = ">" then
                match stack with
                | (rawLine, tokens, children) :: rest ->
                    let node =
                        { Tag = (if tokens.Length > 0 then tokens.[0] else "")
                          Tokens = tokens
                          RawLine = rawLine
                          IsBlock = true
                          Children = List.ofSeq children }
                    match rest with
                    | (_, _, parentChildren) :: _ ->
                        parentChildren.Add node
                        stack <- rest
                    | [] ->
                        if root.IsSome then
                            error <- Some (sprintf "Unexpected second top-level block at line %d" lineNo)
                        else
                            root <- Some node
                            stack <- []
                | [] ->
                    error <- Some (sprintf "Unbalanced '>' at line %d" lineNo)
            else
                let tokens = tokenize line
                let node =
                    { Tag = (if tokens.Length > 0 then tokens.[0] else "")
                      Tokens = tokens
                      RawLine = line
                      IsBlock = false
                      Children = [] }
                match stack with
                | (_, _, children) :: _ -> children.Add node
                | [] -> () // stray leaf outside any block — tolerated

    match error with
    | Some e -> Error e
    | None ->
        if not stack.IsEmpty then
            Error "File ends with an unclosed block (truncated or corrupt project?)"
        else
            match root with
            | None -> Error "No <REAPER_PROJECT block found"
            | Some r when r.Tag <> "REAPER_PROJECT" ->
                Error (sprintf "Root block is <%s, not <REAPER_PROJECT" r.Tag)
            | Some r -> Ok r

// --- Extraction helpers ------------------------------------------------------

let private tryFloat (s: string) : float option =
    match System.Double.TryParse s with
    | true, v -> Some v
    | _ -> None

let private tokenAt (node: RppNode) (i: int) : string option =
    if i < node.Tokens.Length then Some node.Tokens.[i] else None

let private floatAt (node: RppNode) (i: int) : float option =
    tokenAt node i |> Option.bind tryFloat

let private leafChild (tag: string) (node: RppNode) : RppNode option =
    node.Children |> List.tryFind (fun c -> not c.IsBlock && c.Tag = tag)

// --- Regions & markers -------------------------------------------------------

// Top-level MARKER lines: MARKER <id> <pos> <name> <isRegion> <color> ...
// A region is stored as TWO lines with the same id and isRegion=1:
// the first carries the name and start position, the second the end position.
let extractRegionsAndMarkers (root: RppNode) : Region list * Marker list =
    let regions = ResizeArray<Region> ()
    let markers = ResizeArray<Marker> ()
    let pending = System.Collections.Generic.Dictionary<string, string * float * int option> ()

    for node in root.Children do
        if not node.IsBlock && node.Tag = "MARKER" && node.Tokens.Length >= 5 then
            let id = node.Tokens.[1]
            let pos = floatAt node 2 |> Option.defaultValue 0.0
            let name = node.Tokens.[3]
            let isRegion = node.Tokens.[4] = "1"
            let color = floatAt node 5 |> Option.map int
            if isRegion then
                match pending.TryGetValue id with
                | true, (startName, startPos, startColor) ->
                    pending.Remove id |> ignore
                    regions.Add
                        { Id = id
                          Name = startName
                          Start = startPos
                          End = pos
                          Color = startColor }
                | _ -> pending.[id] <- (name, pos, color)
            else
                markers.Add { Name = name; Position = pos }

    // Unpaired region-start lines (no matching end) become zero-length
    // regions rather than being silently dropped.
    for kv in pending do
        let (name, pos, color) = kv.Value
        regions.Add { Id = kv.Key; Name = name; Start = pos; End = pos; Color = color }

    List.ofSeq regions, List.ofSeq markers

// --- Tracks, items and media -------------------------------------------------

/// Source types that produce audio on the timeline. MIDI/EMPTY/CLICK do not.
/// RPP_PROJECT is a subproject, which renders audio. Best-effort list; unknown
/// types are treated as non-audio but their FILE refs are still checked.
let private audioSourceTypes =
    Set.ofList
        [ "WAVE"; "AIFF"; "FLAC"; "MP3"; "OGG"; "VORBIS"; "OPUS"
          "WV"; "WAVPACK"; "CAF"; "FFMPEG"; "DS64"; "RPP_PROJECT" ]

/// All <SOURCE ...> blocks under a node, including sources nested inside
/// SECTION/reversed sources.
let rec private collectSources (node: RppNode) : RppNode list =
    node.Children
    |> List.collect (fun c ->
        if c.IsBlock && c.Tag = "SOURCE" then c :: collectSources c
        elif c.IsBlock then collectSources c
        else [])

let private sourceType (src: RppNode) : string =
    tokenAt src 1 |> Option.defaultValue ""

let private sourceFiles (src: RppNode) : string list =
    src.Children
    |> List.choose (fun c ->
        if not c.IsBlock && c.Tag = "FILE" then tokenAt c 1 else None)

let extractTracks (root: RppNode) : TrackInfo list * AudioItem list * (string * string) list =
    let tracks = ResizeArray<TrackInfo> ()
    let audioItems = ResizeArray<AudioItem> ()
    let mediaFiles = ResizeArray<string * string> ()
    let seenMedia = System.Collections.Generic.HashSet<string> ()

    for trackNode in root.Children do
        if trackNode.IsBlock && trackNode.Tag = "TRACK" then
            let trackGuid = tokenAt trackNode 1 |> Option.defaultValue ""
            let name =
                leafChild "NAME" trackNode
                |> Option.bind (fun n -> tokenAt n 1)
                |> Option.defaultValue ""
            let color =
                leafChild "PEAKCOL" trackNode
                |> Option.bind (fun n -> floatAt n 1)
                |> Option.map int
            let items =
                trackNode.Children |> List.filter (fun c -> c.IsBlock && c.Tag = "ITEM")

            for item in items do
                let pos = leafChild "POSITION" item |> Option.bind (fun n -> floatAt n 1)
                let len = leafChild "LENGTH" item |> Option.bind (fun n -> floatAt n 1)
                let sources = collectSources item
                let types = sources |> List.map sourceType

                for src in sources do
                    let st = sourceType src
                    for file in sourceFiles src do
                        if seenMedia.Add file then
                            mediaFiles.Add (st, file)

                let isAudio = types |> List.exists audioSourceTypes.Contains
                match pos, len with
                | Some p, Some l when isAudio ->
                    audioItems.Add { Position = p; Length = l; SourceTypes = types }
                | _ -> ()

            tracks.Add { Guid = trackGuid; Name = name; Color = color; ItemCount = items.Length }

    List.ofSeq tracks, List.ofSeq audioItems, List.ofSeq mediaFiles

// --- Plugins (best effort) -----------------------------------------------------

let private pluginTags = Set.ofList [ "VST"; "AU"; "JS"; "CLAP"; "DX"; "LV2" ]

/// FX references anywhere in the project (track FX chains, item take FX).
/// This only reads the display name; validating against installed plugins is
/// explicitly best-effort and arrives with the full WarningScanner stage.
let rec private collectPlugins (node: RppNode) : PluginRef list =
    node.Children
    |> List.collect (fun c ->
        if c.IsBlock then
            let here =
                if pluginTags.Contains c.Tag then
                    match tokenAt c 1 with
                    | Some name when name <> "" -> [ { Kind = c.Tag; Name = name } ]
                    | _ -> []
                else []
            here @ collectPlugins c
        else [])

// --- Region Render Matrix (read side) --------------------------------------------

/// Looks like a REAPER GUID token: {XXXXXXXX-....}
let isGuidToken (s: string) =
    s.Length >= 2 && s.StartsWith "{" && s.EndsWith "}"

/// ENTRY lines we fully understand: `ENTRY <regionId> <trackGuid>` and
/// nothing else. Any other shape is intentionally NOT surfaced here so the
/// editor treats it as opaque data to preserve.
let private extractMatrix (root: RppNode) : (string * string) list * bool =
    match root.Children |> List.tryFind (fun c -> c.IsBlock && c.Tag = "REGION_RENDER_MATRIX") with
    | None -> [], false
    | Some block ->
        let entries =
            block.Children
            |> List.choose (fun c ->
                if not c.IsBlock && c.Tag = "ENTRY" && c.Tokens.Length = 3 && isGuidToken c.Tokens.[2] then
                    Some (c.Tokens.[1], c.Tokens.[2])
                else
                    None)
        entries, true

// --- Top-level extraction ------------------------------------------------------

let private extractRecordPath (root: RppNode) : string option =
    leafChild "RECORD_PATH" root
    |> Option.bind (fun n -> tokenAt n 1)
    |> Option.filter (fun p -> p.Trim () <> "")

/// Run all extractors over a parsed tree.
let extract (root: RppNode) : ParsedProject =
    let regions, markers = extractRegionsAndMarkers root
    let tracks, audioItems, mediaFiles = extractTracks root
    let plugins = collectPlugins root |> List.distinctBy (fun p -> p.Kind, p.Name)
    let matrixEntries, matrixFound = extractMatrix root
    { Root = root
      Regions = regions
      Markers = markers
      Tracks = tracks
      AudioItems = audioItems
      MediaFiles = mediaFiles
      Plugins = plugins
      RecordPath = extractRecordPath root
      MatrixEntries = matrixEntries
      MatrixBlockFound = matrixFound }
