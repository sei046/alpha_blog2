module ReaperDashboard.Core.PreviewManager

/// Manifest disk IO for preview renders. The staleness *policy* lives in the
/// pure PreviewPolicy module; this layer just reads/writes the sidecar JSON.
///
/// A preview render produces two files in the preview folder:
///   <key>.<ext>          the audio (format decided by REAPER's render config)
///   <key>.preview.json   this manifest, recording what it was rendered from

open Fable.Core
open Fable.Core.JsInterop
open ReaperDashboard.Core

/// The full on-disk manifest: the freshness facts plus bookkeeping.
type Manifest =
    { ProjectMtime: float
      Media: PreviewPolicy.MediaStamp list
      RenderHash: string
      PreviewFile: string
      Format: string
      RenderedAt: float }

/// The subset PreviewPolicy.evaluate needs.
let facts (m: Manifest) : PreviewPolicy.ManifestFacts =
    { ProjectMtime = m.ProjectMtime; Media = m.Media; RenderHash = m.RenderHash }

let manifestPath (previewFolder: string) (projectPath: string) : string =
    NodeApi.path.join (previewFolder, PreviewPolicy.previewKey projectPath + ".preview.json")

let readManifest (previewFolder: string) (projectPath: string) : JS.Promise<Manifest option> =
    NodeApi.fsp.readFile (manifestPath previewFolder projectPath, "utf8")
    |> Promise.map (fun text ->
        let o = JS.JSON.parse text
        let media: obj[] =
            let arr: obj = o?media
            if JS.Constructors.Array.isArray arr then unbox arr else [||]
        Some
            { ProjectMtime = o?projectMtime |> unbox
              Media =
                media
                |> Array.map (fun m -> { PreviewPolicy.Path = m?path |> unbox; PreviewPolicy.Mtime = m?mtime |> unbox })
                |> List.ofArray
              RenderHash = o?renderHash |> unbox
              PreviewFile = o?previewFile |> unbox
              Format = (if isNull (o?format) then "" else unbox (o?format))
              RenderedAt = o?renderedAt |> unbox })
    |> Promise.catch (fun _ -> None)

let writeManifest (previewFolder: string) (projectPath: string) (m: Manifest) : JS.Promise<unit> =
    let payload =
        createObj
            [ "project" ==> projectPath
              "projectMtime" ==> m.ProjectMtime
              "media" ==> (m.Media |> List.map (fun s -> createObj [ "path" ==> s.Path; "mtime" ==> s.Mtime ]) |> List.toArray)
              "renderHash" ==> m.RenderHash
              "previewFile" ==> m.PreviewFile
              "format" ==> m.Format
              "renderedAt" ==> m.RenderedAt ]
    NodeApi.fsp.writeFile (manifestPath previewFolder projectPath, JS.JSON.stringify (payload, space = 2), "utf8")
