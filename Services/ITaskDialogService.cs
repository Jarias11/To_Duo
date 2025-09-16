namespace TaskMate.Services {
  using System.Threading.Tasks;
  using TaskMate.Models;

  public interface ITaskDialogService {
	Task ShowTaskDetailsAsync(TaskItem item);
  }
}