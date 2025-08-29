using TaskMate.Sync;
using TaskMate.Models;

namespace TaskMate.Services {
	public interface IPartnerRequestService {
		IDisposable ListenIncoming(string myUserId, Action<IList<PartnerRequest>> onSnapshot);
		IDisposable ListenOutgoing(string myUserId, Action<IList<PartnerRequest>> onSnapshot);
		Task SendAsync(string fromUserId, string toUserId, string fromDisplayName);
		Task AcceptAsync(string myUserId, string requesterUserId);
		Task DeclineAsync(string myUserId, string requesterUserId);
		Task CancelAsync(string myUserId, string targetUserId);
	}
}