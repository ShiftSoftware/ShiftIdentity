
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class AccessTree : Profile
{
    public AccessTree()
    {
        CreateMap<Core.Entities.AccessTree, AccessTreeDTO>();
    }
}
