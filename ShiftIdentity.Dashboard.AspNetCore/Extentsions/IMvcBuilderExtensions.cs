using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftEntity.Web.Extensions;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftEntity.Web;

namespace ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class IMvcBuilderExtensions
{
    public static IMvcBuilder AddShiftIdentityDashoard(this IMvcBuilder builder, ShiftIdentityConfiguration configuration)
    {
        if (configuration.ActionTrees is null)
            configuration.ActionTrees = new List<Type>() { typeof(ShiftIdentityActions) };

        builder.AddShiftEntity(o =>
        {
            o.WrapValidationErrorResponseWithShiftEntityResponse(true);
            o.ODatat.DefaultOptions();
            var b = o.ODatat.ODataConvention;
            b.EntitySet<AppDTO>("App");
            b.EntitySet<AccessTreeDTO>("AccessTree");
            b.EntitySet<UserListDTO>("User");
            b.EntitySet<PublicUserListDTO>("PublicUser");

            o.HashId.RegisterHashId(configuration.HashIdSettings.AcceptUnencodedIds);
            o.HashId.RegisterUserIdsHasher(configuration.HashIdSettings.UserIdsSalt,
                configuration.HashIdSettings.UserIdsMinHashLength,
                configuration.HashIdSettings.UserIdsAlphabet);
        });

        return builder;
    }
}
