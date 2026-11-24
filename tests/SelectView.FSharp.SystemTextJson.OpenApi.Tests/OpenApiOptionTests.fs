module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiOptionTests

open System
open System.Text.Json
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

type RecordWithOption = { [<JsonPropertyName "maybe">] Maybe: string option }

type RecordWithVoption = { [<JsonPropertyName "maybeV">] MaybeV: string voption }

[<JsonFSharpConverter(UnwrapOption = true)>]
type RecordWithUnwrapOverride = { [<JsonPropertyName "maybeV">] MaybeV: string voption }

type UnionWithVoption =
  | WithVoption of maybeV: string voption
  | WithNumber of n: int

type RecordWithOptionTuple = { [<JsonPropertyName "pair">] Pair: string option * int }

type PositionalOptionUnion =
  | PosPair of string option * int
  | PosEmpty

type SingleOptionFieldUnion =
  | OnlyOption of string option
  | OnlyNumber of int

type RecordWithNote =
  { [<JsonPropertyName "name">] Name: string
    [<JsonPropertyName "note">] Note: string option }

[<JsonFSharpConverter(SkippableOptionFields = SkippableOptionFields.Always)>]
type RecordSkippingNone =
  { [<JsonPropertyName "name">] Name: string
    [<JsonPropertyName "note">] Note: string option }

let private endpoints : Endpoint list =
  [
    POST [
      route "/option-string" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<string option>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<string option>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/option-record" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<InnerRecord option>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<InnerRecord option>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/record-with-option" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<RecordWithOption>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<RecordWithOption>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/record-with-voption" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<RecordWithVoption>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<RecordWithVoption>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/record-with-unwrap-override" (text "ok")
      |> addOpenApi(
        OpenApiConfig(requestBody = RequestBody(typeof<RecordWithUnwrapOverride>, contentTypes = [| "application/json" |]))
      )
    ]
    POST [
      route "/union-with-voption" (text "ok")
      |> addOpenApi(
        OpenApiConfig(requestBody = RequestBody(typeof<UnionWithVoption>, contentTypes = [| "application/json" |]))
      )
    ]
    POST [
      route "/record-with-option-tuple" (text "ok")
      |> addOpenApi(OpenApiConfig(requestBody = RequestBody(typeof<RecordWithOptionTuple>, contentTypes = [| "application/json" |])))
    ]
    POST [
      route "/positional-option-union" (text "ok")
      |> addOpenApi(OpenApiConfig(requestBody = RequestBody(typeof<PositionalOptionUnion>, contentTypes = [| "application/json" |])))
    ]
    POST [
      route "/single-option-field-union" (text "ok")
      |> addOpenApi(OpenApiConfig(requestBody = RequestBody(typeof<SingleOptionFieldUnion>, contentTypes = [| "application/json" |])))
    ]
    POST [
      route "/record-with-note" (text "ok")
      |> addOpenApi(OpenApiConfig(requestBody = RequestBody(typeof<RecordWithNote>, contentTypes = [| "application/json" |])))
    ]
    POST [
      route "/record-skipping-none" (text "ok")
      |> addOpenApi(OpenApiConfig(requestBody = RequestBody(typeof<RecordSkippingNone>, contentTypes = [| "application/json" |])))
    ]
  ]

let private shippedTransformers = shippedTransformersFor fsharpJsonOptions

let private serialize value = serializeWith fsharpJsonOptions value

let private getOptionPropertySchema (document: JsonNode) =
  getRecordPropertySchema document "/record-with-option" "maybe"

let private getVoptionPropertySchema (document: JsonNode) =
  getRecordPropertySchema document "/record-with-voption" "maybeV"

let private expectedOptionStringSchema =
  """
{
  "type": "string"
}
"""
  |> JsonNode.Parse
  |> fun node ->
    match node with
    | null -> failwith "Failed to parse expected JSON schema."
    | parsed -> parsed
  |> canonicalizeJson
  |> fun x -> x.ToJsonString(jsonOptions)

let private expectedOptionPropertySchema =
  """
{
  "type": [
    "null",
    "string"
  ]
}
"""
  |> JsonNode.Parse
  |> fun node ->
    match node with
    | null -> failwith "Failed to parse expected JSON schema."
    | parsed -> parsed
  |> canonicalizeJson
  |> fun x -> x.ToJsonString(jsonOptions)

[<Fact>]
let ``Option<string> schema matches snapshot`` () : Task =
  task {
    let! builtIn =
      fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints [ OptionSchemaTransformer(fsharpJsonOptions) ])

    let schema =
      getSchemaForPath builtIn "/option-string"
      |> canonicalizeJson
      |> fun x -> x.ToJsonString(jsonOptions)

    %schema.Should().Be(expectedOptionStringSchema)
  }

