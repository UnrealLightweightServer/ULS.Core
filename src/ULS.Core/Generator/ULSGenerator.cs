using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using ULS.Core;

namespace ULS.CodeGen;

public class UnrealProject
{
    public string ProjectName { get; set; } = string.Empty;

    public string ProjectFile { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public bool IsCodeGenerationEnabled { get; set; } = true;
}

[Generator]
public partial class ULSGenerator : IIncrementalGenerator
{
    internal const string Code_RpcCallAtLeastOne = "UR0001";
    internal const string Code_RpcCallFirstParam = "UR0002";
    internal const string Code_UnrealProjectAttribute = "UR0003";
    internal const string Code_UnrealCodeSkip = "UR0004";
    internal const string Code_UnrealProjectFile = "UR0005";
    internal const string Code_UnrealPluginInvalid = "UR0006";
    internal const string Code_UnrealMissingFile = "UR0007";

    internal const string Code_ClientIRpcTargetInterface = "UR0010";

    internal const string Code_ReplicationTypeNetworkObject = "UR0011";
    internal const string Code_ReplicationTypeNotPartial = "UR0012";
    internal const string Code_ReplicationInvalidPropertyType = "UR0013";
    internal const string Code_ReplicationTypeNotPrivate = "UR0014";

    internal const string Code_InvalidParam = "UR0020";

    internal const string Code_RpcCallNoNetworkObject = "UR0030";
    internal const string Code_RpcCallNotPartialType = "UR0031";

    internal const string Code_SpawnFunctionNoActor = "UR0040";
    internal const string Code_SpawnFunctionNoObject = "UR0041";

    internal const string Code_GeneratorFailure = "UR0900";

#if SIMPLE_LOGGING
    internal static string logFile = "D:\\Temp\\codegen_log_2.txt";

    private static void ClearLog()
    {
        File.WriteAllText(logFile, "");
    }

    private static void Log(string text)
    {
        File.AppendAllText(logFile, text + Environment.NewLine);
    }
#else
    private static void ClearLog()
    {
        //
    }

    private static void Log(string text)
    {
        //
    }
#endif

    #region IncrementalGenerator implementation
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        try
        {
            IncrementalValuesProvider<CSharpSyntaxNode> classDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (n, _) => IsSyntaxTargetForGeneration(n),
                    transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
                .Where(static m => m is not null)!;

            IncrementalValueProvider<(Compilation, ImmutableArray<CSharpSyntaxNode>)> compilationAndClasses
                = context.CompilationProvider.Combine(classDeclarations.Collect());

            context.RegisterSourceOutput(compilationAndClasses,
                static (spc, source) => Execute(source.Item1, source.Item2, spc));
        }
        catch (Exception ex)
        {
            Log("ERROR Initialize Incremental: " + ex);
        }
    }

