using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TaskMate.Models;

namespace TaskMate.Data {
    public static class TaskDataService {
        private static readonly string filePath = "tasks.json";

        public static List<TaskItem> LoadTasks() {
            if (!File.Exists(filePath))
                return new List<TaskItem>();

            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<TaskItem>>(json) ?? new List<TaskItem>();
        }

        public static void SaveTasks(IEnumerable<TaskItem> tasks) {
            string json = JsonSerializer.Serialize(tasks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
    }
}