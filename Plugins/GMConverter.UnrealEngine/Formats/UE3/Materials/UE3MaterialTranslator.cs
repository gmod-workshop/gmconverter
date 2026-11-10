using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.SDK.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// Translates an unlit UE3 particle material's expression graph into an SDK <see cref="MaterialGraph"/>,
/// node for node, with the instance's parameters and static switches applied and material functions
/// expanded inline. Particle colour and dynamic parameters become the particle's own values, and depth
/// fades are taken as having nothing behind the particle. Graphs using a node this doesn't translate
/// yield null with the reason, and callers keep the baked texture.
/// </summary>
internal sealed class UE3MaterialTranslator
{
    private const int _maximumDepth = 256;
    private static readonly string[] _inputMasks = ["MaskR", "MaskG", "MaskB", "MaskA"];
    private static readonly string[] _componentMasks = ["R", "G", "B", "A"];

    private readonly UE3GameContent _content;
    private readonly UE3MaterialParameters _parameters;
    private readonly Func<UE3ObjectReference, int> _textureIndex;
    private readonly List<MaterialGraphNode> _nodes = [];
    private readonly Dictionary<(int Scope, string Package, int Node, int Output), int> _compiled = [];
    private readonly HashSet<(int Scope, string Package, int Node)> _compiling = [];
    private int _scopes;

    private UE3MaterialTranslator(UE3GameContent content, UE3MaterialParameters parameters, Func<UE3ObjectReference, int> textureIndex)
    {
        _content = content;
        _parameters = parameters;
        _textureIndex = textureIndex;
    }

    /// <summary>The material's emissive and opacity as a graph whose textures are numbered by <paramref name="textureIndex"/>; null with the reason when it can't be translated.</summary>
    public static (IReadOnlyList<MaterialGraphNode> Nodes, int? Emissive, int? Opacity)? Translate(
        UE3GameContent content, UE3ObjectReference material, Func<UE3ObjectReference, int> textureIndex, out string? failure)
    {
        var parameters = UE3MaterialParameters.Collect(content, material);
        if (parameters.BaseMaterial is not { } baseMaterial)
        {
            failure = "it has no base material";
            return null;
        }

        var package = baseMaterial.Package;
        var properties = package.OpenObject(baseMaterial.Export).Properties;
        if (properties.GetName("LightingModel") is { } lighting and not "MLM_Unlit")
        {
            failure = $"it is lit ({lighting})";
            return null;
        }

        var translator = new UE3MaterialTranslator(content, parameters, textureIndex);
        var scope = new Scope(translator._scopes++, package, null, null);
        try
        {
            var blend = properties.GetName("BlendMode");
            var emissive = translator.Output(scope, properties, "EmissiveColor") is { } color ? translator.Fit(color, 3) : (int?)null;
            int? opacity = blend switch
            {
                "BLEND_Additive" or "BLEND_Translucent" => translator.Output(scope, properties, "Opacity") is { } alpha ? translator.Fit(alpha, 1) : null,
                "BLEND_Masked" => translator.Output(scope, properties, "OpacityMask") is { } mask
                    ? translator.Emit(new MaterialGraphNode(MaterialGraphOp.Select,
                        [translator.Fit(mask, 1), translator.Constant(properties.GetFloat("OpacityMaskClipValue") ?? 1f / 3f), translator.Constant(1f), translator.Constant(0f)]))
                    : null,
                _ => null,
            };
            failure = null;
            return (translator._nodes, emissive, opacity);
        }
        catch (UnsupportedNodeException ex)
        {
            failure = ex.Message;
            return null;
        }
        catch (GMConverterException ex)
        {
            failure = ex.Message;
            return null;
        }
    }

    // Where expressions resolve: a material, or a material function called from another scope with its inputs.
    private sealed record Scope(int Id, UE3Package Package, Scope? Caller, IReadOnlyDictionary<Guid, UE3Properties>? Inputs);

    private sealed class UnsupportedNodeException(string message) : Exception(message);

    private int? Output(Scope scope, UE3Properties material, string name) =>
        material.Find(name) is { Type: "StructProperty" } input ? Input(scope, scope.Package.ReadStruct(input), 0) : null;

