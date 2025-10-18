using System.Windows;
using TaskMate.Services.Notifications;

namespace TaskMate {
    public partial class App : Application {
        protected override void OnStartup(StartupEventArgs e) {
            this.DispatcherUnhandledException += (_, args) => {
                System.Diagnostics.Debug.WriteLine(args.Exception.ToString());
                MessageBox.Show(args.Exception.ToString(), "Startup error");
            };
            base.OnStartup(e);
            TaskMate.Services.SoundService.Initialize();

            ToastRegistration.EnsureRegistered();
            ToastRegistration.ShowSimple("TaskMate", "Notifications ready");

        }
    }
}