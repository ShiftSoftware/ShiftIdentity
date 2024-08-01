using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Models;

public class RefreshTokenSettingsModel
{
    public string Key { get; set; } = default!;

    public string Issuer { get; set; } = default!;

    public string Audience { get; set; } = default!;

    public int ExpireSeconds { get; set; }
}