module SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiTestSetup

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Threading.Tasks
open SelectView.FSharp.SystemTextJson.OpenApi
open Giraffe
open Giraffe.EndpointRouting
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.OpenApi
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.OpenApi

let fsharpJsonOptions =
  JsonFSharpOptions
    .Default()
    .WithUnionNamedFields()
    .WithUnionFieldNamesFromTypes()
    .WithUnionTagName("kind")
    .WithUnionInternalTag()
    .WithAllowOverride()
    .WithUnionUnwrapRecordCases()

let jsonOptions =
  JsonSerializerOptions(WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

let canonicalizeJson (node: JsonNode) =
  let rec loop (n: JsonNode) : JsonNode =
    match n with
    | :? JsonObject as obj ->
      let res = JsonObject()

      obj
      |> Seq.sortBy (fun kvp -> kvp.Key)
      |> Seq.iter (fun kvp ->
        let v = kvp.Value

        if isNull v then
          res.Add(kvp.Key, null)
        else
          res.Add(kvp.Key, loop v)
      )

      res :> JsonNode
    | :? JsonArray as arr ->
      let res = JsonArray()
      arr |> Seq.iter (fun v -> res.Add(if isNull v then null else loop v))
      res :> JsonNode
    | _ -> n.DeepClone()

  loop node

let getJsonPath (root: JsonNode) (segments: string list) =
  (root, segments)
  ||> List.fold (fun current segment ->
    if isNull current then
      failwith $"Missing JSON segment: %s{segment}"

    let next = current[segment]
    if isNull next then
      failwith $"Missing JSON segment: %s{segment}"

    next
  )

let getSchemaForPath (document: JsonNode) (path: string) =
  getJsonPath
    document
    [ "paths"
      path
      "post"
      "requestBody"
      "content"
      "application/json"
      "schema" ]

let getResponseSchemaForPath (document: JsonNode) (path: string) (statusCode: int) =
  getJsonPath
    document
    [ "paths"
      path
      "post"
      "responses"
      string statusCode
      "content"
      "application/json"
      "schema" ]

let dereferenceSchema (document: JsonNode) (schema: JsonNode) =
  let tryGetRef (obj: JsonObject) =
    match obj["$ref"] with
    | null -> None
    | refNode ->
      match refNode with
      | :? JsonValue as refValue ->
        let refString = refValue.GetValue<string>()

        if refString.StartsWith("#/", StringComparison.Ordinal) then
          Some(refString.Substring(2).Split('/') |> Array.toList)
        else
          None
      | _ -> None

  let tryUnwrapSchemaList (obj: JsonObject) (key: string) =
    match obj[key] with
    | :? JsonArray as arr ->
      arr
      |> Seq.tryPick (fun entry ->
        match entry with
        | :? JsonObject as entryObj ->
          match tryGetRef entryObj with
          | Some _ as x -> x |> Option.map (fun segments -> getJsonPath document segments)
          | None when not (isNull entryObj["properties"]) -> Some entry
          | None -> None
        | _ -> None
      )
    | _ -> None

  let rec loop (current: JsonNode) =
    match current with
    | :? JsonObject as obj ->
      match tryGetRef obj with
      | Some segments -> loop (getJsonPath document segments)
      | None ->
        match tryUnwrapSchemaList obj "allOf" with
        | Some unwrapped -> loop unwrapped
        | None ->
          match tryUnwrapSchemaList obj "oneOf" with
          | Some unwrapped -> loop unwrapped
          | None ->
            match tryUnwrapSchemaList obj "anyOf" with
            | Some unwrapped -> loop unwrapped
            | None -> current
    | _ -> current

  loop schema

let expandSchemaRefs (document: JsonNode) (schema: JsonNode) =
  let rec loop (seenRefs: Set<string>) (current: JsonNode) : JsonNode =
    match current with
    | :? JsonObject as obj ->
      match obj["$ref"] with
      | :? JsonValue as refValue ->
        let refString = refValue.GetValue<string>()

        if refString.StartsWith("#/", StringComparison.Ordinal) then
          if seenRefs.Contains(refString) then
            current.DeepClone()
          else
            let segments = refString.Substring(2).Split('/') |> Array.toList
            loop (seenRefs.Add(refString)) (getJsonPath document segments)
        else
          current.DeepClone()
      | _ ->
        let res = JsonObject()

        obj
        |> Seq.iter (fun kvp ->
          let v = kvp.Value
          res.Add(kvp.Key, if isNull v then null else loop seenRefs v)
        )

        res :> JsonNode
    | :? JsonArray as arr ->
      let res = JsonArray()
      arr |> Seq.iter (fun v -> res.Add(if isNull v then null else loop seenRefs v))
      res :> JsonNode
    | _ -> current.DeepClone()

  loop Set.empty schema

let getRecordPropertySchema (document: JsonNode) (endpointPath: string) (propertyName: string) =
  let recordSchema =
    getSchemaForPath document endpointPath
    |> dereferenceSchema document

  match recordSchema with
  | :? JsonObject as obj when not (isNull obj["properties"]) ->
    getJsonPath recordSchema [ "properties"; propertyName ]
  | _ ->
    failwith $"Unexpected schema shape for %s{endpointPath}: {recordSchema.ToJsonString(jsonOptions)}"

let fetchOpenApiJson (createApp: unit -> Task<WebApplication>) =
  task {
    let! createdApp = createApp ()
    use app = createdApp

    do! app.StartAsync()

    use client = app.GetTestClient()
    let! json = client.GetStringAsync("/openapi/v1.json")

    return
      match JsonNode.Parse(json) with
      | null -> failwith "Failed to parse OpenAPI JSON."
      | parsed -> parsed
  }

let createBuiltInOpenApiApp
  (endpoints: Endpoint list)
  (schemaTransformers: IOpenApiSchemaTransformer list)
  =
  task {
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore

    builder.Services.AddRouting().AddGiraffe().AddEndpointsApiExplorer() |> ignore

    builder.Services.AddOpenApi(
      "v1",
      fun options ->
        options.OpenApiVersion <- OpenApiSpecVersion.OpenApi3_1
        schemaTransformers |> List.iter (fun t -> options.AddSchemaTransformer(t) |> ignore)
        options.AddDocumentTransformer(UnionCaseComponentsDocumentTransformer()) |> ignore
        options.AddDocumentTransformer(NullableEnumDocumentTransformer()) |> ignore
    )
    |> ignore

    let app = builder.Build()

    app.UseRouting() |> ignore
    app.UseGiraffe endpoints |> ignore
    app.MapOpenApi("/openapi/{documentName}.json") |> ignore

    return app
  }
