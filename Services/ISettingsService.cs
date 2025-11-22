namespace TaskMate.Services {
	public interface ISettingsService {

		AppTheme Theme { get; set; }


		bool SoundsEnabled { get; set; }
		//string AnimationLevel { get; set; }    // "Off" | "Subtle" | "Extra"
		public bool NotificationsEnabled { get; set; } // DEFAULT OFF

		string UserId { get; }           // from model
		string? PartnerId { get; set; }  // persisted
		string GroupId { get; set; }     // persisted
		string? DisplayName { get; set; }      // optional: if you want this centralized
		public DateTime? PairedSinceUtc { get; set; }
		bool NeedsProfileSetup { get; }
		bool AnimationsEnabled { get; set; }

		public bool UnreadActivity { get; set; }
		public bool UnreadPending { get; set; }
		public bool UnreadCompleted { get; set; }
		public bool UnreadConnections { get; set; }

		void EnsureUserId(string uid);
		void Save();
	}
}