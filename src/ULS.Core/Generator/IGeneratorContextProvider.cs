using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ULS.Core;

namespace ULS.CodeGen
{
    internal interface IGeneratorContextProvider
    {
        List<INamedTypeSymbol> CSharpClientTypes { get; }
        List<InvocationExpressionSyntax> IncorrectSpawnActors { get; }
        List<InvocationExpressionSyntax> IncorrectSpawnObjects { get; }
        List<IFieldSymbol> ReplicationFieldsNotPrivate { get; }
        Dictionary<INamedTypeSymbol, List<IFieldSymbol>> ReplicationMembers { get; }
        List<INamedTypeSymbol> ReplicationMembersNotPartialTypes { get; }
        List<INamedTypeSymbol> RpcCallNotPartialTypes { get; }
        List<IMethodSymbol> RpcCallsNoNetworkObject { get; }
        Dictionary<IEventSymbol, string[]> RpcEventParameterNameLookup { get; }
        Dictionary<INamedTypeSymbol, List<IEventSymbol>> RpcEventsByType { get; }
        List<IEventSymbol> RpcEventsUsingCallStrategy { get; }
        Dictionary<INamedTypeSymbol, List<IMethodSymbol>> RpcMethodsByType { get; }
        Dictionary<string, List<INamedTypeSymbol>> UnrealClassTypeLookup { get; }
        Dictionary<INamedTypeSymbol, List<IMethodSymbol>> UnrealGeneratedRpcMethodsByType { get; }
        Dictionary<INamedTypeSymbol, List<IMethodSymbol>> UnrealPartialReflRpcMethodsByType { get; }
        UnrealProjectAttribute? UnrealProject { get; set; }
    }
}