using Microsoft.AspNetCore.Components;
using MudBlazor;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree;

public partial class ActionTree
{
    public class TreeItemData : TreeItemData<ActionTreeNode>
    {
        public TreeItemData(ActionTreeNode value) : base(value)
        {
            Value = value;
        }
    }

    [Inject] ITypeAuthService tAuth { get; set; } = default!;
    [Inject] ShiftIdentityDashboardBlazorOptions shiftIdentityDashboardBlazorOptions { get; set; } = default!;
    [Inject] HttpService httpService { get; set; } = default!;

    public IReadOnlyCollection<TreeItemData<ActionTreeNode>> TreeItems { get; set; } = Array.Empty<TreeItemData<ActionTreeNode>>();

    private List<ActionTreeNode> LoggedInUserTreeItems_Flattened = new List<ActionTreeNode> { };

    TypeAuthContext tAuth_View = new TypeAuthContext();
    TypeAuthContext tAuth_LoggedInUser = new TypeAuthContext();

    bool initialized = false;

    [Parameter]
    public string Tree { get; set; } = default!;

    [Parameter]
    public EventCallback<string> TreeChanged { get; set; }

    [Parameter]
    public bool ReadOnly { get; set; }
    [Parameter]
    public bool Disabled { get; set; }

    private async Task ExpandIdentityActions()
    {
        await Task.WhenAll(new List<Task>
        {
            Task.Run(async () => {
                var countries = await this.httpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Country.CountryListDTO>>("IdentityCountry?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Countries.Expand(countries.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),

            Task.Run(async () => {
                var regions = await this.httpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Region.RegionListDTO>>("IdentityRegion?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Regions.Expand(regions.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),

            Task.Run(async () => {
                var companies = await this.httpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Company.CompanyListDTO>>("IdentityCompany?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Companies.Expand(companies.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),

            Task.Run(async () =>
            {
                var branches = await this.httpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.CompanyBranch.CompanyBranchListDTO>>("IdentityCompanyBranch?$orderby=Company");

                Core.ShiftIdentityActions.DataLevelAccess.Branches.Expand(branches.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, $"{x.Company} ↔ {x.Name}"); }).ToList(), true, true);
            }),

            Task.Run(async () =>
            {
                var teams = await this.httpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Team.TeamListDTO>>("IdentityTeam?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Teams.Expand(teams.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),

            Task.Run(async () =>
            {
                var brands = await this.httpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Brand.BrandListDTO>>("IdentityBrand?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Brands.Expand(brands.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), false, true);
            }),

            Task.Run(async () =>
            {
                var cities = await this.httpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.City.CityListDTO>>("IdentityCity?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Cities.Expand(cities.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),
        });
    }

    protected override async Task OnInitializedAsync()
    {
        this.tAuth_LoggedInUser = (tAuth as TypeAuthContext)!;

        var typeAuthBuilder = new TypeAuthContextBuilder();

        await ExpandIdentityActions();

        if (shiftIdentityDashboardBlazorOptions.DynamicTypeAuthActionExpander != null)
            await shiftIdentityDashboardBlazorOptions.DynamicTypeAuthActionExpander.Invoke();

        foreach (var actionTree in this.tAuth_LoggedInUser!.GetRegisteredActionTrees())
            typeAuthBuilder.AddActionTree(actionTree);

        this.tAuth_View = typeAuthBuilder
        .AddAccessTree(Tree)
        .Build();

        this.TraverseTree(tAuth_View.ActionTree, null);

        this.TraverseTree(tAuth_LoggedInUser.ActionTree, null, this.LoggedInUserTreeItems_Flattened);

        var tree = tAuth_View.ActionTree.ActionTreeItems;

        this.TreeItems = tree.Select(x => new TreeItemData(x)).ToArray();
        this.initialized = true;
    }

    void TraverseTree(ActionTreeNode tree, ActionTreeNode? parent, List<ActionTreeNode>? flattenedList = null)
    {
        if (flattenedList != null)
            flattenedList.Add(tree);

        tree.AdditionalData = new AdditionalTreeItem
        {
            Expanded = false,
            Parent = parent,
        };

        foreach (var item in tree.ActionTreeItems)
            TraverseTree(item, tree, flattenedList);
    }

    public string GenerateAccessTree()
    {
        return this.tAuth_View.GenerateAccessTree(tAuth_LoggedInUser);
    }

}
