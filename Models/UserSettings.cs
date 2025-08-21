using System;
using System.IO;
using System.Text.Json;

namespace TaskMate.Models {
    public class UserSettings {
        public string UserId { get; set; }
        public string PartnerId { get; set; }

        private static readonly string FilePath = "user_settings.json";

        public static UserSettings Load() {
            if (!File.Exists(FilePath)) {
                var settings = new UserSettings { UserId = Guid.NewGuid().ToString(), PartnerId = "" };
                Save(settings);
                return settings;
            }

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<UserSettings>(json);
        }

        public static void Save(UserSettings settings) {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}