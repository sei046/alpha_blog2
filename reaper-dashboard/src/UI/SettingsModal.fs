module ReaperDashboard.UI.SettingsModal

open Feliz
open ReaperDashboard.App.State

let view (model: Model) (dispatch: Msg -> unit) =
    if not model.ShowSettings then
        Html.none
    else
        Html.div
            [ prop.className "modal-backdrop"
              prop.onClick (fun _ -> dispatch CloseSettingsModal)
              prop.children
                  [ Html.div
                        [ prop.className "modal"
                          // keep clicks inside the dialog from closing it
                          prop.onClick (fun ev -> ev.stopPropagation ())
                          prop.children
                              [ Html.h2 [ prop.text "Settings" ]
                                Html.div
                                    [ prop.className "field"
                                      prop.children
                                          [ Html.label [ prop.text "REAPER application" ]
                                            Html.div
                                                [ prop.className "field-row"
                                                  prop.children
                                                      [ Html.input
                                                            [ prop.type' "text"
                                                              prop.value model.SettingsDraft.ReaperAppPath
                                                              prop.onChange (fun (s: string) -> dispatch (SetDraftReaperPath s)) ]
                                                        Html.button
                                                            [ prop.className "btn"
                                                              prop.text "Browse…"
                                                              prop.onClick (fun _ -> dispatch BrowseReaperApp) ] ] ]
                                            Html.div
                                                [ prop.className "hint"
                                                  prop.text "Default: /Applications/REAPER.app — projects are opened with `open -a` so a running REAPER instance is reused." ] ] ]
                                Html.div
                                    [ prop.className "field"
                                      prop.children
                                          [ Html.label [ prop.text "Preview render folder" ]
                                            Html.div
                                                [ prop.className "field-row"
                                                  prop.children
                                                      [ Html.input
                                                            [ prop.type' "text"
                                                              prop.placeholder "(default: app data / previews)"
                                                              prop.value model.SettingsDraft.PreviewFolder
                                                              prop.onChange (fun (s: string) -> dispatch (SetDraftPreviewFolder s)) ]
                                                        Html.button
                                                            [ prop.className "btn"
                                                              prop.text "Browse…"
                                                              prop.onClick (fun _ -> dispatch BrowsePreviewFolder) ] ] ]
                                            Html.div
                                                [ prop.className "hint"
                                                  prop.text "Where rendered previews and their manifests are stored. Leave blank to use the app's data folder." ] ] ]
                                Html.div
                                    [ prop.className "field"
                                      prop.children
                                          [ Html.label [ prop.text "WAV bounce folder" ]
                                            Html.div
                                                [ prop.className "field-row"
                                                  prop.children
                                                      [ Html.input
                                                            [ prop.type' "text"
                                                              prop.placeholder "(default: alongside each project)"
                                                              prop.value model.SettingsDraft.WavBounceFolder
                                                              prop.onChange (fun (s: string) -> dispatch (SetDraftWavFolder s)) ]
                                                        Html.button
                                                            [ prop.className "btn"
                                                              prop.text "Browse…"
                                                              prop.onClick (fun _ -> dispatch BrowseWavFolder) ] ] ]
                                            Html.div
                                                [ prop.className "hint"
                                                  prop.text "Where WAV bounces are written. Leave blank to write next to each .rpp." ] ] ]
                                Html.div
                                    [ prop.className "field"
                                      prop.children
                                          [ Html.label [ prop.text "WAV filename pattern" ]
                                            Html.div
                                                [ prop.className "field-row"
                                                  prop.children
                                                      [ Html.input
                                                            [ prop.type' "text"
                                                              prop.placeholder "$project"
                                                              prop.value model.SettingsDraft.WavPattern
                                                              prop.onChange (fun (s: string) -> dispatch (SetDraftWavPattern s)) ] ] ]
                                            Html.div
                                                [ prop.className "hint"
                                                  prop.text "Bounce filename. $project is replaced with the project name; .wav is added automatically." ] ] ]
                                Html.div
                                    [ prop.className "modal-actions"
                                      prop.children
                                          [ Html.button
                                                [ prop.className "btn"
                                                  prop.text "Cancel"
                                                  prop.onClick (fun _ -> dispatch CloseSettingsModal) ]
                                            Html.button
                                                [ prop.className "btn primary"
                                                  prop.text "Save"
                                                  prop.onClick (fun _ -> dispatch SaveSettingsModal) ] ] ] ] ] ] ]
