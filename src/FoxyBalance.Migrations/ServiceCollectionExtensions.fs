namespace FoxyBalance.Migrations

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection

[<AutoOpen>]
module ServiceCollectionExtensions =
    type IServiceCollection with
        member services.AddDatabaseMigrationService(connectionStrings: IConfigurationSection) =
            services.AddOptions<ConnectionStrings>()
                .Bind(connectionStrings)
                .ValidateDataAnnotations()
                .ValidateOnStart() |> ignore
            services.AddHostedService<HostedMigrationService>()
