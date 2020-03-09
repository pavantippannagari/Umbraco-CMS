﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security;
using System.Text;
using Umbraco.Core.Configuration.UmbracoSettings;
using Umbraco.Core.Exceptions;
using Umbraco.Core.Logging;

namespace Umbraco.Core.Composing
{
    /// <inheritdoc cref="ITypeFinder"/>
    public class TypeFinder : ITypeFinder
    {
        private readonly ILogger _logger;
        private readonly IAssemblyProvider _assemblyProvider;

        //public TypeFinder(ILogger logger, ITypeFinderConfig typeFinderConfig = null)
        //    : this(logger, new DefaultUmbracoAssemblyProvider(Assembly.GetEntryAssembly()?.GetName()?.Name), typeFinderConfig)
        //{
        //}

        public TypeFinder(ILogger logger, IAssemblyProvider assemblyProvider, ITypeFinderConfig typeFinderConfig = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _assemblyProvider = assemblyProvider;
            _assembliesAcceptingLoadExceptions = typeFinderConfig?.AssembliesAcceptingLoadExceptions.Where(x => !x.IsNullOrWhiteSpace()).ToArray() ?? Array.Empty<string>();           
        }

        private volatile HashSet<Assembly> _localFilteredAssemblyCache;
        private readonly object _localFilteredAssemblyCacheLocker = new object();
        private readonly List<string> _notifiedLoadExceptionAssemblies = new List<string>();
        private static readonly ConcurrentDictionary<string, Type> TypeNamesCache= new ConcurrentDictionary<string, Type>();        
        private readonly string[] _assembliesAcceptingLoadExceptions;

        private bool AcceptsLoadExceptions(Assembly a)
        {
            if (_assembliesAcceptingLoadExceptions.Length == 0)
                return false;
            if (_assembliesAcceptingLoadExceptions.Length == 1 && _assembliesAcceptingLoadExceptions[0] == "*")
                return true;
            var name = a.GetName().Name; // simple name of the assembly
            return _assembliesAcceptingLoadExceptions.Any(pattern =>
            {
                if (pattern.Length > name.Length) return false; // pattern longer than name
                if (pattern.Length == name.Length) return pattern.InvariantEquals(name); // same length, must be identical
                if (pattern[pattern.Length] != '.') return false; // pattern is shorter than name, must end with dot
                return name.StartsWith(pattern); // and name must start with pattern
            });
        }


        private IEnumerable<Assembly> GetAllAssemblies() => _assemblyProvider.Assemblies;

        /// <inheritdoc />
        public IEnumerable<Assembly> AssembliesToScan
        {
            get
            {
                lock (_localFilteredAssemblyCacheLocker)
                {
                    if (_localFilteredAssemblyCache != null)
                        return _localFilteredAssemblyCache;

                    var assemblies = GetFilteredAssemblies(null, KnownAssemblyExclusionFilter);
                    _localFilteredAssemblyCache = new HashSet<Assembly>(assemblies);
                    return _localFilteredAssemblyCache;
                }
            }
        }

        /// <summary>
        /// Return a distinct list of found local Assemblies and excluding the ones passed in and excluding the exclusion list filter
        /// </summary>
        /// <param name="excludeFromResults"></param>
        /// <param name="exclusionFilter"></param>
        /// <returns></returns>
        private IEnumerable<Assembly> GetFilteredAssemblies(
            IEnumerable<Assembly> excludeFromResults = null,
            string[] exclusionFilter = null)
        {
            if (excludeFromResults == null)
                excludeFromResults = new HashSet<Assembly>();
            if (exclusionFilter == null)
                exclusionFilter = new string[] { };

            return GetAllAssemblies()
                .Where(x => excludeFromResults.Contains(x) == false
                            && x.GlobalAssemblyCache == false
                            && exclusionFilter.Any(f => x.FullName.StartsWith(f)) == false);
        }

