using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Model
{
    public class LoginDTO
    {
        private string username = default!;

        [Required]
        [MaxLength(255)]
        public string Username
        {
            get { return username == default ? default! : username.ToLower(); }
            set { username = value.ToLower(); }
        }

        [Required]
        [MaxLength(255)]
        public string Password { get; set; } = default!;
    }
}
