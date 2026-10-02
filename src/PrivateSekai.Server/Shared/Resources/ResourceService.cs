extern alias game;

using System;
using System.Collections.Generic;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Shared.Resources;

public sealed class ResourceService
{
    private readonly UserSession user;
    private readonly Dictionary<string, IResourceHandler> handlers = new(StringComparer.Ordinal);

    public ResourceService(UserSession user, IEnumerable<IResourceHandler> handlers)
    {
        this.user = user;
        foreach (var handler in handlers)
        {
            foreach (var type in handler.ResourceTypes)
            {
                if (string.IsNullOrWhiteSpace(type) || !this.handlers.TryAdd(type, handler))
                    throw new InvalidOperationException($"Invalid or duplicate resource handler: '{type}'.");
            }
        }
    }

    public void Grant(UserResource resource)
    {
        if (resource.quantity <= 0)
            return;

        GetHandler(resource.resourceType).Grant(user, resource);
    }

    public void Grant(IEnumerable<UserResource> resources)
    {
        foreach (var resource in resources)
            Grant(resource);
    }

    public int Consume(string? type, int id, int quantity, bool paidFirst = false)
    {
        if (quantity <= 0)
            return 0;

        return GetHandler(type).Consume(user, type!, id, quantity, paidFirst);
    }

    private IResourceHandler GetHandler(string? type) =>
        type != null && handlers.TryGetValue(type, out var handler)
            ? handler
            : throw new NotSupportedException($"Unsupported resource type: '{type}'.");
}
