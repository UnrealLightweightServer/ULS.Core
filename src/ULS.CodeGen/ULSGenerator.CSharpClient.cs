using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ULS.CodeGen
{
    public partial class ULSGenerator
    {
        private void GenerateCSharpClientClasses(GeneratorExecutionContext context, SyntaxReceiver receiver)
        {
            if (receiver.CSharpClientTypes.Count == 0)
            {
                return;
            }

            var clientType = receiver.CSharpClientTypes[0];

            bool intfFound = ImplementsInterface(clientType, "IRpcTarget");
            if (intfFound == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_ClientIRpcTargetInterface, "", "Classes with CSharpClientAttribute must implement the IRpcTarget interface",
                    "", DiagnosticSeverity.Error, true),
                    clientType.Locations.Length > 0 ? clientType.Locations[0] : null));
                return;
            }

            List<IEventSymbol> events = new List<IEventSymbol>();
            foreach (var pair in receiver.RpcEventsByType)
            {
                events.AddRange(pair.Value);
            }

            List<IMethodSymbol> methods = new List<IMethodSymbol>();
            foreach (var pair in receiver.RpcMethodsByType)
            {
                methods.AddRange(pair.Value);
            }

            GenerateCSharpClientEvents(context, clientType, events);

            GenerateCSharpClientEventHandlers(context, clientType, methods);
        }

        private void GenerateCSharpClientEventHandlers(GeneratorExecutionContext context, INamedTypeSymbol typeSymbol, List<IMethodSymbol> methods)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using ULS.Core;");
            sb.AppendLine($"");
            sb.AppendLine($"namespace {typeSymbol.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine($"{{");
            sb.AppendLine($"   partial class {typeSymbol.Name}");
            sb.AppendLine($"   {{");

            sb.AppendLine($"      partial void Client_ProcessRpcMethod(RpcPayload rpc)");
            sb.AppendLine($"      {{");
            sb.AppendLine($"         switch (rpc.MethodName)");
            sb.AppendLine($"         {{");
            foreach (var item in methods)
            {
                sb.AppendLine($"            case \"{item.Name}\":");
                sb.AppendLine($"               ProcessRpc{item.Name}(rpc);");
                sb.AppendLine($"               break;");
            }

            sb.AppendLine($"            default:");
            sb.AppendLine($"               // If unhandled here, let parent method try to handle the RPC call");
            sb.AppendLine($"               HandleRpcPacket(rpc);");
            sb.AppendLine($"               break;");

            sb.AppendLine($"         }}");
            sb.AppendLine($"      }}");
            sb.AppendLine($"      ");

            foreach (var item in methods)
            {
                string delegateParameters = string.Empty;
                if (item.Parameters.Length > 0)
                {
                    for (int j = 0; j < item.Parameters.Length; j++)
                    {
                        if (j > 0)
                        {
                            delegateParameters += ", ";
                        }

                        delegateParameters += item.Parameters[j].Type.ToString() + " " + item.Parameters[j].Name;
                    }
                }

                sb.AppendLine($"      protected delegate void {item.Name}Delegate({delegateParameters});");
                sb.AppendLine($"      protected event {item.Name}Delegate? OnHandle_{item.Name};");
                sb.AppendLine($"      void ProcessRpc{item.Name}(RpcPayload rpc)");
                sb.AppendLine($"      {{");

                string parameterList = string.Empty;
                for (int j = 0; j < item.Parameters.Length; j++)
                {
                    if (j > 0)
                    {
                        parameterList += ", ";
                    }

                    parameterList += "param_" + item.Parameters[j].Name;

                    if (IsNetworkActor(item.Parameters[j].Type) == true)
                    {
                        sb.AppendLine($"         var param_{item.Parameters[j].Name}_uniqueId = " +
                            $"rpc.GetObject(\"{item.Parameters[j].Name}\", " +
                            $"out bool found_{item.Parameters[j].Name});");
                        sb.AppendLine($"         var param_{item.Parameters[j].Name} = GetNetworkActor<{item.Parameters[j].Type}>(param_{item.Parameters[j].Name}_uniqueId);");
                    }
                    else
                    {
                        sb.AppendLine($"         var param_{item.Parameters[j].Name} = " +
                            $"rpc.{GetGetter(item.Parameters[j].Type)}(\"{item.Parameters[j].Name}\", " +
                            $"out bool found_{item.Parameters[j].Name});");
                    }
                    sb.AppendLine($"         if (found_{item.Parameters[j].Name} == false)");
                    sb.AppendLine($"         {{");
                    sb.AppendLine($"            throw new Exception(\"Required parameter '{item.Parameters[j].Name}' not found or not of required type '{item.Parameters[j].Type}'\");");
                    sb.AppendLine($"         }}");
                }

                sb.AppendLine($"         OnHandle_{item.Name}?.Invoke({parameterList});");
                sb.AppendLine($"      }}");

                sb.AppendLine($"      ");
            }

            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");

            string fn = typeSymbol.ToDisplayString().Replace(".", "_") + "__client_methods.cs";

            string code = sb.ToString();
            context.AddSource(fn, code);
        }

        private void GenerateCSharpClientEvents(GeneratorExecutionContext context, INamedTypeSymbol typeSymbol, List<IEventSymbol> events)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using ULS.Core;");
            sb.AppendLine($"");
            sb.AppendLine($"namespace {typeSymbol.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine($"{{");
            sb.AppendLine($"   partial class {typeSymbol.Name}");
            sb.AppendLine($"   {{");

            foreach (var item in events)
            {
                var members = item.Type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i].Name == "Invoke")
                    {
                        sb.Append($"      void Server_{item.Name}(");

                        var ms = members[i] as IMethodSymbol;
                        // Skip first, which is the Controller itself
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            if (j > 1)
                            {
                                sb.Append(", ");
                            }
                            sb.Append(ms.Parameters[j].Type.ToDisplayString() + " " + ms.Parameters[j].Name);
                        }

                        sb.AppendLine($")");
                        sb.AppendLine($"      {{");

                        sb.AppendLine($"         RpcPayload payload = new RpcPayload();");
                        sb.AppendLine($"         payload.MethodName = \"{item.Name}\";");
                        sb.AppendLine($"         payload.ReturnType = \"{GetReturnType(ms)}\";");
                        sb.AppendLine($"         payload.ReturnValue = string.Empty;");
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            var parameter = ms.Parameters[j];

                            sb.AppendLine($"         payload.Parameters.Add(new RpcPayload.RpcParameter()");
                            sb.AppendLine($"         {{");
                            sb.AppendLine($"            Name = \"{parameter.Name}\",");
                            var paramType = GetParameterType(parameter);
                            switch (paramType)
                            {
                                case "String":
                                    sb.AppendLine($"            Type = RpcParameterType.String,");
                                    sb.AppendLine($"            Value = {parameter.Name},");
                                    break;

                                case "Int32":
                                    sb.AppendLine($"            Type = RpcParameterType.Int,");
                                    sb.AppendLine($"            Value = {parameter.Name},");
                                    break;

                                case "Int64":
                                    sb.AppendLine($"            Type = RpcParameterType.Long,");
                                    sb.AppendLine($"            Value = {parameter.Name},");
                                    break;

                                case "Float":
                                    sb.AppendLine($"            Type = RpcParameterType.Float,");
                                    sb.AppendLine($"            Value = {parameter.Name},");
                                    break;

                                case "NetworkActor":
                                    sb.AppendLine($"            Type = RpcParameterType.Object,");
                                    sb.AppendLine($"            Value = {parameter.Name}.UniqueId,");
                                    break;

                                default:
                                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                                        Code_InvalidParam, "", "Invalid parameter type: " + paramType,
                                        "", DiagnosticSeverity.Error, true),
                                        parameter.Locations.Length > 0 ? parameter.Locations[0] : null));
                                    break;
                            }
                            sb.AppendLine($"         }});");
                        }

                        sb.AppendLine($"         WirePacket packet = payload.GetWirePacket();");
                        sb.AppendLine($"         this.SendRpc(packet);");

                        sb.AppendLine($"      }}");
                        sb.AppendLine($"");
                    }
                }
            }

            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");

            string fn = typeSymbol.ToDisplayString().Replace(".", "_") + "__client_events.cs";

            string code = sb.ToString();
            context.AddSource(fn, code);
        }
    }
}
