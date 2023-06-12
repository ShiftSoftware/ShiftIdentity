using System;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{

    public class UserDataDTO : ShiftEntity.Model.Dtos.ShiftEntityDTO
    {
        [_UserHashId]
        public override string ID { get; set; }
        [Required]
        [MaxLength(255)]
        public string Username { get; set; }

        [MaxLength(255)]
        [EmailAddress]
        public string Email { get; set; }

        [MaxLength(30)]
        [Phone]
        public string Phone { get; set; }

        [Required]
        [MaxLength(255)]
        public string FullName { get; set; }

        private DateTime? birthDate;
        [DataType(DataType.Date)]
        public DateTime? BirthDate
        {
            get { return birthDate; }
            set { birthDate = value?.Date; }
        }
    }
}