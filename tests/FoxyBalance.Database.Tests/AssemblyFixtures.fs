namespace FoxyBalance.Database.Tests

open System.Threading.Tasks
open Xunit

/// Owns the database containers shared by this test run: they start lazily on first use and are
/// removed once every test in the assembly has finished.
type DatabaseContainersFixture() =
    interface IAsyncLifetime with
        member _.InitializeAsync() = ValueTask()
        member _.DisposeAsync() = ValueTask(TestDatabaseContainer.removeShared ())

[<assembly: AssemblyFixture(typeof<DatabaseContainersFixture>)>]
do ()
