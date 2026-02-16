using System;
using System.Data.Common;

namespace IQToolkit.Data.Advantage
{
    public class AdvantageProviderFactory
    {
        private static readonly Lazy<DbProviderFactory> _factory = new Lazy<DbProviderFactory>(() =>
        {
			// Dynamically load the Advantage.Data.Provider factory
			try
			{
				var assembly = System.Reflection.Assembly.LoadFrom("Advantage.Data.Provider.dll");
				var type = assembly.GetType("Advantage.Data.Provider.AdsFactory");
				return (DbProviderFactory)Activator.CreateInstance(type);
			}
			catch (Exception ex)
			{
				// Check ex.FusionLog or ex.LoaderExceptions
				Console.WriteLine(ex.ToString());
			}

		var adsFactoryType = Type.GetType("Advantage.Data.Provider.AdsFactory, Advantage.Data.Provider", throwOnError: true);
            return (DbProviderFactory)Activator.CreateInstance(adsFactoryType);
        });

        public static DbProviderFactory Instance => _factory.Value;
    }
}
