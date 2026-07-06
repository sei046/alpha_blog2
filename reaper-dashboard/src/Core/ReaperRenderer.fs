module ReaperDashboard.Core.ReaperRenderer

/// Drives REAPER to render audio, unattended, from the command line.
///
/// The pure `.rpp` transform lives in RenderProject (unit-tested). This module
/// is the IO half — writing the temp project and spawning REAPER — which needs
/// a real Mac + REAPER to verify, so every step is logged and the output file
/// is checked before a render is reported as successful.
///
/// The numeric REAPER render flags in RenderProject are best-effort from
/// community knowledge (no official .rpp format spec), which is exactly why:
///  * the temp project is re-parsed before REAPER ever sees it,
///  * REAPER runs on a throwaway COPY, never the user's project, and
///  * success requires the output file to actually appear on disk.

open Fable.Core
open Fable.Core.JsInterop
open ReaperDashboard.Core

// Re-export the render request vocabulary so callers use one namespace.
type RenderKind = RenderProject.RenderKind
type RenderRequest = RenderProject.RenderRequest

/// The executable inside a macOS REAPER.app bundle. On other platforms we
/// treat the configured path as the executable itself (handy in dev).
let reaperExecutable (reaperAppPath: string) : string =
    if NodeApi.platform = "darwin" && reaperAppPath.EndsWith ".app" then
        NodeApi.path.join (reaperAppPath, "Contents/MacOS/REAPER")
    else
        reaperAppPath

type RenderOutcome =
    { OutputPath: string
      /// The exact argv used, for the activity log.
      Command: string }

/// Spawn REAPER on a temp project and resolve when the output file exists.
/// `onLog` streams progress lines to the caller (the render queue/log UI).
let render
    (reaperAppPath: string)
    (previewFolder: string)
    (originalText: string)
    (req: RenderRequest)
    (onLog: string -> unit)
    : JS.Promise<RenderOutcome> =
    promise {
        do! NodeApi.ensureDir previewFolder
        match req.Kind with
        | RenderProject.MatrixRender -> do! NodeApi.ensureDir req.OutputPath
        | _ -> do! NodeApi.ensureDir (NodeApi.path.dirname req.OutputPath)

        let exe = reaperExecutable reaperAppPath
        let tempProject = NodeApi.path.join (previewFolder, PreviewPolicy.previewKey req.OutputPath + ".render.rpp")
        let renderText = RenderProject.buildRenderProjectText req originalText

        // The temp project must itself be a valid project before REAPER sees it.
        match RppParser.parse renderText with
        | Error msg -> return failwith (sprintf "Internal error building render project: %s" msg)
        | Ok _ -> ()

        do! NodeApi.fsp.writeFile (tempProject, renderText, "utf8")
        let args = [| "-renderproject"; tempProject |]
        let command = sprintf "%s %s" exe (String.concat " " (args |> Array.map (fun a -> "\"" + a + "\"")))
        onLog (sprintf "Rendering via: %s" command)

        do!
            Promise.create (fun resolve reject ->
                try
                    let cp = NodeApi.childProcess.spawn (exe, args, createObj [ "stdio" ==> "ignore" ])
                    cp?on ("error", fun (err: obj) -> reject (System.Exception (sprintf "Could not launch REAPER at %s: %s" exe (string err)))) |> ignore
                    cp?on ("exit", fun (code: int) ->
                        if code = 0 then resolve ()
                        else reject (System.Exception (sprintf "REAPER exited with code %d" code)))
                    |> ignore
                with ex -> reject ex)

        // Verify REAPER actually produced the output before calling it a win.
        let! ok = NodeApi.fileExists req.OutputPath
        do! NodeApi.deleteQuiet tempProject
        if not ok then
            return failwith (sprintf "REAPER finished but no output appeared at %s (check the project's render format/output settings)" req.OutputPath)
        onLog "Render finished; output verified on disk."
        return { OutputPath = req.OutputPath; Command = command }
    }
