using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;

public class AccessTreeService
{
    private readonly HttpService http;

    public AccessTreeService(HttpService http)
    {
        this.http = http;
    }

    public async Task<HttpResponse<ODataDTO<List<AccessTreeDTO>>>> GetAllAccessTreesAsync()
    {
        return await http.GetAsync<ODataDTO<List<AccessTreeDTO>>>("odata/AccessTree");
    }

    public async Task<HttpResponse<ODataDTO<List<AccessTreeDTO>>>> GetAllAccessTreesAsync
        (string odataQuery = "", bool ignoreGlobalFilters = false)
    {
        return await http.GetAsync<ODataDTO<List<AccessTreeDTO>>>("odata/AccessTree", odataQuery: odataQuery,
            query: new { IgnoreGlobalFilters = ignoreGlobalFilters });
    }
}
