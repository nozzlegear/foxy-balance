namespace FoxyBalance.Database.Tests.Domain

open FoxyBalance.Database.Models
open FoxyBalance.Database.Tests

type TestDatabaseOptions(fixture: DbContainerFixture) =
    interface IDatabaseOptions with
        member this.ConnectionString = fixture.ConnectionString