        /// <summary>
        /// this is our assembly filter to filter out known types that def don't contain types we'd like to find or plugins
        /// </summary>
        /// <remarks>
        /// NOTE the comma vs period... comma delimits the name in an Assembly FullName property so if it ends with comma then its an exact name match
        /// NOTE this means that "foo." will NOT exclude "foo.dll" but only "foo.*.dll"
        /// </remarks>
        internal static readonly string[] KnownAssemblyExclusionFilter = {
            "mscorlib",
            "Antlr3.",
            "AutoMapper,",
            "AutoMapper.",
            "Autofac,", // DI
            "Autofac.",
            "AzureDirectory,",
            "Castle.", // DI, tests
            "ClientDependency.",
            "CookComputing.",
            "CSharpTest.", // BTree for NuCache
            "DataAnnotationsExtensions,",
            "DataAnnotationsExtensions.",
            "Dynamic,",
            "Examine,",
            "Examine.",
            "HtmlAgilityPack,",
            "HtmlAgilityPack.",
            "HtmlDiff,",
            "ICSharpCode.",
            "Iesi.Collections,", // used by NHibernate
            "LightInject.", // DI
            "LightInject,",
            "Lucene.",
            "Markdown,",
            "Microsoft.",
            "MiniProfiler,",
            "Moq,",
            "MySql.",
            "NHibernate,",
            "NHibernate.",
            "Newtonsoft.",
            "NPoco,",
            "NuGet.",
            "RouteDebugger,",
            "Semver.",
            "Serilog.",
            "Serilog,",
            "ServiceStack.",
            "SqlCE4Umbraco,",
            "Superpower,", // used by Serilog
            "System.",
            "TidyNet,",
            "TidyNet.",
            "WebDriver,",
            "itextsharp,",
            "mscorlib,",
            "nunit.framework,",
        };

        /// <summary>
        /// Finds any classes derived from the assignTypeFrom Type that contain the attribute TAttribute
        /// </summary>
        /// <param name="assignTypeFrom"></param>
        /// <param name="attributeType"></param>
        /// <param name="assemblies"></param>
        /// <param name="onlyConcreteClasses"></param>
        /// <returns></returns>
        public IEnumerable<Type> FindClassesOfTypeWithAttribute(
            Type assignTypeFrom,
            Type attributeType,
            IEnumerable<Assembly> assemblies = null,
            bool onlyConcreteClasses = true)
        {
            var assemblyList = (assemblies ?? AssembliesToScan).ToList();

            return GetClassesWithBaseType(assignTypeFrom, assemblyList, onlyConcreteClasses,
                //the additional filter will ensure that any found types also have the attribute applied.
                t => t.GetCustomAttributes(attributeType, false).Any());
        }

        /// <summary>
        /// Returns all types found of in the assemblies specified of type T
        /// </summary>
        /// <param name="assignTypeFrom"></param>
        /// <param name="assemblies"></param>
        /// <param name="onlyConcreteClasses"></param>
        /// <returns></returns>
        public IEnumerable<Type> FindClassesOfType(Type assignTypeFrom, IEnumerable<Assembly> assemblies = null, bool onlyConcreteClasses = true)
        {
            var assemblyList = (assemblies ?? AssembliesToScan).ToList();

            return GetClassesWithBaseType(assignTypeFrom, assemblyList, onlyConcreteClasses);
        }
        
        /// <summary>
        /// Finds any classes with the attribute.
        /// </summary>
        /// <param name="attributeType">The attribute type </param>
        /// <param name="assemblies">The assemblies.</param>
        /// <param name="onlyConcreteClasses">if set to <c>true</c> only concrete classes.</param>
        /// <returns></returns>
        public IEnumerable<Type> FindClassesWithAttribute(
            Type attributeType,
            IEnumerable<Assembly> assemblies = null,
            bool onlyConcreteClasses = true)
        {
            var assemblyList = (assemblies ?? AssembliesToScan).ToList();

            return GetClassesWithAttribute(attributeType, assemblyList, onlyConcreteClasses);
        }

        /// <summary>
        /// Returns a Type for the string type name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public virtual Type GetTypeByName(string name)
        {
            // This is exactly what the BuildManager does, if the type is an assembly qualified type
            // name it will find it.
            if (TypeNameContainsAssembly(name))
            {
                return Type.GetType(name);
            }

            // It didn't parse, so try loading from each already loaded assembly and cache it
            return TypeNamesCache.GetOrAdd(name, s =>
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(x => x.GetType(s))
                    .FirstOrDefault(x => x != null));
        }

        #region Private methods

        // borrowed from aspnet System.Web.UI.Util
        private static bool TypeNameContainsAssembly(string typeName)
        {
            return CommaIndexInTypeName(typeName) > 0;
        }

