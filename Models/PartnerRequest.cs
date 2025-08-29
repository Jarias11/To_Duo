namespace TaskMate.Models
{
    public class PartnerRequest
    {
        public string Id { get; set; } = string.Empty;          // doc id (requesterUid)
        public string FromUserId { get; set; } = string.Empty;  // requester
        public string FromDisplayName { get; set; } = string.Empty;
        public string ToUserId { get; set; } = string.Empty;    // recipient
        public string Status { get; set; } = "pending";         // pending|accepted|declined|canceled
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}