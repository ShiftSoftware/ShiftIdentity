using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.AspNetCore;

public class ShiftIdentityOptions
{
    public TokenUserDataDTO UserData { get; private set; }

    public AppModel App { get; private set; }

    public string[] AccessTrees { get; private set; }

    public string? UserPassword { get; private set; }

    public ShiftIdentityConfiguration Configuration { get;private set; }

    public ShiftIdentityOptions(TokenUserDataDTO userData,
        AppModel app,
        string[] accessTrees,
        ShiftIdentityConfiguration configuration,
        string? userPassword = null)
    {
        this.UserData = userData;
        this.App = app;
        this.AccessTrees = accessTrees;
        this.Configuration = configuration;
        this.UserPassword = userPassword;
    }
}