    private static void Execute(Compilation compilation, ImmutableArray<CSharpSyntaxNode> nodes, 
        SourceProductionContext context)
    {
        if (nodes.IsDefaultOrEmpty)
        {
            // nothing to do yet
            return;
        }

        try
        {
            Log("Execute");

            var syntaxProvider = new IncrementalSyntaxProvider();
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                var semanticModel = compilation.GetSemanticModel(node.SyntaxTree);
                syntaxProvider.VisitNode(semanticModel, node);
            }

            // retrieve the populated receiver 
            if (!(syntaxProvider is IGeneratorContextProvider receiver))
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_GeneratorFailure, "", "Internal error in source generator (invalid SyntaxContextReceiver)",
                    "", DiagnosticSeverity.Error, true), null));
                return;
            }

            Log("BEGIN Execute C#");
            if (GenerateCSharpClasses(context, receiver) == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_GeneratorFailure, "", "Failed to generate C# server code (see previous errors)",
                    "", DiagnosticSeverity.Warning, true), null));
                Log("Failed to generate C# code");
            }
            Log("END Execute C#");

            bool generateClientCode = true;
            if (generateClientCode)
            {
                Log("BEGIN Execute C# Client");
                GenerateCSharpClientClasses(context, receiver);
                Log("END Execute C# Client");
            }

            Log("BEGIN Execute Unreal");
            GenerateUnrealClasses(context, receiver);
            Log("END Execute Unreal");
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                Code_GeneratorFailure, "", "Failed to execute code generator: " + ex,
                "", DiagnosticSeverity.Error, true), null));
            Log("ERROR Execute: " + ex);
        }
    }

    #region Build Semantic Targets
    private static CSharpSyntaxNode? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        try
        {
            if (context.Node is ClassDeclarationSyntax cds &&
                cds.AttributeLists.Count > 0)
            {
                return HandleClass(context, cds);
            }

            if (context.Node is MethodDeclarationSyntax mds &&
                mds.AttributeLists.Count > 0)
            {
                return HandleMethod(context, mds);
            }

            if (context.Node is EventFieldDeclarationSyntax eds &&
                eds.AttributeLists.Count > 0)
            {
                return HandleEvent(context, eds);
            }

            if (context.Node is FieldDeclarationSyntax fds &&
                fds.AttributeLists.Count > 0)
            {
                return HandleField(context, fds);
            }

            if (context.Node is InvocationExpressionSyntax ies)
            {
                return HandleInvocation(context, ies);
            }
        }
        catch (Exception ex)
        {
            Log("GetSemanticTargetForGeneration Exception: " + ex);
        }

        return null;
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        if (node is ClassDeclarationSyntax cds &&
            cds.AttributeLists.Count > 0)
        {
            return true;
        }

        if (node is MethodDeclarationSyntax mds &&
            mds.AttributeLists.Count > 0)
        {
            return true;
        }

        if (node is EventFieldDeclarationSyntax eds &&
            eds.AttributeLists.Count > 0)
        {
            return true;
        }

        if (node is FieldDeclarationSyntax fds &&
            fds.AttributeLists.Count > 0)
        {
            return true;
        }

        if (node is InvocationExpressionSyntax ies)
        {
            return true;
        }

        return false;
    }

    private static CSharpSyntaxNode? HandleInvocation(GeneratorSyntaxContext context, InvocationExpressionSyntax ies)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(ies);
        if (symbol.Symbol is IMethodSymbol ms)
        {
            var name = ms.Name;
            if (name == nameof(INetworkOwner.SpawnNetworkActor))
            {
                if (IsNetworkActor(ms.ReturnType) == false)
                {
                    return ies;
                }
            }
            if (name == nameof(INetworkOwner.SpawnNetworkObject))
            {
                if (IsNetworkActor(ms.ReturnType))
                {
                    return ies;
                }
            }
        }
        return null;
    }

    private static CSharpSyntaxNode? HandleField(GeneratorSyntaxContext context, FieldDeclarationSyntax fds)
    {
        foreach (var variable in fds.Declaration.Variables)
        {
            var fs = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
            if (fs == null)
            {
                return null;
            }
            if (fs.DeclaredAccessibility != Accessibility.Private)
            {
                return fds;
            }
            if (fs.Name == null ||
                fs.Name.StartsWith("_") == false)
            {
                return fds;
            }
            foreach (var attr in fs.GetAttributes())
            {
                if (attr.AttributeClass != null &&
                    attr.AttributeClass.ToDisplayString().EndsWith("ReplicateAttribute"))
                {
                    INamedTypeSymbol enclosingType = fs.ContainingType;

                    var typeDecl = fds.Parent as TypeDeclarationSyntax;
                    if (typeDecl == null)
                    {
                        continue;
                    }
                    if (typeDecl.Modifiers.All(x => x.Text != "partial"))
                    {
                        return fds;
                    }

                    return fds;
                }
            }
        }
        return null;
    }

    private static CSharpSyntaxNode? HandleClass(GeneratorSyntaxContext context, ClassDeclarationSyntax cds)
    {
        var ts = context.SemanticModel.GetDeclaredSymbol(cds) as INamedTypeSymbol;
        if (ts == null)
        {
            return null;
        }
        foreach (var attr in ts.GetAttributes())
        {
            if (attr.AttributeClass != null &&
                attr.AttributeClass.ToDisplayString().EndsWith("UnrealProjectAttribute"))
            {
                return cds;
            }

            if (attr.AttributeClass != null &&
                attr.AttributeClass.ToDisplayString().EndsWith("UnrealClassAttribute"))
            {
                return cds;
            }

            if (attr.AttributeClass != null &&
                attr.AttributeClass.ToDisplayString().EndsWith("CSharpClientAttribute"))
            {
                return cds;
            }
        }
        return null;
    }

    private static CSharpSyntaxNode? HandleEvent(GeneratorSyntaxContext context, EventFieldDeclarationSyntax eds)
    {
        foreach (var variable in eds.Declaration.Variables)
        {
            var es = context.SemanticModel.GetDeclaredSymbol(variable) as IEventSymbol;

            if (es != null)
            {
                foreach (var attr in es.GetAttributes())
                {
                    if (attr.AttributeClass != null &&
                        attr.AttributeClass.ToDisplayString().EndsWith("RpcCallAttribute"))
                    {
                        return eds;
                    }
                }
            }
        }
        return null;
    }

    private static CSharpSyntaxNode? HandleMethod(GeneratorSyntaxContext context, MethodDeclarationSyntax mds)
    {
        var ms = context.SemanticModel.GetDeclaredSymbol(mds) as IMethodSymbol;
        if (ms == null)
        {
            return null;
        }
        foreach (var attr in ms.GetAttributes())
        {
            if (attr.AttributeClass != null &&
                attr.AttributeClass.ToDisplayString().EndsWith("RpcCallAttribute"))
            {
                var outerType = ms.ContainingType;
                if (IsNetworkObject(outerType) == false)
                {
                    return mds;
                }

                var typeDecl = mds.Parent as TypeDeclarationSyntax;
                if (typeDecl == null)
                {
                    continue;
                }
                if (typeDecl.Modifiers.All(x => x.Text != "partial"))
                {
                    return mds;
                }

                return mds;
            }
        }
        return null;
    }
    #endregion
    #endregion

    #region Helpers
    private bool ImplementsInterface(ITypeSymbol typeSymbol, string name)
    {
        foreach (var intf in typeSymbol.AllInterfaces)
        {
            if (intf.Name == name)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsNetworkObject(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.Name == "NetworkObject")
        {
            return true;
        }

        var parent = typeSymbol.BaseType;
        if (parent == null)
        {
            return false;
        }
        if (parent.Name == "NetworkObject")
        {
            return true;
        }
        return IsNetworkObject(parent);
    }

    private static bool IsNetworkActor(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.Name == "NetworkActor")
        {
            return true;
        }

        var parent = typeSymbol.BaseType;
        if (parent == null)
        {
            return false;
        }
        if (parent.Name == "NetworkActor")
        {
            return true;
        }
        return IsNetworkActor(parent);
    }
    #endregion

    class IncrementalSyntaxProvider : IGeneratorContextProvider
    {
        public UnrealProjectAttribute? UnrealProject { get; set; } = null;

        #region Error cases
        public List<IFieldSymbol> ReplicationFieldsNotPrivate { get; } = new List<IFieldSymbol>();
        public List<INamedTypeSymbol> ReplicationMembersNotPartialTypes { get; } = new List<INamedTypeSymbol>();
        public List<IMethodSymbol> RpcCallsNoNetworkObject { get; } = new List<IMethodSymbol>();
        public List<INamedTypeSymbol> RpcCallNotPartialTypes { get; } = new List<INamedTypeSymbol>();
        public List<IEventSymbol> RpcEventsUsingCallStrategy { get; } = new List<IEventSymbol>();
        #endregion

        #region Code Gen Properties
        public List<InvocationExpressionSyntax> IncorrectSpawnActors { get; } = new List<InvocationExpressionSyntax>();
        public List<InvocationExpressionSyntax> IncorrectSpawnObjects { get; } = new List<InvocationExpressionSyntax>();

#pragma warning disable RS1024 // Symbols should be compared for equality
        public Dictionary<INamedTypeSymbol, List<IFieldSymbol>> ReplicationMembers { get; } = new Dictionary<INamedTypeSymbol, List<IFieldSymbol>>();

        public Dictionary<INamedTypeSymbol, List<IMethodSymbol>> RpcMethodsByType { get; } = new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>();
        public Dictionary<INamedTypeSymbol, List<IMethodSymbol>> UnrealGeneratedRpcMethodsByType { get; } = new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>();
        public Dictionary<INamedTypeSymbol, List<IMethodSymbol>> UnrealPartialReflRpcMethodsByType { get; } = new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>();
        public Dictionary<INamedTypeSymbol, List<IEventSymbol>> RpcEventsByType { get; } = new Dictionary<INamedTypeSymbol, List<IEventSymbol>>();
        public Dictionary<IEventSymbol, string[]> RpcEventParameterNameLookup { get; } = new Dictionary<IEventSymbol, string[]>();
#pragma warning restore RS1024 // Symbols should be compared for equality

        public Dictionary<string, List<INamedTypeSymbol>> UnrealClassTypeLookup { get; } = new Dictionary<string, List<INamedTypeSymbol>>();

        public List<INamedTypeSymbol> CSharpClientTypes { get; } = new List<INamedTypeSymbol>();
        #endregion

        #region VisitNode
        internal void VisitNode(SemanticModel semanticModel, CSharpSyntaxNode node)
        {
            try
            {
                if (node is ClassDeclarationSyntax cds &&
                    cds.AttributeLists.Count > 0)
                {
                    HandleClass(semanticModel, cds);
                }

                if (node is MethodDeclarationSyntax mds &&
                    mds.AttributeLists.Count > 0)
                {
                    HandleMethod(semanticModel, mds);
                }

                if (node is EventFieldDeclarationSyntax eds &&
                    eds.AttributeLists.Count > 0)
                {
                    HandleEvent(semanticModel, eds);
                }

                if (node is FieldDeclarationSyntax fds &&
                    fds.AttributeLists.Count > 0)
                {
                    HandleField(semanticModel, fds);
                }

                if (node is InvocationExpressionSyntax ies)
                {
                    HandleInvocation(semanticModel, ies);
                }
            }
            catch (Exception ex)
            {
                Log("EXCEPTION VISIT: " + ex);
            }
        }

        private void HandleInvocation(SemanticModel semanticModel, InvocationExpressionSyntax ies)
        {
            var symbol = semanticModel.GetSymbolInfo(ies);
            if (symbol.Symbol is IMethodSymbol ms)
            {
                var name = ms.Name;
                if (name == nameof(INetworkOwner.SpawnNetworkActor))
                {
                    if (IsNetworkActor(ms.ReturnType) == false)
                    {
                        IncorrectSpawnActors.Add(ies);
                    }
                }
                if (name == nameof(INetworkOwner.SpawnNetworkObject))
                {
                    if (IsNetworkActor(ms.ReturnType))
                    {
                        IncorrectSpawnObjects.Add(ies);
                    }
                }
            }
        }

        private void HandleField(SemanticModel semanticModel, FieldDeclarationSyntax fds)
        {
            foreach (var variable in fds.Declaration.Variables)
            {
                var fs = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fs == null)
                {
                    return;
                }
                if (fs.DeclaredAccessibility != Accessibility.Private)
                {
                    ReplicationFieldsNotPrivate.Add(fs);
                    return;
                }
                if (fs.Name == null ||
                    fs.Name.StartsWith("_") == false)
                {
                    ReplicationFieldsNotPrivate.Add(fs);
                    return;
                }
                foreach (var attr in fs.GetAttributes())
                {
                    if (attr.AttributeClass != null &&
                        attr.AttributeClass.ToDisplayString().EndsWith("ReplicateAttribute"))
                    {
                        INamedTypeSymbol enclosingType = fs.ContainingType;

                        var typeDecl = fds.Parent as TypeDeclarationSyntax;
                        if (typeDecl == null)
                        {
                            continue;
                        }
                        if (typeDecl.Modifiers.All(x => x.Text != "partial"))
                        {
                            ReplicationMembersNotPartialTypes.Add(enclosingType);
                            return;
                        }

                        List<IFieldSymbol> list;
                        if (ReplicationMembers.TryGetValue(enclosingType, out list) == false)
                        {
                            list = new List<IFieldSymbol>();
                            ReplicationMembers[enclosingType] = list;
                        }
                        list.Add(fs);
                    }
                }
            }
        }

        private void HandleClass(SemanticModel semanticModel, ClassDeclarationSyntax cds)
        {
            var ts = semanticModel.GetDeclaredSymbol(cds) as INamedTypeSymbol;
            if (ts == null)
            {
                return;
            }
            foreach (var attr in ts.GetAttributes())
            {
                if (attr.AttributeClass != null &&
                    attr.AttributeClass.ToDisplayString().EndsWith("UnrealProjectAttribute"))
                {
                    UnrealProject = new UnrealProjectAttribute();

                    foreach (var attrData in attr.ConstructorArguments)
                    {
                        //Log("c attrData: " + attrData.ToString());
                    }
                    foreach (var attrData in attr.NamedArguments)
                    {
                        switch (attrData.Key)
                        {
                            case "IsCodeGenerationEnabled":
                                UnrealProject.IsCodeGenerationEnabled = attrData.Value.Value != null ? (bool)attrData.Value.Value : false;
                                break;
                            case "ProjectName":
                                UnrealProject.ProjectName = attrData.Value.Value != null ? (string)attrData.Value.Value : string.Empty;
                                break;
                            case "ProjectFile":
                                UnrealProject.ProjectFile = attrData.Value.Value != null ? (string)attrData.Value.Value : string.Empty;
                                break;
                            case "Module":
                                UnrealProject.Module = attrData.Value.Value != null ? (string)attrData.Value.Value : string.Empty;
                                break;
                        }
                    }
                }

                if (attr.AttributeClass != null &&
                    attr.AttributeClass.ToDisplayString().EndsWith("UnrealClassAttribute"))
                {
                    string? unrealClass = null;
                    foreach (var attrData in attr.NamedArguments)
                    {
                        switch (attrData.Key)
                        {
                            case "ClassName":
                                unrealClass = (string?)attrData.Value.Value;
                                break;
                        }
                    }

                    if (unrealClass != null)
                    {
                        List<INamedTypeSymbol> list;
                        if (UnrealClassTypeLookup.TryGetValue(unrealClass, out list) == false)
                        {
                            list = new List<INamedTypeSymbol>();
                            UnrealClassTypeLookup[unrealClass] = list;
                        }
                        list.Add(ts);
                    }
                }

                if (attr.AttributeClass != null &&
                    attr.AttributeClass.ToDisplayString().EndsWith("CSharpClientAttribute"))
                {
                    CSharpClientTypes.Add(ts);
                }
            }
        }

        private void HandleEvent(SemanticModel semanticModel, EventFieldDeclarationSyntax eds)
        {
            foreach (var variable in eds.Declaration.Variables)
            {
                var es = semanticModel.GetDeclaredSymbol(variable) as IEventSymbol;

                if (es != null)
                {
                    foreach (var attr in es.GetAttributes())
                    {
                        if (attr.AttributeClass != null &&
                            attr.AttributeClass.ToDisplayString().EndsWith("RpcCallAttribute"))
                        {
                            foreach (var attrData in attr.NamedArguments)
                            {
                                switch (attrData.Key)
                                {
                                    case "ParameterNames":
                                        {
                                            string[] values = new string[attrData.Value.Values.Length];
                                            for (int i = 0; i < values.Length; i++)
                                            {
                                                var val = attrData.Value.Values[i].Value;
                                                values[i] = val != null ? (string)val : string.Empty;
                                            }
                                            RpcEventParameterNameLookup[es] = values;
                                        }
                                        break;

                                    case "CallStrategy":
                                        {
                                            RpcEventsUsingCallStrategy.Add(es);
                                            return;
                                        }
                                }
                            }

                            List<IEventSymbol> list;
                            if (RpcEventsByType.TryGetValue(es.ContainingType, out list) == false)
                            {
                                list = new List<IEventSymbol>();
                                RpcEventsByType[es.ContainingType] = list;
                            }

                            list.Add(es);

                            break;
                        }
                    }
                }
            }
        }

        private void HandleMethod(SemanticModel semanticModel, MethodDeclarationSyntax mds)
        {
            var ms = semanticModel.GetDeclaredSymbol(mds) as IMethodSymbol;
            if (ms == null)
            {
                return;
            }
            foreach (var attr in ms.GetAttributes())
            {
                if (attr.AttributeClass != null &&
                    attr.AttributeClass.ToDisplayString().EndsWith("RpcCallAttribute"))
                {
                    var outerType = ms.ContainingType;
                    if (IsNetworkObject(outerType) == false)
                    {
                        RpcCallsNoNetworkObject.Add(ms);
                        return;
                    }

                    var typeDecl = mds.Parent as TypeDeclarationSyntax;
                    if (typeDecl == null)
                    {
                        continue;
                    }
                    if (typeDecl.Modifiers.All(x => x.Text != "partial"))
                    {
                        RpcCallNotPartialTypes.Add(outerType);
                        return;
                    }

                    RpcCallAttribute defaultAttrib = new RpcCallAttribute();
                    CallStrategy callStrategyToUse = defaultAttrib.CallStrategy;
                    foreach (var attrData in attr.NamedArguments)
                    {
                        switch (attrData.Key)
                        {
                            case "CallStrategy":
                                {
                                    if (attrData.Value.Value is CallStrategy strat)
                                    {
                                        callStrategyToUse = strat;
                                    }
                                }
                                break;
                        }
                    }

                    switch (callStrategyToUse)
                    {
                        case CallStrategy.GenerateInWrapperClass:
                            {
                                List<IMethodSymbol> theList;
                                if (UnrealGeneratedRpcMethodsByType.TryGetValue(outerType, out theList) == false)
                                {
                                    theList = new List<IMethodSymbol>();
                                    UnrealGeneratedRpcMethodsByType[outerType] = theList;
                                }
                                theList.Add(ms);
                            }
                            break;
                        case CallStrategy.PartialReflection:
                            {
                                List<IMethodSymbol> theList;
                                if (UnrealPartialReflRpcMethodsByType.TryGetValue(outerType, out theList) == false)
                                {
                                    theList = new List<IMethodSymbol>();
                                    UnrealPartialReflRpcMethodsByType[outerType] = theList;
                                }
                                theList.Add(ms);
                            }
                            break;
                        case CallStrategy.FullReflection:
                            // Nothing to do here, this will only be checked by the server generated code
                            // and that will look directly into RpcMethodsByType
                            break;
                    }

                    List<IMethodSymbol> list;
                    if (RpcMethodsByType.TryGetValue(outerType, out list) == false)
                    {
                        list = new List<IMethodSymbol>();
                        RpcMethodsByType[outerType] = list;
                    }
                    list.Add(ms);
                }
            }
        }
        #endregion
    }
}
