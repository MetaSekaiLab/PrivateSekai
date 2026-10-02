extern alias game;

using System;
using System.Collections.Generic;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Shared.Resources;

public interface IResourceHandler
{
    IReadOnlyCollection<string> ResourceTypes { get; }

    void Grant(UserSession user, UserResource resource);

    int Consume(UserSession user, string type, int id, int quantity, bool paidFirst) =>
        throw new NotSupportedException($"Resource '{type}' cannot be consumed.");
}
