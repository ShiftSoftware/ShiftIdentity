using System.Data;
using System.Data.SqlTypes;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

// Reads an existing local export without importing a database. DacFx handles its native BCP format.
// Only the scope/grant projection is kept; credentials and personal fields are never exported.
internal static class BacpacInput
{
    public static (Dataset Data, object Provenance) Read(string path,string dacAssembly)
    {
        using var archive=ZipFile.OpenRead(path);
        using var modelStream=archive.GetEntry("model.xml")!.Open();
        var model=XDocument.Load(modelStream);var ns=model.Root!.Name.Namespace;
        var assembly=Assembly.LoadFrom(dacAssembly);
        var readerType=assembly.GetType("Microsoft.Data.Tools.Schema.Sql.SqlClient.Bcp.BcpDataReader",true)!;
        var data=new Dataset();var organizations=new HashSet<string>();var countries=new HashSet<string>();
        var projection=new HashSet<string>(["ID","IsActive","IsDeleted","AccessTree","CompanyID","CompanyBranchID","RegionID","CountryID",
            "Tree","UserID","AccessTreeID","TeamID","Name","CompanyType","CityID"]);
        void ReadTable(string table,Action<Dictionary<string,object?>> consume)
        {
            var element=model.Descendants(ns+"Element").Single(x=>(string?)x.Attribute("Type")=="SqlTable"&&(string?)x.Attribute("Name")==$"[ShiftIdentity].[{table}]");
            var columns=element.Elements(ns+"Relationship").Single(x=>(string?)x.Attribute("Name")=="Columns")
                .Elements(ns+"Entry").Select(e=>e.Element(ns+"Element")!).Where(e=>(string?)e.Attribute("Type")=="SqlSimpleColumn").ToArray();
            var schema=new DataTable();
            schema.Columns.Add("ColumnName",typeof(string));schema.Columns.Add("ColumnOrdinal",typeof(int));
            schema.Columns.Add("ColumnSize",typeof(int));schema.Columns.Add("AllowDbNull",typeof(bool));
            schema.Columns.Add("ProviderSpecificDataType",typeof(Type));schema.Columns.Add("DataTypeName",typeof(string));
            schema.Columns.Add("UdtAssemblyQualifiedName",typeof(string));
            for(var i=0;i<columns.Length;i++)
            {
                var c=columns[i];var name=((string)c.Attribute("Name")!).Split('.').Last().Trim('[',']');
                var spec=c.Descendants(ns+"Element").First(e=>(string?)e.Attribute("Type")=="SqlTypeSpecifier");
                var sqlType=((string)spec.Descendants(ns+"References").First().Attribute("Name")!).Trim('[',']');
                var length=(string?)spec.Elements(ns+"Property").FirstOrDefault(p=>(string?)p.Attribute("Name")=="Length")?.Attribute("Value");
                var provider=sqlType switch
                {
                    "bigint"=>typeof(SqlInt64),"int"=>typeof(SqlInt32),"smallint"=>typeof(SqlInt16),"tinyint"=>typeof(SqlByte),
                    "bit"=>typeof(SqlBoolean),"nvarchar" or "varchar" or "nchar" or "char" or "ntext" or "text"=>typeof(SqlString),
                    "varbinary" or "binary" or "image" or "timestamp" or "rowversion"=>typeof(SqlBinary),
                    "datetime2" or "date"=>typeof(DateTime),"datetime" or "smalldatetime"=>typeof(SqlDateTime),
                    "datetimeoffset"=>typeof(DateTimeOffset),"time"=>typeof(TimeSpan),"uniqueidentifier"=>typeof(SqlGuid),
                    "decimal" or "numeric"=>typeof(SqlDecimal),"float"=>typeof(SqlDouble),"real"=>typeof(SqlSingle),
                    _=>throw new NotSupportedException("Unsupported backup schema type."),
                };
                var nullable=(string?)c.Elements(ns+"Property").FirstOrDefault(p=>(string?)p.Attribute("Name")=="IsNullable")?.Attribute("Value")!="False";
                schema.Rows.Add(name,i,length is null?int.MaxValue:int.Parse(length),nullable,provider,sqlType,DBNull.Value);
            }
            foreach(var entry in archive.Entries.Where(e=>e.FullName.StartsWith($"Data/ShiftIdentity.{table}/",StringComparison.Ordinal)&&e.FullName.EndsWith(".BCP")))
            {
                using var source=entry.Open();using var stream=new MemoryStream();source.CopyTo(stream);stream.Position=0;
                using var reader=(IDataReader)Activator.CreateInstance(readerType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,[stream,schema,false],null)!;
                while(reader.Read())
                {
                    var row=new Dictionary<string,object?>();
                    for(var i=0;i<schema.Rows.Count;i++)
                    {
                        var column=(string)schema.Rows[i]["ColumnName"];
                        if(!projection.Contains(column))continue;
                        var value=reader.GetValue(i);
                        if(value is INullable nullable)value=nullable.IsNull?null:value.GetType().GetProperty("Value")!.GetValue(value);
                        row[column]=value is DBNull?null:value;
                    }
                    consume(row);
                }
            }
        }
        static long? Id(Dictionary<string,object?> r,string name)=>r.GetValueOrDefault(name) is {} value?Convert.ToInt64(value):null;
        static bool Flag(Dictionary<string,object?> r,string name)=>Convert.ToBoolean(r[name]);
        static string Key(Dictionary<string,object?> r)=>Id(r,"ID")!.Value.ToString();
        ReadTable("Users",r=>data.Users.Add(new UserModel{id=Key(r),IsActive=Flag(r,"IsActive"),IsDeleted=Flag(r,"IsDeleted"),AccessTree=(string?)r["AccessTree"],
            CompanyID=Id(r,"CompanyID"),CompanyBranchID=Id(r,"CompanyBranchID"),RegionID=Id(r,"RegionID"),CountryID=Id(r,"CountryID"),Username="benchmark",FullName="benchmark"}));
        ReadTable("AccessTrees",r=>data.Trees.Add(new AccessTreeModel{id=Key(r),Name="benchmark",Tree=(string)r["Tree"]!,IsDeleted=Flag(r,"IsDeleted")}));
        ReadTable("UserAccessTrees",r=>data.Assignments.Add(new UserAccessTreeModel{id=Key(r),UserID=Id(r,"UserID")!.Value,AccessTreeID=Id(r,"AccessTreeID")!.Value,IsDeleted=Flag(r,"IsDeleted")}));
        ReadTable("TeamUsers",r=>data.Memberships.Add(new TeamUserModel{id=Key(r),UserID=Id(r,"UserID")!.Value,TeamID=Id(r,"TeamID")!.Value,IsDeleted=Flag(r,"IsDeleted")}));
        ReadTable("Companies",r=>{organizations.Add((string)r["Name"]!);data.Companies.Add(new CompanyModel{id=Key(r),Name="benchmark",CompanyType=(CompanyTypes)Convert.ToInt32(r["CompanyType"]),IsDeleted=Flag(r,"IsDeleted")});});
        ReadTable("CompanyBranches",r=>data.Branches.Add(new CompanyBranchModel{id=Key(r),Name="benchmark",CityID=Id(r,"CityID"),IsDeleted=Flag(r,"IsDeleted")}));
        ReadTable("Countries",r=>countries.Add((string)r["Name"]!));
        using var originStream=archive.GetEntry("Origin.xml")!.Open();var origin=XDocument.Load(originStream);
        using var file=File.OpenRead(path);var digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
        return (data,new {Kind="existing-local-bacpac",Sha256=digest,
            ExportStarted=origin.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Start")?.Value,
            Organizations=organizations.Order().ToArray(),Countries=countries.Order().ToArray()});
    }
}
