namespace TaskMate.Services {
	public interface ISettingsService {

		AppTheme Theme { get; set; }


		//bool SoundsEnabled { get; set; }       // default false
		//string AnimationLevel { get; set; }    // "Off" | "Subtle" | "Extra"

		string? DisplayName { get; set; }      // optional: if you want this centralized
		bool NeedsProfileSetup { get; }
		void Save();
	}
}