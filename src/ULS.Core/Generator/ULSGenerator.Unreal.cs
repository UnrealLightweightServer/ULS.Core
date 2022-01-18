using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ULS.CodeGen
{
    public partial class ULSGenerator
    {
        private string? UnrealModuleBaseDir = null;

        private void GenerateUnrealClasses(GeneratorExecutionContext context, SyntaxReceiver receiver)
        {
            if (ValidateUnrealProjectAttribute(context, receiver) == false)
            {
                return;
            }

            foreach (var item in receiver.UnrealClassTypeLookup)
            {
                string? srcFile = FindImplementationFileForClass(item.Key);
                string? hdrFile = FindHeaderFileForClass(item.Key);

                if (srcFile == null || hdrFile == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                        Code_UnrealMissingFile, "", $"Could not find file for class {item.Key} with expected filename {GetBaseFilenameForClass(item.Key)}",
                        "", DiagnosticSeverity.Error, true),
                        null));
                    return;
                }

                // Get Methods for this class (Server To Client)
                List<IMethodSymbol> methods = new List<IMethodSymbol>();
                foreach (var pair in item.Value)
                {
                    if (receiver.RpcMethodsByType.TryGetValue(pair, out var typeMethods))
                    {
                        methods.AddRange(typeMethods);
                    }
                }
                GenerateHeaderDataForMethods(context, methods, hdrFile);
                GenerateImplementationDataForMethods(context, methods, srcFile);

                // Get Events for this class (Client To Server)
                List<IEventSymbol> events = new List<IEventSymbol>();
                foreach (var pair in item.Value)
                {
                    if (receiver.RpcEventsByType.TryGetValue(pair, out var typeEvents))
                    {
                        events.AddRange(typeEvents);
                    }
                }
                GenerateHeaderDataForEvents(context, events, hdrFile, receiver.RpcEventParameterNameLookup);
                GenerateImplementationDataForEvents(context, events, srcFile, item.Key, receiver.RpcEventParameterNameLookup);
            }
        }

        #region Methods
        private void GenerateHeaderDataForMethods(GeneratorExecutionContext context, List<IMethodSymbol> methods, string filename)
        {
            string start = "BEGIN_RPC_BP_EVENTS_FROM_SERVER";
            string end = "END_RPC_BP_EVENTS_FROM_SERVER";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(start);

            foreach (var item in methods)
            {
                sb.AppendLine("\tUFUNCTION(BlueprintImplementableEvent, Category = Rpc)");
                sb.Append($"\t\t{GetUnrealReturnType(item.ReturnType)} {item.Name}(");
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(GetUnrealParameterType(item.Parameters[i].Type) + " " + item.Parameters[i].Name);
                }
                sb.AppendLine(");");
            }

            sb.Append("\t" + end);

            string code = sb.ToString();
            ReplaceInFile(filename, code, start, end);
        }

        private void GenerateImplementationDataForMethods(GeneratorExecutionContext context, List<IMethodSymbol> methods, string filename)
        {
            string start = "BEGIN_RPC_BP_EVENTS_FROM_SERVER_CALL";
            string end = "END_RPC_BP_EVENTS_FROM_SERVER_CALL";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(start);

            foreach (var item in methods)
            {
                sb.AppendLine($"\t// {item.Name}");
                sb.AppendLine($"\tif (methodName == \"{item.Name}\")");
                sb.AppendLine($"\t{{");

                sb.AppendLine($"\t\t{item.Name}(");
                for (int i = 0; i < item.Parameters.Length; i++)
                {
                    string line = string.Empty;
                    if (IsNetworkActor(item.Parameters[i].Type) == true)
                    {
                        line = $"FindActor(paramField[{i}]->{GetJsonFieldGetter(item.Parameters[i])}(\"value\"))";
                    }
                    else
                    { 
                        line = $"paramField[{i}]->{GetJsonFieldGetter(item.Parameters[i])}(\"value\")";
                    }
                    if (i < item.Parameters.Length - 1)
                    {
                        sb.AppendLine($"\t\t\t\t{line},");
                    }
                    else
                    {
                        sb.AppendLine($"\t\t\t\t{line}");
                    }
                }
                sb.AppendLine($"\t\t\t);");
                sb.AppendLine($"\t}}");
                sb.AppendLine($"\t");
            }

            sb.Append("\t" + end);

            string code = sb.ToString();
            ReplaceInFile(filename, code, start, end);
        }
        #endregion

        private string GetEventName(IEventSymbol symbol)
        {
            string name = symbol.Name;
            if (name.StartsWith("OnHandle"))
            {
                name = name.Substring("OnHandle".Length);
            }
            return name;
        }

        private string GetEventParameterName(IEventSymbol symbol, int index, Dictionary<IEventSymbol, string[]> eventParameterNameLookup)
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

        private void GenerateHeaderDataForEvents(GeneratorExecutionContext context, List<IEventSymbol> events, string filename,
            Dictionary<IEventSymbol, string[]> eventParameterNameLookup)
        {
            string start = "BEGIN_RPC_BP_EVENTS_TO_SERVER";
            string end = "END_RPC_BP_EVENTS_TO_SERVER";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(start);

            foreach (var item in events)
            {
                sb.AppendLine("\tUFUNCTION(BlueprintCallable, Category = Rpc)");
                sb.Append($"\t\tvoid Server_{GetEventName(item)}(");

                var members = item.Type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i].Name == "Invoke")
                    {
                        var ms = members[i] as IMethodSymbol;
                        // Skip first, which is the Controller itself
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            if (j > 1)
                            {
                                sb.Append(", ");
                            }
                            sb.Append(GetUnrealParameterType(ms.Parameters[j].Type) + " " + GetEventParameterName(item, j, eventParameterNameLookup));
                        }
                    }
                }
                sb.AppendLine(");");
            }

            sb.Append("\t" + end);

            string code = sb.ToString();
            ReplaceInFile(filename, code, start, end);
        }

        private void GenerateImplementationDataForEvents(GeneratorExecutionContext context, List<IEventSymbol> events, string filename,
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
                        sb.Append($"void {unrealClassName}::Server_{GetEventName(item)}(");

                        var ms = members[i] as IMethodSymbol;
                        // Skip first, which is the Controller itself
                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            if (j > 1)
                            {
                                sb.Append(", ");
                            }
                            sb.Append(GetUnrealParameterType(ms.Parameters[j].Type) + " " + GetEventParameterName(item, j, eventParameterNameLookup));
                        }

                        sb.AppendLine($")");
                        sb.AppendLine($"{{");

                        sb.AppendLine($"   UVaRestJsonObject* jsonObj = NewObject<UVaRestJsonObject>();");
                        sb.AppendLine($"   jsonObj->SetStringField(\"methodName\", TEXT(\"{item.Name}\"));");
                        sb.AppendLine($"   jsonObj->SetInt64Field(\"uniqueMsgId\", -1);");
                        sb.AppendLine($"   TArray<UVaRestJsonObject*> paramField;");

                        for (int j = 1; j < ms.Parameters.Length; j++)
                        {
                            var setter = GetJsonFieldSetter(ms.Parameters[j]);
                            sb.AppendLine($"   UVaRestJsonObject* fieldObj_{ms.Parameters[j].Name} = NewObject<UVaRestJsonObject>();");
                            sb.AppendLine($"   fieldObj_{ms.Parameters[j].Name}->SetStringField(\"name\", TEXT(\"{ms.Parameters[j].Name}\"));");
                            sb.AppendLine($"   fieldObj_{ms.Parameters[j].Name}->SetIntegerField(\"type\", {GetParameterType(ms.Parameters[j].Type)});");
                            if (IsNetworkActor(ms.Parameters[j].Type) == true)
                            {
                                //sb.AppendLine($"   fieldObj_{ms.Parameters[j].Name}->{setter}(\"value\", FindActor({GetEventParameterName(item, j, eventParameterNameLookup)}->UniqueId));");
                                sb.AppendLine($"   fieldObj_{ms.Parameters[j].Name}->{setter}(\"value\", FindUniqueId({GetEventParameterName(item, j, eventParameterNameLookup)}));");
                            }
                            else
                            {
                                sb.AppendLine($"   fieldObj_{ms.Parameters[j].Name}->{setter}(\"value\", {GetEventParameterName(item, j, eventParameterNameLookup)});");
                            }
                            sb.AppendLine($"   paramField.Add(fieldObj_{ms.Parameters[j].Name});");
                        }

                        sb.AppendLine($"   jsonObj->SetObjectArrayField(\"Parameters\", paramField);");
                        sb.AppendLine($"   auto str = jsonObj->EncodeJsonToSingleString();");
                        sb.AppendLine($"   FTCHARToUTF8 cnv(*str);");
                        sb.AppendLine($"   int32 Len = cnv.Length();");
                        sb.AppendLine($"   TArray<uint8> bytes;");
                        sb.AppendLine($"   bytes.AddUninitialized(Len);");
                        sb.AppendLine($"   FMemory::Memcpy(bytes.GetData(), cnv.Get(), Len);");
                        sb.AppendLine($"   UULSWirePacket* packet = NewObject<UULSWirePacket>();");
                        sb.AppendLine($"   packet->PacketType = (int32)EWirePacketType::Rpc;");
                        sb.AppendLine($"   packet->Payload = bytes;");
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
        private void ReplaceInFile(string filename, string content, string start, string end)
        {
            string contents = File.ReadAllText(filename);

            int idx = contents.IndexOf(start);
            int idx2 = contents.IndexOf(end, idx);

            string part1 = contents.Substring(0, idx);
            string part2 = contents.Substring(idx2 + end.Length);

            contents = part1 + content + part2;
            File.WriteAllText(filename, contents);
        }

        private string? FindHeaderFileForClass(string className)
        {
            string baseFn = GetBaseFilenameForClass(className) + ".h";
            if (File.Exists(Path.Combine(UnrealModuleBaseDir, baseFn)))
            {
                return Path.Combine(UnrealModuleBaseDir, baseFn);
            }

            string[] files = Directory.GetFiles(Path.Combine(UnrealModuleBaseDir), "*.h", SearchOption.AllDirectories);
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

        private string? FindImplementationFileForClass(string className)
        {
            string baseFn = GetBaseFilenameForClass(className) + ".cpp";
            if (File.Exists(Path.Combine(UnrealModuleBaseDir, baseFn)))
            {
                return Path.Combine(UnrealModuleBaseDir, baseFn);
            }

            string[] files = Directory.GetFiles(Path.Combine(UnrealModuleBaseDir), "*.cpp", SearchOption.AllDirectories);
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

        private string? GetBaseFilenameForClass(string className)
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
        private bool ValidateUnrealProjectAttribute(GeneratorExecutionContext context, SyntaxReceiver receiver)
        {
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

            UnrealModuleBaseDir = Path.Combine(
                Path.GetDirectoryName(receiver.UnrealProject.ProjectFile),
                "Plugins",
                receiver.UnrealProject.Plugin,
                "Source",
                receiver.UnrealProject.Plugin);

            if (Directory.Exists(UnrealModuleBaseDir) == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    Code_UnrealPluginInvalid, "", "Module folder at '" + UnrealModuleBaseDir +
                    "' does not exist or is not readable. Skipping code generation for Unreal. Check the Module field in the UnrealProjectAttribute.",
                    "", DiagnosticSeverity.Error, true),
                    null));
                return false;
            }

            return true;
        }
        #endregion

        private object GetJsonFieldGetter(IParameterSymbol paramSymbol)
        {
            if (IsNetworkActor(paramSymbol.Type) == true)
            {
                return "GetInt64Field";
            }

            string csharpType = paramSymbol.Type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "GetStringField";

                case "int":
                    return "GetIntegerField";

                case "long":
                    return "GetInt64Field";

                case "float":
                    return "GetFloatField";

                default:
                    return "GetStringField";
            }
        }

        private object GetJsonFieldSetter(IParameterSymbol paramSymbol)
        {
            if (IsNetworkActor(paramSymbol.Type) == true)
            {
                return "SetInt64Field";
            }

            string csharpType = paramSymbol.Type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "SetStringField";

                case "int":
                    return "SetIntegerField";

                case "long":
                    return "SetInt64Field";

                case "float":
                    return "SetFloatField";

                default:
                    return "SetStringField";
            }
        }

        private string GetParameterType(ITypeSymbol type)
        {
            if (IsNetworkActor(type) == true)
            {
                return "4";
            }

            string csharpType = type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "0";

                case "int":
                    return "1";

                case "long":
                    return "2";

                case "float":
                    return "3";

                default:
                    return "4";
            }
        }

        private string GetUnrealReturnType(ITypeSymbol type)
        {
            if (IsNetworkActor(type) == true)
            {
                return "AActor*";
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

        private string GetUnrealParameterType(ITypeSymbol type)
        {
            if (IsNetworkActor(type) == true)
            {
                return "const AActor*";
            }

            string csharpType = type.ToString();
            switch (csharpType)
            {
                case "string":
                    return "const FString&";

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
    }
}
