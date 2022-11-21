using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ULS.CodeGen
{
    public partial class ULSGenerator
    {
        private static void GenerateUnrealClasses(SourceProductionContext context, IGeneratorContextProvider generatorContext)
        {
            if (ValidateUnrealProjectAttribute(context, generatorContext, out var unrealModuleBaseDir) == false)
            {
                return;
            }

            foreach (var item in generatorContext.UnrealClassTypeLookup)
            {
                string? srcFile = FindImplementationFileForClass(unrealModuleBaseDir, item.Key);
                string? hdrFile = FindHeaderFileForClass(unrealModuleBaseDir, item.Key);

                if (srcFile == null || hdrFile == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                        Code_UnrealMissingFile, "", $"Could not find file for class {item.Key} with expected filename {GetBaseFilenameForClass(item.Key)}",
                        "", DiagnosticSeverity.Error, true),
                        null));
                    return;
                }

                // Get Methods for this class (Server To Client, Generated code)
                List<IMethodSymbol> gen_methods = new List<IMethodSymbol>();
                foreach (var pair in generatorContext.UnrealGeneratedRpcMethodsByType)
                {
                    gen_methods.AddRange(pair.Value);
                }
                // Get Methods for this class (Server To Client, PartialReflected code)
                List<IMethodSymbol> refl_methods = new List<IMethodSymbol>();
                foreach (var pair in generatorContext.UnrealPartialReflRpcMethodsByType)
                {
                    refl_methods.AddRange(pair.Value);
                }
                GenerateHeaderDataForMethods(context, gen_methods, hdrFile);
                GenerateImplementationDataForMethods(context, gen_methods, refl_methods, srcFile);

                // Get Events for this class (Client To Server)
                List<IEventSymbol> events = new List<IEventSymbol>();
                foreach (var pair in generatorContext.RpcEventsByType)
                {
                    events.AddRange(pair.Value);
                }
                GenerateHeaderDataForEvents(context, events, hdrFile, generatorContext.RpcEventParameterNameLookup);
                GenerateImplementationDataForEvents(context, events, srcFile, item.Key, generatorContext.RpcEventParameterNameLookup);
            }
        }

        #region Methods
        private static void GenerateHeaderDataForMethods(SourceProductionContext context, List<IMethodSymbol> methods, string filename)
        {
            string start = "BEGIN_RPC_BP_EVENTS_FROM_SERVER";
            string end = "END_RPC_BP_EVENTS_FROM_SERVER";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(start);

            Log("GenerateHeaderDataForMethods: " + methods.Count());

            foreach (var item in methods)
            {
                sb.AppendLine("\tUFUNCTION(BlueprintImplementableEvent, Category = Rpc)");
                sb.Append($"\t\t{GetUnrealReturnType(item.ReturnType)} {item.Name}(const UObject* callee");
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    sb.Append(", ");
                    sb.Append(GetUnrealParameterType(item.Parameters[i].Type) + " " + item.Parameters[i].Name);
                }
                sb.AppendLine(");");
            }

            sb.Append("\t" + end);

            string code = sb.ToString();
            ReplaceInFile(filename, code, start, end);
        }

        private static void GenerateImplementationDataForMethods(SourceProductionContext context, List<IMethodSymbol> generated_methods, 
            List<IMethodSymbol> reflection_methods, string filename)
        {
            string start = "BEGIN_RPC_BP_EVENTS_FROM_SERVER_CALL";
            string end = "END_RPC_BP_EVENTS_FROM_SERVER_CALL";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(start);

            Log("GenerateImplementationDataForMethods: " + generated_methods.Count());
            Log("GenerateImplementationDataForMethods: " + reflection_methods.Count());

            foreach (var item in reflection_methods)
            {
                sb.AppendLine($"\t// {item.Name}");
                sb.AppendLine($"\tif (methodName == TEXT(\"{item.Name}\"))");
                sb.AppendLine($"\t{{");
                sb.AppendLine($"\t\tauto cls = existingObject->GetClass();");
                sb.AppendLine($"\t\tUFunction* function = cls->FindFunctionByName(FName(TEXT(\"{item.Name}\")));");
                sb.AppendLine($"\t\tif (IsValid(function) == false)");
                sb.AppendLine($"\t\t{{");
                sb.AppendLine($"\t\t\t// TODO: Log properly");
                sb.AppendLine($"\t\t\tUE_LOG(LogTemp, Error, TEXT(\"Failed to call function {item.Name} on object of type %s with uniqueId: %ld\"), *cls->GetName(), FindUniqueId(existingObject));");
                sb.AppendLine($"\t\t\treturn;");
                sb.AppendLine($"\t\t}}");
                sb.AppendLine($"\t\tstruct");
                sb.AppendLine($"\t\t{{");
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    sb.AppendLine($"\t\t\t{GetUnrealParameterType(item.Parameters[i].Type)} param_{item.Name}_{i} = {GetUnrealParameterDefaultValueType(item.Parameters[i].Type)};");
                }
                sb.AppendLine($"\t\t}} FuncParams;");
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    sb.AppendLine($"\t\tFuncParams.param_{item.Name}_{i} = " +
                        GetUnrealDeserializeParameterFunction(item.Parameters[i]) + "(packet, position, position);");
                }
                sb.AppendLine($"\t\texistingObject->ProcessEvent(function, &FuncParams);");
                sb.AppendLine($"\t\treturn;");
                sb.AppendLine($"\t}}");
                sb.AppendLine($"\t");
            }

            foreach (var item in generated_methods)
            {
                sb.AppendLine($"\t// {item.Name}");
                sb.AppendLine($"\tif (methodName == TEXT(\"{item.Name}\"))");
                sb.AppendLine($"\t{{");

                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    sb.AppendLine($"\t\tauto param_{item.Name}_{i} = " +
                        GetUnrealDeserializeParameterFunction(item.Parameters[i]) + "(packet, position, position);");
                }

                sb.Append($"\t\t{item.Name}(existingObject");
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    sb.Append(", ");
                    sb.Append($"param_{item.Name}_{i}");
                }
                sb.AppendLine($");");
                sb.AppendLine($"\t\treturn;");
                sb.AppendLine($"\t}}");
                sb.AppendLine($"\t");
            }

            sb.Append("\t" + end);

            string code = sb.ToString();
            ReplaceInFile(filename, code, start, end);
        }
        #endregion

        private static string GetEventName(IEventSymbol symbol)
        {
            string name = symbol.Name;
            if (name.StartsWith("OnHandle"))
            {
                name = name.Substring("OnHandle".Length);
            }
            return name;
        }

        private static bool HasEventParameterName(IEventSymbol symbol, int index, Dictionary<IEventSymbol, string[]> eventParameterNameLookup)
        {
            if (eventParameterNameLookup.TryGetValue(symbol, out var names))
            {
                if (index >= 0 && index < names.Length)
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetEventParameterName(IEventSymbol symbol, int index, Dictionary<IEventSymbol, string[]> eventParameterNameLookup)
        {
            if (eventParameterNameLookup.TryGetValue(symbol, out var names))
            {
                if (index >= 0 && index < names.Length)
                {
                    return names[index];
                }
            }
            return "arg" + (index + 1);
        }

        private static void GenerateHeaderDataForEvents(SourceProductionContext context, List<IEventSymbol> events, string filename,
            Dictionary<IEventSymbol, string[]> eventParameterNameLookup)
        {
            string start = "BEGIN_RPC_BP_EVENTS_TO_SERVER";
            string end = "END_RPC_BP_EVENTS_TO_SERVER";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(start);

            foreach (var item in events)
            {
                var members = item.Type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i].Name == "Invoke")
                    {
                        var ms = members[i] as IMethodSymbol;
                        if (ms == null)
                        {
                            continue;
                        }

                        sb.AppendLine("\tUFUNCTION(BlueprintCallable, Category = Rpc)");
                        string callerName = "caller";
                        if (HasEventParameterName(item, 0, eventParameterNameLookup) == true)
                        {
                            callerName = GetEventParameterName(item, 0, eventParameterNameLookup);
                        }
                        sb.Append($"\t\tvoid Server_{GetEventName(item)}(UObject* " + callerName);

                        // Skip first, which is the Controller itself
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            sb.Append(", ");
                            sb.Append(GetUnrealParameterType(ms.Parameters[j].Type) + " " + GetEventParameterName(item, j, eventParameterNameLookup));
                        }

                        sb.AppendLine(");");
                    }
                }
            }

            sb.Append("\t" + end);

            string code = sb.ToString();
            ReplaceInFile(filename, code, start, end);
        }

        private static void GenerateImplementationDataForEvents(SourceProductionContext context, List<IEventSymbol> events, string filename,
            string unrealClassName, Dictionary<IEventSymbol, string[]> eventParameterNameLookup)
        {
            string start = "BEGIN_RPC_BP_EVENTS_TO_SERVER_CALL";
            string end = "END_RPC_BP_EVENTS_TO_SERVER_CALL";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(start);

            foreach (var item in events)
            {
                var members = item.Type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i].Name == "Invoke")
                    {
                        sb.AppendLine($"// {item.Name}");
                        string callerName = "caller";
                        if (HasEventParameterName(item, 0, eventParameterNameLookup) == true)
                        {
                            callerName = GetEventParameterName(item, 0, eventParameterNameLookup);
                        }
                        sb.Append($"void {unrealClassName}::Server_{GetEventName(item)}(UObject* " + callerName);

                        var ms = members[i] as IMethodSymbol;
                        if (ms == null)
                        {
                            continue;
                        }
                        // Skip first, which is the Controller itself
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            sb.Append(", ");
                            sb.Append(GetUnrealParameterType(ms.Parameters[j].Type) + " " + GetEventParameterName(item, j, eventParameterNameLookup));
                        }

                        sb.AppendLine($")");
                        sb.AppendLine($"{{");

                        sb.AppendLine($"   UULSWirePacket* packet = NewObject<UULSWirePacket>();");
                        sb.AppendLine($"   packet->PacketType = (int32)EWirePacketType::RpcCall;");
                        sb.AppendLine($"   ");
                        sb.AppendLine($"   FString methodName = TEXT(\"{item.Name}\");");
                        sb.AppendLine($"   FString returnType = TEXT(\"void\");");
                        sb.AppendLine($"   ");
                        sb.AppendLine($"   int requiredPayloadSize = 4 + 8 + 4 + methodName.Len() + 4 + returnType.Len() + 4;");
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            sb.AppendLine($"   FString fieldName_{GetEventParameterName(item, j, eventParameterNameLookup)} = TEXT(\"{GetEventParameterName(item, j, eventParameterNameLookup)}\");");
                            sb.Append($"   requiredPayloadSize += {GetUnrealSerializeSizeFunction(ms.Parameters[j])}(fieldName_{GetEventParameterName(item, j, eventParameterNameLookup)}");
                            if (ms.Parameters[j].Type.ToString() == "string")
                            {
                                sb.Append(", " + GetEventParameterName(item, j, eventParameterNameLookup) + ".Len()");
                            }
                            sb.AppendLine(");");
                        }
                        sb.AppendLine($"   TArray<uint8> bytes;");
                        sb.AppendLine($"   bytes.AddUninitialized(requiredPayloadSize);");
                        sb.AppendLine($"   packet->Payload = bytes;");

                        sb.AppendLine($"   int position = 0;");
                        sb.AppendLine($"   packet->PutInt32(0, position, position); // flags");
                        sb.AppendLine($"   packet->PutInt64(FindUniqueId({callerName}), position, position);");
                        sb.AppendLine($"   packet->PutString(methodName, position, position);");
                        sb.AppendLine($"   packet->PutString(returnType, position, position);");
                        sb.AppendLine($"   packet->PutInt32({ms.Parameters.Length - 1}, position, position); // number of parameters");
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            string serializeFunc = GetUnrealSerializeParameterFunction(ms.Parameters[j]);
                            sb.AppendLine($"   {serializeFunc}(packet, fieldName_{GetEventParameterName(item, j, eventParameterNameLookup)}, {GetEventParameterName(item, j, eventParameterNameLookup)}, position, position);");
                        }

                        sb.AppendLine($"   this->Transport->SendWirePacket(packet);");
                        
                        sb.AppendLine($"}}");
                        sb.AppendLine($"");
                    }

                }
            }

            sb.Append("\t" + end);

            string code = sb.ToString();
            ReplaceInFile(filename, code, start, end);
        }

        #region File Utils
        private static void ReplaceInFile(string filename, string content, string start, string end)
        {
            string contents = File.ReadAllText(filename);

            int idx = contents.IndexOf(start);
            int idx2 = contents.IndexOf(end, idx);

            string part1 = contents.Substring(0, idx);
            string part2 = contents.Substring(idx2 + end.Length);

            contents = part1 + content + part2;
            File.WriteAllText(filename, contents);
        }

        private static string? FindHeaderFileForClass(string unrealModuleBaseDir, string className)
        {
            string baseFn = GetBaseFilenameForClass(className) + ".h";
            if (File.Exists(Path.Combine(unrealModuleBaseDir, baseFn)))
            {
                return Path.Combine(unrealModuleBaseDir, baseFn);
            }

            string[] files = Directory.GetFiles(Path.Combine(unrealModuleBaseDir), "*.h", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string fn = Path.GetFileName(files[i]);
                if (string.Equals(fn, baseFn, StringComparison.InvariantCultureIgnoreCase))
                {
                    return files[i];
                }
            }

            return null;
        }

        private static string? FindImplementationFileForClass(string unrealModuleBaseDir, string className)
        {
            string baseFn = GetBaseFilenameForClass(className) + ".cpp";
            if (File.Exists(Path.Combine(unrealModuleBaseDir, baseFn)))
            {
                return Path.Combine(unrealModuleBaseDir, baseFn);
            }

            string[] files = Directory.GetFiles(Path.Combine(unrealModuleBaseDir), "*.cpp", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string fn = Path.GetFileName(files[i]);
                if (string.Equals(fn, baseFn, StringComparison.InvariantCultureIgnoreCase))
                {
                    return files[i];
                }
            }

            return null;
        }

        private static string? FindGeneratedImplementationFileForClass(string unrealModuleBaseDir, string className)
        {
            string baseFn = GetBaseFilenameForClass(className) + ".gen.cpp";
            if (File.Exists(Path.Combine(unrealModuleBaseDir, baseFn)))
            {
                return Path.Combine(unrealModuleBaseDir, baseFn);
            }

            string[] files = Directory.GetFiles(Path.Combine(unrealModuleBaseDir), "*.cpp", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string fn = Path.GetFileName(files[i]);
                if (string.Equals(fn, baseFn, StringComparison.InvariantCultureIgnoreCase))
                {
                    return files[i];
                }
            }

            return null;
        }

        private static string? GetBaseFilenameForClass(string className)
        {
            if (className.Length < 2)
            {
                return className;
            }

            if (char.IsUpper(className[0]) && char.IsUpper(className[1]) &&
                (className[0] == 'A' || className[0] == 'U'))
            {
                return className.Substring(1);
            }

            return className;
        }
        #endregion

        #region Validation
        private static bool ValidateUnrealProjectAttribute(SourceProductionContext context, IGeneratorContextProvider receiver, 
            out string unrealModuleBaseDir)
        {
            unrealModuleBaseDir = string.Empty;

            if (receiver.UnrealProject == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_UnrealProjectAttribute, "", "At least one class must provide the UnrealProjectAttribute if code generation for Unreal is desired.",
                    "", DiagnosticSeverity.Warning, true),
                    null));
                return false;
            }

            if (receiver.UnrealProject.IsCodeGenerationEnabled == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_UnrealCodeSkip, "", "Skipping code generation for Unreal as defined in UnrealProjectAttribute.",
                    "", DiagnosticSeverity.Warning, true),
                    null));
                return false;
            }

            if (File.Exists(receiver.UnrealProject.ProjectFile) == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_UnrealProjectFile, "", "Unreal project file at '" + receiver.UnrealProject.ProjectFile + 
                    "' does not exist or is not readable. Skipping code generation for Unreal.",
                    "", DiagnosticSeverity.Error, true),
                    null));
                return false;
            }

            unrealModuleBaseDir = Path.Combine(
                Path.GetDirectoryName(receiver.UnrealProject.ProjectFile),
                "Source",
                receiver.UnrealProject.Module);

            if (Directory.Exists(unrealModuleBaseDir) == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_UnrealPluginInvalid, "", "Module folder at '" + unrealModuleBaseDir +
                    "' does not exist or is not readable. Skipping code generation for Unreal. Check the Module field in the UnrealProjectAttribute.",
                    "", DiagnosticSeverity.Error, true),
                    null));
                return false;
            }

            return true;
        }
        #endregion

        private static string GetUnrealSerializeSizeFunction(IParameterSymbol paramSymbol)
        {
            if (IsNetworkObject(paramSymbol.Type) == true)
            {
                return "GetSerializeRefParameterSize";
            }

            string csharpType = paramSymbol.Type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "GetSerializeStringParameterSize";

                case "short":
                    return "GetSerializeInt16ParameterSize";

                case "int":
                    return "GetSerializeInt32ParameterSize";

                case "long":
                    return "GetSerializeInt64ParameterSize";

                case "float":
                    return "GetSerializeFloat32ParameterSize";

                case "bool":
                    return "GetSerializeBoolParameterSize";

                case "System.Numerics.Vector3":
                    return "GetSerializeVectorParameterSize";

                default:
                    // TODO: Show error (unsupported type)
                    return string.Empty;
            }
        }

        private static string GetUnrealSerializeParameterFunction(IParameterSymbol paramSymbol)
        {
            if (IsNetworkObject(paramSymbol.Type) == true)
            {
                return "SerializeRefParameter";
            }

            string csharpType = paramSymbol.Type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "SerializeStringParameter";

                case "short":
                    return "SerializeInt16Parameter";

                case "int":
                    return "SerializeInt32Parameter";

                case "long":
                    return "SerializeInt64Parameter";

                case "float":
                    return "SerializeFloat32Parameter";

                case "bool":
                    return "SerializeBoolParameter";

                case "System.Numerics.Vector3":
                    return "SerializeVectorParameter";

                default:
                    // TODO: Show error (unsupported type)
                    return string.Empty;
            }
        }

        private static string GetUnrealDeserializeFunction(IParameterSymbol paramSymbol)
        {
            if (IsNetworkObject(paramSymbol.Type) == true)
            {
                return "DeserializeRef";
            }

            string csharpType = paramSymbol.Type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "DeserializeString";

                case "short":
                    return "DeserializeInt16";

                case "int":
                    return "DeserializeInt32";

                case "long":
                    return "DeserializeInt64";

                case "float":
                    return "DeserializeFloat32";

                case "bool":
                    return "DeserializeBool";

                case "System.Numerics.Vector3":
                    return "DeserializeVector";

                default:
                    // TODO: Show error (unsupported type)
                    return string.Empty;
            }
        }

        private static string GetUnrealDeserializeParameterFunction(IParameterSymbol paramSymbol)
        {
            if (IsNetworkObject(paramSymbol.Type) == true)
            {
                return "DeserializeRefParameter";
            }

            string csharpType = paramSymbol.Type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "DeserializeStringParameter";

                case "short":
                    return "DeserializeInt16Parameter";

                case "int":
                    return "DeserializeInt32Parameter";

                case "long":
                    return "DeserializeInt64Parameter";

                case "float":
                    return "DeserializeFloat32Parameter";

                case "bool":
                    return "DeserializeBoolParameter";

                case "System.Numerics.Vector3":
                    return "DeserializeVectorParameter";

                default:
                    // TODO: Show error (unsupported type)
                    return string.Empty;
            }
        }

        private static string GetUnrealReturnType(ITypeSymbol type)
        {
            if (IsNetworkObject(type) == true)
            {
                return "UObject*";
            }

            string csharpType = type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "FString";

                case "int":
                    return "int32";

                case "long":
                    return "int64";

                case "float":
                    return "float";

                default:
                    return "void";
            }
        }

        private static string GetUnrealParameterType(ITypeSymbol type)
        {
            if (IsNetworkObject(type) == true)
            {
                return "const UObject*";
            }

            string csharpType = type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "const FString&";

                case "bool":
                    return "bool";

                case "short":
                    return "int16";

                case "int":
                    return "int32";

                case "long":
                    return "int64";

                case "float":
                    return "float";

                case "System.Numerics.Vector3":
                    return "FVector";

                default:
                    // TODO: Error
                    return "void";
            }
        }

        private static string GetUnrealParameterDefaultValueType(ITypeSymbol type)
        {
            if (IsNetworkObject(type) == true)
            {
                return "nullptr";
            }

            string csharpType = type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "FString::Empty()";

                case "bool":
                    return "false";

                case "short":
                    return "0";

                case "int":
                    return "0";

                case "long":
                    return "0";

                case "float":
                    return "0";

                case "System.Numerics.Vector3":
                    return "FVector()";

                default:
                    // TODO: Error
                    return "void";
            }
        }
    }
}
