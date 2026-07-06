module ReaperDashboard.Core.DurationCalculator

/// Pure duration logic, per spec:
///  1. If a region named "render" exists, its length wins.
///  2. Otherwise: first audio item start -> last audio item end.
///  3. No audio items -> Unknown (and the project gets flagged).

open ReaperDashboard.Core

let calculate (regions: Region list) (audioItems: AudioItem list) : float option * DurationSource =
    match regions |> List.tryFind Project.isRenderRegion with
    | Some r when r.End > r.Start ->
        Some (r.End - r.Start), FromRenderRegion
    | _ ->
        match audioItems with
        | [] -> None, UnknownDuration
        | items ->
            let start = items |> List.map (fun i -> i.Position) |> List.min
            let finish = items |> List.map (fun i -> i.Position + i.Length) |> List.max
            Some (max 0.0 (finish - start)), FromAudioBounds
