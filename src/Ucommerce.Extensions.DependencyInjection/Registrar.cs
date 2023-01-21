using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Hosting;
using Castle.MicroKernel;
using Castle.MicroKernel.Handlers;
using Castle.Windsor;
using Castle.Windsor.Diagnostics;
using Ucommerce.Extensions.DependencyInjection;
using Ucommerce.Infrastructure;

[assembly: WebActivatorEx.PreApplicationStartMethod(typeof(Registrar), nameof(Registrar.Configure), Order = 0)]

namespace Ucommerce.Extensions.DependencyInjection
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class Registrar
    {
        private const string DISABLE_CONTAINER_CHECK = "Ucommerce:DisableCheckForPotentiallyMisconfiguredComponents";
        private static readonly List<Assembly> _assemblies = new();
        private static Func<string, bool> _fileFilter = _ => true;

        private static IEnumerable<Assembly> Assemblies
        {
            get
            {
                foreach (var assemblyFile in GetAssemblyFiles()
                             .Where(file => _fileFilter(file)))
                {
                    try
                    {
                        // Ignore assemblies we can't load. They could be native, etc...
                        _assemblies.Add(Assembly.LoadFrom(assemblyFile));
                    }
                    catch (Win32Exception) { }
                    catch (ArgumentException) { }
                    catch (FileNotFoundException) { }
                    catch (PathTooLongException) { }
                    catch (BadImageFormatException) { }
                    catch (SecurityException) { }
                }

                return _assemblies;
            }
        }

        static Registrar()
        {
            DetermineWhatFilesAndAssembliesToScan();
        }

        public static void Configure()
        {
            bool.TryParse(ConfigurationManager.AppSettings[DISABLE_CONTAINER_CHECK], out var result);
            ConfigurationManager.AppSettings.Set(DISABLE_CONTAINER_CHECK, true.ToString());

            var container = new WindsorContainer();
            ObjectFactory.Instance.AddChildContainer(container);

            RunConfigureMethods(container.Parent);

            ConfigurationManager.AppSettings.Set(DISABLE_CONTAINER_CHECK, $"{result}");
            if (!result)
            {
                ObtainDependencyDetails(container.Kernel);
            }
        }

        private static void DetermineWhatFilesAndAssembliesToScan()
        {
            var value = ConfigurationManager.AppSettings["ucommerce:excludedFilesExpression"];
            if (value == null)
            {
                return;
            }

            var fileExpression = new Regex(value.Trim());
            _fileFilter = file => !fileExpression.IsMatch(file);
        }

        private static IEnumerable<string> GetAssemblyFiles()
        {
            // When running under ASP.NET, find assemblies in the bin folder.
            // Outside of ASP.NET, use whatever folder lib itself is in
            var directory = (HostingEnvironment.IsHosted
                                 ? HttpRuntime.BinDirectory
                                 : Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly()
                                     .CodeBase).LocalPath)) ?? string.Empty;

            return Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
        }

        private static IEnumerable<Type> GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.DefinedTypes;
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(x => x != null);
            }
        }

        private static void ObtainDependencyDetails(IKernel kernel)
        {
            var subSystem = kernel.GetSubSystem(SubSystemConstants.DiagnosticsKey);
            if (subSystem is not IDiagnosticsHost diagnosticsHost)
            {
                return;
            }

            var handlers = diagnosticsHost.GetDiagnostic<IPotentiallyMisconfiguredComponentsDiagnostic>()
                .Inspect();

            var message = new StringBuilder();
            var inspector = new DependencyInspector(message);
            handlers.OfType<IExposeDependencyInfo>()
                .ForEach(handler => handler.ObtainDependencyDetails(inspector));
            if (message.Length <= 0)
            {
                return;
            }

            throw new InvalidOperationException(message.ToString());
        }

        private static void RunConfigureMethods(IWindsorContainer container)
        {
            var modules = Assemblies.SelectMany(GetTypesSafely)
                .Distinct()
                .Where(type => type.IsClass)
                .Where(type => typeof(IModule).IsAssignableFrom(type))
                .Select(Activator.CreateInstance)
                .OfType<IModule>()
                .ToList();

            foreach (var module in modules)
            {
                module.Configure(container);
            }
        }
    }
}
