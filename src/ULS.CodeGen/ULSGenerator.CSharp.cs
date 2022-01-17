﻿using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ULS.CodeGen
{
    public partial class ULSGenerator
    {
        private void GenerateCSharpClasses(GeneratorExecutionContext context, SyntaxReceiver receiver)
        {
            if (ValidateReplicationTypes(context, receiver) == false)
            {
                return;
            }
            foreach (var pair in receiver.ReplicationMembers)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__properties.g.cs";

                string code = GenerateSourceForReplicatedMembers(context, pair.Key, pair.Value);
                if (code == null)
                {
                    continue;
                }
                context.AddSource(fn, code);
            }

            foreach (var pair in receiver.RpcMethodsByType)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__methods.g.cs";

                string code = GenerateSourceForMethods(context, pair.Key, pair.Value);
                context.AddSource(fn, code);
            }

            foreach (var pair in receiver.RpcEventsByType)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__events_wrapperfunction.g.cs";
                string code = GenerateRpcEventWrapperFunction(pair.Key, pair.Value);
                context.AddSource(fn, code);
            }

            foreach (var pair in receiver.RpcEventsByType)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__events.g.cs";

                string code = GenerateSourceForEvents(context, pair.Key, pair.Value);
                context.AddSource(fn, code);
            }
        }

        #region Replicated properties
        private bool ValidateReplicationTypes(GeneratorExecutionContext context, SyntaxReceiver receiver)
        {
            foreach (var pair in receiver.ReplicationMembers)
            {
                bool isSubC = IsSubclassOf(pair.Key, "NetworkActor");
                if (isSubC == false)
                {
                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                        Code_ReplicationTypeNetworkActor, "", "Classes using Replicated properties or fields must be derived from NetworkActor",
                        "", DiagnosticSeverity.Error, true),
                        pair.Key.Locations.Length > 0 ? pair.Key.Locations[0] : null));
                    return false;
                }
            }

            if (receiver.ReplicationMembersNotPartialTypes.Count == 0)
            {
                return true;
            }

            foreach (var item in receiver.ReplicationMembersNotPartialTypes)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_ReplicationTypeNotPartial, "", "Classes using Replicated properties or fields must be declared as partial.",
                    "", DiagnosticSeverity.Error, true),
                    item.Locations.Length > 0 ? item.Locations[0] : null));
            }

            return false;
        }
        
        private string GenerateSourceForReplicatedMembers(GeneratorExecutionContext context, INamedTypeSymbol typeSymbol, List<ISymbol> members)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using System.ComponentModel;");
            sb.AppendLine($"using System.Text;");
            sb.AppendLine($"using ULS.Core;");
            sb.AppendLine($"");
            sb.AppendLine($"namespace {typeSymbol.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine($"{{");
            sb.AppendLine($"   partial class {typeSymbol.Name}");
            sb.AppendLine($"   {{");

            foreach (var item in members)
            {
                sb.AppendLine($"      [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]");
                sb.AppendLine($"      [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]");
                if (item is IPropertySymbol prop)
                {
                    sb.AppendLine($"      private {prop.Type} {prop.Name}_replicationBackingField = {GetDefault(prop.Type, prop.Name)};");
                    sb.AppendLine($"      public event Action? {prop.Name}_OnValueChanged;");
                }
                if (item is IFieldSymbol field)
                {
                    sb.AppendLine($"      private {field.Type} {field.Name}_replicationBackingField = {GetDefault(field.Type, field.Name)};");
                    sb.AppendLine($"      public event Action? {field.Name}_OnValueChanged;");
                }
                sb.AppendLine($"      ");
            }            
            sb.AppendLine($"      protected override void ReplicateValuesInternal(BinaryWriter writer, bool forced, ref int numberOfSerializedFields)");
            sb.AppendLine($"      {{");
            sb.AppendLine($"         base.ReplicateValuesInternal(writer, forced, ref numberOfSerializedFields);");
            foreach (var item in members)
            {
                string serializeFunc = null;
                if (item is IPropertySymbol prop)
                {
                    sb.AppendLine($"         if (forced || {prop.Name} != {prop.Name}_replicationBackingField)");
                    sb.AppendLine($"         {{");
                    sb.AppendLine($"            {prop.Name}_replicationBackingField = {prop.Name};");
                    serializeFunc = GetSerializeFunction(context, prop.Type, prop.Name);
                }
                if (item is IFieldSymbol field)
                {
                    sb.AppendLine($"         if (forced || {field.Name} != {field.Name}_replicationBackingField)");
                    sb.AppendLine($"         {{");
                    sb.AppendLine($"            {field.Name}_replicationBackingField = {field.Name};");
                    serializeFunc = GetSerializeFunction(context, field.Type, field.Name);
                }
                if (serializeFunc == null)
                {
                    return null;
                }
                sb.AppendLine($"            {serializeFunc};");
                sb.AppendLine($"            numberOfSerializedFields++;");
                sb.AppendLine($"         }}");
            }
            sb.AppendLine($"      }}");
            sb.AppendLine($"      ");
            sb.AppendLine($"      public override void ApplyReplicatedValues(BinaryReader reader)");
            sb.AppendLine($"      {{");
            sb.AppendLine($"         int fieldCount = reader.ReadInt32();");
            sb.AppendLine($"         for (int i = 0; i < fieldCount; i++)");
            sb.AppendLine($"         {{");
            sb.AppendLine($"            byte type = reader.ReadByte();");
            sb.AppendLine($"            string fieldName = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));");
            sb.AppendLine($"            switch (fieldName)");
            sb.AppendLine($"            {{");
            foreach (var item in members)
            {
                if (item is IPropertySymbol prop)
                {
                    sb.AppendLine($"               case \"{prop.Name}\":");
                    sb.AppendLine($"                  {{");
                    sb.AppendLine($"                     var newValue_{prop.Name} = {GetDeserializeFunction(context, prop.Type, prop.Name)};");
                    sb.AppendLine($"                     if (newValue_{prop.Name} != {prop.Name})");
                    sb.AppendLine($"                     {{");
                    sb.AppendLine($"                        {prop.Name} = newValue_{prop.Name};");
                    sb.AppendLine($"                        {prop.Name}_replicationBackingField = {prop.Name};");
                    sb.AppendLine($"                        {prop.Name}_OnValueChanged?.Invoke();");
                    sb.AppendLine($"                     }}");
                    sb.AppendLine($"                  }}");
                    sb.AppendLine($"                  break;");
                }
                if (item is IFieldSymbol field)
                {
                    sb.AppendLine($"               case \"{field.Name}\":");
                    sb.AppendLine($"                  {{");
                    sb.AppendLine($"                     var newValue_{field.Name} = {GetDeserializeFunction(context, field.Type, field.Name)};");
                    sb.AppendLine($"                     if (newValue_{field.Name} != {field.Name})");
                    sb.AppendLine($"                     {{");
                    sb.AppendLine($"                        {field.Name} = newValue_{field.Name};");
                    sb.AppendLine($"                        {field.Name}_replicationBackingField = {field.Name};");
                    sb.AppendLine($"                        {field.Name}_OnValueChanged?.Invoke();");
                    sb.AppendLine($"                     }}");
                    sb.AppendLine($"                  }}");
                    sb.AppendLine($"                  break;");
                }
            }
            sb.AppendLine($"            }}");
            sb.AppendLine($"         }}");
            sb.AppendLine($"      }}");
            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private string GetDefault(ITypeSymbol symbolType, string symbolName)
        {
            switch (symbolType.ToString())
            {
                case "int":
                case "long":
                case "float":
                case "bool":
                    return $"default({symbolType})";

                case "string":
                    return $"string.Empty";
            }

            if (IsSubclassOf(symbolType, "NetworkActor"))
            {
                return $"null";
            }

            return $"default({symbolType})";
        }

        private string GetDeserializeFunction(GeneratorExecutionContext context, ITypeSymbol symbolType, string symbolName)
        {
            switch (symbolType.ToString())
            {
                case "int":
                case "long":
                case "float":
                case "bool":
                    return $"DeserializeValue<{symbolType}>(reader)";

                case "string":
                    return $"DeserializeString(reader)";
            }

            if (IsSubclassOf(symbolType, "NetworkActor"))
            {
                var symbolTypeString = symbolType.ToString();
                if (symbolTypeString.EndsWith("?"))
                {
                    symbolTypeString = symbolTypeString.Substring(0, symbolTypeString.Length - 1);
                }
                return $"DeserializeRef<{symbolTypeString}>(reader, Owner)";
            }

            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                Code_ReplicationInvalidPropertyType, "", "Classes using Replicated properties must be derived from NetworkActor" + 
                $" (Class: {symbolType.ToDisplayString()})",
                "", DiagnosticSeverity.Error, true),
                symbolType.Locations.Length > 0 ? symbolType.Locations[0] : null));

            return null;
        }

        private string GetSerializeFunction(GeneratorExecutionContext context, ITypeSymbol symbolType, string symbolName)
        {
            switch (symbolType.ToString())
            {
                case "int":
                case "long":
                case "float":
                case "bool":
                    return $"SerializeValue<{symbolType}>(writer, {symbolName}, \"{symbolName}\")";

                case "string":
                    return $"SerializeString(writer, {symbolName}, \"{symbolName}\")";
            }

            if (IsSubclassOf(symbolType, "NetworkActor"))
            {
                return $"SerializeRef(writer, {symbolName}, \"{symbolName}\")";
            }

            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                Code_ReplicationInvalidPropertyType, "", "Classes using Replicated properties must be derived from NetworkActor" +
                $" (Class: {symbolType.ToDisplayString()})",
                "", DiagnosticSeverity.Error, true),
                symbolType.Locations.Length > 0 ? symbolType.Locations[0] : null));
            
            return null;
        }
        #endregion

        #region Events
        private string GenerateRpcEventWrapperFunction(INamedTypeSymbol typeSymbol, List<IEventSymbol> rpcEvents)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using ULS.Core;");
            sb.AppendLine($"");
            sb.AppendLine($"namespace {typeSymbol.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine($"{{");
            sb.AppendLine($"   partial class {typeSymbol.Name}");
            sb.AppendLine($"   {{");
            sb.AppendLine($"      partial void ProcessRpcMethod(RpcPayload rpc)");
            sb.AppendLine($"      {{");

            sb.AppendLine($"         switch (rpc.MethodName)");
            sb.AppendLine($"         {{");
            foreach (var item in rpcEvents)
            {
                sb.AppendLine($"            case \"{item.Name}\":");
                sb.AppendLine($"               ProcessRpc{item.Name}(rpc);");
                sb.AppendLine($"               break;");
            }
            sb.AppendLine($"         }}");

            sb.AppendLine($"      }}");
            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private string GenerateSourceForEvents(GeneratorExecutionContext context, INamedTypeSymbol typeSymbol, List<IEventSymbol> items)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"using ULS.Core;");
            sb.AppendLine($"");
            sb.AppendLine($"namespace {typeSymbol.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine($"{{");
            sb.AppendLine($"   partial class {typeSymbol.Name}");
            sb.AppendLine($"   {{");
            foreach (var item in items)
            {
                //sb.AppendLine($"// item.Locations.Length > 0 ? item.Locations[0] : null => {(item.Locations.Length > 0 ? item.Locations[0] : null)}");

                var members = item.Type.GetMembers();
                sb.AppendLine($"      private void ProcessRpc{item.Name}(RpcPayload rpc)");
                sb.AppendLine($"      {{");
                string parameterList = string.Empty;
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i].Name == "Invoke")
                    {
                        var ms = members[i] as IMethodSymbol;

                        if (ms.Parameters.Length < 1)
                        {
                            context.ReportDiagnostic(
                                Diagnostic.Create(new DiagnosticDescriptor(Code_RpcCallAtLeastOne, "", 
                                "RpcCall client event must have at least one parameter.", "", DiagnosticSeverity.Error, true),
                                item.Locations.Length > 0 ? item.Locations[0] : null));
                            return string.Empty;
                        }

                        var sec = SymbolEqualityComparer.Default;
                        bool firstParamIsEqual = sec.Equals(ms.Parameters[0].Type, typeSymbol);
                        if (firstParamIsEqual == false)
                        {
                            context.ReportDiagnostic(
                                Diagnostic.Create(new DiagnosticDescriptor(Code_RpcCallFirstParam, "", 
                                "The first parameter of an RpcCall client event must be of the same type as the enclosing " +
                                "class. (" + typeSymbol.ToDisplayString() + ")", "", DiagnosticSeverity.Error, true),
                                item.Locations.Length > 0 ? item.Locations[0] : null));
                            return String.Empty;
                        }

                        // Skip first, which is the Controller itself
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            parameterList += ", param_" + ms.Parameters[j].Name;

                            if (IsNetworkActor(ms.Parameters[j].Type) == true)
                            {
                                sb.AppendLine($"         var param_{ms.Parameters[j].Name}_uniqueId = " +
                                    $"rpc.GetObject(\"{ms.Parameters[j].Name}\", " +
                                    $"out bool found_{ms.Parameters[j].Name});");
                                sb.AppendLine($"         var param_{ms.Parameters[j].Name} = GetNetworkActor<{ms.Parameters[j].Type}>(param_{ms.Parameters[j].Name}_uniqueId);");
                            }
                            else
                            {
                                sb.AppendLine($"         var param_{ms.Parameters[j].Name} = " +
                                    $"rpc.{GetGetter(ms.Parameters[j].Type)}(\"{ms.Parameters[j].Name}\", " +
                                    $"out bool found_{ms.Parameters[j].Name});");
                            }
                            sb.AppendLine($"         if (found_{ms.Parameters[j].Name} == false)");
                            sb.AppendLine($"         {{");
                            sb.AppendLine($"            throw new Exception(\"Required parameter '{ms.Parameters[j].Name}' not found or not of required type '{ms.Parameters[j].Type}'\");");
                            sb.AppendLine($"         }}");
                        }
                    }
                }
                sb.AppendLine($"         {item.Name}?.Invoke(this{parameterList});");
                sb.AppendLine($"      }}");
                sb.AppendLine($"      ");
            }
            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");

            return sb.ToString();
        }

        private string GetGetter(ITypeSymbol type)
        {
            string typeString = type.ToString();
            switch (typeString)
            {
                case "int": return "GetInt32";
                case "long": return "GetInt64";
                case "float": return "GetFloat";
                case "bool": return "GetBool";
                default: return "GetString";
            }
        }
        #endregion

        #region RPC calls
        private string GenerateSourceForMethods(GeneratorExecutionContext context, INamedTypeSymbol typeSymbol, List<IMethodSymbol> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using ULS.Core;");
            sb.AppendLine($"");
            sb.AppendLine($"namespace {typeSymbol.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine($"{{");
            sb.AppendLine($"   partial class {typeSymbol.Name}");
            sb.AppendLine($"   {{");

            foreach (var item in items)
            {
                string paramString = string.Empty;
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    if (i > 0)
                    {
                        paramString += ", ";
                    }
                    paramString += item.Parameters[i].Type.ToDisplayString() + " " + item.Parameters[i].Name;
                }

                bool isBroadcast = true;
                foreach (var attr in item.GetAttributes())
                {
                    foreach (var args in attr.NamedArguments)
                    {
                        if (args.Key == "IsBroadcast")
                        {
                            isBroadcast = (bool)args.Value.Value;
                        }
                    }
                }

                sb.AppendLine($"      public partial {GetReturnType(item)} {item.Name}({paramString})");
                sb.AppendLine($"      {{");
                sb.AppendLine($"         RpcPayload payload = new RpcPayload();");
                sb.AppendLine($"         payload.MethodName = \"{item.Name}\";");
                sb.AppendLine($"         payload.ReturnType = \"{GetReturnType(item)}\";");
                sb.AppendLine($"         payload.ReturnValue = string.Empty;");
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    var parameter = item.Parameters[i];

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
                if (isBroadcast == false)
                {
                    sb.AppendLine($"         SendRpc(payload.GetWirePacket());");
                }
                else
                {
                    sb.AppendLine($"         BroadcastRpc(payload.GetWirePacket());");
                }
                sb.AppendLine($"      }}");
            }

            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private string GetParameterType(IParameterSymbol ps)
        {
            if (IsSubclassOf(ps.Type, "NetworkActor") == true)
            {
                return "NetworkActor";
            }

            return ps.Type.Name;
        }

        private string GetReturnType(IMethodSymbol ms)
        {
            if (ms.ReturnsVoid)
            {
                return "void";
            }
            return ms.ReturnType.Name;
        }
        #endregion    
    }
}
