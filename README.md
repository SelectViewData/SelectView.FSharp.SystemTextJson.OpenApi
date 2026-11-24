# SelectView.FSharp.SystemTextJson.OpenApi

OpenAPI schemas for F# types that you serialize with
[FSharp.SystemTextJson](https://github.com/Tarmil/FSharp.SystemTextJson).

`Microsoft.AspNetCore.OpenApi` describes F# types as plain .NET classes. The
schemas then do not match the JSON that FSharp.SystemTextJson writes. This
package adds schema and document transformers that read your
`JsonFSharpOptions` and describe the real JSON:

- `option` and `voption`: the inner value, and `null` for record fields,
  union fields and tuple elements
- records: required and optional properties, including `Skippable<'T>` fields
  and `SkippableOptionFields`
- unions: tags, internal, adjacent and external encodings, named fields,
  unwrapped cases, fieldless cases as strings, unions of only fieldless cases
  as string enums, and one component for each case (except string-enum and
  unwrapped single-case unions)
- tuples, F# collections (`list`, `Set`, `Map`), and `JsonObject`
- enums: string and number encodings, also when nullable
- per-type options from `WithOverrides` and `[<JsonFSharpConverter(...)>]`,
  merged with the global options the same way as FSharp.SystemTextJson
  (`WithOverrideMembers` and `MapFormat` are not supported)

The package targets .NET 10 and OpenAPI 3.1.

## Install

```sh
dotnet add package SelectView.FSharp.SystemTextJson.OpenApi
```

## Use

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

`AddOpenApiForSystemTextJson` does three things:

1. It adds `fsOptions` to `Microsoft.AspNetCore.Http.Json.JsonOptions`. The
   OpenAPI generator and minimal APIs read these options. MVC controllers and
   Giraffe use their own serializer options: give them the same settings.
2. It registers the OpenAPI document (`v1` by default) with all transformers.
3. It sets the OpenAPI version to 3.1.

Use the optional `documentName` and `setup` arguments to change the document
name or to add your own transformers.

To use only some transformers, add them to your own `AddOpenApi` call, for
example `options.AddSchemaTransformer(UnionSchemaTransformer(fsOptions))`.
Then also add `fsOptions` to
`Microsoft.AspNetCore.Http.Json.JsonOptions`, and add the two document
transformers last: `UnionCaseComponentsDocumentTransformer()` gives one
component for each union case, and `NullableEnumDocumentTransformer()` fixes
nullable enums.

## Agent skill

The repository has an [Agent Skills](https://agentskills.io) skill that teaches
coding agents (Claude Code, Codex, GitHub Copilot, Cursor and others) how to
use this package:

```sh
npx skills add SelectViewData/SelectView.FSharp.SystemTextJson.OpenApi
```

## Build

```sh
dotnet test --solution SelectView.FSharp.SystemTextJson.OpenApi.slnx
```

## License

[MIT](https://github.com/SelectViewData/SelectView.FSharp.SystemTextJson.OpenApi/blob/main/LICENSE)
