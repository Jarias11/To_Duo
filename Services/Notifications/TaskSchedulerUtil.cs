// Services/Notifications/TaskSchedulerUtil.cs
using System.Diagnostics;

namespace TaskMate.Services.Notifications
{
    public static class TaskSchedulerUtil
    {
        private const string TaskName = "TaskMate Notifier"; // keep consistent with your setup

        public static void DisableNotifierTask()
        {
            try
            {
                // Disable (keeps it around but won’t run)
                Run("schtasks.exe", $"/change /tn \"{TaskName}\" /disable");
            }
            catch { /* ignore */ }

            try
            {
                // Optional: delete entirely so nothing is left behind
                Run("schtasks.exe", $"/delete /tn \"{TaskName}\" /f");
            }
            catch { /* ignore */ }
        }

        public static void EnableOrCreateNotifierTask(string exePath)
        {
            try
            {
                // Create or replace; runs at logon and every 5 minutes while logged in
                Run("schtasks.exe",
                    $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc minute /mo 5 /rl LIMITED /f");
                Run("schtasks.exe", $"/change /tn \"{TaskName}\" /enable");
            }
            catch { /* inform user if you want */ }
        }

        private static void Run(string file, string args)
        {
            var p = new Process();
            p.StartInfo.FileName = file;
            p.StartInfo.Arguments = args;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.Start();
            p.WaitForExit(8000);
        }
    }
}
