﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Blazor;

public class ShiftIdentityBlazorOptions
{
    public string AppId { get; private set; }
    public string BaseUrl { get; private set; }
    public string FrontEndBaseUrl { get; private set; }

    public ShiftIdentityBlazorOptions(string appId, string baseUrl, string frontEndBaseUrl)
    {
        AppId = appId;
        BaseUrl = baseUrl;
        FrontEndBaseUrl = frontEndBaseUrl;
    }
}