        // borrowed from aspnet System.Web.UI.Util
        private static int CommaIndexInTypeName(string typeName)
        {
            var num1 = typeName.LastIndexOf(',');
            if (num1 < 0)
                return -1;
            var num2 = typeName.LastIndexOf(']');
            if (num2 > num1)
                return -1;
            return typeName.IndexOf(',', num2 + 1);
        }

        private IEnumerable<Type> GetClassesWithAttribute(
            Type attributeType,
            IEnumerable<Assembly> assemblies,
            bool onlyConcreteClasses)
        {
            if (typeof(Attribute).IsAssignableFrom(attributeType) == false)
                throw new ArgumentException("Type " + attributeType + " is not an Attribute type.");

            var candidateAssemblies = new HashSet<Assembly>(assemblies);
            var attributeAssemblyIsCandidate = candidateAssemblies.Contains(attributeType.Assembly);
            candidateAssemblies.Remove(attributeType.Assembly);
            var types = new List<Type>();

            var stack = new Stack<Assembly>();
            stack.Push(attributeType.Assembly);

            while (stack.Count > 0)
            {
                var assembly = stack.Pop();

                Type[] assemblyTypes = null;
                if (assembly != attributeType.Assembly || attributeAssemblyIsCandidate)
                {
                    // get all assembly types that can be assigned to baseType
                    try
                    {
                        assemblyTypes = GetTypesWithFormattedException(assembly)
                            .ToArray(); // in try block
                    }
                    catch (TypeLoadException ex)
                    {
                        _logger.Error(typeof(TypeFinder), ex, "Could not query types on {Assembly} assembly, this is most likely due to this assembly not being compatible with the current Umbraco version", assembly);
                        continue;
                    }

                    types.AddRange(assemblyTypes.Where(x =>
                        x.IsClass // only classes
                        && (x.IsAbstract == false || x.IsSealed == false) // ie non-static, static is abstract and sealed
                        && x.IsNestedPrivate == false // exclude nested private
                        && (onlyConcreteClasses == false || x.IsAbstract == false) // exclude abstract
                        && x.GetCustomAttribute<HideFromTypeFinderAttribute>() == null // exclude hidden
                        && x.GetCustomAttributes(attributeType, false).Any())); // marked with the attribute
                }

                if (assembly != attributeType.Assembly && assemblyTypes.Where(attributeType.IsAssignableFrom).Any() == false)
                    continue;

                foreach (var referencing in TypeHelper.GetReferencingAssemblies(assembly, candidateAssemblies))
                {
                    candidateAssemblies.Remove(referencing);
                    stack.Push(referencing);
                }
            }

            return types;
        }

        /// <summary>
        /// Finds types that are assignable from the assignTypeFrom parameter and will scan for these types in the assembly
        /// list passed in, however we will only scan assemblies that have a reference to the assignTypeFrom Type or any type
        /// deriving from the base type.
        /// </summary>
        /// <param name="baseType"></param>
        /// <param name="assemblies"></param>
        /// <param name="onlyConcreteClasses"></param>
        /// <param name="additionalFilter">An additional filter to apply for what types will actually be included in the return value</param>
        /// <returns></returns>
        private IEnumerable<Type> GetClassesWithBaseType(
            Type baseType,
            IEnumerable<Assembly> assemblies,
            bool onlyConcreteClasses,
            Func<Type, bool> additionalFilter = null)
        {
            var candidateAssemblies = new HashSet<Assembly>(assemblies);
            var baseTypeAssemblyIsCandidate = candidateAssemblies.Contains(baseType.Assembly);
            candidateAssemblies.Remove(baseType.Assembly);
            var types = new List<Type>();

            var stack = new Stack<Assembly>();
            stack.Push(baseType.Assembly);

            while (stack.Count > 0)
            {
                var assembly = stack.Pop();

                // get all assembly types that can be assigned to baseType
                Type[] assemblyTypes = null;
                if (assembly != baseType.Assembly || baseTypeAssemblyIsCandidate)
                {
                    try
                    {
                        assemblyTypes = GetTypesWithFormattedException(assembly)
                            .Where(baseType.IsAssignableFrom)
                            .ToArray(); // in try block
                    }
                    catch (TypeLoadException ex)
                    {
                        _logger.Error(typeof(TypeFinder), ex, "Could not query types on {Assembly} assembly, this is most likely due to this assembly not being compatible with the current Umbraco version", assembly);
                        continue;
                    }

                    types.AddRange(assemblyTypes.Where(x =>
                        x.IsClass // only classes
                        && (x.IsAbstract == false || x.IsSealed == false) // ie non-static, static is abstract and sealed
                        && x.IsNestedPrivate == false // exclude nested private
                        && (onlyConcreteClasses == false || x.IsAbstract == false) // exclude abstract
                        && x.GetCustomAttribute<HideFromTypeFinderAttribute>(false) == null // exclude hidden
                        && (additionalFilter == null || additionalFilter(x)))); // filter
                }

                if (assembly != baseType.Assembly && assemblyTypes.All(x => x.IsSealed))
                    continue;

                foreach (var referencing in TypeHelper.GetReferencingAssemblies(assembly, candidateAssemblies))
                {
                    candidateAssemblies.Remove(referencing);
                    stack.Push(referencing);
                }
            }

            return types;
        }

