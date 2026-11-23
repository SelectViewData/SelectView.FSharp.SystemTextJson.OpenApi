module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiRecordTests

open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Threading.Tasks
open Faqt
open Faqt.Operators
open SelectView.FSharp.SystemTextJson.OpenApi
open Giraffe
open Giraffe.EndpointRouting
open Giraffe.OpenApi
open SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiTestSetup
open Xunit

type SampleRecord = {
  Name: string
  Count: int
  [<JsonPropertyName "renamed">]
  Renamed: int
}

type SampleJsonObjectRecord = { Data: JsonObject }

[<CLIMutable>]
type RequiredSkippableRecord = {
  [<JsonRequired>]
  Value: Skippable<string>
}

let private endpoints: Endpoint list = [
  POST [
    route "/record-sample" (text "ok")
    |> addOpenApi (
      OpenApiConfig(
        requestBody = RequestBody(typeof<SampleRecord>, contentTypes = [| "application/json" |]),
        responseBodies = [ ResponseBody(typeof<SampleRecord>, contentTypes = [| "application/json" |]) ]
      )
    )
  ]
]

let private assertSchemaIsRef (schema: JsonNode) =
  match schema with
  | :? JsonObject as obj ->
    match obj["$ref"] with
    | :? JsonValue as refValue ->
      let refString = refValue.GetValue<string>()
      %refString.Should().Contain("#/components/schemas/")
    | _ -> failwith "Expected schema to be a $ref object."
  | _ -> failwith "Expected schema to be a JSON object."

let private jsonObjectEndpoints: Endpoint list = [
  POST [
    route "/json-object-sample" (text "ok")
    |> addOpenApi (
      OpenApiConfig(
        requestBody = RequestBody(typeof<SampleJsonObjectRecord>, contentTypes = [| "application/json" |]),
        responseBodies = [
          ResponseBody(typeof<SampleJsonObjectRecord>, contentTypes = [| "application/json" |])
        ]
      )
    )
  ]
]

let private requiredSkippableEndpoints: Endpoint list = [
  POST [
    route "/required-skippable" (text "ok")
    |> addOpenApi (
      OpenApiConfig(
        requestBody = RequestBody(typeof<RequiredSkippableRecord>, contentTypes = [| "application/json" |])
      )
    )
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

[<Fact>]
let ``Record schema exposes fields, JSON names, and required properties`` () : Task = task {
  let fsOptions = fsharpJsonOptions

  let! builtIn =
    fetchOpenApiJson (fun () ->
      createBuiltInOpenApiApp endpoints [
        OptionSchemaTransformer(fsOptions)
        UnionSchemaTransformer(fsOptions)
        RecordSchemaTransformer(fsOptions)
        TupleSchemaTransformer(fsOptions)
      ])

  let schema = getSchemaForPath builtIn "/record-sample" |> expandSchemaRefs builtIn

  let properties = schema["properties"] :?> JsonObject
  %(properties.Count).Should().Be(3)
  %(properties.ContainsKey("name")).Should().BeTrue()
  %(properties.ContainsKey("count")).Should().BeTrue()
  %(properties.ContainsKey("renamed")).Should().BeTrue()
  %(getSchemaTypes properties["name"] |> Set.contains "string").Should().BeTrue()
  %(getSchemaTypes properties["count"] |> Set.contains "integer").Should().BeTrue()
  %(getSchemaTypes properties["renamed"] |> Set.contains "integer").Should().BeTrue()

  let required =
    schema["required"] :?> JsonArray |> Seq.map _.GetValue<string>() |> Set.ofSeq

  %required.Should().Be(Set.ofList [ "name"; "count"; "renamed" ])
}

[<Fact>]
let ``Record schema is referenced (not inlined)`` () : Task = task {
  let fsOptions = fsharpJsonOptions

  let! builtIn =
    fetchOpenApiJson (fun () ->
      createBuiltInOpenApiApp endpoints [
        OptionSchemaTransformer(fsOptions)
        UnionSchemaTransformer(fsOptions)
        RecordSchemaTransformer(fsOptions)
        TupleSchemaTransformer(fsOptions)
      ])

  getSchemaForPath builtIn "/record-sample" |> assertSchemaIsRef
  getResponseSchemaForPath builtIn "/record-sample" 200 |> assertSchemaIsRef
}

[<Fact>]
let ``JsonObject record field allows arbitrary object properties`` () : Task = task {
  let fsOptions = fsharpJsonOptions

  let! builtIn =
    fetchOpenApiJson (fun () ->
      createBuiltInOpenApiApp jsonObjectEndpoints [
        OptionSchemaTransformer(fsOptions)
        JsonNodeSchemaTransformer()
        CollectionsSchemaTransformer(fsOptions)
        RecordSchemaTransformer(fsOptions)
        TupleSchemaTransformer(fsOptions)
      ])

  let schema =
    getSchemaForPath builtIn "/json-object-sample" |> expandSchemaRefs builtIn

  let dataSchema = getJsonPath schema [ "properties"; "data" ]

  %((dataSchema["type"] :?> JsonValue).GetValue<string>()).Should().Be("object")

  match dataSchema["additionalProperties"] with
  | :? JsonObject -> ()
  | _ -> failwith "Expected JsonObject schema to allow arbitrary object properties."
}

[<Fact>]
let ``JsonRequired keeps a Skippable record field required`` () : Task = task {
  let fsOptions = fsharpJsonOptions

  let! builtIn =
    fetchOpenApiJson (fun () ->
      createBuiltInOpenApiApp requiredSkippableEndpoints [
        OptionSchemaTransformer(fsOptions)
        UnionSchemaTransformer(fsOptions)
        RecordSchemaTransformer(fsOptions)
      ])

  let schema =
    getSchemaForPath builtIn "/required-skippable" |> expandSchemaRefs builtIn

  let required = schema["required"] :?> JsonArray

  required
  |> Seq.map (fun value -> value.GetValue<string>())
  |> Seq.contains "value"
  |> _.Should().BeTrue()
  |> ignore
}
