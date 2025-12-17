// See https://aka.ms/new-console-template for more information
using IQToolkit.Data.Advantage;
using PCSLib.Data.DBF;
using PCSLib.Data.DTO;
using PCSLib.Data.Enums;
using System.Reflection;
using System.Text;
using Gridify;
using WebServices.Mapperly;


#if NET5_0_OR_GREATER
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif

Console.WriteLine("Hello, World!");

        var type = typeof(LocGen);
        Console.WriteLine($"Type: {type.FullName}");
        
        var props = type.GetProperties();
        foreach (var p in props)
        {
             Console.WriteLine($"Property: {p.Name}, Type: {p.PropertyType}");
        }


string connectionString = "Data Source=C:\\PGS\\LOCA RECEPTION\\Data\\Lyon;ServerType=remote;TableType=CDX;TrimTrailingSpaces=True;CharType=OEM";

var provider = new AdvantageQueryProvider(connectionString);
provider.Log = Console.Out;
provider.EnableQueryTiming = true;


// var configuration = new MapperConfiguration(cfg =>
// {
// 	// Add all Profiles from the Assembly containing this Type
// 	cfg.AddMaps(typeof(LocationProfile));
// });
// Build the query without executing it yet
var locgen = provider.GetTable<LocPer>()
				.ProjectToDto()
				.ApplyFiltering("dtModification >= 2022-12-16T13:00:00")
				.FirstOrDefault();


// var locgenproject = provider.GetTable<LocGen>()
// 	.Where(l => l.NumeroLocation == "210030246")
// 	.ProjectTo<Location>(configuration)
// 	.First();

Console.WriteLine(locgen?.DTModification);
// Console.WriteLine(locgenproject.DTDepartMateriel);

// Print the full execution plan for diagnostics
try
{
 //var planText = provider.GetQueryPlan(queryEntreprise.Expression);
 //Console.WriteLine("=== Execution Plan ===");
 //Console.WriteLine(planText);
 //Console.WriteLine("======================");
}
catch (Exception ex)
{
 Console.WriteLine($"Failed to get query plan: {ex}");
}

// Execute the query
//var resultsNormal = queryEntreprise.ToList();

//Console.ReadLine();