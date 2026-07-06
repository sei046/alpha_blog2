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

let private singleProject (model: Model) (p: Project) (dispatch: Msg -> unit) =
    let isRendering = Queue.pathBusy model p.Path
    let hasPreview = p.PreviewPath.IsSome
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
                                        if hasPreview then
                                            actionButton "▶ Play Preview" true "Audition the rendered preview"
                                                (fun () -> dispatch (PlayPreview p.Path))
                                        actionButton
                                            (if isRendering then "Rendering…"
                                             elif Project.previewIsStale p then "Re-render Preview"
                                             else "Render Preview")
                                            (not isRendering) "Render an audio preview with REAPER (uses the project's render format)"
                                            (fun () -> dispatch (RenderPreview [ p.Path ]))
                                        actionButton "Rescan" true "Re-parse this project from disk"
                                            (fun () -> dispatch (RescanProjects [ p.Path ]))
                                        actionButton "Bounce WAV" (not isRendering)
                                            "Bounce a WAV via REAPER (render region if present, else full bounds)"
                                            (fun () -> dispatch (BounceWav [ p.Path ]))
                                        actionButton
                                            (match p.MatrixState with MatrixAssigned n -> sprintf "Render Matrix Stems (%d)" n | _ -> "Render Matrix Stems")
                                            (not isRendering && (match p.MatrixState with MatrixAssigned _ -> true | _ -> false))
                                            "Render the Region Render Matrix stems via REAPER (best-effort — verify output on first use)"
                                            (fun () -> dispatch (RenderMatrix p.Path))
                                        actionButton "Edit Matrix" true "Edit which region × track combinations render as stems"
                                            (fun () -> dispatch (OpenMatrixEditor p.Path)) ] ] ] ] ] ]

let private multiProjects (model: Model) (paths: string list) (dispatch: Msg -> unit) =
    ignore model
    Html.div
        [ prop.children
              [ Html.div [ prop.className "insp-title"; prop.text (sprintf "%d projects selected" paths.Length) ]
                Html.div
                    [ prop.className "multi-note"
                      prop.text "Batch-render previews or bounce WAVs for the whole selection — jobs run one at a time in the render queue." ]
                Html.div
                    [ prop.className "insp-actions"
                      prop.children
                          [ actionButton
                                (sprintf "Render %d Previews" paths.Length)
                                true "Queue an audio preview for each selected project"
                                (fun () -> dispatch (RenderPreview paths))
                            actionButton
                                (sprintf "Bounce %d WAVs" paths.Length)
                                true "Queue a WAV bounce for each selected project"
                                (fun () -> dispatch (BounceWav paths))
                            actionButton
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
                    | Some p -> singleProject model p dispatch
                    | None -> Html.div [ prop.className "insp-empty"; prop.text "Project not found" ]
                | _ ->
                    multiProjects model (Set.toList model.Selected) dispatch ] ]
