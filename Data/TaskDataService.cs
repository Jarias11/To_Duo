using System.IO;
using System.Text.Json;
using TaskMate.Models;

namespace TaskMate.Data {
    public static class TaskDataService {
        private static readonly string FileName = "tasks.json";

        private static string GetPath() {
            var baseDir = PathEx.GetAppDataDir("TaskMate");
            return PathEx.CombineSafe(baseDir, FileName); // "tasks.json"

        }

        public static List<TaskItem> LoadTasks() {
            var path = GetPath();
            if(!File.Exists(path))
                return new List<TaskItem>();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<TaskItem>>(json) ?? new List<TaskItem>();
        }

        public static void SaveTasks(IEnumerable<TaskItem> tasks) {
            var path = GetPath();
            string json = JsonSerializer.Serialize(tasks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}