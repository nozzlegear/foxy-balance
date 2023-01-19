namespace FoxyBalance.Sync

open System
open System.Net.Http
open System.Text.Json
open System.Web
open JsonSharp

[<RequireQualifiedAccess>]
module Http =
    type Options = {
        Domain : string
        Path : string
        Method : HttpMethod
        Headers : Map<string, string>
        Query : Map<string, string>
        Body : HttpContent option
        Client : HttpClient option
        JsonOptions : JsonDocumentOptions option
    }
    
    module Query =
        let add (key: string) (value: string) =
            Some (key, value)
        let maybeAdd (key: string) (value: string option) =
            match value with
            | Some value -> Some (key, value)
            | None -> None
        let parse (query: string) =
            let values = HttpUtility.ParseQueryString query
            [ for key in values do
                  let value = values[key]
                  Some (key, value) ]
        let parseFromPath (path: string) =
            if not (path.Contains "?") && not (path.Contains "=") then
                raise (invalidArg "path" "Path does not contain a querystring, unable to parse querystring from path.")
            let query = 
                match path.IndexOf("?") with
                | x when x >= 0 -> path.Substring(x)
                | _ -> path
            parse query
        let (=>) key value =
            add key value
        let (=?>) key value =
            maybeAdd key value
    
    let private defaultHttpClient = new HttpClient()
    
    /// Sanitizes a path string, stripping any querystring values that may be present.
    let private sanitizePath (path: string) =
        match path.IndexOf "?" with
        | x when x >= 0 -> path.Substring(0, x)
        | _ -> path
    
    /// Turns a Map<string, string> into a querystring, encoding the values.
    let private buildQueryString (query : Map<string, string>) =
        query
        |> Seq.map (fun (KeyValue(key, value)) -> $"{key}={HttpUtility.UrlEncode(value)}")
        |> fun str -> String.Join("&", str)

    let connect domain =
        { Domain = domain
          Path = ""
          Method = HttpMethod.Get
          Headers = Map.empty
          Query = Map.empty
          Body = None
          Client = None
          JsonOptions = None }
        
    let path path options =
        { options with Path = path }
        
    let method method options =
        { options with Method = method }
        
    let headers (headers : (string * string) list) options =
        { options with Headers = Map.ofList headers }
        
    let query (query : (string * string) option list) options =
        { options with Query = Map.ofList (List.choose id query) }
        
    let body body options =
        { options with Body = Some body }
        
    let jsonBody body options =
        let content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        { options with Body = Some content }
        
    let client client options =
        { options with Client = Some client }
        
    let jsonOptions jsonOptions options =
        { options with JsonOptions = Some jsonOptions }
        
    let executeAsync (options : Options) = task {
        let uri = UriBuilder(options.Domain)
        uri.Path <- sanitizePath options.Path
        uri.Query <- buildQueryString options.Query
        use message = new HttpRequestMessage(options.Method, uri.Uri)

        for KeyValue (k, v) in options.Headers do
            message.Headers.Add(k, v)
        
        options.Body |> Option.iter (fun body ->
            message.Content <- body
        )
        
        let  client = Option.defaultValue defaultHttpClient options.Client
        let! result = client.SendAsync message
        
        return result.EnsureSuccessStatusCode()
    }
    
    let executeJsonAsync<'t> (read : ElementReader -> 't) (options : Options) = task {
        let! result = executeAsync options
        let contentType = result.Content.Headers.ContentType
        
        if not <| contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) then
            raise (HttpRequestException($"Expected HTTP response to contain JSON (\"application/json\"). Content-Type header indicates response is \"{contentType.MediaType}\"."))
        
        use! content = result.Content.ReadAsStreamAsync()
        let! reader =
            match options.JsonOptions with
            | Some jsonOptions -> ElementReader.parseAsync(content, jsonOptions)
            | None -> ElementReader.parseAsync(content)
        
        return read reader
    }