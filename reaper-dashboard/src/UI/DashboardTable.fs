module ReaperDashboard.UI.DashboardTable

open Feliz
open ReaperDashboard.Core
open ReaperDashboard.App.State

let private sortableHeader (model: Model) (dispatch: Msg -> unit) (col: SortColumn) (label: string) =
    let (curCol, curDir) = model.Sort
    Html.th
        [ prop.className "sortable"
          prop.onClick (fun _ -> dispatch (SetSort col))
          prop.children
              [ Html.text label
                if curCol = col then
                    Html.span
                        [ prop.className "sort-arrow"
                          prop.text (if curDir = Asc then "▲" else "▼") ] ] ]

let private durationCell (p: Project) =
    match p.DurationSeconds with
    | Some d ->
        let sourceGlyph =
            match p.DurationSource with
            | FromRenderRegion -> " ⬒" // from the "render" region
            | FromAudioBounds -> " ~" // approximated from item bounds
            | UnknownDuration -> ""
        Html.td
            [ prop.className "num"
              prop.title (sprintf "Source: %s" (Format.durationSourceLabel p.DurationSource))
              prop.text (Format.duration d + sourceGlyph) ]
    | None ->
        Html.td
            [ prop.className "num"
              prop.title "No render region and no audio items found"
              prop.text "Unknown" ]

let private row (model: Model) (dispatch: Msg -> unit) (p: Project) =
    let selected = model.Selected.Contains p.Path
    Html.tr
        [ prop.key p.Path
          prop.className (if selected then "selected" else "")
          prop.onClick (fun ev ->
              dispatch (RowClicked (p.Path, ev.metaKey || ev.ctrlKey, ev.shiftKey)))
          prop.onDoubleClick (fun _ -> dispatch (OpenInReaper [ p.Path ]))
          prop.children
              [ Html.td [ prop.className "proj-name"; prop.title p.Name; prop.text p.Name ]
                Html.td [ Badges.statusPill p.Status ]
                durationCell p
                Html.td [ Badges.renderRegionCell p ]
                Html.td [ Badges.previewPill p ]
                Html.td [ Badges.matrixPill p ]
                Html.td
                    [ prop.className "num"
                      prop.text (if p.LastModifiedMs > 0.0 then Format.dateTime p.LastModifiedMs else "—") ]
                Html.td [ prop.className "num"; prop.text "—" ] // Last render — MVP 2+
                Html.td [ prop.className "path-cell"; prop.title p.Path; prop.text p.Path ] ] ]

let private toolbar (model: Model) (dispatch: Msg -> unit) =
    Html.div
        [ prop.className "toolbar"
          prop.children
              [ Html.button
                    [ prop.className "btn primary"
                      prop.text "Import Files…"
                      prop.onClick (fun _ -> dispatch ImportFilesClicked) ]
                Html.button
                    [ prop.className "btn"
                      prop.text "Import Folder…"
                      prop.onClick (fun _ -> dispatch ImportFolderClicked) ]
                Html.button
                    [ prop.className "btn"
                      prop.text (if model.Scanning.IsEmpty then "Rescan All" else sprintf "Scanning %d…" model.Scanning.Count)
                      prop.disabled (not model.Scanning.IsEmpty || model.Projects.IsEmpty)
                      prop.onClick (fun _ -> dispatch RescanAll) ]
                Html.button
                    [ prop.className "btn danger-text"
                      prop.text "Remove"
                      prop.title "Remove selected projects from the library (files on disk are untouched)"
                      prop.disabled model.Selected.IsEmpty
                      prop.onClick (fun _ -> dispatch RemoveSelected) ]
                Html.div [ prop.className "spacer" ]
                Html.input
                    [ prop.className "search-box"
                      prop.type' "text"
                      prop.placeholder "Search projects…"
                      prop.value model.Search
                      prop.onChange (fun (s: string) -> dispatch (SetSearch s)) ] ] ]

let view (model: Model) (dispatch: Msg -> unit) =
    let visible = visibleProjects model
    Html.div
        [ prop.className "main-pane"
          prop.children
              [ toolbar model dispatch
                Html.div
                    [ prop.className "table-wrap"
                      prop.children
                          [ if model.Projects.IsEmpty then
                                Html.div
                                    [ prop.className "empty-state"
                                      prop.children
                                          [ Html.div [ prop.className "big"; prop.text "No projects imported yet" ]
                                            Html.div [ prop.text "Import .rpp files or a folder to build your library." ]
                                            Html.button
                                                [ prop.className "btn primary"
                                                  prop.text "Import Files…"
                                                  prop.onClick (fun _ -> dispatch ImportFilesClicked) ] ] ]
                            elif visible.IsEmpty then
                                Html.div
                                    [ prop.className "empty-state"
                                      prop.children
                                          [ Html.div [ prop.className "big"; prop.text "No projects match" ]
                                            Html.div [ prop.text "Adjust the filter or search." ] ] ]
                            else
                                Html.table
                                    [ prop.className "dash"
                                      prop.children
                                          [ Html.thead
                                                [ Html.tr
                                                      [ sortableHeader model dispatch SortName "Project"
                                                        sortableHeader model dispatch SortStatus "Status"
                                                        sortableHeader model dispatch SortDuration "Duration"
                                                        Html.th [ prop.text "Render Region" ]
                                                        Html.th [ prop.text "Preview" ]
                                                        Html.th [ prop.text "Matrix" ]
                                                        sortableHeader model dispatch SortModified "Modified"
                                                        Html.th [ prop.text "Last Render" ]
                                                        sortableHeader model dispatch SortPath "Path" ] ]
                                            Html.tbody [ prop.children [ for p in visible -> row model dispatch p ] ] ] ] ] ] ] ]
