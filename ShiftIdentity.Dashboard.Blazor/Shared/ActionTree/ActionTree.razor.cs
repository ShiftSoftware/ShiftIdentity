using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree;

public partial class ActionTree : IDisposable
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
    [Inject] AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    public IReadOnlyCollection<TreeItemData<ActionTreeNode>> TreeItems { get; set; } = [];

    // The admin's tree nodes by path, for the wildcard buttons: each asks whether the admin holds that wildcard there.
    private ILookup<string?, ActionTreeNode> LoggedInUserTreeItems_ByPath = Array.Empty<ActionTreeNode>().ToLookup(x => x.Path);

    // Bumped when the admin's own rights are rebuilt (a token refresh), which every row shows, so every row renders again.
    private int loggedInUserVersion;

    TypeAuthContext tAuth_View = new();
    TypeAuthContext tAuth_LoggedInUser = new();

    bool initialized = false;

    [Parameter]
    public string Tree { get; set; } = default!;

    [Parameter]
    public EventCallback<string> TreeChanged { get; set; }

    /// <summary>
    /// Several trees shown together, read-only, as TypeAuth decides with them: a user's own tree and every assigned
    /// tree. Shown in place of <see cref="Tree"/>. They aren't merged into one first: one tree can't hold everything
    /// several grant together (rows beyond a dynamic action's wildcard).
    /// </summary>
    [Parameter]
    public IReadOnlyList<string>? Trees { get; set; }

    [Parameter]
    public bool ReadOnly { get; set; }
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// When true, the tree renders click-to-pick buttons for each action/access
    /// instead of toggleable access checkboxes. No access-tree editing happens.
    /// </summary>
    [Parameter]
    public bool PickerMode { get; set; }

    [Parameter]
    public EventCallback<ActionPickedEventArgs> OnActionPicked { get; set; }

    internal async Task PickAsync((ActionTreeNode Item, ShiftSoftware.TypeAuth.Core.Access Access, bool AnyDynamicRow) pick)
    {
        var (item, access, anyDynamicRow) = pick;
        var action = item.Action;
        if (action is null)
            return;

        var isDynamicSubItem = item.IsADynamicSubItem;

        var args = new ActionPickedEventArgs
        {
            ActionPath = action.Path!,
            Access = access,
            DynamicId = isDynamicSubItem ? item.ID : null,
            AnyDynamicRow = anyDynamicRow,
            DisplayName = BuildDisplayName(item, access, anyDynamicRow)
        };

        await OnActionPicked.InvokeAsync(args);
    }

    private static string BuildDisplayName(ActionTreeNode item, ShiftSoftware.TypeAuth.Core.Access access, bool anyDynamicRow)
    {
        var label = item.DisplayName ?? item.ID;
        if (anyDynamicRow)
            return $"{label} (any row) · {access}";
        return $"{label} · {access}";
    }

    internal static IEnumerable<ShiftSoftware.TypeAuth.Core.Access> GetPickerAccessLevels(ActionType type) => type switch
    {
        ActionType.ReadWriteDelete => new[] { ShiftSoftware.TypeAuth.Core.Access.Read, ShiftSoftware.TypeAuth.Core.Access.Write, ShiftSoftware.TypeAuth.Core.Access.Delete },
        ActionType.ReadWrite       => new[] { ShiftSoftware.TypeAuth.Core.Access.Read, ShiftSoftware.TypeAuth.Core.Access.Write },
        ActionType.Read            => new[] { ShiftSoftware.TypeAuth.Core.Access.Read },
        ActionType.Boolean         => new[] { ShiftSoftware.TypeAuth.Core.Access.Maximum },
        _                          => Array.Empty<ShiftSoftware.TypeAuth.Core.Access>(),
    };

    internal static MudBlazor.Color GetAccessColor(ShiftSoftware.TypeAuth.Core.Access access) => access switch
    {
        ShiftSoftware.TypeAuth.Core.Access.Delete  => MudBlazor.Color.Error,
        ShiftSoftware.TypeAuth.Core.Access.Write   => MudBlazor.Color.Warning,
        ShiftSoftware.TypeAuth.Core.Access.Read    => MudBlazor.Color.Success,
        ShiftSoftware.TypeAuth.Core.Access.Maximum => MudBlazor.Color.Primary,
        _ => MudBlazor.Color.Default,
    };

    internal static string GetAccessLabel(ShiftSoftware.TypeAuth.Core.Access access) => access switch
    {
        ShiftSoftware.TypeAuth.Core.Access.Delete  => "Delete",
        ShiftSoftware.TypeAuth.Core.Access.Write   => "Write",
        ShiftSoftware.TypeAuth.Core.Access.Read    => "Read",
        ShiftSoftware.TypeAuth.Core.Access.Maximum => "Access",
        _ => access.ToString(),
    };

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
        // An edited tree is posted as one tree, which several can't be written as.
        if (Trees is not null && !ReadOnly)
            throw new InvalidOperationException($"{nameof(Trees)} are shown read-only.");

        this.tAuth_LoggedInUser = (TypeAuth as TypeAuthContext)!;

        var typeAuthBuilder = new TypeAuthContextBuilder();

        await ExpandIdentityActions();

        if (ShiftIdentityDashboardBlazorOptions.DynamicTypeAuthActionExpander != null)
            await ShiftIdentityDashboardBlazorOptions.DynamicTypeAuthActionExpander.Invoke();

        foreach (var actionTree in this.tAuth_LoggedInUser!.GetRegisteredActionTrees())
            typeAuthBuilder.AddActionTree(actionTree);

        if (Trees is null)
            typeAuthBuilder.AddAccessTree(Tree);
        else
            foreach (var tree in Trees)
                typeAuthBuilder.AddAccessTree(tree);

        this.tAuth_View = typeAuthBuilder.Build();

        this.TraverseTree(tAuth_View.ActionTree, null);

        // The root isn't a row: its children, the registered action trees, always show.
        tAuth_View.ActionTree.Extra().Expanded = true;

        this.LoadLoggedInUserTree();

        // The TypeAuth service rebuilds the admin's rights on this event, ahead of this handler, which runs on the renderer.
        AuthenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;

        this.TreeItems = tAuth_View.ActionTree.Extra().Children;
        this.initialized = true;
    }

    private void LoadLoggedInUserTree()
    {
        var loggedInUserTreeItems = new List<ActionTreeNode>();
        this.TraverseTree(tAuth_LoggedInUser.ActionTree, null, loggedInUserTreeItems);
        this.LoggedInUserTreeItems_ByPath = loggedInUserTreeItems.ToLookup(x => x.Path);
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> state) => _ = InvokeAsync(() =>
    {
        this.LoadLoggedInUserTree();
        this.loggedInUserVersion++;
        StateHasChanged();
    });

    public void Dispose() => AuthenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;

    // A collapsed item's rows aren't rendered: MudBlazor renders the children of a collapsed item too, only hidden, and the
    // trees hold hundreds of rows once the data-level lists are expanded.
    private static bool IsShown(ActionTreeNode item) => item.Extra().Parent is not { } parent || parent.Extra().Expanded;

    void TraverseTree(ActionTreeNode tree, ActionTreeNode? parent, List<ActionTreeNode>? flattenedList = null)
    {
        flattenedList?.Add(tree);

        tree.AdditionalData = new AdditionalTreeItem
        {
            Expanded = false,
            Parent = parent,
            Children = tree.ActionTreeItems.Select(x => new TreeItemData(x)).ToArray(),
        };

        foreach (var item in tree.ActionTreeItems)
            TraverseTree(item, tree, flattenedList);
    }

    // The tree as it is, with what the admin left untouched beyond their reach: the server saves a tree only exactly as
    // submitted, so a tree reduced to the admin's reach would be refused for removing those grants.
    public string GenerateAccessTree()
    {
        return this.tAuth_View.GenerateAccessTree(this.tAuth_View);
    }

}
