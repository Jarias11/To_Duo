using TaskMate.Sync;
using TaskMate.Models;
namespace TaskMate.Services {


	public sealed class PartnerRequestService : IPartnerRequestService {
		private readonly IPartnerRequestRepo _repo;
		public PartnerRequestService() : this(new FirestorePartnerRequestRepository()) { }
		public PartnerRequestService(IPartnerRequestRepo repo) => _repo = repo;

		public IDisposable ListenIncoming(string myUserId, Action<IList<PartnerRequest>> onSnapshot)
			=> _repo.ListenIncoming(myUserId, onSnapshot);

		public IDisposable ListenOutgoing(string myUserId, Action<IList<PartnerRequest>> onSnapshot)
			=> _repo.ListenOutgoing(myUserId, onSnapshot);

		public Task SendAsync(string fromUserId, string toUserId, string fromDisplayName)
			=> _repo.SendAsync(fromUserId, toUserId, fromDisplayName);

		public Task AcceptAsync(string myUserId, string requesterUserId)
			=> _repo.AcceptAsync(myUserId, requesterUserId);

		public Task DeclineAsync(string myUserId, string requesterUserId)
			=> _repo.DeclineAsync(myUserId, requesterUserId);

		public Task CancelAsync(string myUserId, string targetUserId)
			=> _repo.CancelAsync(myUserId, targetUserId);
	}
}