using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TaskMate.Models;

namespace TaskMate.Data.Repositories
{
    public interface IPersonalTaskRepo
    {
        Task<IList<TaskItem>> LoadAllAsync(string userId);
        Task UpsertAsync(TaskItem item, string userId);
        Task DeleteAsync(Guid id, string userId);
        IDisposable Listen(string userId, Action<IList<TaskItem>> onSnapshot);
    }
}