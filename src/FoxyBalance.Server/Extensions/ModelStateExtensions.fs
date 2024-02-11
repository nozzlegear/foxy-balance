namespace FoxyBalance.Server.Extensions

open System.Runtime.CompilerServices
open Microsoft.AspNetCore.Mvc.ModelBinding
open Microsoft.FSharp.Quotations

[<AutoOpen>]
module ModelStateExtensions =
    type ModelStateDictionary with
        member modelState.IsValidField(expr: Expr<'T>) =
            match expr with
            | Patterns.PropertyGet(_, pi, _) ->
                match modelState.TryGetValue (ModelNames.CreatePropertyModelName ("", pi.Name)) with
                | true, entry when entry.ValidationState = ModelValidationState.Valid -> true
                | _, _ -> false
            | _ ->
                raise (SwitchExpressionException $"Invalid expression. Expression was expected to return a property, but instead it is a {expr.Type} expression.")
