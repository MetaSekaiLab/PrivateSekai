extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Gacha;

public sealed class GachaResourceHandler : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["gacha_ticket", "gacha_ceil_item"];

    public void Grant(UserSession user, UserResource resource)
    {
        if (resource.resourceId > 0)
            Change(user, resource.resourceType!, resource.resourceId, resource.quantity);
    }

    public int Consume(UserSession user, string type, int id, int quantity, bool paidFirst) =>
        id > 0 ? Change(user, type, id, -quantity) : 0;

    private static int Change(UserSession user, string type, int id, int quantity)
    {
        switch (type)
        {
            case "gacha_ticket":
                var tickets = (user.Data.userGachaTickets ?? []).ToList();
                var ticket = tickets.FirstOrDefault(t => t.gachaTicketId == id);
                if (ticket == null)
                {
                    ticket = new UserGachaTicket { userId = user.UserId, gachaTicketId = id };
                    tickets.Add(ticket);
                }
                ticket.quantity = quantity < 0 ? Math.Max(0, ticket.quantity + quantity) : ticket.quantity + quantity;
                user.Data.userGachaTickets = tickets.OrderBy(t => t.gachaTicketId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userGachaTickets));
                return ticket.quantity;
            case "gacha_ceil_item":
                var items = (user.Data.userGachaCeilItems ?? []).ToList();
                var item = items.FirstOrDefault(i => i.gachaCeilItemId == id);
                if (item == null)
                {
                    item = new UserGachaCeilItem { userId = user.UserId, gachaCeilItemId = id };
                    items.Add(item);
                }
                item.quantity = quantity < 0 ? Math.Max(0, item.quantity + quantity) : item.quantity + quantity;
                user.Data.userGachaCeilItems = items.OrderBy(i => i.gachaCeilItemId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userGachaCeilItems));
                return item.quantity;
            default:
                throw new NotSupportedException($"Unsupported gacha resource: '{type}'.");
        }
    }
}
