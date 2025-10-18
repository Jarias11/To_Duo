// Services/NotificationService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using Microsoft.Toolkit.Uwp.Notifications;          // 7.1.3
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace TaskMate.Services.Notifications {
	/// <summary>
	/// Central place to register, build, show, and schedule toasts.
	/// Uses DesktopNotificationManagerCompat via your ToastRegistration.
	/// </summary>
	public interface INotificationService {
		void EnsureRegistered(); // calls your ToastRegistration.EnsureRegistered()

		// Immediate toasts (while app or helper is running)
		void ShowDueSoonToast(Guid taskId, string title, DateTime whenLocal);
		void ShowDueNowToast(Guid taskId, string title);
		void ShowPartnerRequestToast(string requestId, string fromName, string title);
		public void DisableAllToasts();

		// Scheduled (works when app is closed)
		void ScheduleDueToasts(Guid taskId, string title, DateTime dueLocal, TimeSpan preDueOffset);
		void CancelDueToasts(Guid taskId); // cancel both "soon" and "now"
		void ShowTaskRequestToast(Guid requestId, string fromName, string title);

	}

	public sealed class NotificationService : INotificationService {
		private const string SoonKey = "soon";
		private const string NowKey = "now";
		private static bool IsEnabled => TaskMate.Services.AppServices.Settings?.NotificationsEnabled == true;


		// Persist scheduled ids so we can cancel/update across app restarts
		private readonly string _statePath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"TaskMate", "notifications.json");

		private Dictionary<string, string[]> _scheduledIndex; // taskId -> [soonTag, nowTag]

		public NotificationService() {
			Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
			_scheduledIndex = LoadIndex();
		}
		public void ShowTaskRequestToast(Guid requestId, string fromName, string title) {
			EnsureRegistered();

			new ToastContentBuilder()
				.AddText($"New task from {fromName}")
				.AddText(title)
				.AddButton(new ToastButton("Accept",
						BuildArgs(("action", "acceptRequest"), ("requestId", requestId.ToString())))
						.SetBackgroundActivation())
				.AddButton(new ToastButton("Decline",
						BuildArgs(("action", "declineRequest"), ("requestId", requestId.ToString())))
						.SetBackgroundActivation())
				.Show();
		}

		public void EnsureRegistered() {
			if(!IsEnabled) return;
			// you already have this helper in your project
			ToastRegistration.EnsureRegistered(); // AUMID + COM server + Start menu shortcut
		}

		public void ShowDueSoonToast(Guid taskId, string title, DateTime whenLocal) {
			if(!IsEnabled) return;
			EnsureRegistered();

			new ToastContentBuilder()
				.AddText(title)
				.AddText($"Due at {whenLocal:htt}")
				.AddArgument("action", "openTask")
				.AddArgument("taskId", taskId.ToString())
				.Show(); // DesktopNotificationManagerCompat handles the compat layer
		}

		public void ShowDueNowToast(Guid taskId, string title) {
			if(!IsEnabled) return;
			EnsureRegistered();

			new ToastContentBuilder()
				.AddText(title)
				.AddText("Due now")
				.AddArgument("action", "openTask")
				.AddArgument("taskId", taskId.ToString())
				.Show();
		}

		public void ShowPartnerRequestToast(string requestId, string fromName, string title) {
			if(!IsEnabled) return;
			EnsureRegistered();

			new ToastContentBuilder()
				.AddText($"New task from {fromName}")
				.AddText(title)
				.AddButton(new ToastButton("Accept",
						BuildArgs(("action", "acceptPartner"), ("requestId", requestId)))
						.SetBackgroundActivation())
				.AddButton(new ToastButton("Decline",
						BuildArgs(("action", "declinePartner"), ("requestId", requestId)))
						.SetBackgroundActivation())
				.Show();
		}

		public void ScheduleDueToasts(Guid taskId, string title, DateTime dueLocal, TimeSpan preDueOffset) {
			if(!IsEnabled) return;
			EnsureRegistered();

			// Build a single XML we can reuse (arguments differ by stage)
			ToastContent content = new ToastContentBuilder()
				.AddText(title)
				.AddText($"Due at {dueLocal:htt}")
				.AddArgument("action", "openTask")
				.AddArgument("taskId", taskId.ToString())
				.GetToastContent();

			var xml = new XmlDocument();
			xml.LoadXml(content.GetContent());

			// Create tags to later cancel by Id (unique per task+stage)
			string soonTag = $"{taskId}:{SoonKey}";
			string nowTag = $"{taskId}:{NowKey}";

			// Remove old scheduled ones if any
			CancelDueToasts(taskId);

			// Schedule
			var notifier = DesktopNotificationManagerCompat.CreateToastNotifier();

			DateTime dueUtc = dueLocal.ToUniversalTime();
			DateTime soonUtc = dueUtc - preDueOffset;

			if(soonUtc > DateTime.UtcNow) {
				var soonToast = new ScheduledToastNotification(xml, soonUtc) {
					Tag = soonTag,
					Group = "TaskDue"
				};
				notifier.AddToSchedule(soonToast);
			}

			if(dueUtc > DateTime.UtcNow) {
				var nowToast = new ScheduledToastNotification(xml, dueUtc) {
					Tag = nowTag,
					Group = "TaskDue"
				};
				notifier.AddToSchedule(nowToast);
			}

			// remember for cancellation
			_scheduledIndex[taskId.ToString()] = new[] { soonTag, nowTag };
			SaveIndex();
		}

		public void CancelDueToasts(Guid taskId) {
			if(!IsEnabled) return;
			var notifier = DesktopNotificationManagerCompat.CreateToastNotifier();
			var scheduled = notifier.GetScheduledToastNotifications();

			if(_scheduledIndex.TryGetValue(taskId.ToString(), out var tags)) {
				foreach(var s in scheduled.Where(s => s.Group == "TaskDue" && tags.Contains(s.Tag)))
					notifier.RemoveFromSchedule(s);

				_scheduledIndex.Remove(taskId.ToString());
				SaveIndex();
			}
			else {
				// Best-effort sweep if tags missing
				foreach(var s in scheduled.Where(s => s.Group == "TaskDue" && s.Tag.StartsWith(taskId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList())
					notifier.RemoveFromSchedule(s);
			}
		}

		// --- helpers ----
		private static string BuildArgs(params (string key, string value)[] kv) =>
			string.Join("&", kv.Select(p => $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value)}"));

		private Dictionary<string, string[]> LoadIndex() {
			try {
				if(File.Exists(_statePath)) {
					var json = File.ReadAllText(_statePath);
					return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
						   ?? new();
				}
			}
			catch { /* ignore */ }
			return new();
		}
		private void SaveIndex() {
			try {
				var json = System.Text.Json.JsonSerializer.Serialize(_scheduledIndex);
				File.WriteAllText(_statePath, json);
			}
			catch { /* ignore */ }
		}
		public void DisableAllToasts() {
			// Cancel everything we scheduled (best-effort).
			try {
				var notifier = DesktopNotificationManagerCompat.CreateToastNotifier();
				var scheduled = notifier.GetScheduledToastNotifications();
				foreach(var s in scheduled.ToList()) {
					// If you only want to remove ours, gate on s.Group == "TaskDue"
					notifier.RemoveFromSchedule(s);
				}
			}
			catch { /* ignore */ }

			// Clear our local index so future cancels don’t try to reference stale tags.
			_scheduledIndex.Clear();
			SaveIndex();
		}
	}

	/// <summary>Shared parser for toast activation arguments.</summary>
	public static class ToastArgs {
		public static IReadOnlyDictionary<string, string> Parse(string query) {
			// Desktop activations come in like "action=openTask&taskId=..."
			var nvc = HttpUtility.ParseQueryString(query ?? string.Empty);
			return nvc.AllKeys.Where(k => k != null)
				.ToDictionary(k => k!, k => nvc[k] ?? string.Empty, StringComparer.OrdinalIgnoreCase);
		}

		public static string Get(this IReadOnlyDictionary<string, string> map, string key)
			=> map.TryGetValue(key, out var v) ? v : string.Empty;
	}
}
