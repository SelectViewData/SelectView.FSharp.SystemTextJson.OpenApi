namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

type JsonNodeSchemaTransformer() =
  interface IOpenApiSchemaTransformer with
    member _.TransformAsync
      (schema: OpenApiSchema, ctx: OpenApiSchemaTransformerContext, _ct: CancellationToken)
      : Task =
      let t = ctx.JsonTypeInfo.Type

      if t = typeof<JsonObject> then
        schema.Type <- Nullable(JsonSchemaType.Object)
        schema.AdditionalPropertiesAllowed <- true
        schema.AdditionalProperties <- OpenApiSchema()
        schema.Properties <- null
        schema.Required <- null

      Task.CompletedTask
