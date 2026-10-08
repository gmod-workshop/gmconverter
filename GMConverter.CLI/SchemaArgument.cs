using System.CommandLine;
using GMConverter.SDK.Options;

namespace GMConverter.CLI;

/// <summary>
/// A CLI flag generated from one importer or exporter option descriptor. <see cref="Negated"/>
/// marks a <c>no-</c> alias flag, which sets the opposite of the value it is given.
/// </summary>
internal sealed record SchemaArgument(string Format, OptionDescriptor Descriptor, Option<string> Argument, bool Negated = false);
