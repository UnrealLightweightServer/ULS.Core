using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ULS.CodeGen;

public class UnrealProject
{
    public string ProjectName { get; set; } = string.Empty;

    public string ProjectFile { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public bool IsCodeGenerationEnabled { get; set; } = true;
}

/*
[Generator]
public class ULSIncrementalGenerator : IIncrementalGenerator
{
    internal static string logFile = "D:\\Temp\\codegen_log_4.txt";

    private static void Log(string text)
    {
        File.AppendAllText(logFile, text + Environment.NewLine);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        Log("ULSIncrementalGenerator: Initialize");
        context.RegisterPostInitializationOutput((ctx) =>
        {
            Log("ULSIncrementalGenerator: RegisterPostInitializationOutput");
        });

        IncrementalValuesProvider<CSharpSyntaxNode> assignments = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsSyntaxNodeForGeneration(node),
                transform: static (ctx, _) => GetTransform(ctx))
            .Where(static m => m is AssignmentExpressionSyntax);

        IncrementalValueProvider<(Compilation, ImmutableArray<CSharpSyntaxNode>)> compilationAndClasses =
                context.CompilationProvider.Combine(assignments.Collect());

        context.RegisterSourceOutput(compilationAndClasses, (ctx, src) =>
        {
            Log("ULSIncrementalGenerator: Execute 1: " + ctx);
            Log("ULSIncrementalGenerator: Execute 2: " + src.Item1);
            Log("ULSIncrementalGenerator: Execute 3: " + src.Item2);

            var item = src.Item1;
            item.Emit("d:\\Temp\\Program.exe");

            Log("ULSIncrementalGenerator: Execute 4");
        });
    }

    static bool IsSyntaxNodeForGeneration(SyntaxNode node)
    {        
        var res = node is AssignmentExpressionSyntax;
        if (res)
        {
            Log("IsSyntaxNodeForGeneration: " + node);
        }
        return res;
    }

    static CSharpSyntaxNode GetTransform(GeneratorSyntaxContext ctx)
    {
        Log("GetTransform 1: " + ctx.Node);

        var parent = ctx.Node.Parent as ExpressionStatementSyntax;
        Log("GetTransform 2: " + parent);
        if (parent == null)
        {
            return (CSharpSyntaxNode)ctx.Node;
        }

        BlockSyntax newNode = SyntaxFactory.Block()
            .AddStatements(
                parent,
                SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("Console"),
                        SyntaxFactory.IdentifierName("WriteLine")))
                .WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList<ArgumentSyntax>(
                            SyntaxFactory.Argument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    SyntaxFactory.Literal("Hello World")))))))
                );

        Log("GetTransform 3: " + newNode);
        return (CSharpSyntaxNode)newNode;
    }
}
*/

[Generator]
public partial class ULSGenerator : ISourceGenerator
{
    internal const string Code_RpcCallAtLeastOne = "UR0001";
    internal const string Code_RpcCallFirstParam = "UR0002";
    internal const string Code_UnrealProjectAttribute = "UR0003";
    internal const string Code_UnrealCodeSkip = "UR0004";
    internal const string Code_UnrealProjectFile = "UR0005";
    internal const string Code_UnrealModuleInvalid = "UR0006";

    internal const string Code_ClientIRpcTargetInterface = "UR0010";

    internal const string Code_ReplicationTypeNetworkActor = "UR0011";
    internal const string Code_ReplicationTypeNotPartial = "UR0012";
    internal const string Code_ReplicationInvalidPropertyType = "UR0013";

    internal const string Code_InvalidParam = "UR0020";

    internal static string logFile = "D:\\Temp\\codegen_log_2.txt";

    private void ClearLog()
    {
        File.WriteAllText(logFile, "");
    }

    private void Log(string text)
    {
        File.AppendAllText(logFile, text + Environment.NewLine);
    }

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

    public void Execute(GeneratorExecutionContext context)
    {
        try
        {
            Log("Execute");
            // retrieve the populated receiver 
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            Log("BEGIN Execute C#");
            GenerateCSharpClasses(context, receiver);
            Log("END Execute C#");

            Log("BEGIN Execute Unreal");
            GenerateUnrealClasses(context, receiver);
            Log("END Execute Unreal");

            Log("BEGIN Execute C# Client");
            GenerateCSharpClientClasses(context, receiver);
            Log("END Execute C# Client");
        }
        catch (Exception ex)
        {
            Log("ERROR Execute: " + ex);
        }
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        try
        {
            ClearLog();
            Log("Initialize");

            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }
        catch (Exception ex)
        {
            Log("ERROR Initialize: " + ex);
        }
    }

