extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Inventory;

public sealed class InventoryResourceHandler : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["material", "practice_ticket", "skill_practice_ticket", "boost_item", "event_item"];

    public void Grant(UserSession user, UserResource resource)
    {
        if (resource.resourceId <= 0)
            return;

        switch (resource.resourceType)
        {
            case "material":
                ChangeMaterial(user, resource.resourceId, resource.quantity);
                break;
            case "practice_ticket":
                ChangePracticeTicket(user, resource.resourceId, resource.quantity);
                break;
            case "skill_practice_ticket":
                ChangeSkillPracticeTicket(user, resource.resourceId, resource.quantity);
                break;
            case "boost_item":
                var items = (user.Data.userBoostItems ?? []).ToList();
                var item = items.FirstOrDefault(i => i.boostItemId == resource.resourceId);
                if (item == null)
                {
                    item = new UserBoostItem { userId = user.UserId, boostItemId = resource.resourceId };
                    items.Add(item);
                }
                item.quantity = checked(item.quantity + resource.quantity);
                user.Data.userBoostItems = items.OrderBy(i => i.boostItemId).ToArray();
                user.MarkChanged(nameof(SuiteUser.userBoostItems));
                break;
            default:
                throw new NotSupportedException($"Resource '{resource.resourceType}' cannot be granted.");
        }
    }

    public int Consume(UserSession user, string type, int id, int quantity, bool paidFirst) =>
        type switch
        {
            "boost_item" => ConsumeBoostItem(user, id, quantity),
            "event_item" => ConsumeEventItem(user, id, quantity),
            "material" => id > 0 ? ChangeMaterial(user, id, -quantity) : 0,
            "practice_ticket" => id > 0 ? ChangePracticeTicket(user, id, -quantity) : 0,
            "skill_practice_ticket" => id > 0 ? ChangeSkillPracticeTicket(user, id, -quantity) : 0,
            _ => throw new NotSupportedException($"Resource '{type}' cannot be consumed.")
        };

    private static int ConsumeBoostItem(UserSession user, int id, int quantity)
    {
        var item = user.Data.userBoostItems?.SingleOrDefault(i => i.boostItemId == id)
            ?? throw new ArgumentException("Boost item is not owned.");
        if (quantity <= 0 || item.quantity < quantity) throw new ArgumentException("Insufficient boost items.");
        item.quantity -= quantity;
        user.MarkChanged(nameof(SuiteUser.userBoostItems));
        return item.quantity;
    }

    private static int ConsumeEventItem(UserSession user, int id, int quantity)
    {
        var item = user.Data.userEventItems?.SingleOrDefault(i => i.eventItemId == id)
            ?? throw new ArgumentException("Event item is not owned.");
        if (quantity <= 0 || item.quantity < quantity) throw new ArgumentException("Insufficient event items.");
        item.quantity -= quantity;
        user.MarkChanged(nameof(SuiteUser.userEventItems));
        return item.quantity;
    }

    private static int ChangeSkillPracticeTicket(UserSession user, int id, int quantity)
    {
        var tickets = (user.Data.userSkillPracticeTickets ?? []).ToList();
        var ticket = tickets.SingleOrDefault(t => t.skillPracticeTicketId == id);
        var remaining = checked((ticket?.quantity ?? 0) + quantity);
        if (remaining < 0)
            throw new ArgumentException("Insufficient skill practice tickets.");
        if (ticket == null)
        {
            ticket = new UserSkillPracticeTicket { userId = user.UserId, skillPracticeTicketId = id };
            tickets.Add(ticket);
        }
        ticket.quantity = remaining;
        user.Data.userSkillPracticeTickets = tickets.OrderBy(t => t.skillPracticeTicketId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userSkillPracticeTickets));
        return remaining;
    }

    private static int ChangeMaterial(UserSession user, int id, int quantity)
    {
        var materials = (user.Data.userMaterials ?? []).ToList();
        var material = materials.FirstOrDefault(m => m.materialId == id);
        if (material == null)
        {
            material = new UserMaterial { materialId = id };
            materials.Add(material);
        }

        material.quantity = quantity < 0 ? Math.Max(0, material.quantity + quantity) : material.quantity + quantity;
        user.Data.userMaterials = quantity < 0
            ? materials.OrderBy(m => m.materialId).ToArray()
            : materials.ToArray();
        user.MarkChanged(nameof(SuiteUser.userMaterials));
        return material.quantity;
    }

    private static int ChangePracticeTicket(UserSession user, int id, int quantity)
    {
        var tickets = (user.Data.userPracticeTickets ?? []).ToList();
        var ticket = tickets.FirstOrDefault(t => t.practiceTicketId == id);
        if (ticket == null)
        {
            ticket = new UserPracticeTicket { userId = user.UserId, practiceTicketId = id };
            tickets.Add(ticket);
        }

        ticket.quantity = quantity < 0 ? Math.Max(0, ticket.quantity + quantity) : ticket.quantity + quantity;
        user.Data.userPracticeTickets = quantity < 0
            ? tickets.OrderBy(t => t.practiceTicketId).ToArray()
            : tickets.ToArray();
        user.MarkChanged(nameof(SuiteUser.userPracticeTickets));
        return ticket.quantity;
    }
}
