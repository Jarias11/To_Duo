using System.Security.Cryptography;
using System.Text;

namespace TaskMate.Services.Auth {
    /// <summary>
    /// Encrypts & stores tokens under %APPDATA%\TaskMate\auth.bin using DPAPI (CurrentUser scope).
    /// Implements atomic writes and sharing-friendly reads to avoid file locking.
    /// </summary>
    public static class TokenStore {
        private static readonly string DirPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskMate");

        private static readonly string FilePath = Path.Combine(DirPath, "auth.bin");

        public static void Save(string uid, string refreshToken, string idToken) {
            Directory.CreateDirectory(DirPath);

            // 1) Serialize + protect
            var payload = $"{uid}\n{refreshToken}\n{idToken}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

            // 2) Write to a temp file, then atomically replace
            var tmp = FilePath + ".tmp";
            const int attempts = 3;

            for(int i = 1; i <= attempts; i++) {
                try {
                    using(var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        fs.Write(protectedBytes, 0, protectedBytes.Length);
                        fs.Flush(true);
                    }

                    if(File.Exists(FilePath))
                        File.Replace(tmp, FilePath, destinationBackupFileName: null);
                    else
                        File.Move(tmp, FilePath);

                    return;
                }
                catch(IOException) when(i < attempts) {
                    Thread.Sleep(50 * i);
                }
                catch {
                    // Cleanup temp if something unexpected happens
                    TryDelete(tmp);
                    throw;
                }
            }
        }

        public static (string uid, string refreshToken, string idToken)? Load() {
            if(!File.Exists(FilePath)) return null;

            try {
                // Allow concurrent writers to replace the file while we read
                using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                var protectedBytes = ms.ToArray();

                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var text = Encoding.UTF8.GetString(bytes);
                var parts = text.Split('\n');
                if(parts.Length < 3) return null;
                return (parts[0], parts[1], parts[2]);
            }
            catch {
                // Corrupt/old file — remove it to avoid loops
                Clear();
                return null;
            }
        }

        public static void Clear() {
            TryDelete(FilePath);
            TryDelete(FilePath + ".tmp");
        }

        private static void TryDelete(string path) {
            try {
                if(File.Exists(path)) {
                    // best-effort delete with tiny retry (in case another handle is momentarily open)
                    for(int i = 0; i < 2; i++) {
                        try { File.Delete(path); return; }
                        catch(IOException) { Thread.Sleep(25); }
                    }
                    File.Delete(path);
                }
            }
            catch { /* swallow */ }
        }
    }
}
