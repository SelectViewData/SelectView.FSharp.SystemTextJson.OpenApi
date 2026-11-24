module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiUnionTests

open System
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

type InnerRecord = { Value: string }

type SampleUnion =
  | Fieldless
  | Single of string
  | Pair of string * int
  | FromRecord of InnerRecord

type FieldlessSingleCaseUnion =
  | NotConfigured

type SingleFieldSingleCaseUnion =
  | MyId of int

type AllFieldlessUnion =
  | Alpha
  | Beta

type GenericEnvelope<'T> =
  | Missing
  | Loaded of 'T

type ResultStringInt = Result<string, int>
type ResultIntGuid = Result<int, Guid>
type EnvelopeOfResultArray = GenericEnvelope<ResultStringInt array>
type EnvelopeOfGuidOption = GenericEnvelope<Guid option>

type OptionalPayloadUnion =
  | WithReason of reason: string
  | Plain

type RecordWithOptionalUnion =
  { [<System.Text.Json.Serialization.JsonPropertyName "attention">]
    Attention: OptionalPayloadUnion option }

[<JsonFSharpConverter(unionEncoding = JsonUnionEncoding.UnwrapFieldlessTags)>]
type StringEnumUnion =
  | [<JsonName("first")>] First
  | [<JsonName("second")>] Second

type RecordWithOptionalStringEnum =
  { [<JsonPropertyName "status">]
    Status: StringEnumUnion option }

