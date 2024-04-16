using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core;

public interface ISendEmailVerification
{
    Task SendEmailVerificationAsync(string url, UserDataDTO user);
}
