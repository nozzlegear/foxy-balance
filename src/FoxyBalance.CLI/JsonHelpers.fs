namespace FoxyBalance.CLI

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Thoth.Json.Core

/// Reflection-free JSON encode/decode helpers that replace Thoth.Json.System.Text.Json.
/// Thoth.Json.System.Text.Json uses JsonSerializer.Serialize (reflection-based),
/// which throws under PublishTrimmed with JsonSerializerIsReflectionEnabled=false.
/// This module uses Utf8JsonWriter (no reflection) for encoding and JsonDocument.Parse
/// (no reflection) for decoding.
module JsonHelpers =

    // ---- Encoder helpers (JsonNode-based, no reflection) ----

    let encoderHelpers =
        { new IEncoderHelpers<JsonNode> with
            member _.encodeString value = JsonValue.Create(value)
            member _.encodeChar value = JsonValue.Create(value)
            member _.encodeDecimalNumber value = JsonValue.Create(value)
            member _.encodeBool value = JsonValue.Create(value)
            member _.encodeNull() = JsonValue.Create(null)
            member _.encodeObject(values) =
                let o = JsonObject()
                for key, value in values do
                    o.Add(key, value)
                o
            member _.encodeArray values = JsonArray(values)
            member _.encodeList values = JsonArray(values |> Seq.toArray)
            member _.encodeSeq values = JsonArray(values |> Seq.toArray)
            member _.encodeResizeArray values = JsonArray(values |> Seq.toArray)
            member _.encodeSignedIntegralNumber(value: int32) = JsonValue.Create(value)
            member _.encodeUnsignedIntegralNumber(value: uint32) = JsonValue.Create(value)
        }

    /// Serialize an IEncodable to a JSON string using Utf8JsonWriter (no reflection).
    let encodeToString (space: int) (value: IEncodable) : string =
        let jsonNode = Encode.toJsonValue encoderHelpers value
        use stream = new MemoryStream()
        let mutable writerOptions = JsonWriterOptions()
        if space > 0 then
            writerOptions.Indented <- true
            writerOptions.IndentSize <- space
            writerOptions.NewLine <- "\n"
        do
            use writer = new Utf8JsonWriter(stream, writerOptions)
            jsonNode.WriteTo(writer)
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    // ---- Decoder helpers (JsonElement-based, no reflection) ----

    let decoderHelpers =
        { new IDecoderHelpers<JsonElement> with
            member _.isString jsonValue = jsonValue.ValueKind = JsonValueKind.String
            member _.isNumber jsonValue = jsonValue.ValueKind = JsonValueKind.Number
            member _.isBoolean jsonValue =
                jsonValue.ValueKind = JsonValueKind.True || jsonValue.ValueKind = JsonValueKind.False
            member _.isNullValue jsonValue = jsonValue.ValueKind = JsonValueKind.Null
            member _.isArray jsonValue = jsonValue.ValueKind = JsonValueKind.Array
            member _.isObject jsonValue = jsonValue.ValueKind = JsonValueKind.Object
            member _.hasProperty fieldName jsonValue =
                let d = ref (JsonElement())
                jsonValue.ValueKind = JsonValueKind.Object && jsonValue.TryGetProperty(fieldName, d)
            member _.isIntegralValue jsonValue =
                jsonValue.ValueKind = JsonValueKind.Number
                && jsonValue.GetRawText().IndexOf('.') = -1
            member _.asString jsonValue = jsonValue.GetString()
            member _.asBoolean jsonValue = jsonValue.GetBoolean()
            member _.asArray jsonValue = jsonValue.EnumerateArray() |> Seq.toArray
            member _.asFloat jsonValue = jsonValue.GetDouble()
            member _.asFloat32 jsonValue = jsonValue.GetSingle()
            member _.asInt jsonValue = jsonValue.GetInt32()
            member _.getProperties jsonValue =
                jsonValue.EnumerateObject() |> Seq.map (fun prop -> prop.Name)
            member _.getProperty(fieldName, jsonValue) = jsonValue.GetProperty(fieldName)
            // Use GetRawText instead of JsonSerializer.Serialize (which is reflection-based)
            member _.anyToString jsonValue = jsonValue.GetRawText()
        }

    /// Decode a JSON string using JsonDocument.Parse (no reflection).
    let decodeFromString (decoder: Decoder<'T>) (json: string) : Result<'T, string> =
        try
            let options = JsonDocumentOptions(AllowTrailingCommas = true)
            use doc = JsonDocument.Parse(json, options)
            match decoder.Decode(decoderHelpers, doc.RootElement) with
            | Ok success -> Ok success
            | Error error ->
                let finalError = error |> Decode.Helpers.prependPath "$"
                Error(Decode.errorToString decoderHelpers finalError)
        with :? JsonException as ex ->
            Error("Given an invalid JSON: " + ex.Message)
