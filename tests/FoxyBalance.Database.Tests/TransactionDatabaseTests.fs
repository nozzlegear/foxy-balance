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
                  Type = transactionType  }

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
                  Type = TransactionType.Debit }
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
                  Type = TransactionType.Debit }

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
                  Type = TransactionType.Debit }

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
                  Type = TransactionType.Debit }

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
                  Type = TransactionType.Debit }

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
                  Type = TransactionType.Debit }

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
                  Type = TransactionType.Debit }

            let! created = database.CreateAsync(user.Id, partialTransaction)

            let updatedTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 5
                  Status = Cleared (bogus.Date.RecentOffset 2)
                  Type = TransactionType.Credit }

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
                  Type = TransactionType.Debit }

            let! created = database.CreateAsync(user.Id, partialTransaction)

            let updatedTransaction: PartialTransaction =
                { Name = bogus.Commerce.Product()
                  Amount = bogus.Finance.Amount()
                  DateCreated = bogus.Date.RecentOffset 5
                  Status = Cleared (bogus.Date.RecentOffset 2)
                  Type = TransactionType.Credit }

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
                  Type = TransactionType.Debit })

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
                  Type = TransactionType.Debit })

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
                      Type = TransactionType.Debit }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create some cleared transactions
            for i in 1..2 do
                let transaction =
                    { Name = $"Cleared {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Credit }
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
                  Type = TransactionType.Debit }

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
                      Type = TransactionType.Debit }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create cleared transactions
            for i in 1..2 do
                let transaction =
                    { Name = $"Cleared {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Credit }
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
                      Type = TransactionType.Debit }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create cleared transactions
            for i in 1..2 do
                let transaction =
                    { Name = $"Cleared {i}"
                      Amount = decimal i
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Credit }
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
                      Type = TransactionType.Debit }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create cleared credits
            for i in 1..3 do
                let transaction =
                    { Name = $"Cleared Credit {i}"
                      Amount = 50M
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Cleared (bogus.Date.RecentOffset 10)
                      Type = TransactionType.Credit }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create pending debits
            for i in 1..1 do
                let transaction =
                    { Name = $"Pending Debit {i}"
                      Amount = 75M
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Pending
                      Type = TransactionType.Debit }
                let! _ = database.CreateAsync(user.Id, transaction)
                ()

            // Create pending credits
            for i in 1..2 do
                let transaction =
                    { Name = $"Pending Credit {i}"
                      Amount = 25M
                      DateCreated = bogus.Date.RecentOffset 20
                      Status = Pending
                      Type = TransactionType.Credit }
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
