using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.SDK.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// Translates a UE3 material's expression graph into an SDK <see cref="MaterialGraph"/>, node for node,
/// with the instance's parameters and static switches applied and material functions expanded inline:
/// unlit (particle) materials as their emissive and opacity, and materials lit by CrossFire HD's GGX
/// model as a <see cref="MaterialGraphSurface"/>. Particle colour and dynamic parameters become the
/// particle's own values, and depth fades are taken as having nothing behind the particle. Parameters
/// are the instance's fixed values, so a branch they zero out (a product with 0, a lerp all the way to
/// one end) is left out, as the game's shader compiler would fold it. Graphs that still need a node
/// this doesn't translate yield null with the reason, and callers keep the baked textures.
/// </summary>
internal sealed class UE3MaterialTranslator
{
    private const int _maximumDepth = 256;
    private static readonly string[] _inputMasks = ["MaskR", "MaskG", "MaskB", "MaskA"];
    private static readonly string[] _componentMasks = ["R", "G", "B", "A"];

    // UE3 lit models whose compiled lighting is known: CrossFire HD's GGX (see MaterialShading.UnrealGgx).
    private static readonly Dictionary<string, MaterialShading> _shadings = new(StringComparer.Ordinal)
    {
        ["MLM_GGXSmithSchlickBurley"] = MaterialShading.UnrealGgx,
    };

    // Nodes a target can't evaluate but whose size is known; they translate as a placeholder so a
    // branch that only feeds them into a product with zero still folds away.
    private static readonly Dictionary<string, int> _untranslatedSizes = new(StringComparer.Ordinal)
    {
        ["WorldPosition"] = 3,
        ["ObjectWorldPosition"] = 3,
        ["ActorWorldPosition"] = 3,
        ["CameraWorldPosition"] = 3,
        ["ObjectRadius"] = 1,
        ["PixelNormalWS"] = 3,
        ["VertexNormalWS"] = 3,
        ["LightVector"] = 3,
    };

    private readonly UE3GameContent _content;
    private readonly UE3MaterialParameters _parameters;
    private readonly List<UE3ObjectReference> _textures = [];
    private readonly Dictionary<string, int> _textureIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _untranslated = [];
    private readonly List<MaterialGraphNode> _nodes = [];
    private readonly Dictionary<(int Scope, string Package, int Node, int Output), int> _compiled = [];
    private readonly HashSet<(int Scope, string Package, int Node)> _compiling = [];
    private int _scopes;

    private UE3MaterialTranslator(UE3GameContent content, UE3MaterialParameters parameters)
    {
        _content = content;
        _parameters = parameters;
    }

