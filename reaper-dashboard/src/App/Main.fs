module ReaperDashboard.App.Main

open Elmish
open Elmish.React

Program.mkProgram State.init State.update ReaperDashboard.UI.View.view
|> Program.withReactSynchronous "app"
|> Program.run
