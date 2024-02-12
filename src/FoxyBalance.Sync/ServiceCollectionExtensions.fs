namespace FoxyBalance.Sync

open Microsoft.Extensions.DependencyInjection
open ShopifySharp

[<AutoOpen>]
module ServiceCollectionExtensions =
    let private (~%) x = ignore x

    type IServiceCollection with
        member services.AddFoxyBalanceSyncClients() =
            %services.AddSingleton<GumroadClient>()
            %services.AddSingleton<PaypalTransactionParser>()
            %services.AddSingleton<ShopifyPartnerClient>()
            %services.AddSingleton<IRequestExecutionPolicy, PartnerServiceRetryExecutionPolicy>()
            %services.AddHttpClient()
