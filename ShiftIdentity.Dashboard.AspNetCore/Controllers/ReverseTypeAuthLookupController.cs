using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.ReverseTypeAuthLookup;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.TypeAuth.AspNetCore;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
[Authorize]
[TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), Access.Read)]
public class ReverseTypeAuthLookupController : ControllerBase
{
    private readonly ShiftIdentityDbContext db;
    private readonly ITypeAuthService typeAuthService;

    public ReverseTypeAuthLookupController(ShiftIdentityDbContext db, ITypeAuthService typeAuthService)
    {
        this.db = db;
        this.typeAuthService = typeAuthService;
    }

    [HttpGet]
    public async Task<IActionResult> Lookup([FromQuery] ReverseTypeAuthLookupRequestDTO request)
    {
        // AND-gate against AccessTrees Read (attribute handles Users Read).
        if (!typeAuthService.CanRead(ShiftIdentityActions.AccessTrees))
            return StatusCode((int)HttpStatusCode.Forbidden, new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
            {
                Message = new Message("Forbidden", "Read access to both Users and Access Trees is required.")
            });

        if (string.IsNullOrWhiteSpace(request.ActionPath))
            return BadRequest(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
            {
                Message = new Message("Validation Error", "ActionPath is required.")
            });

        // Resolve the action from its path via TypeAuth's own action-tree index (the same index it
        // evaluates against) instead of re-walking the action tree types by reflection here.
        var actionNode = typeAuthService.FindActionTreeNode(request.ActionPath);
        if (actionNode?.Action is null)
            return NotFound(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
            {
                Message = new Message("Not Found", $"Action '{request.ActionPath}' was not found in any registered action tree.")
            });

        var action = actionNode.Action;
        var isDynamic = action is DynamicAction;

        if (isDynamic && !request.AnyDynamicRow && string.IsNullOrWhiteSpace(request.DynamicId))
            return BadRequest(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
            {
                Message = new Message("Validation Error", "Dynamic actions require either DynamicId or AnyDynamicRow=true.")
            });

        // Registered action trees — the action-tree set every single-tree context is evaluated against.
        var registeredActionTrees = typeAuthService.GetRegisteredActionTrees();

        // Load candidate users — active, not deleted. Include their named access trees.
        var users = await db.Users
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .Include(x => x.CompanyBranch)
            .Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
            .ToListAsync();

        // Builds a context holding the registered action trees plus a single access tree, so one tree
        // can be evaluated on its own. Access is purely additive (merging trees only ever adds grants),
        // so a subject is granted the action iff at least one of their trees grants it — evaluating each
        // tree in isolation is exact, and there is never a need to build a combined per-subject context.
        TypeAuthContext BuildContext(string treeJson)
        {
            var builder = new TypeAuthContextBuilder();
            foreach (var t in registeredActionTrees)
                builder.AddActionTree(t);
            builder.AddAccessTree(treeJson);
            return builder.Build();
        }

        // Named access trees are shared across many users, so their contexts are cached by the tree's
        // (cheap-to-hash) id. A user's inline "direct" tree is unique per user with no reuse, so it is
        // built ad hoc rather than cached.
        var namedTreeContexts = new Dictionary<long, TypeAuthContext>();
        TypeAuthContext NamedTreeContext(long accessTreeId, string treeJson)
        {
            if (!namedTreeContexts.TryGetValue(accessTreeId, out var ctx))
                namedTreeContexts[accessTreeId] = ctx = BuildContext(treeJson);
            return ctx;
        }

        // Active-user assignment count per named access tree (shown in the Access Trees tab).
        var activeUserCountByAccessTreeId = users
            .Where(u => u.AccessTrees != null)
            .SelectMany(u => u.AccessTrees)
            .Where(uat => uat.AccessTree != null)
            .GroupBy(uat => uat.AccessTreeID)
            .ToDictionary(g => g.Key, g => g.Count());

        // Access Trees tab: every named tree that grants the access, queried independently of users.
        // Intentionally NOT derived from the loaded users — a tree can grant the access while assigned to
        // zero active users, and a reverse-lookup/audit still needs to surface it (AssignedUserCount 0).
        var matchedTrees = new List<ReverseTypeAuthLookupAccessTreeDTO>();
        var accessTrees = await db.AccessTrees
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .ToListAsync();

        foreach (var tree in accessTrees)
        {
            if (string.IsNullOrWhiteSpace(tree.Tree))
                continue;

            if (HasAccess(NamedTreeContext(tree.ID, tree.Tree), action, isDynamic, request))
            {
                matchedTrees.Add(new ReverseTypeAuthLookupAccessTreeDTO
                {
                    // Raw long as string — the [AccessTreeHashIdConverter] on the DTO encodes at serialization.
                    ID = tree.ID.ToString(),
                    Name = tree.Name,
                    AssignedUserCount = activeUserCountByAccessTreeId.GetValueOrDefault(tree.ID)
                });
            }
        }

        // Users tab: a user matches iff any of their access-tree sources grants the action. Because access
        // is additive, evaluating each source on its own both decides the match and attributes it — the
        // direct tree feeds GrantedDirectly, each granting named tree feeds GrantingAccessTrees — so there
        // is no separate combined-context pass and no source is evaluated twice.
        var matched = new List<ReverseTypeAuthLookupUserDTO>();

        foreach (var user in users)
        {
            var grantingAccessTrees = new List<string>();
            if (user.AccessTrees != null)
                foreach (var uat in user.AccessTrees)
                    if (uat.AccessTree != null && !string.IsNullOrWhiteSpace(uat.AccessTree.Tree)
                        && HasAccess(NamedTreeContext(uat.AccessTreeID, uat.AccessTree.Tree), action, isDynamic, request))
                        grantingAccessTrees.Add(uat.AccessTree.Name);

            var grantedDirectly = !string.IsNullOrWhiteSpace(user.AccessTree)
                && HasAccess(BuildContext(user.AccessTree), action, isDynamic, request);

            if (!grantedDirectly && grantingAccessTrees.Count == 0)
                continue;

            matched.Add(MapUser(user, grantingAccessTrees, grantedDirectly));
        }

        var displayName = BuildDisplayName(actionNode);

        return Ok(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>(new ReverseTypeAuthLookupResponseDTO
        {
            Users = matched,
            TotalMatchingUsers = matched.Count,
            ActionDisplayName = displayName,
            AccessTrees = matchedTrees,
            TotalMatchingAccessTrees = matchedTrees.Count
        }));
    }

