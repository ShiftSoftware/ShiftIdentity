using System;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User
{

    public class UserDataDTO : ShiftEntity.Model.Dtos.ShiftEntityViewAndUpsertDTO
    {
        [UserHashIdConverter]
        public override string? ID { get; set; }
        [Required]
        [MaxLength(255)]
        public string Username { get; set; } = default!;

        [MaxLength(255)]
        [EmailAddress]
        public string? Email { get; set; } = default!;

        [MaxLength(30)]
        [Phone]
        public string? Phone { get; set; } = default!;

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

        public bool EmailVerified { get; set; }

        public bool PhoneVerified { get; set; }
    }
}