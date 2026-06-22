using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Silo.Foundry;

public static class FoundryCompilation
{
    public static CSharpCompilation Create(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release);
        return CSharpCompilation.Create(assemblyName, new[] { tree }, references, options);
    }

    public static IReadOnlyList<MetadataReference> DefaultReferences()
    {
        var refs = new List<MetadataReference>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                continue;
            refs.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
        return refs;
    }
}
