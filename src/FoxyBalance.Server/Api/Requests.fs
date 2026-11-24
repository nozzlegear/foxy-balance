namespace FoxyBalance.Server.Api.Requests

open System
open System.Text.Json
open System.Text.Json.Serialization

/// Reads a JSON string or number token into a string, so API clients can send IDs as either
/// `"123"` (CLI) or `123` (JSON number).
type LenientStringJsonConverter() =
    inherit JsonConverter<string>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : string =
        match reader.TokenType with
        | JsonTokenType.String -> reader.GetString()
        | JsonTokenType.Number -> (JsonDocument.ParseValue(&reader)).RootElement.GetRawText()
        | _ -> null

    override _.Write(writer: Utf8JsonWriter, value: string, _options: JsonSerializerOptions) =
        writer.WriteStringValue(value)

/// Request type for matching a transaction to a recurring bill.
[<CLIMutable>]
type ApiMatchRequest =
    { [<JsonConverter(typeof<LenientStringJsonConverter>)>]
      TransactionId: string
      [<JsonConverter(typeof<LenientStringJsonConverter>)>]
      RecurringTransactionId: string }
