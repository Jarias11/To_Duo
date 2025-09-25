namespace TaskMate.Services {
    public interface IPartnerService {
        string UserId { get; }
        string? PartnerId { get; set; }
        string GroupId { get; }           // read-only to callers
        event Action? PartnerChanged;
    }
}