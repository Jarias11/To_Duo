using System.Collections.ObjectModel;
using System.Windows.Threading;
using TaskMate.Models;
using TaskMate.Services;
using System.Linq;

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
		private readonly IActivityLogService _activity;
		private readonly SettingsService _settings;
		private bool _incomingPrimed, _outgoingPrimed;

		private IDisposable? _incomingSub;
		private IDisposable? _outgoingSub;
		private bool _purgeEligibleOnStartup;
		private bool _incomingListPrimed; //only for the sound debounce

		public ObservableCollection<PartnerRequest> Incoming { get; private set; } = new();
		public ObservableCollection<PartnerRequest> Outgoing { get; private set; } = new();
		public bool HasPendingOutgoing => Outgoing.Any(r => r.Status == "pending");

		public event Action? PartnerDisconnected;
		public event Action? OutgoingChanged;


		static string OtherOf(PartnerRequest r, string me) =>
	!string.IsNullOrWhiteSpace(r.ToUserId) && r.ToUserId != me ? r.ToUserId :
	!string.IsNullOrWhiteSpace(r.FromUserId) && r.FromUserId != me ? r.FromUserId :
	r.Id;



		public PairingOrchestrator(IPartnerRequestService partnerReqs,
								   IPartnerService partner,
								   ILiveSyncCoordinator live,
								   Dispatcher ui
								   , IActivityLogService activity, SettingsService settings) {
			_partnerReqs = partnerReqs ?? throw new ArgumentNullException(nameof(partnerReqs));
			_partner = partner ?? throw new ArgumentNullException(nameof(partner));
			_live = live ?? throw new ArgumentNullException(nameof(live));
			_ui = ui ?? throw new ArgumentNullException(nameof(ui));
			_activity = activity ?? throw new ArgumentNullException(nameof(activity));
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		}

		public void Attach(ObservableCollection<PartnerRequest> incoming,
						   ObservableCollection<PartnerRequest> outgoing) {
			Incoming = incoming ?? throw new ArgumentNullException(nameof(incoming));
			Outgoing = outgoing ?? throw new ArgumentNullException(nameof(outgoing));
			// Keep HasPendingOutgoing reactive
			Outgoing.CollectionChanged += (_, __) => OutgoingChanged?.Invoke();
		}

		public void Start(string myUserId) {
			if(string.IsNullOrWhiteSpace(myUserId))
				return;
			// (Re)attach snapshot listeners
			_incomingSub?.Dispose();
			_outgoingSub?.Dispose();

			_purgeEligibleOnStartup = string.IsNullOrWhiteSpace(_partner.PartnerId);

			_incomingSub = _partnerReqs.ListenIncoming(myUserId, list => {
				_ui.BeginInvoke(() => {
					var latest = list
						.GroupBy(r => OtherOf(r, myUserId))
						.Select(g => g.OrderByDescending(r => r.UpdatedAt ?? DateTime.MinValue).First())
						.ToDictionary(r => OtherOf(r, myUserId), r => r, StringComparer.OrdinalIgnoreCase);

					ReconcilePairingFromLatest(latest, myUserId, fromIncoming: true);

					// 3) Pending list for UI (from the same 'latest')
					var pending = latest.Values
						.Where(r => string.Equals(r.Status, "pending", StringComparison.OrdinalIgnoreCase))
						.ToList();
					var newOnes = pending.Where(p => Incoming.All(i => i.Id != p.Id)).ToList();


					bool shouldChime =
						newOnes.Count > 0
						|| (!_incomingListPrimed && pending.Count > 0);
					if(shouldChime)
						SoundService.PlayPendingPartnerRequest();

					_incomingListPrimed = true; // mark after first pass
					ReplaceAll(Incoming, pending);
					OutgoingChanged?.Invoke();


				});
			});

			_outgoingSub = _partnerReqs.ListenOutgoing(myUserId, list => {
				_ui.BeginInvoke(() => {
					var latest = list
						.GroupBy(r => OtherOf(r, myUserId))
						.Select(g => g.OrderByDescending(r => r.UpdatedAt ?? DateTime.MinValue).First())
						.ToDictionary(r => OtherOf(r, myUserId), r => r, StringComparer.OrdinalIgnoreCase);

					ReconcilePairingFromLatest(latest, myUserId, fromIncoming: false);

					// 3) Pending list for UI (from the same 'latest')
					var pending = latest.Values
						.Where(r => string.Equals(r.Status, "pending", StringComparison.OrdinalIgnoreCase))
						.ToList();
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
			SoundService.PlaySent();
			await _activity.LogAsync(new ActivityEntry {
				Kind = "pairing.sent",
				Actor = "Me",
				Message = $"Sent partner request to {toUserId}"
			}, myUserId);
		}

		public async Task AcceptAsync(string myUserId, PartnerRequest r) {
			if(r is null) return;
			await _partnerReqs.AcceptAsync(myUserId, r.FromUserId);


			// Locally link partner so both sides flip immediately in UI.
			_ui.Invoke(() => _partner.PartnerId = r.FromUserId);
			var other = r.FromUserId;
			_settings.PairedSinceUtc = DateTime.UtcNow;
			_settings.Save();
			_ui.Invoke(() => {       // persist this (setter should save)
			});

			await _activity.LogAsync(new ActivityEntry {
				Kind = "pairing.accepted",
				Actor = "Me",
				Message = $"Accepted partner request from {r.FromUserId}"
			}, myUserId);

			await _activity.LogAsync(new ActivityEntry {
				Kind = "pairing.connected",
				Actor = "Me",
				Message = $"Connected to {r.FromUserId}"
			}, myUserId);


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
			await _activity.LogAsync(new ActivityEntry {
				Kind = "pairing.disconnected",
				Actor = "Me",
				Message = $"Disconnected from {currentPartnerId}"
			}, myUserId);

			_ui.Invoke(() => {
				_partner.PartnerId = string.Empty; // persists & raises PartnerChanged
				ClearRequests();
				PartnerDisconnected?.Invoke();
				SoundService.PlayPartnerDisconnected();


			});
			_settings.PairedSinceUtc = null;
			_settings.Save();
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

		private void ReconcilePairingFromLatest(
		Dictionary<string, PartnerRequest> latest, string myUserId, bool fromIncoming) {
			if(fromIncoming) _incomingPrimed = true; else _outgoingPrimed = true;

			var pairSince = _settings.PairedSinceUtc; // null if never paired
			string? currentPartner = _partner.PartnerId;

			// If we have any accepted, prefer that (it can also *re*-pair us)
			var accepted = latest.Values
				.Where(r => string.Equals(r.Status, "accepted", StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(r => r.UpdatedAt ?? DateTime.MinValue)
				.FirstOrDefault();

			if(accepted != null) {
				var other = OtherOf(accepted, myUserId);
				if(!string.IsNullOrWhiteSpace(other) &&
				   !string.Equals(currentPartner, other, StringComparison.OrdinalIgnoreCase)) {
					_partner.PartnerId = other; // persists + recomputes GroupId
					if(_settings.PairedSinceUtc == null) { _settings.PairedSinceUtc = DateTime.UtcNow; _settings.Save(); }
				}
				return; // accepted wins; don't consider disconnects this round
			}

			// Don’t auto-unpair until both listeners have primed at least once
			if(!_incomingPrimed || !_outgoingPrimed) return;

			// Only consider disconnects that are fresher than our last "paired since"
			if(!string.IsNullOrWhiteSpace(currentPartner) &&
			   latest.TryGetValue(currentPartner, out var rec) &&
			   string.Equals(rec.Status, "disconnected", StringComparison.OrdinalIgnoreCase)) {
				var recTime = rec.UpdatedAt ?? DateTime.MinValue;
				var since = pairSince ?? DateTime.MinValue;

				if(recTime > since)  // 👈 key guard: ignore stale disconnects
				{
					_partner.PartnerId = string.Empty; // persists & raises PartnerChanged
					ClearRequests();
					PartnerDisconnected?.Invoke();
					SoundService.PlayPartnerDisconnected();
					_settings.PairedSinceUtc = null;
					_settings.Save();
				}
			}
		}
	}

}
