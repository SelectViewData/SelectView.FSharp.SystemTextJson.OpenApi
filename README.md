# SelectView.FSharp.SystemTextJson.OpenApi

OpenAPI schemas for F# types that you serialize with
[FSharp.SystemTextJson](https://github.com/Tarmil/FSharp.SystemTextJson).

`Microsoft.AspNetCore.OpenApi` describes F# types as plain .NET classes. The
schemas then do not match the JSON that FSharp.SystemTextJson writes. This
package adds schema and document transformers that read your
`JsonFSharpOptions` and describe the real JSON:

- `option` and `voption`: nullable values
- records: required and optional properties, including `Skippable<'T>` fields
- unions: tags, internal and external encodings, named fields, unwrapped cases,
  fieldless cases as strings, and one component for each case
- tuples, F# collections (`list`, `Set`, `Map`), and `JsonNode`
- enums: string and number encodings, also when nullable
- per-type options from `[<JsonFSharpConverter(...)>]`

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

1. It adds `fsOptions` to the JSON options of minimal APIs
   (`Microsoft.AspNetCore.Http.Json.JsonOptions`). MVC controllers and Giraffe
   use their own serializer options: add `fsOptions` there yourself.
2. It registers the OpenAPI document (`v1` by default) with all transformers.
3. It sets the OpenAPI version to 3.1.

Use the optional `documentName` and `setup` arguments to change the document
name or to add your own transformers.

To use only some transformers, add them to your own `AddOpenApi` call, for
example `options.AddSchemaTransformer(UnionSchemaTransformer(fsOptions))`.
Then also add `fsOptions` to your serializer options yourself, and add the two
document transformers: `UnionCaseComponentsDocumentTransformer()` gives one
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
