using System;
using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{
    public class ShiftIdentityConfiguration
    {
        public SecuritySettingsModel Security { get; set; }

        public TokenSettingsModel Token { get; set; }

        public TokenSettingsModel RefreshToken { get; set; }

        public HashIdSettings HashIdSettings { get; set; }

        public List<Type> ActionTrees { get; set; }
    }
}
