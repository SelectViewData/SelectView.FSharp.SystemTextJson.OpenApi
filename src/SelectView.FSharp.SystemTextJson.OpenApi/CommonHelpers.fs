namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Threading
open System.Threading.Tasks
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

module internal OpenApiSchemaHelpers =

  let private hasSchemaTypeFlag (schemaType: JsonSchemaType) (flag: JsonSchemaType) =
    ((int schemaType) &&& (int flag)) = int flag

  let private normalizePrimitiveStringCoercion (schema: OpenApiSchema) =
    match schema.Type with
    | value when value.HasValue ->
      let schemaType = value.Value
      let nonNullNonStringFlags =
        ((int schemaType) &&& ~~~(int JsonSchemaType.Null)) &&& ~~~(int JsonSchemaType.String)

      if hasSchemaTypeFlag schemaType JsonSchemaType.String && nonNullNonStringFlags <> 0 then
        let normalizedType =
          LanguagePrimitives.EnumOfValue((int schemaType) &&& ~~~(int JsonSchemaType.String))

        schema.Type <- Nullable(normalizedType)

        if not (hasSchemaTypeFlag normalizedType JsonSchemaType.String) then
          schema.Pattern <- null

      schema
    | _ -> schema

  let convertName (policy: System.Text.Json.JsonNamingPolicy | null) (name: string) =
    match policy with
    | Null -> name
    | NonNull nonNullPolicy -> nonNullPolicy.ConvertName(name)

  let unionWithNull (schemaType: Nullable<JsonSchemaType>) =
    if schemaType.HasValue then
      Nullable(LanguagePrimitives.EnumOfValue((int schemaType.Value) ||| (int JsonSchemaType.Null)))
    else
      Nullable()

  let isNullSchema (schema: IOpenApiSchema) =
    schema.Type = Nullable(JsonSchemaType.Null)

  let addNull (schema: OpenApiSchema) (innerSchema: IOpenApiSchema) =
    let nullSchema = OpenApiSchema(Type = Nullable(JsonSchemaType.Null)) :> IOpenApiSchema

    if schema.Type.HasValue then
      schema.Type <- unionWithNull schema.Type
    else
      match schema.OneOf, schema.AnyOf with
      | NonNull oneOf, _ -> oneOf.Add nullSchema
      | _, NonNull anyOf -> anyOf.Add nullSchema
      | _ -> schema.AnyOf <- ResizeArray [ innerSchema; nullSchema ]

  let ignoreNullValues (options: System.Text.Json.JsonSerializerOptions) =
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull

  let tryGetJsonNames kind (getAttributes: Type -> obj[]) =
    match
      getAttributes typeof<JsonNameAttribute>
      |> Array.choose (function
        | :? JsonNameAttribute as attribute ->
          let field: string | null = attribute.Field
          if isNull field then Some attribute else None
        | _ -> None)
    with
    | [||] ->
      match getAttributes typeof<JsonPropertyNameAttribute> with
      | [| :? JsonPropertyNameAttribute as attribute |] -> ValueSome [| JsonName.String attribute.Name |]
      | _ -> ValueNone
    | [| attribute |] -> ValueSome attribute.AllNames
    | _ ->
      let message =
        $"To provide multiple names for the same %s{kind}, use a single JsonNameAttribute with multiple arguments"

      raise (System.Text.Json.JsonException(message))

  let makeConstEnumSchema (value: string) =
    let enumValue =
      match JsonValue.Create(value) with
      | null -> failwith "Failed to create enum value JSON node."
      | createdValue -> createdValue :> JsonNode

    OpenApiSchema(
      Type = Nullable(JsonSchemaType.String),
      Enum = ResizeArray<JsonNode>([ enumValue ])
    )

  let rec unwrapReferenceSchema (schema: IOpenApiSchema) : IOpenApiSchema =
    match schema with
    | :? OpenApiSchemaReference as schemaReference ->
      match schemaReference.Target with
      | NonNull target -> unwrapReferenceSchema target
      | Null -> schema
    | _ -> schema

  let enumIsEmpty (schema: IOpenApiSchema) =
    match schema.Enum with
    | null -> true
    | enumValues -> enumValues.Count = 0

  let getOrCreateDeterministicSchema
    (ctx: OpenApiSchemaTransformerContext)
    (ct: CancellationToken)
    (t: Type)
    : Task<OpenApiSchema> =
    task {
      let! schema = ctx.GetOrCreateSchemaAsync(t, ctx.ParameterDescription, ct)
      let normalizedSchema = OpenApiSchema()
      SchemaCopy.copyTo normalizedSchema schema
      return normalizePrimitiveStringCoercion normalizedSchema
    }
