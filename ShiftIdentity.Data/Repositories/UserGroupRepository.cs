using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserGroup;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class UserGroupRepository : ShiftRepository<ShiftIdentityDbContext, UserGroup, UserGroupListDTO, UserGroupDTO>
{
    public UserGroupRepository(ShiftIdentityDbContext db) : 
        base(db, x=> x.IncludeRelatedEntitiesWithFindAsync(s=> s.Include(i=> i.UserGroupUsers).ThenInclude(i=> i.User)))
    {
    }

    public override async ValueTask<UserGroup> UpsertAsync(UserGroup entity, UserGroupDTO dto, ActionTypes actionType, long? userId = null)
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
        var deletedUsers = entity.UserGroupUsers.Where(x => !dto.Users.Select(s => s.Value.ToLong())
            .Any(s => s == x.UserID)).ToList();
        var addedUsers = dto.Users.Where(x => !entity.UserGroupUsers.Select(s => s.UserID)
            .Any(s => s == x.Value.ToLong())).ToList();

        db.UserGroupUsers.RemoveRange(deletedUsers);
        await db.UserGroupUsers.AddRangeAsync(addedUsers.Select(x => new UserGroupUser
        {
            UserID = x.Value.ToLong(),
            UserGroup = entity
        }).ToList());

        //ef core may not set the entity state as Modified if the only the collections are changed (CompanyBranchDepartments, CompanyBranchServices)
        db.Entry(entity).State = EntityState.Modified;

        return entity;
    }
}
