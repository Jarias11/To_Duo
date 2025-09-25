using System.IO;
using System.Text.Json;

public static class CategoryDataService {

	private const string DefaultFile = "categories.json";

	private static string GetPath(string userId) {
		var baseDir = PathEx.GetAppDataDir("TaskMate");
		var fileName = string.IsNullOrWhiteSpace(userId)
			? DefaultFile                       // "categories.json"
			: $"categories.{userId}.json";
		return PathEx.CombineSafe(baseDir, fileName);
	}

	public static List<string> Load(string userId) {
		var path = GetPath(userId);
		if(!File.Exists(path)) return new List<string>();

		var json = File.ReadAllText(path);
		return JsonSerializer.Deserialize<List<string>>(json) ?? new();
	}

	public static void Save(string userId, List<string> cats) {
		var path = GetPath(userId);
		var json = JsonSerializer.Serialize(cats, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(path, json);
	}
}
