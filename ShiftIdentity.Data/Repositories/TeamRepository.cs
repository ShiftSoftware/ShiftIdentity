using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class TeamRepository : ShiftRepository<ShiftIdentityDbContext, Team, TeamListDTO, TeamDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;
    public TeamRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc) :
        base(db, x => x.IncludeRelatedEntitiesWithFindAsync(
            s => s.Include(i => i.TeamUsers).ThenInclude(i => i.User),
            s => s.Include(i => i.TeamCompanyBranches).ThenInclude(i => i.CompanyBranch),
            s => s.Include(i => i.Company))
    )
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
    }

    public override async ValueTask<Team> UpsertAsync(Team entity, TeamDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null)
    {
        //Check there are any duplicate users
        if (dto.Users.GroupBy(item => item.Value).Any(group => group.Count() > 1))
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Duplicate users are not allowed."]));

        if (actionType == ActionTypes.Insert)
            return await base.UpsertAsync(entity, dto, actionType, userId);

        //entity.Update(userId);
        entity.Name = dto.Name;
        entity.CompanyID = dto.Company.Value.ToLong();
        entity.IntegrationId = dto.IntegrationId;

        if (dto.Tags is not null)
            entity.Tags = dto.Tags.ToList();
        else
            entity.Tags = new List<string>();

            var deletedUsers = entity.TeamUsers.Where(x => !dto.Users.Select(s => s.Value.ToLong())
                .Any(s => s == x.UserID)).ToList();
        var addedUsers = dto.Users.Where(x => !entity.TeamUsers.Select(s => s.UserID)
            .Any(s => s == x.Value.ToLong())).ToList();

        db.TeamUsers.RemoveRange(deletedUsers);

        await db.TeamUsers.AddRangeAsync(addedUsers.Select(x => new TeamUser
        {
            UserID = x.Value.ToLong(),
            Team = entity
        }).ToList());


        var deletedBranches = entity.TeamCompanyBranches.Where(x => !dto.CompanyBranches.Select(s => s.Value.ToLong())
            .Any(s => s == x.CompanyBranchID)).ToList();
        var addedBranches = dto.CompanyBranches.Where(x => !entity.TeamCompanyBranches.Select(s => s.CompanyBranchID)
            .Any(s => s == x.Value.ToLong())).ToList();

        db.TeamCompanyBranches.RemoveRange(deletedBranches);

        await db.TeamCompanyBranches.AddRangeAsync(addedBranches.Select(x => new TeamCompanyBranch
        {
            CompanyBranchID = x.Value.ToLong(),
            Team = entity
        }).ToList());

        //ef core may not set the entity state as Modified if the only the collections are changed (CompanyBranchDepartments, CompanyBranchServices)
        db.Entry(entity).State = EntityState.Modified;

        return entity;
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.TeamFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Team Feature is locked"]));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}