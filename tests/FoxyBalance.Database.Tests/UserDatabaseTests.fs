namespace FoxyBalance.Database.Tests

open System
open Bogus
open FoxyBalance.Database
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Database.Tests.Domain
open Xunit
open Faqt
open Faqt.Operators

[<Collection("UserDatabase")>]
type UserDatabaseTests(fixture: DbContainerFixture) =
    let userDb: IUserDatabase = UserDatabase(TestDatabaseOptions fixture)
    let bogus = Faker()

    [<Fact>]
    member _.``CreateAsync should create a new user``() = task {
        // Setup
        let partialUser: PartialUser =
            { EmailAddress = bogus.Internet.Email()
              HashedPassword = "some-hashed-password" }

        // Act
        let! result = userDb.CreateAsync partialUser

        // Assert
        %result.EmailAddress.Should().Be(partialUser.EmailAddress)
        %result.HashedPassword.Should().Be(partialUser.HashedPassword)
        %result.DateCreated.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds 1L)
        %result.Id.Should().BeGreaterThanOrEqualTo(1)
    }

    [<Theory>]
    [<CombinatorialData>]
    member _.``GetAsync should return Some User when the user exists``(getByEmail: bool) = task {
        // Setup
        let partialUser: PartialUser =
            { EmailAddress = bogus.Internet.Email()
              HashedPassword = "some-hashed-password-1" }

        let! createdUser = userDb.CreateAsync partialUser

        let userIdentifier =
            match getByEmail with
            | true -> UserIdentifier.Email createdUser.EmailAddress
            | false -> UserIdentifier.Id createdUser.Id

        // Act
        let! result = userDb.GetAsync userIdentifier

        // Assert
        %result.Should().BeSome()
        %result.Value.Should().Be(createdUser)
        %result.Value.EmailAddress.Should().Be(createdUser.EmailAddress)
        %result.Value.HashedPassword.Should().Be(createdUser.HashedPassword)
        %result.Value.DateCreated.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds 1L)
        %result.Value.Id.Should().BeGreaterThanOrEqualTo(1)
    }

    [<Theory>]
    [<CombinatorialData>]
    member _.``GetAsync should return None when the user doesn't exist``(getByEmail: bool) = task {
        // Setup
        let userIdentifier =
            match getByEmail with
            | true -> UserIdentifier.Email "foo"
            | false -> UserIdentifier.Id -1

        // Act
        let! result = userDb.GetAsync userIdentifier

        // Assert
        %result.Should().BeNone()
    }

    [<Theory>]
    [<CombinatorialData>]
    member _.``ExistsAsync should return true or false if the user exists or not``(
        userExists: bool,
        getByEmail: bool
    ) = task {
        // Setup
        let partialUser: PartialUser =
            { EmailAddress = bogus.Internet.Email()
              HashedPassword = "some-hashed-password-1" }
        let! createdUser = userDb.CreateAsync partialUser

        let userIdentifier =
            match userExists, getByEmail with
            | true, true -> UserIdentifier.Email createdUser.EmailAddress
            | true, false -> UserIdentifier.Id createdUser.Id
            | false, true -> UserIdentifier.Email (bogus.Internet.Email())
            | false, false -> UserIdentifier.Id -1

        // Act
        let! result = userDb.ExistsAsync userIdentifier

        // Assert
        %result.Should().Be(userExists, if userExists then "User should exist" else "User should not exist")
    }
