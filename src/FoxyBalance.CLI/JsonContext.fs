namespace FoxyBalance.CLI.Domain

open System.Text.Json
open System.Text.Json.Serialization

/// Shared JSON serializer options for the CLI.
module JsonSerializerOptions =
    let defaults =
        let opts = JsonSerializerOptions()
        opts.PropertyNameCaseInsensitive <- true
        opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        opts
