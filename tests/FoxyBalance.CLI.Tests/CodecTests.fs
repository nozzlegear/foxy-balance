namespace FoxyBalance.CLI.Tests

open System
open FoxyBalance.CLI.Domain
open Xunit
open Faqt
open Faqt.Operators

type CodecTests() =


    // ---- TokenResponse ----

    [<Fact>]
    let ``TokenResponse round-trips through encode/decode`` () =
        let token : TokenResponse =
            { AccessToken = "access-token-123"
              RefreshToken = "refresh-token-456"
              ExpiresIn = 3600
              TokenType = "Bearer" }

        let json = Codecs.serialize Codecs.tokenResponseEncoder token
        let result = Codecs.deserialize Codecs.tokenResponseDecoder json

        %result.Should().Be(Ok token)

    [<Fact>]
    let ``TokenResponse decodes from server JSON`` () =
        let json = """{"accessToken":"a1","refreshToken":"r1","expiresIn":3600,"tokenType":"Bearer"}"""
        let result = Codecs.deserialize Codecs.tokenResponseDecoder json

        %result.Should().Be(Ok {
            AccessToken = "a1"
            RefreshToken = "r1"
            ExpiresIn = 3600
            TokenType = "Bearer"
        })

    [<Fact>]
    let ``TokenResponse deserialization fails on missing field`` () =
        let json = """{"accessToken":"a1"}"""
        let result = Codecs.deserialize Codecs.tokenResponseDecoder json

        %result.IsError.Should().BeTrue()

    // ---- ApiError ----

    [<Fact>]
    let ``ApiError decodes from server JSON`` () =
        let json = """{"error":"Invalid credentials","details":null}"""
        let result = Codecs.deserialize Codecs.apiErrorDecoder json

        %result.Should().Be(Ok { Error = "Invalid credentials"; Details = None })

    [<Fact>]
    let ``ApiError decodes with details list`` () =
        let json = """{"error":"Validation failed","details":["Field X is required","Field Y is invalid"]}"""
        let result = Codecs.deserialize Codecs.apiErrorDecoder json

        %result.Should().Be(Ok {
            Error = "Validation failed"
            Details = Some ["Field X is required"; "Field Y is invalid"]
        })

    // ---- HalLink ----

    [<Fact>]
    let ``HalLink round-trips with method and templated`` () =
        let link : HalLink =
            { Href = "/api/v1/transactions/123"
              Method = Some "PUT"
              Templated = Some true }

        let json = Codecs.serialize Codecs.halLinkEncoder link
        let result = Codecs.deserialize Codecs.halLinkDecoder json

        %result.Should().Be(Ok link)

    [<Fact>]
    let ``HalLink decodes without optional fields`` () =
        let json = """{"href":"/api/v1/balance"}"""
        let result = Codecs.deserialize Codecs.halLinkDecoder json

        %result.Should().Be(Ok {
            Href = "/api/v1/balance"
            Method = None
            Templated = None
        })

    // ---- HalResource<TokenResponse> ----

    [<Fact>]
    let ``HalResource<TokenResponse> decodes from server auth response`` () =
        let json = """{
            "data": {
                "accessToken": "access-abc",
                "refreshToken": "refresh-xyz",
                "expiresIn": 3600,
                "tokenType": "Bearer"
            },
            "links": {
                "self": {"href": "/api/v1/auth/token"},
                "token-refresh": {"href": "/api/v1/auth/refresh", "method": "POST"}
            },
            "embedded": null
        }"""

        let result = Codecs.deserializeHalResource Codecs.tokenResponseDecoder json

        match result with
        | Ok hal ->
            %hal.Data.AccessToken.Should().Be("access-abc")
            %hal.Data.RefreshToken.Should().Be("refresh-xyz")
            %hal.Data.ExpiresIn.Should().Be(3600)
            %hal.Data.TokenType.Should().Be("Bearer")
            %hal.Links.IsSome.Should().BeTrue()
            hal.Links.Value
            |> Map.tryFind "self"
            |> fun l -> %l.IsSome.Should().BeTrue()
            hal.Links.Value
            |> Map.tryFind "self"
            |> Option.iter (fun l -> %l.Href.Should().Be("/api/v1/auth/token"))
        | Error e ->
            Assert.Fail($"Expected Ok, got Error: {e}")

    [<Fact>]
    let ``extractData extracts TokenResponse from HAL wrapper`` () =
        let json = """{
            "data": {
                "accessToken": "a",
                "refreshToken": "r",
                "expiresIn": 3600,
                "tokenType": "Bearer"
            },
            "links": {}
        }"""

        let result = Codecs.extractData Codecs.tokenResponseDecoder json

        %result.Should().Be(Ok {
            AccessToken = "a"
            RefreshToken = "r"
            ExpiresIn = 3600
            TokenType = "Bearer"
        })

    // ---- TransactionDto ----

    [<Fact>]
    let ``TransactionDto round-trips through encode/decode`` () =
        let dto : TransactionDto =
            { Id = 42L
              Name = "Test Transaction"
              Amount = 99.50m
              DateCreated = DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero)
              Type = "debit"
              Status = "pending"
              ClearDate = Some (DateTimeOffset(2024, 1, 16, 0, 0, 0, TimeSpan.Zero))
              RecurringBillId = Some 5L
              AutoGenerated = true
              ImportId = Some "import-123" }

        let json = Codecs.serialize Codecs.transactionDtoEncoder dto
        let result = Codecs.deserialize Codecs.transactionDtoDecoder json

        %result.Should().Be(Ok dto)

    [<Fact>]
    let ``TransactionDto decodes with minimal fields`` () =
        let json = """{
            "id": 1,
            "name": "Coffee",
            "amount": 5.50,
            "dateCreated": "2024-01-15T10:30:00+00:00",
            "type": "debit",
            "status": "cleared"
        }"""

        let result = Codecs.deserialize Codecs.transactionDtoDecoder json

        match result with
        | Ok dto ->
            %dto.Id.Should().Be(1L)
            %dto.Name.Should().Be("Coffee")
            %dto.Amount.Should().Be(5.50m)
            %dto.Type.Should().Be("debit")
            %dto.Status.Should().Be("cleared")
            %dto.ClearDate.Should().Be(None)
            %dto.RecurringBillId.Should().Be(None)
            %dto.AutoGenerated.Should().BeFalse()
            %dto.ImportId.Should().Be(None)
        | Error e ->
            Assert.Fail($"Expected Ok, got Error: {e}")

    // ---- RecurringBillDto ----

    [<Fact>]
    let ``RecurringBillDto round-trips through encode/decode`` () =
        let dto : RecurringBillDto =
            { Id = 10L
              Name = "Rent"
              Amount = 1500.00m
              WeekOfMonth = 1
              DayOfWeek = 1
              DateCreated = DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
              LastAppliedDate = Some (DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero))
              Active = true }

        let json = Codecs.serialize Codecs.recurringBillDtoEncoder dto
        let result = Codecs.deserialize Codecs.recurringBillDtoDecoder json

        %result.Should().Be(Ok dto)

    // ---- TransactionSum ----

    [<Fact>]
    let ``TransactionSum round-trips through encode/decode`` () =
        let sum : TransactionSum =
            { Sum = 1000.00m
              PendingSum = 200.00m
              ClearedSum = 800.00m
              PendingDebitSum = 150.00m
              ClearedDebitSum = 600.00m
              PendingCreditSum = 50.00m
              ClearedCreditSum = 200.00m }

        let json = Codecs.serialize Codecs.transactionSumEncoder sum
        let result = Codecs.deserialize Codecs.transactionSumDecoder json

        %result.Should().Be(Ok sum)

    // ---- MatchSuggestionDto ----

    [<Fact>]
    let ``MatchSuggestionDto round-trips through encode/decode`` () =
        let dto : MatchSuggestionDto =
            { Transaction = {
                Id = 1L
                Name = "Tx"
                Amount = 10.00m
                DateCreated = DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                Type = "debit"
                Status = "pending"
                ClearDate = None
                RecurringBillId = None
                AutoGenerated = false
                ImportId = None }
              RecurringBill = {
                Id = 2L
                Name = "Bill"
                Amount = 10.00m
                WeekOfMonth = 1
                DayOfWeek = 1
                DateCreated = DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                LastAppliedDate = None
                Active = true }
              MatchScore = 0.95m }

        let json = Codecs.serialize Codecs.matchSuggestionDtoEncoder dto
        let result = Codecs.deserialize Codecs.matchSuggestionDtoDecoder json

        %result.Should().Be(Ok dto)

    // ---- ImportResultDto ----

    [<Fact>]
    let ``ImportResultDto round-trips through encode/decode`` () =
        let dto : ImportResultDto =
            { ImportedCount = 42
              TotalCount = 50
              SkippedCount = 8 }

        let json = Codecs.serialize Codecs.importResultDtoEncoder dto
        let result = Codecs.deserialize Codecs.importResultDtoDecoder json

        %result.Should().Be(Ok dto)

    // ---- Request Encoders ----

    [<Fact>]
    let ``TokenExchangeRequest encodes correctly`` () =
        let request : TokenExchangeRequest =
            { ApiKey = "key123"
              ApiSecret = "secret456" }

        let json = Codecs.serialize Codecs.tokenExchangeRequestEncoder request

        %json.Should().Contain("key123")
        %json.Should().Contain("secret456")

    [<Fact>]
    let ``TokenRefreshRequest encodes correctly`` () =
        let request : TokenRefreshRequest = { RefreshToken = "refresh-abc" }
        let json = Codecs.serialize Codecs.tokenRefreshRequestEncoder request

        %json.Should().Contain("refresh-abc")

    [<Fact>]
    let ``ApiMatchRequest encodes correctly`` () =
        let request : ApiMatchRequest = { TransactionId = 42L; BillId = 7L }
        let json = Codecs.serialize Codecs.apiMatchRequestEncoder request

        %json.Should().Contain("42")
        %json.Should().Contain("7")

    // ---- HalResource and HalCollection ----

    [<Fact>]
    let ``HalResource<TransactionDto> round-trips through serialize/deserialize`` () =
        let resource : HalResource<TransactionDto> = {
            Data = {
                Id = 1L
                Name = "Test"
                Amount = 50.00m
                DateCreated = DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                Type = "debit"
                Status = "pending"
                ClearDate = None
                RecurringBillId = None
                AutoGenerated = false
                ImportId = None }
            Links = Some (Map.ofList [
                "self", { Href = "/api/v1/transactions/1"; Method = None; Templated = None }
                "update", { Href = "/api/v1/transactions/1"; Method = Some "PUT"; Templated = None }
            ])
            Embedded = None }

        let json = Codecs.serializeHalResource Codecs.transactionDtoEncoder resource
        let result = Codecs.deserializeHalResource Codecs.transactionDtoDecoder json

        match result with
        | Ok hal ->
            %hal.Data.Id.Should().Be(1L)
            %hal.Data.Name.Should().Be("Test")
            %hal.Links.IsSome.Should().BeTrue()
            hal.Links.Value
            |> Map.tryFind "self"
            |> fun l -> %l.IsSome.Should().BeTrue()
            hal.Links.Value
            |> Map.tryFind "update"
            |> Option.iter (fun l -> %l.Method.IsSome.Should().BeTrue())
        | Error e ->
            Assert.Fail($"Expected Ok, got Error: {e}")

    [<Fact>]
    let ``HalCollection<TransactionDto> round-trips through serialize/deserialize`` () =
        let collection : HalCollection<TransactionDto> = {
            Items = [
                { Data = {
                    Id = 1L
                    Name = "Tx1"
                    Amount = 10.00m
                    DateCreated = DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    Type = "debit"
                    Status = "pending"
                    ClearDate = None
                    RecurringBillId = None
                    AutoGenerated = false
                    ImportId = None }
                  Links = None
                  Embedded = None }
                { Data = {
                    Id = 2L
                    Name = "Tx2"
                    Amount = 20.00m
                    DateCreated = DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)
                    Type = "credit"
                    Status = "cleared"
                    ClearDate = None
                    RecurringBillId = None
                    AutoGenerated = false
                    ImportId = None }
                  Links = None
                  Embedded = None }
            ]
            Page = 1
            TotalPages = 3
            TotalCount = 25
            Links = Some (Map.ofList [
                "self", { Href = "/api/v1/transactions?page=1"; Method = None; Templated = None }
            ]) }

        let json = Codecs.serializeHalCollection Codecs.transactionDtoEncoder collection
        let result = Codecs.deserializeHalCollection Codecs.transactionDtoDecoder json

        match result with
        | Ok col ->
            %col.Items.Length.Should().Be(2)
            %col.Page.Should().Be(1)
            %col.TotalPages.Should().Be(3)
            %col.TotalCount.Should().Be(25)
            %col.Items.[0].Data.Name.Should().Be("Tx1")
            %col.Items.[1].Data.Name.Should().Be("Tx2")
        | Error e ->
            Assert.Fail($"Expected Ok, got Error: {e}")

    // ---- Error cases ----

    [<Fact>]
    let ``Deserialization fails on invalid JSON`` () =
        let json = "not valid json"
        let result = Codecs.deserialize Codecs.tokenResponseDecoder json

        %result.IsError.Should().BeTrue()

    [<Fact>]
    let ``Deserialization fails on wrong type`` () =
        let json = """{"accessToken": 12345}"""
        let result = Codecs.deserialize Codecs.tokenResponseDecoder json

        %result.IsError.Should().BeTrue()
