namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

type TupleSchemaTransformer(fsOptions: System.Text.Json.Serialization.JsonFSharpOptions) =
  let unwrapOption =
    fsOptions.UnionEncoding.HasFlag System.Text.Json.Serialization.JsonUnionEncoding.UnwrapOption

  interface IOpenApiSchemaTransformer with
    member _.TransformAsync(schema: OpenApiSchema, ctx: OpenApiSchemaTransformerContext, ct: CancellationToken) : Task =
      task {
        let t = ctx.JsonTypeInfo.Type

        if FSharpType.IsTuple(t) then
          let tupleElements = FSharpType.GetTupleElements(t)
          let distinctElementTypes = tupleElements |> Array.distinct

          let! elementSchemas =
            distinctElementTypes
            |> Array.map (OpenApiSchemaHelpers.getOrCreateFieldSchema unwrapOption ctx ct)
            |> Task.WhenAll

          let elementSchemaRefs =
            elementSchemas
            |> Array.map (fun schema -> schema :> IOpenApiSchema)

          let hasPlainString =
            elementSchemaRefs
            |> Seq.map OpenApiSchemaHelpers.unwrapReferenceSchema
            |> Seq.exists (fun s -> s.Type = Nullable(JsonSchemaType.String) && OpenApiSchemaHelpers.enumIsEmpty s)

          let hasEnumString =
            elementSchemaRefs
            |> Seq.map OpenApiSchemaHelpers.unwrapReferenceSchema
            |> Seq.exists (fun s -> s.Type = Nullable(JsonSchemaType.String) && not (OpenApiSchemaHelpers.enumIsEmpty s))

          let itemUnionSchema =
            if hasPlainString && hasEnumString then
              OpenApiSchema(AnyOf = ResizeArray<IOpenApiSchema>(elementSchemaRefs))
            else
              OpenApiSchema(OneOf = ResizeArray<IOpenApiSchema>(elementSchemaRefs))

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