    private bool IsSubclassOf(ITypeSymbol typeSymbol, string name)
    {
        var parent = typeSymbol.BaseType;
        if (parent == null)
        {
            return false;
        }
        if (parent.Name == name)
        {
            return true;
        }
        return IsSubclassOf(parent, name);
    }

    private bool IsNetworkActor(ITypeSymbol typeSymbol)
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

    /// <summary>
    /// Created on demand before each generation pass
    /// </summary>
    class SyntaxReceiver : ISyntaxContextReceiver
    {
        public UnrealProject UnrealProject { get; set; } = null;

        public List<INamedTypeSymbol> ReplicationMembersNotPartialTypes = new List<INamedTypeSymbol>();
        public Dictionary<INamedTypeSymbol, List<ISymbol>> ReplicationMembers = new Dictionary<INamedTypeSymbol, List<ISymbol>>();

        public List<IFieldSymbol> ImmediateReplicationFields = new List<IFieldSymbol>();

        public Dictionary<INamedTypeSymbol, List<IMethodSymbol>> RpcMethodsByType { get; } = new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>();
        public Dictionary<INamedTypeSymbol, List<IEventSymbol>> RpcEventsByType { get; } = new Dictionary<INamedTypeSymbol, List<IEventSymbol>>();
        public Dictionary<IEventSymbol, string[]> RpcEventParameterNameLookup { get; } = new Dictionary<IEventSymbol, string[]>();

        public Dictionary<string, List<INamedTypeSymbol>> UnrealClassTypeLookup { get; } = new Dictionary<string, List<INamedTypeSymbol>>();

        public List<INamedTypeSymbol> CSharpClientTypes { get; } = new List<INamedTypeSymbol>();

        /// <summary>
        /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation
        /// </summary>
        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            try
            {
                if (context.Node is ClassDeclarationSyntax cds &&
                    cds.AttributeLists.Count > 0)
                {
                    HandleClass(context, cds);
                }

                if (context.Node is MethodDeclarationSyntax mds &&
                    mds.AttributeLists.Count > 0)
                {
                    HandleMethod(context, mds);
                }

                if (context.Node is EventFieldDeclarationSyntax eds &&
                    eds.AttributeLists.Count > 0)
                {
                    HandleEvent(context, eds);
                }

                if (context.Node is PropertyDeclarationSyntax pds &&
                    pds.AttributeLists.Count > 0)
                {
                    HandleProperty(context, pds);
                }

                if (context.Node is FieldDeclarationSyntax fds &&
                    fds.AttributeLists.Count > 0)
                {
                    HandleField(context, fds);
                }
            }
            catch (Exception ex)
            {
                Log("EXCEPTION VISIT: " + ex);
            }
        }

