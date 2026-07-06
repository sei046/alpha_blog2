module ReaperDashboard.Core.Settings

/// App settings + imported-library persistence.
///
/// Both live as small JSON files in Electron's per-app userData folder
/// (~/Library/Application Support/reaper-project-dashboard on macOS):
///   settings.json — REAPER path, last-used folders
///   library.json  — the list of imported .rpp paths
///
/// The library stores only paths; projects are re-scanned from disk on every
/// launch so the dashboard never shows stale parse results.

open Fable.Core
open Fable.Core.JsInterop
open ReaperDashboard.Core

type AppSettings =
    { ReaperAppPath: string
      LastImportDir: string
      /// Where preview renders + their manifests live. Empty = the default
      /// (<userData>/previews), resolved once userData is known.
      PreviewFolder: string
      /// Where WAV bounces are written. Empty = alongside each project.
      WavBounceFolder: string
      /// Filename pattern for WAV bounces; "$project" -> the project name.
      WavPattern: string }

let defaultSettings =
    { ReaperAppPath = "/Applications/REAPER.app"
      LastImportDir = ""
      PreviewFolder = ""
      WavBounceFolder = ""
      WavPattern = "$project" }

/// The effective preview folder: the configured one, or <userData>/previews.
let effectivePreviewFolder (userData: string) (s: AppSettings) : string =
    if s.PreviewFolder.Trim () <> "" then s.PreviewFolder
    else NodeApi.path.join (userData, "previews")

/// Expand a WAV filename pattern (see Format.expandWavPattern — kept there so
/// it stays pure/testable).
let expandWavPattern = Format.expandWavPattern

let private settingsFile userData = NodeApi.path.join (userData, "settings.json")
let private libraryFile userData = NodeApi.path.join (userData, "library.json")

let private strField (o: obj) (name: string) (fallback: string) : string =
    let v: obj = o?(name)
    if jsTypeof v = "string" then unbox<string> v else fallback

let loadSettings (userData: string) : JS.Promise<AppSettings> =
    NodeApi.fsp.readFile (settingsFile userData, "utf8")
    |> Promise.map (fun text ->
        let o = JS.JSON.parse text
        { ReaperAppPath = strField o "reaperAppPath" defaultSettings.ReaperAppPath
          LastImportDir = strField o "lastImportDir" ""
          PreviewFolder = strField o "previewFolder" ""
          WavBounceFolder = strField o "wavBounceFolder" ""
          WavPattern = strField o "wavPattern" defaultSettings.WavPattern })
    |> Promise.catch (fun _ -> defaultSettings) // first run / unreadable -> defaults

let saveSettings (userData: string) (s: AppSettings) : JS.Promise<unit> =
    let payload =
        createObj
            [ "reaperAppPath" ==> s.ReaperAppPath
              "lastImportDir" ==> s.LastImportDir
              "previewFolder" ==> s.PreviewFolder
              "wavBounceFolder" ==> s.WavBounceFolder
              "wavPattern" ==> s.WavPattern ]
    NodeApi.fsp.writeFile (settingsFile userData, JS.JSON.stringify (payload, space = 2), "utf8")

let loadLibrary (userData: string) : JS.Promise<string[]> =
    NodeApi.fsp.readFile (libraryFile userData, "utf8")
    |> Promise.map (fun text ->
        let o = JS.JSON.parse text
        let arr: obj = o?projects
        if JS.Constructors.Array.isArray arr then
            unbox<obj[]> arr
            |> Array.choose (fun v -> if jsTypeof v = "string" then Some (unbox<string> v) else None)
        else
            [||])
    |> Promise.catch (fun _ -> [||])

let saveLibrary (userData: string) (paths: string[]) : JS.Promise<unit> =
    let payload = createObj [ "projects" ==> paths ]
    NodeApi.fsp.writeFile (libraryFile userData, JS.JSON.stringify (payload, space = 2), "utf8")
