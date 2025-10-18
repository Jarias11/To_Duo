namespace TaskMate.Services {
  using System.Threading.Tasks;
  using System.Windows;
  using TaskMate.Models;
  using TaskMate.ViewModels; // for TaskDetailsViewModel
  using TaskMate.Views;    // for TaskDetailsWindow

  public sealed class TaskDialogService : ITaskDialogService {
    private readonly ITaskActions _actions;
    private readonly IPartnerService _partner;
    private readonly IAnimationService _anim;

    public TaskDialogService(ITaskActions actions, IPartnerService partner, IAnimationService anim) {
      _actions = actions;
      _partner = partner;
      _anim = anim;
    }

    public Task ShowTaskDetailsAsync(TaskItem item) {
      var vm = new TaskDetailsViewModel(
        item,
        _actions,
        _anim,
        myUserId: _partner.UserId,
        groupId: _partner.GroupId
      );

      var win = new TaskDetailsWindow {
        Owner = Application.Current.MainWindow,
        DataContext = vm
      };
      vm.CloseRequested += () => win.Close();

      // modal is simplest; non-modal works too
      win.ShowDialog();
      return Task.CompletedTask;

    }
    public Task<string?> PromptTextAsync(string title, string message, string? placeholder = null) {
      var win = new PromptTextWindow(title, message, placeholder) {
        Owner = Application.Current?.MainWindow,
        ShowInTaskbar = false
      };
      var result = (win.ShowDialog() == true) ? win.Result : null;
      return Task.FromResult(result);
    }
  }
}