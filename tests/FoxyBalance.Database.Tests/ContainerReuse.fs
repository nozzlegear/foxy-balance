namespace FoxyBalance.Database.Tests

open System
open System.Threading
open System.Threading.Tasks
open DotNet.Testcontainers.Containers

module ContainerReuse =

    type ContainerStrategy =
        | AppleContainerStrategy
        | TestcontainersStrategy

    type ContainerReuseResult = {
        Strategy: ContainerStrategy
        ContainerName: string option
        ConnectionString: string
        Reused: bool
    }

    /// Gets the appropriate container strategy based on available runtime
    let getContainerStrategy () : ContainerStrategy =
        match AppleContainer.getContainerPath() with
        | Some _ -> AppleContainerStrategy
        | None -> TestcontainersStrategy

    /// Gets a stable container name for reuse
    let getStableContainerName (dbPrefix: string) : string =
        // Stable name across test runs — same as Testcontainers reuse pattern
        // Format: foxy-balance-{db}-{machine}-{user}
        let containerName = $"foxy-balance-{dbPrefix}-{Environment.MachineName}-{Environment.UserName}"
        containerName

    /// Checks if a container exists (any state)
    let containerExists (strategy: ContainerStrategy) (containerName: string) : bool =
        match strategy with
        | AppleContainerStrategy ->
            match AppleContainer.getContainerPath() with
            | Some path -> AppleContainer.containerExists path containerName
            | None -> false
        | TestcontainersStrategy ->
            // Testcontainers manages its own container lifecycle, so we consider it always "exists" for our purposes
            false

    /// Checks if a container is running
    let containerRunning (strategy: ContainerStrategy) (containerName: string) : bool =
        match strategy with
        | AppleContainerStrategy ->
            match AppleContainer.getContainerPath() with
            | Some path -> AppleContainer.containerRunning path containerName
            | None -> false
        | TestcontainersStrategy ->
            // Testcontainers manages its own lifecycle
            false

    /// Removes a container forcefully
    let removeContainer (strategy: ContainerStrategy) (containerName: string) : unit =
        match strategy with
        | AppleContainerStrategy ->
            match AppleContainer.getContainerPath() with
            | Some path -> AppleContainer.removeContainer path containerName
            | None -> ()
        | TestcontainersStrategy ->
            () // Testcontainers manages its own lifecycle

    /// Gets the port publishing arguments for a database type
    let getPortPublishArgs (dbPrefix: string) : string =
        match dbPrefix with
        | "postgres" -> "-p 5433:5432"
        | "mssql" -> "-p 1433:1433"
        | _ -> ""

    /// Starts a database container using Apple native containers when available,
    /// falling back to Testcontainers. Reuses existing containers.
    /// Returns the connection string and container name (if Apple container).
    let startContainerAndReuse
        (dbPrefix: string)
        (image: string) (digest: string) (envArgs: string)
        (buildConnStr: string -> string)
        (recreateDbFn: string -> string -> Task<unit>)
        (runMigrations: bool) (migrateFn: string -> unit)
        (testcontainersCallback: obj -> unit)
        (cancellationToken: CancellationToken) : Task<ContainerReuseResult> =

        task {
            let strategy = getContainerStrategy()
            let containerName = getStableContainerName dbPrefix
            let portArgs = getPortPublishArgs dbPrefix
            let runId =
                Environment.GetEnvironmentVariable("CI_RUN_ID")
                |> function
                   | null | "" -> Guid.NewGuid().ToString("N")[..7]
                   | v -> v

            let dbName = $"{dbPrefix}_{runId}_{DateTimeOffset.UtcNow.Ticks}"

            match strategy with
            | AppleContainerStrategy ->
                // Handle Apple container reuse
                let imageWithDigest = $"{image}@{digest}"

                // Check container state
                let containerExists = containerExists strategy containerName
                let containerRunning = containerRunning strategy containerName

                match AppleContainer.getContainerPath() with
                | Some path ->
                    if containerRunning then
                        // Container is already running - reuse it
                        let inspectRes = AppleContainer.inspectContainer path containerName
                        if inspectRes.ExitCode <> 0 then
                            failwithf $"Failed to inspect Apple native %s{dbPrefix} container: %s{inspectRes.StdErr}"

                        let ip = AppleContainer.tryGetContainerIp inspectRes.StdOut
                                 |> Option.defaultWith (fun () -> "127.0.0.1")

                        let connStr = buildConnStr ip
                        do! AppleContainer.waitForConnection connStr recreateDbFn dbName cancellationToken
                        let fullConnStr = connStr + $";Database={dbName}"

                        if runMigrations then
                            migrateFn fullConnStr

                        return {
                            Strategy = AppleContainerStrategy
                            ContainerName = Some containerName
                            ConnectionString = fullConnStr
                            Reused = true
                        }
                    elif containerExists then
                        // Exists but stopped – remove and recreate
                        removeContainer strategy containerName

                        // Start new container
                        let args = $"run -d --name {containerName} {portArgs} {envArgs} {imageWithDigest}"
                        let runRes = AppleContainer.runCommand path args
                        if runRes.ExitCode <> 0 then
                            failwithf $"Failed to start Apple native %s{dbPrefix} container: %s{runRes.StdErr}"

                        // Spawn watchdog to clean up when parent process exits
                        AppleContainer.spawnWatchdog path containerName

                        // Wait for container to be running
                        do! AppleContainer.waitForContainerRunning path containerName cancellationToken

                        // Get IP and wait for connection
                        let inspectRes = AppleContainer.inspectContainer path containerName
                        if inspectRes.ExitCode <> 0 then
                            failwithf $"Failed to inspect Apple native %s{dbPrefix} container: %s{inspectRes.StdErr}"

                        let ip = AppleContainer.tryGetContainerIp inspectRes.StdOut
                                 |> Option.defaultWith (fun () -> "127.0.0.1")
                        let connStr = buildConnStr ip
                        do! AppleContainer.waitForConnection connStr recreateDbFn dbName cancellationToken
                        let fullConnStr = connStr + $";Database={dbName}"

                        if runMigrations then
                            migrateFn fullConnStr

                        return {
                            Strategy = AppleContainerStrategy
                            ContainerName = Some containerName
                            ConnectionString = fullConnStr
                            Reused = false
                        }
                    else
                        // Container doesn't exist - create and start
                        let args = $"run -d --name {containerName} {portArgs} {envArgs} {imageWithDigest}"
                        let runRes = AppleContainer.runCommand path args
                        if runRes.ExitCode <> 0 then
                            failwithf $"Failed to start Apple native %s{dbPrefix} container: %s{runRes.StdErr}"

                        // Spawn watchdog to clean up when parent process exits
                        AppleContainer.spawnWatchdog path containerName

                        // Wait for container to be running
                        do! AppleContainer.waitForContainerRunning path containerName cancellationToken

                        // Get IP and wait for connection
                        let inspectRes = AppleContainer.inspectContainer path containerName
                        if inspectRes.ExitCode <> 0 then
                            failwithf $"Failed to inspect Apple native %s{dbPrefix} container: %s{inspectRes.StdErr}"

                        let ip = AppleContainer.tryGetContainerIp inspectRes.StdOut
                                 |> Option.defaultWith (fun () -> "127.0.0.1")
                        let connStr = buildConnStr ip
                        do! AppleContainer.waitForConnection connStr recreateDbFn dbName cancellationToken
                        let fullConnStr = connStr + $";Database={dbName}"

                        if runMigrations then
                            migrateFn fullConnStr

                        return {
                            Strategy = AppleContainerStrategy
                            ContainerName = Some containerName
                            ConnectionString = fullConnStr
                            Reused = false
                        }
                | None ->
                    return failwith "Apple container runtime not available"

            | TestcontainersStrategy ->
                // Use Testcontainers (Foxy Balance only uses PostgreSQL)
                let container : IDatabaseContainer =
                    upcast Testcontainers.PostgreSql.PostgreSqlBuilder()
                        .WithImage($"{image}@{digest}")
                        .Build()

                // Start the container
                match container with
                | :? Testcontainers.PostgreSql.PostgreSqlContainer as c ->
                    do! c.StartAsync(cancellationToken)
                | _ ->
                    failwithf $"Unknown container type {container}"

                let connStr = container.GetConnectionString()
                do! recreateDbFn connStr dbName
                let fullConnStr = connStr + $";Database={dbName}"

                testcontainersCallback null // No container to dispose - Testcontainers manages lifecycle

                if runMigrations then
                    migrateFn fullConnStr

                return {
                    Strategy = TestcontainersStrategy
                    ContainerName = None
                    ConnectionString = fullConnStr
                    Reused = false // Testcontainers manages reuse internally
                }
        }

    /// Disposes an Apple native container started by startContainerAndReuse.
    /// The container is NOT removed — it's reused across test runs.
    /// Use this only when the test runner process dies unexpectedly (handled by watchdog).
    let disposeAppleContainer (containerName: string) : Task<unit> =
        task {
            match AppleContainer.getContainerPath() with
            | Some path ->
                let! _ = AppleContainer.runCommandAsync path $"rm -f {containerName}"
                return ()
            | None ->
                return ()
        }
