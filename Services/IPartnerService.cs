namespace TaskMate.Services {
    public interface IPartnerService {
        string UserId { get; }
        string? PartnerId { get; set; }

        // Derived from (UserId, PartnerId) using your existing ordering rule
        string GroupId { get; }



        // Persist current state (if you need explicit saves)
        void Save();



        // Notify VM when PartnerId or GroupId changes (after save)
        event Action? PartnerChanged;
    }
}