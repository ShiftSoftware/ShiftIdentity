using System;
using System.Collections.Generic;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.Core.Models;

public class TemporaryTokenSettingsModel
{
    public string Key { get; set; } = default!;

    public string Issuer { get; set; } = default!;

    public string Audience { get; set; } = default!;

    public int ExpireSeconds { get; set; } = 300;
}
