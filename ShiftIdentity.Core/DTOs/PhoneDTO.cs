using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs
{

    public class PhoneDTO
    {
        [DataType(DataType.PhoneNumber)]
        public string Phone { get; set; }

        public bool IsVerified { get; set; }
    }

}