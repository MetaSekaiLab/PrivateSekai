using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Tests;

internal static class ArchitectureChecks
{
    public static void Run()
    {
        var assembly = typeof(UserOperation).Assembly;
        var types = assembly.GetTypes();
        var services = types.Where(t => t.Namespace?.StartsWith("PrivateSekai.Modules.", StringComparison.Ordinal) == true &&
            t.Name.EndsWith("Service", StringComparison.Ordinal)).ToArray();
        var handlers = types.Where(t => !t.IsAbstract && typeof(IResourceHandler).IsAssignableFrom(t)).ToArray();
        Check.That(services.Length > 0 && handlers.Length > 0, "架构检查覆盖实际服务和资源处理器");
        Check.That(types.All(t => t.Name is not "GameUser" and not "UserManager"), "旧用户业务门面已删除");

        using var image = AssemblyDefinition.ReadAssembly(assembly.Location);
        foreach (var service in services)
        {
            var dependencies = ReferencedTypes(image.MainModule.GetType(service.FullName!)).Distinct();
            Check.That(dependencies.All(t => !IsTransportOrStore(t)), $"{service.Name} 不依赖 HTTP、用户存储或操作入口");
        }
        foreach (var handler in handlers)
        {
            var dependencies = ReferencedTypes(image.MainModule.GetType(handler.FullName!)).Distinct();
            Check.That(dependencies.All(t => !IsTransportOrStore(t) && !IsBusinessService(t)),
                $"{handler.Name} 不反向依赖业务 Service、用户存储或操作入口");
        }

        var completed = new HashSet<Type>();
        var active = new HashSet<Type>();
        foreach (var root in services.Concat(handlers).Append(typeof(ResourceService)))
            Visit(root, types, active, completed);
        Console.WriteLine("架构：业务依赖边界、资源处理器边界及构造依赖无环检查通过。");
    }

    private static bool IsTransportOrStore(string name) =>
        name.StartsWith("Microsoft.AspNetCore.Http.", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.AspNetCore.Mvc.", StringComparison.Ordinal) ||
        name is "PrivateSekai.Shared.Users.UserOperation" or "PrivateSekai.Shared.Users.IUserStore" or
            "PrivateSekai.Storage.MemoryUserStore" or "System.IServiceProvider";

    private static bool IsBusinessService(string name) =>
        name.StartsWith("PrivateSekai.", StringComparison.Ordinal) && name.EndsWith("Service", StringComparison.Ordinal);

    private static IEnumerable<string> ReferencedTypes(TypeDefinition type)
    {
        foreach (var field in type.Fields)
            foreach (var name in Names(field.FieldType))
                yield return name;
        foreach (var method in type.Methods)
        {
            foreach (var name in Names(method.ReturnType))
                yield return name;
            foreach (var parameter in method.Parameters)
                foreach (var name in Names(parameter.ParameterType))
                    yield return name;
            if (!method.HasBody)
                continue;
            foreach (var variable in method.Body.Variables)
                foreach (var name in Names(variable.VariableType))
                    yield return name;
            foreach (var instruction in method.Body.Instructions)
            {
                var reference = instruction.Operand switch
                {
                    MethodReference called => called.DeclaringType,
                    FieldReference field => field.DeclaringType,
                    TypeReference referenced => referenced,
                    _ => null
                };
                if (reference != null)
                    foreach (var name in Names(reference))
                        yield return name;
            }
        }
        foreach (var nested in type.NestedTypes)
            foreach (var name in ReferencedTypes(nested))
                yield return name;
    }

    private static IEnumerable<string> Names(TypeReference type)
    {
        if (type is GenericInstanceType generic)
        {
            yield return generic.ElementType.FullName;
            foreach (var argument in generic.GenericArguments)
                foreach (var name in Names(argument))
                    yield return name;
        }
        else if (type is TypeSpecification specification)
        {
            foreach (var name in Names(specification.ElementType))
                yield return name;
        }
        else
        {
            yield return type.FullName;
        }
    }

    private static void Visit(Type type, Type[] types, HashSet<Type> active, HashSet<Type> completed)
    {
        if (completed.Contains(type))
            return;
        Check.That(active.Add(type), $"构造依赖无环：{type.Name}");
        foreach (var parameter in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
        {
            var dependency = parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? parameter.GenericTypeArguments[0]
                : parameter;
            foreach (var implementation in types.Where(t => t.IsClass && !t.IsAbstract && dependency.IsAssignableFrom(t)))
                Visit(implementation, types, active, completed);
        }
        active.Remove(type);
        completed.Add(type);
    }
}