        private IEnumerable<Type> GetTypesWithFormattedException(Assembly a)
        {
            //if the assembly is dynamic, do not try to scan it
            if (a.IsDynamic)
                return Enumerable.Empty<Type>();

            var getAll = a.GetCustomAttribute<AllowPartiallyTrustedCallersAttribute>() == null;

            try
            {
                //we need to detect if an assembly is partially trusted, if so we cannot go interrogating all of it's types
                //only its exported types, otherwise we'll get exceptions.
                return getAll ? a.GetTypes() : a.GetExportedTypes();
            }
            catch (TypeLoadException ex) // GetExportedTypes *can* throw TypeLoadException!
            {
                var sb = new StringBuilder();
                AppendCouldNotLoad(sb, a, getAll);
                AppendLoaderException(sb, ex);

                // rethrow as ReflectionTypeLoadException (for consistency) with new message
                throw new ReflectionTypeLoadException(new Type[0], new Exception[] { ex }, sb.ToString());
            }
            catch (ReflectionTypeLoadException rex) // GetTypes throws ReflectionTypeLoadException
            {
                var sb = new StringBuilder();
                AppendCouldNotLoad(sb, a, getAll);
                foreach (var loaderException in rex.LoaderExceptions.WhereNotNull())
                    AppendLoaderException(sb, loaderException);

                var ex = new ReflectionTypeLoadException(rex.Types, rex.LoaderExceptions, sb.ToString());

                // rethrow with new message, unless accepted
                if (AcceptsLoadExceptions(a) == false) throw ex;

                // log a warning, and return what we can
                lock (_notifiedLoadExceptionAssemblies)
                {
                    if (_notifiedLoadExceptionAssemblies.Contains(a.FullName) == false)
                    {
                        _notifiedLoadExceptionAssemblies.Add(a.FullName);
                        _logger.Warn(typeof (TypeFinder), ex, "Could not load all types from {TypeName}.", a.GetName().Name);
                    }
                }
                return rex.Types.WhereNotNull().ToArray();
            }
        }

        private static void AppendCouldNotLoad(StringBuilder sb, Assembly a, bool getAll)
        {
            sb.Append("Could not load ");
            sb.Append(getAll ? "all" : "exported");
            sb.Append(" types from \"");
            sb.Append(a.FullName);
            sb.AppendLine("\" due to LoaderExceptions, skipping:");
        }

        private static void AppendLoaderException(StringBuilder sb, Exception loaderException)
        {
            sb.Append(". ");
            sb.Append(loaderException.GetType().FullName);

            if (loaderException is TypeLoadException tloadex)
            {
                sb.Append(" on ");
                sb.Append(tloadex.TypeName);
            }

            sb.Append(": ");
            sb.Append(loaderException.Message);
            sb.AppendLine();
        }

        #endregion

    }

