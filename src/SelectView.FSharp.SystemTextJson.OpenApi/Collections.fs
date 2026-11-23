namespace SelectView.FSharp.SystemTextJson.OpenApi

open System
open Microsoft.FSharp.Reflection
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.OpenApi

module private CollectionsHelpers =

  let isWrappedStringUnion (t: Type) =
    if not (FSharpType.IsUnion(t, true)) then
      false
    else
      let cases = FSharpType.GetUnionCases(t, true)

      if cases.Length <> 1 then
        false
      else
        let fields = cases[0].GetFields()
        fields.Length = 1 && fields[0].PropertyType = typeof<string>

type CollectionsSchemaTransformer(_fsOptions: System.Text.Json.Serialization.JsonFSharpOptions) =
  interface IOpenApiSchemaTransformer with
    member _.TransformAsync(schema: OpenApiSchema, ctx: OpenApiSchemaTransformerContext, ct: CancellationToken) : Task =
      let t = ctx.JsonTypeInfo.Type

      let getOrCreateSchema (ty: Type) =
        OpenApiSchemaHelpers.getOrCreateDeterministicSchema ctx ct ty

      let copyArraySchema innerType =
        task {
          let! innerSchema = getOrCreateSchema innerType

          let arraySchema =
            OpenApiSchema(Type = Nullable(JsonSchemaType.Array), Items = innerSchema)

          SchemaCopy.copyTo schema arraySchema
        }
        :> Task

      let copyMapSchema valueType =
        task {
          let! valueSchema = getOrCreateSchema valueType

          let mapSchema =
            OpenApiSchema(
              Type = Nullable(JsonSchemaType.Object),
              AdditionalPropertiesAllowed = true,
              AdditionalProperties = valueSchema
            )

          SchemaCopy.copyTo schema mapSchema
        }
        :> Task

      let copyTupleArraySchema genArgs =
        task {
          let tupleType = FSharpType.MakeTupleType(genArgs)
          let! tupleSchema = getOrCreateSchema tupleType

          let mapSchema =
            OpenApiSchema(Type = Nullable(JsonSchemaType.Array), Items = tupleSchema)

          SchemaCopy.copyTo schema mapSchema
        }
        :> Task

      if t.IsArray then
        match t.GetElementType() with
        | null -> Task.CompletedTask
        | innerType -> copyArraySchema innerType

      elif t.IsGenericType then
        let gtd = t.GetGenericTypeDefinition()

        if gtd = typedefof<list<_>> || gtd = typedefof<Set<_>> then
          let innerType = t.GetGenericArguments()[0]
          copyArraySchema innerType

        elif gtd = typedefof<Map<_, _>> then
          let genArgs = t.GetGenericArguments()
          let keyType = genArgs[0]
          let valueType = genArgs[1]

          if keyType = typeof<string> || CollectionsHelpers.isWrappedStringUnion keyType then
            copyMapSchema valueType
          else
            copyTupleArraySchema genArgs
        else
          Task.CompletedTask
      else
        Task.CompletedTask
