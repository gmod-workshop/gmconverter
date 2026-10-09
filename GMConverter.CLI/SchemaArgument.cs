using System.CommandLine;
using GMConverter.SDK.Options;

namespace GMConverter.CLI;

/// <summary>A CLI flag generated from one importer or exporter option descriptor.</summary>
internal sealed record SchemaArgument(string Format, OptionDescriptor Descriptor, Option<string> Argument);
