module ReaperDashboard.UI.Sidebar

open Feliz
open ReaperDashboard.Core
open ReaperDashboard.App.State

let private sideItem (active: bool) (label: string) (count: int option) (dotColor: string option) (onClick: unit -> unit) =
    Html.div
        [ prop.className (if active then "side-item active" else "side-item")
          prop.onClick (fun _ -> onClick ())
          prop.children
              [ match dotColor with
                | Some c ->
                    Html.span [ prop.className "dot"; prop.style [ style.backgroundColor c ] ]
                | None -> Html.none
                Html.span [ prop.className "folder-name"; prop.text label ]
                match count with
                | Some n -> Html.span [ prop.className "count"; prop.text (string n) ]
                | None -> Html.none ] ]

let view (model: Model) (dispatch: Msg -> unit) =
    let all = model.Projects |> Map.toList |> List.map snd
    let countWhere f = all |> List.filter f |> List.length

    Html.div
        [ prop.className "sidebar"
          prop.children
              [ Html.div
                    [ prop.className "side-section"
                      prop.children
                          [ Html.div [ prop.className "side-heading"; prop.text "Library" ]
                            sideItem
                                (model.Filter = FilterAll)
                                "All Projects"
                                (Some all.Length)
                                None
                                (fun () -> dispatch (SetFilter FilterAll)) ] ]

                Html.div
                    [ prop.className "side-section"
                      prop.children
                          [ Html.div [ prop.className "side-heading"; prop.text "Filters" ]
                            sideItem
                                (model.Filter = FilterNeedsPreview)
                                "Needs Preview"
                                (Some (countWhere (matchesFilter FilterNeedsPreview)))
                                (Some "var(--status-info)")
                                (fun () -> dispatch (SetFilter FilterNeedsPreview))
                            sideItem
                                (model.Filter = FilterHasWarnings)
                                "Has Warnings"
                                (Some (countWhere (matchesFilter FilterHasWarnings)))
                                (Some "var(--status-warn)")
                                (fun () -> dispatch (SetFilter FilterHasWarnings))
                            sideItem
                                (model.Filter = FilterMissingRenderRegion)
                                "Missing Render Region"
                                (Some (countWhere (matchesFilter FilterMissingRenderRegion)))
                                (Some "var(--status-error)")
                                (fun () -> dispatch (SetFilter FilterMissingRenderRegion)) ] ]

                Html.div
                    [ prop.className "side-section"
                      prop.children
                          [ Html.div [ prop.className "side-heading"; prop.text "Imported Folders" ]
                            match importedFolders model with
                            | [] ->
                                Html.div [ prop.className "side-empty"; prop.text "Nothing imported yet" ]
                            | folders ->
                                Html.div
                                    [ prop.children
                                          [ for (folder, count) in folders ->
                                                sideItem
                                                    (model.Filter = FilterFolder folder)
                                                    (NodeApi.path.basename folder)
                                                    (Some count)
                                                    None
                                                    (fun () -> dispatch (SetFilter (FilterFolder folder))) ] ] ] ] ] ]
