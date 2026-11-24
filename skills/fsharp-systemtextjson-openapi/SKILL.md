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
OpenAPI document (default `v1`, served at `/openapi/{documentName}.json`,
OpenAPI 3.1) with all transformers. It also adds `fsOptions` to
`Microsoft.AspNetCore.Http.Json.JsonOptions`, which the OpenAPI generator
reads.

## Rules

1. The serializer that writes the JSON and
   `Microsoft.AspNetCore.Http.Json.JsonOptions` must have the same settings:
   the same `fsOptions`, naming policy and ignore condition. If they differ,
   the schema does not match the JSON.
   - Minimal APIs: nothing more to do.
   - MVC: in `AddControllers().AddJsonOptions(fun o -> ...)`, call
     `fsOptions.AddToJsonSerializerOptions(o.JsonSerializerOptions)` and set
     the same naming policy and ignore condition as in `JsonOptions`.
   - Giraffe: register
     `services.AddSingleton<Json.ISerializer>(Json.Serializer(jsonOptions))`,
     where `jsonOptions` has the same settings and `fsOptions` added.
2. To change one type, use `JsonFSharpOptions.WithOverrides`, or put
   `[<JsonFSharpConverter(...)>]` on the type and call `WithAllowOverride()`
   on the global options (without it, the attribute is ignored). The
   transformers apply both the same way as FSharp.SystemTextJson.
3. Per-type options inherit only the union encoding, and only when they do not
   set it. Every other option (for example the union tag name) takes the
   FSharp.SystemTextJson default, not the global value. Set every option that
   the type needs. An `option` or `voption` field follows the options for
   `option<_>` or `voption<_>` (global or overridden), never the options of
   the record or union that contains it.
4. If you add the transformers yourself with `AddOpenApi`, add `fsOptions` to
   `JsonOptions`, and add the two document transformers
   `UnionCaseComponentsDocumentTransformer()` and
   `NullableEnumDocumentTransformer()` last, after your own document
   transformers.
5. The transformers do not model `MapFormat` or `WithOverrideMembers`. With
   these options the schema does not match the JSON.

## What the schema shows

- `option` or `voption` in a record field, a union field or a tuple element:
  the inner schema plus `null`, because `None` and `ValueNone` are written as
  `null`. Top-level `option` and `voption`: the inner schema only.
- With `UnwrapOption` turned off: `option` is `null` or a tagged `Some`
  object; `voption` is a tagged `ValueSome` or `ValueNone` object.
- Record and named union fields: a `Skippable<'T>` field is optional. An
  `option` or `voption` field is optional with
  `SkippableOptionFields.Always`, or with `FromJsonSerializerOptions` (the
  default) and `JsonIgnoreCondition.WhenWritingNull`. With `WhenWritingNull`,
  other fields are optional too. `[<JsonRequired>]` makes a record field
  required.
- Unions: the encoding from the options (internal tag, adjacent tag, external
  tag, untagged, named fields, unwrapped record cases, unwrapped single-field
  cases, unwrapped single-case unions). With `UnwrapFieldlessTags`, a
  fieldless case becomes a string, and a union of only fieldless cases
  becomes a string enum. Every other union gets one
  component for each case, except a single-case union under
  `UnwrapSingleCaseUnions`.
- A tuple becomes a fixed-length array. Its items schema allows any of the
  element types; it does not fix the type of each position.
- `list` and `Set` become arrays. `Map` with `string` keys (or single-case
  unions that wrap a string) becomes an object; other `Map` keys become an
  array of `[key, value]` pairs. `JsonObject` becomes an object with any
  properties.
- Enums use their string or number values, also when they are nullable.

## Check

When you change options or a type, compare the runtime JSON with the schema:

1. Serialize a sample value with the app's serializer options.
2. Read `/openapi/{documentName}.json` and resolve the `$ref` of that type.
3. Every property and union tag in the JSON must be in the schema, and every
   required property in the schema must be in the JSON.
