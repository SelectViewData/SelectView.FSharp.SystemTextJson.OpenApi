module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiUnionEncodingTests

open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Threading.Tasks
open Faqt
open Faqt.Operators
open SelectView.FSharp.SystemTextJson.OpenApi
open Giraffe
open Giraffe.EndpointRouting
open Giraffe.OpenApi
open Microsoft.AspNetCore.OpenApi
open SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiTestSetup
open Xunit

type InnerRecord = { Value: string }

type InternalTagTupleUnion =
  | Fieldless
  | Single of string
  | Pair of string * int

type AdjacentTagUnion =
  | [<JsonName("NoFieldsRenamed")>] Fieldless
  | Single of string
  | FromRecord of InnerRecord
  | Pair of string * int

type ExternalTagUnion =
  | Fieldless
  | Single of string
  | Pair of string * int

type UntaggedUnion =
  | [<JsonName("id", Field = "Int32")>] ById of int
  | [<JsonName("name", Field = "String")>] ByName of string

let private createTransformers fsOptions =
  [ OptionSchemaTransformer(fsOptions) :> IOpenApiSchemaTransformer
    CollectionsSchemaTransformer() :> IOpenApiSchemaTransformer
    RecordSchemaTransformer(fsOptions) :> IOpenApiSchemaTransformer
    TupleSchemaTransformer(fsOptions) :> IOpenApiSchemaTransformer
    UnionSchemaTransformer(fsOptions) :> IOpenApiSchemaTransformer ]

let rec private collectPropertyNames (node: JsonNode) = seq {
  match node with
  | :? JsonObject as obj ->
    match obj["properties"] with
    | :? JsonObject as properties -> yield! properties |> Seq.map _.Key
    | _ -> ()

    for property in obj do
      if not (isNull property.Value) then
        yield! collectPropertyNames property.Value
  | :? JsonArray as values ->
    for value in values do
      if not (isNull value) then
        yield! collectPropertyNames value
  | _ -> ()
}
let rec private collectEnumStrings (node: JsonNode) = seq {
  match node with
  | :? JsonObject as obj ->
    match obj["enum"] with
    | :? JsonArray as values ->
      for value in values do
        match value with
        | :? JsonValue as enumValue ->
          match enumValue.TryGetValue<string>() with
          | true, enumString -> yield enumString
          | _ -> ()
        | _ -> ()
    | _ -> ()

    for property in obj do
      if not (isNull property.Value) then
        yield! collectEnumStrings property.Value
  | :? JsonArray as values ->
    for value in values do
      if not (isNull value) then
        yield! collectEnumStrings value
  | _ -> ()
}

let private getExpandedSchema document path =
  getSchemaForPath document path |> expandSchemaRefs document

let private getSchemaTypes (schema: JsonNode) =
  match schema["type"] with
  | :? JsonValue as value -> Set.singleton (value.GetValue<string>())
  | :? JsonArray as values ->
    values
    |> Seq.choose (function
      | :? JsonValue as value -> Some(value.GetValue<string>())
      | _ -> None)
    |> Set.ofSeq
  | _ -> Set.empty

let private getRequiredPropertyNames (schema: JsonNode) =
  schema["required"] :?> JsonArray
  |> Seq.map (fun value -> (value :?> JsonValue).GetValue<string>())
  |> Set.ofSeq

[<Fact>]
let ``InternalTag without NamedFields uses tuple cases under anyOf`` () : Task = task {
  let fsOptions =
    fsharpJsonOptions.WithUnwrapOption(false).WithUnionNamedFields(false)

  let endpoints: Endpoint list = [
    POST [
      route "/union-internal-tag-tuple" (text "ok")
      |> addOpenApi (
        OpenApiConfig(
          requestBody = RequestBody(typeof<InternalTagTupleUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [
            ResponseBody(typeof<InternalTagTupleUnion>, contentTypes = [| "application/json" |])
          ]
        )
      )
    ]
  ]

  let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (createTransformers fsOptions))

  let schema = getExpandedSchema builtIn "/union-internal-tag-tuple"

  match schema["anyOf"] with
  | :? JsonArray as cases -> %(cases.Count).Should().Be(3)
  | _ -> failwith "Expected InternalTag without NamedFields to use anyOf at the union root."

  let caseTags = collectEnumStrings schema |> Set.ofSeq
  %caseTags.Should().Be(Set.ofList [ "Fieldless"; "Single"; "Pair" ])
}

