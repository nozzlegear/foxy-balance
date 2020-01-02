namespace FoxyBalance.Database

open System.Data
open System.Data.SqlClient
open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive

[<AutoOpen>]
module internal Utils =
    type ParamValue =
        | String of string
        | Int of int
        | Long of int64
        | Decimal of decimal 
        | Bool of bool
        | DateTime of System.DateTime
        | DateTimeOffset of System.DateTimeOffset
        | Null 
    
    let inline (=>) (key : string) (value : ParamValue) =
        let value =
            match value with
            | String s -> box s
            | Int i -> box i
            | Long l -> box l
            | Decimal d -> box d
            | Bool b -> box b
            | DateTime d -> box d
            | DateTimeOffset d -> box d
            | Null -> box null
            
        key, box value

    let inline withConnection connStr (fn : SqlConnection -> Task<_>) =
        task {
            let conn = new SqlConnection(connStr)
            let! result = fn conn
            conn.Dispose()
            return result 
        }

    let inline dictionaryToMap d =
        // Source: https://gist.github.com/theburningmonk/3363893
        d :> seq<_>
        |> Seq.map (|KeyValue|)
        |> Map.ofSeq
                
    /// Attempts to read the column from the reader, returning an Error if the column does not exist at all. Does not check
    /// for null or empty string values. 
    let readColumn name (reader : IDataReader) =
        match reader.GetOrdinal name with
        | x when x < 0 ->
            sprintf "Column %s was not returned in query." name
            |> Error
        | x ->
            reader.GetValue x
            |> Ok 

    /// Converts the column to a string option, handling null and empty values. 
    let stringColumnToOption name (reader: IDataReader) =
        readColumn name reader
        |> Result.bind (function
            | :? System.DBNull
            | :? System.String as x when System.String.IsNullOrEmpty (string x) ->
                Ok None
            | :? System.String as x ->
                Ok (Some x)
            | x ->
                let msg = 
                    sprintf 
                        "Column value was not a string or null. Column name: %s. Value type: %s."
                        name
                        (x.GetType().Name)
                Error msg
            )
