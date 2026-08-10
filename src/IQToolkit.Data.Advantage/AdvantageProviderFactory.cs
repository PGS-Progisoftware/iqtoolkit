using System;
using System.Data.Common;
using System.IO;
using System.Reflection;

namespace IQToolkit.Data.Advantage
{
    /// <summary>
    /// Resolves <c>Advantage.Data.Provider.AdsFactory</c> at runtime so this assembly
    /// does not take a compile-time / redistributable dependency on the licensed ADS DLL.
    /// The client must ensure <c>Advantage.Data.Provider.dll</c> is loadable (app base, already loaded, etc.).
    /// </summary>
    public class AdvantageProviderFactory
    {
        private const string AssemblyFileName = "Advantage.Data.Provider.dll";
        private const string FactoryTypeName = "Advantage.Data.Provider.AdsFactory";
        private const string FactoryTypeAssemblyQualifiedName = FactoryTypeName + ", Advantage.Data.Provider";

        private static readonly Lazy<DbProviderFactory> _factory = new Lazy<DbProviderFactory>(CreateFactory);

        public static DbProviderFactory Instance => _factory.Value;

        private static DbProviderFactory CreateFactory()
        {
            // Prefer standard probing / already-loaded assemblies (app base, deps, etc.).
            var adsFactoryType = Type.GetType(FactoryTypeAssemblyQualifiedName, throwOnError: false);
            if (adsFactoryType != null)
                return CreateInstance(adsFactoryType);

            // Explicit file next to the entry/app base (not CWD — that breaks when working directory ≠ bin).
            var baseDirectory = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                var path = Path.Combine(baseDirectory, AssemblyFileName);
                if (File.Exists(path))
                {
                    var assembly = Assembly.LoadFrom(path);
                    adsFactoryType = assembly.GetType(FactoryTypeName, throwOnError: true);
                    return CreateInstance(adsFactoryType);
                }
            }

            throw new FileNotFoundException(
                $"Could not load '{FactoryTypeName}'. Ensure '{AssemblyFileName}' is present in the application directory or otherwise loadable at runtime.",
                Path.Combine(baseDirectory ?? string.Empty, AssemblyFileName));
        }

        private static DbProviderFactory CreateInstance(Type adsFactoryType)
        {
            return (DbProviderFactory)Activator.CreateInstance(adsFactoryType);
        }
    }
}
