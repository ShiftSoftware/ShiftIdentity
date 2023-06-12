using System.ComponentModel.DataAnnotations;
using System;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{

    public class GenerateExternalTokenWithAppIdOnlyDTO
    {
        [Required]
        public Guid AuthCode { get; set; }

        private string appId;

        [Required]
        public string AppId
        {
            get { return appId.ToLower(); }
            set { appId = value.ToLower(); }
        }

        [Required]
        public string CodeVerifier { get; set; }
    }
}