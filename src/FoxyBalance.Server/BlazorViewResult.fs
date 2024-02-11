namespace FoxyBalance.Server

open System
open System.IO
open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Web
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Mvc.Infrastructure
open Microsoft.Extensions.DependencyInjection

type BlazorViewResult(componentType: Type, parameterView: Map<string, obj>, statusCode: int) =
    inherit ActionResult()

    new(t) = BlazorViewResult(t, Map.empty, 200)
    new(t, statusCode) = BlazorViewResult(t, Map.empty, statusCode)
    new(t, parameterView) = BlazorViewResult(t, parameterView, 200)
    new(t, parameterView: (string * obj) list) = BlazorViewResult(t, Map.ofList parameterView, 200)

    member private this.RenderHtmlToStreamAsync(htmlRenderer: HtmlRenderer, streamWriter: TextWriter) =
        htmlRenderer.Dispatcher.InvokeAsync<unit>(fun () ->
            task {
                let! result = htmlRenderer.RenderComponentAsync(componentType, ParameterView.FromDictionary parameterView)
                result.WriteHtmlTo(streamWriter)
            })

    interface IStatusCodeActionResult with
        member that.StatusCode = Nullable statusCode

    interface IActionResult with
        override this.ExecuteResultAsync(ctx) =
            ArgumentNullException.ThrowIfNull(ctx, nameof ctx)

            let htmlRenderer =
                ctx.HttpContext.RequestServices.GetRequiredService<HtmlRenderer>()

            task {
                use streamWriter = new StreamWriter(ctx.HttpContext.Response.Body)
                do! this.RenderHtmlToStreamAsync(htmlRenderer, streamWriter)
                ctx.HttpContext.Response.ContentType <- "text/html"
                ctx.HttpContext.Response.StatusCode <- statusCode
            }
