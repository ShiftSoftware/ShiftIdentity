using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Core.Entities
{
    [TemporalShiftEntity]
    public class User : ShiftEntity<User>
    {
        #region Security

        [Required]
        [MaxLength(255)]
        public string Username { get; set; } = default!;

        public byte[] PasswordHash { get; set; } = default!;

        public byte[] Salt { get; set; } = default!;

        public int LoginAttempts { get; set; }

        public DateTime? LockDownUntil { get; set; }

        public bool IsSuperAdmin { get; set; }

        public bool IsActive { get; set; }

        public bool BuiltIn { get; set; }

        public string? AccessTree { get; set; }

        public bool RequireChangePassword { get; set; }

        #endregion

        #region Contacts
        [MaxLength(255)]
        public string? Email { get; set; }

        [MaxLength(30)]
        public string? Phone { get; set; }
        #endregion

        #region Profile

        [Required]
        [MaxLength(255)]
        public string FullName { get; set; } = default!;

        public DateTime? BirthDate { get; set; }
        #endregion

        public IEnumerable<UserAccessTree> AccessTrees { get; set; }

        public User()
        {
            AccessTrees = new List<UserAccessTree>();
        }

        public static implicit operator UserListDTO(User entity)
        {
            if (entity == null)
                return default!;

            return new UserListDTO
            {
                Email = entity.Email,
                FullName = entity.FullName,
                ID = entity.ID.ToString(),
                IsActive = entity.IsActive,
                Phone = entity.Phone,
                Username = entity.Username,
            };
        }

        public static implicit operator UserDTO(User entity)
        {
            if (entity == null)
                return default!;

            return new UserDTO
            {
                CreateDate = entity.CreateDate,
                CreatedByUserID = entity.CreatedByUserID.ToString(),
                ID = entity.ID.ToString(),
                IsDeleted = entity.IsDeleted,
                LastSaveDate = entity.LastSaveDate,
                LastSavedByUserID = entity.LastSavedByUserID.ToString(),

                AccessTrees = entity.AccessTrees.Select(x => (AccessTreeDTO)x.AccessTree),
                BirthDate = entity.BirthDate,
                Username = entity.Username,
                Email = entity.Email,
                FullName = entity.FullName,
                IsActive = entity.IsActive,
                Phone = entity.Phone,
                AccessTree = entity.AccessTree
            };
        }

        public static implicit operator UserDataDTO(User entity)
        {
            if (entity == null) return default!;

            return new UserDataDTO
            {
                ID = entity.ID.ToString(),
                Username = entity.Username,
                Email = entity.Email,
                Phone = entity.Phone,
                FullName = entity.FullName,
                BirthDate = entity.BirthDate,


                CreateDate = entity.CreateDate,
                CreatedByUserID = entity.CreatedByUserID.ToString(),
                IsDeleted = entity.IsDeleted,
                LastSaveDate = entity.LastSaveDate,
                LastSavedByUserID = entity.LastSavedByUserID.ToString(),
            };
        }
    }
}