[<Fact>]
let ``option<string> property schema matches snapshot`` () : Task =
  task {
    let! builtIn =
      fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints shippedTransformers)

    let schema =
      getOptionPropertySchema builtIn
      |> canonicalizeJson
      |> fun x -> x.ToJsonString(jsonOptions)

    %schema.Should().Be(expectedOptionPropertySchema)
  }

[<Fact>]
let ``voption<string> property schema matches snapshot`` () : Task =
  task {
    let! builtIn =
      fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints shippedTransformers)

    let schema =
      getVoptionPropertySchema builtIn
      |> canonicalizeJson
      |> fun x -> x.ToJsonString(jsonOptions)

    %serialize({ RecordWithVoption.MaybeV = ValueNone }).Should().Be("""{"maybeV":null}""")
    %schema.Should().Be(expectedOptionPropertySchema)
  }

[<Fact>]
let ``voption<string> union field schema allows null`` () : Task =
  task {
    let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints shippedTransformers)

    let caseSchema =
      getSchemaForPath builtIn "/union-with-voption"
      |> expandSchemaRefs builtIn
      |> fun union -> getJsonPath union [ "oneOf" ] :?> JsonArray
      |> Seq.find (fun case -> not (isNull (getJsonPath case [ "properties" ]).["maybeV"]))

    let schema =
      getJsonPath caseSchema [ "properties"; "maybeV" ]
      |> canonicalizeJson
      |> fun x -> x.ToJsonString(jsonOptions)

    %serialize(WithVoption ValueNone).Should().Be("""{"kind":"WithVoption","maybeV":null}""")
    %schema.Should().Be(expectedOptionPropertySchema)
  }

[<Fact>]
let ``voption field follows the global UnwrapOption, not the parent record's override`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

    let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (shippedTransformersFor fsOptions))

    let schema =
      getRecordPropertySchema builtIn "/record-with-unwrap-override" "maybeV"
      |> expandSchemaRefs builtIn
      |> fun x -> x.ToJsonString()

    %(serializeWith fsOptions { RecordWithUnwrapOverride.MaybeV = ValueNone }).Should().Be("""{"maybeV":{"kind":"ValueNone"}}""")
    %schema.Should().Contain("\"ValueNone\"").And.NotContain("\"null\"")
  }