    /// <summary>
    /// lazily load a reference to all local assemblies and gac assemblies
    /// </summary>
    /// <remarks>
    /// This is a modified version of: http://www.dominicpettifer.co.uk/Blog/44/how-to-get-a-reference-to-all-assemblies-in-the--bin-folder
    /// 
    /// We do this because we cannot use AppDomain.Current.GetAssemblies() as this will return only assemblies that have been
    /// loaded in the CLR, not all assemblies.
    /// See these threads:
    /// http://issues.umbraco.org/issue/U5-198
    /// http://stackoverflow.com/questions/3552223/asp-net-appdomain-currentdomain-getassemblies-assemblies-missing-after-app
    /// http://stackoverflow.com/questions/2477787/difference-between-appdomain-getassemblies-and-buildmanager-getreferencedassembl
    /// </remarks>
    public class BruteForceAssemblyProvider : IAssemblyProvider
    {
        public BruteForceAssemblyProvider()
        {
            _allAssemblies = new Lazy<HashSet<Assembly>>(() =>
            {
                HashSet<Assembly> assemblies = null;
                try
                {
                    //NOTE: we cannot use AppDomain.CurrentDomain.GetAssemblies() because this only returns assemblies that have
                    // already been loaded in to the app domain, instead we will look directly into the bin folder and load each one.
                    var binFolder = GetRootDirectorySafe();
                    var binAssemblyFiles = Directory.GetFiles(binFolder, "*.dll", SearchOption.TopDirectoryOnly).ToList();                    
                    assemblies = new HashSet<Assembly>();
                    foreach (var a in binAssemblyFiles)
                    {
                        try
                        {
                            var assName = AssemblyName.GetAssemblyName(a);
                            var ass = Assembly.Load(assName);
                            assemblies.Add(ass);
                        }
                        catch (Exception e)
                        {
                            if (e is SecurityException || e is BadImageFormatException)
                            {
                                //swallow these exceptions
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }

                    //Since we are only loading in the /bin assemblies above, we will also load in anything that's already loaded (which will include gac items)
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        assemblies.Add(a);
                    }
                }
                catch (InvalidOperationException e)
                {
                    if (e.InnerException is SecurityException == false)
                        throw;
                }

                return assemblies;
            });
        }

        private readonly Lazy<HashSet<Assembly>> _allAssemblies;
        private string _rootDir = string.Empty;

        public IEnumerable<Assembly> Assemblies => _allAssemblies.Value;

        // FIXME - this is only an interim change, once the IIOHelper stuff is merged we should use IIOHelper here
        private string GetRootDirectorySafe()
        {
            if (string.IsNullOrEmpty(_rootDir) == false)
            {
                return _rootDir;
            }

            var codeBase = Assembly.GetExecutingAssembly().CodeBase;
            var uri = new Uri(codeBase);
            var path = uri.LocalPath;
            var baseDirectory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(baseDirectory))
                throw new PanicException("No root directory could be resolved.");

            _rootDir = baseDirectory.Contains("bin")
                ? baseDirectory.Substring(0, baseDirectory.LastIndexOf("bin", StringComparison.OrdinalIgnoreCase) - 1)
                : baseDirectory;

            return _rootDir;
        }
    }

    /// <summary>
    /// Provides a list of assemblies that can be scanned
    /// </summary>
    public interface IAssemblyProvider
    {
        IEnumerable<Assembly> Assemblies { get; }
    }

    /// <summary>
    /// Returns a list of scannable assemblies based on an entry point assembly and it's references
    /// </summary>
    /// <remarks>
    /// This will recursively search through the entry point's assemblies and Umbraco's core assemblies (Core/Web) and their references
    /// to create a list of scannable assemblies based on whether they themselves or their transitive dependencies reference Umbraco core assemblies.
    /// </remarks>
    public class DefaultUmbracoAssemblyProvider : IAssemblyProvider
    {
        private readonly Assembly _entryPointAssembly;
        private static readonly string[] UmbracoCoreAssemblyNames = new[] { "Umbraco.Core", "Umbraco.Web" };

        public DefaultUmbracoAssemblyProvider(Assembly entryPointAssembly)
        {
            _entryPointAssembly = entryPointAssembly ?? throw new ArgumentNullException(nameof(entryPointAssembly));
        }

        public IEnumerable<Assembly> Assemblies
        {
            get
            {
                var finder = new FindAssembliesWithReferencesTo(new[] { _entryPointAssembly }, UmbracoCoreAssemblyNames, true);
                foreach(var found in finder.Find())
                {
                    yield return found;
                }
            }
        }
    }

    /// <summary>
    /// Resolves assemblies that reference one of the specified "targetAssemblies" either directly or transitively.
    /// </summary>
    /// <remarks>
    /// Borrowed and modified from https://github.com/dotnet/aspnetcore-tooling/blob/master/src/Razor/src/Microsoft.NET.Sdk.Razor/ReferenceResolver.cs
    /// </remarks>
    internal class ReferenceResolver
    {
        private readonly HashSet<string> _umbracoAssemblies;
        private readonly IReadOnlyList<Assembly> _assemblyItems;
        private readonly Dictionary<Assembly, Classification> _classifications;
        private readonly List<Assembly> _lookup = new List<Assembly>();

