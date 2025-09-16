using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using TaskMate.Models;
using TaskMate.Services;

namespace TaskMate.Orchestration {
	public interface IPairingOrchestrator : IDisposable {
		// Bindable state (owned by VM, but updated here)
		ObservableCollection<PartnerRequest> Incoming { get; }
		ObservableCollection<PartnerRequest> Outgoing { get; }

		// Computed state helpers
		bool HasPendingOutgoing { get; }

		// Wire-up (call once from VM)
		void Attach(ObservableCollection<PartnerRequest> incoming,
					ObservableCollection<PartnerRequest> outgoing);

		// Start/stop listeners
		void Start(string myUserId);
		Task ReloadForPartnerAsync(); // handy if PartnerId changes

		// Partner request flows
		Task SendAsync(string myUserId, string toUserId, string fromDisplayName);
		Task AcceptAsync(string myUserId, PartnerRequest r);
		Task DeclineAsync(string myUserId, PartnerRequest r);
		Task CancelAsync(string myUserId, PartnerRequest r);

		// Unpair (disconnect both sides ASAP)
		Task DisconnectAsync(string myUserId, string currentPartnerId);

		// Events for the VM
		event Action? PartnerDisconnected;
		event Action? OutgoingChanged;

		void ClearRequests();
		Task PurgePairAsync(string userA, string userB);
	}

	public sealed class PairingOrchestrator : IPairingOrchestrator {
		private readonly IPartnerRequestService _partnerReqs;
		private readonly IPartnerService _partner;
		private readonly ILiveSyncCoordinator _live;
		private readonly Dispatcher _ui;

		private IDisposable? _incomingSub;
		private IDisposable? _outgoingSub;

		public ObservableCollection<PartnerRequest> Incoming { get; private set; } = new();
		public ObservableCollection<PartnerRequest> Outgoing { get; private set; } = new();
		public bool HasPendingOutgoing => Outgoing.Any(r => r.Status == "pending");

		public event Action? PartnerDisconnected;
		public event Action? OutgoingChanged;

		public PairingOrchestrator(IPartnerRequestService partnerReqs,
								   IPartnerService partner,
								   ILiveSyncCoordinator live,
								   Dispatcher ui) {
			_partnerReqs = partnerReqs ?? throw new ArgumentNullException(nameof(partnerReqs));
			_partner = partner ?? throw new ArgumentNullException(nameof(partner));
			_live = live ?? throw new ArgumentNullException(nameof(live));
			_ui = ui ?? throw new ArgumentNullException(nameof(ui));
		}

		public void Attach(ObservableCollection<PartnerRequest> incoming,
						   ObservableCollection<PartnerRequest> outgoing) {
			Incoming = incoming ?? throw new ArgumentNullException(nameof(incoming));
			Outgoing = outgoing ?? throw new ArgumentNullException(nameof(outgoing));
			// Keep HasPendingOutgoing reactive
			Outgoing.CollectionChanged += (_, __) => OutgoingChanged?.Invoke();
		}

		public void Start(string myUserId) {
			// (Re)attach snapshot listeners
			_incomingSub?.Dispose();
			_outgoingSub?.Dispose();

			_incomingSub = _partnerReqs.ListenIncoming(myUserId, list => {
				_ui.Invoke(() => {
					var pending = list.Where(x => string.Equals(x.Status, "pending", StringComparison.OrdinalIgnoreCase)).ToList();
					ReplaceAll(Incoming, pending);
					OutgoingChanged?.Invoke();
				});
			});

			_outgoingSub = _partnerReqs.ListenOutgoing(myUserId, list => {
				_ui.Invoke(() => {
					var pending = list.Where(x => string.Equals(x.Status, "pending", StringComparison.OrdinalIgnoreCase)).ToList();
					ReplaceAll(Outgoing, pending);
					OutgoingChanged?.Invoke();
				});
			});
		}

		public async Task ReloadForPartnerAsync() {
			// When partner changes, tell live sync to realign any listeners it owns.
			await _live.ReloadForPartnerAsync();
		}

		public async Task SendAsync(string myUserId, string toUserId, string fromDisplayName) {
			if(string.IsNullOrWhiteSpace(toUserId) || toUserId == myUserId) return;
			await _partnerReqs.SendAsync(myUserId, toUserId, fromDisplayName);
		}

		public async Task AcceptAsync(string myUserId, PartnerRequest r) {
			if(r is null) return;
			await _partnerReqs.AcceptAsync(myUserId, r.FromUserId);

			// Locally link partner so both sides flip immediately in UI.
			_ui.Invoke(() => _partner.PartnerId = r.FromUserId);
		}

		public async Task DeclineAsync(string myUserId, PartnerRequest r) {
			if(r is null) return;
			await _partnerReqs.DeclineAsync(myUserId, r.FromUserId);
		}

		public async Task CancelAsync(string myUserId, PartnerRequest r) {
			if(r is null) return;
			var target = r.ToUserId == myUserId ? r.FromUserId : r.ToUserId;
			await _partnerReqs.CancelAsync(myUserId, target);
		}

		public async Task DisconnectAsync(string myUserId, string currentPartnerId) {
			if(string.IsNullOrWhiteSpace(currentPartnerId)) return;
			await _partnerReqs.DisconnectAsync(myUserId, currentPartnerId);

			// If your IPartnerRequestService has custom disconnect/purge, call them here.
			// Otherwise: best-effort cleanup + local unlink.
			try {
				// Optional: cancel any pending outgoing to this partner
				var outs = Outgoing.Where(o =>
					(o.ToUserId == currentPartnerId || o.FromUserId == currentPartnerId) &&
					 o.Status == "pending").ToList();

				foreach(var o in outs)
					await CancelAsync(myUserId, o);
			}
			catch { /* non-fatal */ }

			_ui.Invoke(() => {
				_partner.PartnerId = string.Empty; // persists & raises PartnerChanged
				ClearRequests();
				PartnerDisconnected?.Invoke();

			});
		}

		public void Dispose() {
			_incomingSub?.Dispose();
			_outgoingSub?.Dispose();
		}

		private static void ReplaceAll(ObservableCollection<PartnerRequest> target,
									   System.Collections.Generic.IList<PartnerRequest> incoming) {
			target.Clear();
			foreach(var r in incoming) target.Add(r);
		}
		public void ClearRequests() {
			_ui.Invoke(() => {
				Incoming.Clear();
				Outgoing.Clear();
				OutgoingChanged?.Invoke();
			});
		}
		public Task PurgePairAsync(string userA, string userB)
	=> _partnerReqs.PurgePairAsync(userA, userB);
	}
}
