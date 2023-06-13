using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs
{

    public class EmailDTO
    {
        [DataType(DataType.EmailAddress)]
        public string Email { get; set; }

        public bool IsVerified { get; set; }
    }

}