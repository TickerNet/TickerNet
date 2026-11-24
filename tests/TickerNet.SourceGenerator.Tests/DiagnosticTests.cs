using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TickerNet.SourceGenerator;

namespace TickerNet.SourceGenerator.Tests;

/// <summary>
/// Each source-generator diagnostic (TN001-TN011) is triggered by a small
/// snippet, so a descriptor or validator change cannot silently stop reporting.
/// </summary>
public class DiagnosticTests
{
    private const string StubTypes = @"
namespace TickerNet.Utilities.Base
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class TickerFunctionAttribute : System.Attribute
    {
        public TickerFunctionAttribute(string functionName, string cronExpression = null, int taskPriority = 0, int maxConcurrency = 0) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Constructor)]
    public class TickerNetConstructorAttribute : System.Attribute { }

    public class TickerFunctionContext { }
    public class TickerFunctionContext<T> : TickerFunctionContext { public T Request { get; } }
}

namespace TickerNet.Utilities.Interfaces
{
    public interface ITickerFunctionBase { }

    public interface ITickerFunction : ITickerFunctionBase
    {
        System.Threading.Tasks.Task ExecuteAsync(
            TickerNet.Utilities.Base.TickerFunctionContext context,
            System.Threading.CancellationToken cancellationToken = default);
    }

    public interface ITickerFunction<TRequest> : ITickerFunctionBase
    {
        System.Threading.Tasks.Task ExecuteAsync(
            TickerNet.Utilities.Base.TickerFunctionContext<TRequest> context,
            System.Threading.CancellationToken cancellationToken = default);
    }
}

namespace TickerNet.Utilities
{
    public delegate System.Threading.Tasks.Task TickerFunctionDelegate(
        System.Threading.CancellationToken cancellationToken,
        System.IServiceProvider serviceProvider,
        TickerNet.Utilities.Base.TickerFunctionContext context);

    public static class TickerFunctionProvider
    {
        public static void RegisterFunctions(
            System.Collections.Generic.Dictionary<string, (string, TickerNet.Utilities.Enums.TickerTaskPriority, TickerFunctionDelegate, int)> f, int c) { }
        public static void RegisterRequestType(
            System.Collections.Generic.Dictionary<string, (string, System.Type)> t, int c) { }
    }
}

namespace TickerNet.Utilities.Enums
{
    public enum TickerTaskPriority { Normal = 0 }
}
";

    private const string Usings = @"
using System.Threading;
using System.Threading.Tasks;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Interfaces;
";

    [Fact]
    public void TN001_class_without_public_or_internal_modifier()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    class HiddenJob
    {
        [TickerFunction(""hidden"")]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN001", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN002_method_without_public_or_internal_modifier()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class HiddenMethodJob
    {
        [TickerFunction(""hidden-method"")]
        Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN002", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN003_invalid_cron_expression()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class BadCronJob
    {
        [TickerFunction(""bad-cron"", ""not a cron"")]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN003", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN004_missing_function_name()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class MissingNameJob
    {
        [TickerFunction("""")]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN004", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN005_duplicate_function_name()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class FirstJob
    {
        [TickerFunction(""same-name"")]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class SecondJob
    {
        [TickerFunction(""same-name"")]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN005", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN006_multiple_constructors()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class ManyConstructorsJob
    {
        public ManyConstructorsJob() { }
        public ManyConstructorsJob(int value) { }

        [TickerFunction(""many-constructors"")]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN006", DiagnosticSeverity.Warning);
    }

    [Fact]
    public void TN007_abstract_class()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public abstract class AbstractJob
    {
        [TickerFunction(""abstract"")]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN007", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN008_nested_class()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class Outer
    {
        public class NestedJob
        {
            [TickerFunction(""nested"")]
            public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}");
        AssertDiagnostic(diagnostics, "TN008", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN009_invalid_method_parameter()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class BadParameterJob
    {
        [TickerFunction(""bad-parameter"")]
        public Task Run(string unexpected, TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN009", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN010_multiple_ticker_net_constructor_attributes()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class TwoMarkedConstructorsJob
    {
        [TickerNetConstructor]
        public TwoMarkedConstructorsJob() { }

        [TickerNetConstructor]
        public TwoMarkedConstructorsJob(int value) { }

        [TickerFunction(""two-marked"")]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN010", DiagnosticSeverity.Error);
    }

    [Fact]
    public void TN011_negative_max_concurrency()
    {
        var diagnostics = GetDiagnostics(Usings + @"
namespace TestApp
{
    public class NegativeConcurrencyJob
    {
        [TickerFunction(""negative-concurrency"", maxConcurrency: -1)]
        public Task Run(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}");
        AssertDiagnostic(diagnostics, "TN011", DiagnosticSeverity.Error);
    }

    private static void AssertDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id, DiagnosticSeverity severity)
    {
        Assert.Contains(diagnostics, d => d.Id == id && d.Severity == severity);
    }

    private static ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new TickerNetIncrementalSourceGenerator();
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var stubTree = CSharpSyntaxTree.ParseText(StubTypes, parseOptions);

        var references = new List<MetadataReference>();
        var trustedPaths = ((string?)AppDomain.CurrentDomain.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var path in trustedPaths)
        {
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree, stubTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
