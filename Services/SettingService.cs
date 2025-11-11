using TaskMate.Models;
using TaskMate.Sync; // for AppTheme if that enum lives here; else adjust namespace

namespace TaskMate.Services {
	public sealed class SettingsService : ISettingsService {
		private readonly UserSettings _model;


		public SettingsService() {
			_model = UserSettings.Load();
			_model.Theme ??= "Light";
			_model.DisplayName ??= string.Empty;
			_model.PartnerId ??= string.Empty;
			_model.GroupId ??= string.Empty;

			_model.SoundsEnabled ??= true;
			//_model.AnimationLevel ??= "Off";
			_model.NotificationsEnabled ??= false;
		}

		public AppTheme Theme {
			get => (_model.Theme?.Equals("Dark", StringComparison.OrdinalIgnoreCase) == true)
				   ? AppTheme.Dark : AppTheme.Light;
			set => _model.Theme = value == AppTheme.Dark ? "Dark" : "Light";
		}

		/*

        public string AnimationLevel
        {
            get => _model.AnimationLevel ?? "Off";
            set => _model.AnimationLevel = string.IsNullOrWhiteSpace(value) ? "Off" : value;
        }
*/
		public bool NotificationsEnabled {
			get => _model.NotificationsEnabled ?? true;   // default enabled
			set => _model.NotificationsEnabled = value;
		}  // DEFAULT OFF

		public bool AnimationsEnabled {
			get => _model.AnimationsEnabled ?? true;   // default enabled
			set => _model.AnimationsEnabled = value;
		}
		public bool NeedsProfileSetup => string.IsNullOrWhiteSpace(_model.DisplayName);
		public string? DisplayName {
			get => _model.DisplayName;
			set => _model.DisplayName = value?.Trim();
		}
		// NEW: expose ids directly from the model
		public string UserId => _model.UserId;

		public string? PartnerId {
			get => string.IsNullOrWhiteSpace(_model.PartnerId) ? null : _model.PartnerId;
			set => _model.PartnerId = value?.Trim() ?? string.Empty;
		}

		public string GroupId {
			get => _model.GroupId ?? string.Empty;
			set => _model.GroupId = value ?? string.Empty;
		}

		public DateTime? PairedSinceUtc {
			get => _model.PairedSinceUtc;
			set => _model.PairedSinceUtc = value;
		}

		public bool SoundsEnabled {
			get => _model.SoundsEnabled ?? true;   // default true
			set => _model.SoundsEnabled = value;
		}
		public void EnsureUserId(string uid) {
			if(string.IsNullOrWhiteSpace(uid)) return;
			if(!string.Equals(_model.UserId, uid, StringComparison.Ordinal)) {
				_model.UserId = uid;
				Save();
			}
		}

		public void Save() => UserSettings.Save(_model);
	}
}