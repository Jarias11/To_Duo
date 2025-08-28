using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TaskMate.Models;

namespace TaskMate.Services {
	public interface IRequestService {
		// Live listeners (caller is responsible for disposing)
		IDisposable ListenPersonal(string userId, Action<IList<TaskItem>> onChange);
		IDisposable ListenRequests(string groupId, Action<IList<TaskItem>> onChange);

		// Upserts
		Task UpsertPersonalAsync(TaskItem item, string userId);
		Task UpsertRequestAsync(TaskItem item, string groupId);

		// Request actions
		Task AcceptRequestAsync(TaskItem item, string groupId, string myUserId);
		Task DeleteRequestAsync(Guid requestId, string groupId);

		// Personal actions
		Task DeletePersonalAsync(Guid id, string userId);
	}
}