using System;

namespace PrivateSekai.Tests;

internal static class Check
{
    public static int Count { get; private set; }

    public static void That(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("检查失败：" + label);
        Count++;
    }

    public static T Throws<T>(Action action, string label) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            Count++;
            return exception;
        }
        throw new InvalidOperationException("检查失败，未抛出预期异常：" + label);
    }
}
