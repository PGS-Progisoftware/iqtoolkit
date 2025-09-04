using System;
using System.Data.Common;

namespace IQToolkit.Data.Advantage
{
    public class AdvantageProviderFactory
    {
        private static readonly Lazy<DbProviderFactory> _factory = new Lazy<DbProviderFactory>(() =>
        {
            // Dynamically load the Advantage.Data.Provider factory
            var adsFactoryType = Type.GetType("Advantage.Data.Provider.AdsFactory, Advantage.Data.Provider", throwOnError: true);
            return (DbProviderFactory)Activator.CreateInstance(adsFactoryType);
        });

        public static DbProviderFactory Instance => _factory.Value;
    }
}
