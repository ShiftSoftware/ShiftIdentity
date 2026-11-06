using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Components;
using ShiftSoftware.ShiftBlazor.Enums;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.City;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.Company;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.CompanyBranch;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class CountryContentLanguagesTests
{
    private static readonly string[] EnglishRussian = ["English", "Russian"];
    private static readonly string[] BranchPresentationFields = ["Display Name", "Description", "Standard Working Hours", "Standard Working Day", "Address"];
    private static readonly string[] CompanyFields = ["Name", "Legal Name", "HQ Address"];

    private static readonly Dictionary<string, string> Countries = new()
    {
        ["1"] = "Uzbekistan",
        ["2"] = "Turkmenistan",
        ["3"] = "Tajikistan",
        ["4"] = "Japan",
        ["5"] = "Узбекистан",
    };

    [Theory]
    [InlineData("1", "Uzbek")]
    [InlineData("2", "Turkmen")]
    [InlineData("3", "Tajik")]
    [InlineData("5", "Uzbek")]
    public async Task A_branch_in_a_supported_country_gets_its_local_language_on_the_presentation_fields_only(string cityId, string language)
    {
        await using var context = Create(out var transport);
        transport.Branch = Branch(cityId);

        var cut = await OpenBranchAsync(context);

        foreach (var field in BranchPresentationFields)
            cut.WaitForAssertion(() => Assert.Equal([.. EnglishRussian, language], Languages(cut, field)));
        Assert.Equal(EnglishRussian, Languages(cut, "Name"));
    }

    [Fact]
    public async Task A_branch_in_any_other_country_stays_on_the_configured_languages()
    {
        await using var context = Create(out var transport);
        transport.Branch = Branch("4");

        var cut = await OpenBranchAsync(context);

        foreach (var field in BranchPresentationFields)
            Assert.Equal(EnglishRussian, Languages(cut, field));
    }

    [Fact]
    public async Task Stored_local_text_shows_in_the_local_field()
    {
        await using var context = Create(out var transport);
        transport.Branch = Branch("1");
        transport.Branch.DisplayName = Text(("en", "E"), ("ru", "R"), ("uz", "Stored Uzbek"));

        var cut = await OpenBranchAsync(context);

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("input"), x => x.GetAttribute("value") == "Stored Uzbek"));
    }

    [Fact]
    public async Task Choosing_another_city_changes_the_local_language()
    {
        await using var context = Create(out var transport);
        transport.Branch = Branch("1");
        var cut = await OpenBranchAsync(context);
        cut.WaitForAssertion(() => Assert.Contains("Uzbek", Languages(cut, "Address")));

        await ChooseCityAsync(cut, "3");

        cut.WaitForAssertion(() => Assert.Equal([.. EnglishRussian, "Tajik"], Languages(cut, "Address")));
    }

    [Fact]
    public async Task A_new_branch_starts_on_the_configured_languages_and_follows_the_city()
    {
        await using var context = Create(out _);
        var cut = context.Render<CompanyBranchForm>();
        cut.WaitForAssertion(() => Assert.Equal(EnglishRussian, Languages(cut, "Address")));

        await ChooseCityAsync(cut, "2");

        cut.WaitForAssertion(() => Assert.Equal([.. EnglishRussian, "Turkmen"], Languages(cut, "Address")));
    }

    [Fact]
    public async Task Local_text_is_saved_with_the_field_and_the_name_has_no_local_language()
    {
        await using var context = Create(out var transport);
        transport.Branch = Branch("1");
        var cut = await OpenBranchAsync(context);
        var form = cut.FindComponent<ShiftEntityForm<CompanyBranchDTO>>();
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Contains("Uzbek", Languages(cut, "Address")));

        await TypeAsync(cut, "Address", "uz", "Toshkent");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(transport.Saves));
        var saved = JsonSerializer.Deserialize<CompanyBranchDTO>(transport.Saves[0], Web)!;
        Assert.Equal("Toshkent", Parse(saved.Address)["uz"]);
        Assert.Equal(["en", "ru"], Parse(saved.Name).Keys);
    }

    [Fact]
    public async Task A_published_branch_can_save_without_its_local_display_name()
    {
        await using var context = Create(out var transport);
        transport.Branch = Published(Branch("1"));
        var cut = await OpenBranchAsync(context);
        var form = cut.FindComponent<ShiftEntityForm<CompanyBranchDTO>>();
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Contains("Uzbek", Languages(cut, "Display Name")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(transport.Saves));
    }

    [Fact]
    public async Task The_field_replaced_when_the_languages_load_does_not_block_the_save()
    {
        await using var context = Create(out var transport);
        transport.Company = Company();
        transport.Company.Name = "Plain name";
        transport.CompanyBranchCities = ["1"];
        var cut = await OpenCompanyAsync(context);
        var form = cut.FindComponent<ShiftEntityForm<CompanyDTO>>();
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Contains("Uzbek", Languages(cut, "Name")));

        await TypeAsync(cut, "Name", "ru", "Имя");
        await TypeAsync(cut, "Name", "uz", "Nomi");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(transport.Saves));
    }

    [Fact]
    public async Task A_company_gets_the_local_language_of_each_of_its_branches_countries()
    {
        await using var context = Create(out var transport);
        transport.Company = Company();
        transport.CompanyBranchCities = ["1", "3", "4"];

        var cut = await OpenCompanyAsync(context);

        foreach (var field in CompanyFields)
            cut.WaitForAssertion(() => Assert.Equal([.. EnglishRussian, "Uzbek", "Tajik"], Languages(cut, field)));
    }

    [Fact]
    public async Task A_company_with_no_branches_stays_on_the_configured_languages()
    {
        await using var context = Create(out var transport);
        transport.Company = Company();

        var cut = await OpenCompanyAsync(context);

        foreach (var field in CompanyFields)
            Assert.Equal(EnglishRussian, Languages(cut, field));
    }

    [Fact]
    public async Task A_company_cannot_save_without_its_local_name()
    {
        await using var context = Create(out var transport);
        transport.Company = Company();
        transport.CompanyBranchCities = ["1"];
        var cut = await OpenCompanyAsync(context);
        var form = cut.FindComponent<ShiftEntityForm<CompanyDTO>>();
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Contains("Uzbek", Languages(cut, "Name")));

        cut.Find("form").Submit();
        cut.WaitForState(() => form.Instance.TaskInProgress == FormTasks.None);
        Assert.Empty(transport.Saves);

        await TypeAsync(cut, "Name", "uz", "Kompaniya");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(transport.Saves));
    }

    [Fact]
    public async Task When_the_country_lookup_fails_stored_local_text_keeps_its_field()
    {
        await using var context = Create(out var transport);
        transport.Branch = Branch("1");
        transport.Branch.DisplayName = Text(("en", "E"), ("ru", "R"), ("tg", "Stored Tajik"));
        transport.FailCityLookup = true;

        var cut = await OpenBranchAsync(context);

        cut.WaitForAssertion(() => Assert.Equal([.. EnglishRussian, "Tajik"], Languages(cut, "Display Name")));
    }

    [Fact]
    public async Task Other_forms_stay_on_the_configured_languages()
    {
        await using var context = Create(out var transport);

        var cut = context.Render<CityForm>();

        cut.WaitForAssertion(() => Assert.Equal(EnglishRussian, Languages(cut, "Name")));
        Assert.DoesNotContain(transport.Requests, x => x.Contains("IdentityCity"));
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string Text(params (string Language, string Value)[] texts) =>
        JsonSerializer.Serialize(texts.ToDictionary(x => x.Language, x => x.Value));

    private static Dictionary<string, string> Parse(string? value) => JsonSerializer.Deserialize<Dictionary<string, string>>(value!)!;

    private static CompanyBranchDTO Branch(string cityId) => new()
    {
        ID = "42",
        Name = Text(("en", "Name"), ("ru", "Имя")),
        DisplayName = Text(("en", "Display"), ("ru", "Показ")),
        Address = Text(("en", "Address"), ("ru", "Адрес")),
        Company = new() { Value = "7", Text = "Company" },
        City = new() { Value = cityId, Text = "City" },
    };

    private static CompanyBranchDTO Published(CompanyBranchDTO branch)
    {
        branch.PublishTargets = [Enum.GetValues<PublishTarget>()[0]];
        branch.Latitude = 41;
        branch.Longitude = 69;
        return branch;
    }

    private static CompanyDTO Company() => new()
    {
        ID = "7",
        Name = Text(("en", "Company"), ("ru", "Компания")),
    };

    private static List<string> Languages<T>(IRenderedComponent<T> cut, string field) where T : class, Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("label")
            .Select(x => x.TextContent.Trim())
            .Where(x => x.StartsWith($"{field} (") && x.EndsWith(')'))
            .Select(x => x[(field.Length + 2)..^1])
            .ToList();

    private static async Task TypeAsync<T>(IRenderedComponent<T> cut, string field, string language, string text) where T : class, Microsoft.AspNetCore.Components.IComponent
    {
        var input = cut.FindComponents<LocalizedInput>().Single(x => x.Instance.Label == field);
        await cut.InvokeAsync(() => input.Instance.TextChanged(language, text));
    }

    private static async Task ChooseCityAsync<T>(IRenderedComponent<T> cut, string cityId) where T : class, Microsoft.AspNetCore.Components.IComponent
    {
        var city = cut.FindComponent<ShiftAutocomplete<CityListDTO>>();
        await cut.InvokeAsync(() => city.Instance.ValueChanged.InvokeAsync(new ShiftEntitySelectDTO { Value = cityId, Text = "City" }));
    }

    private static Task<IRenderedComponent<CompanyBranchForm>> OpenBranchAsync(BunitContext context)
    {
        var cut = context.Render<CompanyBranchForm>(p => p.Add(x => x.Key, "42"));
        WaitUntilLoaded<CompanyBranchForm, CompanyBranchDTO>(cut, "42");
        return Task.FromResult(cut);
    }

    private static Task<IRenderedComponent<CompanyForm>> OpenCompanyAsync(BunitContext context)
    {
        var cut = context.Render<CompanyForm>(p => p.Add(x => x.Key, "7"));
        WaitUntilLoaded<CompanyForm, CompanyDTO>(cut, "7");
        return Task.FromResult(cut);
    }

    private static void WaitUntilLoaded<TForm, TDto>(IRenderedComponent<TForm> cut, string key)
        where TForm : class, Microsoft.AspNetCore.Components.IComponent
        where TDto : ShiftEntityViewAndUpsertDTO, new()
    {
        var form = cut.FindComponent<ShiftEntityForm<TDto>>();
        cut.WaitForState(() => form.Instance.Value.ID == key && form.Instance.TaskInProgress == FormTasks.None);
    }

    private static BunitContext Create(out Transport transport)
    {
        var context = new BunitContext();
        transport = new();
        context.Services.AddSingleton(new HttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") });
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c =>
        {
            c.BaseAddress = "https://identity.invalid/";
            c.AddLanguage("en-US", "English");
            c.AddLanguage("ru-RU", "Russian");
        });
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization();
        auth.SetAuthorized("synthetic");
        auth.SetClaims(new Claim(TypeAuthClaimTypes.AccessTree, "{\"ShiftIdentityActions\":{\"Companies\":[\"r\",\"w\"],\"Cities\":[\"r\",\"w\"]}}"));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<string> Saves { get; } = [];
        public List<string> Requests { get; } = [];
        public CompanyBranchDTO? Branch { get; set; }
        public CompanyDTO? Company { get; set; }
        public string[] CompanyBranchCities { get; set; } = [];
        public bool FailCityLookup { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Requests.Add(path + query);

            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Saves.Add(body);
                return path.Contains("CompanyBranch")
                    ? Json(new ShiftEntityResponse<CompanyBranchDTO>(JsonSerializer.Deserialize<CompanyBranchDTO>(body, Web)!))
                    : Json(new ShiftEntityResponse<CompanyDTO>(JsonSerializer.Deserialize<CompanyDTO>(body, Web)!));
            }

            if (path == "/IdentityCompanyBranch/42")
                return Json(new ShiftEntityResponse<CompanyBranchDTO>(Branch!));

            if (path == "/IdentityCompany/7")
                return Json(new ShiftEntityResponse<CompanyDTO>(Company!));

            if (path == "/IdentityCompanyBranch" && query.Contains("CompanyId eq '7'"))
            {
                // Answering after the fields first render is what makes them remount, as a real request does.
                await Task.Delay(100, cancellationToken);
                return Json(new { Value = CompanyBranchCities.Select(x => new { CityId = x }) });
            }

            if (path == "/IdentityCity" && query.Contains("ID eq"))
            {
                if (FailCityLookup)
                    return new(HttpStatusCode.InternalServerError);

                return Json(new { Value = Countries.Where(x => query.Contains($"ID eq '{x.Key}'")).Select(x => new { Country = x.Value }) });
            }

            return Json(new { Value = Array.Empty<object>() });
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
