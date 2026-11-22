using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.ReverseTypeAuthLookup;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.TypeAuth.AspNetCore;
using ShiftSoftware.TypeAuth.AspNetCore.EndpointFilters;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

// The reverse-TypeAuth-lookup endpoint ("which users/access-trees grant this action?"), ported verbatim from
// ReverseTypeAuthLookupController (route + verb byte-identical: GET api/ReverseTypeAuthLookup). The controller's
// class-level [Authorize] + [TypeAuth<ShiftIdentityActions>(Users, Read)] becomes .RequireTypeAuthRead(Users); the
// body still AND-gates AccessTrees Read. Composed into the dashboard by MapShiftIdentityDashboard().
internal static class ReverseTypeAuthLookupEndpoints
{
    public static IEndpointRouteBuilder MapReverseTypeAuthLookupEndpoints(this IEndpointRouteBuilder app)
    {
        // GET api/ReverseTypeAuthLookup — Users Read (filter) AND-gated with AccessTrees Read (body).
        // The request DTO binds from the query string ([AsParameters] mirrors the controller's [FromQuery]).
        app.MapGet("api/ReverseTypeAuthLookup",
            async ([AsParameters] ReverseTypeAuthLookupRequestDTO request,
                   ShiftIdentityDbContext db,
                   ITypeAuthService typeAuthService) =>
            {
                // AND-gate against AccessTrees Read (the RequireTypeAuthRead filter handles Users Read).
                if (!typeAuthService.CanRead(ShiftIdentityActions.AccessTrees))
                    return Results.Json(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
                    {
                        Message = new Message("Forbidden", "Read access to both Users and Access Trees is required.")
                    }, statusCode: (int)HttpStatusCode.Forbidden);

                if (string.IsNullOrWhiteSpace(request.ActionPath))
                    return Results.BadRequest(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
                    {
                        Message = new Message("Validation Error", "ActionPath is required.")
                    });

                // Resolve the action from its path via TypeAuth's own action-tree index (the same index it
                // evaluates against) instead of re-walking the action tree types by reflection here.
                var actionNode = typeAuthService.FindActionTreeNode(request.ActionPath);
                if (actionNode?.Action is null)
                    return Results.NotFound(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
                    {
                        Message = new Message("Not Found", $"Action '{request.ActionPath}' was not found in any registered action tree.")
                    });

                var action = actionNode.Action;
                var isDynamic = action is DynamicAction;

                if (isDynamic && !request.AnyDynamicRow && string.IsNullOrWhiteSpace(request.DynamicId))
                    return Results.BadRequest(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
                    {
                        Message = new Message("Validation Error", "Dynamic actions require either DynamicId or AnyDynamicRow=true.")
                    });

                // Text and number actions are searched by a value: who holds at least it, by the action's own
                // combine rule (the greatest number wins; a list covers every entry of the value).
                var isText = action.Type == ActionType.Text;

                if (isText)
                {
                    request.Value = request.Value?.Trim();

                    if (string.IsNullOrEmpty(request.Value))
                        return ValidationError("Text and number actions require a Value.");

                    if (isDynamic && request.AnyDynamicRow)
                        return ValidationError("A dynamic text or number action is searched one row at a time: give a DynamicId.");

                    if (action is DecimalAction or DynamicDecimalAction
                        && !decimal.TryParse(request.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                        return ValidationError($"'{request.Value}' is not a number.");
                }

                // Registered action trees — the action-tree set every single-tree context is evaluated against.
                var registeredActionTrees = typeAuthService.GetRegisteredActionTrees();

                // Builds a context holding the registered action trees plus the given access trees. Access is purely
                // additive (merging trees only ever adds grants), so a subject is granted the action iff at least one of
                // their trees grants it — evaluating each tree in isolation is exact. The one exception is a text action
                // whose rule is a list: two trees together can cover a value neither covers alone, so a user is also
                // checked on all their trees at once (see the Users tab below).
                TypeAuthContext BuildContext(params string[] treeJsons)
                {
                    var builder = new TypeAuthContextBuilder();
                    foreach (var t in registeredActionTrees)
                        builder.AddActionTree(t);
                    foreach (var treeJson in treeJsons)
                        builder.AddAccessTree(treeJson);
                    return builder.Build();
                }

                if (isText)
                {
                    // A value no greater than the action's minimum is held by everyone, access or not: refuse it rather
                    // than list every user. Evaluating it here also refuses a value the action's rule can't read.
                    try
                    {
                        if (HasAccess(BuildContext("{}"), action, isDynamic, request))
                            return ValidationError($"Every user holds '{request.Value}': it is not more than the action's minimum.");
                    }
                    catch (FormatException)
                    {
                        return ValidationError($"'{request.Value}' is not a value this action accepts.");
                    }
                }

                // Load candidate users — active, not deleted. Include their named access trees.
                var users = await db.Users
                    .AsNoTracking()
                    .Where(x => !x.IsDeleted && x.IsActive)
                    .Include(x => x.CompanyBranch)
                    .Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
                    .ToListAsync();

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
                // is no separate combined-context pass and no source is evaluated twice. A text action is the
                // exception: when no source covers the value alone, the user's trees are checked together, and the
                // sources that hold any value of the action are the ones credited.
                var matched = new List<ReverseTypeAuthLookupUserDTO>();

                foreach (var user in users)
                {
                    var namedSources = (user.AccessTrees ?? [])
                        .Where(uat => uat.AccessTree != null && !string.IsNullOrWhiteSpace(uat.AccessTree.Tree))
                        .Select(uat => (uat.AccessTree.Name, Context: NamedTreeContext(uat.AccessTreeID, uat.AccessTree.Tree)))
                        .ToList();
                    var directContext = string.IsNullOrWhiteSpace(user.AccessTree) ? null : BuildContext(user.AccessTree);

                    var grantingAccessTrees = namedSources
                        .Where(x => HasAccess(x.Context, action, isDynamic, request))
                        .Select(x => x.Name)
                        .ToList();

                    var grantedDirectly = directContext != null && HasAccess(directContext, action, isDynamic, request);

                    if (!grantedDirectly && grantingAccessTrees.Count == 0 && isText && namedSources.Count + (directContext is null ? 0 : 1) > 1)
                    {
                        var allTrees = (user.AccessTrees ?? [])
                            .Where(uat => uat.AccessTree != null && !string.IsNullOrWhiteSpace(uat.AccessTree.Tree))
                            .Select(uat => uat.AccessTree.Tree)
                            .Append(user.AccessTree)
                            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)
                            .ToArray();

                        if (HasAccess(BuildContext(allTrees), action, isDynamic, request))
                        {
                            grantingAccessTrees = namedSources.Where(x => HoldsAValue(x.Context, action, request)).Select(x => x.Name).ToList();
                            grantedDirectly = directContext != null && HoldsAValue(directContext, action, request);
                        }
                    }

                    if (!grantedDirectly && grantingAccessTrees.Count == 0)
                        continue;

                    matched.Add(MapUser(user, grantingAccessTrees, grantedDirectly));
                }

                var displayName = BuildDisplayName(typeAuthService, actionNode);

                return Results.Ok(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>(new ReverseTypeAuthLookupResponseDTO
                {
                    Users = matched,
                    TotalMatchingUsers = matched.Count,
                    ActionDisplayName = displayName,
                    AccessTrees = matchedTrees,
                    TotalMatchingAccessTrees = matchedTrees.Count
                }));
            })
            .RequireTypeAuthRead(ShiftIdentityActions.Users);

        return app;
    }

    private static IResult ValidationError(string body) =>
        Results.BadRequest(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO> { Message = new Message("Validation Error", body) });

    private static bool HasAccess(TypeAuthContext context, ActionBase action, bool isDynamic, ReverseTypeAuthLookupRequestDTO request)
    {
        // A text or number action: the tree's value covers the value searched for, by the action's combine rule.
        if (action is TextAction text)
            return context.Covers(text, request.Value);

        if (action is DynamicTextAction dynamicText)
            return context.Covers(dynamicText, request.Value, request.DynamicId);

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

    // Whether a tree holds any value of a text action beyond its minimum (it contributes to a combined value).
    private static bool HoldsAValue(TypeAuthContext context, ActionBase action, ReverseTypeAuthLookupRequestDTO request)
    {
        var (held, minimum) = action switch
        {
            TextAction text => (context.AccessValue(text), text.MinimumAccess),
            DynamicTextAction dynamicText => (context.AccessValue(dynamicText, request.DynamicId), dynamicText.MinimumAccess),
            _ => (null, null),
        };

        return !string.IsNullOrEmpty(held) && held != minimum;
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
    private static string BuildDisplayName(ITypeAuthService typeAuthService, ActionTreeNode actionNode)
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
