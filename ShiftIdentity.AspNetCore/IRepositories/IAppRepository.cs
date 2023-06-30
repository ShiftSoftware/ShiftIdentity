﻿using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.IRepositories
{
    public interface IAppRepository
    {
        Task<App?> GetAppAsync(string appId);
    }
}