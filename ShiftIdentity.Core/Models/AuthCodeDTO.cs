using ShiftSoftware.ShiftIdentity.Core.ToDelete;
using System;
using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{
    public class AuthCodeDTO
    {
        public string AppDisplayName { get; set; }

        public Guid Code { get; set; }

        public string ReturnUrl { get; set; }

        public string RedirectUri { get; set; }

        public List<ScopeDTO> Scopes { get; set; }
    }
}