let private endpoints : Endpoint list =
  [
    POST [
      route "/union-sample" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<SampleUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<SampleUnion>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

let private genericResultEndpoints : Endpoint list =
  [
    POST [
      route "/union-result-string-int" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<ResultStringInt>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<ResultStringInt>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/union-result-int-guid" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<ResultIntGuid>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<ResultIntGuid>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

let private genericEnvelopeEndpoints : Endpoint list =
  [
    POST [
      route "/union-envelope-result-array" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<EnvelopeOfResultArray>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<EnvelopeOfResultArray>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/union-envelope-guid-option" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<EnvelopeOfGuidOption>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<EnvelopeOfGuidOption>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

let private allFieldlessEndpoints : Endpoint list =
  [
    POST [
      route "/union-all-fieldless" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<AllFieldlessUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<AllFieldlessUnion>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

let private singleCaseEndpoints : Endpoint list =
  [
    POST [
      route "/union-single-case-fieldless" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<FieldlessSingleCaseUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<FieldlessSingleCaseUnion>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
    POST [
      route "/union-single-case-single-field" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<SingleFieldSingleCaseUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<SingleFieldSingleCaseUnion>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

let private optionalUnionEndpoints : Endpoint list =
  [
    POST [
      route "/record-with-optional-union" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<RecordWithOptionalUnion>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<RecordWithOptionalUnion>, contentTypes = [| "application/json" |]) ]
        )
      )
    ]
  ]

let private optionalStringEnumEndpoints : Endpoint list =
  [
    POST [
      route "/record-with-optional-string-enum" (text "ok")
      |> addOpenApi(
        OpenApiConfig(
          requestBody = RequestBody(typeof<RecordWithOptionalStringEnum>, contentTypes = [| "application/json" |]),
          responseBodies = [ ResponseBody(typeof<RecordWithOptionalStringEnum>, contentTypes = [| "application/json" |]) ]
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

let private assertSchemaIsInline (schema: JsonNode) =
  match schema with
  | :? JsonObject as obj when not (isNull obj["$ref"]) -> failwith "Expected schema to be inlined, got a $ref."
  | _ -> ()

let private getSchemaRefString (schema: JsonNode) =
  match schema with
  | :? JsonObject as obj ->
    match obj["$ref"] with
    | :? JsonValue as refValue -> refValue.GetValue<string>()
    | _ -> failwith "Expected schema to be a $ref object."
  | _ -> failwith "Expected schema to be a JSON object."

let private getSchemaIdFromRef (refString: string) =
  refString.Split('/') |> Array.last

let private getSchemaTypeValues (schema: JsonNode) =
  match getJsonPath schema [ "type" ] with
  | :? JsonValue as value -> Set.singleton (value.GetValue<string>())
  | :? JsonArray as arr ->
    arr
    |> Seq.choose (function
      | :? JsonValue as value -> Some(value.GetValue<string>())
      | _ -> None)
    |> Set.ofSeq
  | _ -> failwith "Expected schema.type to be a string or array."

let private dereferenceRefSchema (document: JsonNode) (schema: JsonNode) =
  let rec loop (current: JsonNode) =
    match current with
    | :? JsonObject as obj ->
      match obj["$ref"] with
      | :? JsonValue as refValue ->
        let refString = refValue.GetValue<string>()

        if refString.StartsWith("#/", StringComparison.Ordinal) then
          let segments = refString.Substring(2).Split('/') |> Array.toList
          loop (getJsonPath document segments)
        else
          current
      | _ -> current
    | _ -> current

  loop schema

let private getUnionSchema (document: JsonNode) =
  getSchemaForPath document "/union-sample"
  |> dereferenceRefSchema document
  |> canonicalizeJson

let private getSchemaType (schema: JsonNode) =
  getJsonPath schema [ "type" ]
  |> function
    | :? JsonValue as v -> v.GetValue<string>()
    | _ -> failwith "Expected schema.type to be a string."

let private getCaseTag (caseSchema: JsonNode) =
  getJsonPath caseSchema [ "properties"; "kind"; "enum" ]
  |> function
    | :? JsonArray as arr ->
      arr
      |> Seq.choose (function
        | :? JsonValue as v -> Some(v.GetValue<string>())
        | _ -> None)
      |> Seq.exactlyOne
    | _ -> failwith "Expected union case schema to have properties.kind.enum with a single value."

let private getCaseByTag (document: JsonNode) (tag: string) =
  let schema = getUnionSchema document

  let cases =
    getJsonPath schema [ "oneOf" ]
    |> function
      | :? JsonArray as arr ->
        arr
        |> Seq.map (fun caseSchema -> dereferenceRefSchema document caseSchema |> canonicalizeJson)
        |> Seq.toList
      | _ -> failwith "Expected union schema.oneOf to be an array."

  cases
  |> Seq.tryFind (fun caseSchema ->
    match getSchemaType caseSchema with
    | "object" -> String.Equals(getCaseTag caseSchema, tag, StringComparison.Ordinal)
    | _ -> false)
  |> function
    | Some x -> x
    | None -> failwith $"Missing union case '%s{tag}'."

let private getResponseUnionCaseRef (document: JsonNode) (path: string) (tag: string) =
  let unionSchema =
    getResponseSchemaForPath document path 200
    |> dereferenceRefSchema document
    |> canonicalizeJson

  let cases =
    getJsonPath unionSchema [ "oneOf" ]
    |> function
      | :? JsonArray as arr -> arr |> Seq.toList
      | _ -> failwith "Expected union response schema.oneOf to be an array."

  cases
  |> Seq.tryPick (fun caseSchema ->
    let dereferenced = dereferenceRefSchema document caseSchema |> canonicalizeJson

    match getSchemaType dereferenced with
    | "object" when String.Equals(getCaseTag dereferenced, tag, StringComparison.Ordinal) ->
      match caseSchema with
      | :? JsonObject as obj ->
        match obj["$ref"] with
        | :? JsonValue as refValue -> Some(refValue.GetValue<string>())
        | _ -> None
      | _ -> None
    | _ -> None)
  |> function
    | Some refString -> refString
    | None -> failwith $"Missing $ref for response union case '%s{tag}' at path '%s{path}'."

let private getResponseSchemaRef (document: JsonNode) (path: string) =
  getResponseSchemaForPath document path 200 |> getSchemaRefString

let private getResponseUnionCaseByTag (document: JsonNode) (path: string) (tag: string) =
  let unionSchema =
    getResponseSchemaForPath document path 200
    |> dereferenceRefSchema document
    |> canonicalizeJson

  let cases =
    getJsonPath unionSchema [ "oneOf" ]
    |> function
      | :? JsonArray as arr ->
        arr
        |> Seq.map (fun caseSchema -> dereferenceRefSchema document caseSchema |> canonicalizeJson)
        |> Seq.toList
      | _ -> failwith "Expected union response schema.oneOf to be an array."

  cases
  |> Seq.tryFind (fun caseSchema ->
    match getSchemaType caseSchema with
    | "object" -> String.Equals(getCaseTag caseSchema, tag, StringComparison.Ordinal)
    | _ -> false)
  |> function
    | Some schema -> schema
    | None -> failwith $"Missing response union case '%s{tag}' at path '%s{path}'."

let private getSinglePayloadPropertySchema (caseSchema: JsonNode) =
  match getJsonPath caseSchema [ "properties" ] with
  | :? JsonObject as props ->
    props
    |> Seq.filter (fun kvp -> not (String.Equals(kvp.Key, "kind", StringComparison.Ordinal)))
    |> Seq.toList
    |> function
      | [ payload ] -> payload.Value
      | _ -> failwith "Expected exactly one non-kind payload property."
  | _ -> failwith "Expected union case schema.properties to be an object."

[<Fact>]
let ``Union schema matches encoding (InternalTag + NamedFields + UnwrapRecordCases)`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            endpoints
            [ OptionSchemaTransformer(fsOptions)
              UnionSchemaTransformer(fsOptions) ]
      )

    let unionSchema = getUnionSchema builtIn

    let caseCount =
      getJsonPath unionSchema [ "oneOf" ]
      |> function
        | :? JsonArray as arr -> arr.Count
        | _ -> failwith "Expected union schema.oneOf to be an array."

    %caseCount.Should().Be(4)

    let fieldlessSchema = getCaseByTag builtIn "Fieldless"

    let fieldlessRequired =
      getJsonPath fieldlessSchema [ "required" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected Fieldless schema.required to be an array."

    %fieldlessRequired.Should().Be(Set.ofList [ "kind" ])

    let singleSchema = getCaseByTag builtIn "Single"

    let singleFieldType =
      getJsonPath singleSchema [ "properties"; "string"; "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected Single schema.properties.string.type to be a string."

    %singleFieldType.Should().Be("string")

    let pairSchema = getCaseByTag builtIn "Pair"

    let pairStringType =
      getJsonPath pairSchema [ "properties"; "string"; "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected Pair schema.properties.string.type to be a string."

    %pairStringType.Should().Be("string")

    let pairIntType =
      getJsonPath pairSchema [ "properties"; "int32"; "type" ]
      |> function
        | :? JsonValue as v -> Set.ofList [ v.GetValue<string>() ]
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected Pair schema.properties.int32.type to be a string or string array."

    %pairIntType.Contains("integer").Should().BeTrue()

    let fromRecordSchema = getCaseByTag builtIn "FromRecord"

    let recordValueType =
      getJsonPath fromRecordSchema [ "properties"; "value"; "type" ]
      |> function
        | :? JsonValue as v -> Set.ofList [ v.GetValue<string>() ]
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected FromRecord schema.properties.value.type to be a string or string array."

    %recordValueType.Contains("string").Should().BeTrue()
  }

[<Fact>]
let ``Multi-case union schema is referenced (not inlined)`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

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

    getSchemaForPath builtIn "/union-sample" |> assertSchemaIsRef
    getResponseSchemaForPath builtIn "/union-sample" 200 |> assertSchemaIsRef
  }

[<Fact>]
let ``Multi-case union branches are referenced (not inlined)`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

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

    let unionSchema =
      getSchemaForPath builtIn "/union-sample"
      |> dereferenceRefSchema builtIn

    let caseSchemas =
      getJsonPath unionSchema [ "oneOf" ]
      |> function
        | :? JsonArray as arr -> arr
        | _ -> failwith "Expected union schema.oneOf to be an array."

    %caseSchemas.Count.Should().Be(4)
    caseSchemas |> Seq.iter assertSchemaIsRef
  }

[<Fact>]
let ``Single-case fieldless union schema is inlined as case object`` () : Task =
  task {
    let fsOptions =
      fsharpJsonOptions
        .WithUnwrapOption(false)
        .WithUnionUnwrapSingleCaseUnions()

    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            singleCaseEndpoints
            [ OptionSchemaTransformer(fsOptions)
              UnionSchemaTransformer(fsOptions)
              RecordSchemaTransformer(fsOptions)
              TupleSchemaTransformer() ]
      )

    let schema = getResponseSchemaForPath builtIn "/union-single-case-fieldless" 200

    schema |> assertSchemaIsInline

    let kind =
      getJsonPath schema [ "properties"; "kind"; "enum" ]
      |> function
        | :? JsonArray as arr when arr.Count = 1 ->
          match arr[0] with
          | :? JsonValue as v -> v.GetValue<string>()
          | _ -> failwith "Expected properties.kind.enum[0] to be a string."
        | _ -> failwith "Expected properties.kind.enum to be a single-element array."

    %kind.Should().Be("NotConfigured")
  }

[<Fact>]
let ``Single-case single-field union schema is unwrapped`` () : Task =
  task {
    let fsOptions =
      fsharpJsonOptions
        .WithUnwrapOption(false)
        .WithUnionUnwrapSingleCaseUnions()

    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            singleCaseEndpoints
            [ OptionSchemaTransformer(fsOptions)
              UnionSchemaTransformer(fsOptions)
              RecordSchemaTransformer(fsOptions)
              TupleSchemaTransformer() ]
      )

    let schema = getResponseSchemaForPath builtIn "/union-single-case-single-field" 200

    let typeSet =
      match getJsonPath schema [ "type" ] with
      | :? JsonValue as v -> Set.ofList [ v.GetValue<string>() ]
      | :? JsonArray as arr ->
        arr
        |> Seq.choose (function
          | :? JsonValue as v -> Some(v.GetValue<string>())
          | _ -> None)
        |> Set.ofSeq
      | _ -> Set.empty

    %typeSet.Contains("integer").Should().BeTrue()
    match schema with
    | :? JsonObject as obj when not (isNull obj["properties"]) -> failwith "Expected unwrapped schema to have no properties object."
    | _ -> ()
  }

[<Fact>]
let ``Union can unwrap fieldless tags to string enum`` () : Task =
  task {
    let fsOptions =
      fsharpJsonOptions
        .WithUnwrapOption(false)
        .WithUnionUnwrapFieldlessTags()

    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            endpoints
            [ OptionSchemaTransformer(fsOptions)
              UnionSchemaTransformer(fsOptions) ]
      )

    let unionSchema = getUnionSchema builtIn

    let cases =
      getJsonPath unionSchema [ "oneOf" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.map (fun caseSchema -> dereferenceRefSchema builtIn caseSchema |> canonicalizeJson)
          |> Seq.toList
        | _ -> failwith "Expected union schema.oneOf to be an array."

    let getTypeSet (schema: JsonNode) =
      match getJsonPath schema [ "type" ] with
      | :? JsonValue as v -> Set.ofList [ v.GetValue<string>() ]
      | :? JsonArray as arr ->
        arr
        |> Seq.choose (function
          | :? JsonValue as v -> Some(v.GetValue<string>())
          | _ -> None)
        |> Set.ofSeq
      | _ -> Set.empty

    let getEnumStrings (schema: JsonNode) =
      getJsonPath schema [ "enum" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Seq.toList
        | _ -> []

    let fieldless =
      cases
      |> Seq.tryFind (fun caseSchema ->
        getTypeSet caseSchema |> Set.contains "string"
        && getEnumStrings caseSchema = [ "Fieldless" ]
      )

    %fieldless.IsSome.Should().BeTrue()
  }

[<Fact>]
let ``All-fieldless union schema unwraps to string enum`` () : Task =
  task {
    let fsOptions =
      fsharpJsonOptions
        .WithUnwrapOption(false)
        .WithUnionUnwrapFieldlessTags()

    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            allFieldlessEndpoints
            [ OptionSchemaTransformer(fsOptions)
              UnionSchemaTransformer(fsOptions) ]
      )

    let schemaRef = getResponseSchemaForPath builtIn "/union-all-fieldless" 200
    schemaRef |> assertSchemaIsRef

    let schema =
      schemaRef
      |> dereferenceRefSchema builtIn
      |> canonicalizeJson

    let schemaType = getSchemaType schema
    %schemaType.Should().Be("string")

    let enumSet =
      getJsonPath schema [ "enum" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as v -> Some(v.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected schema.enum to be an array."

    %enumSet.Should().Be(Set.ofList [ "Alpha"; "Beta" ])

    match schema with
    | :? JsonObject as obj when not (isNull obj["oneOf"]) -> failwith "Expected all-fieldless union schema to have no oneOf."
    | _ -> ()
  }

[<Fact>]
let ``Generic Result unions with different payload types get distinct case component names`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            genericResultEndpoints
            [ OptionSchemaTransformer(fsOptions)
              UnionSchemaTransformer(fsOptions) ]
      )

    let wrapperRefStringInt = getResponseSchemaRef builtIn "/union-result-string-int"
    let wrapperRefIntGuid = getResponseSchemaRef builtIn "/union-result-int-guid"
    let requestWrapperRefStringInt = getSchemaForPath builtIn "/union-result-string-int" |> getSchemaRefString
    let requestWrapperRefIntGuid = getSchemaForPath builtIn "/union-result-int-guid" |> getSchemaRefString
    let okRefStringInt = getResponseUnionCaseRef builtIn "/union-result-string-int" "Ok"
    let okRefIntGuid = getResponseUnionCaseRef builtIn "/union-result-int-guid" "Ok"
    let errorRefStringInt = getResponseUnionCaseRef builtIn "/union-result-string-int" "Error"
    let errorRefIntGuid = getResponseUnionCaseRef builtIn "/union-result-int-guid" "Error"
    let wrapperIdStringInt = getSchemaIdFromRef wrapperRefStringInt
    let wrapperIdIntGuid = getSchemaIdFromRef wrapperRefIntGuid

    %wrapperRefStringInt.Should().NotBe(wrapperRefIntGuid)
    %requestWrapperRefStringInt.Should().Be(wrapperRefStringInt)
    %requestWrapperRefIntGuid.Should().Be(wrapperRefIntGuid)
    %okRefStringInt.Should().NotBe(okRefIntGuid)
    %errorRefStringInt.Should().NotBe(errorRefIntGuid)
    %wrapperIdStringInt.Should().NotContain("`")
    %wrapperIdIntGuid.Should().NotContain("`")
    %okRefStringInt.Should().NotContain("`")
    %okRefIntGuid.Should().NotContain("`")
    %errorRefStringInt.Should().NotContain("`")
    %errorRefIntGuid.Should().NotContain("`")
    %okRefStringInt.Should().Be($"#/components/schemas/{wrapperIdStringInt}_Ok")
    %errorRefStringInt.Should().Be($"#/components/schemas/{wrapperIdStringInt}_Error")
    %okRefIntGuid.Should().Be($"#/components/schemas/{wrapperIdIntGuid}_Ok")
    %errorRefIntGuid.Should().Be($"#/components/schemas/{wrapperIdIntGuid}_Error")

    let okSchemaStringInt = getResponseUnionCaseByTag builtIn "/union-result-string-int" "Ok"
    let okSchemaIntGuid = getResponseUnionCaseByTag builtIn "/union-result-int-guid" "Ok"
    let errorSchemaStringInt = getResponseUnionCaseByTag builtIn "/union-result-string-int" "Error"
    let errorSchemaIntGuid = getResponseUnionCaseByTag builtIn "/union-result-int-guid" "Error"

    let okStringIntType =
      getJsonPath okSchemaStringInt [ "properties"; "resultValue"; "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected Ok<string,int>.resultValue.type to be a string."

    let okIntGuidType =
      getJsonPath okSchemaIntGuid [ "properties"; "resultValue"; "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected Ok<int,Guid>.resultValue.type to be a string."

    let errorStringIntType =
      getJsonPath errorSchemaStringInt [ "properties"; "errorValue"; "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected Error<string,int>.errorValue.type to be a string."

    let errorIntGuidType =
      getJsonPath errorSchemaIntGuid [ "properties"; "errorValue"; "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected Error<int,Guid>.errorValue.type to be a string."

    let errorIntGuidFormat =
      getJsonPath errorSchemaIntGuid [ "properties"; "errorValue"; "format" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected Error<int,Guid>.errorValue.format to be a string."

    %okStringIntType.Should().Be("string")
    %okIntGuidType.Should().Be("integer")
    %errorStringIntType.Should().Be("integer")
    %errorIntGuidType.Should().Be("string")
    %errorIntGuidFormat.Should().Be("uuid")
  }

[<Fact>]
let ``Generic non-Result unions with nested payloads get stable wrapper and case ids`` () : Task =
  task {
    let fsOptions = fsharpJsonOptions.WithUnwrapOption(false)

    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            genericEnvelopeEndpoints
            [ OptionSchemaTransformer(fsOptions)
              UnionSchemaTransformer(fsOptions)
              RecordSchemaTransformer(fsOptions)
              TupleSchemaTransformer() ]
      )

    let wrapperRefResultArray = getResponseSchemaRef builtIn "/union-envelope-result-array"
    let wrapperRefGuidOption = getResponseSchemaRef builtIn "/union-envelope-guid-option"
    let requestWrapperRefResultArray = getSchemaForPath builtIn "/union-envelope-result-array" |> getSchemaRefString
    let requestWrapperRefGuidOption = getSchemaForPath builtIn "/union-envelope-guid-option" |> getSchemaRefString
    let missingRefResultArray = getResponseUnionCaseRef builtIn "/union-envelope-result-array" "Missing"
    let missingRefGuidOption = getResponseUnionCaseRef builtIn "/union-envelope-guid-option" "Missing"
    let loadedRefResultArray = getResponseUnionCaseRef builtIn "/union-envelope-result-array" "Loaded"
    let loadedRefGuidOption = getResponseUnionCaseRef builtIn "/union-envelope-guid-option" "Loaded"
    let wrapperIdResultArray = getSchemaIdFromRef wrapperRefResultArray
    let wrapperIdGuidOption = getSchemaIdFromRef wrapperRefGuidOption

    %wrapperRefResultArray.Should().NotBe(wrapperRefGuidOption)
    %requestWrapperRefResultArray.Should().Be(wrapperRefResultArray)
    %requestWrapperRefGuidOption.Should().Be(wrapperRefGuidOption)
    %missingRefResultArray.Should().NotBe(missingRefGuidOption)
    %loadedRefResultArray.Should().NotBe(loadedRefGuidOption)
    %loadedRefResultArray.Should().Be($"#/components/schemas/{wrapperIdResultArray}_Loaded")
    %missingRefResultArray.Should().Be($"#/components/schemas/{wrapperIdResultArray}_Missing")
    %loadedRefGuidOption.Should().Be($"#/components/schemas/{wrapperIdGuidOption}_Loaded")
    %missingRefGuidOption.Should().Be($"#/components/schemas/{wrapperIdGuidOption}_Missing")

    let loadedSchemaResultArray = getResponseUnionCaseByTag builtIn "/union-envelope-result-array" "Loaded"
    let loadedSchemaGuidOption = getResponseUnionCaseByTag builtIn "/union-envelope-guid-option" "Loaded"

    let resultArrayPayload = getSinglePayloadPropertySchema loadedSchemaResultArray
    let guidOptionPayload =
      getSinglePayloadPropertySchema loadedSchemaGuidOption
      |> dereferenceRefSchema builtIn
      |> canonicalizeJson

    let resultArrayPayloadType =
      getJsonPath resultArrayPayload [ "type" ]
      |> function
        | :? JsonValue as v -> v.GetValue<string>()
        | _ -> failwith "Expected Loaded<Result<string,int> array> payload type to be a string."

    let resultArrayItemsRef =
      getJsonPath resultArrayPayload [ "items" ]
      |> getSchemaRefString

    let guidOptionPayloadTypes = getSchemaTypeValues guidOptionPayload

    let guidOptionRequired =
      getJsonPath guidOptionPayload [ "required" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as value -> Some(value.GetValue<string>())
            | _ -> None)
          |> Set.ofSeq
        | _ -> failwith "Expected Loaded<Guid option> payload to expose required fields."

    let guidOptionTagValues =
      getJsonPath guidOptionPayload [ "properties"; "kind"; "enum" ]
      |> function
        | :? JsonArray as arr ->
          arr
          |> Seq.choose (function
            | :? JsonValue as value -> Some(value.GetValue<string>())
            | _ -> None)
          |> Seq.toList
        | _ -> failwith "Expected Loaded<Guid option> payload to expose a tagged Some case."

    let guidOptionValueType =
      getJsonPath guidOptionPayload [ "properties"; "value"; "type" ]
      |> function
        | :? JsonValue as value -> value.GetValue<string>()
        | _ -> failwith "Expected Loaded<Guid option> payload value type to be a string."

    let guidOptionValueFormat =
      getJsonPath guidOptionPayload [ "properties"; "value"; "format" ]
      |> function
        | :? JsonValue as value -> value.GetValue<string>()
        | _ -> failwith "Expected Loaded<Guid option> payload value format to be a string."

    %resultArrayPayloadType.Should().Be("array")
    %resultArrayItemsRef.Should().Contain("#/components/schemas/")
    %resultArrayItemsRef.Should().NotContain("`")
    %guidOptionPayloadTypes.Should().Be(Set.ofList [ "null"; "object" ])
    %guidOptionRequired.Should().Be(Set.ofList [ "kind"; "value" ])
    %guidOptionTagValues.Should().Be([ "Some" ])
    %guidOptionValueType.Should().Be("string")
    %guidOptionValueFormat.Should().Be("uuid")
  }

[<Fact>]
let ``Optional payload-union property is a single nullable union component`` () : Task =
  task {
    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            optionalUnionEndpoints
            [ OptionSchemaTransformer(fsharpJsonOptions)
              UnionSchemaTransformer(fsharpJsonOptions)
              RecordSchemaTransformer(fsharpJsonOptions)
              TupleSchemaTransformer() ]
      )

    let unionId =
      getRecordPropertySchema builtIn "/record-with-optional-union" "attention"
      |> getSchemaRefString
      |> getSchemaIdFromRef

    let schemas =
      match getJsonPath builtIn [ "components"; "schemas" ] with
      | :? JsonObject as obj -> obj
      | _ -> failwith "Expected components.schemas to be an object."

    let schemaNames = schemas |> Seq.map (fun kvp -> kvp.Key) |> Seq.toList
    %schemaNames.Should().NotContain(unionId + unionId)

    let withSiblingBranchLists =
      schemas
      |> Seq.filter (fun kvp ->
        match kvp.Value with
        | :? JsonObject as obj -> not (isNull obj["anyOf"]) && not (isNull obj["oneOf"])
        | _ -> false)
      |> Seq.map (fun kvp -> kvp.Key)
      |> Seq.toList

    %withSiblingBranchLists.Should().BeEmpty()

    let refBranches, otherBranches =
      match getJsonPath schemas [ unionId; "oneOf" ] with
      | :? JsonArray as arr ->
        arr
        |> Seq.toList
        |> List.partition (fun branch ->
          match branch with
          | :? JsonObject as obj -> not (isNull obj["$ref"])
          | _ -> false)
      | _ -> failwith "Expected union schema.oneOf to be an array."

    %(refBranches |> List.map getSchemaRefString)
      .Should()
      .Be(
        [ $"#/components/schemas/{unionId}_WithReason"
          $"#/components/schemas/{unionId}_Plain" ]
      )

    %(otherBranches |> List.map (fun branch -> branch.ToJsonString()))
      .Should()
      .Be([ """{"type":"null"}""" ])
  }

[<Fact>]
let ``Optional fieldless-tag union property lists null in its enum`` () : Task =
  task {
    let! builtIn =
      fetchOpenApiJson (
        fun () ->
          createBuiltInOpenApiApp
            optionalStringEnumEndpoints
            [ OptionSchemaTransformer(fsharpJsonOptions)
              UnionSchemaTransformer(fsharpJsonOptions)
              RecordSchemaTransformer(fsharpJsonOptions)
              TupleSchemaTransformer() ]
      )

    let statusSchema =
      getRecordPropertySchema builtIn "/record-with-optional-string-enum" "status"
      |> dereferenceRefSchema builtIn

    // JSON Schema checks `enum` on its own, so `None` (written as null) must be an enum value too.
    let enumValues =
      match getJsonPath statusSchema [ "enum" ] with
      | :? JsonArray as arr ->
        arr
        |> Seq.map (fun value -> if isNull value then None else Some(value.GetValue<string>()))
        |> Seq.toList
      | _ -> failwith "Expected schema.enum to be an array."

    %(getSchemaTypeValues statusSchema).Should().Be(Set.ofList [ "null"; "string" ])
    %enumValues.Should().Be([ Some "first"; Some "second"; None ])
  }
