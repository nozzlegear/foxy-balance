namespace FoxyBalance.Database.Tests

open System.Diagnostics
open System.Runtime.InteropServices
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Faqt.Operators

type CommandResult = { ExitCode: int; StdOut: string; StdErr: string }

module AppleContainer =

    let runCommand (fileName: string) (args: string) =
        try
            let startInfo = ProcessStartInfo(
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            )
            use p = new Process(StartInfo = startInfo)
            %p.Start()
            let stdOut = p.StandardOutput.ReadToEnd()
            let stdErr = p.StandardError.ReadToEnd()
            p.WaitForExit()
            { ExitCode = p.ExitCode; StdOut = stdOut; StdErr = stdErr }
        with ex ->
            { ExitCode = -1; StdOut = ""; StdErr = ex.Message }

    let runCommandAsync (fileName: string) (args: string) =
        task {
            try
                let startInfo = ProcessStartInfo(
                    FileName = fileName,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                )
                use p = new Process(StartInfo = startInfo)
                %p.Start()
                let! stdOutTask = p.StandardOutput.ReadToEndAsync()
                and! stdErrTask = p.StandardError.ReadToEndAsync()
                do! p.WaitForExitAsync()
                return { ExitCode = p.ExitCode; StdOut = stdOutTask; StdErr = stdErrTask }
            with ex ->
                return { ExitCode = -1; StdOut = ""; StdErr = ex.Message }
        }

    let spawnWatchdog (runtimePath: string) (containerName: string) =
        try
            let parentPid = System.Environment.ProcessId
            let runtime = if runtimePath.Contains(" ") then $"\"{runtimePath}\"" else runtimePath
            let script =
                $"while kill -0 {parentPid} 2>/dev/null; do sleep 2; done; {runtime} rm --force {containerName}"

            let startInfo = ProcessStartInfo(
                FileName = "sh",
                Arguments = "-c \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            )
            // Fire-and-forget: start but don't await or dispose — the shell process
            // runs independently and cleans up the container when this process exits.
            let p = new Process(StartInfo = startInfo)
            %p.Start()
        with _ ->
            ()

    let getContainerPath () =
        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            if runCommand "container" "--version" |> _.ExitCode = 0 then
                Some "container"
            elif System.IO.File.Exists("/usr/local/bin/container") then
                Some "/usr/local/bin/container"
            else
                None
        else
            None

    /// Extracts the IPv4 address from a CIDR notation string (e.g. "192.168.1.1/24" -> "192.168.1.1")
    let private parseCidrAddress (addr: string) =
        if addr.Contains("/") then
            Some (addr.Split('/').[0])
        else
            Some addr

    /// Attempts to get the container's IPv4 address from published ports (returns localhost if ports are published)
    let private tryGetIpFromPublishedPorts (config: JsonElement) =
        match config.TryGetProperty("publishedPorts") with
        | true, ports when ports.ValueKind = JsonValueKind.Array && ports.GetArrayLength() > 0 ->
            Some "127.0.0.1"
        | _ -> None

    let tryGetContainerIp (inspectJson: string) =
        try
            use doc = JsonDocument.Parse(inspectJson)
            let root = doc.RootElement
            
            if root.ValueKind <> JsonValueKind.Array || root.GetArrayLength() = 0 then
                None
            else
                let element = root.[0]
                
                // Try published ports first (returns localhost for Apple containers with published ports)
                match element.TryGetProperty("configuration") with
                | true, config -> tryGetIpFromPublishedPorts config
                | false, _ -> None
        with _ ->
            None

    let waitForConnection (connectionString: string) (recreateDbFn: string -> string -> Task<unit>) (dbName: string) (cancellationToken: CancellationToken) =
        task {
            let mutable connected = false
            let mutable attempts = 0
            let maxAttempts = 300 // 30 seconds (100ms sleep)
            while not connected && attempts < maxAttempts do
                cancellationToken.ThrowIfCancellationRequested()
                try
                    do! recreateDbFn connectionString dbName
                    connected <- true
                with _ ->
                    attempts <- attempts + 1
                    do! Task.Delay(100, cancellationToken)
            if not connected then
                failwith "Timed out waiting for database container to accept connections."
        }

    /// Checks whether a container with the given name exists in any state.
    let containerExists (runtimePath: string) (containerName: string) =
        let result = runCommand runtimePath $"list --all"
        result.ExitCode = 0 && result.StdOut.Contains(containerName)

    /// Checks whether a container with the given name is currently running.
    let containerRunning (runtimePath: string) (containerName: string) =
        let result = runCommand runtimePath $"list"
        result.ExitCode = 0 && result.StdOut.Contains(containerName)

    /// Removes a container forcefully.
    let removeContainer (runtimePath: string) (containerName: string) =
        // Use 'rm' to force remove the container
        let result = runCommand runtimePath $"rm --force {containerName}"
        if result.ExitCode <> 0 && not (result.StdErr.Contains("does not exist") || result.StdErr.Contains("not found")) then
            failwithf $"Failed to remove container {containerName}: {result.StdErr}"

    /// Inspects a named container and returns the command result.
    let inspectContainer (runtimePath: string) (containerName: string) =
        runCommand runtimePath $"inspect {containerName}"

    /// Waits for the container to be in running state
    let waitForContainerRunning (runtimePath: string) (containerName: string) (cancellationToken: CancellationToken) =
        task {
            let mutable running = false
            let mutable attempts = 0
            let maxAttempts = 60 // 60 seconds (1 second sleep)
            while not running && attempts < maxAttempts do
                cancellationToken.ThrowIfCancellationRequested()
                if containerRunning runtimePath containerName then
                    running <- true
                else
                    attempts <- attempts + 1
                    do! Task.Delay(1000, cancellationToken)
            if not running then
                failwithf "Timed out waiting for container %s to start." containerName
            
            // Additional delay to allow PostgreSQL to initialize
            do! Task.Delay(5000, cancellationToken)
        }
