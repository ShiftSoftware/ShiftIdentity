using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{

    public class TokenUserDataDTO
    {
        public long ID { get; set; }

        public string Username { get; set; }

        public string FullName { get; set; }

        public IEnumerable<string> Roles { get; set; }

        public List<EmailDTO> Emails { get; set; }

        public List<PhoneDTO> Phones { get; set; }

        public TokenUserDataDTO()
        {
            Roles = new List<string>();
        }
    }
}