[<Fact>]
let ``Option<string> schema matches encoding (WithUnwrapOption disabled)`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

    let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints [ OptionSchemaTransformer(fsOptions) ])

    let schema = getSchemaForPath builtIn "/option-string" |> canonicalizeJson

    let types =
      getJsonPath schema [ "type" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected option schema to have a 'type' array."

    %types.Should().Be(Set.ofList [ "null"; "object" ])

    let required =
      getJsonPath schema [ "required" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected option schema to have a 'required' array."

    %required.Should().Be(Set.ofList [ "kind"; "value" ])

    let tagEnum =
      getJsonPath schema [ "properties"; "kind"; "enum" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Seq.toList
        | _ -> failwith "Expected option schema to have properties.kind.enum."

    %tagEnum.Should().Be([ "Some" ])

    let valueType =
      getJsonPath schema [ "properties"; "value"; "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected option schema properties.Value.type to be a string."

    %valueType.Should().Be("string")
  }

[<Fact>]
let ``Option<record> schema matches encoding (WithUnwrapOption disabled)`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

    let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints [ OptionSchemaTransformer(fsOptions) ])

    let schema = getSchemaForPath builtIn "/option-record" |> canonicalizeJson

    let types =
      getJsonPath schema [ "type" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected option schema to have a 'type' array."

    %types.Should().Be(Set.ofList [ "null"; "object" ])

    // Verify we still model `Some` as a tagged object, without asserting the full record schema details.
    let tagEnum =
      getJsonPath schema [ "properties"; "kind"; "enum" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Seq.toList
        | _ -> failwith "Expected option schema to have properties.kind.enum."

    %tagEnum.Should().Be([ "Some" ])

    match getJsonPath schema [ "properties"; "value" ] with
    | :? JsonObject as obj when not (isNull obj["$ref"]) -> ()
    | :? JsonObject as obj ->
      let valueType =
        getJsonPath obj [ "type" ]
        |> function
          | :? JsonValue as v -> v.GetValue<string>()
          | _ -> failwith "Expected option schema properties.Value.type to be a string."

      %valueType.Should().Be("object")
    | _ -> failwith "Expected option schema properties.Value to be an object schema."
  }

[<Fact>]
let ``voption<string> property schema matches encoding (WithUnwrapOption disabled)`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

    let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints [ OptionSchemaTransformer(fsOptions) ])

    let schema = getVoptionPropertySchema builtIn |> canonicalizeJson

    let cases =
      getJsonPath schema [ "oneOf" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.toList
        | _ -> failwith "Expected voption schema to have a 'oneOf' array."

    %cases.Length.Should().Be(2)

    let getCaseTag (caseSchema: JsonNode) =
      getJsonPath caseSchema [ "properties"; "kind"; "enum" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Seq.exactlyOne
        | _ -> failwith "Expected voption case schema to have a single enum value for 'kind'."

    let getCaseRequired (caseSchema: JsonNode) =
      getJsonPath caseSchema [ "required" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected voption case schema to have a 'required' array."

    let casesByTag =
      cases
      |> Seq.map (fun caseSchema -> getCaseTag caseSchema, caseSchema)
      |> readOnlyDict

    let valueNoneSchema =
      match casesByTag.TryGetValue("ValueNone") with
      | true, x -> x
      | false, _ -> failwith "Expected a voption 'ValueNone' case."

    %getCaseRequired(valueNoneSchema).Should().Be(Set.ofList [ "kind" ])

    let valueSomeSchema =
      match casesByTag.TryGetValue("ValueSome") with
      | true, x -> x
      | false, _ -> failwith "Expected a voption 'ValueSome' case."

    %getCaseRequired(valueSomeSchema).Should().Be(Set.ofList [ "kind"; "string" ])

    let valueSomeFieldType =
      getJsonPath valueSomeSchema [ "properties"; "string"; "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected voption 'ValueSome' schema to have properties.String.type."

    %valueSomeFieldType.Should().Be("string")
  }

let private positionalCases =
  [ "internal tag", JsonFSharpOptions.Default().WithUnionInternalTag(), """["PosPair",null,2]"""
    "adjacent tag", JsonFSharpOptions.Default(), """{"Case":"PosPair","Fields":[null,2]}"""
    "external tag", JsonFSharpOptions.Default().WithUnionExternalTag(), """{"PosPair":[null,2]}""" ]

[<Fact>]
let ``option in a tuple element allows null`` () : Task =
  task {
    let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints shippedTransformers)

    let schema =
      getRecordPropertySchema builtIn "/record-with-option-tuple" "pair" |> expandSchemaRefs builtIn

    %serialize({ RecordWithOptionTuple.Pair = (None, 1) }).Should().Be("""{"pair":[null,1]}""")
    %(allowsNull schema).Should().BeTrue()
  }

[<Fact>]
let ``option in a positional union field allows null`` () : Task =
  task {
    for (name, fsOptions, expectedJson) in positionalCases do
      let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (shippedTransformersFor fsOptions))

      let schema =
        getSchemaForPath builtIn "/positional-option-union" |> expandSchemaRefs builtIn

      %(serializeWith fsOptions (PosPair(None, 2))).Should().Be(expectedJson, name)
      %(allowsNull schema).Should().BeTrue(name)
  }

[<Fact>]
let ``option in an unwrapped single-field case allows null`` () : Task =
  task {
    let fsOptions = JsonFSharpOptions.Default().WithUnionUnwrapSingleFieldCases()

    let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (shippedTransformersFor fsOptions))

    let schema =
      getSchemaForPath builtIn "/single-option-field-union" |> expandSchemaRefs builtIn

    %(serializeWith fsOptions (OnlyOption None)).Should().Be("""{"Case":"OnlyOption","Fields":null}""")
    %(allowsNull schema).Should().BeTrue()
  }

[<Fact>]
let ``SkippableOptionFields decides whether an option field is required`` () : Task =
  task {
    let cases =
      [ "global Always", fsharpJsonOptions.WithSkippableOptionFields(SkippableOptionFields.Always), "/record-with-note",
        serializeWith (fsharpJsonOptions.WithSkippableOptionFields(SkippableOptionFields.Always)) { RecordWithNote.Name = "x"; Note = None },
        """{"name":"x"}""", Set.ofList [ "name" ]
        "attribute Always", fsharpJsonOptions, "/record-skipping-none",
        serializeWith fsharpJsonOptions { RecordSkippingNone.Name = "x"; Note = None },
        """{"name":"x"}""", Set.ofList [ "name" ]
        "default", fsharpJsonOptions, "/record-with-note",
        serializeWith fsharpJsonOptions { RecordWithNote.Name = "x"; Note = None },
        """{"name":"x","note":null}""", Set.ofList [ "name"; "note" ] ]

    for (name, fsOptions, path, json, expectedJson, expectedRequired) in cases do
      let! builtIn = fetchOpenApiJson (fun () -> createBuiltInOpenApiApp endpoints (shippedTransformersFor fsOptions))

      let record = getSchemaForPath builtIn path |> dereferenceSchema builtIn

      %json.Should().Be(expectedJson, name)
      %(requiredNames record).Should().Be(expectedRequired, name)
  }

