namespace FoxyBalance.Migrator.Options

open CommandLine

type ExitType =
    | Success
    | Failure
    
type RunResult =
    | Exit of ExitType
    | ShowHelp of ExitType
    | Problem of string 

[<Verb("up", HelpText = "Migrates the database up to the newest version.")>]
type UpOptions = {
    [<Option('c', "connection-string", Required = true, HelpText = "Connection string for the target database")>]
    connectionString : string
}

[<Verb("to", HelpText = "Migrates the database up or down to a specific version.")>]
type ToOptions = {
    [<Option('c', "connection-string", Required = true, HelpText = "Connection string for the target database")>]
    connectionString : string
   
    [<Value(0, Required = true, MetaName = "version", HelpText = "Target version")>]
    value : int64 
}

[<Verb("baseline", HelpText = "Baselines the database to a specific version. This marks the database as having migrated to that version, but does not actually run the migrations to get there.")>]
type BaselineOptions = {
    [<Option('c', "connection-string", Required = true, HelpText = "Connection string for the target database")>]
    connectionString : string
   
    [<Value(0, Required = true, MetaName = "version", HelpText = "Target version")>]
    value : int64 
}
