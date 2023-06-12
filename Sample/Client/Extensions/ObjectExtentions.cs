using System.Web;

namespace Sample.Client.Extensions;

public static class ObjectExtentions
{
    public static string ToQueryString(this object obj)
    {
        if (obj is null)
            return string.Empty;

        var properties = from p in obj.GetType().GetProperties()
                         where p.GetValue(obj, null) != null
                         select p.Name + "=" + HttpUtility.UrlEncode(p.GetValue(obj, null).ToString());

        // queryString will be set to "Id=1&State=26&Prefix=f&Index=oo"                  
        return string.Join("&", properties.ToArray());
    }
}
