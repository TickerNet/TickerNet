using Microsoft.CodeAnalysis;

namespace TickerNet.SourceGenerator.Validation
{
    /// <summary>
    /// Contains all diagnostic descriptors used by the TickerNet source generator.
    /// </summary>
    internal static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor ClassAccessibility = new DiagnosticDescriptor(
            "TN001",
            "Class accessibility issue",
            "The class '{0}' should be public or internal to be used with [TickerFunction]",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor MethodAccessibility = new DiagnosticDescriptor(
            "TN002",
            "Method accessibility issue",
            "The method '{0}' should be public or internal to be used with [TickerFunction]",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor InvalidCronExpression = new DiagnosticDescriptor(
            "TN003",
            "Invalid cron expression",
            "The cron expression '{0}' in function '{1}' is invalid",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor MissingFunctionName = new DiagnosticDescriptor(
            "TN004",
            "Missing function name",
            "The [TickerFunction] attribute on method '{0}' in class '{1}' must specify a function name",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor DuplicateFunctionName = new DiagnosticDescriptor(
            "TN005",
            "Duplicate function name",
            "The function name '{0}' is already used by another [TickerFunction] method",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor MultipleConstructors = new DiagnosticDescriptor(
            "TN006",
            "Multiple constructors detected",
            "The class '{0}' has multiple constructors. Only the first constructor will be used for dependency injection. Consider using [TickerNetConstructor] attribute to explicitly mark the preferred constructor.",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Warning,
            true
        );

        public static readonly DiagnosticDescriptor AbstractClass = new DiagnosticDescriptor(
            "TN007",
            "Abstract class with TickerFunction",
            "The abstract class '{0}' contains [TickerFunction] methods",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor NestedClass = new DiagnosticDescriptor(
            "TN008",
            "Nested class with TickerFunction",
            "The nested class '{0}' contains [TickerFunction] methods. TickerFunction methods are only allowed in top-level classes.",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor InvalidMethodParameter = new DiagnosticDescriptor(
            "TN009",
            "Invalid TickerFunction parameter",
            "The method '{0}' has invalid parameter '{1}' of type '{2}'. TickerFunction methods can only have TickerFunctionContext, TickerFunctionContext<T>, CancellationToken parameters, or no parameters.",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor MultipleTickerNetConstructorAttributes = new DiagnosticDescriptor(
            "TN010",
            "Multiple TickerNetConstructor attributes",
            "The class '{0}' has multiple constructors with [TickerNetConstructor] attribute. Only one constructor can be marked with [TickerNetConstructor].",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor InvalidMaxConcurrency = new DiagnosticDescriptor(
            "TN011",
            "Invalid maxConcurrency value",
            "The maxConcurrency value for function '{0}' must be >= 0 (0 = unlimited)",
            "TickerNet.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );
    }
}