    private static bool HasAccess(TypeAuthContext context, ActionBase action, bool isDynamic, ReverseTypeAuthLookupRequestDTO request)
    {
        if (!isDynamic)
            return context.Can(action, request.Access);

        var dynamicAction = (DynamicAction)action;

        if (request.AnyDynamicRow)
        {
            var accessible = context.GetAccessibleItems(dynamicAction, a => a == request.Access);
            return accessible.WildCard || accessible.AccessibleIds.Count > 0;
        }

        return context.Can(action, request.Access, request.DynamicId!);
    }

    private static ReverseTypeAuthLookupUserDTO MapUser(User user, List<string> grantingAccessTrees, bool grantedDirectly)
        => new()
        {
            // Raw long as string — the [UserHashIdConverter] on the DTO encodes at serialization.
            ID = user.ID.ToString(),
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            CompanyBranch = user.CompanyBranch?.Name,
            AccessTrees = user.AccessTrees?
                .Where(x => x.AccessTree != null)
                .Select(x => x.AccessTree.Name)
                .ToList() ?? new List<string>(),
            GrantingAccessTrees = grantingAccessTrees,
            GrantedDirectly = grantedDirectly
        };

    // Friendly "Tree › Action" name (e.g. "Identity › Users"), read from the action tree nodes rather
    // than re-derived by reflection: the parent grouping node carries the tree name, the action node the
    // action name.
    private string BuildDisplayName(ActionTreeNode actionNode)
    {
        var path = actionNode.Path ?? "";
        var lastDot = path.LastIndexOf('.');
        var parentNode = lastDot > 0 ? typeAuthService.FindActionTreeNode(path.Substring(0, lastDot)) : null;

        var treeName = parentNode?.DisplayName ?? parentNode?.ID ?? "";
        var actionName = string.IsNullOrWhiteSpace(actionNode.DisplayName)
            ? (path.Split('.').LastOrDefault() ?? "")
            : actionNode.DisplayName;

        return $"{treeName} › {actionName}";
    }
}
