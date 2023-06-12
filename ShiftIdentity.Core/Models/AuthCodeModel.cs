using ShiftSoftware.ShiftIdentity.Core.ToDelete;
using System;
using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{
    public class AuthCodeModel
    {
        public Guid Code { get; set; }

        public long UserID { get; set; }

        public string AppDisplayName { get; set; }

        private string appId;

        public string AppId
        {
            get { return appId.ToLower(); }
            set { appId = value.ToLower(); }
        }

        public DateTime Expire { get; set; }

        public string CodeChallenge { get; set; }

        public List<ScopeDTO> Scopes { get; set; }

        public string RedirectUri { get; set; }
    }
}
