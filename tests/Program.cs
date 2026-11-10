using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClientPlugin.Policy;

namespace LwCargoManager.Tests;

/// <summary>Tiny dependency-free test runner: every public static void method in a *Tests class is a test.</summary>
public static class Program
{
    public static int Main()
    {
        int passed = 0, failed = 0;
        var tests = typeof(Program).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Tests"))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
            .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name);

        foreach (var test in tests)
        {
            try
            {
                test.Invoke(null, null);
                passed++;
            }
            catch (TargetInvocationException e)
            {
                failed++;
                Console.WriteLine($"FAIL {test.DeclaringType.Name}.{test.Name}: {e.InnerException?.Message}");
            }
        }

        Console.WriteLine($"{passed} passed, {failed} failed, {Assert.Count} assertions");
        return failed == 0 ? 0 : 1;
    }
}

public static class Assert
{
    public static int Count;

    public static void True(bool condition, string message = "expected true")
    {
        Count++;
        if (!condition) throw new Exception(message);
    }

    public static void False(bool condition, string message = "expected false") => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string what = "")
    {
        Count++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what} expected <{expected}> but was <{actual}>");
    }
}
