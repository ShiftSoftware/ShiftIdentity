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

    [Inject] ITypeAuthService TypeAuth { get; set; } = default!;
    [Inject] ShiftIdentityDashboardBlazorOptions ShiftIdentityDashboardBlazorOptions { get; set; } = default!;
    [Inject] HttpService HttpService { get; set; } = default!;

    public IReadOnlyCollection<TreeItemData<ActionTreeNode>> TreeItems { get; set; } = [];

    private List<ActionTreeNode> LoggedInUserTreeItems_Flattened = [];

    TypeAuthContext tAuth_View = new();
    TypeAuthContext tAuth_LoggedInUser = new();

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
                var countries = await this.HttpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Country.CountryListDTO>>("IdentityCountry?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Countries.Expand(countries.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),

            Task.Run(async () => {
                var regions = await this.HttpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Region.RegionListDTO>>("IdentityRegion?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Regions.Expand(regions.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),

            Task.Run(async () => {
                var companies = await this.HttpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Company.CompanyListDTO>>("IdentityCompany?$orderby=Name");
                Core.ShiftIdentityActions.DataLevelAccess.Companies.Expand(companies.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name ?? x.ID!); }).ToList(), true, true);
            }),

            Task.Run(async () =>
            {
                var branches = await this.HttpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.CompanyBranch.CompanyBranchListDTO>>("IdentityCompanyBranch?$orderby=Company");

                Core.ShiftIdentityActions.DataLevelAccess.Branches.Expand(branches.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, $"{x.Company} ↔ {x.Name}"); }).ToList(), true, true);
            }),

            Task.Run(async () =>
            {
                var teams = await this.HttpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Team.TeamListDTO>>("IdentityTeam?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Teams.Expand(teams.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),

            Task.Run(async () =>
            {
                var brands = await this.HttpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.Brand.BrandListDTO>>("IdentityBrand?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Brands.Expand(brands.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), false, true);
            }),

            Task.Run(async () =>
            {
                var cities = await this.HttpService.GetAsync<ShiftEntity.Model.Dtos.ODataDTO<ShiftIdentity.Core.DTOs.City.CityListDTO>>("IdentityCity?$orderby=Name");

                Core.ShiftIdentityActions.DataLevelAccess.Cities.Expand(cities.Data!.Value.Select((x) => { return new KeyValuePair<string, string>(x.ID!, x.Name); }).ToList(), true, true);
            }),
        });
    }

    protected override async Task OnInitializedAsync()
    {
        this.tAuth_LoggedInUser = (TypeAuth as TypeAuthContext)!;

        var typeAuthBuilder = new TypeAuthContextBuilder();

        await ExpandIdentityActions();

        if (ShiftIdentityDashboardBlazorOptions.DynamicTypeAuthActionExpander != null)
            await ShiftIdentityDashboardBlazorOptions.DynamicTypeAuthActionExpander.Invoke();

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
        flattenedList?.Add(tree);

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
