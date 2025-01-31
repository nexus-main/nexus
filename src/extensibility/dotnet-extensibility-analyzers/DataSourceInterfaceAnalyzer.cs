using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DataSourceInterfaceAnalyzer : DiagnosticAnalyzer
{
    internal const string DiagnosticId = "NE0001";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
#pragma warning disable RS2008 // Enable analyzer release tracking
        id: DiagnosticId,
#pragma warning restore RS2008 // Enable analyzer release tracking
        title: "Invalid use of IDataSource",
        messageFormat: "Invalid use of IDataSource. Use IDataSource<T> instead.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "IDataSource is for internal use only. Use IDataSource<T> instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterSyntaxNodeAction(AnalyzeIDataSourceUsage, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeIDataSourceUsage(SyntaxNodeAnalysisContext context)
    {
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

        if (classDeclarationSyntax.BaseList is null)
            return;

        var index = classDeclarationSyntax.BaseList.Types
            .IndexOf(type => type.NormalizeWhitespace().ToString() == "IDataSource");

        if (index >= 0)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, classDeclarationSyntax.BaseList.Types[index].GetLocation())
            );
        }
    }
}