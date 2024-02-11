namespace FoxyBalance.Server

open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc

[<AutoOpen>]
module ParameterViewOperators =
    /// Boxes the tuple value so that it plays nicely with the ParameterView map.
    let (=>) left right =
        left, box right

module Blazor =
    type ComponentConfiguration = {
        componentType: System.Type
        parameters: Map<string, obj>
    }

    let Component<'t when 't :> IComponent> =
        { componentType = typeof<'t>; parameters = Map.empty }

    let AddParameter key value config =
        { config with parameters = Map.add key value config.parameters }

    let RemoveParameter key config =
        { config with parameters = Map.remove key config.parameters }

    let SetParameters parameters config =
        { config with parameters = Map.ofList parameters }

    /// Sets a view parameter named "model" with the given value.
    let inline SetModelParameter<'t> (model: 't) config =
        AddParameter "model" model config

type ExtendedActionResult =
    | BlazorView of viewComponent: Blazor.ComponentConfiguration
    | InvalidModelState of viewComponent: Blazor.ComponentConfiguration
    | RedirectToAction of action: string * controllerName: string
    | RedirectToActionWithParams of action: string * controllerName: string * parameters: (string * obj) list
    | ErrorView of statusCode: int * message: string
    with
    interface IActionResult with
        member self.ExecuteResultAsync(context: ActionContext ) =
            match self with
            | BlazorView x ->
                let blazorAction = BlazorViewResult(x.componentType, x.parameters)
                blazorAction.ExecuteResultAsync(context)
            | InvalidModelState x ->
                let blazorAction = BlazorViewResult(x.componentType, x.parameters)
                context.HttpContext.Response.StatusCode <- StatusCodes.Status400BadRequest
                blazorAction.ExecuteResultAsync(context)
            | ErrorView (statusCode, message) ->
                let action =
                    Blazor.Component<FoxyBalance.BlazorViews.Components.Pages.Error>
                    |> Blazor.AddParameter "message" message
                let blazorAction = BlazorViewResult(action.componentType)
                context.HttpContext.Response.StatusCode <- statusCode
                blazorAction.ExecuteResultAsync(context)
            | RedirectToAction (action, controllerName) ->
                let action = RedirectToActionResult(action, controllerName, []) :> IActionResult
                action.ExecuteResultAsync(context)
            | RedirectToActionWithParams (action, controllerName, params') ->
                let action = RedirectToActionResult(action, controllerName, params') :> IActionResult
                action.ExecuteResultAsync(context)
