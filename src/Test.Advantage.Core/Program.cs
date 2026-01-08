// See https://aka.ms/new-console-template for more information
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Data;
using System.Data.Common;
using IQToolkit.Data.Advantage;
using PCSLib.Data.DBF;
using PCSLib.Data.DTO;
using PCSLib.Data.Enums;
using Gridify;
using WebServices.Mapperly;

namespace Test.Advantage.Core
{
    class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        static void Main(string[] args)
        {
#if NET5_0_OR_GREATER
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            Console.WriteLine($"Is64BitProcess: {Environment.Is64BitProcess}");
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ace64.dll");
            IntPtr handle = LoadLibrary(path);
            Console.WriteLine($"LoadLibrary(ace64.dll): {handle}, Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");

            string connectionString = "Data Source=C:\\PGS\\LOCA RECEPTION\\Data\\Lyon;ServerType=remote;TableType=CDX;TrimTrailingSpaces=True;CharType=OEM";

            var provider = new AdvantageQueryProvider(connectionString);
            provider.Log = Console.Out;
            provider.EnableQueryTiming = true;

            Console.WriteLine("test : filter on nullable");
            try 
            {
                var locations = provider.GetTable<LocGen>()
                    .Select(lg => new
                    {
                        numeroloc = lg.NumeroLocation,
                        isst = provider.GetTable<LocStGen>().Any(st => st.NUMLOC == lg.NumeroLocation)

                    })
                    .Take(1)
                    .ToList();

                Console.WriteLine($"let's check the dates...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test 1 Failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}