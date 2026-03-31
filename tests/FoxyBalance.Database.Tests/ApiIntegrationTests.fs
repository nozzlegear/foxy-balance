namespace FoxyBalance.Database.Tests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Faqt
open Faqt.Operators
open FoxyBalance.Database
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Server.Api
open FoxyBalance.Server.Models
open FoxyBalance.Sync
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.Testing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Xunit

/// Test constants implementation for test environment
type TestApiConstants(connStr: string) =
    interface IConstants with
        member _.HashingKey = "test-hashing-key-for-integration-tests-must-be-long-enough"
        member _.ConnectionString = connStr

/// Test database options for API tests
type TestApiDatabaseOptions(connStr: string) =
    interface IDatabaseOptions with
        member _.ConnectionString = connStr

/// Test fixture that provides both database and API server
type ApiTestFixture() =
    let mutable container: Testcontainers.PostgreSql.PostgreSqlContainer = null
    let mutable dbName = String.Empty
    let mutable dbConn = String.Empty
    let mutable factory: WebApplicationFactory<Program.Marker> option = None
    let mutable httpClient: HttpClient option = None

    member _.ConnectionString = dbConn
    member _.HttpClient = httpClient.Value
    member _.Factory = factory.Value

    interface IAsyncLifetime with
        member _.InitializeAsync() =
            ValueTask(
                task {
                    // Initialize the database container
                    let builder = Testcontainers.PostgreSql.PostgreSqlBuilder()

                    container <-
                        builder
                            .WithImage(
                                "docker.io/library/postgres:18-alpine@sha256:154ea39af68ff30dec041cd1f1b5600009993724c811dbadde54126eb10bedd1"
                            )
                            .Build()

                    do! container.StartAsync(TestContext.Current.CancellationToken)

                    let runId =
                        Environment.GetEnvironmentVariable("CI_RUN_ID")
                        |> function
                            | null
                            | "" -> Guid.NewGuid().ToString("N")[..7]
                            | v -> v

                    dbName <- $"foxybalance_api_test_{runId}_{DateTimeOffset.UtcNow.Ticks}"
                    let connStr = container.GetConnectionString()

                    // Create the database
                    use cn = new Npgsql.NpgsqlConnection(connStr)
                    do! cn.OpenAsync()
                    use cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE {dbName};", cn)
                    let! _ = cmd.ExecuteNonQueryAsync()

                    dbConn <- connStr + $";Database={dbName}"

                    // Run migrations
                    FoxyBalance.Migrations.Migrator.migrate
                        FoxyBalance.Migrations.Migrator.MigrationTarget.Latest
                        dbConn

                    // Create the test server with the test database connection
                    let webAppFactory =
                        (new WebApplicationFactory<Program.Marker>())
                            .WithWebHostBuilder(fun builder ->
                                builder.UseEnvironment("Testing") |> ignore

                                // Add test configuration
                                builder.ConfigureAppConfiguration(fun _ config ->
                                    let testConfig =
                                        dict
                                            [ "HashingKey",
                                              "test-hashing-key-for-integration-tests-must-be-long-enough"
                                              "ConnectionStrings:Database", dbConn ]

                                    config.AddInMemoryCollection(testConfig) |> ignore)
                                |> ignore

                                builder.ConfigureTestServices(fun services ->
                                    // Override the constants and database options to use the test container
                                    services.AddSingleton<IConstants>(TestApiConstants(dbConn)) |> ignore

                                    services.AddSingleton<IDatabaseOptions>(TestApiDatabaseOptions(dbConn))
                                    |> ignore

                                    services.AddScoped<IUserDatabase, UserDatabase>() |> ignore
                                    services.AddScoped<ITransactionDatabase, TransactionDatabase>() |> ignore
                                    services.AddScoped<IRecurringBillDatabase, RecurringBillDatabase>() |> ignore
                                    services.AddScoped<IApiKeyDatabase, ApiKeyDatabase>() |> ignore
                                    services.AddScoped<IRefreshTokenDatabase, RefreshTokenDatabase>() |> ignore
                                    services.AddScoped<IIncomeDatabase, IncomeDatabase>() |> ignore
                                    services.AddScoped<FoxyBalance.Server.Services.BillMatchingService>() |> ignore

                                    services.AddScoped<FoxyBalance.Server.Services.RecurringBillApplicationService>()
                                    |> ignore

                                    services.AddFoxyBalanceSyncClients())
                                |> ignore)

                    factory <- Some webAppFactory
                    httpClient <- Some(webAppFactory.CreateClient())
                }
            )

        member _.DisposeAsync() =
            ValueTask(
                task {
                    if httpClient.IsSome then
                        httpClient.Value.Dispose()

                    if factory.IsSome then
                        do! factory.Value.DisposeAsync()

                    if container <> null then
                        do! container.DisposeAsync()
                }
            )

