using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using TaskMate.Models;
using TaskMate.Data;
using TaskMate.Models.Enums;

namespace TaskMate.Services {
	public sealed class LiveSyncCoordinator : ILiveSyncCoordinator {
		private readonly IRequestService _requests;
		private readonly IPartnerService _partner;
		private readonly SemaphoreSlim _reloadGate = new(1, 1);
		private readonly TaskCompletionSource<bool> _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly HashSet<Guid> _lastPendingMine = new();
		private string? _attachedPartnerId;
		private string? _attachedGroupId;

		// UI targets provided by the VM
		private ObservableCollection<TaskItem>? _tasks;
		private ObservableCollection<TaskItem>? _pending;
		private ObservableCollection<TaskItem>? _pendingSent;
		private ICollectionView? _myView;
		private ICollectionView? _partnerView;

		private bool _myPersonalPrimed;
		private bool _partnerPersonalPrimed;

		private IDisposable? _myHandle, _partnerHandle, _groupHandle;

		public LiveSyncCoordinator(IRequestService requests, IPartnerService partner) {
			_requests = requests;
			_partner = partner;
		}

		static LiveSyncCoordinator() {
			var logDir = PathEx.GetAppDataDir("TaskMate");
			var logPath = PathEx.CombineSafe(logDir, "app.log");

			// ✅ make sure the folder is there
			try { System.IO.Directory.CreateDirectory(logDir); } catch { }

			System.Diagnostics.Trace.Listeners.Clear();
			System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(logPath));
			System.Diagnostics.Trace.AutoFlush = true;

			System.Diagnostics.Trace.WriteLine($"[Trace] LiveSync static ctor – log: {logPath}");
		}

		public event Action? PartnerDisconnected;

		public void Attach(
			ObservableCollection<TaskItem> tasks,
			ObservableCollection<TaskItem> pendingRequests,
			ObservableCollection<TaskItem> sentPendingRequests,
			ICollectionView myTasksView,
			ICollectionView partnerTasksView) {
			_tasks = tasks;
			_pending = pendingRequests;
			_pendingSent = sentPendingRequests;
			_myView = myTasksView;
			_partnerView = partnerTasksView;
		}

		public async Task StartAsync() {
			// REST path: no FirestoreClient.InitializeAsync() needed.

			// Prefer the authenticated UID; fall back to Settings.
			var myId = AppServices.Auth?.Uid ?? AppServices.Settings?.UserId ?? string.Empty;
			System.Diagnostics.Trace.WriteLine($"[LiveSync] StartAsync myId='{myId}', partner='{_partner.PartnerId}', group='{_partner.GroupId}'");
			if(string.IsNullOrWhiteSpace(myId)) {
				System.Diagnostics.Trace.WriteLine("[LiveSync] myId is empty; skipping listeners.");
				_startedTcs.TrySetResult(true);
				return;
			}

			var partnerId = _partner.PartnerId;
			var groupId = _partner.GroupId;

			// Personal list
			_myHandle?.Dispose();
			_myHandle = _requests.ListenPersonal(myId, cloud => {
				System.Diagnostics.Trace.WriteLine($"[LiveSync] personal snapshot received: {cloud.Count} tasks");
				Application.Current.Dispatcher.BeginInvoke(() => {
					if(_tasks is null) return;

					// BEFORE: capture current IDs
					var before = new HashSet<Guid>(_tasks.Select(t => t.Id));

					TaskCollectionHelpers.UpsertInto(
						_tasks, cloud,
						assignedTo: Assignee.Me,
						ownerUserId: myId,
						onReplaced: (oldItem, newItem) => {
							if(_myPersonalPrimed) SoundService.PlayTaskCompleted();
						});

					var added = _tasks.Where(t => !before.Contains(t.Id)).ToList();
					if(_myPersonalPrimed && added.Count > 0)
						SoundService.PlayTaskCreated();

					_myPersonalPrimed = true;

					// ✨ Save snapshot AFTER UI list is updated (guaranteed non-null)
					var snapshot = _tasks.ToList();
					_ = Task.Run(() => TaskDataService.SaveTasks(snapshot));

					_myView?.Refresh();
					_partnerView?.Refresh();
				});
			});

			// Partner/group (only when verified)
			if(!string.IsNullOrWhiteSpace(partnerId)) {
				AttachPartnerAndGroup(partnerId, groupId);
				_attachedPartnerId = partnerId;
				_attachedGroupId = groupId;
			}

			_startedTcs.TrySetResult(true);
		}

