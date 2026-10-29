using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: AssemblyInspector <assembly.dll|Managed directory> <type regex> [--members] [--il <method regex>]");
    return 1;
}

var input = Path.GetFullPath(args[0]);
var filter = new Regex(args[1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
var members = args.Contains("--members", StringComparer.Ordinal);
var ilArgument = Array.IndexOf(args, "--il");
var ilFilter = ilArgument >= 0 && ilArgument + 1 < args.Length
    ? new Regex(args[ilArgument + 1], RegexOptions.CultureInvariant)
    : null;
IEnumerable<string> files = Directory.Exists(input)
    ? Directory.EnumerateFiles(input, "*.dll").OrderBy(Path.GetFileName, StringComparer.Ordinal)
    : new[] { input };

foreach (var file in files)
{
    using var stream = File.OpenRead(file);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata) continue;
    var reader = pe.GetMetadataReader();
    var provider = new TypeNames(reader);
    foreach (var handle in reader.TypeDefinitions)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = provider.GetTypeFromDefinition(reader, handle, 0);
        if (!filter.IsMatch(name)) continue;
        Console.WriteLine($"[{Path.GetFileName(file)}] {name} : {provider.FromHandle(type.BaseType)}");
        if (!members && ilFilter == null) continue;
        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (members) Console.WriteLine($"  FIELD {field.Attributes} {field.DecodeSignature(provider, null)} {reader.GetString(field.Name)}");
        }
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var signature = method.DecodeSignature(provider, null);
            var methodName = reader.GetString(method.Name);
            var parameterNames = method.GetParameters().Select(reader.GetParameter)
                .Where(p => p.SequenceNumber > 0)
                .ToDictionary(p => (int)p.SequenceNumber, p => reader.GetString(p.Name));
            var parameters = signature.ParameterTypes.Select((t, i) =>
                parameterNames.TryGetValue(i + 1, out var n) && n.Length > 0 ? t + " " + n : t);
            if (members || ilFilter?.IsMatch(methodName) == true)
                Console.WriteLine($"  METHOD {method.Attributes} {signature.ReturnType} {methodName}({string.Join(", ", parameters)})");
            if (ilFilter?.IsMatch(methodName) == true && method.RelativeVirtualAddress != 0)
                IlListing.Write(pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!, reader, provider);
        }
    }
}

return 0;

sealed class TypeNames(MetadataReader reader) : ISignatureTypeProvider<string, object?>
{
    public string FromHandle(EntityHandle handle) => handle.IsNil ? "-" : handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
        _ => "-"
    };

    public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = metadata.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil
            ? Qualify(metadata.GetString(type.Namespace), metadata.GetString(type.Name))
            : GetTypeFromDefinition(metadata, declaring, 0) + "+" + metadata.GetString(type.Name);
    }

    public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = metadata.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(metadata, (TypeReferenceHandle)type.ResolutionScope, 0) + "+" + metadata.GetString(type.Name)
            : Qualify(metadata.GetString(type.Namespace), metadata.GetString(type.Name));
    }

    public string GetTypeFromSpecification(MetadataReader metadata, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
        => metadata.GetTypeSpecification(handle).DecodeSignature(this, context);
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? context, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    private static string Qualify(string ns, string name) => string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

static class IlListing
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields()
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => op.Value);

    public static void Write(byte[] bytes, MetadataReader reader, TypeNames names)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var start = offset;
            short value = bytes[offset++];
            if (value == 0xfe) value = unchecked((short)(0xfe00 | bytes[offset++]));
            var opcode = Opcodes[value];
            string operand;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    operand = "";
                    break;
                case OperandType.ShortInlineI:
                    operand = unchecked((sbyte)bytes[offset++]).ToString();
                    break;
                case OperandType.ShortInlineVar:
                    operand = bytes[offset++].ToString();
                    break;
                case OperandType.InlineVar:
                    operand = BitConverter.ToUInt16(bytes, offset).ToString(); offset += 2;
                    break;
                case OperandType.InlineI:
                    operand = ReadInt(bytes, ref offset).ToString();
                    break;
                case OperandType.InlineI8:
                    operand = BitConverter.ToInt64(bytes, offset).ToString(); offset += 8;
                    break;
                case OperandType.ShortInlineR:
                    operand = BitConverter.ToSingle(bytes, offset).ToString(System.Globalization.CultureInfo.InvariantCulture); offset += 4;
                    break;
                case OperandType.InlineR:
                    operand = BitConverter.ToDouble(bytes, offset).ToString(System.Globalization.CultureInfo.InvariantCulture); offset += 8;
                    break;
                case OperandType.ShortInlineBrTarget:
                    var delta = unchecked((sbyte)bytes[offset++]);
                    operand = $"IL_{offset + delta:x4}";
                    break;
                case OperandType.InlineBrTarget:
                    var jump = ReadInt(bytes, ref offset);
                    operand = $"IL_{offset + jump:x4}";
                    break;
                case OperandType.InlineSwitch:
                    var count = ReadInt(bytes, ref offset);
                    var end = offset + 4 * count;
                    var targets = new List<string>();
                    for (var i = 0; i < count; i++) targets.Add($"IL_{end + ReadInt(bytes, ref offset):x4}");
                    operand = string.Join(", ", targets);
                    break;
                case OperandType.InlineString:
                    operand = System.Text.Json.JsonSerializer.Serialize(reader.GetUserString(MetadataTokens.UserStringHandle(ReadInt(bytes, ref offset) & 0xffffff)));
                    break;
                default:
                    operand = Describe(reader, names, MetadataTokens.EntityHandle(ReadInt(bytes, ref offset)));
                    break;
            }
            Console.WriteLine($"    IL_{start:x4}: {opcode.Name} {operand}");
        }
    }

    private static int ReadInt(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToInt32(bytes, offset); offset += 4; return value;
    }

    private static string Describe(MetadataReader reader, TypeNames names, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return names.FromHandle(method.GetDeclaringType()) + "::" + reader.GetString(method.Name);
            case HandleKind.FieldDefinition:
                var field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                return names.FromHandle(field.GetDeclaringType()) + "::" + reader.GetString(field.Name);
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                return names.FromHandle(member.Parent) + "::" + reader.GetString(member.Name);
            case HandleKind.MethodSpecification:
                var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return Describe(reader, names, spec.Method) + "<...>";
            case HandleKind.TypeDefinition:
            case HandleKind.TypeReference:
            case HandleKind.TypeSpecification:
                return names.FromHandle(handle);
            default:
                return $"token 0x{MetadataTokens.GetToken(handle):x8}";
        }
    }
}