[<CollectionDefinition("ApiIntegration")>]
type ApiIntegrationCollection() =
    interface ICollectionFixture<ApiTestFixture>

/// Helper module for API tests
module ApiTestHelpers =
    let jsonOptions =
        let opts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        // Use camelCase for serialization to match Giraffe's default JSON configuration
        opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        opts

    let postJson (client: HttpClient) (url: string) (body: obj) =
        task {
            let content =
                new StringContent(JsonSerializer.Serialize(body, jsonOptions), Encoding.UTF8, "application/json")

            return! client.PostAsync(url, content)
        }

    let putJson (client: HttpClient) (url: string) (body: obj) =
        task {
            let content =
                new StringContent(JsonSerializer.Serialize(body, jsonOptions), Encoding.UTF8, "application/json")

            return! client.PutAsync(url, content)
        }

    let getWithAuth (client: HttpClient) (url: string) (accessToken: string) =
        task {
            let request = new HttpRequestMessage(HttpMethod.Get, url)
            request.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", accessToken)
            return! client.SendAsync(request)
        }

    let postJsonWithAuth (client: HttpClient) (url: string) (body: obj) (accessToken: string) =
        task {
            let request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", accessToken)

            request.Content <-
                new StringContent(JsonSerializer.Serialize(body, jsonOptions), Encoding.UTF8, "application/json")

            return! client.SendAsync(request)
        }

    let putJsonWithAuth (client: HttpClient) (url: string) (body: obj) (accessToken: string) =
        task {
            let request = new HttpRequestMessage(HttpMethod.Put, url)
            request.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", accessToken)

            request.Content <-
                new StringContent(JsonSerializer.Serialize(body, jsonOptions), Encoding.UTF8, "application/json")

            return! client.SendAsync(request)
        }

    let deleteWithAuth (client: HttpClient) (url: string) (accessToken: string) =
        task {
            let request = new HttpRequestMessage(HttpMethod.Delete, url)
            request.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", accessToken)
            return! client.SendAsync(request)
        }

    let deserialize<'T> (response: HttpResponseMessage) =
        task {
            let! content = response.Content.ReadAsStringAsync()
            return JsonSerializer.Deserialize<'T>(content, jsonOptions)
        }

/// Response types for deserialization
[<CLIMutable>]
type TokenResponse =
    { AccessToken: string
      RefreshToken: string
      ExpiresIn: int
      TokenType: string }

[<CLIMutable>]
type HalResponse<'T> = { Data: 'T; Links: Map<string, obj> }

[<CLIMutable>]
type BalanceData =
    { CurrentBalance: decimal
      PendingBalance: decimal
      ClearedBalance: decimal
      TotalTransactions: int }

[<CLIMutable>]
type TransactionData =
    { Id: int64
      Name: string
      Amount: decimal
      Type: string
      Status: string }

[<CLIMutable>]
type BillData =
    { Id: int64
      Name: string
      Amount: decimal
      WeekOfMonth: int
      DayOfWeek: int
      Active: bool }

[<CLIMutable>]
type ErrorResponse = { Error: string }

