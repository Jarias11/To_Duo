namespace TaskMate.Services
{
    public interface IPartnerService
    {
        string UserId { get; }
        string? PartnerId { get; set; }

        // Derived from (UserId, PartnerId) using your existing ordering rule
        string GroupId { get; }

        // Profile/display name support
        string? DisplayName { get; }
        bool NeedsProfileSetup { get; }

        // Persist current state (if you need explicit saves)
        void Save();

        // Update the display name and persist
        void SaveDisplayName(string name);

        // Notify VM when PartnerId or GroupId changes (after save)
        event Action? PartnerChanged;
    }
}