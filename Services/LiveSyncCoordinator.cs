using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using TaskMate.Models;
using TaskMate.Sync;
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

			System.Diagnostics.Trace.Listeners.Clear();
			System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(logPath));
			System.Diagnostics.Trace.AutoFlush = true;
		}

		public event Action? PartnerDisconnected;

		public void Attach(
			ObservableCollection<TaskItem> tasks,
			ObservableCollection<TaskItem> pendingRequests,
			ICollectionView myTasksView,
			ICollectionView partnerTasksView) {
			_tasks = tasks;
			_pending = pendingRequests;
			_myView = myTasksView;
			_partnerView = partnerTasksView;
		}

		public async Task StartAsync() {
			await FirestoreClient.InitializeAsync();

			var myId = FirestoreClient.CurrentUserId;
			var partnerId = _partner.PartnerId;
			var groupId = _partner.GroupId;

			// Personal list
			_myHandle = _requests.ListenPersonal(myId, cloud => {
				List<TaskItem>? snapshot = null;

				Application.Current.Dispatcher.BeginInvoke(() => {
					if(_tasks is null) return;
					// BEFORE: capture current IDs
					var before = new HashSet<Guid>(_tasks.Select(t => t.Id));
					TaskCollectionHelpers.UpsertInto(_tasks, cloud, assignedTo: Assignee.Me, ownerUserId: myId,
						onReplaced: (oldItem, newItem) => {
							// fire only after initial priming to avoid startup spam
							if(_myPersonalPrimed) SoundService.PlayTaskCompleted();
						});
					var added = _tasks.Where(t => !before.Contains(t.Id)).ToList();
					// Play only after the first snapshot is primed
					if(_myPersonalPrimed && added.Count > 0)
						SoundService.PlayTaskCreated();
					_myPersonalPrimed = true;
					snapshot = _tasks.ToList();            // take a copy on UI thread
					_myView?.Refresh();
					_partnerView?.Refresh();
				});

				if(snapshot != null)
					_ = Task.Run(() => TaskDataService.SaveTasks(snapshot));  // non-blocking
			});

			// Partner request listeners (incoming/outgoing)


			// Partner/group (only when verified)
			if(!string.IsNullOrWhiteSpace(partnerId)) {
				AttachPartnerAndGroup(partnerId, groupId);
				_attachedPartnerId = partnerId;   // << crucial: record what we attached
				_attachedGroupId = groupId;

			}
			_startedTcs.TrySetResult(true);
		}

		public async Task ReloadForPartnerAsync() {

			await _startedTcs.Task; // ensure StartAsync ran at least once
			await _reloadGate.WaitAsync();
			try {
				// read current desired ids
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
					}
					else if(groupChanged) {
						// Same partner; only group changed → keep partner tasks, drop pending
						_pending.Clear();
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
					TaskCollectionHelpers.UpsertInto(_tasks, cloud, assignedTo: Assignee.Partner, ownerUserId: partnerId,
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

					var myId = TaskMate.Sync.FirestoreClient.CurrentUserId;
					var partnerId = _partner.PartnerId;

					bool IsMineToDecide(TaskItem t) {
						if(!string.IsNullOrWhiteSpace(t.AssignedToUserId))
							return string.Equals(t.AssignedToUserId, myId, StringComparison.OrdinalIgnoreCase);

						// Fallback when AssignedToUserId isn't set
						if(t.AssignedTo == TaskMate.Models.Enums.Assignee.Me)
							return string.Equals(t.CreatedBy, partnerId, StringComparison.OrdinalIgnoreCase);

						if(t.AssignedTo == TaskMate.Models.Enums.Assignee.Partner)
							return string.Equals(t.CreatedBy, myId, StringComparison.OrdinalIgnoreCase) == false;

						return false;
					}

					var mine = cloud.Where(IsMineToDecide).ToList(); // 👈 actually use the helper

					// 👇 Toast only brand-new "mine" requests (dedup with a HashSet<Guid>)
					foreach(var t in mine) {
						if(_lastPendingMine.Add(t.Id)) {
							// We don't have display names; show a friendly label from CreatedBy
							// If the partner created it -> "Partner", else "You"
							var mineId = TaskMate.Sync.FirestoreClient.CurrentUserId;
							var fromLabel = string.Equals(t.CreatedBy, mineId, StringComparison.OrdinalIgnoreCase)
											? "You"
											: "Partner";

							var title = string.IsNullOrWhiteSpace(t.Title) ? "New task" : t.Title;
							AppServices.Notifications.ShowTaskRequestToast(t.Id, fromLabel, title);
						}
					}

					foreach(var t in cloud) t.CanDecide = false;
					foreach(var t in mine) t.CanDecide = true;

					TaskCollectionHelpers.ReplaceAll(
						_pending,
						mine,
						assignedTo: TaskMate.Models.Enums.Assignee.Partner,
						requestMode: true
					);
					foreach(var t in _pending)
						t.CanDecide = true;
					// keep dedupe set tight (drop ids no longer pending)
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