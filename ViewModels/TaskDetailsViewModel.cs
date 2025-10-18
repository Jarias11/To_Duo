namespace TaskMate.ViewModels {
	using System;
	using System.Threading.Tasks;
	using System.Windows.Input;
	using TaskMate.Models;
	using TaskMate.Services;

	public sealed class TaskDetailsViewModel {
		private readonly ITaskActions _actions;
		private readonly IAnimationService _anim;
		private readonly string _myUserId;
		private readonly string _groupId;

		public TaskItem Item { get; }

		public bool CanAccept => Item.CanDecide;      // you already set this via LiveSyncCoordinator
		public bool CanDecline => Item.CanDecide;
		public bool CanDelete => !Item.CanDecide;
		public bool CanComplete => !Item.CanDecide && !Item.IsCompleted;    // simple default; tweak if you want both

		public ICommand AcceptCommand { get; }
		public ICommand DeclineCommand { get; }
		public ICommand DeleteCommand { get; }
		public ICommand SaveCommand { get; }
		public ICommand CompleteCommand { get; }     // optional edit support (see §3)

		public event Action? CloseRequested;

		public TaskDetailsViewModel(TaskItem item, ITaskActions actions, IAnimationService anim, string myUserId, string groupId) {
			Item = item ?? throw new ArgumentNullException(nameof(item));
			_actions = actions ?? throw new ArgumentNullException(nameof(actions));
			_anim = anim ?? throw new ArgumentNullException(nameof(anim));
			_myUserId = myUserId ?? string.Empty;
			_groupId = groupId ?? string.Empty;

			AcceptCommand = new RelayCommand(async _ => { await _actions.AcceptAsync(Item, _myUserId, _groupId); CloseRequested?.Invoke(); }, _ => CanAccept);
			DeclineCommand = new RelayCommand(async _ => { await _actions.DeclineAsync(Item, _groupId); CloseRequested?.Invoke(); }, _ => CanDecline);
			DeleteCommand = new RelayCommand(async _ => { await _actions.DeleteAsync(Item, _myUserId, _groupId); CloseRequested?.Invoke(); }, _ => CanDelete);

			// If you implement UpdateAsync in ITaskActions (§3), wire it here:
			SaveCommand = new RelayCommand(async _ => { await _actions.UpdateAsync(Item, _myUserId, _groupId); CloseRequested?.Invoke(); });
			CompleteCommand = new RelayCommand(async _ => {
				// if it was already complete, do nothing
				if(Item.IsCompleted) { CloseRequested?.Invoke(); return; }

				// rising edge detection (before we mutate)
				bool becameCompleted = !Item.IsCompleted;

				Item.IsCompleted = true;
				Item.CompletedAt ??= DateTime.UtcNow;
				Item.UpdatedAt = DateTime.UtcNow;

				await _actions.UpdateAsync(Item, _myUserId, _groupId);

				SoundService.PlayTaskCompleted();

				// Close the dialog first so the main window is visible
				CloseRequested?.Invoke();

				// Let the UI finish closing the window, then fire confetti on MainWindow
				if(becameCompleted && _anim.Enabled) {
					await Task.Yield();       // next UI tick is enough; could also use Dispatcher.BeginInvoke
					_anim.Confetti();         // renders onto Application.Current.MainWindow
				}
			}, _ => CanComplete);
		}
	}
}