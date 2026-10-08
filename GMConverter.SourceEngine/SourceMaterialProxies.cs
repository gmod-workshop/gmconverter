using System.Numerics;
using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine;

internal static class SourceMaterialProxies
{
    private const float _epsilon = 1e-6f;

    // Material transforms are in image space (V down the stored texture), which is also the space
    // Source applies texture transforms in, so rates, offsets and angles carry over unchanged.
    // A plain scroll keeps the long-standing TextureScroll proxy; anything else (static tiling,
    // rotation, oscillation) drives a TextureTransform from helper variables. The bump transform
    // follows the base so normal detail stays aligned with it, and a separately scrolling emissive
    // layer is the detail layer (see SourceMaterialEmission).
    public static void Write(StreamWriter writer, Material material)
    {
        var builder = new ProxyBuilder();
        var bumpVariable = material.NormalTexture is not null ? "$bumptransform" : null;

        if (BaseTransform(material) is { } baseTransform)
        {
            builder.AddTransform("$gmc_base", baseTransform, "$basetexturetransform", bumpVariable);
        }
        else if (material.UvScrollRate is { } scroll && scroll != Vector2.Zero)
        {
            builder.AddTextureScroll("$basetexturetransform", scroll);
            if (bumpVariable is not null)
            {
                builder.AddTextureScroll(bumpVariable, scroll);
            }
        }

        if (SourceMaterialEmission.UsesDetailLayer(material) && material.EmissiveUvScrollRate is { } detailScroll)
        {
            builder.AddTextureScroll("$detailtexturetransform", detailScroll);
        }
        else if (SourceMaterialDetail.LayerFor(material)?.UvTransform is { IsIdentity: false } detailTransform)
        {
            builder.AddTransform("$gmc_detail", detailTransform, "$detailtexturetransform", null);
        }

        if (SourceMaterialEmission.For(material) == SourceMaterialEmission.Mode.EmissiveBlend &&
            material.EmissiveLayer?.Pulse is { } pulse)
        {
            builder.AddPulse("$emissiveblendtint", pulse);
        }

        builder.WriteTo(writer);
    }

    // The base transform when it needs more than a TextureScroll, with UvScrollRate folded in.
    private static MaterialUvTransform? BaseTransform(Material material)
    {
        if (material.UvTransform is not { IsIdentity: false } transform)
        {
            return null;
        }

        return material.UvScrollRate is { } scroll && transform.ScrollRate == Vector2.Zero
            ? transform with { ScrollRate = scroll }
            : transform;
    }

    private sealed class ProxyBuilder
    {
        private readonly List<string> _variables = [];
        private readonly List<string> _proxies = [];

        public void AddTextureScroll(string transformVariable, Vector2 scroll)
        {
            var rate = scroll.Length();
            var angle = MathF.Atan2(scroll.Y, scroll.X) * (180f / MathF.PI);
            AddProxy("TextureScroll",
                ("texturescrollvar", transformVariable),
                ("texturescrollrate", Format(rate)),
                ("texturescrollangle", FormattableString.Invariant($"{angle:0.##}")));
        }

        // A static transform is written as the variable's value; an animated one becomes a
        // TextureTransform fed by center/scale/rotate/translate helpers that Sine, LinearRamp,
        // WrapMinMax, UniformNoise and Add proxies update each frame.
        public void AddTransform(string prefix, MaterialUvTransform transform, string resultVariable, string? alsoVariable)
        {
            if (!transform.IsAnimated)
            {
                var value = FormattableString.Invariant(
                    $"center {Format(transform.Center.X)} {Format(transform.Center.Y)} scale {Format(transform.Scale.X)} {Format(transform.Scale.Y)} rotate {Format(transform.RotationDegrees)} translate 0 0");
                _variables.Add(Line(resultVariable, value));
                if (alsoVariable is not null)
                {
                    _variables.Add(Line(alsoVariable, value));
                }

                return;
            }

            string center = $"{prefix}_center", scale = $"{prefix}_scale", rotate = $"{prefix}_rotate", translate = $"{prefix}_translate";
            _variables.Add(Line(center, Vector(transform.Center)));
            _variables.Add(Line(scale, Vector(transform.Scale)));
            _variables.Add(Line(rotate, Format(transform.RotationDegrees)));
            _variables.Add(Line(translate, "[0 0]"));

            if (MathF.Abs(transform.RotationRateDegrees) > _epsilon)
            {
                AddWrappedRamp($"{prefix}_rotate_ramp", transform.RotationRateDegrees, transform.RotationDegrees, 360f, rotate);
            }

            AddAxis(prefix, "u", 0, transform.ScrollRate.X, transform.Scale.X, transform.U, scale, translate);
            AddAxis(prefix, "v", 1, transform.ScrollRate.Y, transform.Scale.Y, transform.V, scale, translate);

            foreach (var target in alsoVariable is null ? [resultVariable] : new[] { resultVariable, alsoVariable })
            {
                AddProxy("TextureTransform",
                    ("centerVar", center),
                    ("scaleVar", scale),
                    ("rotateVar", rotate),
                    ("translateVar", translate),
                    ("resultVar", target));
            }
        }

