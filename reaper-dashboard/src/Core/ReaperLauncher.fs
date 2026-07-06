module ReaperDashboard.Core.ReaperLauncher

/// Launch REAPER with a project, safely:
///  * no shell involved — arguments are passed as an argv array, so paths
///    with spaces/quotes cannot be interpreted as commands
///  * on macOS we go through `open -a <REAPER.app> <project.rpp>` which
///    reuses a running REAPER instance
///  * failures reject the promise with a readable message instead of crashing

open Fable.Core
open Fable.Core.JsInterop

let openProject (reaperAppPath: string) (rppPath: string) : JS.Promise<unit> =
    Promise.create (fun resolve reject ->
        try
            if NodeApi.platform = "darwin" then
                // `open` exits quickly: 0 on success, non-zero if the app
                // could not be found/launched.
                let cp =
                    NodeApi.childProcess.spawn (
                        "open",
                        [| "-a"; reaperAppPath; rppPath |],
                        createObj [ "stdio" ==> "ignore" ]
                    )
                cp?on ("error", fun (err: obj) -> reject (System.Exception (string err))) |> ignore
                cp?on ("exit", fun (code: int) ->
                    if code = 0 then resolve ()
                    else reject (System.Exception (sprintf "`open` exited with code %d — is REAPER at \"%s\"?" code reaperAppPath)))
                |> ignore
            else
                // Non-macOS fallback (useful during development): treat the
                // configured path as the REAPER executable itself.
                let cp =
                    NodeApi.childProcess.spawn (
                        reaperAppPath,
                        [| rppPath |],
                        createObj [ "stdio" ==> "ignore"; "detached" ==> true ]
                    )
                cp?on ("error", fun (err: obj) -> reject (System.Exception (string err))) |> ignore
                cp?on ("spawn", fun () ->
                    cp?unref () |> ignore
                    resolve ())
                |> ignore
        with ex ->
            reject ex)
