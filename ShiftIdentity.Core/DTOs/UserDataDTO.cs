using System;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs
{

    public class UserDataDTO : ShiftEntity.Model.Dtos.ShiftEntityDTO
    {
        [_UserHashId]
        public override string ID { get; set; } = default!;
        [Required]
        [MaxLength(255)]
        public string Username { get; set; } = default!;

        [MaxLength(255)]
        [EmailAddress]
        public string Email { get; set; } = default!;

        [MaxLength(30)]
        [Phone]
        public string Phone { get; set; } = default!;

        [Required]
        [MaxLength(255)]
        public string FullName { get; set; } = default!;

        private DateTime? birthDate;
        [DataType(DataType.Date)]
        public DateTime? BirthDate
        {
            get { return birthDate; }
            set { birthDate = value?.Date; }
        }
    }
}