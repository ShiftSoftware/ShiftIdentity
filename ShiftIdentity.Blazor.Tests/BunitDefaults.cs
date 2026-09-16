using System.Runtime.CompilerServices;
using Bunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// bUnit gives WaitForAssertion, WaitForState and WaitForElement one second by default. The first render of a
/// MudBlazor form exceeds that on a loaded two-core agent while the other collections are still JIT-compiling in
/// parallel, which is how identity-ui failed eight seconds into a pipeline run. WaitFor* returns as soon as its
/// condition holds, so a generous budget costs a passing test nothing and only decides how long a broken test takes
/// to fail. The property is static, so this runs once when the test assembly loads and covers every context.
/// </summary>
internal static class BunitDefaults
{
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    [ModuleInitializer]
    internal static void Apply() => BunitContext.DefaultWaitTimeout = WaitTimeout;
}
