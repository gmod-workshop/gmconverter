using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Textures;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// Compiles a UE3 material's expression graph into per-texel evaluators, so its outputs can be
/// baked to textures with the instance's parameter values. Only view-independent nodes compile:
/// anything depending on the camera, time, lighting or material functions makes its output fail
/// to compile, and the caller falls back to the material's plain textures.
/// </summary>
internal sealed class UE3MaterialGraph
{
    private const int _maximumDepth = 128;
    private static readonly string[] _inputMasks = ["MaskR", "MaskG", "MaskB", "MaskA"];
    private static readonly string[] _componentMasks = ["R", "G", "B", "A"];

    private readonly UE3GameContent _content;
    private readonly UE3MaterialParameters _parameters;
    private readonly UE3Package _package;
    private readonly Dictionary<string, UE3MaterialTexture?> _textureCache;
    private readonly Dictionary<int, Compiled?> _nodes = [];
    private readonly HashSet<int> _compiling = [];
    private List<UE3MaterialTexture> _sampled = [];

    public UE3MaterialGraph(
        UE3GameContent content,
        UE3MaterialParameters parameters,
        UE3ObjectReference baseMaterial,
        Dictionary<string, UE3MaterialTexture?> textureCache)
    {
        _content = content;
        _parameters = parameters;
        _package = baseMaterial.Package;
        Material = baseMaterial.Package.OpenObject(baseMaterial.Export).Properties;
        _textureCache = textureCache;
    }

    public delegate UE3MaterialValue Evaluator(Vector2 uv);

    /// <summary>The material's own properties, such as its blend mode.</summary>
    public UE3Properties Material { get; }

    /// <summary>
    /// Compiles one of the material's inputs (such as <c>DiffuseColor</c> or <c>Roughness</c>), with the
    /// textures it samples; null when the input is unset or uses a node that can't be baked.
    /// </summary>
    public UE3MaterialOutput? CompileOutput(string inputName)
    {
        if (Material.Find(inputName) is not { Type: "StructProperty" } input)
        {
            return null;
        }

        // Nodes are compiled per output so each output knows every texture it samples.
        _sampled = [];
        _nodes.Clear();
        var compiled = CompileInput(_package.ReadStruct(input), 0);
        return compiled is null ? null : new UE3MaterialOutput(compiled.Evaluate, compiled.Varies, [.. _sampled.Distinct()]);
    }

    private Compiled? CompileInput(UE3Properties input, int depth)
    {
        var expression = input.GetObject("Expression");
        if (expression == 0)
        {
            return input.GetBool("UseConstant") == true && input.Find("Constant") is { } constant
                ? Constant(ReadConstant(constant))
                : null;
        }

        if (CompileNode(expression, depth + 1) is not { } node)
        {
            return null;
        }

        if (input.GetInt("Mask") is not 1)
        {
            return node;
        }

        var channels = _inputMasks
            .Select((name, index) => input.GetInt(name) is 1 ? index : -1)
            .Where(index => index >= 0)
            .ToArray();
        if (channels.Length == 0)
        {
            return node;
        }

        return Map(node, value =>
        {
            Span<float> components = [0, 0, 0, 0];
            for (var i = 0; i < channels.Length; i++)
            {
                components[i] = value[channels[i]];
            }

            return new UE3MaterialValue(new Vector4(components[0], components[1], components[2], components[3]), channels.Length);
        });
    }

    private UE3MaterialValue ReadConstant(UE3Property constant)
    {
        var archive = new UE3Archive(_package.GetBytes(constant.ValueOffset, constant.Size).ToArray(), _package.Names);
        return constant.Size switch
        {
            4 when constant.StructName is null => UE3MaterialValue.Scalar(archive.ReadSingle()),
            4 => new UE3MaterialValue(new Vector4(archive.ReadByte(), archive.ReadByte(), archive.ReadByte(), archive.ReadByte()) / 255f, 4),
            8 => new UE3MaterialValue(new Vector4(archive.ReadSingle(), archive.ReadSingle(), 0, 0), 2),
            12 => new UE3MaterialValue(new Vector4(archive.ReadSingle(), archive.ReadSingle(), archive.ReadSingle(), 0), 3),
            _ => UE3MaterialValue.Scalar(0),
        };
    }

    private Compiled? CompileNode(int index, int depth)
    {
        if (_nodes.TryGetValue(index, out var cached))
        {
            return cached;
        }

        if (depth > _maximumDepth || !_compiling.Add(index) || _package.GetExport(index) is not { } export)
        {
            return null;
        }

        Compiled? compiled;
        try
        {
            var properties = _package.OpenObject(export).Properties;
            compiled = CompileExpression(_package.GetClassName(export), properties, depth);
        }
        catch (GMConverterException)
        {
            compiled = null;
        }
        finally
        {
            _compiling.Remove(index);
        }

        // Fold anything that doesn't depend on the texel into a constant.
        if (compiled is { Varies: false })
        {
            compiled = Constant(compiled.Evaluate(Vector2.Zero));
        }

        _nodes[index] = compiled;
        return compiled;
    }

