using System;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{
    public class AuthCodeModel
    {
        public Guid Code { get; set; }

        public long UserID { get; set; }

        public string AppDisplayName { get; set; } = default!;

        private string appId = default!;

        [Required]
        public string AppId
        {
            get { return appId.ToLower(); }
            set { appId = value.ToLower(); }
        }

        public DateTime Expire { get; set; } = default!;

        public string CodeChallenge { get; set; } = default!;

        public string RedirectUri { get; set; } = default!;
    }
}
