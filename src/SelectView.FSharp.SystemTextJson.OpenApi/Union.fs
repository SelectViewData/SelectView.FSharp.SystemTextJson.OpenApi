namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

module private UnionHelpers =

  let (|FSharpOptionType|_|) (t: Type) =
    if t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>> then
      Some(t.GetGenericArguments()[0])
    else
      None

  let isFSharpOptionType (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

  let isFSharpValueOptionType (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<ValueOption<_>>

  let isFSharpListType (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

  let isSkippableType (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Skippable<_>>

  let getJsonFieldNames (getAttributes: Type -> obj[]) =
    getAttributes typeof<JsonNameAttribute>
    |> Seq.choose (function
      | :? JsonNameAttribute as attr ->
        let field: string | null = attr.Field
        if not (isNull field) then Some(attr.Field, attr.AllNames) else None
      | _ -> None)
    |> readOnlyDict

  let getUnionCaseTagValue (fsOptions: JsonFSharpOptions) (uci: UnionCaseInfo) =
    match OpenApiSchemaHelpers.tryGetJsonNames "case" uci.GetCustomAttributes with
    | ValueSome name -> name[0].AsString()
    | ValueNone -> OpenApiSchemaHelpers.convertName fsOptions.UnionTagNamingPolicy uci.Name

  let getUnionFieldNamingPolicy (fsOptions: JsonFSharpOptions) (jsonOptions: System.Text.Json.JsonSerializerOptions) =
    let policy : Text.Json.JsonNamingPolicy | null = fsOptions.UnionFieldNamingPolicy // fix for missing nullable info. test if needed
    match policy with
    | null -> jsonOptions.PropertyNamingPolicy
    | policy -> policy

  let getUnionCaseFieldNames (fsOptions: JsonFSharpOptions) (jsonOptions: System.Text.Json.JsonSerializerOptions) (uci: UnionCaseInfo) =
    let jsonFieldNames = getJsonFieldNames uci.GetCustomAttributes
    let fields = uci.GetFields()

    let fieldsAndNames =
      if fsOptions.UnionEncoding.HasFlag(JsonUnionEncoding.UnionFieldNamesFromTypes) then
        let usedFieldNames = Dictionary<string, int>(StringComparer.Ordinal)

        fields
        |> Array.mapi (fun i field ->
          let useTypeName =
            if i = 0 && fields.Length = 1 then
              field.Name = "Item"
            else
              field.Name = "Item" + string (i + 1)

          let baseName = if useTypeName then field.PropertyType.Name else field.Name

          let nameIndex =
            match usedFieldNames.TryGetValue(baseName) with
            | true, ix -> ix + 1
            | false, _ -> 1

          usedFieldNames[baseName] <- nameIndex
          field, baseName, nameIndex, usedFieldNames)
      else
        fields |> Array.map (fun field -> field, field.Name, 1, Dictionary<string, int>(StringComparer.Ordinal))

    let namingPolicy = getUnionFieldNamingPolicy fsOptions jsonOptions

    fieldsAndNames
    |> Array.map (fun (field, baseName, nameIndex, usedFieldNames) ->
      let finalName =
        let mutable nameCount = 1

        if
          nameIndex = 1
          && not (usedFieldNames.TryGetValue(baseName, &nameCount) && nameCount > 1)
        then
          baseName
        else
          baseName + string nameIndex

      let jsonName =
        match jsonFieldNames.TryGetValue(finalName) with
        | true, names -> names[0].AsString()
        | false, _ -> OpenApiSchemaHelpers.convertName namingPolicy finalName

      jsonName, field)

  let getPropertyName (jsonOptions: System.Text.Json.JsonSerializerOptions) (prop: System.Reflection.PropertyInfo) =
    match OpenApiSchemaHelpers.tryGetJsonNames "field" (fun ty -> prop.GetCustomAttributes(ty, true)) with
    | ValueSome names -> names[0].AsString()
    | ValueNone -> OpenApiSchemaHelpers.convertName jsonOptions.PropertyNamingPolicy prop.Name

  let getRecordProperties (fsOptions: JsonFSharpOptions) (recordType: Type) =
    let fields = FSharpType.GetRecordFields(recordType, true)
    let allPublic = recordType.GetProperties(BindingFlags.Instance ||| BindingFlags.Public)

    let all =
      if fields.Length > 0 && (fields[0].GetGetMethod(true) |> Option.ofObj |> Option.exists (fun m -> m.IsPublic)) then
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

type private UnionCasePromotionPlan =
  { CaseName: string
    CaseSchema: OpenApiSchema }

module private UnionCasePromotionPlan =

  [<Literal>]
  let MetadataKey = "SelectView.FSharp.SystemTextJson.OpenApi.UnionCasePromotionPlan"

  let add (schema: OpenApiSchema) (originalMetadata: IDictionary<string, obj> | null) (plans: UnionCasePromotionPlan array) =
    let metadata = Dictionary<string, obj>(StringComparer.Ordinal)

    match originalMetadata with
    | Null -> ()
    | NonNull existing ->
      for KeyValue(key, value) in existing do
        metadata[key] <- value

    metadata[MetadataKey] <- plans :> obj
    schema.Metadata <- metadata

  let tryGet (schema: OpenApiSchema) =
    match schema.Metadata with
    | Null -> ValueNone
    | NonNull metadata ->
      match metadata.TryGetValue(MetadataKey) with
      | true, (:? (UnionCasePromotionPlan array) as plans) -> ValueSome plans
      | true, _ -> failwith "Invalid union case promotion metadata payload."
      | false, _ -> ValueNone

  let remove (schema: OpenApiSchema) =
    match schema.Metadata with
    | Null -> ()
    | NonNull metadata -> metadata.Remove(MetadataKey) |> ignore

type UnionSchemaTransformer(fsOptions: JsonFSharpOptions) =
  interface IOpenApiSchemaTransformer with
    member _.TransformAsync(schema: OpenApiSchema, ctx: OpenApiSchemaTransformerContext, ct: CancellationToken) : Task =
      let t = ctx.JsonTypeInfo.Type
      let fsOptions = FsOptions.getEffectiveFsOptions t fsOptions

      if
        not (FSharpType.IsUnion(t))
        || UnionHelpers.isFSharpOptionType t
        || UnionHelpers.isFSharpValueOptionType t
        || UnionHelpers.isFSharpListType t
      then
        Task.CompletedTask
      else
          // Preserve schema identity so multi-case unions stay component schemas (not inlined everywhere).
          let originalId = schema.Id
          let originalMetadata = schema.Metadata
          let cases = FSharpType.GetUnionCases t

          let ignoreNullValues = OpenApiSchemaHelpers.ignoreNullValues ctx.JsonTypeInfo.Options

          let getOrCreateSchema (ty: Type) =
            OpenApiSchemaHelpers.getOrCreateDeterministicSchema ctx ct ty

          let getFieldPropertySchema (ty: Type) =
            task {
              match ty with
              | UnionHelpers.FSharpOptionType innerType when fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapOption ->
                let! innerSchema = getOrCreateSchema innerType
                let optionSchema = OpenApiSchema()
                SchemaCopy.copyTo optionSchema innerSchema
                OpenApiSchemaHelpers.addNull optionSchema innerSchema
                return optionSchema
              | _ -> return! getOrCreateSchema ty
            }

          let createUnionFieldsObjectSchema (uci: UnionCaseInfo) =
            task {
              let props = Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
              let required = HashSet<string>(StringComparer.Ordinal)

              let fieldInfos =
                UnionHelpers.getUnionCaseFieldNames fsOptions ctx.JsonTypeInfo.Options uci

              for (name, fieldInfo) in fieldInfos do
                let! fieldSchema = getFieldPropertySchema fieldInfo.PropertyType
                props[name] <- fieldSchema

                if not ignoreNullValues && not (UnionHelpers.isSkippableType fieldInfo.PropertyType) then
                  required.Add(name) |> ignore

              return
                OpenApiSchema(
                  Type = Nullable(JsonSchemaType.Object),
                  Properties = props,
                  Required = required,
                  AdditionalPropertiesAllowed = false
                )
            }

          let createEmptyArraySchema () =
            OpenApiSchema(
              Type = Nullable(JsonSchemaType.Array),
              Items = (OpenApiSchema(Type = Nullable(JsonSchemaType.String)) :> IOpenApiSchema),
              MinItems = 0,
              MaxItems = 0
            )

          let createTupleSchema (elementTypes: Type array) =
            let tupleType = FSharpType.MakeTupleType elementTypes
            getOrCreateSchema tupleType

          let createInternalTagNamedFieldsCaseSchema (uci: UnionCaseInfo) =
            task {
              let tagName = fsOptions.UnionTagName
              let caseTagValue = UnionHelpers.getUnionCaseTagValue fsOptions uci
              let fields = uci.GetFields()

              if fields.Length = 0 && fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapFieldlessTags then
                return OpenApiSchemaHelpers.makeConstEnumSchema caseTagValue
              else
                let props = Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                props[tagName] <- OpenApiSchemaHelpers.makeConstEnumSchema caseTagValue :> IOpenApiSchema

                let required = HashSet<string>(StringComparer.Ordinal)
                required.Add(tagName) |> ignore

                if
                  fields.Length = 1
                  && fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapRecordCases
                  && FSharpType.IsRecord fields[0].PropertyType
                then
                  let recordType = fields[0].PropertyType

                  let recordProps =
                    UnionHelpers.getRecordProperties fsOptions recordType
                    |> Array.filter (fun p -> p.GetCustomAttributes(typeof<JsonIgnoreAttribute>, true) |> Array.isEmpty)

                  for p in recordProps do
                    let name = UnionHelpers.getPropertyName ctx.JsonTypeInfo.Options p

                    let canBeSkipped =
                      ignoreNullValues || UnionHelpers.isSkippableType p.PropertyType

                    let effectiveType =
                      if UnionHelpers.isSkippableType p.PropertyType then
                        p.PropertyType.GetGenericArguments()[0]
                      else
                        p.PropertyType

                    let! fieldSchema = getFieldPropertySchema effectiveType

                    props[name] <- fieldSchema

                    if not canBeSkipped then
                      required.Add(name) |> ignore
                else
                  let fieldInfos =
                    UnionHelpers.getUnionCaseFieldNames fsOptions ctx.JsonTypeInfo.Options uci

                  for (name, fieldInfo) in fieldInfos do
                    let! fieldSchema = getFieldPropertySchema fieldInfo.PropertyType
                    props[name] <- fieldSchema

                    if not ignoreNullValues && not (UnionHelpers.isSkippableType fieldInfo.PropertyType) then
                      required.Add(name) |> ignore

                return
                  OpenApiSchema(
                    Type = Nullable(JsonSchemaType.Object),
                    Properties = props,
                    Required = required,
                    AdditionalPropertiesAllowed = false
                  )
            }

          let createAdjacentTagCaseSchema (uci: UnionCaseInfo) =
            let tagName = fsOptions.UnionTagName
            let fieldsName = fsOptions.UnionFieldsName
            let caseTagValue = UnionHelpers.getUnionCaseTagValue fsOptions uci
            let fields = uci.GetFields()

            let buildSchema fieldsSchemaOpt =
              let props = Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
              props[tagName] <- OpenApiSchemaHelpers.makeConstEnumSchema caseTagValue :> IOpenApiSchema

              let required = HashSet<string>(StringComparer.Ordinal)
              required.Add(tagName) |> ignore

              match fieldsSchemaOpt with
              | Some fieldsSchema ->
                props[fieldsName] <- fieldsSchema
                required.Add(fieldsName) |> ignore
              | None -> ()

              OpenApiSchema(
                Type = Nullable(JsonSchemaType.Object),
                Properties = props,
                Required = required,
                AdditionalPropertiesAllowed = false
              )

            if fields.Length = 0 && fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapFieldlessTags then
              Task.FromResult(OpenApiSchemaHelpers.makeConstEnumSchema caseTagValue)
            elif fields.Length = 0 then
              Task.FromResult(buildSchema None)
            else
              task {
                let! fieldsSchema =
                  if
                    fields.Length = 1
                    && (fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapSingleFieldCases
                        || (fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapRecordCases
                            && FSharpType.IsRecord fields[0].PropertyType))
                  then
                    getOrCreateSchema fields[0].PropertyType
                  elif fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.NamedFields then
                    createUnionFieldsObjectSchema uci
                  else
                    fields |> Array.map (fun f -> f.PropertyType) |> createTupleSchema

                return buildSchema (Some fieldsSchema)
              }

          let createExternalTagCaseSchema (uci: UnionCaseInfo) =
            task {
              let caseName = uci.Name
              let caseTagValue = UnionHelpers.getUnionCaseTagValue fsOptions uci
              let fields = uci.GetFields()

              if fields.Length = 0 && fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapFieldlessTags then
                return OpenApiSchemaHelpers.makeConstEnumSchema caseTagValue
              else
                let! payloadSchema =
                  if fields.Length = 1 && fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapSingleFieldCases then
                    getOrCreateSchema fields[0].PropertyType
                  elif fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.NamedFields then
                    createUnionFieldsObjectSchema uci
                  elif fields.Length = 0 then
                    task { return createEmptyArraySchema () }
                  else
                    fields |> Array.map (fun f -> f.PropertyType) |> createTupleSchema

                let props = Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                props[caseName] <- payloadSchema

                let required = HashSet<string>(StringComparer.Ordinal)
                required.Add(caseName) |> ignore

                return
                  OpenApiSchema(
                    Type = Nullable(JsonSchemaType.Object),
                    Properties = props,
                    Required = required,
                    AdditionalPropertiesAllowed = false
                  )
            }

          let createUntaggedCaseSchema (uci: UnionCaseInfo) =
            task {
              let caseTagValue = UnionHelpers.getUnionCaseTagValue fsOptions uci
              let fields = uci.GetFields()

              if fields.Length = 0 && fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapFieldlessTags then
                return OpenApiSchemaHelpers.makeConstEnumSchema caseTagValue
              else
                return! createUnionFieldsObjectSchema uci
            }

          let createInternalTagTupleCaseSchema (uci: UnionCaseInfo) =
            task {
              let caseTagValue = UnionHelpers.getUnionCaseTagValue fsOptions uci
              let fields = uci.GetFields()

              if fields.Length = 0 && fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapFieldlessTags then
                return OpenApiSchemaHelpers.makeConstEnumSchema caseTagValue
              else
                let fieldTypes = fields |> Array.map (fun f -> f.PropertyType)
                let distinctFieldTypes = fieldTypes |> Array.distinct

                let! fieldSchemas =
                  distinctFieldTypes
                  |> Array.map getOrCreateSchema
                  |> Task.WhenAll

                let tagSchema =
                  OpenApiSchemaHelpers.makeConstEnumSchema caseTagValue :> IOpenApiSchema

                let elementSchemas =
                  Array.append [| tagSchema |] (fieldSchemas |> Array.map (fun x -> x :> IOpenApiSchema))

                let hasPlainString =
                  elementSchemas
                  |> Seq.map OpenApiSchemaHelpers.unwrapReferenceSchema
                  |> Seq.exists (fun s -> s.Type = Nullable(JsonSchemaType.String) && OpenApiSchemaHelpers.enumIsEmpty s)

                let hasEnumString =
                  elementSchemas
                  |> Seq.map OpenApiSchemaHelpers.unwrapReferenceSchema
                  |> Seq.exists (fun s -> s.Type = Nullable(JsonSchemaType.String) && not (OpenApiSchemaHelpers.enumIsEmpty s))

                let itemUnionSchema =
                  if hasPlainString && hasEnumString then
                    OpenApiSchema(AnyOf = ResizeArray<IOpenApiSchema>(elementSchemas))
                  else
                    OpenApiSchema(OneOf = ResizeArray<IOpenApiSchema>(elementSchemas))

                let tupleSchema =
                  OpenApiSchema(
                    Type = Nullable(JsonSchemaType.Array),
                    Items = (itemUnionSchema :> IOpenApiSchema)
                  )

                return tupleSchema
            }

          let createCaseSchema (uci: UnionCaseInfo) =
            if fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.AdjacentTag then
              createAdjacentTagCaseSchema uci
            elif fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.ExternalTag then
              createExternalTagCaseSchema uci
            elif fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.InternalTag then
              if fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.NamedFields then
                createInternalTagNamedFieldsCaseSchema uci
              else
                createInternalTagTupleCaseSchema uci
            elif fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.Untagged then
              createUntaggedCaseSchema uci
            else
              createInternalTagNamedFieldsCaseSchema uci

          if
            fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapSingleCaseUnions
            && cases.Length = 1
          then
            match cases[0].GetFields() with
            | [| field |] ->
              task {
                let! innerSchema = getOrCreateSchema field.PropertyType
                SchemaCopy.copyTo schema innerSchema
              }
              :> Task
            | _ ->
              task {
                let! caseSchema = createCaseSchema cases[0]
                SchemaCopy.copyTo schema caseSchema
              }
              :> Task
          elif
            cases |> Seq.forall (fun x -> x.GetFields().Length = 0)
            && fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapFieldlessTags
          then
            let enumValues =
              cases
              |> Seq.map (UnionHelpers.getUnionCaseTagValue fsOptions)
              |> Seq.map (fun v ->
                match JsonValue.Create(v) with
                | null -> failwith "Failed to create enum value JSON node."
                | x -> x :> JsonNode)

            let enumSchema =
              OpenApiSchema(
                Type = Nullable(JsonSchemaType.String),
                Enum = ResizeArray<JsonNode>(enumValues)
              )

            SchemaCopy.copyTo schema enumSchema
            schema.Id <- originalId
            schema.Metadata <- originalMetadata
            Task.CompletedTask
          else
            task {
              let! casePromotionPlans =
                cases
                |> Array.map (fun caseInfo ->
                  task {
                    let! caseSchema = createCaseSchema caseInfo

                    return
                      { CaseName = caseInfo.Name
                        CaseSchema = caseSchema }
                  })
                |> Task.WhenAll

              let caseSchemas =
                casePromotionPlans
                |> Array.map (fun plan -> plan.CaseSchema :> IOpenApiSchema)

              let unionSchema =
                if
                  fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.InternalTag
                  && not (fsOptions.UnionEncoding.HasFlag JsonUnionEncoding.NamedFields)
                then
                  OpenApiSchema(AnyOf = ResizeArray<IOpenApiSchema>(caseSchemas))
                else
                  OpenApiSchema(OneOf = ResizeArray<IOpenApiSchema>(caseSchemas))

              UnionCasePromotionPlan.add unionSchema originalMetadata casePromotionPlans
              SchemaCopy.copyTo schema unionSchema
              schema.Id <- originalId
              schema.Metadata <- unionSchema.Metadata
            }
            :> Task

type UnionCaseComponentsDocumentTransformer() =
  interface IOpenApiDocumentTransformer with
    member _.TransformAsync(document: OpenApiDocument, _context: OpenApiDocumentTransformerContext, _ct: CancellationToken) : Task =
      task {
        let components =
          match document.Components with
          | Null ->
            let created = OpenApiComponents()
            document.Components <- created
            created
          | NonNull existing -> existing

        let schemas =
          match components.Schemas with
          | Null ->
            let created : IDictionary<string, IOpenApiSchema> =
              Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            components.Schemas <- created
            created
          | NonNull existing -> existing

        let componentSchemasSnapshot = schemas |> Seq.toArray

        for KeyValue(componentName, componentSchema) in componentSchemasSnapshot do
          match componentSchema with
          | :? OpenApiSchema as concreteSchema ->
            match UnionCasePromotionPlan.tryGet concreteSchema with
            | ValueSome promotionPlans when promotionPlans.Length > 0 ->
              for promotionPlan in promotionPlans do
                let caseSchemaId = $"{componentName}_{promotionPlan.CaseName}"
                schemas[caseSchemaId] <- promotionPlan.CaseSchema :> IOpenApiSchema

              let caseRefs =
                promotionPlans
                |> Seq.map (fun promotionPlan ->
                  OpenApiSchemaReference($"{componentName}_{promotionPlan.CaseName}", document) :> IOpenApiSchema)

              let promote (branches: IList<IOpenApiSchema>) =
                Seq.append caseRefs (branches |> Seq.filter OpenApiSchemaHelpers.isNullSchema)
                |> ResizeArray

              match concreteSchema.OneOf, concreteSchema.AnyOf with
              | NonNull oneOf, _ -> concreteSchema.OneOf <- promote oneOf
              | _, NonNull anyOf -> concreteSchema.AnyOf <- promote anyOf
              | _ -> failwith "Union promotion plan requires union schema to have oneOf or anyOf."

              UnionCasePromotionPlan.remove concreteSchema
            | _ -> ()
          | _ -> ()
      }
      :> Task
