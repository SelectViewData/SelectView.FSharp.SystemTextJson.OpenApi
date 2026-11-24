namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Collections.Generic
open System.Text.Json.Serialization
open Microsoft.FSharp.Reflection
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

type TupleSchemaTransformer(fsOptions: JsonFSharpOptions) =
  interface IOpenApiSchemaTransformer with
    member _.TransformAsync(schema: OpenApiSchema, ctx: OpenApiSchemaTransformerContext, ct: CancellationToken) : Task =
      task {
        let t = ctx.JsonTypeInfo.Type

        if FSharpType.IsTuple(t) then
          let tupleElements = FSharpType.GetTupleElements(t)
          let distinctElementTypes = tupleElements |> Array.distinct

          let! elementSchemas =
            distinctElementTypes
            |> Array.map (OpenApiSchemaHelpers.getOrCreateFieldSchema fsOptions ctx ct)
            |> Task.WhenAll

          let elementSchemaRefs =
            elementSchemas
            |> Array.map (fun schema -> schema :> IOpenApiSchema)

          let itemUnionSchema = OpenApiSchemaHelpers.anyOfSchema elementSchemaRefs

          let tupleSchema =
            OpenApiSchema(
              Type = Nullable(JsonSchemaType.Array),
              Items = (itemUnionSchema :> IOpenApiSchema),
              MinItems = tupleElements.Length,
              MaxItems = tupleElements.Length
            )

          SchemaCopy.copyTo schema tupleSchema
      }
      :> Task
