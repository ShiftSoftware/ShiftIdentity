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
using System.Reflection;

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

        var resolved = ResolveAction(request.ActionPath);
        if (resolved is null)
            return NotFound(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
            {
                Message = new Message("Not Found", $"Action '{request.ActionPath}' was not found in any registered action tree.")
            });

        var (ownerType, action) = resolved.Value;
        var isDynamic = action is DynamicAction;

        if (isDynamic && !request.AnyDynamicRow && string.IsNullOrWhiteSpace(request.DynamicId))
            return BadRequest(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>
            {
                Message = new Message("Validation Error", "Dynamic actions require either DynamicId or AnyDynamicRow=true.")
            });

        // Registered action trees — same set every user context will be built against.
        var registeredActionTrees = typeAuthService.GetRegisteredActionTrees();

        // Load candidate users — active, not deleted. Include access trees.
        var users = await db.Users
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .Include(x => x.CompanyBranch)
            .Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
            .ToListAsync();

        var contextCache = new Dictionary<string, TypeAuthContext>(StringComparer.Ordinal);

        var matched = new List<ReverseTypeAuthLookupUserDTO>();
        var superAdminCount = 0;

        foreach (var user in users)
        {
            if (user.IsSuperAdmin)
            {
                superAdminCount++;
                matched.Add(MapUser(user, isSuperAdmin: true));
                continue;
            }

            var effectiveTrees = CollectEffectiveTrees(user);
            if (effectiveTrees.Count == 0)
                continue;

            var cacheKey = string.Join("\n", effectiveTrees.OrderBy(x => x, StringComparer.Ordinal));
            if (!contextCache.TryGetValue(cacheKey, out var context))
            {
                var builder = new TypeAuthContextBuilder();
                foreach (var tree in registeredActionTrees)
                    builder.AddActionTree(tree);
                foreach (var at in effectiveTrees)
                    builder.AddAccessTree(at);

                context = builder.Build();
                contextCache[cacheKey] = context;
            }

            if (HasAccess(context, action, isDynamic, request))
                matched.Add(MapUser(user, isSuperAdmin: false));
        }

        var displayName = BuildDisplayName(ownerType, action);

        return Ok(new ShiftEntityResponse<ReverseTypeAuthLookupResponseDTO>(new ReverseTypeAuthLookupResponseDTO
        {
            Users = matched,
            TotalMatchingUsers = matched.Count,
            SuperAdminCount = superAdminCount,
            ActionDisplayName = displayName
        }));
    }

    private static bool HasAccess(TypeAuthContext context, ActionBase action, bool isDynamic, ReverseTypeAuthLookupRequestDTO request)
    {
        if (!isDynamic)
            return context.Can(action, request.Access);

        var dynamicAction = (DynamicAction)action;

        if (request.AnyDynamicRow)
        {
            var (wildCard, ids) = context.GetAccessibleItems(dynamicAction, a => a == request.Access);
            return wildCard || ids.Count > 0;
        }

        return context.Can(action, request.Access, request.DynamicId!);
    }

    private static List<string> CollectEffectiveTrees(User user)
    {
        var trees = new List<string>();

        if (!string.IsNullOrWhiteSpace(user.AccessTree))
            trees.Add(user.AccessTree);

        if (user.AccessTrees != null)
            foreach (var accessTree in user.AccessTrees)
                if (!string.IsNullOrWhiteSpace(accessTree.AccessTree?.Tree))
                    trees.Add(accessTree.AccessTree.Tree);

        return trees;
    }

    private (Type ownerType, ActionBase action)? ResolveAction(string actionPath)
    {
        var segments = actionPath.Split('.');
        if (segments.Length < 2)
            return null;

        foreach (var rootType in typeAuthService.GetRegisteredActionTrees())
        {
            if (!string.Equals(rootType.Name, segments[0], StringComparison.Ordinal))
                continue;

            var currentType = rootType;
            for (int i = 1; i < segments.Length - 1; i++)
            {
                var nested = currentType.GetNestedType(segments[i]);
                if (nested is null)
                    return null;
                currentType = nested;
            }

            var fieldName = segments[^1];
            var field = currentType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            if (field?.GetValue(null) is ActionBase action)
                return (currentType, action);

            return null;
        }

        return null;
    }

    private static string BuildDisplayName(Type ownerType, ActionBase action)
    {
        var treeAttribute = ownerType.GetCustomAttribute<ShiftSoftware.TypeAuth.Core.ActionTree>();
        var treeName = treeAttribute?.Name ?? ownerType.Name;
        var actionName = string.IsNullOrWhiteSpace(action.Name) ? (action.Path?.Split('.').LastOrDefault() ?? "") : action.Name;
        return $"{treeName} › {actionName}";
    }

    private static ReverseTypeAuthLookupUserDTO MapUser(User user, bool isSuperAdmin)
        => new()
        {
            // Raw long as string — the [UserHashIdConverter] on the DTO encodes at serialization.
            ID = user.ID.ToString(),
            RawID = user.ID.ToString(),
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            CompanyBranch = user.CompanyBranch?.Name,
            IsSuperAdmin = isSuperAdmin,
            AccessTrees = user.AccessTrees?
                .Where(x => x.AccessTree != null)
                .Select(x => x.AccessTree.Name)
                .ToList() ?? new List<string>()
        };
}
