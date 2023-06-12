using Microsoft.AspNetCore.Mvc.Authorization;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.Model;
using ShiftSoftware.TypeAuth.Core;

namespace Sample.Server;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        string tokenIssuer = "Shift Identity";
        string tokenKey = "e8SXUkZ6PC1H7X7-ySHeIOXP-iGprnHmiX4_mKM8OnnUTqS3hhLZcbWAEi5QonN4O";

        builder.Services.AddControllersWithViews().AddShiftIdentity(tokenIssuer, tokenKey, allowedScopes: new string[] { "test" })
            .AddFakeIdentity(new TokenSettingsModel
            {
                Audience = "Shift-FakeIdentity",
                ExpireSeconds = 60,
                Issuer = tokenIssuer,
                Key = tokenKey,
            },
            new TokenUserDataDTO
            {
                FullName = "Test",
                ID = 1,
                Username = "test"
            },
            new string[] { "scope1", "scope2", "scope3", "test" },
            new AppModel
            {
                AppId = "App1",
                DisplayName = "App 1",
                RedirectUri = "http://localhost:5239/Auth/Token"
            },
            "123a",
            new string[] { "Tree1", "Tree2", "{Tree 5}" }
            );

        builder.Services.AddRazorPages();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapRazorPages();
        app.MapControllers();
        app.MapFallbackToFile("index.html");

        app.Run();
    }
}