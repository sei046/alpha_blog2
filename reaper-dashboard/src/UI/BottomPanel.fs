module ReaperDashboard.UI.BottomPanel

/// Bottom strip: the audio preview player (a real HTML5 <audio> element wired
/// to the currently-playing project) and the activity log / render queue.

open Feliz
open ReaperDashboard.Core
open ReaperDashboard.App.State

let private levelClass =
    function
    | LogInfo -> "info"
    | LogSuccess -> "success"
    | LogWarning -> "warning"
    | LogError -> "error"

let private playerPane (model: Model) (dispatch: Msg -> unit) =
    // The project being auditioned, and its on-disk preview file (if any).
    let playing =
        model.NowPlaying
        |> Option.bind model.Projects.TryFind
        |> Option.filter (fun p -> p.PreviewPath.IsSome)

    Html.div
        [ prop.className "player-pane"
          prop.children
              [ Html.div [ prop.className "pane-heading"; prop.text "Audio Preview" ]
                match playing with
                | Some p ->
                    Html.div
                        [ prop.className "player-active"
                          prop.children
                              [ Html.div
                                    [ prop.className "player-track"
                                      prop.children
                                          [ Html.span [ prop.className "player-name"; prop.text p.Name ]
                                            Html.button
                                                [ prop.className "btn small"
                                                  prop.text "Stop"
                                                  prop.onClick (fun _ -> dispatch StopPreview) ] ] ]
                                Html.audio
                                    [ prop.className "player-audio"
                                      prop.controls true
                                      prop.autoPlay true
                                      // file:// src so Electron plays the local render directly.
                                      prop.src ("file://" + (p.PreviewPath |> Option.defaultValue "")) ]
                                match p.PreviewStatus with
                                | PreviewStale reason ->
                                    Html.div
                                        [ prop.className "player-note warn"
                                          prop.text (sprintf "Heads up: this preview is stale (%s)." (Project.staleReasonLabel reason)) ]
                                | _ -> Html.none ] ]
                | None ->
                    Html.div
                        [ prop.className "player-placeholder"
                          prop.text "Select a project with a rendered preview and press Play to audition it here — no need to open REAPER." ] ] ]

let private logPane (model: Model) =
    let heading =
        if model.Rendering.IsEmpty then "Activity Log"
        else sprintf "Activity Log · %d render(s) in progress" model.Rendering.Count
    Html.div
        [ prop.className "log-pane"
          prop.children
              [ Html.div [ prop.className "pane-heading"; prop.text heading ]
                Html.div
                    [ prop.className "log-scroll"
                      prop.children
                          [ for e in model.Log ->
                                Html.div
                                    [ prop.className ("log-line " + levelClass e.Level)
                                      prop.children
                                          [ Html.span [ prop.className "t"; prop.text (Format.timeOfDay e.TimeMs) ]
                                            Html.text e.Message ] ] ] ] ] ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div
        [ prop.className "bottom-panel"
          prop.children [ playerPane model dispatch; logPane model ] ]