[<Collection("ApiIntegration")>]
type ApiAuthenticationTests(fixture: ApiTestFixture) =
    let client = fixture.HttpClient
    let bogus = Bogus.Faker()

    let userDatabase: IUserDatabase =
        UserDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

    let apiKeyService =
        let apiKeyDb: IApiKeyDatabase =
            ApiKeyDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

        ApiKeyService(apiKeyDb, "test-hashing-key-for-integration-tests-must-be-long-enough")

    let createTestUser () =
        userDatabase.CreateAsync
            { EmailAddress = bogus.Internet.Email()
              HashedPassword = bogus.Internet.Password() }

    let createApiKey userId name =
        apiKeyService.CreateApiKeyPair(userId, name)

    [<Fact>]
    member _.``POST /api/v1/auth/token should return tokens for valid API credentials``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! _, apiKey, apiSecret = createApiKey user.Id "Test Key"

            // Act
            let! response =
                ApiTestHelpers.postJson
                    client
                    "/api/v1/auth/token"
                    {| ApiKey = apiKey
                       ApiSecret = apiSecret |}

            // Assert
            let! body = response.Content.ReadAsStringAsync()

            if response.StatusCode <> HttpStatusCode.OK then
                failwith $"Expected 200 OK but got {response.StatusCode}. Body: {body}"

            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let doc = JsonDocument.Parse(body)
            let data = doc.RootElement.GetProperty("data")

            // HAL response wraps data in "data" property
            let accessToken = data.GetProperty("accessToken").GetString()
            let refreshToken = data.GetProperty("refreshToken").GetString()
            let tokenType = data.GetProperty("tokenType").GetString()
            let expiresIn = data.GetProperty("expiresIn").GetInt32()

            %accessToken.Should().NotBeNull().And.NotBe("")
            %refreshToken.Should().NotBeNull().And.NotBe("")
            %tokenType.Should().Be("Bearer")
            %expiresIn.Should().BeGreaterThan(0)
        }

    [<Fact>]
    member _.``POST /api/v1/auth/token should return 401 for invalid API credentials``() =
        task {
            // Act
            let! response =
                ApiTestHelpers.postJson
                    client
                    "/api/v1/auth/token"
                    {| ApiKey = "invalid"
                       ApiSecret = "invalid" |}

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.Unauthorized)
        }

    [<Fact>]
    member _.``POST /api/v1/auth/token should return 422 for missing credentials``() =
        task {
            // Act
            let! response = ApiTestHelpers.postJson client "/api/v1/auth/token" {| ApiKey = ""; ApiSecret = "" |}

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity)
        }

    [<Fact>]
    member _.``POST /api/v1/auth/refresh should return new tokens for valid refresh token``() =
        task {
            // Setup - get initial tokens
            let! user = createTestUser ()
            let! _, apiKey, apiSecret = createApiKey user.Id "Test Key"

            let! tokenResponse =
                ApiTestHelpers.postJson
                    client
                    "/api/v1/auth/token"
                    {| ApiKey = apiKey
                       ApiSecret = apiSecret |}

            let! body = tokenResponse.Content.ReadAsStringAsync()
            let tokenDoc = JsonDocument.Parse(body)

            let originalRefreshToken =
                tokenDoc.RootElement.GetProperty("data").GetProperty("refreshToken").GetString()

            // Act - refresh the tokens
            let! refreshResponse =
                ApiTestHelpers.postJson client "/api/v1/auth/refresh" {| RefreshToken = originalRefreshToken |}

            // Assert
            %refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK)

            let! refreshBody = refreshResponse.Content.ReadAsStringAsync()
            let refreshDoc = JsonDocument.Parse(refreshBody)
            let refreshData = refreshDoc.RootElement.GetProperty("data")

            let newAccessToken = refreshData.GetProperty("accessToken").GetString()
            let newRefreshToken = refreshData.GetProperty("refreshToken").GetString()

            %newAccessToken.Should().NotBeNull().And.NotBe("")
            %newRefreshToken.Should().NotBeNull().And.NotBe("")
            // Refresh token should be different (single-use)
            %newRefreshToken.Should().NotBe(originalRefreshToken)
        }

    [<Fact>]
    member _.``POST /api/v1/auth/refresh should return 401 for reused refresh token``() =
        task {
            // Setup - get initial tokens
            let! user = createTestUser ()
            let! _, apiKey, apiSecret = createApiKey user.Id "Test Key"

            let! tokenResponse =
                ApiTestHelpers.postJson
                    client
                    "/api/v1/auth/token"
                    {| ApiKey = apiKey
                       ApiSecret = apiSecret |}

            let! body = tokenResponse.Content.ReadAsStringAsync()
            let tokenDoc = JsonDocument.Parse(body)

            let refreshToken =
                tokenDoc.RootElement.GetProperty("data").GetProperty("refreshToken").GetString()

            // Use the refresh token once
            let! _ = ApiTestHelpers.postJson client "/api/v1/auth/refresh" {| RefreshToken = refreshToken |}

            // Act - try to use the same refresh token again
            let! secondRefresh = ApiTestHelpers.postJson client "/api/v1/auth/refresh" {| RefreshToken = refreshToken |}

            // Assert - should fail because token was already used
            %secondRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized)
        }

