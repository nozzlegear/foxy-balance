namespace FoxyBalance.CLI

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks

/// Shared HTTP handler that prefers IPv4 to avoid hangs on hosts with broken IPv6 routing.
module HttpHandler =

    /// Create a SocketsHttpHandler that resolves IPv4 addresses first.
    let create () =
        let handler = new SocketsHttpHandler()
        handler.PooledConnectionLifetime <- TimeSpan.FromMinutes(2.0)
        handler.ConnectCallback <-
            Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<IO.Stream>>(fun ctx ct ->
                let ep = ctx.DnsEndPoint
                let host = ep.Host
                let port = ep.Port
                // Resolve DNS and prefer IPv4; fall back to IPv6 if no IPv4 address is available.
                let addresses = Dns.GetHostAddresses(host)
                let ipv4 = addresses |> Array.filter (fun a -> a.AddressFamily = System.Net.Sockets.AddressFamily.InterNetwork)
                let target = if ipv4.Length > 0 then ipv4.[0] else addresses.[0]
                let tcpClient = new System.Net.Sockets.TcpClient()
                tcpClient.ConnectAsync(target, port).GetAwaiter().GetResult()
                let sslStream = new System.Net.Security.SslStream(tcpClient.GetStream())
                sslStream.AuthenticateAsClientAsync(host).GetAwaiter().GetResult()
                ValueTask<IO.Stream>(sslStream :> IO.Stream)
            )
        handler
