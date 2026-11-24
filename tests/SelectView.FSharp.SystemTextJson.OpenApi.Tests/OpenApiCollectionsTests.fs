module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiCollectionsTests

open System.Threading.Tasks
open System.Text.Json.Nodes
open Faqt
open Faqt.Operators
open SelectView.FSharp.SystemTextJson.OpenApi
open Giraffe
open Giraffe.EndpointRouting
open Giraffe.OpenApi
open SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiTestSetup
open Xunit

type WrappedString = WrappedString of string

let private endpoints : Endpoint list =
  [
    POST [
      route "/list-int" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<int list>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<int list>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/set-string" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<Set<string>>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<Set<string>>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/map-string-int" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<Map<string, int>>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<Map<string, int>>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/map-wrapped-int" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<Map<WrappedString, int>>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<Map<WrappedString, int>>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/map-int-string" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<Map<int, string>>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<Map<int, string>>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/array-int" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<int[]>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<int[]>, contentTypes = [| "application/json" |]) ]
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
let ``Collections use array and dictionary schemas matching their JSON encoding`` () : Task = task {
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

  let getSchema path =
    getSchemaForPath builtIn path |> expandSchemaRefs builtIn

  let assertArray path itemType =
    let schema = getSchema path
    %(getSchemaTypes schema |> Set.contains "array").Should().BeTrue()
    %(getSchemaTypes schema["items"] |> Set.contains itemType).Should().BeTrue()

  assertArray "/list-int" "integer"
  assertArray "/set-string" "string"
  assertArray "/array-int" "integer"

  for path in [ "/map-string-int"; "/map-wrapped-int" ] do
    let schema = getSchema path
    %(getSchemaTypes schema |> Set.contains "object").Should().BeTrue()
    %(getSchemaTypes schema["additionalProperties"] |> Set.contains "integer").Should().BeTrue()

  let nonStringMap = getSchema "/map-int-string"
  %(getSchemaTypes nonStringMap |> Set.contains "array").Should().BeTrue()

  let entryTuple = nonStringMap["items"]
  %(getSchemaTypes entryTuple |> Set.contains "array").Should().BeTrue()
  %((entryTuple["minItems"] :?> JsonValue).GetValue<int>()).Should().Be(2)
  %((entryTuple["maxItems"] :?> JsonValue).GetValue<int>()).Should().Be(2)

  let entryItemTypes =
    entryTuple["items"]["anyOf"] :?> JsonArray
    |> Seq.collect getSchemaTypes
    |> Set.ofSeq

  %entryItemTypes.Should().Be(Set.ofList [ "integer"; "string" ])
}
