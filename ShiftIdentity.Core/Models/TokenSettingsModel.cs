namespace ShiftSoftware.ShiftIdentity.Core.Models
{
    public class TokenSettingsModel
    {
        public string Key { get; set; }

        public string Issuer { get; set; }

        public string Audience { get; set; }

        public int ExpireSeconds { get; set; }
    }
}
