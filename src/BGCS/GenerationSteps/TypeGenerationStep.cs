namespace BGCS.GenerationSteps
{
    using BGCS.Core;
    using BGCS.Core.Collections;
    using BGCS.Core.CSharp;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;
    using BGCS.FunctionGeneration;
    using BGCS.Metadata;
    using Irony.Parsing.Construction;
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>TypeGenerationStep</c>.
    /// </summary>
    public class TypeGenerationStep : GenerationStep
    {
        protected readonly HashSet<string> LibDefinedTypes = new();
        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public readonly HashSet<string> DefinedTypes = new();
        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public readonly Dictionary<string, string> WrappedPointers = new();
        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public readonly Dictionary<string, HashSet<CsFunctionVariation>> MemberFunctions = new();

        protected FunctionGenerator funcGen = null!;

        /// <summary>
        /// Initializes a new instance of <see cref="TypeGenerationStep"/>.
        /// </summary>
        public TypeGenerationStep(CsCodeGenerator generator, CsCodeGeneratorConfig config) : base(generator, config)
        {
        }

        /// <summary>
        /// Gets <c>Name</c>.
        /// </summary>
        public override string Name { get; } = "Types";

        /// <summary>
        /// Executes public operation <c>Configure</c>.
        /// </summary>
        public override void Configure(CsCodeGeneratorConfig config)
        {
            Enabled = config.GenerateTypes;
            funcGen = generator.FunctionGenerator;
        }

        /// <summary>
        /// Executes public operation <c>CopyToMetadata</c>.
        /// </summary>
        public override void CopyToMetadata(CsCodeGeneratorMetadata metadata)
        {
            metadata.DefinedTypes.AddRange(DefinedTypes);
            metadata.WrappedPointers.AddRange(WrappedPointers);
        }

        /// <summary>
        /// Executes public operation <c>CopyFromMetadata</c>.
        /// </summary>
        public override void CopyFromMetadata(CsCodeGeneratorMetadata metadata)
        {
            LibDefinedTypes.AddRange(metadata.DefinedTypes);
            WrappedPointers.AddRange(metadata.WrappedPointers);
        }

        /// <summary>
        /// Executes public operation <c>Reset</c>.
        /// </summary>
        public override void Reset()
        {
            LibDefinedTypes.Clear();
            DefinedTypes.Clear();
            WrappedPointers.Clear();
            MemberFunctions.Clear();
        }

        protected virtual List<string> SetupTypeUsings()
        {
            List<string> usings =
            [
                "System", "System.Diagnostics", "System.Runtime.CompilerServices", "System.Runtime.InteropServices", "BGCS.Runtime",
                .. config.Usings,
            ];
            return usings;
        }

        protected virtual bool FilterType(GenContext? context, CppClass cppClass, out TypeMapping? mapping, [NotNullWhen(false)] out string? csName, bool bypassDef = false)
        {
            csName = null;
            mapping = null;
            if (string.IsNullOrWhiteSpace(cppClass.Name) || cppClass.Name.StartsWith("(anonymous", StringComparison.OrdinalIgnoreCase))
                return true;
            if (config.AllowedTypes.Count != 0 && !config.AllowedTypes.Contains(cppClass.Name))
                return true;
            if (config.IgnoredTypes.Contains(cppClass.Name))
                return true;
            if (LibDefinedTypes.Contains(cppClass.Name))
                return true;

            if (DefinedTypes.Contains(cppClass.Name) && !bypassDef)
            {
                LogWarn($"{context?.FilePath}: {cppClass} is already defined!");
                return true;
            }

            if (!bypassDef)
                DefinedTypes.Add(cppClass.Name);

            csName = config.GetCsCleanName(cppClass.Name);
            mapping = config.GetTypeMapping(cppClass.Name);
            csName = mapping?.FriendlyName ?? csName;

            if (cppClass.ClassKind == CppClassKind.Class || cppClass.Name.EndsWith("_T") || csName == "void" || string.IsNullOrWhiteSpace(csName))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Runs generation logic through <c>Generate</c>.
        /// </summary>
        public override void Generate(FileSet files, ParseResult result, string outputPath, CsCodeGeneratorConfig config, CsCodeGeneratorMetadata metadata)
        {
            var compilation = result.Compilation;
            string folder = Path.Combine(outputPath, "Structs");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);

            MemberFunctions.Clear();
            IndexWrappedPointers(files, compilation);

            if (config.OneFilePerType)
            {
                // Generate Structures

                for (int i = 0; i < compilation.Classes.Count; i++)
                {
                    CppClass cppClass = compilation.Classes[i];
                    if (!files.Contains(cppClass.SourceFile))
                        continue;

                    if (FilterType(null, cppClass, out var mapping, out var csNameDefault))
                        continue;
                    string filePath = Path.Combine(folder, $"{csNameDefault}.cs");
                    using var writer = new CsCodeWriter(filePath, config.Namespace, SetupTypeUsings(), config.HeaderInjector);
                    GenContext context = new(result, filePath, writer);
                    using (PushApiTypeScope(writer))
                    {
                        WriteClass(context, cppClass, mapping, csNameDefault);

                        if (config.WrapPointersAsHandle)
                        {
                            WriteHandle(context, cppClass);
                        }
                    }
                }
            }
            else
            {
                string filePath = Path.Combine(folder, "Structs.cs");

                // Generate Structures
                using var writer = new CsSplitCodeWriter(filePath, config.Namespace, SetupTypeUsings(), config.HeaderInjector, 1);
                GenContext context = new(result, filePath, writer);

                // Print All classes, structs
                for (int i = 0; i < compilation.Classes.Count; i++)
                {
                    CppClass cppClass = compilation.Classes[i];
                    if (!files.Contains(cppClass.SourceFile))
                        continue;

                    if (FilterType(context, cppClass, out var mapping, out var csNameDefault))
                        continue;

                    using (PushApiTypeScope(writer))
                    {
                        WriteClass(context, cppClass, mapping, csNameDefault);

                        if (config.WrapPointersAsHandle)
                        {
                            WriteHandle(context, cppClass);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Defines the public class <c>CsStruct</c>.
        /// </summary>
        public class CsStruct
        {
            /// <summary>
            /// Exposes public member <c>Name</c>.
            /// </summary>
            public string Name;
            /// <summary>
            /// Exposes public member <c>CppType</c>.
            /// </summary>
            public CppClass CppType;
            /// <summary>
            /// Exposes public member <c>LayoutKind.Sequential</c>.
            /// </summary>
            public LayoutKind LayoutKind = LayoutKind.Sequential;
            /// <summary>
            /// Exposes public member <c>[]</c>.
            /// </summary>
            public List<CsStruct> SubTypes = [];
            /// <summary>
            /// Exposes public member <c>[]</c>.
            /// </summary>
            public List<CsField> Fields = [];
            /// <summary>
            /// Exposes public member <c>[]</c>.
            /// </summary>
            public List<CsPropertyMetadata> Properties = [];
            /// <summary>
            /// Exposes public member <c>[]</c>.
            /// </summary>
            public List<string> Attributes = [];
            /// <summary>
            /// Exposes public member <c>Comment</c>.
            /// </summary>
            public string? Comment;

            /// <summary>
            /// Initializes a new instance of <see cref="CsStruct"/>.
            /// </summary>
            public CsStruct(string name, CppClass cppType)
            {
                Name = name;
                CppType = cppType;
            }

            /// <summary>
            /// Exposes public member <c>CppType.IsAnonymous</c>.
            /// </summary>
            public bool IsAnonymous => CppType.IsAnonymous;
        }

        /// <summary>
        /// Defines the public class <c>CsField</c>.
        /// </summary>
        public class CsField
        {
            /// <summary>
            /// Exposes public member <c>Name</c>.
            /// </summary>
            public string Name;
            /// <summary>
            /// Exposes public member <c>Type</c>.
            /// </summary>
            public CsType Type;
            /// <summary>
            /// Exposes public member <c>CppField</c>.
            /// </summary>
            public CppField CppField;
            /// <summary>
            /// Exposes public member <c>[]</c>.
            /// </summary>
            public List<string> Attributes = [];
            /// <summary>
            /// Exposes public member <c>Comment</c>.
            /// </summary>
            public string? Comment;

            /// <summary>
            /// Initializes a new instance of <see cref="CsField"/>.
            /// </summary>
            public CsField(string name, CsType type, CppField cppField)
            {
                Name = name;
                Type = type;
                CppField = cppField;
            }

            /// <summary>
            /// Exposes public member <c>CppField.IsAnonymous</c>.
            /// </summary>
            public bool IsAnonymous => CppField.IsAnonymous;

            /// <summary>
            /// Exposes public member <c>CppField.Offset</c>.
            /// </summary>
            public long Offset => CppField.Offset;

            /// <summary>
            /// Exposes public member <c>CppField.BitOffset</c>.
            /// </summary>
            public long BitOffset => CppField.BitOffset;

            /// <summary>
            /// Exposes public member <c>CppField.IsBitField</c>.
            /// </summary>
            public bool IsBitField => CppField.IsBitField;

            /// <summary>
            /// Exposes public member <c>CppField.BitFieldWidth</c>.
            /// </summary>
            public int BitFieldWidth => CppField.BitFieldWidth;

            /// <summary>
            /// Exposes public member <c>CppField.Type.SizeOf</c>.
            /// </summary>
            public int SizeOf => CppField.Type.SizeOf;
        }

        protected CsStruct ParseClass(CppClass cppClass, TypeMapping? mapping, string csName)
        {
            CsStruct csStruct = new(csName, cppClass);

            if (mapping?.Comment != null)
            {
                config.WriteCsSummary(mapping.Comment, out csStruct.Comment);
            }
            else
            {
                config.WriteCsSummary(cppClass.Comment, out csStruct.Comment);
            }

            if (config.GenerateMetadata)
            {
                csStruct.Attributes.Add($"[NativeName(NativeNameType.StructOrClass, \"{cppClass.FullName}\")]");
            }

            if (cppClass.ClassKind == CppClassKind.Union)
            {
                csStruct.LayoutKind = LayoutKind.Explicit;
            }

            for (int i = 0; i < cppClass.Classes.Count; i++)
            {
                var subClass = cppClass.Classes[i];
                var csSubName = config.GetCsSubTypeName(cppClass, csName, subClass, i);
                var subClassMapping = config.GetTypeMapping(subClass.FullName);
                csStruct.SubTypes.Add(ParseClass(subClass, subClassMapping, csSubName));
            }

            for (int i = 0; i < cppClass.Fields.Count; i++)
            {
                CppField cppField = cppClass.Fields[i];
            }

            return csStruct;
        }

        protected virtual CsField ParseField(CppField cppField)
        {
            CsField csField = new(config.GetFieldName(cppField.Name), new CsType(config.GetCsTypeName(cppField.Type), cppField.Type.IsEnum(), cppField.Type.GetPrimitiveKind()), cppField);
            config.WriteCsSummary(cppField.Comment, out csField.Comment);

            if (config.GenerateMetadata)
            {
                csField.Attributes.Add($"[NativeName(NativeNameType.Field, \"{cppField.Name}\")]");
                csField.Attributes.Add($"[NativeName(NativeNameType.Type, \"{cppField.Type.GetDisplayName()}\")]");
            }

            return csField;
        }

        private static int GetNaturalAlignment(CppType type)
        {
            return type switch
            {
                CppQualifiedType qualified => GetNaturalAlignment(qualified.ElementType),
                CppArrayType array => GetNaturalAlignment(array.ElementType),
                CppTypedef typedef => GetNaturalAlignment(typedef.ElementType),
                CppClass cppClass => Math.Max(1, cppClass.AlignOf),
                CppEnum cppEnum => GetNaturalAlignment(cppEnum.IntegerType),
                CppPointerType or CppReferenceType or CppFunctionType => nint.Size,
                _ => Math.Clamp(type.SizeOf, 1, 8)
            };
        }

        protected virtual void WriteClass(GenContext context, CppClass cppClass, TypeMapping? mapping, string csName)
        {
            var writer = context.Writer;

            bool isReadOnly = false;
            string modifier = "partial";

            LogInfo("defined struct " + csName);

            bool commentWritten = false;
            if (mapping != null && mapping.Comment != null)
            {
                commentWritten = config.WriteCsSummary(mapping.Comment, writer);
            }
            else
            {
                commentWritten = config.WriteCsSummary(cppClass.Comment, writer);
            }

            if (config.GenerateMetadata)
            {
                writer.WriteLine($"[NativeName(NativeNameType.StructOrClass, \"{cppClass.FullName}\")]");
            }

            bool isUnion = cppClass.ClassKind == CppClassKind.Union;
            int naturalAlignment = cppClass.Fields.Count == 0 ? 1 : cppClass.Fields.Max(field => GetNaturalAlignment(field.Type));
            int pack = cppClass.AlignOf > 0 && cppClass.AlignOf < naturalAlignment ? cppClass.AlignOf : 0;
            string packArgument = pack == 0 ? string.Empty : $", Pack = {pack}";

            if (isUnion)
            {
                writer.WriteLine($"[StructLayout(LayoutKind.Explicit{packArgument})]");
            }
            else
            {
                writer.WriteLine($"[StructLayout(LayoutKind.Sequential{packArgument})]");
            }

            using (writer.PushBlock($"public {modifier} struct {csName}"))
            {
                if (config.GenerateSizeOfStructs && cppClass.SizeOf > 0)
                {
                    writer.WriteLine("/// <summary>");
                    writer.WriteLine($"/// The size of the <see cref=\"{csName}\"/> type, in bytes.");
                    writer.WriteLine("/// </summary>");
                    writer.WriteLine($"public static readonly int SizeInBytes = {cppClass.SizeOf};");
                    writer.WriteLine();
                }

                List<CsSubClass> subClasses = [];

                for (int j = 0; j < cppClass.Classes.Count; j++)
                {
                    var subClass = cppClass.Classes[j];
                    var csSubName = config.GetCsSubTypeName(cppClass, csName, subClass, j);
                    var subClassMapping = config.GetTypeMapping(subClass.FullName);

                    csSubName = subClassMapping?.FriendlyName ?? csSubName;
                    if (subClass.IsAnonymous)
                    {
                        config.TypeConverter.AddAnonymousMapping(subClass, csSubName);
                    }

                    WriteClass(context, subClass, subClassMapping, csSubName);
                    subClasses.Add(new(subClass, csSubName, cppClass.Name, GetSubClassFieldName(cppClass, mapping, subClass, j)));
                }

                List<CsPropertyMetadata> properties = [];
                int bitfieldCount = 0;
                for (int j = 0; j < cppClass.Fields.Count; j++)
                {
                    CppField cppField = cppClass.Fields[j];

                    if (cppField.IsBitField)
                    {
                        GenerateBitfields(context, cppClass, mapping, csName, ref j, properties, ref bitfieldCount);
                        continue;
                    }

                    var fieldMapping = mapping?.GetFieldMapping(cppField.Name);
                    if (cppField.Type is CppClass cppClass1 && cppClass1.ClassKind == CppClassKind.Union && cppClass1.FullParentName == cppClass.FullName)
                    {
                        var fieldCommentWritten = config.WriteCsSummary(cppField.Comment, writer);
                        if (!fieldCommentWritten)
                        {
                            fieldCommentWritten = config.WriteCsSummary(fieldMapping?.Comment, writer);
                        }

                        if (config.GenerateMetadata)
                        {
                            writer.WriteLine($"[NativeName(NativeNameType.Field, \"{cppField.Name}\")]");
                            writer.WriteLine($"[NativeName(NativeNameType.Type, \"{cppField.Type.GetDisplayName()}\")]");
                        }

                        var subClass = subClasses.FirstOrDefault(x => x.CppType == cppClass1);

                        if (isUnion)
                        {
                            writer.WriteLine("[FieldOffset(0)]");
                        }
                        if (subClass == null)
                        {
                            string csFieldName = GetMappedFieldName(cppField, fieldMapping);
                            string csFieldType = config.GetCsCleanName(cppClass1.Name);
                            subClasses.Add(new(cppField.Type, csFieldType, cppField.Name, csFieldName));

                            writer.WriteLine($"public {csFieldType} {csFieldName};");
                            if (fieldCommentWritten)
                            {
                                writer.WriteLine();
                            }

                            continue;
                        }

                        writer.WriteLine($"public {subClass.Name} {subClass.FieldName};");

                        if (fieldCommentWritten)
                            writer.WriteLine();
                    }
                    else if (cppField.Type is CppPointerType cppPointer && cppPointer.IsDelegate(out var cppFunctionType))
                    {
                        var fieldCommentWritten = config.WriteCsSummary(cppField.Comment, writer);
                        if (!fieldCommentWritten)
                        {
                            fieldCommentWritten = config.WriteCsSummary(fieldMapping?.Comment, writer);
                        }

                        if (config.GenerateMetadata)
                        {
                            writer.WriteLine($"[NativeName(NativeNameType.Field, \"{cppField.Name}\")]");
                            writer.WriteLine($"[NativeName(NativeNameType.Type, \"{cppField.Type.GetDisplayName()}\")]");
                        }

                        string csFieldName = GetMappedFieldName(cppField, fieldMapping);
                        if (config.DelegatesAsVoidPointer)
                        {
                            writer.WriteLine($"public unsafe void* {csFieldName};");
                        }
                        else
                        {
                            string delegatePointerType = config.GetDelegatePointerType(cppFunctionType, withConvention: true);
                            writer.WriteLine($"public unsafe {delegatePointerType} {csFieldName};");
                        }

                        if (fieldCommentWritten)
                            writer.WriteLine();
                    }
                    else
                    {
                        WriteField(writer, cppField, fieldMapping, subClasses, isUnion, isReadOnly);
                    }
                }

                writer.WriteLine();

                WriteValidityProperty(writer, cppClass, mapping);

                HashSet<string> definedConstructors = new();

                if (config.GenerateConstructorsForStructs && cppClass.Fields.Count > 0 &&
                    !cppClass.Fields.Any(static field => field.Type is CppArrayType array &&
                        (IsFlexibleArray(array) || array.ElementType is CppArrayType)))
                {
                    config.WriteCsSummary((string?)null, out string? comment);
                    CsFunction function = new(csName, comment);
                    CsFunctionOverload overload = new(string.Empty, csName, comment, csName, CsFunctionKind.Constructor, new(string.Empty, CppPrimitiveKind.Void));
                    function.Overloads.Add(overload);
                    for (int j = 0; j < cppClass.Fields.Count; j++)
                    {
                        var cppField = cppClass.Fields[j];
                        TypeFieldMapping? fieldMapping = mapping?.GetFieldMapping(cppField.Name);
                        var csFieldName = GetMappedFieldName(cppField, fieldMapping);
                        var paramCsTypeName = config.GetCsTypeName(cppField.Type);
                        var paramCsName = config.GetParameterName(j, cppField.Name);
                        var direction = cppField.Type.GetDirection();
                        var kind = cppField.Type.GetPrimitiveKind();
                        if (cppField.Type.IsDelegate(out CppFunctionType? constructorDelegate))
                        {
                            paramCsTypeName = config.GetDelegatePointerType(constructorDelegate, withConvention: true);
                        }
                        if (cppField.Type is CppArrayType parameterArray)
                        {
                            CppType elementType = AnalyzeArray(parameterArray, out _, out _);
                            paramCsTypeName = IsPointerAlias(elementType)
                                ? "nint*"
                                : config.GetCsTypeName(elementType) + "*";
                            kind = elementType.GetPrimitiveKind();
                            if (elementType is CppPointerType pointerType && pointerType.ElementType is CppFunctionType && config.DelegatesAsVoidPointer)
                            {
                                paramCsTypeName = "nint*";
                            }
                        }

                        var subClass = subClasses.FirstOrDefault(x => x.CppType == cppField.Type);

                        if (subClass != null && cppField.Type is CppClass cppClass1 && cppClass1.ClassKind == CppClassKind.Union)
                        {
                            subClass = subClasses.First(x => x.CppType == cppClass1);
                            paramCsTypeName = subClass.Name;
                            paramCsName = subClass.FieldName.ToLower();
                            csFieldName = subClass.FieldName;
                        }

                        if (subClass != null)
                        {
                            paramCsTypeName = subClass.Name;
                        }

                        int depth = 0;
                        var subClass1 = subClasses.FirstOrDefault(x => x.CppType.IsPointerOf(cppField.Type, ref depth));
                        if (subClass1 != null)
                        {
                            paramCsTypeName = subClass1.Name + new string('*', depth);
                        }

                        CsType csType = new(paramCsTypeName, kind);

                        CsParameterInfo csParameter = new(paramCsName, cppField.Type, csType, direction, "default", csFieldName);
                        overload.DefaultValues.TryAdd(paramCsName, "default");
                        overload.Parameters.Add(csParameter);
                    }

                    funcGen.GenerateConstructorVariations(cppClass, subClasses, csName, overload);
                    WriteConstructors(context, definedConstructors, function, overload, cppClass.Fields, properties, subClasses, "public unsafe");
                }

                writer.WriteLine();

                foreach (var property in properties)
                {
                    writer.WriteLine($"public {property.Type.Name} {property.Name} {{ {property.Getter} {property.Setter} }}");
                    writer.WriteLine();
                }

                for (int j = 0; j < cppClass.Fields.Count; j++)
                {
                    CppField cppField = cppClass.Fields[j];
                    if (cppField.Type.TypeKind == CppTypeKind.Array)
                    {
                        WriteProperty(writer, cppField, mapping?.GetFieldMapping(cppField.Name));
                    }
                }

                if (config.KnownMemberFunctions.TryGetValue(cppClass.Name, out var functions))
                {
                    WriteMemberFunctions(context, cppClass, functions, WriteFunctionFlags.UseThis);
                }
            }

            writer.WriteLine();
        }

        private void WriteValidityProperty(ICodeWriter writer, CppClass cppClass, TypeMapping? mapping)
        {
            StructValidityMapping? validity = mapping?.Validity;
            if (validity == null)
            {
                return;
            }

            CppField? field = cppClass.Fields.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, validity.FieldName, StringComparison.Ordinal));
            if (field == null)
            {
                LogError($"Validity mapping for '{cppClass.Name}' references missing field '{validity.FieldName}'.");
                return;
            }

            TypeFieldMapping? fieldMapping = mapping?.GetFieldMapping(field.Name);
            string fieldName = config.GetFieldName(field.Name, fieldMapping?.DisplayName);
            string propertyName = config.GetCsCleanName(validity.PropertyName);
            writer.WriteLine("/// <summary>");
            writer.WriteLine("/// Gets whether this native value handle differs from its invalid sentinel value.");
            writer.WriteLine("/// </summary>");
            writer.WriteLine($"public readonly bool {propertyName} => {fieldName} != {validity.InvalidValue};");
            writer.WriteLine();
        }

        private void GenerateBitfields(GenContext context, CppClass cppClass, TypeMapping? mapping, string csClassName, ref int fieldIndex, List<CsPropertyMetadata> properties, ref int bitfieldCount)
        {
            var writer = context.Writer;
            var fields = cppClass.Fields;
            if (fields[fieldIndex].BitFieldWidth <= 0)
            {
                return;
            }
            var baseOffset = fields[fieldIndex].BitOffset;
            var baseType = fields[fieldIndex].Type;
            int storageBitSize = baseType.SizeOf * 8;
            var csBaseType = new CsType(config.GetCsTypeName(baseType), baseType.IsEnum(), baseType.GetPrimitiveKind());

            var bitfieldId = bitfieldCount++;
            var bitfieldCsName = $"RawBits{bitfieldId}";
            if (cppClass.ClassKind == CppClassKind.Union)
            {
                writer.WriteLine($"[FieldOffset({fields[fieldIndex].Offset})]");
            }
            writer.WriteLine($"public {csBaseType.Name} {bitfieldCsName};");

            int bitfieldOffset = 0;
            int i = fieldIndex;
            for (; i < fields.Count; ++i)
            {
                var field = fields[i];
                if (!field.IsBitField)
                {
                    break;
                }

                if (!field.Type.IsType(baseType))
                {
                    break;
                }

                long relativeBitOffset = field.BitOffset - baseOffset;
                var bitfieldOffsetNext = bitfieldOffset + field.BitFieldWidth;
                if (field.BitFieldWidth <= 0 || bitfieldOffsetNext > storageBitSize ||
                    relativeBitOffset < 0 || relativeBitOffset + field.BitFieldWidth > storageBitSize)
                {
                    break;
                }
                var csFieldName = GetMappedFieldName(field, mapping?.GetFieldMapping(field.Name));
                string getter = IsSignedBitfield(baseType)
                    ? $"get => unchecked(({csBaseType.Name})((long)(Bitfield.ToULong(Bitfield.Get({bitfieldCsName}, {relativeBitOffset}, {field.BitFieldWidth})) << {64 - field.BitFieldWidth}) >> {64 - field.BitFieldWidth}));"
                    : $"get => Bitfield.Get({bitfieldCsName}, {relativeBitOffset}, {field.BitFieldWidth});";
                CsPropertyMetadata property = new(baseType, csBaseType, csFieldName, getter, $"set => Bitfield.Set(ref {bitfieldCsName}, value, {relativeBitOffset}, {field.BitFieldWidth});");
                properties.Add(property);
                bitfieldOffset = bitfieldOffsetNext;
            }

            fieldIndex = i - 1;
        }

        protected virtual void WriteHandle(GenContext context, CppClass cppClass)
        {
            if (FilterType(context, cppClass, out var mapping, out var csName, true))
                return;
            if (IsOpaqueHandlePointee(cppClass, context.Compilation))
                return;

            if (cppClass.IsUsedAsPointer(context.Compilation, out var depths))
            {
                for (int j = 0; j < depths.Count; j++)
                {
                    int depth = depths[j];
                    LogDebug("used as pointer: " + cppClass.Name + ", depth: " + depth);
                    StringBuilder sb1 = new();
                    StringBuilder sb2 = new();
                    sb1.Append(csName);
                    sb2.Append(csName);
                    for (int jj = 0; jj < depth; jj++)
                    {
                        sb1.Append("Ptr");
                        sb2.Append('*');
                    }

                    WriteStructHandle(context, cppClass, mapping, sb1.ToString(), sb2.ToString());
                    RegisterWrappedPointer(sb2.ToString(), sb1.ToString());
                }
            }
        }

        private void IndexWrappedPointers(FileSet files, CppCompilation compilation)
        {
            if (!config.WrapPointersAsHandle)
            {
                return;
            }

            for (int i = 0; i < compilation.Classes.Count; i++)
            {
                CppClass cppClass = compilation.Classes[i];
                if (!files.Contains(cppClass.SourceFile) ||
                    FilterType(null, cppClass, out _, out string? csName, true) ||
                    IsOpaqueHandlePointee(cppClass, compilation) ||
                    !cppClass.IsUsedAsPointer(compilation, out List<int>? depths))
                {
                    continue;
                }

                for (int j = 0; j < depths.Count; j++)
                {
                    int depth = depths[j];
                    RegisterWrappedPointer(
                        csName + new string('*', depth),
                        csName + string.Concat(Enumerable.Repeat("Ptr", depth)));
                }
            }
        }

        private void RegisterWrappedPointer(string nativePointerType, string wrappedPointerType)
        {
            if (WrappedPointers.TryGetValue(nativePointerType, out string? existingType))
            {
                if (!string.Equals(existingType, wrappedPointerType, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Pointer type '{nativePointerType}' maps to both '{existingType}' and '{wrappedPointerType}'.");
                }

                return;
            }

            WrappedPointers.Add(nativePointerType, wrappedPointerType);
        }

        private static bool IsOpaqueHandlePointee(CppClass cppClass, CppCompilation compilation)
        {
            for (int i = 0; i < compilation.Typedefs.Count; i++)
            {
                CppTypedef typedef = compilation.Typedefs[i];
                if (typedef.IsOpaqueHandle() && cppClass.IsPointerOf(typedef.ElementType))
                {
                    return true;
                }
            }

            return false;
        }

        protected virtual bool FilterConstructor(GenContext context, HashSet<string> definedFunctions, string header)
        {
            if (definedFunctions.Contains(header))
            {
                LogWarn($"{context.FilePath}: {header} constructor is already defined!");
                return true;
            }
            definedFunctions.Add(header);
            return false;
        }

        protected virtual void WriteConstructors(GenContext context, HashSet<string> definedFunctions, CsFunction csFunction, CsFunctionOverload overload, IList<CppField> fields, List<CsPropertyMetadata> properties, List<CsSubClass> subClasses, params string[] modifiers)
        {
            for (int j = 0; j < overload.Variations.Count; j++)
            {
                WriteConstructor(context, definedFunctions, csFunction, overload, overload.Variations[j], fields, subClasses, properties, modifiers);
            }
        }

        protected virtual void WriteConstructor(GenContext context, HashSet<string> definedFunctions, CsFunction function, CsFunctionOverload overload, CsFunctionVariation variation, IList<CppField> fields, List<CsSubClass> subClasses, List<CsPropertyMetadata> properties, params string[] modifiers)
        {
            var writer = context.Writer;
            CsType returnType = variation.ReturnType;
            generator.PrepareArgs(variation, returnType);

            string header = variation.BuildFullConstructorSignature(config.GenerateMetadata);
            string id = variation.BuildConstructorSignatureIdentifier();

            if (FilterConstructor(context, definedFunctions, id))
            {
                return;
            }

            CsCodeGenerator.ClassifyParameters(overload, variation, returnType, out _, out _, out _);

            LogInfo("defined constructor " + header);

            writer.WriteLines(overload.Comment);
            if (config.GenerateMetadata)
            {
                writer.WriteLines(overload.Attributes);
            }

            using (writer.PushBlock($"{string.Join(" ", modifiers)} {header}"))
            {
                for (int i = 0; i < overload.Parameters.Count; i++)
                {
                    var cppParameter = overload.Parameters[i];
                    var cppField = fields[i];

                    ParameterFlags paramFlags = ParameterFlags.None;

                    if (variation.TryGetParameter(cppParameter.Name, out var param))
                    {
                        cppParameter = param;
                        paramFlags = param.Flags;
                    }

                    var fieldName = cppParameter.FieldName;

                    if (string.IsNullOrEmpty(fieldName))
                    {
                        var subClass = subClasses.First(x => x.CppType == cppField.Type);
                        fieldName = subClass.Name;
                    }

                    if (fieldName == cppParameter.Name)
                    {
                        fieldName = $"this.{fieldName}";
                    }

                    bool targetsProperty = false;
                    if (properties.Any(x => x.Name == cppParameter.FieldName))
                    {
                        targetsProperty = true;
                    }

                    if (cppField.Type is CppArrayType arrayType)
                    {
                        CppType elementType = AnalyzeArray(arrayType, out int elementCount, out _);
                        bool isBoolArray = config.GetCsTypeName(elementType) == "bool";
                        string elementCast = IsPointerAlias(elementType) ? $"({config.GetCsTypeName(elementType)})" : string.Empty;
                        using (writer.PushBlock($"if ({cppParameter.Name} != default({cppParameter.Type.Name}))"))
                        {
                            for (int j = 0; j < elementCount; j++)
                            {
                                if (isBoolArray)
                                {
                                    writer.WriteLine($"{fieldName}_{j} = ({config.GetBoolType()})({cppParameter.Name}[{j}] ? 1 : 0);");
                                }
                                else
                                {
                                    writer.WriteLine($"{fieldName}_{j} = {elementCast}{cppParameter.Name}[{j}];");
                                }
                            }
                        }
                    }
                    else if (cppField.Type.IsDelegate(out var cppFunction))
                    {
                        string delegateType = $"({config.GetDelegatePointerType(cppFunction, withConvention: true)})";
                        if (cppParameter.Type.Name.StartsWith("delegate*<") || cppParameter.Type.Name.Contains('*') || cppParameter.Type.Name == "nint")
                        {
                            writer.WriteLine($"{fieldName} = {delegateType}{cppParameter.Name};");
                        }
                        else
                        {
                            writer.WriteLine($"{fieldName} = {delegateType}Marshal.GetFunctionPointerForDelegate({cppParameter.Name});");
                        }
                    }
                    else if (paramFlags.HasFlag(ParameterFlags.Bool) && !targetsProperty && !paramFlags.HasFlag(ParameterFlags.Ref) && !paramFlags.HasFlag(ParameterFlags.Pointer))
                    {
                        writer.WriteLine($"{fieldName} = {cppParameter.Name} ? ({config.GetBoolType()})1 : ({config.GetBoolType()})0;");
                    }
                    else
                    {
                        writer.WriteLine($"{fieldName} = {cppParameter.Name};");
                    }
                }
            }

            writer.WriteLine();
        }

        private static bool IsSignedBitfield(CppType type)
        {
            return type.GetPrimitiveKind() is CppPrimitiveKind.Short or CppPrimitiveKind.Int or
                CppPrimitiveKind.Long or CppPrimitiveKind.LongLong or CppPrimitiveKind.Int128;
        }

        private static bool IsFlexibleArray(CppArrayType arrayType)
        {
            while (true)
            {
                if (arrayType.Size <= 0)
                {
                    return true;
                }
                if (arrayType.ElementType is not CppArrayType nested)
                {
                    return false;
                }
                arrayType = nested;
            }
        }

        private static CppType AnalyzeArray(CppArrayType arrayType, out int elementCount, out bool isFlexible)
        {
            elementCount = 1;
            isFlexible = false;
            CppType elementType = arrayType;
            while (elementType is CppArrayType current)
            {
                if (current.Size <= 0)
                {
                    elementCount = 0;
                    isFlexible = true;
                }
                else if (!isFlexible)
                {
                    elementCount = checked(elementCount * current.Size);
                }
                elementType = current.ElementType;
            }
            return elementType;
        }

        private void WriteField(ICodeWriter writer, CppField field, TypeFieldMapping? mapping, List<CsSubClass> subClasses, bool isUnion = false, bool isReadOnly = false)
        {
            string csFieldName = GetMappedFieldName(field, mapping);

            var fieldCommentWritten = config.WriteCsSummary(field.Comment, writer);
            if (!fieldCommentWritten)
            {
                fieldCommentWritten = config.WriteCsSummary(mapping?.Comment, writer);
            }

            if (config.GenerateMetadata)
            {
                writer.WriteLine($"[NativeName(NativeNameType.Field, \"{field.Name}\")]");
                writer.WriteLine($"[NativeName(NativeNameType.Type, \"{field.Type.GetDisplayName()}\")]");
            }

            if (isUnion && field.Type is not CppArrayType)
            {
                writer.WriteLine("[FieldOffset(0)]");
            }

            if (field.Type is CppArrayType arrayType)
            {
                CppType elementType = AnalyzeArray(arrayType, out int elementCount, out bool isFlexible);
                if (isFlexible)
                {
                    writer.WriteLine($"public static readonly int {csFieldName}Offset = {field.Offset};");
                    if (fieldCommentWritten)
                        writer.WriteLine();
                    return;
                }

                string csFieldType = config.GetCsTypeName(elementType);
                if (csFieldType == "bool")
                {
                    csFieldType = config.GetBoolType();
                }
                if (elementType is CppPointerType pointerType && pointerType.ElementType is CppFunctionType && config.DelegatesAsVoidPointer)
                {
                    csFieldType = "nint";
                }

                string unsafePrefix = csFieldType.Contains('*') ? "unsafe " : string.Empty;
                for (int i = 0; i < elementCount; i++)
                {
                    if (isUnion)
                    {
                        writer.WriteLine($"[FieldOffset({elementType.SizeOf * i})]");
                    }
                    writer.WriteLine($"public {unsafePrefix}{csFieldType} {csFieldName}_{i};");
                }
            }
            else
            {
                string csFieldType = config.GetCsTypeName(field.Type);

                var subClass = subClasses.Find(x => x.CppType == field.Type);
                if (subClass != null)
                {
                    csFieldType = subClass.Name;
                }

                int depth = 0;
                var subClass1 = subClasses.FirstOrDefault(x => x.CppType.IsPointerOf(field.Type, ref depth));
                if (subClass1 != null)
                {
                    csFieldType = subClass1.Name + new string('*', depth);
                }

                string fieldPrefix = isReadOnly ? "readonly " : string.Empty;

                if (csFieldType == "bool")
                    csFieldType = config.GetBoolType();

                if (field.Type is CppTypedef typedef &&
                    typedef.ElementType is CppPointerType pointerType &&
                    pointerType.ElementType is CppFunctionType functionType)
                {
                    if (config.DelegatesAsVoidPointer)
                    {
                        writer.WriteLine($"public {fieldPrefix}unsafe void* {csFieldName};");
                    }
                    else
                    {
                        string delegatePointerType = config.GetDelegatePointerType(functionType, withConvention: true);
                        writer.WriteLine($"public {fieldPrefix}unsafe {delegatePointerType} {csFieldName};");
                    }

                    return;
                }

                if (csFieldType.EndsWith('*'))
                {
                    fieldPrefix += "unsafe ";
                }

                writer.WriteLine($"public {fieldPrefix}{csFieldType} {csFieldName};");
            }

            if (fieldCommentWritten)
                writer.WriteLine();
        }

        private void WriteProperty(ICodeWriter writer, CppClass cppClass, string classCsName, CppField field, TypeFieldMapping? mapping, bool isUnion = false, bool isReadOnly = false)
        {
            string csFieldName = GetMappedFieldName(field, mapping);

            var fieldCommentWritten = config.WriteCsSummary(field.Comment, writer);
            if (!fieldCommentWritten)
            {
                fieldCommentWritten = config.WriteCsSummary(mapping?.Comment, writer);
            }

            if (field.Type is CppArrayType arrayType)
            {
                CppType elementType = AnalyzeArray(arrayType, out int elementCount, out bool isFlexible);
                string csFieldType = config.GetCsTypeName(elementType);
                if (elementType is CppClass nestedClass)
                {
                    int nestedIndex = cppClass.Classes.IndexOf(nestedClass);
                    if (nestedIndex != -1)
                    {
                        string ownerName = classCsName.Replace("*", string.Empty, StringComparison.Ordinal);
                        csFieldType = ownerName + "." + config.GetCsSubTypeName(cppClass, ownerName, nestedClass, nestedIndex);
                    }
                }
                if (elementType is CppTypedef typedef && !IsPointerAlias(typedef) && typedef.IsPrimitive(out var primitive))
                {
                    csFieldType = config.GetCsTypeName(primitive);
                }
                if (csFieldType == "bool")
                {
                    csFieldType = config.GetBoolType();
                }

                string spanType = csFieldType;
                if (csFieldType.Contains("delegate*"))
                {
                    csFieldType = "nint";
                    spanType = "nint";
                }
                else if (csFieldType.Contains('*'))
                {
                    spanType = $"Pointer<{csFieldType.Replace("*", "")}>";
                }

                if (isFlexible)
                {
                    writer.WriteLine($"public unsafe Span<{spanType}> {csFieldName}(int length) => new((void*)((byte*)Handle + {field.Offset}), length);");
                }
                else
                {
                    writer.WriteLine($"public unsafe Span<{spanType}> {csFieldName}");
                    using (writer.PushBlock(""))
                    {
                        using (writer.PushBlock("get"))
                        {
                            writer.WriteLine($"return new Span<{spanType}>((void*)&Handle->{csFieldName}_0, {elementCount});");
                        }
                    }
                }
            }
            else
            {
                string csFieldType;

                if (field.Type is CppClass subClass)
                {
                    int index = IndexOfSubClass(cppClass, subClass);
                    if (index != -1)
                    {
                        CppClass declaredSubClass = cppClass.Classes[index];
                        string ownerName = classCsName.Replace("*", "", StringComparison.Ordinal);
                        csFieldType = ownerName + "." + config.GetCsSubTypeName(cppClass, ownerName, declaredSubClass, index);
                        csFieldName = GetSubClassFieldName(cppClass, config.GetTypeMapping(cppClass.FullName), declaredSubClass, index);
                    }
                    else
                    {
                        csFieldType = config.GetCsTypeName(field.Type);
                    }
                }
                else
                {
                    csFieldType = config.GetCsTypeName(field.Type);
                }

                if (field.Type.IsDelegate(out var functionType))
                {
                    if (config.DelegatesAsVoidPointer)
                    {
                        if (isReadOnly)
                        {
                            writer.WriteLine($"public void* {csFieldName} {{ get => Handle->{csFieldName}; }}");
                        }
                        else
                        {
                            writer.WriteLine($"public void* {csFieldName} {{ get => Handle->{csFieldName}; set => Handle->{csFieldName} = value; }}");
                        }
                    }
                    else
                    {
                        string delegatePointerType = config.GetDelegatePointerType(functionType, withConvention: true);
                        if (isReadOnly)
                        {
                            writer.WriteLine($"public {delegatePointerType} {csFieldName} {{ get => Handle->{csFieldName}; }}");
                        }
                        else
                        {
                            writer.WriteLine($"public {delegatePointerType} {csFieldName} {{ get => Handle->{csFieldName}; set => Handle->{csFieldName} = value; }}");
                        }
                    }

                    return;
                }

                if (WrappedPointers.TryGetValue(csFieldType, out string? wrappedPointerType))
                {
                    csFieldType = wrappedPointerType;
                }

                if (csFieldType.EndsWith('*') || field.IsBitField)
                {
                    if (isReadOnly)
                    {
                        writer.WriteLine($"public {csFieldType} {csFieldName} => Handle->{csFieldName};");
                    }
                    else
                    {
                        writer.WriteLine($"public {csFieldType} {csFieldName} {{ get => Handle->{csFieldName}; set => Handle->{csFieldName} = value; }}");
                    }
                }
                else
                {
                    if (isReadOnly)
                    {
                        writer.WriteLine($"public {csFieldType} {csFieldName} => Handle->{csFieldName};");
                    }
                    else
                    {
                        writer.WriteLine($"public ref {csFieldType} {csFieldName} => ref Unsafe.AsRef<{csFieldType}>(&Handle->{csFieldName});");
                    }
                }
            }
        }

        private void WriteProperty(ICodeWriter writer, CppField field, TypeFieldMapping? mapping)
        {
            string csFieldName = GetMappedFieldName(field, mapping);

            if (field.Type is CppArrayType arrayType)
            {
                CppType elementType = AnalyzeArray(arrayType, out int elementCount, out bool isFlexible);
                if (isFlexible)
                {
                    return;
                }

                string csFieldType = config.GetCsTypeName(elementType);
                string spanType = csFieldType;
                bool canUseFixed = elementType is CppPrimitiveType;
                if (elementType is CppTypedef typedef && !IsPointerAlias(typedef) && typedef.IsPrimitive(out var primitive))
                {
                    csFieldType = config.GetCsTypeName(primitive);
                    canUseFixed = true;
                }
                if (csFieldType == "bool")
                {
                    csFieldType = config.GetBoolType();
                    spanType = csFieldType;
                }

                if (canUseFixed)
                {
                    config.WriteCsSummary(field.Comment, writer);
                    writer.WriteLine($"public Span<{csFieldType}> {csFieldName} => MemoryMarshal.CreateSpan(ref {csFieldName}_0, {elementCount});");
                }
                else
                {
                    if (csFieldType.Contains("delegate*"))
                    {
                        if (config.DelegatesAsVoidPointer)
                        {
                            csFieldType = "nint";
                        }
                        spanType = "nint";
                    }
                    else if (IsPointerAlias(elementType))
                    {
                        spanType = "nint";
                    }
                    else if (csFieldType.Contains('*'))
                    {
                        spanType = $"Pointer<{csFieldType.Replace("*", "")}>";
                    }

                    config.WriteCsSummary(field.Comment, writer);
                    writer.WriteLine($"public unsafe Span<{spanType}> {csFieldName}");
                    using (writer.PushBlock(""))
                    {
                        using (writer.PushBlock("get"))
                        {
                            using (writer.PushBlock($"fixed ({csFieldType}* p = &this.{csFieldName}_0)"))
                            {
                                writer.WriteLine($"return new Span<{spanType}>(p, {elementCount});");
                            }
                        }
                    }
                }
            }
        }

        private static bool IsPointerAlias(CppType type)
        {
            while (type is CppTypedef typedef)
                type = typedef.ElementType;
            while (type is CppQualifiedType qualified)
                type = qualified.ElementType;
            return type is CppPointerType;
        }

        private string GetSubClassFieldName(CppClass parentClass, TypeMapping? parentMapping, CppClass subClass, int subClassIndex)
        {
            int fieldIndex = CsCodeGeneratorConfig.IndexOfAnonymousField(parentClass, subClass);
            if (fieldIndex >= 0)
            {
                CppField field = parentClass.Fields[fieldIndex];
                string fieldName = GetMappedFieldName(field, parentMapping?.GetFieldMapping(field.Name));
                if (!string.IsNullOrEmpty(fieldName))
                {
                    return fieldName;
                }
            }

            return $"Union{(parentClass.Classes.Count == 1 ? "" : subClassIndex.ToString())}";
        }

        private string GetMappedFieldName(CppField field, TypeFieldMapping? mapping)
        {
            return config.GetFieldName(field.Name, mapping?.DisplayName);
        }

        private static int IndexOfSubClass(CppClass parentClass, CppType fieldType)
        {
            CppType canonicalFieldType = fieldType.GetCanonicalRoot(true);
            for (int i = 0; i < parentClass.Classes.Count; i++)
            {
                if (canonicalFieldType == parentClass.Classes[i])
                {
                    return i;
                }
            }

            return -1;
        }

        private void WriteStructHandle(GenContext context, CppClass cppClass, TypeMapping? mapping, string csName, string handleType)
        {
            var writer = context.Writer;
            bool isUnion = cppClass.ClassKind == CppClassKind.Union;
            LogInfo("defined handle " + csName);
            config.WriteCsSummary(cppClass.Comment, writer);
            if (config.GenerateMetadata)
            {
                writer.WriteLine($"[NativeName(NativeNameType.Typedef, \"{cppClass.Name}\")]");
            }
            writer.WriteLine($"[DebuggerDisplay(\"{{DebuggerDisplay,nq}}\")]");
            using (writer.PushBlock($"public unsafe struct {csName} : IEquatable<{csName}>"))
            {
                string nullValue = "null";

                writer.WriteLine($"public {csName}({handleType} handle) {{ Handle = handle; }}");
                writer.WriteLine();
                writer.WriteLine($"public {handleType} Handle;");
                writer.WriteLine();
                writer.WriteLine($"public bool IsNull => Handle == null;");
                writer.WriteLine();
                writer.WriteLine($"public static {csName} Null => new {csName}({nullValue});");
                writer.WriteLine();
                writer.WriteLine($"public {handleType.AsSpan().TrimEndFirstOccurrence('*')} this[int index] {{ get => Handle[index]; set => Handle[index] = value; }}");
                writer.WriteLine();
                writer.WriteLine($"public static implicit operator {csName}({handleType} handle) => new {csName}(handle);");
                writer.WriteLine();
                writer.WriteLine($"public static implicit operator {handleType}({csName} handle) => handle.Handle;");
                writer.WriteLine();
                writer.WriteLine($"public static bool operator ==({csName} left, {csName} right) => left.Handle == right.Handle;");
                writer.WriteLine();
                writer.WriteLine($"public static bool operator !=({csName} left, {csName} right) => left.Handle != right.Handle;");
                writer.WriteLine();
                writer.WriteLine($"public static bool operator ==({csName} left, {handleType} right) => left.Handle == right;");
                writer.WriteLine();
                writer.WriteLine($"public static bool operator !=({csName} left, {handleType} right) => left.Handle != right;");
                writer.WriteLine();
                writer.WriteLine($"public bool Equals({csName} other) => Handle == other.Handle;");
                writer.WriteLine();
                writer.WriteLine("/// <inheritdoc/>");
                writer.WriteLine($"public override bool Equals(object obj) => obj is {csName} handle && Equals(handle);");
                writer.WriteLine();
                writer.WriteLine("/// <inheritdoc/>");
                writer.WriteLine($"public override int GetHashCode() => ((nuint)Handle).GetHashCode();");
                writer.WriteLine();
                writer.WriteLine($"private string DebuggerDisplay => string.Format(\"{csName} [0x{{0}}]\", ((nuint)Handle).ToString(\"X\"));");
                var pCount = handleType.Count(x => x == '*');
                if (pCount == 1)
                {
                    for (int j = 0; j < cppClass.Fields.Count; j++)
                    {
                        CppField cppField = cppClass.Fields[j];
                        var fieldMapping = mapping?.GetFieldMapping(cppField.Name);
                        WriteProperty(writer, cppClass, handleType, cppField, fieldMapping, isUnion, false);
                    }

                    if (config.KnownMemberFunctions.TryGetValue(cppClass.Name, out var functions))
                    {
                        WriteMemberFunctions(context, cppClass, functions, WriteFunctionFlags.UseHandle);
                    }
                }
            }
            writer.WriteLine();
        }

        private void WriteMemberFunctions(GenContext context, CppClass cppClass, List<string> functions, WriteFunctionFlags flags)
        {
            HashSet<CsFunctionVariation> definedFunctions = new(IdentifierComparer<CsFunctionVariation>.Default);
            List<CsFunction> commands = new();
            for (int i = 0; i < functions.Count; i++)
            {
                CppFunction cppFunction = CsCodeGenerator.FindFunction(context.Compilation, functions[i]);
                var csFunctionName = config.GetCsFunctionName(cppFunction.Name);

                CsFunction function = generator.CreateCsFunction(cppFunction, CsFunctionKind.Member, csFunctionName, commands, out var overload);
                funcGen.GenerateVariations(cppFunction.Parameters, overload);

                bool useThisRef = false;
                if (cppFunction.Parameters.Count > 0 && cppClass.IsPointerOf(cppFunction.Parameters[0].Type))
                {
                    useThisRef = true;
                }

                bool useThis = false;
                if (cppFunction.Parameters.Count > 0 && cppClass.IsType(cppFunction.Parameters[0].Type))
                {
                    useThis = true;
                }

                if (useThis || useThisRef)
                {
                    GetGenerationStep<FunctionGenerationStep>().WriteFunctions(context, definedFunctions, function, overload, flags, "public unsafe");
                }
            }

            if (flags == WriteFunctionFlags.UseThis)
            {
                if (MemberFunctions.TryGetValue(cppClass.Name, out var funcs))
                {
                    foreach (var f in definedFunctions)
                    {
                        funcs.Add(f);
                    }
                }
                else
                {
                    MemberFunctions.Add(cppClass.Name, definedFunctions);
                }
            }
        }
    }
}
