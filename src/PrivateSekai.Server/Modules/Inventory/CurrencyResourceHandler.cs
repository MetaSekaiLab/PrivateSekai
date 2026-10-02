extern alias game;

using System;
using System.Collections.Generic;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Inventory;

public sealed class CurrencyResourceHandler : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["jewel", "paid_jewel", "coin", "virtual_coin"];

    public void Grant(UserSession user, UserResource resource)
    {
        var data = user.Data;
        switch (resource.resourceType)
        {
            case "jewel":
                data.userChargedCurrency ??= new ChargedCurrency { paidUnitPrices = [] };
                data.userChargedCurrency.free += resource.quantity;
                user.MarkChanged(nameof(SuiteUser.userChargedCurrency));
                break;
            case "coin":
                if (data.userGamedata == null)
                    return;
                data.userGamedata.coin += resource.quantity;
                user.MarkChanged(nameof(SuiteUser.userGamedata));
                break;
            case "virtual_coin":
                if (data.userGamedata == null)
                    return;
                data.userGamedata.virtualCoin += resource.quantity;
                user.MarkChanged(nameof(SuiteUser.userGamedata));
                break;
            default:
                throw new NotSupportedException($"Resource '{resource.resourceType}' cannot be granted.");
        }
    }

    public int Consume(UserSession user, string type, int id, int quantity, bool paidFirst)
    {
        var data = user.Data;
        switch (type)
        {
            case "coin":
                if (data.userGamedata == null)
                    return 0;
                data.userGamedata.coin = Math.Max(0, data.userGamedata.coin - quantity);
                user.MarkChanged(nameof(SuiteUser.userGamedata));
                return data.userGamedata.coin;
            case "paid_jewel":
                data.userChargedCurrency ??= new ChargedCurrency { paidUnitPrices = [] };
                data.userChargedCurrency.paid = Math.Max(0, Math.Max(0, data.userChargedCurrency.paid) - quantity);
                user.MarkChanged(nameof(SuiteUser.userChargedCurrency));
                return data.userChargedCurrency.paid;
            case "jewel":
                data.userChargedCurrency ??= new ChargedCurrency { paidUnitPrices = [] };
                var currency = data.userChargedCurrency;
                if (paidFirst)
                {
                    var paidCost = Math.Min(Math.Max(0, currency.paid), quantity);
                    currency.paid -= paidCost;
                    if (quantity > paidCost)
                        currency.free = Math.Max(0, Math.Max(0, currency.free) - (quantity - paidCost));
                }
                else
                {
                    var freeCost = Math.Min(Math.Max(0, currency.free), quantity);
                    currency.free -= freeCost;
                    if (quantity > freeCost)
                        currency.paid = Math.Max(0, Math.Max(0, currency.paid) - (quantity - freeCost));
                }
                user.MarkChanged(nameof(SuiteUser.userChargedCurrency));
                return Math.Max(0, currency.paid) + Math.Max(0, currency.free);
            default:
                throw new NotSupportedException($"Resource '{type}' cannot be consumed.");
        }
    }
}
