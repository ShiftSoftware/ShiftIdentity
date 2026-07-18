using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung B): Team has no controller and no repository class. The Include config + the
// non-convention mapper members move onto the entity via IConfiguresShiftRepository, and the duplicate-user
// validation + M:N TeamUsers/TeamCompanyBranches sync via IUpsertsShiftRepository. Feature locking is central.
[TemporalShiftEntity]
[Table("Teams", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<TeamListDTO, TeamDTO, ShiftIdentityActions>("api/IdentityTeam", nameof(ShiftIdentityActions.Teams), UseGeneratedMapper = true)]
public class Team : ShiftEntity<Team>, IEntityHasCompany<Team>, IEntityHasTeam<Team>, IShiftEntityReplication,
    IConfiguresShiftRepository<Team, TeamListDTO, TeamDTO>,
    IUpsertsShiftRepository<Team, TeamListDTO, TeamDTO>
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }

    public virtual ICollection<TeamUser> TeamUsers { get; set; } = new HashSet<TeamUser>();
    public long? CompanyID { get; set; }

    public virtual Company? Company { get; set; } = default!;
    public virtual ICollection<TeamCompanyBranch> TeamCompanyBranches { get; set; } = new HashSet<TeamCompanyBranch>();
    public List<string> Tags { get; set; } = new();

    public long? TeamID { get; set; }

    public Team()
    {
        TeamUsers = new HashSet<TeamUser>();
        TeamCompanyBranches = new HashSet<TeamCompanyBranch>();
    }

    public void ConfigureRepository(ShiftRepositoryConfigurationContext<Team, TeamListDTO, TeamDTO> context)
    {
        context.Options.IncludeRelatedEntitiesWithFindAsync(
            s => s.Include(i => i.TeamUsers).ThenInclude(i => i.User),
            s => s.Include(i => i.TeamCompanyBranches).ThenInclude(i => i.CompanyBranch),
            s => s.Include(i => i.Company));

        context.Options.UseGeneratedMapper(map => map
            // VIEW — M:N join → List<ShiftEntitySelectDTO>. Not convention: DTO names (Users/CompanyBranches)
            // don't match the join navigations, and Text reaches through .User/.CompanyBranch.
            .ForView(d => d.Users, e => e.TeamUsers
                .Select(y => new ShiftEntitySelectDTO { Value = y.UserID.ToString(), Text = y.User.Username }).ToList())
            .ForView(d => d.CompanyBranches, e => e.TeamCompanyBranches
                .Select(y => new ShiftEntitySelectDTO { Value = y.CompanyBranchID.ToString(), Text = y.CompanyBranch.Name }).ToList())
            // ENTITY — Tags (List<string>); the write side has no implicit conversion, so it isn't convention-covered.
            .ForEntity(e => e.Tags, dto => dto.Tags != null ? dto.Tags.ToList() : new List<string>())
            // LIST — flattened Company name (through nav) + CompanyId (case-mismatch to entity CompanyID).
            .ForList(d => d.Company, e => e.Company != null ? e.Company.Name : null)
            .ForList(d => d.CompanyId, e => e.CompanyID.HasValue ? e.CompanyID.Value.ToString() : null));
    }

    public async ValueTask<Team> UpsertAsync(
        Team entity,
        TeamDTO dto,
        ActionTypes actionType,
        long? userId,
        Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<Team, TeamListDTO, TeamDTO> context)
    {
        var db = context.Services.GetRequiredService<ShiftIdentityDbContext>();
        var loc = context.Services.GetRequiredService<ShiftIdentityLocalizer>();

        // Duplicate-user validation — BEFORE Base() (fail fast; both insert & update).
        if (dto.Users.GroupBy(item => item.Value).Any(group => group.Count() > 1))
            throw new ShiftEntityException(new Message(loc["Error"], loc["Duplicate users are not allowed."]));

        // Base() maps scalars (Name, CompanyID from Company FK, IntegrationId, Tags via ForEntity), audit-stamps,
        // and runs the company-scoped data-level write check. It leaves the M:N join navigations untouched.
        var saved = await context.Base();

        // M:N differential sync — works for insert too (empty existing ⇒ pure adds).
        var deletedUsers = saved.TeamUsers.Where(x => !dto.Users.Select(s => s.Value.ToLong())
            .Any(s => s == x.UserID)).ToList();
        var addedUsers = dto.Users.Where(x => !saved.TeamUsers.Select(s => s.UserID)
            .Any(s => s == x.Value.ToLong())).ToList();
        db.TeamUsers.RemoveRange(deletedUsers);
        await db.TeamUsers.AddRangeAsync(addedUsers.Select(x => new TeamUser
        {
            UserID = x.Value.ToLong(),
            Team = saved
        }).ToList());

        var deletedBranches = saved.TeamCompanyBranches.Where(x => !dto.CompanyBranches.Select(s => s.Value.ToLong())
            .Any(s => s == x.CompanyBranchID)).ToList();
        var addedBranches = dto.CompanyBranches.Where(x => !saved.TeamCompanyBranches.Select(s => s.CompanyBranchID)
            .Any(s => s == x.Value.ToLong())).ToList();
        db.TeamCompanyBranches.RemoveRange(deletedBranches);
        await db.TeamCompanyBranches.AddRangeAsync(addedBranches.Select(x => new TeamCompanyBranch
        {
            CompanyBranchID = x.Value.ToLong(),
            Team = saved
        }).ToList());

        // EF may not mark the parent Modified when only the child collections change (update only; on insert the
        // entity is already Added and forcing Modified would break it).
        if (actionType == ActionTypes.Update)
            db.Entry(saved).State = EntityState.Modified;

        return saved;
    }
}
