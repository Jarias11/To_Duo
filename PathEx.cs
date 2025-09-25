using System;
using System.IO;
using System.Linq;

public static class PathEx {
	// Optional override root (per instance / per profile)
	private static string? _overrideRoot;

	/// <summary>
	/// Enable writing data next to the exe (or project bin) in a unique per-instance subfolder.
	/// Call this once on startup, e.g. PathEx.UseLocalData("A") and PathEx.UseLocalData("B").
	/// </summary>
	public static void UseLocalData(string profile = "default") {
		var safe = new string(profile.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray());
		if(string.IsNullOrWhiteSpace(safe)) safe = "default";
		_overrideRoot = CombineSafe(AppContext.BaseDirectory, $".tmdata-{safe}");
		EnsureDirectory(_overrideRoot);
	}

	/// <summary>
	/// Returns an app data directory (default: %APPDATA%\TaskMate), unless UseLocalData(...) was called.
	/// Ensures the directory exists before returning.
	/// </summary>
	public static string GetAppDataDir(string appFolderName) {
		// If a local override is active, use it.
		if(!string.IsNullOrWhiteSpace(_overrideRoot)) {
			var dir = CombineSafe(_overrideRoot, appFolderName);
			EnsureDirectory(dir);
			return dir;
		}

		// Normal roaming AppData path
		var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		if(string.IsNullOrWhiteSpace(root))
			root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if(string.IsNullOrWhiteSpace(root))
			root = AppContext.BaseDirectory;

		var path = CombineSafe(root, appFolderName);
		EnsureDirectory(path);
		return path;
	}

	/// <summary>
	/// Combine like Path.Combine, but ignore null/whitespace segments.
	/// </summary>
	public static string CombineSafe(params string?[] parts) {
		var clean = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
		if(clean.Length == 0) return string.Empty;

		var result = clean[0]!;
		for(int i = 1; i < clean.Length; i++)
			result = Path.Combine(result, clean[i]!);
		return result;
	}

	/// <summary>
	/// Ensure directory exists; no-op on null/empty.
	/// </summary>
	public static string EnsureDirectory(string? path) {
		if(!string.IsNullOrWhiteSpace(path))
			Directory.CreateDirectory(path);
		return path ?? string.Empty;
	}
}