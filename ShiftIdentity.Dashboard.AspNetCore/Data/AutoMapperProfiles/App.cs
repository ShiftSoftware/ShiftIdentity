
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class App : Profile
{
	public App()
	{
        CreateMap<Core.Entities.App, AppDTO>().ReverseMap();
    }
}
