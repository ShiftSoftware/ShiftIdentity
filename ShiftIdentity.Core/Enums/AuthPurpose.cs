using System;
using System.Collections.Generic;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.Core.Enums;

public enum AuthPurpose
{
    None = 0,
    ChangePassword,
    Mfa,
    MfaEnrollment,
}
