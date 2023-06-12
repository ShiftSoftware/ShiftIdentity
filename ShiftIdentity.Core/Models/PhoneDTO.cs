using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{

    public class PhoneDTO
    {
        [DataType(DataType.PhoneNumber)]
        public string Phone { get; set; }

        public bool IsVerified { get; set; }
    }

}