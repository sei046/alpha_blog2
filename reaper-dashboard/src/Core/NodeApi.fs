module ReaperDashboard.Core.NodeApi

/// Thin, typed bindings over the Node and Electron APIs the renderer uses.
/// Everything effectful funnels through here so the rest of Core stays pure.

open Fable.Core
open Fable.Core.JsInterop

type Stats =
    abstract size: float
    abstract mtimeMs: float
    abstract isFile: unit -> bool
    abstract isDirectory: unit -> bool

type IPath =
    abstract basename: p: string -> string
    abstract basename: p: string * ext: string -> string
    abstract dirname: p: string -> string
    abstract extname: p: string -> string
    abstract join: a: string * b: string -> string
    abstract isAbsolute: p: string -> bool
    abstract sep: string

type IFsPromises =
    abstract readFile: path: string * encoding: string -> JS.Promise<string>
    abstract writeFile: path: string * data: string * encoding: string -> JS.Promise<unit>
    abstract stat: path: string -> JS.Promise<Stats>
    abstract readdir: path: string * options: obj -> JS.Promise<string[]>

type IChildProcess =
    abstract spawn: cmd: string * args: string[] * options: obj -> obj

type IIpcRenderer =
    abstract invoke: channel: string * arg: obj -> JS.Promise<obj>

let path: IPath = importAll "path"
let fsp: IFsPromises = import "promises" "fs"
let childProcess: IChildProcess = importAll "child_process"
let ipcRenderer: IIpcRenderer = import "ipcRenderer" "electron"

[<Emit("process.platform")>]
let platform: string = jsNative

let nowMs () : float = JS.Constructors.Date.now ()

/// True if the path exists on disk (any file type). Never rejects.
let fileExists (p: string) : JS.Promise<bool> =
    fsp.stat p
    |> Promise.map (fun _ -> true)
    |> Promise.catch (fun _ -> false)

/// Recursively list all files under a directory (absolute paths).
let listFilesRecursive (dir: string) : JS.Promise<string[]> =
    fsp.readdir (dir, createObj [ "recursive" ==> true ])
    |> Promise.map (fun rel -> rel |> Array.map (fun r -> path.join (dir, r)))

// --- IPC wrappers (native dialogs + app paths live in the main process) ----

let chooseRppFiles (defaultPath: string) : JS.Promise<string[]> =
    ipcRenderer.invoke ("dialog:choose-rpp-files", defaultPath)
    |> Promise.map (fun o -> o :?> string[])

let chooseFolder (title: string) (defaultPath: string) : JS.Promise<string> =
    ipcRenderer.invoke ("dialog:choose-folder", createObj [ "title" ==> title; "defaultPath" ==> defaultPath ])
    |> Promise.map (fun o -> o :?> string)

/// macOS .app bundles are directories, but the open-file dialog treats them
/// as selectable packages, so this uses an openFile dialog in the main process.
let chooseApp () : JS.Promise<string> =
    ipcRenderer.invoke ("dialog:choose-app", null)
    |> Promise.map (fun o -> o :?> string)

let getUserDataPath () : JS.Promise<string> =
    ipcRenderer.invoke ("app:get-paths", null)
    |> Promise.map (fun o -> o?userData |> unbox<string>)
