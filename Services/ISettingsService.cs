namespace TaskMate.Services {
	public interface ISettingsService {

		AppTheme Theme { get; set; }


		//bool SoundsEnabled { get; set; }       // default false
		//string AnimationLevel { get; set; }    // "Off" | "Subtle" | "Extra"


		string UserId { get; }           // from model
		string? PartnerId { get; set; }  // persisted
		string GroupId { get; set; }     // persisted
		string? DisplayName { get; set; }      // optional: if you want this centralized
		public DateTime? PairedSinceUtc { get; set; }
		bool NeedsProfileSetup { get; }
		void Save();
	}
}