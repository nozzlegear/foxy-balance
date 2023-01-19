module FoxyBalance.Sync.Tests.HttpQueryTests

open FoxyBalance.Sync
open Xunit

[<Fact>]
let ``Should parse querystring values from a path`` () =
    let path = "/v2/sales?page=2"
    let query =
        Http.Query.parseFromPath path
        |> List.choose id
    let expected = [
        "page", "2"
    ]

    Assert.Equal(expected, seq query)
    
[<Fact>]
let ``Should parse querystring values from a path when one of the values is empty`` () =
    let path = "/v2/sales?page=&foo=bar&baz="
    let query =
        Http.Query.parseFromPath path
        |> List.choose id
    let expected = [
        "page", ""
        "foo", "bar"
        "baz", ""
    ]
    
    Assert.Equal(expected, seq query)
    
[<Fact>]
let ``Should return an empty list when parsing querystring values from a path`` () =
    let path = "/v2/sales?"
    let query =
        Http.Query.parseFromPath path
        |> List.choose id
    let expected = List.empty
    
    Assert.Equal(expected, seq query)
    
[<Fact>]
let ``Should accept the entire path as a querystring when no question mark separator is found`` () =
    let path = "page=2&foo=bar&baz="
    let query =
        Http.Query.parseFromPath path
        |> List.choose id
    let expected = [
        "page", "2"
        "foo", "bar"
        "baz", ""
    ]

    Assert.Equal(expected, seq query)

[<Fact>]
let ``Should fail if the path does not have a querystring`` () =
    // This is likely a failure state for an application, but it's the expected behavior?
    // This method shouldn't be used if there isn't a querystring in the path.
    let path = "/v2/sales"
    let exn = Record.Exception(fun _ -> Http.Query.parseFromPath path |> ignore)

    Assert.NotNull(exn)

    let exn = Assert.IsType<System.ArgumentException>(exn)

    Assert.Equal("path", exn.ParamName)
    Assert.Equal("Path does not contain a querystring, unable to parse querystring from path. (Parameter 'path')", exn.Message)