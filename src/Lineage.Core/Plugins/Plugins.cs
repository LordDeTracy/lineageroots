using System.Reflection;
using System.Runtime.Loader;
using Lineage.Core.Model;
using Lineage.Core.Reports;

namespace Lineage.Core.Plugins;

/// <summary>
/// Something a plugin adds to Lineage Roots. A plugin is a .NET library (.dll) in the Plugins folder with one or more
/// public classes that implement one of the interfaces below and have a constructor without parameters.
/// </summary>
public interface IPlugin
{
    /// <summary>The name shown in Settings and on menus.</summary>
    string Name { get; }
    /// <summary>One sentence on what it does.</summary>
    string Description { get; }
}

/// <summary>A report: build a document, and the program previews it and saves it as PDF or Word like its own reports.</summary>
public interface IReportPlugin : IPlugin
{
    /// <summary><paramref name="current"/> is the person showing, or null.</summary>
    ReportDocument Build(FamilyTree tree, Person? current);
}

/// <summary>A chart, drawn as an SVG picture (the program shows it and can save it).</summary>
public interface IChartPlugin : IPlugin
{
    string DrawSvg(FamilyTree tree, Person current);
}

/// <summary>Reads another kind of family file.</summary>
public interface IImporterPlugin : IPlugin
{
    /// <summary>For the open dialog, e.g. "Family Tree Maker export".</summary>
    string FileTypeName { get; }
    /// <summary>e.g. "*.ftmx".</summary>
    IReadOnlyList<string> Patterns { get; }
    FamilyTree Import(string path);
}

/// <summary>A tool that works on the tree. It may change it (the program makes the run one undoable step).</summary>
public interface IToolPlugin : IPlugin
{
    /// <summary>Returns a sentence for the status line, e.g. "Fixed 12 dates".</summary>
    string Run(FamilyTree tree, Person? current);
}

/// <summary>One plugin found in the folder.</summary>
public sealed record PluginInfo(IPlugin Plugin, string File, string Kind)
{
    /// <summary>The key settings remember it by: file and class.</summary>
    public string Key => $"{Path.GetFileName(File)}:{Plugin.GetType().FullName}";
}

/// <summary>Finds and loads plugins. A library that fails to load is reported, never fatal.</summary>
public static class PluginHost
{
    public static IReadOnlyList<PluginInfo> Find(string folder, out IReadOnlyList<string> problems)
    {
        var found = new List<PluginInfo>();
        var errors = new List<string>();
        if (Directory.Exists(folder))
            foreach (var file in Directory.GetFiles(folder, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    // Each plugin in its own context, so two plugins can use different versions of a library.
                    var context = new PluginContext(file);
                    var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(file));
                    foreach (var type in SafeTypes(assembly))
                    {
                        if (type.IsAbstract || type.IsInterface || !typeof(IPlugin).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
                        if (Activator.CreateInstance(type) is IPlugin p) found.Add(new PluginInfo(p, file, KindOf(p)));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        problems = errors;
        return found;
    }

    /// <summary>
    /// Loads a plugin and the libraries beside it. Anything the program itself has loaded (Lineage.Core above all)
    /// comes from the program, so plugins and the program agree on what an <see cref="IPlugin"/> is.
    /// </summary>
    private sealed class PluginContext : AssemblyLoadContext
    {
        private readonly string _folder;
        public PluginContext(string file) : base(Path.GetFileNameWithoutExtension(file)) => _folder = Path.GetDirectoryName(Path.GetFullPath(file))!;

        protected override Assembly? Load(AssemblyName name)
        {
            if (Default.Assemblies.Any(a => AssemblyName.ReferenceMatchesDefinition(name, a.GetName()))) return null;
            var path = Path.Combine(_folder, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetExportedTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
    }

    private static string KindOf(IPlugin p) => p switch
    {
        IReportPlugin => "Report",
        IChartPlugin => "Chart",
        IImporterPlugin => "Importer",
        IToolPlugin => "Tool",
        _ => "Plugin",
    };
}
