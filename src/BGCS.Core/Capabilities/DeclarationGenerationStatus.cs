namespace BGCS.Core.Capabilities;

/// <summary>Describes the actual source-generation result of one requested declaration.</summary>
public enum DeclarationGenerationStatus
{
    /// <summary>Native bridge source is generated without claiming a managed binding or consumer compilation.</summary>
    GeneratedNativeBridge,
    /// <summary>A correct raw declaration is planned without a friendly projection.</summary>
    GeneratedRaw,
    /// <summary>A friendly projection is planned together with its required raw boundary.</summary>
    GeneratedFriendly,
    /// <summary>An explicit selection rule excludes this declaration.</summary>
    Excluded,
    /// <summary>The selected mechanisms cannot safely generate this requested capability.</summary>
    Unsupported
}
