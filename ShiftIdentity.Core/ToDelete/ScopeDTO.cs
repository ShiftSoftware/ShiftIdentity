using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.ToDelete
{

    public class ScopeDTO : ShiftEntityMixedDTO
    {
        public override string ID { get; set; }
        [Required]
        [MaxLength(255)]
        public string Name { get; set; }

        [Required]
        [MaxLength(255)]
        public string DisplayName { get; set; }

        [MaxLength(4000)]
        public string Description { get; set; }
    }
}