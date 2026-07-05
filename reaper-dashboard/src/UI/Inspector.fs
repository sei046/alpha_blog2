module ReaperDashboard.UI.Inspector

open Feliz
open ReaperDashboard.Core
open ReaperDashboard.App.State

let private kv (k: string) (v: string) =
    Html.div
        [ prop.className "kv-row"
          prop.children
              [ Html.span [ prop.className "k"; prop.text k ]
                Html.span [ prop.className "v"; prop.text v ] ] ]

let private heading (text: string) =
    Html.div [ prop.className "insp-heading"; prop.text text ]

let private actionButton (label: string) (enabled: bool) (tooltip: string) (onClick: unit -> unit) =
    Html.button
        [ prop.className "btn block"
          prop.text label
          prop.disabled (not enabled)
          prop.title tooltip
          prop.onClick (fun _ -> onClick ()) ]

let private warningList (p: Project) =
    if p.Warnings.IsEmpty then
        Html.div
            [ prop.className "kv-row"
              prop.children [ Html.span [ prop.className "k"; prop.text "No warnings" ] ] ]
    else
        Html.div
            [ prop.children
                  [ for w in p.Warnings ->
                        Html.div
                            [ prop.className "warning-item"
                              prop.children
                                  [ Badges.severityDot w.Severity
                                    Html.div
                                        [ prop.children
                                              [ Html.text w.Message
                                                match w.Detail with
                                                | Some d -> Html.span [ prop.className "w-detail"; prop.text d ]
                                                | None -> Html.none ] ] ] ] ] ]

let private singleProject (p: Project) (dispatch: Msg -> unit) =
    Html.div
        [ prop.children
              [ Html.div [ prop.className "insp-title"; prop.text p.Name ]
                Html.div [ prop.className "insp-sub"; prop.text p.Path ]
                Badges.statusPill p.Status

                Html.div
                    [ prop.className "insp-section"
                      prop.children
                          [ heading "Details"
                            kv "Duration"
                                (match p.DurationSeconds with
                                 | Some d -> Format.duration d
                                 | None -> "Unknown")
                            kv "Duration source" (Format.durationSourceLabel p.DurationSource)
                            kv "Modified" (if p.LastModifiedMs > 0.0 then Format.dateTime p.LastModifiedMs else "—")
                            kv "File size" (Format.fileSize p.FileSizeBytes)
                            kv "Tracks" (string p.Tracks.Length)
                            kv "Audio items" (string p.AudioItems.Length)
                            kv "Regions" (string p.Regions.Length)
                            kv "Markers" (string p.Markers.Length)
                            kv "Media files" (string p.MediaRefs.Length)
                            kv "Plugin refs" (string p.Plugins.Length) ] ]

                Html.div
                    [ prop.className "insp-section"
                      prop.children [ heading "Warnings"; warningList p ] ]

                Html.div
                    [ prop.className "insp-section"
                      prop.children
                          [ heading "Actions"
                            Html.div
                                [ prop.className "insp-actions"
                                  prop.children
                                      [ actionButton "Open in REAPER" true "Open this project in REAPER"
                                            (fun () -> dispatch (OpenInReaper [ p.Path ]))
                                        actionButton "Rescan" true "Re-parse this project from disk"
                                            (fun () -> dispatch (RescanProjects [ p.Path ]))
                                        actionButton "Render MP3 Preview" false "Coming in MVP 2 — preview rendering" ignore
                                        actionButton "Bounce WAV" false "Coming in MVP 3 — WAV bouncing & render queue" ignore
                                        actionButton "Render Region Matrix" false "Coming in MVP 4 — Region Render Matrix" ignore
                                        actionButton "Edit Matrix" false "Coming in MVP 5 — visual matrix editor" ignore ] ] ] ] ] ]

let private multiProjects (paths: string list) (dispatch: Msg -> unit) =
    Html.div
        [ prop.children
              [ Html.div [ prop.className "insp-title"; prop.text (sprintf "%d projects selected" paths.Length) ]
                Html.div
                    [ prop.className "multi-note"
                      prop.text "Batch preview rendering and WAV bouncing arrive in MVP 2/3." ]
                Html.div
                    [ prop.className "insp-actions"
                      prop.children
                          [ actionButton
                                (sprintf "Open %d projects in REAPER" paths.Length)
                                (paths.Length <= 8)
                                (if paths.Length <= 8 then "Open all selected projects"
                                 else "Refusing to open more than 8 projects at once")
                                (fun () -> dispatch (OpenInReaper paths))
                            actionButton "Clear selection" true "" (fun () -> dispatch ClearSelection) ] ] ] ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div
        [ prop.className "inspector"
          prop.children
              [ match model.Selected.Count with
                | 0 ->
                    Html.div [ prop.className "insp-empty"; prop.text "Select a project to inspect it" ]
                | 1 ->
                    let path = model.Selected.MinimumElement
                    match model.Projects.TryFind path with
                    | Some p -> singleProject p dispatch
                    | None -> Html.div [ prop.className "insp-empty"; prop.text "Project not found" ]
                | _ ->
                    multiProjects (Set.toList model.Selected) dispatch ] ]
