using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Animation;

namespace TaskMate.Models {
    public class UserSettings {
        public string UserId { get; set; } = "";
        public string PartnerId { get; set; } = "";
        public string GroupId { get; set; } = "";
        public string? DisplayName { get; set; } = "";
        public string Theme { get; set; } = "Light"; // "Light" or "Dark"
        public DateTime? PairedSinceUtc { get; set; }
        public bool? SoundsEnabled { get; set; } = true; // NEW: default to true
        public bool? AnimationsEnabled { get; set; } = true;
        public bool? NotificationsEnabled { get; set; } = false;

        private static readonly string FileName = "user_settings.json";
        private static string GetPath() {
            var dir = PathEx.GetAppDataDir("TaskMate");
            return PathEx.CombineSafe(dir, FileName); // "user_settings.json"
        }

        public static UserSettings Load() {
            var path = GetPath();
            if(!File.Exists(path)) {
                var settings = new UserSettings {
                    UserId = SnowflakeId.New(),   // <<< compact, ordered ID
                    PartnerId = "",
                    GroupId = ""
                };

                Save(settings);
                settings.DisplayName ??= string.Empty;
                return settings;
            }

            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();

            // Backfill for older files that may not have a UserId
            if(string.IsNullOrWhiteSpace(loaded.UserId)) {
                loaded.UserId = SnowflakeId.New();
                Save(loaded);
            }

            return loaded;
        }

        public static void Save(UserSettings settings) {
            var path = GetPath();
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
    }

    /// <summary>
    /// Simple Snowflake-style ID: time-ordered and compact.
    ///  - 41 bits: ms since a custom epoch (2024-01-01)
    ///  - 10 bits: node id (random per process)
    ///  - 12 bits: per-ms sequence
    /// Encoded as Base62 => ~11–12 chars, URL-safe.
    /// </summary>
    internal static class SnowflakeId {
        // Custom epoch (UTC): 2024-01-01
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Node id: pick a stable random at process start (0..1023)
        private static readonly int NodeId = RandomNumberGenerator.GetInt32(0, 1024);

        // Sequence per millisecond (0..4095)
        private static ushort _sequence = 0;
        private static long _lastMs = -1;
        private static readonly object _lock = new();

        public static string New() {
            long id = NextLong();
            return Base62Encode(id);
        }

        private static long NextLong() {
            lock(_lock) {
                long nowMs = (long)(DateTimeOffset.UtcNow - Epoch).TotalMilliseconds;
                if(nowMs == _lastMs) {
                    _sequence = (ushort)((_sequence + 1) & 0x0FFF); // 12 bits
                    if(_sequence == 0) {
                        // sequence wrapped within the same ms; spin to next ms
                        do { nowMs = (long)(DateTimeOffset.UtcNow - Epoch).TotalMilliseconds; }
                        while(nowMs == _lastMs);
                    }
                }
                else {
                    _sequence = 0;
                }

                _lastMs = nowMs;

                // layout: [41 bits time][10 bits node][12 bits seq] => 63 bits total
                long id = (nowMs & 0x1FFFFFFFFFFL) << 22;
                id |= ((long)NodeId & 0x3FFL) << 12;
                id |= _sequence & 0xFFFL;
                return id;
            }
        }

        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        private static string Base62Encode(long value) {
            if(value == 0) return "0";

            Span<char> buf = stackalloc char[12]; // 12 chars is plenty for a 63-bit number in base62
            int i = buf.Length;
            ulong v = (ulong)value;

            while(v > 0) {
                ulong rem = v % 62UL;     // remainder
                v /= 62UL;                // quotient
                buf[--i] = Alphabet[(int)rem];
            }

            return new string(buf[i..]);
        }
    }
}