[<Fact>]
let ``AdjacentTag union uses configured tag and fields properties`` () : Task = task {
  let fsOptions =
    JsonFSharpOptions
      .Default()
      .WithUnwrapOption(false)
      .WithUnionAdjacentTag()
      .WithUnionTagName("kind")
      .WithUnionFieldsName("data")
      .WithUnionFieldNamesFromTypes()
      .WithUnionUnwrapRecordCases()
      .WithUnionUnwrapSingleFieldCases()

  let endpoints: Endpoint list = [
    POST [
      route "/union-adjacent-tag" (text "ok")
      |> addOpenApi (
        OpenApiConfig(
          requestBody = RequestBody(typeof<AdjacentTagUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [
            ResponseBody(typeof<AdjacentTagUnion>, contentTypes = [| "application/json" |])
          ]
        )
      )
    ]
  ]

  let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (createTransformers fsOptions))

  let schema = getExpandedSchema builtIn "/union-adjacent-tag"

  let cases =
    match schema["oneOf"] with
    | :? JsonArray as values -> values
    | _ -> failwith "Expected AdjacentTag union to use oneOf at the union root."

  %(cases.Count).Should().Be(4)

  let getCaseProperties caseTag =
    let matchingCases =
      cases
      |> Seq.choose (function
        | :? JsonObject as caseSchema ->
          match caseSchema["properties"] with
          | :? JsonObject as properties when collectEnumStrings properties["kind"] |> Seq.contains caseTag ->
            Some(caseSchema, properties)
          | _ -> None
        | _ -> None)
      |> Seq.toList

    match matchingCases with
    | [ caseSchema, properties ] -> caseSchema, properties
    | _ -> failwith $"Expected exactly one AdjacentTag branch for case '%s{caseTag}'."

  let fieldlessCase, fieldlessProperties = getCaseProperties "NoFieldsRenamed"
  %(fieldlessProperties.Count).Should().Be(1)
  %(getRequiredPropertyNames fieldlessCase).Should().Be(Set.singleton "kind")
  %(isNull fieldlessProperties["data"]).Should().BeTrue()

  let singleCase, singleProperties = getCaseProperties "Single"
  %(singleProperties.Count).Should().Be(2)
  %(getRequiredPropertyNames singleCase).Should().Be(Set.ofList [ "kind"; "data" ])
  %(getSchemaTypes singleProperties["data"] |> Set.contains "string").Should().BeTrue()

  let recordCase, recordProperties = getCaseProperties "FromRecord"
  %(recordProperties.Count).Should().Be(2)
  %(getRequiredPropertyNames recordCase).Should().Be(Set.ofList [ "kind"; "data" ])

  let recordPayload = recordProperties["data"]
  %(getSchemaTypes recordPayload |> Set.contains "object").Should().BeTrue()

  let recordPayloadProperties = recordPayload["properties"] :?> JsonObject
  %(recordPayloadProperties.Count).Should().Be(1)
  %(getRequiredPropertyNames recordPayload).Should().Be(Set.singleton "value")
  %(getSchemaTypes recordPayloadProperties["value"] |> Set.contains "string").Should().BeTrue()

  let pairCase, pairProperties = getCaseProperties "Pair"
  %(pairProperties.Count).Should().Be(2)
  %(getRequiredPropertyNames pairCase).Should().Be(Set.ofList [ "kind"; "data" ])

  let pairPayload = pairProperties["data"]
  %(getSchemaTypes pairPayload |> Set.contains "object").Should().BeTrue()

  let pairPayloadProperties = pairPayload["properties"] :?> JsonObject
  %(pairPayloadProperties.Count).Should().Be(2)
  %(getRequiredPropertyNames pairPayload).Should().Be(pairPayloadProperties |> Seq.map _.Key |> Set.ofSeq)

  let pairPayloadTypes =
    pairPayloadProperties
    |> Seq.collect (fun property -> getSchemaTypes property.Value)
    |> Set.ofSeq

  %pairPayloadTypes.Should().Be(Set.ofList [ "integer"; "string" ])
}

