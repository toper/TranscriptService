using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace TranscriptService.API.Extensions
{
    public static class JsonExtensions
    {
        public static string ToJson(this object o)
        {
            if (o == null)
            {
                return string.Empty;
            }

            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.LatinExtendedA)
            };

            return JsonSerializer.Serialize(o, options);
        }

        public static T ToObject<T>(this string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(json);
        }
    }
}
