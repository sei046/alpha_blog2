module ReaperDashboard.UI.MatrixEditor

/// Full-screen Region Render Matrix editor: regions as rows, tracks as
/// columns. Click a cell to toggle it, drag to paint a range, click a
/// region/track header to toggle the whole lane. Saving always goes through
/// the dry-run diff, then RppWriter's backup + validation.

open Feliz
open ReaperDashboard.Core
open ReaperDashboard.App.State

let private colorChip (color: int option) =
    Html.span
        [ prop.className "chip"
          prop.style
              [ style.backgroundColor (
                    match color with
                    | Some c -> Format.reaperColor c
                    | None -> "var(--border-strong)"
                ) ] ]

let private trackDisplayName (i: int) (t: TrackInfo) =
    if t.Name = "" then sprintf "Track %d" (i + 1) else t.Name

// --- Dry-run diff modal --------------------------------------------------------

let private diffModal (ed: MatrixEditor) (dispatch: Msg -> unit) =
    let regionName rid =
        ed.Regions
        |> List.tryFind (fun r -> r.Id = rid)
        |> Option.map (fun r -> r.Name)
        |> Option.defaultValue ("region " + rid)
    let trackName guid =
        ed.Tracks
        |> List.tryPick (fun t -> if t.Guid = guid then Some t.Name else None)
        |> Option.defaultValue guid

    let renderChangeList (title: string) (cls: string) (sign: string) (cells: Set<MatrixCell>) =
        Html.div
            [ prop.className "diff-section"
              prop.children
                  [ Html.div [ prop.className "insp-heading"; prop.text (sprintf "%s (%d)" title cells.Count) ]
                    if cells.IsEmpty then
                        Html.div [ prop.className "diff-empty"; prop.text "None" ]
                    else
                        Html.div
                            [ prop.className "diff-lines"
                              prop.children
                                  [ for (rid, guid) in Set.toList cells ->
                                        Html.div
                                            [ prop.className ("diff-line " + cls)
                                              prop.children
                                                  [ Html.span [ prop.className "sign"; prop.text sign ]
                                                    Html.text (sprintf "%s × %s" (regionName rid) (trackName guid))
                                                    Html.span
                                                        [ prop.className "diff-raw"
                                                          prop.text (sprintf "ENTRY %s %s" rid guid) ] ] ] ] ] ] ]

    Html.div
        [ prop.className "modal-backdrop"
          prop.onClick (fun _ -> dispatch (MatrixShowDiff false))
          prop.children
              [ Html.div
                    [ prop.className "modal diff-modal"
                      prop.onClick (fun ev -> ev.stopPropagation ())
                      prop.children
                          [ Html.h2 [ prop.text "Review matrix changes" ]
                            Html.div
                                [ prop.className "diff-intro"
                                  prop.text
                                      (sprintf
                                          "Each enabled region × track cell renders as its own stem via REAPER's Region Render Matrix. Only the lines below change in %s.rpp — everything else is written back byte-for-byte."
                                          ed.ProjectName) ]
                            renderChangeList "Will enable" "add" "+" (Matrix.added ed)
                            renderChangeList "Will disable" "del" "−" (Matrix.removed ed)
                            if not ed.Preserved.IsEmpty then
                                Html.div
                                    [ prop.className "diff-note"
                                      prop.text
                                          (sprintf
                                              "%d other line(s) in the matrix block aren't understood by this app and will be preserved exactly as they are."
                                              ed.Preserved.Length) ]
                            Html.div
                                [ prop.className "diff-note"
                                  prop.text "A timestamped backup of the current file is written and verified before anything is replaced; the result is re-parsed before it's accepted." ]
                            Html.div
                                [ prop.className "modal-actions"
                                  prop.children
                                      [ Html.button
                                            [ prop.className "btn"
                                              prop.text "Back"
                                              prop.disabled ed.Saving
                                              prop.onClick (fun _ -> dispatch (MatrixShowDiff false)) ]
                                        Html.button
                                            [ prop.className "btn primary"
                                              prop.text (if ed.Saving then "Saving…" else "Save with backup")
                                              prop.disabled ed.Saving
                                              prop.onClick (fun _ -> dispatch MatrixSaveConfirmed) ] ] ] ] ] ] ]

// --- The grid --------------------------------------------------------------------

