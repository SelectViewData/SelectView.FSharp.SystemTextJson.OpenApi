namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Text.Json.Serialization

module internal FsOptions =

  let private customOptions (t: Type) =
    match t.GetCustomAttributes(typedefof<JsonFSharpConverterAttribute>, false) with
    | [| atr |] ->
      match atr with
      | :? IJsonFSharpConverterAttribute as converter -> Some converter.Options
      | _ -> None
    | _ -> None

  let getEffectiveFsOptions (t: Type) (fsOptions: JsonFSharpOptions) =
    customOptions t |> Option.defaultValue fsOptions

