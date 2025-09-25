// PartnerService.cs
using TaskMate.Sync;

namespace TaskMate.Services {


	public sealed class PartnerService : IPartnerService {
		private readonly SettingsService _settings;

		public PartnerService(SettingsService settings) {
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));


			// If we boot with a PartnerId already saved, make sure GroupId is aligned.
			if(!string.IsNullOrWhiteSpace(_settings.PartnerId)) {
				var gid = ComputeGroupId(UserId, _settings.PartnerId);
				if(!string.Equals(_settings.GroupId, gid, StringComparison.Ordinal)) {
					_settings.GroupId = gid;
					_settings.Save();
				}
			}

		}

		public string UserId => _settings.UserId;

		public string? PartnerId {
			get => _settings.PartnerId;
			set {
				var incoming = value?.Trim() ?? string.Empty;
				if(string.Equals(_settings.PartnerId ?? string.Empty, incoming, StringComparison.OrdinalIgnoreCase))
					return;

				_settings.PartnerId = incoming;

				// Keep GroupId in lockstep with UserId + PartnerId
				_settings.GroupId = string.IsNullOrWhiteSpace(incoming)
					? string.Empty
					: ComputeGroupId(UserId, incoming);

				_settings.Save();           // <<< persist immediately
				PartnerChanged?.Invoke();   // notify VM / LiveSync
			}
		}

		public string GroupId
			=> string.IsNullOrWhiteSpace(_settings.PartnerId)
				? string.Empty
				: (_settings.GroupId ?? ComputeGroupId(UserId, _settings.PartnerId));

		public event Action? PartnerChanged;

		private static string ComputeGroupId(string a, string b) {
			if(string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
				return string.Empty;

			// deterministic, order-independent id
			return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0
				? $"{a}_{b}"
				: $"{b}_{a}";
		}
	}
}