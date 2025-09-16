namespace TaskMate.ViewModels {
  using System;
  using System.Threading.Tasks;
  using System.Windows.Input;
  using TaskMate.Models;
  using TaskMate.Services;

  public sealed class TaskDetailsViewModel {
	private readonly ITaskActions _actions;
	private readonly string _myUserId;
	private readonly string _groupId;

	public TaskItem Item { get; }

	public bool CanAccept => Item.CanDecide;      // you already set this via LiveSyncCoordinator
	public bool CanDecline => Item.CanDecide;
	public bool CanDelete => !Item.CanDecide;     // simple default; tweak if you want both

	public ICommand AcceptCommand { get; }
	public ICommand DeclineCommand { get; }
	public ICommand DeleteCommand { get; }
	public ICommand SaveCommand { get; }          // optional edit support (see §3)

	public event Action? CloseRequested;

	public TaskDetailsViewModel(TaskItem item, ITaskActions actions, string myUserId, string groupId) {
	  Item = item ?? throw new ArgumentNullException(nameof(item));
	  _actions = actions ?? throw new ArgumentNullException(nameof(actions));
	  _myUserId = myUserId ?? string.Empty;
	  _groupId = groupId ?? string.Empty;

	  AcceptCommand  = new RelayCommand(async _ => { await _actions.AcceptAsync(Item, _myUserId, _groupId); CloseRequested?.Invoke(); }, _ => CanAccept);
	  DeclineCommand = new RelayCommand(async _ => { await _actions.DeclineAsync(Item, _groupId);            CloseRequested?.Invoke(); }, _ => CanDecline);
	  DeleteCommand  = new RelayCommand(async _ => { await _actions.DeleteAsync(Item, _myUserId, _groupId);  CloseRequested?.Invoke(); }, _ => CanDelete);

	  // If you implement UpdateAsync in ITaskActions (§3), wire it here:
	  SaveCommand    = new RelayCommand(async _ => { await _actions.UpdateAsync(Item, _myUserId, _groupId);  CloseRequested?.Invoke(); });
	}
  }
}