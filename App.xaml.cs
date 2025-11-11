// App.xaml.cs
using System.Windows;
using TaskMate.Services;
using TaskMate.Services.Auth;   
using TaskMate.Sync;

namespace TaskMate {
    public partial class App : Application {
        protected override void OnStartup(StartupEventArgs e) {
            this.DispatcherUnhandledException += (_, args) => {
                System.Diagnostics.Debug.WriteLine(args.Exception.ToString());
                MessageBox.Show(args.Exception.ToString(), "Startup error");
            };
            //initialize global Auth + Firestore REST early ===
            const string ProjectId = "taskmate-4777f";
            const string WebApiKey = "AIzaSyD0umHa8ERVEYSV7TdUc54FQ4-665lyDnw";

            var auth = new AuthService();
            AppServices.Auth = auth;
            AppServices.FirestoreRest = new FirestoreRestClient(ProjectId, WebApiKey, auth);

            SoundService.Initialize();
            base.OnStartup(e); // your StartupUri opens MainWindow
        }
    }
}