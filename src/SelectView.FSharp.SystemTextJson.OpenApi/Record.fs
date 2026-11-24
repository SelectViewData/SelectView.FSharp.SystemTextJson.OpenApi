namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

module private RecordHelpers =

  let isSkippableType (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Skippable<_>>

  let (|SkippableType|_|) (t: Type) =
    if isSkippableType t then
      Some(t.GetGenericArguments()[0])
    else
      None

  let getRecordProperties (fsOptions: JsonFSharpOptions) (recordType: Type) =
    let fields = FSharpType.GetRecordFields(recordType, true)

    let allPublic =
      recordType.GetProperties(BindingFlags.Instance ||| BindingFlags.Public)

    let all =
      if
        fields.Length > 0
        && (fields[0].GetGetMethod(true)
            |> Option.ofObj
            |> Option.exists (fun m -> m.IsPublic))
      then
        allPublic
      else
        Array.append fields allPublic

    if fsOptions.IncludeRecordProperties then
      all
    else
      all
      |> Array.filter (fun p ->
        Array.contains p fields
        || (p.GetCustomAttributes(typeof<JsonIncludeAttribute>, true) |> Seq.isEmpty |> not))


type RecordSchemaTransformer(fsOptions: JsonFSharpOptions) =
  interface IOpenApiSchemaTransformer with
    member _.TransformAsync
      (schema: OpenApiSchema, ctx: OpenApiSchemaTransformerContext, ct: CancellationToken)
      : Task =
      task {
        let t = ctx.JsonTypeInfo.Type

        if FSharpType.IsRecord(t, true) then
          // Preserve schema identity so records stay component schemas (not inlined everywhere).
          let originalId = schema.Id
          let originalMetadata = schema.Metadata
          let fsOptions = FsOptions.getEffectiveFsOptions t fsOptions
          let ignoreNulls = OpenApiSchemaHelpers.ignoreNullValues ctx.JsonTypeInfo.Options

          let getOrCreateSchema ty =
            OpenApiSchemaHelpers.getOrCreateDeterministicSchema ctx ct ty

          let props = Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
          let required = HashSet<string>(StringComparer.Ordinal)

          let recordProps =
            RecordHelpers.getRecordProperties fsOptions t
            |> Array.filter (fun p ->
              p.GetCustomAttributes(typeof<JsonIgnoreAttribute>, true) |> Array.isEmpty)

          for p in recordProps do
            let name =
              match
                OpenApiSchemaHelpers.tryGetJsonNames "field" (fun ty -> p.GetCustomAttributes(ty, true))
              with
              | ValueSome names -> names[0].AsString()
              | ValueNone ->
                OpenApiSchemaHelpers.convertName ctx.JsonTypeInfo.Options.PropertyNamingPolicy p.Name

            let canBeSkipped =
              let explicitlyRequired =
                p.GetCustomAttributes(typeof<JsonRequiredAttribute>, true)
                |> Array.isEmpty
                |> not

              not explicitlyRequired
              && (ignoreNulls || RecordHelpers.isSkippableType p.PropertyType)

            let effectiveType =
              match p.PropertyType with
              | RecordHelpers.SkippableType inner -> inner
              | _ -> p.PropertyType

            let propertySchemaTask =
              match effectiveType with
              | OpenApiSchemaHelpers.OptionOrValueOptionType innerType when
                  fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapOption
                  ->
                  task {
                    let! innerSchema = getOrCreateSchema innerType

                    let optionSchema = OpenApiSchema()
                    SchemaCopy.copyTo optionSchema innerSchema
                    OpenApiSchemaHelpers.addNull optionSchema innerSchema
                    return optionSchema
                  }
              | _ -> getOrCreateSchema effectiveType

            let! propertySchema = propertySchemaTask

            props[name] <- propertySchema

            if not canBeSkipped then
              required.Add(name) |> ignore

          let recordSchema =
            OpenApiSchema(
              Type = Nullable(JsonSchemaType.Object),
              Properties = props,
              Required = required,
              AdditionalPropertiesAllowed = false
            )

          SchemaCopy.copyTo schema recordSchema
          schema.Id <- originalId
          schema.Metadata <- originalMetadata
      }
      :> Task
