using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class TeamRepository : ShiftRepository<ShiftIdentityDbContext, Team, TeamListDTO, TeamDTO>
{
    public TeamRepository(ShiftIdentityDbContext db) : 
        base(db, x=> x.IncludeRelatedEntitiesWithFindAsync(s=> s.Include(i=> i.TeamUsers).ThenInclude(i=> i.User)))
    {
    }

    public override async ValueTask<Team> UpsertAsync(Team entity, TeamDTO dto, ActionTypes actionType, long? userId = null)
    {
        //Check there are any duplicate users
        if(dto.Users.GroupBy(item => item.Value).Any(group => group.Count() > 1))
            throw new ShiftEntityException(new Message("Error", "Duplicate users are not allowed."));

        if(actionType == ActionTypes.Insert)
            return await base.UpsertAsync(entity, dto, actionType, userId);

        entity.Update(userId);
        entity.Name = dto.Name;
        entity.IntegrationId = dto.IntegrationId;

        //Update departments
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

        //ef core may not set the entity state as Modified if the only the collections are changed (CompanyBranchDepartments, CompanyBranchServices)
        db.Entry(entity).State = EntityState.Modified;

        return entity;
    }
}