		public async Task ReloadForPartnerAsync() {
			await _startedTcs.Task; // ensure StartAsync ran at least once
			await _reloadGate.WaitAsync();
			try {
				var partnerId = _partner.PartnerId;
				var groupId = _partner.GroupId;
				var expectGroup = !string.IsNullOrWhiteSpace(groupId);
				var haveGroup = _groupHandle is not null;

				// no-op if already attached to the same ids and handles are alive
				if(!string.IsNullOrWhiteSpace(partnerId) &&
					_partnerHandle is not null &&
					string.Equals(_attachedPartnerId, partnerId, StringComparison.OrdinalIgnoreCase) &&
					((expectGroup && haveGroup && string.Equals(_attachedGroupId, groupId, StringComparison.OrdinalIgnoreCase)) ||
					 (!expectGroup && !haveGroup))) {
					return; // nothing to change; don’t clear!
				}

				bool partnerChanged = !string.Equals(_attachedPartnerId, partnerId, StringComparison.OrdinalIgnoreCase);
				bool groupChanged = !string.Equals(_attachedGroupId, groupId, StringComparison.OrdinalIgnoreCase);

				// stop previous partner/group listeners
				_partnerHandle?.Dispose();
				_groupHandle?.Dispose();
				_partnerHandle = _groupHandle = null;

				// clear partner-sourced UI ONLY if we are detaching or switching partners
				Application.Current.Dispatcher.Invoke(() => {
					if(_tasks is null || _pending is null) return;

					if(partnerChanged || string.IsNullOrWhiteSpace(partnerId)) {
						// Switching partner or detaching → remove partner rows
						var keepMine = _tasks.Where(t => t.AssignedTo == Assignee.Me).ToList();
						_tasks.Clear();
						foreach(var t in keepMine) _tasks.Add(t);
						_pending.Clear(); // pending rows belong to group; drop all
						_pendingSent?.Clear();
					}
					else if(groupChanged) {
						// Same partner; only group changed → keep partner tasks, drop pending
						_pending.Clear();
						_pendingSent?.Clear();
					}
					_myView?.Refresh();
					_partnerView?.Refresh();
				});

				// reattach if verified (and keep the “what we attached” snapshot)
				if(!string.IsNullOrWhiteSpace(partnerId)) {
					AttachPartnerAndGroup(partnerId, groupId);
					_attachedPartnerId = partnerId;
					_attachedGroupId = groupId;
				}
				else {
					_attachedPartnerId = _attachedGroupId = null;
				}
			}
			finally {
				_reloadGate.Release();
			}
		}

		private void AttachPartnerAndGroup(string partnerId, string groupId) {
			_partnerHandle = _requests.ListenPersonal(partnerId, cloud => {
				Application.Current.Dispatcher.BeginInvoke(() => {
					if(_tasks is null) return;

					var before = new HashSet<Guid>(_tasks.Select(t => t.Id));

					TaskCollectionHelpers.UpsertInto(
						_tasks, cloud,
						assignedTo: Assignee.Partner,
						ownerUserId: partnerId,
						onReplaced: (oldItem, newItem) => {
							if(_partnerPersonalPrimed) SoundService.PlayTaskCompleted();
						});

					var added = _tasks.Where(t => !before.Contains(t.Id)).ToList();
					if(_partnerPersonalPrimed && added.Count > 0)
						SoundService.PlayTaskCreated();

					_partnerPersonalPrimed = true;
					_myView?.Refresh();
					_partnerView?.Refresh();
				});
			});

			if(string.IsNullOrWhiteSpace(groupId))
				return; // nothing to attach until GroupId is set

			_groupHandle = _requests.ListenRequests(groupId, cloud => {
				Application.Current.Dispatcher.BeginInvoke(() => {
					if(_pending is null) return;

					var myId = AppServices.Settings?.UserId ?? AppServices.Auth?.Uid ?? string.Empty;
					var partnerIdLocal = _partner.PartnerId;

					bool IsMineToDecide(TaskItem t) {
						if(!string.IsNullOrWhiteSpace(t.AssignedToUserId))
							return string.Equals(t.AssignedToUserId, myId, StringComparison.OrdinalIgnoreCase);

						// Fallback when AssignedToUserId isn't set
						if(t.AssignedTo == Assignee.Me)
							return string.Equals(t.CreatedBy, partnerIdLocal, StringComparison.OrdinalIgnoreCase);

						if(t.AssignedTo == Assignee.Partner)
							return !string.Equals(t.CreatedBy, myId, StringComparison.OrdinalIgnoreCase);

						return false;
					}

					var mine = cloud.Where(IsMineToDecide).ToList();
					var sent = cloud.Where(t => !IsMineToDecide(t)).ToList();

					// Toast only brand-new "mine" requests (dedupe)
					foreach(var t in mine) {
						if(_lastPendingMine.Add(t.Id)) {
							var fromLabel = string.Equals(t.CreatedBy, myId, StringComparison.OrdinalIgnoreCase)
								? "You"
								: "Partner";
							var title = string.IsNullOrWhiteSpace(t.Title) ? "New task" : t.Title;
							AppServices.Notifications.ShowTaskRequestToast(t.Id, fromLabel, title);
						}
					}

					TaskCollectionHelpers.ReplaceAll(_pending, mine, assignedTo: Assignee.Partner, requestMode: true);
					if(_pendingSent != null)
						TaskCollectionHelpers.ReplaceAll(_pendingSent, sent, assignedTo: Assignee.Partner, requestMode: true);

					foreach(var t in _pending)
						t.CanDecide = true;

					_lastPendingMine.IntersectWith(mine.Select(x => x.Id));
				});
			});
		}

		public void Dispose() {
			_myHandle?.Dispose();
			_partnerHandle?.Dispose();
			_groupHandle?.Dispose();
			_myHandle = _partnerHandle = _groupHandle = null;
		}
	}
}
