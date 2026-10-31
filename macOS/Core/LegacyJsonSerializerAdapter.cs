using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

// The shared TodoStore needs only this narrow API. Keep the adapter in the Mac
// assembly so Windows still uses its original .NET Framework implementation.
namespace System.Web.Script.Serialization
{
    internal sealed class JavaScriptSerializer
    {
        private int maximumLength = 2097152;
        private int recursionLimit = 100;

        public int MaxJsonLength
        {
            get { return maximumLength; }
            set { if (value < 1) throw new ArgumentOutOfRangeException("value"); maximumLength = value; }
        }

        public int RecursionLimit
        {
            get { return recursionLimit; }
            set { if (value < 1) throw new ArgumentOutOfRangeException("value"); recursionLimit = value; }
        }

        public string Serialize(object value)
        {
            try
            {
                // Files are local UTF-8 JSON, never HTML. Keep readable Unicode
                // like the existing Windows serializer, rather than expanding
                // every CJK character into six ASCII characters.
                string json = JsonSerializer.Serialize(value, new JsonSerializerOptions {
                    MaxDepth = RecursionLimit, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                CheckLength(json);
                return json;
            }
            catch (JsonException error) { throw new InvalidOperationException("Invalid JSON object graph.", error); }
        }

        public object DeserializeObject(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            CheckLength(json);
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json,
                    new JsonDocumentOptions { MaxDepth = RecursionLimit, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false }))
                {
                    return Read(document.RootElement);
                }
            }
            catch (JsonException error)
            {
                // TodoStore's existing corruption/recovery boundary catches this
                // legacy exception type; never leak JsonException around it.
                throw new ArgumentException("Invalid JSON data.", "json", error);
            }
        }

        private void CheckLength(string json)
        { if (json.Length > MaxJsonLength) throw new ArgumentException("JSON exceeds MaxJsonLength.", "json"); }

        private static object Read(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    var result = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (JsonProperty property in value.EnumerateObject()) result[property.Name] = Read(property.Value);
                    return result;
                case JsonValueKind.Array:
                    var elements = new List<object>();
                    foreach (JsonElement element in value.EnumerateArray()) elements.Add(Read(element));
                    return elements.ToArray();
                case JsonValueKind.String:
                    return ReadString(value.GetString(), value.GetRawText());
                case JsonValueKind.Number:
                    int integer; long wideInteger; decimal precise; double number;
                    string rawNumber = value.GetRawText();
                    // Framework JSON numbers with an exponent use Double, even
                    // when their numeric value happens to be an integer.
                    if (rawNumber.IndexOf('e') >= 0 || rawNumber.IndexOf('E') >= 0)
                    {
                        if (value.TryGetDouble(out number) && !Double.IsInfinity(number) && !Double.IsNaN(number)) return number;
                        throw new OverflowException("JSON exponent is outside the supported range.");
                    }
                    if (value.TryGetInt32(out integer)) return integer;
                    if (value.TryGetInt64(out wideInteger)) return wideInteger;
                    if (value.TryGetDecimal(out precise)) return precise;
                    if (value.TryGetDouble(out number) && !Double.IsInfinity(number) && !Double.IsNaN(number)) return number;
                    throw new OverflowException("JSON number is outside the supported range.");
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return null;
                default: throw new ArgumentException("Unsupported JSON value.");
            }
        }

        private static object ReadString(string value, string raw)
        {
            // JavaScriptSerializer accepted Microsoft's old /Date(milliseconds)/
            // representation as a UTC DateTime. ISO round-trip strings remain
            // strings so TodoStore keeps its existing exact date validation.
            if (value != null && raw.StartsWith("\"\\/Date(", StringComparison.Ordinal) && raw.EndsWith(")\\/\"", StringComparison.Ordinal)
                && value.StartsWith("/Date(", StringComparison.Ordinal) && value.EndsWith(")/", StringComparison.Ordinal))
            {
                string milliseconds = value.Substring(6, value.Length - 8);
                if (milliseconds.Length > 1 && ((milliseconds[milliseconds.Length - 1] >= 'a' && milliseconds[milliseconds.Length - 1] <= 'z')
                    || (milliseconds[milliseconds.Length - 1] >= 'A' && milliseconds[milliseconds.Length - 1] <= 'Z')))
                    milliseconds = milliseconds.Substring(0, milliseconds.Length - 1);
                int suffix = -1;
                for (int index = 1; index < milliseconds.Length; index++)
                    if (milliseconds[index] == '+' || milliseconds[index] == '-') { suffix = index; break; }
                if (suffix >= 0)
                {
                    string offset = milliseconds.Substring(suffix);
                    if (offset.Length != 5 || !Digits(offset, 1)) return value;
                    milliseconds = milliseconds.Substring(0, suffix);
                }
                long parsed;
                int firstDigit = milliseconds.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
                if (milliseconds.Length > firstDigit && Digits(milliseconds, firstDigit)
                    && Int64.TryParse(milliseconds, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed))
                {
                    try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(checked(parsed * TimeSpan.TicksPerMillisecond)); }
                    catch (ArgumentOutOfRangeException) { return value; }
                    catch (OverflowException) { return value; }
                }
            }
            return value;
        }

        private static bool Digits(string text, int start)
        {
            for (int index = start; index < text.Length; index++) if (text[index] < '0' || text[index] > '9') return false;
            return true;
        }
    }
}
