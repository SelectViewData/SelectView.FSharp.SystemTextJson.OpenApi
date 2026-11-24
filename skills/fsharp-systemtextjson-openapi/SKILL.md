---
name: fsharp-systemtextjson-openapi
description: Use when an ASP.NET Core app with F# types serialized by FSharp.SystemTextJson needs an OpenAPI document that matches its JSON - setting up SelectView.FSharp.SystemTextJson.OpenApi, adding or changing JsonFSharpOptions or a [<JsonFSharpConverter>] attribute, or fixing an OpenAPI schema (or generated client) that differs from the JSON the app sends for options, records, unions, tuples, enums or F# collections.
---

# FSharp.SystemTextJson OpenAPI schemas

`Microsoft.AspNetCore.OpenApi` describes F# types as plain .NET classes. The
package `SelectView.FSharp.SystemTextJson.OpenApi` adds schema and document
transformers that read your `JsonFSharpOptions`, so the OpenAPI schema
describes the JSON that FSharp.SystemTextJson writes. Requirements: .NET 10,
OpenAPI 3.1.

## Set up

```sh
dotnet add package SelectView.FSharp.SystemTextJson.OpenApi
```

```fsharp
open System.Text.Json.Serialization
open Microsoft.AspNetCore.Builder
open SelectView.FSharp.SystemTextJson.OpenApi

let fsOptions =
  JsonFSharpOptions.Default().WithUnionTagName("kind").WithUnionInternalTag()

let builder = WebApplication.CreateBuilder()
builder.Services.AddOpenApiForSystemTextJson(fsOptions) |> ignore
let app = builder.Build()
app.MapOpenApi() |> ignore
```

`AddOpenApiForSystemTextJson(fsOptions, ?documentName, ?setup)` registers the
OpenAPI document (default `v1`, OpenAPI 3.1) with all transformers. It also
adds `fsOptions` to `Microsoft.AspNetCore.Http.Json.JsonOptions`, which only
minimal APIs read.

## Rules

1. Use one `JsonFSharpOptions` value for the serializer and for the OpenAPI
   document. Two different values give a schema that does not match the JSON.
2. MVC controllers and Giraffe use their own serializer options. Add the same
   `fsOptions` there too, for example
   `fsOptions.AddToJsonSerializerOptions(options.JsonSerializerOptions)` in
   `AddControllers().AddJsonOptions(...)`, or Giraffe's
   `SystemTextJson.Serializer` built from options that include `fsOptions`.
3. To change one type, put `[<JsonFSharpConverter(...)>]` on that type and
   call `WithAllowOverride()` on the global options. The transformers read
   this attribute. Do not use `JsonFSharpOptions.WithOverrides`: the
   serializer applies it, but the transformers do not, so the schema stays
   wrong.
4. The attribute does not inherit the global options. Set every option that
   the type needs (for example the union tag name) in the attribute.
5. If you add the transformers yourself with `AddOpenApi`, also add the two
   document transformers `UnionCaseComponentsDocumentTransformer()` and
   `NullableEnumDocumentTransformer()`, and add `fsOptions` to the serializer.

## What the schema shows

- `option` and `voption`: the inner schema plus `null`. With
  `UnwrapOption` turned off, a tagged union with `Some` and `None`.
- Records: a `Skippable<'T>` field is optional. With
  `JsonIgnoreCondition.WhenWritingNull`, every field is optional.
  `[<JsonRequired>]` makes a field required.
- Unions: the encoding from the options (internal tag, adjacent tag, external
  tag, untagged, named fields, unwrapped record cases, unwrapped single-case
  unions). Fieldless cases become strings with `UnwrapFieldlessTags`. Each
  case gets its own component.
- A tuple becomes a fixed-length array. Its items schema allows any of the
  element types; it does not fix the type of each position.
- `list` and `Set` become arrays. `Map` with `string` keys (or single-case
  unions that wrap a string) becomes an object; other `Map` keys become an
  array of `[key, value]` pairs. `JsonNode` accepts any value.
- Enums use their string or number values, also when they are nullable.

## Check

When you change options or a type, compare the runtime JSON with the schema:

1. Serialize a sample value with the app's serializer options.
2. Read `/openapi/v1.json` and resolve the `$ref` of that type.
3. Every property and union tag in the JSON must be in the schema, and every
   required property in the schema must be in the JSON.

Keep a committed snapshot of the OpenAPI document and fail the build when it
changes without review. Generated clients (for example openapi-typescript)
then change only on purpose.
