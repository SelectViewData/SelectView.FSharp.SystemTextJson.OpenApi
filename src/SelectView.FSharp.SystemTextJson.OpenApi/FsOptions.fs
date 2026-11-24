namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Text.Json.Serialization

module internal FsOptions =

  let private tryGetTypeOrGeneric (t: Type) (overrides: Collections.Generic.IDictionary<Type, JsonFSharpOptions> | null) =
    match overrides with
    | Null -> None
    | NonNull overrides ->
      match overrides.TryGetValue t with
      | true, options -> Some options
      | _ when t.IsGenericType ->
        match overrides.TryGetValue(t.GetGenericTypeDefinition()) with
        | true, options -> Some options
        | _ -> None
      | _ -> None

  let getEffectiveFsOptions (t: Type) (fsOptions: JsonFSharpOptions) =
    let inheritUnionEncoding (options: JsonFSharpOptions) =
      if options.UnionEncoding.HasFlag JsonUnionEncoding.Inherit then
        options.WithUnionEncoding(fsOptions.UnionEncoding)
      else
        options

    match tryGetTypeOrGeneric t (fsOptions.Overrides fsOptions) with
    | Some options -> inheritUnionEncoding options
    | None when fsOptions.AllowOverride ->
      match t.GetCustomAttributes(typeof<IJsonFSharpConverterAttribute>, true) with
      | [||] -> fsOptions
      | attributes -> inheritUnionEncoding (attributes[0] :?> IJsonFSharpConverterAttribute).Options
    | None -> fsOptions
