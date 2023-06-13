using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Models;

public interface IIdentityTokenProvider
{
    public Task<TokenDTO> GetTokenAsync();
}
