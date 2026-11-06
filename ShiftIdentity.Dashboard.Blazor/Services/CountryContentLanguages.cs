using ShiftSoftware.ShiftBlazor.Services;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;

public class CountryContentLanguages(HttpClient http, SettingManager settingManager)
{
    private static readonly (string[] CountryNames, LanguageInfo Language)[] LocalLanguages =
    [
        (["uzbek", "узбек"], new() { CultureName = "uz-UZ", Label = "Uzbek" }),
        (["turkmen", "туркмен"], new() { CultureName = "tk-TM", Label = "Turkmen" }),
        (["tajik", "таджик"], new() { CultureName = "tg-TJ", Label = "Tajik" }),
    ];

    private readonly Dictionary<string, List<LanguageInfo>> lists = [];

    public List<LanguageInfo> Default => List([]);

    public async Task<List<LanguageInfo>> ForCityAsync(string? cityId, params string?[] storedTexts)
    {
        if (string.IsNullOrWhiteSpace(cityId))
            return Default;

        return await ForCitiesAsync([cityId], storedTexts);
    }

    public async Task<List<LanguageInfo>> ForCompanyAsync(string? companyId, params string?[] storedTexts)
    {
        if (string.IsNullOrWhiteSpace(companyId))
            return Default;

        try
        {
            var branches = await GetAsync<BranchRow>("IdentityCompanyBranch", $"CompanyId eq '{companyId}'");

            return await ForCitiesAsync(branches.Select(x => x.CityId).OfType<string>().Distinct().ToList(), storedTexts);
        }
        catch (Exception)
        {
            return ForStoredTexts(storedTexts);
        }
    }

    private async Task<List<LanguageInfo>> ForCitiesAsync(List<string> cityIds, string?[] storedTexts)
    {
        if (cityIds.Count == 0)
            return Default;

        try
        {
            var cities = await GetAsync<CityRow>("IdentityCity", string.Join(" or ", cityIds.Select(id => $"ID eq '{id}'")));

            return List(LocalLanguages
                .Where(x => cities.Any(city => x.CountryNames.Any(name => city.Country?.Contains(name, StringComparison.OrdinalIgnoreCase) == true)))
                .Select(x => x.Language));
        }
        catch (Exception)
        {
            return ForStoredTexts(storedTexts);
        }
    }

    // Without the country, showing the local languages that already have text keeps that text from being dropped on save.
    private List<LanguageInfo> ForStoredTexts(string?[] storedTexts) =>
        List(LocalLanguages
            .Where(x => storedTexts.Any(text => text?.Contains($"\"{x.Language.CultureName[..2]}\"") == true))
            .Select(x => x.Language));

    // @key compares by reference, so a field only remounts if equal lists are the same instance.
    private List<LanguageInfo> List(IEnumerable<LanguageInfo> local)
    {
        var languages = local.ToList();
        var key = string.Join(',', languages.Select(x => x.CultureName));

        if (!lists.TryGetValue(key, out var list))
            lists[key] = list = [.. settingManager.Configuration.ContentLanguages, .. languages];

        return list;
    }

    private async Task<List<T>> GetAsync<T>(string entitySet, string filter)
    {
        var result = await http.GetFromJsonAsync<ODataDTO<T>>($"{entitySet}?$filter={Uri.EscapeDataString(filter)}");

        return result?.Value ?? [];
    }

    private sealed record BranchRow(string? CityId);

    private sealed record CityRow(string? Country);
}