    /// <summary>The material as a graph, with the textures it samples; null with the reason when it can't be translated.</summary>
    public static UE3TranslatedMaterial? Translate(UE3GameContent content, UE3ObjectReference material, out string? failure)
    {
        var parameters = UE3MaterialParameters.Collect(content, material);
        if (parameters.BaseMaterial is not { } baseMaterial)
        {
            failure = "it has no base material";
            return null;
        }

        var package = baseMaterial.Package;
        var properties = package.OpenObject(baseMaterial.Export).Properties;
        MaterialShading? shading = null;
        if (properties.GetName("LightingModel") is { } lighting and not "MLM_Unlit")
        {
            if (!_shadings.TryGetValue(lighting, out var known))
            {
                failure = $"it is lit by {lighting}, whose shading isn't known";
                return null;
            }

            shading = known;
        }

        var translator = new UE3MaterialTranslator(content, parameters);
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
            var surface = shading is { } model ? translator.Surface(scope, properties, model) : null;
            var result = translator.Compact(emissive, opacity, surface);
            failure = null;
            return result;
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

    private MaterialGraphSurface Surface(Scope scope, UE3Properties material, MaterialShading shading)
    {
        int Scalar(string name) => Output(scope, material, name) is { } value ? Fit(value, 1) : throw new UnsupportedNodeException($"its {name} input is unset");
        var baseColor = Output(scope, material, "DiffuseColor") is { } diffuse ? Fit(diffuse, 3) : throw new UnsupportedNodeException("its DiffuseColor input is unset");
        var normal = Output(scope, material, "Normal") is { } n ? Fit(n, 3) : (int?)null;
        var reflection = Output(scope, material, "WorldReflection") is { } r ? Fit(r, 3) : (int?)null;
        return new MaterialGraphSurface(shading, baseColor, Scalar("Metallic"), Scalar("SpecularColor"), Scalar("Roughness"), normal, reflection);
    }

    // The nodes the outputs read, renumbered in order, with only the textures those nodes sample.
    private UE3TranslatedMaterial Compact(int? emissive, int? opacity, MaterialGraphSurface? surface)
    {
        int?[] outputs = surface is { } lit
            ? [emissive, opacity, lit.BaseColor, lit.Metallic, lit.Specular, lit.Roughness, lit.Normal, lit.Reflection]
            : [emissive, opacity];
        var live = new bool[_nodes.Count];
        foreach (var output in outputs)
        {
            if (output is not { } node)
            {
                continue;
            }

            if (_untranslated.TryGetValue(node, out var reason))
            {
                throw new UnsupportedNodeException(reason);
            }

            live[node] = true;
        }

        for (var index = _nodes.Count - 1; index >= 0; index--)
        {
            if (live[index])
            {
                foreach (var input in _nodes[index].Inputs)
                {
                    live[input] = true;
                }
            }
        }

        var renumbered = new int[_nodes.Count];
        List<MaterialGraphNode> nodes = [];
        List<UE3ObjectReference> textures = [];
        Dictionary<int, int> textureIndices = [];
        for (var index = 0; index < _nodes.Count; index++)
        {
            if (!live[index])
            {
                continue;
            }

            var node = _nodes[index] with { Inputs = [.. _nodes[index].Inputs.Select(input => renumbered[input])] };
            if (node.Op == MaterialGraphOp.TextureSample)
            {
                if (!textureIndices.TryGetValue(node.Texture, out var texture))
                {
                    texture = textures.Count;
                    textures.Add(_textures[node.Texture]);
                    textureIndices[node.Texture] = texture;
                }

                node = node with { Texture = texture };
            }

            renumbered[index] = nodes.Count;
            nodes.Add(node);
        }

        int? Map(int? node) => node is { } value ? renumbered[value] : null;
        var mapped = surface is { } s
            ? s with
            {
                BaseColor = renumbered[s.BaseColor],
                Metallic = renumbered[s.Metallic],
                Specular = renumbered[s.Specular],
                Roughness = renumbered[s.Roughness],
                Normal = Map(s.Normal),
                Reflection = Map(s.Reflection),
            }
            : null;
        return new UE3TranslatedMaterial(nodes, textures, Map(emissive), Map(opacity), mapped);
    }

    // Where expressions resolve: a material, or a material function called from another scope with its inputs.
    private sealed record Scope(int Id, UE3Package Package, Scope? Caller, IReadOnlyDictionary<Guid, UE3Properties>? Inputs)
    {
        /// <summary>The material function this scope expands, for messages; null in the material itself.</summary>
        public string? Function { get; init; }

        public string Where => Function is null ? string.Empty : $" (in material function {Function})";
    }

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
        int Binary(MaterialGraphOp op) => Fold(new MaterialGraphNode(op, [Required("A"), Required("B")]));
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
                    // Targets have one coordinate set; a branch that folds away may still read another.
                    var otherSet = Emit(MaterialGraphNode.Constant(Vector4.Zero, 2));
                    _untranslated[otherSet] = $"it samples a second texture coordinate set{scope.Where}";
                    return otherSet;
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
            case "TextureSampleParameterNormal":
                return Emit(new MaterialGraphNode(MaterialGraphOp.TextureSample, [Coordinate(Optional("Coordinates"))]) { Texture = SampledTexture(scope, node, depth) });
            case "CameraVector":
                return Emit(new MaterialGraphNode(MaterialGraphOp.CameraVector, []));
            case "ReflectionVector":
                return Emit(new MaterialGraphNode(MaterialGraphOp.ReflectionVector, []));
            case "Transform":
                // UE3's defaults (unsaved) are tangent space to world space; the compiled shaders agree.
                if (node.GetName("TransformSourceType") is { } from and not "TRANSFORMSOURCE_Tangent" || node.GetName("TransformType") is { } to and not "TRANSFORM_World")
                {
                    throw new UnsupportedNodeException("it transforms a vector other than from tangent to world space");
                }

                return Emit(new MaterialGraphNode(MaterialGraphOp.TangentToWorld, [Fit(Required("Input"), 3)]));
            case "TextureSampleCubeLOD" when Optional("Coordinates") is null && Optional("LOD") is null:
                // CrossFire HD compiles this into a sample of the scene's reflection probe along the
                // specular direction, at a mip for the surface's roughness; its own texture is unused.
                return Emit(new MaterialGraphNode(MaterialGraphOp.SceneReflection, []));
            case "DeriveNormalZ":
                // z = sqrt(1 - x^2 - y^2), with nothing below zero.
                var xy = Fit(Required("InXY"), 2);
                var squared = Emit(new MaterialGraphNode(MaterialGraphOp.Dot, [xy, xy]));
                var z = Emit(new MaterialGraphNode(MaterialGraphOp.Sqrt, [Emit(new MaterialGraphNode(MaterialGraphOp.OneMinus, [squared]))]));
                return Emit(new MaterialGraphNode(MaterialGraphOp.Append, [xy, z]));
            case "Fresnel":
                // UE3: (1 - max(0, normal . camera))^exponent, with the normal (0, 0, 1) unless connected.
                var fresnelNormal = Optional("Normal") is { } connectedNormal ? Fit(connectedNormal, 3) : Emit(MaterialGraphNode.Constant(new Vector4(0f, 0f, 1f, 0f), 3));
                var facing = Emit(new MaterialGraphNode(MaterialGraphOp.Dot, [fresnelNormal, Emit(new MaterialGraphNode(MaterialGraphOp.CameraVector, []))]));
                var rim = Emit(new MaterialGraphNode(MaterialGraphOp.OneMinus, [Emit(new MaterialGraphNode(MaterialGraphOp.Max, [Constant(0f), facing]))]));
                return Emit(new MaterialGraphNode(MaterialGraphOp.Power, [rim, Constant(Float("Exponent", 3f))]));
            case "Add":
                return Binary(MaterialGraphOp.Add);
            case "Subtract":
                return Binary(MaterialGraphOp.Subtract);
            case "Multiply":
                return Product(() => Required("A"), () => Required("B"));
            case "Divide":
                return Binary(MaterialGraphOp.Divide);
            case "Power":
                return Fold(new MaterialGraphNode(MaterialGraphOp.Power, [Required("Base"), Required("Exponent")]));
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
                // The alpha first: a lerp all the way to one end never needs the other, except for the
                // result's size when the end it keeps is a scalar.
                var lerpAlpha = Required("Alpha");
                if (_nodes[lerpAlpha].Uniform is 0f or 1f)
                {
                    var (kept, other) = _nodes[lerpAlpha].Uniform == 0f ? ("A", "B") : ("B", "A");
                    var end = Optional(kept) ?? Constant(kept == "A" ? 0f : 1f);
                    if (Size(end) > 1)
                    {
                        return end;
                    }

                    return Fit(end, Size(Optional(other) ?? Constant(0f)));
                }

                return Fold(new MaterialGraphNode(MaterialGraphOp.Lerp, [Optional("A") ?? Constant(0f), Optional("B") ?? Constant(1f), lerpAlpha]));
            case "Distance":
                var difference = Emit(new MaterialGraphNode(MaterialGraphOp.Subtract, [Required("A"), Required("B")]));
                return Emit(new MaterialGraphNode(MaterialGraphOp.Sqrt, [Emit(new MaterialGraphNode(MaterialGraphOp.Dot, [difference, difference]))]));
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
                var notGreater = Fold(new MaterialGraphNode(MaterialGraphOp.Select, [b, a, Required("ALessThanB"), Optional("AEqualsB") ?? greater]));
                return Fold(new MaterialGraphNode(MaterialGraphOp.Select, [a, b, greater, notGreater]));
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
            case "Custom":
                // Its own HLSL, which isn't translated; its size is its declared output type (float3 unsaved).
                var custom = Emit(MaterialGraphNode.Constant(Vector4.Zero, node.GetName("OutputType") switch
                {
                    "CMOT_Float1" => 1,
                    "CMOT_Float2" => 2,
                    "CMOT_Float4" => 4,
                    _ => 3,
                }));
                _untranslated[custom] = $"it uses a Custom node{scope.Where}";
                return custom;
            default:
                if (_untranslatedSizes.TryGetValue(className, out var untranslatedSize))
                {
                    var placeholder = Emit(MaterialGraphNode.Constant(Vector4.Zero, untranslatedSize));
                    _untranslated[placeholder] = $"it uses a {className} node{scope.Where}";
                    return placeholder;
                }

                throw new UnsupportedNodeException($"it uses a {className} node{scope.Where}");
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
        var functionScope = new Scope(_scopes++, package, scope, inputs) { Function = function.Export.ObjectName };
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

    // The texture a sample reads: the texture object connected to it, else its own.
    private int SampledTexture(Scope scope, UE3Properties node, int depth)
    {
        if (node.Find("TextureObject") is { Type: "StructProperty" } input && TextureObject(scope, scope.Package.ReadStruct(input), depth) is { } connected)
        {
            return connected;
        }

        return Texture(scope, node);
    }

    // A texture object input: a TextureObject(Parameter) node, directly or through function inputs.
    private int? TextureObject(Scope scope, UE3Properties input, int depth)
    {
        var expression = input.GetObject("Expression");
        if (expression == 0)
        {
            return null;
        }

        if (depth > _maximumDepth || scope.Package.GetExport(expression) is not { } export)
        {
            throw new UnsupportedNodeException("a texture object input loops or references a missing node");
        }

        var node = scope.Package.OpenObject(export).Properties;
        switch (scope.Package.GetClassName(export).Replace("MaterialExpression", string.Empty, StringComparison.Ordinal))
        {
            case "TextureObject":
            case "TextureObjectParameter":
                return Texture(scope, node);
            case "FunctionInput":
                if (scope.Caller is { } caller && ReadGuid(scope.Package, node, "Id") is { } id && scope.Inputs?.TryGetValue(id, out var connected) == true &&
                    TextureObject(caller, connected, depth + 1) is { } bound)
                {
                    return bound;
                }

                return node.Find("Preview") is { Type: "StructProperty" } preview ? TextureObject(scope, scope.Package.ReadStruct(preview), depth + 1) : null;
            default:
                throw new UnsupportedNodeException("a texture object input reads something other than a texture");
        }
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

        if (!_textureIndices.TryGetValue(texture.Key, out var index))
        {
            index = _textures.Count;
            _textures.Add(texture);
            _textureIndices[texture.Key] = index;
        }

        return index;
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

        if (_nodes[node].Op == MaterialGraphOp.Constant && !_untranslated.ContainsKey(node))
        {
            // Padding a constant with zeros: the known components, then the zeros (Vector4's are 0).
            var value = _nodes[node].Value;
            float[] components = [value.X, value.Y, value.Z, value.W];
            for (var index = have; index < 4; index++)
            {
                components[index] = 0f;
            }

            return Emit(MaterialGraphNode.Constant(new Vector4(components[0], components[1], components[2], components[3]), size));
        }

        var padding = Emit(MaterialGraphNode.Constant(Vector4.Zero, size - have));
        return Emit(new MaterialGraphNode(MaterialGraphOp.Append, [node, padding]));
    }

    private int Swizzle(int node, string components)
    {
        // A one-component value has only x: repeat it for each requested component.
        var size = Size(node);
        var valid = string.Concat(components.Select(component => "xyzw".IndexOf(component, StringComparison.Ordinal) < size ? component : 'x'));
        if (_nodes[node].Op == MaterialGraphOp.Constant && !_untranslated.ContainsKey(node))
        {
            // A constant's components are known: keep the result a constant, so it can fold.
            var value = _nodes[node].Value;
            float[] source = [value.X, value.Y, value.Z, value.W];
            var picked = valid.Select(component => source["xyzw".IndexOf(component, StringComparison.Ordinal)]).Concat([0f, 0f, 0f]).ToArray();
            return Emit(MaterialGraphNode.Constant(new Vector4(picked[0], picked[1], picked[2], picked[3]), valid.Length));
        }

        return Emit(new MaterialGraphNode(MaterialGraphOp.Swizzle, [node]) { Components = valid });
    }

    private int Size(int node) => new MaterialGraph(_nodes, [], null, null).SizeOf(node);

    private int Constant(float value) => Emit(MaterialGraphNode.Constant(value));

    private int Emit(MaterialGraphNode node)
    {
        _nodes.Add(node);
        var index = _nodes.Count - 1;
        foreach (var input in node.Inputs)
        {
            if (_untranslated.TryGetValue(input, out var reason))
            {
                _untranslated[index] = reason;
                break;
            }
        }

        return index;
    }

    // A product, where a zero on one side means the other isn't needed (so a branch that can't be
    // translated but is multiplied by zero still folds); its size then comes from the zero, which
    // UE3 only allows to differ from the other side's when one of them is a scalar.
    private int Product(Func<int> left, Func<int> right)
    {
        UnsupportedNodeException? failure = null;
        int? Try(Func<int> operand)
        {
            try
            {
                return operand();
            }
            catch (UnsupportedNodeException ex)
            {
                failure ??= ex;
                return null;
            }
        }

        var a = Try(left);
        if (a is { } knownA && _nodes[knownA].Uniform == 0f && Size(knownA) > 1)
        {
            return knownA;
        }

        var b = Try(right);
        if (a is { } x && b is { } y)
        {
            return Fold(new MaterialGraphNode(MaterialGraphOp.Multiply, [x, y]));
        }

        if (a is { } zeroA && _nodes[zeroA].Uniform == 0f && Size(zeroA) > 1)
        {
            return zeroA;
        }

        if (b is { } zeroB && _nodes[zeroB].Uniform == 0f && Size(zeroB) > 1)
        {
            return zeroB;
        }

        throw failure!;
    }

    // Arithmetic with an operand whose value is already fixed: a product with zero is zero, adding or
    // subtracting zero leaves the other operand, a first power is its base (as the game's compiler
    // reduces it, sign included), a lerp all the way to one end is that end, and a lerp or choice
    // between two equal constants is that constant.
    private int Fold(MaterialGraphNode node)
    {
        float? Uniform(int position) => _nodes[node.Inputs[position]].Uniform;
        var size = new MaterialGraph([.. _nodes, node], [], null, null).SizeOf(_nodes.Count);
        int? Same(int first, int second) => Uniform(first) is { } value && Uniform(second) == value ? Emit(MaterialGraphNode.Constant(new Vector4(value), size)) : null;
        return node.Op switch
        {
            MaterialGraphOp.Multiply when Uniform(0) == 0f || Uniform(1) == 0f => Emit(MaterialGraphNode.Constant(Vector4.Zero, size)),
            MaterialGraphOp.Add when Uniform(0) == 0f => Fit(node.Inputs[1], size),
            MaterialGraphOp.Add or MaterialGraphOp.Subtract when Uniform(1) == 0f => Fit(node.Inputs[0], size),
            MaterialGraphOp.Power when Uniform(1) == 1f => Fit(node.Inputs[0], size),
            MaterialGraphOp.Lerp when Uniform(2) == 0f => Fit(node.Inputs[0], size),
            MaterialGraphOp.Lerp when Uniform(2) == 1f => Fit(node.Inputs[1], size),
            MaterialGraphOp.Lerp when Same(0, 1) is { } constant => constant,
            MaterialGraphOp.Select when Same(2, 3) is { } constant => constant,
            _ => Emit(node),
        };
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
