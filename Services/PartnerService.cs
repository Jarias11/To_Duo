using TaskMate.Models;
using TaskMate.Sync;

namespace TaskMate.Services {
	public sealed class PartnerService : IPartnerService {
		private UserSettings _settings;

		public PartnerService() {
			_settings = UserSettings.Load();
			// Keep GroupId in sync on startup
			_settings.GroupId = ComputeGroupId(_settings.UserId, _settings.PartnerId);
			UserSettings.Save(_settings);
		}

		public string UserId => _settings.UserId;

		public string? PartnerId {
			get => _settings.PartnerId;
			set {
				var next = value?.Trim() ?? "";
				if(_settings.PartnerId == next) return;

				_settings.PartnerId = next;
				_settings.GroupId = ComputeGroupId(_settings.UserId, _settings.PartnerId);

				UserSettings.Save(_settings);
				// Keep any Firestore-aware singletons in sync if you use them:
				FirestoreClient.SavePartnerAndGroup(_settings.PartnerId, _settings.GroupId);

				PartnerChanged?.Invoke();
			}
		}

		public string GroupId => _settings.GroupId ?? ComputeGroupId(_settings.UserId, _settings.PartnerId);

		public void Save() => UserSettings.Save(_settings);

		public event Action? PartnerChanged;

		private static string ComputeGroupId(string userId, string partnerId) {
			var a = userId ?? "";
			var b = partnerId ?? "";
			if(string.IsNullOrWhiteSpace(b)) return $"user-{a}";
			return string.CompareOrdinal(a, b) <= 0 ? $"{a}-{b}" : $"{b}-{a}";
		}
	}
}