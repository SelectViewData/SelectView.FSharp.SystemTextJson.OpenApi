module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiEnumTests

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

[<JsonConverter(typeof<JsonStringEnumConverter>)>]
type StringEnum =
  | Only = 0

type IntEnum =
  | Only = 0

let private endpoints : Endpoint list =
  [
    POST [
      route "/enum-string" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<StringEnum>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<StringEnum>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/enum-int" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<IntEnum>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<IntEnum>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

let private getTypeSet (schema: JsonNode) =
  match getJsonPath schema [ "type" ] with
  | :? JsonValue as v -> Set.ofList [ v.GetValue<string>() ]
  | :? JsonArray as arr ->
    arr
    |> Seq.choose (function
      | :? JsonValue as v -> Some(v.GetValue<string>())
      | _ -> None)
    |> Set.ofSeq
  | _ -> Set.empty

let private getFormat (schema: JsonNode) =
  match getJsonPath schema [ "format" ] with
  | :? JsonValue as v -> Some(v.GetValue<string>())
  | _ -> None

let private getEnumStrings (schema: JsonNode) =
  getJsonPath schema [ "enum" ]
  |> function
    | :? JsonArray as arr ->
      arr
      |> Seq.choose (function
        | :? JsonValue as v -> Some(v.GetValue<string>())
        | _ -> None)
      |> Seq.toList
    | _ -> []

let private getEnumNumbers (schema: JsonNode) =
  getJsonPath schema [ "enum" ]
  |> function
    | :? JsonArray as arr ->
      arr
      |> Seq.choose (function
        | :? JsonValue as v -> Some(v.GetValue<int64>())
        | _ -> None)
      |> Seq.toList
    | _ -> []

[<Fact>]
let ``Numeric enums include explicit values and int32 format`` () : Task =
  task {
    let! builtIn =
      fetchOpenApiJson (
        fun () -> createBuiltInOpenApiApp endpoints [ EnumSchemaTransformer() :> Microsoft.AspNetCore.OpenApi.IOpenApiSchemaTransformer ]
      )

    let schema =
      getSchemaForPath builtIn "/enum-int"
      |> expandSchemaRefs builtIn
      |> canonicalizeJson

    %(getTypeSet schema |> Set.contains "integer").Should().BeTrue()
    %(getFormat schema).Should().Be(Some "int32")
    %(getEnumNumbers schema).Should().Be([ 0L ])
  }

[<Fact>]
let ``String enums include explicit type`` () : Task =
  task {
    let! builtIn =
      fetchOpenApiJson (
        fun () -> createBuiltInOpenApiApp endpoints [ EnumSchemaTransformer() :> Microsoft.AspNetCore.OpenApi.IOpenApiSchemaTransformer ]
      )

    let schema =
      getSchemaForPath builtIn "/enum-string"
      |> expandSchemaRefs builtIn
      |> canonicalizeJson

    %(getTypeSet schema |> Set.contains "string").Should().BeTrue()
    %(getEnumStrings schema).Should().Be([ "Only" ])
  }