[<Collection("ApiIntegration")>]
type ApiBalanceTests(fixture: ApiTestFixture) =
    let client = fixture.HttpClient
    let bogus = Bogus.Faker()

    let userDatabase: IUserDatabase =
        UserDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

    let transactionDatabase: ITransactionDatabase =
        TransactionDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

    let apiKeyService =
        let apiKeyDb: IApiKeyDatabase =
            ApiKeyDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

        ApiKeyService(apiKeyDb, "test-hashing-key-for-integration-tests-must-be-long-enough")

    let createTestUser () =
        userDatabase.CreateAsync
            { EmailAddress = bogus.Internet.Email()
              HashedPassword = bogus.Internet.Password() }

    let getAccessToken userId =
        task {
            let! _, apiKey, apiSecret = apiKeyService.CreateApiKeyPair(userId, "Test Key")

            let! response =
                ApiTestHelpers.postJson
                    client
                    "/api/v1/auth/token"
                    {| ApiKey = apiKey
                       ApiSecret = apiSecret |}

            let! body = response.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(body)
            return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()
        }

    let createTransaction userId name amount =
        task {
            let partial: PartialTransaction =
                { Name = name
                  Amount = amount
                  DateCreated = DateTimeOffset.UtcNow
                  Status = Pending
                  Type = Debit
                  ImportId = None
                  RecurringBillId = None
                  AutoGenerated = false }

            return! transactionDatabase.CreateAsync(userId, partial)
        }

    [<Fact>]
    member _.``GET /api/v1/balance should return balance data``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            // Create some transactions
            let! _ = createTransaction user.Id "Test Transaction 1" 100M
            let! _ = createTransaction user.Id "Test Transaction 2" 50M

            // Act
            let! response = ApiTestHelpers.getWithAuth client "/api/v1/balance" accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("sum")
            %body.Should().Contain("pendingSum")
        }

    [<Fact>]
    member _.``GET /api/v1/balance should return 401 without authentication``() =
        task {
            // Act
            let! response = client.GetAsync("/api/v1/balance")

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.Unauthorized)
        }

    [<Fact>]
    member _.``GET /api/v1/balance should return 401 with invalid token``() =
        task {
            // Act
            let! response = ApiTestHelpers.getWithAuth client "/api/v1/balance" "invalid-token"

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.Unauthorized)
        }

