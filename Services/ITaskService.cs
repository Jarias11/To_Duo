namespace TaskMate.Services
{
    using System.Collections.ObjectModel;
    using TaskMate.Models;

    public interface ITaskService
    {
        ObservableCollection<TaskItem> Tasks { get; }
        ObservableCollection<TaskItem> PendingTasks { get; }

        // Load from disk + cloud and populate collections
        Task InitializeAsync(string groupId);

        // Core actions
        TaskItem AddTask(TaskItem task, string groupId);
        void DeleteTask(TaskItem task, string groupId);
        void AcceptTask(TaskItem task, string groupId);
        void DeclineTask(TaskItem task, string groupId);

        // Persistence
        void SaveAll(string groupId);
    }
}