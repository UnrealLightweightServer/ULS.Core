using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ULS.Core;

namespace ULS.CodeGen
{
    public partial class ULSGenerator
    {
        private static bool GenerateCSharpClasses(SourceProductionContext context, IGeneratorContextProvider generatorContext)
        {
            if (ValidateReplicationTypes(context, generatorContext) == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_GeneratorFailure, "", "Failed to validate replication types during C# code generation (see previous errors)",
                    "", DiagnosticSeverity.Warning, true), null));
                return false;
            }
            if (ValidateRpcCallTypes(context, generatorContext) == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_GeneratorFailure, "", "Failed to validate rpc call types during C# code generation (see previous errors)",
                    "", DiagnosticSeverity.Warning, true), null));
                return false;
            }
            if (ValidateSpawnFunctions(context, generatorContext) == false)
            {
                return false;
            }

            foreach (var pair in generatorContext.ReplicationMembers)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__server_properties.g.cs";

                string? code = GenerateSourceForReplicatedMembers(context, pair.Key, pair.Value);
                if (code == null)
                {
                    // TODO: Add warning
                    continue;
                }
                context.AddSource(fn, code);
            }

            foreach (var pair in generatorContext.RpcMethodsByType)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__server_methods.g.cs";

                string? code = GenerateSourceForMethods(context, pair.Key, pair.Value);
                if (code == null)
                {
                    // TODO: Add warning
                    continue;
                }
                context.AddSource(fn, code);
            }

            /*foreach (var pair in receiver.RpcEventsByType)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__events_wrapperfunction.g.cs";
                string code = GenerateRpcEventWrapperFunction(pair.Key, pair.Value);
                context.AddSource(fn, code);
            }*/

            foreach (var pair in generatorContext.RpcEventsByType)
            {
                string fn = pair.Key.ToDisplayString().Replace(".", "_") + "__server_events.g.cs";

                string code = GenerateSourceForEvents(context, pair.Key, pair.Value, generatorContext.RpcEventParameterNameLookup);
                context.AddSource(fn, code);
            }

            return true;
        }

        private static bool ValidateSpawnFunctions(SourceProductionContext context, IGeneratorContextProvider receiver)
        {
            if (receiver.IncorrectSpawnActors.Count == 0 && receiver.IncorrectSpawnObjects.Count == 0)
            {
                return true;
            }

            foreach (var item in receiver.IncorrectSpawnObjects)
            {
                var d = Diagnostic.Create(new DiagnosticDescriptor(
                        Code_SpawnFunctionNoObject, "",
                        "SpawnNetworkObject must not be used on types derived from NetworkActor. Use SpawnNetworkActor instead.", "",
                        DiagnosticSeverity.Error, true
                    ), item.GetLocation(), item.ToString());
                context.ReportDiagnostic(d);
            }

            foreach (var item in receiver.IncorrectSpawnActors)
            {
                var d = Diagnostic.Create(new DiagnosticDescriptor(
                        Code_SpawnFunctionNoActor, "",
                        "SpawnNetworkActor may only be used on types derived from NetworkActor. Use SpawnNetworkObject instead.", "",
                        DiagnosticSeverity.Error, true
                    ), item.GetLocation(), item.ToString());
                context.ReportDiagnostic(d);
            }

            return false;
        }

        #region Replicated properties
        private static bool ValidateReplicationTypes(SourceProductionContext context, IGeneratorContextProvider generatorContext)
        {
            foreach (var pair in generatorContext.ReplicationMembers)
            {
                bool isSubC = IsNetworkObject(pair.Key);
                if (isSubC == false)
                {
                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                        Code_ReplicationTypeNetworkObject, "", "Classes using Replicated properties or fields must be derived from NetworkObject",
                        "", DiagnosticSeverity.Error, true),
                        pair.Key.Locations.Length > 0 ? pair.Key.Locations[0] : null));
                    return false;
                }
            }

            if (generatorContext.ReplicationMembersNotPartialTypes.Count == 0 &&
                generatorContext.ReplicationFieldsNotPrivate.Count == 0)
            {
                return true;
            }

            foreach (var item in generatorContext.ReplicationFieldsNotPrivate)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_ReplicationTypeNotPrivate, "", "Replicated fields must be a field member, private and start with an underscore.",
                    "", DiagnosticSeverity.Error, true),
                    item.Locations.Length > 0 ? item.Locations[0] : null));
            }

            foreach (var item in generatorContext.ReplicationMembersNotPartialTypes)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_ReplicationTypeNotPartial, "", "Classes using Replicated fields must be declared as partial.",
                    "", DiagnosticSeverity.Error, true),
                    item.Locations.Length > 0 ? item.Locations[0] : null));
            }

            return false;
        }

        private static string? GenerateSourceForReplicatedMembers(SourceProductionContext context, INamedTypeSymbol typeSymbol, List<IFieldSymbol> members)
        {
            bool generateIfChangedCode = false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using System.ComponentModel;");
            sb.AppendLine($"using System.Text;");
            sb.AppendLine($"using ULS.Core;");
            sb.AppendLine($"");
            sb.AppendLine($"#nullable enable");
            sb.AppendLine($"");
            sb.AppendLine($"namespace {typeSymbol.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine($"{{");
            sb.AppendLine($"   partial class {typeSymbol.Name}");
            sb.AppendLine($"   {{");
            foreach (var field in members)
            {
                if (generateIfChangedCode)
                {
                    sb.AppendLine($"      [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]");
                    sb.AppendLine($"      [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]");
                    sb.AppendLine($"      private {field.Type} {field.Name}_replicationBackingField = {GetDefault(field.Type, field.Name)};");
                }
                sb.AppendLine($"      public {field.Type} {GetReplicationFieldPublicName(field)}");
                sb.AppendLine($"      {{");
                sb.AppendLine($"         get {{ return {field.Name}; }}");
                sb.AppendLine($"         set");
                sb.AppendLine($"         {{");
                sb.AppendLine($"            if ({field.Name} != value)");
                sb.AppendLine($"            {{");
                sb.AppendLine($"               {field.Name} = value;");
                if (IsImmediateReplicationField(field))
                {
                    sb.AppendLine($"               // TODO: Optimize");
                    sb.AppendLine($"               System.IO.MemoryStream ms = new System.IO.MemoryStream();");
                    sb.AppendLine($"               System.IO.BinaryWriter writer = new System.IO.BinaryWriter(ms);");
                    sb.AppendLine($"               writer.Write((int)0);");
                    sb.AppendLine($"               writer.Write(UniqueId);");
                    sb.AppendLine($"               writer.Write((int)1);");
                    string? serializeFunc = GetSerializeFunction(context, field);
                    if (serializeFunc != null)
                    {
                        sb.AppendLine($"               {serializeFunc};");
                    }
                    sb.AppendLine($"               Owner.ReplicateValueDirect(this, ms.ToArray());");
                }
                sb.AppendLine($"            }}");
                sb.AppendLine($"         }}");
                sb.AppendLine($"      }}");
                sb.AppendLine($"      public event Action? {GetReplicationFieldPublicName(field)}_OnValueChanged;");
                sb.AppendLine($"      ");
            }
            sb.AppendLine($"      protected override void ReplicateValuesInternal(BinaryWriter writer, bool forced, ref int numberOfSerializedFields)");
            sb.AppendLine($"      {{");
            sb.AppendLine($"         base.ReplicateValuesInternal(writer, forced, ref numberOfSerializedFields);");

            foreach (var field in members)
            {
                string additionalIndent = string.Empty;
                if (generateIfChangedCode)
                {
                    additionalIndent = "   ";
                    sb.AppendLine($"         if (forced || {field.Name} != {field.Name}_replicationBackingField)");
                    sb.AppendLine($"         {{");
                    sb.AppendLine($"            {field.Name}_replicationBackingField = {field.Name};");

                }
                string? serializeFunc = GetSerializeFunction(context, field);
                if (serializeFunc == null)
                {
                    return null;
                }
                sb.AppendLine($"         {additionalIndent}{serializeFunc};");
                sb.AppendLine($"         {additionalIndent}numberOfSerializedFields++;");
                if (generateIfChangedCode)
                {
                    sb.AppendLine($"         }}");
                }
            }
            sb.AppendLine($"      }}");
            sb.AppendLine($"      ");
            sb.AppendLine($"      protected override void DeserializeFieldInternal(byte type, string fieldName, BinaryReader reader)");
            sb.AppendLine($"      {{");
            sb.AppendLine($"         base.DeserializeFieldInternal(type, fieldName, reader);");
            foreach (var field in members)
            {
                sb.AppendLine($"         bool value_{field.Name}_changed = false;");
            }
            sb.AppendLine($"         switch (fieldName)");
            sb.AppendLine($"         {{");
            foreach (var field in members)
            {
                sb.AppendLine($"            case \"{GetReplicationFieldReplicationName(field)}\":");
                sb.AppendLine($"               {{");
                sb.AppendLine($"                  var newValue_{field.Name} = {GetDeserializeFunction(context, field)};");
                sb.AppendLine($"                  value_{field.Name}_changed = (newValue_{field.Name} != {field.Name});");
                sb.AppendLine($"                  if (value_{field.Name}_changed)");
                sb.AppendLine($"                  {{");
                sb.AppendLine($"                     {field.Name} = newValue_{field.Name};");
                if (generateIfChangedCode)
                {
                    sb.AppendLine($"                     {field.Name}_replicationBackingField = {field.Name};");
                }
                sb.AppendLine($"                  }}");
                sb.AppendLine($"               }}");
                sb.AppendLine($"               break;");
                sb.AppendLine();
            }
            sb.AppendLine($"         }}");
            foreach (var field in members)
            {
                sb.AppendLine($"         if (value_{field.Name}_changed)");
                sb.AppendLine($"         {{");
                sb.AppendLine($"            {GetReplicationFieldPublicName(field)}_OnValueChanged?.Invoke();");
                sb.AppendLine($"         }}");
            }
            sb.AppendLine($"      }}");
            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private static bool IsImmediateReplicationField(IFieldSymbol field)
        {
            var defAttrib = new ReplicateAttribute();
            bool isImmediate = false;
            if (defAttrib.ReplicationStrategy == ReplicationStrategy.Immediate)
            {
                isImmediate = true;
            }
            if (defAttrib.ReplicationStrategy == ReplicationStrategy.Automatic)
            {
                isImmediate = IsNetworkObject(field.Type);
            }
            var attribs = field.GetAttributes();
            foreach (var attr in attribs)
            {
                if (attr.AttributeClass != null &&
                    attr.AttributeClass.ToDisplayString().EndsWith("ReplicateAttribute"))
                {
                    foreach (var attrData in attr.NamedArguments)
                    {
                        switch (attrData.Key)
                        {
                            case "ReplicationStrategy":
                                {
                                    if (attrData.Value.Value is ReplicationStrategy strat)
                                    {
                                        switch (strat)
                                        {
                                            case ReplicationStrategy.Automatic:
                                                isImmediate = IsNetworkObject(field.Type);
                                                break;
                                            case ReplicationStrategy.Manual:
                                                isImmediate = false;
                                                break;
                                            case ReplicationStrategy.Immediate:
                                                isImmediate = true;
                                                break;
                                        }
                                    }
                                }
                                break;
                        }
                    }
                }
            }
            return isImmediate;
        }

        /// <summary>
        /// Returns the name of the field that should be placed into the serialization packet
        /// (as expected by the receiving end)
        /// </summary>
        private static string GetReplicationFieldReplicationName(IFieldSymbol field)
        {
            var str = field.Name.Substring(1);
            return char.ToUpperInvariant(str[0]) + str.Substring(1);
        }

        private static string GetReplicationFieldPublicName(IFieldSymbol field)
        {
            var str = field.Name.Substring(1);
            return char.ToUpperInvariant(str[0]) + str.Substring(1);
        }

        private static string GetDefault(ITypeSymbol symbolType, string symbolName)
        {
            switch (symbolType.ToString())
            {
                case "byte":
                case "short":
                case "int":
                case "long":
                case "float":
                case "double":
                case "bool":
                    return $"default({symbolType})";

                case "string":
                    return $"string.Empty";

                case "System.Numerics.Vector3":
                    return "System.Numerics.Vector3.Zero";
            }

            if (IsNetworkObject(symbolType))
            {
                return $"null";
            }

            return $"default({symbolType})";
        }

        private static string? GetDeserializeFunction(SourceProductionContext context, IFieldSymbol field)
        {
            ITypeSymbol symbolType = field.Type;
            switch (symbolType.ToString())
            {
                case "byte":
                case "short":
                case "int":
                case "long":
                case "bool":
                    return $"DeserializePrimitiveInt<{symbolType}>(reader)";

                case "float":
                case "double":
                    return $"DeserializePrimitiveFloat<{symbolType}>(reader)";

                case "string":
                    return $"DeserializeString(reader)";

                case "System.Numerics.Vector3":
                    return $"DeserializeVector3(reader)";
            }

            if (IsNetworkObject(symbolType))
            {
                var symbolTypeString = symbolType.ToString();
                if (symbolTypeString.EndsWith("?"))
                {
                    symbolTypeString = symbolTypeString.Substring(0, symbolTypeString.Length - 1);
                }
                return $"DeserializeRef<{symbolTypeString}>(reader, Owner)";
            }

            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                Code_ReplicationInvalidPropertyType, "", $"Type (Class: {symbolType.ToDisplayString()}) is not supported by replication",
                "", DiagnosticSeverity.Error, true),
                field.Locations.Length > 0 ? field.Locations[0] : null));

            return null;
        }

        private static string? GetSerializeFunction(SourceProductionContext context, IFieldSymbol field)
        {
            ITypeSymbol symbolType = field.Type;
            string symbolName = GetReplicationFieldReplicationName(field);
            switch (symbolType.ToString())
            {
                case "byte":
                case "short":
                case "int":
                case "long":
                case "bool":
                    return $"SerializePrimitiveInt<{symbolType}>(writer, {symbolName}, \"{symbolName}\")";

                case "float":
                case "double":
                    return $"SerializePrimitiveFloat<{symbolType}>(writer, {symbolName}, \"{symbolName}\")";

                case "string":
                    return $"SerializeString(writer, {symbolName}, \"{symbolName}\")";

                case "System.Numerics.Vector3":
                    return $"SerializeVector3(writer, {symbolName}, \"{symbolName}\")";
            }

            if (IsNetworkObject(symbolType))
            {
                return $"SerializeRef(writer, {symbolName}, \"{symbolName}\")";
            }

            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                Code_ReplicationInvalidPropertyType, "", $"Type (Class: {symbolType.ToDisplayString()}) is not supported by replication",
                "", DiagnosticSeverity.Error, true),
                field.Locations.Length > 0 ? field.Locations[0] : null));

            return null;
        }
        #endregion

        #region Events
        private static string GenerateProcessRpc(SourceProductionContext context,  string methodName, IMethodSymbol item, IEventSymbol eItem,
            Dictionary<IEventSymbol, string[]> eventParameterNameLookup, string baseIndent = "")
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"{baseIndent}private void ProcessRpc{methodName}(BinaryReader reader)");
            sb.AppendLine($"{baseIndent}{{");

            string parameterList = string.Empty;
            for (int j = 1; j < item.Parameters.Length; j++)
            {
                if (j > 1)
                {
                    parameterList += ", ";
                }

                parameterList += "param_" + GetEventParameterName(eItem, j, eventParameterNameLookup);

                sb.AppendLine($"{baseIndent}   var param_{GetEventParameterName(eItem, j, eventParameterNameLookup)} = " +
                    GetDeserializeParameterFunction(context, item.Parameters[j]) + ";");
            }

            if (parameterList.Length > 0)
            {
                parameterList = ", " + parameterList;
            }
            parameterList = $"this" + parameterList;

            if (item.Parameters.Length > 1)
            {
                sb.AppendLine($"");
            }
            sb.AppendLine($"{baseIndent}   {methodName}?.Invoke({parameterList});");
            sb.AppendLine($"{baseIndent}}}");

            return sb.ToString();
        }

        private static string GenerateSourceForEvents(SourceProductionContext context, INamedTypeSymbol typeSymbol, List<IEventSymbol> items,
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

            sb.AppendLine($"      protected override void ProcessRpcMethodInternal(BinaryReader reader)");
            sb.AppendLine($"      {{");
            sb.AppendLine($"         string methodName = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));");
            sb.AppendLine($"         string returnType = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));");
            sb.AppendLine($"         int numberOfParameters = reader.ReadInt32();");
            sb.AppendLine($"         switch (methodName)");
            sb.AppendLine($"         {{");
            foreach (var item in items)
            {
                sb.AppendLine($"            case \"{item.Name}\":");
                sb.AppendLine($"               ProcessRpc{item.Name}(reader);");
                sb.AppendLine($"               break;");
            }
            sb.AppendLine($"         }}");
            sb.AppendLine($"      }}");
            sb.AppendLine("");

            foreach (var item in items)
            {
                var members = item.Type.GetMembers();

                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i].Name == "Invoke")
                    {
                        var ms = members[i] as IMethodSymbol;
                        if (ms != null)
                        {
                            sb.Append(GenerateProcessRpc(context, item.Name, ms, item, eventParameterNameLookup, "      "));
                            sb.AppendLine($"      ");
                        }
                    }
                }
            }
            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");

            return sb.ToString();
        }
        #endregion

        #region RPC calls
        private static bool ValidateRpcCallTypes(SourceProductionContext context, IGeneratorContextProvider generatorContext)
        {
            if (generatorContext.RpcCallsNoNetworkObject.Count == 0 &&
                generatorContext.RpcCallNotPartialTypes.Count == 0)
            {
                return true;
            }

            foreach (var item in generatorContext.RpcCallsNoNetworkObject)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_RpcCallNoNetworkObject, "", "RpcCalls can only be used in classes derived from NetworkObject",
                    "", DiagnosticSeverity.Error, true),
                    item.Locations.Length > 0 ? item.Locations[0] : null));
            }

            foreach (var item in generatorContext.RpcCallNotPartialTypes)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_RpcCallNotPartialType, "", "Classes using RpcCalls must be declared as partial",
                    "", DiagnosticSeverity.Error, true),
                    item.Locations.Length > 0 ? item.Locations[0] : null));
            }

            return false;
        }

        private static string? GenerateSourceForMethods(SourceProductionContext context, INamedTypeSymbol typeSymbol, List<IMethodSymbol> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"using System.Text;");
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

                RpcCallAttribute defaultAttrib = new RpcCallAttribute();
                CallStrategy callStrategyToUse = defaultAttrib.CallStrategy;

                var attribs = item.GetAttributes();
                foreach (var attrib in attribs)
                {
                    if (attrib.AttributeClass != null &&
                        attrib.AttributeClass.ToDisplayString().EndsWith(nameof(RpcCallAttribute)))
                    {
                        foreach (var attrData in attrib.NamedArguments)
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
                    }
                }

                int flags = 0;
                if (callStrategyToUse == CallStrategy.FullReflection)
                {
                    // TODO: Use strongly typed enum flags
                    flags |= 1 << 0;
                }

                sb.AppendLine($"      public partial {GetReturnType(item)} {item.Name}({paramString})");
                sb.AppendLine($"      {{");
                sb.AppendLine($"         MemoryStream ms = new MemoryStream();");
                sb.AppendLine($"         BinaryWriter writer = new BinaryWriter(ms);");
                sb.AppendLine($"         writer.Write((int){flags});              // flags");
                sb.AppendLine($"         writer.Write(this.UniqueId);");
                sb.AppendLine($"         writer.Write(Encoding.UTF8.GetByteCount(\"{item.Name}\"));");
                sb.AppendLine($"         writer.Write(Encoding.UTF8.GetBytes(\"{item.Name}\"));");
                sb.AppendLine($"         writer.Write(Encoding.UTF8.GetByteCount(\"{GetReturnType(item)}\"));");
                sb.AppendLine($"         writer.Write(Encoding.UTF8.GetBytes(\"{GetReturnType(item)}\"));");
                sb.AppendLine($"         writer.Write((int){item.Parameters.Length}); // Number of parameters");
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    var parameter = item.Parameters[i];

                    string? serializeFunc = GetSerializeParameterFunction(context, parameter.Name, parameter);
                    if (serializeFunc == null)
                    {
                        return null;
                    }
                    sb.AppendLine($"         {serializeFunc};");
                }
                sb.AppendLine($"         this.Owner.SendRpc(NetworkRelevantOnlyFor, ms.ToArray());");
                sb.AppendLine($"      }}");
                sb.AppendLine($"      ");
            }

            sb.AppendLine($"   }}");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private static string? GetSerializeParameterFunction(SourceProductionContext context, string serializedParamName, IParameterSymbol param)
        {
            ITypeSymbol symbolType = param.Type;
            string symbolName = serializedParamName;
            switch (symbolType.ToString())
            {
                case "byte":
                case "short":
                case "int":
                case "long":
                case "bool":
                    return $"SerializePrimitiveInt<{symbolType}>(writer, {symbolName}, \"{symbolName}\")";

                case "float":
                case "double":
                    return $"SerializePrimitiveFloat<{symbolType}>(writer, {symbolName}, \"{symbolName}\")";

                case "string":
                    return $"SerializeString(writer, {symbolName}, \"{symbolName}\")";

                case "System.Numerics.Vector3":
                    return $"SerializeVector3(writer, {symbolName}, \"{symbolName}\")";
            }

            if (IsNetworkObject(symbolType))
            {
                return $"SerializeRef(writer, {symbolName}, \"{symbolName}\")";
            }

            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                Code_ReplicationInvalidPropertyType, "", $"Type (Class: {symbolType.ToDisplayString()}) is not supported by replication",
                "", DiagnosticSeverity.Error, true),
                param.Locations.Length > 0 ? param.Locations[0] : null));

            return null;
        }

        private static string? GetDeserializeParameterFunction(SourceProductionContext context, IParameterSymbol param)
        {
            ITypeSymbol symbolType = param.Type;
            string symbolName = param.Name;
            switch (symbolType.ToString())
            {
                case "byte":
                case "short":
                case "int":
                case "long":
                case "bool":
                    return $"DeserializePrimitiveIntWithMetadata<{symbolType}>(reader)";

                case "float":
                case "double":
                    return $"DeserializePrimitiveFloatWithMetadata<{symbolType}>(reader)";

                case "string":
                    return $"DeserializeStringWithMetadata(reader)";

                case "System.Numerics.Vector3":
                    return $"DeserializeVector3WithMetadata(reader)";
            }

            if (IsNetworkObject(symbolType))
            {
                var symbolTypeString = symbolType.ToString();
                if (symbolTypeString.EndsWith("?"))
                {
                    symbolTypeString = symbolTypeString.Substring(0, symbolTypeString.Length - 1);
                }
                return $"DeserializeRefWithMetadata<{symbolTypeString}>(reader, Owner)";
            }

            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                Code_ReplicationInvalidPropertyType, "", $"Type (Class: {symbolType.ToDisplayString()}) is not supported by replication",
                "", DiagnosticSeverity.Error, true),
                param.Locations.Length > 0 ? param.Locations[0] : null));

            return null;
        }

        private static string GetReturnType(IMethodSymbol ms)
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
