// Services/AppServices.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using TaskMate.Models;
using TaskMate.Orchestration;
using TaskMate.Services.Notifications;
using TaskMate.Services.Auth;   // NEW
using TaskMate.Sync;        // NEW

namespace TaskMate.Services {
	public static class AppServices {
		// === NEW: Auth + Firestore REST singletons (set in App.xaml.cs on startup) ===
		public static IAuthService Auth { get; set; } = default!;
		public static FirestoreRestClient FirestoreRest { get; set; } = default!;

		// Existing singletons you already use around the app:
		public static INotificationService Notifications { get; set; } = new NotificationService();
		public static IPairingOrchestrator Pairing { get; set; } = default!;
		public static ITaskDialogService TaskDialogs { get; set; } = default!;
		public static ITaskService Tasks { get; set; } = default!;
		public static IPartnerService Partner { get; set; } = default!;   // for UserId/GroupId
		public static ITaskActions Actions { get; set; } = default!;
		public static ISettingsService Settings { get; set; } = default!;
		public static bool NotificationsEnabled => Settings?.NotificationsEnabled == true;

		// Called by ToastActivator to open a task by id
		public static async Task OpenTaskDetailsByIdAsync(Guid taskId) {
			if(Tasks is null || TaskDialogs is null) return;

			var item =
				Tasks.Tasks.FirstOrDefault(t => t.Id == taskId) ??
				Tasks.PendingTasks.FirstOrDefault(t => t.Id == taskId);

			if(item != null)
				await TaskDialogs.ShowTaskDetailsAsync(item);
		}

		public static class PairingHelpers {
			public static async Task TryAcceptByRequestIdAsync(string requestId) {
				var r = Pairing.Incoming.FirstOrDefault(p => string.Equals(p.Id, requestId, StringComparison.OrdinalIgnoreCase));
				if(r != null) {
					var me = Partner.UserId;
					await Pairing.AcceptAsync(me, r);
				}
			}

			public static async Task TryDeclineByRequestIdAsync(string requestId) {
				var r = Pairing.Incoming.FirstOrDefault(p => string.Equals(p.Id, requestId, StringComparison.OrdinalIgnoreCase));
				if(r != null) {
					var me = Partner.UserId;
					await Pairing.DeclineAsync(me, r);
				}
			}

			public static async Task TryAcceptPendingByIdAsync(Guid id) {
				var item = Tasks?.PendingTasks?.FirstOrDefault(t => t.Id == id);
				if(item == null) return;
				var me = Partner.UserId;
				var group = Partner.GroupId;
				await Actions.AcceptAsync(item, me, group);
			}

			public static async Task TryDeclinePendingByIdAsync(Guid id) {
				var item = Tasks?.PendingTasks?.FirstOrDefault(t => t.Id == id);
				if(item == null) return;
				var group = Partner.GroupId;
				await Actions.DeclineAsync(item, group);
			}
		}

		public static class TaskRequestHelpers {
			public static async Task TryAcceptPendingByIdAsync(Guid id) {
				var item = Tasks?.PendingTasks?.FirstOrDefault(t => t.Id == id);
				if(item == null) return;
				await Actions.AcceptAsync(item, Partner.UserId, Partner.GroupId);
			}

			public static async Task TryDeclinePendingByIdAsync(Guid id) {
				var item = Tasks?.PendingTasks?.FirstOrDefault(t => t.Id == id);
				if(item == null) return;
				await Actions.DeclineAsync(item, Partner.GroupId);
			}
		}
	}
}
