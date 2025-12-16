namespace FoxyBalance.Database.Tests

open System
open System.Runtime.CompilerServices
open Faqt
open Faqt.Operators
open FoxyBalance.Database
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Database.Tests.Domain
open Npgsql
open Xunit

type TestTransactionType =
    | Check = 1
    | NonRecurringBill = 2
    | RecurringBill = 3
    | Credit = 4
    | Debit = 5

[<Collection("TransactionDatabase")>]
type TransactionDatabaseTests(fixture: DbContainerFixture) =
    let database: ITransactionDatabase = TransactionDatabase(TestDatabaseOptions fixture)
    let userDatabase: IUserDatabase = UserDatabase(TestDatabaseOptions fixture)
    let bogus = Bogus.Faker()

    let createUser () =
        userDatabase.CreateAsync { EmailAddress = bogus.Internet.Email(); HashedPassword = bogus.Internet.Password() }

    [<Theory>]
    [<CombinatorialData>]
    member _.``CreateAsync should create a partial transaction``(isCleared: bool, transactionType: TestTransactionType) =
        task {
            // Setup
            let status =
                match isCleared with
                | true ->  Cleared (bogus.Date.RecentOffset 10)
                | false -> Pending
            let transactionType =
                match transactionType with
                | TestTransactionType.Check -> Check { CheckNumber = bogus.Random.Number(1000, 9999).ToString("D4") }
                | TestTransactionType.NonRecurringBill -> Bill { Recurring = false }
                | TestTransactionType.RecurringBill -> Bill { Recurring = true }
                | TestTransactionType.Credit -> Credit
                | TestTransactionType.Debit -> Debit
                | _ -> raise (SwitchExpressionException transactionType)

            let! user = createUser ()

            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = status
                  Type = transactionType
                  ImportId = None }

            // Act
            let! result = database.CreateAsync(user.Id, partialTransaction)

            // Assert
            %result.Id.Should().BeGreaterThan(0)
            %result.DateCreated.Should().BeCloseTo(partialTransaction.DateCreated, TimeSpan.FromSeconds 1L)
            %result.Amount.Should().Be(partialTransaction.Amount)
            %result.Name.Should().Be(partialTransaction.Name)
            %result.Status.Should().Be(partialTransaction.Status)
            %result.Type.Should().Be(partialTransaction.Type)
        }

    [<Fact>]
    member _.``CreateAsync should fail if the user does not exist``() =
        task {
            // Setup
            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None }
            let userId = -1

            // Act
            let act () =
                database.CreateAsync(userId, partialTransaction)
                |> Async.AwaitTask
                |> Async.RunSynchronously

            // Assert
            %act.Should().ThrowInner<PostgresException, _>()
                 .Whose
                 .Message.Should().Contain("violates foreign key constraint")
        }

    [<Theory>]
    [<CombinatorialData>]
    member _.``GetStatusAsync should return the transaction status``(isCleared: bool) =
        task {
            // Setup
            let! user = createUser ()
            let clearedDate = bogus.Date.RecentOffset 10
            let status =
                match isCleared with
                | true ->  Cleared clearedDate
                | false -> Pending

            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = status
                  Type = TransactionType.Debit
                  ImportId = None }

            let! created = database.CreateAsync(user.Id, partialTransaction)

            // Act
            let! result = database.GetStatusAsync(user.Id, created.Id)

            // Assert
            match (result, status) with
            | _, Pending -> %result.Should().Be(Pending)
            | Cleared clearedDate, Cleared expectedDate -> %clearedDate.Should().BeCloseTo(expectedDate, TimeSpan.FromSeconds 1L)
            | _ -> raise (SwitchExpressionException (box (result, status)))
        }

    [<Fact>]
    member _.``GetAsync should return the transaction when it exists``() =
        task {
            // Setup
            let! user = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None }

            let! created = database.CreateAsync(user.Id, partialTransaction)

            // Act
            let! result = database.GetAsync(user.Id, created.Id)

            // Assert
            %result.Should().BeSome()
            let transaction = result.Value
            %transaction.Id.Should().Be(created.Id)
            %transaction.Name.Should().Be(partialTransaction.Name)
            %transaction.Amount.Should().Be(partialTransaction.Amount)
            %transaction.Status.Should().Be(partialTransaction.Status)
            %transaction.Type.Should().Be(partialTransaction.Type)
        }

    [<Fact>]
    member _.``GetAsync should return None when the transaction does not exist``() =
        task {
            // Setup
            let! user = createUser ()
            let nonExistentId = 999999L

            // Act
            let! result = database.GetAsync(user.Id, nonExistentId)

            // Assert
            %result.Should().BeNone()
        }

    [<Fact>]
    member _.``GetAsync should return None when the transaction belongs to a different user``() =
        task {
            // Setup
            let! user1 = createUser ()
            let! user2 = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None }

            let! created = database.CreateAsync(user1.Id, partialTransaction)

            // Act
            let! result = database.GetAsync(user2.Id, created.Id)

            // Assert
            %result.Should().BeNone()
        }

    [<Fact>]
    member _.``ExistsAsync should return true when the transaction exists``() =
        task {
            // Setup
            let! user = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None }

            let! created = database.CreateAsync(user.Id, partialTransaction)

            // Act
            let! result = database.ExistsAsync(user.Id, created.Id)

            // Assert
            %result.Should().BeTrue()
        }

    [<Fact>]
    member _.``ExistsAsync should return false when the transaction does not exist``() =
        task {
            // Setup
            let! user = createUser ()
            let nonExistentId = 999999L

            // Act
            let! result = database.ExistsAsync(user.Id, nonExistentId)

            // Assert
            %result.Should().BeFalse()
        }

    [<Fact>]
    member _.``ExistsAsync should return false when the transaction belongs to a different user``() =
        task {
            // Setup
            let! user1 = createUser ()
            let! user2 = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None }

            let! created = database.CreateAsync(user1.Id, partialTransaction)

            // Act
            let! result = database.ExistsAsync(user2.Id, created.Id)

            // Assert
            %result.Should().BeFalse()
        }

    [<Fact>]
    member _.``UpdateAsync should update the transaction``() =
        task {
            // Setup
            let! user = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None }

            let! created = database.CreateAsync(user.Id, partialTransaction)

            let updatedTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 5
                  Status = Cleared (bogus.Date.RecentOffset 2)
                  Type = TransactionType.Credit
                  ImportId = None }

            // Act
            let! result = database.UpdateAsync(user.Id, created.Id, updatedTransaction)

            // Assert
            %result.Id.Should().Be(created.Id)
            %result.Name.Should().Be(updatedTransaction.Name)
            %result.Amount.Should().Be(updatedTransaction.Amount)
            %result.Status.Should().Be(updatedTransaction.Status)
            %result.Type.Should().Be(updatedTransaction.Type)
        }

    [<Fact>]
    member _.``UpdateAsync should preserve the original DateCreated``() =
        task {
            // Setup
            let! user = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None }

            let! created = database.CreateAsync(user.Id, partialTransaction)

            let updatedTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 5
                  Status = Cleared (bogus.Date.RecentOffset 2)
                  Type = TransactionType.Credit
                  ImportId = None }

            // Act
            let! result = database.UpdateAsync(user.Id, created.Id, updatedTransaction)

            // Assert
            %result.DateCreated.Should().BeCloseTo(created.DateCreated, TimeSpan.FromSeconds 1L)
        }

    [<Fact>]
    member _.``ListAsync should return transactions for a user``() =
        task {
            // Setup
            let! user = createUser ()
            let transactions = [1..5] |> List.map (fun i ->
                { Name = $"Transaction {i}"
                  Amount = decimal i
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None })

            for transaction in transactions do
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            let listOptions = { Limit = 10; Offset = 0; Order = Ascending; Status = AllTransactions }

            // Act
            let! result = database.ListAsync(user.Id, listOptions)

            // Assert
            %result.Should().HaveLength(5)
        }

    [<Theory>]
    [<CombinatorialData>]
    member _.``ListAsync should respect limit, offset and order``(orderIsAscending: bool) =
        task {
            // Setup
            let! user = createUser ()
            let order = if orderIsAscending then Ascending else Descending
            let transactions = [1..10] |> List.map (fun i ->
                { Name = $"Transaction {i}"
                  Amount = decimal i
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None })

            for transaction in transactions do
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            let listOptions = { Limit = 3; Offset = 2; Order = order; Status = AllTransactions }

            // Act
            let! result = database.ListAsync(user.Id, listOptions)

            // Assert
            %result.Should().HaveLength(3)

            if orderIsAscending then
                %result.Should().BeStrictlyAscendingBy(_.DateCreated)
            else
                %result.Should().BeStrictlyDescendingBy(_.DateCreated)
        }

    [<Theory>]
    [<CombinatorialData>]
    member _.``ListAsync should filter by status``(isPending: bool) =
        task {
            // Setup
            let! user = createUser ()

            // Create some pending transactions
            for i in 1..3 do
                let transaction =
                    { Name = $"Pending {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Pending
                      Type = TransactionType.Debit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create some cleared transactions
            for i in 1..2 do
                let transaction =
                    { Name = $"Cleared {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Credit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            let statusFilter = if isPending then PendingTransactions else ClearedTransactions
            let listOptions = { Limit = 10; Offset = 0; Order = Ascending; Status = statusFilter }

            // Act
            let! result = database.ListAsync(user.Id, listOptions)

            // Assert
            %result.Should().HaveLength(if isPending then 3 else 2)
        }

    [<Fact>]
    member _.``DeleteAsync should delete the transaction``() =
        task {
            // Setup
            let! user = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = TransactionType.Debit
                  ImportId = None }

            let! created = database.CreateAsync(user.Id, partialTransaction)

            // Act
            do! database.DeleteAsync(user.Id, created.Id)

            // Assert
            let! exists = database.ExistsAsync(user.Id, created.Id)
            %exists.Should().BeFalse()
        }

    [<Fact>]
    member _.``CountAsync should count all transactions when status is AllTransactions``() =
        task {
            // Setup
            let! user = createUser ()

            // Create pending transactions
            for i in 1..3 do
                let transaction =
                    { Name = $"Pending {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Pending
                      Type = TransactionType.Debit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create cleared transactions
            for i in 1..2 do
                let transaction =
                    { Name = $"Cleared {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Credit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Act
            let! count = database.CountAsync(user.Id, AllTransactions)

            // Assert
            %count.Should().Be(5)
        }

    [<Theory>]
    [<CombinatorialData>]
    member _.``CountAsync should count transactions by status``(isPending: bool) =
        task {
            // Setup
            let! user = createUser ()

            // Create pending transactions
            for i in 1..3 do
                let transaction =
                    { Name = $"Pending {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Pending
                      Type = TransactionType.Debit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create cleared transactions
            for i in 1..2 do
                let transaction =
                    { Name = $"Cleared {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Credit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            let statusFilter = if isPending then PendingTransactions else ClearedTransactions
            let expectedCount = if isPending then 3 else 2

            // Act
            let! count = database.CountAsync(user.Id, statusFilter)

            // Assert
            %count.Should().Be(expectedCount)
        }

    [<Fact>]
    member _.``SumAsync should calculate transaction sums correctly``() =
        task {
            // Setup
            let! user = createUser ()

            // Create cleared debits
            for i in 1..2 do
                let transaction =
                    { Name = $"Cleared Debit {i}"
                      Amount = 100M
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Debit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create cleared credits
            for i in 1..3 do
                let transaction =
                    { Name = $"Cleared Credit {i}"
                      Amount = 50M
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Credit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create pending debits
            for i in 1..1 do
                let transaction =
                    { Name = $"Pending Debit {i}"
                      Amount = 75M
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Pending
                      Type = TransactionType.Debit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create pending credits
            for i in 1..2 do
                let transaction =
                    { Name = $"Pending Credit {i}"
                      Amount = 25M
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Pending
                      Type = TransactionType.Credit
                      ImportId = None }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Act
            let! result = database.SumAsync(user.Id)

            // Assert
            // Cleared: 150M credit - 200M debit = -50M
            %result.ClearedCreditSum.Should().Be(150M)
            %result.ClearedDebitSum.Should().Be(200M)
            %result.ClearedSum.Should().Be(-50M)

            // Pending: 50M credit - 75M debit = -25M
            %result.PendingCreditSum.Should().Be(50M)
            %result.PendingDebitSum.Should().Be(75M)
            %result.PendingSum.Should().Be(-25M)

            // Total: 200M credit - 275M debit = -75M
            %result.Sum.Should().Be(-75M)
        }

    [<Fact>]
    member _.``SumAsync should return zero sums when user has no transactions``() =
        task {
            // Setup
            let! user = createUser ()

            // Act
            let! result = database.SumAsync(user.Id)

            // Assert
            %result.ClearedCreditSum.Should().Be(0M)
            %result.ClearedDebitSum.Should().Be(0M)
            %result.ClearedSum.Should().Be(0M)
            %result.PendingCreditSum.Should().Be(0M)
            %result.PendingDebitSum.Should().Be(0M)
            %result.PendingSum.Should().Be(0M)
            %result.Sum.Should().Be(0M)
        }

    [<Fact>]
    member _.``BulkCreateAsync should return 0 when given an empty list``() =
        task {
            // Setup
            let! user = createUser ()
            let emptyList: PartialTransaction list = []

            // Act
            let! result = database.BulkCreateAsync(user.Id, emptyList)

            // Assert
            %result.Should().Be(0)
        }

    [<Fact>]
    member _.``BulkCreateAsync should create multiple transactions``() =
        task {
            // Setup
            let! user = createUser ()
            let transactions = [1..10] |> List.map (fun i ->
                { Name = $"Bulk Transaction {i}"
                  Amount = decimal i * 10M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = if i % 2 = 0 then Cleared (bogus.Date.RecentOffset 5) else Pending
                  Type = if i % 3 = 0 then Credit else Debit
                  ImportId = None })

            // Act
            let! rowsCreated = database.BulkCreateAsync(user.Id, transactions)

            // Assert
            %rowsCreated.Should().Be(10)

            // Verify all transactions were created
            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            %allTransactions.Should().HaveLength(10)
        }

    [<Theory>]
    [<CombinatorialData>]
    member _.``BulkCreateAsync should handle all transaction types``(transactionType: TestTransactionType) =
        task {
            // Setup
            let! user = createUser ()
            let transactionType =
                match transactionType with
                | TestTransactionType.Check -> Check { CheckNumber = bogus.Random.Number(1000, 9999).ToString("D4") }
                | TestTransactionType.NonRecurringBill -> Bill { Recurring = false }
                | TestTransactionType.RecurringBill -> Bill { Recurring = true }
                | TestTransactionType.Credit -> Credit
                | TestTransactionType.Debit -> Debit
                | _ -> raise (SwitchExpressionException transactionType)

            let transactions = [1..5] |> List.map (fun i ->
                { Name = $"Bulk {transactionType} {i}"
                  Amount = decimal i * 5M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = transactionType
                  ImportId = None })

            // Act
            let! rowsCreated = database.BulkCreateAsync(user.Id, transactions)

            // Assert
            %rowsCreated.Should().Be(5)

            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            %allTransactions.Should().HaveLength(5)
            %allTransactions.Should().AllSatisfy(fun t -> t.Type.Should().Be(transactionType))
        }

    [<Theory>]
    [<CombinatorialData>]
    member _.``BulkCreateAsync should handle both Pending and Cleared statuses``(isCleared: bool) =
        task {
            // Setup
            let! user = createUser ()
            let clearedDate = bogus.Date.RecentOffset 10
            let status = if isCleared then Cleared clearedDate else Pending

            let transactions = [1..3] |> List.map (fun i ->
                { Name = $"Transaction {i}"
                  Amount = decimal i * 20M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = status
                  Type = Debit
                  ImportId = None })

            // Act
            let! rowsCreated = database.BulkCreateAsync(user.Id, transactions)

            // Assert
            %rowsCreated.Should().Be(3)

            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            %allTransactions.Should().HaveLength(3)

            for transaction in allTransactions do
                match (transaction.Status, isCleared) with
                | Pending, false -> () // Expected
                | Cleared actualDate, true ->
                    %actualDate.Should().BeCloseTo(clearedDate, TimeSpan.FromSeconds 1L)
                | _ -> failwith "Unexpected status combination"
        }

    [<Fact>]
    member _.``BulkCreateAsync should create transactions with mixed types and statuses``() =
        task {
            // Setup
            let! user = createUser ()
            let transactions = [
                { Name = "Check Transaction"
                  Amount = 100M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Check { CheckNumber = "1234" }
                  ImportId = None }
                { Name = "Recurring Bill"
                  Amount = 50M
                  DateCreated = bogus.Date.RecentOffset 19
                  Status = Cleared (bogus.Date.RecentOffset 5)
                  Type = Bill { Recurring = true }
                  ImportId = None }
                { Name = "Non-Recurring Bill"
                  Amount = 75M
                  DateCreated = bogus.Date.RecentOffset 18
                  Status = Pending
                  Type = Bill { Recurring = false }
                  ImportId = None }
                { Name = "Debit Transaction"
                  Amount = 25M
                  DateCreated = bogus.Date.RecentOffset 17
                  Status = Cleared (bogus.Date.RecentOffset 3)
                  Type = Debit
                  ImportId = None }
                { Name = "Credit Transaction"
                  Amount = 200M
                  DateCreated = bogus.Date.RecentOffset 16
                  Status = Pending
                  Type = Credit
                  ImportId = None }
            ]

            // Act
            let! rowsCreated = database.BulkCreateAsync(user.Id, transactions)

            // Assert
            %rowsCreated.Should().Be(5)

            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            %allTransactions.Should().HaveLength(5)

            // Verify we have all the different types
            let hasCheck = allTransactions |> Seq.exists (fun t -> match t.Type with Check _ -> true | _ -> false)
            let hasBill = allTransactions |> Seq.exists (fun t -> match t.Type with Bill _ -> true | _ -> false)
            let hasDebit = allTransactions |> Seq.exists (fun t -> match t.Type with Debit -> true | _ -> false)
            let hasCredit = allTransactions |> Seq.exists (fun t -> match t.Type with Credit -> true | _ -> false)

            %hasCheck.Should().BeTrue()
            %hasBill.Should().BeTrue()
            %hasDebit.Should().BeTrue()
            %hasCredit.Should().BeTrue()
        }

    [<Fact>]
    member _.``BulkCreateAsync should fail if the user does not exist``() =
        task {
            // Setup
            let transactions = [
                { Name = "Transaction 1"
                  Amount = 100M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Debit
                  ImportId = None }
            ]
            let userId = -1

            // Act
            let act () =
                database.BulkCreateAsync(userId, transactions)
                |> Async.AwaitTask
                |> Async.RunSynchronously

            // Assert
            %act.Should().ThrowInner<PostgresException, _>()
                 .Whose
                 .Message.Should().Contain("violates foreign key constraint")
        }

    [<Fact>]
    member _.``BulkCreateAsync should handle ImportId when provided``() =
        task {
            // Setup
            let! user = createUser ()
            let baseDate = DateTimeOffset.UtcNow.AddDays(-20.0)
            let transactions = [
                { Name = "Imported Transaction 1"
                  Amount = 100M
                  DateCreated = baseDate
                  Status = Pending
                  Type = Debit
                  ImportId = Some "import-123" }
                { Name = "Imported Transaction 2"
                  Amount = 200M
                  DateCreated = baseDate.AddDays(1.0)
                  Status = Cleared (bogus.Date.RecentOffset 5)
                  Type = Credit
                  ImportId = Some "import-456" }
            ]

            // Act
            let! rowsCreated = database.BulkCreateAsync(user.Id, transactions)

            // Assert
            %rowsCreated.Should().Be(2)

            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            %allTransactions.Should().HaveLength(2)

            let transactionsList = allTransactions |> Seq.toList
            %transactionsList[0].ImportId.Should().Be(Some "import-123")
            %transactionsList[1].ImportId.Should().Be(Some "import-456")
        }

    [<Fact>]
    member _.``BulkCreateAsync should handle missing ImportId``() =
        task {
            // Setup
            let! user = createUser ()
            let transactions = [
                { Name = "Transaction without ImportId"
                  Amount = 50M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Debit
                  ImportId = None }
            ]

            // Act
            let! rowsCreated = database.BulkCreateAsync(user.Id, transactions)

            // Assert
            %rowsCreated.Should().Be(1)

            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            %allTransactions.Should().HaveLength(1)

            let transaction = allTransactions |> Seq.head
            %transaction.ImportId.Should().Be(None)
        }

    [<Fact>]
    member _.``BulkCreateAsync should handle mixed ImportId scenarios``() =
        task {
            // Setup
            let! user = createUser ()
            let transactions = [
                { Name = "With ImportId"
                  Amount = 100M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Debit
                  ImportId = Some "import-789" }
                { Name = "Without ImportId"
                  Amount = 50M
                  DateCreated = bogus.Date.RecentOffset 19
                  Status = Pending
                  Type = Credit
                  ImportId = None }
            ]

            // Act
            let! rowsCreated = database.BulkCreateAsync(user.Id, transactions)

            // Assert
            %rowsCreated.Should().Be(2)

            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            let transactionsList = allTransactions |> Seq.toList

            %transactionsList.Should().HaveLength(2)
            %transactionsList.Should().SatisfyExactlyOneThat(_.ImportId.Should().Be(Some "import-789"))
            %transactionsList.Should().SatisfyExactlyOneThat(_.ImportId.Should().Be(None))
        }

    [<Fact>]
    member _.``CreateAsync should handle ImportId when provided``() =
        task {
            // Setup
            let! user = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = "Transaction with ImportId"
                  Amount = 150M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Debit
                  ImportId = Some "create-import-123" }

            // Act
            let! result = database.CreateAsync(user.Id, partialTransaction)

            // Assert
            %result.ImportId.Should().Be(Some "create-import-123")

            // Verify it was persisted
            let! retrieved = database.GetAsync(user.Id, result.Id)
            %retrieved.Should().BeSome()
            %retrieved.Value.ImportId.Should().Be(Some "create-import-123")
        }

    [<Fact>]
    member _.``CreateAsync should handle missing ImportId``() =
        task {
            // Setup
            let! user = createUser ()
            let partialTransaction: PartialTransaction =
                { Name = "Transaction without ImportId"
                  Amount = 75M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Credit
                  ImportId = None }

            // Act
            let! result = database.CreateAsync(user.Id, partialTransaction)

            // Assert
            %result.ImportId.Should().Be(None)

            // Verify it was persisted
            let! retrieved = database.GetAsync(user.Id, result.Id)
            %retrieved.Should().BeSome()
            %retrieved.Value.ImportId.Should().Be(None)
        }

    [<Fact>]
    member _.``UpdateAsync should preserve ImportId``() =
        task {
            // Setup
            let! user = createUser ()
            let originalTransaction: PartialTransaction =
                { Name = "Original Transaction"
                  Amount = 100M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Debit
                  ImportId = Some "update-import-123" }

            let! created = database.CreateAsync(user.Id, originalTransaction)

            let updatedTransaction: PartialTransaction =
                { Name = "Updated Transaction"
                  Amount = 200M
                  DateCreated = bogus.Date.RecentOffset 5
                  Status = Cleared (bogus.Date.RecentOffset 2)
                  Type = Credit
                  ImportId = Some "update-import-123" }

            // Act
            let! result = database.UpdateAsync(user.Id, created.Id, updatedTransaction)

            // Assert
            %result.ImportId.Should().Be(Some "update-import-123")
            %result.Name.Should().Be("Updated Transaction")
        }

    [<Fact>]
    member _.``BulkCreateAsync should skip transactions with duplicate ImportId``() =
        task {
            // Setup
            let! user = createUser ()

            // First, create some transactions with ImportIds
            let initialTransactions = [
                { Name = "Existing Transaction 1"
                  Amount = 100M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Debit
                  ImportId = Some "import-duplicate-1" }
                { Name = "Existing Transaction 2"
                  Amount = 200M
                  DateCreated = bogus.Date.RecentOffset 19
                  Status = Cleared (bogus.Date.RecentOffset 5)
                  Type = Credit
                  ImportId = Some "import-duplicate-2" }
            ]

            let! initialRowsCreated = database.BulkCreateAsync(user.Id, initialTransactions)
            %initialRowsCreated.Should().Be(2)

            // Now try to import again with some duplicates and some new transactions
            let duplicateTransactions = [
                { Name = "Duplicate Transaction 1"  // Should be skipped
                  Amount = 150M
                  DateCreated = bogus.Date.RecentOffset 18
                  Status = Pending
                  Type = Debit
                  ImportId = Some "import-duplicate-1" }
                { Name = "New Transaction"  // Should be imported
                  Amount = 300M
                  DateCreated = bogus.Date.RecentOffset 17
                  Status = Pending
                  Type = Credit
                  ImportId = Some "import-new-1" }
                { Name = "Duplicate Transaction 2"  // Should be skipped
                  Amount = 250M
                  DateCreated = bogus.Date.RecentOffset 16
                  Status = Pending
                  Type = Debit
                  ImportId = Some "import-duplicate-2" }
                { Name = "Another New Transaction"  // Should be imported
                  Amount = 400M
                  DateCreated = bogus.Date.RecentOffset 15
                  Status = Pending
                  Type = Credit
                  ImportId = Some "import-new-2" }
            ]

            // Act
            let! secondRowsCreated = database.BulkCreateAsync(user.Id, duplicateTransactions)

            // Assert
            %secondRowsCreated.Should().Be(2)  // Only 2 new transactions should be created

            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            %allTransactions.Should().HaveLength(4)  // 2 original + 2 new

            // Verify the original transactions were not duplicated
            let transactionsList = allTransactions |> Seq.toList
            let transaction1 = transactionsList |> List.find (fun t -> t.ImportId = Some "import-duplicate-1")
            let transaction2 = transactionsList |> List.find (fun t -> t.ImportId = Some "import-duplicate-2")

            %transaction1.Name.Should().Be("Existing Transaction 1")
            %transaction1.Amount.Should().Be(100M)
            %transaction2.Name.Should().Be("Existing Transaction 2")
            %transaction2.Amount.Should().Be(200M)
        }

    [<Fact>]
    member _.``BulkCreateAsync should not skip transactions without ImportId``() =
        task {
            // Setup
            let! user = createUser ()

            let transactions = [
                { Name = "Transaction 1"
                  Amount = 100M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Debit
                  ImportId = None }
                { Name = "Transaction 2"
                  Amount = 200M
                  DateCreated = bogus.Date.RecentOffset 19
                  Status = Pending
                  Type = Debit
                  ImportId = None }
            ]

            // Act - Import the same transactions twice
            let! firstImport = database.BulkCreateAsync(user.Id, transactions)
            let! secondImport = database.BulkCreateAsync(user.Id, transactions)

            // Assert - All transactions should be imported since they don't have ImportId
            %firstImport.Should().Be(2)
            %secondImport.Should().Be(2)

            let! allTransactions = database.ListAsync(user.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            %allTransactions.Should().HaveLength(4)  // Both imports should create transactions
        }

    [<Fact>]
    member _.``BulkCreateAsync should only check ImportId for the specific user``() =
        task {
            // Setup
            let! user1 = createUser ()
            let! user2 = createUser ()

            let transaction1 = [
                { Name = "User 1 Transaction"
                  Amount = 100M
                  DateCreated = bogus.Date.RecentOffset 20
                  Status = Pending
                  Type = Debit
                  ImportId = Some "shared-import-id" }
            ]

            let transaction2 = [
                { Name = "User 2 Transaction"
                  Amount = 200M
                  DateCreated = bogus.Date.RecentOffset 19
                  Status = Pending
                  Type = Credit
                  ImportId = Some "shared-import-id" }
            ]

            // Act - Both users import transactions with the same ImportId
            let! user1Rows = database.BulkCreateAsync(user1.Id, transaction1)
            let! user2Rows = database.BulkCreateAsync(user2.Id, transaction2)

            // Assert - Both should succeed because ImportId uniqueness is scoped to the user
            %user1Rows.Should().Be(1)
            %user2Rows.Should().Be(1)

            let! user1Transactions = database.ListAsync(user1.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })
            let! user2Transactions = database.ListAsync(user2.Id, { Limit = 100; Offset = 0; Order = Ascending; Status = AllTransactions })

            %user1Transactions.Should().HaveLength(1)
            %user2Transactions.Should().HaveLength(1)

            let user1Transaction = user1Transactions |> Seq.head
            let user2Transaction = user2Transactions |> Seq.head

            %user1Transaction.Name.Should().Be("User 1 Transaction")
            %user2Transaction.Name.Should().Be("User 2 Transaction")
        }
