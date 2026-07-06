module ReaperDashboard.UI.View

open Feliz
open ReaperDashboard.App.State

let private titlebar (model: Model) (dispatch: Msg -> unit) =
    Html.div
        [ prop.className "titlebar"
          prop.children
              [ Html.span [ prop.className "app-title"; prop.text "REAPER Project Dashboard" ]
                Html.div
                    [ prop.className "titlebar-actions"
                      prop.children
                          [ Html.button
                                [ prop.className "btn small"
                                  prop.text "Settings"
                                  prop.onClick (fun _ -> dispatch OpenSettingsModal) ] ] ] ] ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div
        [ prop.className "app-shell"
          prop.children
              [ titlebar model dispatch
                Sidebar.view model dispatch
                DashboardTable.view model dispatch
                Inspector.view model dispatch
                BottomPanel.view model dispatch
                SettingsModal.view model dispatch
                MatrixEditor.view model dispatch ] ]
