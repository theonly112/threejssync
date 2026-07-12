using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ThreeJsSync.Core
{
    public static class CanonicalStateComparer
    {
        public static bool AreEqual(JToken left, JToken right, double numericTolerance = 1e-9)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (IsNumber(left) && IsNumber(right)) return Math.Abs(left.Value<double>() - right.Value<double>()) <= numericTolerance;
            if (left is JObject leftObject && right is JObject rightObject)
            {
                var leftNames = leftObject.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                var rightNames = rightObject.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                return leftNames.SequenceEqual(rightNames) && leftNames.All(name => AreEqual(leftObject[name], rightObject[name], numericTolerance));
            }
            if (left is JArray leftArray && right is JArray rightArray)
                return leftArray.Count == rightArray.Count && leftArray.Zip(rightArray, (l, r) => AreEqual(l, r, numericTolerance)).All(equal => equal);
            return JToken.DeepEquals(left, right);
        }

        private static bool IsNumber(JToken token) => token.Type == JTokenType.Integer || token.Type == JTokenType.Float;
    }
}

