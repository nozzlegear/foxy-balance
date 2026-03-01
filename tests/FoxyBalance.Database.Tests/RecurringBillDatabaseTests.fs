namespace FoxyBalance.Database.Tests

open System
open Faqt
open Faqt.Operators
open FoxyBalance.Database
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Database.Tests.Domain
open Npgsql
open Xunit

[<Collection("RecurringBillDatabase")>]
type RecurringBillDatabaseTests(fixture: DbContainerFixture) =
    let database: IRecurringBillDatabase = RecurringBillDatabase(TestDatabaseOptions fixture)
    let userDatabase: IUserDatabase = UserDatabase(TestDatabaseOptions fixture)
    let bogus = Bogus.Faker()

    let createUser () =
        userDatabase.CreateAsync { EmailAddress = bogus.Internet.Email(); HashedPassword = bogus.Internet.Password() }

    [<Fact>]
    member _.``CreateAsync should create a recurring bill``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Electric Bill"
                  Amount = 125.50M
                  WeekOfMonth = SecondWeek
                  DayOfWeek = DayOfWeek.Wednesday }

            // Act
            let! result = database.CreateAsync(user.Id, partialBill)

            // Assert
            %result.Id.Should().BeGreaterThan(0L)
            %result.Name.Should().Be(partialBill.Name)
            %result.Amount.Should().Be(partialBill.Amount)
            %result.WeekOfMonth.Should().Be(partialBill.WeekOfMonth)
            %result.DayOfWeek.Should().Be(partialBill.DayOfWeek)
            %result.Active.Should().BeTrue()
            %result.LastAppliedDate.Should().BeNone()
            %result.DateCreated.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds 5L)
        }

    [<Theory>]
    [<CombinatorialData>]
    member _.``CreateAsync should handle all week/day combinations``(weekNumber: int, dayNumber: int) =
        task {
            if weekNumber < 1 || weekNumber > 4 || dayNumber < 0 || dayNumber > 6 then
                () // Skip invalid combinations
            else
                // Setup
                let! user = createUser ()
                let week = WeekOfMonth.FromInt(weekNumber)
                let day = enum<DayOfWeek>(dayNumber)

                let partialBill: PartialRecurringBill =
                    { Name = $"Bill {weekNumber}-{dayNumber}"
                      Amount = decimal (weekNumber * 10 + dayNumber)
                      WeekOfMonth = week
                      DayOfWeek = day }

                // Act
                let! result = database.CreateAsync(user.Id, partialBill)

                // Assert
                %result.WeekOfMonth.Should().Be(week)
                %result.DayOfWeek.Should().Be(day)
        }

    [<Fact>]
    member _.``CreateAsync should fail if the user does not exist``() =
        task {
            // Setup
            let partialBill: PartialRecurringBill =
                { Name = "Invalid Bill"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }
            let userId = -1

            // Act
            let act () =
                database.CreateAsync(userId, partialBill)
                |> Async.AwaitTask
                |> Async.RunSynchronously

            // Assert
            %act.Should().ThrowInner<PostgresException, _>()
                 .Whose
                 .Message.Should().Contain("violates foreign key constraint")
        }

    [<Fact>]
    member _.``GetAsync should return the bill when it exists``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Water Bill"
                  Amount = 45.75M
                  WeekOfMonth = ThirdWeek
                  DayOfWeek = DayOfWeek.Friday }

            let! created = database.CreateAsync(user.Id, partialBill)

            // Act
            let! result = database.GetAsync(user.Id, created.Id)

            // Assert
            %result.Should().BeSome()
            let bill = result.Value
            %bill.Id.Should().Be(created.Id)
            %bill.Name.Should().Be(partialBill.Name)
            %bill.Amount.Should().Be(partialBill.Amount)
            %bill.WeekOfMonth.Should().Be(partialBill.WeekOfMonth)
            %bill.DayOfWeek.Should().Be(partialBill.DayOfWeek)
        }

    [<Fact>]
    member _.``GetAsync should return None when the bill does not exist``() =
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
    member _.``GetAsync should return None when the bill belongs to a different user``() =
        task {
            // Setup
            let! user1 = createUser ()
            let! user2 = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "User 1 Bill"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user1.Id, partialBill)

            // Act
            let! result = database.GetAsync(user2.Id, created.Id)

            // Assert
            %result.Should().BeNone()
        }

    [<Fact>]
    member _.``ListAsync should return all bills for a user``() =
        task {
            // Setup
            let! user = createUser ()
            let bills = [1..5] |> List.map (fun i ->
                { Name = $"Bill {i}"
                  Amount = decimal (i * 10)
                  WeekOfMonth = WeekOfMonth.FromInt((i % 4) + 1)
                  DayOfWeek = enum<DayOfWeek>(i % 7) })

            for bill in bills do
                let! _ = database.CreateAsync(user.Id, bill)
                ()

            // Act
            let! result = database.ListAsync(user.Id, false)

            // Assert
            %result.Should().HaveLength(5)
        }

    [<Fact>]
    member _.``ListAsync should filter active bills only``() =
        task {
            // Setup
            let! user = createUser ()

            // Create active bills
            for i in 1..3 do
                let bill = { Name = $"Active {i}"; Amount = decimal i; WeekOfMonth = FirstWeek; DayOfWeek = DayOfWeek.Monday }
                let! _ = database.CreateAsync(user.Id, bill)
                ()

            // Create and pause some bills
            for i in 1..2 do
                let bill = { Name = $"Paused {i}"; Amount = decimal i; WeekOfMonth = SecondWeek; DayOfWeek = DayOfWeek.Tuesday }
                let! created = database.CreateAsync(user.Id, bill)
                do! database.SetActiveAsync(user.Id, created.Id, false)

            // Act
            let! activeOnly = database.ListAsync(user.Id, true)
            let! allBills = database.ListAsync(user.Id, false)

            // Assert
            %activeOnly.Should().HaveLength(3)
            %allBills.Should().HaveLength(5)
        }

    [<Fact>]
    member _.``ListAsync should return bills sorted by name``() =
        task {
            // Setup
            let! user = createUser ()
            let billNames = ["Zebra Bill"; "Apple Bill"; "Maple Bill"]

            for name in billNames do
                let bill = { Name = name; Amount = 50M; WeekOfMonth = FirstWeek; DayOfWeek = DayOfWeek.Monday }
                let! _ = database.CreateAsync(user.Id, bill)
                ()

            // Act
            let! result = database.ListAsync(user.Id, false)

            // Assert
            let names = result |> Seq.map (_.Name) |> Seq.toList
            %names.Should().Be(["Apple Bill"; "Maple Bill"; "Zebra Bill"])
        }

    [<Fact>]
    member _.``ListAsync should not return bills from other users``() =
        task {
            // Setup
            let! user1 = createUser ()
            let! user2 = createUser ()

            // Create bills for user1
            for i in 1..3 do
                let bill = { Name = $"User1 Bill {i}"; Amount = decimal i; WeekOfMonth = FirstWeek; DayOfWeek = DayOfWeek.Monday }
                let! _ = database.CreateAsync(user1.Id, bill)
                ()

            // Create bills for user2
            for i in 1..2 do
                let bill = { Name = $"User2 Bill {i}"; Amount = decimal i; WeekOfMonth = SecondWeek; DayOfWeek = DayOfWeek.Tuesday }
                let! _ = database.CreateAsync(user2.Id, bill)
                ()

            // Act
            let! user1Bills = database.ListAsync(user1.Id, false)
            let! user2Bills = database.ListAsync(user2.Id, false)

            // Assert
            %user1Bills.Should().HaveLength(3)
            %user2Bills.Should().HaveLength(2)
        }

    [<Fact>]
    member _.``UpdateAsync should update the bill``() =
        task {
            // Setup
            let! user = createUser ()
            let originalBill: PartialRecurringBill =
                { Name = "Original Name"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, originalBill)

            let updatedBill: PartialRecurringBill =
                { Name = "Updated Name"
                  Amount = 200M
                  WeekOfMonth = FourthWeek
                  DayOfWeek = DayOfWeek.Saturday }

            // Act
            let! result = database.UpdateAsync(user.Id, created.Id, updatedBill)

            // Assert
            %result.Id.Should().Be(created.Id)
            %result.Name.Should().Be(updatedBill.Name)
            %result.Amount.Should().Be(updatedBill.Amount)
            %result.WeekOfMonth.Should().Be(updatedBill.WeekOfMonth)
            %result.DayOfWeek.Should().Be(updatedBill.DayOfWeek)
        }

    [<Fact>]
    member _.``UpdateAsync should preserve DateCreated, LastAppliedDate, and Active status``() =
        task {
            // Setup
            let! user = createUser ()
            let originalBill: PartialRecurringBill =
                { Name = "Original"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, originalBill)

            // Update LastAppliedDate and Active status
            let appliedDate = DateTimeOffset.UtcNow.AddDays(-5.0)
            do! database.UpdateLastAppliedDateAsync(user.Id, created.Id, appliedDate)
            do! database.SetActiveAsync(user.Id, created.Id, false)

            let updatedBill: PartialRecurringBill =
                { Name = "Updated"
                  Amount = 200M
                  WeekOfMonth = SecondWeek
                  DayOfWeek = DayOfWeek.Tuesday }

            // Act
            let! result = database.UpdateAsync(user.Id, created.Id, updatedBill)

            // Assert
            %result.DateCreated.Should().BeCloseTo(created.DateCreated, TimeSpan.FromSeconds 1L)
            %result.LastAppliedDate.Should().BeSome()
            %result.LastAppliedDate.Value.Should().BeCloseTo(appliedDate, TimeSpan.FromSeconds 1L)
            %result.Active.Should().BeFalse()
        }

    [<Fact>]
    member _.``UpdateLastAppliedDateAsync should update the last applied date``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Test Bill"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, partialBill)
            let appliedDate = DateTimeOffset.UtcNow.AddDays(-3.0)

            // Act
            do! database.UpdateLastAppliedDateAsync(user.Id, created.Id, appliedDate)

            // Assert
            let! updated = database.GetAsync(user.Id, created.Id)
            %updated.Should().BeSome()
            %updated.Value.LastAppliedDate.Should().BeSome()
            %updated.Value.LastAppliedDate.Value.Should().BeCloseTo(appliedDate, TimeSpan.FromSeconds 1L)
        }

    [<Fact>]
    member _.``SetActiveAsync should pause and resume bills``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Test Bill"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, partialBill)

            // Act - Pause the bill
            do! database.SetActiveAsync(user.Id, created.Id, false)
            let! paused = database.GetAsync(user.Id, created.Id)

            // Act - Resume the bill
            do! database.SetActiveAsync(user.Id, created.Id, true)
            let! resumed = database.GetAsync(user.Id, created.Id)

            // Assert
            %paused.Should().BeSome()
            %paused.Value.Active.Should().BeFalse()
            %resumed.Should().BeSome()
            %resumed.Value.Active.Should().BeTrue()
        }

    [<Fact>]
    member _.``DeleteAsync should delete the bill``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Test Bill"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, partialBill)

            // Act
            do! database.DeleteAsync(user.Id, created.Id)

            // Assert
            let! result = database.GetAsync(user.Id, created.Id)
            %result.Should().BeNone()
        }

    [<Fact>]
    member _.``DeleteAsync should not delete bills from other users``() =
        task {
            // Setup
            let! user1 = createUser ()
            let! user2 = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "User 1 Bill"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user1.Id, partialBill)

            // Act - Try to delete as user2
            do! database.DeleteAsync(user2.Id, created.Id)

            // Assert - Bill should still exist for user1
            let! result = database.GetAsync(user1.Id, created.Id)
            %result.Should().BeSome()
        }

    [<Fact>]
    member _.``GetBillsDueForApplicationAsync should return bills never applied``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Never Applied"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, partialBill)

            // Act
            let! results = database.GetBillsDueForApplicationAsync(DateTimeOffset.UtcNow)

            // Assert
            let resultsList = results |> Seq.toList
            %resultsList.Should().NotBeEmpty()

            let (userId, bill) = resultsList |> List.find (fun (_, b) -> b.Id = created.Id)
            %userId.Should().Be(user.Id)
            %bill.Name.Should().Be(partialBill.Name)
        }

    [<Fact>]
    member _.``GetBillsDueForApplicationAsync should return bills applied over a week ago``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Old Application"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, partialBill)

            // Set LastAppliedDate to 10 days ago
            let oldDate = DateTimeOffset.UtcNow.AddDays(-10.0)
            do! database.UpdateLastAppliedDateAsync(user.Id, created.Id, oldDate)

            // Act
            let! results = database.GetBillsDueForApplicationAsync(DateTimeOffset.UtcNow)

            // Assert
            let resultsList = results |> Seq.toList
            let matchingBill = resultsList |> List.tryFind (fun (_, b) -> b.Id = created.Id)
            %matchingBill.Should().BeSome()
        }

    [<Fact>]
    member _.``GetBillsDueForApplicationAsync should not return bills applied within the last week``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Recently Applied"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, partialBill)

            // Set LastAppliedDate to 3 days ago
            let recentDate = DateTimeOffset.UtcNow.AddDays(-3.0)
            do! database.UpdateLastAppliedDateAsync(user.Id, created.Id, recentDate)

            // Act
            let! results = database.GetBillsDueForApplicationAsync(DateTimeOffset.UtcNow)

            // Assert
            let resultsList = results |> Seq.toList
            let matchingBill = resultsList |> List.tryFind (fun (_, b) -> b.Id = created.Id)
            %matchingBill.Should().BeNone()
        }

    [<Fact>]
    member _.``GetBillsDueForApplicationAsync should not return inactive bills``() =
        task {
            // Setup
            let! user = createUser ()
            let partialBill: PartialRecurringBill =
                { Name = "Inactive Bill"
                  Amount = 100M
                  WeekOfMonth = FirstWeek
                  DayOfWeek = DayOfWeek.Monday }

            let! created = database.CreateAsync(user.Id, partialBill)
            do! database.SetActiveAsync(user.Id, created.Id, false)

            // Act
            let! results = database.GetBillsDueForApplicationAsync(DateTimeOffset.UtcNow)

            // Assert
            let resultsList = results |> Seq.toList
            let matchingBill = resultsList |> List.tryFind (fun (_, b) -> b.Id = created.Id)
            %matchingBill.Should().BeNone()
        }