[<Collection("ApiIntegration")>]
type ApiTransactionTests(fixture: ApiTestFixture) =
    let client = fixture.HttpClient
    let bogus = Bogus.Faker()

    let userDatabase: IUserDatabase =
        UserDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

    let apiKeyService =
        let apiKeyDb: IApiKeyDatabase =
            ApiKeyDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

        ApiKeyService(apiKeyDb, "test-hashing-key-for-integration-tests-must-be-long-enough")

    let createTestUser () =
        userDatabase.CreateAsync
            { EmailAddress = bogus.Internet.Email()
              HashedPassword = bogus.Internet.Password() }

    let getAccessToken userId =
        task {
            let! _, apiKey, apiSecret = apiKeyService.CreateApiKeyPair(userId, "Test Key")

            let! response =
                ApiTestHelpers.postJson
                    client
                    "/api/v1/auth/token"
                    {| ApiKey = apiKey
                       ApiSecret = apiSecret |}

            let! body = response.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(body)
            return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()
        }

    [<Fact>]
    member _.``POST /api/v1/transactions should create a transaction``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newTransaction =
                {| Name = "Test Transaction"
                   Amount = "99.99"
                   TransactionType = "debit"
                   Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                   ClearDate = ""
                   CheckNumber = "" |}

            // Act
            let! response = ApiTestHelpers.postJsonWithAuth client "/api/v1/transactions" newTransaction accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.Created)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Test Transaction")
            %body.Should().Contain("99.99")
        }

    [<Fact>]
    member _.``GET /api/v1/transactions should list transactions``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            // Create some transactions
            let tx1 =
                {| Name = "Transaction 1"
                   Amount = "50"
                   TransactionType = "debit"
                   Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                   ClearDate = ""
                   CheckNumber = "" |}

            let tx2 =
                {| Name = "Transaction 2"
                   Amount = "75"
                   TransactionType = "credit"
                   Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                   ClearDate = ""
                   CheckNumber = "" |}

            let! _ = ApiTestHelpers.postJsonWithAuth client "/api/v1/transactions" tx1 accessToken
            let! _ = ApiTestHelpers.postJsonWithAuth client "/api/v1/transactions" tx2 accessToken

            // Act
            let! response = ApiTestHelpers.getWithAuth client "/api/v1/transactions" accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Transaction 1")
            %body.Should().Contain("Transaction 2")
        }

    [<Fact>]
    member _.``GET /api/v1/transactions/:id should get a specific transaction``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newTransaction =
                {| Name = "Specific Transaction"
                   Amount = "123.45"
                   TransactionType = "debit"
                   Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                   ClearDate = ""
                   CheckNumber = "" |}

            let! createResponse =
                ApiTestHelpers.postJsonWithAuth client "/api/v1/transactions" newTransaction accessToken

            let! createBody = createResponse.Content.ReadAsStringAsync()

            // Extract the ID from the response (HAL format has links with self)
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64()

            // Act
            let! response = ApiTestHelpers.getWithAuth client $"/api/v1/transactions/{id}" accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Specific Transaction")
        }

    [<Fact>]
    member _.``PUT /api/v1/transactions/:id should update a transaction``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newTransaction =
                {| Name = "Original Name"
                   Amount = "100"
                   TransactionType = "debit"
                   Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                   ClearDate = ""
                   CheckNumber = "" |}

            let! createResponse =
                ApiTestHelpers.postJsonWithAuth client "/api/v1/transactions" newTransaction accessToken

            let! createBody = createResponse.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64()

            // Act
            let updateData =
                {| Name = "Updated Name"
                   Amount = "150"
                   TransactionType = "debit"
                   Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                   ClearDate = ""
                   CheckNumber = "" |}

            let! response = ApiTestHelpers.putJsonWithAuth client $"/api/v1/transactions/{id}" updateData accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Updated Name")
            %body.Should().Contain("150")
        }

    [<Fact>]
    member _.``DELETE /api/v1/transactions/:id should delete a transaction``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newTransaction =
                {| Name = "To Be Deleted"
                   Amount = "50"
                   TransactionType = "debit"
                   Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                   ClearDate = ""
                   CheckNumber = "" |}

            let! createResponse =
                ApiTestHelpers.postJsonWithAuth client "/api/v1/transactions" newTransaction accessToken

            let! createBody = createResponse.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64()

            // Act
            let! deleteResponse = ApiTestHelpers.deleteWithAuth client $"/api/v1/transactions/{id}" accessToken

            // Assert
            %deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent)

            // Verify it's gone
            let! getResponse = ApiTestHelpers.getWithAuth client $"/api/v1/transactions/{id}" accessToken
            %getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound)
        }

    [<Fact>]
    member _.``GET /api/v1/transactions/:id should return 404 for non-existent transaction``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            // Act
            let! response = ApiTestHelpers.getWithAuth client "/api/v1/transactions/999999" accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.NotFound)
        }

    [<Fact>]
    member _.``Transactions should be isolated between users``() =
        task {
            // Setup - create two users
            let! user1 = createTestUser ()
            let! user2 = createTestUser ()
            let! accessToken1 = getAccessToken user1.Id
            let! accessToken2 = getAccessToken user2.Id

            // Create transaction for user1
            let tx =
                {| Name = "User1 Transaction"
                   Amount = "100"
                   TransactionType = "debit"
                   Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                   ClearDate = ""
                   CheckNumber = "" |}

            let! createResponse = ApiTestHelpers.postJsonWithAuth client "/api/v1/transactions" tx accessToken1
            let! createBody = createResponse.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64()

            // Act - user2 tries to access user1's transaction
            let! response = ApiTestHelpers.getWithAuth client $"/api/v1/transactions/{id}" accessToken2

            // Assert - should return 404 (not 403, to avoid information disclosure)
            %response.StatusCode.Should().Be(HttpStatusCode.NotFound)
        }

