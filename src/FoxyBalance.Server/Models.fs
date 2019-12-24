namespace FoxyBalance.Server.Models

open Microsoft.Extensions.Configuration

type IConstants =
    abstract member HashingKey : string with get
    abstract member ConnectionString : string with get
    
type Constants(config : IConfiguration) =
    let get key =
        match config.Item key with
        | null
        | "" -> failwithf "Constant %s was not found in configuration settings." key
        | x -> x 
    let connStr key =
        match config.GetConnectionString key with
        | null
        | "" -> failwithf "Connection String %s was not found in configuration settings." key
        | x -> x
        
    interface IConstants with
        member val HashingKey = get "HashingKey"
        member val ConnectionString = connStr "SqlDatabase"
        
type DatabaseOptions(constants : IConstants) =
    interface FoxyBalance.Database.Models.IDatabaseOptions with
        member val ConnectionString = constants.ConnectionString
    
type Session =
    { UserId : int }
    
module ViewModels = 
    type LoginViewModel =
        { Error : string option
          Username : string option }
     
module RequestModels =
    ()