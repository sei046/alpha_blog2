module ReaperDashboard.UI.BottomPanel

/// Bottom strip: the audio preview player, the render queue, and the activity
/// log / render report.

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

// --- Render queue ----------------------------------------------------------------

let private jobStatusPill (status: RenderJobStatus) =
    let cls, label =
        match status with
        | JobQueued -> "neutral", "Queued"
        | JobRunning -> "info", "Rendering…"
        | JobDone _ -> "ok", "Done"
        | JobFailed _ -> "error", "Failed"
    Html.span [ prop.className ("pill " + cls); prop.text label ]

let private queuePane (model: Model) (dispatch: Msg -> unit) =
    let active = model.Queue |> List.filter Queue.isActive |> List.length
    Html.div
        [ prop.className "queue-pane"
          prop.children
              [ Html.div
                    [ prop.className "queue-head"
                      prop.children
                          [ Html.span
                                [ prop.className "pane-heading"
                                  prop.text (if active > 0 then sprintf "Render Queue · %d active" active else "Render Queue") ]
                            if Queue.hasFinished model then
                                Html.button
                                    [ prop.className "btn small"
                                      prop.text "Clear finished"
                                      prop.onClick (fun _ -> dispatch ClearFinishedJobs) ]
                            else Html.none ] ]
                if model.Queue.IsEmpty then
                    Html.div [ prop.className "queue-empty"; prop.text "No renders queued." ]
                else
                    Html.div
                        [ prop.className "queue-list"
                          prop.children
                              [ for j in List.rev model.Queue ->
                                    Html.div
                                        [ prop.key (string j.Id)
                                          prop.className "queue-item"
                                          prop.children
                                              [ Html.span [ prop.className "queue-kind"; prop.text (Queue.kindLabel j.Kind) ]
                                                Html.span [ prop.className "queue-name"; prop.title j.Name; prop.text j.Name ]
                                                match j.Status with
                                                | JobFailed reason ->
                                                    Html.span [ prop.className "queue-reason"; prop.title reason; prop.text reason ]
                                                | _ -> Html.none
                                                jobStatusPill j.Status ] ] ] ] ] ]

let private logPane (model: Model) =
    Html.div
        [ prop.className "log-pane"
          prop.children
              [ Html.div [ prop.className "pane-heading"; prop.text "Activity Log" ]
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
          prop.children [ playerPane model dispatch; queuePane model dispatch; logPane model ] ]
