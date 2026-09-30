using System;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftwareLocalization.Identity;
using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.App;

public class AppDTO : ShiftEntityMixedDTO
{
    [JsonHashIdConverter<AppDTO>(5)]
    public override string? ID { get; set; }
    public string DisplayName { get; set; } = default!;

    public string AppId { get; set; } = default!;

    //[MaxLength(255)]
    public string? AppSecret { get; set; }

    public string? Description { get; set; }

    public string RedirectUri { get; set; } = default!;

    //[Required]
    //[MaxLength(4000)]
    //public string PostLogoutRedirectUri { get; set; } = default!;
}

public class AppValidator : AbstractValidator<AppDTO>
{
    /// <param name="registeredRedirectUri">The RedirectUri the row already has, or null for a new app. The App form
    /// passes it so that an existing row can be saved unchanged.</param>
    public AppValidator(ShiftIdentityLocalizer localizer, Func<string?>? registeredRedirectUri = null)
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage(localizer["Please enter a name"])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.AppId)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["App Id"]])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.Description)
            .MaximumLength(4000).WithMessage(localizer["Your input cannot be more than 4000 characters"]);

        RuleFor(x => x.RedirectUri)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Redirect URI"]])
            .MaximumLength(4000).WithMessage(localizer["Your input cannot be more than 4000 characters"]);

        // The sign-in page appends "/Auth/Token", so a value ending in "/" or in that route is a typo. The page trims a
        // trailing slash itself, so a row that already has one keeps working. Only a new or changed value is refused:
        // the app-code binding hashes the stored RedirectUri, and forcing a fix would end the app's live sessions.
        RuleFor(x => x.RedirectUri)
            .Must(x => !x.TrimEnd().EndsWith('/') && !x.TrimEnd().EndsWith("/Auth/Token", StringComparison.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrEmpty(x.RedirectUri) && x.RedirectUri != registeredRedirectUri?.Invoke())
            .WithMessage(localizer["The Redirect URI cannot end with / or /Auth/Token"]);
    }
}
