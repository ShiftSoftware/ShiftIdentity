using ShiftSoftware.ShiftEntity.Model.HashId;

namespace ShiftSoftware.ShiftIdentity.Model;

public class _UserHashId : JsonHashIdConverterAttribute
{
    public _UserHashId() : base(HashId.UserIdsSalt, HashId.UserIdsMinHashLength, HashId.UserIdsAlphabet)
    {

    }
}
