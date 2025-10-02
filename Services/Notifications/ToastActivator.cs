// Services/Notifications/ToastActivator.cs
using System;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;   // NotificationActivator, NotificationUserInput

namespace TaskMate.Services.Notifications {
	internal static class ToastIds {
		public const string Aumid = "TaskMate.ToDuo";                // any unique string
		public const string ActivatorClsid = "7da91697-ce9b-4f49-af9c-efadbcea44df"; // your GUID
	}

	[ComVisible(true)]
	[Guid(ToastIds.ActivatorClsid)]
	[ClassInterface(ClassInterfaceType.None)]
#pragma warning disable CS0618 // Toolkit marks it obsolete; still required for generic <T> registration
	public sealed class ToastActivator : NotificationActivator
#pragma warning restore CS0618
	{
		public override void OnActivated(string arguments, NotificationUserInput userInput, string appUserModelId) {
			// Parse args (ToastArgs is in TaskMate.Services)
			var args = ToastArgs.Parse(arguments);
			var action = args.Get("action");

			// Bring app to front
			Application.Current?.Dispatcher?.Invoke(() => {
				var mw = Application.Current.MainWindow;
				if(mw != null) {
					if(!mw.IsVisible) mw.Show();
					mw.Activate();
					mw.Topmost = true; mw.Topmost = false;
				}
			});

			// Route
			switch(action?.ToLowerInvariant()) {
				case "opentask": {
						var id = args.Get("taskid");
						Application.Current?.Dispatcher?.Invoke(async () => {
							if(Guid.TryParse(id, out var taskId))
								await AppServices.OpenTaskDetailsByIdAsync(taskId);
						});
						break;
					}
				case "acceptpartner": {
						var rid = args.Get("requestid");
						Application.Current?.Dispatcher?.Invoke(async () => {
							await AppServices.PairingHelpers.TryAcceptByRequestIdAsync(rid);
							AppServices.Notifications.ShowDueSoonToast(Guid.NewGuid(), "Partner request", DateTime.Now);
						});
						break;
					}
				case "declinepartner": {
						var rid = args.Get("requestid");
						Application.Current?.Dispatcher?.Invoke(async () => {
							await AppServices.PairingHelpers.TryDeclineByRequestIdAsync(rid);
						});
						break;
					}
				case "acceptrequest": {
						var id = args.Get("requestid");
						if(Guid.TryParse(id, out var rid)) {
							_ = Application.Current?.Dispatcher?.InvokeAsync(async () => {
								try { await AppServices.TaskRequestHelpers.TryAcceptPendingByIdAsync(rid); }
								catch(Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
							});
						}
						break;
					}

				case "declinerequest": {
						var id = args.Get("requestid");
						if(Guid.TryParse(id, out var rid)) {
							_ = Application.Current?.Dispatcher?.InvokeAsync(async () => {
								try { await AppServices.TaskRequestHelpers.TryDeclinePendingByIdAsync(rid); }
								catch(Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
							});
						}
						break;
					}
			}
		}
	}
}
