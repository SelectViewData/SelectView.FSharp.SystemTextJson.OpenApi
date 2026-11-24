module SelectView.FSharp.SystemTextJson.OpenApi.Tests.BootstrapTests

open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Faqt
open Faqt.Operators
open Giraffe
open Giraffe.EndpointRouting
open Giraffe.OpenApi
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open SelectView.FSharp.SystemTextJson.OpenApi
open SelectView.FSharp.SystemTextJson.OpenApi.Tests.OpenApiTestSetup
open Xunit

type Shape =
  | Circle of radius: float
  | Square of side: float

let private createApp () =
  task {
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting().AddGiraffe().AddEndpointsApiExplorer() |> ignore
    builder.Services.AddOpenApiForSystemTextJson(fsharpJsonOptions) |> ignore

    let app = builder.Build()
    app.UseRouting() |> ignore

    app.UseGiraffe [
      POST [
        route "/shape" (text "ok")
        |> addOpenApi (
          OpenApiConfig(requestBody = RequestBody(typeof<Shape>, contentTypes = [| "application/json" |]))
        )
      ]
    ]
    |> ignore

    app.MapOpenApi("/openapi/{documentName}.json") |> ignore
    return app
  }

[<Fact>]
let ``AddOpenApiForSystemTextJson describes the JSON that the configured serializer writes`` () : Task =
  task {
    let! app = createApp ()
    use app = app
    do! app.StartAsync()

    let serializerOptions =
      app.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions

    let json = JsonSerializer.Serialize(Circle 1.0, serializerOptions)

    use client = app.GetTestClient()
    let! document = client.GetStringAsync("/openapi/v1.json")
    let document = JsonNode.Parse(document) |> nonNull

    let schema =
      getSchemaForPath document "/shape"
      |> expandSchemaRefs document
      |> fun x -> x.ToJsonString()

    %json.Should().Be("""{"kind":"Circle","radius":1}""")
    %schema.Should().Contain("\"kind\"").And.Contain("\"radius\"").And.Contain("\"side\"")
  }
