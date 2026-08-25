using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Mappers;

/// <summary>
/// In-memory User -> DTO projections for the flows that map a User OUTSIDE the repository's
/// list/view pipeline: the bulk user endpoints and the self-service UserManager endpoints.
/// </summary>
/// <remarks>
/// These used to be <c>IMapper.Map&lt;T&gt;</c> calls against the maps in
/// <c>AutoMapperProfiles/User.cs</c>. The repository's own triple (User / UserListDTO / UserDTO) is
/// source-generated and never came through here; only these ad-hoc shapes did, which is why this
/// class exists rather than a generated mapper — <see cref="UserInfoDTO"/> and
/// <see cref="UserDataDTO"/> are not part of any repository triple.
/// <para>
/// The projections are hand-written and deliberately explicit: a convention map silently maps
/// whatever names happen to line up, and on the WRITE direction that included members no caller
/// ever intended to expose. See <see cref="ApplyProfileEdits"/>.
/// </para>
/// </remarks>
public static class UserProjections
{
    /// <summary>User -> UserListDTO, matching what the generated list projection produces.</summary>
    public static UserListDTO ToListDTO(this User user)
    {
        var dto = new UserListDTO();
        FillListFields(dto, user);
        return dto;
    }

    public static IEnumerable<UserListDTO> ToListDTOs(this IEnumerable<User> users)
        => users.Select(ToListDTO).ToList();

    /// <summary>
    /// User -> UserInfoDTO (UserListDTO + BirthDate). <c>PlainTextPassword</c> is intentionally not
    /// set here — only the caller that generated the password knows it.
    /// </summary>
    public static UserInfoDTO ToInfoDTO(this User user)
    {
        var dto = new UserInfoDTO { BirthDate = user.BirthDate };
        FillListFields(dto, user);
        return dto;
    }

    public static IEnumerable<UserInfoDTO> ToInfoDTOs(this IEnumerable<User> users)
        => users.Select(ToInfoDTO).ToList();

    /// <summary>User -> UserDataDTO, the self-service "my profile" shape.</summary>
    public static UserDataDTO ToDataDTO(this User user)
    {
        return new UserDataDTO
        {
            Username = user.Username,
            Email = user.Email,
            Phone = user.Phone,
            FullName = user.FullName,
            BirthDate = user.BirthDate,
            EmailVerified = user.EmailVerified,
            PhoneVerified = user.PhoneVerified,
            Signature = user.Signature.ToShiftFiles(),
        }.MapBaseFields(user);
    }

    /// <summary>
    /// Applies the editable part of a self-service profile PUT onto the tracked user.
    /// </summary>
    /// <remarks>
    /// Only the fields a user may change about themselves are written. The convention map this
    /// replaced wrote every name that lined up, which on this path meant a caller-supplied
    /// <c>IsDeleted</c>, the audit fields, the primary key, and — the one with teeth —
    /// <c>EmailVerified</c> / <c>PhoneVerified</c>, letting the account assert its own
    /// verification state and skip the SAS-token flow that is supposed to establish it.
    /// Those members are owned by the repository, the audit pipeline, and the verification
    /// endpoints respectively; none of them is profile data, so none of them is written here.
    /// <para>
    /// <c>Phone</c> is assigned by the caller after this returns, from the formatted/validated
    /// value — it is written here too so the mapping stays complete on its own terms.
    /// </para>
    /// </remarks>
    public static void ApplyProfileEdits(this UserDataDTO dto, User user)
    {
        user.Username = dto.Username;
        user.Email = dto.Email;
        user.Phone = dto.Phone;
        user.FullName = dto.FullName;
        user.BirthDate = dto.BirthDate;
        user.Signature = dto.Signature.ToJsonString();
    }

    private static void FillListFields(UserListDTO dto, User user)
    {
        dto.CompanyBranch = user.CompanyBranch?.Name;
        dto.CompanyBranchID = user.CompanyBranchID?.ToString();
        dto.CompanyID = user.CompanyID?.ToString();
        dto.FullName = user.FullName;
        dto.Username = user.Username;
        dto.IntegrationId = user.IntegrationId;
        dto.Phone = user.Phone;
        dto.PhoneVerified = user.PhoneVerified;
        dto.Email = user.Email;
        dto.EmailVerified = user.EmailVerified;
        dto.IsActive = user.IsActive;
        dto.TotpEnabled = user.TotpSecret != null;

        // UserLog is the authoritative LastSeen when it has one; the column on User is the fallback.
        dto.LastSeen = (user.UserLog?.LastSeen ?? user.LastSeen) ?? default;

        // Null when the navigation was not included — an empty list, never a NullReferenceException.
        dto.AccessTrees = user.AccessTrees?
            .Select(y => new ShiftEntitySelectDTO
            {
                Value = y.AccessTreeID.ToString(),
                Text = y.AccessTree?.Name,
            })
            .ToList() ?? new List<ShiftEntitySelectDTO>();

        dto.MapBaseListFields(user);
    }
}
