using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sample.Client.General;
using Sample.Client.Services;
using Sample.Client.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Model;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Blazor.Providers;
using CustomAuthStateProvider = Sample.Client.General.CustomAuthStateProvider;

namespace Sample.Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");

            builder.Services.AddScoped(sp => new HttpClient(sp.GetRequiredService<RefreshTokenMessageHandler>()) 
                { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
            builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
            builder.Services.AddAuthorizationCore();
            builder.Services.AddBlazoredLocalStorage();

            builder.Services.AddShiftIdentity("app1", builder.HostEnvironment.BaseAddress, builder.HostEnvironment.BaseAddress);
            //builder.Services.AddShiftIdentity("test", "http://localhost:5088/", "http://localhost:5088/");

            builder.Services.AddScoped<StorageService>();
            builder.Services.AddScoped<IIdentityTokenStore, StorageService>();
            builder.Services.AddScoped<IIdentityTokenProvider, StorageService>();
            builder.Services.AddScoped<IHttpService, HttpService>();

            var app = builder.Build();

            await app.RefreshTokenAsync(50);

            await app.RunAsync();
        }
    }
}