[<Collection("ApiIntegration")>]
type ApiBillTests(fixture: ApiTestFixture) =
    let client = fixture.HttpClient
    let bogus = Bogus.Faker()

    let userDatabase: IUserDatabase =
        UserDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

    let apiKeyService =
        let apiKeyDb: IApiKeyDatabase =
            ApiKeyDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

        ApiKeyService(apiKeyDb, "test-hashing-key-for-integration-tests-must-be-long-enough")

    let createTestUser () =
        userDatabase.CreateAsync
            { EmailAddress = bogus.Internet.Email()
              HashedPassword = bogus.Internet.Password() }

    let getAccessToken userId =
        task {
            let! _, apiKey, apiSecret = apiKeyService.CreateApiKeyPair(userId, "Test Key")

            let! response =
                ApiTestHelpers.postJson
                    client
                    "/api/v1/auth/token"
                    {| ApiKey = apiKey
                       ApiSecret = apiSecret |}

            let! body = response.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(body)
            return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()
        }

    [<Fact>]
    member _.``POST /api/v1/bills should create a recurring bill``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newBill =
                {| Name = "Monthly Rent"
                   Amount = "1500"
                   WeekOfMonth = "1"
                   DayOfWeek = "1" |}

            // Act
            let! response = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills" newBill accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.Created)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Monthly Rent")
            %body.Should().Contain("1500")
        }

    [<Fact>]
    member _.``GET /api/v1/bills should list recurring bills``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let bill1 =
                {| Name = "Bill 1"
                   Amount = "100"
                   WeekOfMonth = "1"
                   DayOfWeek = "1" |}

            let bill2 =
                {| Name = "Bill 2"
                   Amount = "200"
                   WeekOfMonth = "2"
                   DayOfWeek = "3" |}

            let! _ = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills" bill1 accessToken
            let! _ = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills" bill2 accessToken

            // Act
            let! response = ApiTestHelpers.getWithAuth client "/api/v1/bills" accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Bill 1")
            %body.Should().Contain("Bill 2")
        }

    [<Fact>]
    member _.``GET /api/v1/bills/:id should get a specific bill``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newBill =
                {| Name = "Specific Bill"
                   Amount = "75"
                   WeekOfMonth = "3"
                   DayOfWeek = "5" |}

            let! createResponse = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills" newBill accessToken
            let! createBody = createResponse.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64()

            // Act
            let! response = ApiTestHelpers.getWithAuth client $"/api/v1/bills/{id}" accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Specific Bill")
        }

    [<Fact>]
    member _.``PUT /api/v1/bills/:id should update a bill``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newBill =
                {| Name = "Original Bill"
                   Amount = "100"
                   WeekOfMonth = "1"
                   DayOfWeek = "1" |}

            let! createResponse = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills" newBill accessToken
            let! createBody = createResponse.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64()

            // Act
            let updateData =
                {| Name = "Updated Bill"
                   Amount = "200"
                   WeekOfMonth = "2"
                   DayOfWeek = "3" |}

            let! response = ApiTestHelpers.putJsonWithAuth client $"/api/v1/bills/{id}" updateData accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Updated Bill")
            %body.Should().Contain("200")
        }

    [<Fact>]
    member _.``DELETE /api/v1/bills/:id should delete a bill``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newBill =
                {| Name = "To Be Deleted"
                   Amount = "50"
                   WeekOfMonth = "1"
                   DayOfWeek = "1" |}

            let! createResponse = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills" newBill accessToken
            let! createBody = createResponse.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64()

            // Act
            let! deleteResponse = ApiTestHelpers.deleteWithAuth client $"/api/v1/bills/{id}" accessToken

            // Assert
            %deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent)

            // Verify it's gone
            let! getResponse = ApiTestHelpers.getWithAuth client $"/api/v1/bills/{id}" accessToken
            %getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound)
        }

    [<Fact>]
    member _.``POST /api/v1/bills/:id/toggle-active should toggle bill active status``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let newBill =
                {| Name = "Toggle Bill"
                   Amount = "50"
                   WeekOfMonth = "1"
                   DayOfWeek = "1" |}

            let! createResponse = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills" newBill accessToken
            let! createBody = createResponse.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64()

            // Bill starts as active by default
            // Act - toggle to inactive
            let! toggleResponse =
                ApiTestHelpers.postJsonWithAuth client $"/api/v1/bills/{id}/toggle-active" {| |} accessToken

            // Assert
            %toggleResponse.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = toggleResponse.Content.ReadAsStringAsync()
            %body.Should().Contain("false").And.Subject // active should be false
        }

