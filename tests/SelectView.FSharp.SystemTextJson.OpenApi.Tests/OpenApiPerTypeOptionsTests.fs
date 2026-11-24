module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiPerTypeOptionsTests

open System.Text.Json.Serialization
open System.Text.Json.Nodes
open System.Threading.Tasks
open Faqt
open Faqt.Operators
open SelectView.FSharp.SystemTextJson.OpenApi
open Giraffe
open Giraffe.EndpointRouting
open Giraffe.OpenApi
open SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiTestSetup
open Xunit

[<JsonFSharpConverter(IncludeRecordProperties = true)>]
type RecordWithComputed =
  { Name: string }

  member _.Computed = 123

[<JsonFSharpConverter(
    UnionEncoding = (JsonUnionEncoding.ExternalTag ||| JsonUnionEncoding.NamedFields),
    UnionFieldNamesFromTypes = true,
    UnionTagName = "kind",
    UnionFieldsName = "data",
    UnwrapOption = false
  )>]
type ExternalTagUnionPerType =
  | Fieldless
  | Pair of string * int

[<JsonFSharpConverter(UnionTagName = "type")>]
type InheritingShape =
  | Circle of radius: float
  | Dot

type OverriddenShape =
  | Square of side: float
  | NoShape

let private endpoints : Endpoint list =
  [
    POST [
      route "/per-type-record" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<RecordWithComputed>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<RecordWithComputed>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/per-type-union" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<ExternalTagUnionPerType>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<ExternalTagUnionPerType>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/inheriting-shape" (text "ok")
      |> addOpenApi(OpenApiConfig(requestBody = RequestBody(typeof<InheritingShape>, contentTypes = [| "application/json" |])))
    ]
    POST [
      route "/overridden-shape" (text "ok")
      |> addOpenApi(OpenApiConfig(requestBody = RequestBody(typeof<OverriddenShape>, contentTypes = [| "application/json" |])))
    ]
  ]

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
[<Fact>]
let ``Per-type JsonFSharpConverterAttribute options shape record and union schemas`` () : Task = task {
  let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

  let! builtIn =
    fetchOpenApiJson (fun () ->
      createBuiltInOpenApiApp endpoints [
        OptionSchemaTransformer(fsOptions)
        CollectionsSchemaTransformer()
        RecordSchemaTransformer(fsOptions)
        TupleSchemaTransformer(fsOptions)
        UnionSchemaTransformer(fsOptions)
      ])

  let recordSchema =
    getSchemaForPath builtIn "/per-type-record" |> expandSchemaRefs builtIn

  let recordProperties = recordSchema["properties"] :?> JsonObject
  %(recordProperties.ContainsKey("name")).Should().BeTrue()
  %(recordProperties.ContainsKey("computed")).Should().BeTrue()
  %(getSchemaTypes recordProperties["computed"] |> Set.contains "integer").Should().BeTrue()

  let unionProperties =
    getSchemaForPath builtIn "/per-type-union"
    |> expandSchemaRefs builtIn
    |> collectPropertyNames
    |> Set.ofSeq

  %(unionProperties.Contains("Fieldless")).Should().BeTrue()
  %(unionProperties.Contains("Pair")).Should().BeTrue()
}

let private shapeCases =
  let withOverride =
    fsharpJsonOptions.WithOverrides(
      dict [ typeof<OverriddenShape>, JsonFSharpOptions.InheritUnionEncoding().WithUnionTagName("shape") ]
    )

  [ "attribute inherits the union encoding", fsharpJsonOptions, "/inheriting-shape",
    serializeWith fsharpJsonOptions (Circle 1.0), """{"type":"Circle","radius":1}""", "type", "kind"
    "WithOverrides applies", withOverride, "/overridden-shape",
    serializeWith withOverride (Square 1.0), """{"shape":"Square","side":1}""", "shape", "kind"
    "attribute ignored without AllowOverride", fsharpJsonOptions.WithAllowOverride(false), "/inheriting-shape",
    serializeWith (fsharpJsonOptions.WithAllowOverride(false)) (Circle 1.0), """{"kind":"Circle","radius":1}""", "kind", "type" ]

[<Fact>]
let ``Per-type options merge with the global options like FSharp.SystemTextJson`` () : Task = task {
  for (name, fsOptions, path, json, expectedJson, tag, absentTag) in shapeCases do
    let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (shippedTransformersFor fsOptions))

    let names = getSchemaForPath builtIn path |> expandSchemaRefs builtIn |> collectPropertyNames |> Set.ofSeq

    %json.Should().Be(expectedJson, name)
    %(names.Contains tag).Should().BeTrue(name)
    %(names.Contains absentTag).Should().BeFalse(name)
    %(names.Contains "Fields").Should().BeFalse(name)
}