    // An ExpressionInput: the node it reads (and which output), with its channel mask applied.
    private int? Input(Scope scope, UE3Properties input, int depth)
    {
        var expression = input.GetObject("Expression");
        if (expression == 0)
        {
            return null;
        }

        var node = Node(scope, expression, Math.Max(0, input.GetInt("OutputIndex") ?? 0), depth + 1);
        if (input.GetInt("Mask") is not 1)
        {
            return node;
        }

        var components = string.Concat(_inputMasks.Select((mask, index) => input.GetInt(mask) is 1 ? "xyzw"[index].ToString() : string.Empty));
        return components.Length == 0 ? node : Swizzle(node, components);
    }

    private int Node(Scope scope, int index, int output, int depth)
    {
        var key = (scope.Id, scope.Package.FilePath, index, output);
        if (_compiled.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (depth > _maximumDepth || !_compiling.Add((scope.Id, scope.Package.FilePath, index)) || scope.Package.GetExport(index) is not { } export)
        {
            throw new UnsupportedNodeException("its graph loops or references a missing node");
        }

        try
        {
            var className = scope.Package.GetClassName(export).Replace("MaterialExpression", string.Empty, StringComparison.Ordinal);
            var result = Compile(scope, className, scope.Package.OpenObject(export).Properties, output, depth);
            _compiled[key] = result;
            return result;
        }
        finally
        {
            _compiling.Remove((scope.Id, scope.Package.FilePath, index));
        }
    }

    private int Compile(Scope scope, string className, UE3Properties node, int output, int depth)
    {
        int? Optional(string name) => node.Find(name) is { Type: "StructProperty" } input ? Input(scope, scope.Package.ReadStruct(input), depth) : null;
        int Required(string name) => Optional(name) ?? throw new UnsupportedNodeException($"its {className} node has no {name} input");
        float Float(string name, float fallback) => node.GetFloat(name) ?? fallback;
        int Binary(MaterialGraphOp op) => Emit(new MaterialGraphNode(op, [Required("A"), Required("B")]));
        int Unary(MaterialGraphOp op) => Emit(new MaterialGraphNode(op, [Required("Input")]));

        switch (className)
        {
            case "Constant":
                return Constant(Float("R", 0f));
            case "Constant2Vector":
                return Emit(MaterialGraphNode.Constant(new Vector4(Float("R", 0f), Float("G", 0f), 0f, 0f), 2));
            case "Constant3Vector":
                return Emit(MaterialGraphNode.Constant(new Vector4(Float("R", 0f), Float("G", 0f), Float("B", 0f), 0f), 3));
            case "Constant4Vector":
                return Emit(MaterialGraphNode.Constant(new Vector4(Float("R", 0f), Float("G", 0f), Float("B", 0f), Float("A", 0f)), 4));
            case "ScalarParameter":
                var scalarName = node.GetName("ParameterName") ?? string.Empty;
                return Constant(_parameters.Scalars.TryGetValue(scalarName, out var scalar) ? scalar : Float("DefaultValue", 0f));
            case "VectorParameter":
                var vectorName = node.GetName("ParameterName") ?? string.Empty;
                return Emit(MaterialGraphNode.Constant(_parameters.Vectors.TryGetValue(vectorName, out var vector) ? vector : ReadColor(scope, node, "DefaultValue"), 4));
            case "StaticSwitchParameter":
                return Required(StaticSwitch(node) ? "A" : "B");
            case "StaticSwitch":
                return Required(StaticBool(scope, node) ? "A" : "B");
            case "TextureCoordinate":
                if ((node.GetInt("CoordinateIndex") ?? 0) != 0)
                {
                    throw new UnsupportedNodeException("it samples a second texture coordinate set");
                }

                var tiling = new Vector2(Float("UTiling", 1f), Float("VTiling", 1f));
                var uv = Emit(new MaterialGraphNode(MaterialGraphOp.TexCoord, []));
                return tiling == Vector2.One ? uv : Emit(new MaterialGraphNode(MaterialGraphOp.Multiply, [uv, Emit(MaterialGraphNode.Constant(new Vector4(tiling, 0f, 0f), 2))]));
            case "Time":
                return Emit(new MaterialGraphNode(MaterialGraphOp.Time, []));
            case "Panner":
                // CrossFire HD's UE3 slides without wrapping: clamped textures rely on sliding off their ends.
                var pannerTime = Optional("Time") is { } t ? Fit(t, 1) : Emit(new MaterialGraphNode(MaterialGraphOp.Time, []));
                var speed = Emit(MaterialGraphNode.Constant(new Vector4(Float("SpeedX", 0f), Float("SpeedY", 0f), 0f, 0f), 2));
                return Emit(new MaterialGraphNode(MaterialGraphOp.Add, [Coordinate(Optional("Coordinate")), Emit(new MaterialGraphNode(MaterialGraphOp.Multiply, [pannerTime, speed]))]));
            case "Rotator":
                return Rotate(Coordinate(Optional("Coordinate")), Optional("Time") is { } rotatorTime ? Fit(rotatorTime, 1) : Emit(new MaterialGraphNode(MaterialGraphOp.Time, [])),
                    Float("Speed", 0.25f), new Vector2(Float("CenterX", 0.5f), Float("CenterY", 0.5f)));
            case "TextureSample":
            case "TextureSampleParameter2D":
                return Emit(new MaterialGraphNode(MaterialGraphOp.TextureSample, [Coordinate(Optional("Coordinates"))]) { Texture = Texture(scope, node) });
            case "Add":
                return Binary(MaterialGraphOp.Add);
            case "Subtract":
                return Binary(MaterialGraphOp.Subtract);
            case "Multiply":
                return Binary(MaterialGraphOp.Multiply);
            case "Divide":
                return Binary(MaterialGraphOp.Divide);
            case "Power":
                return Emit(new MaterialGraphNode(MaterialGraphOp.Power, [Required("Base"), Required("Exponent")]));
            case "DotProduct":
                return Binary(MaterialGraphOp.Dot);
            case "AppendVector":
                return Binary(MaterialGraphOp.Append);
            case "OneMinus":
                return Unary(MaterialGraphOp.OneMinus);
            case "Floor":
                return Unary(MaterialGraphOp.Floor);
            case "Ceil":
                return Unary(MaterialGraphOp.Ceil);
            case "Frac":
                return Unary(MaterialGraphOp.Frac);
            case "Abs":
                return Unary(MaterialGraphOp.Abs);
            case "SquareRoot":
                return Unary(MaterialGraphOp.Sqrt);
            case "Sine":
            case "Cosine":
                var period = Float("Period", 1f);
                var angle = period > 0f ? Emit(new MaterialGraphNode(MaterialGraphOp.Multiply, [Required("Input"), Constant(2f * MathF.PI / period)])) : Required("Input");
                return Emit(new MaterialGraphNode(className == "Sine" ? MaterialGraphOp.Sine : MaterialGraphOp.Cosine, [angle]));
            case "ConstantClamp":
                return Emit(new MaterialGraphNode(MaterialGraphOp.Clamp, [Required("Input"), Constant(Float("Min", 0f)), Constant(Float("Max", 1f))]));
            case "Clamp":
                return Emit(new MaterialGraphNode(MaterialGraphOp.Clamp, [Required("Input"), Optional("Min") ?? Constant(0f), Optional("Max") ?? Constant(1f)]));
            case "ConstantBiasScale":
                return Emit(new MaterialGraphNode(MaterialGraphOp.Multiply, [Emit(new MaterialGraphNode(MaterialGraphOp.Add, [Required("Input"), Constant(Float("Bias", 1f))])), Constant(Float("Scale", 0.5f))]));
            case "LinearInterpolate":
                return Emit(new MaterialGraphNode(MaterialGraphOp.Lerp, [Optional("A") ?? Constant(0f), Optional("B") ?? Constant(1f), Required("Alpha")]));
            case "ComponentMask":
                var components = string.Concat(_componentMasks.Select((name, index) => node.GetBool(name) == true ? "xyzw"[index].ToString() : string.Empty));
                return components.Length == 0 ? throw new UnsupportedNodeException("a component mask selects nothing") : Swizzle(Required("Input"), components);
            case "Desaturation":
                // UE3: grey = dot(rgb, luminance factors); lerp to it by Percent, or grey alone without one.
                var colour = Fit(Required("Input"), 3);
                var factors = node.Find("LuminanceFactors") is { Size: 16 } ? ReadColor(scope, node, "LuminanceFactors") : new Vector4(0.3f, 0.59f, 0.11f, 0f);
                var grey = Emit(new MaterialGraphNode(MaterialGraphOp.Dot, [colour, Emit(MaterialGraphNode.Constant(factors, 3))]));
                return Optional("Percent") is { } percent ? Emit(new MaterialGraphNode(MaterialGraphOp.Lerp, [colour, grey, Fit(percent, 1)])) : grey;
            case "Normalize":
                var vectorInput = Required("VectorInput");
                var length = Emit(new MaterialGraphNode(MaterialGraphOp.Sqrt, [Emit(new MaterialGraphNode(MaterialGraphOp.Dot, [vectorInput, vectorInput]))]));
                return Emit(new MaterialGraphNode(MaterialGraphOp.Divide, [vectorInput, Emit(new MaterialGraphNode(MaterialGraphOp.Max, [length, Constant(1e-6f)]))]));
            case "If":
                // A > B ? AGreaterThanB : (A < B ? ALessThanB : AEqualsB, or AGreaterThanB without one).
                var a = Fit(Required("A"), 1);
                var b = Fit(Required("B"), 1);
                var greater = Required("AGreaterThanB");
                var notGreater = Emit(new MaterialGraphNode(MaterialGraphOp.Select, [b, a, Required("ALessThanB"), Optional("AEqualsB") ?? greater]));
                return Emit(new MaterialGraphNode(MaterialGraphOp.Select, [a, b, greater, notGreater]));
            case "SphereMask":
                return SphereMask(Required("A"), Required("B"), Optional("Radius") is { } radius ? Fit(radius, 1) : Constant(Float("AttenuationRadius", 256f)),
                    Optional("Hardness") is { } hardness ? Fit(hardness, 1) : Constant(Float("HardnessPercent", 100f)));
            case "VertexColor":
            case "MeshEmitterVertexColor":
                return Emit(new MaterialGraphNode(MaterialGraphOp.ParticleColor, []));
            case "DynamicParameter":
            case "MeshEmitterDynamicParameter":
                return Emit(new MaterialGraphNode(MaterialGraphOp.ParticleParameters, []));
            case "DepthBiasedAlpha":
                // Fades where the particle meets what's behind it; with nothing close behind, the alpha as given.
                return Required("Alpha");
            case "SceneDepth":
                // Nothing close behind the particle: the scene is far.
                return Constant(1e6f);
            case "PixelDepth":
                return Constant(0f);
            case "MaterialFunctionCall":
                return FunctionCall(scope, node, output, depth);
            case "FunctionInput":
                return FunctionInput(scope, node, depth);
            case "FunctionOutput":
                return Required("A");
            default:
                throw new UnsupportedNodeException($"it uses a {className} node");
        }
    }

    // A material function's output, with the function's inputs bound to the call's.
    private int FunctionCall(Scope scope, UE3Properties call, int output, int depth)
    {
        if (_content.Resolve(scope.Package, call.GetObject("MaterialFunction")) is not { } function)
        {
            throw new UnsupportedNodeException("it calls a material function that can't be found");
        }

        var version = scope.Package.Summary.FileVersion;
        Dictionary<Guid, UE3Properties> inputs = [];
        foreach (var input in call.GetStructArray("FunctionInputs", version))
        {
            if (ReadGuid(scope.Package, input, "ExpressionInputId") is { } id && input.Find("Input") is { Type: "StructProperty" } expressionInput)
            {
                inputs[id] = scope.Package.ReadStruct(expressionInput);
            }
        }

        var outputs = call.GetStructArray("FunctionOutputs", version);
        if (output >= outputs.Count || ReadGuid(scope.Package, outputs[output], "ExpressionOutputId") is not { } outputId)
        {
            throw new UnsupportedNodeException($"it reads output {output} of a material function that has {outputs.Count}");
        }

        var package = function.Package;
        var functionScope = new Scope(_scopes++, package, scope, inputs);
        foreach (var index in package.OpenObject(function.Export).Properties.GetObjectArray("FunctionExpressions"))
        {
            if (package.GetExport(index) is { } export && package.GetClassName(export) == "MaterialExpressionFunctionOutput" &&
                ReadGuid(package, package.OpenObject(export).Properties, "Id") == outputId)
            {
                return Node(functionScope, index, 0, depth);
            }
        }

        throw new UnsupportedNodeException($"material function {function.Export.ObjectName} has no output matching the call");
    }

    // A function's input: what the call connected, evaluated where the call is, else its preview.
    private int FunctionInput(Scope scope, UE3Properties node, int depth)
    {
        if (scope.Caller is { } caller && ReadGuid(scope.Package, node, "Id") is { } id &&
            scope.Inputs?.TryGetValue(id, out var connected) == true && Input(caller, connected, depth) is { } value)
        {
            return value;
        }

        if (node.Find("Preview") is { Type: "StructProperty" } preview && Input(scope, scope.Package.ReadStruct(preview), depth) is { } previewValue)
        {
            return previewValue;
        }

        var constant = ReadColor(scope, node, "PreviewValue");
        return node.GetName("InputType") switch
        {
            "FunctionInput_Vector2" => Emit(MaterialGraphNode.Constant(constant, 2)),
            "FunctionInput_Vector3" => Emit(MaterialGraphNode.Constant(constant, 3)),
            "FunctionInput_Vector4" => Emit(MaterialGraphNode.Constant(constant, 4)),
            "FunctionInput_Scalar" or null => Constant(constant.X),
            var type => throw new UnsupportedNodeException($"a material function takes a {type} input"),
        };
    }

    private bool StaticSwitch(UE3Properties node)
    {
        var name = node.GetName("ParameterName") ?? string.Empty;
        return _parameters.StaticSwitches.TryGetValue(name, out var value) ? value : node.GetBool("DefaultValue") ?? false;
    }

    // A StaticSwitch's Value: a static bool parameter, else the node's own default.
    private bool StaticBool(Scope scope, UE3Properties node)
    {
        if (node.Find("Value") is { Type: "StructProperty" } value &&
            scope.Package.GetExport(scope.Package.ReadStruct(value).GetObject("Expression")) is { } source &&
            scope.Package.GetClassName(source) == "MaterialExpressionStaticBoolParameter")
        {
            return StaticSwitch(scope.Package.OpenObject(source).Properties);
        }

        return node.GetBool("DefaultValue") ?? false;
    }

    private int Texture(Scope scope, UE3Properties node)
    {
        var parameterName = node.GetName("ParameterName");
        UE3ObjectReference? reference = parameterName is not null && _parameters.Textures.TryGetValue(parameterName, out var overridden)
            ? overridden
            : _content.Resolve(scope.Package, node.GetObject("Texture")) is { } found ? new UE3ObjectReference(found.Package, found.Export) : null;
        if (reference is not { } texture || !texture.ClassName.Equals("Texture2D", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedNodeException("it samples a texture that isn't a 2D texture or can't be found");
        }

        return _textureIndex(texture);
    }

    private int Coordinate(int? coordinate) => coordinate is { } value ? Fit(value, 2) : Emit(new MaterialGraphNode(MaterialGraphOp.TexCoord, []));

    // UE3's Rotator: the coordinate turned about the centre by time * speed radians.
    private int Rotate(int coordinate, int time, float speed, Vector2 center)
    {
        var angle = Emit(new MaterialGraphNode(MaterialGraphOp.Multiply, [time, Constant(speed)]));
        var cos = Emit(new MaterialGraphNode(MaterialGraphOp.Cosine, [angle]));
        var sin = Emit(new MaterialGraphNode(MaterialGraphOp.Sine, [angle]));
        var origin = Emit(MaterialGraphNode.Constant(new Vector4(center, 0f, 0f), 2));
        var local = Emit(new MaterialGraphNode(MaterialGraphOp.Subtract, [coordinate, origin]));
        var negSin = Emit(new MaterialGraphNode(MaterialGraphOp.Multiply, [sin, Constant(-1f)]));
        var rowX = Emit(new MaterialGraphNode(MaterialGraphOp.Append, [cos, negSin]));
        var rowY = Emit(new MaterialGraphNode(MaterialGraphOp.Append, [sin, cos]));
        var rotated = Emit(new MaterialGraphNode(MaterialGraphOp.Append,
            [Emit(new MaterialGraphNode(MaterialGraphOp.Dot, [rowX, local])), Emit(new MaterialGraphNode(MaterialGraphOp.Dot, [rowY, local]))]));
        return Emit(new MaterialGraphNode(MaterialGraphOp.Add, [rotated, origin]));
    }

    // UE3's SphereMask: 1 at the centre falling to 0 at the radius, sharpened by hardness (percent).
    private int SphereMask(int a, int b, int radius, int hardness)
    {
        var offset = Emit(new MaterialGraphNode(MaterialGraphOp.Subtract, [a, b]));
        var distance = Emit(new MaterialGraphNode(MaterialGraphOp.Sqrt, [Emit(new MaterialGraphNode(MaterialGraphOp.Dot, [offset, offset]))]));
        var normalized = Emit(new MaterialGraphNode(MaterialGraphOp.Divide, [distance, Emit(new MaterialGraphNode(MaterialGraphOp.Max, [radius, Constant(1e-5f)]))]));
        var softness = Emit(new MaterialGraphNode(MaterialGraphOp.Clamp,
            [Emit(new MaterialGraphNode(MaterialGraphOp.OneMinus, [Emit(new MaterialGraphNode(MaterialGraphOp.Multiply, [hardness, Constant(0.01f)]))])), Constant(1e-5f), Constant(1f)]));
        var mask = Emit(new MaterialGraphNode(MaterialGraphOp.Divide, [Emit(new MaterialGraphNode(MaterialGraphOp.OneMinus, [normalized])), softness]));
        return Emit(new MaterialGraphNode(MaterialGraphOp.Clamp, [mask, Constant(0f), Constant(1f)]));
    }

    // Matches UE3's casts: a scalar spreads across components, extra components drop, missing ones are 0.
    private int Fit(int node, int size)
    {
        var have = Size(node);
        if (have == size)
        {
            return node;
        }

        if (have == 1)
        {
            return Swizzle(node, new string('x', size));
        }

        if (have > size)
        {
            return Swizzle(node, "xyzw"[..size]);
        }

        var padding = Emit(MaterialGraphNode.Constant(Vector4.Zero, size - have));
        return Emit(new MaterialGraphNode(MaterialGraphOp.Append, [node, padding]));
    }

    private int Swizzle(int node, string components)
    {
        // A one-component value has only x: repeat it for each requested component.
        var size = Size(node);
        var valid = string.Concat(components.Select(component => "xyzw".IndexOf(component, StringComparison.Ordinal) < size ? component : 'x'));
        return Emit(new MaterialGraphNode(MaterialGraphOp.Swizzle, [node]) { Components = valid });
    }

    private int Size(int node) => new MaterialGraph(_nodes, [], null, null).SizeOf(node);

    private int Constant(float value) => Emit(MaterialGraphNode.Constant(value));

    private int Emit(MaterialGraphNode node)
    {
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    private static Guid? ReadGuid(UE3Package package, UE3Properties properties, string name) =>
        properties.Find(name) is { Size: 16 } property ? new Guid(package.GetBytes(property.ValueOffset, 16).Span) : null;

    private static Vector4 ReadColor(Scope scope, UE3Properties node, string name)
    {
        if (node.Find(name) is not { Size: 16 } property)
        {
            return Vector4.Zero;
        }

        var archive = new UE3Archive(scope.Package.GetBytes(property.ValueOffset, 16).ToArray(), scope.Package.Names);
        return new Vector4(archive.ReadSingle(), archive.ReadSingle(), archive.ReadSingle(), archive.ReadSingle());
    }
}
