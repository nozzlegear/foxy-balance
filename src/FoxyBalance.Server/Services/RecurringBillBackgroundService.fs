namespace FoxyBalance.Server.Services

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection

type RecurringBillBackgroundService(
    logger: ILogger<RecurringBillBackgroundService>,
    serviceProvider: IServiceProvider) =
    inherit BackgroundService()

    override this.ExecuteAsync(stoppingToken: CancellationToken) =
        task {
            logger.LogInformation("Recurring Bill Background Service is starting")

            // Don't start immediately - wait 1 minute to avoid startup slowdown
            do! Task.Delay(TimeSpan.FromMinutes(1.0), stoppingToken)

            while not stoppingToken.IsCancellationRequested do
                try
                    // Create a scope for scoped services
                    use scope = serviceProvider.CreateScope()
                    let billService = scope.ServiceProvider.GetRequiredService<RecurringBillApplicationService>()

                    do! billService.ApplyBillsForAllUsers()

                    // Wait 1 hour before next check
                    do! Task.Delay(TimeSpan.FromHours(1.0), stoppingToken)
                with
                | :? OperationCanceledException ->
                    logger.LogInformation("Recurring Bill Background Service is stopping")
                | ex ->
                    logger.LogError(ex, "Error in recurring bill background service")
                    // Wait a bit before retrying to avoid tight error loops
                    do! Task.Delay(TimeSpan.FromMinutes(5.0), stoppingToken)
        } :> Task
