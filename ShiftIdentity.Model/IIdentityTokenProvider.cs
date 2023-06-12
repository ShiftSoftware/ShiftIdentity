﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Model;

public interface IIdentityTokenProvider
{
    public Task<TokenDTO> GetTokenAsync();
}
