using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

namespace BGCS.Examples.CustomLowering;

/// <summary>Registers a complete C value conversion through neutral public extension contracts.</summary>
public sealed class MeasurePlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public PluginDescriptor descriptor => new("bgcs.examples.measure", "4.0.0", [new("example.measure", ExtensionPort.NativeTypeLowering, 100)]);
    /// <inheritdoc />
    public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration)
        => registry.RegisterFactory<INativeTypeLowering>("example.measure", _ => new MeasureLowering());
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
        => Identity(cancellationToken);
    internal static CacheFingerprintContribution Identity(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            typeof(MeasurePlugin).Assembly.ManifestModule.ModuleVersionId.ToString("D"))))), []);
    }
}

/// <summary>Converts the declared C Measure value to one fixed-width integer without changing its native declaration.</summary>
public sealed class MeasureLowering : INativeTypeLowering, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public bool CanLower(NativeTypeView type, LoweringContext context)
        => context.module.language == BGCS.Core.Inputs.SourceLanguage.C && type.type.category == NativeTypeCategory.Record
            && type.type.declaration is StableId declaration && context.module.GetDeclaration(declaration).qualifiedName == "Measure";
    /// <inheritdoc />
    public NativeTypeLoweringPlan Lower(NativeTypeView type, LoweringContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanLower(type, context)) throw new ArgumentException("This lowering owns only the declared C Measure value.", nameof(type));
        NativeDeclaration declaration = context.module.GetDeclaration(type.type.declaration!.Value);
        NativeDeclaration[] fields = declaration.children.Select(context.module.GetDeclaration).Where(item => item.kind == "Field").ToArray();
        if (fields.Length != 1 || fields[0].name != "raw" || fields[0].type is not StableId fieldType
            || context.module.GetType(fieldType) is not { category: NativeTypeCategory.Primitive, primitiveKind: "Int", size: 4, isSigned: true })
            throw new NotSupportedException("Measure must declare exactly one signed int32 raw field; the plugin does not infer a different value protocol.");
        BridgeType integer = new(new("example.measure.i32"), BridgeTypeKind.Scalar, "int32_t");
        return new(integer.id, [integer], BridgeConversionKind.SourceExpression, BridgeConversionKind.SourceExpression)
        {
            parameterExpression = Expression("({nativeType}){ {value} }", type.id),
            resultExpression = Expression("{value}.raw", type.id)
        };
    }
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
        => MeasurePlugin.Identity(cancellationToken);
    private static ControlledSourceFragment Expression(string text, StableId nativeType)
        => new("example.measure", SourceFragmentKind.Expression, text,
            new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))))
            { typeDependencies = [nativeType], evidence = new(EvidenceLevel.UserAsserted, "example.measure/conversion") };
}
