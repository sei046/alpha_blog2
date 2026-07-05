module ReaperDashboard.UI.BottomPanel

/// Bottom strip: audio preview player (placeholder until MVP 2) and the
/// activity log (grows into the render queue in MVP 3).

open Feliz
open ReaperDashboard.Core
open ReaperDashboard.App.State

let private levelClass =
    function
    | LogInfo -> "info"
    | LogSuccess -> "success"
    | LogWarning -> "warning"
    | LogError -> "error"

let view (model: Model) (_dispatch: Msg -> unit) =
    Html.div
        [ prop.className "bottom-panel"
          prop.children
              [ Html.div
                    [ prop.className "player-pane"
                      prop.children
                          [ Html.div [ prop.className "pane-heading"; prop.text "Audio Preview" ]
                            Html.div
                                [ prop.className "player-placeholder"
                                  prop.text "MP3 preview player arrives in MVP 2 — select a project with a rendered preview to audition it here." ] ] ]
                Html.div
                    [ prop.className "log-pane"
                      prop.children
                          [ Html.div
                                [ prop.className "pane-heading"
                                  prop.text "Activity Log · render queue arrives in MVP 3" ]
                            Html.div
                                [ prop.className "log-scroll"
                                  prop.children
                                      [ for e in model.Log ->
                                            Html.div
                                                [ prop.className ("log-line " + levelClass e.Level)
                                                  prop.children
                                                      [ Html.span [ prop.className "t"; prop.text (Format.timeOfDay e.TimeMs) ]
                                                        Html.text e.Message ] ] ] ] ] ] ] ]