    private Compiled? CompileExpression(string className, UE3Properties node, int depth)
    {
        Compiled? Input(string name) => node.Find(name) is { Type: "StructProperty" } input ? CompileInput(_package.ReadStruct(input), depth) : null;
        float Float(string name, float fallback) => node.GetFloat(name) ?? fallback;

        switch (className.Replace("MaterialExpression", string.Empty, StringComparison.Ordinal))
        {
            case "Constant":
                return Constant(UE3MaterialValue.Scalar(Float("R", 0)));
            case "Constant2Vector":
                return Constant(new UE3MaterialValue(new Vector4(Float("R", 0), Float("G", 0), 0, 0), 2));
            case "Constant3Vector":
                return Constant(new UE3MaterialValue(new Vector4(Float("R", 0), Float("G", 0), Float("B", 0), 0), 3));
            case "Constant4Vector":
                return Constant(new UE3MaterialValue(new Vector4(Float("R", 0), Float("G", 0), Float("B", 0), Float("A", 0)), 4));
            case "ScalarParameter":
                var scalarName = node.GetName("ParameterName") ?? string.Empty;
                return Constant(UE3MaterialValue.Scalar(_parameters.Scalars.TryGetValue(scalarName, out var scalar) ? scalar : Float("DefaultValue", 0)));
            case "VectorParameter":
                var vectorName = node.GetName("ParameterName") ?? string.Empty;
                return Constant(new UE3MaterialValue(_parameters.Vectors.TryGetValue(vectorName, out var vector) ? vector : ReadColor(node, "DefaultValue"), 4));
            case "StaticSwitchParameter":
                var switchName = node.GetName("ParameterName") ?? string.Empty;
                var enabled = _parameters.StaticSwitches.TryGetValue(switchName, out var value) ? value : node.GetBool("DefaultValue") ?? false;
                return Input(enabled ? "A" : "B");
            case "TextureSample":
            case "TextureSampleParameter2D":
            case "TextureSampleParameterNormal":
            // A particle flipbook samples one frame of its texture; the whole sheet is baked and the
            // frames are picked when the effect is drawn.
            case "ParticleSubUV":
            case "TextureSampleParameterSubUV":
                return CompileTextureSample(node, className.EndsWith("Normal", StringComparison.Ordinal), Input("Coordinates"));
            case "TextureCoordinate":
                if ((node.GetInt("CoordinateIndex") ?? 0) != 0)
                {
                    return null;
                }

                var tiling = new Vector2(Float("UTiling", 1), Float("VTiling", 1));
                return new Compiled(uv => new UE3MaterialValue(new Vector4(uv * tiling, 0, 0), 2), true);
            case "BumpOffset":
            case "Panner":
            case "Rotator":
                // View-dependent parallax and animated offsets: the texel's own coordinate at rest.
                return Input("Coordinate") ?? new Compiled(uv => new UE3MaterialValue(new Vector4(uv, 0, 0), 2), true);
            case "Multiply":
                return Binary(Input("A"), Input("B"), (a, b) => a * b);
            case "Add":
                return Binary(Input("A"), Input("B"), (a, b) => a + b);
            case "Subtract":
                return Binary(Input("A"), Input("B"), (a, b) => a - b);
            case "Divide":
                return Binary(Input("A"), Input("B"), (a, b) => a / b);
            case "Power":
                return Binary(Input("Base"), Input("Exponent"), (a, b) => new Vector4(
                    MathF.Pow(MathF.Max(a.X, 0), b.X),
                    MathF.Pow(MathF.Max(a.Y, 0), b.Y),
                    MathF.Pow(MathF.Max(a.Z, 0), b.Z),
                    MathF.Pow(MathF.Max(a.W, 0), b.W)));
            case "OneMinus":
                return Unary(Input("Input"), a => Vector4.One - a);
            case "Abs":
                return Unary(Input("Input"), Vector4.Abs);
            case "SquareRoot":
                return Unary(Input("Input"), a => Vector4.SquareRoot(Vector4.Max(a, Vector4.Zero)));
            case "Floor":
                return Unary(Input("Input"), a => new Vector4(MathF.Floor(a.X), MathF.Floor(a.Y), MathF.Floor(a.Z), MathF.Floor(a.W)));
            case "Ceil":
                return Unary(Input("Input"), a => new Vector4(MathF.Ceiling(a.X), MathF.Ceiling(a.Y), MathF.Ceiling(a.Z), MathF.Ceiling(a.W)));
            case "Frac":
                return Unary(Input("Input"), a => a - new Vector4(MathF.Floor(a.X), MathF.Floor(a.Y), MathF.Floor(a.Z), MathF.Floor(a.W)));
            case "ConstantClamp":
                var minimum = new Vector4(Float("Min", 0));
                var maximum = new Vector4(Float("Max", 1));
                return Unary(Input("Input"), a => Vector4.Clamp(a, minimum, maximum));
            case "Clamp":
                return Ternary(Input("Input"), Input("Min"), Input("Max"), (a, low, high) => Vector4.Min(Vector4.Max(a, low), high));
            case "LinearInterpolate":
                // A constant 0 or 1 blend picks one side, so the other needn't be bakeable (switched-off effects).
                var alphaInput = Input("Alpha");
                if (alphaInput is { Varies: false } && alphaInput.Evaluate(Vector2.Zero).Expanded is var fixedAlpha &&
                    (fixedAlpha == Vector4.Zero || fixedAlpha == Vector4.One))
                {
                    return Input(fixedAlpha == Vector4.Zero ? "A" : "B");
                }

                return Ternary(Input("A"), Input("B"), alphaInput, (a, b, alpha) => a + (b - a) * alpha);
            case "AppendVector":
                return Append(Input("A"), Input("B"));
            case "ComponentMask":
                return CompileComponentMask(node, Input("Input"));
            case "DeriveNormalZ":
                return Map(Input("InXY"), value =>
                {
                    var xy = new Vector2(value[0], value[1]);
                    return new UE3MaterialValue(new Vector4(xy, MathF.Sqrt(MathF.Max(0, 1 - Vector2.Dot(xy, xy))), 0), 3);
                });
            case "Normalize":
                return Map(Input("VectorInput"), value =>
                {
                    var v = new Vector3(value[0], value[1], value[2]);
                    return new UE3MaterialValue(new Vector4(v.LengthSquared() > 0 ? Vector3.Normalize(v) : v, 0), 3);
                });
            case "DotProduct":
                return Binary(Input("A"), Input("B"), (a, b) => new Vector4(Vector3.Dot(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z))), scalarResult: true);
            case "Desaturation":
                var factors = node.Find("LuminanceFactors") is { Size: 16 } ? ReadColor(node, "LuminanceFactors") : new Vector4(0.3f, 0.59f, 0.11f, 0);
                var percent = Input("Percent") ?? Constant(UE3MaterialValue.Scalar(1));
                return Binary(Input("Input"), percent, (color, amount) =>
                {
                    var gray = new Vector4(color.X * factors.X + color.Y * factors.Y + color.Z * factors.Z);
                    return color + (gray - color) * amount;
                });
            case "If":
                return CompileIf(Input("A"), Input("B"), Input("AGreaterThanB"), Input("AEqualsB"), Input("ALessThanB"));
            // Particle and vertex colour are multiplied in when drawn, so the bake takes them as white;
            // a dynamic parameter takes its default.
            case "VertexColor":
                return Constant(new UE3MaterialValue(Vector4.One, 4));
            case "DynamicParameter":
                return Constant(new UE3MaterialValue(Vector4.One, 4));
            // Depth fades soften where a sprite meets geometry; the bake keeps the alpha they fade.
            case "DepthBiasedAlpha":
                return Input("Alpha");
            default:
                return null;
        }
    }

    private Compiled? CompileTextureSample(UE3Properties node, bool isNormal, Compiled? coordinates)
    {
        var parameterName = node.GetName("ParameterName");
        UE3ObjectReference? reference = parameterName is not null && _parameters.Textures.TryGetValue(parameterName, out var overridden)
            ? overridden
            : _content.Resolve(_package, node.GetObject("Texture")) is { } found ? new UE3ObjectReference(found.Package, found.Export) : null;
        if (reference is not { } textureReference || LoadTexture(textureReference) is not { } texture)
        {
            return null;
        }

        _sampled.Add(texture);
        var uvSource = coordinates ?? new Compiled(uv => new UE3MaterialValue(new Vector4(uv, 0, 0), 2), true);
        return new Compiled(uv =>
        {
            var coordinate = uvSource.Evaluate(uv);
            var sample = texture.Sample(new Vector2(coordinate[0], coordinate[1]));
            if (isNormal)
            {
                sample = new Vector4(sample.X * 2 - 1, sample.Y * 2 - 1, sample.Z * 2 - 1, sample.W);
            }

            return new UE3MaterialValue(sample, 4);
        }, true);
    }

    private UE3MaterialTexture? LoadTexture(UE3ObjectReference reference)
    {
        if (_textureCache.TryGetValue(reference.Key, out var cached))
        {
            return cached;
        }

        UE3MaterialTexture? texture = null;
        if (reference.ClassName.Equals("Texture2D", StringComparison.OrdinalIgnoreCase))
        {
            var source = UE3Texture2D.Read(reference.Package, reference.Export);
            var image = UE3TextureDecoder.Decode(reference.Package, source, _content.TextureCaches);
            texture = image.IsValid ? new UE3MaterialTexture(image, source) : null;
        }

        _textureCache[reference.Key] = texture;
        return texture;
    }

    private static Compiled? CompileComponentMask(UE3Properties node, Compiled? input)
    {
        var channels = _componentMasks
            .Select((name, index) => node.GetBool(name) == true ? index : -1)
            .Where(index => index >= 0)
            .ToArray();
        return channels.Length == 0 ? null : Map(input, value =>
        {
            Span<float> components = [0, 0, 0, 0];
            for (var i = 0; i < channels.Length; i++)
            {
                components[i] = value[channels[i]];
            }

            return new UE3MaterialValue(new Vector4(components[0], components[1], components[2], components[3]), channels.Length);
        });
    }

    private static Compiled? CompileIf(Compiled? a, Compiled? b, Compiled? greater, Compiled? equal, Compiled? less)
    {
        if (a is null || b is null || greater is null || less is null)
        {
            return null;
        }

        var equalOrGreater = equal ?? greater;
        return new Compiled(uv =>
        {
            var left = a.Evaluate(uv)[0];
            var right = b.Evaluate(uv)[0];
            return left > right ? greater.Evaluate(uv) : left < right ? less.Evaluate(uv) : equalOrGreater.Evaluate(uv);
        }, a.Varies || b.Varies || greater.Varies || equalOrGreater.Varies || less.Varies);
    }

    private static Compiled? Append(Compiled? a, Compiled? b)
    {
        if (a is null || b is null)
        {
            return null;
        }

        return new Compiled(uv =>
        {
            var left = a.Evaluate(uv);
            var right = b.Evaluate(uv);
            Span<float> components = [0, 0, 0, 0];
            var count = 0;
            for (var i = 0; i < left.Components && count < 4; i++)
            {
                components[count++] = left[i];
            }

            for (var i = 0; i < right.Components && count < 4; i++)
            {
                components[count++] = right[i];
            }

            return new UE3MaterialValue(new Vector4(components[0], components[1], components[2], components[3]), count);
        }, a.Varies || b.Varies);
    }

    private static Compiled Constant(UE3MaterialValue value) => new(_ => value, false);

    private static Compiled? Map(Compiled? input, Func<UE3MaterialValue, UE3MaterialValue> map)
    {
        return input is null ? null : new Compiled(uv => map(input.Evaluate(uv)), input.Varies);
    }

    private static Compiled? Unary(Compiled? input, Func<Vector4, Vector4> operation)
    {
        return Map(input, value => new UE3MaterialValue(operation(value.Expanded), value.Components));
    }

    private static Compiled? Binary(Compiled? a, Compiled? b, Func<Vector4, Vector4, Vector4> operation, bool scalarResult = false)
    {
        if (a is null || b is null)
        {
            return null;
        }

        return new Compiled(uv =>
        {
            var result = UE3MaterialValue.Combine(a.Evaluate(uv), b.Evaluate(uv), operation);
            return scalarResult ? new UE3MaterialValue(result.Value, 1) : result;
        }, a.Varies || b.Varies);
    }

    private static Compiled? Ternary(Compiled? a, Compiled? b, Compiled? c, Func<Vector4, Vector4, Vector4, Vector4> operation)
    {
        if (a is null || b is null || c is null)
        {
            return null;
        }

        return new Compiled(uv =>
        {
            var first = a.Evaluate(uv);
            var second = b.Evaluate(uv);
            var third = c.Evaluate(uv);
            var components = Math.Max(first.Components, Math.Max(second.Components, third.Components));
            return new UE3MaterialValue(operation(first.Expanded, second.Expanded, third.Expanded), components);
        }, a.Varies || b.Varies || c.Varies);
    }

    private Vector4 ReadColor(UE3Properties node, string name)
    {
        if (node.Find(name) is not { Size: 16 } property)
        {
            return Vector4.Zero;
        }

        var archive = new UE3Archive(_package.GetBytes(property.ValueOffset, 16).ToArray(), _package.Names);
        return new Vector4(archive.ReadSingle(), archive.ReadSingle(), archive.ReadSingle(), archive.ReadSingle());
    }

    private sealed record Compiled(Evaluator Evaluate, bool Varies);
}
