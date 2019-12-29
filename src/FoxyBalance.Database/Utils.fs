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

    /// Converts the column to a string option, handling null and empty values. Will throw an exception if the column type
    /// is not string.
    let stringColumnToOption (columnOrdinal: int) (reader: IDataReader) =
        let columnName = reader.GetName columnOrdinal
        match reader.GetValue columnOrdinal with
        | :? System.DBNull
        | :? System.String as x when System.String.IsNullOrEmpty (string x) ->
            None
        | :? System.String as x ->
            Some x
        | x ->
            failwithf
                "Column value was not a string or null. Column name: %s. Value type: %s."
                columnName
                (reader.GetDataTypeName columnOrdinal)
                
    /// Parses the column from the data reader, returning None if it is null or an empty string. Maps the value with the
    /// mapper function (e.g. to turn the object into a string or an int).
    let readColumn name mapper (reader : IDataReader) =
        let column = reader.GetOrdinal name
        match reader.GetValue column with
        | :? System.DBNull
        | :? System.String as x when System.String.IsNullOrEmpty (string x) ->
            None
        | x ->
            Some x |> Option.map mapper 

