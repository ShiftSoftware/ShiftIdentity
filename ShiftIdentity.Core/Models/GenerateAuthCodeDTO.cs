using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{
    public class GenerateAuthCodeDTO
    {
        private string appId;

        [Required]
        public string AppId
        {
            get { return appId.ToLower(); }
            set { appId = value.ToLower(); }
        }


        [Required]
        public string CodeChallenge { get; set; }

        public string ReturnUrl { get; set; }
    }
}
