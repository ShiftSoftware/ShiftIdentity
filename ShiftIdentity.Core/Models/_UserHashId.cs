using ShiftSoftware.ShiftEntity.Model.HashId;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{

    public class _UserHashId : JsonHashIdConverterAttribute
    {
        public _UserHashId() : base(HashId.UserIdsSalt, HashId.UserIdsMinHashLength, HashId.UserIdsAlphabet)
        {

        }
    }
}