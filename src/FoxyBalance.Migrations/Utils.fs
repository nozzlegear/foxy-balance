namespace FoxyBalance.Migrations

open System
open System.IO
open System.Reflection
open System.Text
open System.Text.RegularExpressions

module Utils = 
    let private getPath fileName = 
        // Use the assembly to get the current directory, as it can switch between bin and project directory
        // https://github.com/dotnet/project-system/issues/589
        let currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)

        Path.Combine(currentDirectory, "Migrations", "sql", fileName)

    let readSqlFile fileName : string = 
        getPath fileName
        |> File.ReadAllText

    let readSqlFileBatches fileName : string seq = 
        let text = readSqlFile fileName
        let goRegex = Regex("^GO[;| ]*$", RegexOptions.Multiline)

        // Split the text of the file by each GO, removing empty entries after splitting (e.g. if the last line in the file is "GO")
        goRegex.Split text
        |> Seq.choose (fun str -> if String.IsNullOrWhiteSpace str then None else Some str)

