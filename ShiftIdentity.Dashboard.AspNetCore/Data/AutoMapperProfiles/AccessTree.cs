
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class AccessTree : Profile
{
    public AccessTree()
    {
        CreateMap<ShiftIdentity.AspNetCore.Entities.AccessTree, AccessTreeDTO>();
    }
}
