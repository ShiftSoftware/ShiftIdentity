using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Core.ValidatorsAndFormatters;
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
    /// Applies ordinary profile edits after refusing changes that need separate authority.
    /// </summary>
    /// <remarks>
    /// Unchanged legacy username and contact fields are accepted but never assigned. Username
    /// changes require an administrator; contact changes require their separate authentication
    /// flow. Verification, credentials, identity, and audit fields are never profile writes.
    /// </remarks>
    public static void ApplyProfileEdits(this UserDataDTO dto, User user) => ApplyProfileEdits(dto, user, null);

    public static void ApplyProfileEdits(this UserDataDTO dto, User user, ShiftIdentityLocalizer? localizer)
    {
        string Text(string value) => localizer is null ? value : localizer[value];

        if (!string.Equals(dto.Username?.Trim(), user.Username?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ShiftEntityException(new Message(Text("Validation Error"),
                Text("Only an administrator can change your username.")) { For = nameof(UserDataDTO.Username) });

        if (!SameEmail(dto.Email, user.Email) || !SamePhone(dto.Phone, user.Phone))
            throw new ShiftEntityException(new Message(Text("Additional authentication required"),
                Text("Use the contact change flow to change your email or phone.")) { For = "ContactReauthenticationRequired" });

        user.FullName = dto.FullName;
        user.BirthDate = dto.BirthDate;
        user.Signature = dto.Signature.ToJsonString();
    }

    private static string? ContactValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool SameEmail(string? requested, string? saved) =>
        string.Equals(ContactValue(requested), ContactValue(saved), StringComparison.OrdinalIgnoreCase);

    private static bool SamePhone(string? requested, string? saved)
    {
        requested = ContactValue(requested);
        saved = ContactValue(saved);
        // An unchanged historical value needs no rewriting or new ownership decision.
        if (string.Equals(requested, saved, StringComparison.Ordinal)) return true;
        return requested is not null && saved is not null &&
            PhoneNumber.PhoneIsValid(requested) && PhoneNumber.PhoneIsValid(saved) &&
            string.Equals(PhoneNumber.GetFormattedPhone(requested), PhoneNumber.GetFormattedPhone(saved), StringComparison.Ordinal);
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
