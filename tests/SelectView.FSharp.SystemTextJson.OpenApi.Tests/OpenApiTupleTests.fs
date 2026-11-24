module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiTupleTests

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

type SampleTuple = string * int

let private endpoints : Endpoint list =
  [
    POST [
      route "/tuple-sample" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<SampleTuple>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<SampleTuple>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

let private assertSchemaIsInline (schema: JsonNode) =
  match schema with
  | :? JsonObject as obj when not (isNull obj["$ref"]) -> failwith "Expected schema to be inlined, got a $ref."
  | _ -> ()

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
let ``Tuple schema is a fixed-length array of its element types`` () : Task = task {
  let fsOptions = fsharpJsonOptions

  let! builtIn =
    fetchOpenApiJson (fun () ->
      createBuiltInOpenApiApp endpoints [
        OptionSchemaTransformer(fsOptions)
        UnionSchemaTransformer(fsOptions)
        RecordSchemaTransformer(fsOptions)
        TupleSchemaTransformer()
      ])

  let schema = getSchemaForPath builtIn "/tuple-sample" |> expandSchemaRefs builtIn

  %(getSchemaTypes schema |> Set.contains "array").Should().BeTrue()
  %((schema["minItems"] :?> JsonValue).GetValue<int>()).Should().Be(2)
  %((schema["maxItems"] :?> JsonValue).GetValue<int>()).Should().Be(2)

  let itemAlternatives = schema["items"]["oneOf"] :?> JsonArray

  let itemTypes = itemAlternatives |> Seq.collect getSchemaTypes |> Set.ofSeq

  %itemTypes.Should().Be(Set.ofList [ "integer"; "string" ])
}

[<Fact>]
let ``Tuple schema is inlined`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions

    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            endpoints
            [ OptionSchemaTransformer(fsOptions)
              UnionSchemaTransformer(fsOptions)
              RecordSchemaTransformer(fsOptions)
              TupleSchemaTransformer() ]
      )

    getSchemaForPath builtIn "/tuple-sample" |> assertSchemaIsInline
    getResponseSchemaForPath builtIn "/tuple-sample" 200 |> assertSchemaIsInline
  }
