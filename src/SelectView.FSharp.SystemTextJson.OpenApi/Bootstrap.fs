namespace SelectView.FSharp.SystemTextJson.OpenApi

open System.Runtime.CompilerServices
open System.Text.Json.Serialization
open Microsoft.AspNetCore.OpenApi
open Microsoft.Extensions.DependencyInjection
open Microsoft.OpenApi

type DependencyExtension() =

  /// Adds OpenAPI schema generation support for F# types using System.Text.Json and FSharp.SystemTextJson.
  [<Extension>]
  static member AddOpenApiForSystemTextJson
    (
      services: IServiceCollection,
      fsOptions: JsonFSharpOptions,
      ?documentName: string,
      ?setup: OpenApiOptions -> unit
    ) =
    let documentName = documentName |> Option.defaultValue "v1"

    services.AddSingleton(fsOptions) |> ignore

    services.Configure(fun (o: Microsoft.AspNetCore.Http.Json.JsonOptions) ->
      fsOptions.AddToJsonSerializerOptions(o.SerializerOptions))
    |> ignore

    services.AddOpenApi(
      documentName,
      fun options ->
        options.OpenApiVersion <- OpenApiSpecVersion.OpenApi3_1

        [
          OptionSchemaTransformer(fsOptions) :> IOpenApiSchemaTransformer
          JsonNodeSchemaTransformer()
          CollectionsSchemaTransformer()
          RecordSchemaTransformer(fsOptions)
          TupleSchemaTransformer(fsOptions)
          UnionSchemaTransformer(fsOptions)
          EnumSchemaTransformer()
        ]
        |> List.iter (fun t -> options.AddSchemaTransformer(t) |> ignore)

        setup |> Option.iter (fun s -> s options)

        options.AddDocumentTransformer(UnionCaseComponentsDocumentTransformer())
        |> ignore

        options.AddDocumentTransformer(NullableEnumDocumentTransformer()) |> ignore
    )
