using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarMecaSim.Shared;

namespace CarMecaSim.Mp
{
    /// <summary>Reads the mp_config.json the launcher writes. System.Text.Json is in-box on net6.</summary>
    internal static class ConfigIo
    {
        private static readonly JsonSerializerOptions Opts = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static MpConfig Read(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MpConfig>(json, Opts) ?? new MpConfig();
        }
    }
}