        private void HandleField(GeneratorSyntaxContext context, FieldDeclarationSyntax fds)
        {
            foreach (var variable in fds.Declaration.Variables)
            {
                var fs = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                foreach (var attr in fs.GetAttributes())
                {
                    if (attr.AttributeClass.ToDisplayString().EndsWith("ReplicateAttribute"))
                    {
                        INamedTypeSymbol enclosingType = fs.ContainingType;

                        var typeDecl = fds.Parent as TypeDeclarationSyntax;
                        if (typeDecl.Modifiers.All(x => x.Text != "partial"))
                        {
                            ReplicationMembersNotPartialTypes.Add(enclosingType);
                            return;
                        }

                        List<ISymbol> list;
                        if (ReplicationMembers.TryGetValue(enclosingType, out list) == false)
                        {
                            list = new List<ISymbol>();
                            ReplicationMembers[enclosingType] = list;
                        }
                        list.Add(fs);

                        foreach (var attrData in attr.NamedArguments)
                        {
                            switch (attrData.Key)
                            {
                                case "ReplicationStrategy":
                                    {
                                        string strValue = attrData.Value.ToCSharpString();
                                        if (strValue.EndsWith("Immediate"))
                                        {
                                            ImmediateReplicationFields.Add(fs);
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }

        private void HandleProperty(GeneratorSyntaxContext context, PropertyDeclarationSyntax pds)
        {
            var ps = context.SemanticModel.GetDeclaredSymbol(pds) as IPropertySymbol;
            foreach (var attr in ps.GetAttributes())
            {
                if (attr.AttributeClass.ToDisplayString().EndsWith("ReplicateAttribute"))
                {
                    INamedTypeSymbol enclosingType = ps.ContainingType;

                    var typeDecl = pds.Parent as TypeDeclarationSyntax;
                    if (typeDecl.Modifiers.All(x => x.Text != "partial"))
                    {
                        ReplicationMembersNotPartialTypes.Add(enclosingType);
                        return;
                    }

                    List<ISymbol> list;
                    if (ReplicationMembers.TryGetValue(enclosingType, out list) == false)
                    {
                        list = new List<ISymbol>();
                        ReplicationMembers[enclosingType] = list;
                    }
                    list.Add(ps);
                }
            }
        }

        private void HandleClass(GeneratorSyntaxContext context, ClassDeclarationSyntax cds)
        {
            var ts = context.SemanticModel.GetDeclaredSymbol(cds) as INamedTypeSymbol;
            foreach (var attr in ts.GetAttributes())
            {
                if (attr.AttributeClass.ToDisplayString().EndsWith("UnrealProjectAttribute"))
                {
                    UnrealProject = new UnrealProject();

                    foreach (var attrData in attr.ConstructorArguments)
                    {
                        //Log("c attrData: " + attrData.ToString());
                    }
                    foreach (var attrData in attr.NamedArguments)
                    {
                        switch (attrData.Key)
                        {
                            case "IsCodeGenerationEnabled":
                                UnrealProject.IsCodeGenerationEnabled = (bool)attrData.Value.Value;
                                break;
                            case "ProjectName":
                                UnrealProject.ProjectName = (string)attrData.Value.Value;
                                break;
                            case "ProjectFile":
                                UnrealProject.ProjectFile = (string)attrData.Value.Value;
                                break;
                            case "Module":
                                UnrealProject.Module = (string)attrData.Value.Value;
                                break;
                        }
                    }
                }

                if (attr.AttributeClass.ToDisplayString().EndsWith("UnrealClassAttribute"))
                {
                    string unrealClass = null;
                    foreach (var attrData in attr.NamedArguments)
                    {
                        switch (attrData.Key)
                        {
                            case "ClassName":
                                unrealClass = (string)attrData.Value.Value;
                                break;
                        }
                    }

                    List<INamedTypeSymbol> list;
                    if (UnrealClassTypeLookup.TryGetValue(unrealClass, out list) == false)
                    {
                        list = new List<INamedTypeSymbol>();
                        UnrealClassTypeLookup[unrealClass] = list;
                    }
                    list.Add(ts);
                }

                if (attr.AttributeClass.ToDisplayString().EndsWith("CSharpClientAttribute"))
                {
                    CSharpClientTypes.Add(ts);
                }
            }
        }

        private void HandleEvent(GeneratorSyntaxContext context, EventFieldDeclarationSyntax eds)
        {
            foreach (var variable in eds.Declaration.Variables)
            {
                var es = context.SemanticModel.GetDeclaredSymbol(variable) as IEventSymbol;

                if (es != null)
                {
                    foreach (var attr in es.GetAttributes())
                    {
                        if (attr.AttributeClass.ToDisplayString().EndsWith("RpcCallAttribute"))
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
                                                values[i] = (string)attrData.Value.Values[i].Value;
                                            }
                                            RpcEventParameterNameLookup[es] = values;
                                        }                                            
                                        break;
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

        private void HandleMethod(GeneratorSyntaxContext context, MethodDeclarationSyntax mds)
        {
            var ms = context.SemanticModel.GetDeclaredSymbol(mds) as IMethodSymbol;
            foreach (var attr in ms.GetAttributes())
            {
                if (attr.AttributeClass.ToDisplayString().EndsWith("RpcCallAttribute"))
                {
                    List<IMethodSymbol> list;
                    if (RpcMethodsByType.TryGetValue(ms.ContainingType, out list) == false)
                    {
                        list = new List<IMethodSymbol>();
                        RpcMethodsByType[ms.ContainingType] = list;
                    }

                    list.Add(ms);
                }
            }
        }            

        private void Log(string text)
        {
            File.AppendAllText(logFile, text + Environment.NewLine);
        }
    }
}
