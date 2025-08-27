using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TaskMate.Models;

namespace TaskMate.Data.Repositories {
	public interface IRequestRepo {
		Task<IList<TaskItem>> LoadAllAsync(string groupId);
		Task UpsertAsync(TaskItem item, string groupId);      // create/edit request
		Task DeleteAsync(Guid id, string groupId);
		IDisposable Listen(string groupId, Action<IList<TaskItem>> onSnapshot);
		Task AcceptAsync(TaskItem item, string groupId, string assigneeUserId); // move -> personal
	}
}