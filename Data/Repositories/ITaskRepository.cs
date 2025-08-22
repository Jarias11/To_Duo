using System.Collections.Generic;
using System.Threading.Tasks;
using TaskMate.Models;

namespace TaskMate.Data.Repositories {
    public interface ITaskRepository {
        Task<IList<TaskItem>> LoadAllAsync(string groupId);
        Task UpsertAsync(string groupId, TaskItem item);
        Task DeleteAsync(string groupId, Guid id);
        IDisposable ListenAll(string groupId, Action<IList<TaskItem>> onSnapshot);
    }
}