namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

module private EnumHelpers =

  let isStringValue (node: JsonNode) =
    match node with
    | :? JsonValue as value ->
      try
        value.GetValue<string>() |> ignore
        true
      with _ ->
        false
    | _ -> false

  let private getIntegerFormat (t: Type) =
    let underlying = Enum.GetUnderlyingType(t)

    if underlying = typeof<int64> || underlying = typeof<uint64> then
      "int64"
    else
      "int32"

  let private getEnumNumericValues (t: Type) =
    let underlying = Enum.GetUnderlyingType(t)

    let toNumericValue (enumValue: obj) =
      if
        underlying = typeof<uint64>
        || underlying = typeof<uint>
        || underlying = typeof<uint16>
        || underlying = typeof<byte>
      then
        box (Convert.ToUInt64(enumValue))
      else
        box (Convert.ToInt64(enumValue))

    Enum.GetValues(t)
    |> Seq.cast<obj>
    |> Seq.map toNumericValue
    |> Seq.distinct
    |> Seq.choose (fun v ->
      match JsonValue.Create(v) with
      | null -> None
      | x -> Some(x :> JsonNode))
    |> Seq.toList

  let setSchemaForNumericEnum (schema: OpenApiSchema) (t: Type) =
    schema.Type <- Nullable(JsonSchemaType.Integer)

    if String.IsNullOrWhiteSpace(schema.Format) then
      schema.Format <- getIntegerFormat t

    match schema.Enum with
    | null ->
      let values = getEnumNumericValues t
      if values.Length > 0 then
        schema.Enum <- ResizeArray<JsonNode>(values)
    | values when values.Count = 0 ->
      let values = getEnumNumericValues t
      if values.Length > 0 then
        schema.Enum <- ResizeArray<JsonNode>(values)
    | _ -> ()

type EnumSchemaTransformer() =
  interface IOpenApiSchemaTransformer with
    member _.TransformAsync(schema: OpenApiSchema, ctx: OpenApiSchemaTransformerContext, _ct: CancellationToken) : Task =
      task {
        let t = ctx.JsonTypeInfo.Type

        if t.IsEnum then
          match schema.Enum with
          | NonNull values when values.Count > 0 ->
            let allStrings = values |> Seq.forall EnumHelpers.isStringValue

            if allStrings then
              schema.Type <- Nullable(JsonSchemaType.String)
            elif not schema.Type.HasValue || schema.Type.Value = JsonSchemaType.Number then
              EnumHelpers.setSchemaForNumericEnum schema t
          | _ ->
            EnumHelpers.setSchemaForNumericEnum schema t
      }
      :> Task

/// JSON Schema checks `enum` apart from `type`, so an enum component whose final type allows null (an
/// option of it) must list null too. Registered after the app's own document transformers, which may
/// make such a component non-null again.
type NullableEnumDocumentTransformer() =
  interface IOpenApiDocumentTransformer with
    member _.TransformAsync
      (document: OpenApiDocument, _context: OpenApiDocumentTransformerContext, _ct: CancellationToken)
      : Task =
      let schemas =
        match document.Components with
        | NonNull components ->
          match components.Schemas with
          | NonNull schemas -> schemas.Values |> Seq.toList
          | Null -> []
        | Null -> []

      for schema in schemas do
        match schema with
        | :? OpenApiSchema as schema when
          schema.Type.HasValue && schema.Type.Value.HasFlag JsonSchemaType.Null
          ->
          match schema.Enum with
          | NonNull values when values.Count > 0 && not (values |> Seq.exists _.IsJsonNullSentinel()) ->
            values.Add(JsonNullSentinel.JsonNull)
          | _ -> ()
        | _ -> ()

      Task.CompletedTask