        public ReferenceResolver(IReadOnlyList<string> targetAssemblies, IReadOnlyList<Assembly> assemblyItems)
        {
            _umbracoAssemblies = new HashSet<string>(targetAssemblies, StringComparer.Ordinal);
            _assemblyItems = assemblyItems;
            _classifications = new Dictionary<Assembly, Classification>();

            foreach (var item in assemblyItems)
            {
                _lookup.Add(item);
            }
        }

        public IEnumerable<Assembly> ResolveAssemblies()
        {
            var applicationParts = new List<Assembly>();

            foreach (var item in _assemblyItems)
            {
                var classification = Resolve(item);
                if (classification == Classification.ReferencesUmbraco || classification == Classification.IsUmbraco)
                {
                    applicationParts.Add(item);
                }
            }

            return applicationParts;
        }

        private Classification Resolve(Assembly assemblyItem)
        {
            if (_classifications.TryGetValue(assemblyItem, out var classification))
            {
                return classification;
            }

            // Initialize the dictionary with a value to short-circuit recursive references.
            classification = Classification.Unknown;
            _classifications[assemblyItem] = classification;

            if (_umbracoAssemblies.Contains(assemblyItem.GetName().Name))
            {
                classification = Classification.IsUmbraco;
            }
            else
            {
                classification = Classification.DoesNotReferenceUmbraco;
                foreach (var reference in GetReferences(assemblyItem))
                {
                    // recurse
                    var referenceClassification = Resolve(reference);

                    if (referenceClassification == Classification.IsUmbraco || referenceClassification == Classification.ReferencesUmbraco)
                    {
                        classification = Classification.ReferencesUmbraco;
                        break;
                    }
                }
            }

            Debug.Assert(classification != Classification.Unknown);
            _classifications[assemblyItem] = classification;
            return classification;
        }

        protected virtual IEnumerable<Assembly> GetReferences(Assembly assembly)
        {            
            foreach (var referenceName in assembly.GetReferencedAssemblies())
            {
                // don't include if this is excluded
                if (TypeFinder.KnownAssemblyExclusionFilter.Any(f => referenceName.FullName.StartsWith(f)))
                    continue;

                var reference = Assembly.Load(referenceName);
                if (!_lookup.Contains(reference))
                {
                    // A dependency references an item that isn't referenced by this project.
                    // We'll construct an item for so that we can calculate the classification based on it's name.

                    _lookup.Add(reference);

                    yield return reference;
                }                
            }
        }

        protected enum Classification
        {
            Unknown,
            DoesNotReferenceUmbraco,
            ReferencesUmbraco,
            IsUmbraco,
        }
    }


    /// <summary>
    /// Finds Assemblies from the entry point assemblies, it's dependencies and it's transitive dependencies that reference that targetAssemblyNames
    /// </summary>
    /// <remarkes>
    /// borrowed and modified from here https://github.com/dotnet/aspnetcore-tooling/blob/master/src/Razor/src/Microsoft.NET.Sdk.Razor/FindAssembliesWithReferencesTo.cs
    /// </remarkes>
    internal class FindAssembliesWithReferencesTo 
    {
        private readonly Assembly[] _referenceAssemblies;
        private readonly string[] _targetAssemblies;
        private readonly bool _includeTargets;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="referenceAssemblies">Entry point assemblies</param>
        /// <param name="targetAssemblyNames">Used to check if the entry point or it's transitive assemblies reference these assembly names</param>
        /// <param name="includeTargets">If true will also use the target assembly names as entry point assemblies</param>
        public FindAssembliesWithReferencesTo(Assembly[] referenceAssemblies, string[] targetAssemblyNames, bool includeTargets)
        {
            _referenceAssemblies = referenceAssemblies;
            _targetAssemblies = targetAssemblyNames;
            _includeTargets = includeTargets;
        }

        public IEnumerable<Assembly> Find()
        {
            var referenceItems = new List<Assembly>();
            foreach (var assembly in _referenceAssemblies)
            {
                referenceItems.Add(assembly);
            }

            if (_includeTargets)
            {
                foreach(var target in _targetAssemblies)
                {
                    referenceItems.Add(Assembly.Load(target));
                }
            }

            var provider = new ReferenceResolver(_targetAssemblies, referenceItems);
            var assemblyNames = provider.ResolveAssemblies();
            return assemblyNames.ToList();
        }

    }
}
