namespace AutoHotKeyTrigger
{
    using AutoHotKeyTrigger.ProfileManager.Component;
    using Newtonsoft.Json;

    internal static class AutoHotKeyTriggerJson
    {
        internal static readonly JsonSerializerSettings Settings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Populate,
            Converters = { new ComponentJsonConverter() },
        };
    }

    internal sealed class ComponentJsonConverter : JsonConverter<IComponent?>
    {
        public override IComponent? ReadJson(
            JsonReader reader,
            System.Type objectType,
            IComponent? existingValue,
            bool hasExisting,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
            if (obj.ContainsKey("duration"))
            {
                return obj.ToObject<Wait>(serializer);
            }

            return null;
        }

        public override void WriteJson(JsonWriter writer, IComponent? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            serializer.Serialize(writer, value);
        }
    }
}
