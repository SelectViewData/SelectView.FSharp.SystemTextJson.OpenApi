namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

module private Helpers =
  let (|FSharpOptionType|_|) (t: Type) =
    if t.IsGenericType then
      let gtd = t.GetGenericTypeDefinition()

      if gtd = typedefof<option<_>> then
        Some(t.GetGenericArguments()[0])
      else
        None
    else
      None
      
  let (|FSharpValueOptionType|_|) (t: Type) =
    if t.IsGenericType then
      let gtd = t.GetGenericTypeDefinition()

      if gtd = typedefof<ValueOption<_>> then
        Some(t.GetGenericArguments()[0])
      else
        None
    else
      None

  let getUnionCaseSingleFieldName (fsOptions: JsonFSharpOptions) (jsonOptions: System.Text.Json.JsonSerializerOptions) (unionType: Type) (caseName: string) =
    let caseInfo =
      FSharpType.GetUnionCases unionType
      |> Array.find (fun c -> String.Equals(c.Name, caseName, StringComparison.Ordinal))

    match caseInfo.GetFields() with
    | [| field |] ->
      let fieldName =
        if fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnionFieldNamesFromTypes then
          let useTypeName = field.Name = "Item"
          if useTypeName then field.PropertyType.Name else field.Name
        else
          field.Name

      let policy : Text.Json.JsonNamingPolicy | null = fsOptions.UnionFieldNamingPolicy // fix for missing nullable info. test if needed
      
      let namingPolicy =
        match policy with
        | null -> jsonOptions.PropertyNamingPolicy
        | policy -> policy

      OpenApiSchemaHelpers.convertName namingPolicy fieldName
    | _ -> failwith $"Expected union case '%s{caseName}' to have a single field."

  let getUnionCaseName (fsOptions: JsonFSharpOptions) (caseName: string) =
    OpenApiSchemaHelpers.convertName fsOptions.UnionTagNamingPolicy caseName

type OptionSchemaTransformer(fsOptions: JsonFSharpOptions) =
  interface IOpenApiSchemaTransformer with
    member _.TransformAsync(schema: OpenApiSchema, ctx: OpenApiSchemaTransformerContext, ct: CancellationToken) : Task =
      let fsOptions = FsOptions.getEffectiveFsOptions ctx.JsonTypeInfo.Type fsOptions
      let getOrCreateSchema (ty: Type) = OpenApiSchemaHelpers.getOrCreateDeterministicSchema ctx ct ty

      let transformUnwrappedOption innerType =
        task {
          let! innerSchema = getOrCreateSchema innerType
          SchemaCopy.copyTo schema innerSchema

          // Top-level option and voption schemas are unwrapped, while option- and voption-valued record properties
          // remain nullable: FSharp.SystemTextJson writes `None` and `ValueNone` as `null`.
          if not (isNull ctx.JsonPropertyInfo) then
            OpenApiSchemaHelpers.addNull schema innerSchema
        }
        :> Task

      let transformWrappedOption innerType =
        task {
          // When options are not unwrapped, `None` is still `null` (because FSharpOption<'T> is a reference type),
          // but `Some x` is encoded as a tagged object.
          //
          // We model this as a single schema that allows either:
          // - `null`
          // - an object with the union tag + the `Some` field
          let tagName = fsOptions.UnionTagName
          let someCase = Helpers.getUnionCaseName fsOptions "Some"
          let valuePropName =
            Helpers.getUnionCaseSingleFieldName fsOptions ctx.JsonTypeInfo.Options ctx.JsonTypeInfo.Type "Some"

          let! innerSchema = getOrCreateSchema innerType

          let someSchema =
            OpenApiSchema(
              Type = OpenApiSchemaHelpers.unionWithNull (Nullable(JsonSchemaType.Object)),
              Properties =
                Dictionary<string, IOpenApiSchema>(
                  [ KeyValuePair(tagName, OpenApiSchemaHelpers.makeConstEnumSchema someCase :> IOpenApiSchema)
                    KeyValuePair(valuePropName, innerSchema) ]
                ),
              Required = HashSet([ tagName; valuePropName ])
            )

          SchemaCopy.copyTo schema someSchema
        }
        :> Task

      let transformWrappedValueOption innerType =
        task {
          // ValueOption<'T> is a struct so `ValueNone` is not `null`. Without UnwrapOption, it's encoded as a tagged union.
          let tagName = fsOptions.UnionTagName
          let noneCase = Helpers.getUnionCaseName fsOptions "ValueNone"
          let someCase = Helpers.getUnionCaseName fsOptions "ValueSome"
          let valuePropName =
            Helpers.getUnionCaseSingleFieldName fsOptions ctx.JsonTypeInfo.Options ctx.JsonTypeInfo.Type "ValueSome"

          let! innerSchema = getOrCreateSchema innerType

          let valueNoneSchema =
            OpenApiSchema(
              Type = Nullable(JsonSchemaType.Object),
              Properties = Dictionary<string, IOpenApiSchema>([ KeyValuePair(tagName, OpenApiSchemaHelpers.makeConstEnumSchema noneCase :> IOpenApiSchema) ]),
              Required = HashSet([ tagName ])
            )

          let valueSomeSchema =
            OpenApiSchema(
              Type = Nullable(JsonSchemaType.Object),
              Properties =
                Dictionary<string, IOpenApiSchema>(
                  [ KeyValuePair(tagName, OpenApiSchemaHelpers.makeConstEnumSchema someCase :> IOpenApiSchema)
                    KeyValuePair(valuePropName, innerSchema) ]
                ),
              Required = HashSet([ tagName; valuePropName ])
            )

          let unionSchema =
            OpenApiSchema(OneOf = ResizeArray<IOpenApiSchema>([ valueNoneSchema :> IOpenApiSchema; valueSomeSchema :> IOpenApiSchema ]))

          SchemaCopy.copyTo schema unionSchema
        }
        :> Task

      match ctx.JsonTypeInfo.Type with
      | Helpers.FSharpOptionType innerType
      | Helpers.FSharpValueOptionType innerType when fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapOption ->
        transformUnwrappedOption innerType
      | Helpers.FSharpOptionType innerType ->
        transformWrappedOption innerType
      | Helpers.FSharpValueOptionType innerType ->
        transformWrappedValueOption innerType
      | _ ->
        Task.CompletedTask
