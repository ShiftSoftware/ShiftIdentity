using Microsoft.AspNetCore.Components.WebAssembly.Services;
using ShiftSoftware.ShiftIdentity.Core.ValidatorsAndFormatters;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class PhoneNumberEngine
{
    private readonly LazyAssemblyLoader? loader;
    private Task? loading;

    public PhoneNumberEngine(IServiceProvider services)
    {
        loader = services.GetService(typeof(LazyAssemblyLoader)) as LazyAssemblyLoader;
    }

    public Task EnsureLoadedAsync() => loader is null ? Task.CompletedTask : loading ??= LoadAsync();

    public static Task PreloadAsync(IServiceProvider services) =>
        (services.GetService(typeof(PhoneNumberEngine)) as PhoneNumberEngine)?.EnsureLoadedAsync() ?? Task.CompletedTask;

    private async Task LoadAsync()
    {
        try
        {
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "PhoneNumbers"))
                await loader!.LoadAssembliesAsync(new[] { "PhoneNumbers.wasm" });

            PhoneNumber.EngineAvailable = true;
        }
        catch
        {
            loading = null;
        }
    }
}