        // Each colour channel follows its own Sine between the two linear colours.
        public void AddPulse(string tintVariable, MaterialColorPulse pulse)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                AddProxy("Sine",
                    ("sinemin", Format(SrgbToLinear(pulse.From[channel]))),
                    ("sinemax", Format(SrgbToLinear(pulse.To[channel]))),
                    ("sineperiod", Format(pulse.Period)),
                    ("timeoffset", Format(pulse.Phase)),
                    ("resultVar", $"{tintVariable}[{channel}]"));
            }
        }

        public void WriteTo(StreamWriter writer)
        {
            foreach (var variable in _variables)
            {
                writer.WriteLine(variable);
            }

            if (_proxies.Count == 0)
            {
                return;
            }

            writer.WriteLine("    \"Proxies\"");
            writer.WriteLine("    {");
            foreach (var proxy in _proxies)
            {
                writer.Write(proxy);
            }

            writer.WriteLine("    }");
        }

        // Scale oscillates in place (Sine between scale x (1 -/+ amplitude)); translation is the
        // sum of a wrapped scroll, a pan Sine and jitter noise, chained through Add proxies.
        private void AddAxis(string prefix, string axis, int component, float scrollRate, float baseScale, MaterialOscillation? oscillation, string scale, string translate)
        {
            List<string> terms = [];
            if (MathF.Abs(scrollRate) > _epsilon)
            {
                var ramp = $"{prefix}_{axis}_scroll";
                _variables.Add(Line(ramp, "0"));
                AddWrappedRamp($"{ramp}_ramp", scrollRate, 0f, 1f, ramp);
                terms.Add(ramp);
            }

            if (oscillation is { } osc && MathF.Abs(osc.Amplitude) > _epsilon)
            {
                var period = osc.Rate > 0f ? 1f / osc.Rate : 1f;
                var timeOffset = osc.Rate > 0f ? osc.Phase / (2f * MathF.PI * osc.Rate) : 0f;
                switch (osc.Kind)
                {
                    case MaterialOscillationKind.Stretch:
                        AddProxy("Sine",
                            ("sinemin", Format(baseScale * (1f - osc.Amplitude))),
                            ("sinemax", Format(baseScale * (1f + osc.Amplitude))),
                            ("sineperiod", Format(period)),
                            ("timeoffset", Format(timeOffset)),
                            ("resultVar", $"{scale}[{component}]"));
                        break;
                    case MaterialOscillationKind.Pan:
                        var pan = $"{prefix}_{axis}_pan";
                        _variables.Add(Line(pan, "0"));
                        AddProxy("Sine",
                            ("sinemin", Format(-osc.Amplitude)),
                            ("sinemax", Format(osc.Amplitude)),
                            ("sineperiod", Format(period)),
                            ("timeoffset", Format(timeOffset)),
                            ("resultVar", pan));
                        terms.Add(pan);
                        break;
                    case MaterialOscillationKind.Jitter:
                        // UniformNoise draws a new value every frame; at the source rates (15-120
                        // per second) that is close to the original flicker.
                        var jitter = $"{prefix}_{axis}_jitter";
                        _variables.Add(Line(jitter, "0"));
                        AddProxy("UniformNoise",
                            ("minVal", Format(-osc.Amplitude / 2f)),
                            ("maxVal", Format(osc.Amplitude / 2f)),
                            ("resultVar", jitter));
                        terms.Add(jitter);
                        break;
                }
            }

            var target = $"{translate}[{component}]";
            if (terms.Count == 1)
            {
                AddProxy("Equals", ("srcVar1", terms[0]), ("resultVar", target));
                return;
            }

            for (var i = 1; i < terms.Count; i++)
            {
                var sum = i == terms.Count - 1 ? target : $"{prefix}_{axis}_sum{i}";
                if (sum != target)
                {
                    _variables.Add(Line(sum, "0"));
                }

                AddProxy("Add", ("srcVar1", i == 1 ? terms[0] : $"{prefix}_{axis}_sum{i - 1}"), ("srcVar2", terms[i]), ("resultVar", sum));
            }
        }

        private void AddWrappedRamp(string ramp, float rate, float initialValue, float wrap, string result)
        {
            _variables.Add(Line(ramp, "0"));
            AddProxy("LinearRamp", ("rate", Format(rate)), ("initialValue", Format(initialValue)), ("resultVar", ramp));
            AddProxy("WrapMinMax", ("srcVar1", ramp), ("minVal", "0"), ("maxVal", Format(wrap)), ("resultVar", result));
        }

        private void AddProxy(string name, params (string Key, string Value)[] parameters)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(FormattableString.Invariant($"        \"{name}\""));
            text.AppendLine("        {");
            foreach (var (key, value) in parameters)
            {
                text.AppendLine(FormattableString.Invariant($"            \"{key}\" \"{value}\""));
            }

            text.AppendLine("        }");
            _proxies.Add(text.ToString());
        }

        private static string Line(string variable, string value) => FormattableString.Invariant($"    \"{variable}\" \"{value}\"");

        private static string Vector(Vector2 value) => $"[{Format(value.X)} {Format(value.Y)}]";

        private static string Format(float value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

        private static float SrgbToLinear(float value) =>
            value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }
}