let private grid (ed: MatrixEditor) (dispatch: Msg -> unit) =
    let regions = Matrix.visibleRegions ed
    let tracks = Matrix.visibleTracks ed

    Html.div
        [ prop.className "matrix-grid-wrap"
          // End drag-painting whenever the mouse is released or leaves the grid.
          prop.onMouseUp (fun _ -> dispatch MatrixPaintEnd)
          prop.onMouseLeave (fun _ -> dispatch MatrixPaintEnd)
          prop.children
              [ if ed.Regions.IsEmpty then
                    Html.div
                        [ prop.className "matrix-empty"
                          prop.text "This project has no regions. Add regions in REAPER (or a \"render\" region) to build a render matrix." ]
                elif tracks.IsEmpty && ed.Tracks.IsEmpty then
                    Html.div [ prop.className "matrix-empty"; prop.text "This project has no tracks." ]
                else
                    Html.table
                        [ prop.className "matrix"
                          prop.children
                              [ Html.thead
                                    [ Html.tr
                                          [ yield Html.th
                                                [ prop.className "corner"
                                                  prop.text "Region \\ Track" ]
                                            for (i, t) in List.indexed tracks do
                                                yield Html.th
                                                    [ prop.key t.Guid
                                                      prop.className "track-head"
                                                      prop.title (sprintf "%s — click to toggle whole column" (trackDisplayName i t))
                                                      prop.onClick (fun _ -> dispatch (MatrixToggleTrack t.Guid))
                                                      prop.children
                                                          [ colorChip t.Color
                                                            Html.span [ prop.className "track-head-name"; prop.text (trackDisplayName i t) ] ] ] ] ]
                                Html.tbody
                                    [ prop.children
                                          [ for r in regions ->
                                                Html.tr
                                                    [ prop.key r.Id
                                                      prop.children
                                                          [ yield Html.th
                                                                [ prop.className "region-head"
                                                                  prop.title "Click to toggle whole row"
                                                                  prop.onClick (fun _ -> dispatch (MatrixToggleRegion r.Id))
                                                                  prop.children
                                                                      [ colorChip r.Color
                                                                        Html.span [ prop.className "region-name"; prop.text (if r.Name = "" then "(unnamed)" else r.Name) ]
                                                                        Html.span [ prop.className "region-len"; prop.text (Format.duration (r.End - r.Start)) ] ] ]
                                                            for t in tracks do
                                                                let cell = (r.Id, t.Guid)
                                                                let on = ed.Current.Contains cell
                                                                let changed = ed.Current.Contains cell <> ed.Original.Contains cell
                                                                yield Html.td
                                                                    [ prop.key (r.Id + t.Guid)
                                                                      prop.className
                                                                          ("mcell"
                                                                           + (if on then " on" else "")
                                                                           + (if changed then " changed" else ""))
                                                                      prop.onMouseDown (fun ev ->
                                                                          ev.preventDefault ()
                                                                          dispatch (MatrixPaintStart cell))
                                                                      prop.onMouseEnter (fun _ -> dispatch (MatrixPaintOver cell))
                                                                      prop.children [ Html.span [ prop.className "mdot" ] ] ] ] ] ] ] ] ] ] ]

// --- Editor shell ------------------------------------------------------------------

let view (model: Model) (dispatch: Msg -> unit) =
    match model.MatrixEditor with
    | None -> Html.none
    | Some ed ->
        let addedCount = (Matrix.added ed).Count
        let removedCount = (Matrix.removed ed).Count
        Html.div
            [ prop.className "matrix-overlay"
              prop.children
                  [ Html.div
                        [ prop.className "matrix-window"
                          prop.children
                              [ Html.div
                                    [ prop.className "matrix-header"
                                      prop.children
                                          [ Html.div
                                                [ prop.children
                                                      [ Html.div
                                                            [ prop.className "insp-title"
                                                              prop.text (sprintf "Region Render Matrix — %s" ed.ProjectName) ]
                                                        Html.div [ prop.className "insp-sub"; prop.text ed.ProjectPath ] ] ]
                                            Html.button
                                                [ prop.className "btn"
                                                  prop.text "Close"
                                                  prop.onClick (fun _ -> dispatch MatrixCancel) ] ] ]
                                Html.div
                                    [ prop.className "matrix-toolbar"
                                      prop.children
                                          [ Html.input
                                                [ prop.className "search-box"
                                                  prop.type' "text"
                                                  prop.placeholder "Filter regions…"
                                                  prop.value ed.RegionFilter
                                                  prop.onChange (fun (s: string) -> dispatch (MatrixSetRegionFilter s)) ]
                                            Html.input
                                                [ prop.className "search-box"
                                                  prop.type' "text"
                                                  prop.placeholder "Filter tracks…"
                                                  prop.value ed.TrackFilter
                                                  prop.onChange (fun (s: string) -> dispatch (MatrixSetTrackFilter s)) ]
                                            Html.button
                                                [ prop.className "btn small"
                                                  prop.text "Enable all shown"
                                                  prop.onClick (fun _ -> dispatch MatrixEnableAllVisible) ]
                                            Html.button
                                                [ prop.className "btn small"
                                                  prop.text "Clear all shown"
                                                  prop.onClick (fun _ -> dispatch MatrixClearAllVisible) ]
                                            Html.div [ prop.className "spacer" ]
                                            Html.span
                                                [ prop.className "matrix-count"
                                                  prop.text
                                                      (sprintf "%d assignment(s)%s" ed.Current.Count
                                                          (if Matrix.isDirty ed then sprintf "  ·  +%d / −%d pending" addedCount removedCount
                                                           else "")) ] ] ]
                                grid ed dispatch
                                Html.div
                                    [ prop.className "matrix-footer"
                                      prop.children
                                          [ Html.span
                                                [ prop.className "matrix-safety-note"
                                                  prop.text "Click cells to toggle · drag to paint · click a region/track name to toggle the lane. Saving creates a timestamped backup and validates the file first." ]
                                            Html.div [ prop.className "spacer" ]
                                            Html.button
                                                [ prop.className "btn"
                                                  prop.text "Revert"
                                                  prop.disabled (not (Matrix.isDirty ed))
                                                  prop.onClick (fun _ -> dispatch MatrixRevert) ]
                                            Html.button
                                                [ prop.className "btn"
                                                  prop.text "Cancel"
                                                  prop.onClick (fun _ -> dispatch MatrixCancel) ]
                                            Html.button
                                                [ prop.className "btn primary"
                                                  prop.text "Review & Save…"
                                                  prop.disabled (not (Matrix.isDirty ed))
                                                  prop.onClick (fun _ -> dispatch (MatrixShowDiff true)) ] ] ] ] ]
                    if ed.ShowDiff then diffModal ed dispatch ] ]
