using System;

namespace PrivateSekai.Tests;

internal static class Program
{
    public static void Main()
    {
        UserOperationChecks.Run();
        ArchitectureChecks.Run();
        ResourceTests.Run();
        FeatureChecks.Run();
        SkillPracticeChecks.Run();
        TransportChecks.Run();
        Console.WriteLine($"通过 {Check.Count} 项检查。");
    }
}
