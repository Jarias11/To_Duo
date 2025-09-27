using System;
using System.Collections.ObjectModel;
using TaskMate.Models;
namespace TaskMate.Services {
	public interface IActivityLogService : IDisposable {
		ObservableCollection<ActivityEntry> Feed { get; }              // local + live view
		Task LogAsync(ActivityEntry e, string myUserId);                // writes mine
		Task PostMessageAsync(string text, string myUserId); // convenience for chat.message
		Task StartMineAsync(string myUserId);                           // listen to my logs
		Task StartPartnerSinceAsync(string partnerUserId, DateTime sinceUtc); // listen to partner logs from 'since' forward
		Task StopPartnerAsync();
		Task ReactAsync(string ownerUserId, string entryId, string reactorUserId, string emoji); // toggle reaction on an entry
	}
}