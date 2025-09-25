namespace TaskMate.Models {
	public sealed class ActivityEntry {
		public DateTime Timestamp { get; init; } = DateTime.UtcNow;
		public string Actor { get; init; } = "Me";             // "Me" or "Partner"
		public string Kind { get; init; } = "";                // e.g., pairing.sent, pairing.accepted, task.add, task.complete, pairing.disconnect
		public string Message { get; init; } = "";
		public bool IsMine { get; set; } = false;
	}
}