[<Collection("ApiIntegration")>]
type ApiBillMatchingTests(fixture: ApiTestFixture) =
    let client = fixture.HttpClient
    let bogus = Bogus.Faker()

    let userDatabase: IUserDatabase =
        UserDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

    let transactionDatabase: ITransactionDatabase =
        TransactionDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

    let billDatabase: IRecurringBillDatabase =
        RecurringBillDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

    let apiKeyService =
        let apiKeyDb: IApiKeyDatabase =
            ApiKeyDatabase(TestApiDatabaseOptions(fixture.ConnectionString))

        ApiKeyService(apiKeyDb, "test-hashing-key-for-integration-tests-must-be-long-enough")

    let createTestUser () =
        userDatabase.CreateAsync
            { EmailAddress = bogus.Internet.Email()
              HashedPassword = bogus.Internet.Password() }

    let getAccessToken userId =
        task {
            let! _, apiKey, apiSecret = apiKeyService.CreateApiKeyPair(userId, "Test Key")

            let! response =
                ApiTestHelpers.postJson
                    client
                    "/api/v1/auth/token"
                    {| ApiKey = apiKey
                       ApiSecret = apiSecret |}

            let! body = response.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(body)
            return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()
        }

    let createBill userId name amount weekOfMonth dayOfWeek =
        billDatabase.CreateAsync(
            userId,
            { Name = name
              Amount = amount
              WeekOfMonth = weekOfMonth
              DayOfWeek = dayOfWeek }
        )

    let createImportedTransaction userId name amount =
        task {
            let partial: PartialTransaction =
                { Name = name
                  Amount = amount
                  DateCreated = DateTimeOffset.UtcNow
                  Status = Pending
                  Type = Debit
                  ImportId = Some(Guid.NewGuid().ToString())
                  RecurringBillId = None
                  AutoGenerated = false }

            return! transactionDatabase.CreateAsync(userId, partial)
        }

    [<Fact>]
    member _.``GET /api/v1/bills/match/suggestions should return match suggestions``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let! _ = createBill user.Id "Electric Bill" 100M FirstWeek DayOfWeek.Monday
            let! _ = createImportedTransaction user.Id "ELECTRIC COMPANY" 100M

            // Act
            let! response = ApiTestHelpers.getWithAuth client "/api/v1/bills/match/suggestions" accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain("Electric Bill")
            %body.Should().Contain("ELECTRIC COMPANY")
            %body.Should().Contain("matchScore")
        }

    [<Fact>]
    member _.``POST /api/v1/bills/match should execute a match``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let! bill = createBill user.Id "Water Bill" 50M FirstWeek DayOfWeek.Monday
            let! transaction = createImportedTransaction user.Id "WATER COMPANY" 50M

            // Act
            let matchRequest =
                {| TransactionId = transaction.Id
                   BillId = bill.Id |}

            let! response = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills/match" matchRequest accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.OK)

            // Verify the transaction now has the bill ID
            let! body = response.Content.ReadAsStringAsync()
            %body.Should().Contain($"{bill.Id}")
        }

    [<Fact>]
    member _.``POST /api/v1/bills/match should return 404 for non-existent bill``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let! transaction = createImportedTransaction user.Id "Some Transaction" 100M

            // Act
            let matchRequest =
                {| TransactionId = transaction.Id
                   BillId = 999999L |}

            let! response = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills/match" matchRequest accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.NotFound)
        }

    [<Fact>]
    member _.``POST /api/v1/bills/match should return 404 for non-existent transaction``() =
        task {
            // Setup
            let! user = createTestUser ()
            let! accessToken = getAccessToken user.Id

            let! bill = createBill user.Id "Test Bill" 100M FirstWeek DayOfWeek.Monday

            // Act
            let matchRequest =
                {| TransactionId = 999999L
                   BillId = bill.Id |}

            let! response = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills/match" matchRequest accessToken

            // Assert
            %response.StatusCode.Should().Be(HttpStatusCode.NotFound)
        }

    [<Fact>]
    member _.``POST /api/v1/bills/match should prevent matching another user's bill``() =
        task {
            // Setup - create two users
            let! user1 = createTestUser ()
            let! user2 = createTestUser ()
            let! accessToken2 = getAccessToken user2.Id

            // Create bill owned by user1
            let! bill = createBill user1.Id "User1 Bill" 100M FirstWeek DayOfWeek.Monday

            // Create transaction owned by user2
            let! transaction = createImportedTransaction user2.Id "Some Transaction" 100M

            // Act - user2 tries to match their transaction to user1's bill
            let matchRequest =
                {| TransactionId = transaction.Id
                   BillId = bill.Id |}

            let! response = ApiTestHelpers.postJsonWithAuth client "/api/v1/bills/match" matchRequest accessToken2

            // Assert - should return 404 (not 403, to avoid information disclosure)
            %response.StatusCode.Should().Be(HttpStatusCode.NotFound)
        }
