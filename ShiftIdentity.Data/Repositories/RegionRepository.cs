﻿using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class RegionRepository : ShiftRepository<ShiftIdentityDbContext, Region, RegionListDTO, RegionDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    public RegionRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
    }

    public override ValueTask<Region> UpsertAsync(Region entity, RegionDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

        return base.UpsertAsync(entity, dto, actionType, userId);
    }

    public override ValueTask<Region> DeleteAsync(Region entity, bool isHardDelete = false, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId);
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.RegionFeatureIsLocked)
            throw new ShiftEntityException(new Message("Error", "Region Feature is locked"));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}
