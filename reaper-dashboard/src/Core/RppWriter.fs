module ReaperDashboard.Core.RppWriter

/// The ONLY module that writes to .rpp files, and it refuses to do so unless
/// the full safety ritual passes:
///
///   1. The file on disk must be unchanged since it was loaded (mtime check),
///      so we never clobber edits made in REAPER while the editor was open.
///   2. The new text must re-parse as a valid REAPER project (RppParser).
///   3. A timestamped backup of the original text is written FIRST and its
///      content read back and verified before the original is touched.
///
/// Returns the backup path so the UI can tell the user where it is.

open Fable.Core
open ReaperDashboard.Core

let private pad2 (n: int) = if n < 10 then "0" + string n else string n

let private timestamp () =
    let d = JS.Constructors.Date.Create (JS.Constructors.Date.now ())
    sprintf "%04d%s%s-%s%s%s" d.Year (pad2 d.Month) (pad2 d.Day) (pad2 d.Hour) (pad2 d.Minute) (pad2 d.Second)

let backupPathFor (rppPath: string) = rppPath + ".backup-" + timestamp ()

/// Validate, back up, then write. Rejects (with a readable message) on any
/// failure — and always before the original has been modified.
let saveProjectText
    (rppPath: string)
    (originalText: string)
    (loadedMtimeMs: float)
    (newText: string)
    : JS.Promise<string> =
    promise {
        // 1. Concurrent-modification guard.
        let! st = NodeApi.fsp.stat rppPath
        if st.mtimeMs <> loadedMtimeMs then
            failwith "The project file changed on disk since the editor was opened. Close and re-open the editor to pick up the new state — nothing was written."

        // 2. The result must still be a valid project before we touch anything.
        match RppParser.parse newText with
        | Error e ->
            failwith (sprintf "Refusing to write: the edited project failed validation (%s). Nothing was written." e)
        | Ok _ -> ()

        // 3. Backup first, and verify the backup actually holds the original.
        let backup = backupPathFor rppPath
        do! NodeApi.fsp.writeFile (backup, originalText, "utf8")
        let! written = NodeApi.fsp.readFile (backup, "utf8")
        if written <> originalText then
            failwith "Backup verification failed — nothing was written to the project."

        // 4. Only now replace the project file.
        do! NodeApi.fsp.writeFile (rppPath, newText, "utf8")
        return backup
    }
