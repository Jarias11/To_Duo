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
		private readonly IPartnerRequestService _partnerReqs;
		private readonly IPartnerService _partner;

		// UI targets provided by the VM
		private ObservableCollection<TaskItem>? _tasks;
		private ObservableCollection<TaskItem>? _pending;
		private ObservableCollection<PartnerRequest>? _incoming;
		private ObservableCollection<PartnerRequest>? _outgoing;
		private ICollectionView? _myView;
		private ICollectionView? _partnerView;

		// Live handles
		private IDisposable? _myHandle, _partnerHandle, _groupHandle, _incomingHandle, _outgoingHandle;

		public LiveSyncCoordinator(IRequestService requests, IPartnerRequestService partnerReqs, IPartnerService partner) {
			_requests = requests;
			_partnerReqs = partnerReqs;
			_partner = partner;
		}

		public event Action? PartnerDisconnected;

		public void Attach(
			ObservableCollection<TaskItem> tasks,
			ObservableCollection<TaskItem> pendingRequests,
			ObservableCollection<PartnerRequest> incomingReqs,
			ObservableCollection<PartnerRequest> outgoingReqs,
			ICollectionView myTasksView,
			ICollectionView partnerTasksView) {
			_tasks = tasks;
			_pending = pendingRequests;
			_incoming = incomingReqs;
			_outgoing = outgoingReqs;
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
				Application.Current.Dispatcher.Invoke(() => {
					if(_tasks is null) return;
					TaskCollectionHelpers.UpsertInto(_tasks, cloud, assignedTo: Assignee.Me, ownerUserId: myId);
					TaskDataService.SaveTasks(_tasks.ToList());
					_myView?.Refresh();
					_partnerView?.Refresh();
				});
			});

			// Partner request listeners (incoming/outgoing)
			_incomingHandle = _partnerReqs.ListenIncoming(myId, list => {
				Application.Current.Dispatcher.Invoke(() => {
					if(_incoming is null) return;
					_incoming.Clear();
					foreach(var r in list.Where(x => x.Status == "pending"))
						_incoming.Add(r);

					var disconnected = list.FirstOrDefault(x => x.Status == "disconnected");
					if(disconnected != null) {
						var other = disconnected.FromUserId;
						if(!string.IsNullOrWhiteSpace(other)) {
							_ = _partnerReqs.PurgePairAsync(myId, other);
							_partner.PartnerId = string.Empty;
							TearDownPartnerListenersAndClearUi();
							PartnerDisconnected?.Invoke();
						}
					}
				});
			});

			_outgoingHandle = _partnerReqs.ListenOutgoing(myId, list => {
				Application.Current.Dispatcher.Invoke(() => {
					if(_outgoing is null) return;
					_outgoing.Clear();
					foreach(var r in list)
						_outgoing.Add(r);

					var accepted = list.FirstOrDefault(x => x.Status == "accepted");
					if(accepted != null) {
						var other = accepted.ToUserId == myId ? accepted.FromUserId : accepted.ToUserId;
						if(!string.IsNullOrWhiteSpace(other) && _partner.PartnerId != other) {
							_partner.PartnerId = other; // persists + raises PartnerChanged in your service
							_ = ReloadForPartnerAsync();
						}
					}

					var disconnected = list.FirstOrDefault(x => x.Status == "disconnected");
					if(disconnected != null) {
						var other = disconnected.FromUserId;
						if(!string.IsNullOrWhiteSpace(other)) {
							_ = _partnerReqs.PurgePairAsync(myId, other);
							_partner.PartnerId = string.Empty;
							TearDownPartnerListenersAndClearUi();
							PartnerDisconnected?.Invoke();
						}
					}
				});
			});

			// Partner/group (only when verified)
			if(!string.IsNullOrWhiteSpace(partnerId))
				AttachPartnerAndGroup(partnerId, groupId);
		}

		public async Task ReloadForPartnerAsync() {
			// stop previous partner/group listeners
			_partnerHandle?.Dispose();
			_groupHandle?.Dispose();
			_partnerHandle = _groupHandle = null;

			// clear partner-sourced UI
			Application.Current.Dispatcher.Invoke(() => {
				if(_tasks is null || _pending is null) return;
				var mine = _tasks.Where(t => t.AssignedTo == Assignee.Me).ToList();
				_tasks.Clear();
				foreach(var t in mine) _tasks.Add(t);
				_pending.Clear();
			});

			// reattach if verified
			var partnerId = _partner.PartnerId;
			if(!string.IsNullOrWhiteSpace(partnerId))
				AttachPartnerAndGroup(partnerId, _partner.GroupId);

			await Task.CompletedTask;
		}

		private void AttachPartnerAndGroup(string partnerId, string groupId) {
			_partnerHandle = _requests.ListenPersonal(partnerId, cloud => {
				Application.Current.Dispatcher.Invoke(() => {
					if(_tasks is null) return;
					TaskCollectionHelpers.UpsertInto(_tasks, cloud, assignedTo: Assignee.Partner, ownerUserId: partnerId);
					_partnerView?.Refresh();
				});
			});

			_groupHandle = _requests.ListenRequests(groupId, cloud => {
				Application.Current.Dispatcher.Invoke(() => {
					if(_pending is null) return;
					TaskCollectionHelpers.ReplaceAll(_pending, cloud, assignedTo: Assignee.Partner, requestMode: true);
				});
			});
		}

		private void TearDownPartnerListenersAndClearUi() {
			_partnerHandle?.Dispose();
			_groupHandle?.Dispose();
			_partnerHandle = _groupHandle = null;

			Application.Current.Dispatcher.Invoke(() => {
				if(_tasks is null || _pending is null || _outgoing is null || _incoming is null) return;

				var mine = _tasks.Where(t => t.AssignedTo == Assignee.Me).ToList();
				_tasks.Clear();
				foreach(var t in mine) _tasks.Add(t);

				_pending.Clear();
				_outgoing.Clear();
				_incoming.Clear();
			});
		}

		public void Dispose() {
			_myHandle?.Dispose();
			_partnerHandle?.Dispose();
			_groupHandle?.Dispose();
			_incomingHandle?.Dispose();
			_outgoingHandle?.Dispose();
			_myHandle = _partnerHandle = _groupHandle = _incomingHandle = _outgoingHandle = null;
		}
	}
}