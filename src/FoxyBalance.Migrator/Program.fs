open CommandLine
open FoxyBalance.Migrations.Migrator
open FoxyBalance.Migrator.Options

let processParserErrors (result : NotParsed<obj>) =
    let error = Seq.head result.Errors
    
    match error.Tag with
    | ErrorType.MissingRequiredOptionError ->
        Exit Failure
    | ErrorType.HelpRequestedError
    | ErrorType.HelpVerbRequestedError
    | ErrorType.VersionRequestedError ->
        // Parser will have already written the options to console
        Exit Success
    | x ->
        Problem $"Received parser error %A{x}"
        
let run connStr action =
    migrate action connStr
    Exit Success
        
let parseAndRun args =
    let result = Parser.Default.ParseArguments<UpOptions, ToOptions, BaselineOptions> args
    let runResult =
        match result with
        | :? Parsed<obj> as opts ->
            match opts.Value with
            | :? UpOptions as up ->
                MigrationTarget.Latest
                |> run up.connectionString 
            | :? ToOptions as opts ->
                MigrationTarget.Target opts.value
                |> run opts.connectionString 
            | :? BaselineOptions as opts ->
                MigrationTarget.Baseline opts.value
                |> run opts.connectionString 
            | x ->
                failwithf "Unhandled parsed command options type '%s'" (x.GetType().FullName)
        | :? NotParsed<obj> as notParsed ->
            processParserErrors notParsed
        | x ->
            sprintf "Unhandled parse arguments result type '%s'" (x.GetType().FullName)
            |> Problem 
        
    match runResult with
    | Exit exitType ->
        exitType
    | ShowHelp exitType ->
        exitType
    | Problem message ->
        eprintfn "%s" message
        Failure 

[<EntryPoint>]
let main argv =
    match parseAndRun argv with
    | Success -> 0
    | Failure -> 1
