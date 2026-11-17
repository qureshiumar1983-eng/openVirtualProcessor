using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Newtonsoft.Json
{
    public enum Formatting { None = 0, Indented = 1 }
    public enum NullValueHandling { Include = 0, Ignore = 1 }
    public enum DefaultValueHandling { Include = 0, Ignore = 1, Populate = 2, IgnoreAndPopulate = 3 }
    public enum MissingMemberHandling { Ignore = 0, Error = 1 }
    public enum ReferenceLoopHandling { Error = 0, Ignore = 1, Serialize = 2 }

    public class JsonSerializerSettings
    {
        public NullValueHandling NullValueHandling { get; set; }
        public DefaultValueHandling DefaultValueHandling { get; set; }
        public MissingMemberHandling MissingMemberHandling { get; set; }
        public ReferenceLoopHandling ReferenceLoopHandling { get; set; }
        public Formatting Formatting { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class JsonPropertyAttribute : Attribute
    {
        public JsonPropertyAttribute() { }
        public JsonPropertyAttribute(string propertyName) { PropertyName = propertyName; }
        public string PropertyName { get; set; }
        public NullValueHandling NullValueHandling { get; set; }
        public bool Required { get; set; }
        public int Order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class JsonIgnoreAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class JsonObjectAttribute : Attribute
    {
        public JsonObjectAttribute() { }
        public JsonObjectAttribute(MemberSerialization ms) { }
    }

    public enum MemberSerialization { OptOut = 0, OptIn = 1, Fields = 2 }

    public class JsonException : Exception
    {
        public JsonException() { }
        public JsonException(string message) : base(message) { }
        public JsonException(string message, Exception inner) : base(message, inner) { }
    }

    public class JsonReaderException : JsonException
    {
        public JsonReaderException() { }
        public JsonReaderException(string message) : base(message) { }
        public JsonReaderException(string message, Exception inner) : base(message, inner) { }
        public int LineNumber { get; set; }
        public int LinePosition { get; set; }
        public string Path { get; set; }
    }

    public class JsonSerializationException : JsonException
    {
        public JsonSerializationException() { }
        public JsonSerializationException(string message) : base(message) { }
        public JsonSerializationException(string message, Exception inner) : base(message, inner) { }
    }

    public static class JsonConvert
    {
        public static string SerializeObject(object value) => SerializeObject(value, Formatting.None, null);
        public static string SerializeObject(object value, Formatting formatting) => SerializeObject(value, formatting, null);
        public static string SerializeObject(object value, JsonSerializerSettings settings) => SerializeObject(value, settings?.Formatting ?? Formatting.None, settings);
        public static string SerializeObject(object value, Formatting formatting, JsonSerializerSettings settings)
        {
            var node = Mapper.ToNode(value, settings ?? new JsonSerializerSettings(), 0);
            return node == null ? "null" : node.ToJsonString(new JsonSerializerOptions { WriteIndented = formatting == Formatting.Indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        public static T DeserializeObject<T>(string json) => (T)DeserializeObject(json, typeof(T));
        public static T DeserializeObject<T>(string json, JsonSerializerSettings settings) => (T)DeserializeObject(json, typeof(T));
        public static object DeserializeObject(string json) => Linq.JToken.Wrap(Mapper.Parse(json));
        public static object DeserializeObject(string json, Type type) => Mapper.FromNode(Mapper.Parse(json), type);
        public static void PopulateObject(string json, object target) => Mapper.Populate(Mapper.Parse(json) as JsonObject, target);

        public static string ToString(object value) => SerializeObject(value);
    }

    internal static class Mapper
    {
        public static JsonNode Parse(string json)
        {
            try { return JsonNode.Parse(json ?? "", documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (System.Text.Json.JsonException e) { throw new JsonReaderException(e.Message, e); }
        }

        private static string NameOf(MemberInfo m) => m.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? m.Name;

        private static IEnumerable<MemberInfo> Members(Type t) =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.CanRead).Cast<MemberInfo>()
             .Concat(t.GetFields(BindingFlags.Public | BindingFlags.Instance))
             .Concat(t.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance).Where(p => p.GetCustomAttribute<JsonPropertyAttribute>() != null))
             .Concat(t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Where(f => f.GetCustomAttribute<JsonPropertyAttribute>() != null))
             .Where(m => m.GetCustomAttribute<JsonIgnoreAttribute>() == null);

        private static object Get(MemberInfo m, object o) => m is PropertyInfo p ? p.GetValue(o) : ((FieldInfo)m).GetValue(o);
        private static Type TypeOf(MemberInfo m) => m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType;
        private static void Set(MemberInfo m, object o, object v)
        {
            if (m is PropertyInfo p) { if (p.CanWrite) p.SetValue(o, v); }
            else ((FieldInfo)m).SetValue(o, v);
        }

        public static JsonNode ToNode(object v, JsonSerializerSettings s, int depth)
        {
            if (depth > 64) return null;
            switch (v)
            {
                case null: return null;
                case Linq.JToken t: return t.Node?.DeepClone();
                case JsonNode n: return n.DeepClone();
                case string str: return JsonValue.Create(str);
                case bool b: return JsonValue.Create(b);
                case char c: return JsonValue.Create(c.ToString());
                case Enum e: return JsonValue.Create(Convert.ToInt64(e, CultureInfo.InvariantCulture));
                case DateTime dt: return JsonValue.Create(dt.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture));
                case Guid g: return JsonValue.Create(g.ToString());
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    return JsonValue.Create(Convert.ToDecimal(v, CultureInfo.InvariantCulture));
                case IDictionary dict:
                    var o = new JsonObject();
                    foreach (DictionaryEntry kv in dict)
                    {
                        var child = ToNode(kv.Value, s, depth + 1);
                        if (child == null && s.NullValueHandling == NullValueHandling.Ignore) continue;
                        o[Convert.ToString(kv.Key, CultureInfo.InvariantCulture)] = child;
                    }
                    return o;
                case IEnumerable list:
                    var a = new JsonArray();
                    foreach (var item in list) a.Add(ToNode(item, s, depth + 1));
                    return a;
            }
            var obj = new JsonObject();
            foreach (var m in Members(v.GetType()))
            {
                object value;
                try { value = Get(m, v); } catch { continue; }
                var ignoreNull = s.NullValueHandling == NullValueHandling.Ignore || m.GetCustomAttribute<JsonPropertyAttribute>()?.NullValueHandling == NullValueHandling.Ignore;
                if (value == null && ignoreNull) continue;
                obj[NameOf(m)] = ToNode(value, s, depth + 1);
            }
            return obj;
        }

        public static object FromNode(JsonNode n, Type t)
        {
            var under = Nullable.GetUnderlyingType(t);
            if (n == null) return t.IsValueType && under == null ? Activator.CreateInstance(t) : null;
            if (under != null) t = under;
            if (typeof(Linq.JToken).IsAssignableFrom(t) || t == typeof(object)) return Linq.JToken.Wrap(n.DeepClone());
            if (t == typeof(string)) return n is JsonValue ? Scalar(n)?.ToString() : n.ToJsonString();
            if (t.IsEnum)
            {
                var sv = Scalar(n);
                return sv is string es ? Enum.Parse(t, es, true) : Enum.ToObject(t, Convert.ToInt64(sv, CultureInfo.InvariantCulture));
            }
            if (t == typeof(bool)) { var b = Scalar(n); return b is bool bb ? bb : b is string bs ? bs.Equals("true", StringComparison.OrdinalIgnoreCase) : Convert.ToDecimal(b) != 0; }
            if (t.IsPrimitive || t == typeof(decimal))
            {
                var sv = Scalar(n);
                if (sv is string ns) sv = decimal.Parse(ns, CultureInfo.InvariantCulture);
                if (sv is bool sb) sv = sb ? 1 : 0;
                return Convert.ChangeType(sv, t, CultureInfo.InvariantCulture);
            }
            if (t == typeof(DateTime)) return DateTime.Parse(Scalar(n).ToString(), CultureInfo.InvariantCulture);
            if (t == typeof(Guid)) return Guid.Parse(Scalar(n).ToString());
            if (t.IsArray && n is JsonArray arr)
            {
                var et = t.GetElementType();
                var result = Array.CreateInstance(et, arr.Count);
                for (int i = 0; i < arr.Count; i++) result.SetValue(FromNode(arr[i], et), i);
                return result;
            }
            if (typeof(IDictionary).IsAssignableFrom(t) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
            {
                var dt = t.IsInterface ? typeof(Dictionary<,>).MakeGenericType(t.GetGenericArguments()) : t;
                var dict = (IDictionary)Activator.CreateInstance(dt);
                var args = dt.IsGenericType ? dt.GetGenericArguments() : new[] { typeof(string), typeof(object) };
                if (n is JsonObject jo)
                    foreach (var kv in jo) dict[Convert.ChangeType(kv.Key, args[0], CultureInfo.InvariantCulture)] = FromNode(kv.Value, args[1]);
                return dict;
            }
            if (typeof(IEnumerable).IsAssignableFrom(t) && n is JsonArray list)
            {
                var et = t.IsGenericType ? t.GetGenericArguments()[0] : typeof(object);
                var lt = t.IsInterface ? typeof(List<>).MakeGenericType(et) : t;
                var l = (IList)Activator.CreateInstance(lt);
                foreach (var item in list) l.Add(FromNode(item, et));
                return l;
            }
            var target = Activator.CreateInstance(t, nonPublic: true);
            Populate(n as JsonObject, target);
            return target;
        }

        public static void Populate(JsonObject o, object target)
        {
            if (o == null || target == null) return;
            var members = Members(target.GetType()).ToList();
            foreach (var kv in o)
            {
                var m = members.FirstOrDefault(x => NameOf(x) == kv.Key) ?? members.FirstOrDefault(x => NameOf(x).Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                if (m == null) continue;
                try { Set(m, target, FromNode(kv.Value, TypeOf(m))); } catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException) { }
            }
        }

        public static object Scalar(JsonNode n)
        {
            if (n is not JsonValue v) return n?.ToJsonString();
            var e = v.GetValue<JsonElement>();
            return e.ValueKind switch
            {
                JsonValueKind.String => e.GetString(),
                JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
    }
}

namespace Newtonsoft.Json.Linq
{
    public enum JTokenType { None = 0, Object = 1, Array = 2, Constructor = 3, Property = 4, Comment = 5, Integer = 6, Float = 7, String = 8, Boolean = 9, Null = 10, Undefined = 11, Date = 12, Raw = 13, Bytes = 14, Guid = 15, Uri = 16, TimeSpan = 17 }

    public abstract class JToken : IEnumerable<JToken>
    {
        internal JsonNode Node;
        protected JToken(JsonNode node) { Node = node; }

        internal static JToken Wrap(JsonNode n) => n switch
        {
            null => new JValue((object)null),
            JsonObject o => new JObject(o),
            JsonArray a => new JArray(a),
            _ => new JValue(n)
        };

        internal static JsonNode Unwrap(object value) => value switch
        {
            null => null,
            JToken t => t.Node?.Parent != null ? t.Node.DeepClone() : t.Node,
            _ => Mapper.ToNode(value, new JsonSerializerSettings(), 0)
        };

        public static JToken Parse(string json) => Wrap(Mapper.Parse(json));
        public static JToken FromObject(object o) => Wrap(Mapper.ToNode(o, new JsonSerializerSettings(), 0));

        public abstract JTokenType Type { get; }
        public virtual bool HasValues => false;
        public JToken Parent => Node?.Parent == null ? null : Wrap(Node.Parent);
        public string Path => Node?.GetPath() ?? "";

        public virtual JToken this[object key]
        {
            get
            {
                JsonNode child = key switch
                {
                    string s when Node is JsonObject o => o[s],
                    int i when Node is JsonArray a => i >= 0 && i < a.Count ? a[i] : null,
                    _ => null
                };
                return child == null && !(Node is JsonObject ob && key is string ks && ob.ContainsKey(ks)) ? null : Wrap(child);
            }
            set
            {
                if (key is string s && Node is JsonObject o) o[s] = Unwrap(value);
                else if (key is int i && Node is JsonArray a) a[i] = Unwrap(value);
            }
        }

        public T Value<T>(object key) { var t = this[key]; return t == null ? default : t.ToObject<T>(); }
        public T Value<T>() => ToObject<T>();
        public T ToObject<T>() => (T)Mapper.FromNode(Node, typeof(T));
        public object ToObject(Type t) => Mapper.FromNode(Node, t);
        public IEnumerable<JToken> Children() => this;
        public IEnumerable<T> Values<T>() => this.Select(t => t.ToObject<T>());

        public JToken SelectToken(string path)
        {
            JToken cur = this;
            foreach (var raw in (path ?? "").Replace("[", ".[").Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (cur == null) return null;
                cur = raw.StartsWith("[") ? cur[int.Parse(raw.Trim('[', ']'), CultureInfo.InvariantCulture)] : cur[raw.Trim('\'', '"')];
            }
            return cur;
        }

        public JToken DeepClone() => Wrap(Node?.DeepClone());
        public override string ToString() => ToString(Formatting.Indented);
        public string ToString(Formatting formatting) => Node == null ? "" : Node is JsonValue v && Mapper.Scalar(v) is string s ? s : Node.ToJsonString(new JsonSerializerOptions { WriteIndented = formatting == Formatting.Indented });

        public virtual IEnumerator<JToken> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private object S => Mapper.Scalar(Node);
        public static explicit operator string(JToken t) => t?.Node == null ? null : t.Node is JsonValue ? t.S?.ToString() : t.Node.ToJsonString();
        public static explicit operator bool(JToken t) => t.ToObject<bool>();
        public static explicit operator bool?(JToken t) => t?.Node == null ? null : t.ToObject<bool>();
        public static explicit operator int(JToken t) => t.ToObject<int>();
        public static explicit operator int?(JToken t) => t?.Node == null ? null : t.ToObject<int>();
        public static explicit operator uint(JToken t) => t.ToObject<uint>();
        public static explicit operator long(JToken t) => t.ToObject<long>();
        public static explicit operator ulong(JToken t) => t.ToObject<ulong>();
        public static explicit operator short(JToken t) => t.ToObject<short>();
        public static explicit operator ushort(JToken t) => t.ToObject<ushort>();
        public static explicit operator byte(JToken t) => t.ToObject<byte>();
        public static explicit operator float(JToken t) => t.ToObject<float>();
        public static explicit operator double(JToken t) => t.ToObject<double>();
        public static explicit operator double?(JToken t) => t?.Node == null ? null : t.ToObject<double>();
        public static explicit operator decimal(JToken t) => t.ToObject<decimal>();
        public static explicit operator DateTime(JToken t) => t.ToObject<DateTime>();

        public static implicit operator JToken(string v) => new JValue(v);
        public static implicit operator JToken(bool v) => new JValue(v);
        public static implicit operator JToken(int v) => new JValue(v);
        public static implicit operator JToken(long v) => new JValue(v);
        public static implicit operator JToken(double v) => new JValue(v);
        public static implicit operator JToken(float v) => new JValue(v);
        public static implicit operator JToken(ushort v) => new JValue(v);
    }

    public abstract class JContainer : JToken
    {
        protected JContainer(JsonNode node) : base(node) { }
        public abstract int Count { get; }
        public override bool HasValues => Count > 0;
        public JToken First => this.FirstOrDefault();
        public JToken Last => this.LastOrDefault();
    }

    public class JProperty : JToken
    {
        public JProperty(string name, object value) : base(Unwrap(value)) { Name = name; }
        public string Name { get; }
        public JToken Value => Wrap(Node);
        public override JTokenType Type => JTokenType.Property;
    }

    public class JObject : JContainer, IEnumerable<KeyValuePair<string, JToken>>
    {
        public JObject() : base(new JsonObject()) { }
        internal JObject(JsonObject o) : base(o) { }
        public JObject(params object[] content) : this() { foreach (var p in content.OfType<JProperty>()) Add(p.Name, p.Value); }
        private JsonObject O => (JsonObject)Node;

        public static new JObject Parse(string json) => Mapper.Parse(json) as JsonObject is { } o ? new JObject(o) : throw new JsonReaderException("JSON is not an object");
        public static new JObject FromObject(object o) => new JObject((JsonObject)Mapper.ToNode(o, new JsonSerializerSettings(), 0));

        public override JTokenType Type => JTokenType.Object;
        public override int Count => O.Count;
        public JToken this[string name] { get => base[name]; set => base[name] = value; }
        public bool ContainsKey(string name) => O.ContainsKey(name);
        public bool TryGetValue(string name, out JToken value) { value = O.ContainsKey(name) ? Wrap(O[name]) : null; return value != null; }
        public JToken GetValue(string name) => ContainsKey(name) ? Wrap(O[name]) : null;
        public JToken GetValue(string name, StringComparison c) { var k = O.Select(kv => kv.Key).FirstOrDefault(x => string.Equals(x, name, c)); return k == null ? null : Wrap(O[k]); }
        public void Add(string name, JToken value) => O.Add(name, Unwrap(value));
        public void Add(JProperty p) => Add(p.Name, p.Value);
        public bool Remove(string name) => O.Remove(name);
        public IEnumerable<JProperty> Properties() => O.Select(kv => new JProperty(kv.Key, Wrap(kv.Value)));
        public JProperty Property(string name) => O.ContainsKey(name) ? new JProperty(name, Wrap(O[name])) : null;
        public override IEnumerator<JToken> GetEnumerator() => Properties().Cast<JToken>().GetEnumerator();
        IEnumerator<KeyValuePair<string, JToken>> IEnumerable<KeyValuePair<string, JToken>>.GetEnumerator() =>
            O.Select(kv => new KeyValuePair<string, JToken>(kv.Key, Wrap(kv.Value))).GetEnumerator();
    }

    public class JArray : JContainer
    {
        public JArray() : base(new JsonArray()) { }
        internal JArray(JsonArray a) : base(a) { }
        public JArray(params object[] content) : this() { foreach (var c in content) Add(c is JToken t ? t : new JValue(c)); }
        private JsonArray A => (JsonArray)Node;
        public static new JArray Parse(string json) => Mapper.Parse(json) as JsonArray is { } a ? new JArray(a) : throw new JsonReaderException("JSON is not an array");
        public static new JArray FromObject(object o) => new JArray((JsonArray)Mapper.ToNode(o, new JsonSerializerSettings(), 0));
        public override JTokenType Type => JTokenType.Array;
        public override int Count => A.Count;
        public JToken this[int index] { get => base[index]; set => base[index] = value; }
        public void Add(JToken item) => A.Add(Unwrap(item));
        public void Add(object item) => A.Add(Unwrap(item));
        public void RemoveAt(int index) => A.RemoveAt(index);
        public void Clear() => A.Clear();
        public override IEnumerator<JToken> GetEnumerator() => A.Select(Wrap).ToList().GetEnumerator();
    }

    public class JValue : JToken
    {
        public JValue(object value) : base(value == null ? null : Mapper.ToNode(value, new JsonSerializerSettings(), 0)) { }
        internal JValue(JsonNode n) : base(n) { }
        public object Value => Mapper.Scalar(Node);
        public override JTokenType Type => Node == null ? JTokenType.Null : Value switch
        {
            string => JTokenType.String, bool => JTokenType.Boolean, long => JTokenType.Integer, decimal => JTokenType.Float, _ => JTokenType.Null
        };
    }
}
