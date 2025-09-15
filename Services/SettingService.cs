using TaskMate.Models;
using TaskMate.Sync; // for AppTheme if that enum lives here; else adjust namespace

namespace TaskMate.Services {
	public sealed class SettingsService : ISettingsService {
		private readonly UserSettings _model;

		public SettingsService() {
			_model = UserSettings.Load();

			// Backfill sensible defaults if missing (won’t change behavior)
			_model.Theme ??= "Light";
			//_model.SoundsEnabled ??= false;
			//_model.AnimationLevel ??= "Off";
		}

		public AppTheme Theme {
			get => (_model.Theme?.Equals("Dark", StringComparison.OrdinalIgnoreCase) == true)
				   ? AppTheme.Dark : AppTheme.Light;
			set => _model.Theme = value == AppTheme.Dark ? "Dark" : "Light";
		}

		/*public bool SoundsEnabled
        {
            get => _model.SoundsEnabled ?? false;
            set => _model.SoundsEnabled = value;
        }

        public string AnimationLevel
        {
            get => _model.AnimationLevel ?? "Off";
            set => _model.AnimationLevel = string.IsNullOrWhiteSpace(value) ? "Off" : value;
        }
*/
		public bool NeedsProfileSetup => string.IsNullOrWhiteSpace(_model.DisplayName);
		public string? DisplayName {
			get => _model.DisplayName;
			set => _model.DisplayName = value?.Trim();
		}

		public void Save() => UserSettings.Save(_model);
	}
}