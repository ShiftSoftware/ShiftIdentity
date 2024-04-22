using System.Collections.Generic;
using System;
using ShiftSoftware.ShiftIdentity.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.AspNetCore;

public class ShiftIdentityConfiguration
{
    public SecuritySettingsModel Security { get; set; } = default!;

    public TokenSettingsModel Token { get; set; } = default!;

    public TokenSettingsModel RefreshToken { get; set; } = default!;

    public HashIdSettings HashIdSettings { get; set; } = default!;

    public List<Type> ActionTrees { get; set; } = default!;

    public SASTokenModel SASToken { get; set; } = default!;

    internal bool IsFakeIdentity { get; set; }
    public ShiftIdentityHostingTypes ShiftIdentityHostingType { get; set; }
    public string? FrontEndUrl { get; set; } = default!;
}
