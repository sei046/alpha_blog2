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
      LastImportDir: string }

let defaultSettings =
    { ReaperAppPath = "/Applications/REAPER.app"
      LastImportDir = "" }

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
          LastImportDir = strField o "lastImportDir" "" })
    |> Promise.catch (fun _ -> defaultSettings) // first run / unreadable -> defaults

let saveSettings (userData: string) (s: AppSettings) : JS.Promise<unit> =
    let payload =
        createObj
            [ "reaperAppPath" ==> s.ReaperAppPath
              "lastImportDir" ==> s.LastImportDir ]
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
