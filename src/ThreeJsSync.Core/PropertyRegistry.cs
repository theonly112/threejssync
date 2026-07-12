using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ThreeJsSync.Core
{
    public sealed class PropertyCodec
    {
        public Func<JToken, bool> Validate { get; set; }
        public Func<SyncedObjectState, JToken> Read { get; set; }
        public Action<SyncedObjectState, JToken> Apply { get; set; }
    }

    public sealed class PropertyRegistry
    {
        private static readonly Regex ColorPattern = new Regex("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
        private readonly Dictionary<string, PropertyCodec> _codecs = new Dictionary<string, PropertyCodec>(StringComparer.Ordinal);

        public PropertyRegistry()
        {
            Register("position", VectorCodec(
                s => new JObject { ["x"] = s.PositionX, ["y"] = s.PositionY, ["z"] = s.PositionZ },
                (s, v) => { s.PositionX = D(v, "x"); s.PositionY = D(v, "y"); s.PositionZ = D(v, "z"); }));
            Register("quaternion", new PropertyCodec
            {
                Validate = IsQuaternion,
                Read = s => new JObject { ["x"] = s.QuaternionX, ["y"] = s.QuaternionY, ["z"] = s.QuaternionZ, ["w"] = s.QuaternionW },
                Apply = (s, v) => { s.QuaternionX = D(v, "x"); s.QuaternionY = D(v, "y"); s.QuaternionZ = D(v, "z"); s.QuaternionW = D(v, "w"); }
            });
            Register("scale", VectorCodec(
                s => new JObject { ["x"] = s.ScaleX, ["y"] = s.ScaleY, ["z"] = s.ScaleZ },
                (s, v) => { s.ScaleX = D(v, "x"); s.ScaleY = D(v, "y"); s.ScaleZ = D(v, "z"); },
                v => IsVector(v) && Math.Abs(D(v, "x")) > 1e-9 && Math.Abs(D(v, "y")) > 1e-9 && Math.Abs(D(v, "z")) > 1e-9));
            Register("visible", ScalarCodec(JTokenType.Boolean, s => new JValue(s.Visible), (s, v) => s.Visible = v.Value<bool>()));
            Register("name", new PropertyCodec
            {
                Validate = v => v?.Type == JTokenType.String && v.Value<string>().Length <= 256,
                Read = s => new JValue(s.Name), Apply = (s, v) => s.Name = v.Value<string>()
            });
            Register("material.color", new PropertyCodec
            {
                Validate = v => v?.Type == JTokenType.String && ColorPattern.IsMatch(v.Value<string>()),
                Read = s => new JValue(s.MaterialColor), Apply = (s, v) => s.MaterialColor = v.Value<string>()
            });
            Register("material.opacity", new PropertyCodec
            {
                Validate = v => IsFiniteNumber(v) && v.Value<double>() >= 0 && v.Value<double>() <= 1,
                Read = s => new JValue(s.MaterialOpacity), Apply = (s, v) => s.MaterialOpacity = v.Value<double>()
            });
        }

        public IEnumerable<string> FixedKeys => _codecs.Keys;

        public void Register(string key, PropertyCodec codec)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A property key is required.", nameof(key));
            if (codec?.Validate == null || codec.Read == null || codec.Apply == null) throw new ArgumentException("A complete codec is required.", nameof(codec));
            _codecs[key] = codec;
        }

        public bool TryGet(string key, out PropertyCodec codec)
        {
            if (_codecs.TryGetValue(key, out codec)) return true;
            if (!key.StartsWith("metadata.", StringComparison.Ordinal) || key.Length <= 9 || key.Length > 137) return false;
            var metadataKey = key.Substring(9);
            codec = new PropertyCodec
            {
                Validate = IsMetadata,
                Read = s => s.Metadata.TryGetValue(metadataKey, out var value) ? JToken.FromObject(value) : JValue.CreateNull(),
                Apply = (s, v) => s.SetMetadata(metadataKey, v.Type == JTokenType.Null ? null : v.ToObject<object>())
            };
            return true;
        }

        public string KeyForStateProperty(string propertyName)
        {
            if (propertyName.StartsWith("Position", StringComparison.Ordinal)) return "position";
            if (propertyName.StartsWith("Quaternion", StringComparison.Ordinal)) return "quaternion";
            if (propertyName.StartsWith("Scale", StringComparison.Ordinal)) return "scale";
            if (propertyName == "Visible") return "visible";
            if (propertyName == "Name") return "name";
            if (propertyName == "MaterialColor") return "material.color";
            if (propertyName == "MaterialOpacity") return "material.opacity";
            if (propertyName.StartsWith("Metadata.", StringComparison.Ordinal)) return "metadata." + propertyName.Substring(9);
            return null;
        }

        private static PropertyCodec VectorCodec(Func<SyncedObjectState, JToken> read, Action<SyncedObjectState, JToken> apply, Func<JToken, bool> validate = null) =>
            new PropertyCodec { Validate = validate ?? IsVector, Read = read, Apply = apply };

        private static PropertyCodec ScalarCodec(JTokenType type, Func<SyncedObjectState, JToken> read, Action<SyncedObjectState, JToken> apply) =>
            new PropertyCodec { Validate = v => v?.Type == type, Read = read, Apply = apply };

        private static bool IsVector(JToken value) => IsFiniteNumber(value?["x"]) && IsFiniteNumber(value?["y"]) && IsFiniteNumber(value?["z"]);

        private static bool IsQuaternion(JToken value)
        {
            if (!IsVector(value) || !IsFiniteNumber(value?["w"])) return false;
            var lengthSquared = Math.Pow(D(value, "x"), 2) + Math.Pow(D(value, "y"), 2) + Math.Pow(D(value, "z"), 2) + Math.Pow(D(value, "w"), 2);
            return lengthSquared > 1e-12 && Math.Abs(lengthSquared - 1) < 0.02;
        }

        private static bool IsFiniteNumber(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Float && value.Type != JTokenType.Integer)) return false;
            var number = value.Value<double>();
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static bool IsMetadata(JToken value)
        {
            if (value == null) return false;
            if (value.Type != JTokenType.Null && value.Type != JTokenType.String && value.Type != JTokenType.Boolean && value.Type != JTokenType.Integer && value.Type != JTokenType.Float) return false;
            return value.ToString(Newtonsoft.Json.Formatting.None).Length <= 4096;
        }

        private static double D(JToken value, string field) => value[field].Value<double>();
    }
}