[<Fact>]
let ``ExternalTag union uses case names as object properties`` () : Task = task {
  let fsOptions =
    JsonFSharpOptions
      .Default()
      .WithUnwrapOption(false)
      .WithUnionExternalTag()
      .WithUnionNamedFields(true)
      .WithUnionFieldNamesFromTypes()
      .WithUnionUnwrapSingleFieldCases()

  let endpoints: Endpoint list = [
    POST [
      route "/union-external-tag" (text "ok")
      |> addOpenApi (
        OpenApiConfig(
          requestBody = RequestBody(typeof<ExternalTagUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [
            ResponseBody(typeof<ExternalTagUnion>, contentTypes = [| "application/json" |])
          ]
        )
      )
    ]
  ]

  let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (createTransformers fsOptions))

  let schema = getExpandedSchema builtIn "/union-external-tag"

  let cases =
    match schema["oneOf"] with
    | :? JsonArray as values -> values
    | _ -> failwith "Expected ExternalTag union to use oneOf at the union root."

  %(cases.Count).Should().Be(3)

  let getCasePayload caseName =
    let matchingCases =
      cases
      |> Seq.choose (function
        | :? JsonObject as caseSchema ->
          match caseSchema["properties"] with
          | :? JsonObject as properties when properties.ContainsKey(caseName) -> Some(caseSchema, properties)
          | _ -> None
        | _ -> None)
      |> Seq.toList

    match matchingCases with
    | [ caseSchema, properties ] ->
      %(properties.Count).Should().Be(1)

      %(getRequiredPropertyNames caseSchema).Should().Be(Set.singleton caseName)
      properties[caseName]
    | _ -> failwith $"Expected exactly one ExternalTag branch for case '%s{caseName}'."

  let fieldlessPayload = getCasePayload "Fieldless"
  %(getSchemaTypes fieldlessPayload |> Set.contains "object").Should().BeTrue()
  %(isNull fieldlessPayload["properties"]).Should().BeTrue()

  let singlePayload = getCasePayload "Single"
  %(getSchemaTypes singlePayload |> Set.contains "string").Should().BeTrue()

  let pairPayload = getCasePayload "Pair"
  %(getSchemaTypes pairPayload |> Set.contains "object").Should().BeTrue()

  let pairProperties = pairPayload["properties"] :?> JsonObject
  %(pairProperties.Count).Should().Be(2)

  let pairPropertyTypes =
    pairProperties
    |> Seq.collect (fun property -> getSchemaTypes property.Value)
    |> Set.ofSeq

  %pairPropertyTypes.Should().Be(Set.ofList [ "integer"; "string" ])

  let requiredPairProperties = getRequiredPropertyNames pairPayload
  %(requiredPairProperties.Count).Should().Be(2)
  %requiredPairProperties.Should().Be(pairProperties |> Seq.map _.Key |> Set.ofSeq)
}

[<Fact>]
let ``Untagged union exposes case fields without a discriminator`` () : Task = task {
  let fsOptions =
    JsonFSharpOptions.Default().WithUnwrapOption(false).WithUnionUntagged().WithUnionFieldNamesFromTypes()

  let endpoints: Endpoint list = [
    POST [
      route "/union-untagged" (text "ok")
      |> addOpenApi (
        OpenApiConfig(
          requestBody = RequestBody(typeof<UntaggedUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<UntaggedUnion>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

  let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (createTransformers fsOptions))

  let propertyNames =
    getExpandedSchema builtIn "/union-untagged" |> collectPropertyNames |> Set.ofSeq

  %propertyNames.Should().Be(Set.ofList [ "id"; "name" ])
}
