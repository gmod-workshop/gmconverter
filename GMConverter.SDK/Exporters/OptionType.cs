using System.Diagnostics.CodeAnalysis;

namespace GMConverter.SDK.Exporters;

/// <summary>
/// Logical type of an exporter option, used by the host to render an appropriate control in the
/// UI and bind an appropriate CLI argument shape. Plugin authors typically discover these via
/// IntelliSense, so the names are chosen for instinctive matching against CLR primitive type
/// names (the same convention <see cref="TypeCode"/> uses) — CA1720 is suppressed for this reason.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Plugin authors instinctively type OptionType.String / Int / Bool / Float; matches the convention of System.TypeCode.")]
public enum OptionType
{
    /// <summary>Free-form text.</summary>
    String,

    /// <summary>Filesystem path; UI renders a path picker.</summary>
    Path,

    /// <summary>Boolean toggle.</summary>
    Bool,

    /// <summary>Whole number.</summary>
    Int,

    /// <summary>Floating-point number.</summary>
    Float,

    /// <summary>
    /// One of a fixed set of string values. The <see cref="OptionDescriptor.Choices"/> property
    /// must be populated when this type is used.
    /// </summary>
    Enum,
}
