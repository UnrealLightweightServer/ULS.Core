using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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

            List<IEventSymbol> events = new List<IEventSymbol>();
            foreach (var pair in receiver.RpcEventsByType)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__client_methods.g.cs";

                string? code = GenerateCSharpClientEvents(context, pair.Key, pair.Value, receiver.RpcEventParameterNameLookup);
                if (code == null)
                {
                    // TODO: Add warning
                    continue;
                }
                context.AddSource(fn, code);
            }

            List<IMethodSymbol> methods = new List<IMethodSymbol>();
            foreach (var pair in receiver.RpcMethodsByType)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__client_events.g.cs";

                string? code = GenerateCSharpClientEventHandlers(context, pair.Key, pair.Value);
                if (code == null)
                {
                    // TODO: Add warning
                    continue;
                }
                context.AddSource(fn, code);
            }
        }

        private string GenerateClientProcessRpc(GeneratorExecutionContext context, string methodName, IMethodSymbol item, string baseIndent = "")
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"{baseIndent}private void ProcessRpc{methodName}(BinaryReader reader)");
            sb.AppendLine($"{baseIndent}{{");

            string parameterList = string.Empty;
            for (int j = 0; j < item.Parameters.Length; j++)
            {
                if (j > 0)
                {
                    parameterList += ", ";
                }

                parameterList += "param_" + item.Parameters[j].Name;

                sb.AppendLine($"{baseIndent}   var param_{item.Parameters[j].Name} = " + 
                    GetDeserializeParameterFunction(context, item.Parameters[j]) + ";");

                sb.AppendLine($"");
            }

            if (parameterList.Length > 0)
            {
                parameterList = ", " + parameterList;
            }
            parameterList = $"this" + parameterList;

            sb.AppendLine($"{baseIndent}   OnHandle_{methodName}?.Invoke({parameterList});");
            sb.AppendLine($"{baseIndent}}}");

            return sb.ToString();
        }

        private string? GenerateCSharpClientEventHandlers(GeneratorExecutionContext context, INamedTypeSymbol typeSymbol, List<IMethodSymbol> methods)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using System.Text;");
            sb.AppendLine($"using ULS.Core;");
            sb.AppendLine($"");
            sb.AppendLine($"namespace {typeSymbol.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine($"{{");
            sb.AppendLine($"   partial class {typeSymbol.Name}");
            sb.AppendLine($"   {{");

            sb.AppendLine($"      protected override void Client_ProcessRpcMethodInternal(BinaryReader reader)");
            sb.AppendLine($"      {{");
            sb.AppendLine($"         long pos = reader.BaseStream.Position;");
            sb.AppendLine($"         string methodName = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));");
            sb.AppendLine($"         string returnType = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));");
            sb.AppendLine($"         int numberOfParameters = reader.ReadInt32();");
            sb.AppendLine($"         switch (methodName)");
            sb.AppendLine($"         {{");
            foreach (var item in methods)
            {
                sb.AppendLine($"            case \"{item.Name}\":");
                sb.AppendLine($"               ProcessRpc{item.Name}(reader);");
                sb.AppendLine($"               break;");
            }

            /*,sb.AppendLine($"            default:");
            sb.AppendLine($"               // If unhandled here, let parent method try to handle the RPC call");
            sb.AppendLine($"               reader.BaseStream.Seek(pos, SeekOrigin.Begin);");
            sb.AppendLine($"               HandleRpcPacket(reader);");
            sb.AppendLine($"               break;");*/

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
                if (delegateParameters.Length > 0)
                {
                    delegateParameters = ", " + delegateParameters;
                }
                delegateParameters = $"{typeSymbol.Name} caller" + delegateParameters;

                sb.AppendLine($"      protected delegate void {item.Name}Delegate({delegateParameters});");
                sb.AppendLine($"      protected event {item.Name}Delegate? OnHandle_{item.Name};");
                sb.Append(GenerateClientProcessRpc(context, item.Name, item, "      "));

                sb.AppendLine($"      ");
            }

            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");

            return sb.ToString();
        }

        private string? GenerateCSharpClientEvents(GeneratorExecutionContext context, INamedTypeSymbol typeSymbol, List<IEventSymbol> events,
            Dictionary<IEventSymbol, string[]> eventParameterNameLookup)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using System.Text;");
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
                        sb.Append($"      public void Server_{item.Name}(");

                        var ms = members[i] as IMethodSymbol;
                        if (ms == null)
                        {
                            continue;
                        }
                        // Skip first, which is the Controller itself
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            if (j > 1)
                            {
                                sb.Append(", ");
                            }
                            sb.Append(ms.Parameters[j].Type.ToDisplayString() + " " + GetEventParameterName(item, j, eventParameterNameLookup));
                        }

                        sb.AppendLine($")");
                        sb.AppendLine($"      {{");

                        sb.AppendLine($"         MemoryStream ms = new MemoryStream();");
                        sb.AppendLine($"         BinaryWriter writer = new BinaryWriter(ms);");
                        sb.AppendLine($"         writer.Write((int)0);              // flags");
                        sb.AppendLine($"         writer.Write(this.UniqueId);");
                        sb.AppendLine($"         writer.Write(Encoding.ASCII.GetByteCount(\"{item.Name}\"));");
                        sb.AppendLine($"         writer.Write(Encoding.ASCII.GetBytes(\"{item.Name}\"));");
                        sb.AppendLine($"         writer.Write(Encoding.ASCII.GetByteCount(\"{GetReturnType(ms)}\"));");
                        sb.AppendLine($"         writer.Write(Encoding.ASCII.GetBytes(\"{GetReturnType(ms)}\"));");
                        sb.AppendLine($"         writer.Write((int){ms.Parameters.Length - 1}); // Number of parameters");
                        // Skip first, which is the Controller itself
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            var parameter = ms.Parameters[j];

                            string? serializeFunc = GetSerializeParameterFunction(context, 
                                GetEventParameterName(item, j, eventParameterNameLookup), parameter);
                            if (serializeFunc == null)
                            {
                                return null;
                            }
                            sb.AppendLine($"         {serializeFunc};");
                        }
                        sb.AppendLine($"         this.Owner.SendRpc(null, ms.ToArray());");

                        sb.AppendLine($"      }}");
                        sb.AppendLine($"");
                    }
                }
            }

            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");

            return sb.ToString();
        }
    }
}
