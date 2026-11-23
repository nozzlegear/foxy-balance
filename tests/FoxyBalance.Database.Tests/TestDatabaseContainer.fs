namespace FoxyBalance.Database.Tests

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open DotNet.Testcontainers.Containers
open Testcontainers.PostgreSql

/// How a test database container is run: Apple's native `container` CLI on macOS, or Testcontainers
/// where that runtime isn't available (e.g. Linux CI).
type ContainerRuntime =
    | AppleNativeRuntime
    | TestcontainersRuntime

/// A database container created for one test run. Fixtures get a fresh database inside it and the
/// assembly fixture removes the container once the run finishes.
type TestDatabaseContainer =
    { Runtime: ContainerRuntime
      /// Name of the Apple native container. Testcontainers names its own containers.
      ContainerName: string option
      /// Connection string without a database name; `createDatabase` appends one.
      ConnectionString: string
      /// Handle for containers managed by Testcontainers, disposed by `remove`.
      TestcontainersContainer: IContainer option }

module TestDatabaseContainer =
    /// Label marking containers owned by the database tests, so a cleanup script can reap leaked
    /// ones: `container list --all --format json` (Apple) or `docker ps --filter label=foxybalance.test-db`.
    [<Literal>]
    let LabelName = "foxybalance.test-db"

    /// The port postgres listens on inside its container.
    let containerPort = 5432

    /// Containers shared by every fixture in this run, keyed by database kind.
    let private sharedContainers = ConcurrentDictionary<string, Lazy<Task<TestDatabaseContainer>>>()

    /// Asks the OS for a free host port. A fixed host port collides with any other database on it,
    /// e.g. a local dev database already publishing 5432/5433.
    let private findFreeHostPort () =
        let listener = new TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        try
            (listener.LocalEndpoint :?> IPEndPoint).Port
        finally
            listener.Stop()

    let private randomSuffix () =
        Guid.NewGuid().ToString("N").Substring(0, 8)

    let private currentRuntime () =
        if (AppleContainer.getContainerPath ()).IsSome then AppleNativeRuntime else TestcontainersRuntime

    /// Waits until the published host port accepts connections.
    let private waitForPort (port: int) (cancellationToken: CancellationToken) =
        task {
            let mutable connected = false
            let mutable attempts = 0
            let maxAttempts = 300 // 30 seconds (100ms sleep)
            while not connected && attempts < maxAttempts do
                cancellationToken.ThrowIfCancellationRequested()
                try
                    use client = new TcpClient()
                    do! client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).AsTask()
                    connected <- true
                with _ ->
                    attempts <- attempts + 1
                    do! Task.Delay(100, cancellationToken)
            if not connected then
                failwithf $"Timed out waiting for the database container on port %i{port} to accept connections."
        }

    /// Starts a new database container publishing a free host port. `buildConnStr` receives the
    /// published host port and returns a connection string without a database name.
    let start
        (dbPrefix: string)
        (image: string)
        (digest: string)
        (envArgs: string)
        (buildConnStr: int -> string)
        (cancellationToken: CancellationToken)
        : Task<TestDatabaseContainer> =
        task {
            let imageRef = $"{image}@{digest}"

            match currentRuntime () with
            | AppleNativeRuntime ->
                match AppleContainer.getContainerPath () with
                | None -> return failwith "Apple container runtime not available"
                | Some path ->
                    let containerName = $"foxybalance-test-{dbPrefix}-{randomSuffix()}"
                    let hostPort = findFreeHostPort ()
                    let args =
                        $"run -d --name {containerName} -p {hostPort}:{containerPort} "
                        + $"-l {LabelName}=true {envArgs} {imageRef}"

                    let runResult = AppleContainer.runCommand path args
                    if runResult.ExitCode <> 0 then
                        failwithf $"Failed to start ephemeral %s{dbPrefix} container: %s{runResult.StdErr}"

                    // Safety net: removes the container if this process dies before the run finishes.
                    AppleContainer.spawnWatchdog path containerName
                    do! AppleContainer.waitForContainerRunning path containerName cancellationToken
                    do! waitForPort hostPort cancellationToken

                    return {
                        Runtime = AppleNativeRuntime
                        ContainerName = Some containerName
                        ConnectionString = buildConnStr hostPort
                        TestcontainersContainer = None
                    }
            | TestcontainersRuntime ->
                let container: IDatabaseContainer =
                    upcast PostgreSqlBuilder(imageRef)
                        .WithName($"foxybalance-test-{dbPrefix}-{randomSuffix()}")
                        .WithLabel(LabelName, "true")
                        .WithPortBinding(containerPort, true)
                        .Build()

                do! container.StartAsync(cancellationToken)

                return {
                    Runtime = TestcontainersRuntime
                    ContainerName = None
                    ConnectionString = container.GetConnectionString()
                    TestcontainersContainer = Some(container :> IContainer)
                }
        }

    /// The container shared by every fixture in this run, started on first use.
    let getShared
        (dbPrefix: string)
        (image: string)
        (digest: string)
        (envArgs: string)
        (buildConnStr: int -> string)
        (cancellationToken: CancellationToken)
        : Task<TestDatabaseContainer> =
        let entry =
            sharedContainers.GetOrAdd(
                dbPrefix,
                fun _ -> lazy (start dbPrefix image digest envArgs buildConnStr cancellationToken)
            )

        entry.Value

    /// Creates a fresh database inside the container, retrying until the server accepts connections,
    /// and returns the connection string that selects it.
    let createDatabase
        (container: TestDatabaseContainer)
        (dbPrefix: string)
        (recreateDbFn: string -> string -> Task<unit>)
        (runMigrations: bool)
        (migrateFn: string -> unit)
        (cancellationToken: CancellationToken)
        : Task<string> =
        task {
            let dbName = $"{dbPrefix}_{randomSuffix()}_{DateTimeOffset.UtcNow.Ticks}"
            let mutable created = false
            let mutable attempts = 0

            while not created && attempts < 1200 do // 120 seconds (100ms sleep)
                cancellationToken.ThrowIfCancellationRequested()
                attempts <- attempts + 1
                try
                    do! recreateDbFn container.ConnectionString dbName
                    created <- true
                with _ ->
                    do! Task.Delay(100, cancellationToken)

            if not created then
                failwithf $"Timed out creating test database %s{dbName}."

            let connectionString = container.ConnectionString + $";Database={dbName}"

            if runMigrations then
                migrateFn connectionString

            return connectionString
        }

    /// Stops and removes one container.
    let remove (container: TestDatabaseContainer) : Task<unit> =
        task {
            match container.Runtime with
            | AppleNativeRuntime ->
                match container.ContainerName, AppleContainer.getContainerPath () with
                | Some containerName, Some path ->
                    AppleContainer.removeContainer path containerName
                | _ -> ()
            | TestcontainersRuntime ->
                match container.TestcontainersContainer with
                | Some managed -> do! managed.DisposeAsync().AsTask()
                | None -> ()
        }

    /// Removes every container shared by this run. Called when the assembly's tests finish.
    let removeShared () : Task<unit> =
        task {
            for entry in sharedContainers.Values do
                if entry.IsValueCreated then
                    let! container = entry.Value
                    do! remove container

            sharedContainers.Clear()
        }
