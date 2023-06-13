using System;
using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs
{
    public class AuthCodeDTO
    {
        public string AppDisplayName { get; set; } = default!;

        public Guid Code { get; set; }

        public string ReturnUrl { get; set; } = default!;

        public string RedirectUri { get; set; } = default!;
    }
}
