using System.Windows;
using TaskMate.Services.Notifications;

namespace TaskMate {
    public partial class App : Application {
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);
            TaskMate.Services.SoundService.Initialize();

            ToastRegistration.EnsureRegistered();
            ToastRegistration.ShowSimple("TaskMate", "Notifications ready");

            PathEx.UseLocalData("B");
        }
    }
}